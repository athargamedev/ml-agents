using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Network_Game.Diagnostics;
using Network_Game.ThirdPersonController;
using Network_Game.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Runtime visual feedback prompt for applied dialogue effects.
    /// Captures what the tester actually saw in-game and writes JSONL records.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(5000)]
    public sealed class DialogueEffectFeedbackPrompt : MonoBehaviour
    {
        private const string kLogCategory = "DialogueFX";
        private const int kWindowId = 96403;
        public static bool IsBlockingPromptActive { get; private set; }
        public static event Action<FeedbackSubmission> OnFeedbackSubmitted;

        public struct FeedbackSubmission
        {
            public DialogueSceneEffectsController.AppliedEffectInfo Effect;
            public string Outcome;
            public string Comment;
        }

        [Header("Prompt")]
        [SerializeField]
        private bool m_EnablePrompt = true;

        [SerializeField]
        [Tooltip("Pauses gameplay while a feedback prompt is open.")]
        private bool m_PauseGameWhilePromptOpen = false;

        [SerializeField]
        [Tooltip(
            "Render the feedback prompt using UI Toolkit to match project UI styles. Falls back to IMGUI when no UIDocument root is available."
         )]
        private bool m_UseUiToolkitOverlay = true;

        [SerializeField]
        [Tooltip(
            "If true, prompt captures cursor/input even when gameplay is not paused. Keep disabled for camera-safe probe runs."
         )]
        private bool m_CaptureInputWhenNotPaused = false;

        [SerializeField]
        [Min(1)]
        private int m_MaxQueuedPrompts = 12;

        [Header("Output")]
        [SerializeField]
        [Tooltip(
            "JSONL output path for visual feedback records. Relative path resolves from project root."
         )]
        private string m_OutputPath = "output/effect_visual_feedback.jsonl";

        [SerializeField]
        [Tooltip(
            "Also append compact visual records to the shared feedback log for downstream training utilities."
         )]
        private bool m_WriteUnifiedFeedbackLog = true;

        [SerializeField]
        [Tooltip("Unified feedback JSONL path. Relative path resolves from project root.")]
        private string m_UnifiedFeedbackPath = "output/feedback_log.jsonl";

        [SerializeField]
        private bool m_LogFeedbackSubmissions = true;

        [Header("Queue")]
        [SerializeField]
        [Min(0f)]
        [Tooltip("Suppresses duplicate prompts for the same effect+target within this window.")]
        private float m_DuplicateSuppressSeconds = 0.75f;

        [SerializeField]
        [Tooltip("If true, keep only one pending prompt while a prompt is already open.")]
        private bool m_KeepOnlyLatestWhilePromptOpen = true;

        [SerializeField]
        [Tooltip(
            "When a choice is submitted, drop queued prompts so gameplay can continue cleanly."
         )]
        private bool m_ClearQueuedPromptsOnSubmit = true;

        [SerializeField]
        [Tooltip("Clears pending prompt queue whenever active scene changes.")]
        private bool m_ClearQueueOnSceneChange = true;

        private struct PendingFeedback
        {
            public DialogueSceneEffectsController.AppliedEffectInfo Effect;
            public string SourceName;
            public string TargetName;
        }

        private readonly Queue<PendingFeedback> m_Queue = new Queue<PendingFeedback>();
        private PendingFeedback m_Current;
        private bool m_HasCurrent;
        private string m_ResolvedOutputPath = string.Empty;
        private string m_ResolvedUnifiedOutputPath = string.Empty;
        private string m_Comment = string.Empty;
        private Rect m_WindowRect = new Rect(12f, 12f, 1220f, 86f);
        private VisualElement m_UiHostRoot;
        private VisualElement m_UiOverlayRoot;
        private Label m_UiTitleLabel;
        private Label m_UiEffectLabel;
        private Label m_UiSourceLabel;
        private Label m_UiTargetLabel;
        private Label m_UiMetricsLabel;
        private Label m_UiModeLabel;
        private Label m_UiQueueLabel;
        private TextField m_UiCommentField;
        private readonly Dictionary<string, float> m_RecentQueueTimestamps = new Dictionary<
            string,
            float
            >(StringComparer.Ordinal);
        private string m_LastSceneName = string.Empty;
        private float m_PrePromptTimeScale = 1f;
        private bool m_TimeScaleCaptured;
        private CursorLockMode m_PrePromptCursorLockMode = CursorLockMode.None;
        private bool m_PrePromptCursorVisible = true;
        private bool m_CursorStateCaptured;
        private StarterAssetsInputs m_CachedStarterInputs;
        private float m_NextInputResolveTime = float.MinValue;
        private const float kInputResolveInterval = 0.5f;
        private bool m_RuntimeCaptureInputWhenNotPaused;
        private bool m_InteractionCaptureActive;
        private bool m_InputStateCaptured;
        private bool m_UsingHudCursorRouter;
        private bool m_PrePromptInputsCursorLocked = true;
        private bool m_PrePromptInputsCursorInputForLook = true;
#if ENABLE_INPUT_SYSTEM
        private PlayerInput m_CachedPlayerInput;
        private bool m_PrePromptPlayerInputEnabled;
        private bool m_PlayerInputStateCaptured;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimePrompt()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (FindExistingInstance() != null)
            {
                return;
            }

            var go = new GameObject("DialogueEffectFeedbackPrompt");
            DontDestroyOnLoad(go);
            go.AddComponent<DialogueEffectFeedbackPrompt>();
        }

        public static DialogueEffectFeedbackPrompt EnsureForAutomation()
        {
            EnsureRuntimePrompt();
            return FindExistingInstance();
        }

        public void ForceEnablePrompt(bool enabled = true)
        {
            m_EnablePrompt = enabled;
            if (!enabled)
            {
                ClearPendingPrompts("disabled_by_automation");
            }
            RefreshPromptUiState();
        }

        private static DialogueEffectFeedbackPrompt FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<DialogueEffectFeedbackPrompt>();
