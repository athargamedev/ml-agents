using System;
using System.Collections.Generic;
using Network_Game.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue.Effects
{
    /// <summary>
    /// Resolves semantic effect intents (player, floor, wall) into concrete scene/runtime targets.
    /// This is intentionally independent from dialogue parsing so effect execution can be deterministic.
    /// </summary>
    public static class EffectTargetResolverService
    {
        public sealed class PlayerTarget
        {
            public int OrderedIndex; // 1-based for human-facing P1/P2 naming.
            public CombatHealth Health;
            public NetworkObject NetworkObject;
            public Transform Transform;
        }

        public sealed class SurfaceTarget
        {
            public RaycastHit Hit;
            public Collider Collider;
            public Renderer Renderer;
            public EffectSurface EffectSurface;
            public int MaterialSlotIndex;
            public string Classification; // floor / wall / unknown
            public string ResolverReason;
        }

        private static readonly List<CombatHealth> s_PlayerBuffer = new List<CombatHealth>(8);
        private static readonly Comparison<CombatHealth> s_PlayerComparer = ComparePlayers;

        public static void GetOrderedPlayers(List<PlayerTarget> output, bool includeDead = true)
        {
            if (output == null)
            {
                return;
            }

            output.Clear();
            s_PlayerBuffer.Clear();

            CombatHealthRegistry.CopyTo(s_PlayerBuffer);
            if (s_PlayerBuffer.Count == 0)
            {
#if UNITY_2023_1_OR_NEWER
                CombatHealth[] found = UnityEngine.Object.FindObjectsByType<CombatHealth>(
                    FindObjectsInactive.Exclude
                );
#else
                CombatHealth[] found = UnityEngine.Object.FindObjectsOfType<CombatHealth>();
#endif
                if (found != null && found.Length > 0)
                {
                    for (int i = 0; i < found.Length; i++)
                    {
                        if (found[i] != null)
                        {
                            s_PlayerBuffer.Add(found[i]);
                        }
                    }
                }
            }

            for (int i = s_PlayerBuffer.Count - 1; i >= 0; i--)
            {
                CombatHealth health = s_PlayerBuffer[i];
                if (health == null || !health.isActiveAndEnabled)
                {
                    s_PlayerBuffer.RemoveAt(i);
                    continue;
                }

                NetworkObject no = health.CachedNetworkObject != null ? health.CachedNetworkObject : health.GetComponent<NetworkObject>();
                if (no == null)
                {
                    s_PlayerBuffer.RemoveAt(i);
                    continue;
                }

                if (!includeDead && health.IsDead)
                {
                    s_PlayerBuffer.RemoveAt(i);
                }
            }

            s_PlayerBuffer.Sort(s_PlayerComparer);

            for (int i = 0; i < s_PlayerBuffer.Count; i++)
            {
                CombatHealth health = s_PlayerBuffer[i];
                NetworkObject no = health.CachedNetworkObject != null ? health.CachedNetworkObject : health.GetComponent<NetworkObject>();
                output.Add(new PlayerTarget
                {
                    OrderedIndex = i + 1,
                    Health = health,
                    NetworkObject = no,
                    Transform = health.transform,
                });
            }
        }

        public static bool TryResolveNpc(
            string npcProfileId,
            string npcName,
            out NpcDialogueActor actor,
            out string reason
        )
        {
            actor = null;
            reason = string.Empty;

#if UNITY_2023_1_OR_NEWER
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif
            if (actors == null || actors.Length == 0)
            {
                reason = "No NpcDialogueActor instances found in the scene.";
                return false;
            }

            string wantedProfileId = string.IsNullOrWhiteSpace(npcProfileId) ? null : npcProfileId.Trim();
            string wantedName = string.IsNullOrWhiteSpace(npcName) ? null : npcName.Trim();

            if (wantedProfileId == null && wantedName == null)
            {
                actor = actors[0];
                reason = "No NPC selector provided; using first active NPC.";
                return actor != null;
            }

            for (int i = 0; i < actors.Length; i++)
            {
                NpcDialogueActor candidate = actors[i];
                if (candidate == null)
                {
                    continue;
                }

                if (
                    wantedProfileId != null
                    && string.Equals(candidate.ProfileId, wantedProfileId, StringComparison.OrdinalIgnoreCase)
                )
                {
                    actor = candidate;
                    reason = $"Matched npc_profile_id '{wantedProfileId}'.";
                    return true;
                }

                if (
                    wantedName != null
                    && string.Equals(candidate.name, wantedName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    actor = candidate;
                    reason = $"Matched npc_name '{wantedName}'.";
                    return true;
                }
            }

            reason = wantedProfileId != null
                ? $"NPC profile '{wantedProfileId}' not found."
                : $"NPC '{wantedName}' not found.";
            return false;
        }

        public static bool TryResolvePlayer(
            List<PlayerTarget> orderedPlayers,
            string selector,
            NpcDialogueActor npc,
            out PlayerTarget player,
            out string reason
        )
        {
            player = null;
            reason = string.Empty;

            if (orderedPlayers == null || orderedPlayers.Count == 0)
            {
                reason = "No players available.";
                return false;
            }

            string normalized = string.IsNullOrWhiteSpace(selector)
                ? "nearest_to_npc"
                : selector.Trim().ToLowerInvariant();

            if (normalized == "p1" || normalized == "player1" || normalized == "1")
            {
                player = orderedPlayers[0];
                reason = "Selected P1.";
                return true;
            }

            if (normalized == "p2" || normalized == "player2" || normalized == "2")
            {
                if (orderedPlayers.Count < 2)
                {
                    reason = "P2 requested but only one player is available.";
                    return false;
                }

                player = orderedPlayers[1];
                reason = "Selected P2.";
                return true;
            }

            if (normalized.StartsWith("client:", StringComparison.Ordinal))
            {
                if (!ulong.TryParse(normalized.Substring("client:".Length), out ulong clientId))
                {
                    reason = $"Invalid client selector '{selector}'.";
                    return false;
                }

                for (int i = 0; i < orderedPlayers.Count; i++)
                {
                    if (orderedPlayers[i].NetworkObject != null && orderedPlayers[i].NetworkObject.OwnerClientId == clientId)
                    {
                        player = orderedPlayers[i];
                        reason = $"Matched client:{clientId}.";
                        return true;
                    }
                }

                reason = $"No player with client id {clientId}.";
                return false;
            }

            if (normalized.StartsWith("network:", StringComparison.Ordinal))
            {
                if (!ulong.TryParse(normalized.Substring("network:".Length), out ulong networkObjectId))
                {
                    reason = $"Invalid network selector '{selector}'.";
                    return false;
                }

                for (int i = 0; i < orderedPlayers.Count; i++)
                {
                    if (orderedPlayers[i].NetworkObject != null && orderedPlayers[i].NetworkObject.NetworkObjectId == networkObjectId)
                    {
                        player = orderedPlayers[i];
                        reason = $"Matched network:{networkObjectId}.";
                        return true;
                    }
                }

                reason = $"No player with network object id {networkObjectId}.";
                return false;
            }

            if (normalized == "nearest_to_npc" || normalized == "nearest")
            {
                if (npc == null)
                {
                    player = orderedPlayers[0];
                    reason = "No NPC available for nearest selection; defaulted to P1.";
                    return true;
                }

                float bestDist = float.PositiveInfinity;
                PlayerTarget best = null;
                Vector3 npcPos = npc.transform.position;
                for (int i = 0; i < orderedPlayers.Count; i++)
                {
                    PlayerTarget candidate = orderedPlayers[i];
                    if (candidate?.Transform == null)
                    {
                        continue;
                    }

                    float dist = (candidate.Transform.position - npcPos).sqrMagnitude;
                    if (!(dist < bestDist))
                    {
                        continue;
                    }

                    bestDist = dist;
                    best = candidate;
                }

                if (best != null)
                {
                    player = best;
                    reason = $"Selected nearest player to NPC ({Mathf.Sqrt(bestDist):0.00}m).";
                    return true;
                }
            }

            reason = $"Unsupported player selector '{selector}'. Use p1, p2, nearest_to_npc, client:<id>, or network:<id>.";
            return false;
        }

        public static bool TryResolveFloorUnderPlayer(
            PlayerTarget player,
            float rayDistance,
            int layerMask,
            out SurfaceTarget surface,
            out string reason
        )
        {
            surface = null;
            reason = string.Empty;

            if (player == null || player.Transform == null)
            {
                reason = "Player target is null.";
                return false;
            }

            Vector3 origin = player.Transform.position + Vector3.up * 1.5f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, Mathf.Max(0.25f, rayDistance), layerMask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                reason = "No floor hit found below player.";
                return false;
            }

            Array.Sort(hits, CompareHitsByDistance);

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                Collider collider = hit.collider;
                if (collider == null)
                {
                    continue;
                }

                if (BelongsToEntity(collider.transform, player.Transform))
                {
                    continue;
                }

                if (!IsFloorLikeHit(hit))
                {
                    if (!HasSurfaceType(collider, EffectSurfaceType.Floor))
                    {
                        continue;
                    }
                }

                surface = BuildSurfaceTarget(hit, "floor", "Resolved by downward raycast from player.");
                reason = "Resolved floor below player.";
                return true;
            }

            reason = "Raycast hit colliders, but none matched floor criteria.";
            return false;
        }

        public static bool TryResolveWallBetweenNpcAndPlayer(
            NpcDialogueActor npc,
            PlayerTarget player,
            float maxDistance,
            float sphereRadius,
            int layerMask,
            out SurfaceTarget surface,
            out string reason
        )
        {
            surface = null;
            reason = string.Empty;

            if (npc == null || npc.transform == null)
            {
                reason = "NPC target is null.";
                return false;
            }

            if (player == null || player.Transform == null)
            {
                reason = "Player target is null.";
                return false;
            }

            Vector3 start = npc.transform.position + Vector3.up * 1.2f;
            Vector3 end = player.Transform.position + Vector3.up * 1.0f;
            Vector3 delta = end - start;
            float lineDistance = delta.magnitude;
            if (lineDistance <= 0.05f)
            {
                reason = "NPC and player are too close to resolve a wall ray.";
                return false;
            }

            float distance = Mathf.Min(Mathf.Max(0.5f, maxDistance), lineDistance);
            Vector3 dir = delta / lineDistance;
            RaycastHit[] hits = sphereRadius > 0.001f
                ? Physics.SphereCastAll(start, sphereRadius, dir, distance, layerMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastAll(start, dir, distance, layerMask, QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
            {
                reason = "No collider found on NPC-to-player ray.";
                return false;
            }

            Array.Sort(hits, CompareHitsByDistance);

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                Collider collider = hit.collider;
                if (collider == null)
                {
                    continue;
                }

                if (BelongsToEntity(collider.transform, npc.transform) || BelongsToEntity(collider.transform, player.Transform))
                {
                    continue;
                }

                bool wallLike = IsWallLikeHit(hit) || HasSurfaceType(collider, EffectSurfaceType.Wall);
                if (!wallLike)
                {
                    continue;
                }

                surface = BuildSurfaceTarget(hit, "wall", "Resolved on NPC-to-player line probe.");
                reason = "Resolved wall between NPC and player.";
                return true;
            }

            reason = "Line probe found colliders, but no wall-like surface before the player.";
            return false;
        }

        public static string GetHierarchyPath(Transform t)
        {
            if (t == null)
            {
                return string.Empty;
            }

            string path = t.name;
            Transform current = t.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static SurfaceTarget BuildSurfaceTarget(RaycastHit hit, string classification, string resolverReason)
        {
            Collider collider = hit.collider;
            EffectSurface effectSurface = collider != null ? collider.GetComponentInParent<EffectSurface>() : null;
            Renderer renderer = null;
            int materialSlot = 0;

            if (effectSurface != null)
            {
                effectSurface.TryResolveRenderer(collider, out renderer);
                materialSlot = effectSurface.ResolveMaterialSlot(renderer, -1);
            }
            else if (collider != null)
            {
                renderer = collider.GetComponent<Renderer>() ?? collider.GetComponentInParent<Renderer>();
            }

            if (renderer != null && effectSurface == null)
            {
                materialSlot = Mathf.Clamp(materialSlot, 0, Mathf.Max(0, renderer.sharedMaterials.Length - 1));
            }

            return new SurfaceTarget
            {
                Hit = hit,
                Collider = collider,
                Renderer = renderer,
                EffectSurface = effectSurface,
                MaterialSlotIndex = materialSlot,
                Classification = classification ?? "unknown",
                ResolverReason = resolverReason ?? string.Empty,
            };
        }

        private static bool HasSurfaceType(Collider collider, EffectSurfaceType type)
        {
            if (collider == null)
            {
                return false;
            }

            EffectSurface surface = collider.GetComponentInParent<EffectSurface>();
            return surface != null && surface.SurfaceType == type;
        }

        private static bool IsFloorLikeHit(RaycastHit hit)
        {
            return hit.normal.y >= 0.45f;
        }

        private static bool IsWallLikeHit(RaycastHit hit)
        {
            return Mathf.Abs(hit.normal.y) <= 0.55f;
        }

        private static bool BelongsToEntity(Transform hitTransform, Transform entityTransform)
        {
            if (hitTransform == null || entityTransform == null)
            {
                return false;
            }

            return hitTransform == entityTransform || hitTransform.IsChildOf(entityTransform) || entityTransform.IsChildOf(hitTransform);
        }

        private static int CompareHitsByDistance(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }

        private static int ComparePlayers(CombatHealth a, CombatHealth b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            NetworkObject aNo = a.CachedNetworkObject != null ? a.CachedNetworkObject : a.GetComponent<NetworkObject>();
            NetworkObject bNo = b.CachedNetworkObject != null ? b.CachedNetworkObject : b.GetComponent<NetworkObject>();

            ulong aOwner = aNo != null ? aNo.OwnerClientId : ulong.MaxValue;
            ulong bOwner = bNo != null ? bNo.OwnerClientId : ulong.MaxValue;
            int ownerCompare = aOwner.CompareTo(bOwner);
            if (ownerCompare != 0)
            {
                return ownerCompare;
            }

            ulong aId = aNo != null ? aNo.NetworkObjectId : ulong.MaxValue;
            ulong bId = bNo != null ? bNo.NetworkObjectId : ulong.MaxValue;
            int idCompare = aId.CompareTo(bId);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
