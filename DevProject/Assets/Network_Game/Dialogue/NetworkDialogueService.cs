using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLMUnity;
using Network_Game.Auth;
using Network_Game.Diagnostics;
using Network_Game.Dialogue.Effects;
using Network_Game.Dialogue.MCP;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Server-authoritative dialogue service that routes LLMUnity requests and stores per-conversation history.
    /// </summary>
    [DefaultExecutionOrder(-450)]
    public class NetworkDialogueService : NetworkBehaviour
    {
        [Serializable]
        public struct DialogueRequest
        {
            public string Prompt;
            public string ConversationKey;
            public ulong SpeakerNetworkId;
            public ulong ListenerNetworkId;
            public ulong RequestingClientId;
            public bool Broadcast;
            public float BroadcastDuration;
            public bool NotifyClient;
            public int ClientRequestId;
            public bool IsUserInitiated;
            public bool BlockRepeatedPrompt;
            public float MinRepeatDelaySeconds;
            public bool RequireUserReply;
        }

        private const int GameplayProbeClientRequestIdMin = 99400;

        public enum DialogueStatus
        {
            Pending,
            InProgress,
            Completed,
            Failed,
            Cancelled,
        }

        public struct DialogueResponse
        {
            public int RequestId;
            public DialogueStatus Status;
            public string ResponseText;
            public string Error;
            public DialogueRequest Request;
        }

        public struct DialogueResponseTelemetry
        {
            public int RequestId;
            public DialogueStatus Status;
            public string Error;
            public DialogueRequest Request;
            public int RetryCount;
            public float QueueLatencyMs;
            public float ModelLatencyMs;
            public float TotalLatencyMs;
        }

        public struct PlayerIdentitySnapshot
        {
            public ulong ClientId;
            public ulong PlayerNetworkId;
            public string NameId;
            public string CustomizationJson;
            public string LastUpdatedUtc;
        }

        public struct DialogueStats
        {
            public int PendingCount;
            public int ActiveCount;
            public int HistoryCount;
            public bool HasLlmAgent;
            public bool IsServer;
            public bool IsClient;
            public string WarmupState;
            public bool WarmupInProgress;
            public bool WarmupDegraded;
            public int WarmupFailureCount;
            public float WarmupRetryInSeconds;
            public string WarmupLastFailureReason;
            public int TotalTerminalCompleted;
            public int TotalTerminalFailed;
            public int TotalTerminalCancelled;
            public int TotalTerminalRejected;
            public int TotalRequestsEnqueued;
            public int TotalRequestsFinished;
            public int TimeoutCount;
            public float TimeoutRate;
            public float SuccessRate;
            public LatencyHistogram QueueWaitHistogram;
            public LatencyHistogram ModelExecutionHistogram;
            public KeyValuePair<string, int>[] RejectionReasonCounts;
        }

        [Serializable]
        public struct LatencyHistogram
        {
            public int SampleCount;
            public float TotalMs;
            public float MinMs;
            public float MaxMs;
            public float P50Ms;
            public float P95Ms;
            public int Under100Ms;
            public int Under250Ms;
            public int Under500Ms;
            public int Under1000Ms;
            public int Under2000Ms;
            public int Over2000Ms;
        }

        private class DialogueRequestState
        {
            public DialogueRequest Request;
            public DialogueStatus Status;
            public string ResponseText;
            public string Error;
            public float EnqueuedAt;
            public float StartedAt;
            public float EffectiveTimeoutSeconds;
            public int RetryCount;
            public float FirstAttemptAt = float.MinValue;
            public float NextAttemptAt;
            public bool CompletionIssued;
        }

        private class ConversationState
        {
            public bool IsInFlight;
            public int ActiveRequestId = -1;
            public string LastCompletedPrompt;
            public float LastCompletedAt = float.MinValue;
            public bool AwaitingUserInput;
            public int UserMessageCount;
            public int AssistantMessageCount;
        }

        private struct PrefabPowerEffect
        {
            public string PrefabName;
            public float DurationSeconds;
            public float Scale;
            public Vector3 SpawnOffset;
            public bool SpawnInFront;
            public float ForwardDistance;
            public Color Color;
            public bool UseColorOverride;
            public string PowerName;
            public bool EnableGameplayDamage;
            public bool EnableHoming;
            public float ProjectileSpeed;
            public float HomingTurnRateDegrees;
            public float DamageAmount;
            public float DamageRadius;
            public bool AffectPlayerOnly;
            public string DamageType;
            public ulong TargetNetworkObjectId;
        }

        [Serializable]
        private class PlayerPromptContextBinding
        {
            public ulong PlayerNetworkId;
            public string NameId = string.Empty;
            public string CustomizationJson = "{}";
            public bool Enabled = true;
        }

        [Serializable]
        private class PlayerIdentityBinding
        {
            public ulong ClientId;
            public ulong PlayerNetworkId;
            public string NameId = string.Empty;
            public string CustomizationJson = "{}";
            public string LastUpdatedUtc = string.Empty;
            public bool Enabled = true;
        }

        /// <summary>
        /// Per-player effect modifiers derived from the player's customization data.
        /// Applied server-side in ApplyContextEffects before ClampDynamicMultiplier.
        /// </summary>
        private struct PlayerEffectModifier
        {
            public float DamageScaleReceived; // >1 = more vulnerable, <1 = resistant
            public float EffectSizeScale; // player-specific visual scale
            public float EffectDurationScale; // e.g., cursed player gets longer effects
            public Color? PreferredColor; // from customization "color_theme"
            public string PreferredElement; // from customization "element_affinity"
            public bool IsShielded; // from customization "has_shield"
            public float AggressionBias; // scales all offensive effect multipliers

            public static PlayerEffectModifier Neutral =>
                new PlayerEffectModifier
                {
                    DamageScaleReceived = 1f,
                    EffectSizeScale = 1f,
                    EffectDurationScale = 1f,
                    PreferredColor = null,
                    PreferredElement = null,
                    IsShielded = false,
                    AggressionBias = 1f,
                };
        }

        public static NetworkDialogueService Instance { get; private set; }
        public static event Action<DialogueResponse> OnDialogueResponse;
        public static event Action<DialogueResponseTelemetry> OnDialogueResponseTelemetry;

        public LLMAgent LlmAgent => m_LlmAgent;

        /// <summary>
        /// Check if LLM agent is ready for requests
        /// </summary>
        public bool IsLLMReady => m_LlmAgent != null;

        /// <summary>
        /// Check if warmup is in degraded mode
        /// </summary>
        public bool IsWarmupDegraded => m_WarmupDegradedMode;

        /// <summary>
        /// Get warmup failure count
        /// </summary>
        public int WarmupFailureCount => m_WarmupConsecutiveFailures;

        /// <summary>
        /// Get pending request count
        /// </summary>
        public int PendingRequestCount => m_RequestQueue.Count;

        /// <summary>
        /// Get active request count
        /// </summary>
        public int ActiveRequestCount => m_ActiveRequestIds.Count;

        /// <summary>
        /// Get total requests enqueued
        /// </summary>
        public int TotalRequestsEnqueued => m_TotalRequestsEnqueued;

        /// <summary>
        /// Get total requests finished
        /// </summary>
        public int TotalRequestsFinished => m_TotalRequestsFinished;

        /// <summary>
        /// Get total completed (terminal success)
        /// </summary>
        public int TotalCompleted => m_TotalTerminalCompleted;

        /// <summary>
        /// Get total failed (terminal failure)
        /// </summary>
        public int TotalFailed => m_TotalTerminalFailed;

        /// <summary>
        /// Get timeout count
        /// </summary>
        public int TimeoutCount => m_TimeoutCount;

        /// <summary>
        /// Get all conversation keys with history
        /// </summary>
        public string[] GetConversationKeys() => GetAllHistory().Keys.ToArray();

        /// <summary>
        /// Get history for a specific conversation (public accessor)
        /// </summary>
        /// <param name="conversationKey">The conversation key</param>
        /// <returns>List of chat messages or null if not found</returns>
        public List<ChatMessage> GetHistoryPublic(string conversationKey)
        {
            return m_Histories.TryGetValue(conversationKey, out var history) ? history : null;
        }

        /// <summary>
        /// Get all history dictionary (for debugging)
        /// </summary>
        public Dictionary<string, List<ChatMessage>> GetAllHistory() => m_Histories;

        /// <summary>
        /// Clear history for a specific conversation
        /// </summary>
        public void ClearHistory(string conversationKey)
        {
            if (m_Histories.ContainsKey(conversationKey))
            {
                m_Histories.Remove(conversationKey);
            }
            if (m_ConversationStates.ContainsKey(conversationKey))
            {
                m_ConversationStates.Remove(conversationKey);
            }
        }

        /// <summary>
        /// Clear all pending requests (emergency use only)
        /// </summary>
        public void ClearPendingRequests()
        {
            m_RequestQueue.Clear();
            foreach (var id in m_ActiveRequestIds)
            {
                if (m_Requests.TryGetValue(id, out var state))
                {
                    state.Status = DialogueStatus.Cancelled;
                    state.Error = "cleared_by_mcp";
                }
            }
            m_ActiveRequestIds.Clear();
        }

        /// <summary>
        /// Get rejection reason counts
        /// </summary>
        public Dictionary<string, int> GetRejectionReasons() =>
            new Dictionary<string, int>(m_RejectionReasonCounts);

        [Header("LLMUnity")]
        [SerializeField]
        private LLMAgent m_LlmAgent;

        [Header("Persona")]
        [SerializeField]
        private bool m_EnablePersonaRouting = true;

        [SerializeField]
        [TextArea(3, 8)]
        private string m_DefaultSystemPromptOverride = "";

        [Header("Player Prompt Context")]
        [SerializeField]
        private bool m_EnablePlayerPromptContext = true;

        [SerializeField]
        [Min(64)]
        private int m_MaxPlayerCustomizationChars = 720;

        [SerializeField]
        private List<PlayerPromptContextBinding> m_PlayerPromptContextBindings = new();

        [Header("Player Identity")]
        [SerializeField]
        private List<PlayerIdentityBinding> m_PlayerIdentityBindings = new();

        [Header("Scene Effects")]
        [SerializeField]
        private bool m_EnableContextSceneEffects = true;

        [SerializeField]
        private DialogueSceneEffectsController m_SceneEffectsController;

        [Header("Effect Spatial Mapping")]
        [SerializeField]
        private bool m_EnableSpatialEffectResolver = true;

        [SerializeField]
        private LayerMask m_EffectGroundMask = ~0;

        [SerializeField]
        private LayerMask m_EffectCollisionMask = ~0;

        [SerializeField]
        private LayerMask m_EffectLineOfSightMask = ~0;

        [SerializeField]
        [Min(0.05f)]
        private float m_EffectGroundProbeUp = 4f;

        [SerializeField]
        [Min(0.05f)]
        private float m_EffectGroundProbeDown = 12f;

        [SerializeField]
        [Min(0f)]
        private float m_EffectGroundOffset = 0.04f;

        [SerializeField]
        [Min(0.05f)]
        private float m_EffectCollisionClearanceRadius = 0.45f;

        [SerializeField]
        [Min(0.25f)]
        private float m_EffectFallbackDistance = 1.5f;

        [SerializeField]
        private int m_MaxHistoryMessages = 20;

        [SerializeField]
        private int m_MaxPendingRequests = 32;

        [SerializeField]
        [Range(1, 8)]
        private int m_MaxConcurrentRequests = 1;

        [SerializeField]
        private int m_MaxRequestsPerClient = 4;

        [SerializeField]
        private float m_MinSecondsBetweenRequests = 0.2f;

        [SerializeField]
        private float m_RequestTimeoutSeconds = 90f;

        [Header("Retry")]
        [SerializeField]
        [Min(0)]
        private int m_MaxRetries = 3;

        [SerializeField]
        [Min(0f)]
        private float m_RetryBackoffSeconds = 2f;

        [SerializeField]
        [Min(0f)]
        private float m_RetryJitterSeconds = 1f;

        [Header("Warmup")]
        [SerializeField]
        [Min(0f)]
        private float m_WarmupTimeoutSeconds = 60f;

        [SerializeField]
        [Min(1)]
        private int m_DegradedWarmupFailureThreshold = 3;

        [SerializeField]
        [Min(0f)]
        private float m_WarmupRetryCooldownSeconds = 5f;

        [Header("Broadcast")]
        [SerializeField]
        [Min(40)]
        private int m_BroadcastMaxCharacters = 180;

        [SerializeField]
        private bool m_BroadcastSingleLinePreview = true;

        [SerializeField]
        private bool m_LogDebug = true;

        private readonly Dictionary<int, DialogueRequestState> m_Requests = new();
        private readonly Queue<int> m_RequestQueue = new();
        private readonly HashSet<int> m_ActiveRequestIds = new();
        private readonly HashSet<string> m_ActiveConversationKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ChatMessage>> m_Histories = new();
        private readonly Dictionary<string, ConversationState> m_ConversationStates = new(
            StringComparer.Ordinal
        );
        private readonly List<string> m_ConversationKeysCache = new();
        private readonly Dictionary<ulong, float> m_LastRequestTimeByClient = new();
        private readonly Dictionary<
            ulong,
            PlayerPromptContextBinding
        > m_PlayerPromptContextByNetworkId = new();
        private readonly Dictionary<ulong, PlayerIdentityBinding> m_PlayerIdentityByClientId =
            new();
        private readonly Dictionary<ulong, PlayerIdentityBinding> m_PlayerIdentityByNetworkId =
            new();
        private readonly Queue<float> m_QueueWaitSamplesMs = new();
        private readonly Queue<float> m_ModelExecutionSamplesMs = new();
        private readonly Dictionary<string, int> m_RejectionReasonCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Queue<string> m_RejectionReasonOrder = new();
        private int m_TotalTerminalCompleted;
        private int m_TotalTerminalFailed;
        private int m_TotalTerminalCancelled;
        private int m_TotalTerminalRejected;
        private int m_TotalRequestsEnqueued;
        private int m_TotalRequestsFinished;
        private int m_TimeoutCount;
        private float m_LastSummaryLogAt;
        private const float kStuckCheckInterval = 1f;
        private const float kStuckConversationTimeout = 60f;
        private float m_LastStuckCheckTime;
        private int m_NextRequestId = 1;
        private bool m_IsProcessing;
        private Task m_WarmupTask;
        private int m_WarmupConsecutiveFailures;
        private float m_NextWarmupRetryAt;
        private bool m_WarmupDegradedMode;
        private string m_LastWarmupFailureReason = string.Empty;
        private string m_DefaultSystemPrompt = string.Empty;
        private EffectCatalog m_EffectCatalog;
        private bool m_EffectCatalogLoaded;

        [Header("Remote OpenAI")]
        [SerializeField]
        [Tooltip(
            "Model name for OpenAI-compatible remote servers. Leave empty if server has only one model loaded."
        )]
        private string m_RemoteModelName = "";

        [SerializeField]
        [Tooltip(
            "Stop sequences injected into every LM Studio request to prevent chat-template bleed. "
                + "Typical values for Llama-3 / Mistral LoRA models: [\"\u003c/s\u003e\", \"[INST]\", \"User:\", \"Assistant:\"]. "
                + "Leave empty to omit the stop field entirely (LM Studio uses its own template defaults)."
        )]
        private string[] m_RemoteStopSequences = new string[0];

        [SerializeField]
        [Min(120f)]
        [Tooltip(
            "Minimum timeout (seconds) used for remote OpenAI-compatible requests. Prevents premature cancellation during large prompt prefill."
        )]
        private float m_RemoteMinRequestTimeoutSeconds = 240f;

        [SerializeField]
        [Min(512)]
        [Tooltip(
            "Maximum system prompt characters sent to remote OpenAI-compatible backends. Long prompts are trimmed to reduce prefill latency."
        )]
        private int m_RemoteSystemPromptCharBudget = 8000;

        [SerializeField]
        [Min(0)]
        [Tooltip(
            "Maximum prior chat messages forwarded to remote backends. Lower values reduce prompt prefill latency."
        )]
        private int m_RemoteMaxHistoryMessages = 6;

        [SerializeField]
        [Min(1)]
        [Tooltip(
            "Hard cap for remote history message count, applied after m_RemoteMaxHistoryMessages."
        )]
        private int m_RemoteHistoryHardCapMessages = 8;

        [SerializeField]
        [Min(64)]
        [Tooltip("Maximum characters per prior history message sent to remote backends.")]
        private int m_RemoteHistoryMessageCharBudget = 320;

        [SerializeField]
        [Min(64)]
        [Tooltip("Maximum characters for the current user prompt sent to remote backends.")]
        private int m_RemoteUserPromptCharBudget = 520;

        [SerializeField]
        [Min(512)]
        [Tooltip("Absolute safety cap for remote system prompt size.")]
        private int m_RemoteSystemPromptHardCapChars = 3200;

        [SerializeField]
        [Min(64)]
        [Tooltip("Maximum player customization JSON characters included for remote prompts.")]
        private int m_RemoteMaxPlayerCustomizationChars = 220;

        private OpenAIChatClient m_OpenAIChatClient;
        private LlmAgentInferenceClient m_LlmAgentInferenceClient;

        // ML-Agents SideChannel override — set via SetMLAgentsSideChannelClient()
        private IDialogueInferenceClient m_OverrideClient;

        [Header("Diagnostics")]
        [SerializeField]
        [Min(32)]
        private int m_LatencySampleWindow = 256;

        [SerializeField]
        [Min(16)]
        private int m_RejectionReasonWindow = 256;

        [SerializeField]
        [Min(5f)]
        private float m_SummaryLogIntervalSeconds = 30f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                NGLog.Warn("Dialogue", $"Duplicate instance detected. Disabling. ({this})");
                enabled = false;
                return;
            }

            Instance = this;
            NormalizeRemoteRuntimeTuning();
            NGLog.Info("Dialogue", $"NetworkDialogueService initialized. ({this})");
            {
                m_LlmAgent = GetComponent<LLMAgent>();
                if (m_LlmAgent == null)
                {
#if UNITY_2023_1_OR_NEWER
                    m_LlmAgent = FindAnyObjectByType<LLMAgent>(FindObjectsInactive.Exclude);
#else
                    m_LlmAgent = FindObjectOfType<LLMAgent>();
#endif
                }
            }

            if (m_LlmAgent != null)
            {
                EnsureLlmAgentEnabled("Awake");
                m_DefaultSystemPrompt = m_LlmAgent.systemPrompt ?? string.Empty;
            }

            ValidateLlmConfiguration();

            if (m_SceneEffectsController == null)
            {
#if UNITY_2023_1_OR_NEWER
                m_SceneEffectsController = FindAnyObjectByType<DialogueSceneEffectsController>(
                    FindObjectsInactive.Exclude
                );
#else
                m_SceneEffectsController = FindObjectOfType<DialogueSceneEffectsController>();
#endif
            }

            RebuildPlayerPromptContextLookup();
            RebuildPlayerIdentityLookup();
            SynthesizeIdentityBindingsFromRuntimeBindings();
        }

        private void NormalizeRemoteRuntimeTuning()
        {
            m_RemoteMaxHistoryMessages = Mathf.Clamp(m_RemoteMaxHistoryMessages, 0, 32);
            m_RemoteHistoryHardCapMessages = Mathf.Clamp(m_RemoteHistoryHardCapMessages, 1, 64);
            if (m_RemoteHistoryHardCapMessages < m_RemoteMaxHistoryMessages)
            {
                m_RemoteHistoryHardCapMessages = m_RemoteMaxHistoryMessages;
            }

            m_RemoteHistoryMessageCharBudget = Mathf.Clamp(
                m_RemoteHistoryMessageCharBudget,
                64,
                2048
            );
            m_RemoteUserPromptCharBudget = Mathf.Clamp(m_RemoteUserPromptCharBudget, 64, 2048);
            m_RemoteSystemPromptCharBudget = Mathf.Clamp(
                m_RemoteSystemPromptCharBudget,
                512,
                24000
            );
            m_RemoteSystemPromptHardCapChars = Mathf.Clamp(
                m_RemoteSystemPromptHardCapChars,
                512,
                24000
            );
            if (m_RemoteSystemPromptHardCapChars < m_RemoteSystemPromptCharBudget)
            {
                m_RemoteSystemPromptHardCapChars = m_RemoteSystemPromptCharBudget;
            }
        }

        private void ValidateLlmConfiguration()
        {
            if (m_LlmAgent == null)
            {
                NGLog.Warn("Dialogue", "LLMAgent missing on NetworkDialogueService.");
                return;
            }

            if (m_LlmAgent.remote)
            {
                if (m_LogDebug)
                {
                    NGLog.Info(
                        "Dialogue",
                        "LLMAgent is configured as remote; warmup/chat uses remote provider."
                    );
                }

                DisableUnusedLocalLlmServers();
                return;
            }

            if (m_LlmAgent.llm == null && m_LogDebug)
            {
                NGLog.Warn(
                    "Dialogue",
                    "LLMAgent.LLM reference is null; EnsureLlmAgentReady will attempt to resolve it at runtime."
                );
            }
        }

        private void DisableUnusedLocalLlmServers()
        {
#if UNITY_2023_1_OR_NEWER
            var llmServers = FindObjectsByType<LLMUnity.LLM>(FindObjectsInactive.Exclude);
#else
            var llmServers = FindObjectsOfType<LLMUnity.LLM>();
#endif
            if (llmServers == null)
            {
                return;
            }

            for (int i = 0; i < llmServers.Length; i++)
            {
                LLMUnity.LLM llm = llmServers[i];
                if (llm == null)
                {
                    continue;
                }

                llm.gameObject.SetActive(false);
                NGLog.Info(
                    "Dialogue",
                    NGLog.Format(
                        "Deactivated local LLM server (remote mode active)",
                        ("llm", llm.gameObject.name)
                    )
                );
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now - m_LastStuckCheckTime < kStuckCheckInterval)
            {
                return;
            }
            m_LastStuckCheckTime = now;
            RecoverStuckConversations(now);
        }

        private void RecoverStuckConversations(float now)
        {
            // Snapshot keys to avoid modifying collection during iteration.
            m_ConversationKeysCache.Clear();
            m_ConversationKeysCache.AddRange(m_ConversationStates.Keys);
            for (int i = 0; i < m_ConversationKeysCache.Count; i++)
            {
                string conversationKey = m_ConversationKeysCache[i];
                if (
                    !m_ConversationStates.TryGetValue(
                        conversationKey,
                        out ConversationState conversationState
                    )
                )
                {
                    continue;
                }

                if (!conversationState.IsInFlight)
                {
                    continue;
                }

                int activeId = conversationState.ActiveRequestId;
                if (activeId < 0)
                {
                    conversationState.IsInFlight = false;
                    conversationState.ActiveRequestId = -1;
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Recovered orphaned conversation (no active request)",
                            ("key", conversationKey)
                        )
                    );
                    continue;
                }

                if (!m_Requests.TryGetValue(activeId, out DialogueRequestState reqState))
                {
                    conversationState.IsInFlight = false;
                    conversationState.ActiveRequestId = -1;
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Recovered orphaned conversation (request state missing)",
                            ("key", conversationKey),
                            ("activeId", activeId)
                        )
                    );
                    continue;
                }

                float elapsed = now - reqState.EnqueuedAt;
                float requestTimeoutSeconds =
                    reqState.EffectiveTimeoutSeconds > 0f
                        ? reqState.EffectiveTimeoutSeconds
                        : (
                            UseOpenAIRemote
                                ? Mathf.Max(
                                    m_RequestTimeoutSeconds,
                                    m_RemoteMinRequestTimeoutSeconds
                                )
                                : m_RequestTimeoutSeconds
                        );
                float stuckTimeoutSeconds = Mathf.Max(
                    kStuckConversationTimeout,
                    requestTimeoutSeconds + 20f
                );
                if (elapsed > stuckTimeoutSeconds)
                {
                    reqState.Status = DialogueStatus.Failed;
                    reqState.Error = "Request timed out (stuck recovery).";
                    CompleteConversationRequest(activeId, reqState, false);
                    NotifyIfRequested(activeId, reqState);
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Force-recovered stuck request",
                            ("id", activeId),
                            ("key", conversationKey),
                            ("elapsed", elapsed),
                            ("stuckTimeout", stuckTimeoutSeconds)
                        )
                    );
                }
            }
        }

        public bool TryGetPlayerIdentityByClientId(
            ulong clientId,
            out PlayerIdentitySnapshot snapshot
        )
        {
            snapshot = default;
            if (
                !m_PlayerIdentityByClientId.TryGetValue(clientId, out var identity)
                || identity == null
                || !identity.Enabled
            )
            {
                return false;
            }

            snapshot = ToSnapshot(identity);
            return true;
        }

        public bool TryGetPlayerIdentityByNetworkId(
            ulong playerNetworkId,
            out PlayerIdentitySnapshot snapshot
        )
        {
            snapshot = default;
            if (
                playerNetworkId == 0
                || !m_PlayerIdentityByNetworkId.TryGetValue(playerNetworkId, out var identity)
                || identity == null
                || !identity.Enabled
            )
            {
                return false;
            }

            snapshot = ToSnapshot(identity);
            return true;
        }

        public bool SetPlayerPromptContext(
            ulong playerNetworkId,
            string nameId,
            string customizationJson
        )
        {
            if (!IsServer)
            {
                NGLog.Warn("Dialogue", "SetPlayerPromptContext called on non-server.");
                return false;
            }

            if (playerNetworkId == 0)
            {
                return false;
            }

            string normalizedNameId = string.IsNullOrWhiteSpace(nameId)
                ? string.Empty
                : nameId.Trim();
            string normalizedCustomizationJson = NormalizePromptContextJson(
                normalizedNameId,
                customizationJson
            );

            PlayerPromptContextBinding binding = FindOrCreatePlayerPromptContextBinding(
                playerNetworkId
            );
            binding.NameId = normalizedNameId;
            binding.CustomizationJson = normalizedCustomizationJson;
            binding.Enabled = true;
            m_PlayerPromptContextByNetworkId[playerNetworkId] = binding;
            UpsertPlayerIdentity(
                ResolveOwnerClientIdForPlayerNetworkId(playerNetworkId),
                playerNetworkId,
                normalizedNameId,
                normalizedCustomizationJson
            );
            return true;
        }

        public bool ClearPlayerPromptContext(ulong playerNetworkId)
        {
            if (!IsServer || playerNetworkId == 0)
            {
                return false;
            }

            bool removedAny = false;
            removedAny |= m_PlayerPromptContextByNetworkId.Remove(playerNetworkId);
            if (m_PlayerPromptContextBindings != null)
            {
                for (int i = m_PlayerPromptContextBindings.Count - 1; i >= 0; i--)
                {
                    PlayerPromptContextBinding binding = m_PlayerPromptContextBindings[i];
                    if (binding == null || binding.PlayerNetworkId != playerNetworkId)
                    {
                        continue;
                    }

                    m_PlayerPromptContextBindings.RemoveAt(i);
                    removedAny = true;
                }
            }

            UpsertPlayerIdentity(
                ResolveOwnerClientIdForPlayerNetworkId(playerNetworkId),
                playerNetworkId,
                null,
                "{}"
            );

            return removedAny;
        }

        public bool SetPlayerPromptContextForClient(
            ulong clientId,
            string nameId,
            string customizationJson
        )
        {
            if (!TryGetPlayerNetworkObjectIdForClient(clientId, out ulong playerNetworkId))
            {
                return false;
            }

            bool applied = SetPlayerPromptContext(playerNetworkId, nameId, customizationJson);
            if (applied)
            {
                UpsertPlayerIdentity(clientId, playerNetworkId, nameId, customizationJson);
            }

            return applied;
        }

        public bool ClearPlayerPromptContextForClient(ulong clientId)
        {
            return TryGetPlayerNetworkObjectIdForClient(clientId, out ulong playerNetworkId)
                && ClearPlayerPromptContext(playerNetworkId);
        }

        public bool RequestSetPlayerPromptContextFromClient(string nameId, string customizationJson)
        {
            if (IsServer || !IsClient)
            {
                return false;
            }

            SetPlayerPromptContextServerRpc(nameId ?? string.Empty, customizationJson ?? "{}");
            return true;
        }

        public bool RequestClearPlayerPromptContextFromClient()
        {
            if (IsServer || !IsClient)
            {
                return false;
            }

            ClearPlayerPromptContextServerRpc();
            return true;
        }

        public int EnqueueRequest(DialogueRequest request)
        {
            return TryEnqueueRequest(request, out int requestId, out _) ? requestId : -1;
        }

        public bool TryEnqueueRequest(
            DialogueRequest request,
            out int requestId,
            out string rejectionReason
        )
        {
            requestId = -1;
            rejectionReason = null;
            if (!IsServer)
            {
                NGLog.Warn("Dialogue", "EnqueueRequest called on non-server.");
                rejectionReason = "not_server";
                return false;
            }

            if (m_Requests.Count >= m_MaxPendingRequests)
            {
                NGLog.Warn("Dialogue", "Max pending request limit reached.");
                rejectionReason = "queue_full";
                TrackRejected(rejectionReason);
                return false;
            }

            request.ConversationKey = ResolveConversationKey(
                request.SpeakerNetworkId,
                request.ListenerNetworkId,
                request.RequestingClientId,
                request.ConversationKey
            );

            if (!CanAcceptRequest(request, out string reason))
            {
                rejectionReason = reason;
                TrackRejected(rejectionReason);
                if (m_LogDebug)
                {
                    NGLog.Warn("Dialogue", $"Request rejected: {reason}");
                }
                return false;
            }

            requestId = m_NextRequestId++;
            m_TotalRequestsEnqueued++;
            m_Requests[requestId] = new DialogueRequestState
            {
                Request = request,
                Status = DialogueStatus.Pending,
                EnqueuedAt = Time.realtimeSinceStartup,
            };
            m_RequestQueue.Enqueue(requestId);

            if (!m_IsProcessing)
            {
                _ = ProcessQueue();
            }

            NGLog.Info(
                "Dialogue",
                NGLog.Format(
                    "Enqueued request",
                    ("id", requestId),
                    ("key", request.ConversationKey),
                    ("speaker", request.SpeakerNetworkId),
                    ("listener", request.ListenerNetworkId),
                    ("requester", request.RequestingClientId)
                )
            );
            return true;
        }

        public bool TryConsumeResponse(int requestId, out DialogueResponse response)
        {
            response = default;
            if (!m_Requests.TryGetValue(requestId, out DialogueRequestState state))
            {
                return false;
            }

            if (
                state.Status == DialogueStatus.Completed
                || state.Status == DialogueStatus.Failed
                || state.Status == DialogueStatus.Cancelled
            )
            {
                response = new DialogueResponse
                {
                    RequestId = requestId,
                    Status = state.Status,
                    ResponseText = state.ResponseText,
                    Error = state.Error,
                    Request = state.Request,
                };
                m_Requests.Remove(requestId);
                return true;
            }

            return false;
        }

        public bool TryConsumeResponseByClientRequestId(
            int clientRequestId,
            out DialogueResponse response,
            ulong requestingClientId = ulong.MaxValue
        )
        {
            response = default;
            if (clientRequestId <= 0)
            {
                return false;
            }

            int matchedRequestId = -1;
            foreach (var pair in m_Requests)
            {
                DialogueRequestState state = pair.Value;
                if (state == null || state.Request.ClientRequestId != clientRequestId)
                {
                    continue;
                }

                if (
                    requestingClientId != ulong.MaxValue
                    && state.Request.RequestingClientId != requestingClientId
                )
                {
                    continue;
                }

                if (
                    state.Status != DialogueStatus.Completed
                    && state.Status != DialogueStatus.Failed
                    && state.Status != DialogueStatus.Cancelled
                )
                {
                    return false;
                }

                matchedRequestId = pair.Key;
                break;
            }

            return matchedRequestId > 0 && TryConsumeResponse(matchedRequestId, out response);
        }

        public bool TryGetTerminalResponseByClientRequestId(
            int clientRequestId,
            out DialogueResponse response,
            ulong requestingClientId = ulong.MaxValue
        )
        {
            response = default;
            if (clientRequestId <= 0)
            {
                return false;
            }

            foreach (var pair in m_Requests)
            {
                DialogueRequestState state = pair.Value;
                if (state == null || state.Request.ClientRequestId != clientRequestId)
                {
                    continue;
                }

                if (
                    requestingClientId != ulong.MaxValue
                    && state.Request.RequestingClientId != requestingClientId
                )
                {
                    continue;
                }

                if (
                    state.Status != DialogueStatus.Completed
                    && state.Status != DialogueStatus.Failed
                    && state.Status != DialogueStatus.Cancelled
                )
                {
                    return false;
                }

                response = new DialogueResponse
                {
                    RequestId = pair.Key,
                    Status = state.Status,
                    ResponseText = state.ResponseText,
                    Error = state.Error,
                    Request = state.Request,
                };
                return true;
            }

            return false;
        }

        public DialogueStats GetStats()
        {
            int active = 0;
            foreach (var entry in m_Requests.Values)
            {
                if (
                    entry.Status == DialogueStatus.Pending
                    || entry.Status == DialogueStatus.InProgress
                )
                {
                    active++;
                }
            }

            int terminal =
                m_TotalTerminalCompleted + m_TotalTerminalFailed + m_TotalTerminalCancelled;
            int completedOrFailed = Math.Max(1, m_TotalTerminalCompleted + m_TotalTerminalFailed);
            return new DialogueStats
            {
                PendingCount = m_RequestQueue.Count,
                ActiveCount = active,
                HistoryCount = m_Histories.Count,
                HasLlmAgent = m_LlmAgent != null,
                IsServer = IsServer,
                IsClient = IsClient,
                WarmupState = BuildWarmupStateLabel(),
                WarmupInProgress = m_WarmupTask != null && !m_WarmupTask.IsCompleted,
                WarmupDegraded = m_WarmupDegradedMode,
                WarmupFailureCount = m_WarmupConsecutiveFailures,
                WarmupRetryInSeconds = Mathf.Max(
                    0f,
                    m_NextWarmupRetryAt - Time.realtimeSinceStartup
                ),
                WarmupLastFailureReason = m_LastWarmupFailureReason ?? string.Empty,
                TotalTerminalCompleted = m_TotalTerminalCompleted,
                TotalTerminalFailed = m_TotalTerminalFailed,
                TotalTerminalCancelled = m_TotalTerminalCancelled,
                TotalTerminalRejected = m_TotalTerminalRejected,
                TotalRequestsEnqueued = m_TotalRequestsEnqueued,
                TotalRequestsFinished = m_TotalRequestsFinished,
                TimeoutCount = m_TimeoutCount,
                TimeoutRate = m_TimeoutCount / (float)completedOrFailed,
                SuccessRate = m_TotalTerminalCompleted / (float)Math.Max(1, terminal),
                QueueWaitHistogram = BuildLatencyHistogram(m_QueueWaitSamplesMs),
                ModelExecutionHistogram = BuildLatencyHistogram(m_ModelExecutionSamplesMs),
                RejectionReasonCounts = BuildRejectionCountsSnapshot(),
            };
        }

        public bool IsClientRequestInFlight(
            int clientRequestId,
            ulong requestingClientId = ulong.MaxValue
        )
        {
            if (clientRequestId <= 0)
            {
                return false;
            }

            foreach (var pair in m_Requests)
            {
                DialogueRequestState state = pair.Value;
                if (state == null || state.Request.ClientRequestId != clientRequestId)
                {
                    continue;
                }

                if (
                    requestingClientId != ulong.MaxValue
                    && state.Request.RequestingClientId != requestingClientId
                )
                {
                    continue;
                }

                return state.Status == DialogueStatus.Pending
                    || state.Status == DialogueStatus.InProgress;
            }

            return false;
        }

        [ContextMenu("Dialogue/Log Player Identity Report")]
        public void LogPlayerIdentityReport()
        {
            NGLog.Info("Dialogue", BuildPlayerIdentityReport());
        }

        public string BuildPlayerIdentityReport()
        {
            var sb = new StringBuilder(256);
            sb.Append("Player identity bindings:");
            int count = 0;

            foreach (var pair in m_PlayerIdentityByClientId)
            {
                PlayerIdentityBinding identity = pair.Value;
                if (identity == null || !identity.Enabled)
                {
                    continue;
                }

                count++;
                sb.Append(" [client=")
                    .Append(identity.ClientId)
                    .Append(", playerNetId=")
                    .Append(identity.PlayerNetworkId)
                    .Append(", name_id=")
                    .Append(
                        string.IsNullOrWhiteSpace(identity.NameId) ? "unknown" : identity.NameId
                    )
                    .Append("]");
            }

            if (count == 0)
            {
                sb.Append(" none");
            }

            return sb.ToString();
        }

        public string ResolveConversationKey(
            ulong speakerNetworkId,
            ulong listenerNetworkId,
            ulong requestingClientId,
            string conversationKeyOverride = null
        )
        {
            if (!string.IsNullOrWhiteSpace(conversationKeyOverride))
            {
                return conversationKeyOverride.Trim();
            }

            if (speakerNetworkId != 0 && listenerNetworkId != 0)
            {
                ulong first = Math.Min(speakerNetworkId, listenerNetworkId);
                ulong second = Math.Max(speakerNetworkId, listenerNetworkId);
                return $"{first}:{second}";
            }

            if (speakerNetworkId != 0)
            {
                return $"actor:{speakerNetworkId}";
            }

            if (listenerNetworkId != 0)
            {
                return $"actor:{listenerNetworkId}";
            }

            return $"client:{requestingClientId}";
        }

        /// <summary>
        /// Lightweight pre-check for auto-trigger dialogue nodes so they can skip enqueue attempts
        /// when the conversation is intentionally gated (for example waiting for user reply).
        /// </summary>
        public bool TryGetAutoRequestBlockReason(
            string conversationKey,
            string prompt,
            bool blockRepeatedPrompt,
            float minRepeatDelaySeconds,
            bool requireUserReply,
            out string reason
        )
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(conversationKey))
            {
                return false;
            }

            string key = ResolveConversationKey(0, 0, 0, conversationKey);
            ConversationState state = GetConversationState(key);
            if (state.IsInFlight)
            {
                reason = "conversation_in_flight";
                return true;
            }

            if (requireUserReply && state.AwaitingUserInput)
            {
                reason = "awaiting_user_message";
                return true;
            }

            if (
                blockRepeatedPrompt
                && !string.IsNullOrWhiteSpace(prompt)
                && !string.IsNullOrWhiteSpace(state.LastCompletedPrompt)
                && string.Equals(
                    state.LastCompletedPrompt,
                    prompt,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                reason = "duplicate_prompt";
                return true;
            }

            if (minRepeatDelaySeconds > 0f && state.LastCompletedAt > float.MinValue)
            {
                float elapsedSinceLast = Time.realtimeSinceStartup - state.LastCompletedAt;
                if (elapsedSinceLast < minRepeatDelaySeconds)
                {
                    reason = "repeat_delay";
                    return true;
                }
            }

            return false;
        }

        public void RequestDialogue(DialogueRequest request)
        {
            if (IsServer)
            {
                if (!TryEnqueueRequest(request, out int requestId, out string rejectionReason))
                {
                    string key = ResolveConversationKey(
                        request.SpeakerNetworkId,
                        request.ListenerNetworkId,
                        request.RequestingClientId,
                        request.ConversationKey
                    );
                    NGLog.Warn(
                        "Dialogue",
                        $"Request rejected on server | reason={rejectionReason ?? "unknown"} | key={key}"
                    );
                    if (request.NotifyClient)
                    {
                        PublishLocalRejection(
                            requestId,
                            request.ClientRequestId,
                            key,
                            rejectionReason,
                            request.SpeakerNetworkId,
                            request.ListenerNetworkId
                        );
                    }
                }
                return;
            }

            if (!IsClient)
            {
                NGLog.Warn("Dialogue", "RequestDialogue called without client/server.");
                if (request.NotifyClient)
                {
                    PublishLocalRejection(
                        0,
                        request.ClientRequestId,
                        request.ConversationKey,
                        "not_server",
                        request.SpeakerNetworkId,
                        request.ListenerNetworkId
                    );
                }
                return;
            }

            NGLog.Debug("Dialogue", "RequestDialogue sending ServerRpc");
            RequestDialogueServerRpc(
                request.Prompt,
                request.ConversationKey,
                request.SpeakerNetworkId,
                request.ListenerNetworkId,
                request.Broadcast,
                request.BroadcastDuration,
                request.ClientRequestId,
                request.IsUserInitiated,
                request.BlockRepeatedPrompt,
                request.MinRepeatDelaySeconds,
                request.RequireUserReply
            );
        }

        public void CancelRequest(int requestId)
        {
            if (m_Requests.TryGetValue(requestId, out DialogueRequestState state))
            {
                state.Status = DialogueStatus.Cancelled;
                state.Error = "request_cancelled";
            }
        }

        private void PublishLocalRejection(
            int requestId,
            int clientRequestId,
            string conversationKey,
            string rejectionReason,
            ulong speakerNetworkId,
            ulong listenerNetworkId
        )
        {
            var response = new DialogueResponse
            {
                RequestId = requestId,
                Status = DialogueStatus.Failed,
                ResponseText = string.Empty,
                Error = string.IsNullOrWhiteSpace(rejectionReason)
                    ? "request_rejected"
                    : rejectionReason,
                Request = new DialogueRequest
                {
                    ConversationKey = conversationKey ?? string.Empty,
                    ClientRequestId = clientRequestId,
                    SpeakerNetworkId = speakerNetworkId,
                    ListenerNetworkId = listenerNetworkId,
                },
            };

            OnDialogueResponse?.Invoke(response);
            OnDialogueResponseTelemetry?.Invoke(
                new DialogueResponseTelemetry
                {
                    RequestId = requestId,
                    Status = DialogueStatus.Failed,
                    Error = response.Error,
                    Request = response.Request,
                    RetryCount = 0,
                    QueueLatencyMs = 0f,
                    ModelLatencyMs = 0f,
                    TotalLatencyMs = 0f,
                }
            );
        }

        private void SendRejectedDialogueResponseToClient(
            ulong targetClientId,
            int requestId,
            int clientRequestId,
            string rejectionReason,
            string conversationKey,
            ulong speakerNetworkId,
            ulong listenerNetworkId,
            bool isUserInitiated = false
        )
        {
            DialogueResponseClientRpc(
                requestId,
                clientRequestId,
                DialogueStatus.Failed,
                string.Empty,
                string.IsNullOrWhiteSpace(rejectionReason) ? "request_rejected" : rejectionReason,
                conversationKey ?? string.Empty,
                speakerNetworkId,
                listenerNetworkId,
                targetClientId,
                isUserInitiated,
                RpcTarget.Single(targetClientId, RpcTargetUse.Temp)
            );
        }

        public bool AppendMessage(string conversationKey, string role, string content)
        {
            if (!IsServer)
            {
                if (!IsClient)
                {
                    NGLog.Warn("Dialogue", "AppendMessage called without client/server.");
                    return false;
                }

                AppendMessageServerRpc(conversationKey, role, content);
                return true;
            }

            return AppendMessageInternal(conversationKey, role, content);
        }

        private bool AppendMessageInternal(string conversationKey, string role, string content)
        {
            if (string.IsNullOrWhiteSpace(conversationKey) || string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            conversationKey = ResolveConversationKey(0, 0, 0, conversationKey);
            var history = GetHistory(conversationKey);
            string normalizedRole = string.IsNullOrWhiteSpace(role)
                ? "user"
                : role.Trim().ToLowerInvariant();
            history.Add(new ChatMessage(normalizedRole, content));
            StoreHistory(conversationKey, history);

            ConversationState conversationState = GetConversationState(conversationKey);
            if (normalizedRole == "user")
            {
                conversationState.AwaitingUserInput = false;
                conversationState.UserMessageCount++;
            }
            else if (normalizedRole == "assistant")
            {
                conversationState.AwaitingUserInput = true;
                conversationState.AssistantMessageCount++;
            }

            return true;
        }

        private async Task ProcessQueue()
        {
            if (m_IsProcessing)
            {
                return;
            }

            m_IsProcessing = true;
            if (m_LogDebug)
            {
                NGLog.Debug(
                    "Dialogue",
                    NGLog.Format("ProcessQueue start", ("pending", m_RequestQueue.Count))
                );
            }
            while (m_RequestQueue.Count > 0 || m_ActiveRequestIds.Count > 0)
            {
                bool startedWorker = false;
                int maxWorkers = Mathf.Max(1, m_MaxConcurrentRequests);
                while (m_ActiveRequestIds.Count < maxWorkers)
                {
                    if (
                        !TryDequeueNextRequestForExecution(
                            out int requestId,
                            out DialogueRequestState state
                        )
                    )
                    {
                        break;
                    }

                    m_ActiveRequestIds.Add(requestId);
                    startedWorker = true;
                    _ = ExecuteRequestWorkerAsync(requestId, state);
                }

                if (!startedWorker)
                {
                    await Task.Delay(10);
                }
            }

            m_IsProcessing = false;
            if (m_LogDebug)
            {
                NGLog.Debug("Dialogue", "ProcessQueue complete");
            }
        }

        private bool TryDequeueNextRequestForExecution(
            out int requestId,
            out DialogueRequestState state
        )
        {
            requestId = -1;
            state = null;

            int pendingCount = m_RequestQueue.Count;
            if (pendingCount <= 0)
            {
                return false;
            }

            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < pendingCount; i++)
            {
                int candidateId = m_RequestQueue.Dequeue();
                if (!m_Requests.TryGetValue(candidateId, out DialogueRequestState candidateState))
                {
                    if (m_LogDebug)
                    {
                        NGLog.Warn(
                            "Dialogue",
                            NGLog.Format("Missing request state", ("id", candidateId))
                        );
                    }
                    continue;
                }

                if (
                    candidateState.Status == DialogueStatus.Completed
                    || candidateState.Status == DialogueStatus.Failed
                    || candidateState.Status == DialogueStatus.Cancelled
                )
                {
                    continue;
                }

                if (candidateState.NextAttemptAt > 0f && now < candidateState.NextAttemptAt)
                {
                    m_RequestQueue.Enqueue(candidateId);
                    continue;
                }

                requestId = candidateId;
                state = candidateState;
                return true;
            }

            return false;
        }

        private async Task ExecuteRequestWorkerAsync(int requestId, DialogueRequestState state)
        {
            if (state == null)
            {
                m_ActiveRequestIds.Remove(requestId);
                return;
            }

            bool terminal = false;
            string activeConversationKey = null;

            try
            {
                if (state.Status == DialogueStatus.Cancelled)
                {
                    state.Error = string.IsNullOrWhiteSpace(state.Error)
                        ? "request_cancelled"
                        : state.Error;
                    terminal = true;
                    return;
                }

                float now = Time.realtimeSinceStartup;
                if (state.FirstAttemptAt == float.MinValue)
                {
                    state.FirstAttemptAt = now;
                }

                if (m_LlmAgent == null)
                {
                    state.Status = DialogueStatus.Failed;
                    state.Error = "LLMAgent not assigned.";
                    terminal = true;
                    return;
                }

                EnsureLlmAgentReady();
                bool warmupReady = await EnsureWarmup();
                if (!warmupReady)
                {
                    state.Status = DialogueStatus.Failed;
                    state.Error = string.IsNullOrWhiteSpace(m_LastWarmupFailureReason)
                        ? "LLM warmup unavailable."
                        : m_LastWarmupFailureReason;
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Request failed during warmup",
                            ("id", requestId),
                            ("reason", state.Error),
                            ("degraded", m_WarmupDegradedMode),
                            (
                                "retryIn",
                                Mathf.Max(0f, m_NextWarmupRetryAt - Time.realtimeSinceStartup)
                            )
                        )
                    );
                    terminal = true;
                    return;
                }

                if (m_RequestTimeoutSeconds > 0f)
                {
                    float queueTimeoutSeconds = UseOpenAIRemote
                        ? Mathf.Max(m_RequestTimeoutSeconds, m_RemoteMinRequestTimeoutSeconds)
                        : m_RequestTimeoutSeconds;
                    float waitTime = Time.realtimeSinceStartup - state.EnqueuedAt;
                    if (waitTime > queueTimeoutSeconds)
                    {
                        if (
                            ShouldRetryTimeoutFailures()
                            && TryScheduleRetry(requestId, state, "timeout_in_queue")
                        )
                        {
                            return;
                        }

                        state.Status = DialogueStatus.Failed;
                        state.Error = "Dialogue request timed out in queue.";
                        TrackTimeout();
                        NGLog.Warn(
                            "Dialogue",
                            $"Request timed out in queue | id={requestId} | wait={waitTime}"
                        );
                        terminal = true;
                        return;
                    }
                }

                BeginConversationRequest(requestId, state.Request);
                state.Status = DialogueStatus.InProgress;
                state.StartedAt = Time.realtimeSinceStartup;
                AddLatencySample(
                    m_QueueWaitSamplesMs,
                    Mathf.Max(0f, (state.StartedAt - state.EnqueuedAt) * 1000f)
                );

                string key = ResolveConversationKey(
                    state.Request.SpeakerNetworkId,
                    state.Request.ListenerNetworkId,
                    state.Request.RequestingClientId,
                    state.Request.ConversationKey
                );
                state.Request.ConversationKey = key;
                activeConversationKey = key;
                m_ActiveConversationKeys.Add(key);

                NGLog.Info(
                    "Dialogue",
                    $"Worker started request | id={requestId} | key={key} | queueLatency={Mathf.Max(0f, state.StartedAt - state.EnqueuedAt)} | enqueuedAt={state.EnqueuedAt} | startedAt={state.StartedAt}"
                );

                string result = null;
                bool useOpenAI = UseOpenAIRemote;
                IDialogueInferenceClient inferenceClient = ResolveInferenceClient(useOpenAI);
                if (inferenceClient == null)
                {
                    state.Status = DialogueStatus.Failed;
                    state.Error = useOpenAI
                        ? "Remote inference client unavailable."
                        : "Local inference client unavailable.";
                    terminal = true;
                    return;
                }
                List<ChatMessage> history = GetHistory(key);
                List<ChatMessage> requestHistory = useOpenAI
                    ? BuildRemoteHistorySlice(history)
                    : history;
                List<DialogueInferenceMessage> inferenceHistory = ConvertToInferenceHistory(
                    requestHistory
                );
                string promptForRequest = useOpenAI
                    ? ApplyRemoteUserPromptBudget(state.Request.Prompt ?? string.Empty)
                    : (state.Request.Prompt ?? string.Empty);
                ApplyPersonaForRequest(state.Request);
                CancellationTokenSource openAiTimeoutCts = null;
                try
                {
                    float effectiveRequestTimeoutSeconds = GetEffectiveRequestTimeoutSeconds(
                        useOpenAI,
                        m_LlmAgent.systemPrompt,
                        promptForRequest
                    );
                    state.EffectiveTimeoutSeconds = effectiveRequestTimeoutSeconds;

                    if (m_LogDebug)
                    {
                        UnityEngine.Debug.Log(
                            $"[Dialogue][DEBUG] Starting chat | id={requestId} | key={key} | target={inferenceClient.BackendName} | promptLen={promptForRequest.Length} | systemLen={m_LlmAgent.systemPrompt?.Length ?? 0} | historyCount={requestHistory?.Count ?? 0} | timeoutSec={effectiveRequestTimeoutSeconds}"
                        );
                    }

                    Task<string> chatTask;
                    openAiTimeoutCts =
                        useOpenAI && effectiveRequestTimeoutSeconds > 0f
                            ? new CancellationTokenSource(
                                TimeSpan.FromSeconds(effectiveRequestTimeoutSeconds)
                            )
                            : null;

                    if (useOpenAI)
                    {
                        chatTask = inferenceClient.ChatAsync(
                            m_LlmAgent.systemPrompt,
                            inferenceHistory,
                            promptForRequest,
                            addToHistory: false,
                            openAiTimeoutCts != null
                                ? openAiTimeoutCts.Token
                                : CancellationToken.None
                        );
                    }
                    else
                    {
                        chatTask = inferenceClient.ChatAsync(
                            m_LlmAgent.systemPrompt,
                            inferenceHistory,
                            promptForRequest,
                            addToHistory: true,
                            CancellationToken.None
                        );
                    }

                    if (!useOpenAI && effectiveRequestTimeoutSeconds > 0f)
                    {
                        var completedTask = await Task.WhenAny(
                            chatTask,
                            Task.Delay(TimeSpan.FromSeconds(effectiveRequestTimeoutSeconds))
                        );
                        if (completedTask != chatTask)
                        {
                            state.Status = DialogueStatus.Failed;
                            state.Error = "Dialogue request timed out.";
                            TrackTimeout();
                            if (!useOpenAI)
                                m_LlmAgent.CancelRequests();
                            if (
                                ShouldRetryTimeoutFailures()
                                && TryScheduleRetry(requestId, state, "timeout")
                            )
                            {
                                return;
                            }

                            state.Status = DialogueStatus.Failed;
                            state.Error = BuildRetryExhaustedError(
                                "retry_exhausted_timeout",
                                "Sorry, dialogue timed out. Please try again."
                            );
                            terminal = true;
                            return;
                        }
                    }

                    result = await chatTask;

                    // For stateless clients (e.g., OpenAI API), persist history here.
                    if (
                        !inferenceClient.ManagesHistoryInternally
                        && !string.IsNullOrWhiteSpace(result)
                    )
                    {
                        history.Add(new ChatMessage("user", promptForRequest));
                        history.Add(new ChatMessage("assistant", result));
                    }
                }
                catch (OperationCanceledException)
                    when (useOpenAI
                        && openAiTimeoutCts != null
                        && openAiTimeoutCts.IsCancellationRequested
                    )
                {
                    state.Status = DialogueStatus.Failed;
                    state.Error = "Dialogue request timed out.";
                    TrackTimeout();
                    if (
                        ShouldRetryTimeoutFailures()
                        && TryScheduleRetry(requestId, state, "timeout")
                    )
                    {
                        return;
                    }

                    state.Status = DialogueStatus.Failed;
                    state.Error = BuildRetryExhaustedError(
                        "retry_exhausted_timeout",
                        "Sorry, dialogue timed out. Please try again."
                    );
                    terminal = true;
                    return;
                }
                catch (Exception ex)
                {
                    bool transientException = IsTransientException(ex);
                    if (
                        transientException
                        && TryScheduleRetry(requestId, state, "transient_exception")
                    )
                    {
                        return;
                    }

                    state.Status = DialogueStatus.Failed;
                    state.Error = transientException
                        ? BuildRetryExhaustedError(
                            "retry_exhausted_transient_exception",
                            "Sorry, dialogue is temporarily unavailable. Please try again."
                        )
                        : $"chat_exception_non_transient: {ex.Message}";
                    UnityEngine.Debug.LogError(
                        $"[Dialogue][ERROR] Chat exception | id={requestId} | retry={state.RetryCount} | transient={transientException} | error={ex.Message}"
                    );
                    terminal = true;
                    return;
                }
                finally
                {
                    openAiTimeoutCts?.Dispose();
                }

                if (string.IsNullOrWhiteSpace(result))
                {
                    if (TryScheduleRetry(requestId, state, "empty_response"))
                    {
                        return;
                    }

                    state.Status = DialogueStatus.Failed;
                    state.Error = BuildRetryExhaustedError(
                        "retry_exhausted_empty_response",
                        "Sorry, no response was generated. Please try again."
                    );
                    NGLog.Warn(
                        "Dialogue",
                        $"Empty response | id={requestId} | retry={state.RetryCount}"
                    );
                    terminal = true;
                    return;
                }

                result = RewriteRefusalResponseForEffectCommands(state.Request, result);

                state.Status = DialogueStatus.Completed;
                state.ResponseText = result;
                // For local LLM: snapshot-clone the chat list so subsequent requests
                // on different conversation keys cannot mutate this stored history.
                List<ChatMessage> historyToStore = useOpenAI
                    ? history
                    : new List<ChatMessage>(m_LlmAgent.chat);
                StoreHistory(key, historyToStore);
                NGLog.Info(
                    "Dialogue",
                    $"Completed request | id={requestId} | responseLen={result.Length}"
                );

                terminal = true;

                // From this point onward, the dialogue response must still reach the UI
                // even if effect parsing/spawning fails. Keep FX work isolated.
                try
                {
                    ApplyContextEffects(state.Request, state.ResponseText);
                }
                catch (Exception ex)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "ApplyContextEffects failed; response will still be delivered",
                            ("id", requestId),
                            ("error", ex.Message ?? string.Empty)
                        )
                    );
                    Debug.LogException(ex);
                }

                try
                {
                    state.ResponseText = EffectParser.StripTags(state.ResponseText);
                }
                catch (Exception ex)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "Effect tag stripping failed; response will still be delivered",
                            ("id", requestId),
                            ("error", ex.Message ?? string.Empty)
                        )
                    );
                }

                if (state.Request.Broadcast)
                {
                    try
                    {
                        TryBroadcast(
                            state.Request.SpeakerNetworkId,
                            state.ResponseText,
                            state.Request.BroadcastDuration
                        );
                    }
                    catch (Exception ex)
                    {
                        NGLog.Warn(
                            "DialogueFX",
                            NGLog.Format(
                                "Broadcast failed; response already completed",
                                ("id", requestId),
                                ("error", ex.Message ?? string.Empty)
                            )
                        );
                    }
                }
            }
            finally
            {
                m_ActiveRequestIds.Remove(requestId);

                if (!string.IsNullOrWhiteSpace(activeConversationKey))
                {
                    m_ActiveConversationKeys.Remove(activeConversationKey);
                }

                if (terminal)
                {
                    FinalizeTerminalRequest(
                        requestId,
                        state,
                        state.Status == DialogueStatus.Completed
                    );
                    TrackTerminalStatus(state);
                    if (state.StartedAt > 0f)
                    {
                        AddLatencySample(
                            m_ModelExecutionSamplesMs,
                            (Time.realtimeSinceStartup - state.StartedAt) * 1000f
                        );
                    }
                    PublishDialogueTelemetry(requestId, state);
                    NotifyIfRequested(requestId, state);
                }

                TryLogPeriodicSummary();

                if (m_RequestQueue.Count > 0 && !m_IsProcessing)
                {
                    _ = ProcessQueue();
                }
            }
        }

        private bool TryScheduleRetry(int requestId, DialogueRequestState state, string reason)
        {
            if (state == null)
            {
                return false;
            }

            if (
                state.Status == DialogueStatus.Completed
                || state.Status == DialogueStatus.Cancelled
            )
            {
                return false;
            }

            if (state.RetryCount >= Mathf.Max(0, m_MaxRetries))
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "Retry budget exhausted",
                        ("id", requestId),
                        ("retry", state.RetryCount),
                        ("maxRetries", m_MaxRetries),
                        ("reason", reason)
                    )
                );
                return false;
            }

            state.RetryCount++;
            state.Status = DialogueStatus.Pending;
            state.ResponseText = null;
            float jitter =
                m_RetryJitterSeconds > 0f ? UnityEngine.Random.Range(0f, m_RetryJitterSeconds) : 0f;
            float delay = Mathf.Max(0f, m_RetryBackoffSeconds) * state.RetryCount + jitter;
            state.NextAttemptAt = Time.realtimeSinceStartup + delay;
            state.EnqueuedAt = state.NextAttemptAt;
            state.Error = null;
            m_RequestQueue.Enqueue(requestId);

            NGLog.Warn(
                "Dialogue",
                NGLog.Format(
                    "Retry scheduled",
                    ("id", requestId),
                    ("retry", state.RetryCount),
                    ("maxRetries", m_MaxRetries),
                    ("delaySeconds", delay),
                    ("reason", reason)
                )
            );
            return true;
        }

        private bool ShouldRetryTimeoutFailures()
        {
            // Local inference timeouts are usually deterministic throughput limits.
            // Retrying them extends in-flight stalls and blocks new requests.
            return m_LlmAgent != null && m_LlmAgent.remote;
        }

        private string BuildRetryExhaustedError(string code, string friendlyMessage)
        {
            return $"{code}: {friendlyMessage}";
        }

        private string RewriteRefusalResponseForEffectCommands(
            DialogueRequest request,
            string responseText
        )
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return responseText;
            }

            if (!LooksLikeModelRefusal(responseText))
            {
                return responseText;
            }

            string prompt = request.Prompt ?? string.Empty;
            PlayerSpecialEffectMode specialMode = ResolvePlayerSpecialEffectMode(
                promptText: prompt,
                responseText: string.Empty,
                intents: null
            );
            if (specialMode == PlayerSpecialEffectMode.None)
            {
                return responseText;
            }

            string rewritten =
                specialMode == PlayerSpecialEffectMode.Dissolve
                    ? "As you wish. You fade from sight."
                    : "As you wish. You return to view.";

            NGLog.Warn(
                "DialogueFX",
                NGLog.Format(
                    "Rewrote model refusal for effect command",
                    ("mode", specialMode.ToString()),
                    ("prompt", prompt),
                    ("original", responseText)
                )
            );

            return rewritten;
        }

        private static bool LooksLikeModelRefusal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string lower = text.ToLowerInvariant();
            if (lower.Contains("can't assist") || lower.Contains("cannot assist"))
            {
                return true;
            }

            if (lower.Contains("can't help") || lower.Contains("cannot help"))
            {
                return true;
            }

            return lower.Contains("i'm sorry")
                && (
                    lower.Contains("can't") || lower.Contains("cannot") || lower.Contains("unable")
                );
        }

        private void FinalizeTerminalRequest(
            int requestId,
            DialogueRequestState requestState,
            bool completed
        )
        {
            if (requestState == null || requestState.CompletionIssued)
            {
                return;
            }

            requestState.CompletionIssued = true;
            CompleteConversationRequest(requestId, requestState, completed);
        }

        private bool IsTransientException(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            if (
                ex is TimeoutException
                || ex is TaskCanceledException
                || ex is OperationCanceledException
            )
            {
                return true;
            }

            string message = ex.Message ?? string.Empty;
            return message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("temporar", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("503", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TrackTerminalStatus(DialogueRequestState state)
        {
            m_TotalRequestsFinished++;
            switch (state.Status)
            {
                case DialogueStatus.Completed:
                    m_TotalTerminalCompleted++;
                    break;
                case DialogueStatus.Cancelled:
                    m_TotalTerminalCancelled++;
                    break;
                case DialogueStatus.Failed:
                    m_TotalTerminalFailed++;
                    if (IsRejectedReason(state.Error))
                    {
                        TrackRejected(state.Error);
                    }
                    break;
            }
        }

        private void TrackRejected(string rejectionReason)
        {
            string reason = string.IsNullOrWhiteSpace(rejectionReason)
                ? "request_rejected"
                : rejectionReason.Trim();
            m_TotalTerminalRejected++;
            m_RejectionReasonOrder.Enqueue(reason);
            if (m_RejectionReasonCounts.TryGetValue(reason, out int current))
            {
                m_RejectionReasonCounts[reason] = current + 1;
            }
            else
            {
                m_RejectionReasonCounts[reason] = 1;
            }

            while (m_RejectionReasonOrder.Count > m_RejectionReasonWindow)
            {
                string oldest = m_RejectionReasonOrder.Dequeue();
                if (!m_RejectionReasonCounts.TryGetValue(oldest, out int count))
                {
                    continue;
                }

                count--;
                if (count <= 0)
                {
                    m_RejectionReasonCounts.Remove(oldest);
                }
                else
                {
                    m_RejectionReasonCounts[oldest] = count;
                }
            }
        }

        private static bool IsRejectedReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            return reason.IndexOf("request_rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("queue_full", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("conversation_in_flight", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("awaiting_user_message", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("duplicate_prompt", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("repeat_delay", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("rate_limited", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("invalid_", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("participants_missing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TrackTimeout()
        {
            m_TimeoutCount++;
        }

        private void AddLatencySample(Queue<float> samples, float valueMs)
        {
            samples.Enqueue(Mathf.Max(0f, valueMs));
            while (samples.Count > m_LatencySampleWindow)
            {
                samples.Dequeue();
            }
        }

        private static LatencyHistogram BuildLatencyHistogram(IEnumerable<float> samples)
        {
            var ordered = new List<float>();
            float total = 0f;
            float min = float.MaxValue;
            float max = 0f;
            int b100 = 0;
            int b250 = 0;
            int b500 = 0;
            int b1000 = 0;
            int b2000 = 0;
            int over = 0;

            foreach (float sample in samples)
            {
                ordered.Add(sample);
                total += sample;
                min = Math.Min(min, sample);
                max = Math.Max(max, sample);
                if (sample < 100f)
                {
                    b100++;
                }
                else if (sample < 250f)
                {
                    b250++;
                }
                else if (sample < 500f)
                {
                    b500++;
                }
                else if (sample < 1000f)
                {
                    b1000++;
                }
                else if (sample < 2000f)
                {
                    b2000++;
                }
                else
                {
                    over++;
                }
            }

            if (ordered.Count == 0)
            {
                return default;
            }

            ordered.Sort();
            return new LatencyHistogram
            {
                SampleCount = ordered.Count,
                TotalMs = total,
                MinMs = min,
                MaxMs = max,
                P50Ms = Percentile(ordered, 0.5f),
                P95Ms = Percentile(ordered, 0.95f),
                Under100Ms = b100,
                Under250Ms = b250,
                Under500Ms = b500,
                Under1000Ms = b1000,
                Under2000Ms = b2000,
                Over2000Ms = over,
            };
        }

        private static float Percentile(List<float> orderedSamples, float percentile)
        {
            if (orderedSamples == null || orderedSamples.Count == 0)
            {
                return 0f;
            }

            float clamped = Mathf.Clamp01(percentile);
            int index = Mathf.Clamp(
                Mathf.CeilToInt(clamped * orderedSamples.Count) - 1,
                0,
                orderedSamples.Count - 1
            );
            return orderedSamples[index];
        }

        private KeyValuePair<string, int>[] BuildRejectionCountsSnapshot()
        {
            if (m_RejectionReasonCounts.Count == 0)
            {
                return Array.Empty<KeyValuePair<string, int>>();
            }

            var snapshot = new List<KeyValuePair<string, int>>(m_RejectionReasonCounts);
            snapshot.Sort((left, right) => right.Value.CompareTo(left.Value));
            return snapshot.ToArray();
        }

        private void TryLogPeriodicSummary()
        {
            if (m_SummaryLogIntervalSeconds <= 0f)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (m_LastSummaryLogAt > 0f && now - m_LastSummaryLogAt < m_SummaryLogIntervalSeconds)
            {
                return;
            }

            m_LastSummaryLogAt = now;
            DialogueStats stats = GetStats();
            string topReason = "none";
            int topReasonCount = 0;
            if (stats.RejectionReasonCounts != null && stats.RejectionReasonCounts.Length > 0)
            {
                topReason = stats.RejectionReasonCounts[0].Key;
                topReasonCount = stats.RejectionReasonCounts[0].Value;
            }

            NGLog.Info(
                "Dialogue",
                NGLog.Format(
                    "Summary",
                    ("enqueued", stats.TotalRequestsEnqueued),
                    ("finished", stats.TotalRequestsFinished),
                    ("completed", stats.TotalTerminalCompleted),
                    ("failed", stats.TotalTerminalFailed),
                    ("cancelled", stats.TotalTerminalCancelled),
                    ("rejected", stats.TotalTerminalRejected),
                    ("timeouts", stats.TimeoutCount),
                    ("successRate", stats.SuccessRate.ToString("P1")),
                    ("queueP95Ms", stats.QueueWaitHistogram.P95Ms.ToString("F0")),
                    ("modelP95Ms", stats.ModelExecutionHistogram.P95Ms.ToString("F0")),
                    ("topRejection", topReason),
                    ("topRejectionCount", topReasonCount)
                )
            );
        }

        private void EnsureLlmAgentReady()
        {
            if (m_LlmAgent == null || m_LlmAgent.remote)
            {
                return;
            }

            EnsureLlmAgentEnabled("EnsureLlmAgentReady");

            if (m_LlmAgent.llm == null || !m_LlmAgent.llm.started || m_LlmAgent.llm.failed)
            {
#if UNITY_2023_1_OR_NEWER
                var llm = FindAnyObjectByType<LLM>();
#else
                var llm = FindAnyObjectByType<LLM>();
#endif
                if (llm != null)
                {
                    m_LlmAgent.llm = llm;
                    NGLog.Info("Dialogue", "LLM assigned to LLMAgent.");
                    EnsureLlmAgentEnabled("EnsureLlmAgentReady.AssignLlm");
                }
                else if (m_LlmAgent.llm != null && !m_LlmAgent.llm.gameObject.scene.IsValid())
                {
                    // If the agent references an LLM prefab asset (scene not valid), instantiate it so there is a runtime LLM.
                    try
                    {
                        GameObject instance = Instantiate(m_LlmAgent.llm.gameObject);
                        instance.name = "LLM";
                        LLM instanceLlm = instance.GetComponent<LLM>();
                        if (instanceLlm != null)
                        {
                            m_LlmAgent.llm = instanceLlm;
                            NGLog.Info("Dialogue", "Instantiated LLM prefab for runtime.");
                            EnsureLlmAgentEnabled("EnsureLlmAgentReady.InstantiatePrefab");
                        }
                    }
                    catch (Exception ex)
                    {
                        NGLog.Warn(
                            "Dialogue",
                            NGLog.Format("Failed to instantiate LLM prefab", ("error", ex.Message))
                        );
                    }
                }
                else if (m_LogDebug)
                {
                    NGLog.Warn("Dialogue", "LLM not found in scene.");
                }
            }
        }

        private void EnsureLlmAgentEnabled(string context)
        {
            if (m_LlmAgent == null || m_LlmAgent.remote || m_LlmAgent.enabled)
            {
                return;
            }

            m_LlmAgent.enabled = true;
            if (m_LogDebug)
            {
                string agentName =
                    m_LlmAgent.gameObject != null ? m_LlmAgent.gameObject.name : "unknown";
                NGLog.Info(
                    "Dialogue",
                    NGLog.Format(
                        "Enabled LLMAgent component",
                        ("context", context ?? string.Empty),
                        ("agent", agentName)
                    )
                );
            }
        }

        private bool TryValidateLocalModelReady(out string failureReason)
        {
            failureReason = string.Empty;

            if (m_LlmAgent == null || m_LlmAgent.remote)
            {
                return true;
            }

            if (m_LlmAgent.llm == null)
            {
                failureReason = "llm_component_missing";
                return false;
            }

            string model = m_LlmAgent.llm.model;
            if (string.IsNullOrWhiteSpace(model))
            {
                failureReason = "model_not_set";
                return false;
            }

            string resolved = LLM.GetLLMManagerAssetRuntime(model);
            string resolvedFullPath = LLMUnitySetup.GetFullPath(resolved);
            if (!File.Exists(resolvedFullPath))
            {
                string fileName = Path.GetFileName(model);
                failureReason = string.IsNullOrWhiteSpace(fileName)
                    ? "model_file_not_found"
                    : $"model_file_not_found:{fileName}";

                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "LLM model file not found",
                        ("model", model),
                        ("resolved", resolvedFullPath)
                    )
                );
                return false;
            }

            return true;
        }

        private async Task<bool> EnsureWarmup()
        {
            if (m_LlmAgent == null)
            {
                m_LastWarmupFailureReason = "LLMAgent not assigned.";
                return false;
            }

            EnsureLlmAgentReady();
            if (!TryValidateLocalModelReady(out string modelFailure))
            {
                m_LastWarmupFailureReason = modelFailure;
                return false;
            }

            if (m_WarmupTask != null && (m_WarmupTask.IsFaulted || m_WarmupTask.IsCanceled))
            {
                string staleReason = ExtractWarmupFailureReason(m_WarmupTask);
                m_WarmupTask = null;
                m_LastWarmupFailureReason = staleReason;
                if (m_LogDebug)
                {
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format("Cleared stale warmup task", ("reason", staleReason))
                    );
                }
            }

            float now = Time.realtimeSinceStartup;
            if (m_WarmupTask == null && m_NextWarmupRetryAt > now)
            {
                float retryIn = m_NextWarmupRetryAt - now;
                if (m_WarmupDegradedMode)
                {
                    m_LastWarmupFailureReason =
                        $"Warmup degraded mode active; retry in {retryIn:0.0}s.";
                }
                return false;
            }

            if (m_WarmupTask == null)
            {
                if (m_LogDebug)
                {
                    NGLog.Debug("Dialogue", "Starting LLM warmup.");
                }

                if (UseOpenAIRemote)
                {
                    IDialogueInferenceClient inferenceClient = ResolveInferenceClient(
                        useOpenAI: true
                    );
                    if (inferenceClient == null)
                    {
                        throw new Exception("Remote inference client unavailable.");
                    }
                    float remoteProbeTimeoutSeconds = Mathf.Clamp(
                        m_WarmupTimeoutSeconds > 0f ? m_WarmupTimeoutSeconds : 10f,
                        5f,
                        20f
                    );
                    m_WarmupTask = Task.Run(async () =>
                    {
                        using var warmupCts = new CancellationTokenSource(
                            TimeSpan.FromSeconds(remoteProbeTimeoutSeconds)
                        );
                        bool ok = await inferenceClient.CheckConnectionAsync(warmupCts.Token);
                        if (!ok)
                            throw new Exception(
                                $"OpenAI-compatible warmup probe failed at {m_LlmAgent.host}:{m_LlmAgent.port}"
                            );
                    });
                }
                else
                {
                    m_WarmupTask = m_LlmAgent.Warmup();
                }
            }

            Task activeTask = m_WarmupTask;

            float effectiveWarmupTimeoutSeconds = m_WarmupTimeoutSeconds;
            if (UseOpenAIRemote)
            {
                // Keep remote warmup bounded to avoid conversation stuck-recovery races.
                effectiveWarmupTimeoutSeconds =
                    effectiveWarmupTimeoutSeconds > 0f
                        ? Mathf.Min(effectiveWarmupTimeoutSeconds, 20f)
                        : 20f;
            }

            if (effectiveWarmupTimeoutSeconds > 0f)
            {
                Task completed = await Task.WhenAny(
                    activeTask,
                    Task.Delay(TimeSpan.FromSeconds(effectiveWarmupTimeoutSeconds))
                );
                if (completed != activeTask)
                {
                    return HandleWarmupFailure(
                        $"Warmup timed out after {effectiveWarmupTimeoutSeconds:0.0}s."
                    );
                }
            }

            try
            {
                await activeTask;
                m_WarmupTask = activeTask;
                m_WarmupConsecutiveFailures = 0;
                m_WarmupDegradedMode = false;
                m_NextWarmupRetryAt = 0f;
                m_LastWarmupFailureReason = string.Empty;
                return true;
            }
            catch (Exception)
            {
                return HandleWarmupFailure(ExtractWarmupFailureReason(activeTask));
            }
        }

        private bool HandleWarmupFailure(string reason)
        {
            m_WarmupTask = null;
            m_WarmupConsecutiveFailures++;
            m_LastWarmupFailureReason = string.IsNullOrWhiteSpace(reason)
                ? "Warmup failed with unknown reason."
                : reason;

            float cooldown = Mathf.Max(0f, m_WarmupRetryCooldownSeconds);
            m_NextWarmupRetryAt = Time.realtimeSinceStartup + cooldown;

            bool entersDegraded =
                m_WarmupConsecutiveFailures >= Mathf.Max(1, m_DegradedWarmupFailureThreshold);
            if (entersDegraded)
            {
                m_WarmupDegradedMode = true;
                NGLog.Error(
                    "Dialogue",
                    NGLog.Format(
                        "Warmup degraded mode enabled",
                        ("failures", m_WarmupConsecutiveFailures),
                        ("reason", m_LastWarmupFailureReason),
                        ("retryIn", cooldown)
                    )
                );
            }
            else
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "Warmup failed",
                        ("failures", m_WarmupConsecutiveFailures),
                        ("reason", m_LastWarmupFailureReason),
                        ("retryIn", cooldown)
                    )
                );
            }

            return false;
        }

        private static string ExtractWarmupFailureReason(Task task)
        {
            if (task == null)
            {
                return "Warmup task missing.";
            }

            if (task.IsCanceled)
            {
                return "Warmup task canceled.";
            }

            if (!task.IsFaulted)
            {
                return "Warmup task failed.";
            }

            Exception ex = task.Exception?.GetBaseException();
            return string.IsNullOrWhiteSpace(ex?.Message)
                ? "Warmup task faulted."
                : $"Warmup task faulted: {ex.Message}";
        }

        private void EnsureOpenAIChatClient()
        {
            if (m_OpenAIChatClient != null)
            {
                if (m_LlmAgent != null && m_LlmAgent.remote)
                {
                    SyncOpenAIChatClientParams();
                }
                return;
            }
            if (m_LlmAgent == null || !m_LlmAgent.remote)
                return;

            m_OpenAIChatClient = new OpenAIChatClient();
            SyncOpenAIChatClientParams();

            NGLog.Info(
                "Dialogue",
                NGLog.Format(
                    "OpenAI chat client initialized",
                    ("host", m_OpenAIChatClient.Host),
                    ("port", m_OpenAIChatClient.Port),
                    (
                        "model",
                        string.IsNullOrEmpty(ResolveConfiguredRemoteModelName())
                            ? "(auto)"
                            : ResolveConfiguredRemoteModelName()
                    )
                )
            );
        }

        private LlmAgentInferenceClient EnsureLlmAgentInferenceClient()
        {
            if (m_LlmAgentInferenceClient == null)
            {
                m_LlmAgentInferenceClient = new LlmAgentInferenceClient();
            }

            m_LlmAgentInferenceClient.SetAgent(m_LlmAgent);
            return m_LlmAgentInferenceClient;
        }

        /// <summary>
        /// Inject an ML-Agents SideChannel-backed client that takes precedence over
        /// the built-in LlmAgent and OpenAI backends. Called by NpcDialogueAgent.
        /// Pass null to restore normal backend selection.
        /// </summary>
        public void SetMLAgentsSideChannelClient(IDialogueInferenceClient client)
        {
            m_OverrideClient = client;
        }

        private IDialogueInferenceClient ResolveInferenceClient(bool useOpenAI)
        {
            if (m_OverrideClient != null)
                return m_OverrideClient;

            if (useOpenAI)
            {
                EnsureOpenAIChatClient();
                return m_OpenAIChatClient;
            }

            return EnsureLlmAgentInferenceClient();
        }

        private void SyncOpenAIChatClientParams()
        {
            if (m_OpenAIChatClient == null || m_LlmAgent == null)
                return;

            m_OpenAIChatClient.ApplyConfig(BuildInferenceRuntimeConfig());
        }

        private DialogueInferenceRuntimeConfig BuildInferenceRuntimeConfig()
        {
            var config = new DialogueInferenceRuntimeConfig
            {
                Host = string.IsNullOrWhiteSpace(m_LlmAgent.host)
                    ? "127.0.0.1"
                    : m_LlmAgent.host.Trim(),
                Port = m_LlmAgent.port,
                ApiKey = ResolveRemoteApiKey(),
                Model = ResolveConfiguredRemoteModelName(),
                Temperature = m_LlmAgent.temperature,
                MaxTokens = m_LlmAgent.numPredict,
                TopP = m_LlmAgent.topP,
                FrequencyPenalty = m_LlmAgent.frequencyPenalty,
                PresencePenalty = m_LlmAgent.presencePenalty,
                Seed = m_LlmAgent.seed,
                TopK = m_LlmAgent.topK,
                RepeatPenalty = m_LlmAgent.repeatPenalty,
                MinP = m_LlmAgent.minP,
                TypicalP = m_LlmAgent.typicalP,
                RepeatLastN = m_LlmAgent.repeatLastN,
                Mirostat = m_LlmAgent.mirostat,
                MirostatTau = m_LlmAgent.mirostatTau,
                MirostatEta = m_LlmAgent.mirostatEta,
                NProbs = m_LlmAgent.nProbs,
                IgnoreEos = m_LlmAgent.ignoreEos,
                CachePrompt = m_LlmAgent.cachePrompt,
                Grammar = string.IsNullOrWhiteSpace(m_LlmAgent.grammar) ? null : m_LlmAgent.grammar,
                StopSequences =
                    m_RemoteStopSequences != null && m_RemoteStopSequences.Length > 0
                        ? m_RemoteStopSequences
                        : null,
            };

            return config;
        }

        private string ResolveConfiguredRemoteModelName()
        {
            string configured = m_RemoteModelName == null ? string.Empty : m_RemoteModelName.Trim();
            if (string.IsNullOrWhiteSpace(configured))
            {
                return string.Empty;
            }

            if (
                string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(configured, "(auto)", StringComparison.OrdinalIgnoreCase)
            )
            {
                return string.Empty;
            }

            return configured;
        }

        private string ResolveRemoteApiKey()
        {
            string apiKey;

            // Prefer LM Studio's on-disk token first so runtime key rotation does not depend
            // on Unity process environment inheritance.
            if (TryGetApiKeyFromLmStudioFile(out apiKey))
            {
                return apiKey;
            }

            if (TryGetApiKeyFromEnvironment("LMSTUDIO_API_KEY", out apiKey))
            {
                return apiKey;
            }

            if (TryGetApiKeyFromEnvironment("OPENAI_API_KEY", out apiKey))
            {
                return apiKey;
            }

            return m_LlmAgent != null ? (m_LlmAgent.APIKey ?? string.Empty).Trim() : string.Empty;
        }

        private static bool TryGetApiKeyFromEnvironment(string varName, out string apiKey)
        {
            apiKey = Environment.GetEnvironmentVariable(varName);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = string.Empty;
                return false;
            }

            apiKey = apiKey.Trim();
            return true;
        }

        private static bool TryGetApiKeyFromLmStudioFile(out string apiKey)
        {
            apiKey = string.Empty;
            try
            {
                string profilePath = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );
                if (string.IsNullOrWhiteSpace(profilePath))
                {
                    return false;
                }

                string keyPath = Path.Combine(profilePath, ".lmstudio", "lms-key");
                if (!File.Exists(keyPath))
                {
                    return false;
                }

                string keyFromFile = File.ReadAllText(keyPath);
                if (string.IsNullOrWhiteSpace(keyFromFile))
                {
                    return false;
                }

                apiKey = keyFromFile.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool UseOpenAIRemote => m_LlmAgent != null && m_LlmAgent.remote;

        private string BuildWarmupStateLabel()
        {
            if (m_LlmAgent == null)
            {
                return "MissingAgent";
            }

            if (m_WarmupTask != null && !m_WarmupTask.IsCompleted)
            {
                return "InProgress";
            }

            if (m_WarmupDegradedMode)
            {
                return "Degraded";
            }

            if (m_WarmupConsecutiveFailures > 0)
            {
                return "RetryCooldown";
            }

            if (
                m_WarmupTask != null
                && m_WarmupTask.IsCompleted
                && !m_WarmupTask.IsFaulted
                && !m_WarmupTask.IsCanceled
            )
            {
                return "Ready";
            }

            return "Idle";
        }

        private List<ChatMessage> GetHistory(string key)
        {
            if (!m_Histories.TryGetValue(key, out List<ChatMessage> history))
            {
                history = new List<ChatMessage>();
                m_Histories[key] = history;
            }

            return history;
        }

        private List<ChatMessage> BuildRemoteHistorySlice(List<ChatMessage> fullHistory)
        {
            if (fullHistory == null || fullHistory.Count == 0)
            {
                return new List<ChatMessage>();
            }

            int maxMessages = Mathf.Max(0, m_RemoteMaxHistoryMessages);
            int hardCap = Mathf.Max(1, m_RemoteHistoryHardCapMessages);
            maxMessages = Mathf.Min(maxMessages, hardCap);
            if (maxMessages <= 0)
            {
                return new List<ChatMessage>();
            }

            int takeCount = Mathf.Min(maxMessages, fullHistory.Count);
            int startIndex = fullHistory.Count - takeCount;
            int messageCharBudget = Mathf.Clamp(m_RemoteHistoryMessageCharBudget, 64, 1024);
            var slice = new List<ChatMessage>(takeCount);
            for (int i = startIndex; i < fullHistory.Count; i++)
            {
                ChatMessage message = fullHistory[i];
                string content = TrimPromptSegment(message.content, messageCharBudget);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                slice.Add(new ChatMessage(NormalizeHistoryRole(message.role), content));
            }

            return slice;
        }

        private static List<DialogueInferenceMessage> ConvertToInferenceHistory(
            List<ChatMessage> history
        )
        {
            if (history == null || history.Count == 0)
            {
                return new List<DialogueInferenceMessage>();
            }

            var converted = new List<DialogueInferenceMessage>(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                ChatMessage message = history[i];
                if (message == null)
                {
                    continue;
                }

                converted.Add(
                    new DialogueInferenceMessage(
                        NormalizeHistoryRole(message.role),
                        message.content ?? string.Empty
                    )
                );
            }

            return converted;
        }

        private static string NormalizeHistoryRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return "user";
            }

            string normalized = role.Trim().ToLowerInvariant();
            if (normalized == "user" || normalized == "assistant" || normalized == "system")
            {
                return normalized;
            }

            return normalized.Contains("assistant", StringComparison.Ordinal)
                ? "assistant"
                : "user";
        }

        private static string TrimPromptSegment(string content, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            string normalized = content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (normalized.Length <= maxChars)
            {
                return normalized;
            }

            maxChars = Mathf.Max(64, maxChars);
            int head = Mathf.Clamp(Mathf.FloorToInt(maxChars * 0.72f), 48, maxChars - 24);
            int tail = maxChars - head - 12;
            if (tail < 12)
            {
                tail = 12;
                head = maxChars - tail - 12;
            }

            return normalized.Substring(0, head).TrimEnd()
                + " [..] "
                + normalized.Substring(normalized.Length - tail).TrimStart();
        }

        private string ApplyRemoteUserPromptBudget(string prompt)
        {
            if (!UseOpenAIRemote || string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            int budget = Mathf.Clamp(m_RemoteUserPromptCharBudget, 64, 1024);
            string trimmed = TrimPromptSegment(prompt, budget);
            if (m_LogDebug && trimmed.Length < prompt.Length)
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "Trimmed remote user prompt",
                        ("fromChars", prompt.Length),
                        ("toChars", trimmed.Length),
                        ("budget", budget)
                    )
                );
            }

            return trimmed;
        }

        private void StoreHistory(string key, List<ChatMessage> history)
        {
            if (history == null)
            {
                return;
            }

            if (m_MaxHistoryMessages > 0 && history.Count > m_MaxHistoryMessages)
            {
                int removeCount = history.Count - m_MaxHistoryMessages;
                history.RemoveRange(0, removeCount);
            }

            m_Histories[key] = history;

            if (m_LogDebug)
            {
                NGLog.Debug(
                    "Dialogue",
                    NGLog.Format(
                        "Stored conversation history",
                        ("key", key),
                        ("messageCount", history.Count)
                    )
                );
            }
        }

        private string BuildConversationKey(DialogueRequest request)
        {
            return ResolveConversationKey(
                request.SpeakerNetworkId,
                request.ListenerNetworkId,
                request.RequestingClientId,
                request.ConversationKey
            );
        }

        private ConversationState GetConversationState(string conversationKey)
        {
            string key = ResolveConversationKey(0, 0, 0, conversationKey);
            if (!m_ConversationStates.TryGetValue(key, out ConversationState state))
            {
                state = new ConversationState();
                m_ConversationStates[key] = state;
            }

            return state;
        }

        private void BeginConversationRequest(int requestId, DialogueRequest request)
        {
            ConversationState state = GetConversationState(request.ConversationKey);
            state.IsInFlight = true;
            state.ActiveRequestId = requestId;
            if (request.IsUserInitiated)
            {
                state.AwaitingUserInput = false;
            }
        }

        private void CompleteConversationRequest(
            int requestId,
            DialogueRequestState requestState,
            bool completed
        )
        {
            if (requestState == null)
            {
                return;
            }

            string key = BuildConversationKey(requestState.Request);
            ConversationState state = GetConversationState(key);
            if (state.ActiveRequestId == requestId || state.IsInFlight)
            {
                state.IsInFlight = false;
                state.ActiveRequestId = -1;
            }

            if (!completed)
            {
                return;
            }

            state.LastCompletedPrompt = requestState.Request.Prompt;
            state.LastCompletedAt = Time.realtimeSinceStartup;
            state.AssistantMessageCount++;
            if (requestState.Request.RequireUserReply)
            {
                state.AwaitingUserInput = true;
            }
        }

        private bool CanAcceptRequest(DialogueRequest request, out string reason)
        {
            reason = null;
            ConversationState conversationState = GetConversationState(request.ConversationKey);
            if (request.IsUserInitiated)
            {
                conversationState.AwaitingUserInput = false;
            }

            if (conversationState.IsInFlight)
            {
                reason = "conversation_in_flight";
                return false;
            }

            if (request.RequireUserReply && conversationState.AwaitingUserInput)
            {
                reason = "awaiting_user_message";
                return false;
            }

            if (
                request.BlockRepeatedPrompt
                && !string.IsNullOrWhiteSpace(request.Prompt)
                && !string.IsNullOrWhiteSpace(conversationState.LastCompletedPrompt)
                && string.Equals(
                    conversationState.LastCompletedPrompt,
                    request.Prompt,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                reason = "duplicate_prompt";
                return false;
            }

            if (
                request.MinRepeatDelaySeconds > 0f
                && conversationState.LastCompletedAt > float.MinValue
            )
            {
                float elapsedSinceLast =
                    Time.realtimeSinceStartup - conversationState.LastCompletedAt;
                if (elapsedSinceLast < request.MinRepeatDelaySeconds)
                {
                    reason = "repeat_delay";
                    return false;
                }
            }

            if (request.RequestingClientId == 0)
            {
                return true;
            }

            if (m_MaxRequestsPerClient > 0)
            {
                int active = 0;
                foreach (var entry in m_Requests.Values)
                {
                    if (entry.Request.RequestingClientId != request.RequestingClientId)
                    {
                        continue;
                    }

                    if (
                        entry.Status == DialogueStatus.Pending
                        || entry.Status == DialogueStatus.InProgress
                    )
                    {
                        active++;
                    }
                }

                if (active >= m_MaxRequestsPerClient)
                {
                    reason = "Too many active requests for client.";
                    return false;
                }
            }

            if (m_MinSecondsBetweenRequests > 0f)
            {
                if (
                    m_LastRequestTimeByClient.TryGetValue(
                        request.RequestingClientId,
                        out float lastTime
                    )
                )
                {
                    float elapsed = Time.realtimeSinceStartup - lastTime;
                    if (elapsed < m_MinSecondsBetweenRequests)
                    {
                        reason = "Request rate limited.";
                        return false;
                    }
                }
                m_LastRequestTimeByClient[request.RequestingClientId] = Time.realtimeSinceStartup;
            }

            return true;
        }

        private void NotifyIfRequested(int requestId, DialogueRequestState state)
        {
            if (!state.Request.NotifyClient)
            {
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "Dialogue",
                        NGLog.Format("Notify skipped (flag)", ("id", requestId))
                    );
                }
                return;
            }

            if (!IsServer)
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format("Notify skipped (not server)", ("id", requestId))
                );
                return;
            }

            if (state.Request.RequestingClientId == 0 && !IsHost)
            {
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "Dialogue",
                        NGLog.Format("Notify skipped (host only)", ("id", requestId))
                    );
                }
                return;
            }

            if (m_LogDebug)
            {
                NGLog.Debug(
                    "Dialogue",
                    NGLog.Format(
                        "Notify client",
                        ("id", requestId),
                        ("client", state.Request.RequestingClientId),
                        ("status", state.Status),
                        ("key", state.Request.ConversationKey ?? string.Empty),
                        ("speaker", state.Request.SpeakerNetworkId),
                        ("listener", state.Request.ListenerNetworkId)
                    )
                );
            }
            DialogueResponseClientRpc(
                requestId,
                state.Request.ClientRequestId,
                state.Status,
                state.ResponseText ?? string.Empty,
                state.Error ?? string.Empty,
                state.Request.ConversationKey ?? string.Empty,
                state.Request.SpeakerNetworkId,
                state.Request.ListenerNetworkId,
                state.Request.RequestingClientId,
                state.Request.IsUserInitiated,
                RpcTarget.Single(state.Request.RequestingClientId, RpcTargetUse.Temp)
            );
        }

        private void PublishDialogueTelemetry(int requestId, DialogueRequestState state)
        {
            if (state == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            float queueLatencyMs = 0f;
            float modelLatencyMs = 0f;
            float totalLatencyMs = 0f;

            if (state.StartedAt > 0f)
            {
                queueLatencyMs = Mathf.Max(0f, (state.StartedAt - state.EnqueuedAt) * 1000f);
                modelLatencyMs = Mathf.Max(0f, (now - state.StartedAt) * 1000f);
            }

            if (state.FirstAttemptAt != float.MinValue)
            {
                totalLatencyMs = Mathf.Max(0f, (now - state.FirstAttemptAt) * 1000f);
            }
            else if (state.EnqueuedAt > 0f)
            {
                totalLatencyMs = Mathf.Max(0f, (now - state.EnqueuedAt) * 1000f);
            }

            OnDialogueResponseTelemetry?.Invoke(
                new DialogueResponseTelemetry
                {
                    RequestId = requestId,
                    Status = state.Status,
                    Error = state.Error,
                    Request = state.Request,
                    RetryCount = Mathf.Max(0, state.RetryCount),
                    QueueLatencyMs = queueLatencyMs,
                    ModelLatencyMs = modelLatencyMs,
                    TotalLatencyMs = totalLatencyMs,
                }
            );
        }

        private void TryBroadcast(ulong speakerNetworkId, string text, float duration)
        {
            if (speakerNetworkId == 0 || string.IsNullOrWhiteSpace(text))
            {
                if (m_LogDebug)
                {
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format("Broadcast skipped", ("speaker", speakerNetworkId))
                    );
                }
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.SpawnManager == null)
            {
                if (m_LogDebug)
                {
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Broadcast skipped (network subsystem unavailable)",
                            ("speaker", speakerNetworkId)
                        )
                    );
                }
                return;
            }

            if (
                !manager.SpawnManager.SpawnedObjects.TryGetValue(
                    speakerNetworkId,
                    out NetworkObject networkObject
                )
            )
            {
                if (m_LogDebug)
                {
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Broadcast skipped (speaker missing)",
                            ("speaker", speakerNetworkId)
                        )
                    );
                }
                return;
            }

            float displayDuration = duration <= 0f ? 2f : duration;
            string preview = BuildBroadcastPreviewText(text);

            NpcDialogueActor actor = networkObject.GetComponent<NpcDialogueActor>();
            if (actor != null)
            {
                actor.ShowSpeechText(preview, displayDuration);
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "Dialogue",
                        NGLog.Format(
                            "Broadcasted response",
                            ("speaker", speakerNetworkId),
                            ("chars", preview.Length),
                            ("duration", displayDuration),
                            ("mode", "NpcDialogueActor")
                        )
                    );
                }
                return;
            }

            NGLog.Warn(
                "Dialogue",
                NGLog.Format(
                    "Broadcast skipped (NpcDialogueActor missing)",
                    ("speaker", speakerNetworkId),
                    ("name", networkObject.name)
                )
            );
        }

        private string BuildBroadcastPreviewText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string preview = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (m_BroadcastSingleLinePreview)
            {
                preview = preview.Replace('\n', ' ');
                while (preview.Contains("  ", StringComparison.Ordinal))
                {
                    preview = preview.Replace("  ", " ", StringComparison.Ordinal);
                }
            }

            int maxChars = Math.Max(40, m_BroadcastMaxCharacters);
            if (preview.Length > maxChars)
            {
                preview = preview.Substring(0, maxChars).TrimEnd() + "...";
            }

            return preview;
        }

        private void ApplyPersonaForRequest(DialogueRequest request)
        {
            if (m_LlmAgent == null)
            {
                return;
            }

            string basePrompt = string.IsNullOrWhiteSpace(m_DefaultSystemPromptOverride)
                ? m_DefaultSystemPrompt
                : m_DefaultSystemPromptOverride;
            if (basePrompt == null)
            {
                basePrompt = string.Empty;
            }

            if (!m_EnablePersonaRouting)
            {
                m_LlmAgent.systemPrompt = ApplyRemoteSystemPromptBudget(basePrompt);
                return;
            }

            string prompt = basePrompt;
            NpcDialogueActor actor = ResolveDialogueActorForRequest(
                request,
                out ulong resolvedSpeakerNetworkId,
                out ulong resolvedListenerNetworkId,
                out _
            );
            GameObject listenerObject = ResolveSpawnedObject(resolvedListenerNetworkId);
            if (actor != null && actor.Profile != null)
            {
                prompt = actor.BuildSystemPrompt(basePrompt, request.Prompt, listenerObject);
            }

            string playerContextPrompt = BuildPlayerContextPrompt(request, listenerObject);
            if (!string.IsNullOrWhiteSpace(playerContextPrompt))
            {
                prompt = string.IsNullOrWhiteSpace(prompt)
                    ? playerContextPrompt
                    : $"{prompt}\n\n{playerContextPrompt}";
            }

            prompt = ApplyRemoteSystemPromptBudget(prompt);
            m_LlmAgent.systemPrompt = prompt;
        }

        private float GetEffectiveRequestTimeoutSeconds(
            bool useOpenAI,
            string systemPrompt,
            string userPrompt
        )
        {
            if (!useOpenAI)
            {
                return m_RequestTimeoutSeconds;
            }

            float minRemote = Mathf.Max(120f, m_RemoteMinRequestTimeoutSeconds);
            float configured = Mathf.Max(m_RequestTimeoutSeconds, minRemote);
            int promptChars = (systemPrompt?.Length ?? 0) + (userPrompt?.Length ?? 0);
            float heuristic = 75f + (promptChars * 0.06f);
            float resolved = Mathf.Max(configured, heuristic);
            return Mathf.Clamp(resolved, minRemote, 420f);
        }

        private string ApplyRemoteSystemPromptBudget(string prompt)
        {
            if (!UseOpenAIRemote || string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            int hardCap = Mathf.Clamp(m_RemoteSystemPromptHardCapChars, 512, 16000);
            int budget = Mathf.Clamp(m_RemoteSystemPromptCharBudget, 512, hardCap);
            if (prompt.Length <= budget)
            {
                return prompt;
            }

            // Priority: Always preserve effect format instructions at the end
            // Look for common effect format section markers
            string[] effectSectionMarkers = new[]
            {
                "EFFECT FORMAT",
                "EFFECT TAGS",
                "When using powers",
                "include an effect tag",
                "include effect tags",
                "[effect:",
                "effect tag at the END",
            };

            // Find the last effect section marker to preserve
            int effectSectionStart = -1;
            foreach (var marker in effectSectionMarkers)
            {
                int idx = prompt.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx > effectSectionStart)
                {
                    effectSectionStart = idx;
                }
            }

            // If we found an effect section, prioritize preserving it even if it is short.
            // Short effect sections are common in profile prompts and were previously
            // skipped by the min-tail heuristic, causing the generic trim path to cut
            // the exact [EFFECT:] formatting instructions.
            if (effectSectionStart > 0)
            {
                int effectSectionLength = prompt.Length - effectSectionStart;
                const string trimMarker = "\n[...trimmed for latency...]\n";
                int trimMarkerLength = trimMarker.Length;

                // Keep as much head context as possible while always preserving the
                // effect section tail. If the tail alone is too large, keep the tail
                // ending and guarantee at least one marker occurrence.
                int maxTailBudget = Mathf.Max(128, budget - trimMarkerLength - 64);
                int preservedTailLength = Mathf.Min(effectSectionLength, maxTailBudget);
                int preservedTailStart = prompt.Length - preservedTailLength;
                if (preservedTailStart > effectSectionStart)
                {
                    // If we had to trim into the tail, keep the marker line at the
                    // start of the preserved tail for easier debugging.
                    preservedTailStart = Mathf.Max(effectSectionStart, preservedTailStart);
                }

                int head = budget - preservedTailLength - trimMarkerLength;
                string tail = prompt.Substring(preservedTailStart).TrimStart();
                string trimmed;
                if (head >= 64)
                {
                    trimmed = prompt.Substring(0, head).TrimEnd() + trimMarker + tail;
                }
                else
                {
                    // Extremely tight budgets: keep marker + tail only.
                    trimmed = trimMarker + tail;
                }

                if (m_LogDebug)
                {
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Trimmed remote system prompt (preserved effect section)",
                            ("fromChars", prompt.Length),
                            ("toChars", trimmed.Length),
                            ("budget", budget),
                            ("effectSectionStart", effectSectionStart),
                            ("preservedTailLength", preservedTailLength)
                        )
                    );
                }
                return trimmed;
            }

            // Fallback: standard head/tail trim
            int head2 = Mathf.Clamp(Mathf.FloorToInt(budget * 0.58f), 256, budget - 256);
            int tail2 = budget - head2 - 24;
            if (tail2 < 128)
            {
                tail2 = 128;
                head2 = budget - tail2 - 24;
            }

            string trimmed2 =
                prompt.Substring(0, head2).TrimEnd()
                + "\n[...trimmed for latency...]\n"
                + prompt.Substring(prompt.Length - tail2).TrimStart();

            if (m_LogDebug)
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "Trimmed remote system prompt",
                        ("fromChars", prompt.Length),
                        ("toChars", trimmed2.Length),
                        ("budget", budget)
                    )
                );
            }

            return trimmed2;
        }

        private GameObject ResolveSpawnedObject(ulong networkObjectId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (
                networkObjectId == 0
                || manager == null
                || !manager.IsListening
                || manager.SpawnManager == null
            )
            {
                return null;
            }

            if (
                manager.SpawnManager.SpawnedObjects.TryGetValue(
                    networkObjectId,
                    out NetworkObject networkObject
                )
            )
            {
                return networkObject.gameObject;
            }

            return null;
        }

        private NpcDialogueActor ResolveDialogueActor(ulong speakerNetworkId)
        {
            GameObject speakerObject = ResolveSpawnedObject(speakerNetworkId);
            if (speakerObject == null)
            {
                return null;
            }

            return speakerObject.GetComponent<NpcDialogueActor>();
        }

        private NpcDialogueActor ResolveDialogueActorForRequest(
            DialogueRequest request,
            out ulong resolvedSpeakerNetworkId,
            out ulong resolvedListenerNetworkId,
            out bool usedListenerFallback
        )
        {
            resolvedSpeakerNetworkId = request.SpeakerNetworkId;
            resolvedListenerNetworkId = request.ListenerNetworkId;
            usedListenerFallback = false;

            NpcDialogueActor speakerActor = ResolveDialogueActor(request.SpeakerNetworkId);
            if (speakerActor != null && speakerActor.Profile == null)
            {
                speakerActor.TryAutoAssignProfileFromName();
            }
            if (speakerActor != null && speakerActor.Profile != null)
            {
                return speakerActor;
            }

            NpcDialogueActor listenerActor = ResolveDialogueActor(request.ListenerNetworkId);
            if (listenerActor != null && listenerActor.Profile == null)
            {
                listenerActor.TryAutoAssignProfileFromName();
            }
            if (listenerActor != null && listenerActor.Profile != null)
            {
                resolvedSpeakerNetworkId = request.ListenerNetworkId;
                resolvedListenerNetworkId = request.SpeakerNetworkId;
                usedListenerFallback = request.SpeakerNetworkId != request.ListenerNetworkId;
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "Dialogue",
                        NGLog.Format(
                            "Resolved NPC speaker fallback",
                            ("originalSpeaker", request.SpeakerNetworkId),
                            ("originalListener", request.ListenerNetworkId),
                            ("resolvedSpeaker", resolvedSpeakerNetworkId),
                            ("resolvedListener", resolvedListenerNetworkId)
                        )
                    );
                }
                return listenerActor;
            }

            if (speakerActor != null)
            {
                return speakerActor;
            }

            if (listenerActor != null)
            {
                resolvedSpeakerNetworkId = request.ListenerNetworkId;
                resolvedListenerNetworkId = request.SpeakerNetworkId;
                usedListenerFallback = request.SpeakerNetworkId != request.ListenerNetworkId;
                return listenerActor;
            }

            return null;
        }

        private void RebuildPlayerPromptContextLookup()
        {
            m_PlayerPromptContextByNetworkId.Clear();
            if (m_PlayerPromptContextBindings == null)
            {
                m_PlayerPromptContextBindings = new List<PlayerPromptContextBinding>();
                return;
            }

            for (int i = 0; i < m_PlayerPromptContextBindings.Count; i++)
            {
                PlayerPromptContextBinding binding = m_PlayerPromptContextBindings[i];
                if (binding == null || !binding.Enabled || binding.PlayerNetworkId == 0)
                {
                    continue;
                }

                binding.NameId = string.IsNullOrWhiteSpace(binding.NameId)
                    ? string.Empty
                    : binding.NameId.Trim();
                binding.CustomizationJson = string.IsNullOrWhiteSpace(binding.CustomizationJson)
                    ? "{}"
                    : binding.CustomizationJson.Trim();
                m_PlayerPromptContextByNetworkId[binding.PlayerNetworkId] = binding;
            }
        }

        private void RebuildPlayerIdentityLookup()
        {
            m_PlayerIdentityByClientId.Clear();
            m_PlayerIdentityByNetworkId.Clear();
            if (m_PlayerIdentityBindings == null)
            {
                m_PlayerIdentityBindings = new List<PlayerIdentityBinding>();
                return;
            }

            for (int i = 0; i < m_PlayerIdentityBindings.Count; i++)
            {
                PlayerIdentityBinding binding = m_PlayerIdentityBindings[i];
                if (binding == null || !binding.Enabled)
                {
                    continue;
                }

                binding.NameId = string.IsNullOrWhiteSpace(binding.NameId)
                    ? string.Empty
                    : binding.NameId.Trim();
                binding.CustomizationJson = NormalizePromptContextJson(
                    binding.NameId,
                    binding.CustomizationJson
                );
                if (string.IsNullOrWhiteSpace(binding.LastUpdatedUtc))
                {
                    binding.LastUpdatedUtc = DateTime.UtcNow.ToString("o");
                }

                if (binding.ClientId != ulong.MaxValue)
                {
                    m_PlayerIdentityByClientId[binding.ClientId] = binding;
                }

                if (binding.PlayerNetworkId != 0)
                {
                    m_PlayerIdentityByNetworkId[binding.PlayerNetworkId] = binding;
                }
            }
        }

        private void SynthesizeIdentityBindingsFromRuntimeBindings()
        {
            foreach (var promptBinding in m_PlayerPromptContextByNetworkId.Values)
            {
                if (
                    promptBinding == null
                    || !promptBinding.Enabled
                    || promptBinding.PlayerNetworkId == 0
                )
                {
                    continue;
                }

                UpsertPlayerIdentity(
                    ResolveOwnerClientIdForPlayerNetworkId(promptBinding.PlayerNetworkId),
                    promptBinding.PlayerNetworkId,
                    promptBinding.NameId,
                    promptBinding.CustomizationJson
                );
            }
        }

        private PlayerPromptContextBinding FindOrCreatePlayerPromptContextBinding(
            ulong playerNetworkId
        )
        {
            if (m_PlayerPromptContextBindings == null)
            {
                m_PlayerPromptContextBindings = new List<PlayerPromptContextBinding>();
            }

            for (int i = 0; i < m_PlayerPromptContextBindings.Count; i++)
            {
                PlayerPromptContextBinding existing = m_PlayerPromptContextBindings[i];
                if (existing == null || existing.PlayerNetworkId != playerNetworkId)
                {
                    continue;
                }

                return existing;
            }

            var created = new PlayerPromptContextBinding
            {
                PlayerNetworkId = playerNetworkId,
                NameId = string.Empty,
                CustomizationJson = "{}",
                Enabled = true,
            };
            m_PlayerPromptContextBindings.Add(created);
            return created;
        }

        private PlayerIdentityBinding FindOrCreatePlayerIdentityBinding(
            ulong clientId,
            ulong playerNetworkId
        )
        {
            if (m_PlayerIdentityBindings == null)
            {
                m_PlayerIdentityBindings = new List<PlayerIdentityBinding>();
            }

            if (
                clientId != ulong.MaxValue
                && m_PlayerIdentityByClientId.TryGetValue(clientId, out var byClient)
            )
            {
                return byClient;
            }

            if (
                playerNetworkId != 0
                && m_PlayerIdentityByNetworkId.TryGetValue(playerNetworkId, out var byPlayer)
            )
            {
                return byPlayer;
            }

            for (int i = 0; i < m_PlayerIdentityBindings.Count; i++)
            {
                PlayerIdentityBinding existing = m_PlayerIdentityBindings[i];
                if (existing == null)
                {
                    continue;
                }

                if (
                    (clientId != ulong.MaxValue && existing.ClientId == clientId)
                    || (playerNetworkId != 0 && existing.PlayerNetworkId == playerNetworkId)
                )
                {
                    return existing;
                }
            }

            var created = new PlayerIdentityBinding
            {
                ClientId = clientId,
                PlayerNetworkId = playerNetworkId,
                NameId = string.Empty,
                CustomizationJson = "{}",
                Enabled = true,
                LastUpdatedUtc = DateTime.UtcNow.ToString("o"),
            };
            m_PlayerIdentityBindings.Add(created);
            return created;
        }

        private void UpsertPlayerIdentity(
            ulong clientId,
            ulong playerNetworkId,
            string nameId,
            string customizationJson
        )
        {
            PlayerIdentityBinding identity = FindOrCreatePlayerIdentityBinding(
                clientId,
                playerNetworkId
            );
            if (identity == null)
            {
                return;
            }

            ulong oldClientId = identity.ClientId;
            ulong oldPlayerNetworkId = identity.PlayerNetworkId;
            string oldNameId = identity.NameId ?? string.Empty;
            string oldCustomization = identity.CustomizationJson ?? "{}";

            if (clientId != ulong.MaxValue)
            {
                identity.ClientId = clientId;
            }

            if (playerNetworkId != 0)
            {
                identity.PlayerNetworkId = playerNetworkId;
            }

            if (!string.IsNullOrWhiteSpace(nameId))
            {
                string normalizedName = nameId.Trim();
                bool incomingIsPlaceholder = normalizedName.StartsWith(
                    "client_",
                    StringComparison.OrdinalIgnoreCase
                );
                if (string.IsNullOrWhiteSpace(identity.NameId) || !incomingIsPlaceholder)
                {
                    identity.NameId = normalizedName;
                }
            }

            if (!string.IsNullOrWhiteSpace(customizationJson))
            {
                identity.CustomizationJson = NormalizePromptContextJson(
                    identity.NameId,
                    customizationJson
                );
            }

            identity.Enabled = true;
            identity.LastUpdatedUtc = DateTime.UtcNow.ToString("o");

            if (identity.ClientId != ulong.MaxValue)
            {
                m_PlayerIdentityByClientId[identity.ClientId] = identity;
            }

            if (identity.PlayerNetworkId != 0)
            {
                m_PlayerIdentityByNetworkId[identity.PlayerNetworkId] = identity;
            }

            if (m_LogDebug)
            {
                bool changed =
                    oldClientId != identity.ClientId
                    || oldPlayerNetworkId != identity.PlayerNetworkId
                    || !string.Equals(oldNameId, identity.NameId, StringComparison.Ordinal)
                    || !string.Equals(
                        oldCustomization,
                        identity.CustomizationJson,
                        StringComparison.Ordinal
                    );

                if (changed)
                {
                    NGLog.Info(
                        "Dialogue",
                        NGLog.Format(
                            "Player identity updated",
                            ("clientId", identity.ClientId),
                            ("playerNetworkId", identity.PlayerNetworkId),
                            ("name_id", identity.NameId ?? string.Empty),
                            (
                                "hasCustomization",
                                !IsPlaceholderPromptContextJson(identity.CustomizationJson)
                            )
                        )
                    );
                }
            }
        }

        private PlayerIdentityBinding ResolvePlayerIdentityForRequest(DialogueRequest request)
        {
            if (request.RequestingClientId != 0)
            {
                if (
                    m_PlayerIdentityByClientId.TryGetValue(
                        request.RequestingClientId,
                        out PlayerIdentityBinding byClient
                    )
                    && byClient != null
                    && byClient.Enabled
                )
                {
                    return byClient;
                }
            }

            ulong playerNetworkId = ResolvePlayerNetworkIdForRequest(request);
            if (
                playerNetworkId != 0
                && m_PlayerIdentityByNetworkId.TryGetValue(
                    playerNetworkId,
                    out PlayerIdentityBinding byNetwork
                )
                && byNetwork != null
                && byNetwork.Enabled
            )
            {
                return byNetwork;
            }

            if (
                request.RequestingClientId != 0
                && TryGetPlayerNetworkObjectIdForClient(
                    request.RequestingClientId,
                    out ulong resolvedPlayerNetworkId
                )
            )
            {
                if (
                    m_PlayerIdentityByNetworkId.TryGetValue(
                        resolvedPlayerNetworkId,
                        out PlayerIdentityBinding fallback
                    )
                    && fallback != null
                    && fallback.Enabled
                )
                {
                    return fallback;
                }
            }

            return null;
        }

        private ulong ResolveOwnerClientIdForPlayerNetworkId(ulong playerNetworkId)
        {
            if (playerNetworkId == 0 || NetworkManager.Singleton?.SpawnManager == null)
            {
                return ulong.MaxValue;
            }

            if (
                !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    playerNetworkId,
                    out NetworkObject playerObject
                )
                || playerObject == null
            )
            {
                return ulong.MaxValue;
            }

            return playerObject.OwnerClientId;
        }

        private static PlayerIdentitySnapshot ToSnapshot(PlayerIdentityBinding identity)
        {
            return new PlayerIdentitySnapshot
            {
                ClientId = identity.ClientId,
                PlayerNetworkId = identity.PlayerNetworkId,
                NameId = identity.NameId ?? string.Empty,
                CustomizationJson = identity.CustomizationJson ?? "{}",
                LastUpdatedUtc = identity.LastUpdatedUtc ?? string.Empty,
            };
        }

        private PlayerPromptContextBinding ResolvePlayerPromptContextBinding(
            DialogueRequest request
        )
        {
            if (!m_EnablePlayerPromptContext)
            {
                return null;
            }

            ulong playerNetworkId = ResolvePlayerNetworkIdForRequest(request);
            if (playerNetworkId == 0)
            {
                return null;
            }

            return m_PlayerPromptContextByNetworkId.TryGetValue(playerNetworkId, out var binding)
                ? binding
                : null;
        }

        private string BuildPlayerContextPrompt(DialogueRequest request, GameObject listenerObject)
        {
            if (!m_EnablePlayerPromptContext)
            {
                return string.Empty;
            }

            PlayerPromptContextBinding binding = ResolvePlayerPromptContextBinding(request);
            PlayerIdentityBinding identity = ResolvePlayerIdentityForRequest(request);
            string nameId = identity?.NameId;
            if (string.IsNullOrWhiteSpace(nameId))
            {
                nameId = binding?.NameId ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(nameId))
            {
                LocalPlayerAuthService authService = LocalPlayerAuthService.Instance;
                if (authService != null && authService.HasCurrentPlayer)
                {
                    nameId = authService.CurrentPlayer.NameId;
                }
            }

            if (string.IsNullOrWhiteSpace(nameId) && listenerObject != null)
            {
                nameId = listenerObject.name;
            }

            string customizationJson = identity?.CustomizationJson;
            if (string.IsNullOrWhiteSpace(customizationJson))
            {
                customizationJson = binding?.CustomizationJson ?? "{}";
            }
            if (string.IsNullOrWhiteSpace(customizationJson))
            {
                customizationJson = "{}";
            }

            if (IsPlaceholderPromptContextJson(customizationJson))
            {
                LocalPlayerAuthService authService = LocalPlayerAuthService.Instance;
                if (
                    authService != null
                    && authService.HasCurrentPlayer
                    && (
                        string.IsNullOrWhiteSpace(nameId)
                        || string.Equals(
                            authService.CurrentPlayer.NameId,
                            nameId,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    customizationJson = authService.GetCustomizationJson();
                }
            }

            customizationJson = NormalizePromptContextJson(nameId, customizationJson);

            int maxChars = Mathf.Max(64, m_MaxPlayerCustomizationChars);
            if (UseOpenAIRemote)
            {
                maxChars = Mathf.Min(maxChars, Mathf.Max(64, m_RemoteMaxPlayerCustomizationChars));
            }
            if (customizationJson.Length > maxChars)
            {
                customizationJson = customizationJson.Substring(0, maxChars).TrimEnd() + "...";
            }

            ulong playerNetworkId =
                identity?.PlayerNetworkId != 0
                    ? identity.PlayerNetworkId
                    : ResolvePlayerNetworkIdForRequest(request);
            ulong clientId = request.RequestingClientId;
            if (clientId == 0 && identity != null)
            {
                clientId = identity.ClientId;
            }

            string playerRole = clientId == 0 ? "host" : "client";

            NpcDialogueActor narrativeActor = ResolveDialogueActorForRequest(
                request,
                out _,
                out _,
                out _
            );
            string narrativeHints = BuildNarrativeHints(
                identity,
                narrativeActor?.Profile,
                customizationJson
            );
            string playerContextBlock =
                "[PlayerContext]\n"
                + $"name_id: {nameId}\n"
                + $"client_id: {clientId}\n"
                + $"player_network_id: {playerNetworkId}\n"
                + $"player_role: {playerRole}\n"
                + $"customization_json: {customizationJson}\n"
                + "Use this context to personalize details naturally without exposing internal formatting.";
            if (!string.IsNullOrEmpty(narrativeHints))
            {
                playerContextBlock += "\n\n" + narrativeHints;
            }
            return playerContextBlock;
        }

        private static string NormalizePromptContextJson(string nameId, string customizationJson)
        {
            string trimmed = string.IsNullOrWhiteSpace(customizationJson)
                ? "{}"
                : customizationJson.Trim();
            if (!IsPlaceholderPromptContextJson(trimmed))
            {
                return trimmed;
            }

            string resolvedName = string.IsNullOrWhiteSpace(nameId)
                ? "player_local"
                : nameId.Trim();
            return "{"
                + $"\"name_id\":\"{EscapeJsonString(resolvedName)}\","
                + "\"customization\":{},"
                + "\"source\":\"network_context\""
                + "}";
        }

        private static bool IsPlaceholderPromptContextJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            string trimmed = json.Trim();
            return trimmed == "{}" || trimmed == "{ }" || trimmed == "null";
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
        }

        /// <summary>
        /// Parses a player's CustomizationJson to build per-player effect modifiers.
        /// Applied server-side before ClampDynamicMultiplier so NPC profile bounds still govern extremes.
        /// </summary>
        private static PlayerEffectModifier BuildPlayerEffectModifier(
            PlayerIdentityBinding identity
        )
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.CustomizationJson))
            {
                return PlayerEffectModifier.Neutral;
            }

            PlayerEffectModifier mod = PlayerEffectModifier.Neutral;
            string json = identity.CustomizationJson;

            mod.DamageScaleReceived = TryReadJsonFloat(json, "vulnerability", 1f);
            mod.EffectSizeScale = TryReadJsonFloat(json, "effect_size_bias", 1f);
            mod.EffectDurationScale = TryReadJsonFloat(json, "effect_duration_bias", 1f);
            mod.AggressionBias = TryReadJsonFloat(json, "aggression_bias", 1f);

            string shieldStr = TryReadJsonString(json, "has_shield");
            mod.IsShielded =
                string.Equals(shieldStr, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shieldStr, "1", StringComparison.Ordinal);

            mod.PreferredElement = TryReadJsonString(json, "element_affinity");

            string colorTheme = TryReadJsonString(json, "color_theme");
            if (!string.IsNullOrWhiteSpace(colorTheme))
            {
                mod.PreferredColor = ParseColorFromTheme(colorTheme);
            }

            // Clamp to safe ranges
            mod.DamageScaleReceived = Mathf.Clamp(mod.DamageScaleReceived, 0f, 3f);
            mod.EffectSizeScale = Mathf.Clamp(mod.EffectSizeScale, 0.25f, 4f);
            mod.EffectDurationScale = Mathf.Clamp(mod.EffectDurationScale, 0.25f, 4f);
            mod.AggressionBias = Mathf.Clamp(mod.AggressionBias, 0.25f, 3f);
            return mod;
        }

        /// <summary>
        /// Lightweight JSON string field reader. Looks for "key":"value" or "key": "value".
        /// Returns null if key is not found.
        /// </summary>
        private static string TryReadJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }
            string search = $"\"{key}\"";
            int keyIdx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0)
            {
                return null;
            }
            int colonIdx = json.IndexOf(':', keyIdx + search.Length);
            if (colonIdx < 0)
            {
                return null;
            }
            int quoteStart = json.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0)
            {
                return null;
            }
            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0)
            {
                return null;
            }
            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        /// <summary>
        /// Lightweight JSON float field reader. Returns defaultValue if key is not found or not parseable.
        /// </summary>
        private static float TryReadJsonFloat(string json, string key, float defaultValue = 1f)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return defaultValue;
            }
            string search = $"\"{key}\"";
            int keyIdx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0)
            {
                return defaultValue;
            }
            int colonIdx = json.IndexOf(':', keyIdx + search.Length);
            if (colonIdx < 0)
            {
                return defaultValue;
            }
            // Skip whitespace
            int valueStart = colonIdx + 1;
            while (
                valueStart < json.Length && (json[valueStart] == ' ' || json[valueStart] == '\t')
            )
            {
                valueStart++;
            }
            // Handle quoted float string ("1.5")
            if (valueStart < json.Length && json[valueStart] == '"')
            {
                int qEnd = json.IndexOf('"', valueStart + 1);
                if (qEnd > valueStart)
                {
                    string quoted = json.Substring(valueStart + 1, qEnd - valueStart - 1);
                    if (
                        float.TryParse(
                            quoted,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float qv
                        )
                    )
                    {
                        return qv;
                    }
                }
                return defaultValue;
            }
            // Unquoted number
            int valueEnd = valueStart;
            while (
                valueEnd < json.Length
                && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.' || json[valueEnd] == '-')
            )
            {
                valueEnd++;
            }
            if (valueEnd > valueStart)
            {
                string numStr = json.Substring(valueStart, valueEnd - valueStart);
                if (
                    float.TryParse(
                        numStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float v
                    )
                )
                {
                    return v;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Converts a color theme keyword to a Unity Color.
        /// Returns null if not recognized.
        /// </summary>
        private static Color? ParseColorFromTheme(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme))
            {
                return null;
            }
            switch (theme.Trim().ToLowerInvariant())
            {
                case "red":
                    return new Color(0.9f, 0.15f, 0.15f);
                case "blue":
                    return new Color(0.15f, 0.4f, 0.95f);
                case "green":
                    return new Color(0.1f, 0.8f, 0.2f);
                case "yellow":
                    return new Color(1f, 0.9f, 0.1f);
                case "orange":
                    return new Color(1f, 0.5f, 0.05f);
                case "purple":
                    return new Color(0.6f, 0.1f, 0.9f);
                case "white":
                    return Color.white;
                case "black":
                    return new Color(0.1f, 0.1f, 0.1f);
                case "fire":
                    return new Color(1f, 0.4f, 0.05f);
                case "ice":
                    return new Color(0.4f, 0.8f, 1f);
                case "storm":
                    return new Color(0.5f, 0.5f, 0.9f);
                case "void":
                    return new Color(0.2f, 0f, 0.3f);
                case "nature":
                    return new Color(0.2f, 0.7f, 0.2f);
                case "water":
                    return new Color(0.1f, 0.5f, 0.9f);
                case "earth":
                    return new Color(0.5f, 0.35f, 0.1f);
                case "mystic":
                    return new Color(0.7f, 0.3f, 0.9f);
                default:
                    if (ColorUtility.TryParseHtmlString(theme, out Color parsed))
                    {
                        return parsed;
                    }
                    return null;
            }
        }

        /// <summary>
        /// Converts player customization data and the NPC's profile into English roleplay hints
        /// injected into the NPC system prompt so the LLM reasons about the player's traits.
        /// </summary>
        private static string BuildNarrativeHints(
            PlayerIdentityBinding identity,
            NpcDialogueProfile npcProfile,
            string customizationJson
        )
        {
            if (string.IsNullOrWhiteSpace(customizationJson) || customizationJson.Trim() == "{}")
            {
                return string.Empty;
            }

            var hints = new System.Text.StringBuilder();
            hints.Append("[NarrativeHints]\n");
            bool hasHints = false;

            string element = TryReadJsonString(customizationJson, "element_affinity");
            if (!string.IsNullOrWhiteSpace(element))
            {
                hints.Append(
                    $"- This player has a {element} affinity; reference {element}-themed imagery in your response.\n"
                );
                hasHints = true;
            }

            string colorTheme = TryReadJsonString(customizationJson, "color_theme");
            if (!string.IsNullOrWhiteSpace(colorTheme))
            {
                hints.Append(
                    $"- The player's color theme is {colorTheme}; prefer {colorTheme}-toned effects.\n"
                );
                hasHints = true;
            }

            float aggressionBias = TryReadJsonFloat(customizationJson, "aggression_bias", 1f);
            if (aggressionBias >= 1.3f)
            {
                hints.Append(
                    "- You are hostile to this player; use menacing language and cast aggressive effects.\n"
                );
                hasHints = true;
            }
            else if (aggressionBias <= 0.7f)
            {
                hints.Append(
                    "- You are friendly toward this player; use warm, supportive language.\n"
                );
                hasHints = true;
            }

            string shieldStr = TryReadJsonString(customizationJson, "has_shield");
            bool hasShield =
                string.Equals(shieldStr, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shieldStr, "1", StringComparison.Ordinal);
            if (hasShield)
            {
                hints.Append(
                    "- This player has an active shield; acknowledge their defenses in your response.\n"
                );
                hasHints = true;
            }

            float vulnerability = TryReadJsonFloat(customizationJson, "vulnerability", 1f);
            if (vulnerability >= 1.4f)
            {
                hints.Append(
                    "- This player is vulnerable; effects may hit harder than expected.\n"
                );
                hasHints = true;
            }

            // Player class / archetype greeting
            string playerClass = TryReadJsonString(customizationJson, "class");
            if (!string.IsNullOrWhiteSpace(playerClass))
            {
                hints.Append(
                    $"- This player is a {playerClass}; open your greeting with lore that fits their archetype.\n"
                );
                hasHints = true;
            }

            // NPC-specific reputation (5-tier), with fallback to legacy binary relationship
            if (npcProfile != null && !string.IsNullOrWhiteSpace(npcProfile.ProfileId))
            {
                string repKey = $"reputation_{npcProfile.ProfileId}";
                string repStr = TryReadJsonString(customizationJson, repKey);
                if (!string.IsNullOrWhiteSpace(repStr) && int.TryParse(repStr, out int rep))
                {
                    rep = Mathf.Clamp(rep, 0, 100);
                    string repHint = rep switch
                    {
                        < 20 =>
                            $"- This player has deeply wronged you (reputation {rep}/100); be cold, suspicious, and unwilling to help.\n",
                        < 40 =>
                            $"- This player is distrusted (reputation {rep}/100); keep them at arm's length and be guarded.\n",
                        < 60 =>
                            $"- This player is a neutral acquaintance (reputation {rep}/100); treat them professionally.\n",
                        < 80 =>
                            $"- This player has earned your goodwill (reputation {rep}/100); be warm and willing to share extra lore.\n",
                        _ =>
                            $"- This player is a trusted champion (reputation {rep}/100); speak with reverence and share your deepest secrets.\n",
                    };
                    hints.Append(repHint);
                    hasHints = true;
                }
                else
                {
                    // Fallback: legacy binary relationship field
                    string relationship = TryReadJsonString(
                        customizationJson,
                        $"relationship_{npcProfile.ProfileId}"
                    );
                    if (string.Equals(relationship, "hostile", StringComparison.OrdinalIgnoreCase))
                    {
                        hints.Append(
                            "- Your relationship with this player is hostile; act accordingly.\n"
                        );
                        hasHints = true;
                    }
                    else if (
                        string.Equals(relationship, "ally", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        hints.Append("- This player is your ally; protect and empower them.\n");
                        hasHints = true;
                    }
                }
            }

            // Inventory
            string inventoryRaw = TryReadJsonString(customizationJson, "inventory_tags");
            if (!string.IsNullOrWhiteSpace(inventoryRaw))
            {
                hints.Append(
                    $"- The player carries: {inventoryRaw}. Reference these items naturally if relevant.\n"
                );
                hasHints = true;
            }

            // Quest flags
            string questRaw = TryReadJsonString(customizationJson, "quest_flags");
            if (!string.IsNullOrWhiteSpace(questRaw))
            {
                hints.Append(
                    $"- Player story flags: {questRaw}. Use these to advance or acknowledge the narrative.\n"
                );
                hasHints = true;
            }

            // Last in-world action
            string lastAction = TryReadJsonString(customizationJson, "last_action");
            if (!string.IsNullOrWhiteSpace(lastAction))
            {
                hints.Append(
                    $"- The player recently: {lastAction}. React to this in your opening line if appropriate.\n"
                );
                hasHints = true;
            }

            hints.Append(
                "- Do not reference internal identifiers or JSON formatting in your response."
            );

            return hasHints ? hints.ToString() : string.Empty;
        }

        private ulong ResolvePlayerNetworkIdForRequest(DialogueRequest request)
        {
            // Prefer the requesting client's player for per-player targeting/context.
            if (
                (request.RequestingClientId != 0 || request.IsUserInitiated)
                && TryGetPlayerNetworkObjectIdForClient(
                    request.RequestingClientId,
                    out ulong requesterPlayerNetworkId
                )
            )
            {
                return requesterPlayerNetworkId;
            }

            if (IsConnectedClientPlayerObject(request.ListenerNetworkId))
            {
                return request.ListenerNetworkId;
            }

            if (IsConnectedClientPlayerObject(request.SpeakerNetworkId))
            {
                return request.SpeakerNetworkId;
            }

            return 0;
        }

        private bool TryResolveSingleConnectedPlayerFallback(out ulong playerNetworkId)
        {
            playerNetworkId = 0;
            if (
                NetworkManager.Singleton == null
                || NetworkManager.Singleton.ConnectedClients == null
                || NetworkManager.Singleton.ConnectedClients.Count == 0
            )
            {
                return false;
            }

            ulong resolved = 0;
            int matchedCount = 0;
            foreach (var client in NetworkManager.Singleton.ConnectedClients.Values)
            {
                if (
                    TryGetPlayerNetworkObjectIdForClient(
                        client.ClientId,
                        out ulong candidatePlayerNetworkId
                    )
                )
                {
                    matchedCount++;
                    resolved = candidatePlayerNetworkId;
                    if (matchedCount > 1)
                    {
                        break;
                    }
                }
            }

            if (matchedCount == 1 && resolved != 0)
            {
                playerNetworkId = resolved;
                return true;
            }

            return false;
        }

        private bool IsConnectedClientPlayerObject(ulong networkObjectId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (
                networkObjectId == 0
                || manager == null
                || !manager.IsListening
                || manager.ConnectedClients == null
            )
            {
                return false;
            }

            foreach (var client in manager.ConnectedClients.Values)
            {
                if (client?.PlayerObject == null)
                {
                    continue;
                }

                if (client.PlayerObject.NetworkObjectId == networkObjectId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetPlayerNetworkObjectIdForClient(ulong clientId, out ulong playerNetworkId)
        {
            playerNetworkId = 0;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.ConnectedClients == null)
            {
                return false;
            }

            if (
                manager.ConnectedClients.TryGetValue(clientId, out var client)
                && client?.PlayerObject != null
            )
            {
                playerNetworkId = client.PlayerObject.NetworkObjectId;
                return true;
            }

            return false;
        }

        private bool IsObjectOwnedByClient(ulong networkObjectId, ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (
                networkObjectId == 0
                || manager == null
                || !manager.IsListening
                || manager.SpawnManager == null
            )
            {
                return false;
            }

            if (
                !manager.SpawnManager.SpawnedObjects.TryGetValue(
                    networkObjectId,
                    out NetworkObject networkObject
                )
            )
            {
                return false;
            }

            return networkObject != null && networkObject.OwnerClientId == clientId;
        }

        private bool TryValidateClientDialogueParticipants(
            ulong senderClientId,
            ulong speakerNetworkId,
            ulong listenerNetworkId,
            out string rejectionReason
        )
        {
            rejectionReason = null;
            bool hasSenderPlayer = TryGetPlayerNetworkObjectIdForClient(
                senderClientId,
                out ulong senderPlayerNetworkId
            );

            if (speakerNetworkId == 0 && listenerNetworkId == 0)
            {
                rejectionReason = "participants_missing";
                return false;
            }

            bool senderOwnsSpeaker = IsObjectOwnedByClient(speakerNetworkId, senderClientId);
            bool senderOwnsListener = IsObjectOwnedByClient(listenerNetworkId, senderClientId);
            if (!hasSenderPlayer && !senderOwnsSpeaker && !senderOwnsListener)
            {
                rejectionReason = "requester_player_missing";
                return false;
            }

            bool senderMatchesPlayer =
                hasSenderPlayer
                && (
                    speakerNetworkId == senderPlayerNetworkId
                    || listenerNetworkId == senderPlayerNetworkId
                );
            if (!senderMatchesPlayer && !senderOwnsSpeaker && !senderOwnsListener)
            {
                rejectionReason = "invalid_participants";
                return false;
            }

            return true;
        }

        private bool IsConversationKeyVisibleToClient(string conversationKey, ulong senderClientId)
        {
            if (string.IsNullOrWhiteSpace(conversationKey))
            {
                return false;
            }

            bool hasSenderPlayer = TryGetPlayerNetworkObjectIdForClient(
                senderClientId,
                out ulong senderPlayerNetworkId
            );

            string key = conversationKey.Trim();
            string clientScopePrefix = $"client:{senderClientId}";
            if (
                string.Equals(key, clientScopePrefix, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith($"{clientScopePrefix}:", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            if (
                key.StartsWith("actor:", StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(key.Substring("actor:".Length), out ulong actorId)
            )
            {
                return (hasSenderPlayer && actorId == senderPlayerNetworkId)
                    || IsObjectOwnedByClient(actorId, senderClientId);
            }

            int firstColon = key.IndexOf(':');
            int secondColon = firstColon >= 0 ? key.IndexOf(':', firstColon + 1) : -1;
            if (
                firstColon > 0
                && secondColon < 0
                && ulong.TryParse(key.Substring(0, firstColon), out ulong first)
                && ulong.TryParse(key.Substring(firstColon + 1), out ulong second)
            )
            {
                return (
                        hasSenderPlayer
                        && (first == senderPlayerNetworkId || second == senderPlayerNetworkId)
                    )
                    || IsObjectOwnedByClient(first, senderClientId)
                    || IsObjectOwnedByClient(second, senderClientId);
            }

            return false;
        }

        private void ApplyContextEffects(DialogueRequest request, string responseText)
        {
            if (!m_EnableContextSceneEffects)
            {
                NGLog.Info("DialogueFX", "Skip effects (disabled in NetworkDialogueService).");
                return;
            }

            if (string.IsNullOrWhiteSpace(responseText))
            {
                NGLog.Info("DialogueFX", "Skip effects (empty response text).");
                return;
            }

            bool isGameplayProbe = IsGameplayProbeRequest(request, request.Prompt);
            NpcDialogueActor actor = ResolveDialogueActorForRequest(
                request,
                out ulong resolvedSpeakerNetworkId,
                out ulong resolvedListenerNetworkId,
                out bool usedListenerFallback
            );

            DialogueRequest normalizedRequest = request;
            if (usedListenerFallback)
            {
                normalizedRequest.SpeakerNetworkId = resolvedSpeakerNetworkId;
                normalizedRequest.ListenerNetworkId = resolvedListenerNetworkId;
            }

            NpcDialogueProfile profile = actor != null ? actor.Profile : null;
            bool missingActorProfile = actor == null || profile == null;
            if (missingActorProfile && !isGameplayProbe)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Skip effects (actor missing)",
                        ("speaker", request.SpeakerNetworkId),
                        ("listener", request.ListenerNetworkId),
                        ("resolvedSpeaker", resolvedSpeakerNetworkId),
                        ("resolvedListener", resolvedListenerNetworkId)
                    )
                );
                return;
            }

            if (missingActorProfile && isGameplayProbe)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Probe mode fallback (actor/profile missing); continuing with parser-only dispatch",
                        ("speaker", request.SpeakerNetworkId),
                        ("listener", request.ListenerNetworkId),
                        ("resolvedSpeaker", resolvedSpeakerNetworkId),
                        ("resolvedListener", resolvedListenerNetworkId)
                    )
                );
            }

            if (m_LogDebug)
            {
                ulong resolvedPlayerNetworkId = ResolvePlayerNetworkIdForRequest(normalizedRequest);
                PlayerIdentityBinding resolvedIdentity = ResolvePlayerIdentityForRequest(
                    normalizedRequest
                );
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Effect dispatch player identity",
                        ("senderClientId", normalizedRequest.RequestingClientId),
                        ("resolvedPlayerNetId", resolvedPlayerNetworkId),
                        (
                            "name_id",
                            string.IsNullOrWhiteSpace(resolvedIdentity?.NameId)
                                ? "<unknown>"
                                : resolvedIdentity.NameId
                        ),
                        ("speaker", normalizedRequest.SpeakerNetworkId),
                        ("listener", normalizedRequest.ListenerNetworkId)
                    )
                );
            }

            string effectContext = BuildEffectContextText(request.Prompt, responseText);
            string keywordContext = isGameplayProbe
                ? (responseText ?? string.Empty)
                : effectContext;
            Color boredColor = Color.blue;
            float boredIntensity = 1f;
            float transitionSeconds = 0.35f;
            bool hasBoredLighting =
                actor != null
                && profile != null
                && actor.TryGetBoredLighting(
                    keywordContext,
                    out boredColor,
                    out boredIntensity,
                    out transitionSeconds
                );
            if (isGameplayProbe)
            {
                // Probe runs validate one requested power per step. Avoid ambient side-effects.
                hasBoredLighting = false;
            }

            ParticleParameterExtractor.ParticleParameterIntent parameterIntent =
                isGameplayProbe || (profile != null && profile.EnableDynamicEffectParameters)
                    ? ParticleParameterExtractor.Extract(
                        isGameplayProbe ? (responseText ?? string.Empty) : effectContext
                    )
                    : ParticleParameterExtractor.ParticleParameterIntent.Default;
            if (parameterIntent.HasAnyOverride && hasBoredLighting)
            {
                float intensityMultiplier = ClampDynamicMultiplier(
                    parameterIntent.IntensityMultiplier,
                    profile
                );
                if (parameterIntent.HasExplicitIntensityMultiplier)
                {
                    intensityMultiplier = ClampDynamicMultiplier(
                        parameterIntent.ExplicitIntensityMultiplier,
                        profile
                    );
                }
                float durationMultiplier = ClampDynamicMultiplier(
                    parameterIntent.DurationMultiplier,
                    profile
                );
                float emotionalMul = parameterIntent.EmotionalMultiplier;
                if (!Mathf.Approximately(emotionalMul, 1f))
                {
                    intensityMultiplier = ClampDynamicMultiplier(
                        intensityMultiplier * emotionalMul,
                        profile
                    );
                }

                boredColor = ApplyDynamicColorOverride(boredColor, parameterIntent, 0.82f);
                boredIntensity = Mathf.Max(0f, boredIntensity * intensityMultiplier);
                transitionSeconds = Mathf.Max(0.05f, transitionSeconds * durationMultiplier);
            }

            EffectCatalog catalog = EnsureEffectCatalog();
            List<EffectIntent> catalogIntents = EffectParser.ExtractIntents(
                responseText,
                catalog,
                stripTags: false
            );
            bool hasCatalogIntents = catalogIntents != null && catalogIntents.Count > 0;
            if (isGameplayProbe)
            {
                AdjustIntentsForProbeMode(
                    request,
                    catalog,
                    ref catalogIntents,
                    ref hasCatalogIntents
                );
            }
            PlayerSpecialEffectMode specialEffectMode = ResolvePlayerSpecialEffectMode(
                isGameplayProbe ? string.Empty : request.Prompt,
                isGameplayProbe ? string.Empty : responseText,
                catalogIntents
            );

            // Resolve per-player effect modifier from the target player's customization data
            PlayerIdentityBinding targetPlayerIdentity = ResolvePlayerIdentityForRequest(
                normalizedRequest
            );
            PlayerEffectModifier playerMod = BuildPlayerEffectModifier(targetPlayerIdentity);

            bool hasPlayerSpecialEffect = specialEffectMode != PlayerSpecialEffectMode.None;
            bool shouldApplyFloorFreezeMaterial =
                !isGameplayProbe
                && ShouldApplyFloorFreezeSurfaceMaterial(normalizedRequest, catalogIntents);
            if (
                !hasBoredLighting
                && !hasCatalogIntents
                && !hasPlayerSpecialEffect
                && !shouldApplyFloorFreezeMaterial
            )
            {
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "DialogueFX",
                        NGLog.Format(
                            "No effects matched",
                            ("speaker", normalizedRequest.SpeakerNetworkId),
                            (
                                "promptSnippet",
                                request.Prompt?.Length > 60
                                    ? request.Prompt.Substring(0, 60) + "..."
                                    : request.Prompt ?? string.Empty
                            )
                        )
                    );
                }
                return;
            }

            if (hasPlayerSpecialEffect)
            {
                bool specialApplied = ApplyPlayerSpecialEffects(
                    normalizedRequest,
                    parameterIntent,
                    specialEffectMode
                );
                if (specialApplied && specialEffectMode == PlayerSpecialEffectMode.Dissolve)
                {
                    // Prioritize true invisibility over cosmetic smoke/projectiles.
                    hasCatalogIntents = false;
                    if (m_LogDebug)
                    {
                        NGLog.Debug(
                            "DialogueFX",
                            "Suppressed prefab/catalog effects due to dissolve priority."
                        );
                    }
                }
            }

            GameObject speakerObject = ResolveSpawnedObject(normalizedRequest.SpeakerNetworkId);
            GameObject listenerObject = ResolveSpawnedObject(normalizedRequest.ListenerNetworkId);
            ResolveEffectSpatialContext(
                effectContext,
                speakerObject,
                listenerObject,
                out Vector3 effectOrigin,
                out Vector3 effectForward,
                out string effectAnchorLabel
            );
            if (m_LogDebug)
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Resolved effect anchor",
                        ("anchor", effectAnchorLabel),
                        ("origin", effectOrigin)
                    )
                );
            }

            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Skip effects (controller missing)",
                        ("speaker", request.SpeakerNetworkId)
                    )
                );
                return;
            }

            if (shouldApplyFloorFreezeMaterial)
            {
                TryApplyFloorFreezeSurfaceMaterial(
                    normalizedRequest,
                    catalogIntents,
                    parameterIntent
                );
            }

            if (hasBoredLighting)
            {
                float explicitDuration = ResolveExplicitDurationSeconds(
                    parameterIntent,
                    0.05f,
                    10f
                );
                if (explicitDuration > 0f)
                {
                    transitionSeconds = explicitDuration;
                }

                ApplyBoredLightingClientRpc(
                    new Vector4(boredColor.r, boredColor.g, boredColor.b, boredColor.a),
                    boredIntensity,
                    transitionSeconds
                );
            }

            if (hasCatalogIntents)
            {
                ApplyEffectParserIntents(
                    catalogIntents,
                    normalizedRequest,
                    effectOrigin,
                    effectForward,
                    (PlayerEffectModifier?)playerMod,
                    isGameplayProbe
                );
            }
        }

        private static bool ShouldApplyFloorFreezeSurfaceMaterial(
            DialogueRequest request,
            List<EffectIntent> intents
        )
        {
            if (IsGroundFreezePrompt(request.Prompt))
            {
                return true;
            }

            if (intents == null || intents.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < intents.Count; i++)
            {
                EffectIntent intent = intents[i];
                if (intent == null)
                {
                    continue;
                }

                EffectDefinition def = intent.definition;
                if (ShouldForceGroundFreezeForIntent(request, intent, def))
                {
                    return true;
                }

                if (!LooksLikeFreezeIntent(intent, def))
                {
                    continue;
                }

                string requestedTarget = !string.IsNullOrWhiteSpace(intent.anchor)
                    ? intent.anchor
                    : intent.target;
                if (string.IsNullOrWhiteSpace(requestedTarget))
                {
                    continue;
                }

                string lowerTarget = requestedTarget.Trim().ToLowerInvariant();
                if (IsGroundAlias(lowerTarget))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryApplyFloorFreezeSurfaceMaterial(
            DialogueRequest request,
            List<EffectIntent> intents,
            ParticleParameterExtractor.ParticleParameterIntent parameterIntent
        )
        {
            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                return;
            }

            ulong playerNetworkId = ResolvePlayerNetworkIdForRequest(request);
            if (playerNetworkId == 0)
            {
                TryResolveSingleConnectedPlayerFallback(out playerNetworkId);
            }

            if (playerNetworkId == 0)
            {
                NGLog.Warn(
                    "DialogueFX",
                    "Floor freeze material skipped (no unambiguous player target resolved)."
                );
                return;
            }

            GameObject playerObject = ResolveSpawnedObject(playerNetworkId);
            if (playerObject == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (player object missing)",
                        ("player", playerNetworkId)
                    )
                );
                return;
            }

            NetworkObject playerNetworkObject = playerObject.GetComponent<NetworkObject>();
            var playerTarget = new EffectTargetResolverService.PlayerTarget
            {
                NetworkObject = playerNetworkObject,
                Transform = playerObject.transform,
            };

            if (
                !EffectTargetResolverService.TryResolveFloorUnderPlayer(
                    playerTarget,
                    rayDistance: 8f,
                    layerMask: Physics.DefaultRaycastLayers,
                    out EffectTargetResolverService.SurfaceTarget surfaceTarget,
                    out string resolveReason
                )
            )
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (floor unresolved)",
                        ("player", playerNetworkId),
                        ("reason", resolveReason ?? string.Empty)
                    )
                );
                return;
            }

            Renderer targetRenderer = surfaceTarget?.Renderer;
            if (targetRenderer == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (surface renderer missing)",
                        ("player", playerNetworkId),
                        ("surfaceType", surfaceTarget?.Classification ?? string.Empty)
                    )
                );
                return;
            }

            EffectSurface effectSurface = surfaceTarget.EffectSurface;
            if (effectSurface != null && !effectSurface.AllowFreeze)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (surface disallows freeze)",
                        ("surfaceId", effectSurface.SurfaceId),
                        ("surfaceType", effectSurface.SurfaceType.ToString())
                    )
                );
                return;
            }

            int materialSlotIndex =
                effectSurface != null
                    ? effectSurface.ResolveMaterialSlot(
                        targetRenderer,
                        surfaceTarget.MaterialSlotIndex
                    )
                    : Mathf.Clamp(
                        surfaceTarget.MaterialSlotIndex,
                        0,
                        Mathf.Max(0, (targetRenderer.sharedMaterials?.Length ?? 1) - 1)
                    );

            float durationSeconds = ResolveFloorFreezeSurfaceDurationSeconds(
                request,
                intents,
                parameterIntent
            );
            string surfaceId = effectSurface != null ? effectSurface.SurfaceId : string.Empty;
            string rendererPath = EffectTargetResolverService.GetHierarchyPath(
                targetRenderer.transform
            );

            try
            {
                ApplyFloorFreezeSurfaceMaterialClientRpc(
                    surfaceId,
                    rendererPath,
                    materialSlotIndex,
                    durationSeconds,
                    request.SpeakerNetworkId,
                    playerNetworkId
                );
            }
            catch (Exception ex)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material RPC failed; attempting local fallback",
                        ("surfaceId", surfaceId),
                        ("rendererPath", rendererPath),
                        ("slot", materialSlotIndex),
                        ("error", ex.Message ?? string.Empty)
                    )
                );
                Debug.LogException(ex);
                m_SceneEffectsController?.ApplyFloorFreezeSurfaceMaterial(
                    surfaceId,
                    rendererPath,
                    materialSlotIndex,
                    durationSeconds,
                    request.SpeakerNetworkId,
                    playerNetworkId
                );
            }

            NGLog.Info(
                "DialogueFX",
                NGLog.Format(
                    "Floor freeze material dispatch",
                    ("surfaceId", surfaceId),
                    ("renderer", targetRenderer.name),
                    ("slot", materialSlotIndex),
                    ("duration", durationSeconds.ToString("F2")),
                    ("player", playerNetworkId),
                    ("reason", surfaceTarget.ResolverReason ?? string.Empty)
                )
            );
        }

        private static float ResolveFloorFreezeSurfaceDurationSeconds(
            DialogueRequest request,
            List<EffectIntent> intents,
            ParticleParameterExtractor.ParticleParameterIntent parameterIntent
        )
        {
            float durationSeconds = 8f;
            if (intents != null)
            {
                for (int i = 0; i < intents.Count; i++)
                {
                    EffectIntent intent = intents[i];
                    if (intent == null)
                    {
                        continue;
                    }

                    if (!ShouldForceGroundFreezeForIntent(request, intent, intent.definition))
                    {
                        continue;
                    }

                    if (intent.duration > 0f)
                    {
                        return Mathf.Clamp(intent.duration, 0.25f, 60f);
                    }
                }
            }

            if (parameterIntent.HasExplicitDurationSeconds)
            {
                durationSeconds = parameterIntent.ExplicitDurationSeconds;
            }
            else if (!Mathf.Approximately(parameterIntent.DurationMultiplier, 1f))
            {
                durationSeconds *= parameterIntent.DurationMultiplier;
            }

            return Mathf.Clamp(durationSeconds, 0.25f, 60f);
        }

        private static readonly string[] DissolveKeywords = new[]
        {
            "dissolve",
            "invisible",
            "fade away",
            "vanish",
            "disappear",
            "make me invisible",
            "turn invisible",
            "fade out",
            "fade to nothing",
            "invisibility",
            "hide me",
            "can't see me",
            "invisible to",
            "esvanecer",
            "invisível",
            "desaparecer",
            "oculto",
        };

        private static readonly string[] RespawnKeywords = new[]
        {
            "respawn",
            "restore",
            "bring back",
            "revive",
            "make visible",
            "reveal",
            "restore me",
            "back to normal",
            "ressuscitar",
            "restaurar",
            "reaparecer",
            "visível",
        };

        private static readonly string[] DissolvePromptCommands = new[]
        {
            "make me invisible",
            "turn me invisible",
            "turn invisible",
            "hide me",
            "make player invisible",
            "make the player invisible",
            "fade me out",
            "vanish me",
        };

        private static readonly string[] RespawnPromptCommands = new[]
        {
            "respawn me",
            "revive me",
            "bring me back",
            "restore me",
            "make me visible",
            "make me visible again",
            "reveal me",
            "show me again",
        };

        private enum PlayerSpecialEffectMode
        {
            None,
            Dissolve,
            Respawn,
        }

        private static PlayerSpecialEffectMode ResolvePlayerSpecialEffectMode(
            string promptText,
            string responseText,
            List<EffectIntent> intents
        )
        {
            if (
                IntentMatchesKeywords(intents, DissolveKeywords)
                || ContainsAnyKeyword(promptText, DissolvePromptCommands)
                || ContainsAnyKeyword(
                    responseText,
                    new[] { "make you invisible", "you are invisible" }
                )
            )
            {
                return PlayerSpecialEffectMode.Dissolve;
            }

            if (
                IntentMatchesKeywords(intents, RespawnKeywords)
                || ContainsAnyKeyword(promptText, RespawnPromptCommands)
                || ContainsAnyKeyword(
                    responseText,
                    new[] { "make you visible", "you are visible again" }
                )
            )
            {
                return PlayerSpecialEffectMode.Respawn;
            }

            return PlayerSpecialEffectMode.None;
        }

        private static bool IntentMatchesKeywords(List<EffectIntent> intents, string[] keywords)
        {
            if (intents == null || intents.Count == 0 || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < intents.Count; i++)
            {
                EffectIntent intent = intents[i];
                if (intent == null)
                {
                    continue;
                }

                if (
                    ContainsAnyKeyword(intent.rawTagName, keywords)
                    || ContainsAnyKeyword(intent.target, keywords)
                    || ContainsAnyKeyword(intent.anchor, keywords)
                )
                {
                    return true;
                }
            }

            return false;
        }

        private bool ApplyPlayerSpecialEffects(
            DialogueRequest request,
            ParticleParameterExtractor.ParticleParameterIntent parameterIntent,
            PlayerSpecialEffectMode mode
        )
        {
            if (mode == PlayerSpecialEffectMode.None)
            {
                return false;
            }

            // Get the player NetworkObjectId from the request
            ulong playerNetworkId = ResolvePlayerNetworkIdForRequest(request);
            if (playerNetworkId == 0)
            {
                // Safe fallback only when exactly one player can be resolved.
                TryResolveSingleConnectedPlayerFallback(out playerNetworkId);
            }

            if (playerNetworkId == 0)
            {
                NGLog.Debug(
                    "DialogueFX",
                    "Skip player special effects - no unambiguous player target found"
                );
                return false;
            }

            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                NGLog.Warn("DialogueFX", "Skip player special effects - controller missing");
                return false;
            }

            if (mode == PlayerSpecialEffectMode.Dissolve)
            {
                float durationSeconds = 5f;
                if (parameterIntent.HasExplicitDurationSeconds)
                {
                    durationSeconds = Mathf.Clamp(parameterIntent.ExplicitDurationSeconds, 1f, 30f);
                }
                else if (parameterIntent.DurationMultiplier != 1f)
                {
                    durationSeconds = Mathf.Clamp(parameterIntent.DurationMultiplier * 5f, 1f, 30f);
                }

                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "DialogueFX",
                        NGLog.Format(
                            "Player special effect request",
                            ("mode", "dissolve"),
                            ("player", playerNetworkId),
                            ("speaker", request.SpeakerNetworkId),
                            ("listener", request.ListenerNetworkId),
                            ("duration", durationSeconds),
                            ("explicitDuration", parameterIntent.HasExplicitDurationSeconds),
                            ("intensityMul", parameterIntent.IntensityMultiplier)
                        )
                    );
                }

                try
                {
                    ApplyDissolveEffectClientRpc(playerNetworkId, durationSeconds);
                }
                catch (Exception ex)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "Dissolve RPC failed; attempting local fallback",
                            ("player", playerNetworkId),
                            ("duration", durationSeconds),
                            ("error", ex.Message ?? string.Empty)
                        )
                    );
                    Debug.LogException(ex);
                    m_SceneEffectsController?.ApplyDissolveEffect(playerNetworkId, durationSeconds);
                }
                NGLog.Info(
                    "DialogueFX",
                    NGLog.Format(
                        "Player dissolve effect",
                        ("player", playerNetworkId),
                        ("duration", durationSeconds)
                    )
                );
                return true;
            }

            if (mode == PlayerSpecialEffectMode.Respawn)
            {
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "DialogueFX",
                        NGLog.Format(
                            "Player special effect request",
                            ("mode", "respawn"),
                            ("player", playerNetworkId),
                            ("speaker", request.SpeakerNetworkId),
                            ("listener", request.ListenerNetworkId)
                        )
                    );
                }

                try
                {
                    ApplyRespawnEffectClientRpc(playerNetworkId);
                }
                catch (Exception ex)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "Respawn RPC failed; attempting local fallback",
                            ("player", playerNetworkId),
                            ("error", ex.Message ?? string.Empty)
                        )
                    );
                    Debug.LogException(ex);
                    m_SceneEffectsController?.ApplyRespawnEffect(playerNetworkId);
                }
                NGLog.Info(
                    "DialogueFX",
                    NGLog.Format("Player respawn effect", ("player", playerNetworkId))
                );
                return true;
            }

            return false;
        }

        private void EnsureSceneEffectsController()
        {
            if (m_SceneEffectsController != null)
            {
                return;
            }

#if UNITY_2023_1_OR_NEWER
            m_SceneEffectsController = FindAnyObjectByType<DialogueSceneEffectsController>();
#else
            m_SceneEffectsController = FindObjectOfType<DialogueSceneEffectsController>();
#endif

            if (m_SceneEffectsController == null)
            {
                var go = new GameObject("DialogueSceneEffectsController");
                m_SceneEffectsController = go.AddComponent<DialogueSceneEffectsController>();
            }
        }

        private EffectCatalog EnsureEffectCatalog()
        {
            if (m_EffectCatalogLoaded)
                return m_EffectCatalog;

            m_EffectCatalogLoaded = true;
            try
            {
                m_EffectCatalog = EffectCatalog.Load();
            }
            catch (System.Exception ex)
            {
                NGLog.Warn("DialogueFX", $"EffectCatalog.Load() failed: {ex.Message}");
                m_EffectCatalog = null;
            }

            if (m_EffectCatalog != null)
            {
                RegisterAllProfilePowers(m_EffectCatalog);

                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "DialogueFX",
                        $"EffectCatalog loaded with {m_EffectCatalog.allEffects.Count} definitions."
                    );
                }
            }

            return m_EffectCatalog;
        }

        private void RegisterAllProfilePowers(EffectCatalog catalog)
        {
            NpcDialogueActor[] actors = FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
            int registeredCount = 0;
            foreach (NpcDialogueActor actor in actors)
            {
                if (actor?.Profile?.PrefabPowers == null)
                    continue;
                foreach (PrefabPowerEntry entry in actor.Profile.PrefabPowers)
                {
                    if (
                        entry == null
                        || !entry.Enabled
                        || string.IsNullOrWhiteSpace(entry.PowerName)
                    )
                        continue;
                    catalog.RegisterRuntimeEffect(entry.ToEffectDefinition());
                    registeredCount++;
                }
            }
            if (m_LogDebug)
            {
                NGLog.Debug(
                    "DialogueFX",
                    $"Registered {registeredCount} profile powers with EffectCatalog."
                );
            }
        }

        private void AdjustIntentsForProbeMode(
            DialogueRequest request,
            EffectCatalog catalog,
            ref List<EffectIntent> catalogIntents,
            ref bool hasCatalogIntents
        )
        {
            // Probe mode is deterministic: execute the step tag from the probe prompt
            // rather than whatever tag the NPC response emits.
            List<EffectIntent> requestedIntents = EffectParser.ExtractIntents(
                request.Prompt,
                catalog,
                stripTags: false
            );
            if (requestedIntents == null || requestedIntents.Count == 0)
            {
                if (!hasCatalogIntents)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "Probe step had no parsable [EFFECT:] tag",
                            ("requestId", request.ClientRequestId)
                        )
                    );
                }
                return;
            }

            requestedIntents = requestedIntents
                .Where(intent =>
                    intent != null && !LooksLikePlaceholderEffectTag(intent.rawTagName)
                )
                .ToList();

            if (requestedIntents.Count == 0)
            {
                NGLog.Info(
                    "DialogueFX",
                    NGLog.Format(
                        "Probe intents filtered out (placeholder/example tags)",
                        ("requestId", request.ClientRequestId)
                    )
                );
                return;
            }

            if (requestedIntents.Count > 1)
            {
                EffectIntent selected =
                    requestedIntents.FirstOrDefault(intent =>
                        intent != null && !LooksLikePlaceholderEffectTag(intent.rawTagName)
                    ) ?? requestedIntents[0];
                requestedIntents = new List<EffectIntent>(1) { selected };
            }

            catalogIntents = requestedIntents;
            hasCatalogIntents = true;

            if (m_LogDebug)
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Probe intent selected",
                        ("requestId", request.ClientRequestId),
                        ("tag", requestedIntents[0].rawTagName ?? string.Empty),
                        ("valid", requestedIntents[0].isValid),
                        (
                            "definition",
                            requestedIntents[0].definition != null
                                ? requestedIntents[0].definition.effectTag
                                : "<none>"
                        )
                    )
                );
            }
        }

        private void ApplyEffectParserIntents(
            List<EffectIntent> intents,
            DialogueRequest request,
            Vector3 effectOrigin,
            Vector3 effectForward,
            PlayerEffectModifier? playerModOverride = null,
            bool probeTrace = false
        )
        {
            PlayerEffectModifier playerMod = playerModOverride ?? PlayerEffectModifier.Neutral;
            for (int i = 0; i < intents.Count; i++)
            {
                EffectIntent intent = intents[i];
                if (probeTrace)
                {
                    NGLog.Info(
                        "DialogueFX",
                        NGLog.Format(
                            "Probe intent evaluate",
                            ("requestId", request.ClientRequestId),
                            ("index", i),
                            ("tag", intent.rawTagName ?? string.Empty),
                            ("valid", intent.isValid),
                            (
                                "definition",
                                intent.definition != null ? intent.definition.effectTag : "<none>"
                            ),
                            ("target", intent.target ?? string.Empty),
                            ("placement", intent.placementType ?? string.Empty)
                        )
                    );
                }

                if (!intent.isValid)
                {
                    // Tag not in catalog — skip. Register profile powers as EffectDefinitions in the catalog.
                    if (m_LogDebug)
                    {
                        NGLog.Debug(
                            "DialogueFX",
                            $"[EffectParser] Skipping unresolved intent '{intent.rawTagName}'."
                        );
                    }
                    continue;
                }

                EffectDefinition def = intent.definition;

                string prefabName =
                    def.effectPrefab != null ? def.effectPrefab.name : def.effectTag;
                float intensity = Mathf.Clamp(
                    intent.intensity > 0f ? intent.intensity : 1f,
                    0.1f,
                    4f
                );
                float intensityScale = Mathf.Clamp(Mathf.Pow(intensity, 0.5f), 0.5f, 2.25f);
                float scale = Mathf.Clamp(intent.GetEffectiveScale() * intensityScale, 0.1f, 50f);
                float duration = Mathf.Clamp(intent.GetEffectiveDuration(), 0.1f, 45f);
                Color color = intent.GetEffectiveColor();
                bool useColorOverride = color != Color.white && color != default;
                ulong targetNetworkObjectId = request.ListenerNetworkId;
                float projectileSpeed = Mathf.Clamp(
                    Mathf.Max(0.1f, intent.GetEffectiveSpeed()),
                    0.1f,
                    120f
                );
                float damageRadius = Mathf.Clamp(
                    Mathf.Max(0.1f, intent.GetEffectiveRadius()),
                    0.1f,
                    40f
                );
                float damageAmount = Mathf.Clamp(
                    Mathf.Max(0f, def.damageAmount) * intensity,
                    0f,
                    400f
                );

                // Apply LLM-supplied emotion multiplier to damage and scale
                if (!string.IsNullOrWhiteSpace(intent.emotion))
                {
                    float emotionMul = ParticleParameterExtractor.EmotionKeywordToMultiplier(
                        intent.emotion
                    );
                    if (!Mathf.Approximately(emotionMul, 1f))
                    {
                        scale = Mathf.Clamp(scale * Mathf.Max(1f, emotionMul), 0.1f, 50f);
                        damageAmount = Mathf.Clamp(
                            damageAmount * Mathf.Max(1f, emotionMul),
                            0f,
                            400f
                        );
                    }
                }

                // Apply LLM-supplied damage multiplier when explicitly set
                if (intent.damage > 0f)
                {
                    damageAmount = Mathf.Clamp(damageAmount * intent.damage, 0f, 400f);
                }

                string requestedTarget = !string.IsNullOrWhiteSpace(intent.anchor)
                    ? intent.anchor
                    : intent.target;
                requestedTarget = ResolveTargetHintForDefinition(requestedTarget, def);
                string resolvedPlacementHint = ResolvePlacementHintForDefinition(
                    intent.placementType,
                    def
                );

                // Apply player-specific modifiers from customization (server-authoritative)
                scale = Mathf.Clamp(scale * playerMod.EffectSizeScale, 0.1f, 50f);
                duration = Mathf.Clamp(duration * playerMod.EffectDurationScale, 0.1f, 45f);
                damageAmount = Mathf.Clamp(
                    damageAmount * playerMod.DamageScaleReceived * playerMod.AggressionBias,
                    0f,
                    400f
                );
                if (playerMod.IsShielded)
                {
                    damageAmount = 0f;
                }
                if (!useColorOverride && playerMod.PreferredColor.HasValue)
                {
                    color = playerMod.PreferredColor.Value;
                    useColorOverride = true;
                }

                scale = Mathf.Clamp(scale, def.minScale, def.maxScale);
                duration = Mathf.Clamp(duration, def.minDuration, def.maxDuration);
                damageRadius = Mathf.Clamp(damageRadius, def.minRadius, def.maxRadius);

                Vector3 spawnForward = effectForward;
                Vector3 spawnPos = effectOrigin + effectForward * 1.5f;
                GameObject resolvedTargetObject = ResolveSpawnedObject(targetNetworkObjectId);
                if (
                    TryResolveEffectIntentTarget(
                        requestedTarget,
                        request,
                        effectOrigin,
                        effectForward,
                        out ulong resolvedTargetNetworkObjectId,
                        out Vector3 resolvedSpawnPos,
                        out Vector3 resolvedSpawnForward,
                        out GameObject resolvedTargetGameObject
                    )
                )
                {
                    targetNetworkObjectId = resolvedTargetNetworkObjectId;
                    spawnPos = resolvedSpawnPos;
                    spawnForward = resolvedSpawnForward;
                    resolvedTargetObject = resolvedTargetGameObject;
                }

                EffectSpatialType intentSpatialType = ResolveEffectSpatialType(
                    resolvedPlacementHint,
                    intent.rawTagName,
                    requestedTarget,
                    def.enableHoming,
                    projectileSpeed,
                    damageRadius,
                    scale,
                    def.affectPlayerOnly
                );
                EffectSpatialPolicy intentSpatialPolicy = BuildEffectSpatialPolicy(
                    intentSpatialType,
                    def.enableGameplayDamage,
                    intent.collisionPolicy,
                    intent.groundSnap,
                    intent.requireLineOfSight
                );
                DialogueEffectSpatialResolver.ResolveResult spatial =
                    ResolveSpatialPlacementForPower(
                        intent.rawTagName,
                        request,
                        spawnPos,
                        spawnForward,
                        effectOrigin,
                        effectForward,
                        scale,
                        damageRadius,
                        intentSpatialPolicy,
                        enableGameplayDamage: def.enableGameplayDamage,
                        targetObject: resolvedTargetObject
                    );
                if (!spatial.IsValid)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "EffectParser intent skipped (spatial invalid)",
                            ("tag", intent.rawTagName),
                            ("reason", spatial.Reason ?? "invalid")
                        )
                    );
                    if (probeTrace)
                    {
                        NGLog.Warn(
                            "DialogueFX",
                            NGLog.Format(
                                "Probe catalog intent skipped",
                                ("requestId", request.ClientRequestId),
                                ("tag", intent.rawTagName ?? string.Empty),
                                ("reason", spatial.Reason ?? "invalid")
                            )
                        );
                    }
                    continue;
                }

                LogEffectTargetResolution(
                    "catalog_intent",
                    intent.rawTagName,
                    requestedTarget,
                    targetNetworkObjectId,
                    resolvedTargetObject,
                    request.SpeakerNetworkId
                );

                ApplyPrefabPowerEffectClientRpc(
                    prefabName,
                    spatial.Position,
                    spatial.Forward,
                    scale,
                    duration,
                    new Vector4(color.r, color.g, color.b, color.a),
                    useColorOverride,
                    def.enableGameplayDamage,
                    def.enableHoming,
                    projectileSpeed,
                    def.homingTurnRateDegrees,
                    damageAmount,
                    damageRadius,
                    def.affectPlayerOnly,
                    def.damageType ?? "effect",
                    targetNetworkObjectId,
                    request.SpeakerNetworkId,
                    attachToTarget: intentSpatialType == EffectSpatialType.Attached,
                    fitToTargetMesh: intentSpatialType == EffectSpatialType.Attached
                        && def.preferFitTargetMesh,
                    serverSpawnTimeSeconds: ResolveServerEffectTimeSeconds(),
                    effectSeed: ResolveEffectSeed()
                );

                NGLog.Info(
                    "DialogueFX",
                    NGLog.Format(
                        "EffectParser dispatch",
                        ("tag", intent.rawTagName),
                        ("prefab", prefabName),
                        ("scale", scale),
                        ("duration", duration),
                        ("intensity", intensity),
                        ("radius", damageRadius),
                        ("speed", projectileSpeed),
                        ("target_raw", requestedTarget ?? string.Empty),
                        ("speaker", request.SpeakerNetworkId),
                        ("target", targetNetworkObjectId),
                        ("gameplay", def.enableGameplayDamage),
                        ("homing", def.enableHoming),
                        ("spatialType", intentSpatialType.ToString()),
                        ("spatial", spatial.Reason ?? "ok")
                    )
                );
                if (probeTrace)
                {
                    NGLog.Info(
                        "DialogueFX",
                        NGLog.Format(
                            "Probe catalog intent dispatch",
                            ("requestId", request.ClientRequestId),
                            ("tag", intent.rawTagName ?? string.Empty),
                            ("prefab", prefabName ?? string.Empty),
                            ("target", targetNetworkObjectId)
                        )
                    );
                }
            }
        }

        private void LogEffectTargetResolution(
            string flow,
            string tag,
            string requestedTarget,
            ulong resolvedTargetNetworkObjectId,
            GameObject resolvedTargetObject,
            ulong speakerNetworkObjectId
        )
        {
            if (!m_LogDebug)
            {
                return;
            }

            NGLog.Debug(
                "DialogueFX",
                NGLog.Format(
                    "Effect target resolution",
                    ("flow", flow ?? string.Empty),
                    ("tag", tag ?? string.Empty),
                    ("requested", requestedTarget ?? string.Empty),
                    ("speaker", speakerNetworkObjectId),
                    ("resolvedId", resolvedTargetNetworkObjectId),
                    (
                        "resolvedObject",
                        resolvedTargetObject != null ? resolvedTargetObject.name : "<null>"
                    )
                )
            );
        }

        private static bool LooksLikePlaceholderEffectTag(string rawTagName)
        {
            if (string.IsNullOrWhiteSpace(rawTagName))
            {
                return true;
            }

            string trimmed = rawTagName.Trim();
            if (trimmed == "..." || trimmed == "…")
            {
                return true;
            }

            string lower = trimmed.ToLowerInvariant();
            return lower.Contains("...")
                || lower.Contains("effectname")
                || lower.Contains("your_effect")
                || lower.Contains("example");
        }

        private enum EffectSpatialType
        {
            Ambient,
            Projectile,
            Area,
            Attached,
        }

        private struct EffectSpatialPolicy
        {
            public EffectSpatialType Type;
            public bool GroundSnap;
            public bool RequireLineOfSight;
            public string CollisionPolicyHint;
            public float ClearanceScale;
            public bool UseFallback;
            public float FallbackDistance;
            public bool PreferTargetOrigin;
            public float TargetHeightOffset;
        }

        private static bool TryParseEffectSpatialTypeHint(
            string placementHint,
            out EffectSpatialType type
        )
        {
            type = EffectSpatialType.Ambient;
            if (string.IsNullOrWhiteSpace(placementHint))
            {
                return false;
            }

            string normalized = placementHint
                .Trim()
                .Replace("-", "_")
                .Replace(" ", "_")
                .ToLowerInvariant();
            switch (normalized)
            {
                case "projectile":
                case "bolt":
                case "missile":
                case "shot":
                    type = EffectSpatialType.Projectile;
                    return true;
                case "area":
                case "aoe":
                case "explosion":
                case "blast":
                    type = EffectSpatialType.Area;
                    return true;
                case "attached":
                case "attach":
                case "self":
                case "aura":
                    type = EffectSpatialType.Attached;
                    return true;
                case "ambient":
                case "world":
                case "scene":
                    type = EffectSpatialType.Ambient;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TextHasToken(string source, string token)
        {
            return !string.IsNullOrWhiteSpace(source)
                && !string.IsNullOrWhiteSpace(token)
                && source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolvePlacementHintForDefinition(
            string parserPlacementHint,
            EffectDefinition definition
        )
        {
            if (!string.IsNullOrWhiteSpace(parserPlacementHint))
            {
                return parserPlacementHint;
            }

            if (definition == null)
            {
                return parserPlacementHint;
            }

            switch (definition.placementMode)
            {
                case EffectPlacementMode.AttachMesh:
                    return "attached";
                case EffectPlacementMode.GroundAoe:
                    return "area";
                case EffectPlacementMode.SkyVolume:
                    return "ambient";
                case EffectPlacementMode.Projectile:
                    return "projectile";
                default:
                    return parserPlacementHint;
            }
        }

        private static string ResolveTargetHintForDefinition(
            string parserTargetHint,
            EffectDefinition definition
        )
        {
            if (definition == null || definition.targetType == EffectTargetType.Auto)
            {
                return parserTargetHint;
            }

            switch (definition.targetType)
            {
                case EffectTargetType.Player:
                    return "player";
                case EffectTargetType.Floor:
                    return "ground";
                case EffectTargetType.Npc:
                    return "npc";
                case EffectTargetType.WorldPoint:
                    return "world";
                default:
                    return parserTargetHint;
            }
        }

        private EffectSpatialType ResolveEffectSpatialType(
            string placementHint,
            string effectName,
            string targetHint,
            bool enableHoming,
            float projectileSpeed,
            float damageRadius,
            float scale,
            bool affectPlayerOnly
        )
        {
            if (TryParseEffectSpatialTypeHint(placementHint, out EffectSpatialType hinted))
            {
                return hinted;
            }

            bool likelyProjectile =
                enableHoming
                || projectileSpeed >= 12f
                || TextHasToken(effectName, "projectile")
                || TextHasToken(effectName, "missile")
                || TextHasToken(effectName, "bolt")
                || TextHasToken(effectName, "arrow")
                || TextHasToken(effectName, "lance")
                || TextHasToken(effectName, "orb");

            bool likelyArea =
                damageRadius >= 2.5f
                || scale >= 2.4f
                || TextHasToken(effectName, "explosion")
                || TextHasToken(effectName, "blast")
                || TextHasToken(effectName, "shockwave")
                || TextHasToken(effectName, "nova")
                || TextHasToken(effectName, "storm")
                || TextHasToken(effectName, "rain");

            bool targetIsActor =
                TextHasToken(targetHint, "player")
                || TextHasToken(targetHint, "listener")
                || TextHasToken(targetHint, "self")
                || TextHasToken(targetHint, "caster")
                || TextHasToken(targetHint, "me")
                || TextHasToken(targetHint, "hero")
                || TextHasToken(targetHint, "user");

            if (!likelyProjectile && targetIsActor && (affectPlayerOnly || damageRadius <= 1.25f))
            {
                return EffectSpatialType.Attached;
            }

            if (likelyProjectile)
            {
                return EffectSpatialType.Projectile;
            }

            if (likelyArea)
            {
                return EffectSpatialType.Area;
            }

            return EffectSpatialType.Ambient;
        }

        private EffectSpatialPolicy BuildEffectSpatialPolicy(
            EffectSpatialType type,
            bool enableGameplayDamage,
            string collisionPolicyHintOverride,
            bool? groundSnapOverride = null,
            bool? requireLineOfSightOverride = null
        )
        {
            EffectSpatialPolicy policy = new EffectSpatialPolicy
            {
                Type = type,
                GroundSnap = true,
                RequireLineOfSight = false,
                CollisionPolicyHint = "relaxed",
                ClearanceScale = 0.2f,
                UseFallback = true,
                FallbackDistance = Mathf.Max(0.25f, m_EffectFallbackDistance),
                PreferTargetOrigin = false,
                TargetHeightOffset = 0f,
            };

            switch (type)
            {
                case EffectSpatialType.Projectile:
                    policy.GroundSnap = false;
                    policy.RequireLineOfSight = true;
                    policy.CollisionPolicyHint = "strict";
                    policy.ClearanceScale = 0.12f;
                    policy.UseFallback = false;
                    policy.FallbackDistance = Mathf.Max(0.5f, m_EffectFallbackDistance * 0.75f);
                    break;
                case EffectSpatialType.Area:
                    policy.GroundSnap = true;
                    policy.RequireLineOfSight = enableGameplayDamage;
                    policy.CollisionPolicyHint = enableGameplayDamage ? "strict" : "relaxed";
                    policy.ClearanceScale = 0.28f;
                    policy.UseFallback = true;
                    break;
                case EffectSpatialType.Attached:
                    policy.GroundSnap = false;
                    policy.RequireLineOfSight = false;
                    policy.CollisionPolicyHint = "allow_overlap";
                    policy.ClearanceScale = 0.08f;
                    policy.UseFallback = true;
                    policy.FallbackDistance = Mathf.Max(0.5f, m_EffectFallbackDistance * 0.8f);
                    policy.PreferTargetOrigin = true;
                    policy.TargetHeightOffset = 0.2f;
                    break;
                default:
                    break;
            }

            if (groundSnapOverride.HasValue)
            {
                policy.GroundSnap = groundSnapOverride.Value;
            }

            if (requireLineOfSightOverride.HasValue)
            {
                policy.RequireLineOfSight = requireLineOfSightOverride.Value;
            }

            if (!string.IsNullOrWhiteSpace(collisionPolicyHintOverride))
            {
                policy.CollisionPolicyHint = collisionPolicyHintOverride;
            }

            return policy;
        }

        private DialogueEffectSpatialResolver.CollisionPolicy ResolveCollisionPolicy(
            string collisionPolicyHint,
            bool enableGameplayDamage
        )
        {
            if (
                !string.IsNullOrWhiteSpace(collisionPolicyHint)
                && DialogueEffectSpatialResolver.TryParseCollisionPolicy(
                    collisionPolicyHint,
                    out DialogueEffectSpatialResolver.CollisionPolicy parsed
                )
            )
            {
                return parsed;
            }

            return enableGameplayDamage
                ? DialogueEffectSpatialResolver.CollisionPolicy.Strict
                : DialogueEffectSpatialResolver.CollisionPolicy.Relaxed;
        }

        private DialogueEffectSpatialResolver.ResolveResult ResolveSpatialPlacementForPower(
            string label,
            DialogueRequest request,
            Vector3 desiredPosition,
            Vector3 desiredForward,
            Vector3 fallbackOrigin,
            Vector3 fallbackForward,
            float scale,
            float damageRadius,
            EffectSpatialPolicy policy,
            bool enableGameplayDamage,
            GameObject targetObject
        )
        {
            Vector3 normalizedForward =
                desiredForward.sqrMagnitude > 0.0001f
                    ? desiredForward.normalized
                    : (
                        fallbackForward.sqrMagnitude > 0.0001f
                            ? fallbackForward.normalized
                            : Vector3.forward
                    );

            if (!m_EnableSpatialEffectResolver)
            {
                return new DialogueEffectSpatialResolver.ResolveResult
                {
                    IsValid = true,
                    Position = desiredPosition,
                    Forward = normalizedForward,
                    Reason = "resolver_disabled",
                };
            }

            GameObject speakerObject = ResolveSpawnedObject(request.SpeakerNetworkId);
            GameObject listenerObject = ResolveSpawnedObject(request.ListenerNetworkId);

            if (policy.PreferTargetOrigin && targetObject != null)
            {
                desiredPosition =
                    ResolveEffectOrigin(targetObject) + Vector3.up * policy.TargetHeightOffset;
                normalizedForward = ResolveEffectForward(speakerObject, targetObject);
            }

            Vector3 lineOfSightOrigin =
                speakerObject != null ? ResolveEffectOrigin(speakerObject) : fallbackOrigin;

            float clearance = Mathf.Clamp(
                Mathf.Max(
                    m_EffectCollisionClearanceRadius,
                    scale * Mathf.Max(0.05f, policy.ClearanceScale),
                    damageRadius * Mathf.Max(0.05f, policy.ClearanceScale)
                ),
                0.05f,
                4f
            );

            var resolveRequest = new DialogueEffectSpatialResolver.ResolveRequest
            {
                DesiredPosition = desiredPosition,
                DesiredForward = normalizedForward,
                FallbackOrigin = fallbackOrigin,
                FallbackForward = fallbackForward,
                FallbackForwardDistance = Mathf.Max(0.25f, policy.FallbackDistance),
                UseFallback = policy.UseFallback,
                GroundSnap = policy.GroundSnap,
                GroundProbeUp = Mathf.Max(0.05f, m_EffectGroundProbeUp),
                GroundProbeDown = Mathf.Max(0.05f, m_EffectGroundProbeDown),
                GroundOffset = Mathf.Max(0f, m_EffectGroundOffset),
                GroundMask = m_EffectGroundMask,
                ClearanceRadius = clearance,
                CollisionMask = m_EffectCollisionMask,
                CollisionPolicy = ResolveCollisionPolicy(
                    policy.CollisionPolicyHint,
                    enableGameplayDamage
                ),
                RequireLineOfSight = policy.RequireLineOfSight,
                LineOfSightOrigin = lineOfSightOrigin,
                LineOfSightMask = m_EffectLineOfSightMask,
                IgnoreRootA = speakerObject != null ? speakerObject.transform.root : null,
                IgnoreRootB = listenerObject != null ? listenerObject.transform.root : null,
                IgnoreRootC = targetObject != null ? targetObject.transform.root : null,
            };

            DialogueEffectSpatialResolver.ResolveResult resolved =
                DialogueEffectSpatialResolver.Resolve(resolveRequest);
            if (
                m_LogDebug
                && (resolved.CollisionAdjusted || resolved.UsedFallback || !resolved.IsValid)
            )
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Spatial resolve",
                        ("label", label ?? string.Empty),
                        ("valid", resolved.IsValid),
                        ("reason", resolved.Reason ?? string.Empty),
                        ("fallback", resolved.UsedFallback),
                        ("collisionAdjusted", resolved.CollisionAdjusted),
                        ("groundSnapped", resolved.GroundSnapped),
                        ("x", resolved.Position.x),
                        ("y", resolved.Position.y),
                        ("z", resolved.Position.z)
                    )
                );
            }

            return resolved;
        }

        private bool TryResolveEffectIntentTarget(
            string targetText,
            DialogueRequest request,
            Vector3 fallbackOrigin,
            Vector3 fallbackForward,
            out ulong targetNetworkObjectId,
            out Vector3 spawnPos,
            out Vector3 spawnForward,
            out GameObject resolvedTargetObject
        )
        {
            targetNetworkObjectId = request.ListenerNetworkId;
            spawnPos = fallbackOrigin + fallbackForward * 1.5f;
            spawnForward = fallbackForward;
            resolvedTargetObject = ResolveSpawnedObject(request.ListenerNetworkId);

            if (string.IsNullOrWhiteSpace(targetText))
            {
                return false;
            }

            string cleanedTarget = targetText.Trim().Trim('"', '\'');
            if (cleanedTarget.Length == 0)
            {
                return false;
            }

            string lower = cleanedTarget.ToLowerInvariant();
            string[] prefixes = { "at ", "on ", "near ", "around ", "to ", "target " };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (lower.StartsWith(prefix, StringComparison.Ordinal))
                {
                    cleanedTarget = cleanedTarget.Substring(prefix.Length).Trim();
                    lower = cleanedTarget.ToLowerInvariant();
                    break;
                }
            }

            if (ulong.TryParse(cleanedTarget, out ulong explicitTargetId) && explicitTargetId != 0)
            {
                targetNetworkObjectId = explicitTargetId;
                GameObject explicitTargetObject = ResolveSpawnedObject(explicitTargetId);
                resolvedTargetObject = explicitTargetObject;
                if (explicitTargetObject != null)
                {
                    spawnPos = ResolveEffectOrigin(explicitTargetObject);
                    GameObject speakerObject = ResolveSpawnedObject(request.SpeakerNetworkId);
                    spawnForward = ResolveEffectForward(speakerObject, explicitTargetObject);
                }
                return true;
            }

            // Anchor aliases are emitted by LLMs as "Target: head/feet/ground".
            // Treat them as player-relative placement targets.
            if (IsPlayerHeadAlias(lower) || IsPlayerFeetAlias(lower) || IsGroundAlias(lower))
            {
                ulong resolvedPlayerNetworkId = ResolvePlayerNetworkIdForRequest(request);
                if (resolvedPlayerNetworkId != 0)
                {
                    targetNetworkObjectId = resolvedPlayerNetworkId;
                }
                else
                {
                    targetNetworkObjectId = request.ListenerNetworkId;
                }

                GameObject listenerObject = ResolveSpawnedObject(targetNetworkObjectId);
                GameObject speakerObject = ResolveSpawnedObject(request.SpeakerNetworkId);

                if (IsGroundAlias(lower))
                {
                    GameObject semanticGround =
                        FindSceneObjectByName("role:floor")
                        ?? FindSceneObjectByName("role:terrain")
                        ?? FindSceneObjectByName("ground")
                        ?? FindSceneObjectByName("floor");
                    if (semanticGround != null)
                    {
                        NetworkObject semanticGroundNetworkObject =
                            semanticGround.GetComponentInParent<NetworkObject>();
                        targetNetworkObjectId =
                            semanticGroundNetworkObject != null
                            && semanticGroundNetworkObject.IsSpawned
                                ? semanticGroundNetworkObject.NetworkObjectId
                                : 0UL;
                        resolvedTargetObject = semanticGround;
                        Vector3 groundReference =
                            listenerObject != null
                                ? listenerObject.transform.position
                                : fallbackOrigin;
                        spawnPos = ResolveGroundPlacementNearReference(
                            semanticGround,
                            groundReference
                        );
                        spawnForward = ResolveEffectForward(
                            speakerObject,
                            listenerObject != null ? listenerObject : semanticGround
                        );
                        return true;
                    }
                }

                if (IsGroundAlias(lower))
                {
                    // Ground targets should not remain bound to player network IDs.
                    targetNetworkObjectId = 0UL;
                }
                resolvedTargetObject = listenerObject;
                if (listenerObject != null)
                {
                    Vector3 anchorPosition = ResolveEffectOrigin(listenerObject);
                    if (
                        TryGetObjectBounds(listenerObject, out Bounds listenerBounds)
                        && listenerBounds.size.sqrMagnitude > 0.0001f
                    )
                    {
                        if (IsPlayerHeadAlias(lower))
                        {
                            anchorPosition = new Vector3(
                                listenerBounds.center.x,
                                listenerBounds.max.y + 0.06f,
                                listenerBounds.center.z
                            );
                        }
                        else if (IsPlayerFeetAlias(lower) || IsGroundAlias(lower))
                        {
                            anchorPosition = new Vector3(
                                listenerBounds.center.x,
                                listenerBounds.min.y + 0.03f,
                                listenerBounds.center.z
                            );
                        }
                    }

                    spawnPos = anchorPosition;
                    spawnForward = ResolveEffectForward(speakerObject, listenerObject);
                }
                return true;
            }

            if (IsPlayerTargetToken(lower))
            {
                ulong resolvedPlayerNetworkId = ResolvePlayerNetworkIdForRequest(request);
                if (resolvedPlayerNetworkId != 0)
                {
                    targetNetworkObjectId = resolvedPlayerNetworkId;
                }
                else
                {
                    targetNetworkObjectId = request.ListenerNetworkId;
                }

                GameObject listenerObject = ResolveSpawnedObject(targetNetworkObjectId);
                resolvedTargetObject = listenerObject;
                if (listenerObject != null)
                {
                    spawnPos = ResolveEffectOrigin(listenerObject);
                    GameObject speakerObject = ResolveSpawnedObject(request.SpeakerNetworkId);
                    spawnForward = ResolveEffectForward(speakerObject, listenerObject);
                }
                return true;
            }

            if (lower is "self" or "npc" or "caster" or "speaker" or "enemy" or "boss")
            {
                targetNetworkObjectId = request.SpeakerNetworkId;
                GameObject speakerObject = ResolveSpawnedObject(request.SpeakerNetworkId);
                resolvedTargetObject = speakerObject;
                if (speakerObject != null)
                {
                    spawnPos = ResolveEffectOrigin(speakerObject);
                    spawnForward =
                        speakerObject.transform.forward.sqrMagnitude > 0.0001f
                            ? speakerObject.transform.forward.normalized
                            : fallbackForward;
                }
                return true;
            }

            GameObject objectTarget = FindSceneObjectByName(cleanedTarget);
            if (objectTarget != null)
            {
                resolvedTargetObject = objectTarget;
                spawnPos = ResolveEffectOrigin(objectTarget);
                GameObject speakerObject = ResolveSpawnedObject(request.SpeakerNetworkId);
                spawnForward = ResolveEffectForward(speakerObject, objectTarget);
                var networkObject = objectTarget.GetComponentInParent<NetworkObject>();
                if (networkObject != null)
                {
                    targetNetworkObjectId = networkObject.NetworkObjectId;
                }
                return true;
            }

            return false;
        }

        private static bool IsPlayerTargetToken(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
            {
                return false;
            }

            return lower
                    is "player"
                        or "listener"
                        or "me"
                        or "myself"
                        or "target"
                        or "hero"
                        or "user"
                || lower.Equals("role:player", StringComparison.Ordinal)
                || lower.Equals("semantic:player", StringComparison.Ordinal)
                || lower.StartsWith("id:player", StringComparison.Ordinal)
                || lower.StartsWith("semantic:player:", StringComparison.Ordinal)
                || lower.StartsWith("player:", StringComparison.Ordinal);
        }

        private static bool IsGroundAlias(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
            {
                return false;
            }

            return lower
                    is "ground"
                        or "floor"
                        or "terrain"
                        or "grounded"
                        or "fllor"
                        or "flor"
                        or "grond"
                || lower.Contains("on ground", StringComparison.Ordinal)
                || lower.Contains("at ground", StringComparison.Ordinal)
                || lower.Contains("on floor", StringComparison.Ordinal)
                || lower.Contains("on fllor", StringComparison.Ordinal)
                || lower.Contains("at fllor", StringComparison.Ordinal)
                || lower.Contains("at feet", StringComparison.Ordinal)
                || lower.Contains("under feet", StringComparison.Ordinal);
        }

        private static bool IsPlayerHeadAlias(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
            {
                return false;
            }

            return lower is "head" or "hair" or "face"
                || lower.Contains("player head", StringComparison.Ordinal)
                || lower.Contains("on head", StringComparison.Ordinal)
                || lower.Contains("at head", StringComparison.Ordinal)
                || lower.Contains("player hair", StringComparison.Ordinal);
        }

        private static bool IsPlayerFeetAlias(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
            {
                return false;
            }

            return lower is "feet" or "foot" or "toes" or "legs" or "atfeet"
                || lower.Contains("player feet", StringComparison.Ordinal)
                || lower.Contains("at feet", StringComparison.Ordinal)
                || lower.Contains("on feet", StringComparison.Ordinal)
                || lower.Contains("under player", StringComparison.Ordinal);
        }

        private static bool TryGetObjectBounds(GameObject obj, out Bounds bounds)
        {
            bounds = default;
            if (obj == null)
            {
                return false;
            }

            bool hasBounds = false;
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (collider == null)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = collider.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            return hasBounds;
        }

        private static void LogPrefabPowerSuccess(
            string prompt,
            string response,
            string powerName,
            string prefabName,
            float scale,
            float duration,
            float damage,
            float damageRadius,
            bool gameplay,
            bool homing,
            ulong sourceId,
            ulong targetId
        )
        {
            try
            {
                string logDir = Path.Combine(Application.persistentDataPath, "DialogueLogs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string logPath = Path.Combine(logDir, "prefab_power_success.jsonl");
                string timestamp = DateTime.UtcNow.ToString("o");
                string safePrompt = EscapeJsonString(prompt ?? string.Empty);
                string safeResponse = EscapeJsonString(
                    response != null && response.Length > 300
                        ? response.Substring(0, 300)
                        : response ?? string.Empty
                );
                string safePower = EscapeJsonString(powerName ?? string.Empty);
                string safePrefab = EscapeJsonString(prefabName ?? string.Empty);

                string json = string.Concat(
                    "{\"ts\":\"",
                    timestamp,
                    "\",\"power\":\"",
                    safePower,
                    "\",\"prefab\":\"",
                    safePrefab,
                    "\",\"scale\":",
                    scale.ToString("F2"),
                    ",\"duration\":",
                    duration.ToString("F2"),
                    ",\"damage\":",
                    damage.ToString("F2"),
                    ",\"damageRadius\":",
                    damageRadius.ToString("F2"),
                    ",\"gameplay\":",
                    gameplay ? "true" : "false",
                    ",\"homing\":",
                    homing ? "true" : "false",
                    ",\"source\":",
                    sourceId.ToString(),
                    ",\"target\":",
                    targetId.ToString(),
                    ",\"prompt\":\"",
                    safePrompt,
                    "\",\"response\":\"",
                    safeResponse,
                    "\"}"
                );

                File.AppendAllText(logPath, json + "\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                NGLog.Warn("DialogueFX", $"Failed to log prefab power success: {ex.Message}");
            }
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyBoredLightingClientRpc(
            Vector4 color,
            float intensity,
            float transitionSeconds
        )
        {
            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                return;
            }

            var lightColor = new Color(color.x, color.y, color.z, color.w);
            m_SceneEffectsController.ApplyBoredLighting(lightColor, intensity, transitionSeconds);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyDissolveEffectClientRpc(
            ulong targetNetworkObjectId,
            float durationSeconds
        )
        {
            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                return;
            }

            m_SceneEffectsController.ApplyDissolveEffect(targetNetworkObjectId, durationSeconds);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyRespawnEffectClientRpc(ulong targetNetworkObjectId)
        {
            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                return;
            }

            m_SceneEffectsController.ApplyRespawnEffect(targetNetworkObjectId);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyFloorFreezeSurfaceMaterialClientRpc(
            string surfaceId,
            string rendererHierarchyPath,
            int materialSlotIndex,
            float durationSeconds,
            ulong sourceNetworkObjectId,
            ulong targetNetworkObjectId
        )
        {
            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                return;
            }

            m_SceneEffectsController.ApplyFloorFreezeSurfaceMaterial(
                surfaceId,
                rendererHierarchyPath,
                materialSlotIndex,
                durationSeconds,
                sourceNetworkObjectId,
                targetNetworkObjectId
            );
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyPrefabPowerEffectClientRpc(
            string prefabName,
            Vector3 position,
            Vector3 forward,
            float scale,
            float durationSeconds,
            Vector4 color,
            bool useColorOverride,
            bool enableGameplayDamage,
            bool enableHoming,
            float projectileSpeed,
            float homingTurnRateDegrees,
            float damageAmount,
            float damageRadius,
            bool affectPlayerOnly,
            string damageType,
            ulong targetNetworkObjectId,
            ulong sourceNetworkObjectId,
            bool attachToTarget,
            bool fitToTargetMesh,
            float serverSpawnTimeSeconds,
            uint effectSeed
        )
        {
            EnsureSceneEffectsController();
            if (m_SceneEffectsController == null)
            {
                return;
            }

            var effectColor = new Color(color.x, color.y, color.z, color.w);
            m_SceneEffectsController.ApplyPrefabPower(
                prefabName,
                position,
                forward,
                scale,
                durationSeconds,
                effectColor,
                useColorOverride,
                enableGameplayDamage,
                enableHoming,
                projectileSpeed,
                homingTurnRateDegrees,
                damageAmount,
                damageRadius,
                affectPlayerOnly,
                damageType,
                sourceNetworkObjectId: sourceNetworkObjectId,
                targetNetworkObjectId: targetNetworkObjectId,
                attachToTarget: attachToTarget,
                fitToTargetMesh: fitToTargetMesh,
                serverSpawnTimeSeconds: serverSpawnTimeSeconds,
                effectSeed: effectSeed
            );
        }

        private static float ResolveServerEffectTimeSeconds()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                return Time.time;
            }

            return networkManager.ServerTime.TimeAsFloat;
        }

        private static uint ResolveEffectSeed()
        {
            return (uint)UnityEngine.Random.Range(1, int.MaxValue);
        }

        private static float ResolveExplicitDurationSeconds(
            ParticleParameterExtractor.ParticleParameterIntent parameterIntent,
            float min,
            float max
        )
        {
            if (!parameterIntent.HasExplicitDurationSeconds)
            {
                return -1f;
            }

            return Mathf.Clamp(parameterIntent.ExplicitDurationSeconds, min, max);
        }

        private static void ResolveEffectSpatialContext(
            string effectContext,
            GameObject speakerObject,
            GameObject listenerObject,
            out Vector3 origin,
            out Vector3 forward,
            out string anchorLabel
        )
        {
            origin = ResolveEffectOrigin(speakerObject);
            forward = ResolveEffectForward(speakerObject, listenerObject);
            anchorLabel = "npc";

            if (TryResolveObjectAnchor(effectContext, out GameObject objectAnchor))
            {
                Vector3 objectOrigin = ResolveEffectOrigin(objectAnchor);
                if (speakerObject != null)
                {
                    origin = ResolveEffectOrigin(speakerObject);
                    Vector3 toObject = objectOrigin - origin;
                    toObject.y = 0f;
                    if (toObject.sqrMagnitude > 0.0001f)
                    {
                        forward = toObject.normalized;
                    }
                    else
                    {
                        forward = ResolveEffectForward(speakerObject, objectAnchor);
                    }
                }
                else
                {
                    origin = objectOrigin - objectAnchor.transform.forward * 0.5f;
                    forward =
                        objectAnchor.transform.forward.sqrMagnitude > 0.0001f
                            ? objectAnchor.transform.forward.normalized
                            : Vector3.forward;
                }

                anchorLabel = $"object:{objectAnchor.name}";
                return;
            }

            if (WantsPlayerAnchor(effectContext) && listenerObject != null)
            {
                origin = ResolveEffectOrigin(listenerObject);
                forward = ResolveEffectForward(speakerObject, listenerObject);
                anchorLabel = "player";
            }
        }

        private static Vector3 ResolveEffectOrigin(GameObject speakerObject)
        {
            if (speakerObject == null)
            {
                return Vector3.zero;
            }

            Vector3 origin = speakerObject.transform.position;
            Collider collider = speakerObject.GetComponentInChildren<Collider>();
            if (collider != null)
            {
                origin.y = collider.bounds.center.y;
            }

            return origin;
        }

        private static Vector3 ResolveEffectForward(
            GameObject speakerObject,
            GameObject listenerObject
        )
        {
            if (speakerObject == null)
            {
                return Vector3.forward;
            }

            if (listenerObject != null)
            {
                Vector3 toListener =
                    listenerObject.transform.position - speakerObject.transform.position;
                toListener.y = 0f;
                if (toListener.sqrMagnitude > 0.0001f)
                {
                    return toListener.normalized;
                }
            }

            Vector3 forward = speakerObject.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            return forward.normalized;
        }

        private static bool WantsPlayerAnchor(string effectContext)
        {
            if (string.IsNullOrWhiteSpace(effectContext))
            {
                return false;
            }

            string lower = effectContext.ToLowerInvariant();
            return lower.Contains("at player", StringComparison.Ordinal)
                || lower.Contains("on player", StringComparison.Ordinal)
                || lower.Contains("around player", StringComparison.Ordinal)
                || lower.Contains("player position", StringComparison.Ordinal)
                || lower.Contains("at me", StringComparison.Ordinal)
                || lower.Contains("on me", StringComparison.Ordinal)
                || lower.Contains("around me", StringComparison.Ordinal)
                || lower.Contains("my position", StringComparison.Ordinal);
        }

        private static bool TryResolveObjectAnchor(
            string effectContext,
            out GameObject targetObject
        )
        {
            targetObject = null;
            if (!TryExtractObjectAnchorName(effectContext, out string objectName))
            {
                return false;
            }

            targetObject = FindSceneObjectByName(objectName);
            return targetObject != null;
        }

        private static bool TryExtractObjectAnchorName(
            string effectContext,
            out string objectAnchorName
        )
        {
            objectAnchorName = string.Empty;
            if (string.IsNullOrWhiteSpace(effectContext))
            {
                return false;
            }

            string lower = effectContext.ToLowerInvariant();
            string[] markers =
            {
                "at object ",
                "on object ",
                "near object ",
                "around object ",
                "at the object ",
                "on the object ",
                "near the object ",
                "around the object ",
                "at wall ",
                "on wall ",
                "near wall ",
                "around wall ",
                "at the wall ",
                "on the wall ",
                "near the wall ",
                "around the wall ",
            };

            for (int i = 0; i < markers.Length; i++)
            {
                string marker = markers[i];
                int markerIndex = lower.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    continue;
                }

                int start = markerIndex + marker.Length;
                if (start >= effectContext.Length)
                {
                    continue;
                }

                int end = effectContext.Length;
                for (int c = start; c < effectContext.Length; c++)
                {
                    char ch = effectContext[c];
                    if (
                        ch == '\n'
                        || ch == '\r'
                        || ch == ','
                        || ch == '.'
                        || ch == ';'
                        || ch == '!'
                        || ch == '?'
                    )
                    {
                        end = c;
                        break;
                    }
                }

                string candidate = effectContext.Substring(start, end - start).Trim();
                int connector = candidate.IndexOf(" for ", StringComparison.OrdinalIgnoreCase);
                if (connector >= 0)
                {
                    candidate = candidate.Substring(0, connector).Trim();
                }

                connector = candidate.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
                if (connector >= 0)
                {
                    candidate = candidate.Substring(0, connector).Trim();
                }

                connector = candidate.IndexOf(" and ", StringComparison.OrdinalIgnoreCase);
                if (connector >= 0)
                {
                    candidate = candidate.Substring(0, connector).Trim();
                }

                candidate = candidate.Trim('"', '\'');
                if (candidate.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate.Substring(4).Trim();
                }

                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length >= 2)
                {
                    objectAnchorName = candidate;
                    return true;
                }
            }

            return false;
        }

        private static GameObject FindSceneObjectByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            string trimmed = objectName.Trim();
            if (trimmed.Length < 2)
            {
                return null;
            }

            if (TryFindSceneObjectBySemanticTag(trimmed, out GameObject semanticMatch))
            {
                return semanticMatch;
            }

            GameObject exact = GameObject.Find(trimmed);
            if (exact != null)
            {
                return exact;
            }

