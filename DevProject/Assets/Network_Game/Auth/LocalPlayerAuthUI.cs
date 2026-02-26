using Network_Game.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Network_Game.Auth
{
    [DefaultExecutionOrder(-80)]
    public class LocalPlayerAuthUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private TMP_InputField m_NameIdInput;

        [SerializeField]
        private Button m_LoginButton;

        [SerializeField]
        private TMP_Text m_StatusText;

        [Header("Behavior")]
        [SerializeField]
        private bool m_AutoLoginOnStart = true;

        [SerializeField]
        [Tooltip("If enabled, this UI will never auto-submit login on startup.")]
        private bool m_RequireExplicitLoginEachSession = true;

        [SerializeField]
        private string m_FallbackNameId = "player_local";

        [SerializeField]
        private bool m_AutoCreateRuntimeUiIfMissing = true;

        [SerializeField]
        private bool m_AllowGenericChildFallback;

        public static LocalPlayerAuthUI Instance { get; private set; }
        private LocalPlayerAuthService m_Service;

        public static LocalPlayerAuthUI EnsureOnDialogueCanvas()
        {
            LocalPlayerAuthUI existing = FindExisting();
            if (existing != null)
            {
                existing.EnsureRuntimeUiIfNeeded();
                return existing;
            }

            Canvas targetCanvas = ResolveDialogueCanvas();
            if (targetCanvas == null)
            {
                return null;
            }

            var root = new GameObject("LocalPlayerAuthUI", typeof(RectTransform));
            root.transform.SetParent(targetCanvas.transform, false);
            LocalPlayerAuthUI ui = root.AddComponent<LocalPlayerAuthUI>();
            ui.m_AutoLoginOnStart = false;
            ui.m_AutoCreateRuntimeUiIfMissing = true;
            ui.EnsureRuntimeUiIfNeeded();
            return ui;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureRuntimeUiIfNeeded();
        }

        private void Start()
        {
            m_Service = LocalPlayerAuthService.EnsureInstance();
            if (m_Service == null)
            {
                SetStatus("Auth service unavailable.");
                return;
            }

            if (m_NameIdInput != null && string.IsNullOrWhiteSpace(m_NameIdInput.text))
            {
                m_NameIdInput.text = m_Service.LastLoginNameId;
            }

            if (m_Service.HasCurrentPlayer)
            {
                SetStatus($"Logged in: {m_Service.CurrentPlayer.NameId}");
            }
            else
            {
                SetStatus("Not logged in.");
            }

            if (
                m_AutoLoginOnStart
                && !m_RequireExplicitLoginEachSession
                && !m_Service.HasCurrentPlayer
            )
            {
                LoginFromInput();
            }
        }

        private void OnEnable()
        {
            if (m_LoginButton != null)
            {
                m_LoginButton.onClick.AddListener(LoginFromInput);
            }

            LocalPlayerAuthService.OnPlayerLoggedIn += HandleLoggedIn;
            LocalPlayerAuthService.OnPlayerLoggedOut += HandleLoggedOut;

            UnlockCursor();
        }

        private void Update()
        {
            if (m_Service != null && !m_Service.HasCurrentPlayer)
            {
                UnlockCursor();
            }
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable()
        {
            if (m_LoginButton != null)
            {
                m_LoginButton.onClick.RemoveListener(LoginFromInput);
            }

            LocalPlayerAuthService.OnPlayerLoggedIn -= HandleLoggedIn;
            LocalPlayerAuthService.OnPlayerLoggedOut -= HandleLoggedOut;
        }

        public void LoginFromInput()
        {
            if (m_Service == null)
            {
                m_Service = LocalPlayerAuthService.EnsureInstance();
            }

            if (m_Service == null)
            {
                SetStatus("Auth service unavailable.");
                return;
            }

            string nameId = m_NameIdInput != null ? m_NameIdInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(nameId))
            {
                nameId = m_FallbackNameId;
            }

            bool success = m_Service.Login(nameId);
            if (!success)
            {
                SetStatus("Login failed. Use letters/numbers/_/-.");
                return;
            }

            SetStatus($"Logged in: {m_Service.CurrentPlayer.NameId}");
            NGLog.Info(
                "AuthUI",
                NGLog.Format("Login button", ("name_id", m_Service.CurrentPlayer.NameId))
            );
        }

        private void HandleLoggedIn(LocalPlayerAuthService.LocalPlayerRecord record)
        {
            SetStatus($"Logged in: {record.NameId}");
            if (m_NameIdInput != null)
            {
                m_NameIdInput.text = record.NameId;
            }
        }

        private void HandleLoggedOut()
        {
            SetStatus("Logged out.");
        }

        private void SetStatus(string text)
        {
            if (m_StatusText != null)
            {
                m_StatusText.text = text ?? string.Empty;
            }
        }

        private void EnsureRuntimeUiIfNeeded()
        {
            ResolveUiReferences();
            if (m_NameIdInput != null && m_LoginButton != null && m_StatusText != null)
            {
                return;
            }

            if (!m_AutoCreateRuntimeUiIfMissing)
            {
                return;
            }

            BuildRuntimeUi();
            ResolveUiReferences();
        }

        private void ResolveUiReferences()
        {
            if (m_NameIdInput == null)
            {
                m_NameIdInput = FindChildByName<TMP_InputField>("Auth_NameIdInput");
            }

            if (m_LoginButton == null)
            {
                m_LoginButton = FindChildByName<Button>("Auth_LoginButton");
            }

            if (m_StatusText == null)
            {
                m_StatusText = FindChildByName<TMP_Text>("Auth_StatusText");
            }

            if (!m_AllowGenericChildFallback)
            {
                return;
            }

            if (m_NameIdInput == null)
            {
                m_NameIdInput = GetComponentInChildren<TMP_InputField>(true);
            }

            if (m_LoginButton == null)
            {
                m_LoginButton = GetComponentInChildren<Button>(true);
            }

            if (m_StatusText == null)
            {
                m_StatusText = GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void BuildRuntimeUi()
        {
            RectTransform root = EnsureRootRectTransform();
            EnsureRootImage();

            if (m_NameIdInput == null)
            {
                m_NameIdInput = CreateNameIdInput(root);
            }

            if (m_LoginButton == null)
            {
                m_LoginButton = CreateLoginButton(root);
            }

            if (m_StatusText == null)
            {
                m_StatusText = CreateStatusText(root);
            }
        }

        private RectTransform EnsureRootRectTransform()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
            {
                root = gameObject.AddComponent<RectTransform>();
            }

            root.anchorMin = new Vector2(1f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-12f, -12f);
            root.sizeDelta = new Vector2(380f, 110f);
            return root;
        }

        private void EnsureRootImage()
        {
            Image panelImage = GetComponent<Image>();
            if (panelImage == null)
            {
                panelImage = gameObject.AddComponent<Image>();
            }

            panelImage.color = new Color(0f, 0f, 0f, 0.45f);
        }

        private TMP_InputField CreateNameIdInput(RectTransform parent)
        {
            var inputGo = new GameObject(
                "Auth_NameIdInput",
                typeof(RectTransform),
                typeof(Image),
                typeof(TMP_InputField)
            );
            inputGo.transform.SetParent(parent, false);

            RectTransform inputRect = inputGo.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(0f, 1f);
            inputRect.pivot = new Vector2(0f, 1f);
            inputRect.anchoredPosition = new Vector2(12f, -12f);
            inputRect.sizeDelta = new Vector2(230f, 36f);

            Image inputBg = inputGo.GetComponent<Image>();
            inputBg.color = new Color(1f, 1f, 1f, 0.92f);

            var textAreaGo = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textAreaGo.transform.SetParent(inputGo.transform, false);
            RectTransform textAreaRect = textAreaGo.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10f, 6f);
            textAreaRect.offsetMax = new Vector2(-10f, -6f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(textAreaGo.transform, false);
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.fontSize = 20f;
            text.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false;

            var placeholderGo = new GameObject(
                "Placeholder",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );
            placeholderGo.transform.SetParent(textAreaGo.transform, false);
            RectTransform placeholderRect = placeholderGo.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;

            var placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
            placeholder.fontSize = 18f;
            placeholder.color = new Color(0.3f, 0.3f, 0.3f, 0.65f);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.text = "name_id";

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                text.font = font;
                placeholder.font = font;
            }

            TMP_InputField input = inputGo.GetComponent<TMP_InputField>();
            input.textViewport = textAreaRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = m_FallbackNameId;
            return input;
        }

        private Button CreateLoginButton(RectTransform parent)
        {
            var buttonGo = new GameObject(
                "Auth_LoginButton",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button)
            );
            buttonGo.transform.SetParent(parent, false);

            RectTransform buttonRect = buttonGo.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = new Vector2(-12f, -12f);
            buttonRect.sizeDelta = new Vector2(120f, 36f);

            Image buttonImage = buttonGo.GetComponent<Image>();
            buttonImage.color = new Color(0.18f, 0.55f, 0.92f, 0.95f);

            Button button = buttonGo.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = buttonImage.color;
            colors.highlightedColor = new Color(0.22f, 0.62f, 1f, 0.95f);
            colors.pressedColor = new Color(0.12f, 0.42f, 0.76f, 0.95f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.6f);
            button.colors = colors;

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(buttonGo.transform, false);
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.fontSize = 18f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = "Login";

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                label.font = font;
            }

            return button;
        }

        private TMP_Text CreateStatusText(RectTransform parent)
        {
            var statusGo = new GameObject(
                "Auth_StatusText",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );
            statusGo.transform.SetParent(parent, false);

            RectTransform statusRect = statusGo.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            statusRect.anchoredPosition = new Vector2(12f, -54f);
            statusRect.sizeDelta = new Vector2(356f, 44f);

            var status = statusGo.GetComponent<TextMeshProUGUI>();
            status.fontSize = 17f;
            status.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            status.alignment = TextAlignmentOptions.TopLeft;
            status.textWrappingMode = TextWrappingModes.Normal;
            status.text = "Login with name_id";

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                status.font = font;
            }

            return status;
        }

        private T FindChildByName<T>(string nodeName)
            where T : Component
        {
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return null;
            }

            Transform[] allChildren = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allChildren.Length; i++)
            {
                Transform child = allChildren[i];
                if (child == null || !string.Equals(child.name, nodeName))
                {
                    continue;
                }

                return child.GetComponent<T>();
            }

            return null;
        }

        private static LocalPlayerAuthUI FindExisting()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<LocalPlayerAuthUI>();
#else
            return FindAnyObjectByType<LocalPlayerAuthUI>();
#endif
        }

        private static Canvas ResolveDialogueCanvas()
        {
            GameObject byName = GameObject.Find("Canvas_Dialogue");
            if (byName != null)
            {
                Canvas canvas = byName.GetComponent<Canvas>();
                if (canvas != null)
                {
                    return canvas;
                }
            }

#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<Canvas>();
#else
            return FindAnyObjectByType<Canvas>();
#endif
        }
    }
}
