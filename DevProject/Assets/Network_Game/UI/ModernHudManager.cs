using System;
using System.Collections.Generic;
using Network_Game.Combat;
using Network_Game.Dialogue;
using Network_Game.ThirdPersonController;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Network_Game.UI
{
    /// <summary>
    /// Unified HUD Manager - single controller for all runtime UI elements.
    /// Binds to ModernHUD.uxml for full UI Builder editing support.
    /// 
    /// Key Features:
    /// - Single UXML controls all panels (Login, Dialogue, Profile, Combat)
    /// - Zones (TopBar, TopLeft, RightDock, BottomBar, Modal) are pre-defined in UXML
    /// - All visual properties editable in UI Builder
    /// - No conflicting scripts - one source of truth
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-520)]
    public sealed class ModernHudManager : MonoBehaviour
    {
        #region Enums

        public enum HudPanel
        {
            Login,
            Profile,
            Dialogue,
        }

        public enum HudZone
        {
            TopBar,
            TopLeft,
            RightDock,
            BottomBar,
            ModalOverlay,
        }

        #endregion

        #region Serialized Fields

        [Header("HUD Document")]
        [SerializeField]
        private UIDocument m_HudDocument;

        [Header("Layout Profile")]
        [SerializeField]
        private ModernHudLayoutProfile m_LayoutProfile;

        [Header("Runtime Services References")]
        [SerializeField]
        private Transform m_RuntimeServicesRoot;

        [SerializeField]
        private DialogueFeedbackCollector m_FeedbackCollector;

        [SerializeField]
        private DialogueEffectFeedbackRuntimeTuner m_FeedbackRuntimeTuner;

        [Header("Visibility State")]
        [SerializeField]
        private bool m_LoginVisible = true;

        [SerializeField]
        private bool m_ProfileVisible;

        [SerializeField]
        private bool m_DialogueVisible = true;

        [Header("Panel Toggle Keys")]
        [Tooltip("Keyboard shortcut to show/hide the Login panel. None = disabled.")]
        [SerializeField]
        private KeyCode m_LoginToggleKey = KeyCode.F1;

        [Tooltip("Keyboard shortcut to show/hide the Profile panel. None = disabled.")]
        [SerializeField]
        private KeyCode m_ProfileToggleKey = KeyCode.F2;

        [Tooltip("Keyboard shortcut to show/hide the Dialogue panel. None = disabled.")]
        [SerializeField]
        private KeyCode m_DialogueToggleKey = KeyCode.F3;

        #endregion

        #region Private Fields

        private static ModernHudManager s_ActiveInstance;

        private readonly HashSet<int> m_UiCursorOwners = new HashSet<int>();
        private bool m_IsUiCursorMode;

        // Cached VisualElement references - set from UXML
        private VisualElement m_HudRoot;
        private VisualElement m_ZoneTopBar;
        private VisualElement m_ZoneTopLeft;
        private VisualElement m_ZoneRightDock;
        private VisualElement m_ZoneBottomBar;
        private VisualElement m_ZoneModalOverlay;
        private VisualElement m_PanelLogin;
        private VisualElement m_PanelDialogue;
        private VisualElement m_PanelProfile;

        #endregion

        #region Public Properties

        public static ModernHudManager Active
        {
            get
            {
                if (s_ActiveInstance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    s_ActiveInstance = FindAnyObjectByType<ModernHudManager>();
#else
                    s_ActiveInstance = FindAnyObjectByType<ModernHudManager>();
#endif
                }
                return s_ActiveInstance;
            }
        }

        public UIDocument HudDocument => m_HudDocument;
        public ModernHudLayoutProfile LayoutProfile => m_LayoutProfile;
        public Transform RuntimeServicesRoot => m_RuntimeServicesRoot;

        public VisualElement HudRoot => m_HudRoot;
        public VisualElement ZoneTopBar => m_ZoneTopBar;
        public VisualElement ZoneTopLeft => m_ZoneTopLeft;
        public VisualElement ZoneRightDock => m_ZoneRightDock;
        public VisualElement ZoneBottomBar => m_ZoneBottomBar;
        public VisualElement ZoneModalOverlay => m_ZoneModalOverlay;

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            // Don't fire panel shortcuts while any text field has keyboard focus.
            if (IsAnyInputFieldFocused())
                return;

            if (IsPanelKeyPressed(m_LoginToggleKey))
                SetPanelVisibleInternal(HudPanel.Login, !m_LoginVisible);

            if (IsPanelKeyPressed(m_ProfileToggleKey))
                SetPanelVisibleInternal(HudPanel.Profile, !m_ProfileVisible);

            if (IsPanelKeyPressed(m_DialogueToggleKey))
                SetPanelVisibleInternal(HudPanel.Dialogue, !m_DialogueVisible);
        }

        private void Reset()
        {
            RefreshBindings();
        }

        private void Awake()
        {
            RefreshBindings();
            ApplyHudVisibility();
        }

        private void OnEnable()
        {
            s_ActiveInstance = this;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RefreshBindings();
                return;
            }
