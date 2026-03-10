using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Network_Game.Auth;
using Network_Game.Combat;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
#if !UNITY_WEBGL
using System.Net;
using System.Net.Sockets;
#endif

namespace Network_Game.Behavior
{
    /// <summary>
    /// Bootstraps the scene by starting host/client networking and wiring runtime systems.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BehaviorSceneBootstrap : MonoBehaviour
    {
        [Header("NPC")]
        [SerializeField]
        [FormerlySerializedAs("m_NpcAgent")]
        private GameObject m_PrimaryNpc;

        [SerializeField]
        private string m_PlayerTag = "Player";

        [SerializeField]
        private Transform m_PlayerSpawnPoint;

        [Header("Auth Gate")]
        [SerializeField]
        [Min(0.5f)]
        private float m_AuthGateTimeoutSeconds = 15f;

        [SerializeField]
        [Tooltip(
            "If true, host startup waits until auth is confirmed instead of continuing after timeout."
        )]
        private bool m_BlockNetworkStartUntilAuthenticated = true;

        [SerializeField]
        [Tooltip("If enabled, login must be explicit each run; bootstrap will not auto-login.")]
        private bool m_RequireExplicitLoginEachSession = true;

        [Header("Spawn")]
        [SerializeField]
        [Tooltip(
            "When enabled, local player is aligned to SpawnPoint after network spawn resolves."
        )]
        private bool m_AlignLocalPlayerToSpawnPoint = true;

        [Header("Client Mode (MPPM / 2-Player)")]
        [SerializeField]
        [Tooltip(
            "Force this instance to start as a client instead of host. Use for manual 2-player testing."
        )]
        private bool m_ForceClientMode;

        [SerializeField]
        [Tooltip("MPPM player tag that triggers client mode (e.g. 'Client').")]
#pragma warning disable CS0414
        private string m_ClientModeTag = "Client";
#pragma warning restore CS0414

        [SerializeField]
        [Tooltip(
            "Avoids noisy host bind failures by switching to client mode when the configured UTP listen port is already occupied."
        )]
        private bool m_AvoidHostStartWhenPortIsInUse = true;

        [SerializeField]
        [Tooltip(
            "If host startup fails unexpectedly, retry host on the next free UDP port instead of silently falling back to client mode."
        )]
        private bool m_TryHostPortFallbackOnStartFailure = true;

        [SerializeField]
        [Min(1024)]
        [Tooltip("First UDP port to try when host startup fallback is enabled.")]
        private int m_HostFallbackPortStart = 7778;

        [SerializeField]
        [Range(1, 32)]
        [Tooltip("How many sequential fallback ports to probe when host startup fails.")]
        private int m_HostFallbackPortAttempts = 8;

        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip(
            "Auto-creates LlmDebugAssistant at runtime. Keep disabled for multiplayer latency tests to avoid extra LLM traffic."
        )]
        private bool m_AutoCreateLlmDebugAssistant;

        [SerializeField]
        [Tooltip("When enabled, clients can also auto-create LlmDebugAssistant.")]
        public bool m_EnableDebugAssistantOnClients;

        private const float EditorClientEndpointWaitSeconds = 5f;
        private bool m_AuthGateSatisfied;
        private SceneCameraManager m_CameraManager;
        private NPCAgentBootstrap m_NpcBootstrap;
        private readonly HashSet<ulong> m_SpawnAlignedPlayerIds = new HashSet<ulong>();

        private void Awake()
        {
            NGLog.Info("Bootstrap", "Awake");
            InitializeModules();
            if (!IsClientMode())
            {
                ClearEditorHostEndpoint();
            }
        }

        private void InitializeModules()
        {
            m_CameraManager = GetComponent<SceneCameraManager>();
            if (m_CameraManager == null)
                m_CameraManager = gameObject.AddComponent<SceneCameraManager>();

            m_NpcBootstrap = GetComponent<NPCAgentBootstrap>();
            if (m_NpcBootstrap == null)
                m_NpcBootstrap = gameObject.AddComponent<NPCAgentBootstrap>();
        }

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private void OnDisable()
        {
            if (m_CameraManager != null)
                m_CameraManager.StopMonitoring();

            if (!IsClientMode())
            {
                ClearEditorHostEndpoint();
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.ConnectionApprovalCallback = null;
            }
        }