#if UNITY_2023_1_OR_NEWER
            Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                findObjectsInactive: FindObjectsInactive.Exclude
            );

#else
            Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>();
#endif

            GameObject partialMatch = null;
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || candidate.gameObject == null)
                {
                    continue;
                }

                string candidateName = candidate.name ?? string.Empty;
                if (candidateName.Length == 0)
                {
                    continue;
                }

                if (string.Equals(candidateName, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.gameObject;
                }

                if (
                    partialMatch == null
                    && candidateName.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0
                )
                {
                    partialMatch = candidate.gameObject;
                }
            }

            return partialMatch;
        }

        private static bool TryFindSceneObjectBySemanticTag(string query, out GameObject target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

#if UNITY_2023_1_OR_NEWER
            DialogueSemanticTag[] tags = UnityEngine.Object.FindObjectsByType<DialogueSemanticTag>(
                findObjectsInactive: FindObjectsInactive.Exclude
            );

#else
            DialogueSemanticTag[] tags =
                UnityEngine.Object.FindObjectsOfType<DialogueSemanticTag>();
#endif
            if (tags == null || tags.Length == 0)
            {
                return false;
            }

            string raw = query.Trim().Trim('"', '\'');
            if (raw.Length == 0)
            {
                return false;
            }

            string lower = raw.ToLowerInvariant();
            bool useRoleFilter = lower.StartsWith("role:", StringComparison.Ordinal);
            bool useIdFilter =
                lower.StartsWith("id:", StringComparison.Ordinal)
                || lower.StartsWith("semantic:", StringComparison.Ordinal);
            string filterValue = raw;
            if (useRoleFilter)
            {
                filterValue = raw.Substring("role:".Length).Trim();
            }
            else if (lower.StartsWith("id:", StringComparison.Ordinal))
            {
                filterValue = raw.Substring("id:".Length).Trim();
            }
            else if (lower.StartsWith("semantic:", StringComparison.Ordinal))
            {
                filterValue = raw.Substring("semantic:".Length).Trim();
            }

            if (filterValue.Length == 0)
            {
                return false;
            }

            string filterLower = filterValue.ToLowerInvariant();
            string filterNorm = NormalizeSemanticToken(filterLower);

            int bestScore = int.MinValue;
            DialogueSemanticTag bestTag = null;
            for (int i = 0; i < tags.Length; i++)
            {
                DialogueSemanticTag tag = tags[i];
                if (tag == null || tag.gameObject == null)
                {
                    continue;
                }

                int score = ScoreSemanticTagMatch(
                    tag,
                    raw,
                    lower,
                    filterLower,
                    filterNorm,
                    useRoleFilter,
                    useIdFilter
                );
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTag = tag;
                }
            }

            if (bestTag == null || bestScore <= 0)
            {
                return false;
            }

            target = bestTag.gameObject;
            return true;
        }

        private static int ScoreSemanticTagMatch(
            DialogueSemanticTag tag,
            string rawQuery,
            string lowerQuery,
            string filterLower,
            string filterNorm,
            bool useRoleFilter,
            bool useIdFilter
        )
        {
            string semanticId = tag.SemanticId ?? string.Empty;
            string semanticIdLower = semanticId.ToLowerInvariant();
            string semanticIdNorm = NormalizeSemanticToken(semanticIdLower);
            string display = tag.ResolveDisplayName(tag.gameObject) ?? string.Empty;
            string displayLower = display.ToLowerInvariant();
            string displayNorm = NormalizeSemanticToken(displayLower);
            string role = tag.RoleKey ?? string.Empty;
            string roleNorm = NormalizeSemanticToken(role);

            if (useRoleFilter && role != filterLower && roleNorm != filterNorm)
            {
                return 0;
            }

            if (useIdFilter && semanticIdLower != filterLower && semanticIdNorm != filterNorm)
            {
                return 0;
            }

            int score = 0;
            if (semanticIdLower == filterLower)
            {
                score = Mathf.Max(score, 320);
            }
            else if (semanticIdNorm.Length > 0 && semanticIdNorm == filterNorm)
            {
                score = Mathf.Max(score, 300);
            }

            if (displayLower == lowerQuery || displayLower == filterLower)
            {
                score = Mathf.Max(score, 260);
            }
            else if (
                displayNorm.Length > 0
                && (displayNorm == NormalizeSemanticToken(lowerQuery) || displayNorm == filterNorm)
            )
            {
                score = Mathf.Max(score, 235);
            }
            else if (displayLower.IndexOf(filterLower, StringComparison.Ordinal) >= 0)
            {
                score = Mathf.Max(score, 150);
            }

            if (role == filterLower || roleNorm == filterNorm)
            {
                score = Mathf.Max(score, 180);
            }

            string[] aliases = tag.Aliases;
            if (aliases != null)
            {
                for (int i = 0; i < aliases.Length; i++)
                {
                    string alias = aliases[i];
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    string aliasLower = alias.Trim().ToLowerInvariant();
                    string aliasNorm = NormalizeSemanticToken(aliasLower);
                    if (aliasLower == lowerQuery || aliasLower == filterLower)
                    {
                        score = Mathf.Max(score, 240);
                        break;
                    }

                    if (
                        aliasNorm.Length > 0
                        && (
                            aliasNorm == NormalizeSemanticToken(lowerQuery)
                            || aliasNorm == filterNorm
                        )
                    )
                    {
                        score = Mathf.Max(score, 220);
                        break;
                    }
                }
            }

            return score;
        }

        private static string NormalizeSemanticToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(token.Length);
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        private static bool ContainsAnyKeyword(string source, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(source) || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            string lower = source.ToLowerInvariant();
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (lower.Contains(keyword.Trim().ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPrefabPowerEffects(
            NpcDialogueProfile profile,
            string effectContext,
            out List<PrefabPowerEffect> effects
        )
        {
            effects = null;
            PrefabPowerEntry[] powers = profile != null ? profile.PrefabPowers : null;
            if (powers == null || powers.Length == 0)
            {
                return false;
            }

            bool wantsGroundFreeze = IsGroundFreezePrompt(effectContext);
            for (int i = 0; i < powers.Length; i++)
            {
                PrefabPowerEntry entry = powers[i];
                if (entry == null || !entry.Enabled || entry.EffectPrefab == null)
                {
                    continue;
                }

                if (wantsGroundFreeze && !LooksLikeGroundFreezePower(entry))
                {
                    continue;
                }

                if (
                    !ContainsAnyKeyword(effectContext, entry.Keywords)
                    && !ContainsAnyKeyword(effectContext, entry.CreativeTriggers)
                    && !(wantsGroundFreeze && LooksLikeGroundFreezePower(entry))
                )
                {
                    continue;
                }

                if (effects == null)
                {
                    effects = new List<PrefabPowerEffect>(4);
                }

                effects.Add(
                    new PrefabPowerEffect
                    {
                        PrefabName = entry.EffectPrefab.name,
                        DurationSeconds = Mathf.Max(0.5f, entry.DurationSeconds),
                        Scale = Mathf.Max(0.1f, entry.Scale),
                        SpawnOffset = entry.SpawnOffset,
                        SpawnInFront = entry.SpawnInFrontOfNpc,
                        ForwardDistance = Mathf.Max(0f, entry.ForwardDistance),
                        Color = entry.ColorOverride,
                        UseColorOverride = entry.UseColorOverride,
                        PowerName = entry.PowerName ?? string.Empty,
                        EnableGameplayDamage = entry.EnableGameplayDamage,
                        EnableHoming = entry.EnableHoming,
                        ProjectileSpeed = Mathf.Max(0.1f, entry.ProjectileSpeed),
                        HomingTurnRateDegrees = Mathf.Max(0f, entry.HomingTurnRateDegrees),
                        DamageAmount = Mathf.Max(0f, entry.DamageAmount),
                        DamageRadius = Mathf.Max(0.1f, entry.DamageRadius),
                        AffectPlayerOnly = entry.AffectPlayerOnly,
                        DamageType = string.IsNullOrWhiteSpace(entry.DamageType)
                            ? "effect"
                            : entry.DamageType.Trim(),
                        TargetNetworkObjectId = 0,
                    }
                );
            }

            return effects != null && effects.Count > 0;
        }

        private static readonly string[] GroundFreezeKeywords = new[]
        {
            "freeze",
            "freez",
            "freexe",
            "frezze",
            "frozen",
            "frost",
            "ice",
            "glacial",
            "icy",
            "freeze solid",
            "congelar",
            "gelar",
            "gelo",
            "frozen ground",
        };

        private static readonly string[] GroundSurfaceKeywords = new[]
        {
            "ground",
            "grond",
            "grnd",
            "floor",
            "fllor",
            "flor",
            "terrain",
            "arena",
            "soil",
            "field",
            "land",
            "chao",
            "chão",
            "piso",
            "solo",
        };

        private static readonly string[] GroundFreezePreferredTags = new[]
        {
            "Ground Fog",
            "GroundFog",
            "Ground Freeze",
            "Frost Field",
            "Ice Field",
            "Ground Burst",
            "Ground Break",
        };

        private static bool IsGroundFreezePrompt(string prompt)
        {
            return ContainsAnyKeyword(prompt, GroundFreezeKeywords)
                && ContainsAnyKeyword(prompt, GroundSurfaceKeywords);
        }

        private static PrefabPowerEntry PickPromptMatchedPower(
            NpcDialogueProfile profile,
            string promptContext,
            string preferredElement = null,
            float aggressionBias = 1f
        )
        {
            if (profile == null)
            {
                return null;
            }

            if (IsGroundFreezePrompt(promptContext))
            {
                PrefabPowerEntry groundFreeze = TryFindGroundFreezeProfilePower(profile);
                if (groundFreeze != null)
                {
                    return groundFreeze;
                }
            }

            PrefabPowerEntry[] powers = profile.PrefabPowers;
            if (powers == null || powers.Length == 0)
            {
                return null;
            }

            string context = string.IsNullOrWhiteSpace(promptContext)
                ? string.Empty
                : promptContext.Trim();
            string lowerContext = context.ToLowerInvariant();

            PrefabPowerEntry best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < powers.Length; i++)
            {
                PrefabPowerEntry entry = powers[i];
                if (entry == null || !entry.Enabled || entry.EffectPrefab == null)
                {
                    continue;
                }

                int score = 0;
                if (ContainsAnyKeyword(context, entry.Keywords))
                {
                    score += 120;
                }

                if (ContainsAnyKeyword(context, entry.CreativeTriggers))
                {
                    score += 80;
                }

                string powerName = entry.PowerName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(powerName))
                {
                    string lowerPowerName = powerName.Trim().ToLowerInvariant();
                    if (lowerContext.Contains(lowerPowerName))
                    {
                        score += 60;
                    }
                }

                if (
                    !string.IsNullOrWhiteSpace(preferredElement)
                    && !string.IsNullOrWhiteSpace(entry.Element)
                    && string.Equals(
                        preferredElement,
                        entry.Element,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    score += 15;
                }

                // Bias toward or away from combat powers based on NPC aggression toward this player
                if (aggressionBias > 1.15f && IsCombatPowerKeywords(entry))
                    score += Mathf.RoundToInt((aggressionBias - 1f) * 60f);
                else if (aggressionBias < 0.85f && IsCombatPowerKeywords(entry))
                    score -= Mathf.RoundToInt((1f - aggressionBias) * 40f);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }

            if (best != null && bestScore > 0)
            {
                return best;
            }

            return PickRandomEnabledPower(profile, preferredElement);
        }

        private static bool LooksLikeFreezeIntent(EffectIntent intent, EffectDefinition definition)
        {
            if (intent == null)
            {
                return false;
            }

            string raw = intent.rawTagName ?? string.Empty;
            string target = intent.target ?? string.Empty;
            string anchor = intent.anchor ?? string.Empty;
            string tag = definition != null ? definition.effectTag : string.Empty;

            if (
                ContainsAnyKeyword(raw, GroundFreezeKeywords)
                || ContainsAnyKeyword(tag, GroundFreezeKeywords)
                || ContainsAnyKeyword(target, GroundFreezeKeywords)
                || ContainsAnyKeyword(anchor, GroundFreezeKeywords)
            )
            {
                return true;
            }

            if (definition?.alternativeTags != null)
            {
                for (int i = 0; i < definition.alternativeTags.Length; i++)
                {
                    string alt = definition.alternativeTags[i];
                    if (ContainsAnyKeyword(alt, GroundFreezeKeywords))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool LooksProjectileLike(EffectIntent intent, EffectDefinition definition)
        {
            if (
                intent != null
                && !string.IsNullOrWhiteSpace(intent.placementType)
                && intent.placementType.IndexOf("projectile", StringComparison.OrdinalIgnoreCase)
                    >= 0
            )
            {
                return true;
            }

            if (definition == null)
            {
                return false;
            }

            if (definition.enableHoming || definition.projectileSpeed >= 8f)
            {
                return true;
            }

            string lowerTag = (definition.effectTag ?? string.Empty).ToLowerInvariant();
            return lowerTag.Contains("lance")
                || lowerTag.Contains("bolt")
                || lowerTag.Contains("missile")
                || lowerTag.Contains("projectile")
                || lowerTag.Contains("arrow")
                || lowerTag.Contains("fireball");
        }

        private static bool ShouldForceGroundFreezeForIntent(
            DialogueRequest request,
            EffectIntent intent,
            EffectDefinition definition
        )
        {
            if (intent == null)
            {
                return false;
            }

            bool promptGroundFreeze = IsGroundFreezePrompt(request.Prompt);
            string requestedTarget = !string.IsNullOrWhiteSpace(intent.anchor)
                ? intent.anchor
                : intent.target;
            string requestedTargetLower = string.IsNullOrWhiteSpace(requestedTarget)
                ? string.Empty
                : requestedTarget.Trim().ToLowerInvariant();
            bool explicitGroundTarget = IsGroundAlias(requestedTargetLower);
            bool freezeIntent = LooksLikeFreezeIntent(intent, definition);

            if (!freezeIntent)
            {
                return false;
            }

            if (promptGroundFreeze)
            {
                return true;
            }

            if (explicitGroundTarget && LooksProjectileLike(intent, definition))
            {
                return true;
            }

            return false;
        }

        private static void EnforceGroundFreezeIntentParameters(EffectIntent intent)
        {
            if (intent == null)
            {
                return;
            }

            intent.anchor = "ground";
            intent.target = "ground";
            intent.placementType = "area";
            intent.groundSnap = true;
            if (!intent.requireLineOfSight.HasValue)
            {
                intent.requireLineOfSight = false;
            }
        }

        private static PrefabPowerEntry TryFindGroundFreezeProfilePower(NpcDialogueProfile profile)
        {
            if (profile == null || profile.PrefabPowers == null || profile.PrefabPowers.Length == 0)
            {
                return null;
            }

            PrefabPowerEntry best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < profile.PrefabPowers.Length; i++)
            {
                PrefabPowerEntry entry = profile.PrefabPowers[i];
                if (entry == null || !entry.Enabled || entry.EffectPrefab == null)
                {
                    continue;
                }

                int score = 0;
                if (LooksLikeGroundFreezePower(entry))
                {
                    score += 120;
                }

                if (!entry.EnableHoming && entry.ProjectileSpeed <= 6f)
                {
                    score += 24;
                }

                if (entry.DamageRadius >= 2f)
                {
                    score += 16;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }

            return bestScore >= 120 ? best : null;
        }

        private static bool LooksLikeGroundFreezePower(PrefabPowerEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            string powerName = entry.PowerName ?? string.Empty;
            string prefabName = entry.EffectPrefab != null ? entry.EffectPrefab.name : string.Empty;
            bool hasGroundSignal =
                ContainsAnyKeyword(powerName, GroundSurfaceKeywords)
                || ContainsAnyKeyword(prefabName, GroundSurfaceKeywords)
                || ContainsAnyKeyword(
                    powerName,
                    new[] { "fog", "mist", "aoe", "field", "burst", "break" }
                )
                || ContainsAnyKeyword(
                    prefabName,
                    new[] { "fog", "mist", "aoe", "field", "burst", "break" }
                )
                || ContainsAnyKeyword(
                    string.Join(" ", entry.Keywords ?? Array.Empty<string>()),
                    GroundSurfaceKeywords
                )
                || ContainsAnyKeyword(
                    string.Join(" ", entry.CreativeTriggers ?? Array.Empty<string>()),
                    GroundSurfaceKeywords
                );

            bool hasFreezeSignal =
                ContainsAnyKeyword(powerName, GroundFreezeKeywords)
                || ContainsAnyKeyword(prefabName, GroundFreezeKeywords)
                || ContainsAnyKeyword(entry.Element, new[] { "ice", "frost", "cold", "water" })
                || ContainsAnyKeyword(
                    string.Join(" ", entry.Keywords ?? Array.Empty<string>()),
                    GroundFreezeKeywords
                )
                || ContainsAnyKeyword(
                    string.Join(" ", entry.CreativeTriggers ?? Array.Empty<string>()),
                    GroundFreezeKeywords
                );

            bool hasGroundFogSignal =
                ContainsAnyKeyword(
                    powerName,
                    new[] { "groundfog", "ground fog", "frost field", "ice field" }
                )
                || ContainsAnyKeyword(
                    prefabName,
                    new[] { "groundfog", "ground fog", "frost field", "ice field" }
                )
                || ContainsAnyKeyword(powerName, new[] { "fog", "mist" })
                || ContainsAnyKeyword(prefabName, new[] { "fog", "mist" });

            return hasGroundSignal
                && (hasFreezeSignal || hasGroundFogSignal || entry.DamageRadius >= 1f);
        }

        private static readonly HashSet<string> s_CombatKeywords = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            "damage",
            "attack",
            "strike",
            "blast",
            "hit",
            "hurt",
            "harm",
            "kill",
            "destroy",
            "burn",
            "freeze",
            "shock",
            "explode",
            "fire",
            "lightning",
            "curse",
            "wound",
            "slash",
        };

        private static bool IsCombatPowerKeywords(PrefabPowerEntry entry)
        {
            if (entry == null)
                return false;
            if (entry.Keywords != null)
                foreach (string kw in entry.Keywords)
                    if (!string.IsNullOrEmpty(kw) && s_CombatKeywords.Contains(kw))
                        return true;
            if (!string.IsNullOrEmpty(entry.PowerName))
                foreach (string kw in s_CombatKeywords)
                    if (entry.PowerName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return true;
            return false;
        }

        private EffectDefinition ResolveGroundFreezeDefinition(EffectCatalog catalog)
        {
            if (catalog == null || catalog.allEffects == null || catalog.allEffects.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < GroundFreezePreferredTags.Length; i++)
            {
                if (catalog.TryGet(GroundFreezePreferredTags[i], out EffectDefinition preferred))
                {
                    return preferred;
                }
            }

            EffectDefinition best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < catalog.allEffects.Count; i++)
            {
                EffectDefinition def = catalog.allEffects[i];
                if (def == null || string.IsNullOrWhiteSpace(def.effectTag))
                {
                    continue;
                }

                string haystack = string.Concat(
                    def.effectTag,
                    " ",
                    def.description ?? string.Empty,
                    " ",
                    string.Join(" ", def.alternativeTags ?? Array.Empty<string>())
                );

                int score = 0;
                if (ContainsAnyKeyword(haystack, GroundSurfaceKeywords))
                {
                    score += 50;
                }
                if (
                    ContainsAnyKeyword(haystack, GroundFreezeKeywords)
                    || ContainsAnyKeyword(haystack, new[] { "fog", "mist", "field" })
                )
                {
                    score += 40;
                }
                if (!def.enableHoming && def.projectileSpeed <= 6f)
                {
                    score += 20;
                }
                if (def.damageRadius >= 2f)
                {
                    score += 15;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = def;
                }
            }

            return bestScore >= 60 ? best : null;
        }

        private static Vector3 ResolveGroundPlacementNearReference(
            GameObject groundObject,
            Vector3 referencePosition
        )
        {
            if (groundObject == null)
            {
                return referencePosition;
            }

            if (TryGetObjectBounds(groundObject, out Bounds bounds))
            {
                Vector3 clamped = new Vector3(
                    Mathf.Clamp(referencePosition.x, bounds.min.x, bounds.max.x),
                    bounds.max.y + 0.03f,
                    Mathf.Clamp(referencePosition.z, bounds.min.z, bounds.max.z)
                );
                return clamped;
            }

            Vector3 origin = ResolveEffectOrigin(groundObject);
            origin.y += 0.03f;
            return origin;
        }

        private static readonly string[] PowerRequestPhrases = new[]
        {
            "power",
            "attack",
            "cast",
            "spell",
            "ability",
            "skill",
            "use your",
            "show me",
            "demonstrate",
            "blast",
            "strike",
            "shoot",
            "hit me",
            "fire at",
            "throw",
            "launch",
            "unleash",
            "summon",
            "conjure",
            "invoke",
            "hurl",
            "smite",
            "ultimate",
            "special",
            "vfx",
            "effect",
            "fx",
            "poder",
            "habilidade",
            "ataque",
        };

        private static bool LooksLikePowerRequest(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return false;
            }

            string lower = prompt.ToLowerInvariant();
            for (int i = 0; i < PowerRequestPhrases.Length; i++)
            {
                if (lower.Contains(PowerRequestPhrases[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static PrefabPowerEntry PickRandomEnabledPower(
            NpcDialogueProfile profile,
            string preferredElement = null
        )
        {
            PrefabPowerEntry[] powers = profile != null ? profile.PrefabPowers : null;
            if (powers == null || powers.Length == 0)
            {
                return null;
            }

            // Gather enabled powers with valid prefabs
            var candidates = new List<PrefabPowerEntry>(powers.Length);
            for (int i = 0; i < powers.Length; i++)
            {
                PrefabPowerEntry entry = powers[i];
                if (entry != null && entry.Enabled && entry.EffectPrefab != null)
                {
                    candidates.Add(entry);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            // Weight element-matching powers 3× when a preferred element is specified
            if (!string.IsNullOrWhiteSpace(preferredElement))
            {
                var weighted = new List<PrefabPowerEntry>(candidates.Count * 2);
                for (int i = 0; i < candidates.Count; i++)
                {
                    PrefabPowerEntry entry = candidates[i];
                    weighted.Add(entry);
                    if (
                        !string.IsNullOrWhiteSpace(entry.Element)
                        && string.Equals(
                            entry.Element,
                            preferredElement,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        // Add two extra copies = 3× selection weight
                        weighted.Add(entry);
                        weighted.Add(entry);
                    }
                }
                return weighted[UnityEngine.Random.Range(0, weighted.Count)];
            }

            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static PrefabPowerEntry TryMatchProfilePowerByName(
            NpcDialogueProfile profile,
            string tagName
        )
        {
            if (profile == null || string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            PrefabPowerEntry[] powers = profile.PrefabPowers;
            if (powers == null || powers.Length == 0)
            {
                return null;
            }

            string normalizedTag = tagName.Trim().ToLowerInvariant();
            string compactTag = CompactEffectMatchToken(normalizedTag);
            for (int i = 0; i < powers.Length; i++)
            {
                PrefabPowerEntry entry = powers[i];
                if (entry == null || !entry.Enabled || entry.EffectPrefab == null)
                {
                    continue;
                }

                // Match against PowerName
                if (
                    !string.IsNullOrWhiteSpace(entry.PowerName)
                    && entry
                        .PowerName.Trim()
                        .Equals(normalizedTag, System.StringComparison.OrdinalIgnoreCase)
                )
                {
                    return entry;
                }

                // Match against prefab asset name
                if (
                    entry.EffectPrefab.name.Equals(
                        normalizedTag,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return entry;
                }

                // Fuzzy: check if the tag contains the power name or vice versa.
                string lowerPowerName = (entry.PowerName ?? string.Empty).Trim().ToLowerInvariant();
                string compactPowerName = CompactEffectMatchToken(lowerPowerName);
                string lowerPrefabName = (entry.EffectPrefab.name ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();
                string compactPrefabName = CompactEffectMatchToken(lowerPrefabName);
                if (
                    lowerPowerName.Length > 2
                    && (
                        normalizedTag.Contains(lowerPowerName)
                        || lowerPowerName.Contains(normalizedTag)
                    )
                )
                {
                    return entry;
                }

                if (
                    compactTag.Length > 2
                    && (
                        compactPowerName.Contains(compactTag)
                        || compactTag.Contains(compactPowerName)
                        || compactPrefabName.Contains(compactTag)
                        || compactTag.Contains(compactPrefabName)
                    )
                )
                {
                    return entry;
                }

                if (
                    HasLooseTagMatch(normalizedTag, compactTag, entry.Keywords)
                    || HasLooseTagMatch(normalizedTag, compactTag, entry.CreativeTriggers)
                )
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool HasLooseTagMatch(
            string normalizedTag,
            string compactTag,
            string[] candidates
        )
        {
            if (candidates == null || candidates.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string lowerCandidate = candidate.Trim().ToLowerInvariant();
                if (
                    lowerCandidate.Equals(normalizedTag, System.StringComparison.Ordinal)
                    || lowerCandidate.Contains(normalizedTag, System.StringComparison.Ordinal)
                    || normalizedTag.Contains(lowerCandidate, System.StringComparison.Ordinal)
                )
                {
                    return true;
                }

                string compactCandidate = CompactEffectMatchToken(lowerCandidate);
                if (
                    compactCandidate.Length > 2
                    && (
                        compactCandidate.Equals(compactTag, System.StringComparison.Ordinal)
                        || compactCandidate.Contains(compactTag, System.StringComparison.Ordinal)
                        || compactTag.Contains(compactCandidate, System.StringComparison.Ordinal)
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static string CompactEffectMatchToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static PrefabPowerEffect BuildPrefabPowerEffect(PrefabPowerEntry entry)
        {
            return new PrefabPowerEffect
            {
                PrefabName = entry.EffectPrefab.name,
                DurationSeconds = Mathf.Max(0.5f, entry.DurationSeconds),
                Scale = Mathf.Max(0.1f, entry.Scale),
                SpawnOffset = entry.SpawnOffset,
                SpawnInFront = entry.SpawnInFrontOfNpc,
                ForwardDistance = Mathf.Max(0f, entry.ForwardDistance),
                Color = entry.ColorOverride,
                UseColorOverride = entry.UseColorOverride,
                PowerName = entry.PowerName ?? string.Empty,
                EnableGameplayDamage = entry.EnableGameplayDamage,
                EnableHoming = entry.EnableHoming,
                ProjectileSpeed = Mathf.Max(0.1f, entry.ProjectileSpeed),
                HomingTurnRateDegrees = Mathf.Max(0f, entry.HomingTurnRateDegrees),
                DamageAmount = Mathf.Max(0f, entry.DamageAmount),
                DamageRadius = Mathf.Max(0.1f, entry.DamageRadius),
                AffectPlayerOnly = entry.AffectPlayerOnly,
                DamageType = string.IsNullOrWhiteSpace(entry.DamageType)
                    ? "effect"
                    : entry.DamageType.Trim(),
                TargetNetworkObjectId = 0,
            };
        }

        private void TryApplyPromptOnlyPowerFallback(DialogueRequest request, string failureReason)
        {
            if (!m_EnableContextSceneEffects)
            {
                return;
            }

            if (!request.IsUserInitiated || !LooksLikePowerRequest(request.Prompt))
            {
                return;
            }

            NpcDialogueActor actor = ResolveDialogueActorForRequest(
                request,
                out ulong resolvedSpeakerNetworkId,
                out ulong resolvedListenerNetworkId,
                out _
            );
            NpcDialogueProfile profile = actor != null ? actor.Profile : null;
            if (profile == null)
            {
                return;
            }

            PlayerIdentityBinding fallbackIdentity = ResolvePlayerIdentityForRequest(request);
            PlayerEffectModifier fallbackMod = BuildPlayerEffectModifier(fallbackIdentity);
            PrefabPowerEntry fallback = PickPromptMatchedPower(
                profile,
                request.Prompt,
                fallbackMod.PreferredElement,
                fallbackMod.AggressionBias
            );
            if (fallback == null)
            {
                return;
            }

            PrefabPowerEffect power = BuildPrefabPowerEffect(fallback);
            GameObject speakerObject = ResolveSpawnedObject(resolvedSpeakerNetworkId);
            GameObject listenerObject = ResolveSpawnedObject(resolvedListenerNetworkId);
            ResolveEffectSpatialContext(
                request.Prompt,
                speakerObject,
                listenerObject,
                out Vector3 effectOrigin,
                out Vector3 effectForward,
                out _
            );

            Vector3 spawnPos = effectOrigin + power.SpawnOffset;
            if (power.SpawnInFront)
            {
                spawnPos += effectForward * Mathf.Max(0f, power.ForwardDistance);
            }

            ulong targetNetworkObjectId =
                resolvedListenerNetworkId != 0
                    ? resolvedListenerNetworkId
                    : power.TargetNetworkObjectId;
            GameObject fallbackTargetObject = ResolveSpawnedObject(targetNetworkObjectId);
            EffectSpatialType fallbackPowerSpatialType = ResolveEffectSpatialType(
                placementHint: null,
                effectName: string.IsNullOrWhiteSpace(power.PowerName)
                    ? power.PrefabName
                    : power.PowerName,
                targetHint: fallbackTargetObject != null ? fallbackTargetObject.name : "player",
                enableHoming: power.EnableHoming,
                projectileSpeed: power.ProjectileSpeed,
                damageRadius: power.DamageRadius,
                scale: power.Scale,
                affectPlayerOnly: power.AffectPlayerOnly
            );
            EffectSpatialPolicy fallbackPowerSpatialPolicy = BuildEffectSpatialPolicy(
                fallbackPowerSpatialType,
                power.EnableGameplayDamage,
                collisionPolicyHintOverride: null
            );
            DialogueEffectSpatialResolver.ResolveResult fallbackSpatial =
                ResolveSpatialPlacementForPower(
                    power.PowerName,
                    request,
                    spawnPos,
                    effectForward,
                    effectOrigin,
                    effectForward,
                    power.Scale,
                    power.DamageRadius,
                    fallbackPowerSpatialPolicy,
                    enableGameplayDamage: power.EnableGameplayDamage,
                    targetObject: fallbackTargetObject
                );
            if (!fallbackSpatial.IsValid)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Prompt-only power fallback skipped (spatial invalid)",
                        ("power", power.PowerName),
                        ("prefab", power.PrefabName),
                        ("reason", fallbackSpatial.Reason ?? "invalid")
                    )
                );
                return;
            }

            ApplyPrefabPowerEffectClientRpc(
                power.PrefabName,
                fallbackSpatial.Position,
                fallbackSpatial.Forward,
                power.Scale,
                power.DurationSeconds,
                new Vector4(power.Color.r, power.Color.g, power.Color.b, power.Color.a),
                power.UseColorOverride,
                power.EnableGameplayDamage,
                power.EnableHoming,
                power.ProjectileSpeed,
                power.HomingTurnRateDegrees,
                power.DamageAmount,
                power.DamageRadius,
                power.AffectPlayerOnly,
                power.DamageType ?? "effect",
                targetNetworkObjectId,
                resolvedSpeakerNetworkId,
                attachToTarget: fallbackPowerSpatialType == EffectSpatialType.Attached,
                fitToTargetMesh: fallbackPowerSpatialType == EffectSpatialType.Attached,
                serverSpawnTimeSeconds: ResolveServerEffectTimeSeconds(),
                effectSeed: ResolveEffectSeed()
            );

            NGLog.Warn(
                "DialogueFX",
                NGLog.Format(
                    "Applied prompt-only power fallback",
                    ("power", power.PowerName),
                    ("prefab", power.PrefabName),
                    ("speaker", resolvedSpeakerNetworkId),
                    ("listener", resolvedListenerNetworkId),
                    ("spatialType", fallbackPowerSpatialType.ToString()),
                    ("spatial", fallbackSpatial.Reason ?? "ok"),
                    ("reason", failureReason ?? "unknown")
                )
            );
        }

        private static float ClampDynamicMultiplier(float value, NpcDialogueProfile profile)
        {
            float min = 0.6f;
            float max = 2f;
            if (profile != null)
            {
                min = Mathf.Clamp(profile.DynamicEffectMinMultiplier, 0.25f, 1f);
                max = Mathf.Clamp(profile.DynamicEffectMaxMultiplier, 1f, 10f);
                if (max < min)
                {
                    max = min;
                }
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 1f;
            }

            return Mathf.Clamp(value, min, max);
        }

        private static Color ApplyDynamicColorOverride(
            Color baseColor,
            ParticleParameterExtractor.ParticleParameterIntent parameterIntent,
            float blend = 0.78f
        )
        {
            if (!parameterIntent.HasColorOverride)
            {
                return baseColor;
            }

            float t = Mathf.Clamp01(blend);
            Color color = Color.Lerp(baseColor, parameterIntent.ColorOverride, t);
            color.a = baseColor.a;
            return color;
        }

        private static string BuildEffectContextText(string prompt, string response)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return response ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                return prompt;
            }

            return $"{prompt}\n{response}";
        }

        private static bool IsGameplayProbeRequest(DialogueRequest request, string promptText)
        {
            if (request.ClientRequestId >= GameplayProbeClientRequestIdMin)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(promptText)
                && promptText.IndexOf("Gameplay probe for ", StringComparison.OrdinalIgnoreCase)
                    >= 0;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SetPlayerPromptContextServerRpc(
            string nameId,
            string customizationJson,
            RpcParams rpcParams = default
        )
        {
            if (
                !SetPlayerPromptContextForClient(
                    rpcParams.Receive.SenderClientId,
                    nameId,
                    customizationJson
                )
            )
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "Player prompt context RPC apply failed",
                        ("sender", rpcParams.Receive.SenderClientId)
                    )
                );
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ClearPlayerPromptContextServerRpc(RpcParams rpcParams = default)
        {
            if (!ClearPlayerPromptContextForClient(rpcParams.Receive.SenderClientId))
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "Player prompt context RPC clear failed",
                        ("sender", rpcParams.Receive.SenderClientId)
                    )
                );
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void AppendMessageServerRpc(
            string conversationKey,
            string role,
            string content,
            RpcParams rpcParams = default
        )
        {
            if (string.IsNullOrWhiteSpace(conversationKey) || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            if (
                !IsConversationKeyVisibleToClient(conversationKey, rpcParams.Receive.SenderClientId)
            )
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "Append rejected (invalid conversation key)",
                        ("sender", rpcParams.Receive.SenderClientId),
                        ("key", conversationKey)
                    )
                );
                return;
            }

            AppendMessageInternal(conversationKey, role, content);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDialogueServerRpc(
            string prompt,
            string conversationKey,
            ulong speakerNetworkId,
            ulong listenerNetworkId,
            bool broadcast,
            float broadcastDuration,
            int clientRequestId,
            bool isUserInitiated,
            bool blockRepeatedPrompt,
            float minRepeatDelaySeconds,
            bool requireUserReply,
            RpcParams rpcParams = default
        )
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (m_LogDebug)
            {
                NGLog.Debug(
                    "Dialogue",
                    NGLog.Format(
                        "ServerRpc received",
                        ("sender", senderClientId),
                        ("speaker", speakerNetworkId),
                        ("listener", listenerNetworkId)
                    )
                );
            }

            string canonicalKey = ResolveConversationKey(
                speakerNetworkId,
                listenerNetworkId,
                senderClientId,
                null
            );
            if (
                TryGetPlayerNetworkObjectIdForClient(
                    senderClientId,
                    out ulong senderPlayerNetworkId
                )
            )
            {
                UpsertPlayerIdentity(
                    senderClientId,
                    senderPlayerNetworkId,
                    $"client_{senderClientId}",
                    null
                );
            }

            if (
                !TryValidateClientDialogueParticipants(
                    senderClientId,
                    speakerNetworkId,
                    listenerNetworkId,
                    out string participantReason
                )
            )
            {
                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "ServerRpc request rejected (participant validation)",
                        ("reason", participantReason ?? "unknown"),
                        ("key", canonicalKey),
                        ("clientRequest", clientRequestId)
                    )
                );
                SendRejectedDialogueResponseToClient(
                    senderClientId,
                    -1,
                    clientRequestId,
                    participantReason,
                    canonicalKey,
                    speakerNetworkId,
                    listenerNetworkId
                );
                return;
            }

            if (!string.IsNullOrWhiteSpace(conversationKey))
            {
                string requestedKey = ResolveConversationKey(
                    speakerNetworkId,
                    listenerNetworkId,
                    senderClientId,
                    conversationKey
                );
                string clientScopedPrefix = $"client:{senderClientId}:";
                if (requestedKey.StartsWith(clientScopedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalKey = requestedKey;
                }
                else if (!string.Equals(requestedKey, canonicalKey, StringComparison.Ordinal))
                {
                    NGLog.Warn(
                        "Dialogue",
                        NGLog.Format(
                            "Ignoring client conversation override",
                            ("sender", senderClientId),
                            ("requested", requestedKey),
                            ("canonical", canonicalKey)
                        )
                    );
                }
            }

            var request = new DialogueRequest
            {
                Prompt = prompt,
                ConversationKey = canonicalKey,
                SpeakerNetworkId = speakerNetworkId,
                ListenerNetworkId = listenerNetworkId,
                RequestingClientId = senderClientId,
                Broadcast = broadcast,
                BroadcastDuration = broadcastDuration,
                NotifyClient = true,
                ClientRequestId = clientRequestId,
                IsUserInitiated = isUserInitiated,
                BlockRepeatedPrompt = blockRepeatedPrompt,
                MinRepeatDelaySeconds = minRepeatDelaySeconds,
                RequireUserReply = requireUserReply,
            };

            if (TryEnqueueRequest(request, out int requestId, out string rejectionReason))
            {
                return;
            }

            NGLog.Warn(
                "Dialogue",
                NGLog.Format(
                    "ServerRpc request rejected",
                    ("reason", rejectionReason ?? "unknown"),
                    ("key", canonicalKey),
                    ("clientRequest", clientRequestId)
                )
            );
            SendRejectedDialogueResponseToClient(
                senderClientId,
                requestId,
                clientRequestId,
                rejectionReason,
                canonicalKey,
                speakerNetworkId,
                listenerNetworkId,
                isUserInitiated
            );
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void DialogueResponseClientRpc(
            int requestId,
            int clientRequestId,
            DialogueStatus status,
            string responseText,
            string error,
            string conversationKey,
            ulong speakerNetworkId,
            ulong listenerNetworkId,
            ulong requestingClientId,
            bool isUserInitiated,
            RpcParams rpcParams = default
        )
        {
            if (m_LogDebug)
            {
                NGLog.Debug(
                    "Dialogue",
                    NGLog.Format(
                        "ClientRpc response",
                        ("id", requestId),
                        ("clientRequest", clientRequestId),
                        ("status", status),
                        ("key", conversationKey ?? string.Empty),
                        ("speaker", speakerNetworkId),
                        ("listener", listenerNetworkId)
                    )
                );
            }
            var response = new DialogueResponse
            {
                RequestId = requestId,
                Status = status,
                ResponseText = responseText,
                Error = error,
                Request = new DialogueRequest
                {
                    ConversationKey = conversationKey,
                    ClientRequestId = clientRequestId,
                    SpeakerNetworkId = speakerNetworkId,
                    ListenerNetworkId = listenerNetworkId,
                    RequestingClientId = requestingClientId,
                    IsUserInitiated = isUserInitiated,
                },
            };

            OnDialogueResponse?.Invoke(response);
            DialogueMCPBridge.LogDialogueDebugEntry(
                response.Request,
                response.ResponseText,
                response.Status.ToString()
            );
        }

        /// <summary>
        /// Analyzes a runtime error or log message using the LLM to suggest fixes.
        /// Bypasses the standard dialogue queue for immediate analysis.
        /// </summary>
        public async Task<string> AnalyzeDebugLog(string logContext, string errorMessage)
        {
            if (m_LlmAgent == null)
                return "LLM Agent not available.";

            string debugSystemPrompt =
                @"You are an expert Unity C# engineer and debugger.
Your task is to analyze the provided runtime log and error message.
Identify the root cause and suggest a specific code fix.
Format your response as a concise summary followed by a code block if applicable.
Do not roleplay as a character.";

            string userPrompt =
                $"Analyze this Unity error:\nContext:\n{logContext}\n\nError:\n{errorMessage}";

            try
            {
                IDialogueInferenceClient inferenceClient = ResolveInferenceClient(UseOpenAIRemote);
                if (inferenceClient == null)
                {
                    return "LLM backend not ready.";
                }

                if (UseOpenAIRemote)
                {
                    return await inferenceClient.ChatAsync(
                        debugSystemPrompt,
                        new List<DialogueInferenceMessage>(),
                        userPrompt,
                        addToHistory: false
                    );
                }
                else if (m_LlmAgent.llm != null)
                {
                    if (m_IsProcessing)
                        return "LLM is busy with dialogue. Debug analysis skipped.";

                    return await inferenceClient.ChatAsync(
                        debugSystemPrompt,
                        new List<DialogueInferenceMessage>(),
                        userPrompt,
                        addToHistory: false
                    );
                }
            }
            catch (Exception ex)
            {
                return $"Analysis failed: {ex.Message}";
            }

            return "LLM backend not ready.";
        }
    }
}
