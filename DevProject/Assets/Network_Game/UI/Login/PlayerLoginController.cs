using Network_Game.Auth;
using Network_Game.Behavior;
using Network_Game.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Network_Game.UI.Login
{
    [RequireComponent(typeof(UIDocument))]
    public class PlayerLoginController : MonoBehaviour
    {
        private VisualElement m_Root;
        private TextField m_NameInput;
        private TextField m_BioInput;
        private Button m_LoginButton;
        private Label m_StatusLabel;
        private bool m_UsingHudCursorRouter;
        private DisplayStyle m_LastDisplayStyle = DisplayStyle.None;
        private bool m_BootstrapEventsSubscribed;

        private void OnEnable()
        {
            LocalPlayerAuthService authService = LocalPlayerAuthService.EnsureInstance();
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            m_NameInput = m_Root.Q<TextField>("name-input");
            m_BioInput = m_Root.Q<TextField>("bio-input");
            m_LoginButton = m_Root.Q<Button>("login-button");
            m_StatusLabel = m_Root.Q<Label>("status-label");

            if (m_LoginButton != null)
                m_LoginButton.clicked += OnLoginClicked;

            LocalPlayerAuthService.OnPlayerLoggedIn += HandleLoginSuccess;
            UpdateBootstrapEventSubscription(true);

            // Load last used name
            if (authService != null && m_NameInput != null)
            {
                m_NameInput.value = authService.LastLoginNameId;
            }

            bool hasCurrentPlayer =
                authService != null
                && authService.HasCurrentPlayer;
            SetLoginVisible(!hasCurrentPlayer);

            if (hasCurrentPlayer)
            {
                RestoreGameplayCursorAndLookState();
            }
            else
            {
                ApplyUiCursorAndLookState();
            }
        }

        private void ApplyUiCursorAndLookState()
        {
            if (ModernHudManager.TryAcquireUiCursor(this))
            {
                m_UsingHudCursorRouter = true;
                return;
            }

            m_UsingHudCursorRouter = false;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            var inputs =
                Object.FindObjectsByType<Network_Game.ThirdPersonController.StarterAssetsInputs>(
                    FindObjectsInactive.Include
                );
            foreach (var input in inputs)
            {
                input.cursorLocked = false;
                input.cursorInputForLook = false;
                input.SetCursorState(false);
            }
        }

        private void RestoreGameplayCursorAndLookState()
        {
            if (m_UsingHudCursorRouter)
            {
                ModernHudManager.TryReleaseUiCursor(this);
                m_UsingHudCursorRouter = false;
                return;
            }

            // Only restore gameplay look after auth is completed, otherwise login UI loses focus control.
            if (
                LocalPlayerAuthService.Instance == null
                || !LocalPlayerAuthService.Instance.HasCurrentPlayer
            )
            {
                return;
            }

            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;

            var inputs =
                Object.FindObjectsByType<Network_Game.ThirdPersonController.StarterAssetsInputs>(
                    FindObjectsInactive.Include
                );
            foreach (var input in inputs)
            {
                input.cursorLocked = true;
                input.cursorInputForLook = true;
                input.SetCursorState(true);
            }
        }

        private void Update()
        {
            if (!m_BootstrapEventsSubscribed && NetworkBootstrapEvents.Instance != null)
            {
                UpdateBootstrapEventSubscription(true);
            }

            if (m_Root == null)
                return;

            DisplayStyle current = m_Root.resolvedStyle.display;
            if (current == m_LastDisplayStyle)
                return;

            m_LastDisplayStyle = current;

            if (current != DisplayStyle.None)
                ApplyUiCursorAndLookState();
            else
                RestoreGameplayCursorAndLookState();
        }

        private void OnDisable()
        {
            if (m_LoginButton != null)
                m_LoginButton.clicked -= OnLoginClicked;
            LocalPlayerAuthService.OnPlayerLoggedIn -= HandleLoginSuccess;
            UpdateBootstrapEventSubscription(false);
            RestoreGameplayCursorAndLookState();
        }

        private void OnLoginClicked()
        {
            LocalPlayerAuthService authService = LocalPlayerAuthService.EnsureInstance();
            if (authService == null)
            {
                if (m_StatusLabel != null)
                {
                    m_StatusLabel.text = "LOGIN SERVICE UNAVAILABLE";
                    m_StatusLabel.style.color = new StyleColor(Color.red);
                }
                return;
            }

            string nameId = m_NameInput != null ? m_NameInput.value?.Trim() : string.Empty;
            if (string.IsNullOrEmpty(nameId))
            {
                if (m_StatusLabel != null)
                {
                    m_StatusLabel.text = "ERROR: NAME_ID CANNOT BE EMPTY";
                    m_StatusLabel.style.color = new StyleColor(Color.red);
                }
                return;
            }

            if (m_StatusLabel != null)
            {
                m_StatusLabel.text = "LOGGING IN...";
                m_StatusLabel.style.color = new StyleColor(new Color(1f, 0.84f, 0.54f)); // System warning color
            }

            // First, login to local service
            if (authService.Login(nameId))
            {
                AttachCurrentLocalPlayer(authService);

                // If bio is provided, set it as customization JSON
                string bioText = m_BioInput != null ? m_BioInput.value?.Trim() : string.Empty;
                if (!string.IsNullOrEmpty(bioText))
                {
                    // Basic JSON check or just wrap it if it's not JSON
                    if (!bioText.StartsWith("{"))
                    {
                        bioText = "{\"bio\": \"" + bioText.Replace("\"", "\\\"") + "\"}";
                    }
                    authService.SetCustomizationJson(bioText);
                }
            }
            else
            {
                if (m_StatusLabel != null)
                {
                    m_StatusLabel.text = "LOGIN FAILED";
                    m_StatusLabel.style.color = new StyleColor(Color.red);
                }
            }
        }

        private void HandleLoginSuccess(LocalPlayerAuthService.LocalPlayerRecord record)
        {
            m_StatusLabel.text = $"LOGGED IN AS {record.NameId.ToUpper()}";
            m_StatusLabel.style.color = new StyleColor(new Color(0.72f, 0.96f, 0.76f)); // Success green

            // Fade out root after a delay
            m_Root
                .schedule.Execute(() =>
            {
                SetLoginVisible(false);
                RestoreGameplayCursorAndLookState();
            })
                .StartingIn(1500);
        }

        public void Show()
        {
            SetLoginVisible(true);
        }

        private void SetLoginVisible(bool visible)
        {
            if (!ModernHudManager.SetPanelVisible(ModernHudManager.HudPanel.Login, visible))
            {
                if (m_Root != null)
                {
                    m_Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        private static void AttachCurrentLocalPlayer(LocalPlayerAuthService authService)
        {
            if (authService == null)
            {
                return;
            }

            GameObject localPlayer = null;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
            {
                localPlayer = manager.LocalClient.PlayerObject.gameObject;
            }

            authService.AttachLocalPlayer(localPlayer);
        }

        private void HandleLocalPlayerSpawned(GameObject player)
        {
            AttachAuthenticatedLocalPlayer(player);
        }

        private void HandleLocalPlayerReady(GameObject player)
        {
            AttachAuthenticatedLocalPlayer(player);
        }

        private static void AttachAuthenticatedLocalPlayer(GameObject player)
        {
            LocalPlayerAuthService authService = LocalPlayerAuthService.Instance;
            if (authService == null || !authService.HasCurrentPlayer)
            {
                return;
            }

            authService.AttachLocalPlayer(player);
        }

        private void UpdateBootstrapEventSubscription(bool subscribe)
        {
            NetworkBootstrapEvents events = NetworkBootstrapEvents.Instance;
            if (events == null)
            {
                m_BootstrapEventsSubscribed = false;
                return;
            }

            if (subscribe)
            {
                if (m_BootstrapEventsSubscribed)
                {
                    return;
                }

                events.OnLocalPlayerSpawned += HandleLocalPlayerSpawned;
                events.OnLocalPlayerReady += HandleLocalPlayerReady;
                m_BootstrapEventsSubscribed = true;
                return;
            }

            if (!m_BootstrapEventsSubscribed)
            {
                return;
            }

            events.OnLocalPlayerSpawned -= HandleLocalPlayerSpawned;
            events.OnLocalPlayerReady -= HandleLocalPlayerReady;
            m_BootstrapEventsSubscribed = false;
        }
    }
}
