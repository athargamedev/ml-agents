using Network_Game.Auth;
using Network_Game.Dialogue;
using PlayerController = Network_Game.ThirdPersonController.ThirdPersonController;
using Unity.Netcode;
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

        [Header("Auth + Spawn")]
        [SerializeField]
        private bool m_AuthServicePresent;

        [SerializeField]
        private bool m_HasAuthenticatedLocalPlayer;

        [SerializeField, TextArea(1, 2)]
        private string m_LocalNameId;

        [SerializeField]
        private ulong m_AttachedLocalPlayerNetworkId;

        [SerializeField]
        private bool m_LocalPlayerObjectPresent;

        [SerializeField, TextArea(1, 2)]
        private string m_LocalPlayerObjectName;

        [SerializeField]
        private bool m_LocalPlayerAttachmentMismatch;

        [SerializeField]
        private bool m_LocalControllerPresent;

        [SerializeField]
        private bool m_LocalControllerHasOwnerAuthority;

        [SerializeField]
        private bool m_LocalInputComponentEnabled;

        [SerializeField]
        private bool m_LocalPlayerInputEnabled;

        [SerializeField, TextArea(1, 2)]
        private string m_LocalActionMap;

        [SerializeField]
        private bool m_LocalFlyModeComponentEnabled;

        [SerializeField]
        private bool m_LocalCameraFollowAssigned;

        [Header("Network State")]
        [SerializeField]
        private bool m_NetworkManagerPresent;

        [SerializeField]
        private bool m_IsListening;

        [SerializeField]
        private bool m_IsServer;

        [SerializeField]
        private bool m_IsClient;

        [SerializeField]
        private bool m_IsConnectedClient;

        [SerializeField]
        private ulong m_LocalClientId;

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

        [Header("Last Dialogue Telemetry")]
        [SerializeField]
        private int m_LastRequestId;

        [SerializeField]
        private int m_LastClientRequestId;

        [SerializeField]
        private string m_LastDialogueStatus;

        [SerializeField]
        private string m_LastDialogueError;

        [SerializeField]
        private int m_LastRetryCount;

        [SerializeField]
        private float m_LastQueueLatencyMs;

        [SerializeField]
        private float m_LastModelLatencyMs;

        [SerializeField]
        private float m_LastTotalLatencyMs;

        [SerializeField]
        private bool m_LastUserInitiated;

        [SerializeField]
        private ulong m_LastSpeakerNetworkId;

        [SerializeField]
        private ulong m_LastListenerNetworkId;

        [SerializeField, TextArea(1, 2)]
        private string m_LastConversationKey;

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
            NetworkDialogueService.OnDialogueResponseTelemetry += HandleDialogueTelemetry;
            m_Assistant = FindAnyObjectByType<LlmDebugAssistant>();
        }

        private void OnDisable()
        {
            if (InferenceWatchReporter.ActiveWatchdog == this)
                InferenceWatchReporter.ActiveWatchdog = null;
            Application.logMessageReceived -= OnLog;
            NetworkDialogueService.OnDialogueResponseTelemetry -= HandleDialogueTelemetry;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < m_NextPoll)
                return;
            m_NextPoll = Time.realtimeSinceStartup + m_PollInterval;
            PollAssistant();
            PollNetworkState();
            PollAuthAndSpawn();
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

        private void PollAuthAndSpawn()
        {
            LocalPlayerAuthService auth = LocalPlayerAuthService.Instance;
            m_AuthServicePresent = auth != null;
            if (auth == null)
            {
                m_HasAuthenticatedLocalPlayer = false;
                m_LocalNameId = string.Empty;
                m_AttachedLocalPlayerNetworkId = 0;
                m_LocalPlayerObjectPresent = false;
                m_LocalPlayerObjectName = string.Empty;
                m_LocalPlayerAttachmentMismatch = false;
                m_LocalControllerPresent = false;
                m_LocalControllerHasOwnerAuthority = false;
                m_LocalInputComponentEnabled = false;
                m_LocalPlayerInputEnabled = false;
                m_LocalActionMap = string.Empty;
                m_LocalFlyModeComponentEnabled = false;
                m_LocalCameraFollowAssigned = false;
                return;
            }

            m_HasAuthenticatedLocalPlayer = auth.HasCurrentPlayer;
            m_LocalNameId = auth.HasCurrentPlayer ? Truncate(auth.CurrentPlayer.NameId, 80) : string.Empty;
            m_AttachedLocalPlayerNetworkId = auth.LocalPlayerNetworkId;

            NetworkObject localPlayerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            m_LocalPlayerObjectPresent = localPlayerObject != null;
            m_LocalPlayerObjectName =
                localPlayerObject != null ? Truncate(localPlayerObject.gameObject.name, 80) : string.Empty;

            ulong actualLocalPlayerNetworkId =
                localPlayerObject != null ? localPlayerObject.NetworkObjectId : 0;
            m_LocalPlayerAttachmentMismatch =
                m_HasAuthenticatedLocalPlayer
                && actualLocalPlayerNetworkId != 0
                && m_AttachedLocalPlayerNetworkId != actualLocalPlayerNetworkId;

            PlayerController controller =
                localPlayerObject != null ? localPlayerObject.GetComponent<PlayerController>() : null;
            m_LocalControllerPresent = controller != null;
            m_LocalControllerHasOwnerAuthority = controller != null && controller.IsOwner;
            m_LocalInputComponentEnabled = controller != null && controller.InputComponentEnabled;
            m_LocalPlayerInputEnabled = controller != null && controller.PlayerInputComponentEnabled;
            m_LocalActionMap = controller != null ? Truncate(controller.ActiveInputActionMap, 60) : string.Empty;
            m_LocalFlyModeComponentEnabled = controller != null && controller.FlyModeComponentEnabled;
            m_LocalCameraFollowAssigned = controller != null && controller.HasAssignedCameraFollow;
        }

        private void PollNetworkState()
        {
            NetworkManager manager = NetworkManager.Singleton;
            m_NetworkManagerPresent = manager != null;
            if (manager == null)
            {
                m_IsListening = false;
                m_IsServer = false;
                m_IsClient = false;
                m_IsConnectedClient = false;
                m_LocalClientId = 0;
                return;
            }

            m_IsListening = manager.IsListening;
            m_IsServer = manager.IsServer;
            m_IsClient = manager.IsClient;
            m_IsConnectedClient = manager.IsConnectedClient;
            m_LocalClientId = manager.LocalClientId;
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

        private void HandleDialogueTelemetry(NetworkDialogueService.DialogueResponseTelemetry telemetry)
        {
            m_LastRequestId = telemetry.RequestId;
            m_LastClientRequestId = telemetry.Request.ClientRequestId;
            m_LastDialogueStatus = telemetry.Status.ToString();
            m_LastDialogueError = Truncate(telemetry.Error, 180);
            m_LastRetryCount = telemetry.RetryCount;
            m_LastQueueLatencyMs = telemetry.QueueLatencyMs;
            m_LastModelLatencyMs = telemetry.ModelLatencyMs;
            m_LastTotalLatencyMs = telemetry.TotalLatencyMs;
            m_LastUserInitiated = telemetry.Request.IsUserInitiated;
            m_LastSpeakerNetworkId = telemetry.Request.SpeakerNetworkId;
            m_LastListenerNetworkId = telemetry.Request.ListenerNetworkId;
            m_LastConversationKey = Truncate(telemetry.Request.ConversationKey, 120);
        }

        private static string Truncate(string s, int max) =>
            s == null ? string.Empty
            : s.Length <= max ? s
            : s[..max] + "…";
    }
}
