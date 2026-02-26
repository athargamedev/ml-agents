using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Network_Game.Dialogue;
using Network_Game.Dialogue.Effects;
using Network_Game.Dialogue.MCP;
using Network_Game.Editor.CustomTools.Services;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools
{
    [McpForUnityTool(
        "ng_create_npc_profile",
        Description = "Create a complete NPC dialogue profile with persona, system prompt, lore, powers, and keywords — all in one call. Params: profile_id (required), display_name, system_prompt, lore, bored_keywords (comma-separated), enable_bored_light, enable_dynamic_params, powers (JSON array of {name, keywords, prefab_name, element})."
    )]
    public static class CreateNpcProfileTool
    {
        public static object HandleCommand(JObject @params)
        {
            string profileId = @params?.Value<string>("profile_id");
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return new ErrorResponse("profile_id is required.");
            }

            string displayName = @params.Value<string>("display_name") ?? profileId;
            string systemPrompt = @params.Value<string>("system_prompt")
                ?? "You are {npc_name}. Keep responses concise, in-character, and useful to the player.";
            string lore = @params.Value<string>("lore") ?? "";

            // Parse bored keywords
            string[] boredKeywords = null;
            string boredStr = @params.Value<string>("bored_keywords");
            if (!string.IsNullOrWhiteSpace(boredStr))
            {
                boredKeywords = boredStr.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
            }

            bool enableBoredLight = @params.Value<bool?>("enable_bored_light") ?? true;
            bool enableDynamicParams = @params.Value<bool?>("enable_dynamic_params") ?? true;

            // Create profile
            var (profile, assetPath, warnings) = ProfileAutomationService.CreateProfile(
                profileId, displayName, systemPrompt, lore,
                boredKeywords, enableBoredLight, enableDynamicParams);

            // Add powers if specified
            JArray powersJson = @params?.Value<JArray>("powers");
            if (powersJson != null && powersJson.Count > 0)
            {
                var powersList = new List<(string powerName, string[] keywords, string prefabName, string element)>();
                foreach (JObject powerObj in powersJson)
                {
                    string powerName = powerObj.Value<string>("name") ?? "";
                    string keywordsStr = powerObj.Value<string>("keywords") ?? powerName;
                    string[] keywords = keywordsStr.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToArray();
                    string prefabName = powerObj.Value<string>("prefab_name") ?? "";
                    string element = powerObj.Value<string>("element") ?? "";

                    powersList.Add((powerName, keywords, prefabName, element));
                }

                var powerWarnings = ProfileAutomationService.AddPowersToProfile(profileId, powersList);
                warnings.AddRange(powerWarnings);
            }

            return new SuccessResponse(
                $"NPC profile '{displayName}' created at {assetPath}.",
                new
                {
                    profile_id = profileId,
                    display_name = displayName,
                    asset_path = assetPath,
                    power_count = powersJson?.Count ?? 0,
                    warnings = warnings,
                });
        }
    }

    [McpForUnityTool(
        "ng_modify_npc_profile",
        Description = "Patch NPC dialogue profile fields (prompt, lore, display_name, bored_keywords, etc.) by profile_id. Params: profile_id (required), fields (JSON object with field names and values, e.g. {\"system_prompt\": \"You are...\", \"lore\": \"Ancient wizard...\"}). Supported fields: system_prompt, display_name, lore, enable_bored_light, enable_dynamic_params, bored_light_color, bored_light_intensity."
    )]
    public static class ModifyNpcProfileTool
    {
        public static object HandleCommand(JObject @params)
        {
            string profileId = @params?.Value<string>("profile_id");
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return new ErrorResponse("profile_id is required.");
            }

            JObject fieldsJson = @params?.Value<JObject>("fields");
            if (fieldsJson == null || !fieldsJson.HasValues)
            {
                return new ErrorResponse("fields object is required with at least one field to update.");
            }

            // Convert JObject to dictionary
            var fieldPatches = new Dictionary<string, object>();
            foreach (var kvp in fieldsJson)
            {
                if (kvp.Value.Type == JTokenType.String)
                {
                    fieldPatches[kvp.Key] = kvp.Value.Value<string>();
                }
                else if (kvp.Value.Type == JTokenType.Boolean)
                {
                    fieldPatches[kvp.Key] = kvp.Value.Value<bool>();
                }
                else if (kvp.Value.Type == JTokenType.Float || kvp.Value.Type == JTokenType.Integer)
                {
                    fieldPatches[kvp.Key] = kvp.Value.Value<float>();
                }
                else
                {
                    fieldPatches[kvp.Key] = kvp.Value.ToString();
                }
            }

            var (success, warnings) = ProfileAutomationService.PatchProfile(profileId, fieldPatches);

            if (!success && warnings.Any(w => w.Contains("not found")))
            {
                return new ErrorResponse($"Profile '{profileId}' not found.", new { warnings });
            }

            // Get updated profile summary
            var profile = NpcDialogueProfile.GetProfile(profileId);
            var summary = profile != null
                ? ProfileAutomationService.GetProfileSummary(profile)
                : null;

            return new SuccessResponse(
                success
                    ? $"Profile '{profileId}' updated ({fieldPatches.Count} field(s))."
                    : $"Profile '{profileId}' update had issues.",
                new
                {
                    profile_id = profileId,
                    fields_attempted = fieldPatches.Count,
                    success = success,
                    updated_profile = summary,
                    warnings = warnings,
                });
        }
    }

    [McpForUnityTool(
        "ng_probe_npc_dialogue",
        Description = "Send a player message to an NPC via the dialogue pipeline and get the full response with parsed effect intents. Play Mode only. Params: player_message (required), npc_profile_id (optional, uses first found NPC if omitted)."
    )]
    public static class ProbeNpcDialogueTool
    {
        public static object HandleCommand(JObject @params)
        {
            if (!EditorApplication.isPlaying)
            {
                return new ErrorResponse("Unity must be in Play Mode to probe NPC dialogue.");
            }

            string playerMessage = @params?.Value<string>("player_message");
            if (string.IsNullOrWhiteSpace(playerMessage))
            {
                return new ErrorResponse("player_message is required.");
            }

            string npcProfileId = @params?.Value<string>("npc_profile_id");

            // Find NPC actor
            NpcDialogueActor targetActor = null;
            var actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude);

            if (actors == null || actors.Length == 0)
            {
                return new ErrorResponse("No NpcDialogueActor found in scene.");
            }

            if (!string.IsNullOrWhiteSpace(npcProfileId))
            {
                for (int i = 0; i < actors.Length; i++)
                {
                    if (actors[i] != null && actors[i].ProfileId == npcProfileId)
                    {
                        targetActor = actors[i];
                        break;
                    }
                }

                if (targetActor == null)
                {
                    return new ErrorResponse(
                        $"NPC with profile_id '{npcProfileId}' not found in scene. Available: "
                        + string.Join(", ", actors
                            .Where(a => a != null)
                            .Select(a => a.ProfileId)));
                }
            }
            else
            {
                targetActor = actors[0];
            }

            if (targetActor.NetworkObject == null)
            {
                return new ErrorResponse($"NPC '{targetActor.ProfileId}' has no NetworkObject.");
            }

            var service = NetworkDialogueService.Instance;
            if (service == null)
            {
                return new ErrorResponse("NetworkDialogueService.Instance is null. Is the server running?");
            }

            if (!service.IsLLMReady)
            {
                return new ErrorResponse("LLM is not ready yet.", new
                {
                    warmup_degraded = service.IsWarmupDegraded,
                    warmup_failure_count = service.WarmupFailureCount,
                });
            }

            // Get local player for conversation key
            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer == null)
            {
                return new ErrorResponse("No local player object found.");
            }

            // Resolve conversation key and submit
            ulong speakerId = targetActor.NetworkObjectId;
            ulong listenerId = localPlayer.NetworkObjectId;
            ulong clientId = localPlayer.OwnerClientId;
            string conversationKey = service.ResolveConversationKey(
                speakerId, listenerId, clientId, null);

            // Submit via the public RequestDialogue method
            service.RequestDialogue(
                new NetworkDialogueService.DialogueRequest
                {
                    Prompt = playerMessage,
                    ConversationKey = conversationKey,
                    SpeakerNetworkId = speakerId,
                    ListenerNetworkId = listenerId,
                    RequestingClientId = clientId,
                    Broadcast = false,
                    BroadcastDuration = 0f,
                    NotifyClient = true,
                    ClientRequestId = 0,
                    IsUserInitiated = true,
                    BlockRepeatedPrompt = false,
                    MinRepeatDelaySeconds = 0f,
                    RequireUserReply = false,
                });

            return new SuccessResponse(
                $"Dialogue probe submitted to NPC '{targetActor.ProfileId}'. Response will arrive asynchronously via the pipeline.",
                new
                {
                    npc_profile_id = targetActor.ProfileId,
                    npc_display_name = targetActor.Profile?.DisplayName ?? "",
                    npc_network_id = targetActor.NetworkObjectId,
                    conversation_key = conversationKey,
                    player_message = playerMessage,
                    queue_status = DialogueMCPBridge.GetQueueStatus(),
                    hint = "Use ng_pipeline_status to check for response, or read conversation history with DialogueMCPBridge.",
                });
        }
    }
}
