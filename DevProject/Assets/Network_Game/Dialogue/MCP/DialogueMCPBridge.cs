using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Network_Game.Auth;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue.MCP
{
    /// <summary>
    /// Bridge class that exposes dialogue system data for MCP tools.
    /// This class is in Assembly-CSharp so it can be accessed by MCP.
    /// </summary>
    public class DialogueMCPBridge
    {
        private const int DefaultSceneElementCount = 10;
        private const float DefaultSceneProbeDistance = 120f;
        private static int s_NextGameplayCopilotRequestId = 99400;

        private struct SceneElement
        {
            public string Name;
            public string Path;
            public Vector3 Position;
            public Vector3 Size;
            public float Distance;
            public string SemanticId;
            public string SemanticRole;
            public string[] SemanticAliases;
            public int SemanticPriority;
        }

        /// <summary>
        /// Get dialogue service stats as a dictionary
        /// </summary>
        public static Dictionary<string, object> GetStats()
        {
            var service = NetworkDialogueService.Instance;
            if (service == null)
                return null;

            int total = service.TotalCompleted + service.TotalFailed;
            float successRate = total > 0 ? (float)service.TotalCompleted / total : 0;

            return new Dictionary<string, object>
            {
                ["pending"] = service.PendingRequestCount,
                ["active"] = service.ActiveRequestCount,
                ["total_enqueued"] = service.TotalRequestsEnqueued,
                ["total_finished"] = service.TotalRequestsFinished,
                ["total_completed"] = service.TotalCompleted,
                ["total_failed"] = service.TotalFailed,
                ["timeout_count"] = service.TimeoutCount,
                ["success_rate"] = Math.Round(successRate * 100, 1),
                ["is_llm_ready"] = service.IsLLMReady,
                ["warmup_degraded"] = service.IsWarmupDegraded,
                ["warmup_failure_count"] = service.WarmupFailureCount,
                ["rejection_reasons"] = service.GetRejectionReasons(),
            };
        }

        /// <summary>
        /// Get LLM status
        /// </summary>
        public static Dictionary<string, object> GetLLMStatus()
        {
            var service = NetworkDialogueService.Instance;
            if (service == null)
                return null;

            return new Dictionary<string, object>
            {
                ["is_ready"] = service.IsLLMReady,
                ["backend"] = service.ActiveInferenceBackendName,
                ["has_backend_config"] = service.HasDialogueBackendConfig,
                ["remote_only"] = true,
                ["warmup_degraded"] = service.IsWarmupDegraded,
                ["warmup_failure_count"] = service.WarmupFailureCount,
                ["remote"] = service.UsesRemoteInference,
                ["remote_endpoint"] = service.RemoteInferenceEndpoint,
            };
        }

        /// <summary>
        /// Get all NPC profiles
        /// </summary>
        public static List<Dictionary<string, object>> GetProfiles()
        {
            var profiles = NpcDialogueProfile.GetAllProfiles();
            return profiles
                .Select(p => new Dictionary<string, object>
                {
                    ["id"] = p.ProfileId,
                    ["display_name"] = p.DisplayName,
                    ["keywords"] = p.GetKeywords(),
                    ["power_count"] = p.PrefabPowers?.Length ?? 0,
                })
                .ToList();
        }

        /// <summary>
        /// Get a specific profile by ID
        /// </summary>
        public static Dictionary<string, object> GetProfile(string profileId)
        {
            var profile = NpcDialogueProfile.GetProfile(profileId);
            if (profile == null)
                return null;

            var result = new Dictionary<string, object>
            {
                ["id"] = profile.ProfileId,
                ["display_name"] = profile.DisplayName,
                ["system_prompt"] = profile.SystemPrompt,
                ["lore"] = profile.Lore,
                ["keywords"] = profile.GetKeywords(),
            };

            if (profile.PrefabPowers != null)
            {
                result["powers"] = profile
                    .PrefabPowers.Select(power => new Dictionary<string, object>
                    {
                        ["name"] = power.PowerName,
                        ["enabled"] = power.Enabled,
                        ["keywords"] = power.Keywords,
                        ["duration"] = power.DurationSeconds,
                        ["scale"] = power.Scale,
                        ["element"] = power.Element,
                    })
                    .ToList();
            }

            result["effect_settings"] = new Dictionary<string, object>
            {
                ["bored_enabled"] = profile.EnableBoredLightEffect,
                ["bored_keywords"] = profile.BoredKeywords,
                ["dynamic_params_enabled"] = profile.EnableDynamicEffectParameters,
                ["min_multiplier"] = profile.DynamicEffectMinMultiplier,
                ["max_multiplier"] = profile.DynamicEffectMaxMultiplier,
            };

            return result;
        }

        /// <summary>
        /// Get available effect types
        /// </summary>
        public static string[] GetEffectTypes()
        {
            return DialogueSceneEffectsController.GetAvailableEffects();
        }

        /// <summary>
        /// Get history for a conversation
        /// </summary>
        public static List<Dictionary<string, string>> GetHistory(string conversationKey)
        {
            var service = NetworkDialogueService.Instance;
            if (service == null)
                return null;

            var history = service.GetHistoryPublic(conversationKey);
            if (history == null)
                return new List<Dictionary<string, string>>();

            return history
                .Select(m => new Dictionary<string, string>
                {
                    ["role"] = m.Role ?? string.Empty,
                    ["content"] = m.Content ?? string.Empty,
                })
                .ToList();
        }

        /// <summary>
        /// Get all conversation keys
        /// </summary>
        public static string[] GetConversationKeys()
        {
            var service = NetworkDialogueService.Instance;
            if (service == null)
                return Array.Empty<string>();

            return service.GetConversationKeys();
        }

        /// <summary>
        /// Get queue status
        /// </summary>
        public static Dictionary<string, int> GetQueueStatus()
        {
            var service = NetworkDialogueService.Instance;
            if (service == null)
                return null;

            return new Dictionary<string, int>
            {
                ["pending"] = service.PendingRequestCount,
                ["active"] = service.ActiveRequestCount,
                ["total_enqueued"] = service.TotalRequestsEnqueued,
                ["total_finished"] = service.TotalRequestsFinished,
            };
        }

        /// <summary>
        /// Build a compact scene snapshot around the local player (or camera fallback).
        /// </summary>
        public static string BuildSceneSnapshotText(
            int maxCount = DefaultSceneElementCount,
            float maxDistance = DefaultSceneProbeDistance
        )
        {
            List<SceneElement> elements = CollectSceneElements(maxCount, maxDistance);
            if (elements.Count == 0)
            {
                return "Scene snapshot: no gameplay-relevant renderers found.";
            }

            var builder = new StringBuilder(512);
            builder.AppendLine("Scene snapshot:");
            for (int i = 0; i < elements.Count; i++)
            {
                SceneElement element = elements[i];
                builder.Append("- ");
                builder.Append(element.Name);
                builder.Append(" at [");
                builder.Append(element.Position.x.ToString("F1"));
                builder.Append(", ");
                builder.Append(element.Position.y.ToString("F1"));
                builder.Append(", ");
                builder.Append(element.Position.z.ToString("F1"));
                builder.Append("], size ");
                builder.Append(element.Size.x.ToString("F1"));
                builder.Append("x");
                builder.Append(element.Size.y.ToString("F1"));
                builder.Append("x");
                builder.Append(element.Size.z.ToString("F1"));
                builder.Append("m, distance ");
                builder.Append(element.Distance.ToString("F1"));
                builder.Append("m");
                if (!string.IsNullOrWhiteSpace(element.SemanticRole))
                {
                    builder.Append(", role=");
                    builder.Append(element.SemanticRole);
                }
                if (!string.IsNullOrWhiteSpace(element.SemanticId))
                {
                    builder.Append(", id=");
                    builder.Append(element.SemanticId);
                }
                if (element.SemanticAliases != null && element.SemanticAliases.Length > 0)
                {
                    builder.Append(", aliases=");
                    builder.Append(string.Join("/", element.SemanticAliases));
                }
                builder.AppendLine(".");
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Send a scene-aware gameplay probe request to nearby NPCs.
        /// </summary>
        public static Dictionary<string, object> SendGameplayProbeToNpcs(
            string directive = null,
            int maxNpcs = 3,
            ulong listenerNetworkObjectId = 0
        )
        {
            if (
                !TryResolveGameplayProbeContext(
                    out NetworkDialogueService service,
                    out ulong localClientId,
                    out ulong listenerNetworkId,
                    out NetworkObject localPlayer,
                    out string error,
                    listenerNetworkObjectId
                )
            )
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = error ?? "Unknown probe setup error.",
                };
            }

            NpcDialogueActor[] actors = FindNpcActors();
            if (actors == null || actors.Length == 0)
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = "No NpcDialogueActor found in scene.",
                };
            }

            string sceneSnapshot = BuildSceneSnapshotText();
            string effectiveDirective = string.IsNullOrWhiteSpace(directive)
                ? "Choose one nearby scene element, respond in character, and trigger one visual power so we can validate effect logic."
                : directive.Trim();

            int targetNpcCount = Mathf.Clamp(maxNpcs, 1, actors.Length);
            Vector3 anchor =
                localPlayer != null
                    ? localPlayer.transform.position
                    : GetSceneProbeAnchorPosition();
            List<NpcDialogueActor> orderedActors = GetOrderedSpawnedNpcActors(
                actors,
                anchor,
                targetNpcCount
            );

            var requestIds = new List<int>(orderedActors.Count);
            var npcNames = new List<string>(orderedActors.Count);
            for (int i = 0; i < orderedActors.Count; i++)
            {
                NpcDialogueActor actor = orderedActors[i];
                int requestId = SendGameplayProbeRequest(
                    service,
                    localClientId,
                    listenerNetworkId,
                    actor,
                    effectiveDirective,
                    sceneSnapshot
                );
                requestIds.Add(requestId);
                npcNames.Add(GetNpcDisplayName(actor));
            }

            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["sent_count"] = requestIds.Count,
                ["request_ids"] = requestIds.ToArray(),
                ["npc_names"] = npcNames.ToArray(),
                ["scene_snapshot"] = sceneSnapshot,
                ["directive"] = effectiveDirective,
                ["listener_network_id"] = listenerNetworkId,
                ["listener_missing"] = localPlayer == null,
            };
        }

        /// <summary>
        /// Send a single scene-aware gameplay probe request to a specific NPC network object.
        /// </summary>
        public static Dictionary<string, object> SendGameplayProbeToNpc(
            ulong speakerNetworkObjectId,
            string directive = null,
            ulong listenerNetworkObjectId = 0
        )
        {
            if (
                !TryResolveGameplayProbeContext(
                    out NetworkDialogueService service,
                    out ulong localClientId,
                    out ulong listenerNetworkId,
                    out _,
                    out string error,
                    listenerNetworkObjectId
                )
            )
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = error ?? "Unknown probe setup error.",
                };
            }

            NpcDialogueActor[] actors = FindNpcActors();
            if (actors == null || actors.Length == 0)
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = "No NpcDialogueActor found in scene.",
                };
            }

            NpcDialogueActor targetActor = null;
            for (int i = 0; i < actors.Length; i++)
            {
                NpcDialogueActor candidate = actors[i];
                if (
                    candidate == null
                    || candidate.NetworkObject == null
                    || !candidate.NetworkObject.IsSpawned
                )
                {
                    continue;
                }

                if (candidate.NetworkObjectId == speakerNetworkObjectId)
                {
                    targetActor = candidate;
                    break;
                }
            }

            if (targetActor == null)
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = $"NPC with NetworkObjectId={speakerNetworkObjectId} not found.",
                };
            }

            string sceneSnapshot = BuildSceneSnapshotText();
            string effectiveDirective = string.IsNullOrWhiteSpace(directive)
                ? "Choose one nearby scene element, respond in character, and trigger one visual power so we can validate effect logic."
                : directive.Trim();

            int requestId = SendGameplayProbeRequest(
                service,
                localClientId,
                listenerNetworkId,
                targetActor,
                effectiveDirective,
                sceneSnapshot
            );

            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["sent_count"] = 1,
                ["request_id"] = requestId,
                ["speaker_network_id"] = speakerNetworkObjectId,
                ["npc_name"] = GetNpcDisplayName(targetActor),
                ["scene_snapshot"] = sceneSnapshot,
                ["directive"] = effectiveDirective,
                ["listener_network_id"] = listenerNetworkId,
            };
        }

        /// <summary>
        /// Send a deterministic animation validation probe to a specific NPC.
        /// This mirrors the effect validation flow but forces an [ANIM:] response.
        /// </summary>
        public static Dictionary<string, object> SendAnimationProbeToNpc(
            ulong speakerNetworkObjectId,
            string animationTag = "EmphasisReact",
            ulong listenerNetworkObjectId = 0
        )
        {
            if (
                !TryResolveGameplayProbeContext(
                    out NetworkDialogueService service,
                    out ulong localClientId,
                    out ulong listenerNetworkId,
                    out _,
                    out string error,
                    listenerNetworkObjectId
                )
            )
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = error ?? "Unknown probe setup error.",
                };
            }

            NpcDialogueActor[] actors = FindNpcActors();
            if (actors == null || actors.Length == 0)
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = "No NpcDialogueActor found in scene.",
                };
            }

            NpcDialogueActor targetActor = null;
            for (int i = 0; i < actors.Length; i++)
            {
                NpcDialogueActor candidate = actors[i];
                if (
                    candidate == null
                    || candidate.NetworkObject == null
                    || !candidate.NetworkObject.IsSpawned
                )
                {
                    continue;
                }

                if (candidate.NetworkObjectId == speakerNetworkObjectId)
                {
                    targetActor = candidate;
                    break;
                }
            }

            if (targetActor == null)
            {
                return new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = $"NPC with NetworkObjectId={speakerNetworkObjectId} not found.",
                };
            }

            int requestId = ++s_NextGameplayCopilotRequestId;
            string key = service.ResolveConversationKey(
                targetActor.NetworkObjectId,
                listenerNetworkId,
                localClientId,
                null
            );

            string resolvedTag = string.IsNullOrWhiteSpace(animationTag)
                ? "EmphasisReact"
                : animationTag.Trim();
            string prompt = BuildAnimationProbePrompt(resolvedTag);
            service.RequestDialogue(
                new NetworkDialogueService.DialogueRequest
                {
                    Prompt = prompt,
                    ConversationKey = key,
                    SpeakerNetworkId = targetActor.NetworkObjectId,
                    ListenerNetworkId = listenerNetworkId,
                    RequestingClientId = localClientId,
                    Broadcast = true,
                    BroadcastDuration = 3f,
                    NotifyClient = true,
                    ClientRequestId = requestId,
                    IsUserInitiated = true,
                    BlockRepeatedPrompt = false,
                    MinRepeatDelaySeconds = 0f,
                    RequireUserReply = false,
                }
            );

            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["sent_count"] = 1,
                ["request_id"] = requestId,
                ["speaker_network_id"] = speakerNetworkObjectId,
                ["npc_name"] = GetNpcDisplayName(targetActor),
                ["animation_tag"] = resolvedTag,
                ["listener_network_id"] = listenerNetworkId,
                ["prompt"] = prompt,
            };
        }

        /// <summary>
        /// Return nearby spawned NPC dialogue actors sorted by distance to local player/camera.
        /// </summary>
        public static List<Dictionary<string, object>> GetNearbyNpcActors(int maxNpcs = 6)
        {
            var result = new List<Dictionary<string, object>>();
            if (!Application.isPlaying)
            {
                return result;
            }

            NpcDialogueActor[] actors = FindNpcActors();
            if (actors == null || actors.Length == 0)
            {
                return result;
            }

            Vector3 anchor = GetSceneProbeAnchorPosition();
            int targetNpcCount = Mathf.Clamp(maxNpcs, 1, actors.Length);
            List<NpcDialogueActor> orderedActors = GetOrderedSpawnedNpcActors(
                actors,
                anchor,
                targetNpcCount
            );

            for (int i = 0; i < orderedActors.Count; i++)
            {
                NpcDialogueActor actor = orderedActors[i];
                float distance = Vector3.Distance(anchor, actor.transform.position);
                NpcDialogueProfile profile = actor.Profile;
                result.Add(
                    new Dictionary<string, object>
                    {
                        ["network_id"] = actor.NetworkObjectId,
                        ["name"] = GetNpcDisplayName(actor),
                        ["distance"] = distance,
                        ["profile_id"] = profile != null ? profile.ProfileId : string.Empty,
                        ["power_names"] = GetEnabledPowerNames(profile),
                    }
                );
            }

            return result;
        }

        private static string[] GetEnabledPowerNames(NpcDialogueProfile profile)
        {
            if (profile == null || profile.PrefabPowers == null || profile.PrefabPowers.Length == 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>(profile.PrefabPowers.Length);
            for (int i = 0; i < profile.PrefabPowers.Length; i++)
            {
                PrefabPowerEntry power = profile.PrefabPowers[i];
                if (power == null || !power.Enabled)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(power.PowerName)
                    ? power.EffectPrefab != null
                        ? power.EffectPrefab.name
                        : string.Empty
                    : power.PowerName.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool TryResolveGameplayProbeContext(
            out NetworkDialogueService service,
            out ulong localClientId,
            out ulong listenerNetworkId,
            out NetworkObject localPlayer,
            out string error,
            ulong preferredListenerNetworkId = 0
        )
        {
            service = NetworkDialogueService.Instance;
            localClientId = 0;
            listenerNetworkId = 0;
            localPlayer = null;
            error = null;

            if (!Application.isPlaying)
            {
                error = "Enter Play Mode first.";
                return false;
            }

            if (service == null)
            {
                error = "NetworkDialogueService not found.";
                return false;
            }

            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            {
                error = "Local Netcode client not ready.";
                return false;
            }

            localClientId = NetworkManager.Singleton.LocalClientId;
            localPlayer = ResolveLocalPlayerObject(localClientId);
            if (preferredListenerNetworkId != 0)
            {
                NetworkObject explicitListener = ResolveProbeListenerObject(
                    preferredListenerNetworkId
                );
                if (explicitListener == null)
                {
                    error =
                        $"Listener player NetworkObjectId={preferredListenerNetworkId} is not a valid spawned player target.";
                    return false;
                }

                listenerNetworkId = explicitListener.NetworkObjectId;
                return true;
            }

            listenerNetworkId = localPlayer != null ? localPlayer.NetworkObjectId : 0;
            return true;
        }

        private static List<NpcDialogueActor> GetOrderedSpawnedNpcActors(
            NpcDialogueActor[] actors,
            Vector3 anchor,
            int maxCount
        )
        {
            int targetCount = Mathf.Max(1, maxCount);
            return actors
                .Where(actor =>
                    actor != null && actor.NetworkObject != null && actor.NetworkObject.IsSpawned
                )
                .OrderBy(actor => Vector3.Distance(actor.transform.position, anchor))
                .Take(targetCount)
                .ToList();
        }

        private static int SendGameplayProbeRequest(
            NetworkDialogueService service,
            ulong localClientId,
            ulong listenerNetworkId,
            NpcDialogueActor actor,
            string effectiveDirective,
            string sceneSnapshot
        )
        {
            int requestId = ++s_NextGameplayCopilotRequestId;
            string key = service.ResolveConversationKey(
                actor.NetworkObjectId,
                listenerNetworkId,
                localClientId,
                null
            );

            string prompt = BuildGameplayPrompt(actor, effectiveDirective, sceneSnapshot);
            service.RequestDialogue(
                new NetworkDialogueService.DialogueRequest
                {
                    Prompt = prompt,
                    ConversationKey = key,
                    SpeakerNetworkId = actor.NetworkObjectId,
                    ListenerNetworkId = listenerNetworkId,
                    RequestingClientId = localClientId,
                    Broadcast = true,
                    BroadcastDuration = 3f,
                    NotifyClient = true,
                    ClientRequestId = requestId,
                    IsUserInitiated = true,
                    BlockRepeatedPrompt = false,
                    MinRepeatDelaySeconds = 0f,
                    RequireUserReply = false,
                }
            );
            return requestId;
        }

        private static string BuildAnimationProbePrompt(string animationTag)
        {
            string tag = string.IsNullOrWhiteSpace(animationTag)
                ? "EmphasisReact"
                : animationTag.Trim();

            return string.Concat(
                    "Animation validation step. Respond in character with one short sentence, then append exactly one tag: [ANIM: ",
                    tag,
                    " | Target: Self].",
                    " Think in visible scene results only: play the animation on your own body and keep it on yourself.",
                    " Output only the final sentence and tag. No analysis, no extra lines.",
                    " Do not emit any [EFFECT:] tags for this step.",
                    " The animation tag name must match exactly and Target must stay Self."
                )
                .Trim();
        }

        private static List<SceneElement> CollectSceneElements(int maxCount, float maxDistance)
        {
            int targetCount = Mathf.Max(1, maxCount);
            Vector3 anchor = GetSceneProbeAnchorPosition();
            var elements = new List<SceneElement>(targetCount);
            var seenRoots = new HashSet<EntityId>();

            Renderer[] renderers = FindSceneRenderers();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                GameObject root =
                    renderer.transform.root != null
                        ? renderer.transform.root.gameObject
                        : renderer.gameObject;

                if (!seenRoots.Add(root.GetEntityId()) || IsIgnoredSceneRoot(root))
                {
                    continue;
                }

                DialogueSemanticTag semantic = ResolveSemanticTag(root);
                if (semantic != null && !semantic.IncludeInSceneSnapshots)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                float distance = Vector3.Distance(anchor, bounds.center);
                if (maxDistance > 0f && distance > maxDistance)
                {
                    continue;
                }

                string semanticId = semantic != null ? semantic.SemanticId : string.Empty;
                string semanticRole = semantic != null ? semantic.RoleKey : string.Empty;
                string[] semanticAliases =
                    semantic != null ? semantic.GetCompactAliases() : Array.Empty<string>();
                elements.Add(
                    new SceneElement
                    {
                        Name = semantic != null ? semantic.ResolveDisplayName(root) : root.name,
                        Path = BuildGameObjectPath(root.transform),
                        Position = bounds.center,
                        Size = bounds.size,
                        Distance = distance,
                        SemanticId = semanticId,
                        SemanticRole = semanticRole,
                        SemanticAliases = semanticAliases,
                        SemanticPriority = semantic != null ? semantic.Priority : 0,
                    }
                );
            }

            return elements
                .OrderByDescending(element => element.SemanticPriority)
                .ThenBy(element => element.Distance)
                .ThenByDescending(element => element.Size.x * element.Size.y * element.Size.z)
                .Take(targetCount)
                .ToList();
        }

        private static DialogueSemanticTag ResolveSemanticTag(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            DialogueSemanticTag tag = root.GetComponent<DialogueSemanticTag>();
            if (tag != null)
            {
                return tag;
            }

            return root.GetComponentInChildren<DialogueSemanticTag>(true);
        }

        private static string BuildGameplayPrompt(
            NpcDialogueActor actor,
            string directive,
            string sceneSnapshot
        )
        {
            string npcName = GetNpcDisplayName(actor);
            var builder = new StringBuilder(640);
            builder.AppendLine($"Gameplay probe for {npcName}.");
            builder.AppendLine(directive);
            builder.AppendLine("Keep the response short (1-2 sentences).");
            builder.AppendLine(
                "When triggering a power, append exactly one [EFFECT: ...] tag at the end."
            );
            builder.AppendLine(sceneSnapshot);
            return builder.ToString().TrimEnd();
        }

        private static string GetNpcDisplayName(NpcDialogueActor actor)
        {
            if (actor == null)
            {
                return "NPC";
            }

            if (actor.Profile != null && !string.IsNullOrWhiteSpace(actor.Profile.DisplayName))
            {
                return actor.Profile.DisplayName.Trim();
            }

            return actor.name;
        }

        private static Vector3 GetSceneProbeAnchorPosition()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                NetworkObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localPlayer != null)
                {
                    return localPlayer.transform.position;
                }
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                return mainCamera.transform.position;
            }

            return Vector3.zero;
        }

        private static bool IsIgnoredSceneRoot(GameObject root)
        {
            if (root == null)
            {
                return true;
            }

            if (root.GetComponentInChildren<NpcDialogueActor>() != null)
            {
                return true;
            }

            if (root.GetComponentInChildren<Camera>() != null)
            {
                return true;
            }

            if (root.GetComponentInChildren<Canvas>() != null)
            {
                return true;
            }

            string name = root.name ?? string.Empty;
            return name.StartsWith("Network", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("EventSystem", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Dialogue", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("MainCamera", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("PlayerCinemachine", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Modern_HUD", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildGameObjectPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new List<string>(8);
            Transform cursor = transform;
            while (cursor != null)
            {
                names.Add(cursor.name);
                cursor = cursor.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static NpcDialogueActor[] FindNpcActors()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            return UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif
        }

        private static NetworkObject ResolveLocalPlayerObject(ulong localClientId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                NetworkObject direct = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (direct != null)
                {
                    return direct;
                }
            }

            NetworkObject[] objects = FindSceneNetworkObjects();
            for (int i = 0; i < objects.Length; i++)
            {
                NetworkObject candidate = objects[i];
                if (
                    candidate == null
                    || !candidate.IsSpawned
                    || candidate.OwnerClientId != localClientId
                )
                {
                    continue;
                }

                if (candidate.GetComponent<NpcDialogueActor>() != null)
                {
                    continue;
                }

                if (candidate.GetComponent<NetworkDialogueService>() != null)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static bool IsValidProbeListenerPlayer(NetworkObject candidate)
        {
            if (candidate == null || !candidate.IsSpawned)
            {
                return false;
            }

            if (candidate.GetComponent<NpcDialogueActor>() != null)
            {
                return false;
            }

            if (candidate.GetComponent<NetworkDialogueService>() != null)
            {
                return false;
            }

            return true;
        }

        private static NetworkObject ResolveProbeListenerObject(ulong listenerNetworkObjectId)
        {
            if (listenerNetworkObjectId == 0)
            {
                return null;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (
                manager != null
                && manager.IsListening
                && manager.SpawnManager != null
                && manager.SpawnManager.SpawnedObjects != null
                && manager.SpawnManager.SpawnedObjects.TryGetValue(
                    listenerNetworkObjectId,
                    out NetworkObject spawned
                )
                && IsValidProbeListenerPlayer(spawned)
            )
            {
                return spawned;
            }

            NetworkObject[] objects = FindSceneNetworkObjects();
            for (int i = 0; i < objects.Length; i++)
            {
                NetworkObject candidate = objects[i];
                if (
                    candidate != null
                    && candidate.NetworkObjectId == listenerNetworkObjectId
                    && IsValidProbeListenerPlayer(candidate)
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        internal static List<(
            ulong networkId,
            ulong clientId,
            string label
        )> GetProbeListenerTargets()
        {
            var targets = new List<(ulong networkId, ulong clientId, string label)>();
            NetworkManager manager = NetworkManager.Singleton;
            if (
                manager == null
                || !manager.IsListening
                || manager.ConnectedClients == null
                || manager.ConnectedClients.Count == 0
            )
            {
                return targets;
            }

            foreach (var pair in manager.ConnectedClients)
            {
                ulong clientId = pair.Key;
                NetworkClient client = pair.Value;
                NetworkObject playerObject = client != null ? client.PlayerObject : null;
                if (!IsValidProbeListenerPlayer(playerObject))
                {
                    continue;
                }

                string label = $"client_{clientId}";
                NetworkDialogueService service = NetworkDialogueService.Instance;
                if (
                    service != null
                    && service.TryGetPlayerIdentityByClientId(clientId, out var snapshot)
                    && !string.IsNullOrWhiteSpace(snapshot.NameId)
                )
                {
                    label = snapshot.NameId.Trim();
                }

                targets.Add((playerObject.NetworkObjectId, clientId, label));
            }

            return targets
                .OrderBy(target => target.clientId)
                .ThenBy(target => target.networkId)
                .ToList();
        }

        private static NetworkObject[] FindSceneNetworkObjects()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude);
#else
            return UnityEngine.Object.FindObjectsOfType<NetworkObject>();
#endif
        }

        private static Renderer[] FindSceneRenderers()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
#else
            return UnityEngine.Object.FindObjectsOfType<Renderer>();
#endif
        }

        // ─── Debug Log Ring Buffer ────────────────────────────────────────────────

        private const int DebugLogCapacity = 100;

        /// <summary>Immutable record of one NPC dialogue exchange for MCP debug tooling.</summary>
        public struct DebugLogEntry
        {
            public long TimestampMs;
            public int RequestId;
            public int ServerRequestId;
            public ulong SpeakerNetworkId;
            public ulong ListenerNetworkId;
            public string ConversationKey;
            public string PromptSnippet;
            public string ResponseSnippet;
            public string[] EffectTagsParsed;
            public string[] AnimationTagsParsed;
            public string Status;
            public string Error;
            public int RetryCount;
            public float QueueLatencyMs;
            public float ModelLatencyMs;
            public float TotalLatencyMs;
            public bool IsUserInitiated;
        }

        /// <summary>LM Studio analysis result pushed back from the Python server.</summary>
        public struct LMStudioLogEntry
        {
            public long TimestampMs;
            public string Mode; // "mirror" | "critique"
            public string Summary;
            public string Detail;
        }

        private static readonly Queue<DebugLogEntry> s_DebugLog = new Queue<DebugLogEntry>(
            DebugLogCapacity
        );
        private static readonly Queue<LMStudioLogEntry> s_LMStudioLog = new Queue<LMStudioLogEntry>(
            20
        );
        private static readonly object s_LogLock = new object();

        private static readonly System.Text.RegularExpressions.Regex s_EffectTagRegex =
            new System.Text.RegularExpressions.Regex(
                @"\[EFFECT:\s*([^\]]+)\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Compiled
            );
        private static readonly System.Text.RegularExpressions.Regex s_AnimationTagRegex =
            new System.Text.RegularExpressions.Regex(
                @"\[ANIM:\s*([^\]]+)\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Compiled
            );

        /// <summary>
        /// Called by NetworkDialogueService after each completed response to populate the ring buffer.
        /// </summary>
        public static void LogDialogueDebugEntry(
            NetworkDialogueService.DialogueRequest request,
            string responseText,
            string status,
            string error = "",
            int retryCount = 0,
            float queueLatencyMs = 0f,
            float modelLatencyMs = 0f,
            float totalLatencyMs = 0f,
            int serverRequestId = 0
        )
        {
            string[] effectTags = ExtractEffectTags(responseText);
            string[] animationTags = ExtractAnimationTags(responseText);
            var entry = new DebugLogEntry
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                RequestId = request.ClientRequestId,
                ServerRequestId = serverRequestId,
                SpeakerNetworkId = request.SpeakerNetworkId,
                ListenerNetworkId = request.ListenerNetworkId,
                ConversationKey = request.ConversationKey ?? string.Empty,
                PromptSnippet = Truncate(request.Prompt, 200),
                ResponseSnippet = Truncate(responseText, 300),
                EffectTagsParsed = effectTags,
                AnimationTagsParsed = animationTags,
                Status = status ?? "unknown",
                Error = error ?? string.Empty,
                RetryCount = retryCount,
                QueueLatencyMs = queueLatencyMs,
                ModelLatencyMs = modelLatencyMs,
                TotalLatencyMs = totalLatencyMs,
                IsUserInitiated = request.IsUserInitiated,
            };

            lock (s_LogLock)
            {
                if (s_DebugLog.Count >= DebugLogCapacity)
                    s_DebugLog.Dequeue();
                s_DebugLog.Enqueue(entry);
            }
        }

        /// <summary>
        /// Stores an LM Studio result entry for display in DialogueDebugPanel.
        /// </summary>
        public static void AddLMStudioLogEntry(LMStudioLogEntry entry)
        {
            lock (s_LogLock)
            {
                if (s_LMStudioLog.Count >= 20)
                    s_LMStudioLog.Dequeue();
                s_LMStudioLog.Enqueue(entry);
            }
        }

        /// <summary>
        /// Returns last N debug log entries as serialisable dictionaries for MCP.
        /// </summary>
        public static List<Dictionary<string, object>> GetDebugLog(int maxEntries = 30)
        {
            var result = new List<Dictionary<string, object>>();
            lock (s_LogLock)
            {
                var entries = s_DebugLog.ToArray();
                int start = Math.Max(0, entries.Length - maxEntries);
                for (int i = start; i < entries.Length; i++)
                {
                    var e = entries[i];
                    result.Add(
                        new Dictionary<string, object>
                        {
                            ["timestamp_ms"] = e.TimestampMs,
                            ["request_id"] = e.RequestId,
                            ["server_request_id"] = e.ServerRequestId,
                            ["speaker_network_id"] = e.SpeakerNetworkId.ToString(),
                            ["listener_network_id"] = e.ListenerNetworkId.ToString(),
                            ["conversation_key"] = e.ConversationKey,
                            ["prompt_snippet"] = e.PromptSnippet,
                            ["response_snippet"] = e.ResponseSnippet,
                            ["effect_tags_parsed"] = e.EffectTagsParsed,
                            ["animation_tags_parsed"] = e.AnimationTagsParsed,
                            ["status"] = e.Status,
                            ["error"] = e.Error,
                            ["retry_count"] = e.RetryCount,
                            ["queue_latency_ms"] = e.QueueLatencyMs,
                            ["model_latency_ms"] = e.ModelLatencyMs,
                            ["total_latency_ms"] = e.TotalLatencyMs,
                            ["is_user_initiated"] = e.IsUserInitiated,
                        }
                    );
                }
            }
            return result;
        }

        /// <summary>
        /// Returns LM Studio log entries for the debug panel.
        /// </summary>
        public static List<LMStudioLogEntry> GetLMStudioLog(int maxEntries = 10)
        {
            lock (s_LogLock)
            {
                var all = s_LMStudioLog.ToArray();
                int start = Math.Max(0, all.Length - maxEntries);
                var result = new List<LMStudioLogEntry>();
                for (int i = start; i < all.Length; i++)
                    result.Add(all[i]);
                return result;
            }
        }

        /// <summary>
        /// Scans the scene for active ParticleSystems tagged as dialogue-spawned.
        /// Returns name, position, remaining lifetime, and source NPC.
        /// </summary>
        public static List<Dictionary<string, object>> GetActiveVfxState()
        {
            var result = new List<Dictionary<string, object>>();
            if (!Application.isPlaying)
                return result;

#if UNITY_2023_1_OR_NEWER
            var systems = UnityEngine.Object.FindObjectsByType<ParticleSystem>(
                FindObjectsInactive.Exclude
            );
#else
            var systems = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
#endif
            foreach (var ps in systems)
            {
                if (ps == null || !ps.isPlaying)
                    continue;

                // Only include systems that carry the dialogue-spawned marker
                if (ps.GetComponent<DialogueSpawnedMarker>() == null)
                    continue;

                var marker = ps.GetComponent<DialogueSpawnedMarker>();
                Vector3 pos = ps.transform.position;
                var main = ps.main;
                var emission = ps.emission;
                var shape = ps.shape;
                Vector3 worldScale = ps.transform.lossyScale;

                result.Add(
                    new Dictionary<string, object>
                    {
                        ["name"] = ps.gameObject.name,
                        ["position"] = new[] { pos.x, pos.y, pos.z },
                        ["duration_remaining"] = Mathf.Max(0f, main.duration - ps.time),
                        ["source_npc"] = marker.SourceNpcProfileId ?? string.Empty,
                        ["effect_tag"] = marker.EffectTag ?? string.Empty,
                        ["source_network_id"] = marker.SourceNetworkObjectId.ToString(),
                        ["target_network_id"] = marker.TargetNetworkObjectId.ToString(),
                        ["configured_scale"] = marker.ConfiguredScale,
                        ["configured_duration"] = marker.ConfiguredDurationSeconds,
                        ["attach_to_target"] = marker.AttachToTarget,
                        ["fit_to_target_mesh"] = marker.FitToTargetMesh,
                        ["seed"] = marker.EffectSeed.ToString(),
                        ["world_scale"] = new[] { worldScale.x, worldScale.y, worldScale.z },
                        ["main_duration"] = main.duration,
                        ["simulation_speed"] = main.simulationSpeed,
                        ["max_particles"] = main.maxParticles,
                        ["start_size"] = ResolveCurveMid(main.startSize),
                        ["start_speed"] = ResolveCurveMid(main.startSpeed),
                        ["start_lifetime"] = ResolveCurveMid(main.startLifetime),
                        ["emission_rate_over_time"] = ResolveCurveMid(emission.rateOverTime),
                        ["emission_rate_over_distance"] = ResolveCurveMid(
                            emission.rateOverDistance
                        ),
                        ["shape_radius"] = shape.enabled ? shape.radius : 0f,
                        ["shape_angle"] = shape.enabled ? shape.angle : 0f,
                        ["is_attached"] = ps.transform.parent != null,
                    }
                );
            }
            return result;
        }

        private static float ResolveCurveMid(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return (curve.constantMin + curve.constantMax) * 0.5f;
                case ParticleSystemCurveMode.Curve:
                    return curve.curve != null
                        ? curve.curve.Evaluate(0.5f) * curve.curveMultiplier
                        : 0f;
                case ParticleSystemCurveMode.TwoCurves:
                    float min = curve.curveMin != null ? curve.curveMin.Evaluate(0.5f) : 0f;
                    float max = curve.curveMax != null ? curve.curveMax.Evaluate(0.5f) : 0f;
                    return (min + max) * 0.5f * curve.curveMultiplier;
                default:
                    return curve.constant;
            }
        }

        private static string[] ExtractEffectTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();
            var matches = s_EffectTagRegex.Matches(text);
            if (matches.Count == 0)
                return Array.Empty<string>();
            var tags = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++)
                tags[i] = matches[i].Groups[1].Value.Trim();
            return tags;
        }

        private static string[] ExtractAnimationTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();
            var matches = s_AnimationTagRegex.Matches(text);
            if (matches.Count == 0)
                return Array.Empty<string>();
            var tags = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++)
                tags[i] = matches[i].Groups[1].Value.Trim();
            return tags;
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor menu to expose dialogue MCP commands
    /// </summary>
    public static class DialogueMCPMenu
    {
        private const string SceneScanMenuPath = "Network Game/MCP/Gameplay Copilot/Scan Scene";
        private const string ProbeNearbyNpcsMenuPath =
            "Network Game/MCP/Gameplay Copilot/Probe Nearby NPCs";
        private const string RunAutomatedProbesMenuPath =
            "Network Game/MCP/Gameplay Copilot/Run Automated Probes (Feedback-Gated)";
        private const string RunAutomatedTrainingProbesMenuPath =
            "Network Game/MCP/Gameplay Copilot/Run Automated Probes (Training Mode, Skip Narrative)";
        private const string StopAutomatedProbesMenuPath =
            "Network Game/MCP/Gameplay Copilot/Stop Automated Probes";
        private const string ClearAutomationPlacementMemoryMenuPath =
            "Network Game/MCP/Gameplay Copilot/Clear Placement Memory";
        private const string AutomationPlacementMemoryRelativePath =
            "output/automation_probe_placement_memory.json";
        private const int MaxAutomatedNpcCount = 4;
        private const int MaxAutomatedEffectsPerRun = 24;
        private static readonly string[] s_AutomationSpecialEffectTags = { "dissolve", "respawn" };
        private static readonly string[] s_AutomationFallbackEffectTags =
        {
            "FireBall",
            "Lightning Storm",
            "Ice Lance",
            "Ground Fog",
            "Forge Sparks",
            "Guide Fireflies",
        };
        private static readonly HashSet<int> s_GameplayProbeRequestIds = new HashSet<int>();
        private static readonly Dictionary<
            string,
            HashSet<string>
        > s_AutomationDisfavoredPlacementsByTag = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        private static bool s_AutomationPlacementMemoryLoaded;
        private static bool s_GameplayProbeSubscribed;
        private static GameplayProbeAutomationRunner s_AutomationRunner;

        [Serializable]
        private sealed class AutomationPlacementMemoryEntry
        {
            public string effect_tag = string.Empty;
            public List<string> disfavored_placements = new List<string>();
        }

        [Serializable]
        private sealed class AutomationPlacementMemoryFile
        {
            public int schema_version = 1;
            public string updated_utc = string.Empty;
            public List<AutomationPlacementMemoryEntry> entries =
                new List<AutomationPlacementMemoryEntry>();
        }

        /// <summary>
        /// Get dialogue stats - use via menu "Network Game/MCP/Dialogue Stats"
        /// </summary>
        [UnityEditor.MenuItem("Network Game/MCP/Dialogue Stats")]
        public static void PrintStats()
        {
            var stats = DialogueMCPBridge.GetStats();
            if (stats == null)
            {
                Debug.LogWarning("[DialogueMCP] NetworkDialogueService not found in scene.");
                return;
            }

            var json = SimpleJson(stats);
            UnityEditor.EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log($"[DialogueMCP] Stats: {json}");
        }

        /// <summary>
        /// Get LLM status - use via menu "Network Game/MCP/LLM Status"
        /// </summary>
        [UnityEditor.MenuItem("Network Game/MCP/LLM Status")]
        public static void PrintLLMStatus()
        {
            var status = DialogueMCPBridge.GetLLMStatus();
            if (status == null)
            {
                Debug.LogWarning("[DialogueMCP] NetworkDialogueService not found in scene.");
                return;
            }

            var json = SimpleJson(status);
            UnityEditor.EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log($"[DialogueMCP] LLM Status: {json}");
        }

        /// <summary>
        /// List all profiles - use via menu "Network Game/MCP/List Profiles"
        /// </summary>
        [UnityEditor.MenuItem("Network Game/MCP/List Profiles")]
        public static void ListProfiles()
        {
            var profiles = DialogueMCPBridge.GetProfiles();
            var json = SimpleJson(profiles);
            UnityEditor.EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log($"[DialogueMCP] Profiles ({profiles.Count}): {json}");
        }

        /// <summary>
        /// List effect types - use via menu "Network Game/MCP/List Effects"
        /// </summary>
        [UnityEditor.MenuItem("Network Game/MCP/List Effects")]
        public static void ListEffects()
        {
            var effects = DialogueMCPBridge.GetEffectTypes();
            var json = SimpleJson(effects);
            UnityEditor.EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log($"[DialogueMCP] Effects: {json}");
        }

        /// <summary>
        /// Get queue status - use via menu "Network Game/MCP/Queue Status"
        /// </summary>
        [UnityEditor.MenuItem("Network Game/MCP/Queue Status")]
        public static void QueueStatus()
        {
            var queue = DialogueMCPBridge.GetQueueStatus();
            if (queue == null)
            {
                Debug.LogWarning("[DialogueMCP] NetworkDialogueService not found.");
                return;
            }

            var json = SimpleJson(queue);
            UnityEditor.EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log($"[DialogueMCP] Queue: {json}");
        }

        [UnityEditor.MenuItem(SceneScanMenuPath)]
        public static void ScanGameplayScene()
        {
            string snapshot = DialogueMCPBridge.BuildSceneSnapshotText();
            UnityEditor.EditorGUIUtility.systemCopyBuffer = snapshot;
            Debug.Log($"[DialogueMCP] Scene scan:\n{snapshot}");
        }

        [UnityEditor.MenuItem(ProbeNearbyNpcsMenuPath)]
        public static void ProbeNearbyNpcs()
        {
            EnsureGameplayProbeSubscription();
            Dictionary<string, object> result = DialogueMCPBridge.SendGameplayProbeToNpcs();
            if (result == null)
            {
                Debug.LogWarning("[DialogueMCP] Gameplay probe failed: null result.");
                return;
            }

            bool ok =
                result.TryGetValue("ok", out object okValue) && okValue is bool boolOk && boolOk;

            if (!ok)
            {
                string error = result.TryGetValue("error", out object errorValue)
                    ? errorValue?.ToString()
                    : "unknown error";
                Debug.LogWarning($"[DialogueMCP] Gameplay probe failed: {error}");
                return;
            }

            if (
                result.TryGetValue("request_ids", out object requestIdsValue)
                && requestIdsValue is int[] requestIds
            )
            {
                for (int i = 0; i < requestIds.Length; i++)
                {
                    s_GameplayProbeRequestIds.Add(requestIds[i]);
                }
            }

            int sentCount =
                result.TryGetValue("sent_count", out object sentValue) && sentValue is int sent
                    ? sent
                    : 0;

            string snapshot = result.TryGetValue("scene_snapshot", out object snapshotValue)
                ? snapshotValue?.ToString() ?? string.Empty
                : string.Empty;

            Debug.Log(
                $"[DialogueMCP] Gameplay probe sent to {sentCount} NPC(s). Waiting for responses...\n{snapshot}"
            );
        }

        [UnityEditor.MenuItem(RunAutomatedProbesMenuPath)]
        public static void RunAutomatedProbes()
        {
            RunAutomatedProbesInternal(includeNarrativePrelude: true);
        }

        [UnityEditor.MenuItem(RunAutomatedTrainingProbesMenuPath)]
        public static void RunAutomatedTrainingProbes()
        {
            RunAutomatedProbesInternal(includeNarrativePrelude: false);
        }

        private static void RunAutomatedProbesInternal(bool includeNarrativePrelude)
        {
            EnsureGameplayProbeSubscription();
            EnsureAutomationPlacementMemoryLoaded();

            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Automated Probes",
                    "Enter Play Mode first, then click Run Automated Probes.",
                    "OK"
                );
                return;
            }

            bool netcodeReady =
                NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening
                && NetworkManager.Singleton.LocalClient != null;

            if (!netcodeReady)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Automated Probes",
                    "NetworkManager is not started.\n\nPress 'Start Host' in the NetworkManager inspector, then try again.",
                    "OK"
                );
                return;
            }

            if (s_AutomationRunner != null && s_AutomationRunner.IsRunning)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Automated Probes",
                    "Probes are already running.\n\nUse  Network Game > MCP > Gameplay Copilot > Stop Automated Probes  first.",
                    "OK"
                );
                return;
            }

            List<Dictionary<string, object>> nearbyNpcs = DialogueMCPBridge.GetNearbyNpcActors(
                MaxAutomatedNpcCount
            );
            if (nearbyNpcs.Count == 0)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Automated Probes",
                    "No spawned NPCs found.\n\nMake sure at least one NpcDialogueActor with a NetworkObject is spawned in the scene.",
                    "OK"
                );
                return;
            }

            var npcTargets = new List<(ulong id, string name)>(nearbyNpcs.Count);
            var allEffectTags = new List<string>(48);
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < nearbyNpcs.Count; i++)
            {
                Dictionary<string, object> npc = nearbyNpcs[i];
                if (!TryReadNpcFromDictionary(npc, out ulong npcId, out string npcName))
                {
                    continue;
                }

                npcTargets.Add((npcId, npcName));

                if (npc.TryGetValue("power_names", out object powerObj))
                {
                    AppendTagsFromObject(powerObj, allEffectTags, seenTags);
                }
            }

            AppendCatalogEffectTags(allEffectTags, seenTags);
            for (int i = 0; i < s_AutomationSpecialEffectTags.Length; i++)
            {
                AddTagIfUnique(s_AutomationSpecialEffectTags[i], allEffectTags, seenTags);
            }

            if (allEffectTags.Count == 0)
            {
                for (int i = 0; i < s_AutomationFallbackEffectTags.Length; i++)
                {
                    AddTagIfUnique(s_AutomationFallbackEffectTags[i], allEffectTags, seenTags);
                }
            }

            if (npcTargets.Count == 0 || allEffectTags.Count == 0)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Automated Probes",
                    $"Could not build probe steps (npcs={npcTargets.Count}, effectTags={allEffectTags.Count}).\n\n"
                        + "Check that NPCs have NpcDialogueProfile with PrefabPowers enabled, or that EffectCatalog has entries.",
                    "OK"
                );
                return;
            }

            List<(ulong networkId, ulong clientId, string label)> listenerTargets =
                DialogueMCPBridge.GetProbeListenerTargets();
            if (listenerTargets.Count == 0)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Automated Probes",
                    "No spawned player listener targets found.\n\nMake sure host/client player objects are spawned before running probes.",
                    "OK"
                );
                return;
            }

            int narrativeStepCount = 0;
            if (includeNarrativePrelude)
            {
                // ── Seed player data for narrative hint testing ────────────────────
                LocalPlayerAuthService auth = LocalPlayerAuthService.Instance;
                if (auth != null)
                {
                    string testNpcId = npcTargets[0]
                        .name.Trim()
                        .ToLowerInvariant()
                        .Replace(" ", "_");
                    auth.SetPlayerClass("berserker");
                    auth.SetReputation(testNpcId, 90);
                    auth.AddInventoryTag("cursed_blade");
                    auth.AddInventoryTag("ancient_tome");
                    auth.SetQuestFlag("met_elder");
                    auth.SetQuestFlag("defeated_dragon");
                    auth.SetLastAction("defeated_boss_unscathed");
                    Debug.Log(
                        $"[DialogueMCP] Seeded test player data: class=berserker, reputation[{testNpcId}]=90, "
                            + "inventory=[cursed_blade,ancient_tome], flags=[met_elder,defeated_dragon], "
                            + "last_action=defeated_boss_unscathed"
                    );
                }
                else
                {
                    Debug.LogWarning(
                        "[DialogueMCP] LocalPlayerAuthService not found — narrative hints will not be seeded."
                    );
                }
            }
            else
            {
                Debug.Log(
                    "[DialogueMCP] Training-mode automated probes: skipping narrative prelude and player-data seeding."
                );
            }

            List<string> plannedTags = BuildDiverseAutomationPlan(allEffectTags);
            if (plannedTags.Count > MaxAutomatedEffectsPerRun)
            {
                plannedTags = plannedTags.Take(MaxAutomatedEffectsPerRun).ToList();
            }

            var steps = new List<GameplayProbeAutomationRunner.ProbeStep>(
                plannedTags.Count + (includeNarrativePrelude ? 5 : 0)
            );

            if (includeNarrativePrelude)
            {
                // ── Prepend 5 narrative probe steps (one per new player-data field) ─
                (ulong narrativeListenerNetworkId, ulong _, string narrativeListenerLabel) =
                    listenerTargets[0];
                List<GameplayProbeAutomationRunner.ProbeStep> narrativeSteps =
                    BuildNarrativeProbeSteps(
                        npcTargets[0],
                        narrativeListenerNetworkId,
                        narrativeListenerLabel
                    );
                steps.AddRange(narrativeSteps);
                narrativeStepCount = narrativeSteps.Count;
            }

            // ── Effect validation steps ───────────────────────────────────────────
            for (int i = 0; i < plannedTags.Count; i++)
            {
                (ulong npcId, string npcName) = npcTargets[i % npcTargets.Count];
                (ulong listenerNetworkId, ulong __, string listenerLabel) = listenerTargets[
                    i % listenerTargets.Count
                ];
                string effectTag = plannedTags[i];
                string placementHint = SelectAutomationPlacementHint(effectTag, null);
                if (string.IsNullOrWhiteSpace(placementHint))
                {
                    continue;
                }

                steps.Add(
                    new GameplayProbeAutomationRunner.ProbeStep
                    {
                        NpcNetworkId = npcId,
                        NpcName = npcName,
                        EffectTag = effectTag,
                        PlacementHint = placementHint,
                        AttemptIndex = 0,
                        ListenerNetworkId = listenerNetworkId,
                        ListenerLabel = listenerLabel,
                        Directive = BuildAutomationDirectiveForEffect(
                            effectTag,
                            placementHint,
                            listenerLabel
                        ),
                    }
                );
            }

            s_AutomationRunner = new GameplayProbeAutomationRunner(steps);
            s_AutomationRunner.Start();
            string listenerSummary = string.Join(
                ", ",
                listenerTargets.Select(target =>
                    $"{target.label}(client={target.clientId}, net={target.networkId})"
                )
            );
            Debug.Log(
                $"[DialogueMCP] Built automated plan: {narrativeStepCount} narrative check(s) + {plannedTags.Count} effect tag(s) across {npcTargets.Count} NPC(s) and {listenerTargets.Count} listener target(s): {listenerSummary}"
            );
        }

        [UnityEditor.MenuItem(StopAutomatedProbesMenuPath)]
        public static void StopAutomatedProbes()
        {
            if (s_AutomationRunner == null || !s_AutomationRunner.IsRunning)
            {
                Debug.Log("[DialogueMCP] No automated probe run is active.");
                return;
            }

            s_AutomationRunner.Stop("stopped_by_user");
        }

        [UnityEditor.MenuItem(ClearAutomationPlacementMemoryMenuPath)]
        public static void ClearAutomationPlacementMemory()
        {
            s_AutomationDisfavoredPlacementsByTag.Clear();
            s_AutomationPlacementMemoryLoaded = true;
            TrySaveAutomationPlacementMemory();
            Debug.Log("[DialogueMCP] Cleared persisted automation placement memory.");
        }

        private static bool TryReadNpcFromDictionary(
            Dictionary<string, object> data,
            out ulong npcId,
            out string npcName
        )
        {
            npcId = 0;
            npcName = "NPC";
            if (data == null)
            {
                return false;
            }

            if (!data.TryGetValue("network_id", out object idObj) || idObj == null)
            {
                return false;
            }

            try
            {
                npcId = Convert.ToUInt64(idObj);
            }
            catch
            {
                return false;
            }

            if (data.TryGetValue("name", out object nameObj) && nameObj != null)
            {
                string raw = nameObj.ToString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    npcName = raw.Trim();
                }
            }

            return npcId != 0;
        }

        private static void AppendCatalogEffectTags(List<string> tags, HashSet<string> seen)
        {
            try
            {
                Effects.EffectCatalog catalog = Effects.EffectCatalog.Load();
                if (catalog == null || catalog.allEffects == null)
                {
                    return;
                }

                for (int i = 0; i < catalog.allEffects.Count; i++)
                {
                    Effects.EffectDefinition effect = catalog.allEffects[i];
                    if (effect == null)
                    {
                        continue;
                    }

                    AddTagIfUnique(effect.effectTag, tags, seen);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DialogueMCP] Could not read EffectCatalog for automation: {ex.Message}"
                );
            }
        }

        private static void AppendTagsFromObject(
            object source,
            List<string> tags,
            HashSet<string> seen
        )
        {
            if (source == null)
            {
                return;
            }

            if (source is string[] arr)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    AddTagIfUnique(arr[i], tags, seen);
                }
                return;
            }

            if (source is IEnumerable<string> enumerable)
            {
                foreach (string item in enumerable)
                {
                    AddTagIfUnique(item, tags, seen);
                }
                return;
            }

            if (source is IEnumerable<object> objEnumerable)
            {
                foreach (object item in objEnumerable)
                {
                    AddTagIfUnique(item?.ToString(), tags, seen);
                }
                return;
            }

            AddTagIfUnique(source.ToString(), tags, seen);
        }

        private static void AddTagIfUnique(string rawTag, List<string> tags, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(rawTag))
            {
                return;
            }

            string tag = rawTag.Trim();
            string dedupKey = CanonicalizeAutomationTag(tag);
            if (string.IsNullOrWhiteSpace(dedupKey))
            {
                return;
            }

            if (seen.Add(dedupKey))
            {
                tags.Add(tag);
            }
        }

        private static string CanonicalizeAutomationTag(string rawTag)
        {
            if (string.IsNullOrWhiteSpace(rawTag))
            {
                return string.Empty;
            }

            string tag = rawTag.Trim();
            string lower = tag.ToLowerInvariant();

            if (lower.Contains("dissolve"))
            {
                return "dissolve";
            }

            if (lower.Contains("respawn") || lower.Contains("revive") || lower.Contains("reappear"))
            {
                return "respawn";
            }

            var compact = new StringBuilder(tag.Length);
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if (char.IsLetterOrDigit(c))
                {
                    compact.Append(c);
                }
            }

            return compact.Length > 0 ? compact.ToString() : lower;
        }

        private enum AutomationEffectCategory
        {
            Projectile = 0,
            Fire = 1,
            Weather = 2,
            Area = 3,
            Ambient = 4,
            Utility = 5,
            Special = 6,
        }

        private static List<string> BuildDiverseAutomationPlan(List<string> tags)
        {
            var buckets = new Dictionary<AutomationEffectCategory, Queue<string>>();
            foreach (
                AutomationEffectCategory category in Enum.GetValues(
                    typeof(AutomationEffectCategory)
                )
            )
            {
                buckets[category] = new Queue<string>();
            }

            for (int i = 0; i < tags.Count; i++)
            {
                string tag = tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                AutomationEffectCategory category = ClassifyAutomationCategory(tag);
                buckets[category].Enqueue(tag);
            }

            var ordered = new List<string>(tags.Count);
            AutomationEffectCategory[] passOrder =
            {
                AutomationEffectCategory.Projectile,
                AutomationEffectCategory.Fire,
                AutomationEffectCategory.Weather,
                AutomationEffectCategory.Area,
                AutomationEffectCategory.Ambient,
                AutomationEffectCategory.Utility,
                AutomationEffectCategory.Special,
            };

            bool addedAny;
            do
            {
                addedAny = false;
                for (int i = 0; i < passOrder.Length; i++)
                {
                    Queue<string> bucket = buckets[passOrder[i]];
                    if (bucket.Count == 0)
                    {
                        continue;
                    }

                    ordered.Add(bucket.Dequeue());
                    addedAny = true;
                }
            } while (addedAny);

            return ordered;
        }

        private static AutomationEffectCategory ClassifyAutomationCategory(string effectTag)
        {
            string lower = (effectTag ?? string.Empty).ToLowerInvariant();
            if (lower == "dissolve" || lower == "respawn")
            {
                return AutomationEffectCategory.Special;
            }

            if (
                lower.Contains("projectile")
                || lower.Contains("missile")
                || lower.Contains("rocket")
                || lower.Contains("arrow")
                || lower.Contains("bolt")
                || lower.Contains("lance")
                || lower.Contains("fireball")
            )
            {
                return AutomationEffectCategory.Projectile;
            }

            if (
                lower.Contains("fire")
                || lower.Contains("flame")
                || lower.Contains("burn")
                || lower.Contains("heat")
                || lower.Contains("wildfire")
            )
            {
                return AutomationEffectCategory.Fire;
            }

            if (
                lower.Contains("ice")
                || lower.Contains("frost")
                || lower.Contains("freeze")
                || lower.Contains("storm")
                || lower.Contains("rain")
                || lower.Contains("water")
                || lower.Contains("thunder")
                || lower.Contains("lightning")
            )
            {
                return AutomationEffectCategory.Weather;
            }

            if (
                lower.Contains("explosion")
                || lower.Contains("blast")
                || lower.Contains("shockwave")
                || lower.Contains("impact")
                || lower.Contains("shatter")
                || lower.Contains("splash")
            )
            {
                return AutomationEffectCategory.Area;
            }

            if (
                lower.Contains("fog")
                || lower.Contains("smoke")
                || lower.Contains("steam")
                || lower.Contains("dust")
                || lower.Contains("motes")
                || lower.Contains("sand")
                || lower.Contains("flies")
            )
            {
                return AutomationEffectCategory.Ambient;
            }

            return AutomationEffectCategory.Utility;
        }

        private static string BuildAutomationDirectiveForEffect(
            string effectTag,
            string placementHint,
            string listenerLabel = null
        )
        {
            string tag = string.IsNullOrWhiteSpace(effectTag) ? "FireBall" : effectTag.Trim();
            bool special =
                tag.Equals("dissolve", StringComparison.OrdinalIgnoreCase)
                || tag.Equals("respawn", StringComparison.OrdinalIgnoreCase);
            string placement = string.IsNullOrWhiteSpace(placementHint)
                ? ResolveAutomationPlacementHint(tag, special)
                : placementHint;
            string targetHint = ResolveAutomationTargetHint(placement, special);
            string extras = placement switch
            {
                "attached" => "Collision: allow_overlap | GroundSnap: false",
                "projectile" => "Collision: strict | RequireLineOfSight: true",
                "area" => "Collision: strict | GroundSnap: true",
                _ => "Collision: relaxed | GroundSnap: true",
            };

            string caution = special
                ? string.Empty
                : "Use only the requested effect tag for this step. Do NOT use dissolve or respawn unless explicitly requested.";
            string listenerInstruction = string.IsNullOrWhiteSpace(listenerLabel)
                ? string.Empty
                : $" Address the current listener player ({listenerLabel}) directly.";
            string spatialGuide = placement switch
            {
                "attached" =>
                    " Think in visible scene results only: place the effect on the target body so it follows the target. Match the target's visible body size.",
                "area" =>
                    " Think in visible scene results only: place the effect on the ground close to the target.",
                "projectile" =>
                    " Think in visible scene results only: start the effect away from the target and have it travel toward the target.",
                _ =>
                    " Think in visible scene results only: keep the effect in nearby world space around the target.",
            };

            return string.Concat(
                    "Effect validation step. Respond in character with one short sentence, then append exactly one tag: [EFFECT: ",
                    tag,
                    " | Target: ",
                    targetHint,
                    " | Placement: ",
                    placement,
                    " | Scale: 1.0 | Duration: 3.5 | ",
                    extras,
                    "].",
                    spatialGuide,
                    listenerInstruction,
                    " Output only the final sentence and tag. No analysis, no extra lines.",
                    " Do not reason about Unity internals such as GameObjects, meshes, materials, shaders, or animations.",
                    " The effect tag name must match exactly and you must not emit any additional [EFFECT:] tags. ",
                    caution
                )
                .Trim();
        }

        private static List<GameplayProbeAutomationRunner.ProbeStep> BuildNarrativeProbeSteps(
            (ulong id, string name) npcTarget,
            ulong listenerNetworkId,
            string listenerLabel
        )
        {
            // Each tuple: (playerDataField, acceptedKeywordsInResponse, openDialoguePrompt)
            // Keywords are deliberately broad so a paraphrase still counts as a PASS.
            var configs = new (string field, string[] keywords, string prompt)[]
            {
                (
                    "class",
                    new[] { "berserker", "warrior", "fighter" },
                    "Greet me, warrior. What do you think of someone with the fighting spirit of a berserker?"
                ),
                (
                    "reputation",
                    new[] { "champion", "hero", "renowned" },
                    "Tell me honestly — do you know who I am and how much I've done for you?"
                ),
                (
                    "inventory_tags",
                    new[] { "cursed", "blade", "tome" },
                    "I carry something unusual on my person. Do you notice what I have in my possession?"
                ),
                (
                    "quest_flags",
                    new[] { "elder", "keep", "journey" },
                    "I recently met the elder of the northern keep. Have you heard of my journey?"
                ),
                (
                    "last_action",
                    new[] { "boss", "victory", "accomplishment", "defeated" },
                    "Word travels fast in these lands. Have you heard about my recent accomplishment in the keep?"
                ),
            };

            var steps = new List<GameplayProbeAutomationRunner.ProbeStep>(configs.Length);
            foreach (var (field, keywords, prompt) in configs)
            {
                steps.Add(
                    new GameplayProbeAutomationRunner.ProbeStep
                    {
                        NpcNetworkId = npcTarget.id,
                        NpcName = npcTarget.name,
                        ListenerNetworkId = listenerNetworkId,
                        ListenerLabel = listenerLabel,
                        EffectTag = string.Empty,
                        PlacementHint = string.Empty,
                        AttemptIndex = 0,
                        Directive = prompt,
                        IsNarrativeCheck = true,
                        NarrativeField = field,
                        NarrativeKeywords = keywords,
                    }
                );
            }

            return steps;
        }

        private static string ResolveAutomationTargetHint(string placementHint, bool special)
        {
            if (special)
            {
                return "Player";
            }

            return placementHint switch
            {
                "projectile" => "Player",
                "attached" => "Player",
                "area" => "Ground",
                "ambient" => "World",
                _ => "Player",
            };
        }

        private static string SelectAutomationPlacementHint(
            string effectTag,
            string excludePlacement
        )
        {
            EnsureAutomationPlacementMemoryLoaded();
            bool special =
                effectTag.Equals("dissolve", StringComparison.OrdinalIgnoreCase)
                || effectTag.Equals("respawn", StringComparison.OrdinalIgnoreCase);
            string defaultPlacement = ResolveAutomationPlacementHint(effectTag, special);
            var candidates = new List<string>(5)
            {
                defaultPlacement,
                "attached",
                "area",
                "projectile",
                "ambient",
            };

            string canonicalTag = CanonicalizeAutomationTag(effectTag);
            s_AutomationDisfavoredPlacementsByTag.TryGetValue(
                canonicalTag,
                out HashSet<string> disfavored
            );
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (
                    !string.IsNullOrWhiteSpace(excludePlacement)
                    && candidate.Equals(excludePlacement, StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                if (disfavored != null && disfavored.Contains(candidate))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static bool IsNegativePlacementOutcome(string outcome)
        {
            if (string.IsNullOrWhiteSpace(outcome))
            {
                return false;
            }

            string normalized = outcome.Trim().ToLowerInvariant();
            return normalized == "wrong_target"
                || normalized == "wrong_placement"
                || normalized == "wrong_mesh_fit"
                || normalized == "not_visible";
        }

        private static void RegisterAutomationPlacementFeedback(
            string effectTag,
            string placementHint,
            string outcome
        )
        {
            if (string.IsNullOrWhiteSpace(effectTag) || string.IsNullOrWhiteSpace(placementHint))
            {
                return;
            }

            EnsureAutomationPlacementMemoryLoaded();
            string canonicalTag = CanonicalizeAutomationTag(effectTag);
            if (
                !s_AutomationDisfavoredPlacementsByTag.TryGetValue(
                    canonicalTag,
                    out HashSet<string> set
                )
            )
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                s_AutomationDisfavoredPlacementsByTag[canonicalTag] = set;
            }

            if (IsNegativePlacementOutcome(outcome))
            {
                if (set.Add(placementHint.Trim()))
                {
                    TrySaveAutomationPlacementMemory();
                }
                return;
            }

            if (
                !string.IsNullOrWhiteSpace(outcome)
                && outcome.Trim().Equals("looks_correct", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (set.Remove(placementHint.Trim()))
                {
                    TrySaveAutomationPlacementMemory();
                }
            }
        }

        private static void EnsureAutomationPlacementMemoryLoaded()
        {
            if (s_AutomationPlacementMemoryLoaded)
            {
                return;
            }

            s_AutomationPlacementMemoryLoaded = true;
            string path = ResolveAutomationPlacementMemoryPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var data = JsonUtility.FromJson<AutomationPlacementMemoryFile>(json);
                if (data?.entries == null)
                {
                    return;
                }

                s_AutomationDisfavoredPlacementsByTag.Clear();
                for (int i = 0; i < data.entries.Count; i++)
                {
                    AutomationPlacementMemoryEntry entry = data.entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.effect_tag))
                    {
                        continue;
                    }

                    string canonicalTag = CanonicalizeAutomationTag(entry.effect_tag);
                    if (string.IsNullOrWhiteSpace(canonicalTag))
                    {
                        continue;
                    }

                    if (
                        !s_AutomationDisfavoredPlacementsByTag.TryGetValue(
                            canonicalTag,
                            out HashSet<string> set
                        )
                    )
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        s_AutomationDisfavoredPlacementsByTag[canonicalTag] = set;
                    }

                    if (entry.disfavored_placements == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < entry.disfavored_placements.Count; j++)
                    {
                        string placement = entry.disfavored_placements[j];
                        if (string.IsNullOrWhiteSpace(placement))
                        {
                            continue;
                        }

                        set.Add(placement.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DialogueMCP] Failed to load automation placement memory: {ex.Message}"
                );
            }
        }

        private static void TrySaveAutomationPlacementMemory()
        {
            try
            {
                string path = ResolveAutomationPlacementMemoryPath();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var data = new AutomationPlacementMemoryFile
                {
                    schema_version = 1,
                    updated_utc = DateTime.UtcNow.ToString("o"),
                };

                foreach (var kvp in s_AutomationDisfavoredPlacementsByTag)
                {
                    if (
                        string.IsNullOrWhiteSpace(kvp.Key)
                        || kvp.Value == null
                        || kvp.Value.Count == 0
                    )
                    {
                        continue;
                    }

                    var entry = new AutomationPlacementMemoryEntry { effect_tag = kvp.Key };
                    foreach (string placement in kvp.Value)
                    {
                        if (string.IsNullOrWhiteSpace(placement))
                        {
                            continue;
                        }

                        entry.disfavored_placements.Add(placement.Trim());
                    }

                    if (entry.disfavored_placements.Count > 0)
                    {
                        data.entries.Add(entry);
                    }
                }

                data.entries.Sort(
                    (a, b) =>
                        string.Compare(
                            a.effect_tag,
                            b.effect_tag,
                            StringComparison.OrdinalIgnoreCase
                        )
                );
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DialogueMCP] Failed to save automation placement memory: {ex.Message}"
                );
            }
        }

        private static string ResolveAutomationPlacementMemoryPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(
                Path.Combine(projectRoot, AutomationPlacementMemoryRelativePath)
            );
        }

        private static bool TagsMatch(string expectedTag, string appliedTag)
        {
            string a = CanonicalizeAutomationTag(expectedTag);
            string b = CanonicalizeAutomationTag(appliedTag);
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveAutomationPlacementHint(string effectTag, bool special)
        {
            if (special)
            {
                return "attached";
            }

            string lower = (effectTag ?? string.Empty).ToLowerInvariant();
            if (
                lower.Contains("projectile")
                || lower.Contains("lance")
                || lower.Contains("bolt")
                || lower.Contains("arrow")
                || lower.Contains("fireball")
                || lower.Contains("missile")
            )
            {
                return "projectile";
            }

            if (
                lower.Contains("explosion")
                || lower.Contains("blast")
                || lower.Contains("shockwave")
                || lower.Contains("storm")
                || lower.Contains("rain")
                || lower.Contains("nova")
            )
            {
                return "area";
            }

            if (
                lower.Contains("shield")
                || lower.Contains("aura")
                || lower.Contains("dissolve")
                || lower.Contains("respawn")
            )
            {
                return "attached";
            }

            return "attached";
        }

        private static void EnsureGameplayProbeSubscription()
        {
            if (s_GameplayProbeSubscribed)
            {
                return;
            }

            NetworkDialogueService.OnDialogueResponse += HandleGameplayProbeResponse;
            s_GameplayProbeSubscribed = true;
        }

        private static void HandleGameplayProbeResponse(
            NetworkDialogueService.DialogueResponse response
        )
        {
            int clientRequestId = response.Request.ClientRequestId;
            if (s_AutomationRunner != null && s_AutomationRunner.HandleResponse(response))
            {
                return;
            }

            if (!s_GameplayProbeRequestIds.Remove(clientRequestId))
            {
                return;
            }

            string speaker = response.Request.SpeakerNetworkId.ToString();
            string status = response.Status.ToString();
            string text = string.IsNullOrWhiteSpace(response.ResponseText)
                ? "(empty response)"
                : response.ResponseText.Trim();
            string error = string.IsNullOrWhiteSpace(response.Error)
                ? string.Empty
                : response.Error.Trim();

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning(
                    $"[DialogueMCP] Probe response request={clientRequestId} speaker={speaker} status={status} error={error}\n{text}"
                );
                return;
            }

            Debug.Log(
                $"[DialogueMCP] Probe response request={clientRequestId} speaker={speaker} status={status}\n{text}"
            );
        }

        private sealed class GameplayProbeAutomationRunner
        {
            internal struct ProbeStep
            {
                public ulong NpcNetworkId;
                public string NpcName;
                public ulong ListenerNetworkId;
                public string ListenerLabel;
                public string EffectTag;
                public string PlacementHint;
                public int AttemptIndex;
                public string Directive;

                /// <summary>When true this step validates a narrative hint field, not an effect placement.</summary>
                public bool IsNarrativeCheck;

                /// <summary>Which player-data field is under test: "class" | "reputation" | "inventory_tags" | "quest_flags" | "last_action"</summary>
                public string NarrativeField;

                /// <summary>Accepted substrings anywhere in the LLM response (case-insensitive) for a PASS verdict.</summary>
                public string[] NarrativeKeywords;
            }

            private const double LocalStepResponseTimeoutSeconds = 55d;
            private const double RemoteStepResponseTimeoutSeconds = 480d;
            private const double ResponseSettleWithoutPromptSeconds = 2.5d;
            private const double InterStepDelaySeconds = 0.35d;
            private const double TransientRetryDelaySeconds = 4.0d;
            private const double CancelDrainGraceSeconds = 5.0d;
            private const double SendBusyRetryDelaySeconds = 2.0d;

            private sealed class NarrativeCheckResult
            {
                public string Field;
                public string Keywords;
                public bool Passed;
                public string Excerpt;
            }

            private readonly List<ProbeStep> m_Steps;
            private readonly List<NarrativeCheckResult> m_NarrativeResults =
                new List<NarrativeCheckResult>();
            private int m_StepIndex;
            private int m_ActiveRequestId;
            private bool m_WaitingForResponse;
            private bool m_WaitingForFeedback;
            private bool m_SawBlockingPrompt;
            private bool m_WaitingForCancelDrain;
            private double m_RequestSentAt;
            private double m_ResponseAt;
            private double m_NextStepAt;
            private double m_CancelIssuedAt;
            private string m_LastFeedbackOutcome;

            public bool IsRunning { get; private set; }

            public GameplayProbeAutomationRunner(List<ProbeStep> steps)
            {
                m_Steps = steps ?? new List<ProbeStep>();
                m_StepIndex = 0;
                m_ActiveRequestId = -1;
                m_LastFeedbackOutcome = string.Empty;
            }

            public void Start()
            {
                if (IsRunning)
                {
                    return;
                }

                DialogueEffectFeedbackPrompt prompt =
                    DialogueEffectFeedbackPrompt.EnsureForAutomation();
                if (prompt != null)
                {
                    prompt.ForceEnablePrompt(true);
                }
                else
                {
                    Debug.LogWarning(
                        "[DialogueMCP] Feedback prompt instance could not be created; probes may auto-advance with no_feedback_prompt."
                    );
                }

                IsRunning = true;
                m_NextStepAt = UnityEditor.EditorApplication.timeSinceStartup;
                UnityEditor.EditorApplication.update += Tick;
                DialogueEffectFeedbackPrompt.OnFeedbackSubmitted += HandleFeedbackSubmitted;
                Debug.Log(
                    $"[DialogueMCP] Automated probe run started with {m_Steps.Count} step(s). Submit the feedback popup each step to advance."
                );
            }

            public void Stop(string reason)
            {
                if (!IsRunning)
                {
                    return;
                }

                IsRunning = false;
                m_WaitingForResponse = false;
                m_WaitingForFeedback = false;
                m_WaitingForCancelDrain = false;
                m_ActiveRequestId = -1;
                UnityEditor.EditorApplication.update -= Tick;
                DialogueEffectFeedbackPrompt.OnFeedbackSubmitted -= HandleFeedbackSubmitted;
                Debug.Log($"[DialogueMCP] Automated probe run stopped ({reason}).");

                if (m_NarrativeResults.Count > 0)
                {
                    int narrativePass = m_NarrativeResults.Count(r => r.Passed);
                    int narrativeFail = m_NarrativeResults.Count - narrativePass;
                    Debug.Log(
                        $"[DialogueMCP] ── Narrative hint summary: {narrativePass} PASS / {narrativeFail} FAIL ──"
                    );
                    foreach (NarrativeCheckResult r in m_NarrativeResults)
                    {
                        string marker = r.Passed ? "✅" : "❌";
                        Debug.Log(
                            $"  {marker} [{r.Field}] keywords='{r.Keywords}'\n     {r.Excerpt}"
                        );
                    }
                }
            }

            public bool HandleResponse(NetworkDialogueService.DialogueResponse response)
            {
                if (!IsRunning || !m_WaitingForResponse)
                {
                    return false;
                }

                int clientRequestId = response.Request.ClientRequestId;
                if (clientRequestId != m_ActiveRequestId)
                {
                    return false;
                }

                m_WaitingForResponse = false;

                // ── Transient rejection (NPC busy) → retry the same step ──────────
                string transientError = string.IsNullOrWhiteSpace(response.Error)
                    ? string.Empty
                    : response.Error.Trim();
                bool isTransientRejection =
                    response.Status == NetworkDialogueService.DialogueStatus.Failed
                    && (
                        transientError == "conversation_in_flight"
                        || transientError == "repeat_delay"
                        || transientError == "request_rejected"
                    );
                if (isTransientRejection)
                {
                    m_WaitingForFeedback = false;
                    m_NextStepAt =
                        UnityEditor.EditorApplication.timeSinceStartup + TransientRetryDelaySeconds;
                    Debug.Log(
                        $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} transient rejection"
                            + $" ({transientError}) for {m_Steps[m_StepIndex].NpcName} - retrying in {TransientRetryDelaySeconds:F1}s."
                    );
                    return true;
                }

                m_WaitingForFeedback = true;
                m_ResponseAt = UnityEditor.EditorApplication.timeSinceStartup;
                m_SawBlockingPrompt = DialogueEffectFeedbackPrompt.IsBlockingPromptActive;
                m_LastFeedbackOutcome = string.Empty;

                ProbeStep currentStep = m_Steps[m_StepIndex];
                string status = response.Status.ToString();
                string error = string.IsNullOrWhiteSpace(response.Error)
                    ? string.Empty
                    : response.Error.Trim();
                string text = string.IsNullOrWhiteSpace(response.ResponseText)
                    ? "(empty response)"
                    : response.ResponseText.Trim();

                if (string.IsNullOrWhiteSpace(error))
                {
                    Debug.Log(
                        $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} response request={clientRequestId} status={status}\n{text}"
                    );
                }
                else
                {
                    Debug.LogWarning(
                        $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} response request={clientRequestId} status={status} error={error}\n{text}"
                    );
                }

                // Narrative check: validate that at least one accepted keyword appears in the response
                if (
                    currentStep.IsNarrativeCheck
                    && currentStep.NarrativeKeywords != null
                    && currentStep.NarrativeKeywords.Length > 0
                )
                {
                    string keywordSummary = string.Join("|", currentStep.NarrativeKeywords);
                    bool passed = false;
                    for (int i = 0; i < currentStep.NarrativeKeywords.Length; i++)
                    {
                        string narrativeKeyword = currentStep.NarrativeKeywords[i];
                        if (
                            !string.IsNullOrWhiteSpace(narrativeKeyword)
                            && text.IndexOf(narrativeKeyword, StringComparison.OrdinalIgnoreCase)
                                >= 0
                        )
                        {
                            passed = true;
                            break;
                        }
                    }

                    string excerpt = text.Length > 140 ? text.Substring(0, 140) + "…" : text;
                    m_NarrativeResults.Add(
                        new NarrativeCheckResult
                        {
                            Field = currentStep.NarrativeField,
                            Keywords = keywordSummary,
                            Passed = passed,
                            Excerpt = excerpt,
                        }
                    );
                    string verdict = passed ? "✅ PASS" : "❌ FAIL (keywords not found in response)";
                    Debug.Log(
                        $"[DialogueMCP] Narrative [{currentStep.NarrativeField}] keywords='{keywordSummary}' → {verdict}"
                    );
                }

                return true;
            }

            private void Tick()
            {
                if (!IsRunning)
                {
                    return;
                }

                if (!Application.isPlaying)
                {
                    Stop("play_mode_exited");
                    return;
                }

                double now = UnityEditor.EditorApplication.timeSinceStartup;
                if (m_WaitingForResponse)
                {
                    if (TryRecoverMissedResponseViaPolling())
                    {
                        return;
                    }

                    if (now - m_RequestSentAt > GetCurrentStepResponseTimeoutSeconds())
                    {
                        CancelTimedOutRequestAndWaitForDrain(now);
                    }
                    return;
                }

                if (m_WaitingForCancelDrain)
                {
                    TickCancelDrainWait(now);
                    return;
                }

                if (m_WaitingForFeedback)
                {
                    TickFeedbackWait(now);
                    return;
                }

                if (now < m_NextStepAt)
                {
                    return;
                }

                if (DialogueEffectFeedbackPrompt.IsBlockingPromptActive)
                {
                    return;
                }

                if (m_StepIndex >= m_Steps.Count)
                {
                    Stop("completed");
                    return;
                }

                SendCurrentStep();
            }

            private void TickFeedbackWait(double now)
            {
                bool isBlocking = DialogueEffectFeedbackPrompt.IsBlockingPromptActive;
                if (isBlocking)
                {
                    m_SawBlockingPrompt = true;
                    return;
                }

                if (m_SawBlockingPrompt)
                {
                    string outcome = string.IsNullOrWhiteSpace(m_LastFeedbackOutcome)
                        ? "feedback_submitted"
                        : "feedback_" + m_LastFeedbackOutcome;
                    AdvanceStep(outcome);
                    return;
                }

                if (now - m_ResponseAt > ResponseSettleWithoutPromptSeconds)
                {
                    AdvanceStep("no_feedback_prompt");
                    return;
                }
            }

            private void SendCurrentStep()
            {
                ProbeStep step = m_Steps[m_StepIndex];
                Dictionary<string, object> result = DialogueMCPBridge.SendGameplayProbeToNpc(
                    step.NpcNetworkId,
                    step.Directive,
                    step.ListenerNetworkId
                );

                bool ok =
                    result != null
                    && result.TryGetValue("ok", out object okValue)
                    && okValue is bool boolOk
                    && boolOk;
                if (!ok)
                {
                    string error =
                        result != null && result.TryGetValue("error", out object errorValue)
                            ? errorValue?.ToString() ?? "unknown error"
                            : "unknown error";
                    bool transientBusySendError =
                        string.Equals(
                            error,
                            "conversation_in_flight",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(error, "repeat_delay", StringComparison.OrdinalIgnoreCase);
                    if (transientBusySendError)
                    {
                        m_NextStepAt =
                            UnityEditor.EditorApplication.timeSinceStartup
                            + SendBusyRetryDelaySeconds;
                        Debug.LogWarning(
                            $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} send transient busy for {step.NpcName}: {error}. Retrying in {SendBusyRetryDelaySeconds:F1}s."
                        );
                        return;
                    }

                    Debug.LogWarning(
                        $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} send failed for {step.NpcName}: {error}"
                    );
                    AdvanceStep("send_failed");
                    return;
                }

                if (
                    !result.TryGetValue("request_id", out object requestObj)
                    || !TryConvertToInt(requestObj, out int requestId)
                )
                {
                    Debug.LogWarning(
                        $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} missing request_id for {step.NpcName}."
                    );
                    AdvanceStep("missing_request_id");
                    return;
                }

                m_ActiveRequestId = requestId;
                m_WaitingForResponse = true;
                m_RequestSentAt = UnityEditor.EditorApplication.timeSinceStartup;
                Debug.Log(
                    $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} -> request={requestId} npc={step.NpcName} listener={step.ListenerLabel}({step.ListenerNetworkId}) tag={step.EffectTag} placement={step.PlacementHint} attempt={step.AttemptIndex}\nDirective: {step.Directive}"
                );
            }

            private void HandleFeedbackSubmitted(
                DialogueEffectFeedbackPrompt.FeedbackSubmission submission
            )
            {
                if (!IsRunning || !m_WaitingForFeedback)
                {
                    return;
                }

                ProbeStep step = m_Steps[m_StepIndex];
                // Narrative steps have no placement logic — feedback popup is not expected for them
                if (step.IsNarrativeCheck)
                {
                    return;
                }

                m_SawBlockingPrompt = true;
                m_LastFeedbackOutcome = submission.Outcome ?? string.Empty;

                RegisterAutomationPlacementFeedback(
                    step.EffectTag,
                    step.PlacementHint,
                    m_LastFeedbackOutcome
                );

                bool effectMismatch = !TagsMatch(step.EffectTag, submission.Effect.EffectName);
                bool shouldRetry =
                    step.AttemptIndex < 1
                    && (effectMismatch || IsNegativePlacementOutcome(m_LastFeedbackOutcome));
                if (!shouldRetry)
                {
                    return;
                }

                string retryPlacement = SelectAutomationPlacementHint(
                    step.EffectTag,
                    step.PlacementHint
                );
                if (
                    string.IsNullOrWhiteSpace(retryPlacement)
                    || retryPlacement.Equals(step.PlacementHint, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return;
                }

                ProbeStep retryStep = new ProbeStep
                {
                    NpcNetworkId = step.NpcNetworkId,
                    NpcName = step.NpcName,
                    ListenerNetworkId = step.ListenerNetworkId,
                    ListenerLabel = step.ListenerLabel,
                    EffectTag = step.EffectTag,
                    PlacementHint = retryPlacement,
                    AttemptIndex = step.AttemptIndex + 1,
                    Directive = BuildAutomationDirectiveForEffect(
                        step.EffectTag,
                        retryPlacement,
                        step.ListenerLabel
                    ),
                };
                m_Steps.Add(retryStep);
                Debug.Log(
                    $"[DialogueMCP] Queued retry for tag={step.EffectTag} with alternative placement={retryPlacement} (outcome={m_LastFeedbackOutcome}, mismatch={effectMismatch})."
                );
            }

            private void AdvanceStep(string reason)
            {
                ProbeStep step = m_Steps[m_StepIndex];
                Debug.Log(
                    $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} finished for npc={step.NpcName} listener={step.ListenerLabel}({step.ListenerNetworkId}) tag={step.EffectTag} placement={step.PlacementHint} attempt={step.AttemptIndex} ({reason})."
                );

                m_StepIndex++;
                m_ActiveRequestId = -1;
                m_WaitingForResponse = false;
                m_WaitingForFeedback = false;
                m_WaitingForCancelDrain = false;
                m_SawBlockingPrompt = false;
                m_LastFeedbackOutcome = string.Empty;
                m_NextStepAt =
                    UnityEditor.EditorApplication.timeSinceStartup + InterStepDelaySeconds;

                if (m_StepIndex >= m_Steps.Count)
                {
                    Stop("completed");
                }
            }

            private double GetCurrentStepResponseTimeoutSeconds()
            {
                NetworkDialogueService service = NetworkDialogueService.Instance;
                if (service == null)
                {
                    return LocalStepResponseTimeoutSeconds;
                }

                bool isRemote = service.UsesRemoteInference;
                return isRemote
                    ? RemoteStepResponseTimeoutSeconds
                    : LocalStepResponseTimeoutSeconds;
            }

            private void CancelTimedOutRequestAndWaitForDrain(double now)
            {
                if (m_ActiveRequestId <= 0)
                {
                    AdvanceStep("response_timeout");
                    return;
                }

                NetworkDialogueService service = NetworkDialogueService.Instance;
                if (service != null)
                {
                    service.CancelRequest(m_ActiveRequestId);
                }

                m_WaitingForResponse = false;
                m_WaitingForFeedback = false;
                m_WaitingForCancelDrain = true;
                m_CancelIssuedAt = now;

                Debug.LogWarning(
                    $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} request={m_ActiveRequestId} exceeded timeout ({GetCurrentStepResponseTimeoutSeconds():F0}s). Cancelling and waiting for dialogue service to drain."
                );
            }

            private void TickCancelDrainWait(double now)
            {
                NetworkDialogueService service = NetworkDialogueService.Instance;
                bool canAdvance = service == null || service.ActiveRequestCount <= 0;
                bool drainTimedOut = now - m_CancelIssuedAt >= CancelDrainGraceSeconds;
                if (!canAdvance && !drainTimedOut)
                {
                    return;
                }

                if (!canAdvance && service != null)
                {
                    Debug.LogWarning(
                        $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} cancel drain grace expired after {CancelDrainGraceSeconds:F1}s (active={service.ActiveRequestCount}, pending={service.PendingRequestCount}). Advancing anyway."
                    );
                }

                AdvanceStep(
                    drainTimedOut
                        ? "response_timeout_cancelled_drain_timeout"
                        : "response_timeout_cancelled"
                );
            }

            private bool TryRecoverMissedResponseViaPolling()
            {
                if (m_ActiveRequestId <= 0)
                {
                    return false;
                }

                NetworkDialogueService service = NetworkDialogueService.Instance;
                if (service == null)
                {
                    return false;
                }

                if (
                    service.IsClientRequestInFlight(
                        m_ActiveRequestId,
                        NetworkManager.Singleton != null
                            ? NetworkManager.Singleton.LocalClientId
                            : ulong.MaxValue
                    )
                )
                {
                    return false;
                }

                if (
                    !service.TryGetTerminalResponseByClientRequestId(
                        m_ActiveRequestId,
                        out var response,
                        NetworkManager.Singleton != null
                            ? NetworkManager.Singleton.LocalClientId
                            : ulong.MaxValue
                    )
                )
                {
                    return false;
                }

                Debug.LogWarning(
                    $"[DialogueMCP] Auto step {m_StepIndex + 1}/{m_Steps.Count} recovered missed response callback for clientRequest={m_ActiveRequestId} via polling."
                );
                HandleResponse(response);
                return true;
            }

            private static bool TryConvertToInt(object value, out int result)
            {
                result = 0;
                if (value == null)
                {
                    return false;
                }

                if (value is int intValue)
                {
                    result = intValue;
                    return true;
                }

                if (value is long longValue)
                {
                    result = (int)longValue;
                    return true;
                }

                if (value is short shortValue)
                {
                    result = shortValue;
                    return true;
                }

                if (value is uint uintValue && uintValue <= int.MaxValue)
                {
                    result = (int)uintValue;
                    return true;
                }

                if (value is ulong ulongValue && ulongValue <= int.MaxValue)
                {
                    result = (int)ulongValue;
                    return true;
                }

                return int.TryParse(value.ToString(), out result);
            }
        }

        /// <summary>
        /// Simple JSON serialization for dictionaries
        /// </summary>
        private static string SimpleJson(object obj)
        {
            if (obj == null)
                return "null";

            if (obj is Dictionary<string, object> dict)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var kvp in dict)
                {
                    parts.Add($"\"{kvp.Key}\":{ValueToJson(kvp.Value)}");
                }
                return "{" + string.Join(",", parts) + "}";
            }

            if (
                obj
                is System.Collections.Generic.List<System.Collections.Generic.Dictionary<
                    string,
                    object
                >> list
            )
            {
                var items = new System.Collections.Generic.List<string>();
                foreach (var item in list)
                {
                    var itemParts = new System.Collections.Generic.List<string>();
                    foreach (var kvp in item)
                    {
                        itemParts.Add($"\"{kvp.Key}\":{ValueToJson(kvp.Value)}");
                    }
                    items.Add("{" + string.Join(",", itemParts) + "}");
                }
                return "[" + string.Join(",", items) + "]";
            }

            if (obj is string[] arr)
            {
                return "[\"" + string.Join("\",\"", arr) + "\"]";
            }

            return obj.ToString();
        }

        private static string ValueToJson(object value)
        {
            if (value == null)
                return "null";
            if (value is string s)
                return $"\"{s}\"";
            if (value is bool b)
                return b ? "true" : "false";
            if (value is int i)
                return i.ToString();
            if (value is float f)
                return f.ToString("F2");
            if (value is double d)
                return d.ToString("F2");
            return $"\"{value}\"";
        }
    }
#endif
}