#else
            return FindAnyObjectByType<DialogueEffectFeedbackPrompt>();
#endif
        }

        private void Awake()
        {
            ResolveOutputPath();
            EnsureWindowInView();
            m_LastSceneName = SceneManager.GetActiveScene().name;
            m_RuntimeCaptureInputWhenNotPaused = m_CaptureInputWhenNotPaused;
            TryEnsureUiToolkitOverlay();
            RefreshPromptUiState();
        }

        private void OnEnable()
        {
            DialogueSceneEffectsController.OnEffectApplied += HandleEffectApplied;
        }

        private void OnDisable()
        {
            DialogueSceneEffectsController.OnEffectApplied -= HandleEffectApplied;
            CloseCurrentPrompt();
            DestroyUiToolkitOverlay();
        }

        private void Update()
        {
            EnsureWindowInView();
            TryEnsureUiToolkitOverlay();
            if (m_ClearQueueOnSceneChange)
            {
                string currentScene = SceneManager.GetActiveScene().name;
                if (!string.Equals(currentScene, m_LastSceneName, StringComparison.Ordinal))
                {
                    m_LastSceneName = currentScene;
                    ClearPendingPrompts("scene_changed");
                }
            }

            if (m_HasCurrent && ShouldCaptureInteraction())
            {
                EnsureModalInteractionState();
            }

            HandleKeyboardShortcuts();
        }

        private void LateUpdate()
        {
            if (m_HasCurrent && ShouldCaptureInteraction())
            {
                EnsureModalInteractionState();
            }
        }

        private void OnGUI()
        {
            if (m_UseUiToolkitOverlay && m_UiOverlayRoot != null)
            {
                return;
            }

            if (!m_EnablePrompt || !m_HasCurrent)
            {
                return;
            }

            m_WindowRect = GUI.ModalWindow(
                kWindowId,
                m_WindowRect,
                DrawPromptWindow,
                "Effect Visual Feedback"
            );
        }

        private void HandleEffectApplied(DialogueSceneEffectsController.AppliedEffectInfo info)
        {
            if (!m_EnablePrompt)
            {
                return;
            }

            var feedback = new PendingFeedback
            {
                Effect = info,
                SourceName = ResolveNetworkObjectName(info.SourceNetworkObjectId),
                TargetName = ResolveNetworkObjectName(info.TargetNetworkObjectId),
            };

            float delay = Mathf.Max(0f, info.FeedbackDelaySeconds);
            if (delay <= 0.001f)
            {
                EnqueueFeedback(feedback);
                return;
            }

            StartCoroutine(EnqueueFeedbackAfterDelay(feedback, delay));
        }

        private IEnumerator EnqueueFeedbackAfterDelay(PendingFeedback feedback, float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySeconds));
            EnqueueFeedback(feedback);
        }

        private void EnqueueFeedback(PendingFeedback feedback)
        {
            if (!m_EnablePrompt)
            {
                return;
            }

            DialogueSceneEffectsController.AppliedEffectInfo effect = feedback.Effect;
            if (ShouldSuppressDuplicate(effect))
            {
                if (m_LogFeedbackSubmissions)
                {
                    NGLog.Debug(
                        kLogCategory,
                        NGLog.Format(
                            "Suppressed duplicate visual feedback prompt",
                            ("effect", effect.EffectName ?? effect.EffectType ?? "unknown"),
                            ("target", effect.TargetNetworkObjectId.ToString())
                        )
                    );
                }
                return;
            }

            if (m_Queue.Count >= Mathf.Max(1, m_MaxQueuedPrompts))
            {
                m_Queue.Dequeue();
            }

            if (m_HasCurrent && m_KeepOnlyLatestWhilePromptOpen && m_Queue.Count > 0)
            {
                m_Queue.Clear();
            }

            m_Queue.Enqueue(feedback);
            if (m_LogFeedbackSubmissions)
            {
                NGLog.Debug(
                    kLogCategory,
                    NGLog.Format(
                        "Visual effect feedback prompt queued",
                        ("effect", effect.EffectName ?? effect.EffectType ?? "unknown"),
                        ("type", effect.EffectType ?? "unknown"),
                        ("queue", m_Queue.Count),
                        ("delay", effect.FeedbackDelaySeconds.ToString("F2"))
                    )
                );
            }

            if (!m_HasCurrent)
            {
                AdvanceToNextPrompt();
            }

            RefreshPromptUiState();
        }

        private void TryEnsureUiToolkitOverlay()
        {
            if (!m_UseUiToolkitOverlay)
            {
                DestroyUiToolkitOverlay();
                return;
            }

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

            if (
                m_UiOverlayRoot != null
                && (m_UiOverlayRoot.parent == null || m_UiOverlayRoot.panel == null)
            )
            {
                DestroyUiToolkitOverlay();
            }

            VisualElement topBarZone = Network_Game.UI.ModernHudManager.TryGetZone(Network_Game.UI.ModernHudManager.HudZone.TopBar);

            if (topBarZone != null)
            {
                BuildUiToolkitOverlay(topBarZone, true);
                RefreshPromptUiState();
                return;
            }

            UIDocument hostDocument = FindUiToolkitHostDocument();
            if (hostDocument == null || hostDocument.rootVisualElement == null)
            {
                return;
            }

            BuildUiToolkitOverlay(hostDocument.rootVisualElement, false);
            RefreshPromptUiState();
        }

        private UIDocument FindUiToolkitHostDocument()
        {
            UIDocument[] docs = FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);
            if (docs == null || docs.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < docs.Length; i++)
            {
                UIDocument doc = docs[i];
                if (doc == null)
                {
                    continue;
                }

                if (!doc.isActiveAndEnabled)
                {
                    continue;
                }

                if (!IsUsableUiToolkitHostRoot(doc.rootVisualElement))
                {
                    continue;
                }

                return doc;
            }

            return null;
        }

        private static bool IsUsableUiToolkitHostRoot(VisualElement hostRoot)
        {
            if (hostRoot == null || hostRoot.panel == null)
            {
                return false;
            }

            if (hostRoot.style.display.value == DisplayStyle.None)
            {
                return false;
            }

            return hostRoot.resolvedStyle.display != DisplayStyle.None;
        }

        private void BuildUiToolkitOverlay(VisualElement hostRoot, bool useHudTopBarZone)
        {
            if (hostRoot == null)
            {
                return;
            }

            m_UiHostRoot = hostRoot;

            var overlay = new VisualElement { name = "dialogue-effect-feedback-overlay" };
            overlay.style.display = DisplayStyle.None;
            overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            overlay.pickingMode = PickingMode.Ignore;

            if (!useHudTopBarZone)
            {
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0f;
                overlay.style.right = 0f;
                overlay.style.top = 0f;
                overlay.style.bottom = 0f;
            }
            else
            {
                overlay.style.flexGrow = 1f;
                overlay.style.flexDirection = FlexDirection.Column;
                overlay.style.alignItems = Align.Stretch;
            }

            var card = new VisualElement { name = "dialogue-effect-feedback-card" };
            card.AddToClassList("blocks-profile-card");
            card.style.backgroundColor = new Color(16f / 255f, 16f / 255f, 16f / 255f, 0.80f);
            card.style.borderTopLeftRadius = 12f;
            card.style.borderTopRightRadius = 12f;
            card.style.borderBottomLeftRadius = 12f;
            card.style.borderBottomRightRadius = 12f;
            card.style.paddingTop = 6f;
            card.style.paddingBottom = 6f;
            card.style.paddingLeft = 10f;
            card.style.paddingRight = 10f;
            card.style.borderTopWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderTopColor = new Color(0.26f, 0.26f, 0.26f, 1f);
            card.style.borderBottomColor = new Color(0.26f, 0.26f, 0.26f, 1f);
            card.style.borderLeftColor = new Color(0.26f, 0.26f, 0.26f, 1f);
            card.style.borderRightColor = new Color(0.26f, 0.26f, 0.26f, 1f);
            card.style.flexDirection = FlexDirection.Column;
            card.style.alignItems = Align.Stretch;
            card.style.justifyContent = Justify.FlexStart;
            card.style.minHeight = 78f;

            if (!useHudTopBarZone)
            {
                card.style.position = Position.Absolute;
                card.style.left = 12f;
                card.style.right = 12f;
                card.style.top = 10f;
            }
            else
            {
                card.style.width = new Length(100f, LengthUnit.Percent);
                card.style.flexGrow = 1f;
            }

            var summaryRow = new VisualElement();
            summaryRow.style.flexDirection = FlexDirection.Row;
            summaryRow.style.alignItems = Align.Center;
            summaryRow.style.marginBottom = 3f;

            var titleBlock = new VisualElement();
            titleBlock.style.width = 138f;
            titleBlock.style.flexShrink = 0f;
            titleBlock.style.flexDirection = FlexDirection.Column;
            titleBlock.style.alignItems = Align.FlexStart;
            titleBlock.style.marginRight = 10f;

            m_UiTitleLabel = new Label("EFFECT FEEDBACK");
            m_UiTitleLabel.AddToClassList("blocks-header");
            m_UiTitleLabel.style.fontSize = 11f;
            m_UiTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_UiTitleLabel.style.letterSpacing = 0.3f;
            titleBlock.Add(m_UiTitleLabel);

            m_UiQueueLabel = new Label("Queue: 0");
            m_UiQueueLabel.AddToClassList("blocks-login-label");
            m_UiQueueLabel.style.fontSize = 9f;
            m_UiQueueLabel.style.opacity = 0.8f;
            titleBlock.Add(m_UiQueueLabel);

            summaryRow.Add(titleBlock);

            var infoColumn = new VisualElement();
            infoColumn.style.flexGrow = 1f;
            infoColumn.style.flexShrink = 1f;
            infoColumn.style.minWidth = 0f;

            m_UiEffectLabel = CreateInfoLabel(infoColumn);
            m_UiSourceLabel = CreateInfoLabel(infoColumn);
            m_UiTargetLabel = CreateInfoLabel(infoColumn);
            m_UiTargetLabel.style.display = DisplayStyle.None;
            m_UiMetricsLabel = CreateInfoLabel(infoColumn);
            summaryRow.Add(infoColumn);
            card.Add(summaryRow);

            m_UiModeLabel = new Label(string.Empty);
            m_UiModeLabel.AddToClassList("blocks-login-label");
            m_UiModeLabel.style.fontSize = 8f;
            m_UiModeLabel.style.color = new Color(0.72f, 0.96f, 0.76f, 1f);
            m_UiModeLabel.style.marginBottom = 4f;
            m_UiModeLabel.style.whiteSpace = WhiteSpace.NoWrap;
            m_UiModeLabel.style.overflow = Overflow.Hidden;
            m_UiModeLabel.style.textOverflow = TextOverflow.Ellipsis;
            card.Add(m_UiModeLabel);

            var actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.alignItems = Align.Center;
            actionsRow.style.marginBottom = 4f;

            var actionsWrap = new VisualElement { name = "dialogue-effect-feedback-actions" };
            actionsWrap.style.flexDirection = FlexDirection.Row;
            actionsWrap.style.flexWrap = Wrap.Wrap;
            actionsWrap.style.alignItems = Align.Center;
            actionsWrap.style.justifyContent = Justify.FlexStart;
            actionsWrap.style.flexGrow = 1f;
            actionsWrap.style.flexShrink = 1f;
            actionsWrap.Add(CreateOutcomeButton("CORRECT", "looks_correct", "Looks Correct"));
            actionsWrap.Add(CreateOutcomeButton("HIDDEN", "not_visible", "Not Visible"));
            actionsWrap.Add(CreateOutcomeButton("TARGET", "wrong_target", "Wrong Target"));
            actionsWrap.Add(CreateOutcomeButton("PLACE", "wrong_placement", "Wrong Placement"));
            actionsWrap.Add(CreateOutcomeButton("MESH", "wrong_mesh_fit", "Wrong Mesh Fit"));
            actionsWrap.Add(CreateOutcomeButton("SKIP", "skipped", "Skip"));
            actionsRow.Add(actionsWrap);
            card.Add(actionsRow);

            var notesRow = new VisualElement();
            notesRow.style.flexDirection = FlexDirection.Row;
            notesRow.style.alignItems = Align.Center;

            if (useHudTopBarZone)
            {
                ModernHudLayoutProfile profile = Network_Game.UI.ModernHudManager.Active != null
                    ? Network_Game.UI.ModernHudManager.Active.LayoutProfile
                    : null;

                float summaryWeight = 0.34f;
                float actionsWeight = 0.31f;
                float notesWeight = 0.35f;
                if (profile != null)
                {
                    profile.GetNormalizedFeedbackRowWeights(
                        out summaryWeight,
                        out actionsWeight,
                        out notesWeight
                    );
                }

                ApplyHudTopBarRowLayout(
                    summaryRow,
                    actionsRow,
                    notesRow,
                    summaryWeight,
                    actionsWeight,
                    notesWeight
                );
            }

            m_UiCommentField = new TextField();
            m_UiCommentField.multiline = false;
            m_UiCommentField.value = m_Comment ?? string.Empty;
            m_UiCommentField.AddToClassList("blocks-textfield");
            m_UiCommentField.style.flexGrow = 1f;
            m_UiCommentField.style.flexShrink = 1f;
            m_UiCommentField.style.minWidth = 0f;
            m_UiCommentField.style.minHeight = 22f;
            m_UiCommentField.style.maxHeight = 22f;
            m_UiCommentField.style.height = 22f;
            m_UiCommentField.style.whiteSpace = WhiteSpace.NoWrap;
            m_UiCommentField.style.marginRight = 6f;
            m_UiCommentField.RegisterValueChangedCallback(evt =>
                m_Comment = evt.newValue ?? string.Empty
            );
            notesRow.Add(m_UiCommentField);

            var submitNoteButton = new Button(() => SubmitCurrent("note_only", m_Comment))
            {
                text = "NOTE",
            };
            submitNoteButton.AddToClassList("blocks-button");
            submitNoteButton.style.height = 22f;
            submitNoteButton.style.minWidth = 56f;
            submitNoteButton.style.flexShrink = 0f;
            submitNoteButton.style.fontSize = 9f;
            notesRow.Add(submitNoteButton);

            card.Add(notesRow);
            overlay.Add(card);
            hostRoot.Add(overlay);

            m_UiOverlayRoot = overlay;
        }

        private Label CreateInfoLabel(VisualElement parent)
        {
            var label = new Label(string.Empty);
            label.AddToClassList("blocks-login-label");
            label.style.fontSize = 9f;
            label.style.marginBottom = 1f;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexShrink = 1f;
            parent.Add(label);
            return label;
        }

        private Button CreateOutcomeButton(string label, string outcome, string tooltip)
        {
            var button = new Button(() => SubmitCurrent(outcome, m_Comment)) { text = label };
            button.AddToClassList("blocks-button");
            button.tooltip = tooltip;
            button.style.height = 19f;
            button.style.minWidth = 58f;
            button.style.marginRight = 4f;
            button.style.marginBottom = 4f;
            button.style.paddingLeft = 4f;
            button.style.paddingRight = 4f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.fontSize = 8f;
            button.style.letterSpacing = 0f;
            return button;
        }

        private static void ApplyHudTopBarRowLayout(
            VisualElement summaryRow,
            VisualElement actionsRow,
            VisualElement notesRow,
            float summaryWeight,
            float actionsWeight,
            float notesWeight
        )
        {
            ConfigureTopBarRow(summaryRow, summaryWeight, 28f);
            ConfigureTopBarRow(actionsRow, actionsWeight, 24f);
            ConfigureTopBarRow(notesRow, notesWeight, 24f);
        }

        private static void ConfigureTopBarRow(VisualElement row, float weight, float minHeight)
        {
            if (row == null)
            {
                return;
            }

            row.style.flexGrow = Mathf.Max(0.01f, weight);
            row.style.flexShrink = 1f;
            row.style.minHeight = minHeight;
        }

        private void DestroyUiToolkitOverlay()
        {
            if (m_UiOverlayRoot != null && m_UiOverlayRoot.parent != null)
            {
                m_UiOverlayRoot.parent.Remove(m_UiOverlayRoot);
            }

            m_UiHostRoot = null;
            m_UiOverlayRoot = null;
            m_UiTitleLabel = null;
            m_UiEffectLabel = null;
            m_UiSourceLabel = null;
            m_UiTargetLabel = null;
            m_UiMetricsLabel = null;
            m_UiModeLabel = null;
            m_UiQueueLabel = null;
            m_UiCommentField = null;
        }

        private void RefreshPromptUiState()
        {
            bool active = m_EnablePrompt && m_HasCurrent;

            Network_Game.UI.ModernHudManager.SetFeedbackVisible(active);

            if (m_UiOverlayRoot == null)
            {
                return;
            }

            m_UiOverlayRoot.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            m_UiOverlayRoot.pickingMode =
                active && ShouldCaptureInteraction() ? PickingMode.Position : PickingMode.Ignore;

            if (m_UiQueueLabel != null)
            {
                int queued = m_Queue.Count + (m_HasCurrent ? 1 : 0);
                m_UiQueueLabel.text = $"Queue: {queued}";
            }

            if (m_UiCommentField != null && m_UiCommentField.value != (m_Comment ?? string.Empty))
            {
                m_UiCommentField.SetValueWithoutNotify(m_Comment ?? string.Empty);
            }

            if (!active)
            {
                return;
            }

            DialogueSceneEffectsController.AppliedEffectInfo effect = m_Current.Effect;
            if (m_UiEffectLabel != null)
            {
                m_UiEffectLabel.text = $"{effect.EffectName} | {effect.EffectType}";
            }
            if (m_UiSourceLabel != null)
            {
                m_UiSourceLabel.text =
                    $"{BuildDisplayName(m_Current.SourceName, effect.SourceNetworkObjectId)} -> {BuildDisplayName(m_Current.TargetName, effect.TargetNetworkObjectId)}";
            }
            if (m_UiMetricsLabel != null)
            {
                m_UiMetricsLabel.text =
                    $"{effect.Scale:F2}x  {effect.DurationSeconds:F1}s  {(effect.AttachToTarget ? "Attached" : "Free")}  Mesh {(effect.FitToTargetMesh ? "On" : "Off")}";
            }

            if (m_UiModeLabel != null)
            {
                if (!ShouldCaptureInteraction())
                {
                    m_UiModeLabel.text = "1-6 submit  •  Enter note  •  F8 pointer";
                }
                else if (!m_PauseGameWhilePromptOpen)
                {
                    m_UiModeLabel.text = "Pointer mode on  •  F8 to exit";
                }
                else
                {
                    m_UiModeLabel.text = "Modal input capture enabled";
                }
            }
        }

        private void DrawPromptWindow(int windowId)
        {
            if (ShouldCaptureInteraction())
            {
                EnsureModalInteractionState();
            }

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("EFFECT", GUILayout.Width(54f));
            GUILayout.Label($"Q:{m_Queue.Count + 1}", GUILayout.Width(36f));
            GUILayout.Label(
                $"{m_Current.Effect.EffectName} | {m_Current.Effect.EffectType}",
                GUILayout.Width(220f)
            );
            GUILayout.Label(
                $"{BuildDisplayName(m_Current.SourceName, m_Current.Effect.SourceNetworkObjectId)} -> {BuildDisplayName(m_Current.TargetName, m_Current.Effect.TargetNetworkObjectId)}",
                GUILayout.ExpandWidth(true)
            );
            GUILayout.Label(
                $"{m_Current.Effect.Scale:F2}x  {m_Current.Effect.DurationSeconds:F1}s  {(m_Current.Effect.AttachToTarget ? "Attached" : "Free")}  Mesh {(m_Current.Effect.FitToTargetMesh ? "On" : "Off")}",
                GUILayout.Width(220f)
            );
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("CORRECT", GUILayout.Height(20f), GUILayout.Width(70f)))
            {
                SubmitCurrent("looks_correct", m_Comment);
            }
            if (GUILayout.Button("HIDDEN", GUILayout.Height(20f), GUILayout.Width(66f)))
            {
                SubmitCurrent("not_visible", m_Comment);
            }
            if (GUILayout.Button("TARGET", GUILayout.Height(20f), GUILayout.Width(66f)))
            {
                SubmitCurrent("wrong_target", m_Comment);
            }
            if (GUILayout.Button("PLACE", GUILayout.Height(20f), GUILayout.Width(62f)))
            {
                SubmitCurrent("wrong_placement", m_Comment);
            }
            if (GUILayout.Button("MESH", GUILayout.Height(20f), GUILayout.Width(60f)))
            {
                SubmitCurrent("wrong_mesh_fit", m_Comment);
            }
            if (GUILayout.Button("SKIP", GUILayout.Height(20f), GUILayout.Width(56f)))
            {
                SubmitCurrent("skipped", m_Comment);
            }
            GUILayout.Space(6f);
            if (!ShouldCaptureInteraction())
            {
                GUILayout.Label("1-6 / Enter / F8", GUILayout.Width(88f));
            }
            else if (!m_PauseGameWhilePromptOpen)
            {
                GUILayout.Label("F8 exits", GUILayout.Width(60f));
            }
            else
            {
                GUILayout.Label("Modal", GUILayout.Width(60f));
            }
            m_Comment = GUILayout.TextField(
                m_Comment ?? string.Empty,
                GUILayout.Height(22f),
                GUILayout.ExpandWidth(true)
            );
            if (GUILayout.Button("NOTE", GUILayout.Height(22f), GUILayout.Width(56f)))
            {
                SubmitCurrent("note_only", m_Comment);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private void SubmitCurrent(string outcome, string comment)
        {
            if (!m_HasCurrent)
            {
                return;
            }

            WriteFeedbackRecord(m_Current, outcome, comment ?? string.Empty);
            EmitFeedbackSubmitted(m_Current, outcome, comment ?? string.Empty);
            m_Comment = string.Empty;
            if (m_ClearQueuedPromptsOnSubmit)
            {
                m_Queue.Clear();
            }
            CloseCurrentPrompt();
            RefreshPromptUiState();
        }

        private static void EmitFeedbackSubmitted(
            PendingFeedback feedback,
            string outcome,
            string comment
        )
        {
            Action<FeedbackSubmission> handler = OnFeedbackSubmitted;
            if (handler == null)
            {
                return;
            }

            handler(
                new FeedbackSubmission
                {
                    Effect = feedback.Effect,
                    Outcome = outcome ?? string.Empty,
                    Comment = comment ?? string.Empty,
                }
            );
        }

        private void AdvanceToNextPrompt()
        {
            if (m_Queue.Count == 0)
            {
                CloseCurrentPrompt();
                return;
            }

            m_Current = m_Queue.Dequeue();
            m_HasCurrent = true;
            IsBlockingPromptActive = true;

            if (m_PauseGameWhilePromptOpen)
            {
                CaptureAndPauseTimeScale();
            }
            else if (ShouldCaptureInteraction())
            {
                SetUiInteractionEnabled(true);
            }

            if (!m_UseUiToolkitOverlay)
            {
                float width = Mathf.Clamp(Screen.width - 24f, 360f, Screen.width - 20f);
                float height = Mathf.Clamp(m_WindowRect.height, 72f, 92f);
                m_WindowRect = new Rect(10f, 10f, width, height);
            }

            RefreshPromptUiState();
        }

        private void CloseCurrentPrompt()
        {
            m_HasCurrent = false;
            IsBlockingPromptActive = false;
            RestoreTimeScaleIfNeeded();
            SetUiInteractionEnabled(false);
            RefreshPromptUiState();
        }

        private bool ShouldCaptureInteraction()
        {
            return m_PauseGameWhilePromptOpen || m_RuntimeCaptureInputWhenNotPaused;
        }

        private void CaptureAndPauseTimeScale()
        {
            if (!m_TimeScaleCaptured)
            {
                m_PrePromptTimeScale = Time.timeScale;
                m_TimeScaleCaptured = true;
            }

            if (!m_CursorStateCaptured)
            {
                m_PrePromptCursorLockMode = Cursor.lockState;
                m_PrePromptCursorVisible = Cursor.visible;
                m_CursorStateCaptured = true;
            }

            SetUiInteractionEnabled(true);
            Time.timeScale = 0f;
        }

        private void RestoreTimeScaleIfNeeded()
        {
            if (!m_TimeScaleCaptured)
            {
                return;
            }

            Time.timeScale = Mathf.Max(0f, m_PrePromptTimeScale);
            m_TimeScaleCaptured = false;
            m_PrePromptTimeScale = 1f;
            SetUiInteractionEnabled(false);
        }

        private void EnsureModalInteractionState()
        {
            if (!ShouldCaptureInteraction())
            {
                return;
            }

            if (m_PauseGameWhilePromptOpen && Time.timeScale != 0f)
            {
                Time.timeScale = 0f;
            }

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }

            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }

            SetUiInteractionEnabled(true);
        }

        private void ClearPendingPrompts(string reason)
        {
            m_Queue.Clear();
            m_HasCurrent = false;
            IsBlockingPromptActive = false;
            m_Comment = string.Empty;
            RestoreTimeScaleIfNeeded();
            SetUiInteractionEnabled(false);
            RefreshPromptUiState();

            if (m_LogFeedbackSubmissions)
            {
                NGLog.Info(
                    kLogCategory,
                    NGLog.Format(
                        "Cleared feedback prompt queue",
                        ("reason", reason ?? string.Empty)
                    )
                );
            }
        }

        private bool ShouldSuppressDuplicate(
            DialogueSceneEffectsController.AppliedEffectInfo effect
        )
        {
            float window = Mathf.Max(0f, m_DuplicateSuppressSeconds);
            if (window <= 0f)
            {
                return false;
            }

            string key = BuildEffectQueueKey(effect);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            float now = Time.realtimeSinceStartup;
            if (m_RecentQueueTimestamps.TryGetValue(key, out float previousAt))
            {
                if (now - previousAt < window)
                {
                    return true;
                }
            }

            m_RecentQueueTimestamps[key] = now;
            if (m_RecentQueueTimestamps.Count > 256)
            {
                TrimRecentQueueMap(now, window * 2f);
            }

            return false;
        }

        private void TrimRecentQueueMap(float now, float ttlSeconds)
        {
            if (m_RecentQueueTimestamps.Count == 0)
            {
                return;
            }

            var stale = new List<string>();
            foreach (KeyValuePair<string, float> kvp in m_RecentQueueTimestamps)
            {
                if (now - kvp.Value > ttlSeconds)
                {
                    stale.Add(kvp.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                m_RecentQueueTimestamps.Remove(stale[i]);
            }
        }

        private static string BuildEffectQueueKey(
            DialogueSceneEffectsController.AppliedEffectInfo effect
        )
        {
            return string.Concat(
                effect.EffectType ?? string.Empty,
                "|",
                effect.EffectName ?? string.Empty,
                "|",
                effect.TargetNetworkObjectId.ToString()
            );
        }

        private void ResolveOutputPath()
        {
            m_ResolvedOutputPath = ResolvePath(m_OutputPath, "output/effect_visual_feedback.jsonl");
            m_ResolvedUnifiedOutputPath = ResolvePath(
                m_UnifiedFeedbackPath,
                "output/feedback_log.jsonl"
            );

            EnsureDirectoryForPath(m_ResolvedOutputPath);
            EnsureDirectoryForPath(m_ResolvedUnifiedOutputPath);
        }

        private static string ResolvePath(string configuredPath, string fallback)
        {
            string configured = string.IsNullOrWhiteSpace(configuredPath)
                ? fallback
                : configuredPath.Trim();

            if (Path.IsPathRooted(configured))
            {
                return configured;
            }

            string baseDir = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                : Application.persistentDataPath;
            return Path.GetFullPath(Path.Combine(baseDir, configured));
        }

        private static void EnsureDirectoryForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string dir = Path.GetDirectoryName(path);
#if !UNITY_WEBGL
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
#endif
        }

        private void EnsureWindowInView()
        {
            float width = Mathf.Clamp(Screen.width - 24f, 360f, Screen.width - 20f);
            float height = Mathf.Clamp(m_WindowRect.height, 72f, 92f);
            m_WindowRect = new Rect(10f, 10f, width, height);
        }

        private void WriteFeedbackRecord(PendingFeedback feedback, string outcome, string comment)
        {
            if (string.IsNullOrWhiteSpace(m_ResolvedOutputPath))
            {
                ResolveOutputPath();
            }

            DialogueSceneEffectsController.AppliedEffectInfo effect = feedback.Effect;
            string sceneName = SceneManager.GetActiveScene().name;
            string json = string.Concat(
                "{",
                "\"ts\":\"",
                EscapeJson(DateTime.UtcNow.ToString("o")),
                "\"",
                ",\"scene\":\"",
                EscapeJson(sceneName),
                "\"",
                ",\"effect_type\":\"",
                EscapeJson(effect.EffectType),
                "\"",
                ",\"effect_name\":\"",
                EscapeJson(effect.EffectName),
                "\"",
                ",\"source_network_id\":",
                effect.SourceNetworkObjectId.ToString(),
                ",\"target_network_id\":",
                effect.TargetNetworkObjectId.ToString(),
                ",\"source_name\":\"",
                EscapeJson(feedback.SourceName),
                "\"",
                ",\"target_name\":\"",
                EscapeJson(feedback.TargetName),
                "\"",
                ",\"position\":[",
                effect.Position.x.ToString("F3"),
                ",",
                effect.Position.y.ToString("F3"),
                ",",
                effect.Position.z.ToString("F3"),
                "]",
                ",\"scale\":",
                effect.Scale.ToString("F3"),
                ",\"duration_seconds\":",
                effect.DurationSeconds.ToString("F3"),
                ",\"feedback_delay_seconds\":",
                effect.FeedbackDelaySeconds.ToString("F3"),
                ",\"attach_to_target\":",
                effect.AttachToTarget ? "true" : "false",
                ",\"fit_to_target_mesh\":",
                effect.FitToTargetMesh ? "true" : "false",
                ",\"outcome\":\"",
                EscapeJson(outcome),
                "\"",
                ",\"comment\":\"",
                EscapeJson(comment ?? string.Empty),
                "\"",
                "}"
            );

            try
            {
#if !UNITY_WEBGL
                File.AppendAllText(m_ResolvedOutputPath, json + "\n", Encoding.UTF8);
#endif
                if (m_WriteUnifiedFeedbackLog)
                {
                    WriteUnifiedFeedbackRecord(feedback, outcome, comment);
                }
                if (m_LogFeedbackSubmissions)
                {
                    NGLog.Info(
                        kLogCategory,
                        NGLog.Format(
                            "Visual effect feedback recorded",
                            ("effect", effect.EffectName ?? effect.EffectType ?? "unknown"),
                            ("outcome", outcome ?? "unknown"),
                            ("path", m_ResolvedOutputPath)
                        )
                    );
                }
            }
            catch (IOException ex)
            {
                NGLog.Warn(kLogCategory, $"Failed to write visual feedback: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                NGLog.Warn(kLogCategory, $"Failed to write visual feedback: {ex.Message}");
            }
        }

        private void WriteUnifiedFeedbackRecord(
            PendingFeedback feedback,
            string outcome,
            string comment
        )
        {
            if (string.IsNullOrWhiteSpace(m_ResolvedUnifiedOutputPath))
            {
                m_ResolvedUnifiedOutputPath = ResolvePath(
                    m_UnifiedFeedbackPath,
                    "output/feedback_log.jsonl"
                );
                EnsureDirectoryForPath(m_ResolvedUnifiedOutputPath);
            }

            DialogueSceneEffectsController.AppliedEffectInfo effect = feedback.Effect;
            int score = OutcomeToScore(outcome);
            string record = string.Concat(
                "{",
                "\"ts\":\"",
                EscapeJson(DateTime.UtcNow.ToString("o")),
                "\"",
                ",\"record_type\":\"visual_feedback\"",
                ",\"npc_id\":\"",
                EscapeJson(feedback.SourceName),
                "\"",
                ",\"prompt\":\"\"",
                ",\"response\":\"\"",
                ",\"score\":",
                score.ToString(),
                ",\"status\":\"visual_feedback\"",
                ",\"effect_type\":\"",
                EscapeJson(effect.EffectType),
                "\"",
                ",\"effect_name\":\"",
                EscapeJson(effect.EffectName),
                "\"",
                ",\"outcome\":\"",
                EscapeJson(outcome),
                "\"",
                ",\"comment\":\"",
                EscapeJson(comment ?? string.Empty),
                "\"",
                ",\"target_name\":\"",
                EscapeJson(feedback.TargetName),
                "\"",
                ",\"target_network_id\":",
                effect.TargetNetworkObjectId.ToString(),
                ",\"source_network_id\":",
                effect.SourceNetworkObjectId.ToString(),
                "}"
            );

#if !UNITY_WEBGL
            File.AppendAllText(m_ResolvedUnifiedOutputPath, record + "\n", Encoding.UTF8);
#endif
        }

        private static int OutcomeToScore(string outcome)
        {
            string normalized = string.IsNullOrWhiteSpace(outcome)
                ? string.Empty
                : outcome.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "looks_correct":
                    return 3;
                case "not_visible":
                    return -3;
                case "wrong_target":
                case "wrong_placement":
                case "wrong_mesh_fit":
                    return -2;
                case "skipped":
                case "note_only":
                default:
                    return 0;
            }
        }

        private static string ResolveNetworkObjectName(ulong networkObjectId)
        {
            if (networkObjectId == 0)
            {
                return string.Empty;
            }

            if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
            {
                return string.Empty;
            }

            if (
                NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                    networkObjectId,
                    out NetworkObject networkObject
                )
                && networkObject != null
            )
            {
                return networkObject.name;
            }

            return string.Empty;
        }

        private void SetUiInteractionEnabled(bool enabled)
        {
            if (enabled && !ShouldCaptureInteraction())
            {
                return;
            }

            StarterAssetsInputs inputs = ResolveLocalPlayerInputs();
#if ENABLE_INPUT_SYSTEM
            PlayerInput playerInput = ResolveLocalPlayerPlayerInput();
#endif
            if (enabled)
            {
                if (m_InteractionCaptureActive)
                {
                    return;
                }

                m_InteractionCaptureActive = true;
                m_UsingHudCursorRouter = Network_Game.UI.ModernHudManager.TryAcquireUiCursor(this);

                if (!m_UsingHudCursorRouter)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    if (inputs != null)
                    {
                        if (!m_InputStateCaptured)
                        {
                            m_PrePromptInputsCursorLocked = inputs.cursorLocked;
                            m_PrePromptInputsCursorInputForLook = inputs.cursorInputForLook;
                            m_InputStateCaptured = true;
                        }

                        inputs.cursorLocked = false;
                        inputs.cursorInputForLook = false;
                        inputs.SetCursorState(false);
                    }
                }
#if ENABLE_INPUT_SYSTEM
                if (playerInput != null && m_PauseGameWhilePromptOpen)
                {
                    if (!m_PlayerInputStateCaptured)
                    {
                        m_PrePromptPlayerInputEnabled = playerInput.enabled;
                        m_PlayerInputStateCaptured = true;
                    }
                    playerInput.enabled = false;
                }
#endif
                return;
            }

            if (!m_InteractionCaptureActive)
            {
                return;
            }

            m_InteractionCaptureActive = false;
            bool usedHudCursorRouter = m_UsingHudCursorRouter;

            if (usedHudCursorRouter)
            {
                Network_Game.UI.ModernHudManager.TryReleaseUiCursor(this);
                m_UsingHudCursorRouter = false;
                m_CursorStateCaptured = false;
            }
            else if (m_CursorStateCaptured)
            {
                Cursor.lockState = m_PrePromptCursorLockMode;
                Cursor.visible = m_PrePromptCursorVisible;
                m_CursorStateCaptured = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (!usedHudCursorRouter && inputs != null)
            {
                if (m_InputStateCaptured)
                {
                    inputs.cursorLocked = m_PrePromptInputsCursorLocked;
                    inputs.cursorInputForLook = m_PrePromptInputsCursorInputForLook;
                    inputs.SetCursorState(m_PrePromptInputsCursorLocked);
                    m_InputStateCaptured = false;
                }
                else
                {
                    bool shouldLock = Cursor.lockState == CursorLockMode.Locked;
                    inputs.cursorLocked = shouldLock;
                    inputs.cursorInputForLook = shouldLock;
                }
            }

            if (usedHudCursorRouter)
            {
                m_InputStateCaptured = false;
            }

#if ENABLE_INPUT_SYSTEM
            if (playerInput != null && m_PlayerInputStateCaptured)
            {
                playerInput.enabled = m_PrePromptPlayerInputEnabled;
                m_PlayerInputStateCaptured = false;
            }
#endif
        }

        private StarterAssetsInputs ResolveLocalPlayerInputs()
        {
            if (m_CachedStarterInputs != null)
            {
                return m_CachedStarterInputs;
            }

            if (Time.unscaledTime < m_NextInputResolveTime)
            {
                return null;
            }

            m_NextInputResolveTime = Time.unscaledTime + kInputResolveInterval;

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

#if UNITY_2023_1_OR_NEWER
            StarterAssetsInputs[] inputs = FindObjectsByType<StarterAssetsInputs>(
                FindObjectsInactive.Exclude
            );
#else
            StarterAssetsInputs[] inputs = FindObjectsOfType<StarterAssetsInputs>();
#endif
            if (inputs != null && inputs.Length > 0)
            {
                m_CachedStarterInputs = inputs[0];
            }

            return m_CachedStarterInputs;
        }

