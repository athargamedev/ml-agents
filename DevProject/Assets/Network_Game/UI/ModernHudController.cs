using System.Collections.Generic;
using Network_Game.Combat;
using Network_Game.Dialogue;
using Network_Game.ThirdPersonController;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Network_Game.UI
{
    /// <summary>
    /// Composition root for the runtime HUD.
    /// Keeps visual HUD ownership on Modern_HUD_Root and points non-visual
    /// dialogue feedback services to a separate sibling object.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-520)]
    public sealed class ModernHudController : MonoBehaviour
    {
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

        private static ModernHudController s_ActiveInstance;

        private const string kLoginName = "Login_Screen";
        private const string kProfileName = "Profile_Card";
        private const string kDialogueName = "Dialogue_Overlay";
        private const string kRuntimeServicesName = "Dialogue_RuntimeServices";

        [Header("Documents")]
        [SerializeField]
        private UIDocument m_LoginDocument;

        [SerializeField]
        private UIDocument m_ProfileDocument;

        [SerializeField]
        private UIDocument m_DialogueDocument;

        [Header("Visual Components")]
        [SerializeField]
        private ModernUISetup m_ModernUiSetup;

        [SerializeField]
        private ModernHudLayoutProfile m_LayoutProfile;

        [SerializeField]
        private DialogueEffectFeedbackPrompt m_FeedbackPrompt;

        [SerializeField]
        private CombatRuntimeOverlay m_CombatOverlay;

        [SerializeField]
        private DialogueDebugPanel m_DebugPanel;

        [Header("Runtime Services")]
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

        private readonly HashSet<int> m_UiCursorOwners = new HashSet<int>();
        private bool m_IsUiCursorMode;
        private bool m_FeedbackVisible;
        private VisualElement m_ZoneHostRoot;
        private VisualElement m_HudZonesRoot;
        private VisualElement m_TopBarZone;
        private VisualElement m_TopLeftZone;
        private VisualElement m_RightDockZone;
        private VisualElement m_BottomBarZone;
        private VisualElement m_ModalOverlayZone;

        public static ModernHudController Active
        {
            get
            {
                if (s_ActiveInstance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    s_ActiveInstance = FindAnyObjectByType<ModernHudController>();
#else
                    s_ActiveInstance = FindAnyObjectByType<ModernHudController>();
#endif
                }

                return s_ActiveInstance;
            }
        }

        public UIDocument LoginDocument => m_LoginDocument;
        public UIDocument ProfileDocument => m_ProfileDocument;
        public UIDocument DialogueDocument => m_DialogueDocument;
        public ModernUISetup ModernUiSetup => m_ModernUiSetup;
        public ModernHudLayoutProfile LayoutProfile => m_LayoutProfile;
        public Transform RuntimeServicesRoot => m_RuntimeServicesRoot;

        private void Reset()
        {
            RefreshBindings();
        }

        private void Awake()
        {
            RefreshBindings();
            EnsureHudZones();
            ApplyHudVisibility();
            ValidateRuntimeTopology();
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
            EnsureHudZones();
            ApplyHudVisibility();
            ValidateRuntimeTopology();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                m_UiCursorOwners.Clear();
                ApplyUiCursorMode(false);
            }

            DestroyHudZones();

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

        [ContextMenu("Refresh HUD Bindings")]
        public void RefreshBindings()
        {
            m_LoginDocument = ResolveChildDocument(m_LoginDocument, kLoginName);
            m_ProfileDocument = ResolveChildDocument(m_ProfileDocument, kProfileName);
            m_DialogueDocument = ResolveChildDocument(m_DialogueDocument, kDialogueName);

            m_ModernUiSetup = ResolveLocalComponent(m_ModernUiSetup);
            m_FeedbackPrompt = ResolveLocalComponent(m_FeedbackPrompt);
            m_CombatOverlay = ResolveLocalComponent(m_CombatOverlay);
            m_DebugPanel = ResolveLocalComponent(m_DebugPanel);

            m_RuntimeServicesRoot = ResolveRuntimeServicesRoot();
            m_FeedbackCollector = ResolveServicesComponent(m_FeedbackCollector);
            m_FeedbackRuntimeTuner = ResolveServicesComponent(m_FeedbackRuntimeTuner);
        }

        public static bool TryAcquireUiCursor(Object owner)
        {
            return Active != null && Active.SetUiCursorOwner(owner, true);
        }

        public static bool TryReleaseUiCursor(Object owner)
        {
            return Active != null && Active.SetUiCursorOwner(owner, false);
        }

        public static bool SetPanelVisible(HudPanel panel, bool visible)
        {
            return Active != null && Active.SetPanelVisibleInternal(panel, visible);
        }

        public static bool SetFeedbackVisible(bool visible)
        {
            return Active != null && Active.SetFeedbackVisibleInternal(visible);
        }

        public static VisualElement TryGetZone(HudZone zone)
        {
            if (Active == null)
            {
                return null;
            }

            Active.EnsureHudZones();
            return Active.ResolveZone(zone);
        }

        public static bool TryApplyBottomBarLayout(VisualElement element)
        {
            return Active != null && Active.ApplyBottomBarLayoutToElement(element);
        }

        public void SetLayoutProfile(ModernHudLayoutProfile profile)
        {
            if (m_LayoutProfile == profile)
            {
                return;
            }

            m_LayoutProfile = profile;
            EnsureHudZones();
            ApplyHudVisibility();
        }

        private void ValidateRuntimeTopology()
        {
            if (GetComponent<DialogueFeedbackCollector>() != null)
            {
                Debug.LogWarning(
                    "Modern_HUD_Root should not host DialogueFeedbackCollector. Move it to Dialogue_RuntimeServices.",
                    this
                );
            }

            if (GetComponent<DialogueEffectFeedbackRuntimeTuner>() != null)
            {
                Debug.LogWarning(
                    "Modern_HUD_Root should not host DialogueEffectFeedbackRuntimeTuner. Move it to Dialogue_RuntimeServices.",
                    this
                );
            }

            if (m_RuntimeServicesRoot == null)
            {
                Debug.LogWarning(
                    "Modern_HUD_Root could not resolve Dialogue_RuntimeServices. Feedback services are not centralized.",
                    this
                );
            }
        }

        private UIDocument ResolveChildDocument(UIDocument current, string childName)
        {
            if (current != null)
            {
                return current;
            }

            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<UIDocument>() : null;
        }

        private T ResolveLocalComponent<T>(T current)
            where T : Component
        {
            return current != null ? current : GetComponent<T>();
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

        private bool SetUiCursorOwner(Object owner, bool wantsUiCursor)
        {
            if (!Application.isPlaying || owner == null)
            {
                return false;
            }

            bool changed = wantsUiCursor
                ? m_UiCursorOwners.Add(owner.GetInstanceID())
                : m_UiCursorOwners.Remove(owner.GetInstanceID());

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

        private bool SetFeedbackVisibleInternal(bool visible)
        {
            bool changed = m_FeedbackVisible != visible;
            m_FeedbackVisible = visible;
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
            StarterAssetsInputs[] inputs = Object.FindObjectsByType<StarterAssetsInputs>(
                FindObjectsInactive.Include
            );
#else
            StarterAssetsInputs[] inputs = Object.FindObjectsOfType<StarterAssetsInputs>();
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
            ApplyDocumentVisibility(m_LoginDocument, m_LoginVisible);
            ApplyDocumentVisibility(m_ProfileDocument, m_ProfileVisible);
            ApplyDocumentVisibility(m_DialogueDocument, m_DialogueVisible);

            HudLayoutMetrics layout = ResolveLayoutMetrics(m_ZoneHostRoot);
            float dockTop = m_FeedbackVisible ? layout.DockTopWithFeedback : layout.DockTopWithoutFeedback;
            if (m_TopLeftZone != null)
            {
                m_TopLeftZone.style.top = dockTop;
            }

            if (m_RightDockZone != null)
            {
                m_RightDockZone.style.top = dockTop;
            }
        }

        private static void ApplyDocumentVisibility(UIDocument document, bool visible)
        {
            if (document == null || document.rootVisualElement == null)
            {
                return;
            }

            document.rootVisualElement.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void EnsureHudZones()
        {
            VisualElement hostRoot = ResolveZoneHostRoot();
            if (hostRoot == null)
            {
                DestroyHudZones();
                return;
            }

            if (
                m_HudZonesRoot != null
                && m_ZoneHostRoot == hostRoot
                && m_HudZonesRoot.parent == hostRoot
                && m_HudZonesRoot.panel != null
            )
            {
                ApplyZoneLayout(hostRoot);
                return;
            }

            DestroyHudZones();

            m_ZoneHostRoot = hostRoot;
            m_HudZonesRoot = new VisualElement { name = "modern-hud-zones-root" };
            m_HudZonesRoot.style.position = Position.Absolute;
            m_HudZonesRoot.style.left = 0f;
            m_HudZonesRoot.style.right = 0f;
            m_HudZonesRoot.style.top = 0f;
            m_HudZonesRoot.style.bottom = 0f;
            m_HudZonesRoot.pickingMode = PickingMode.Ignore;

            m_TopBarZone = CreateZone("modern-hud-zone-top-bar");
            m_TopBarZone.style.position = Position.Absolute;
            m_TopBarZone.style.left = 12f;
            m_TopBarZone.style.right = 12f;
            m_TopBarZone.style.top = 10f;
            m_TopBarZone.style.flexDirection = FlexDirection.Column;
            m_TopBarZone.style.alignItems = Align.Stretch;

            m_TopLeftZone = CreateZone("modern-hud-zone-top-left");
            m_TopLeftZone.style.position = Position.Absolute;
            m_TopLeftZone.style.left = 12f;
            m_TopLeftZone.style.top = 12f;
            m_TopLeftZone.style.flexDirection = FlexDirection.Column;
            m_TopLeftZone.style.alignItems = Align.FlexStart;

            m_RightDockZone = CreateZone("modern-hud-zone-right-dock");
            m_RightDockZone.style.position = Position.Absolute;
            m_RightDockZone.style.top = 12f;
            m_RightDockZone.style.right = 12f;
            m_RightDockZone.style.flexDirection = FlexDirection.Column;
            m_RightDockZone.style.alignItems = Align.FlexEnd;

            m_BottomBarZone = CreateZone("modern-hud-zone-bottom-bar");
            m_BottomBarZone.style.position = Position.Absolute;
            m_BottomBarZone.style.left = 0f;
            m_BottomBarZone.style.right = 0f;
            m_BottomBarZone.style.bottom = 0f;
            m_BottomBarZone.style.flexDirection = FlexDirection.Column;
            m_BottomBarZone.style.alignItems = Align.Stretch;

            m_ModalOverlayZone = CreateZone("modern-hud-zone-modal-overlay");
            m_ModalOverlayZone.style.position = Position.Absolute;
            m_ModalOverlayZone.style.left = 0f;
            m_ModalOverlayZone.style.right = 0f;
            m_ModalOverlayZone.style.top = 0f;
            m_ModalOverlayZone.style.bottom = 0f;

            m_HudZonesRoot.Add(m_TopBarZone);
            m_HudZonesRoot.Add(m_TopLeftZone);
            m_HudZonesRoot.Add(m_RightDockZone);
            m_HudZonesRoot.Add(m_BottomBarZone);
            m_HudZonesRoot.Add(m_ModalOverlayZone);

            hostRoot.Add(m_HudZonesRoot);
            ApplyZoneLayout(hostRoot);
            ApplyHudVisibility();
        }

        private void DestroyHudZones()
        {
            if (m_HudZonesRoot != null && m_HudZonesRoot.parent != null)
            {
                m_HudZonesRoot.parent.Remove(m_HudZonesRoot);
            }

            m_ZoneHostRoot = null;
            m_HudZonesRoot = null;
            m_TopBarZone = null;
            m_TopLeftZone = null;
            m_RightDockZone = null;
            m_BottomBarZone = null;
            m_ModalOverlayZone = null;
        }

        private VisualElement ResolveZone(HudZone zone)
        {
            return zone switch
            {
                HudZone.TopBar => m_TopBarZone,
                HudZone.TopLeft => m_TopLeftZone,
                HudZone.RightDock => m_RightDockZone,
                HudZone.BottomBar => m_BottomBarZone,
                HudZone.ModalOverlay => m_ModalOverlayZone,
                _ => null,
            };
        }

        private bool ApplyBottomBarLayoutToElement(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            VisualElement hostRoot = ResolveZoneHostRoot();
            if (hostRoot == null)
            {
                return false;
            }

            HudLayoutMetrics layout = ResolveLayoutMetrics(hostRoot);
            element.style.position = Position.Absolute;
            element.style.left = layout.Margin;
            element.style.right = layout.Margin;
            element.style.bottom = layout.Margin;
            element.style.height = layout.BottomBarHeight;
            element.style.width = StyleKeyword.Auto;
            return true;
        }

        private void ApplyZoneLayout(VisualElement hostRoot)
        {
            HudLayoutMetrics layout = ResolveLayoutMetrics(hostRoot);

            if (m_TopBarZone != null)
            {
                m_TopBarZone.style.left = layout.Margin;
                m_TopBarZone.style.right = layout.Margin;
                m_TopBarZone.style.top = layout.TopInset;
                m_TopBarZone.style.height = layout.TopBarHeight;
                m_TopBarZone.style.minHeight = layout.TopBarHeight;
            }

            if (m_TopLeftZone != null)
            {
                m_TopLeftZone.style.left = layout.Margin;
                m_TopLeftZone.style.width = layout.LeftDockWidth;
            }

            if (m_RightDockZone != null)
            {
                m_RightDockZone.style.right = layout.Margin;
                m_RightDockZone.style.width = layout.RightDockWidth;
            }

            if (m_BottomBarZone != null)
            {
                m_BottomBarZone.style.left = layout.Margin;
                m_BottomBarZone.style.right = layout.Margin;
                m_BottomBarZone.style.bottom = layout.Margin;
                m_BottomBarZone.style.height = layout.BottomBarHeight;
                m_BottomBarZone.style.minHeight = layout.BottomBarHeight;
            }
        }

        private HudLayoutMetrics ResolveLayoutMetrics(VisualElement hostRoot)
        {
            float width = hostRoot != null && hostRoot.resolvedStyle.width > 1f
                ? hostRoot.resolvedStyle.width
                : Mathf.Max(1280f, Screen.width);
            float height = hostRoot != null && hostRoot.resolvedStyle.height > 1f
                ? hostRoot.resolvedStyle.height
                : Mathf.Max(720f, Screen.height);

            ModernHudLayoutProfile profile = m_LayoutProfile;
            float margin = Mathf.Max(
                8f,
                width * (profile != null ? profile.OuterMarginPercent : 0.0125f)
            );
            float topInset = Mathf.Max(
                6f,
                height * (profile != null ? profile.TopBarTopPercent : 0.008f)
            );
            float gap = Mathf.Max(
                6f,
                width * (profile != null ? profile.DockGapPercent : 0.008f)
            );
            float topBarHeight = Mathf.Max(
                profile != null ? profile.TopBarMinHeightPx : 118f,
                height * (profile != null ? profile.TopBarReservedHeightPercent : 0.18f)
            );
            float bottomBarHeight = Mathf.Max(
                140f,
                height * (profile != null ? profile.BottomBarHeightPercent : 0.24f)
            );

            float centerReserve = Mathf.Max(280f, width * 0.20f);
            float availableDockWidth = Mathf.Max(
                320f,
                width - centerReserve - (margin * 2f) - gap
            );
            float leftDockWidth = width * (profile != null ? profile.LeftDockWidthPercent : 0.28f);
            float rightDockWidth = width * (profile != null ? profile.RightDockWidthPercent : 0.36f);
            FitDockWidths(
                ref leftDockWidth,
                ref rightDockWidth,
                availableDockWidth,
                profile != null ? profile.MinDockWidthPx : 260f
            );

            return new HudLayoutMetrics
            {
                Margin = margin,
                TopInset = topInset,
                TopBarHeight = topBarHeight,
                DockTopWithoutFeedback = margin,
                DockTopWithFeedback = topInset + topBarHeight + gap,
                LeftDockWidth = leftDockWidth,
                RightDockWidth = rightDockWidth,
                BottomBarHeight = bottomBarHeight,
            };
        }

        private static void FitDockWidths(
            ref float leftDockWidth,
            ref float rightDockWidth,
            float availableDockWidth,
            float minimumDockWidth
        )
        {
            leftDockWidth = Mathf.Max(minimumDockWidth, leftDockWidth);
            rightDockWidth = Mathf.Max(minimumDockWidth, rightDockWidth);

            float total = leftDockWidth + rightDockWidth;
            if (total <= availableDockWidth || total <= 0f)
            {
                return;
            }

            float minimumTotal = Mathf.Max(160f, minimumDockWidth * 0.55f) * 2f;
            if (availableDockWidth <= minimumTotal)
            {
                float forcedWidth = Mathf.Max(120f, availableDockWidth * 0.5f);
                leftDockWidth = forcedWidth;
                rightDockWidth = forcedWidth;
                return;
            }

            float scale = availableDockWidth / total;
            leftDockWidth *= scale;
            rightDockWidth *= scale;
        }

        private VisualElement ResolveZoneHostRoot()
        {
            if (IsUsableUiToolkitHostRoot(m_DialogueDocument?.rootVisualElement))
            {
                return m_DialogueDocument.rootVisualElement;
            }

            if (IsUsableUiToolkitHostRoot(m_ProfileDocument?.rootVisualElement))
            {
                return m_ProfileDocument.rootVisualElement;
            }

            if (IsUsableUiToolkitHostRoot(m_LoginDocument?.rootVisualElement))
            {
                return m_LoginDocument.rootVisualElement;
            }

            return null;
        }

        private static VisualElement CreateZone(string name)
        {
            var zone = new VisualElement { name = name };
            zone.pickingMode = PickingMode.Ignore;
            return zone;
        }

        private static bool IsUsableUiToolkitHostRoot(VisualElement root)
        {
            if (root == null || root.panel == null)
            {
                return false;
            }

            if (root.style.display.value == DisplayStyle.None)
            {
                return false;
            }

            return root.resolvedStyle.display != DisplayStyle.None;
        }

        private struct HudLayoutMetrics
        {
            public float Margin;
            public float TopInset;
            public float TopBarHeight;
            public float DockTopWithoutFeedback;
            public float DockTopWithFeedback;
            public float LeftDockWidth;
            public float RightDockWidth;
            public float BottomBarHeight;
        }

        private Transform ResolveRuntimeServicesRoot()
        {
            if (m_RuntimeServicesRoot != null)
            {
                return m_RuntimeServicesRoot;
            }

            Transform sibling = transform.parent != null
                ? transform.parent.Find(kRuntimeServicesName)
                : null;
            if (sibling != null)
            {
                return sibling;
            }

            Transform existingChild = transform.Find(kRuntimeServicesName);
            if (existingChild != null)
            {
                return existingChild;
            }

            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root != null && root.name == kRuntimeServicesName)
                {
                    return root.transform;
                }
            }

            return null;
        }
    }
}
