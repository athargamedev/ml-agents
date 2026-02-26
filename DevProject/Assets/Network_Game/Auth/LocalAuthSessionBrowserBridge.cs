using Network_Game.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Network_Game.Auth
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LocalAuthSessionBrowserBridge : MonoBehaviour
    {
        [Header("Defaults")]
        [SerializeField]
        private bool m_AutoEnsureLoggedIn = true;

        [SerializeField]
        [Tooltip("If enabled, startup never triggers auto-login; user must click Login.")]
        private bool m_RequireExplicitLoginEachSession = true;

        [SerializeField]
        private string m_DefaultCustomizationJson = "";

        [SerializeField]
        private bool m_StartMinimized = true;

        [SerializeField]
        private bool m_AutoMinimizeAfterLogin = true;

        [SerializeField]
        private bool m_LogDebug = true;

        private UIDocument m_Document;
        private LocalPlayerAuthService m_Service;
        private VisualElement m_PanelsContainer;
        private Label m_CompactStatusLabel;
        private Button m_ToggleButton;

        private TextField m_NameIdField;
        private TextField m_CustomizationField;
        private Label m_StatusLabel;
        private Label m_SummaryLabel;
        private Label m_MirrorLabel;
        private Button m_LoginButton;
        private Button m_LogoutButton;
        private Button m_ApplyCustomizationButton;
        private bool m_IsMinimized;

        private void Awake()
        {
            m_Document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            EnsureService();
            m_IsMinimized = m_StartMinimized;
            BuildUi();
            BindEvents();

            if (m_AutoEnsureLoggedIn && !m_RequireExplicitLoginEachSession && m_Service != null)
            {
                m_Service.EnsureLoggedIn();
            }

            RefreshView();
            if (m_AutoMinimizeAfterLogin && m_Service != null && m_Service.HasCurrentPlayer)
            {
                m_IsMinimized = true;
            }
            ApplyMinimizedState();
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        private void EnsureService()
        {
            m_Service = LocalPlayerAuthService.EnsureInstance();
        }

        private void BindEvents()
        {
            LocalPlayerAuthService.OnPlayerLoggedIn += HandleLoggedIn;
            LocalPlayerAuthService.OnPlayerLoggedOut += HandleLoggedOut;

            if (m_LoginButton != null)
            {
                m_LoginButton.clicked += HandleLoginClicked;
            }

            if (m_LogoutButton != null)
            {
                m_LogoutButton.clicked += HandleLogoutClicked;
            }

            if (m_ApplyCustomizationButton != null)
            {
                m_ApplyCustomizationButton.clicked += HandleApplyCustomizationClicked;
            }

            if (m_NameIdField != null)
            {
                m_NameIdField.RegisterCallback<KeyDownEvent>(OnNameFieldKeyDown);
            }

            if (m_ToggleButton != null)
            {
                m_ToggleButton.clicked += ToggleMinimized;
            }
        }

        private void UnbindEvents()
        {
            LocalPlayerAuthService.OnPlayerLoggedIn -= HandleLoggedIn;
            LocalPlayerAuthService.OnPlayerLoggedOut -= HandleLoggedOut;

            if (m_LoginButton != null)
            {
                m_LoginButton.clicked -= HandleLoginClicked;
            }

            if (m_LogoutButton != null)
            {
                m_LogoutButton.clicked -= HandleLogoutClicked;
            }

            if (m_ApplyCustomizationButton != null)
            {
                m_ApplyCustomizationButton.clicked -= HandleApplyCustomizationClicked;
            }

            if (m_NameIdField != null)
            {
                m_NameIdField.UnregisterCallback<KeyDownEvent>(OnNameFieldKeyDown);
            }

            if (m_ToggleButton != null)
            {
                m_ToggleButton.clicked -= ToggleMinimized;
            }
        }

        private void BuildUi()
        {
            if (m_Document == null)
            {
                return;
            }

            VisualElement root = m_Document.rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.FlexStart;
            root.style.justifyContent = Justify.FlexStart;
            root.style.paddingLeft = 12f;
            root.style.paddingTop = 10f;
            root.style.paddingRight = 12f;
            root.style.paddingBottom = 12f;
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.top = 0f;
            root.style.flexDirection = FlexDirection.Column;

            VisualElement topRow = new VisualElement();
            topRow.AddToClassList("blocks-container--horizontal");
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 8f;

            m_ToggleButton = new Button();
            m_ToggleButton.AddToClassList("blocks-button");
            m_ToggleButton.AddToClassList("blocks-button--round");
            m_ToggleButton.style.minWidth = 152f;
            m_ToggleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_ToggleButton.style.fontSize = 15f;
            m_ToggleButton.style.color = new StyleColor(Color.white);
            topRow.Add(m_ToggleButton);

            m_CompactStatusLabel = new Label("Auth panel collapsed");
            m_CompactStatusLabel.AddToClassList("blocks-label");
            m_CompactStatusLabel.style.marginLeft = 10f;
            m_CompactStatusLabel.style.fontSize = 15f;
            topRow.Add(m_CompactStatusLabel);

            m_PanelsContainer = new VisualElement();
            m_PanelsContainer.style.flexDirection = FlexDirection.Row;
            m_PanelsContainer.style.alignItems = Align.FlexStart;

            VisualElement authPanel = BuildAuthPanel();
            VisualElement profilePanel = BuildProfilePanel();
            profilePanel.style.marginLeft = 16f;

            m_PanelsContainer.Add(authPanel);
            m_PanelsContainer.Add(profilePanel);

            root.Add(topRow);
            root.Add(m_PanelsContainer);
        }

        private VisualElement BuildAuthPanel()
        {
            VisualElement modal = new VisualElement();
            modal.AddToClassList("blocks-modal");
            modal.style.minWidth = 400f;
            modal.style.maxWidth = 440f;

            var header = new Label("PLAYER AUTH");
            header.AddToClassList("blocks-header");
            header.AddToClassList("blocks-header--space-bottom");
            modal.Add(header);

            m_NameIdField = new TextField();
            m_NameIdField.name = "name-id-field";
            m_NameIdField.label = string.Empty;
            m_NameIdField.value = m_Service != null ? m_Service.LastLoginNameId : "player_local";
            m_NameIdField.AddToClassList("blocks-textfield");
            m_NameIdField.AddToClassList("blocks-element--space-bottom");
            m_NameIdField.style.minWidth = 240f;
            m_NameIdField.style.maxWidth = 340f;
            m_NameIdField.style.whiteSpace = WhiteSpace.NoWrap;
            m_NameIdField.style.fontSize = 22f;
            modal.Add(m_NameIdField);
            ConfigureReadableTextField(m_NameIdField, 22f, 52f);

            VisualElement buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("blocks-container--horizontal");
            buttonsRow.AddToClassList("blocks-element--space-bottom");

            m_LoginButton = new Button { text = "LOGIN" };
            m_LoginButton.AddToClassList("blocks-button");
            m_LoginButton.AddToClassList("blocks-button--round");
            m_LoginButton.AddToClassList("blocks-element--space-right");
            m_LoginButton.style.minWidth = 128f;
            m_LoginButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_LoginButton.style.fontSize = 16f;
            m_LoginButton.style.color = new StyleColor(Color.white);

            m_LogoutButton = new Button { text = "LOGOUT" };
            m_LogoutButton.AddToClassList("blocks-button");
            m_LogoutButton.AddToClassList("blocks-button--round");
            m_LogoutButton.style.minWidth = 128f;
            m_LogoutButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_LogoutButton.style.fontSize = 16f;
            m_LogoutButton.style.color = new StyleColor(Color.white);

            buttonsRow.Add(m_LoginButton);
            buttonsRow.Add(m_LogoutButton);
            modal.Add(buttonsRow);

            m_StatusLabel = new Label("Not logged in");
            m_StatusLabel.AddToClassList("blocks-label");
            m_StatusLabel.AddToClassList("blocks-element--space-bottom-sm");
            m_StatusLabel.style.fontSize = 18f;
            modal.Add(m_StatusLabel);

            m_MirrorLabel = new Label("Mirror LoRA: none");
            m_MirrorLabel.AddToClassList("blocks-label");
            m_MirrorLabel.style.fontSize = 17f;
            modal.Add(m_MirrorLabel);

            return modal;
        }

        private VisualElement BuildProfilePanel()
        {
            VisualElement modal = new VisualElement();
            modal.AddToClassList("blocks-modal");
            modal.style.minWidth = 360f;
            modal.style.maxWidth = 520f;

            var header = new Label("PLAYER PROFILE CONTEXT");
            header.AddToClassList("blocks-header");
            header.AddToClassList("blocks-header--space-bottom");
            modal.Add(header);

            m_CustomizationField = new TextField();
            m_CustomizationField.name = "customization-json-field";
            m_CustomizationField.label = string.Empty;
            m_CustomizationField.multiline = true;
            m_CustomizationField.value = m_DefaultCustomizationJson;
            m_CustomizationField.AddToClassList("blocks-textfield");
            m_CustomizationField.AddToClassList("blocks-element--space-bottom");
            m_CustomizationField.style.minWidth = 300f;
            m_CustomizationField.style.minHeight = 118f;
            m_CustomizationField.style.maxWidth = 460f;
            m_CustomizationField.style.fontSize = 18f;
            modal.Add(m_CustomizationField);
            ConfigureReadableTextField(m_CustomizationField, 18f, 140f);

            m_ApplyCustomizationButton = new Button { text = "SAVE PROFILE" };
            m_ApplyCustomizationButton.AddToClassList("blocks-button");
            m_ApplyCustomizationButton.AddToClassList("blocks-button--round");
            m_ApplyCustomizationButton.AddToClassList("blocks-element--space-bottom");
            m_ApplyCustomizationButton.style.minWidth = 160f;
            m_ApplyCustomizationButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_ApplyCustomizationButton.style.fontSize = 16f;
            m_ApplyCustomizationButton.style.color = new StyleColor(Color.white);
            modal.Add(m_ApplyCustomizationButton);

            m_SummaryLabel = new Label("No profile data loaded");
            m_SummaryLabel.AddToClassList("blocks-label");
            m_SummaryLabel.style.fontSize = 17f;
            modal.Add(m_SummaryLabel);

            return modal;
        }

        private void OnNameFieldKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                HandleLoginClicked();
                evt.StopPropagation();
            }
        }

        private void HandleLoginClicked()
        {
            EnsureService();
            if (m_Service == null)
            {
                return;
            }

            string requestedName = m_NameIdField != null ? m_NameIdField.value : string.Empty;
            bool ok = m_Service.Login(requestedName);
            if (!ok)
            {
                SetStatus("Login failed. Use letters/numbers/_/- only.");
                return;
            }

            if (m_LogDebug)
            {
                NGLog.Info("AuthUI", "Session browser login success.");
            }

            RefreshView();
            if (m_AutoMinimizeAfterLogin)
            {
                m_IsMinimized = true;
                ApplyMinimizedState();
            }
        }

        private void HandleLogoutClicked()
        {
            EnsureService();
            if (m_Service == null)
            {
                return;
            }

            m_Service.Logout();
            RefreshView();
        }

        private void HandleApplyCustomizationClicked()
        {
            EnsureService();
            if (m_Service == null || !m_Service.HasCurrentPlayer)
            {
                SetStatus("Login first to save profile context.");
                return;
            }

            string payload =
                m_CustomizationField != null ? m_CustomizationField.value : string.Empty;
            if (string.IsNullOrWhiteSpace(payload))
            {
                payload = "{}";
            }

            bool saved = m_Service.SetCustomizationJson(payload);
            if (!saved)
            {
                SetStatus("Profile save failed.");
                return;
            }

            SetStatus("Profile context saved.");
            RefreshView();
        }

        private void HandleLoggedIn(LocalPlayerAuthService.LocalPlayerRecord _)
        {
            RefreshView();
            if (m_AutoMinimizeAfterLogin)
            {
                m_IsMinimized = true;
                ApplyMinimizedState();
            }
        }

        private void HandleLoggedOut()
        {
            RefreshView();
        }

        private void RefreshView()
        {
            EnsureService();
            if (m_Service == null)
            {
                SetStatus("Auth service unavailable.");
                return;
            }

            bool hasPlayer = m_Service.HasCurrentPlayer;
            m_LogoutButton?.SetEnabled(hasPlayer);
            m_ApplyCustomizationButton?.SetEnabled(hasPlayer);

            if (!hasPlayer)
            {
                if (m_NameIdField != null)
                {
                    m_NameIdField.value = m_Service.LastLoginNameId;
                }

                if (
                    m_CustomizationField != null
                    && (
                        string.IsNullOrWhiteSpace(m_CustomizationField.value)
                        || string.Equals(
                            m_CustomizationField.value.Trim(),
                            "{}",
                            System.StringComparison.Ordinal
                        )
                    )
                )
                {
                    m_CustomizationField.value = ResolveDefaultCustomizationTemplate(
                        m_Service.LastLoginNameId
                    );
                }

                SetStatus("Not logged in.");
                SetMirrorLabel("none", 0f);
                SetSummary("No player profile loaded.");
                UpdateCompactStatus("Not logged in");
                return;
            }

            LocalPlayerAuthService.LocalPlayerRecord player = m_Service.CurrentPlayer;
            m_Service.EnsurePromptContextInitialized();
            if (m_NameIdField != null)
            {
                m_NameIdField.value = player.NameId;
            }

            string customizationJson = m_Service.GetCustomizationJson();
            if (m_CustomizationField != null)
            {
                m_CustomizationField.value = customizationJson;
            }

            SetStatus($"Logged in as: {player.NameId}");
            SetMirrorLabel(player.MirrorLoraPath, player.MirrorLoraWeight);
            SetSummary(BuildSummary(customizationJson));
            UpdateCompactStatus(player.NameId);
        }

        private void SetStatus(string text)
        {
            if (m_StatusLabel != null)
            {
                m_StatusLabel.text = text;
            }
        }

        private void SetSummary(string text)
        {
            if (m_SummaryLabel != null)
            {
                m_SummaryLabel.text = text;
            }
        }

        private void SetMirrorLabel(string path, float weight)
        {
            if (m_MirrorLabel == null)
            {
                return;
            }

            string resolvedPath = string.IsNullOrWhiteSpace(path) ? "none" : path;
            m_MirrorLabel.text = $"Mirror LoRA: {resolvedPath} (w={weight:0.##})";
        }

        private static string BuildSummary(string customizationJson)
        {
            string payload = string.IsNullOrWhiteSpace(customizationJson)
                ? "{}"
                : customizationJson.Trim();
            int maxPreview = 140;
            if (payload.Length > maxPreview)
            {
                payload = payload.Substring(0, maxPreview).TrimEnd() + "...";
            }

            return $"Prompt context JSON: {payload}";
        }

        private static void ConfigureReadableTextField(
            TextField field,
            float fontSize,
            float minHeight
        )
        {
            if (field == null)
            {
                return;
            }

            field.style.minHeight = minHeight;
            field.style.fontSize = fontSize;
            field.style.color = new StyleColor(Color.white);
            field.style.unityFontStyleAndWeight = FontStyle.Bold;

            VisualElement input = field.Q(className: "unity-text-input");
            if (input != null)
            {
                input.style.fontSize = fontSize;
                input.style.minHeight = minHeight - 12f;
                input.style.color = new StyleColor(Color.white);
                input.style.unityFontStyleAndWeight = FontStyle.Normal;
            }
        }

        private void ToggleMinimized()
        {
            m_IsMinimized = !m_IsMinimized;
            ApplyMinimizedState();
        }

        private void ApplyMinimizedState()
        {
            if (m_PanelsContainer != null)
            {
                m_PanelsContainer.style.display = m_IsMinimized
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            if (m_ToggleButton != null)
            {
                m_ToggleButton.text = m_IsMinimized ? "OPEN PLAYER PANEL" : "MINIMIZE PANEL";
            }

            if (m_CompactStatusLabel != null)
            {
                m_CompactStatusLabel.style.display = m_IsMinimized
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private void UpdateCompactStatus(string nameId)
        {
            if (m_CompactStatusLabel == null)
            {
                return;
            }

            string label = string.IsNullOrWhiteSpace(nameId) ? "player_local" : nameId.Trim();
            m_CompactStatusLabel.text = $"Player: {label} | Session data loaded";
        }

        private string ResolveDefaultCustomizationTemplate(string nameId)
        {
            if (!string.IsNullOrWhiteSpace(m_DefaultCustomizationJson))
            {
                return m_DefaultCustomizationJson;
            }

            string resolvedName = string.IsNullOrWhiteSpace(nameId)
                ? "player_local"
                : nameId.Trim();
            return "{\n"
                + $"  \"name_id\": \"{resolvedName}\",\n"
                + "  \"preferences\": {\n"
                + "    \"mood\": \"curious\",\n"
                + "    \"chat_style\": \"concise\"\n"
                + "  },\n"
                + "  \"session_notes\": []\n"
                + "}";
        }
    }
}
