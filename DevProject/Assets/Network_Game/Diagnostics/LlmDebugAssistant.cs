using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Network_Game.Dialogue;
using UnityEngine;

namespace Network_Game.Diagnostics
{
    /// <summary>
    /// Tracks runtime errors and uses the NetworkDialogueService to request
    /// an LLM analysis and code fix suggestion.
    /// </summary>
    public class LlmDebugAssistant : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Enable sending errors to LLM for analysis.")]
        private bool m_EnableLlmAnalysis = true;

        [SerializeField]
        [Tooltip("Only analyze errors that contain these keywords (leave empty for all).")]
        private List<string> m_FilterKeywords = new List<string>();

        [SerializeField]
        [Tooltip("Maximum number of analyses to perform per session to assume cost control.")]
        private int m_MaxAnalysesPerSession = 5;

        [SerializeField]
        [Tooltip(
            "Skip noisy transport bind errors (for example port already in use) so they don't consume LLM time."
        )]
        private bool m_IgnoreTransportBindNoise = true;

        private int m_AnalysisCount = 0;
        private string m_LastSuggestion = string.Empty;
        private readonly Queue<string> m_LogBuffer = new Queue<string>();

        // Using concurrent queue for thread-safe buffering of requests from background threads
        private readonly ConcurrentQueue<LogRequest> m_PendingRequests =
            new ConcurrentQueue<LogRequest>();

        private const int MAX_LOG_BUFFER = 15;
        private bool m_IsAnalyzing = false;

        private struct LogRequest
        {
            public string Condition;
            public string StackTrace;
        }

        private void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
        }

        private void Update()
        {
            // Process pending requests on the main thread
            if (m_IsAnalyzing || !m_EnableLlmAnalysis || m_AnalysisCount >= m_MaxAnalysesPerSession)
            {
                return;
            }

            if (m_PendingRequests.TryDequeue(out LogRequest request))
            {
                TriggerAnalysis(request.Condition, request.StackTrace);
            }
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            // Always buffer recent logs for context
            // We lock the regular queue since it's accessed from multiple threads
            lock (m_LogBuffer)
            {
                if (m_LogBuffer.Count >= MAX_LOG_BUFFER)
                {
                    m_LogBuffer.Dequeue();
                }
                m_LogBuffer.Enqueue($"[{type}] {condition}");
            }

            if (!m_EnableLlmAnalysis || m_AnalysisCount >= m_MaxAnalysesPerSession)
            {
                return;
            }

            // Only trigger on Error or Exception (and filter out our own analysis logs to avoid infinite loops)
            if (type != LogType.Error && type != LogType.Exception)
            {
                return;
            }

            if (condition.Contains("LLM Analysis Received"))
            {
                return;
            }

            if (m_IgnoreTransportBindNoise && IsTransportBindNoise(condition, stackTrace))
            {
                return;
            }

            // Check filters if any
            if (m_FilterKeywords.Count > 0)
            {
                bool match = false;
                foreach (var keyword in m_FilterKeywords)
                {
                    if (condition.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }
                if (!match)
                    return;
            }

            // Enqueue for processing on main thread
            m_PendingRequests.Enqueue(
                new LogRequest { Condition = condition, StackTrace = stackTrace }
            );
        }

        private static bool IsTransportBindNoise(string condition, string stackTrace)
        {
            string text = $"{condition}\n{stackTrace}";
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool hasBindSignal =
                text.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
                || text.Contains("failed to bind", StringComparison.OrdinalIgnoreCase)
                || text.Contains("bind socket", StringComparison.OrdinalIgnoreCase)
                || text.Contains("port 7777", StringComparison.OrdinalIgnoreCase);

            if (!hasBindSignal)
            {
                return false;
            }

            bool hasTransportSignal =
                text.Contains("UnityTransport", StringComparison.OrdinalIgnoreCase)
                || text.Contains("UTP", StringComparison.OrdinalIgnoreCase)
                || text.Contains("UDP", StringComparison.OrdinalIgnoreCase)
                || text.Contains("NetworkDriver", StringComparison.OrdinalIgnoreCase);
            return hasTransportSignal;
        }

        /// <summary>Snapshot of internal debug-assistant state for DebugWatchdog polling.</summary>
        public struct WatchSnapshot
        {
            public int AnalysisCount;
            public int BudgetLeft;
            public bool IsAnalyzing;
            public int PendingQueueDepth;
            public string LastSuggestion;
        }

        public WatchSnapshot GetWatchSnapshot() => new WatchSnapshot
        {
            AnalysisCount     = m_AnalysisCount,
            BudgetLeft        = m_MaxAnalysesPerSession - m_AnalysisCount,
            IsAnalyzing       = m_IsAnalyzing,
            PendingQueueDepth = m_PendingRequests.Count,
            LastSuggestion    = m_LastSuggestion,
        };

        /// <summary>
        /// VS tracepoint anchor — set a breakpoint here and use Actions → Log Message
        /// to print tag/count without pausing the editor thread.
        /// Calls are stripped in non-UNITY_EDITOR builds.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void TraceAnalysis(string tag, string errorCondition, int count) =>
            UnityEngine.Debug.Log($"[TRACE:LlmDebugAssistant:{tag}] error={errorCondition} analysisCount={count}");

        private async void TriggerAnalysis(string condition, string stackTrace)
        {
            // Double check in case frame state changed
            if (m_IsAnalyzing || m_AnalysisCount >= m_MaxAnalysesPerSession)
                return;

            TraceAnalysis("begin", condition, m_AnalysisCount + 1);
            m_IsAnalyzing = true;
            m_AnalysisCount++;

            try
            {
                StringBuilder contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("Recent Log History:");
                lock (m_LogBuffer)
                {
                    foreach (var log in m_LogBuffer)
                    {
                        contextBuilder.AppendLine(log);
                    }
                }

                if (NetworkDialogueService.Instance != null)
                {
                    NGLog.Info("LlmDebugAssistant", "Requesting LLM analysis for error...");

                    string suggestion = await NetworkDialogueService.Instance.AnalyzeDebugLog(
                        contextBuilder.ToString(),
                        $"{condition}\n{stackTrace}"
                    );

                    m_LastSuggestion = suggestion;
                    NGLog.Info("LlmDebugAssistant", "LLM Analysis Received:\n" + suggestion);
                    TraceAnalysis("complete", condition, m_AnalysisCount);

                    // Here you could also send the suggestion to a webhook, file, or in-game console
                }
            }
            catch (Exception ex)
            {
                NGLog.Warn("LlmDebugAssistant", $"Analysis failed to execute: {ex.Message}");
            }
            finally
            {
                m_IsAnalyzing = false;
            }
        }
    }
}