#if ENABLE_INPUT_SYSTEM
        private PlayerInput ResolveLocalPlayerPlayerInput()
        {
            if (m_CachedPlayerInput != null)
            {
                return m_CachedPlayerInput;
            }

            if (
                NetworkManager.Singleton != null
                && NetworkManager.Singleton.LocalClient != null
                && NetworkManager.Singleton.LocalClient.PlayerObject != null
            )
            {
                m_CachedPlayerInput =
                    NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerInput>();
                if (m_CachedPlayerInput != null)
                {
                    return m_CachedPlayerInput;
                }
            }

#if UNITY_2023_1_OR_NEWER
            PlayerInput[] inputs = FindObjectsByType<PlayerInput>(FindObjectsInactive.Exclude);
#else
            PlayerInput[] inputs = FindObjectsOfType<PlayerInput>();
#endif
            if (inputs != null && inputs.Length > 0)
            {
                m_CachedPlayerInput = inputs[0];
            }

            return m_CachedPlayerInput;
        }

#endif

        private void HandleKeyboardShortcuts()
        {
            if (!m_HasCurrent)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (!m_PauseGameWhilePromptOpen && kb.f8Key.wasPressedThisFrame)
            {
                m_RuntimeCaptureInputWhenNotPaused = !m_RuntimeCaptureInputWhenNotPaused;
                if (m_RuntimeCaptureInputWhenNotPaused)
                {
                    SetUiInteractionEnabled(true);
                }
                else
                {
                    SetUiInteractionEnabled(false);
                }
                RefreshPromptUiState();
                return;
            }

            if (kb.digit1Key.wasPressedThisFrame)
                SubmitCurrent("looks_correct", m_Comment);
            else if (kb.digit2Key.wasPressedThisFrame)
                SubmitCurrent("not_visible", m_Comment);
            else if (kb.digit3Key.wasPressedThisFrame)
                SubmitCurrent("wrong_target", m_Comment);
            else if (kb.digit4Key.wasPressedThisFrame)
                SubmitCurrent("wrong_placement", m_Comment);
            else if (kb.digit5Key.wasPressedThisFrame)
                SubmitCurrent("wrong_mesh_fit", m_Comment);
            else if (kb.digit6Key.wasPressedThisFrame)
                SubmitCurrent("skipped", m_Comment);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                SubmitCurrent("note_only", m_Comment);
            }
#endif
        }

        private static string BuildDisplayName(string name, ulong networkObjectId)
        {
            string trimmed = string.IsNullOrWhiteSpace(name) ? "(none)" : name.Trim();
            if (networkObjectId == 0)
            {
                return trimmed;
            }

            return $"{trimmed} [N{networkObjectId}]";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
