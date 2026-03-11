using System;
using System.Collections;
using Network_Game.Auth;
using Network_Game.Diagnostics;
using Network_Game.UI.Login;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Handles authentication gate - ensures player is authenticated before network proceeds.
    /// </summary>
    [DefaultExecutionOrder(-220)]
    public class AuthBootstrap : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField][Min(0.5f)] public float m_TimeoutSeconds = 15f;
        [SerializeField] public bool m_BlockNetworkStartUntilAuthenticated = true;
        [SerializeField] public bool m_RequireExplicitLoginEachSession = true;

        private LocalPlayerAuthService m_AuthService;
        private bool m_IsClientMode;
        private bool m_AuthSatisfied;

        public bool IsAuthenticated => m_AuthSatisfied;

        private void OnEnable()
        {
            var events = NetworkBootstrapEvents.Instance;
            if (events != null)
            {
                events.OnClientModeDetermined += OnClientModeDetermined;
                events.OnNetworkReady += OnNetworkReady;
            }
        }

        private void OnDisable()
        {
            var events = NetworkBootstrapEvents.Instance;
            if (events != null)
            {
                events.OnClientModeDetermined -= OnClientModeDetermined;
                events.OnNetworkReady -= OnNetworkReady;
            }
        }

        private void OnClientModeDetermined(bool isClient)
        {
            m_IsClientMode = isClient;
        }

        private void OnNetworkReady(NetworkManager manager)
        {
            StartCoroutine(EnsureAuthGate());
        }

        private IEnumerator EnsureAuthGate()
        {
            m_AuthSatisfied = false;
            m_AuthService = LocalPlayerAuthService.EnsureInstance();

            if (m_AuthService == null)
            {
                m_AuthSatisfied = ShouldAllowUnauthenticatedStart();
                if (m_AuthSatisfied)
                    NetworkBootstrapEvents.Instance.PublishAuthGatePassed();
                yield break;
            }

            EnsureAuthLoginUiAvailable();

            // Already have a player
            if (m_AuthService.HasCurrentPlayer)
            {
                m_AuthService.EnsurePromptContextInitialized();
                m_AuthSatisfied = true;
                NetworkBootstrapEvents.Instance.PublishAuthGatePassed();
                yield break;
            }

            // Auto-login if not required explicit
            if (!m_RequireExplicitLoginEachSession)
            {
                m_AuthService.EnsureLoggedIn();
            }

            // Wait for auth
            float timeout = Mathf.Max(0.5f, m_TimeoutSeconds);
            while (!m_AuthService.HasCurrentPlayer && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            // Handle timeout
            if (!m_AuthService.HasCurrentPlayer && m_BlockNetworkStartUntilAuthenticated)
            {
                if (ShouldAllowUnauthenticatedStart())
                {
                    NGLog.Warn("AuthBootstrap", "Continuing without login (editor/client mode)");
                }
                else
                {
                    NGLog.Info("AuthBootstrap", "Waiting for login...");
                    while (!m_AuthService.HasCurrentPlayer)
                        yield return null;
                }
            }

            if (m_AuthService.HasCurrentPlayer)
            {
                m_AuthService.EnsurePromptContextInitialized();
            }

            m_AuthSatisfied = true;
            NetworkBootstrapEvents.Instance.PublishAuthGatePassed();
            NGLog.Info("AuthBootstrap", "Auth gate passed");
        }

        private bool ShouldAllowUnauthenticatedStart()
        {
#if UNITY_EDITOR
            // Editor multiplayer sessions
            if (IsEditorMultiplayerSession())
                return true;
#endif
            // Client mode always allows
            return m_IsClientMode;
        }

#if UNITY_EDITOR
        private static bool IsEditorMultiplayerSession()
        {
            try
            {
                var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.Tags;
                return tags != null && tags.Count > 0;
            }
            catch {}
            return false;
        }

#endif

        private void EnsureAuthLoginUiAvailable()
        {
            if (m_AuthService == null) return;

            var loginControllers = Resources.FindObjectsOfTypeAll<PlayerLoginController>();
            for (int i = 0; i < loginControllers.Length; i++)
            {
                var loginController = loginControllers[i];
                if (loginController == null || !loginController.gameObject.scene.IsValid())
                    continue;

                if (!loginController.gameObject.activeSelf)
                    loginController.gameObject.SetActive(true);

                if (!loginController.enabled)
                    loginController.enabled = true;

                loginController.Show();
                return;
            }
        }

        /// <summary>
        /// Called by other components to attach auth to a player.
        /// </summary>
        public void AttachAuthToPlayer(GameObject player)
        {
            if (m_AuthService == null) return;
            m_AuthService.AttachLocalPlayer(player);

            if (m_IsClientMode)
            {
                if (!m_RequireExplicitLoginEachSession)
                {
                    string clientName = GenerateClientPlayerName();
                    m_AuthService.Login(clientName);
                    NGLog.Info("AuthBootstrap", $"Client auto-auth: {clientName}");
                }
                else
                {
                    EnsureAuthLoginUiAvailable();
                }
            }
        }

        private string GenerateClientPlayerName()
        {
            string baseName = "player";
#if UNITY_EDITOR
            try
            {
                var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.Tags;
                if (tags != null && tags.Count > 0 && !string.IsNullOrEmpty(tags[0]))
                    baseName = tags[0].Replace(" ", "_").ToLowerInvariant();
            }
            catch {}
#endif
            return $"{baseName}_{UnityEngine.Random.Range(100, 999)}";
        }
    }
}
