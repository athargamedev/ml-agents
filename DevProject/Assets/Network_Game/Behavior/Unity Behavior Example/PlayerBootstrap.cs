using System;
using System.Collections;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Handles player lifecycle: waiting for spawn, fallback spawning, alignment to spawn point.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerBootstrap : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] public string m_PlayerTag = "Player";
        [SerializeField] public Transform m_PlayerSpawnPoint;
        [SerializeField] public bool m_AlignLocalPlayerToSpawnPoint = true;

        [Header("Timeouts")]
        [SerializeField] private float m_ClientWaitTimeout = 25f; // Editor default
        [SerializeField] private float m_BuildClientWaitTimeout = 15f;
        [SerializeField] private float m_HostWaitTimeout = 5f;

        private NetworkManager m_Manager;
        private bool m_IsClientMode;
        private readonly HashSet<ulong> m_SpawnAlignedPlayerIds = new HashSet<ulong>();

        private void Awake()
        {
            m_Manager = NetworkManager.Singleton;
            ResolveSpawnPointReference();
        }

        private void OnEnable()
        {
            var events = NetworkBootstrapEvents.Instance;
            if (events != null)
            {
                events.OnNetworkReady += OnNetworkReady;
                events.OnClientModeDetermined += OnClientModeDetermined;
            }
        }

        private void OnDisable()
        {
            var events = NetworkBootstrapEvents.Instance;
            if (events != null)
            {
                events.OnNetworkReady -= OnNetworkReady;
                events.OnClientModeDetermined -= OnClientModeDetermined;
            }
        }

        private void OnNetworkReady(NetworkManager manager)
        {
            StartCoroutine(WaitForPlayer());
        }

        private void OnClientModeDetermined(bool isClient)
        {
            m_IsClientMode = isClient;
        }

        private IEnumerator WaitForPlayer()
        {
            float timeout = GetTimeout();
            float retryInterval = 3f;
            float nextRetry = retryInterval;
            GameObject player = null;

            while (player == null && timeout > 0)
            {
                player = ResolveLocalPlayer();

                if (player == null && m_Manager != null && !m_Manager.IsListening)
                {
                    if (m_IsClientMode)
                    {
                        nextRetry -= Time.deltaTime;
                        if (nextRetry <= 0f && m_Manager.StartClient())
                        {
                            NGLog.Info("PlayerBootstrap", "Client disconnected, retrying StartClient");
                            nextRetry = retryInterval;
                        }
                    }
                    else
                    {
                        player = GameObject.FindGameObjectWithTag(m_PlayerTag);
                    }
                }

                if (player == null)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            // Handle spawn failure
            if (player == null && !m_IsClientMode)
            {
                NGLog.Warn("PlayerBootstrap", "Player missing on host, spawning fallback");
                player = SpawnFallbackPlayer();
            }
            else if (player == null && m_IsClientMode)
            {
                NGLog.Error("PlayerBootstrap", "Client player was not spawned by host after timeout.");
                NetworkBootstrapEvents.Instance.PublishLocalPlayerReady(null);
                yield break;
            }

            if (player != null)
            {
                NetworkBootstrapEvents.Instance.PublishLocalPlayerSpawned(player);

                // Align to spawn point
                if (m_AlignLocalPlayerToSpawnPoint)
                {
                    AlignPlayerToSpawnPoint(player);
                }

                // Configure networking
                ConfigureLocalPlayerNetworking(player);
                EnableLocalInput(player);

                NetworkBootstrapEvents.Instance.PublishLocalPlayerReady(player);
                NGLog.Info("PlayerBootstrap", $"Local player ready: {player.name}");
            }
        }

        private float GetTimeout()
        {
            if (m_IsClientMode)
                return Application.isEditor ? m_ClientWaitTimeout : m_BuildClientWaitTimeout;
            return m_HostWaitTimeout;
        }

        private GameObject ResolveLocalPlayer()
        {
            if (m_Manager != null)
            {
                // Try NetworkManager's local client first
                GameObject localPlayer = m_Manager.LocalClient?.PlayerObject?.gameObject;
                if (localPlayer != null)
                    return localPlayer;

                // Try finding by ownership
                if (m_Manager.IsListening || m_IsClientMode)
                {
                    return ResolveTaggedPlayerCandidate();
                }
            }

            return ResolveTaggedPlayerCandidate();
        }

        private GameObject ResolveTaggedPlayerCandidate()
        {
            if (string.IsNullOrWhiteSpace(m_PlayerTag))
                return null;

            GameObject[] candidates = GameObject.FindGameObjectsWithTag(m_PlayerTag);
            if (candidates == null || candidates.Length == 0)
                return null;

            if (m_Manager != null)
            {
                ulong localClientId = m_Manager.LocalClientId;
                foreach (GameObject candidate in candidates)
                {
                    if (candidate == null || !candidate.activeInHierarchy)
                        continue;

                    NetworkObject networkObject = candidate.GetComponent<NetworkObject>();
                    if (networkObject != null && networkObject.IsSpawned && networkObject.OwnerClientId == localClientId)
                        return candidate;
                }
            }

            // Fallback: if only one candidate
            if (candidates.Length == 1 && candidates[0] != null && candidates[0].activeInHierarchy)
                return candidates[0];

            return null;
        }

        private GameObject SpawnFallbackPlayer()
        {
            if (m_Manager == null || m_Manager.NetworkConfig == null)
                return null;

            var prefab = m_Manager.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                NGLog.Error("PlayerBootstrap", "PlayerPrefab not configured in NetworkManager!");
                return null;
            }

            ResolveSpawnPointReference();
            Vector3 position = m_PlayerSpawnPoint?.position ?? Vector3.zero;
            Quaternion rotation = m_PlayerSpawnPoint?.rotation ?? Quaternion.identity;

            var instance = Instantiate(prefab, position, rotation);
            var netObj = instance.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.SpawnAsPlayerObject(m_Manager.LocalClientId, true);
            }

            NGLog.Info("PlayerBootstrap", "Spawned fallback player");
            return instance;
        }

        private void AlignPlayerToSpawnPoint(GameObject player)
        {
            if (player == null) return;

            ResolveSpawnPointReference();
            if (m_PlayerSpawnPoint == null) return;

            NetworkObject netObj = player.GetComponent<NetworkObject>();
            ulong playerId = netObj != null ? netObj.NetworkObjectId : 0;
            if (playerId != 0 && m_SpawnAlignedPlayerIds.Contains(playerId))
                return;

            bool canMove = netObj == null || netObj.IsOwner || (m_Manager != null && m_Manager.IsServer);
            if (!canMove)
                return;

            CharacterController characterController = player.GetComponent<CharacterController>();
            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
                characterController.enabled = false;

            player.transform.SetPositionAndRotation(m_PlayerSpawnPoint.position, m_PlayerSpawnPoint.rotation);

            if (controllerWasEnabled)
                characterController.enabled = true;

            if (playerId != 0)
                m_SpawnAlignedPlayerIds.Add(playerId);

            NGLog.Info("PlayerBootstrap", $"Aligned player to spawn point: {m_PlayerSpawnPoint.name}");
        }

        private void ConfigureLocalPlayerNetworking(GameObject player)
        {
            if (m_Manager == null || player == null) return;

            var netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && m_Manager.IsServer && !netObj.IsOwner)
            {
                NGLog.Info("PlayerBootstrap", "Assigning local ownership to player");
                netObj.ChangeOwnership(m_Manager.LocalClientId);
            }

            var netTransform = player.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (netTransform != null && netObj != null && netObj.IsOwner)
            {
                netTransform.Interpolate = false;
                netTransform.SlerpPosition = false;
            }

            var netAnimator = player.GetComponent<Unity.Netcode.Components.NetworkAnimator>();
            if (netAnimator != null)
            {
                netAnimator.AuthorityMode = Unity.Netcode.Components.NetworkAnimator.AuthorityModes.Owner;
                if (netAnimator.Animator != null && netAnimator.Animator.applyRootMotion)
                    netAnimator.Animator.applyRootMotion = false;
            }
        }

        private void EnableLocalInput(GameObject player)
        {
            if (player == null) return;

            var netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned && !netObj.IsOwner)
            {
                NGLog.Debug("PlayerBootstrap", "Skipped EnableLocalInput — not local owner");
                return;
            }

            var input = player.GetComponent<PlayerInput>();
            if (input != null)
            {
                input.enabled = true;
                input.ActivateInput();
                if (input.currentActionMap == null || input.currentActionMap.name != "Player")
                    input.SwitchCurrentActionMap("Player");
                input.currentActionMap?.Enable();
            }

            var starterInputs = player.GetComponent<Network_Game.ThirdPersonController.StarterAssetsInputs>();
            if (starterInputs != null)
            {
                starterInputs.enabled = true;
                starterInputs.cursorLocked = true;
                starterInputs.cursorInputForLook = true;
            }

            var controller = player.GetComponent<Network_Game.ThirdPersonController.ThirdPersonController>();
            if (controller != null)
                controller.enabled = true;

            var flyController = player.GetComponent<Network_Game.ThirdPersonController.FlyModeController>();
            if (flyController != null)
            {
                flyController.enabled = true;
                flyController.SetFlyMode(false);
            }

            NGLog.Debug("PlayerBootstrap", "Local input enabled");
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
    }
}
