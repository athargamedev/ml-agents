using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Network_Game.Auth;
using Network_Game.Diagnostics;
using Network_Game.UI.Login;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Serialization;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Handles network lifecycle: transport config, host/client startup, connection approval.
    /// Publishes events for other components to react.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class NetworkBootstrap : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] public bool m_ForceClientMode;
        [SerializeField] public string m_ClientModeTag = "Client";
        [SerializeField] public bool m_AvoidHostStartWhenPortIsInUse = true;
        [SerializeField] public bool m_TryHostPortFallbackOnStartFailure = true;
        [SerializeField][Min(1024)] public int m_HostFallbackPortStart = 7778;
        [SerializeField][Range(1, 32)] public int m_HostFallbackPortAttempts = 8;

        [Header("Spawn Point")]
        [SerializeField] public Transform m_PlayerSpawnPoint;
        [SerializeField] public string m_PlayerTag = "Player";

        private NetworkManager m_Manager;
        private bool m_IsClientMode;
        private bool m_NetworkCallbacksRegistered;

        public bool IsClientMode => m_IsClientMode;
        public NetworkManager NetworkManager => m_Manager;

        private void Awake()
        {
            m_Manager = NetworkManager.Singleton;
            EnsureEventsComponent();
        }

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private void OnDisable()
        {
            UnregisterNetworkCallbacks();
            ClearEditorHostEndpoint();
        }

        private void EnsureEventsComponent()
        {
            if (NetworkBootstrapEvents.Instance == null)
            {
                gameObject.AddComponent<NetworkBootstrapEvents>();
            }
        }

        private void RegisterNetworkCallbacks()
        {
            if (m_Manager == null || m_NetworkCallbacksRegistered)
            {
                return;
            }

            m_Manager.OnClientConnectedCallback += HandleClientConnected;
            m_Manager.OnClientDisconnectCallback += HandleClientDisconnected;
            m_NetworkCallbacksRegistered = true;
        }

        private void UnregisterNetworkCallbacks()
        {
            if (m_Manager == null)
            {
                return;
            }

            m_Manager.ConnectionApprovalCallback = null;
            if (!m_NetworkCallbacksRegistered)
            {
                return;
            }

            m_Manager.OnClientConnectedCallback -= HandleClientConnected;
            m_Manager.OnClientDisconnectCallback -= HandleClientDisconnected;
            m_NetworkCallbacksRegistered = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            NGLog.Info("NetworkBootstrap", $"Client connected: {clientId}");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            NGLog.Warn("NetworkBootstrap", $"Client disconnected: {clientId}");

            if (m_Manager == null || clientId != m_Manager.LocalClientId)
            {
                return;
            }

            string message = m_IsClientMode
                ? "Disconnected from host"
                : "Host networking stopped";
            NetworkBootstrapEvents.Instance.PublishNetworkError(message);
        }

        private IEnumerator Initialize()
        {
            // Wait for NetworkManager
            float duration = 15f;
            float pollInterval = 0.5f;

            while (m_Manager == null && duration > 0f)
            {
                m_Manager = NetworkManager.Singleton;
                duration -= pollInterval;
                yield return new WaitForSeconds(pollInterval);
            }

            if (m_Manager == null)
            {
                NGLog.Error("NetworkBootstrap", "NetworkManager not found after timeout.");
                yield break;
            }

            RegisterNetworkCallbacks();

            // Setup connection approval
            ResolveSpawnPointReference();
            m_Manager.NetworkConfig.ConnectionApproval = true;
            m_Manager.ConnectionApprovalCallback = OnConnectionApproval;

#if UNITY_SERVER
            if (!Application.isEditor)
            {
                ConfigureWebSocketTransport();
                NGLog.Info("NetworkBootstrap", "Starting as Dedicated Server");
                m_Manager.StartServer();
                NetworkBootstrapEvents.Instance.PublishNetworkReady(m_Manager);
                yield break;
            }
#endif

            // Determine mode
            m_IsClientMode = DetermineClientMode();
            NetworkBootstrapEvents.Instance.PublishClientModeDetermined(m_IsClientMode);

            yield return StartCoroutine(WaitForAuthenticationIfRequired());

            // Configure transport
            ConfigureTransport();

#if UNITY_WEBGL && !UNITY_EDITOR
            m_IsClientMode = true;
#endif

            // Start networking
            if (m_IsClientMode)
            {
                yield return StartCoroutine(WaitForEditorHostEndpoint());
                if (!m_Manager.StartClient())
                {
                    NGLog.Error("NetworkBootstrap", "Client startup failed.");
                    NetworkBootstrapEvents.Instance.PublishNetworkError("Client startup failed");
                    yield break;
                }
                NetworkBootstrapEvents.Instance.PublishNetworkReady(m_Manager);
                NGLog.Info("NetworkBootstrap", "Client started");
                NetworkBootstrapEvents.Instance.PublishClientStarted();
            }
            else
            {
                ClearEditorHostEndpoint();
                if (!TryStartHost())
                {
                    NGLog.Error("NetworkBootstrap", "Host start failed.");
                    NetworkBootstrapEvents.Instance.PublishNetworkError("Host start failed");
                    yield break;
                }
                NetworkBootstrapEvents.Instance.PublishNetworkReady(m_Manager);
                NGLog.Info("NetworkBootstrap", "Host started");
                NetworkBootstrapEvents.Instance.PublishHostStarted();
            }
        }

        private IEnumerator WaitForAuthenticationIfRequired()
        {
            AuthBootstrap authBootstrap = GetComponent<AuthBootstrap>();
            if (authBootstrap == null || !authBootstrap.m_BlockNetworkStartUntilAuthenticated)
            {
                yield break;
            }

            LocalPlayerAuthService authService = LocalPlayerAuthService.EnsureInstance();
            if (authService == null)
            {
                yield break;
            }

            EnsureLoginUiAvailable();

            if (!authBootstrap.m_RequireExplicitLoginEachSession)
            {
                authService.EnsureLoggedIn();
            }

            while (!authService.HasCurrentPlayer)
            {
                EnsureLoginUiAvailable();
                yield return null;
            }

            authService.EnsurePromptContextInitialized();
        }

        private static void EnsureLoginUiAvailable()
        {
            PlayerLoginController[] loginControllers =
                Resources.FindObjectsOfTypeAll<PlayerLoginController>();
            for (int i = 0; i < loginControllers.Length; i++)
            {
                PlayerLoginController loginController = loginControllers[i];
                if (loginController == null || !loginController.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (!loginController.gameObject.activeSelf)
                {
                    loginController.gameObject.SetActive(true);
                }

                if (!loginController.enabled)
                {
                    loginController.enabled = true;
                }

                loginController.Show();
                return;
            }
        }

        private bool DetermineClientMode()
        {
            if (m_ForceClientMode)
                return true;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-client", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (IsEditorVirtualPlayerClone())
                return true;

#if UNITY_EDITOR
            try
            {
                var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.Tags;
                if (tags != null)
                {
                    for (int i = 0; i < tags.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(tags[i]) && tags[i].Contains(m_ClientModeTag, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch {}
#endif
            return false;
        }

        private void ConfigureTransport()
        {
            var transport = m_Manager.GetComponent<UnityTransport>();
            if (transport == null) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            transport.UseWebSockets = true;
#elif UNITY_SERVER && !UNITY_EDITOR
            transport.UseWebSockets = true;
#else
            transport.UseWebSockets = false;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            ReadServerAddressFromUrl(transport);
#endif
        }

        private bool TryStartHost()
        {
            if (m_Manager.StartHost())
            {
                PersistEditorHostEndpoint();
                return true;
            }

            if (!m_TryHostPortFallbackOnStartFailure)
                return false;

            var transport = m_Manager.GetComponent<UnityTransport>();
            if (transport == null)
                return false;

            int startPort = Mathf.Clamp(m_HostFallbackPortStart, 1024, 65535);
            int attempts = Mathf.Clamp(m_HostFallbackPortAttempts, 1, 32);

            for (int i = 0; i < attempts; i++)
            {
                int candidatePort = startPort + i;
                if (!CanBindUdpPort(candidatePort))
                    continue;

                if (m_Manager.IsListening)
                    m_Manager.Shutdown();

                transport.SetConnectionData("127.0.0.1", (ushort)candidatePort, "0.0.0.0");

                if (m_Manager.StartHost())
                {
                    PersistEditorHostEndpoint();
                    NGLog.Warn("NetworkBootstrap", $"Host started on fallback port {candidatePort}");
                    return true;
                }
            }

            return false;
        }

        private void OnConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            ResolveSpawnPointReference();
            Vector3 spawnPos = m_PlayerSpawnPoint?.position ?? Vector3.zero;
            Quaternion spawnRot = m_PlayerSpawnPoint?.rotation ?? Quaternion.identity;

            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = spawnPos;
            response.Rotation = spawnRot;

            NGLog.Info("NetworkBootstrap", $"Connection approved for client {request.ClientNetworkId} at {spawnPos}");
        }

        private void ResolveSpawnPointReference()
        {
            if (m_PlayerSpawnPoint != null) return;

            var spawnByName = GameObject.Find("SpawnPoint");
            if (spawnByName != null)
            {
                m_PlayerSpawnPoint = spawnByName.transform;
                return;
            }

            try
            {
                var spawnByTag = GameObject.FindGameObjectWithTag("SpawnPoint");
                if (spawnByTag != null)
                    m_PlayerSpawnPoint = spawnByTag.transform;
            }
            catch (UnityException) {}
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private void ReadServerAddressFromUrl(UnityTransport transport)
        {
            try
            {
                string url = Application.absoluteURL;
                if (string.IsNullOrEmpty(url)) return;

                int queryStart = url.IndexOf('?');
                if (queryStart < 0) return;

                string query = url.Substring(queryStart + 1);
                string serverIp = "127.0.0.1";
                ushort port = 7777;

                foreach (string pair in query.Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq < 0) continue;

                    string key = pair.Substring(0, eq);
                    string val = pair.Substring(eq + 1);

                    if (string.Equals(key, "server", StringComparison.OrdinalIgnoreCase))
                        serverIp = val;
                    else if (string.Equals(key, "port", StringComparison.OrdinalIgnoreCase))
                        if (ushort.TryParse(val, out ushort p))
                            port = p;
                }

                transport.SetConnectionData(serverIp, port);
                NGLog.Info("NetworkBootstrap", $"Server address from URL: {serverIp}:{port}");
            }
            catch (Exception ex)
            {
                NGLog.Warn("NetworkBootstrap", $"Failed to read server address: {ex.Message}");
            }
        }

#endif

        private IEnumerator WaitForEditorHostEndpoint()
        {
#if UNITY_EDITOR
            if (!IsEditorVirtualPlayerClone())
                yield break;

            var transport = m_Manager?.GetComponent<UnityTransport>();
            if (transport == null)
                yield break;

            float timeout = 5f;
            while (timeout > 0f)
            {
                if (TryReadEditorHostEndpoint(out string hostAddress, out ushort hostPort))
                {
                    transport.SetConnectionData(hostAddress, hostPort);
                    NGLog.Info("NetworkBootstrap", $"Using host endpoint: {hostAddress}:{hostPort}");
                    yield break;
                }

                timeout -= Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : 0.1f;
                yield return null;
            }

            NGLog.Warn("NetworkBootstrap", "Host endpoint file not ready, using default");
#else
            yield break;
#endif
        }

        private static bool IsEditorVirtualPlayerClone()
        {
#if UNITY_EDITOR
            try
            {
                string normalizedDataPath = Application.dataPath.Replace('\\', '/');
                return normalizedDataPath.IndexOf("/Library/VP/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch {}
#endif
            return false;
        }

        private static string GetEditorHostEndpointFilePath()
        {
#if UNITY_EDITOR
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkGame");
            return Path.Combine(folder, "behavior-scene-host-endpoint.txt");
#else
            return string.Empty;
#endif
        }

        private static void ClearEditorHostEndpoint()
        {
#if UNITY_EDITOR
            try
            {
                string path = GetEditorHostEndpointFilePath();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch {}
#endif
        }

        private void PersistEditorHostEndpoint()
        {
#if UNITY_EDITOR
            if (m_Manager == null) return;

            try
            {
                int port = ResolveConfiguredListenPort();
                if (port <= 0 || port > 65535) return;

                string path = GetEditorHostEndpointFilePath();
                if (string.IsNullOrWhiteSpace(path)) return;

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, $"127.0.0.1:{port}");
            }
            catch {}
#endif
        }

        private static bool TryReadEditorHostEndpoint(out string address, out ushort port)
        {
            address = "127.0.0.1";
            port = 0;
#if UNITY_EDITOR
            try
            {
                string path = GetEditorHostEndpointFilePath();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;

                string content = File.ReadAllText(path)?.Trim();
                if (string.IsNullOrWhiteSpace(content))
                    return false;

                string[] parts = content.Split(':');
                if (parts.Length != 2)
                    return false;

                string parsedAddress = string.IsNullOrWhiteSpace(parts[0]) ? "127.0.0.1" : parts[0].Trim();
                if (!ushort.TryParse(parts[1], out ushort parsedPort) || parsedPort == 0)
                    return false;

                address = parsedAddress;
                port = parsedPort;
                return true;
            }
            catch {}
#endif
            return false;
        }

        private static int ResolveConfiguredListenPort()
        {
            const int fallbackPort = 7777;
            var manager = NetworkManager.Singleton;
            object transport = manager?.NetworkConfig?.NetworkTransport;
            if (transport == null) return fallbackPort;

            try
            {
                PropertyInfo connectionDataProperty = transport.GetType().GetProperty("ConnectionData");
                if (connectionDataProperty == null) return fallbackPort;

                object connectionData = connectionDataProperty.GetValue(transport);
                if (connectionData == null) return fallbackPort;

                Type connectionType = connectionData.GetType();
                PropertyInfo portProperty = connectionType.GetProperty("Port");

                if (portProperty != null && int.TryParse(portProperty.GetValue(connectionData)?.ToString(), out int propertyPort) && propertyPort > 0)
                    return propertyPort;

                FieldInfo portField = connectionType.GetField("Port");
                if (portField != null && int.TryParse(portField.GetValue(connectionData)?.ToString(), out int fieldPort) && fieldPort > 0)
                    return fieldPort;
            }
            catch {}

            return fallbackPort;
        }

        private static bool CanBindUdpPort(int port)
        {
#if UNITY_WEBGL
            return true;
#else
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ExclusiveAddressUse = true };
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                return true;
            }
            catch (SocketException) { return false; }
            finally { socket?.Close(); }
#endif
        }
    }
}
