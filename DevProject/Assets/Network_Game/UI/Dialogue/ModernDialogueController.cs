using System;
using System.Collections.Generic;
using Network_Game.Auth;
using Network_Game.Diagnostics;
using Network_Game.Dialogue;
using Network_Game.UI;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Network_Game.UI.Dialogue
{
    [DefaultExecutionOrder(-55)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Network Game/UI/Modern Dialogue Controller")]
    public sealed class ModernDialogueController : MonoBehaviour
    {
        private enum TranscriptRole
        {
            Player,
            Npc,
            System,
        }

        private sealed class TranscriptEntry
        {
            public int ClientRequestId;
            public bool IsPending;
            public TranscriptRole Role;
            public string Speaker;
            public string Message;
        }

        [Header("Dialogue")]
        [SerializeField]
        [Min(0f)]
        private float m_MaxListenerDistance = 4f;

        [SerializeField]
        private bool m_AutoSelectListener = true;

        [SerializeField]
        private bool m_AutoOpenWhenNpcInRange = true;

        [SerializeField]
        private bool m_AutoCloseWhenNoNpcInRange = true;

        [SerializeField]
        private bool m_RequireLeaveRangeAfterManualClose = true;

        [SerializeField]
        private bool m_LogProximityDebug;

        [SerializeField]
        [Min(0.1f)]
        private float m_PlayerResolveIntervalSeconds = 0.5f;

        [SerializeField]
        [Min(0.01f)]
        private float m_ProximityCheckIntervalSeconds = 0.05f;

        [SerializeField]
        [Min(0.02f)]
        private float m_InputLegibilityCheckIntervalSeconds = 0.2f;

        [SerializeField]
        [Min(0f)]
        private float m_ManualCloseSuppressSeconds = 0.2f;

        [SerializeField]
        private Color m_InputTextColor = new(0.95f, 0.95f, 0.95f, 1f);

        [Header("Camera Switch")]
        [SerializeField]
        private bool m_EnableCameraSwitchButton = true;

        [SerializeField]
        [Min(1)]
        private int m_SelectedCameraPriorityBoost = 50;

        [SerializeField]
        private bool m_UseExclusiveCameraActivation = true;

        [SerializeField]
        private List<string> m_PreferredCameraOrder = new()
        {
            "PlayerCinemachineCamera",
            "PlayerCinemachineCameraFar",
            "NPCFollowCamera",
        };

        private const int MaxTranscriptLines = 64;
        private const float NearZeroDistance = 0.0001f;
        private const float MinCameraSearchCooldown = 0.25f;
        private const float MinChatInputFontSize = 14f;
        private const float MinChatInputHeight = 40f;
        private const float InputResolveInterval = 0.5f;

        private UIDocument m_Document;
        private VisualElement m_BoundRoot;
        private bool m_UiCallbacksBound;
        private VisualElement m_ChatContainer;
        private Label m_ListenerStatus;
        private ScrollView m_TranscriptScroll;
        private VisualElement m_TranscriptContent;
        private VisualElement m_InputRow;
        private TextField m_ChatInput;
        private Button m_SendButton;
        private Button m_CloseButton;
        private Button m_CameraSwitchButton;
        private VisualElement m_ChatInputInner;

        private readonly List<TranscriptEntry> m_Transcript = new();
        private bool m_ChatVisible = true;
        private bool m_CurrentInRange;
        private bool m_WaitForRangeExitToAutoOpen;
        private string m_LastLoggedTargetName = string.Empty;
        private float m_LastLoggedDistance = -1f;
        private bool m_LastLoggedInRange;
        private bool m_LastLoggedVisible;
        private bool m_LastLoggedWaitForExit;
        private ulong m_ManualCloseNpcId;

        private float m_NextPlayerResolveAt;
        private float m_NextProximityCheckAt;
        private float m_NextInputLegibilityCheckAt;
        private Transform m_LocalPlayerTransform;
        private NetworkObject m_LocalPlayerNetworkObject;
        private NetworkObject m_SelectedNpc;

        private int m_ClientRequestId;
        private int m_LastPendingRequestId;
        private float m_ManualCloseSuppressUntil;
        private bool m_GameplayInputSuppressed;
        private float m_NextInputResolveAt;
        private Network_Game.ThirdPersonController.StarterAssetsInputs m_LocalStarterInputs;

        private NpcDialogueActor[] m_CachedNpcActors;
        private float m_NextNpcCacheRefreshAt;
        private const float NpcCacheRefreshInterval = 0.5f;

        private readonly List<CinemachineVirtualCameraBase> m_Cameras = new();
        private readonly Dictionary<CinemachineVirtualCameraBase, int> m_CameraBasePriorities =
            new();
        private int m_SelectedCameraIndex = -1;
        private float m_NextCameraRefreshAt;

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
            EnsureUiBinding(force: true);
            AppendSystemLine("Dialogue ready.");
        }

        private void OnEnable()
        {
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
            EnsureUiBinding(force: true);
            RefreshCameraList(force: true);
            m_NextProximityCheckAt = 0f;
            m_NextInputLegibilityCheckAt = 0f;
            UpdateListenerStatus();
        }

        private void OnDisable()
        {
            if (m_UiCallbacksBound)
            {
                RegisterUiCallbacks(false);
                m_UiCallbacksBound = false;
            }
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
            ApplyGameplayInputSuppression(false);
        }

        private void Update()
        {
            EnsureUiBinding(force: false);
            ApplyHudDrivenLayout();

            float now = Time.unscaledTime;
            if (now >= m_NextInputLegibilityCheckAt)
            {
                m_NextInputLegibilityCheckAt =
                    now + Mathf.Max(0.02f, m_InputLegibilityCheckIntervalSeconds);
                EnsureInputTextVisible();
            }

            if (now >= m_NextProximityCheckAt)
            {
                m_NextProximityCheckAt = now + Mathf.Max(0.01f, m_ProximityCheckIntervalSeconds);
                EvaluateProximityAndVisibility();
            }

            if (m_EnableCameraSwitchButton && now >= m_NextCameraRefreshAt)
            {
                RefreshCameraList(force: false);
            }
        }

        private void EnsureUiBinding(bool force)
        {
            if (m_Document == null)
            {
                m_Document = GetComponent<UIDocument>();
            }

            VisualElement root = m_Document != null ? m_Document.rootVisualElement : null;
            if (root == null)
            {
                return;
            }

            bool missingRefs =
                m_ChatContainer == null
                || m_TranscriptScroll == null
                || m_TranscriptContent == null
                || m_ChatInput == null
                || m_SendButton == null
                || m_CloseButton == null;

            bool rootChanged = !ReferenceEquals(root, m_BoundRoot);
            if (!force && !rootChanged && !missingRefs)
            {
                return;
            }

            if (m_UiCallbacksBound)
            {
                RegisterUiCallbacks(false);
                m_UiCallbacksBound = false;
            }

            m_BoundRoot = root;
            BindUi();
            RegisterUiCallbacks(true);
            m_UiCallbacksBound = true;

            ConfigureCameraSwitchButton();
            SetDialogueVisible(m_ChatVisible, false, true);
            UpdateListenerStatus();
            RenderTranscript();

            if (m_LogProximityDebug && (rootChanged || missingRefs))
            {
                NGLog.Debug(
                    "DialogueUI",
                    NGLog.Format(
                        "UI rebound",
                        ("rootChanged", rootChanged),
                        ("missingRefs", missingRefs),
                        ("rootName", root.name)
                    ),
                    this
                );
            }
        }

        private void BindUi()
        {
            if (m_Document == null)
            {
                m_Document = GetComponent<UIDocument>();
            }

            VisualElement root = m_Document != null ? m_Document.rootVisualElement : null;
            if (root == null)
            {
                return;
            }

            m_ChatContainer = root.Q<VisualElement>("chat-container");
            m_ListenerStatus = root.Q<Label>("listener-status");
            m_TranscriptScroll = root.Q<ScrollView>("transcript-scroll");
            m_TranscriptContent = root.Q<VisualElement>("transcript-content");
            m_InputRow = root.Q<VisualElement>("input-row");
            m_ChatInput = root.Q<TextField>("chat-input");
            m_SendButton = root.Q<Button>("send-button");
            m_CloseButton = root.Q<Button>("close-chat");
            m_CameraSwitchButton = root.Q<Button>("camera-switch");

            if (m_ChatContainer != null)
            {
                m_ChatContainer.style.flexDirection = FlexDirection.Column;
                m_ChatContainer.style.alignItems = Align.Stretch;
                m_ChatContainer.style.justifyContent = Justify.FlexStart;
                m_ChatContainer.style.overflow = Overflow.Hidden;
            }

            ApplyHudDrivenLayout();

            if (m_TranscriptScroll != null)
            {
                m_TranscriptScroll.style.flexGrow = 1f;
                m_TranscriptScroll.style.flexShrink = 1f;
                m_TranscriptScroll.style.minHeight = 0f;
            }

            if (m_TranscriptContent != null)
            {
                m_TranscriptContent.style.flexDirection = FlexDirection.Column;
                m_TranscriptContent.style.alignItems = Align.Stretch;
                m_TranscriptContent.style.justifyContent = Justify.FlexStart;
            }

            if (m_InputRow == null && m_ChatInput != null)
            {
                m_InputRow = m_ChatInput.parent;
            }

            if (m_InputRow != null)
            {
                m_InputRow.style.flexDirection = FlexDirection.Row;
                m_InputRow.style.alignItems = Align.Center;
                m_InputRow.style.flexShrink = 0f;
                m_InputRow.style.flexGrow = 0f;
            }

            if (m_ChatInput != null)
            {
                m_ChatInput.multiline = false;
                m_ChatInput.isReadOnly = false;
                m_ChatInput.focusable = true;
                m_ChatInput.delegatesFocus = true;
                m_ChatInput.pickingMode = PickingMode.Position;
                m_ChatInput.style.flexGrow = 1f;
                m_ChatInput.style.flexShrink = 1f;
                m_ChatInput.style.color = m_InputTextColor;
                m_ChatInput.style.opacity = 1f;
                m_ChatInput.style.minHeight = MinChatInputHeight;
                m_ChatInput.style.fontSize = MinChatInputFontSize;

                // Ensure the inner text input gets focus/click events even with custom USS.
                m_ChatInputInner = m_ChatInput.Q(className: "unity-text-input");
                if (m_ChatInputInner == null)
                {
                    m_ChatInputInner = m_ChatInput.Q(className: "unity-base-field__input");
                }
                if (m_ChatInputInner == null)
                {
                    m_ChatInputInner = m_ChatInput.Q(className: "unity-text-field__input");
                }
                if (m_ChatInputInner != null)
                {
                    m_ChatInputInner.focusable = true;
                    m_ChatInputInner.pickingMode = PickingMode.Position;
                    m_ChatInputInner.style.color = m_InputTextColor;
                    m_ChatInputInner.style.opacity = 1f;
                    m_ChatInputInner.style.fontSize = MinChatInputFontSize;
                    m_ChatInputInner.style.minHeight = MinChatInputHeight;
                    m_ChatInputInner.style.paddingTop = 8f;
                    m_ChatInputInner.style.paddingBottom = 8f;
                    m_ChatInputInner.style.paddingLeft = 10f;
                    m_ChatInputInner.style.paddingRight = 10f;
                    m_ChatInputInner.style.unityTextAlign = TextAnchor.MiddleLeft;
                    m_ChatInputInner.style.whiteSpace = WhiteSpace.NoWrap;
                }

                ApplyChatInputLegibilityStyle();
            }
        }

        private void RegisterUiCallbacks(bool subscribe)
        {
            if (m_SendButton != null)
            {
                if (subscribe)
                {
                    m_SendButton.clicked += OnSendClicked;
                }
                else
                {
                    m_SendButton.clicked -= OnSendClicked;
                }
            }

            if (m_CloseButton != null)
            {
                if (subscribe)
                {
                    m_CloseButton.clicked += OnCloseClicked;
                }
                else
                {
                    m_CloseButton.clicked -= OnCloseClicked;
                }
            }

            if (m_CameraSwitchButton != null)
            {
                if (subscribe)
                {
                    m_CameraSwitchButton.clicked += OnCameraSwitchClicked;
                }
                else
                {
                    m_CameraSwitchButton.clicked -= OnCameraSwitchClicked;
                }
            }

            if (m_ChatInput != null)
            {
                if (subscribe)
                {
                    m_ChatInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
                    m_ChatInput.RegisterCallback<PointerDownEvent>(OnInputPointerDown);
                    m_ChatInput.RegisterCallback<MouseDownEvent>(OnInputMouseDown);
                    m_ChatInput.RegisterCallback<FocusInEvent>(OnChatInputFocusIn);
                    m_ChatInput.RegisterCallback<FocusOutEvent>(OnChatInputFocusOut);
                    if (m_ChatInputInner != null)
                    {
                        m_ChatInputInner.RegisterCallback<PointerDownEvent>(OnInputPointerDown);
                        m_ChatInputInner.RegisterCallback<MouseDownEvent>(OnInputMouseDown);
                        m_ChatInputInner.RegisterCallback<FocusInEvent>(OnChatInputFocusIn);
                        m_ChatInputInner.RegisterCallback<FocusOutEvent>(OnChatInputFocusOut);
                    }
                }
                else
                {
                    m_ChatInput.UnregisterCallback<KeyDownEvent>(OnInputKeyDown);
                    m_ChatInput.UnregisterCallback<PointerDownEvent>(OnInputPointerDown);
                    m_ChatInput.UnregisterCallback<MouseDownEvent>(OnInputMouseDown);
                    m_ChatInput.UnregisterCallback<FocusInEvent>(OnChatInputFocusIn);
                    m_ChatInput.UnregisterCallback<FocusOutEvent>(OnChatInputFocusOut);
                    if (m_ChatInputInner != null)
                    {
                        m_ChatInputInner.UnregisterCallback<PointerDownEvent>(OnInputPointerDown);
                        m_ChatInputInner.UnregisterCallback<MouseDownEvent>(OnInputMouseDown);
                        m_ChatInputInner.UnregisterCallback<FocusInEvent>(OnChatInputFocusIn);
                        m_ChatInputInner.UnregisterCallback<FocusOutEvent>(OnChatInputFocusOut);
                    }
                }
            }
        }

        private void ApplyHudDrivenLayout()
        {
            if (m_ChatContainer == null)
            {
                return;
            }

            if (ModernHudController.TryApplyBottomBarLayout(m_ChatContainer))
            {
                m_ChatContainer.style.maxWidth = StyleKeyword.None;
                m_ChatContainer.style.maxHeight = StyleKeyword.None;
            }
        }

        private void OnInputPointerDown(PointerDownEvent _)
        {
            if (m_ChatInput == null || !m_ChatVisible)
            {
                return;
            }

            m_ChatInput.Focus();
        }

        private void OnInputMouseDown(MouseDownEvent _)
        {
            if (m_ChatInput == null || !m_ChatVisible)
            {
                return;
            }

            m_ChatInput.Focus();
        }

        private void OnChatInputFocusIn(FocusInEvent _)
        {
            // Claim the UI cursor so gameplay look/movement is suppressed while typing.
            ModernHudController.TryAcquireUiCursor(this);
        }

        private void OnChatInputFocusOut(FocusOutEvent _)
        {
            // Release cursor only when the dialogue panel itself is also hidden;
            // if it's still visible we keep input suppressed so the player doesn't
            // accidentally start moving the moment they submit a message.
            if (!m_ChatVisible)
                ModernHudController.TryReleaseUiCursor(this);
        }

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            if (evt.shiftKey || evt.ctrlKey || evt.altKey)
            {
                return;
            }

            evt.StopPropagation();
            OnSendClicked();
        }

        private void OnSendClicked()
        {
            if (m_ChatInput == null)
            {
                return;
            }

            string prompt = (m_ChatInput.value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                return;
            }

            NetworkDialogueService service = NetworkDialogueService.Instance;
            if (service == null)
            {
                AppendSystemLine("Dialogue service not available.");
                return;
            }

            if (!TryResolveLocalPlayer(out _, out NetworkObject localPlayer) || localPlayer == null)
            {
                AppendSystemLine("Local player not resolved.");
                return;
            }

            if (
                !TryResolveNearestNpc(
                    localPlayer.transform.position,
                    out NetworkObject targetNpc,
                    out _
                )
            )
            {
                AppendSystemLine("No NPC in range.");
                return;
            }

            m_SelectedNpc = targetNpc;
            m_CurrentInRange = true;
            UpdateListenerStatus();

            int requestId = ++m_ClientRequestId;
            m_LastPendingRequestId = requestId;

            ulong speakerId = targetNpc.NetworkObjectId;
            ulong listenerId = localPlayer.NetworkObjectId;
            ulong requesterId =
                NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;

            string conversationKey = service.ResolveConversationKey(
                speakerId,
                listenerId,
                requesterId,
                null
            );

            service.RequestDialogue(
                new NetworkDialogueService.DialogueRequest
                {
                    Prompt = prompt,
                    ConversationKey = conversationKey,
                    SpeakerNetworkId = speakerId,
                    ListenerNetworkId = listenerId,
                    RequestingClientId = requesterId,
                    Broadcast = true,
                    BroadcastDuration = 2f,
                    NotifyClient = true,
                    ClientRequestId = requestId,
                    IsUserInitiated = true,
                    BlockRepeatedPrompt = false,
                    MinRepeatDelaySeconds = 0f,
                    RequireUserReply = false,
                }
            );

            AppendTranscript(
                new TranscriptEntry
                {
                    Role = TranscriptRole.Player,
                    Speaker = "YOU",
                    Message = prompt,
                    ClientRequestId = requestId,
                    IsPending = false,
                }
            );
            AppendTranscript(
                new TranscriptEntry
                {
                    Role = TranscriptRole.System,
                    Speaker = "SYSTEM",
                    Message = "Thinking...",
                    ClientRequestId = requestId,
                    IsPending = true,
                }
            );

            m_ChatInput.value = string.Empty;
            m_ChatInput.Focus();
            ScrollTranscriptToBottom();
        }

        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            if (!ShouldShowResponse(response))
            {
                return;
            }

            if (response.Request.ClientRequestId > 0)
            {
                RemovePending(response.Request.ClientRequestId);
                if (m_LastPendingRequestId == response.Request.ClientRequestId)
                {
                    m_LastPendingRequestId = 0;
                }
            }

            if (response.Status == NetworkDialogueService.DialogueStatus.Completed)
            {
                string speaker = ResolveNetworkObjectName(response.Request.SpeakerNetworkId);
                if (string.IsNullOrWhiteSpace(speaker))
                {
                    speaker = "NPC";
                }

                AppendTranscript(
                    new TranscriptEntry
                    {
                        Role = TranscriptRole.Npc,
                        Speaker = speaker.ToUpperInvariant(),
                        Message = string.IsNullOrWhiteSpace(response.ResponseText)
                            ? "(empty response)"
                            : response.ResponseText.Trim(),
                        ClientRequestId = response.Request.ClientRequestId,
                        IsPending = false,
                    }
                );
            }
            else if (
                response.Status == NetworkDialogueService.DialogueStatus.Failed
                || response.Status == NetworkDialogueService.DialogueStatus.Cancelled
            )
            {
                string reason = string.IsNullOrWhiteSpace(response.Error)
                    ? response.Status.ToString()
                    : response.Error;
                AppendSystemLine($"Dialogue failed: {reason}");
            }

            ScrollTranscriptToBottom();
        }

        private bool ShouldShowResponse(NetworkDialogueService.DialogueResponse response)
        {
            if (response.Request.ClientRequestId == m_ClientRequestId)
            {
                return true;
            }

            if (
                response.Request.ClientRequestId > 0
                && response.Request.ClientRequestId != m_LastPendingRequestId
            )
            {
                return false;
            }

            if (!TryResolveLocalPlayer(out _, out NetworkObject localPlayer) || localPlayer == null)
            {
                return false;
            }

            ulong localPlayerId = localPlayer.NetworkObjectId;
            bool localInvolved =
                response.Request.ListenerNetworkId == localPlayerId
                || response.Request.SpeakerNetworkId == localPlayerId;
            if (!localInvolved)
            {
                return false;
            }

            if (m_SelectedNpc == null)
            {
                return true;
            }

            ulong selectedNpcId = m_SelectedNpc.NetworkObjectId;
            return response.Request.ListenerNetworkId == selectedNpcId
                || response.Request.SpeakerNetworkId == selectedNpcId;
        }

        private void EvaluateProximityAndVisibility()
        {
            if (
                !TryResolveLocalPlayer(out Transform playerTransform, out NetworkObject localPlayer)
            )
            {
                m_CurrentInRange = false;
                UpdateListenerStatus();
                return;
            }

            bool hasNpc = TryResolveNearestNpc(
                playerTransform.position,
                out NetworkObject nearestNpc,
                out float nearestDistance
            );

            m_CurrentInRange = hasNpc;
            if (hasNpc)
            {
                m_SelectedNpc = nearestNpc;
            }
            else if (m_AutoSelectListener)
            {
                m_SelectedNpc = null;
            }

            if (!m_CurrentInRange && m_WaitForRangeExitToAutoOpen)
            {
                m_WaitForRangeExitToAutoOpen = false;
                m_ManualCloseNpcId = 0;
            }

            if (
                m_WaitForRangeExitToAutoOpen
                && m_CurrentInRange
                && nearestNpc != null
                && m_ManualCloseNpcId != 0
                && nearestNpc.NetworkObjectId != m_ManualCloseNpcId
            )
            {
                m_WaitForRangeExitToAutoOpen = false;
                m_ManualCloseNpcId = 0;
            }

            if (m_AutoCloseWhenNoNpcInRange && !m_CurrentInRange && m_ChatVisible)
            {
                SetDialogueVisible(false, false, false);
            }
            else if (
                m_AutoOpenWhenNpcInRange
                && m_CurrentInRange
                && !m_ChatVisible
                && !m_WaitForRangeExitToAutoOpen
                && Time.unscaledTime >= m_ManualCloseSuppressUntil
            )
            {
                SetDialogueVisible(true, false, false);
            }

            UpdateListenerStatus();
            LogProximityStateIfChanged(nearestNpc, nearestDistance, m_CurrentInRange, localPlayer);
        }

        private bool TryResolveLocalPlayer(
            out Transform playerTransform,
            out NetworkObject playerNetObj
        )
        {
            playerTransform = null;
            playerNetObj = null;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsClient && manager.LocalClient != null)
            {
                NetworkObject localPlayer = manager.LocalClient.PlayerObject;
                if (localPlayer != null)
                {
                    m_LocalPlayerNetworkObject = localPlayer;
                    m_LocalPlayerTransform = localPlayer.transform;
                    playerTransform = m_LocalPlayerTransform;
                    playerNetObj = m_LocalPlayerNetworkObject;
                    return true;
                }
            }

            if (Time.unscaledTime < m_NextPlayerResolveAt && m_LocalPlayerTransform != null)
            {
                playerTransform = m_LocalPlayerTransform;
                playerNetObj = m_LocalPlayerNetworkObject;
                return true;
            }

            m_NextPlayerResolveAt =
                Time.unscaledTime + Mathf.Max(0.1f, m_PlayerResolveIntervalSeconds);
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer == null)
            {
                m_LocalPlayerTransform = null;
                m_LocalPlayerNetworkObject = null;
                return false;
            }

            m_LocalPlayerTransform = taggedPlayer.transform;
            m_LocalPlayerNetworkObject = taggedPlayer.GetComponent<NetworkObject>();

            playerTransform = m_LocalPlayerTransform;
            playerNetObj = m_LocalPlayerNetworkObject;
            return playerTransform != null;
        }

        private bool TryResolveNearestNpc(
            Vector3 playerPosition,
            out NetworkObject nearestNpc,
            out float nearestDistance
        )
        {
            nearestNpc = null;
            nearestDistance = float.PositiveInfinity;

            if (!m_AutoSelectListener && m_SelectedNpc != null)
            {
                if (!IsValidNpc(m_SelectedNpc))
                {
                    return false;
                }

                float selectedDistance = Vector3.Distance(
                    playerPosition,
                    m_SelectedNpc.transform.position
                );
                if (!IsWithinRange(selectedDistance))
                {
                    return false;
                }

                nearestNpc = m_SelectedNpc;
                nearestDistance = selectedDistance;
                return true;
            }

            NpcDialogueActor[] actors = GetCachedNpcActors();
            if (actors == null || actors.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < actors.Length; i++)
            {
                NpcDialogueActor actor = actors[i];
                if (actor == null)
                {
                    continue;
                }

                NetworkObject npc = actor.GetComponent<NetworkObject>();
                if (!IsValidNpc(npc))
                {
                    continue;
                }

                float distance = Vector3.Distance(playerPosition, npc.transform.position);
                if (!IsWithinRange(distance))
                {
                    continue;
                }

                if (distance + NearZeroDistance >= nearestDistance)
                {
                    continue;
                }

                nearestNpc = npc;
                nearestDistance = distance;
            }

            return nearestNpc != null;
        }

        private bool IsWithinRange(float distance)
        {
            if (m_MaxListenerDistance <= 0f)
            {
                return true;
            }

            return distance <= m_MaxListenerDistance + NearZeroDistance;
        }

        private static bool IsValidNpc(NetworkObject npc)
        {
            return npc != null && npc.IsSpawned && npc.gameObject.activeInHierarchy;
        }

        private NpcDialogueActor[] GetCachedNpcActors()
        {
            if (m_CachedNpcActors != null && Time.unscaledTime < m_NextNpcCacheRefreshAt)
            {
                return m_CachedNpcActors;
            }

            m_NextNpcCacheRefreshAt = Time.unscaledTime + NpcCacheRefreshInterval;
#if UNITY_2023_1_OR_NEWER
            m_CachedNpcActors = FindObjectsByType<NpcDialogueActor>(FindObjectsInactive.Exclude);
#else
            m_CachedNpcActors = FindObjectsOfType<NpcDialogueActor>();
#endif
            return m_CachedNpcActors;
        }

        private void UpdateListenerStatus()
        {
            if (m_ListenerStatus == null)
            {
                return;
            }

            if (m_SelectedNpc == null)
            {
                m_ListenerStatus.text = "NO TARGET";
                return;
            }

            string status = m_CurrentInRange ? "IN RANGE" : "OUT OF RANGE";
            m_ListenerStatus.text = $"{status} / {m_SelectedNpc.name}".ToUpperInvariant();
        }

        private void SetDialogueVisible(bool visible, bool userInitiated, bool force)
        {
            if (!force && m_ChatVisible == visible)
            {
                return;
            }

            m_ChatVisible = visible;
            if (m_ChatContainer != null)
            {
                m_ChatContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ApplyGameplayInputSuppression(visible);

            if (
                userInitiated
                && !visible
                && m_RequireLeaveRangeAfterManualClose
                && m_CurrentInRange
            )
            {
                m_WaitForRangeExitToAutoOpen = true;
                m_ManualCloseNpcId = m_SelectedNpc != null ? m_SelectedNpc.NetworkObjectId : 0;
            }

            if (visible)
            {
                m_WaitForRangeExitToAutoOpen = false;
                m_ManualCloseNpcId = 0;
                if (m_ChatInput != null)
                {
                    m_ChatInput.schedule.Execute(() =>
                    {
                        if (m_ChatVisible && m_ChatInput != null)
                        {
                            m_ChatInput.Focus();
                        }
                    });
                }
            }

            if (m_LogProximityDebug)
            {
                NGLog.Debug(
                    "DialogueUI",
                    NGLog.Format(
                        "Dialogue visibility changed",
                        ("visible", visible),
                        ("userInitiated", userInitiated),
                        ("waitForExit", m_WaitForRangeExitToAutoOpen)
                    ),
                    this
                );
            }
        }

        private void OnCloseClicked()
        {
            if (m_CurrentInRange)
            {
                m_WaitForRangeExitToAutoOpen = true;
                m_ManualCloseNpcId = m_SelectedNpc != null ? m_SelectedNpc.NetworkObjectId : 0;
            }
            m_ManualCloseSuppressUntil =
                Time.unscaledTime + Mathf.Max(0f, m_ManualCloseSuppressSeconds);
            SetDialogueVisible(false, true, false);
        }

        private void EnsureInputTextVisible()
        {
            if (m_ChatInput == null)
            {
                return;
            }

            ApplyChatInputLegibilityStyle();

            Color fieldColor = m_ChatInput.resolvedStyle.color;
            if (IsUnreadable(fieldColor))
            {
                m_ChatInput.style.color = m_InputTextColor;
                m_ChatInput.style.opacity = 1f;
            }

            if (m_ChatInputInner == null)
            {
                return;
            }

            Color innerColor = m_ChatInputInner.resolvedStyle.color;
            if (IsUnreadable(innerColor))
            {
                m_ChatInputInner.style.color = m_InputTextColor;
                m_ChatInputInner.style.opacity = 1f;
            }
        }

        private void ApplyChatInputLegibilityStyle()
        {
            if (m_ChatInput == null)
            {
                return;
            }

            m_ChatInput.style.minHeight = MinChatInputHeight;
            m_ChatInput.style.fontSize = MinChatInputFontSize;
            m_ChatInput.style.opacity = 1f;

            if (m_ChatInputInner != null)
            {
                m_ChatInputInner.style.minHeight = MinChatInputHeight;
                m_ChatInputInner.style.fontSize = MinChatInputFontSize;
                m_ChatInputInner.style.paddingTop = 8f;
                m_ChatInputInner.style.paddingBottom = 8f;
                m_ChatInputInner.style.paddingLeft = 10f;
                m_ChatInputInner.style.paddingRight = 10f;
                m_ChatInputInner.style.unityTextAlign = TextAnchor.MiddleLeft;
                m_ChatInputInner.style.whiteSpace = WhiteSpace.NoWrap;
                m_ChatInputInner.style.opacity = 1f;
            }
        }

        private static bool IsUnreadable(Color c)
        {
            if (c.a <= 0.05f)
            {
                return true;
            }

            // Very dark text is effectively invisible on this HUD's dark background.
            float luminance = (c.r * 0.2126f) + (c.g * 0.7152f) + (c.b * 0.0722f);
            return luminance < 0.15f;
        }

        private void ApplyGameplayInputSuppression(bool suppress)
        {
            if (!suppress && m_GameplayInputSuppressed == suppress)
            {
                return;
            }

            Network_Game.ThirdPersonController.StarterAssetsInputs inputs =
                ResolveLocalStarterInputs();

            if (suppress)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
                if (inputs != null)
                {
                    inputs.cursorLocked = false;
                    inputs.cursorInputForLook = false;
                    inputs.SetCursorState(false);
                }
                m_GameplayInputSuppressed = true;
                return;
            }

            bool hasAuth =
                LocalPlayerAuthService.Instance != null
                && LocalPlayerAuthService.Instance.HasCurrentPlayer;
            if (!hasAuth)
            {
                m_GameplayInputSuppressed = false;
                return;
            }

            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
            if (inputs != null)
            {
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
                inputs.SetCursorState(true);
            }

            m_GameplayInputSuppressed = false;
        }

        private Network_Game.ThirdPersonController.StarterAssetsInputs ResolveLocalStarterInputs()
        {
            if (m_LocalStarterInputs != null)
            {
                return m_LocalStarterInputs;
            }

            if (Time.unscaledTime < m_NextInputResolveAt)
            {
                return null;
            }

            m_NextInputResolveAt = Time.unscaledTime + InputResolveInterval;

            if (
                NetworkManager.Singleton != null
                && NetworkManager.Singleton.LocalClient != null
                && NetworkManager.Singleton.LocalClient.PlayerObject != null
            )
            {
                m_LocalStarterInputs =
                    NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Network_Game.ThirdPersonController.StarterAssetsInputs>();
                if (m_LocalStarterInputs != null)
                {
                    return m_LocalStarterInputs;
                }
            }

#if UNITY_2023_1_OR_NEWER
            var allInputs =
                FindObjectsByType<Network_Game.ThirdPersonController.StarterAssetsInputs>(FindObjectsInactive.Exclude);
#else
            var allInputs =
                FindObjectsOfType<Network_Game.ThirdPersonController.StarterAssetsInputs>();
#endif
            if (allInputs != null && allInputs.Length > 0)
            {
                m_LocalStarterInputs = allInputs[0];
            }

            return m_LocalStarterInputs;
        }

        private void AppendSystemLine(string message)
        {
            AppendTranscript(
                new TranscriptEntry
                {
                    Role = TranscriptRole.System,
                    Speaker = "SYSTEM",
                    Message = message,
                }
            );
        }

        private void AppendTranscript(TranscriptEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Message))
            {
                return;
            }

            m_Transcript.Add(entry);
            while (m_Transcript.Count > MaxTranscriptLines)
            {
                m_Transcript.RemoveAt(0);
            }

            RenderTranscript();
        }

        private void RemovePending(int clientRequestId)
        {
            for (int i = m_Transcript.Count - 1; i >= 0; i--)
            {
                TranscriptEntry entry = m_Transcript[i];
                if (entry != null && entry.IsPending && entry.ClientRequestId == clientRequestId)
                {
                    m_Transcript.RemoveAt(i);
                }
            }

            RenderTranscript();
        }

        private void RenderTranscript()
        {
            if (m_TranscriptContent == null)
            {
                return;
            }

            m_TranscriptContent.Clear();
            for (int i = 0; i < m_Transcript.Count; i++)
            {
                TranscriptEntry entry = m_Transcript[i];
                if (entry == null)
                {
                    continue;
                }

                Label line = new Label($"{entry.Speaker}: {entry.Message}");
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.unityTextAlign = TextAnchor.UpperLeft;
                line.style.fontSize = 16f;
                line.style.marginBottom = 6f;
                line.style.color = ResolveRoleColor(entry);
                m_TranscriptContent.Add(line);
            }
        }

        private static Color ResolveRoleColor(TranscriptEntry entry)
        {
            if (entry == null)
            {
                return Color.white;
            }

            if (entry.IsPending)
            {
                return new Color(1f, 0.94f, 0.68f, 1f);
            }

            return entry.Role switch
            {
                TranscriptRole.Player => new Color(0.72f, 0.87f, 1f, 1f),
                TranscriptRole.Npc => new Color(0.73f, 0.97f, 0.77f, 1f),
                _ => new Color(1f, 0.92f, 0.72f, 1f),
            };
        }

        private void ScrollTranscriptToBottom()
        {
            if (m_TranscriptScroll == null)
            {
                return;
            }

            m_TranscriptScroll.scrollOffset = new Vector2(0f, float.MaxValue);
        }

        private string ResolveNetworkObjectName(ulong networkObjectId)
        {
            if (networkObjectId == 0)
            {
                return string.Empty;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null)
            {
                return string.Empty;
            }

            if (
                !manager.SpawnManager.SpawnedObjects.TryGetValue(
                    networkObjectId,
                    out NetworkObject obj
                )
            )
            {
                return string.Empty;
            }

            return obj != null ? obj.name : string.Empty;
        }

        private void LogProximityStateIfChanged(
            NetworkObject targetNpc,
            float distance,
            bool inRange,
            NetworkObject localPlayer
        )
        {
            if (!m_LogProximityDebug)
            {
                return;
            }

            string targetName = targetNpc != null ? targetNpc.name : "<none>";
            bool changed =
                inRange != m_LastLoggedInRange
                || m_ChatVisible != m_LastLoggedVisible
                || m_WaitForRangeExitToAutoOpen != m_LastLoggedWaitForExit
                || !string.Equals(targetName, m_LastLoggedTargetName, StringComparison.Ordinal)
                || Mathf.Abs(distance - m_LastLoggedDistance) > 0.25f;

            if (!changed)
            {
                return;
            }

            m_LastLoggedInRange = inRange;
            m_LastLoggedVisible = m_ChatVisible;
            m_LastLoggedWaitForExit = m_WaitForRangeExitToAutoOpen;
            m_LastLoggedTargetName = targetName;
            m_LastLoggedDistance = distance;

            NGLog.Debug(
                "DialogueUI",
                NGLog.Format(
                    "Proximity state",
                    ("inRange", inRange),
                    ("target", targetName),
                    ("distance", float.IsFinite(distance) ? distance.ToString("F2") : "n/a"),
                    ("chatVisible", m_ChatVisible),
                    ("waitForExit", m_WaitForRangeExitToAutoOpen),
                    ("radius", m_MaxListenerDistance),
                    ("player", localPlayer != null ? localPlayer.name : "<none>")
                ),
                this
            );
        }

        private void ConfigureCameraSwitchButton()
        {
            if (m_CameraSwitchButton == null)
            {
                return;
            }

            m_CameraSwitchButton.style.display = m_EnableCameraSwitchButton
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void OnCameraSwitchClicked()
        {
            if (!m_EnableCameraSwitchButton)
            {
                return;
            }

            RefreshCameraList(force: true);
            if (m_Cameras.Count == 0)
            {
                return;
            }

            int nextIndex = (m_SelectedCameraIndex + 1 + m_Cameras.Count) % m_Cameras.Count;
            ActivateCamera(nextIndex);
        }

        private void RefreshCameraList(bool force)
        {
            if (!force && Time.unscaledTime < m_NextCameraRefreshAt)
            {
                return;
            }

            m_NextCameraRefreshAt = Time.unscaledTime + MinCameraSearchCooldown;

#if UNITY_2023_1_OR_NEWER
            CinemachineVirtualCameraBase[] found = FindObjectsByType<CinemachineVirtualCameraBase>(
                FindObjectsInactive.Include
            );
#else
            CinemachineVirtualCameraBase[] found = FindObjectsOfType<CinemachineVirtualCameraBase>(
                true
            );
#endif
            m_Cameras.Clear();
            m_CameraBasePriorities.Clear();

            if (found == null || found.Length == 0)
            {
                m_SelectedCameraIndex = -1;
                UpdateCameraButtonLabel();
                return;
            }

            for (int i = 0; i < found.Length; i++)
            {
                CinemachineVirtualCameraBase camera = found[i];
                if (camera == null)
                {
                    continue;
                }

                m_Cameras.Add(camera);
                m_CameraBasePriorities[camera] = GetCameraPriority(camera);
            }

            m_Cameras.Sort(CompareCameraOrder);
            m_SelectedCameraIndex = ResolveCurrentCameraIndex();
            UpdateCameraButtonLabel();
        }

        private int CompareCameraOrder(
            CinemachineVirtualCameraBase left,
            CinemachineVirtualCameraBase right
        )
        {
            int leftRank = ResolveCameraRank(left != null ? left.name : string.Empty);
            int rightRank = ResolveCameraRank(right != null ? right.name : string.Empty);
            if (leftRank != rightRank)
            {
                return leftRank.CompareTo(rightRank);
            }

            string leftName = left != null ? left.name : string.Empty;
            string rightName = right != null ? right.name : string.Empty;
            return string.CompareOrdinal(leftName, rightName);
        }

        private int ResolveCameraRank(string cameraName)
        {
            if (string.IsNullOrWhiteSpace(cameraName) || m_PreferredCameraOrder == null)
            {
                return int.MaxValue;
            }

            for (int i = 0; i < m_PreferredCameraOrder.Count; i++)
            {
                if (string.Equals(m_PreferredCameraOrder[i], cameraName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private int ResolveCurrentCameraIndex()
        {
            if (m_Cameras.Count == 0)
            {
                return -1;
            }

            if (m_UseExclusiveCameraActivation)
            {
                for (int i = 0; i < m_Cameras.Count; i++)
                {
                    CinemachineVirtualCameraBase camera = m_Cameras[i];
                    if (camera != null && camera.gameObject.activeInHierarchy)
                    {
                        return i;
                    }
                }
            }

            int bestIndex = 0;
            int bestPriority = int.MinValue;
            for (int i = 0; i < m_Cameras.Count; i++)
            {
                int priority = GetCameraPriority(m_Cameras[i]);
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void ActivateCamera(int index)
        {
            if (index < 0 || index >= m_Cameras.Count)
            {
                return;
            }

            m_SelectedCameraIndex = index;
            for (int i = 0; i < m_Cameras.Count; i++)
            {
                CinemachineVirtualCameraBase camera = m_Cameras[i];
                if (camera == null)
                {
                    continue;
                }

                bool selected = i == index;
                if (m_UseExclusiveCameraActivation)
                {
                    camera.gameObject.SetActive(selected);
                }
                else
                {
                    int basePriority = m_CameraBasePriorities.TryGetValue(camera, out int stored)
                        ? stored
                        : GetCameraPriority(camera);
                    int priority = selected
                        ? basePriority + m_SelectedCameraPriorityBoost
                        : basePriority;
                    SetCameraPriority(camera, priority);
                }
            }

            UpdateCameraButtonLabel();
            CinemachineVirtualCameraBase selectedCamera = m_Cameras[index];
            NGLog.Info(
                "DialogueUI",
                NGLog.Format(
                    "Switched camera",
                    ("camera", selectedCamera != null ? selectedCamera.name : "<null>"),
                    ("exclusive", m_UseExclusiveCameraActivation),
                    ("index", index)
                ),
                this
            );
        }

        private void UpdateCameraButtonLabel()
        {
            if (m_CameraSwitchButton == null)
            {
                return;
            }

            if (m_Cameras.Count == 0)
            {
                m_CameraSwitchButton.text = "CAM";
                m_CameraSwitchButton.SetEnabled(false);
                return;
            }

            m_CameraSwitchButton.SetEnabled(true);
            m_CameraSwitchButton.text = "CAM";
        }

        private static int GetCameraPriority(CinemachineVirtualCameraBase camera)
        {
            if (camera == null)
            {
                return 0;
            }

            try
            {
                object value = camera.Priority;
                if (value is int intPriority)
                {
                    return intPriority;
                }

                if (value != null)
                {
                    var valueType = value.GetType();
                    var valueProperty = valueType.GetProperty("Value");
                    if (valueProperty != null && valueProperty.PropertyType == typeof(int))
                    {
                        return (int)valueProperty.GetValue(value);
                    }
                }
            }
            catch { }

            return 0;
        }

        private static void SetCameraPriority(CinemachineVirtualCameraBase camera, int priority)
        {
            if (camera == null)
            {
                return;
            }

            try
            {
                object value = camera.Priority;
                if (value is int)
                {
                    camera.Priority = priority;
                    return;
                }

                if (value != null)
                {
                    var valueType = value.GetType();
                    var valueProperty = valueType.GetProperty("Value");
                    if (valueProperty != null && valueProperty.PropertyType == typeof(int))
                    {
                        object boxed = value;
                        valueProperty.SetValue(boxed, priority);
                        camera.Priority = (PrioritySettings)boxed;
                    }
                }
            }
            catch { }
        }
    }
}
