using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Network_Game.Diagnostics;
using Network_Game.ThirdPersonController;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Client-side UI helper for sending dialogue prompts and presenting chat transcript.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class DialogueClientUI : MonoBehaviour
    {
        private enum TranscriptRole
        {
            Player,
            Npc,
            System,
        }

        private sealed class TranscriptLine
        {
            public string Text;
            public string SpeakerLabel;
            public TranscriptRole Role;
            public int ClientRequestId;
            public bool IsPending;
        }

        [Header("UI")]
        [SerializeField]
        private TMP_InputField m_InputField;

        [SerializeField]
        private TMP_Text m_OutputText;

        [SerializeField]
        private Button m_SendButton;

        [Header("Participants")]
        [SerializeField]
        private NetworkObject m_Speaker;

        [SerializeField]
        private NetworkObject m_Listener;

        [Header("Targeting")]
        [SerializeField]
        [Tooltip("If true, the listener NPC is re-selected at send time.")]
        private bool m_AutoSelectListener = true;

        [SerializeField]
        [Tooltip("If true, camera center aim target is preferred before nearest NPC fallback.")]
        private bool m_PrioritizeAimedNpc = true;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Max listener selection distance. Set 0 for unlimited.")]
        private float m_MaxListenerDistance = 20f;

        [SerializeField]
        [Tooltip("Physics mask used for aimed NPC selection.")]
        private LayerMask m_ListenerRaycastMask = ~0;

        [Header("Options")]
        [SerializeField]
        private bool m_Broadcast = true;

        [SerializeField]
        private float m_BroadcastDuration = 2f;

        [SerializeField]
        private string m_ConversationKeyOverride = "";

        [SerializeField]
        private bool m_ShowExternalResponses = true;

        [SerializeField]
        [Tooltip(
            "If true, external responses must match the currently selected speaker/listener pair."
        )]
        private bool m_FilterExternalResponsesByParticipants = true;

        [SerializeField]
        private bool m_AppendExternalResponses = true;

        [SerializeField]
        [Tooltip("If true, shows speaker name prefixes in output text when available.")]
        private bool m_ShowSpeakerLabel = true;

        [SerializeField]
        [Tooltip(
            "If enabled, the listener NPC is treated as the responder (speaker) for generated replies."
        )]
        private bool m_RequestNpcResponder = true;

        [SerializeField]
        [Tooltip("If true, appends the technical rejection code after the user-facing message.")]
        private bool m_ShowTechnicalErrorCodes;

        [Header("Presentation")]
        [SerializeField]
        private bool m_ShowPlayerMessages = true;

        [SerializeField]
        private bool m_ShowPendingStatus = true;

        [SerializeField]
        private bool m_DisableSendWhilePending = true;

        [SerializeField]
        [Min(4)]
        private int m_MaxTranscriptMessages = 20;

        [SerializeField]
        [Min(80)]
        private int m_MaxMessageCharacters = 280;

        [SerializeField]
        [Min(30)]
        private int m_WrapColumn = 56;

        [SerializeField]
        private bool m_StripMarkdownArtifacts = true;

        [SerializeField]
        private bool m_PrettifySentenceLayout = true;

        [SerializeField]
        private bool m_ShowTimestamps;

        [SerializeField]
        private bool m_UseRichText = true;

        [SerializeField]
        private string m_PlayerLabel = "You";

        [SerializeField]
        private string m_PlayerLabelColor = "#A8DBFF";

        [SerializeField]
        private string m_NpcLabelColor = "#B8F5C3";

        [SerializeField]
        private string m_SystemLabelColor = "#FFD68A";

        [SerializeField]
        private bool m_StyleOutputText = true;

        [SerializeField]
        [Min(12f)]
        private float m_OutputFontSize = 18f;

        [SerializeField]
        private Vector2 m_MinOutputSize = new Vector2(560f, 180f);

        [SerializeField]
        [Tooltip(
            "Limits very long NPC responses to first N sentences in UI for readability. Set 0 to disable."
        )]
        [Min(0)]
        private int m_MaxSentencesPerMessage = 2;

        [Header("Layout")]
        [SerializeField]
        [Tooltip("Builds a dedicated chat panel with scroll and docks controls for readability.")]
        private bool m_EnableReadableLayout = true;

        [SerializeField]
        [Tooltip(
            "If true, the auto panel uses dedicated input/send controls instead of reusing legacy scene controls."
        )]
        private bool m_UseDedicatedAutoControls = true;

        [SerializeField]
        [Tooltip(
            "If true, legacy dialogue controls outside DialoguePanel_Auto are hidden when dedicated controls are active."
        )]
        private bool m_HideLegacyDialogueObjects = true;

        [SerializeField]
        private bool m_AutoScrollToLatest = true;

        [SerializeField]
        private Vector2 m_ChatPanelSize = new Vector2(700f, 320f);

        [SerializeField]
        private Vector2 m_ChatPanelOffset = new Vector2(0f, 22f);

        [SerializeField]
        [Min(20f)]
        private float m_ChatHeaderHeight = 34f;

        [SerializeField]
        [Min(30f)]
        private float m_InputRowHeight = 52f;

        [SerializeField]
        [Min(14f)]
        private float m_InputFontSize = 36f;

        [SerializeField]
        private Color m_ChatPanelColor = new Color(0.05f, 0.06f, 0.08f, 0.92f);

        [SerializeField]
        private Color m_ChatViewportColor = new Color(0.02f, 0.03f, 0.04f, 0.88f);

        [SerializeField]
        private Color m_ChatHeaderColor = new Color(0.93f, 0.93f, 0.93f, 1f);

        [SerializeField]
        private Color m_ChatPanelBorderColor = new Color(0.82f, 0.84f, 0.88f, 0.34f);

        [SerializeField]
        [Min(0.5f)]
        private float m_ChatPanelBorderWidth = 1f;

        [SerializeField]
        [Min(2f)]
        private float m_BlocksSpacingSm = 8f;

        [SerializeField]
        [Min(4f)]
        private float m_BlocksSpacingMd = 16f;

        [SerializeField]
        [Tooltip("If true, a short listener indicator is shown in the chat header.")]
        private bool m_ShowListenerInHeader = true;

        [Header("Visibility")]
        [SerializeField]
        private bool m_EnableCloseButton = true;

        [SerializeField]
        private bool m_StartHidden;

        [SerializeField]
        private KeyCode m_CloseKey = KeyCode.Escape;

        [SerializeField]
        private KeyCode m_ToggleKey = KeyCode.BackQuote;

        [Header("Bubble Style")]
        [SerializeField]
        private bool m_UseBubbleMessages = false;

        [SerializeField]
        [Min(220f)]
        private float m_BubbleMaxWidth = 360f;

        [SerializeField]
        private Color m_PlayerBubbleColor = new Color(0.09f, 0.22f, 0.38f, 0.8f);

        [SerializeField]
        private Color m_NpcBubbleColor = new Color(0.09f, 0.22f, 0.15f, 0.8f);

        [SerializeField]
        private Color m_SystemBubbleColor = new Color(0.26f, 0.24f, 0.09f, 0.75f);

        [SerializeField]
        private Color m_BubbleTextColor = new Color(0.95f, 0.95f, 0.95f, 1f);

        [SerializeField]
        [Min(4)]
        private int m_MaxRecentSpeakers = 6;

        private int m_ClientRequestId;
        private float m_LastSendTime;
        private int m_LastPendingRequestId;

        [SerializeField]
        [Tooltip(
            "Client-side safety timeout in seconds. If no response arrives within this time, the Thinking placeholder is removed and an error is shown. Set 0 to disable."
        )]
        private float m_ClientSideTimeoutSeconds = 45f;
        private ulong m_LastResolvedListenerId;
        private readonly List<TranscriptLine> m_Transcript = new List<TranscriptLine>();
        private RectTransform m_ChatPanelRect;
        private RectTransform m_ChatContentRect;
        private RectTransform m_ChatMessageListRect;
        private ScrollRect m_ChatScrollRect;
        private TMP_Text m_ChatHeaderText;
        private Button m_RecentSpeakerButton;
        private TMP_Text m_RecentSpeakerButtonText;
        private Button m_CloseButton;
        private TMP_Text m_CloseButtonText;
        private readonly List<ulong> m_RecentSpeakerIds = new List<ulong>();
        private readonly Dictionary<ulong, string> m_RecentSpeakerNames =
            new Dictionary<ulong, string>();
        private int m_RecentSpeakerCycleIndex = -1;
        private bool m_UseManualListenerSelection;
        private ulong m_ManualListenerNetworkId;
        private Coroutine m_PendingAutoScrollRoutine;
        private bool m_DialogueVisible = true;
        private bool m_WasInputSuppressed;

        public static event Action<bool> OnDialogueVisibilityChanged;
        public bool IsDialogueVisible => m_DialogueVisible;
        public static DialogueClientUI Instance { get; private set; }

        private static readonly Regex s_MultiSpaceRegex = new Regex(
            @"[ \t]{2,}",
            RegexOptions.Compiled
        );
        private static readonly Regex s_SentenceBreakRegex = new Regex(
            @"([.!?])\s+(?=[A-Za-z0-9])",
            RegexOptions.Compiled
        );

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            NormalizeRuntimePresentationSettings();
            NormalizeLegacyContainerLayout();
            ResolveUiReferences();
            EnsureReadableLayout();
            EnsureOutputText();
            ApplyOutputTextStyle();
            ApplyInputAndSendStyle();
            RenderTranscript();
            SetDialogueVisible(!m_StartHidden, true);
        }

        private void Start()
        {
            ResolveParticipants();
            UpdateHeaderText();
            UpdateSendButtonState();
        }

        private void OnEnable()
        {
            if (m_SendButton != null)
            {
                m_SendButton.onClick.AddListener(SendPrompt);
            }
            if (m_RecentSpeakerButton != null)
            {
                m_RecentSpeakerButton.onClick.AddListener(CycleRecentSpeaker);
            }
            if (m_CloseButton != null)
            {
                m_CloseButton.onClick.AddListener(CloseDialoguePanel);
            }
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
            UpdateSendButtonState();
        }

        private void OnDisable()
        {
            if (m_SendButton != null)
            {
                m_SendButton.onClick.RemoveListener(SendPrompt);
            }
            if (m_RecentSpeakerButton != null)
            {
                m_RecentSpeakerButton.onClick.RemoveListener(CycleRecentSpeaker);
            }
            if (m_CloseButton != null)
            {
                m_CloseButton.onClick.RemoveListener(CloseDialoguePanel);
            }
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
            if (m_PendingAutoScrollRoutine != null)
            {
                StopCoroutine(m_PendingAutoScrollRoutine);
                m_PendingAutoScrollRoutine = null;
            }
            if (m_WasInputSuppressed)
            {
                m_WasInputSuppressed = false;
                SetPlayerGameplayInputEnabled(true);
            }
        }

        public void SendPrompt()
        {
            ResolveParticipants();
            if (m_InputField == null)
            {
                NGLog.Warn("DialogueUI", "InputField not set.");
                return;
            }

            if (m_DisableSendWhilePending && HasPendingTranscriptLine())
            {
                NGLog.Debug("DialogueUI", "Send blocked while response is pending.");
                return;
            }

            string prompt = (m_InputField.text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            var service = NetworkDialogueService.Instance;
            if (service == null)
            {
                NGLog.Warn("DialogueUI", "NetworkDialogueService not found.");
                return;
            }

            if (m_Speaker == null || !m_Speaker.IsSpawned)
            {
                NGLog.Warn("DialogueUI", "Speaker not resolved.");
            }

            if (m_Listener == null || !m_Listener.IsSpawned)
            {
                NGLog.Warn("DialogueUI", "Listener not resolved.");
            }

            if (m_RequestNpcResponder && (m_Listener == null || !m_Listener.IsSpawned))
            {
                AddTranscriptLine(
                    BuildSystemDisplayText("No NPC selected. Move closer to or aim at an NPC."),
                    0,
                    false,
                    TranscriptRole.System,
                    "System"
                );
                UpdateSendButtonState();
                return;
            }

            int requestId = ++m_ClientRequestId;
            ulong sourceSpeakerId = m_Speaker != null ? m_Speaker.NetworkObjectId : 0;
            ulong sourceListenerId = m_Listener != null ? m_Listener.NetworkObjectId : 0;
            ulong speakerId = sourceSpeakerId;
            ulong listenerId = sourceListenerId;
            if (m_RequestNpcResponder && sourceListenerId != 0)
            {
                speakerId = sourceListenerId;
                listenerId = sourceSpeakerId;
            }

            ulong requesterClientId =
                NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            string resolvedConversationKey = service.ResolveConversationKey(
                speakerId,
                listenerId,
                requesterClientId,
                string.IsNullOrWhiteSpace(m_ConversationKeyOverride)
                    ? null
                    : m_ConversationKeyOverride
            );

            var request = new NetworkDialogueService.DialogueRequest
            {
                Prompt = prompt,
                ConversationKey = resolvedConversationKey,
                SpeakerNetworkId = speakerId,
                ListenerNetworkId = listenerId,
                RequestingClientId = requesterClientId,
                Broadcast = m_Broadcast,
                BroadcastDuration = m_BroadcastDuration,
                NotifyClient = true,
                ClientRequestId = requestId,
                IsUserInitiated = true,
                BlockRepeatedPrompt = false,
                MinRepeatDelaySeconds = 0f,
                RequireUserReply = false,
            };

            if (m_ShowPlayerMessages)
            {
                AddTranscriptLine(
                    BuildPlayerDisplayText(prompt),
                    requestId,
                    false,
                    TranscriptRole.Player,
                    m_PlayerLabel
                );
            }

            if (m_ShowPendingStatus)
            {
                AddPendingPlaceholder(requestId, request.SpeakerNetworkId);
            }

            service.RequestDialogue(request);
            m_LastSendTime = Time.realtimeSinceStartup;
            m_LastPendingRequestId = requestId;
            NGLog.Info(
                "DialogueUI",
                NGLog.Format(
                    "Sent prompt",
                    ("clientRequest", requestId),
                    ("speaker", request.SpeakerNetworkId),
                    ("listener", request.ListenerNetworkId)
                )
            );

            m_InputField.text = string.Empty;
            m_InputField.ActivateInputField();
            UpdateSendButtonState();
        }

        public void ConfigureParticipants(NetworkObject speaker, NetworkObject listener)
        {
            m_Speaker = speaker;
            m_Listener = listener;
            UpdateHeaderText();
        }

        private void Update()
        {
            HandleVisibilityInput();
            HandlePlayerInputSuppression();

            if (m_ClientSideTimeoutSeconds <= 0f || m_LastPendingRequestId <= 0)
            {
                return;
            }

            if (!HasPendingTranscriptLine())
            {
                m_LastPendingRequestId = 0;
                return;
            }

            float elapsed = Time.realtimeSinceStartup - m_LastSendTime;
            if (elapsed <= m_ClientSideTimeoutSeconds)
            {
                return;
            }

            if (ShouldDeferClientSideTimeout())
            {
                return;
            }

            NGLog.Warn(
                "DialogueUI",
                NGLog.Format(
                    "Client-side timeout reached",
                    ("requestId", m_LastPendingRequestId),
                    ("elapsed", elapsed),
                    ("timeout", m_ClientSideTimeoutSeconds)
                )
            );
            RemovePendingPlaceholder(m_LastPendingRequestId);
            m_LastPendingRequestId = 0;
            AddTranscriptLine(
                BuildSystemDisplayText(
                    "Response timed out. The model may still be processing or unavailable. Please try again."
                ),
                0,
                false,
                TranscriptRole.System,
                "System"
            );
            UpdateSendButtonState();
        }

        private bool ShouldDeferClientSideTimeout()
        {
            NetworkDialogueService service = NetworkDialogueService.Instance;
            if (service == null || !service.IsServer || m_LastPendingRequestId <= 0)
            {
                return false;
            }

            ulong localClientId =
                NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            return service.IsClientRequestInFlight(m_LastPendingRequestId, localClientId);
        }

        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            bool matchesRequest = response.Request.ClientRequestId == m_ClientRequestId;
            int effectiveClientRequestId = response.Request.ClientRequestId;
            if (
                !matchesRequest
                && m_LastPendingRequestId > 0
                && HasPendingTranscriptLine()
                && MatchesCurrentParticipants(response.Request)
            )
            {
                matchesRequest = true;
                if (effectiveClientRequestId <= 0)
                {
                    effectiveClientRequestId = m_LastPendingRequestId;
                }
                NGLog.Debug(
                    "DialogueUI",
                    NGLog.Format(
                        "Recovered response request match via participant fallback",
                        ("responseClientRequest", response.Request.ClientRequestId),
                        ("effectiveClientRequest", effectiveClientRequestId),
                        ("localClientRequest", m_ClientRequestId),
                        ("speaker", response.Request.SpeakerNetworkId),
                        ("listener", response.Request.ListenerNetworkId)
                    )
                );
            }
            if (!matchesRequest)
            {
                if (!m_ShowExternalResponses || response.Request.ClientRequestId != 0)
                {
                    return;
                }

                if (
                    m_FilterExternalResponsesByParticipants
                    && !MatchesCurrentParticipants(response.Request)
                )
                {
                    return;
                }
            }

            EnsureOutputText();
            if (m_OutputText == null)
            {
                return;
            }

            RemovePendingPlaceholder(effectiveClientRequestId);
            m_LastPendingRequestId = 0;

            if (response.Status == NetworkDialogueService.DialogueStatus.Completed)
            {
                string displayText = BuildDisplayText(response);
                string npcName = ResolveNetworkObjectName(response.Request.SpeakerNetworkId);
                if (string.IsNullOrWhiteSpace(npcName))
                {
                    npcName = "NPC";
                }

                TrackRecentSpeaker(response.Request.SpeakerNetworkId, npcName);
                NGLog.Info(
                    "DialogueUI",
                    NGLog.Format(
                        "Response received",
                        ("clientRequest", response.Request.ClientRequestId),
                        ("speaker", response.Request.SpeakerNetworkId),
                        ("listener", response.Request.ListenerNetworkId),
                        ("key", response.Request.ConversationKey ?? string.Empty),
                        ("len", response.ResponseText?.Length ?? 0)
                    )
                );

                if (!matchesRequest && !m_AppendExternalResponses)
                {
                    ReplaceTranscriptWith(displayText, TranscriptRole.Npc, npcName);
                }
                else
                {
                    AddTranscriptLine(
                        displayText,
                        effectiveClientRequestId,
                        false,
                        TranscriptRole.Npc,
                        npcName
                    );
                }
            }
            else
            {
                string friendlyError = FormatErrorMessage(
                    response.Error,
                    m_ShowTechnicalErrorCodes
                );
                NGLog.Warn(
                    "DialogueUI",
                    NGLog.Format(
                        "Response failed",
                        ("clientRequest", response.Request.ClientRequestId),
                        ("speaker", response.Request.SpeakerNetworkId),
                        ("listener", response.Request.ListenerNetworkId),
                        ("key", response.Request.ConversationKey ?? string.Empty),
                        ("error", response.Error ?? "Dialogue failed.")
                    )
                );
                AddTranscriptLine(
                    BuildSystemDisplayText(friendlyError),
                    effectiveClientRequestId,
                    false,
                    TranscriptRole.System,
                    "System"
                );
            }

            UpdateSendButtonState();
        }

        private bool MatchesCurrentParticipants(NetworkDialogueService.DialogueRequest request)
        {
            ulong requestSpeakerId = request.SpeakerNetworkId;
            ulong requestListenerId = request.ListenerNetworkId;
            if (requestSpeakerId == 0 || requestListenerId == 0)
            {
                return true;
            }

            ulong sourceSpeakerId = m_Speaker != null ? m_Speaker.NetworkObjectId : 0;
            ulong sourceListenerId = m_Listener != null ? m_Listener.NetworkObjectId : 0;
            if (sourceSpeakerId == 0 || sourceListenerId == 0)
            {
                return true;
            }

            ulong expectedSpeakerId = sourceSpeakerId;
            ulong expectedListenerId = sourceListenerId;
            if (m_RequestNpcResponder)
            {
                expectedSpeakerId = sourceListenerId;
                expectedListenerId = sourceSpeakerId;
            }

            return requestSpeakerId == expectedSpeakerId && requestListenerId == expectedListenerId;
        }

        private string BuildPlayerDisplayText(string prompt)
        {
            string label = string.IsNullOrWhiteSpace(m_PlayerLabel) ? "You" : m_PlayerLabel.Trim();
            string body = NormalizeMessageBody(prompt);
            return BuildTranscriptLine(label, body, TranscriptRole.Player);
        }

        private string BuildDisplayText(NetworkDialogueService.DialogueResponse response)
        {
            string text = NormalizeMessageBody(response.ResponseText);
            if (!m_ShowSpeakerLabel || response.Request.SpeakerNetworkId == 0)
            {
                return text;
            }

            string speakerName = ResolveNetworkObjectName(response.Request.SpeakerNetworkId);
            if (string.IsNullOrWhiteSpace(speakerName))
            {
                speakerName = "NPC";
            }

            return BuildTranscriptLine(speakerName, text, TranscriptRole.Npc);
        }

        private string BuildSystemDisplayText(string text)
        {
            return BuildTranscriptLine("System", NormalizeMessageBody(text), TranscriptRole.System);
        }

        private string BuildTranscriptLine(string label, string body, TranscriptRole role)
        {
            string effectiveBody = string.IsNullOrWhiteSpace(body) ? "..." : body;
            if (m_UseBubbleMessages && m_EnableReadableLayout)
            {
                return effectiveBody;
            }

            if (!m_ShowSpeakerLabel || string.IsNullOrWhiteSpace(label))
            {
                return effectiveBody;
            }

            string timestampPrefix = m_ShowTimestamps ? $"[{DateTime.Now:HH:mm}] " : string.Empty;
            string effectiveLabel = label.Trim();
            if (m_UseRichText)
            {
                string color = role switch
                {
                    TranscriptRole.Player => m_PlayerLabelColor,
                    TranscriptRole.Npc => m_NpcLabelColor,
                    _ => m_SystemLabelColor,
                };
                effectiveLabel = $"<color={color}><b>{effectiveLabel}</b></color>";
            }

            return $"{timestampPrefix}{effectiveLabel}: {effectiveBody}";
        }

        private string NormalizeMessageBody(string text)
        {
            string normalized = (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (m_StripMarkdownArtifacts)
            {
                normalized = StripMarkdownArtifacts(normalized);
            }

            normalized = s_MultiSpaceRegex.Replace(normalized, " ");
            normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");

            if (m_PrettifySentenceLayout)
            {
                normalized = s_SentenceBreakRegex.Replace(normalized, "$1\n");
            }

            normalized = WrapToColumn(normalized, m_WrapColumn);
            normalized = TrimToSentenceLimit(normalized, m_MaxSentencesPerMessage);

            if (m_MaxMessageCharacters > 0 && normalized.Length > m_MaxMessageCharacters)
            {
                normalized = normalized.Substring(0, m_MaxMessageCharacters).TrimEnd() + "...";
            }

            return normalized;
        }

        private static string StripMarkdownArtifacts(string text)
        {
            string result = text.Replace(
                    "```json",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase
                )
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("__", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal);

            result = result.Trim();
            if (
                result.StartsWith("\"", StringComparison.Ordinal)
                && result.EndsWith("\"", StringComparison.Ordinal)
            )
            {
                result = result.Trim('"');
            }

            return result;
        }

        private static string WrapToColumn(string text, int column)
        {
            if (string.IsNullOrWhiteSpace(text) || column < 20)
            {
                return text;
            }

            string[] lines = text.Split('\n');
            var builder = new StringBuilder(text.Length + 32);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    if (i < lines.Length - 1)
                    {
                        builder.Append('\n');
                    }
                    continue;
                }

                if (line.Length <= column)
                {
                    builder.Append(line);
                    if (i < lines.Length - 1)
                    {
                        builder.Append('\n');
                    }
                    continue;
                }

                int current = 0;
                string[] words = line.Split(' ');
                for (int w = 0; w < words.Length; w++)
                {
                    string word = words[w];
                    if (string.IsNullOrWhiteSpace(word))
                    {
                        continue;
                    }

                    if (current == 0)
                    {
                        builder.Append(word);
                        current = word.Length;
                        continue;
                    }

                    if (current + 1 + word.Length > column)
                    {
                        builder.Append('\n');
                        builder.Append(word);
                        current = word.Length;
                        continue;
                    }

                    builder.Append(' ');
                    builder.Append(word);
                    current += 1 + word.Length;
                }

                if (i < lines.Length - 1)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString().Trim();
        }

        private static string TrimToSentenceLimit(string text, int maxSentences)
        {
            if (string.IsNullOrWhiteSpace(text) || maxSentences <= 0)
            {
                return text;
            }

            int sentenceCount = 0;
            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                builder.Append(c);
                if (c == '.' || c == '!' || c == '?')
                {
                    sentenceCount++;
                    if (sentenceCount >= maxSentences)
                    {
                        if (i < text.Length - 1)
                        {
                            builder.Append(" ...");
                        }
                        break;
                    }
                }
            }

            return builder.ToString().Trim();
        }

        private string ResolveNetworkObjectName(ulong networkObjectId)
        {
            if (networkObjectId == 0 || NetworkManager.Singleton == null)
            {
                return string.Empty;
            }

            if (
                NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    networkObjectId,
                    out NetworkObject networkObject
                )
            )
            {
                return networkObject != null ? networkObject.gameObject.name : string.Empty;
            }

            return string.Empty;
        }

        public static string FormatErrorMessage(string error, bool includeTechnicalCode = false)
        {
            string raw = string.IsNullOrWhiteSpace(error) ? "Dialogue failed." : error.Trim();
            string lower = raw.ToLowerInvariant();
            string friendly;
            switch (lower)
            {
                case "llm_component_missing":
                    friendly = "Dialogue backend is not configured. Check the remote LM Studio settings.";
                    break;
                case "model_not_set":
                    friendly = "No remote model is configured. Set the LM Studio model name.";
                    break;
                case "conversation_in_flight":
                    friendly = "NPC is still responding. Wait a moment and try again.";
                    break;
                case "awaiting_user_message":
                    friendly = "NPC is waiting for your next message.";
                    break;
                case "duplicate_prompt":
                    friendly = "You already sent that. Try a different message.";
                    break;
                case "repeat_delay":
                    friendly = "Please wait a moment before sending another message.";
                    break;
                case "queue_full":
                    friendly = "Dialogue queue is busy. Try again in a few seconds.";
                    break;
                case "not_server":
                    friendly = "Dialogue service is not ready yet. Start host/server first.";
                    break;
                case "request_rejected":
                    friendly = "Dialogue request was rejected. Please try again.";
                    break;
                default:
                    if (lower.StartsWith("model_file_not_found", StringComparison.Ordinal))
                    {
                        string model = raw;
                        int idx = raw.IndexOf(':');
                        if (idx >= 0 && idx + 1 < raw.Length)
                        {
                            model = raw.Substring(idx + 1).Trim();
                        }

                        friendly = $"Configured dialogue model is unavailable ({model}). Check LM Studio.";
                        break;
                    }
                    if (lower.Contains("timed out"))
                    {
                        friendly = "NPC took too long to respond. Please try again.";
                    }
                    else if (lower.Contains("rate limited"))
                    {
                        friendly = "You are sending messages too fast. Please wait briefly.";
                    }
                    else if (lower.Contains("too many active requests"))
                    {
                        friendly = "Too many pending messages. Wait for current responses first.";
                    }
                    else
                    {
                        friendly = raw;
                    }
                    break;
            }

            if (includeTechnicalCode && !string.Equals(friendly, raw, StringComparison.Ordinal))
            {
                return $"{friendly} ({raw})";
            }

            return friendly;
        }

        private void ResolveUiReferences()
        {
            if (m_InputField == null)
            {
                TMP_InputField[] inputFields = GetComponentsInChildren<TMP_InputField>(true);
                for (int i = 0; i < inputFields.Length; i++)
                {
                    TMP_InputField candidate = inputFields[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    string n = candidate.name.ToLowerInvariant();
                    if (n.Contains("login") || n.Contains("auth") || n.Contains("name_id"))
                    {
                        continue;
                    }

                    m_InputField = candidate;
                    break;
                }

                if (m_InputField == null && inputFields.Length > 0)
                {
                    m_InputField = inputFields[0];
                }
            }

            if (m_SendButton == null)
            {
                Button[] buttons = GetComponentsInChildren<Button>(true);
                for (int i = 0; i < buttons.Length; i++)
                {
                    Button candidate = buttons[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    string n = candidate.name.ToLowerInvariant();
                    if (n.Contains("send"))
                    {
                        m_SendButton = candidate;
                        break;
                    }
                }

                if (m_SendButton == null && buttons.Length > 0)
                {
                    m_SendButton = buttons[0];
                }
            }
        }

        private void EnsureReadableLayout()
        {
            if (!m_EnableReadableLayout)
            {
                return;
            }

            RectTransform root = ResolvePanelRootRect();
            if (root == null)
            {
                return;
            }

            TMP_InputField legacyInput = m_InputField;
            Button legacySend = m_SendButton;
            TMP_Text legacyOutput = m_OutputText;

            RectTransform panelRect = FindChildRectTransformRecursive(root, "DialoguePanel_Auto");
            if (panelRect == null)
            {
                panelRect = EnsureChildRectTransform(root, "DialoguePanel_Auto");
            }
            else if (panelRect.parent != root)
            {
                panelRect.SetParent(root, false);
            }

            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = m_ChatPanelOffset;
            panelRect.localScale = Vector3.one;
            panelRect.sizeDelta = ResolvePanelSize(root);
            ApplyLayerRecursively(panelRect, root.gameObject.layer);
            panelRect.SetAsLastSibling();

            Image panelImage = EnsureComponent<Image>(panelRect.gameObject);
            panelImage.color = m_ChatPanelColor;
            Outline panelOutline = EnsureComponent<Outline>(panelRect.gameObject);
            panelOutline.effectColor = m_ChatPanelBorderColor;
            panelOutline.effectDistance = new Vector2(
                m_ChatPanelBorderWidth,
                -m_ChatPanelBorderWidth
            );
            panelOutline.useGraphicAlpha = true;
            VerticalLayoutGroup panelLayout = EnsureComponent<VerticalLayoutGroup>(
                panelRect.gameObject
            );
            panelLayout.padding = new RectOffset(
                Mathf.RoundToInt(m_BlocksSpacingMd),
                Mathf.RoundToInt(m_BlocksSpacingMd),
                Mathf.RoundToInt(m_BlocksSpacingMd),
                Mathf.RoundToInt(m_BlocksSpacingMd)
            );
            panelLayout.spacing = m_BlocksSpacingSm;
            panelLayout.childAlignment = TextAnchor.UpperLeft;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childScaleWidth = false;
            panelLayout.childScaleHeight = false;

            m_ChatPanelRect = panelRect;

            RectTransform headerRect = EnsureChildRectTransform(panelRect, "Header");
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, m_ChatHeaderHeight);
            LayoutElement headerElement = EnsureComponent<LayoutElement>(headerRect.gameObject);
            headerElement.minHeight = m_ChatHeaderHeight;
            headerElement.preferredHeight = m_ChatHeaderHeight;
            headerElement.flexibleHeight = 0f;
            headerElement.flexibleWidth = 1f;
            HorizontalLayoutGroup headerLayout = EnsureComponent<HorizontalLayoutGroup>(
                headerRect.gameObject
            );
            headerLayout.padding = new RectOffset(0, 0, 0, 0);
            headerLayout.spacing = m_BlocksSpacingSm;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = false;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            headerLayout.childScaleWidth = false;
            headerLayout.childScaleHeight = false;

            RectTransform headerTitleRect = EnsureChildRectTransform(headerRect, "Title");
            LayoutElement headerTitleElement = EnsureComponent<LayoutElement>(
                headerTitleRect.gameObject
            );
            headerTitleElement.minWidth = 160f;
            headerTitleElement.preferredWidth = -1f;
            headerTitleElement.flexibleWidth = 1f;
            headerTitleElement.minHeight = m_ChatHeaderHeight;

            m_ChatHeaderText = EnsureComponent<TextMeshProUGUI>(headerTitleRect.gameObject);
            m_ChatHeaderText.fontSize = Mathf.Max(14f, m_OutputFontSize - 4f);
            m_ChatHeaderText.alignment = TextAlignmentOptions.Left;
            m_ChatHeaderText.verticalAlignment = VerticalAlignmentOptions.Middle;
            m_ChatHeaderText.color = m_ChatHeaderColor;
            m_ChatHeaderText.textWrappingMode = TextWrappingModes.NoWrap;
            m_ChatHeaderText.overflowMode = TextOverflowModes.Ellipsis;
            m_ChatHeaderText.richText = true;

            RectTransform headerControlsRect = EnsureChildRectTransform(
                headerRect,
                "HeaderControls"
            );
            HorizontalLayoutGroup controlsLayout = EnsureComponent<HorizontalLayoutGroup>(
                headerControlsRect.gameObject
            );
            controlsLayout.padding = new RectOffset(0, 0, 0, 0);
            controlsLayout.spacing = m_BlocksSpacingSm;
            controlsLayout.childAlignment = TextAnchor.MiddleRight;
            controlsLayout.childControlWidth = false;
            controlsLayout.childControlHeight = true;
            controlsLayout.childForceExpandWidth = false;
            controlsLayout.childForceExpandHeight = false;
            controlsLayout.childScaleWidth = false;
            controlsLayout.childScaleHeight = false;
            LayoutElement controlsElement = EnsureComponent<LayoutElement>(
                headerControlsRect.gameObject
            );
            controlsElement.minHeight = m_ChatHeaderHeight;
            controlsElement.preferredHeight = m_ChatHeaderHeight;
            controlsElement.flexibleWidth = 0f;
            controlsElement.preferredWidth = m_EnableCloseButton ? 266f : 184f;

            float closeButtonWidth = m_EnableCloseButton ? 78f : 0f;
            float targetButtonWidth = 176f;
            RectTransform targetButtonRect = EnsureChildRectTransform(
                headerControlsRect,
                "TargetCycleButton"
            );
            targetButtonRect.sizeDelta = new Vector2(0f, Mathf.Max(24f, m_ChatHeaderHeight - 4f));
            LayoutElement targetButtonLayout = EnsureComponent<LayoutElement>(
                targetButtonRect.gameObject
            );
            targetButtonLayout.minWidth = targetButtonWidth;
            targetButtonLayout.preferredWidth = targetButtonWidth;
            targetButtonLayout.flexibleWidth = 0f;
            targetButtonLayout.minHeight = Mathf.Max(24f, m_ChatHeaderHeight - 4f);

            Image targetButtonImage = EnsureComponent<Image>(targetButtonRect.gameObject);
            targetButtonImage.color = new Color(0.14f, 0.18f, 0.23f, 0.86f);
            m_RecentSpeakerButton = EnsureComponent<Button>(targetButtonRect.gameObject);
            m_RecentSpeakerButton.transition = Selectable.Transition.ColorTint;
            m_RecentSpeakerButton.targetGraphic = targetButtonImage;

            RectTransform targetButtonLabelRect = EnsureChildRectTransform(
                targetButtonRect,
                "TargetCycleLabel"
            );
            targetButtonLabelRect.anchorMin = Vector2.zero;
            targetButtonLabelRect.anchorMax = Vector2.one;
            targetButtonLabelRect.offsetMin = new Vector2(8f, 2f);
            targetButtonLabelRect.offsetMax = new Vector2(-8f, -2f);
            m_RecentSpeakerButtonText = EnsureComponent<TextMeshProUGUI>(
                targetButtonLabelRect.gameObject
            );
            m_RecentSpeakerButtonText.fontSize = Mathf.Max(12f, m_OutputFontSize - 7f);
            m_RecentSpeakerButtonText.alignment = TextAlignmentOptions.MidlineLeft;
            m_RecentSpeakerButtonText.textWrappingMode = TextWrappingModes.NoWrap;
            m_RecentSpeakerButtonText.overflowMode = TextOverflowModes.Ellipsis;
            m_RecentSpeakerButtonText.color = new Color(0.86f, 0.9f, 0.95f, 1f);

            RectTransform closeButtonRect = EnsureChildRectTransform(
                headerControlsRect,
                "CloseButton"
            );
            closeButtonRect.sizeDelta = new Vector2(0f, Mathf.Max(24f, m_ChatHeaderHeight - 4f));
            closeButtonRect.gameObject.SetActive(m_EnableCloseButton);
            LayoutElement closeButtonLayout = EnsureComponent<LayoutElement>(
                closeButtonRect.gameObject
            );
            closeButtonLayout.minWidth = Mathf.Max(64f, closeButtonWidth);
            closeButtonLayout.preferredWidth = Mathf.Max(64f, closeButtonWidth);
            closeButtonLayout.flexibleWidth = 0f;
            closeButtonLayout.minHeight = Mathf.Max(24f, m_ChatHeaderHeight - 4f);

            Image closeButtonImage = EnsureComponent<Image>(closeButtonRect.gameObject);
            closeButtonImage.color = new Color(0.18f, 0.13f, 0.14f, 0.86f);
            m_CloseButton = EnsureComponent<Button>(closeButtonRect.gameObject);
            m_CloseButton.transition = Selectable.Transition.ColorTint;
            m_CloseButton.targetGraphic = closeButtonImage;
            m_CloseButton.interactable = m_EnableCloseButton;

            RectTransform closeButtonLabelRect = EnsureChildRectTransform(
                closeButtonRect,
                "CloseLabel"
            );
            closeButtonLabelRect.anchorMin = Vector2.zero;
            closeButtonLabelRect.anchorMax = Vector2.one;
            closeButtonLabelRect.offsetMin = new Vector2(8f, 2f);
            closeButtonLabelRect.offsetMax = new Vector2(-8f, -2f);
            m_CloseButtonText = EnsureComponent<TextMeshProUGUI>(closeButtonLabelRect.gameObject);
            m_CloseButtonText.fontSize = Mathf.Max(12f, m_OutputFontSize - 7f);
            m_CloseButtonText.alignment = TextAlignmentOptions.Center;
            m_CloseButtonText.textWrappingMode = TextWrappingModes.NoWrap;
            m_CloseButtonText.overflowMode = TextOverflowModes.Ellipsis;
            m_CloseButtonText.color = new Color(0.95f, 0.85f, 0.86f, 1f);
            m_CloseButtonText.text = "Close";

            RectTransform bodyRect = EnsureChildRectTransform(panelRect, "Body");
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 0.5f);
            bodyRect.sizeDelta = Vector2.zero;
            LayoutElement bodyElement = EnsureComponent<LayoutElement>(bodyRect.gameObject);
            bodyElement.minHeight = 120f;
            bodyElement.preferredHeight = -1f;
            bodyElement.flexibleHeight = 1f;
            bodyElement.flexibleWidth = 1f;

            RectTransform scrollRectTransform = EnsureChildRectTransform(
                bodyRect,
                "TranscriptScroll"
            );
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = Vector2.zero;

            m_ChatScrollRect = EnsureComponent<ScrollRect>(scrollRectTransform.gameObject);
            m_ChatScrollRect.horizontal = false;
            m_ChatScrollRect.vertical = true;
            m_ChatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            m_ChatScrollRect.scrollSensitivity = 30f;

            RectTransform viewportRect = EnsureChildRectTransform(scrollRectTransform, "Viewport");
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            Image viewportImage = EnsureComponent<Image>(viewportRect.gameObject);
            viewportImage.color = m_ChatViewportColor;
            Mask viewportMask = EnsureComponent<Mask>(viewportRect.gameObject);
            viewportMask.showMaskGraphic = true;

            RectTransform contentRect = EnsureChildRectTransform(viewportRect, "Content");
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            contentRect.localScale = Vector3.one;

            ContentSizeFitter contentFitter = EnsureComponent<ContentSizeFitter>(
                contentRect.gameObject
            );
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            VerticalLayoutGroup contentLayout = EnsureComponent<VerticalLayoutGroup>(
                contentRect.gameObject
            );
            contentLayout.padding = new RectOffset(0, 0, 0, Mathf.RoundToInt(m_BlocksSpacingSm));
            contentLayout.spacing = 0f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childScaleWidth = false;
            contentLayout.childScaleHeight = false;
            m_ChatContentRect = contentRect;

            RectTransform messageListRect = EnsureChildRectTransform(contentRect, "MessageList");
            messageListRect.anchorMin = new Vector2(0f, 1f);
            messageListRect.anchorMax = new Vector2(1f, 1f);
            messageListRect.pivot = new Vector2(0.5f, 1f);
            messageListRect.anchoredPosition = new Vector2(0f, -m_BlocksSpacingSm);
            messageListRect.sizeDelta = new Vector2(-(m_BlocksSpacingMd), 0f);
            messageListRect.localScale = Vector3.one;
            VerticalLayoutGroup messageListLayout = EnsureComponent<VerticalLayoutGroup>(
                messageListRect.gameObject
            );
            messageListLayout.padding = new RectOffset(0, 0, 0, 0);
            messageListLayout.spacing = m_BlocksSpacingSm;
            messageListLayout.childAlignment = TextAnchor.UpperLeft;
            messageListLayout.childControlWidth = true;
            messageListLayout.childControlHeight = true;
            messageListLayout.childForceExpandWidth = true;
            messageListLayout.childForceExpandHeight = false;
            ContentSizeFitter messageListFitter = EnsureComponent<ContentSizeFitter>(
                messageListRect.gameObject
            );
            messageListFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            messageListFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement messageListElement = EnsureComponent<LayoutElement>(
                messageListRect.gameObject
            );
            messageListElement.minHeight = 0f;
            messageListElement.preferredHeight = -1f;
            messageListElement.flexibleHeight = 0f;
            messageListElement.minWidth = 0f;
            messageListElement.preferredWidth = -1f;
            messageListElement.flexibleWidth = 1f;
            m_ChatMessageListRect = messageListRect;

            RectTransform textRect = EnsureChildRectTransform(contentRect, "TranscriptText");
            m_OutputText = EnsureComponent<TextMeshProUGUI>(textRect.gameObject);
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -m_BlocksSpacingSm);
            textRect.sizeDelta = new Vector2(-(m_BlocksSpacingMd), 0f);
            textRect.localScale = Vector3.one;

            ContentSizeFitter textFitter = EnsureComponent<ContentSizeFitter>(
                m_OutputText.gameObject
            );
            textFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement textLayoutElement = EnsureComponent<LayoutElement>(
                m_OutputText.gameObject
            );
            textLayoutElement.minHeight = 0f;
            textLayoutElement.preferredHeight = -1f;
            textLayoutElement.flexibleHeight = 0f;
            textLayoutElement.minWidth = 0f;
            textLayoutElement.preferredWidth = -1f;
            textLayoutElement.flexibleWidth = 1f;

            m_ChatScrollRect.viewport = viewportRect;
            m_ChatScrollRect.content = contentRect;

            RectTransform footerRect = EnsureChildRectTransform(panelRect, "Footer");
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(0f, m_InputRowHeight);
            LayoutElement footerElement = EnsureComponent<LayoutElement>(footerRect.gameObject);
            footerElement.minHeight = m_InputRowHeight;
            footerElement.preferredHeight = m_InputRowHeight;
            footerElement.flexibleHeight = 0f;
            footerElement.flexibleWidth = 1f;
            HorizontalLayoutGroup footerLayout = EnsureComponent<HorizontalLayoutGroup>(
                footerRect.gameObject
            );
            footerLayout.padding = new RectOffset(0, 0, 0, 0);
            footerLayout.spacing = m_BlocksSpacingSm;
            footerLayout.childAlignment = TextAnchor.MiddleLeft;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = true;
            footerLayout.childScaleWidth = false;
            footerLayout.childScaleHeight = false;

            bool requiresDedicatedControls =
                m_UseDedicatedAutoControls
                || m_InputField == null
                || m_SendButton == null
                || !IsTransformUnder(m_InputField.transform, footerRect)
                || !IsTransformUnder(m_SendButton.transform, footerRect);

            if (requiresDedicatedControls)
            {
                m_InputField = EnsureAutoInputField(footerRect);
                m_SendButton = EnsureAutoSendButton(footerRect);
            }
            else
            {
                RectTransform inputRect = m_InputField.GetComponent<RectTransform>();
                RectTransform sendRect = m_SendButton.GetComponent<RectTransform>();
                if (inputRect != null)
                {
                    inputRect.SetParent(footerRect, false);
                    LayoutElement inputLayout = EnsureComponent<LayoutElement>(
                        m_InputField.gameObject
                    );
                    inputLayout.minWidth = 240f;
                    inputLayout.preferredWidth = -1f;
                    inputLayout.flexibleWidth = 1f;
                    inputLayout.minHeight = m_InputRowHeight;
                    inputLayout.preferredHeight = m_InputRowHeight;
                    inputLayout.flexibleHeight = 0f;
                }

                if (sendRect != null)
                {
                    sendRect.SetParent(footerRect, false);
                    LayoutElement sendLayout = EnsureComponent<LayoutElement>(
                        m_SendButton.gameObject
                    );
                    sendLayout.minWidth = 112f;
                    sendLayout.preferredWidth = 120f;
                    sendLayout.flexibleWidth = 0f;
                    sendLayout.minHeight = m_InputRowHeight;
                    sendLayout.preferredHeight = m_InputRowHeight;
                    sendLayout.flexibleHeight = 0f;
                }
            }

            if (m_UseDedicatedAutoControls || m_HideLegacyDialogueObjects)
            {
                HideLegacyDialogueObjects(panelRect, legacyInput, legacySend, legacyOutput);
            }

            ApplyInputAndSendStyle();
            UpdateRecentSpeakerButtonText();
        }

        private void EnsureOutputText()
        {
            if (m_EnableReadableLayout)
            {
                if (TryResolveReadableTranscriptText(out TMP_Text readableText))
                {
                    m_OutputText = readableText;
                    return;
                }

                EnsureReadableLayout();
                if (TryResolveReadableTranscriptText(out readableText))
                {
                    m_OutputText = readableText;
                    return;
                }
            }

            if (m_OutputText != null)
            {
                return;
            }

            var outputGo = new GameObject(
                "OutputText (TMP)",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );
            outputGo.transform.SetParent(transform, false);

            var rect = outputGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(860f, 280f);

            var text = outputGo.GetComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = m_OutputFontSize;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = new Color(0.93f, 0.93f, 0.93f, 1f);
            text.richText = m_UseRichText;

            m_OutputText = text;
        }

        private bool TryResolveReadableTranscriptText(out TMP_Text outputText)
        {
            outputText = null;
            RectTransform root = ResolvePanelRootRect();
            if (root == null)
            {
                return false;
            }

            RectTransform panelRect = FindChildRectTransformRecursive(root, "DialoguePanel_Auto");
            if (panelRect == null)
            {
                return false;
            }

            RectTransform textRect = FindChildRectTransformRecursive(panelRect, "TranscriptText");
            if (textRect == null)
            {
                return false;
            }

            outputText = EnsureComponent<TextMeshProUGUI>(textRect.gameObject);
            return outputText != null;
        }

        private void ApplyOutputTextStyle()
        {
            if (!m_StyleOutputText || m_OutputText == null)
            {
                return;
            }

            bool outputInAutoPanel =
                m_ChatPanelRect != null
                && IsTransformUnder(m_OutputText.transform, m_ChatPanelRect);

            m_OutputText.fontSize = Mathf.Max(12f, m_OutputFontSize);
            m_OutputText.alignment = TextAlignmentOptions.TopLeft;
            m_OutputText.textWrappingMode = TextWrappingModes.Normal;
            m_OutputText.overflowMode = outputInAutoPanel
                ? TextOverflowModes.Overflow
                : TextOverflowModes.Truncate;
            m_OutputText.richText = m_UseRichText;
            m_OutputText.raycastTarget = false;
            m_OutputText.enableAutoSizing = false;
            m_OutputText.color = new Color(0.92f, 0.95f, 0.98f, 0.98f);

            RectTransform rect = m_OutputText.rectTransform;
            if (rect != null)
            {
                if (outputInAutoPanel)
                {
                    rect.sizeDelta = Vector2.zero;
                }
                else
                {
                    Vector2 size = rect.sizeDelta;
                    size.x = Mathf.Max(size.x, m_MinOutputSize.x);
                    size.y = Mathf.Max(size.y, m_MinOutputSize.y);
                    rect.sizeDelta = size;
                }
            }

            UpdateHeaderText();
        }

        private TMP_InputField EnsureAutoInputField(RectTransform footerRect)
        {
            RectTransform inputRect = EnsureChildRectTransform(footerRect, "DialogueInputField");
            inputRect.localScale = Vector3.one;
            inputRect.sizeDelta = Vector2.zero;

            LayoutElement inputLayout = EnsureComponent<LayoutElement>(inputRect.gameObject);
            inputLayout.minWidth = 240f;
            inputLayout.preferredWidth = -1f;
            inputLayout.flexibleWidth = 1f;
            inputLayout.minHeight = m_InputRowHeight;
            inputLayout.preferredHeight = m_InputRowHeight;
            inputLayout.flexibleHeight = 0f;

            Image inputImage = EnsureComponent<Image>(inputRect.gameObject);
            inputImage.color = new Color(1f, 1f, 1f, 0.95f);
            Outline inputOutline = EnsureComponent<Outline>(inputRect.gameObject);
            inputOutline.effectColor = new Color(0.12f, 0.16f, 0.2f, 0.28f);
            inputOutline.effectDistance = new Vector2(1f, -1f);

            TMP_InputField input = EnsureComponent<TMP_InputField>(inputRect.gameObject);
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.richText = false;
            input.caretWidth = 2;
            input.characterLimit = Mathf.Max(180, m_MaxMessageCharacters * 3);
            input.resetOnDeActivation = false;
            input.restoreOriginalTextOnEscape = false;

            RectTransform textAreaRect = EnsureChildRectTransform(inputRect, "Text Area");
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.pivot = new Vector2(0.5f, 0.5f);
            textAreaRect.offsetMin = new Vector2(14f, 9f);
            textAreaRect.offsetMax = new Vector2(-14f, -9f);
            textAreaRect.localScale = Vector3.one;
            EnsureComponent<RectMask2D>(textAreaRect.gameObject);

            RectTransform textRect = EnsureChildRectTransform(textAreaRect, "Text");
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textRect.localScale = Vector3.one;
            TextMeshProUGUI text = EnsureComponent<TextMeshProUGUI>(textRect.gameObject);
            text.text = string.Empty;
            text.fontSize = Mathf.Max(14f, m_InputFontSize);
            text.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;

            RectTransform placeholderRect = EnsureChildRectTransform(textAreaRect, "Placeholder");
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.pivot = new Vector2(0f, 0.5f);
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            placeholderRect.localScale = Vector3.one;
            TextMeshProUGUI placeholder = EnsureComponent<TextMeshProUGUI>(
                placeholderRect.gameObject
            );
            placeholder.text = "Enter text...";
            placeholder.fontSize = Mathf.Max(12f, m_InputFontSize - 3f);
            placeholder.color = new Color(0.26f, 0.26f, 0.26f, 0.78f);
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.richText = false;
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.overflowMode = TextOverflowModes.Ellipsis;

            input.textViewport = textAreaRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        private Button EnsureAutoSendButton(RectTransform footerRect)
        {
            RectTransform sendRect = EnsureChildRectTransform(footerRect, "SendButton");
            sendRect.localScale = Vector3.one;
            sendRect.sizeDelta = Vector2.zero;

            LayoutElement sendLayout = EnsureComponent<LayoutElement>(sendRect.gameObject);
            sendLayout.minWidth = 112f;
            sendLayout.preferredWidth = 120f;
            sendLayout.flexibleWidth = 0f;
            sendLayout.minHeight = m_InputRowHeight;
            sendLayout.preferredHeight = m_InputRowHeight;
            sendLayout.flexibleHeight = 0f;

            Image sendImage = EnsureComponent<Image>(sendRect.gameObject);
            sendImage.color = new Color(0.95f, 0.95f, 0.95f, 0.96f);
            Outline sendOutline = EnsureComponent<Outline>(sendRect.gameObject);
            sendOutline.effectColor = new Color(0.12f, 0.16f, 0.2f, 0.2f);
            sendOutline.effectDistance = new Vector2(1f, -1f);

            Button sendButton = EnsureComponent<Button>(sendRect.gameObject);
            sendButton.transition = Selectable.Transition.ColorTint;
            sendButton.targetGraphic = sendImage;

            RectTransform labelRect = EnsureChildRectTransform(sendRect, "Label");
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(6f, 2f);
            labelRect.offsetMax = new Vector2(-6f, -2f);
            labelRect.localScale = Vector3.one;
            TextMeshProUGUI label = EnsureComponent<TextMeshProUGUI>(labelRect.gameObject);
            label.text = "Send";
            label.fontSize = Mathf.Max(16f, m_InputFontSize - 2f);
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            label.richText = false;

            return sendButton;
        }

        private void HideLegacyDialogueObjects(
            RectTransform panelRect,
            TMP_InputField legacyInput,
            Button legacySend,
            TMP_Text legacyOutput
        )
        {
            if (
                legacyInput != null
                && legacyInput != m_InputField
                && !IsTransformUnder(legacyInput.transform, panelRect)
            )
            {
                DisableOrDestroyLegacyObject(legacyInput.gameObject);
            }

            if (
                legacySend != null
                && legacySend != m_SendButton
                && !IsTransformUnder(legacySend.transform, panelRect)
            )
            {
                DisableOrDestroyLegacyObject(legacySend.gameObject);
            }

            if (
                legacyOutput != null
                && legacyOutput != m_OutputText
                && !IsTransformUnder(legacyOutput.transform, panelRect)
            )
            {
                DisableOrDestroyLegacyObject(legacyOutput.gameObject);
            }

            Transform cleanupRoot = ResolveLegacyCleanupRoot(panelRect);
            if (cleanupRoot == null)
            {
                return;
            }

            TMP_InputField[] allInputFields = cleanupRoot.GetComponentsInChildren<TMP_InputField>(
                true
            );
            for (int i = 0; i < allInputFields.Length; i++)
            {
                TMP_InputField candidate = allInputFields[i];
                if (
                    candidate == null
                    || candidate == m_InputField
                    || IsTransformUnder(candidate.transform, panelRect)
                )
                {
                    continue;
                }

                DisableOrDestroyLegacyObject(candidate.gameObject);
            }

            Button[] allButtons = cleanupRoot.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < allButtons.Length; i++)
            {
                Button candidate = allButtons[i];
                if (
                    candidate == null
                    || candidate == m_SendButton
                    || candidate == m_RecentSpeakerButton
                    || candidate == m_CloseButton
                    || IsTransformUnder(candidate.transform, panelRect)
                )
                {
                    continue;
                }

                DisableOrDestroyLegacyObject(candidate.gameObject);
            }

            TMP_Text[] allTexts = cleanupRoot.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < allTexts.Length; i++)
            {
                TMP_Text candidate = allTexts[i];
                if (
                    candidate == null
                    || candidate == m_OutputText
                    || candidate == m_ChatHeaderText
                    || candidate == m_RecentSpeakerButtonText
                    || candidate == m_CloseButtonText
                    || IsTransformUnder(candidate.transform, panelRect)
                )
                {
                    continue;
                }

                DisableOrDestroyLegacyObject(candidate.gameObject);
            }

            RectTransform legacyPanelRect = cleanupRoot as RectTransform;
            if (
                legacyPanelRect != null
                && panelRect != null
                && legacyPanelRect != panelRect
                && !IsTransformUnder(panelRect, legacyPanelRect)
            )
            {
                Image legacyPanelImage = legacyPanelRect.GetComponent<Image>();
                if (legacyPanelImage != null)
                {
                    legacyPanelImage.enabled = false;
                    legacyPanelImage.raycastTarget = false;
                }

                Outline legacyPanelOutline = legacyPanelRect.GetComponent<Outline>();
                if (legacyPanelOutline != null)
                {
                    legacyPanelOutline.enabled = false;
                }
            }
        }

        private Transform ResolveLegacyCleanupRoot(RectTransform panelRect)
        {
            Transform panelRoot = panelRect != null ? panelRect.parent : null;
            if (panelRoot == null)
            {
                return transform;
            }

            RectTransform legacyPanelRect = FindChildRectTransformRecursive(panelRoot, "Panel");
            if (
                legacyPanelRect != null
                && legacyPanelRect != panelRect
                && !IsTransformUnder(panelRect, legacyPanelRect)
            )
            {
                return legacyPanelRect;
            }

            return transform;
        }

        private static void DisableOrDestroyLegacyObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
                return;
            }

            target.SetActive(false);
        }

        private static bool IsTransformUnder(Transform child, Transform ancestor)
        {
            if (child == null || ancestor == null)
            {
                return false;
            }

            Transform current = child;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }
                current = current.parent;
            }

            return false;
        }

        private static void ApplyLayerRecursively(Transform root, int layer)
        {
            if (root == null)
            {
                return;
            }

            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                ApplyLayerRecursively(root.GetChild(i), layer);
            }
        }

        private void UpdateHeaderText()
        {
            if (m_ChatHeaderText == null)
            {
                return;
            }

            if (!m_ShowListenerInHeader)
            {
                m_ChatHeaderText.text = "<b>Dialogue</b>";
                UpdateRecentSpeakerButtonText();
                return;
            }

            string listenerName = "No target";
            if (m_Listener != null)
            {
                listenerName = m_Listener.name;
            }

            string mode = m_UseManualListenerSelection ? "Manual" : "Auto";
            m_ChatHeaderText.text =
                $"<b>Dialogue</b>  <color=#9FB5C8>Target:</color> {listenerName}  <color=#7E91A2>({mode})</color>";
            UpdateRecentSpeakerButtonText();
        }

        public void ToggleDialoguePanel()
        {
            SetDialogueVisible(!m_DialogueVisible);
        }

        public void OpenDialoguePanel()
        {
            SetDialogueVisible(true);
        }

        public void CloseDialoguePanel()
        {
            SetDialogueVisible(false);
        }

        public void SetDialogueVisible(bool visible)
        {
            SetDialogueVisible(visible, false);
        }

        private void SetDialogueVisible(bool visible, bool force)
        {
            if (!force && m_DialogueVisible == visible)
            {
                return;
            }

            m_DialogueVisible = visible;
            if (m_ChatPanelRect != null)
            {
                m_ChatPanelRect.gameObject.SetActive(visible);
            }
            else if (m_InputField != null)
            {
                m_InputField.gameObject.SetActive(visible);
            }

            if (visible && m_InputField != null)
            {
                m_InputField.ActivateInputField();
            }

            UpdateSendButtonState();
            ApplyInputModeForDialogueState(visible);
            OnDialogueVisibilityChanged?.Invoke(visible);
        }

        private void ApplyInputModeForDialogueState(bool dialogueOpen)
        {
            if (dialogueOpen)
            {
                SuppressPlayerInput();
            }
            else
            {
                RestorePlayerInput();
            }
        }

        private void SuppressPlayerInput()
        {
            if (m_WasInputSuppressed)
            {
                return;
            }

            m_WasInputSuppressed = true;
            SetPlayerGameplayInputEnabled(false);
        }

        private void RestorePlayerInput()
        {
            if (!m_WasInputSuppressed)
            {
                return;
            }

            m_WasInputSuppressed = false;
            if (m_InputField != null)
            {
                m_InputField.DeactivateInputField();
            }

            SetPlayerGameplayInputEnabled(true);
        }

        private void HandleVisibilityInput()
        {
            bool togglePressed = IsVisibilityKeyPressed(m_ToggleKey);
            if (togglePressed)
            {
                ToggleDialoguePanel();
                return;
            }

            bool closePressed = IsVisibilityKeyPressed(m_CloseKey);
            if (m_DialogueVisible && closePressed)
            {
                CloseDialoguePanel();
            }
        }

        private static bool IsVisibilityKeyPressed(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return keyCode switch
            {
                KeyCode.Escape => keyboard.escapeKey.wasPressedThisFrame,
                KeyCode.BackQuote => keyboard.backquoteKey.wasPressedThisFrame,
                KeyCode.Backspace => keyboard.backspaceKey.wasPressedThisFrame,
                KeyCode.Tab => keyboard.tabKey.wasPressedThisFrame,
                KeyCode.Return => keyboard.enterKey.wasPressedThisFrame,
                KeyCode.Space => keyboard.spaceKey.wasPressedThisFrame,
                KeyCode.F1 => keyboard.f1Key.wasPressedThisFrame,
                KeyCode.F2 => keyboard.f2Key.wasPressedThisFrame,
                KeyCode.F3 => keyboard.f3Key.wasPressedThisFrame,
                KeyCode.F4 => keyboard.f4Key.wasPressedThisFrame,
                KeyCode.F5 => keyboard.f5Key.wasPressedThisFrame,
                KeyCode.F6 => keyboard.f6Key.wasPressedThisFrame,
                KeyCode.F7 => keyboard.f7Key.wasPressedThisFrame,
                KeyCode.F8 => keyboard.f8Key.wasPressedThisFrame,
                KeyCode.Alpha1 => keyboard.digit1Key.wasPressedThisFrame,
                KeyCode.Alpha2 => keyboard.digit2Key.wasPressedThisFrame,
                KeyCode.Alpha3 => keyboard.digit3Key.wasPressedThisFrame,
                KeyCode.Alpha4 => keyboard.digit4Key.wasPressedThisFrame,
                _ => false,
            };
#else
            return false;
#endif
        }

        private void HandlePlayerInputSuppression()
        {
            bool dialogueWantsSuppress = m_DialogueVisible;
            bool fieldFocused = m_InputField != null && m_InputField.isFocused;
            bool shouldSuppress = dialogueWantsSuppress || fieldFocused;
            if (shouldSuppress && !m_WasInputSuppressed)
            {
                SuppressPlayerInput();
            }
            else if (!shouldSuppress && m_WasInputSuppressed)
            {
                RestorePlayerInput();
            }
        }

        private void SetPlayerGameplayInputEnabled(bool enabled)
        {
            // Find the local player's StarterAssetsInputs and suppress movement/look by zeroing input values.
            // This keeps the PlayerInput component active so UI mouse clicks still work.
            StarterAssetsInputs inputs = ResolveLocalPlayerInputs();

            if (enabled)
            {
                // Restore cursor and gameplay input
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                if (inputs != null)
                {
                    inputs.cursorLocked = true;
                    inputs.cursorInputForLook = true;
                    // Movement and look will be restored by StarterAssetsInputs receiving input events again
                }
            }
            else
            {
                // Unlock cursor for UI interaction
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                if (inputs != null)
                {
                    inputs.cursorLocked = false;
                    inputs.cursorInputForLook = false;
                    // Zero out movement/look to prevent player from moving while in dialogue UI
                    inputs.MoveInput(Vector2.zero);
                    inputs.LookInput(Vector2.zero);
                }
            }
        }

        // Cached reference for ResolveLocalPlayerInputs
        private StarterAssetsInputs m_CachedStarterInputs;
        private float m_NextInputResolveTime = float.MinValue;
        private const float kInputResolveInterval = 0.5f;

        /// <summary>
        /// Resolves the local player's StarterAssetsInputs component for input control.
        /// Uses caching and fallback to FindObjectsOfType for robustness.
        /// </summary>
        private StarterAssetsInputs ResolveLocalPlayerInputs()
        {
            // Use cached result if recent
            if (m_CachedStarterInputs != null)
            {
                return m_CachedStarterInputs;
            }

            // Throttle resolution attempts
            if (Time.unscaledTime < m_NextInputResolveTime)
            {
                return null;
            }

            m_NextInputResolveTime = Time.unscaledTime + kInputResolveInterval;

            // Try NetworkManager.LocalClient.PlayerObject first
            if (
                NetworkManager.Singleton != null
                && NetworkManager.Singleton.LocalClient != null
                && NetworkManager.Singleton.LocalClient.PlayerObject != null
            )
            {
                m_CachedStarterInputs =
                    NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<StarterAssetsInputs>();
                if (m_CachedStarterInputs != null)
                {
                    return m_CachedStarterInputs;
                }
            }

            // Fallback: find any StarterAssetsInputs in scene
#if UNITY_2023_1_OR_NEWER
            var allInputs = FindObjectsByType<StarterAssetsInputs>(FindObjectsInactive.Exclude);
#else
            var allInputs = FindObjectsOfType<StarterAssetsInputs>();
#endif
            if (allInputs != null && allInputs.Length > 0)
            {
                m_CachedStarterInputs = allInputs[0];
            }

            return m_CachedStarterInputs;
        }

        private void UpdateRecentSpeakerButtonText()
        {
            if (m_RecentSpeakerButton == null || m_RecentSpeakerButtonText == null)
            {
                return;
            }

            if (m_RecentSpeakerIds.Count == 0)
            {
                m_RecentSpeakerButton.interactable = false;
                m_RecentSpeakerButtonText.text = "Target: Auto";
                return;
            }

            m_RecentSpeakerButton.interactable = true;
            if (!m_UseManualListenerSelection || m_ManualListenerNetworkId == 0)
            {
                m_RecentSpeakerButtonText.text = "Target: Auto";
                return;
            }

            if (TryGetSpawnedObject(m_ManualListenerNetworkId, out NetworkObject listener))
            {
                m_RecentSpeakerButtonText.text = $"Target: {listener.name}";
                return;
            }

            if (
                m_RecentSpeakerNames.TryGetValue(m_ManualListenerNetworkId, out string listenerName)
            )
            {
                m_RecentSpeakerButtonText.text = $"Target: {listenerName}";
                return;
            }

            m_RecentSpeakerButtonText.text = "Target: Auto";
        }

        private void CycleRecentSpeaker()
        {
            if (m_RecentSpeakerIds.Count == 0)
            {
                m_UseManualListenerSelection = false;
                m_ManualListenerNetworkId = 0;
                UpdateHeaderText();
                return;
            }

            int states = m_RecentSpeakerIds.Count + 1;
            m_RecentSpeakerCycleIndex = (m_RecentSpeakerCycleIndex + 1) % states;

            if (m_RecentSpeakerCycleIndex == 0)
            {
                m_UseManualListenerSelection = false;
                m_ManualListenerNetworkId = 0;
                NGLog.Info("DialogueUI", "Target selection set to Auto.");
            }
            else
            {
                ulong selectedId = m_RecentSpeakerIds[m_RecentSpeakerCycleIndex - 1];
                m_UseManualListenerSelection = true;
                m_ManualListenerNetworkId = selectedId;
                NGLog.Info(
                    "DialogueUI",
                    NGLog.Format("Target selection set to Manual", ("listenerId", selectedId))
                );
            }

            ResolveParticipants();
        }

        private void TrackRecentSpeaker(ulong speakerNetworkId, string speakerName)
        {
            if (speakerNetworkId == 0)
            {
                return;
            }

            if (m_Speaker != null && m_Speaker.NetworkObjectId == speakerNetworkId)
            {
                return;
            }

            m_RecentSpeakerIds.Remove(speakerNetworkId);
            m_RecentSpeakerIds.Insert(0, speakerNetworkId);

            int maxRecent = Mathf.Max(1, m_MaxRecentSpeakers);
            while (m_RecentSpeakerIds.Count > maxRecent)
            {
                ulong removed = m_RecentSpeakerIds[m_RecentSpeakerIds.Count - 1];
                m_RecentSpeakerIds.RemoveAt(m_RecentSpeakerIds.Count - 1);
                if (m_RecentSpeakerNames.ContainsKey(removed))
                {
                    m_RecentSpeakerNames.Remove(removed);
                }
            }

            m_RecentSpeakerNames[speakerNetworkId] = string.IsNullOrWhiteSpace(speakerName)
                ? $"NPC {speakerNetworkId}"
                : speakerName;
            if (m_RecentSpeakerCycleIndex > m_RecentSpeakerIds.Count)
            {
                m_RecentSpeakerCycleIndex = 0;
            }
            UpdateRecentSpeakerButtonText();
        }

        private static bool TryGetSpawnedObject(
            ulong networkObjectId,
            out NetworkObject networkObject
        )
        {
            networkObject = null;
            var manager = NetworkManager.Singleton;
            if (networkObjectId == 0 || manager == null || manager.SpawnManager == null)
            {
                return false;
            }

            if (
                !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out networkObject)
                || networkObject == null
                || !networkObject.IsSpawned
            )
            {
                networkObject = null;
                return false;
            }

            return true;
        }

        private static RectTransform EnsureChildRectTransform(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child == null)
            {
                var go = new GameObject(childName, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.layer = parent.gameObject.layer;
                child = go.transform;
            }
            else if (child.gameObject.layer != parent.gameObject.layer)
            {
                child.gameObject.layer = parent.gameObject.layer;
            }

            return (RectTransform)child;
        }

        private static T EnsureComponent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }

            return component;
        }

        private void NormalizeRuntimePresentationSettings()
        {
            // Keep dialogue panel behavior stable even when scene overrides drift.
            m_EnableReadableLayout = true;
            m_UseDedicatedAutoControls = true;
            m_HideLegacyDialogueObjects = true;
            m_UseBubbleMessages = false;
            m_EnableCloseButton = true;
            m_StartHidden = false;

            m_MaxMessageCharacters = Mathf.Clamp(m_MaxMessageCharacters, 140, 320);
            m_WrapColumn = Mathf.Clamp(m_WrapColumn, 42, 68);
            m_MaxSentencesPerMessage = Mathf.Clamp(m_MaxSentencesPerMessage, 1, 3);
            m_OutputFontSize = Mathf.Clamp(m_OutputFontSize, 18f, 22f);
            m_InputFontSize = Mathf.Clamp(m_InputFontSize, 18f, 26f);
            m_BubbleMaxWidth = Mathf.Clamp(m_BubbleMaxWidth, 280f, 420f);

            m_ChatPanelSize.x = Mathf.Clamp(m_ChatPanelSize.x, 620f, 920f);
            m_ChatPanelSize.y = Mathf.Clamp(m_ChatPanelSize.y, 260f, 420f);
            m_ChatPanelOffset.x = 0f;
            m_ChatPanelOffset.y = Mathf.Max(16f, m_ChatPanelOffset.y);
            m_InputRowHeight = Mathf.Clamp(m_InputRowHeight, 46f, 72f);
            m_BlocksSpacingSm = Mathf.Clamp(m_BlocksSpacingSm, 8f, 12f);
            m_BlocksSpacingMd = Mathf.Clamp(m_BlocksSpacingMd, 14f, 20f);

            // Keep dialogue visuals consistent with Auth/Session blocks style.
            m_ChatPanelColor = new Color(0.05f, 0.06f, 0.08f, 0.42f);
            m_ChatViewportColor = new Color(0.02f, 0.03f, 0.04f, 0.16f);
            m_ChatHeaderColor = new Color(0.93f, 0.93f, 0.93f, 1f);
            m_ChatPanelBorderColor = new Color(0.82f, 0.84f, 0.88f, 0.34f);
            m_ChatPanelBorderWidth = Mathf.Clamp(m_ChatPanelBorderWidth, 1f, 1.5f);
        }

        private void ApplyInputAndSendStyle()
        {
            if (m_InputField != null)
            {
                m_InputField.pointSize = Mathf.Max(14f, m_InputFontSize);
                TMP_Text inputText = m_InputField.textComponent;
                if (inputText != null)
                {
                    inputText.fontSize = Mathf.Max(14f, m_InputFontSize);
                    inputText.color = new Color(0.08f, 0.08f, 0.08f, 1f);
                    inputText.richText = false;
                    inputText.textWrappingMode = TextWrappingModes.NoWrap;
                }

                if (m_InputField.placeholder is TMP_Text placeholderText)
                {
                    placeholderText.fontSize = Mathf.Max(12f, m_InputFontSize - 3f);
                    placeholderText.color = new Color(0.26f, 0.26f, 0.26f, 0.85f);
                }

                Image inputImage = m_InputField.GetComponent<Image>();
                if (inputImage != null)
                {
                    inputImage.color = new Color(1f, 1f, 1f, 0.95f);
                }
            }

            if (m_SendButton != null)
            {
                TMP_Text tmpLabel = m_SendButton.GetComponentInChildren<TMP_Text>(true);
                if (tmpLabel != null)
                {
                    tmpLabel.fontSize = Mathf.Max(16f, m_InputFontSize - 2f);
                    tmpLabel.fontStyle = FontStyles.Bold;
                }

                Text legacyLabel = m_SendButton.GetComponentInChildren<Text>(true);
                if (legacyLabel != null)
                {
                    legacyLabel.fontSize = Mathf.RoundToInt(Mathf.Max(16f, m_InputFontSize - 2f));
                    legacyLabel.fontStyle = FontStyle.Bold;
                }
            }
        }

        private void NormalizeLegacyContainerLayout()
        {
            LayoutGroup rootLayout = GetComponent<LayoutGroup>();
            if (rootLayout != null && rootLayout.enabled)
            {
                rootLayout.enabled = false;
            }

            ContentSizeFitter rootFitter = GetComponent<ContentSizeFitter>();
            if (rootFitter != null && rootFitter.enabled)
            {
                rootFitter.enabled = false;
            }
        }

        private RectTransform ResolvePanelRootRect()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                RectTransform canvasRect = canvas.GetComponent<RectTransform>();
                if (canvasRect != null)
                {
                    return canvasRect;
                }
            }

            return transform as RectTransform;
        }

        private static RectTransform FindChildRectTransformRecursive(
            Transform root,
            string childName
        )
        {
            if (root == null)
            {
                return null;
            }

            RectTransform[] allRects = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < allRects.Length; i++)
            {
                RectTransform rect = allRects[i];
                if (rect != null && string.Equals(rect.name, childName, StringComparison.Ordinal))
                {
                    return rect;
                }
            }

            return null;
        }

        private Vector2 ResolvePanelSize(RectTransform root)
        {
            if (root == null)
            {
                return m_ChatPanelSize;
            }

            float canvasWidth = Mathf.Max(320f, root.rect.width);
            float canvasHeight = Mathf.Max(240f, root.rect.height);
            float maxWidth = Mathf.Max(520f, canvasWidth - (m_BlocksSpacingMd * 2f));
            float maxHeight = Mathf.Max(240f, canvasHeight - (m_BlocksSpacingMd * 2f));
            float targetWidth = Mathf.Clamp(canvasWidth * 0.56f, 620f, maxWidth);
            float targetHeight = Mathf.Clamp(canvasHeight * 0.36f, 280f, maxHeight);
            return new Vector2(
                Mathf.Clamp(Mathf.Max(m_ChatPanelSize.x, targetWidth), 620f, maxWidth),
                Mathf.Clamp(Mathf.Max(m_ChatPanelSize.y, targetHeight), 280f, maxHeight)
            );
        }

        private void ResolveParticipants()
        {
            var manager = NetworkManager.Singleton;

            if (m_Speaker == null || !m_Speaker.IsSpawned)
            {
                if (manager != null && manager.LocalClient?.PlayerObject != null)
                {
                    m_Speaker = manager.LocalClient.PlayerObject;
                }
            }

            if (m_UseManualListenerSelection && m_ManualListenerNetworkId != 0)
            {
                if (
                    TryGetSpawnedObject(m_ManualListenerNetworkId, out NetworkObject manualListener)
                )
                {
                    m_Listener = manualListener;
                    m_LastResolvedListenerId = manualListener.NetworkObjectId;
                    UpdateHeaderText();
                    return;
                }

                m_UseManualListenerSelection = false;
                m_ManualListenerNetworkId = 0;
                m_RecentSpeakerCycleIndex = 0;
            }

            if (m_AutoSelectListener)
            {
                NetworkObject selected = SelectBestListener(m_Speaker);
                if (
                    selected != null
                    && (
                        m_Listener == null || m_Listener.NetworkObjectId != selected.NetworkObjectId
                    )
                )
                {
                    m_Listener = selected;
                    if (m_LastResolvedListenerId != selected.NetworkObjectId)
                    {
                        m_LastResolvedListenerId = selected.NetworkObjectId;
                        NGLog.Info(
                            "DialogueUI",
                            NGLog.Format(
                                "Auto listener selected",
                                ("listener", selected.name),
                                ("id", selected.NetworkObjectId)
                            )
                        );
                    }
                    UpdateHeaderText();
                    return;
                }
            }

            if (m_Listener == null || !m_Listener.IsSpawned)
            {
                GameObject npc = GameObject.FindGameObjectWithTag("NPC");
                if (npc != null)
                {
                    var npcNetObj = npc.GetComponent<NetworkObject>();
                    if (npcNetObj != null)
                    {
                        m_Listener = npcNetObj;
                        m_LastResolvedListenerId = npcNetObj.NetworkObjectId;
                    }
                }
            }

            UpdateHeaderText();
        }

        private NetworkObject SelectBestListener(NetworkObject speaker)
        {
            List<NetworkObject> candidates = CollectNpcCandidates(speaker);
            if (candidates.Count == 0)
            {
                return null;
            }

            if (m_PrioritizeAimedNpc && TryGetAimedNpc(candidates, out NetworkObject aimedNpc))
            {
                return aimedNpc;
            }

            return GetNearestNpc(speaker, candidates);
        }

        private List<NetworkObject> CollectNpcCandidates(NetworkObject speaker)
        {
            var candidates = new List<NetworkObject>();
            GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
            if (npcs == null || npcs.Length == 0)
            {
                return candidates;
            }

            for (int i = 0; i < npcs.Length; i++)
            {
                GameObject npc = npcs[i];
                if (npc == null)
                {
                    continue;
                }

                NetworkObject netObj = npc.GetComponent<NetworkObject>();
                if (netObj == null || !netObj.IsSpawned)
                {
                    continue;
                }

                if (speaker != null && netObj.NetworkObjectId == speaker.NetworkObjectId)
                {
                    continue;
                }

                candidates.Add(netObj);
            }

            return candidates;
        }

        private bool TryGetAimedNpc(List<NetworkObject> candidates, out NetworkObject aimedNpc)
        {
            aimedNpc = null;
            Camera cam = Camera.main;
            if (cam == null || candidates == null || candidates.Count == 0)
            {
                return false;
            }

            float maxDistance = m_MaxListenerDistance > 0f ? m_MaxListenerDistance : 1000f;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                maxDistance,
                m_ListenerRaycastMask,
                QueryTriggerInteraction.Ignore
            );
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                NetworkObject target = hit.collider.GetComponentInParent<NetworkObject>();
                if (target == null || !target.IsSpawned || !target.CompareTag("NPC"))
                {
                    continue;
                }

                for (int c = 0; c < candidates.Count; c++)
                {
                    if (
                        candidates[c] != null
                        && candidates[c].NetworkObjectId == target.NetworkObjectId
                    )
                    {
                        aimedNpc = target;
                        return true;
                    }
                }
            }

            return false;
        }

        private NetworkObject GetNearestNpc(NetworkObject speaker, List<NetworkObject> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            Vector3 origin = Vector3.zero;
            bool hasOrigin = false;
            if (speaker != null)
            {
                origin = speaker.transform.position;
                hasOrigin = true;
            }
            else if (Camera.main != null)
            {
                origin = Camera.main.transform.position;
                hasOrigin = true;
            }

            NetworkObject nearest = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                NetworkObject candidate = candidates[i];
                if (candidate == null || !candidate.IsSpawned)
                {
                    continue;
                }

                float distance = 0f;
                if (hasOrigin)
                {
                    distance = Vector3.Distance(origin, candidate.transform.position);
                    if (m_MaxListenerDistance > 0f && distance > m_MaxListenerDistance)
                    {
                        continue;
                    }
                }

                if (nearest == null || distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private void AddPendingPlaceholder(int requestId, ulong speakerNetworkId)
        {
            string npcName = ResolveNetworkObjectName(speakerNetworkId);
            if (string.IsNullOrWhiteSpace(npcName))
            {
                npcName = "NPC";
            }

            AddTranscriptLine("Thinking...", requestId, true, TranscriptRole.System, npcName);
        }

        private void RemovePendingPlaceholder(int requestId)
        {
            if (requestId <= 0 || m_Transcript.Count == 0)
            {
                return;
            }

            for (int i = m_Transcript.Count - 1; i >= 0; i--)
            {
                TranscriptLine line = m_Transcript[i];
                if (line != null && line.IsPending && line.ClientRequestId == requestId)
                {
                    m_Transcript.RemoveAt(i);
                    break;
                }
            }

            RenderTranscript();
        }

        private void AddTranscriptLine(
            string text,
            int clientRequestId = 0,
            bool isPending = false,
            TranscriptRole role = TranscriptRole.System,
            string speakerLabel = null
        )
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            m_Transcript.Add(
                new TranscriptLine
                {
                    Text = text,
                    SpeakerLabel = speakerLabel,
                    Role = role,
                    ClientRequestId = clientRequestId,
                    IsPending = isPending,
                }
            );

            TrimTranscript();
            RenderTranscript();
        }

        private void ReplaceTranscriptWith(
            string text,
            TranscriptRole role = TranscriptRole.System,
            string speakerLabel = null
        )
        {
            m_Transcript.Clear();
            AddTranscriptLine(text, 0, false, role, speakerLabel);
        }

        private void TrimTranscript()
        {
            int maxLines = Mathf.Max(4, m_MaxTranscriptMessages);
            while (m_Transcript.Count > maxLines)
            {
                m_Transcript.RemoveAt(0);
            }
        }

        private void RenderTranscript()
        {
            EnsureOutputText();
            if (m_OutputText == null)
            {
                return;
            }

            if (m_UseBubbleMessages && m_EnableReadableLayout && m_ChatMessageListRect != null)
            {
                SetTranscriptLayoutMode(true);
                RenderBubbleTranscript();
                if (m_AutoScrollToLatest)
                {
                    QueueAutoScrollToBottom();
                }
                return;
            }

            SetTranscriptLayoutMode(false);
            if (m_ChatMessageListRect != null)
            {
                m_ChatMessageListRect.gameObject.SetActive(false);
            }
            m_OutputText.gameObject.SetActive(true);

            var builder = new StringBuilder(512);
            for (int i = 0; i < m_Transcript.Count; i++)
            {
                TranscriptLine line = m_Transcript[i];
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append(line.Text);
            }

            m_OutputText.text = builder.ToString();
            ResizeTextTranscriptContent();
            if (m_AutoScrollToLatest)
            {
                QueueAutoScrollToBottom();
            }
        }

        private void SetTranscriptLayoutMode(bool bubbleMode)
        {
            if (m_ChatContentRect == null || m_OutputText == null)
            {
                return;
            }

            VerticalLayoutGroup contentLayout =
                m_ChatContentRect.GetComponent<VerticalLayoutGroup>();
            if (contentLayout != null)
            {
                contentLayout.enabled = bubbleMode;
            }

            ContentSizeFitter contentFitter = m_ChatContentRect.GetComponent<ContentSizeFitter>();
            if (contentFitter != null)
            {
                contentFitter.enabled = bubbleMode;
            }

            RectTransform textRect = m_OutputText.rectTransform;
            if (textRect != null)
            {
                if (textRect.parent != m_ChatContentRect)
                {
                    textRect.SetParent(m_ChatContentRect, false);
                }

                textRect.anchorMin = new Vector2(0f, 1f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.pivot = new Vector2(0.5f, 1f);
                textRect.anchoredPosition = new Vector2(0f, -m_BlocksSpacingSm);
                textRect.sizeDelta = new Vector2(-(m_BlocksSpacingMd), 0f);

                LayoutElement textLayoutElement = EnsureComponent<LayoutElement>(
                    m_OutputText.gameObject
                );
                textLayoutElement.ignoreLayout = !bubbleMode;
                textLayoutElement.minHeight = 0f;
                textLayoutElement.preferredHeight = -1f;
                textLayoutElement.flexibleHeight = 0f;
                textLayoutElement.minWidth = 0f;
                textLayoutElement.preferredWidth = -1f;
                textLayoutElement.flexibleWidth = 1f;
            }

            if (!bubbleMode)
            {
                m_ChatContentRect.anchorMin = new Vector2(0f, 1f);
                m_ChatContentRect.anchorMax = new Vector2(1f, 1f);
                m_ChatContentRect.pivot = new Vector2(0.5f, 1f);
                m_ChatContentRect.anchoredPosition = Vector2.zero;
            }
        }

        private void ResizeTextTranscriptContent()
        {
            if (m_ChatContentRect == null || m_OutputText == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(m_OutputText.rectTransform);

            float viewportHeight = 0f;
            if (m_ChatScrollRect != null && m_ChatScrollRect.viewport != null)
            {
                viewportHeight = Mathf.Max(0f, m_ChatScrollRect.viewport.rect.height);
            }

            float preferredTextHeight = Mathf.Max(0f, m_OutputText.preferredHeight);
            float targetHeight = Mathf.Max(
                viewportHeight,
                preferredTextHeight + (m_BlocksSpacingSm * 2f)
            );

            Vector2 size = m_ChatContentRect.sizeDelta;
            size.y = targetHeight;
            m_ChatContentRect.sizeDelta = size;
        }

        private void RenderBubbleTranscript()
        {
            if (m_ChatMessageListRect == null)
            {
                return;
            }

            m_ChatMessageListRect.gameObject.SetActive(true);
            m_OutputText.gameObject.SetActive(false);
            m_OutputText.text = string.Empty;

            int rowIndex = 0;
            for (int i = 0; i < m_Transcript.Count; i++)
            {
                TranscriptLine line = m_Transcript[i];
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                RectTransform rowRect = EnsureBubbleRow(rowIndex);
                ConfigureBubbleRow(rowRect, line, rowIndex);
                rowRect.gameObject.SetActive(true);
                rowIndex++;
            }

            for (int i = rowIndex; i < m_ChatMessageListRect.childCount; i++)
            {
                Transform child = m_ChatMessageListRect.GetChild(i);
                if (child != null && child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private RectTransform EnsureBubbleRow(int index)
        {
            if (index < m_ChatMessageListRect.childCount)
            {
                Transform existing = m_ChatMessageListRect.GetChild(index);
                if (existing is RectTransform existingRect)
                {
                    return existingRect;
                }
            }

            var rowGo = new GameObject("Msg", typeof(RectTransform));
            rowGo.transform.SetParent(m_ChatMessageListRect, false);
            RectTransform rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.sizeDelta = Vector2.zero;

            HorizontalLayoutGroup rowLayout = EnsureComponent<HorizontalLayoutGroup>(rowGo);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childScaleWidth = false;
            rowLayout.childScaleHeight = false;

            ContentSizeFitter rowFitter = EnsureComponent<ContentSizeFitter>(rowGo);
            rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            LayoutElement rowLayoutElement = EnsureComponent<LayoutElement>(rowGo);
            rowLayoutElement.minHeight = 0f;
            rowLayoutElement.preferredHeight = -1f;
            rowLayoutElement.flexibleHeight = 0f;
            rowLayoutElement.minWidth = 0f;
            rowLayoutElement.preferredWidth = -1f;
            rowLayoutElement.flexibleWidth = 1f;

            EnsureRowSpacer(rowRect, "LeftSpacer");
            EnsureBubbleContainer(rowRect);
            EnsureRowSpacer(rowRect, "RightSpacer");

            return rowRect;
        }

        private void ConfigureBubbleRow(RectTransform rowRect, TranscriptLine line, int index)
        {
            if (rowRect == null || line == null)
            {
                return;
            }

            rowRect.name = $"Msg_{index}";

            LayoutElement leftSpacer = EnsureRowSpacer(rowRect, "LeftSpacer");
            LayoutElement rightSpacer = EnsureRowSpacer(rowRect, "RightSpacer");
            RectTransform bubbleRect = EnsureBubbleContainer(rowRect);
            Image bubbleImage = EnsureComponent<Image>(bubbleRect.gameObject);
            bubbleImage.color = GetBubbleColor(line);
            bubbleImage.raycastTarget = false;

            VerticalLayoutGroup bubbleLayout = EnsureComponent<VerticalLayoutGroup>(
                bubbleRect.gameObject
            );
            bubbleLayout.padding = new RectOffset(12, 12, 8, 8);
            bubbleLayout.spacing = 4f;
            bubbleLayout.childAlignment = TextAnchor.UpperLeft;
            bubbleLayout.childControlWidth = true;
            bubbleLayout.childControlHeight = true;
            bubbleLayout.childForceExpandWidth = false;
            bubbleLayout.childForceExpandHeight = false;
            bubbleLayout.childScaleWidth = false;
            bubbleLayout.childScaleHeight = false;

            ContentSizeFitter bubbleFitter = EnsureComponent<ContentSizeFitter>(
                bubbleRect.gameObject
            );
            bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement bubbleElement = EnsureComponent<LayoutElement>(bubbleRect.gameObject);
            float approximateWidth = 120f + Mathf.Min(96, line.Text.Length) * 4.2f;
            bubbleElement.preferredWidth = Mathf.Clamp(approximateWidth, 180f, m_BubbleMaxWidth);
            bubbleElement.minWidth = 140f;
            bubbleElement.flexibleWidth = 0f;

            TextMeshProUGUI labelText = EnsureBubbleLabel(bubbleRect);
            bool showLabel = m_ShowSpeakerLabel && !string.IsNullOrWhiteSpace(line.SpeakerLabel);
            labelText.gameObject.SetActive(showLabel);
            if (showLabel)
            {
                labelText.text = line.SpeakerLabel;
                labelText.fontSize = Mathf.Max(12f, m_OutputFontSize - 8f);
                labelText.fontStyle = FontStyles.Bold;
                labelText.color = line.Role switch
                {
                    TranscriptRole.Player => new Color(0.82f, 0.92f, 1f, 1f),
                    TranscriptRole.Npc => new Color(0.82f, 1f, 0.88f, 1f),
                    _ => new Color(1f, 0.94f, 0.74f, 1f),
                };
                labelText.textWrappingMode = TextWrappingModes.NoWrap;
                labelText.overflowMode = TextOverflowModes.Ellipsis;
                labelText.richText = false;
            }

            TextMeshProUGUI bodyText = EnsureBubbleBody(bubbleRect);
            bodyText.text = line.Text;
            bodyText.fontSize = Mathf.Max(13f, m_OutputFontSize - 5f);
            bodyText.color = line.IsPending
                ? new Color(m_BubbleTextColor.r, m_BubbleTextColor.g, m_BubbleTextColor.b, 0.78f)
                : m_BubbleTextColor;
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.overflowMode = TextOverflowModes.Overflow;
            bodyText.richText = false;

            bool rightAlign = line.Role == TranscriptRole.Player;
            bool centerAlign = line.Role == TranscriptRole.System;
            leftSpacer.gameObject.SetActive(rightAlign || centerAlign);
            rightSpacer.gameObject.SetActive(!rightAlign || centerAlign);
        }

        private static LayoutElement EnsureRowSpacer(RectTransform parent, string spacerName)
        {
            Transform spacerTransform = parent.Find(spacerName);
            RectTransform spacerRect;
            if (spacerTransform == null)
            {
                var spacerGo = new GameObject(spacerName, typeof(RectTransform));
                spacerGo.transform.SetParent(parent, false);
                spacerRect = spacerGo.GetComponent<RectTransform>();
            }
            else
            {
                spacerRect = spacerTransform as RectTransform;
            }

            LayoutElement spacer = EnsureComponent<LayoutElement>(spacerRect.gameObject);
            spacer.minWidth = 0f;
            spacer.preferredWidth = -1f;
            spacer.flexibleWidth = 1f;
            spacer.minHeight = 0f;
            spacer.preferredHeight = -1f;
            spacer.flexibleHeight = 0f;
            return spacer;
        }

        private RectTransform EnsureBubbleContainer(RectTransform parent)
        {
            Transform bubbleTransform = parent.Find("Bubble");
            RectTransform bubbleRect;
            if (bubbleTransform == null)
            {
                var bubbleGo = new GameObject("Bubble", typeof(RectTransform));
                bubbleGo.transform.SetParent(parent, false);
                bubbleRect = bubbleGo.GetComponent<RectTransform>();
            }
            else
            {
                bubbleRect = bubbleTransform as RectTransform;
            }

            bubbleRect.anchorMin = new Vector2(0f, 1f);
            bubbleRect.anchorMax = new Vector2(0f, 1f);
            bubbleRect.pivot = new Vector2(0f, 1f);
            bubbleRect.localScale = Vector3.one;
            return bubbleRect;
        }

        private static TextMeshProUGUI EnsureBubbleLabel(RectTransform bubbleRect)
        {
            Transform labelTransform = bubbleRect.Find("Label");
            GameObject labelGo;
            if (labelTransform == null)
            {
                labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(bubbleRect, false);
            }
            else
            {
                labelGo = labelTransform.gameObject;
            }

            return EnsureComponent<TextMeshProUGUI>(labelGo);
        }

        private static TextMeshProUGUI EnsureBubbleBody(RectTransform bubbleRect)
        {
            Transform bodyTransform = bubbleRect.Find("Body");
            GameObject bodyGo;
            if (bodyTransform == null)
            {
                bodyGo = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
                bodyGo.transform.SetParent(bubbleRect, false);
            }
            else
            {
                bodyGo = bodyTransform.gameObject;
            }

            return EnsureComponent<TextMeshProUGUI>(bodyGo);
        }

        private Color GetBubbleColor(TranscriptLine line)
        {
            return line.Role switch
            {
                TranscriptRole.Player => m_PlayerBubbleColor,
                TranscriptRole.Npc => GetNpcBubbleColor(line.SpeakerLabel),
                _ => m_SystemBubbleColor,
            };
        }

        private Color GetNpcBubbleColor(string speakerLabel)
        {
            if (string.IsNullOrWhiteSpace(speakerLabel))
            {
                return m_NpcBubbleColor;
            }

            int hash = Mathf.Abs(speakerLabel.GetHashCode());
            float tint = ((hash % 21) - 10) * 0.012f;
            return new Color(
                Mathf.Clamp01(m_NpcBubbleColor.r + tint),
                Mathf.Clamp01(m_NpcBubbleColor.g - tint * 0.25f),
                Mathf.Clamp01(m_NpcBubbleColor.b + tint * 0.35f),
                m_NpcBubbleColor.a
            );
        }

        private void ScrollTranscriptToBottom()
        {
            if (m_ChatScrollRect == null || m_ChatContentRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            if (m_ChatMessageListRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(m_ChatMessageListRect);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(m_ChatContentRect);

            RectTransform viewportRect = m_ChatScrollRect.viewport;
            float viewportHeight = viewportRect != null ? viewportRect.rect.height : 0f;
            float contentHeight = m_ChatContentRect.rect.height;
            bool hasOverflow = contentHeight > viewportHeight + 0.5f;

            // Keep short transcripts pinned to top so first NPC replies stay visible.
            m_ChatScrollRect.verticalNormalizedPosition = hasOverflow ? 0f : 1f;
            m_ChatScrollRect.velocity = Vector2.zero;
        }

        private void QueueAutoScrollToBottom()
        {
            if (!isActiveAndEnabled)
            {
                ScrollTranscriptToBottom();
                return;
            }

            if (m_PendingAutoScrollRoutine != null)
            {
                StopCoroutine(m_PendingAutoScrollRoutine);
            }

            m_PendingAutoScrollRoutine = StartCoroutine(ScrollToBottomAfterLayout());
        }

        private IEnumerator ScrollToBottomAfterLayout()
        {
            yield return null;
            ScrollTranscriptToBottom();
            yield return null;
            ScrollTranscriptToBottom();
            m_PendingAutoScrollRoutine = null;
        }

        private bool HasPendingTranscriptLine()
        {
            for (int i = 0; i < m_Transcript.Count; i++)
            {
                TranscriptLine line = m_Transcript[i];
                if (line != null && line.IsPending)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateSendButtonState()
        {
            if (m_SendButton == null)
            {
                return;
            }

            if (!m_DialogueVisible)
            {
                m_SendButton.interactable = false;
                return;
            }

            if (!m_DisableSendWhilePending)
            {
                m_SendButton.interactable = true;
                return;
            }

            m_SendButton.interactable = !HasPendingTranscriptLine();
        }
    }
}
