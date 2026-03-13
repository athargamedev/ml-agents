using System.Collections.Generic;
using Network_Game.Diagnostics;
using Network_Game.Dialogue;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Handles NPC discovery and local player/NPC dialogue bindings.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NPCAgentBootstrap : MonoBehaviour
    {
        [Header("NPC Discovery")]
        [SerializeField]
        private string m_NpcTag = "NPC";

        private DialogueClientUI m_LastWiredUi;
        private NetworkObject m_LastWiredPlayer;
        private NetworkObject m_LastWiredNpc;

        public void DisableLlmAgent(GameObject target)
        {
            // Player-side dialogue components were removed; keep this method as a no-op
            // so existing bootstrap call sites remain harmless during migration cleanup.
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

            GameObject[] taggedNpcs;
            try
            {
                taggedNpcs = GameObject.FindGameObjectsWithTag(m_NpcTag);
            }
            catch (UnityException ex)
            {
                NGLog.Warn(
                    "NPCBootstrap",
                    NGLog.Format(
                        "NPC discovery skipped because tag is invalid",
                        ("tag", m_NpcTag),
                        ("reason", ex.Message)
                    )
                );
                return results;
            }

            foreach (GameObject npc in taggedNpcs)
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

            DialogueClientUI dialogueUi = DialogueClientUI.Instance;
            if (dialogueUi == null)
            {
                return false;
            }

            if (
                m_LastWiredUi == dialogueUi
                && m_LastWiredPlayer == playerNet
                && m_LastWiredNpc == npcNet
            )
            {
                return true;
            }

            NGLog.Info(
                "NPCBootstrap",
                $"Wiring Dialogue UI between '{player.name}' and '{npcObject.name}'"
            );
            dialogueUi.ConfigureParticipants(playerNet, npcNet);
            m_LastWiredUi = dialogueUi;
            m_LastWiredPlayer = playerNet;
            m_LastWiredNpc = npcNet;
            return true;
        }

        private static bool IsValidNpc(GameObject npc)
        {
            if (npc == null)
            {
                return false;
            }

            if (npc.GetComponent<NetworkObject>() == null)
            {
                NGLog.Debug("NPCBootstrap", $"Skipping NPC '{npc.name}' - No NetworkObject found.");
                return false;
            }

            return true;
        }
    }
}
