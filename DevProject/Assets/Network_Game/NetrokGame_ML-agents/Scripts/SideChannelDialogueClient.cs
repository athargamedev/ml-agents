using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Network_Game.Dialogue;
using Unity.MLAgents.NpcDialogue;

/// <summary>
/// IDialogueInferenceClient backed by the ML-Agents LlmDialogueChannel SideChannel.
///
/// Drops into NetworkDialogueService as a training/testing override on top of the
/// normal OpenAIChatClient path. Routes each ChatAsync() call through the Python LLM bridge:
///
///   1. ChatAsync() packs the request and calls LlmDialogueChannel.SendRequest().
///   2. Python receives it, dispatches to Ollama/OpenAI in a background thread.
///   3. Python calls flush_responses() → Unity receives the DialogueResponse.
///   4. OnResponseReceived fires → TaskCompletionSource is resolved → ChatAsync returns.
///
/// Optionally inject a GameStateProvider to enrich every system prompt with
/// live player state (health, combat, position) before forwarding to Python.
/// </summary>
public class SideChannelDialogueClient : IDialogueInferenceClient
{
    private const string k_DefaultMessageType = "dialogue";
    private const string k_PingMessageType = "ping";
    private const string k_StatusOk = "ok";
    private const string k_StatusBusy = "busy";
    private const string k_PingResponseText = "__pong__";
    private const string k_HeartbeatNpcId = "__bridge__";
    private const string k_HeartbeatResponseText = "__bridge_ready__";
    private const long k_HeartbeatFreshWindowMs = 5000;
    private const int k_HeartbeatProbeWaitMs = 1000;
    private const int k_HeartbeatProbePollMs = 50;

    public string BackendName            => "ml-agents-sidechannel";
    public bool   ManagesHistoryInternally => false;
    public event Action<DialogueResponse> OnStructuredDialogueResponseReceived;

    private readonly LlmDialogueChannel m_Channel;
    private GameStateProvider m_GameState;
    private long m_LastBridgeSignalTickMs;

    // Pending requests keyed by the short GUID used as npcId for routing.
    private readonly ConcurrentDictionary<string, PendingRequest> m_Pending
        = new ConcurrentDictionary<string, PendingRequest>();

    private sealed class PendingRequest
    {
        public PendingRequest(
            TaskCompletionSource<string> completion,
            CancellationTokenRegistration cancellationRegistration,
            string messageType)
        {
            Completion = completion;
            CancellationRegistration = cancellationRegistration;
            MessageType = string.IsNullOrWhiteSpace(messageType)
                ? k_DefaultMessageType
                : messageType;
        }

        public TaskCompletionSource<string> Completion { get; }
        public CancellationTokenRegistration CancellationRegistration { get; }
        public string MessageType { get; }
    }

    public SideChannelDialogueClient(LlmDialogueChannel channel)
    {
        m_Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        m_Channel.OnResponseReceived += HandleResponse;
    }

    /// <summary>
    /// Optionally attach a GameStateProvider whose BuildDynamicContext() output is
    /// appended to every system prompt before it is sent to Python.
    /// </summary>
    public void SetGameStateProvider(GameStateProvider provider)
    {
        m_GameState = provider;
    }

    public void Dispose()
    {
        m_Channel.OnResponseReceived -= HandleResponse;

        foreach (KeyValuePair<string, PendingRequest> kvp in m_Pending)
        {
            if (m_Pending.TryRemove(kvp.Key, out PendingRequest pending))
            {
                pending.CancellationRegistration.Dispose();
                pending.Completion.TrySetCanceled();
            }
        }
    }

    // IDialogueInferenceClient -------------------------------------------------

    public void ApplyConfig(DialogueInferenceRuntimeConfig config) { /* no-op */ }

    /// <summary>
    /// Reports readiness for warmup. A SideChannel roundtrip probe would deadlock when
    /// invoked during UnityEnvironment.reset(), because Python cannot send the ping reply
    /// until the next exchange.
    ///
    /// Keep this non-blocking for NetworkDialogueService warmup. Use the structured
    /// response path during normal dialogue turns for runtime health/reward signals.
    /// </summary>
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return false;

        if (IsBridgeSignalFresh())
            return true;

        // Don't send a sidechannel ping here (that can deadlock during env.reset()).
        // Instead, briefly wait for a bridge heartbeat/response that is emitted once
        // the Python bridge has entered the env.step()/flush_responses() loop.
        int waitedMs = 0;
        while (waitedMs < k_HeartbeatProbeWaitMs && !ct.IsCancellationRequested)
        {
            await Task.Delay(k_HeartbeatProbePollMs, ct).ConfigureAwait(false);
            if (IsBridgeSignalFresh())
                return true;
            waitedMs += k_HeartbeatProbePollMs;
        }