#endif
            RefreshBindings();
            ApplyHudVisibility();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                m_UiCursorOwners.Clear();
                ApplyUiCursorMode(false);
            }

            if (s_ActiveInstance == this)
            {
                s_ActiveInstance = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            EditorApplication.delayCall -= RefreshBindings;
            EditorApplication.delayCall += RefreshBindings;
        }
#endif

        #endregion

        #region Public API

        /// <summary>
        /// Refreshes all UI element references from the UXML document.
        /// Call this after modifying the UXML in UI Builder.
        /// </summary>
        [ContextMenu("Refresh HUD Bindings")]
        public void RefreshBindings()
        {
            if (m_HudDocument == null)
            {
                m_HudDocument = GetComponent<UIDocument>();
            }

            if (m_HudDocument?.rootVisualElement == null)
            {
                return;
            }

            // Cache root element
            m_HudRoot = m_HudDocument.rootVisualElement.Q("hud-root");

            // Cache zone elements
            m_ZoneTopBar = m_HudDocument.rootVisualElement.Q("zone-top-bar");
            m_ZoneTopLeft = m_HudDocument.rootVisualElement.Q("zone-top-left");
            m_ZoneRightDock = m_HudDocument.rootVisualElement.Q("zone-right-dock");
            m_ZoneBottomBar = m_HudDocument.rootVisualElement.Q("zone-bottom-bar");
            m_ZoneModalOverlay = m_HudDocument.rootVisualElement.Q("zone-modal-overlay");

            // Cache panel elements
            m_PanelLogin = m_HudDocument.rootVisualElement.Q("panel-login");
            m_PanelDialogue = m_HudDocument.rootVisualElement.Q("panel-dialogue");
            m_PanelProfile = m_HudDocument.rootVisualElement.Q("panel-profile");

            // Resolve runtime services
            m_RuntimeServicesRoot = ResolveRuntimeServicesRoot();
            m_FeedbackCollector = ResolveServicesComponent(m_FeedbackCollector);
            m_FeedbackRuntimeTuner = ResolveServicesComponent(m_FeedbackRuntimeTuner);

            ApplyHudVisibility();
        }

        /// <summary>
        /// Attempts to acquire UI cursor mode for an owner.
        /// </summary>
        public static bool TryAcquireUiCursor(UnityEngine.Object owner)
        {
            return Active != null && Active.SetUiCursorOwner(owner, true);
        }

        /// <summary>
        /// Attempts to release UI cursor mode for an owner.
        /// </summary>
        public static bool TryReleaseUiCursor(UnityEngine.Object owner)
        {
            return Active != null && Active.SetUiCursorOwner(owner, false);
        }

        /// <summary>
        /// Sets the visibility of a HUD panel.
        /// </summary>
        public static bool SetPanelVisible(HudPanel panel, bool visible)
        {
            return Active != null && Active.SetPanelVisibleInternal(panel, visible);
        }

        /// <summary>
        /// Tries to get a zone VisualElement for external components to add content.
        /// </summary>
        public static VisualElement TryGetZone(HudZone zone)
        {
            if (Active == null)
            {
                return null;
            }

            return Active.ResolveZone(zone);
        }

        /// <summary>
        /// Attempts to apply bottom bar layout to an element.
        /// </summary>
        public static bool TryApplyBottomBarLayout(VisualElement element)
        {
            return Active != null && Active.ApplyBottomBarLayoutToElement(element);
        }

        private bool ApplyBottomBarLayoutToElement(VisualElement element)
        {
            if (element == null || m_LayoutProfile == null)
            {
                return false;
            }

            element.style.position = Position.Absolute;
            element.style.left = new Length(12, LengthUnit.Pixel);
            element.style.right = new Length(12, LengthUnit.Pixel);
            element.style.bottom = new Length(12, LengthUnit.Pixel);
            element.style.height = new Length(220, LengthUnit.Pixel);
            element.style.width = StyleKeyword.Auto;
            return true;
        }

        /// <summary>
        /// Sets the visibility of the feedback prompt.
        /// </summary>
        private bool m_FeedbackVisible;
        public static bool SetFeedbackVisible(bool visible)
        {
            return Active != null && Active.SetFeedbackVisibleInternal(visible);
        }

        private bool SetFeedbackVisibleInternal(bool visible)
        {
            bool changed = m_FeedbackVisible != visible;
            m_FeedbackVisible = visible;
            return changed;
        }

        /// <summary>
        /// Sets the layout profile for responsive sizing.
        /// </summary>
        public void SetLayoutProfile(ModernHudLayoutProfile profile)
        {
            if (m_LayoutProfile == profile)
            {
                return;
            }

            m_LayoutProfile = profile;
            ApplyHudVisibility();
        }

        #endregion

        #region Private Methods

        private static bool IsPanelKeyPressed(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None)
                return false;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return false;

            return keyCode switch
            {
                KeyCode.F1 => kb.f1Key.wasPressedThisFrame,
                KeyCode.F2 => kb.f2Key.wasPressedThisFrame,
                KeyCode.F3 => kb.f3Key.wasPressedThisFrame,
                KeyCode.F4 => kb.f4Key.wasPressedThisFrame,
                KeyCode.F5 => kb.f5Key.wasPressedThisFrame,
                KeyCode.F6 => kb.f6Key.wasPressedThisFrame,
                KeyCode.F7 => kb.f7Key.wasPressedThisFrame,
                KeyCode.F8 => kb.f8Key.wasPressedThisFrame,
                KeyCode.Escape => kb.escapeKey.wasPressedThisFrame,
                KeyCode.BackQuote => kb.backquoteKey.wasPressedThisFrame,
                KeyCode.Tab => kb.tabKey.wasPressedThisFrame,
                KeyCode.Alpha1 => kb.digit1Key.wasPressedThisFrame,
                KeyCode.Alpha2 => kb.digit2Key.wasPressedThisFrame,
                KeyCode.Alpha3 => kb.digit3Key.wasPressedThisFrame,
                KeyCode.Alpha4 => kb.digit4Key.wasPressedThisFrame,
                _ => false,
            };
