using System;
using System.Collections.Generic;
using System.Linq;
using Network_Game.Dialogue;
using Network_Game.Dialogue.Effects;
using Network_Game.Dialogue.MCP;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools.Services
{
    /// <summary>
    /// Aggregates dialogue pipeline diagnostic data for MCP tools.
    /// </summary>
    public static class DialoguePipelineInspector
    {
        /// <summary>
        /// Get combined pipeline snapshot: stats, LLM status, queue, catalog health, active NPCs.
        /// </summary>
        public static Dictionary<string, object> GetPipelineSnapshot()
        {
            var result = new Dictionary<string, object>();

            // Editor state
            result["is_playing"] = EditorApplication.isPlaying;
            result["is_compiling"] = EditorApplication.isCompiling;

            // Pipeline stats (from DialogueMCPBridge)
            var stats = DialogueMCPBridge.GetStats();
            result["pipeline_stats"] = stats ?? new Dictionary<string, object> { ["status"] = "service_not_running" };

            // LLM status
            var llmStatus = DialogueMCPBridge.GetLLMStatus();
            result["llm_status"] = llmStatus ?? new Dictionary<string, object> { ["status"] = "not_available" };

            // Queue
            var queue = DialogueMCPBridge.GetQueueStatus();
            result["queue"] = queue ?? new Dictionary<string, int> { ["status"] = 0 };

            // Catalog summary
            result["catalog"] = GetCatalogSummary();

            // Active NPCs
            result["active_npcs"] = GetActiveNpcSummary();

            // Console errors
            result["recent_error_count"] = GetRecentErrorCount();

            return result;
        }

        public static Dictionary<string, object> GetCatalogSummary()
        {
            var catalog = EffectCatalog.Load();
            if (catalog == null)
            {
                return new Dictionary<string, object>
                {
                    ["loaded"] = false,
                    ["total_effects"] = 0,
                };
            }

            int total = catalog.allEffects?.Count ?? 0;
            int withPrefab = 0;
            int missingPrefab = 0;
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (catalog.allEffects != null)
            {
                for (int i = 0; i < catalog.allEffects.Count; i++)
                {
                    var effect = catalog.allEffects[i];
                    if (effect == null)
                    {
                        continue;
                    }

                    if (effect.effectPrefab != null)
                    {
                        withPrefab++;
                    }
                    else
                    {
                        missingPrefab++;
                    }

                    // Derive category from placement mode or description
                    if (!string.IsNullOrWhiteSpace(effect.description))
                    {
                        // Use first word as rough category
                        string firstWord = effect.description.Split(' ')[0].ToLowerInvariant();
                        categories.Add(firstWord);
                    }
                }
            }

            return new Dictionary<string, object>
            {
                ["loaded"] = true,
                ["total_effects"] = total,
                ["with_prefab"] = withPrefab,
                ["missing_prefab"] = missingPrefab,
                ["has_fallback"] = catalog.fallbackEffectPrefab != null,
                ["allow_unknown_tags"] = catalog.allowUnknownTags,
            };
        }

        public static List<Dictionary<string, object>> GetActiveNpcSummary()
        {
            var npcs = new List<Dictionary<string, object>>();
            if (!EditorApplication.isPlaying)
            {
                return npcs;
            }

#if UNITY_2023_1_OR_NEWER
            var actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(FindObjectsInactive.Exclude);
#else
            var actors = UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif
            if (actors == null)
            {
                return npcs;
            }

            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (actor == null)
                {
                    continue;
                }

                var profile = actor.Profile;
                npcs.Add(new Dictionary<string, object>
                {
                    ["name"] = actor.name,
                    ["profile_id"] = actor.ProfileId ?? "",
                    ["display_name"] = profile?.DisplayName ?? "",
                    ["has_network_object"] = actor.NetworkObject != null,
                    ["network_id"] = actor.NetworkObject != null ? actor.NetworkObject.NetworkObjectId : 0UL,
                    ["power_count"] = profile?.PrefabPowers?.Length ?? 0,
                });
            }

            return npcs;
        }

        private static int GetRecentErrorCount()
        {
            // Use LogEntries reflection to get error count
            try
            {
                var logEntriesType = System.Type.GetType("UnityEditor.LogEntries, UnityEditor");
                if (logEntriesType != null)
                {
                    var getCountMethod = logEntriesType.GetMethod("GetCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (getCountMethod != null)
                    {
                        return (int)getCountMethod.Invoke(null, null);
                    }
                }
            }
            catch
            {
                // Ignore reflection failures
            }

            return -1;
        }
    }
}
