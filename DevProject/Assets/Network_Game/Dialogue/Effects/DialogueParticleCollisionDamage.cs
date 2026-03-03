using System.Collections.Generic;
using Network_Game.Combat;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Applies server-authoritative combat damage when particle collisions hit actors.
    /// Attach to the same GameObject that owns a ParticleSystem with collision messages enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueParticleCollisionDamage : MonoBehaviour
    {
        private const int DefaultOverlapBufferSize = 32;
        private const int MaxOverlapBufferSize = 1024;

        private ulong m_SourceNetworkObjectId;
        private ulong m_TargetNetworkObjectId;
        private bool m_RestrictDamageToTarget;
        private float m_DamagePerHit;
        private float m_HitCooldownSeconds;
        private bool m_AffectPlayerOnly;
        private string m_DamageType = "effect";
        private bool m_LogDebug;
        private readonly Dictionary<EntityId, float> m_NextHitAtByTarget = new Dictionary<EntityId, float>();
        private Collider[] m_OverlapBuffer = new Collider[DefaultOverlapBufferSize];
        private float m_ProximityRadius = 0.5f;
        private float m_ProximitySweepIntervalSeconds = 0.1f;
        private float m_NextProximitySweepAt;

        public void Configure(
            ulong sourceNetworkObjectId,
            ulong targetNetworkObjectId,
            bool restrictDamageToTarget,
            float damagePerHit,
            float hitCooldownSeconds,
            float proximityRadius,
            bool affectPlayerOnly,
            string damageType,
            bool logDebug
        )
        {
            m_SourceNetworkObjectId = sourceNetworkObjectId;
            m_TargetNetworkObjectId = targetNetworkObjectId;
            m_RestrictDamageToTarget = restrictDamageToTarget && targetNetworkObjectId != 0UL;
            m_DamagePerHit = Mathf.Max(0f, damagePerHit);
            m_HitCooldownSeconds = Mathf.Max(0.02f, hitCooldownSeconds);
            m_ProximityRadius = Mathf.Clamp(proximityRadius, 0.15f, 5f);
            m_ProximitySweepIntervalSeconds = Mathf.Clamp(m_HitCooldownSeconds * 0.5f, 0.05f, 0.5f);
            m_AffectPlayerOnly = affectPlayerOnly;
            m_DamageType = string.IsNullOrWhiteSpace(damageType) ? "effect" : damageType.Trim();
            m_LogDebug = logDebug;
            m_NextHitAtByTarget.Clear();
            m_NextProximitySweepAt = Time.time;
        }

        private void Update()
        {
            if (!HasServerAuthority() || m_DamagePerHit <= 0f)
            {
                return;
            }

            float now = Time.time;
            if (now < m_NextProximitySweepAt)
            {
                return;
            }

            m_NextProximitySweepAt = now + m_ProximitySweepIntervalSeconds;
            int hitCount = CollectOverlapHits(transform.position, m_ProximityRadius);
            if (hitCount <= 0)
            {
                return;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = m_OverlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                CombatHealth health = col.GetComponentInParent<CombatHealth>();
                TryApplyDamage(health, "particle_proximity");
            }
        }

        private void OnParticleCollision(GameObject other)
        {
            if (!HasServerAuthority() || other == null || m_DamagePerHit <= 0f)
            {
                return;
            }

            CombatHealth health = other.GetComponentInParent<CombatHealth>();
            TryApplyDamage(health, "particle_collision");
        }

        private void TryApplyDamage(CombatHealth health, string hitMode)
        {
            if (health == null)
            {
                return;
            }

            NetworkObject targetNetworkObject = health.GetComponent<NetworkObject>();
            if (targetNetworkObject != null)
            {
                if (targetNetworkObject.NetworkObjectId == m_SourceNetworkObjectId)
                {
                    return;
                }

                if (
                    m_RestrictDamageToTarget
                    && targetNetworkObject.NetworkObjectId != m_TargetNetworkObjectId
                )
                {
                    return;
                }

                if (m_AffectPlayerOnly && !IsPlayerObject(targetNetworkObject.gameObject))
                {
                    return;
                }
            }
            else if (m_AffectPlayerOnly || m_RestrictDamageToTarget)
            {
                return;
            }

            EntityId targetId = health.GetEntityId();
            float now = Time.time;
            if (
                m_NextHitAtByTarget.TryGetValue(targetId, out float nextAllowedTime)
                && now < nextAllowedTime
            )
            {
                return;
            }

            m_NextHitAtByTarget[targetId] = now + m_HitCooldownSeconds;
            health.ApplyDamage(m_DamagePerHit, m_SourceNetworkObjectId, m_DamageType);

            if (m_LogDebug)
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Particle collision damage",
                        ("target", health.gameObject.name),
                        ("damage", m_DamagePerHit.ToString("F2")),
                        ("cooldown", m_HitCooldownSeconds.ToString("F2")),
                        ("source", m_SourceNetworkObjectId),
                        ("targetLock", m_TargetNetworkObjectId),
                        ("strictTarget", m_RestrictDamageToTarget),
                        ("type", m_DamageType),
                        ("mode", hitMode)
                    )
                );
            }
        }

        private static bool HasServerAuthority()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                return true;
            }

            return networkManager.IsServer;
        }

        private static bool IsPlayerObject(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.CompareTag("Player"))
            {
                return true;
            }

            return target.GetComponent<Network_Game.ThirdPersonController.ThirdPersonController>()
                != null;
        }

        private int CollectOverlapHits(Vector3 center, float radius)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                center,
                radius,
                m_OverlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide
            );

            if (hitCount < m_OverlapBuffer.Length || m_OverlapBuffer.Length >= MaxOverlapBufferSize)
            {
                return hitCount;
            }

            int expandedSize = Mathf.Min(m_OverlapBuffer.Length * 2, MaxOverlapBufferSize);
            m_OverlapBuffer = new Collider[expandedSize];
            return Physics.OverlapSphereNonAlloc(
                center,
                radius,
                m_OverlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide
            );
        }
    }
}