        private IEnumerator Initialize()
        {
            float duration = 15f;
            float pollInterval = 0.5f;

            NetworkManager manager = NetworkManager.Singleton;
            while (manager == null && duration > 0f)
            {
                manager = NetworkManager.Singleton;
                duration -= pollInterval;
                yield return new WaitForSeconds(pollInterval);
            }

            if (manager == null)
            {
                NGLog.Error("Bootstrap", "NetworkManager not found after timeout.");
                yield break;
            }

            // --- Register connection approval so all players spawn at SpawnPoint ---
            ResolveSpawnPointReference();
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = OnConnectionApproval;

#if UNITY_SERVER
            // In Editor Play Mode, never force dedicated-server bootstrap automatically.
            if (!Application.isEditor)
            {
                // --- Dedicated headless server: enable WebSocket transport and start server-only ---
                ConfigureWebSocketTransport(manager);
                NGLog.Info("Bootstrap", "Starting as Dedicated Server (UNITY_SERVER)");
                manager.StartServer();
                NGLog.Info(
                    "Bootstrap",
                    NGLog.Format(
                        "Dedicated server listening",
                        ("port", ResolveConfiguredListenPort(manager))
                    )
                );
                yield break; // No local player, no camera, no input needed on server
            }
#endif

            // --- Auth gate FIRST (host/client only): player must identify before network spawn ---
            yield return StartCoroutine(EnsureAuthGate());

#if UNITY_WEBGL && !UNITY_EDITOR
            // --- WebGL client: read server address from URL query string ---
            ReadServerAddressFromUrl(manager);
#endif

            // --- WebGL always uses WebSocket transport ---
            ConfigureWebSocketTransport(manager);

            bool clientMode = IsClientMode();

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL build is always client-only for Netcode runtime.
            clientMode = true;
#endif

            if (clientMode)
            {
                yield return StartCoroutine(WaitForEditorHostEndpoint(manager));
                NGLog.Info("Bootstrap", "Starting as Client (MPPM / Manual Force)");
                if (!manager.StartClient())
                {
                    NGLog.Error("Bootstrap", "Client startup failed.");
                    yield break;
                }
            }
            else
            {
                ClearEditorHostEndpoint();

                if (
                    m_AvoidHostStartWhenPortIsInUse
                    && IsConfiguredListenPortInUse(manager, out int occupiedPort)
                )
                {
                    NGLog.Warn(
                        "Bootstrap",
                        NGLog.Format(
                            "Configured listen port already occupied; trying host fallback ports",
                            ("port", occupiedPort)
                        )
                    );
                }

                bool hostStarted = TryStartHostWithPortFallback(manager);
                if (!hostStarted)
                {
                    NGLog.Error(
                        "Bootstrap",
                        "Host start failed and no fallback port succeeded. Network bootstrap aborted."
                    );
                    yield break;
                }
            }

            EnsureDebugAssistant(clientMode);

            // Clients need longer: TCP connect + host approval + spawn replication
            float playerWaitTimeout = clientMode ? (Application.isEditor ? 25f : 15f) : 5f;
            // Retry interval for client reconnect (host may not be up when client first connects)
            float clientRetryInterval = 3f;
            float nextClientRetry = clientRetryInterval;
            GameObject player = null;
            while (player == null && playerWaitTimeout > 0)
            {
                player = ResolveLocalPlayer(manager);
                if (player == null && manager != null && !manager.IsListening)
                {
                    if (clientMode)
                    {
                        // UTP exhausts retries instantly on a closed UDP port — retry StartClient
                        nextClientRetry -= Time.deltaTime;
                        if (nextClientRetry <= 0f)
                        {
                            NGLog.Info(
                                "Bootstrap",
                                "Client not connected; retrying StartClient..."
                            );
                            manager.StartClient();
                            nextClientRetry = clientRetryInterval;
                        }
                    }
                    else
                    {
                        player = GameObject.FindGameObjectWithTag(m_PlayerTag);
                    }
                }

                if (player == null)
                {
                    playerWaitTimeout -= Time.deltaTime;
                    yield return null;
                }
            }

            if (player == null && !clientMode)
            {
                NGLog.Warn(
                    "Bootstrap",
                    "Player missing after timeout, spawning fallback (host only)"
                );
                player = SpawnFallbackPlayer(manager);
            }
            else if (player == null && clientMode)
            {
                NGLog.Error("Bootstrap", "Client player was not spawned by host after timeout.");
            }

            if (player != null)
            {
                _ = AlignPlayerToSpawnPoint(manager, player);
                ConfigureLocalPlayerNetworking(manager, player);
                EnableLocalInput(player);
            }

            List<GameObject> npcObjects =
                m_NpcBootstrap != null
                    ? m_NpcBootstrap.CollectAndPrioritizeNpcs(m_PrimaryNpc)
                    : new List<GameObject>();
            if (npcObjects.Count > 0)
            {
                m_PrimaryNpc = npcObjects[0];
            }

            if (player != null)
            {
                NGLog.Info("Bootstrap", "Configuring player input/network");
                if (m_NpcBootstrap != null)
                {
                    m_NpcBootstrap.DisableLlmAgent(player);
                    m_NpcBootstrap.ConfigureDialogueUiParticipants(player, m_PrimaryNpc);
                }
                ConfigureLocalPlayerNetworking(manager, player);
                EnsureCombatHealth(player);
                if (m_CameraManager != null)
                    m_CameraManager.ConfigureCamera(player);
                EnableLocalInput(player);

                if (clientMode)
                {
                    ConfigureClientAutoAuth(player);
                }
                else
                {
                    ConfigureLocalAuth(player);
                }
            }
            else
            {
                if (clientMode)
                {
                    ConfigureClientAutoAuth(null);
                }
                else
                {
                    ConfigureLocalAuth(null);
                }
            }

            yield return StartCoroutine(EnsureRuntimeBindings(manager));

            if (m_CameraManager != null)
            {
                m_CameraManager.StartMonitoring(manager);
            }

            NGLog.Info("Bootstrap", "Initialize complete");
        }

