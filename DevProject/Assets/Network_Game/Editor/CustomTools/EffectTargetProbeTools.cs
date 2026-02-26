using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Network_Game.Dialogue;
using Network_Game.Dialogue.Effects;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools
{
    [McpForUnityTool(
        "ng_probe_effect_targets",
        Description = "Probe NPC/player/surface targets in the current play scene. Resolves P1/P2 ordering, target player selection, floor under player, and wall between NPC and player."
    )]
    public static class ProbeEffectTargetsTool
    {
        public class Parameters
        {
            [ToolParameter("Optional NPC profile id (e.g. npc.storm_oracle). If empty, uses first active NPC.", Required = false)]
            public string npc_profile_id { get; set; }

            [ToolParameter("Optional NPC GameObject name fallback selector.", Required = false)]
            public string npc_name { get; set; }

            [ToolParameter("Player selector: p1, p2, nearest_to_npc, client:<id>, network:<id>.", Required = false)]
            public string player_selector { get; set; }

            [ToolParameter("Resolve floor under selected player (default true).", Required = false)]
            public bool? resolve_floor { get; set; }

            [ToolParameter("Resolve wall between NPC and player (default true).", Required = false)]
            public bool? resolve_wall { get; set; }

            [ToolParameter("Downward floor ray distance in meters (default 8).", Required = false)]
            public float? floor_ray_distance { get; set; }

            [ToolParameter("Max wall probe distance in meters (default 40).", Required = false)]
            public float? wall_max_distance { get; set; }

            [ToolParameter("Sphere radius for wall probe. Use 0 for pure raycast (default 0.15).", Required = false)]
            public float? wall_sphere_radius { get; set; }

            [ToolParameter("Physics layer mask integer used for floor/wall probes (default all layers).", Required = false)]
            public int? surface_layer_mask { get; set; }
        }

        [MenuItem("Network Game/MCP/Probe Effect Targets (Default)")]
        private static void MenuProbeDefault()
        {
            var result = HandleCommand(new JObject());
            JObject jo = result as JObject ?? JObject.FromObject(result);
            if (jo.Value<bool?>("success") == true)
            {
                Debug.Log("[EffectTargetProbe] " + jo.ToString(Newtonsoft.Json.Formatting.None));
            }
            else
            {
                Debug.LogError("[EffectTargetProbe] " + jo.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        public static object HandleCommand(JObject @params)
        {
            Parameters p = @params?.ToObject<Parameters>() ?? new Parameters();
            var options = new EffectTargetProbeService.ProbeOptions
            {
                NpcProfileId = p.npc_profile_id,
                NpcName = p.npc_name,
                PlayerSelector = p.player_selector,
                ResolveFloor = p.resolve_floor ?? true,
                ResolveWall = p.resolve_wall ?? true,
                FloorRayDistance = p.floor_ray_distance ?? 8f,
                WallMaxDistance = p.wall_max_distance ?? 40f,
                WallSphereRadius = p.wall_sphere_radius ?? 0.15f,
                SurfaceLayerMask = p.surface_layer_mask ?? ~0,
            };

            var data = EffectTargetProbeService.Probe(options);
            if (!EditorApplication.isPlaying)
            {
                return new ErrorResponse(
                    "Unity must be in Play Mode to probe runtime effect targets.",
                    data
                );
            }

            return new SuccessResponse("Effect targets probed.", data);
        }
    }

    internal static class EffectTargetProbeService
    {
        internal sealed class ProbeOptions
        {
            public string NpcProfileId;
            public string NpcName;
            public string PlayerSelector;
            public bool ResolveFloor = true;
            public bool ResolveWall = true;
            public float FloorRayDistance = 8f;
            public float WallMaxDistance = 40f;
            public float WallSphereRadius = 0.15f;
            public int SurfaceLayerMask = ~0;
        }

        internal static Dictionary<string, object> Probe(ProbeOptions options)
        {
            options ??= new ProbeOptions();

            var result = new Dictionary<string, object>
            {
                ["is_playing"] = EditorApplication.isPlaying,
                ["options"] = new Dictionary<string, object>
                {
                    ["npc_profile_id"] = options.NpcProfileId ?? string.Empty,
                    ["npc_name"] = options.NpcName ?? string.Empty,
                    ["player_selector"] = string.IsNullOrWhiteSpace(options.PlayerSelector) ? "nearest_to_npc" : options.PlayerSelector,
                    ["resolve_floor"] = options.ResolveFloor,
                    ["resolve_wall"] = options.ResolveWall,
                    ["floor_ray_distance"] = options.FloorRayDistance,
                    ["wall_max_distance"] = options.WallMaxDistance,
                    ["wall_sphere_radius"] = options.WallSphereRadius,
                    ["surface_layer_mask"] = options.SurfaceLayerMask,
                }
            };

            if (!EditorApplication.isPlaying)
            {
                result["players"] = Array.Empty<object>();
                result["note"] = "Enter Play Mode to resolve runtime players and NPC effect targets.";
                return result;
            }

            var players = new List<EffectTargetResolverService.PlayerTarget>(4);
            EffectTargetResolverService.GetOrderedPlayers(players, includeDead: true);
            result["players"] = BuildPlayers(players);

            bool hasNpc = EffectTargetResolverService.TryResolveNpc(
                options.NpcProfileId,
                options.NpcName,
                out NpcDialogueActor npc,
                out string npcReason
            );
            result["npc"] = hasNpc ? BuildNpc(npc, npcReason) : new Dictionary<string, object>
            {
                ["resolved"] = false,
                ["reason"] = npcReason,
            };

            bool hasPlayer = EffectTargetResolverService.TryResolvePlayer(
                players,
                options.PlayerSelector,
                npc,
                out EffectTargetResolverService.PlayerTarget player,
                out string playerReason
            );
            result["selected_player"] = hasPlayer ? BuildPlayer(player, playerReason) : new Dictionary<string, object>
            {
                ["resolved"] = false,
                ["reason"] = playerReason,
            };

            if (options.ResolveFloor)
            {
                if (
                    hasPlayer
                    && EffectTargetResolverService.TryResolveFloorUnderPlayer(
                        player,
                        Mathf.Max(0.5f, options.FloorRayDistance),
                        options.SurfaceLayerMask,
                        out EffectTargetResolverService.SurfaceTarget floor,
                        out string floorReason
                    )
                )
                {
                    result["floor_under_player"] = BuildSurface(floor, floorReason);
                }
                else
                {
                    result["floor_under_player"] = new Dictionary<string, object>
                    {
                        ["resolved"] = false,
                        ["reason"] = hasPlayer ? "Floor not resolved." : "Player not resolved.",
                    };
                }
            }

            if (options.ResolveWall)
            {
                if (
                    hasNpc
                    && hasPlayer
                    && EffectTargetResolverService.TryResolveWallBetweenNpcAndPlayer(
                        npc,
                        player,
                        Mathf.Max(0.5f, options.WallMaxDistance),
                        Mathf.Max(0f, options.WallSphereRadius),
                        options.SurfaceLayerMask,
                        out EffectTargetResolverService.SurfaceTarget wall,
                        out string wallReason
                    )
                )
                {
                    result["wall_between_npc_and_player"] = BuildSurface(wall, wallReason);
                }
                else
                {
                    result["wall_between_npc_and_player"] = new Dictionary<string, object>
                    {
                        ["resolved"] = false,
                        ["reason"] = !hasNpc
                            ? "NPC not resolved."
                            : !hasPlayer
                                ? "Player not resolved."
                                : "No wall-like surface found between NPC and player.",
                    };
                }
            }

            result["recommendations"] = BuildRecommendations(result);
            return result;
        }

        private static List<Dictionary<string, object>> BuildPlayers(
            List<EffectTargetResolverService.PlayerTarget> players
        )
        {
            var list = new List<Dictionary<string, object>>(players != null ? players.Count : 0);
            if (players == null)
            {
                return list;
            }

            for (int i = 0; i < players.Count; i++)
            {
                list.Add(BuildPlayer(players[i], null));
            }

            return list;
        }

        private static Dictionary<string, object> BuildPlayer(
            EffectTargetResolverService.PlayerTarget player,
            string reason
        )
        {
            var dict = new Dictionary<string, object>();
            if (player == null)
            {
                dict["resolved"] = false;
                dict["reason"] = reason ?? "Player is null.";
                return dict;
            }

            dict["resolved"] = true;
            dict["ordered_index"] = player.OrderedIndex;
            dict["name"] =
                player.Transform != null
                    ? player.Transform.name
                    : (player.Health != null ? player.Health.name : string.Empty);
            dict["path"] =
                player.Transform != null
                    ? EffectTargetResolverService.GetHierarchyPath(player.Transform)
                    : string.Empty;
            dict["position"] = player.Transform != null ? ToVec3(player.Transform.position) : null;
            dict["network_object_id"] =
                player.NetworkObject != null ? player.NetworkObject.NetworkObjectId : 0UL;
            dict["owner_client_id"] =
                player.NetworkObject != null ? player.NetworkObject.OwnerClientId : 0UL;
            dict["health"] = player.Health != null ? player.Health.CurrentHealth : 0f;
            dict["max_health"] = player.Health != null ? player.Health.MaxHealth : 0f;
            dict["is_dead"] = player.Health != null && player.Health.IsDead;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                dict["reason"] = reason;
            }

            return dict;
        }

        private static Dictionary<string, object> BuildNpc(NpcDialogueActor npc, string reason)
        {
            return new Dictionary<string, object>
            {
                ["resolved"] = npc != null,
                ["reason"] = reason ?? string.Empty,
                ["name"] = npc != null ? npc.name : string.Empty,
                ["profile_id"] = npc != null ? npc.ProfileId ?? string.Empty : string.Empty,
                ["position"] = npc != null ? ToVec3(npc.transform.position) : null,
                ["network_object_id"] =
                    npc != null && npc.NetworkObject != null ? npc.NetworkObject.NetworkObjectId : 0UL,
            };
        }

        private static Dictionary<string, object> BuildSurface(
            EffectTargetResolverService.SurfaceTarget surface,
            string reason
        )
        {
            var dict = new Dictionary<string, object>();
            if (surface == null)
            {
                dict["resolved"] = false;
                dict["reason"] = reason ?? "Surface is null.";
                return dict;
            }

            dict["resolved"] = true;
            dict["reason"] = reason ?? surface.ResolverReason ?? string.Empty;
            dict["classification"] = surface.Classification ?? "unknown";
            dict["point"] = ToVec3(surface.Hit.point);
            dict["normal"] = ToVec3(surface.Hit.normal);
            dict["distance"] = surface.Hit.distance;

            dict["collider"] = surface.Collider == null
                ? null
                : new Dictionary<string, object>
                {
                    ["name"] = surface.Collider.name,
                    ["path"] = EffectTargetResolverService.GetHierarchyPath(surface.Collider.transform),
                    ["type"] = surface.Collider.GetType().Name,
                    ["layer"] =
                        LayerMask.LayerToName(surface.Collider.gameObject.layer)
                        ?? surface.Collider.gameObject.layer.ToString(),
                };

            dict["renderer"] = surface.Renderer == null
                ? null
                : new Dictionary<string, object>
                {
                    ["name"] = surface.Renderer.name,
                    ["path"] = EffectTargetResolverService.GetHierarchyPath(surface.Renderer.transform),
                    ["type"] = surface.Renderer.GetType().Name,
                    ["material_slot"] = surface.MaterialSlotIndex,
                    ["material_count"] =
                        surface.Renderer.sharedMaterials != null
                            ? surface.Renderer.sharedMaterials.Length
                            : 0,
                    ["material_name"] = TryGetMaterialName(surface.Renderer, surface.MaterialSlotIndex),
                };

            dict["effect_surface"] = surface.EffectSurface == null
                ? null
                : new Dictionary<string, object>
                {
                    ["surface_id"] = surface.EffectSurface.SurfaceId,
                    ["surface_type"] = surface.EffectSurface.SurfaceType.ToString(),
                    ["default_material_slot"] = surface.EffectSurface.DefaultMaterialSlot,
                    ["allow_freeze"] = surface.EffectSurface.AllowFreeze,
                    ["allow_burn"] = surface.EffectSurface.AllowBurn,
                    ["allow_decals"] = surface.EffectSurface.AllowDecals,
                };

            return dict;
        }

        private static List<string> BuildRecommendations(Dictionary<string, object> probe)
        {
            var recommendations = new List<string>(4);

            if (
                probe.TryGetValue("floor_under_player", out object floorObj)
                && floorObj is Dictionary<string, object> floorDict
                && floorDict.TryGetValue("resolved", out object floorResolvedObj)
                && floorResolvedObj is bool floorResolved
                && floorResolved
            )
            {
                if (!floorDict.ContainsKey("effect_surface") || floorDict["effect_surface"] == null)
                {
                    recommendations.Add(
                        "Add an EffectSurface component to the resolved floor object so freeze/burn effects can target a stable material slot."
                    );
                }
            }

            if (
                probe.TryGetValue("wall_between_npc_and_player", out object wallObj)
                && wallObj is Dictionary<string, object> wallDict
                && wallDict.TryGetValue("resolved", out object wallResolvedObj)
                && wallResolvedObj is bool wallResolved
                && wallResolved
            )
            {
                if (!wallDict.ContainsKey("effect_surface") || wallDict["effect_surface"] == null)
                {
                    recommendations.Add(
                        "Add an EffectSurface component to the resolved wall object to enable deterministic wall burn/freeze material effects."
                    );
                }
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add(
                    "Target resolution looks healthy. Next step: route dialogue effect intents through the resolver before effect execution."
                );
            }

            return recommendations;
        }

        private static string TryGetMaterialName(Renderer renderer, int slot)
        {
            if (
                renderer == null
                || renderer.sharedMaterials == null
                || renderer.sharedMaterials.Length == 0
            )
            {
                return string.Empty;
            }

            int clamped = Mathf.Clamp(slot, 0, renderer.sharedMaterials.Length - 1);
            Material mat = renderer.sharedMaterials[clamped];
            return mat != null ? mat.name : string.Empty;
        }

        private static float[] ToVec3(Vector3 v)
        {
            return new[] { v.x, v.y, v.z };
        }
    }
}
