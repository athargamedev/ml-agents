using System;
using System.Collections;
using System.Collections.Generic;
using Network_Game.Behavior;
using Network_Game.Combat;
using Network_Game.Diagnostics;
using Network_Game.Dialogue;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Handles runtime wiring after player is ready: camera, dialogue, NPCs, combat.
    /// Runs once after local player is available.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class RuntimeBinder : MonoBehaviour
    {
        [Header("NPC Configuration")]
        [SerializeField] public GameObject m_PrimaryNpc;
        [SerializeField] private string m_NpcTag = "NPC";

        [Header("Diagnostics")]
        [SerializeField] public bool m_AutoCreateLlmDebugAssistant;
        [SerializeField] public bool m_EnableDebugAssistantOnClients;

        private SceneCameraManager m_CameraManager;
        private NPCAgentBootstrap m_NpcBootstrap;
        private AuthBootstrap m_AuthBootstrap;
        private GameObject m_LocalPlayer;
        private Coroutine m_BindRoutine;

        private void Awake()
        {
            m_CameraManager = GetComponent<SceneCameraManager>();
            if (m_CameraManager == null)
                m_CameraManager = gameObject.AddComponent<SceneCameraManager>();

            m_NpcBootstrap = GetComponent<NPCAgentBootstrap>();
            if (m_NpcBootstrap == null)
                m_NpcBootstrap = gameObject.AddComponent<NPCAgentBootstrap>();

            m_AuthBootstrap = GetComponent<AuthBootstrap>();
        }

        private void OnEnable()
        {
            var events = NetworkBootstrapEvents.Instance;
            if (events != null)
            {
                events.OnLocalPlayerReady += OnLocalPlayerReady;
                events.OnClientModeDetermined += OnClientModeDetermined;
            }
        }

        private void OnDisable()
        {
            if (m_BindRoutine != null)
            {
                StopCoroutine(m_BindRoutine);
                m_BindRoutine = null;
            }

            if (m_CameraManager != null)
                m_CameraManager.StopMonitoring();

            var events = NetworkBootstrapEvents.Instance;
            if (events != null)
            {
                events.OnLocalPlayerReady -= OnLocalPlayerReady;
                events.OnClientModeDetermined -= OnClientModeDetermined;
            }
        }

        private void OnClientModeDetermined(bool isClient)
        {
            if (m_AutoCreateLlmDebugAssistant && (isClient || m_EnableDebugAssistantOnClients))
            {
                EnsureDebugAssistant();
            }
        }

        private void OnLocalPlayerReady(GameObject player)
        {
            if (player == null)
            {
                NGLog.Warn("RuntimeBinder", "Local player is null, skipping bindings");
                return;
            }

            m_LocalPlayer = player;
            if (m_BindRoutine != null)
            {
                StopCoroutine(m_BindRoutine);
            }

            m_BindRoutine = StartCoroutine(BindAll());
        }

        private IEnumerator BindAll()
        {
            if (m_LocalPlayer == null)
            {
                yield break;
            }

            if (m_AuthBootstrap != null)
            {
                m_AuthBootstrap.AttachAuthToPlayer(m_LocalPlayer);
            }

            // Ensure combat health
            EnsureCombatHealth(m_LocalPlayer);

            // Configure camera (blocking wait)
            bool cameraBound = false;
            if (m_CameraManager != null)
            {
                cameraBound = m_CameraManager.ConfigureCamera(m_LocalPlayer);
                m_CameraManager.StartMonitoring(NetworkManager.Singleton);
            }

            // Collect and wire NPCs
            CollectAndWireNpcs();

            // Rebind dialogue participants
            RebindDialogue();

            NGLog.Info("RuntimeBinder", "Core runtime bindings complete");

            yield return WaitForAuthAndFinalize();
            m_BindRoutine = null;
        }

        private IEnumerator WaitForAuthAndFinalize()
        {
            if (m_AuthBootstrap == null)
            {
                yield break;
            }

            while (m_AuthBootstrap != null && !m_AuthBootstrap.IsAuthenticated)
            {
                yield return null;
            }

            if (m_AuthBootstrap == null || m_LocalPlayer == null)
            {
                yield break;
            }

            m_AuthBootstrap.AttachAuthToPlayer(m_LocalPlayer);
            RebindDialogue();

            NGLog.Info("RuntimeBinder", "Auth-dependent runtime bindings complete");
        }

        private void EnsureCombatHealth(GameObject player)
        {
            if (player == null) return;
            if (player.GetComponent<CombatHealth>() != null) return;

            player.AddComponent<CombatHealth>();
            NGLog.Info("RuntimeBinder", $"Added CombatHealth to {player.name}");
        }

        private void CollectAndWireNpcs()
        {
            if (m_NpcBootstrap == null) return;

            // Collect NPCs
            var npcObjects = m_NpcBootstrap.CollectAndPrioritizeNpcs(m_PrimaryNpc);
            if (npcObjects.Count > 0)
            {
                m_PrimaryNpc = npcObjects[0];
            }

            // Disable LLM agent on player (single owner enforcement)
            m_NpcBootstrap.DisableLlmAgent(m_LocalPlayer);

            // Configure dialogue UI participants
            m_NpcBootstrap.ConfigureDialogueUiParticipants(m_LocalPlayer, m_PrimaryNpc);

            NGLog.Info("RuntimeBinder", $"Wired {npcObjects.Count} NPCs");
        }

        private void RebindDialogue()
        {
            if (m_LocalPlayer == null) return;

            bool reboundAny = false;

            Network_Game.UI.Dialogue.ModernDialogueController[] modernControllers =
                FindObjectsOfType<Network_Game.UI.Dialogue.ModernDialogueController>(true);
            for (int i = 0; i < modernControllers.Length; i++)
            {
                Network_Game.UI.Dialogue.ModernDialogueController controller = modernControllers[i];
                if (controller == null || !controller.gameObject.scene.IsValid())
                {
                    continue;
                }

                controller.ForceRefreshBindings();
                reboundAny = true;
            }

            var dialogueUI = FindObjectOfType<DialogueClientUI>();
            if (dialogueUI != null)
            {
                // Find primary NPC
                GameObject primaryNpc = FindPrimaryNpc();

                dialogueUI.SetPlayer(m_LocalPlayer);
                if (primaryNpc != null)
                {
                    dialogueUI.SetNpc(primaryNpc);
                }

                reboundAny = true;
            }

            if (reboundAny)
            {
                NGLog.Info("RuntimeBinder", "Dialogue participants rebound");
            }
        }

        private GameObject FindPrimaryNpc()
        {
            if (m_PrimaryNpc != null)
                return m_PrimaryNpc;

            if (string.IsNullOrWhiteSpace(m_NpcTag))
                return null;

            var npcs = GameObject.FindGameObjectsWithTag(m_NpcTag);
            if (npcs != null && npcs.Length > 0)
            {
                // Return first NPC with NpcDialogueActor
                foreach (var npc in npcs)
                {
                    if (npc.GetComponent<NpcDialogueActor>() != null)
                        return npc;
                }
                return npcs[0];
            }

            return null;
        }

        private void EnsureDebugAssistant()
        {
#if UNITY_2023_1_OR_NEWER
            if (FindAnyObjectByType<LlmDebugAssistant>(FindObjectsInactive.Exclude) != null)
                return;
#else
            if (FindObjectOfType<LlmDebugAssistant>() != null)
                return;
#endif
            var debugGo = new GameObject("DebugSystem");
            debugGo.AddComponent<LlmDebugAssistant>();
            DontDestroyOnLoad(debugGo);
            NGLog.Info("RuntimeBinder", "Initialized LlmDebugAssistant");
        }

        /// <summary>
        /// Can be called externally to rebind camera to current player.
        /// </summary>
        public void RebindCamera()
        {
            if (m_CameraManager != null && m_LocalPlayer != null)
            {
                m_CameraManager.ConfigureCamera(m_LocalPlayer);
            }
        }
    }
}