#else
            return Input.GetKeyDown(keyCode);
#endif
        }

        private bool IsAnyInputFieldFocused()
        {
            if (m_HudDocument?.rootVisualElement == null)
                return false;

            var focused = m_HudDocument.rootVisualElement.panel.focusController?.focusedElement as VisualElement;
            if (focused == null)
                return false;

            // Walk up the visual tree
            VisualElement node = focused;
            while (node != null)
            {
                if (node is TextField)
                    return true;
                node = node.parent;
            }
            return false;
        }

        private bool SetUiCursorOwner(UnityEngine.Object owner, bool wantsUiCursor)
        {
            if (!Application.isPlaying || owner == null)
            {
                return false;
            }

            bool changed = wantsUiCursor
                ? m_UiCursorOwners.Add(owner.GetHashCode())
                : m_UiCursorOwners.Remove(owner.GetHashCode());

            if (changed)
            {
                ApplyUiCursorMode(m_UiCursorOwners.Count > 0);
            }

            return true;
        }

        private bool SetPanelVisibleInternal(HudPanel panel, bool visible)
        {
            bool changed = false;
            switch (panel)
            {
                case HudPanel.Login:
                    changed = m_LoginVisible != visible;
                    m_LoginVisible = visible;
                    break;
                case HudPanel.Profile:
                    changed = m_ProfileVisible != visible;
                    m_ProfileVisible = visible;
                    break;
                case HudPanel.Dialogue:
                    changed = m_DialogueVisible != visible;
                    m_DialogueVisible = visible;
                    break;
            }

            ApplyHudVisibility();
            return changed;
        }

        private void ApplyUiCursorMode(bool wantsUiCursor)
        {
            if (!Application.isPlaying || m_IsUiCursorMode == wantsUiCursor)
            {
                return;
            }

            m_IsUiCursorMode = wantsUiCursor;

            Cursor.lockState = wantsUiCursor ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = wantsUiCursor;

#if UNITY_2023_1_OR_NEWER
            StarterAssetsInputs[] inputs = UnityEngine.Object.FindObjectsByType<StarterAssetsInputs>(
                FindObjectsInactive.Include
            );
#else
            StarterAssetsInputs[] inputs = UnityEngine.Object.FindObjectsOfType<StarterAssetsInputs>();
#endif
            bool allowGameplayLook = !wantsUiCursor;
            for (int i = 0; i < inputs.Length; i++)
            {
                StarterAssetsInputs input = inputs[i];
                if (input == null)
                {
                    continue;
                }

                input.cursorLocked = allowGameplayLook;
                input.cursorInputForLook = allowGameplayLook;
                input.SetCursorState(allowGameplayLook);
            }
        }

        private void ApplyHudVisibility()
        {
            ApplyElementVisibility(m_PanelLogin, m_LoginVisible);
            ApplyElementVisibility(m_PanelProfile, m_ProfileVisible);
            ApplyElementVisibility(m_PanelDialogue, m_DialogueVisible);
        }

        private static void ApplyElementVisibility(VisualElement element, bool visible)
        {
            if (element == null)
            {
                return;
            }

            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement ResolveZone(HudZone zone)
        {
            return zone switch
            {
                HudZone.TopBar => m_ZoneTopBar,
                HudZone.TopLeft => m_ZoneTopLeft,
                HudZone.RightDock => m_ZoneRightDock,
                HudZone.BottomBar => m_ZoneBottomBar,
                HudZone.ModalOverlay => m_ZoneModalOverlay,
                _ => null,
            };
        }

        private T ResolveServicesComponent<T>(T current)
            where T : Component
        {
            if (current != null)
            {
                return current;
            }

            return m_RuntimeServicesRoot != null ? m_RuntimeServicesRoot.GetComponent<T>() : null;
        }

        private Transform ResolveRuntimeServicesRoot()
        {
            if (m_RuntimeServicesRoot != null)
            {
                return m_RuntimeServicesRoot;
            }

            const string kRuntimeServicesName = "Dialogue_RuntimeServices";

            // Check parent
            Transform sibling = transform.parent != null ? transform.parent.Find(kRuntimeServicesName) : null;
            if (sibling != null)
                return sibling;

            // Check children
            Transform existingChild = transform.Find(kRuntimeServicesName);
            if (existingChild != null)
                return existingChild;

            // Check scene roots
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root != null && root.name == kRuntimeServicesName)
                {
                    return root.transform;
                }
            }

            return null;
        }

        #endregion
    }
}
