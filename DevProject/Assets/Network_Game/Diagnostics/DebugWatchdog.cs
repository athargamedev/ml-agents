using Network_Game.Dialogue;
using UnityEngine;

namespace Network_Game.Diagnostics
{
    /// <summary>
    /// Live Inspector watch panel for the LLM + dialogue debugging pipeline.
    /// Attach to any GameObject in Play mode. All fields refresh every PollInterval seconds.
    /// Pairs with VS breakpoints set at the [TRACE:*] log lines in the pipeline.
    /// </summary>
    public class DebugWatchdog : MonoBehaviour
    {
        [Header("LLM Debug Assistant")]
        [SerializeField]
        private int m_AnalysisCount;

        [SerializeField]
        private int m_AnalysisBudgetLeft;

        [SerializeField]
        private bool m_IsAnalyzing;

        [SerializeField]
        private int m_PendingQueueDepth;

        [SerializeField, TextArea(1, 3)]
        private string m_LastAnalysisSuggestion;

        [Header("Inference Timing")]
        [SerializeField, TextArea(1, 2)]
        private string m_LastPrompt;

        [SerializeField, TextArea(1, 2)]
        private string m_LastResponse;

        [SerializeField]
        private float m_LastInferenceMs;

        [SerializeField]
        private int m_TotalInferenceCalls;

        [Header("Dialogue Service")]
        [SerializeField]
        private bool m_ServicePresent;

        [SerializeField]
        private int m_PendingDialogueCount;

        [SerializeField]
        private int m_ActiveDialogueCount;

        [SerializeField]
        private string m_WarmupState;

        [SerializeField]
        private float m_SuccessRate;

        [SerializeField]
        private float m_P50QueueWaitMs;

        [SerializeField]
        private float m_P95ModelMs;

        [SerializeField]
        private int m_TimeoutCount;

        [Header("Log Stats (this session)")]
        [SerializeField]
        private int m_ErrorCount;

        [SerializeField]
        private int m_WarningCount;

        [SerializeField]
        private int m_LogCount;

        [SerializeField, TextArea(1, 2)]
        private string m_LastError;

        [Header("Poll Settings")]
        [SerializeField, Tooltip("Inspector refresh interval in seconds")]
        private float m_PollInterval = 0.25f;

        private float m_NextPoll;
        private LlmDebugAssistant m_Assistant;

        private void OnEnable()
        {
            InferenceWatchReporter.ActiveWatchdog = this;
            Application.logMessageReceived += OnLog;
            m_Assistant = FindAnyObjectByType<LlmDebugAssistant>();
        }

        private void OnDisable()
        {
            if (InferenceWatchReporter.ActiveWatchdog == this)
                InferenceWatchReporter.ActiveWatchdog = null;
            Application.logMessageReceived -= OnLog;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < m_NextPoll)
                return;
            m_NextPoll = Time.realtimeSinceStartup + m_PollInterval;
            PollAssistant();
            PollDialogueService();
        }

        private void PollAssistant()
        {
            if (m_Assistant == null)
                m_Assistant = FindAnyObjectByType<LlmDebugAssistant>();
            if (m_Assistant == null)
                return;

            LlmDebugAssistant.WatchSnapshot snap = m_Assistant.GetWatchSnapshot();
            m_AnalysisCount = snap.AnalysisCount;
            m_AnalysisBudgetLeft = snap.BudgetLeft;
            m_IsAnalyzing = snap.IsAnalyzing;
            m_PendingQueueDepth = snap.PendingQueueDepth;
            if (!string.IsNullOrEmpty(snap.LastSuggestion))
                m_LastAnalysisSuggestion = Truncate(snap.LastSuggestion, 200);
        }

        private void PollDialogueService()
        {
            var svc = NetworkDialogueService.Instance;
            m_ServicePresent = svc != null;
            if (svc == null)
                return;

            NetworkDialogueService.DialogueStats s = svc.GetStats();
            m_PendingDialogueCount = s.PendingCount;
            m_ActiveDialogueCount = s.ActiveCount;
            m_WarmupState = s.WarmupState;
            m_SuccessRate = s.SuccessRate;
            m_P50QueueWaitMs = s.QueueWaitHistogram.P50Ms;
            m_P95ModelMs = s.ModelExecutionHistogram.P95Ms;
            m_TimeoutCount = s.TimeoutCount;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                    m_ErrorCount++;
                    m_LastError = Truncate(condition, 150);
                    break;
                case LogType.Warning:
                    m_WarningCount++;
                    break;
                default:
                    m_LogCount++;
                    break;
            }
        }

        /// <summary>Called by InferenceWatchReporter (stripped in non-editor builds).</summary>
        internal void RecordInference(string prompt, string response, float elapsedMs)
        {
            m_TotalInferenceCalls++;
            m_LastPrompt = Truncate(prompt, 120);
            m_LastResponse = Truncate(response, 120);
            m_LastInferenceMs = elapsedMs;
        }

        private static string Truncate(string s, int max) =>
            s == null ? string.Empty
            : s.Length <= max ? s
            : s[..max] + "…";
    }
}
