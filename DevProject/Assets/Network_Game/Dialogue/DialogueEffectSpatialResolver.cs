using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Resolves dialogue effect placement into collision-safe and ground-aware world positions.
    /// This runs on the server before effect RPC dispatch.
    /// </summary>
    public static class DialogueEffectSpatialResolver
    {
        public enum CollisionPolicy
        {
            Strict,
            Relaxed,
            AllowOverlap,
        }

        public struct ResolveRequest
        {
            public Vector3 DesiredPosition;
            public Vector3 DesiredForward;
            public Vector3 FallbackOrigin;
            public Vector3 FallbackForward;
            public float FallbackForwardDistance;
            public bool UseFallback;

            public bool GroundSnap;
            public float GroundProbeUp;
            public float GroundProbeDown;
            public float GroundOffset;
            public LayerMask GroundMask;

            public float ClearanceRadius;
            public LayerMask CollisionMask;
            public CollisionPolicy CollisionPolicy;

            public bool RequireLineOfSight;
            public Vector3 LineOfSightOrigin;
            public LayerMask LineOfSightMask;

            public Transform IgnoreRootA;
            public Transform IgnoreRootB;
            public Transform IgnoreRootC;
        }

        public struct ResolveResult
        {
            public bool IsValid;
            public Vector3 Position;
            public Vector3 Forward;
            public bool UsedFallback;
            public bool GroundSnapped;
            public bool CollisionAdjusted;
            public string Reason;
        }

        private const int kMaxOverlapBuffer = 64;
        private const int kMaxLoSBuffer = 16;
        private const int kSearchRings = 4;
        private const int kSamplesPerRing = 12;
        private static readonly Collider[] s_OverlapBuffer = new Collider[kMaxOverlapBuffer];
        private static readonly RaycastHit[] s_LoSHitBuffer = new RaycastHit[kMaxLoSBuffer];

        public static bool TryParseCollisionPolicy(string rawPolicy, out CollisionPolicy policy)
        {
            policy = CollisionPolicy.Strict;
            if (string.IsNullOrWhiteSpace(rawPolicy))
            {
                return false;
            }

            string normalized = rawPolicy
                .Trim()
                .Replace("-", "_")
                .Replace(" ", "_")
                .ToLowerInvariant();
            switch (normalized)
            {
                case "strict":
                case "block":
                case "blocked":
                case "no_overlap":
                    policy = CollisionPolicy.Strict;
                    return true;
                case "relaxed":
                case "soft":
                case "prefer_clear":
                    policy = CollisionPolicy.Relaxed;
                    return true;
                case "allow":
                case "allow_overlap":
                case "overlap":
                case "ignore_collision":
                    policy = CollisionPolicy.AllowOverlap;
                    return true;
                default:
                    return false;
            }
        }

        public static ResolveResult Resolve(ResolveRequest request)
        {
            Vector3 fallbackForward = NormalizeForward(request.FallbackForward, Vector3.forward);
            Vector3 resolvedForward = NormalizeForward(request.DesiredForward, fallbackForward);
            Vector3 resolvedPosition = request.DesiredPosition;

            bool usedFallback = false;
            bool groundSnapped = false;
            bool collisionAdjusted = false;
            bool hardFailure = false;
            string reason = "ok";

            if (!IsFinite(resolvedPosition))
            {
                resolvedPosition =
                    request.FallbackOrigin
                    + fallbackForward * Mathf.Max(0.25f, request.FallbackForwardDistance);
                usedFallback = true;
                reason = "invalid_desired_position";
            }

            if (request.GroundSnap)
            {
                if (
                    TryProjectToGround(
                        resolvedPosition,
                        request.GroundProbeUp,
                        request.GroundProbeDown,
                        request.GroundOffset,
                        request.GroundMask,
                        out Vector3 projected
                    )
                )
                {
                    resolvedPosition = projected;
                    groundSnapped = true;
                }
                else if (request.UseFallback)
                {
                    resolvedPosition =
                        request.FallbackOrigin
                        + fallbackForward * Mathf.Max(0.25f, request.FallbackForwardDistance);
                    usedFallback = true;
                    reason = AppendReason(reason, "ground_fallback");
                    if (
                        TryProjectToGround(
                            resolvedPosition,
                            request.GroundProbeUp,
                            request.GroundProbeDown,
                            request.GroundOffset,
                            request.GroundMask,
                            out projected
                        )
                    )
                    {
                        resolvedPosition = projected;
                        groundSnapped = true;
                    }
                }
            }

            float clearanceRadius = Mathf.Max(0f, request.ClearanceRadius);
            if (request.CollisionPolicy == CollisionPolicy.Relaxed)
            {
                clearanceRadius *= 0.5f;
            }

            bool requireClearance =
                request.CollisionPolicy != CollisionPolicy.AllowOverlap && clearanceRadius > 0.01f;

            if (
                requireClearance
                && IsBlocked(
                    resolvedPosition,
                    clearanceRadius,
                    request.CollisionMask,
                    request.IgnoreRootA,
                    request.IgnoreRootB,
                    request.IgnoreRootC
                )
            )
            {
                if (
                    TryFindNearbyValidPosition(
                        resolvedPosition,
                        resolvedForward,
                        request,
                        clearanceRadius,
                        out Vector3 nearby
                    )
                )
                {
                    resolvedPosition = nearby;
                    collisionAdjusted = true;
                    reason = AppendReason(reason, "collision_adjusted");
                }
                else if (request.UseFallback)
                {
                    Vector3 fallbackPosition =
                        request.FallbackOrigin
                        + fallbackForward * Mathf.Max(0.25f, request.FallbackForwardDistance);
                    if (request.GroundSnap)
                    {
                        if (
                            TryProjectToGround(
                                fallbackPosition,
                                request.GroundProbeUp,
                                request.GroundProbeDown,
                                request.GroundOffset,
                                request.GroundMask,
                                out Vector3 projectedFallback
                            )
                        )
                        {
                            fallbackPosition = projectedFallback;
                            groundSnapped = true;
                        }
                    }

                    bool fallbackBlocked = IsBlocked(
                        fallbackPosition,
                        clearanceRadius,
                        request.CollisionMask,
                        request.IgnoreRootA,
                        request.IgnoreRootB,
                        request.IgnoreRootC
                    );

                    if (!fallbackBlocked || request.CollisionPolicy == CollisionPolicy.Relaxed)
                    {
                        resolvedPosition = fallbackPosition;
                        usedFallback = true;
                        collisionAdjusted = true;
                        reason = AppendReason(reason, "collision_fallback");
                    }
                    else
                    {
                        reason = AppendReason(reason, "collision_unresolved");
                        if (request.CollisionPolicy == CollisionPolicy.Strict)
                        {
                            hardFailure = true;
                        }
                    }
                }
                else
                {
                    reason = AppendReason(reason, "collision_unresolved");
                    if (request.CollisionPolicy == CollisionPolicy.Strict)
                    {
                        hardFailure = true;
                    }
                }
            }

            if (
                request.RequireLineOfSight
                && !HasLineOfSight(
                    request.LineOfSightOrigin,
                    resolvedPosition,
                    request.LineOfSightMask,
                    request.IgnoreRootA,
                    request.IgnoreRootB,
                    request.IgnoreRootC
                )
            )
            {
                if (
                    TryFindNearbyValidPosition(
                        resolvedPosition,
                        resolvedForward,
                        request,
                        clearanceRadius,
                        out Vector3 nearbyLos
                    )
                )
                {
                    resolvedPosition = nearbyLos;
                    collisionAdjusted = true;
                    reason = AppendReason(reason, "los_adjusted");
                }
                else if (request.UseFallback)
                {
                    Vector3 losFallback =
                        request.FallbackOrigin
                        + fallbackForward * Mathf.Max(0.25f, request.FallbackForwardDistance);

                    if (request.GroundSnap)
                    {
                        if (
                            TryProjectToGround(
                                losFallback,
                                request.GroundProbeUp,
                                request.GroundProbeDown,
                                request.GroundOffset,
                                request.GroundMask,
                                out Vector3 projectedLosFallback
                            )
                        )
                        {
                            losFallback = projectedLosFallback;
                            groundSnapped = true;
                        }
                    }

                    bool fallbackHasLos = HasLineOfSight(
                        request.LineOfSightOrigin,
                        losFallback,
                        request.LineOfSightMask,
                        request.IgnoreRootA,
                        request.IgnoreRootB,
                        request.IgnoreRootC
                    );
                    if (fallbackHasLos)
                    {
                        resolvedPosition = losFallback;
                        usedFallback = true;
                        reason = AppendReason(reason, "los_fallback");
                    }
                    else
                    {
                        reason = AppendReason(reason, "los_unresolved");
                        if (request.CollisionPolicy == CollisionPolicy.Strict)
                        {
                            hardFailure = true;
                        }
                    }
                }
                else
                {
                    reason = AppendReason(reason, "los_blocked");
                    if (request.CollisionPolicy == CollisionPolicy.Strict)
                    {
                        hardFailure = true;
                    }
                }
            }

            if (hardFailure)
            {
                return new ResolveResult
                {
                    IsValid = false,
                    Position = resolvedPosition,
                    Forward = resolvedForward,
                    UsedFallback = usedFallback,
                    GroundSnapped = groundSnapped,
                    CollisionAdjusted = collisionAdjusted,
                    Reason = AppendReason(reason, "policy_rejected"),
                };
            }

            if (!IsFinite(resolvedPosition))
            {
                return new ResolveResult
                {
                    IsValid = false,
                    Position = request.FallbackOrigin,
                    Forward = fallbackForward,
                    UsedFallback = true,
                    GroundSnapped = groundSnapped,
                    CollisionAdjusted = collisionAdjusted,
                    Reason = AppendReason(reason, "invalid_final_position"),
                };
            }

            return new ResolveResult
            {
                IsValid = true,
                Position = resolvedPosition,
                Forward = resolvedForward,
                UsedFallback = usedFallback,
                GroundSnapped = groundSnapped,
                CollisionAdjusted = collisionAdjusted,
                Reason = reason,
            };
        }

        private static bool TryFindNearbyValidPosition(
            Vector3 center,
            Vector3 forward,
            ResolveRequest request,
            float clearanceRadius,
            out Vector3 resolvedPosition
        )
        {
            resolvedPosition = center;
            Vector3 baseForward = NormalizeForward(forward, request.FallbackForward);
            float step = Mathf.Max(0.35f, clearanceRadius * 0.75f);

            for (int ring = 1; ring <= kSearchRings; ring++)
            {
                float distance = ring * step;
                for (int sample = 0; sample < kSamplesPerRing; sample++)
                {
                    float angle = (360f / kSamplesPerRing) * sample;
                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * baseForward;
                    Vector3 candidate = center + dir * distance;

                    if (request.GroundSnap)
                    {
                        if (
                            !TryProjectToGround(
                                candidate,
                                request.GroundProbeUp,
                                request.GroundProbeDown,
                                request.GroundOffset,
                                request.GroundMask,
                                out candidate
                            )
                        )
                        {
                            continue;
                        }
                    }

                    if (
                        IsBlocked(
                            candidate,
                            clearanceRadius,
                            request.CollisionMask,
                            request.IgnoreRootA,
                            request.IgnoreRootB,
                            request.IgnoreRootC
                        )
                    )
                    {
                        continue;
                    }

                    if (
                        request.RequireLineOfSight
                        && !HasLineOfSight(
                            request.LineOfSightOrigin,
                            candidate,
                            request.LineOfSightMask,
                            request.IgnoreRootA,
                            request.IgnoreRootB,
                            request.IgnoreRootC
                        )
                    )
                    {
                        continue;
                    }

                    resolvedPosition = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryProjectToGround(
            Vector3 position,
            float probeUp,
            float probeDown,
            float groundOffset,
            LayerMask groundMask,
            out Vector3 projected
        )
        {
            projected = position;
            float up = Mathf.Max(0.05f, probeUp);
            float down = Mathf.Max(0.05f, probeDown);
            int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;
            Vector3 rayOrigin = position + Vector3.up * up;
            float distance = up + down;
            if (
                Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    distance,
                    mask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                projected = hit.point + Vector3.up * Mathf.Max(0f, groundOffset);
                return true;
            }

            return false;
        }

        private static bool IsBlocked(
            Vector3 position,
            float radius,
            LayerMask collisionMask,
            Transform ignoreRootA,
            Transform ignoreRootB,
            Transform ignoreRootC
        )
        {
            if (radius <= 0.01f)
            {
                return false;
            }

            int mask =
                collisionMask.value == 0 ? Physics.DefaultRaycastLayers : collisionMask.value;
            int count = Physics.OverlapSphereNonAlloc(
                position,
                radius,
                s_OverlapBuffer,
                mask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < count; i++)
            {
                Collider collider = s_OverlapBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                if (IsIgnored(collider, ignoreRootA, ignoreRootB, ignoreRootC))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool HasLineOfSight(
            Vector3 origin,
            Vector3 destination,
            LayerMask mask,
            Transform ignoreRootA,
            Transform ignoreRootB,
            Transform ignoreRootC
        )
        {
            if (!IsFinite(origin) || !IsFinite(destination))
            {
                return false;
            }

            if ((destination - origin).sqrMagnitude <= 0.01f)
            {
                return true;
            }

            int lineMask = mask.value == 0 ? Physics.DefaultRaycastLayers : mask.value;
            Vector3 source = origin + Vector3.up * 0.1f;
            Vector3 target = destination + Vector3.up * 0.1f;
            Vector3 direction = target - source;
            float distance = direction.magnitude;

            int hitCount = Physics.RaycastNonAlloc(
                source,
                direction / distance,
                s_LoSHitBuffer,
                distance,
                lineMask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = s_LoSHitBuffer[i].collider;
                if (col != null && !IsIgnored(col, ignoreRootA, ignoreRootB, ignoreRootC))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIgnored(
            Collider collider,
            Transform ignoreRootA,
            Transform ignoreRootB,
            Transform ignoreRootC
        )
        {
            if (collider == null)
            {
                return false;
            }

            Transform root = collider.transform != null ? collider.transform.root : null;
            return IsSameRoot(root, ignoreRootA)
                || IsSameRoot(root, ignoreRootB)
                || IsSameRoot(root, ignoreRootC);
        }

        private static bool IsSameRoot(Transform a, Transform b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a == b || a == b.root;
        }

        private static Vector3 NormalizeForward(Vector3 preferred, Vector3 fallback)
        {
            Vector3 forward = preferred;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = fallback;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude <= 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static string AppendReason(string baseReason, string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
            {
                return baseReason;
            }

            if (string.IsNullOrWhiteSpace(baseReason))
            {
                return suffix;
            }

            if (baseReason.IndexOf(suffix, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseReason;
            }

            return $"{baseReason}|{suffix}";
        }
    }
}
