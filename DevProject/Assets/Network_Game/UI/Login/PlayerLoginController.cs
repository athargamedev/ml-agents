using Network_Game.Auth;
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

        private void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            m_NameInput = m_Root.Q<TextField>("name-input");
            m_BioInput = m_Root.Q<TextField>("bio-input");
            m_LoginButton = m_Root.Q<Button>("login-button");
            m_StatusLabel = m_Root.Q<Label>("status-label");

            m_LoginButton.clicked += OnLoginClicked;
            LocalPlayerAuthService.OnPlayerLoggedIn += HandleLoginSuccess;

            // Load last used name
            if (LocalPlayerAuthService.Instance != null)
            {
                m_NameInput.value = LocalPlayerAuthService.Instance.LastLoginNameId;
            }

            // Ensure cursor is unlocked for UI interaction
            ApplyUiCursorAndLookState();
        }

        private void ApplyUiCursorAndLookState()
        {
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
            if (m_Root == null)
            {
                return;
            }

            if (m_Root.style.display != DisplayStyle.None)
            {
                ApplyUiCursorAndLookState();
            }
            else
            {
                RestoreGameplayCursorAndLookState();
            }
        }

        private void OnDisable()
        {
            if (m_LoginButton != null)
                m_LoginButton.clicked -= OnLoginClicked;
            LocalPlayerAuthService.OnPlayerLoggedIn -= HandleLoginSuccess;
            RestoreGameplayCursorAndLookState();
        }

        private void OnLoginClicked()
        {
            string nameId = m_NameInput.value?.Trim();
            if (string.IsNullOrEmpty(nameId))
            {
                m_StatusLabel.text = "ERROR: NAME_ID CANNOT BE EMPTY";
                m_StatusLabel.style.color = new StyleColor(Color.red);
                return;
            }

            m_StatusLabel.text = "LOGGING IN...";
            m_StatusLabel.style.color = new StyleColor(new Color(1f, 0.84f, 0.54f)); // System warning color

            // First, login to local service
            if (LocalPlayerAuthService.Instance.Login(nameId))
            {
                // If bio is provided, set it as customization JSON
                string bioText = m_BioInput.value?.Trim();
                if (!string.IsNullOrEmpty(bioText))
                {
                    // Basic JSON check or just wrap it if it's not JSON
                    if (!bioText.StartsWith("{"))
                    {
                        bioText = "{\"bio\": \"" + bioText.Replace("\"", "\\\"") + "\"}";
                    }
                    LocalPlayerAuthService.Instance.SetCustomizationJson(bioText);
                }
            }
            else
            {
                m_StatusLabel.text = "LOGIN FAILED";
                m_StatusLabel.style.color = new StyleColor(Color.red);
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
                m_Root.style.display = DisplayStyle.None;
                RestoreGameplayCursorAndLookState();
            })
                .StartingIn(1500);
        }

        public void Show()
        {
            m_Root.style.display = DisplayStyle.Flex;
        }
    }
}
