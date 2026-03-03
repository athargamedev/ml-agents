using Network_Game.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

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
        private VisualElement m_UiHostRoot;
        private VisualElement m_UiOverlayRoot;
        private Label m_UiStatsLabel;
        private Label m_UiLastResponseLabel;
        private Label m_UiLastErrorLabel;
        private VisualElement m_UiLmList;
        private VisualElement m_UiVfxList;

        private void OnEnable()
        {
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
            m_NextRefreshTime = Time.realtimeSinceStartup;
        }

        private void OnDisable()
        {
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
            DestroyUiToolkitOverlay();
        }

        private void Update()
        {
            TryEnsureUiToolkitOverlay();

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

            RefreshUiToolkitOverlay();
        }

        private void OnGUI()
        {
            if (m_UiOverlayRoot != null && m_UiOverlayRoot.panel != null)
            {
                return;
            }

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

        private void TryEnsureUiToolkitOverlay()
        {
            if (m_UiHostRoot != null && !IsUsableUiToolkitHostRoot(m_UiHostRoot))
            {
                DestroyUiToolkitOverlay();
            }

            if (
                m_UiOverlayRoot != null
                && m_UiOverlayRoot.parent != null
                && m_UiOverlayRoot.panel != null
            )
            {
                return;
            }

            VisualElement hudZone = ModernHudController.TryGetZone(
                ModernHudController.HudZone.RightDock
            );
            if (hudZone != null)
            {
                BuildUiToolkitOverlay(hudZone, true);
                RefreshUiToolkitOverlay();
                return;
            }

            VisualElement hostRoot = FindUiToolkitHostRoot();
            if (hostRoot == null)
            {
                return;
            }

            BuildUiToolkitOverlay(hostRoot, false);
            RefreshUiToolkitOverlay();
        }

        private void BuildUiToolkitOverlay(VisualElement hostRoot, bool useHudZone)
        {
            m_UiHostRoot = hostRoot;

            var overlay = new VisualElement { name = "dialogue-debug-panel" };
            overlay.style.maxHeight = 340f;
            overlay.style.display = DisplayStyle.None;
            overlay.style.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.78f);
            overlay.style.borderTopLeftRadius = 10f;
            overlay.style.borderTopRightRadius = 10f;
            overlay.style.borderBottomLeftRadius = 10f;
            overlay.style.borderBottomRightRadius = 10f;
            overlay.style.borderTopWidth = 1f;
            overlay.style.borderRightWidth = 1f;
            overlay.style.borderBottomWidth = 1f;
            overlay.style.borderLeftWidth = 1f;
            overlay.style.borderTopColor = new Color(0.24f, 0.28f, 0.34f, 1f);
            overlay.style.borderRightColor = new Color(0.24f, 0.28f, 0.34f, 1f);
            overlay.style.borderBottomColor = new Color(0.24f, 0.28f, 0.34f, 1f);
            overlay.style.borderLeftColor = new Color(0.24f, 0.28f, 0.34f, 1f);
            overlay.style.paddingLeft = 10f;
            overlay.style.paddingRight = 10f;
            overlay.style.paddingTop = 8f;
            overlay.style.paddingBottom = 8f;
            overlay.pickingMode = PickingMode.Ignore;

            if (!useHudZone)
            {
                overlay.style.width = 520f;
                overlay.style.position = Position.Absolute;
                overlay.style.left = 12f;
                overlay.style.top = 160f;
            }
            else
            {
                overlay.style.width = new Length(100f, LengthUnit.Percent);
                overlay.style.position = Position.Relative;
                overlay.style.marginBottom = 8f;
                overlay.style.alignSelf = Align.FlexEnd;
            }

            var title = new Label($"Dialogue Debug  ({m_ToggleKey})");
            title.style.fontSize = 11f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.88f, 0.95f, 1f, 1f);
            title.style.marginBottom = 4f;
            overlay.Add(title);

            m_UiStatsLabel = CreateOverlayLabel(true);
            overlay.Add(m_UiStatsLabel);

            m_UiLastResponseLabel = CreateOverlayLabel(false);
            overlay.Add(m_UiLastResponseLabel);

            m_UiLastErrorLabel = CreateOverlayLabel(false);
            overlay.Add(m_UiLastErrorLabel);

            if (m_ShowLMStudio)
            {
                overlay.Add(CreateSectionHeader("LM Studio"));
                m_UiLmList = new VisualElement();
                m_UiLmList.style.marginBottom = 4f;
                overlay.Add(m_UiLmList);
            }

            if (m_ShowActiveVfx)
            {
                overlay.Add(CreateSectionHeader("Active VFX"));
                m_UiVfxList = new VisualElement();
                overlay.Add(m_UiVfxList);
            }

            hostRoot.Add(overlay);
            m_UiOverlayRoot = overlay;
        }

        private void RefreshUiToolkitOverlay()
        {
            if (m_UiOverlayRoot == null)
            {
                return;
            }

            m_UiOverlayRoot.style.display = m_ShowPanel ? DisplayStyle.Flex : DisplayStyle.None;
            if (!m_ShowPanel)
            {
                return;
            }

            var service = NetworkDialogueService.Instance;
            if (service == null)
            {
                if (m_UiStatsLabel != null)
                {
                    m_UiStatsLabel.text = "Dialogue service unavailable.";
                }
                return;
            }

            var stats = service.GetStats();
            if (m_UiStatsLabel != null)
            {
                m_UiStatsLabel.text =
                    $"Queue {stats.PendingCount}  Active {stats.ActiveCount}  Histories {stats.HistoryCount}  "
                    + $"Success {stats.SuccessRate:P0}  Timeout {stats.TimeoutRate:P0}  "
                    + $"Queue p50/p95 {stats.QueueWaitHistogram.P50Ms:F0}/{stats.QueueWaitHistogram.P95Ms:F0} ms";
            }

            if (m_UiLastResponseLabel != null)
            {
                string statusText = m_HasResponse ? m_LastStatus.ToString() : "None";
                m_UiLastResponseLabel.text = $"Last response: {statusText}";
            }

            if (m_UiLastErrorLabel != null)
            {
                m_UiLastErrorLabel.text = $"Last error: {m_LastErrorFriendly} | {m_LastErrorRaw}";
            }

            RebuildLmStudioList();
            RebuildVfxList();
        }

        private void RebuildLmStudioList()
        {
            if (m_UiLmList == null)
            {
                return;
            }

            m_UiLmList.Clear();
            if (m_CachedLMLog == null || m_CachedLMLog.Count == 0)
            {
                m_UiLmList.Add(CreateListLabel("(no LM Studio results yet)"));
                return;
            }

            int showCount = Mathf.Min(m_CachedLMLog.Count, 3);
            for (int i = 0; i < showCount; i++)
            {
                var entry = m_CachedLMLog[m_CachedLMLog.Count - 1 - i];
                string ts = System
                    .DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampMs)
                    .ToString("HH:mm:ss");
                m_UiLmList.Add(CreateListLabel($"[{ts}] {entry.Mode.ToUpper()} — {entry.Summary}"));
                if (!string.IsNullOrWhiteSpace(entry.Detail))
                {
                    m_UiLmList.Add(CreateListLabel(entry.Detail, 8f, 6f));
                }
            }
        }

        private void RebuildVfxList()
        {
            if (m_UiVfxList == null)
            {
                return;
            }

            m_UiVfxList.Clear();
            if (m_CachedVfxState == null || m_CachedVfxState.Count == 0)
            {
                m_UiVfxList.Add(CreateListLabel("(no active dialogue particle systems)"));
                return;
            }

            int showCount = Mathf.Min(m_CachedVfxState.Count, 5);
            for (int i = 0; i < showCount; i++)
            {
                var fx = m_CachedVfxState[i];
                string name = fx.TryGetValue("name", out object n) ? n?.ToString() : "?";
                string remaining = fx.TryGetValue("duration_remaining", out object d)
                    ? $"{d:F1}s"
                    : "?";
                string tag = fx.TryGetValue("effect_tag", out object t)
                    ? t?.ToString()
                    : string.Empty;
                m_UiVfxList.Add(CreateListLabel($"• {name}  [{tag}]  remaining: {remaining}"));
            }
        }

        private static Label CreateSectionHeader(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 10f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.88f, 0.95f, 1f, 1f);
            label.style.marginTop = 2f;
            label.style.marginBottom = 1f;
            return label;
        }

        private static Label CreateOverlayLabel(bool bold)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = 9f;
            label.style.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 2f;
            if (bold)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            return label;
        }

        private static Label CreateListLabel(
            string text,
            float fontSize = 8.5f,
            float leftMargin = 0f
        )
        {
            var label = new Label(text);
            label.style.fontSize = fontSize;
            label.style.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = leftMargin;
            label.style.marginBottom = 1f;
            return label;
        }

        private void DestroyUiToolkitOverlay()
        {
            if (m_UiOverlayRoot != null && m_UiOverlayRoot.parent != null)
            {
                m_UiOverlayRoot.parent.Remove(m_UiOverlayRoot);
            }

            m_UiHostRoot = null;
            m_UiOverlayRoot = null;
            m_UiStatsLabel = null;
            m_UiLastResponseLabel = null;
            m_UiLastErrorLabel = null;
            m_UiLmList = null;
            m_UiVfxList = null;
        }

        private static bool IsUsableUiToolkitHostRoot(VisualElement hostRoot)
        {
            if (hostRoot == null || hostRoot.panel == null)
            {
                return false;
            }

            return hostRoot.resolvedStyle.display != DisplayStyle.None;
        }

        private static VisualElement FindUiToolkitHostRoot()
        {
            ModernHudController hud = UnityEngine.Object.FindAnyObjectByType<ModernHudController>();
            if (hud != null)
            {
                UIDocument[] preferred =
                {
                    hud.DialogueDocument,
                    hud.ProfileDocument,
                    hud.LoginDocument,
                };

                for (int i = 0; i < preferred.Length; i++)
                {
                    UIDocument doc = preferred[i];
                    if (
                        doc != null
                        && doc.isActiveAndEnabled
                        && IsUsableUiToolkitHostRoot(doc.rootVisualElement)
                    )
                    {
                        return doc.rootVisualElement;
                    }
                }
            }

            UIDocument[] docs = UnityEngine.Object.FindObjectsByType<UIDocument>(
                FindObjectsInactive.Exclude
            );
            for (int i = 0; i < docs.Length; i++)
            {
                UIDocument doc = docs[i];
                if (
                    doc != null
                    && doc.isActiveAndEnabled
                    && IsUsableUiToolkitHostRoot(doc.rootVisualElement)
                )
                {
                    return doc.rootVisualElement;
                }
            }

            return null;
        }
    }
}
