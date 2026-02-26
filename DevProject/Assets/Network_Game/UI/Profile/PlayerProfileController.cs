using Network_Game.Auth;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Network_Game.UI.Profile
{
    [RequireComponent(typeof(UIDocument))]
    public class PlayerProfileController : MonoBehaviour
    {
        private VisualElement m_ProfileCard;
        private VisualElement m_Content;
        private Label m_NameLabel;
        private Label m_StatusLabel;
        private Label m_BioLabel;
        private Label m_ClientIdLabel;
        private Button m_MinimizeButton;

        private bool m_IsMinimized = false;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_ProfileCard = root.Q<VisualElement>("profile-card");
            m_Content = root.Q<VisualElement>("content");
            m_NameLabel = root.Q<Label>("player-name");
            m_StatusLabel = root.Q<Label>("player-status");
            m_BioLabel = root.Q<Label>("player-bio");
            m_ClientIdLabel = root.Q<Label>("client-id");
            m_MinimizeButton = root.Q<Button>("minimize-button");

            m_MinimizeButton.clicked += ToggleMinimize;
            LocalPlayerAuthService.OnPlayerLoggedIn += UpdateProfile;
            LocalPlayerAuthService.OnPlayerLoggedOut += ClearProfile;

            // Check if already logged in
            if (
                LocalPlayerAuthService.Instance != null
                && LocalPlayerAuthService.Instance.HasCurrentPlayer
            )
            {
                UpdateProfile(LocalPlayerAuthService.Instance.CurrentPlayer);
            }
        }

        private void OnDisable()
        {
            if (m_MinimizeButton != null)
                m_MinimizeButton.clicked -= ToggleMinimize;
            LocalPlayerAuthService.OnPlayerLoggedIn -= UpdateProfile;
            LocalPlayerAuthService.OnPlayerLoggedOut -= ClearProfile;
        }

        private void Update()
        {
            RefreshClientIdLabel();
        }

        private void ToggleMinimize()
        {
            m_IsMinimized = !m_IsMinimized;
            if (m_IsMinimized)
            {
                m_ProfileCard.AddToClassList("blocks-profile-card--minimized");
                m_MinimizeButton.text = "+";
            }
            else
            {
                m_ProfileCard.RemoveFromClassList("blocks-profile-card--minimized");
                m_MinimizeButton.text = "—";
            }
        }

        private void UpdateProfile(LocalPlayerAuthService.LocalPlayerRecord record)
        {
            m_NameLabel.text = record.NameId.ToUpper();
            m_StatusLabel.text = "ONLINE / AUTHENTICATED";
            m_StatusLabel.style.color = new StyleColor(new Color(0.72f, 0.96f, 0.76f));

            m_BioLabel.text = LocalPlayerAuthService.Instance.GetCustomizationJson();
            RefreshClientIdLabel();

            m_ProfileCard.style.display = DisplayStyle.Flex;
        }

        private void ClearProfile()
        {
            m_NameLabel.text = "NOT LOGGED IN";
            m_StatusLabel.text = "Waiting for identity...";
            m_StatusLabel.style.color = new StyleColor(new Color(1f, 1f, 1f, 0.5f));
            m_BioLabel.text = "";
            m_ClientIdLabel.text = "-";
        }

        private void RefreshClientIdLabel()
        {
            if (m_ClientIdLabel == null)
            {
                return;
            }

            string value = "-";
            if (
                NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening
                && NetworkManager.Singleton.LocalClient != null
            )
            {
                value = NetworkManager.Singleton.LocalClientId.ToString();
            }
            else if (
                LocalPlayerAuthService.Instance != null
                && LocalPlayerAuthService.Instance.HasCurrentPlayer
            )
            {
                value = LocalPlayerAuthService.Instance.CurrentPlayer.PlayerId.ToString();
            }

            if (!string.Equals(m_ClientIdLabel.text, value))
            {
                m_ClientIdLabel.text = value;
            }
        }
    }
}