        /// <summary>
        /// Configures UnityTransport for the current runtime.
        /// WebGL and dedicated server use WebSockets; native/editor builds use UDP.
        /// </summary>
        private static void ConfigureWebSocketTransport(NetworkManager manager)
        {
            var transport = manager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                bool shouldUseWebSockets =
#if (UNITY_SERVER && !UNITY_EDITOR) || (UNITY_WEBGL && !UNITY_EDITOR)
                    true;
#else
                    false;
#endif

                if (transport.UseWebSockets != shouldUseWebSockets)
                {
                    transport.UseWebSockets = shouldUseWebSockets;
                    NGLog.Info(
                        "Bootstrap",
                        shouldUseWebSockets
                            ? "WebSocket transport enabled"
                            : "UDP transport enabled"
                    );
                }
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// Reads server IP and port from the page URL query string.
        /// Expected format: https://your-site.com/game?server=1.2.3.4&port=7777
        /// Falls back to 127.0.0.1:7777 if not supplied.
        /// </summary>
        private static void ReadServerAddressFromUrl(NetworkManager manager)
        {
            try
            {
                string url = Application.absoluteURL;
                if (string.IsNullOrEmpty(url))
                    return;

                int queryStart = url.IndexOf('?');
                if (queryStart < 0)
                    return;

                string query = url.Substring(queryStart + 1);
                string serverIp = "127.0.0.1";
                ushort port = 7777;

                foreach (string pair in query.Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq < 0)
                        continue;
                    string key = pair.Substring(0, eq);
                    string val = pair.Substring(eq + 1);
                    if (string.Equals(key, "server", StringComparison.OrdinalIgnoreCase))
                        serverIp = val;
                    else if (string.Equals(key, "port", StringComparison.OrdinalIgnoreCase))
                        if (ushort.TryParse(val, out ushort p))
                            port = p;
                }

                var transport = manager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData(serverIp, port);
                    NGLog.Info(
                        "Bootstrap",
                        NGLog.Format("Server address from URL", ("ip", serverIp), ("port", port))
                    );
                }
            }
            catch (Exception ex)
            {
                NGLog.Warn("Bootstrap", $"Failed to read server address from URL: {ex.Message}");
            }
        }
