using UnityEngine;
using UnityEngine.InputSystem;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Simple debug overlay for monitoring dialogue queue status at runtime.
    /// Also shows LM Studio analysis results and active dialogue VFX.
    /// </summary>
    public class DialogueDebugPanel : MonoBehaviour
    {
        [SerializeField]
        private bool m_ShowPanel = true;

        [SerializeField]
        private Key m_ToggleKey = Key.F10;

        [SerializeField]
        private bool m_ShowLastResponse = true;

        [SerializeField]
        private bool m_ShowLMStudio = true;

        [SerializeField]
        private bool m_ShowActiveVfx = true;

        private string m_LastErrorRaw = "-";
        private string m_LastErrorFriendly = "-";
        private NetworkDialogueService.DialogueStatus m_LastStatus;
        private bool m_HasResponse;

        // LM Studio / VFX refresh state
        private float m_NextRefreshTime;
        private const float k_RefreshInterval = 2f;
        private System.Collections.Generic.List<Network_Game.Dialogue.MCP.DialogueMCPBridge.LMStudioLogEntry> m_CachedLMLog;
        private System.Collections.Generic.List<System.Collections.Generic.Dictionary<
            string,
            object
        >> m_CachedVfxState;

        private void OnEnable()
        {
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
            m_NextRefreshTime = Time.realtimeSinceStartup;
        }

        private void OnDisable()
        {
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[m_ToggleKey].wasPressedThisFrame)
            {
                m_ShowPanel = !m_ShowPanel;
            }

            if (
                m_ShowPanel
                && Application.isPlaying
                && Time.realtimeSinceStartup >= m_NextRefreshTime
            )
            {
                m_NextRefreshTime = Time.realtimeSinceStartup + k_RefreshInterval;
                if (m_ShowLMStudio)
                {
                    m_CachedLMLog = Network_Game.Dialogue.MCP.DialogueMCPBridge.GetLMStudioLog(5);
                }
                if (m_ShowActiveVfx)
                {
                    m_CachedVfxState =
                        Network_Game.Dialogue.MCP.DialogueMCPBridge.GetActiveVfxState();
                }
            }
        }

        private void OnGUI()
        {
            if (!m_ShowPanel)
            {
                return;
            }

            var service = NetworkDialogueService.Instance;
            if (service == null)
            {
                return;
            }

            var stats = service.GetStats();

            // Calculate total panel height dynamically
            float baseHeight = m_ShowLastResponse ? 330f : 290f;
            float lmHeight = 0f;
            if (m_ShowLMStudio)
            {
                int lmCount = m_CachedLMLog != null ? Mathf.Min(m_CachedLMLog.Count, 5) : 0;
                lmHeight = 30f + lmCount * 45f + 10f;
            }
            float vfxHeight = 0f;
            if (m_ShowActiveVfx)
            {
                int vfxCount = m_CachedVfxState != null ? Mathf.Min(m_CachedVfxState.Count, 8) : 0;
                vfxHeight = 30f + vfxCount * 22f + 10f;
            }

            float panelHeight = baseHeight + lmHeight + vfxHeight;
            GUI.Box(new Rect(10, 10, 900, panelHeight), "Dialogue Debug");
            GUI.Label(new Rect(20, 40, 300, 20), $"Pending Queue: {stats.PendingCount}");
            GUI.Label(new Rect(20, 60, 300, 20), $"Active Requests: {stats.ActiveCount}");
            GUI.Label(new Rect(20, 80, 300, 20), $"Histories: {stats.HistoryCount}");
            GUI.Label(
                new Rect(20, 100, 300, 20),
                $"LLM Agent: {(stats.HasLlmAgent ? "OK" : "Missing")}, Server: {stats.IsServer}, Client: {stats.IsClient}"
            );
            GUI.Label(
                new Rect(20, 120, 860, 20),
                $"Warmup: {stats.WarmupState}, InProgress: {stats.WarmupInProgress}, Degraded: {stats.WarmupDegraded}, Failures: {stats.WarmupFailureCount}, RetryIn: {stats.WarmupRetryInSeconds:0.0}s"
            );
            GUI.Label(
                new Rect(20, 140, 860, 20),
                $"Warmup Last Failure: {stats.WarmupLastFailureReason}"
            );

            GUI.Label(
                new Rect(20, 160, 420, 20),
                $"Success Rate: {stats.SuccessRate:P1} ({stats.TotalTerminalCompleted}/{stats.TotalRequestsFinished})"
            );
            GUI.Label(
                new Rect(20, 180, 420, 20),
                $"Timeout Rate: {stats.TimeoutRate:P1} ({stats.TimeoutCount}/{Mathf.Max(1, stats.TotalTerminalCompleted + stats.TotalTerminalFailed)})"
            );
            GUI.Label(
                new Rect(20, 200, 420, 20),
                $"Queue Latency p50/p95: {stats.QueueWaitHistogram.P50Ms:F0}/{stats.QueueWaitHistogram.P95Ms:F0} ms"
            );
            GUI.Label(
                new Rect(20, 220, 420, 20),
                $"Model Latency p50/p95: {stats.ModelExecutionHistogram.P50Ms:F0}/{stats.ModelExecutionHistogram.P95Ms:F0} ms"
            );
            GUI.Label(
                new Rect(20, 240, 840, 20),
                $"Terminal Totals - Completed: {stats.TotalTerminalCompleted}, Failed: {stats.TotalTerminalFailed}, Cancelled: {stats.TotalTerminalCancelled}, Rejected: {stats.TotalTerminalRejected}"
            );

            string rejectionSummary = BuildRejectionSummary(stats.RejectionReasonCounts, 3);
            GUI.Label(new Rect(20, 260, 840, 20), $"Top Rejections (rolling): {rejectionSummary}");

            if (m_ShowLastResponse)
            {
                string statusText = m_HasResponse ? m_LastStatus.ToString() : "None";
                GUI.Label(new Rect(340, 40, 550, 20), $"Last Response Status: {statusText}");
                GUI.Label(
                    new Rect(340, 60, 550, 20),
                    $"Last Error (friendly | code): {m_LastErrorFriendly} | {m_LastErrorRaw}"
                );
            }

            // LM Studio Analysis Section
            float y = baseHeight + 10f;
            if (m_ShowLMStudio)
            {
                GUI.Label(new Rect(20, y, 860, 22), "─── LM Studio Analysis (last 5) ───");
                y += 24f;

                if (m_CachedLMLog == null || m_CachedLMLog.Count == 0)
                {
                    GUI.Label(
                        new Rect(30, y, 840, 20),
                        "(no LM Studio results yet — trigger dialogue to populate)"
                    );
                    y += 22f;
                }
                else
                {
                    int showCount = Mathf.Min(m_CachedLMLog.Count, 5);
                    for (int i = 0; i < showCount; i++)
                    {
                        var entry = m_CachedLMLog[m_CachedLMLog.Count - 1 - i];
                        string ts = System
                            .DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampMs)
                            .ToString("HH:mm:ss");
                        GUI.Label(
                            new Rect(30, y, 840, 20),
                            $"[{ts}] {entry.Mode.ToUpper()} — {entry.Summary}"
                        );
                        y += 20f;
                        GUI.Label(new Rect(40, y, 830, 20), entry.Detail);
                        y += 25f;
                    }
                }
            }

            // Active VFX Section
            if (m_ShowActiveVfx)
            {
                GUI.Label(new Rect(20, y, 860, 22), "─── Active Dialogue VFX ───");
                y += 24f;

                if (m_CachedVfxState == null || m_CachedVfxState.Count == 0)
                {
                    GUI.Label(new Rect(30, y, 840, 20), "(no active dialogue particle systems)");
                }
                else
                {
                    int showCount = Mathf.Min(m_CachedVfxState.Count, 8);
                    for (int i = 0; i < showCount; i++)
                    {
                        var fx = m_CachedVfxState[i];
                        string name = fx.TryGetValue("name", out object n) ? n?.ToString() : "?";
                        string remaining = fx.TryGetValue("duration_remaining", out object d)
                            ? $"{d:F1}s"
                            : "?";
                        string tag = fx.TryGetValue("effect_tag", out object t)
                            ? t?.ToString()
                            : "";
                        GUI.Label(
                            new Rect(30, y, 840, 20),
                            $"• {name}  [{tag}]  remaining: {remaining}"
                        );
                        y += 22f;
                    }
                    if (m_CachedVfxState.Count > 8)
                    {
                        GUI.Label(
                            new Rect(30, y, 840, 20),
                            $"  ...and {m_CachedVfxState.Count - 8} more"
                        );
                    }
                }
            }
        }

        private static string BuildRejectionSummary(
            System.Collections.Generic.KeyValuePair<string, int>[] reasons,
            int maxEntries
        )
        {
            if (reasons == null || reasons.Length == 0 || maxEntries <= 0)
            {
                return "none";
            }

            int count = Mathf.Min(maxEntries, reasons.Length);
            string summary = string.Empty;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    summary += ", ";
                }

                summary += $"{reasons[i].Key}={reasons[i].Value}";
            }

            return summary;
        }

        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            m_HasResponse = true;
            m_LastStatus = response.Status;
            if (response.Status == NetworkDialogueService.DialogueStatus.Completed)
            {
                m_LastErrorRaw = "-";
                m_LastErrorFriendly = "-";
                return;
            }

            m_LastErrorRaw = string.IsNullOrWhiteSpace(response.Error)
                ? "Dialogue failed."
                : response.Error.Trim();
            m_LastErrorFriendly = DialogueClientUI.FormatErrorMessage(m_LastErrorRaw);
        }
    }
}
