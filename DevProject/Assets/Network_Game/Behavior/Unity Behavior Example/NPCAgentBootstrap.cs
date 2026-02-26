using System.Collections.Generic;
using LLMUnity;
using Network_Game.Diagnostics;
using Network_Game.Dialogue;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Handles LLM agent configuration and NPC discovery.
    /// Extracted from BehaviorSceneBootstrap for modularity.
    /// </summary>
    public class NPCAgentBootstrap : MonoBehaviour
    {
        [Header("NPC Discovery")]
        [SerializeField]
        private string m_NpcTag = "NPC";

        /// <summary>
        /// Configures a list of NPC agents.
        /// </summary>
        public void ConfigureNpcAgents(List<GameObject> npcObjects) { }

        public void PrewireLlmAgent(GameObject target) { }

        public void DisableLlmAgent(GameObject target)
        {
            if (target == null)
                return;
            var llm = target.GetComponent<LLMAgent>();
            if (llm != null)
            {
                NGLog.Info(
                    "NPCBootstrap",
                    $"Disabling LLM Agent on '{target.name}' (Player Instance)"
                );
                llm.enabled = false;
            }
        }

        /// <summary>
        /// Collects all valid NPCs in the scene, ensuring the primary NPC is prioritized at index 0.
        /// </summary>
        public List<GameObject> CollectAndPrioritizeNpcs(GameObject primaryNpc = null)
        {
            var results = new List<GameObject>();
            var seen = new HashSet<GameObject>();

            if (primaryNpc != null && IsValidNpc(primaryNpc))
            {
                results.Add(primaryNpc);
                seen.Add(primaryNpc);
            }

            GameObject[] taggedNpcs = GameObject.FindGameObjectsWithTag(m_NpcTag);
            foreach (var npc in taggedNpcs)
            {
                if (npc != null && IsValidNpc(npc) && !seen.Contains(npc))
                {
                    results.Add(npc);
                    seen.Add(npc);
                }
            }

            return results;
        }

        public bool ConfigureDialogueUiParticipants(GameObject player, GameObject npcObject)
        {
            if (player == null || npcObject == null)
            {
                return false;
            }

            NetworkObject playerNet = player.GetComponent<NetworkObject>();
            NetworkObject npcNet = npcObject.GetComponent<NetworkObject>();
            if (playerNet == null || npcNet == null)
            {
                return false;
            }
            if (playerNet.IsSpawned && !playerNet.IsOwner)
            {
                NGLog.Warn(
                    "NPCBootstrap",
                    NGLog.Format(
                        "Skipping Dialogue UI wiring for non-owner player",
                        ("player", player.name),
                        ("playerNetId", playerNet.NetworkObjectId),
                        ("ownerClientId", playerNet.OwnerClientId)
                    )
                );
                return false;
            }

            var dialogueUi = DialogueClientUI.Instance;
            if (dialogueUi == null)
            {
                return false;
            }

            NGLog.Info(
                "NPCBootstrap",
                $"Wiring Dialogue UI between '{player.name}' and '{npcObject.name}'"
            );
            dialogueUi.ConfigureParticipants(playerNet, npcNet);
            return true;
        }

        private bool IsValidNpc(GameObject npc)
        {
            if (npc == null)
                return false;

            // Check for NetworkObject as per BehaviorSceneBootstrap legacy logic
            if (npc.GetComponent<NetworkObject>() == null)
            {
                NGLog.Debug("NPCBootstrap", $"Skipping NPC '{npc.name}' - No NetworkObject found.");
                return false;
            }

            return true;
        }
    }
}