#endif

        private IEnumerator WaitForEditorHostEndpoint(NetworkManager manager)
        {
#if UNITY_EDITOR
            if (!IsEditorVirtualPlayerClone())
            {
                yield break;
            }

            var transport = manager?.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport == null)
            {
                yield break;
            }

            float timeout = EditorClientEndpointWaitSeconds;
            while (timeout > 0f)
            {
                if (TryReadEditorHostEndpoint(out string hostAddress, out ushort hostPort))
                {
                    transport.SetConnectionData(hostAddress, hostPort);
                    NGLog.Info(
                        "Bootstrap",
                        NGLog.Format(
                            "Configured client endpoint from host session",
                            ("address", hostAddress),
                            ("port", hostPort)
                        )
                    );
                    yield break;
                }

                timeout -= Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : 0.1f;
                yield return null;
            }

            NGLog.Warn(
                "Bootstrap",
                "Host endpoint file not ready; using serialized/default client transport endpoint."
            );
#else
            yield break;
#endif
        }

        private static string GetEditorHostEndpointFilePath()
        {
#if UNITY_EDITOR
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetworkGame"
            );
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
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
#endif
        }

        private static void PersistEditorHostEndpoint(NetworkManager manager)
        {
#if UNITY_EDITOR
            if (manager == null)
            {
                return;
            }

            try
            {
                int port = ResolveConfiguredListenPort(manager);
                if (port <= 0 || port > 65535)
                {
                    return;
                }

                string path = GetEditorHostEndpointFilePath();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, $"127.0.0.1:{port}");
            }
            catch
            {
                // Best effort only; client will fall back to serialized transport settings.
            }
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
                {
                    return false;
                }

                string content = File.ReadAllText(path)?.Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return false;
                }

                string[] parts = content.Split(':');
                if (parts.Length != 2)
                {
                    return false;
                }

                string parsedAddress = string.IsNullOrWhiteSpace(parts[0])
                    ? "127.0.0.1"
                    : parts[0].Trim();
                if (!ushort.TryParse(parts[1], out ushort parsedPort) || parsedPort == 0)
                {
                    return false;
                }

                address = parsedAddress;
                port = parsedPort;
                return true;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }

        private IEnumerator EnsureAuthGate()
        {
            m_AuthGateSatisfied = false;
            bool allowUnauthenticatedStart = ShouldAllowUnauthenticatedNetworkStart();
            LocalPlayerAuthService authService = LocalPlayerAuthService.EnsureInstance();
            if (authService == null)
            {
                m_AuthGateSatisfied =
                    !m_BlockNetworkStartUntilAuthenticated || allowUnauthenticatedStart;
                yield break;
            }

            EnsureAuthLoginUiAvailable();

            if (authService.HasCurrentPlayer)
            {
                authService.EnsurePromptContextInitialized();
                m_AuthGateSatisfied = true;
                yield break;
            }

            if (!m_RequireExplicitLoginEachSession)
            {
                authService.EnsureLoggedIn();
            }

            float timeout = Mathf.Max(0.5f, m_AuthGateTimeoutSeconds);
            while (!authService.HasCurrentPlayer && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (!authService.HasCurrentPlayer && m_BlockNetworkStartUntilAuthenticated)
            {
                if (allowUnauthenticatedStart)
                {
                    NGLog.Warn(
                        "Bootstrap",
                        "Continuing network start without login (editor multiplayer/client mode)."
                    );
                }
                else
                {
                    NGLog.Info("Bootstrap", "Waiting for login before starting network...");
                    while (!authService.HasCurrentPlayer)
                    {
                        yield return null;
                    }
                }
            }

            if (authService.HasCurrentPlayer)
            {
                authService.EnsurePromptContextInitialized();
            }
            m_AuthGateSatisfied = true;
        }

        private IEnumerator EnsureRuntimeBindings(NetworkManager manager)
        {
            float duration = 10f;
            float pollInterval = 0.33f;
            bool cameraBoundAtLeastOnce = false;

            GameObject lastBoundPlayer = null;

            while (duration > 0f)
            {
                GameObject player = ResolveLocalPlayer(manager);
                if (player != null)
                {
                    if (player != lastBoundPlayer)
                    {
                        NGLog.Info(
                            "Bootstrap",
                            $"Re-verifying bindings for player '{player.name}'"
                        );
                        if (m_NpcBootstrap != null)
                        {
                            m_NpcBootstrap.DisableLlmAgent(player);
                            m_NpcBootstrap.ConfigureDialogueUiParticipants(player, m_PrimaryNpc);
                        }
                        _ = AlignPlayerToSpawnPoint(manager, player);
                        ConfigureLocalPlayerNetworking(manager, player);
                        EnsureCombatHealth(player);
                        EnableLocalInput(player);
                        if (IsClientMode())
                        {
                            ConfigureClientAutoAuth(player);
                        }
                        else
                        {
                            ConfigureLocalAuth(player);
                        }

                        lastBoundPlayer = player;
                    }

                    if (m_CameraManager != null)
                        cameraBoundAtLeastOnce |= m_CameraManager.ConfigureCamera(player);
                }

                duration -= pollInterval;
                yield return new WaitForSeconds(pollInterval);
            }

            if (!cameraBoundAtLeastOnce)
            {
                NGLog.Warn("Bootstrap", "Could not bind camera during initialization period.");
            }
        }

        private GameObject ResolveLocalPlayer(NetworkManager manager)
        {
            if (manager != null)
            {
                GameObject localPlayer = manager.LocalClient?.PlayerObject?.gameObject;
                if (localPlayer != null)
                {
                    return localPlayer;
                }

                GameObject ownedTaggedPlayer = ResolveTaggedPlayerCandidate(manager);
                if (ownedTaggedPlayer != null)
                {
                    return ownedTaggedPlayer;
                }

                if (manager.IsListening)
                {
                    return null;
                }
            }

            return ResolveTaggedPlayerCandidate(null);
        }

        private GameObject ResolveTaggedPlayerCandidate(NetworkManager manager)
        {
            if (string.IsNullOrWhiteSpace(m_PlayerTag))
            {
                return null;
            }

            GameObject[] candidates = GameObject.FindGameObjectsWithTag(m_PlayerTag);
            if (candidates == null || candidates.Length == 0)
            {
                return null;
            }

            if (manager != null)
            {
                ulong localClientId = manager.LocalClientId;
                foreach (GameObject candidate in candidates)
                {
                    if (candidate == null || !candidate.activeInHierarchy)
                    {
                        continue;
                    }

                    NetworkObject networkObject = candidate.GetComponent<NetworkObject>();
                    if (
                        networkObject != null
                        && networkObject.IsSpawned
                        && networkObject.OwnerClientId == localClientId
                    )
                    {
                        return candidate;
                    }
                }
            }

            if (candidates.Length == 1 && candidates[0] != null && candidates[0].activeInHierarchy)
            {
                return candidates[0];
            }

            return null;
        }

        private void EnableLocalInput(GameObject player)
        {
            if (player == null)
            {
                return;
            }

            // Safety: only enable input on the local owner's player object.
            var netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned && !netObj.IsOwner)
            {
                NGLog.Debug("Bootstrap", "Skipped EnableLocalInput — not local owner");
                return;
            }

            var input = player.GetComponent<PlayerInput>();
            if (input != null)
            {
                input.enabled = true;
                input.ActivateInput();
                if (input.currentActionMap == null || input.currentActionMap.name != "Player")
                {
                    input.SwitchCurrentActionMap("Player");
                }
                input.currentActionMap?.Enable();
            }

            var starterInputs =
                player.GetComponent<Network_Game.ThirdPersonController.StarterAssetsInputs>();
            if (starterInputs != null)
            {
                starterInputs.enabled = true;
                starterInputs.cursorLocked = true;
                starterInputs.cursorInputForLook = true;
            }

            var controller =
                player.GetComponent<Network_Game.ThirdPersonController.ThirdPersonController>();
            if (controller != null)
            {
                controller.enabled = true;
            }

            var flyController =
                player.GetComponent<Network_Game.ThirdPersonController.FlyModeController>();
            if (flyController != null)
            {
                flyController.enabled = true;
                flyController.SetFlyMode(false);
            }
            NGLog.Debug("Bootstrap", "Local input enabled");
        }

        private void ConfigureLocalPlayerNetworking(NetworkManager manager, GameObject player)
        {
            if (manager == null || player == null)
            {
                return;
            }

            var netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && manager.IsServer && !netObj.IsOwner)
            {
                NGLog.Info("Bootstrap", "Assigning local ownership to player");
                netObj.ChangeOwnership(manager.LocalClientId);
            }

            var netTransform = player.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (netTransform != null)
            {
                if (
                    netTransform.AuthorityMode
                    != Unity.Netcode.Components.NetworkTransform.AuthorityModes.Owner
                )
                {
                    NGLog.Warn("Bootstrap", "Server Authority detected!");
                } // Removed assignment
                // Assignment removed for NGO compliance. Authority set in Prefab.
                if (netObj != null && netObj.IsOwner)
                {
                    netTransform.Interpolate = false;
                    netTransform.SlerpPosition = false;
                }
            }

            var netAnimator = player.GetComponent<Unity.Netcode.Components.NetworkAnimator>();
            if (netAnimator != null)
            {
                netAnimator.AuthorityMode = Unity
                    .Netcode
                    .Components
                    .NetworkAnimator
                    .AuthorityModes
                    .Owner;

                if (netAnimator.Animator != null && netAnimator.Animator.applyRootMotion)
                {
                    netAnimator.Animator.applyRootMotion = false;
                }
            }
            NGLog.Debug("Bootstrap", "Network authority configured");
        }

        private void EnsureCombatHealth(GameObject player)
        {
            if (player == null)
            {
                return;
            }

            if (player.GetComponent<CombatHealth>() != null)
            {
                return;
            }

            player.AddComponent<CombatHealth>();
            NGLog.Info(
                "Bootstrap",
                NGLog.Format("Added CombatHealth to player", ("player", player.name))
            );
        }

        private bool IsClientMode()
        {
            if (m_ForceClientMode)
            {
                return true;
            }

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-client", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (IsEditorVirtualPlayerClone())
            {
                return true;
            }

#if UNITY_EDITOR
            try
            {
                var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.Tags;
                if (tags != null)
                {
                    for (int i = 0; i < tags.Count; i++)
                    {
                        if (
                            !string.IsNullOrEmpty(tags[i])
                            && tags[i].Contains(m_ClientModeTag, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // CurrentPlayer API unavailable (MPPM not configured); ignore.
            }
#endif
            return false;
        }

        private bool ShouldAllowUnauthenticatedNetworkStart()
        {
            return IsClientMode() || IsEditorMultiplayerSession();
        }

        private static bool IsEditorMultiplayerSession()
        {
#if UNITY_EDITOR
            if (IsEditorVirtualPlayerClone())
            {
                return true;
            }

            try
            {
                var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.Tags;
                return tags != null && tags.Count > 0;
            }
            catch
            {
                // MPPM API unavailable or not active.
            }
#endif
            return false;
        }

        private static bool IsEditorVirtualPlayerClone()
        {
#if UNITY_EDITOR
            try
            {
                string normalizedDataPath = Application.dataPath.Replace('\\', '/');
                return normalizedDataPath.IndexOf(
                        "/Library/VP/",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0;
            }
            catch
            {
                // Best effort only.
            }
#endif
            return false;
        }

        private void EnsureDebugAssistant(bool clientMode)
        {
            if (!m_AutoCreateLlmDebugAssistant)
            {
                return;
            }

            if (clientMode && !m_EnableDebugAssistantOnClients)
            {
                return;
            }

#if UNITY_2023_1_OR_NEWER
            if (
                FindAnyObjectByType<Network_Game.Diagnostics.LlmDebugAssistant>(
                    FindObjectsInactive.Exclude
                ) != null
            )
#else
            if (FindObjectOfType<Network_Game.Diagnostics.LlmDebugAssistant>() != null)
#endif
            {
                return;
            }

            var debugGo = new GameObject("DebugSystem");
            debugGo.AddComponent<Network_Game.Diagnostics.LlmDebugAssistant>();
            DontDestroyOnLoad(debugGo);
            NGLog.Info(
                "Bootstrap",
                NGLog.Format(
                    "Initialized LlmDebugAssistant",
                    ("mode", clientMode ? "client" : "host")
                )
            );
        }

        private static bool IsConfiguredListenPortInUse(NetworkManager manager, out int port)
        {
            port = ResolveConfiguredListenPort(manager);
            if (port <= 0)
            {
                return false;
            }

            return !CanBindUdpPort(port);
        }

        private static int ResolveConfiguredListenPort(NetworkManager manager)
        {
            const int fallbackPort = 7777;
            object transport = manager?.NetworkConfig?.NetworkTransport;
            if (transport == null)
            {
                return fallbackPort;
            }

            try
            {
                PropertyInfo connectionDataProperty = transport
                    .GetType()
                    .GetProperty("ConnectionData");
                if (connectionDataProperty == null)
                {
                    return fallbackPort;
                }

                object connectionData = connectionDataProperty.GetValue(transport);
                if (connectionData == null)
                {
                    return fallbackPort;
                }

                Type connectionType = connectionData.GetType();
                PropertyInfo portProperty = connectionType.GetProperty("Port");
                if (
                    portProperty != null
                    && int.TryParse(
                        portProperty.GetValue(connectionData)?.ToString(),
                        out int propertyPort
                    )
                    && propertyPort > 0
                    && propertyPort <= 65535
                )
                {
                    return propertyPort;
                }

                FieldInfo portField = connectionType.GetField("Port");
                if (
                    portField != null
                    && int.TryParse(
                        portField.GetValue(connectionData)?.ToString(),
                        out int fieldPort
                    )
                    && fieldPort > 0
                    && fieldPort <= 65535
                )
                {
                    return fieldPort;
                }
            }
            catch
            {
                // Reflection fallback is best effort only.
            }

            return fallbackPort;
        }

        private bool TryStartHostWithPortFallback(NetworkManager manager)
        {
            if (manager == null)
            {
                return false;
            }

            NGLog.Info("Bootstrap", "Attempting to start as Host...");
            if (manager.StartHost())
            {
                PersistEditorHostEndpoint(manager);
                NGLog.Info(
                    "Bootstrap",
                    NGLog.Format("Host started", ("port", ResolveConfiguredListenPort(manager)))
                );
                return true;
            }

            if (!m_TryHostPortFallbackOnStartFailure)
            {
                return false;
            }

            var transport = manager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport == null)
            {
                return false;
            }

            int startPort = Mathf.Clamp(m_HostFallbackPortStart, 1024, 65535);
            int attempts = Mathf.Clamp(m_HostFallbackPortAttempts, 1, 32);
            for (int i = 0; i < attempts; i++)
            {
                int candidatePort = startPort + i;
                if (candidatePort < 1 || candidatePort > 65535)
                {
                    break;
                }

                if (!CanBindUdpPort(candidatePort))
                {
                    continue;
                }

                if (manager.IsListening)
                {
                    manager.Shutdown();
                }

                transport.SetConnectionData("127.0.0.1", (ushort)candidatePort, "0.0.0.0");
                NGLog.Warn(
                    "Bootstrap",
                    NGLog.Format("Retrying host start on fallback port", ("port", candidatePort))
                );

                if (manager.StartHost())
                {
                    PersistEditorHostEndpoint(manager);
                    NGLog.Info(
                        "Bootstrap",
                        NGLog.Format("Host started on fallback port", ("port", candidatePort))
                    );
                    return true;
                }
            }

            return false;
        }

        private static bool CanBindUdpPort(int port)
        {
#if UNITY_WEBGL
            // WebGL has no raw socket access — always report as available
            // (WebGL is always a pure client, so this check is irrelevant).
            return true;
#else
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    ExclusiveAddressUse = true,
                };
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                socket?.Close();
            }
#endif
        }

        private string GenerateClientPlayerName()
        {
            string baseName = "player";
#if UNITY_EDITOR
            try
            {
                var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.Tags;
                if (tags != null && tags.Count > 0 && !string.IsNullOrEmpty(tags[0]))
                {
                    baseName = tags[0].Replace(" ", "_").ToLowerInvariant();
                }
            }
            catch
            {
                // Ignore if MPPM API is unavailable.
            }
#endif
            return $"{baseName}_{UnityEngine.Random.Range(100, 999)}";
        }

        private void ConfigureLocalAuth(GameObject player)
        {
            LocalPlayerAuthService authService = LocalPlayerAuthService.EnsureInstance();
            if (authService == null)
            {
                return;
            }

            authService.AttachLocalPlayer(player);
            EnsureAuthLoginUiAvailable();
        }

        private void ConfigureClientAutoAuth(GameObject player)
        {
            LocalPlayerAuthService authService = LocalPlayerAuthService.EnsureInstance();
            if (authService == null)
            {
                return;
            }

            authService.AttachLocalPlayer(player);
            if (m_RequireExplicitLoginEachSession)
            {
                EnsureAuthLoginUiAvailable();
                return;
            }

            string clientName = GenerateClientPlayerName();
            authService.Login(clientName);
            NGLog.Info("Bootstrap", NGLog.Format("Client auto-auth", ("name_id", clientName)));
        }

        private GameObject SpawnFallbackPlayer(NetworkManager manager)
        {
            if (manager == null || manager.NetworkConfig == null)
            {
                return null;
            }

            var prefab = manager.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                return null;
            }

            if (m_PlayerSpawnPoint == null)
            {
                var spawn = GameObject.Find("SpawnPoint");
                if (spawn != null)
                {
                    m_PlayerSpawnPoint = spawn.transform;
                }
            }

            Vector3 position =
                m_PlayerSpawnPoint != null ? m_PlayerSpawnPoint.position : Vector3.zero;
            Quaternion rotation =
                m_PlayerSpawnPoint != null ? m_PlayerSpawnPoint.rotation : Quaternion.identity;
            var instance = Instantiate(prefab, position, rotation);
            var netObj = instance.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.SpawnAsPlayerObject(manager.LocalClientId, true);
            }

            NGLog.Info("Bootstrap", "Spawned fallback player");
            return instance;
        }

        private bool AlignPlayerToSpawnPoint(NetworkManager manager, GameObject player)
        {
            if (!m_AlignLocalPlayerToSpawnPoint || player == null)
            {
                return false;
            }

            ResolveSpawnPointReference();
            if (m_PlayerSpawnPoint == null)
            {
                return false;
            }

            NetworkObject netObj = player.GetComponent<NetworkObject>();
            ulong playerId = netObj != null ? netObj.NetworkObjectId : 0;
            if (playerId != 0 && m_SpawnAlignedPlayerIds.Contains(playerId))
            {
                return true;
            }

            bool canMove =
                netObj == null || netObj.IsOwner || (manager != null && manager.IsServer);
            if (!canMove)
            {
                return false;
            }

            CharacterController characterController = player.GetComponent<CharacterController>();
            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }

            player.transform.SetPositionAndRotation(
                m_PlayerSpawnPoint.position,
                m_PlayerSpawnPoint.rotation
            );

            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }

            if (playerId != 0)
            {
                m_SpawnAlignedPlayerIds.Add(playerId);
            }

            NGLog.Info(
                "Bootstrap",
                NGLog.Format(
                    "Aligned player to spawn point",
                    ("player", player.name),
                    ("spawn", m_PlayerSpawnPoint.name)
                )
            );
            return true;
        }

        private void ResolveSpawnPointReference()
        {
            if (m_PlayerSpawnPoint != null)
            {
                return;
            }

            GameObject spawnByName = GameObject.Find("SpawnPoint");
            if (spawnByName != null)
            {
                m_PlayerSpawnPoint = spawnByName.transform;
                return;
            }

            try
            {
                GameObject spawnByTag = GameObject.FindGameObjectWithTag("SpawnPoint");
                if (spawnByTag != null)
                {
                    m_PlayerSpawnPoint = spawnByTag.transform;
                }
            }
            catch (UnityException)
            {
                // Scene does not define SpawnPoint tag; name lookup fallback is sufficient.
            }
        }

        private void OnConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response
        )
        {
            ResolveSpawnPointReference();
            Vector3 spawnPos =
                m_PlayerSpawnPoint != null ? m_PlayerSpawnPoint.position : Vector3.zero;
            Quaternion spawnRot =
                m_PlayerSpawnPoint != null ? m_PlayerSpawnPoint.rotation : Quaternion.identity;

            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = spawnPos;
            response.Rotation = spawnRot;

            NGLog.Info(
                "Bootstrap",
                NGLog.Format(
                    "Connection approved — spawning at SpawnPoint",
                    ("clientId", request.ClientNetworkId),
                    ("position", spawnPos)
                )
            );
        }

        private static void EnsureAuthLoginUiAvailable()
        {
            Network_Game.UI.Login.PlayerLoginController[] loginControllers =
                Resources.FindObjectsOfTypeAll<Network_Game.UI.Login.PlayerLoginController>();
            for (int i = 0; i < loginControllers.Length; i++)
            {
                Network_Game.UI.Login.PlayerLoginController loginController = loginControllers[i];
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
    }
}