        return IsBridgeSignalFresh() && !ct.IsCancellationRequested;
    }

    /// <summary>
    /// Sends the dialogue turn to the Python LLM bridge and awaits the response.
    /// The TaskCompletionSource is resolved when OnResponseReceived fires (main thread).
    /// </summary>
    public Task<string> ChatAsync(
        string systemPrompt,
        IReadOnlyList<DialogueInferenceMessage> history,
        string userPrompt,
        bool addToHistory = true,
        CancellationToken ct = default)
    {
        string requestId = Guid.NewGuid().ToString("N");

        // Optionally enrich the system prompt with real-time game state.
        string enrichedPrompt = systemPrompt;
        if (m_GameState != null)
        {
            string dynamicCtx = m_GameState.BuildDynamicContext();
            if (!string.IsNullOrEmpty(dynamicCtx))
                enrichedPrompt = $"{systemPrompt}\n\n{dynamicCtx}";
        }

        return SendRequestAsync(
            requestId,
            new DialogueRequest
            {
                requestId           = requestId,
                messageType         = k_DefaultMessageType,
                npcId               = requestId, // legacy fallback for older bridge builds
                playerInput         = userPrompt,
                npcPersonality      = enrichedPrompt,      // full system prompt → Python
                conversationHistory = SerializeHistory(history),
            },
            k_DefaultMessageType,
            ct);
    }

    private Task<string> SendRequestAsync(
        string requestId,
        DialogueRequest request,
        string messageType,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<string>(ct);

        var tcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        CancellationTokenRegistration cancellationRegistration = default;
        if (ct.CanBeCanceled)
        {
            cancellationRegistration = ct.Register(() =>
            {
                if (m_Pending.TryRemove(requestId, out PendingRequest pending))
                {
                    pending.CancellationRegistration.Dispose();
                    pending.Completion.TrySetCanceled();
                }
            });
        }

        var pendingRequest = new PendingRequest(tcs, cancellationRegistration, messageType);
        m_Pending[requestId] = pendingRequest;

        try
        {
            m_Channel.SendRequest(request);
        }
        catch
        {
            if (m_Pending.TryRemove(requestId, out PendingRequest pending))
            {
                pending.CancellationRegistration.Dispose();
            }

            throw;
        }

        return tcs.Task;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void HandleResponse(DialogueResponse resp)
    {
        if (resp == null)
            return;

        if (IsBridgeHeartbeat(resp))
        {
            MarkBridgeSignalReceived();
            return;
        }

        string responseRequestId = GetResponseRequestId(resp);
        if (string.IsNullOrEmpty(responseRequestId))
            return;

        MarkBridgeSignalReceived();

        if (!m_Pending.TryRemove(responseRequestId, out PendingRequest pending))
            return;

        pending.CancellationRegistration.Dispose();

        if (!string.Equals(pending.MessageType, k_PingMessageType, StringComparison.Ordinal))
        {
            OnStructuredDialogueResponseReceived?.Invoke(resp);
        }

        if (HasBridgeFailure(resp))
        {
            pending.Completion.TrySetException(
                new InvalidOperationException(BuildBridgeErrorMessage(resp))
            );
            return;
        }

        pending.Completion.TrySetResult(resp.responseText ?? string.Empty);
    }

    private static bool IsBridgeHeartbeat(DialogueResponse resp)
    {
        return resp != null
            && string.Equals(resp.npcId, k_HeartbeatNpcId, StringComparison.Ordinal)
            && string.Equals(resp.responseText, k_HeartbeatResponseText, StringComparison.Ordinal);
    }

    private static string GetResponseRequestId(DialogueResponse resp)
    {
        if (resp == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(resp.requestId))
            return resp.requestId.Trim();

        return string.IsNullOrWhiteSpace(resp.npcId) ? string.Empty : resp.npcId.Trim();
    }

    private static bool HasBridgeFailure(DialogueResponse resp)
    {
        if (resp == null)
            return true;

        if (!string.IsNullOrWhiteSpace(resp.error))
            return true;

        if (string.IsNullOrWhiteSpace(resp.status))
            return false;

        return !string.Equals(resp.status, k_StatusOk, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBridgeErrorMessage(DialogueResponse resp)
    {
        if (resp == null)
            return "Dialogue bridge returned an empty response.";

        string status = string.IsNullOrWhiteSpace(resp.status) ? "error" : resp.status.Trim();
        string error = string.IsNullOrWhiteSpace(resp.error)
            ? resp.responseText ?? string.Empty
            : resp.error.Trim();

        if (string.Equals(status, k_StatusBusy, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(error)
                ? "Dialogue bridge is temporarily unavailable."
                : $"Dialogue bridge is temporarily unavailable: {error}";
        }

        if (string.IsNullOrWhiteSpace(error))
            return $"Dialogue bridge returned status '{status}'.";

        return $"Dialogue bridge returned status '{status}': {error}";
    }

    private void MarkBridgeSignalReceived()
    {
        Interlocked.Exchange(ref m_LastBridgeSignalTickMs, DateTime.UtcNow.Ticks);
    }

    private bool IsBridgeSignalFresh()
    {
        long last = Interlocked.Read(ref m_LastBridgeSignalTickMs);
        if (last <= 0)
            return false;

        long ageTicks = DateTime.UtcNow.Ticks - last;
        if (ageTicks < 0)
            return false;

        long ageMs = ageTicks / TimeSpan.TicksPerMillisecond;
        return ageMs <= k_HeartbeatFreshWindowMs;
    }

    private static string SerializeHistory(IReadOnlyList<DialogueInferenceMessage> history)
    {
        if (history == null || history.Count == 0)
            return "[]";

        var sb = new StringBuilder("[");
        for (int i = 0; i < history.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(
                $"{{\"role\":\"{history[i].Role}\",\"content\":{JsonEscape(history[i].Content)}}}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string JsonEscape(string s)
        => "\"" + (s ?? string.Empty)
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n",  "\\n")
        .Replace("\r",  "\\r") + "\"";
}
