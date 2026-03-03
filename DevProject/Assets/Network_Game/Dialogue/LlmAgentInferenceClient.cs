using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LLMUnity;
using Network_Game.Diagnostics;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Adapter that exposes LLMAgent local/legacy chat through the unified dialogue inference contract.
    /// </summary>
    public sealed class LlmAgentInferenceClient : IDialogueInferenceClient
    {
        private LLMAgent m_Agent;

        private bool IsLegacyLocalAgentAvailable => m_Agent != null && !m_Agent.remote;

        public string BackendName => "llmunity-legacy-local";
        public bool ManagesHistoryInternally => true;

        public void SetAgent(LLMAgent agent)
        {
            m_Agent = agent != null && !agent.remote ? agent : null;
        }

        public void ApplyConfig(DialogueInferenceRuntimeConfig config)
        {
            // Legacy local LLMAgent fields are managed directly by NetworkDialogueService.
            // This adapter only preserves the old local call surface during migration.
        }

        public Task<bool> CheckConnectionAsync(CancellationToken ct = default)
        {
            return Task.FromResult(IsLegacyLocalAgentAvailable);
        }

        public async Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<DialogueInferenceMessage> history,
            string userPrompt,
            bool addToHistory = true,
            CancellationToken ct = default
        )
        {
            if (!IsLegacyLocalAgentAvailable)
            {
                NGLog.Warn(
                    "Dialogue",
                    "Legacy local inference requested without an available non-remote LLMAgent."
                );
                return string.Empty;
            }

            m_Agent.systemPrompt = systemPrompt ?? string.Empty;
            m_Agent.chat = ConvertHistory(history);
            ct.ThrowIfCancellationRequested();

            TraceInference("begin", userPrompt);
            var sw = Stopwatch.StartNew();
            string reply = await m_Agent.Chat(userPrompt ?? string.Empty, null, null, addToHistory);
            sw.Stop();
            TraceInference("complete", reply);
            InferenceWatchReporter.ReportInference(userPrompt, reply, (float)sw.Elapsed.TotalMilliseconds);
            return reply;
        }

        /// <summary>
        /// VS tracepoint anchor — right-click this breakpoint → Actions → Log Message
        /// to observe prompt/response flow without pausing Unity.
        /// Stripped in non-UNITY_EDITOR builds.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        private static void TraceInference(string tag, string data) =>
            UnityEngine.Debug.Log($"[TRACE:Inference:{tag}] {(data != null && data.Length > 80 ? data[..80] + "…" : data)}");

        private static List<ChatMessage> ConvertHistory(IReadOnlyList<DialogueInferenceMessage> history)
        {
            if (history == null || history.Count == 0)
            {
                return new List<ChatMessage>();
            }

            var converted = new List<ChatMessage>(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                DialogueInferenceMessage entry = history[i];
                converted.Add(new ChatMessage(entry.Role, entry.Content));
            }

            return converted;
        }
    }
}
