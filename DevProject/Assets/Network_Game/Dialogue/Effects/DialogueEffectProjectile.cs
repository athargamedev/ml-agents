using System.Collections.Generic;
using Network_Game.Combat;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Lightweight projectile driver for dialogue-triggered effects.
    /// Movement can run on all clients for visuals; damage is server-authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public class DialogueEffectProjectile : MonoBehaviour
    {
        private const float k_SpawnGraceSeconds = 0.15f;
        private const int DefaultOverlapBufferSize = 32;
        private const int MaxOverlapBufferSize = 1024;

        private ulong m_SourceNetworkObjectId;
        private ulong m_TargetNetworkObjectId;
        private bool m_RestrictDamageToTarget;
        private Transform m_TargetTransform;
        private bool m_EnableHoming;
        private float m_Speed;
        private float m_HomingTurnRateDegrees;
        private float m_DamageAmount;
        private float m_DamageRadius;
        private float m_LifetimeSeconds;
        private bool m_AffectPlayerOnly;
        private string m_DamageType = "effect";
        private float m_SpawnTime;
        private bool m_IsConfigured;
        private bool m_LogDebug;
        private Collider[] m_SourceColliders;
        private Collider[] m_OverlapBuffer = new Collider[DefaultOverlapBufferSize];
        private readonly HashSet<CombatHealth> m_VisitedTargets = new HashSet<CombatHealth>();

        public void Configure(
            ulong sourceNetworkObjectId,
            ulong targetNetworkObjectId,
            Transform targetTransform,
            bool enableHoming,
            float speed,
            float homingTurnRateDegrees,
            float damageAmount,
            float damageRadius,
            float lifetimeSeconds,
            bool affectPlayerOnly,
            bool restrictDamageToTarget,
            string damageType,
            bool logDebug
        )
        {
            m_SourceNetworkObjectId = sourceNetworkObjectId;
            m_TargetNetworkObjectId = targetNetworkObjectId;
            m_TargetTransform = targetTransform;
            m_EnableHoming = enableHoming;
            m_Speed = Mathf.Max(0.1f, speed);
            m_HomingTurnRateDegrees = Mathf.Max(0f, homingTurnRateDegrees);
            m_DamageAmount = Mathf.Max(0f, damageAmount);
            m_DamageRadius = Mathf.Max(0.1f, damageRadius);
            m_LifetimeSeconds = Mathf.Max(0.2f, lifetimeSeconds);
            m_AffectPlayerOnly = affectPlayerOnly;
            m_RestrictDamageToTarget = restrictDamageToTarget && targetNetworkObjectId != 0UL;
            m_DamageType = string.IsNullOrWhiteSpace(damageType) ? "effect" : damageType.Trim();
            m_LogDebug = logDebug;
            m_SpawnTime = Time.time;
            m_IsConfigured = true;

            ResolveSourceColliders();
        }

        private void ResolveSourceColliders()
        {
            if (m_SourceNetworkObjectId == 0)
            {
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (
                networkManager == null
                || !networkManager.IsListening
                || networkManager.SpawnManager == null
            )
            {
                return;
            }

            if (
                networkManager.SpawnManager.SpawnedObjects.TryGetValue(
                    m_SourceNetworkObjectId,
                    out NetworkObject sourceObject
                )
                && sourceObject != null
            )
            {
                m_SourceColliders = sourceObject.GetComponentsInChildren<Collider>();
            }
        }

        private void Update()
        {
            if (!m_IsConfigured)
            {
                return;
            }

            float elapsed = Time.time - m_SpawnTime;
            if (elapsed >= m_LifetimeSeconds)
            {
                Impact(transform.position);
                return;
            }

            RefreshTargetTransform();

            Vector3 direction = transform.forward;
            if (m_EnableHoming && m_TargetTransform != null)
            {
                Vector3 toTarget = m_TargetTransform.position - transform.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(
                        toTarget.normalized,
                        Vector3.up
                    );
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        targetRotation,
                        m_HomingTurnRateDegrees * Time.deltaTime
                    );
                    direction = transform.forward;
                }

                if (toTarget.magnitude <= Mathf.Max(0.4f, m_DamageRadius))
                {
                    Impact(transform.position);
                    return;
                }
            }

            float stepDistance = m_Speed * Time.deltaTime;

            // Skip SphereCast during grace period to avoid self-collision with NPC
            if (elapsed > k_SpawnGraceSeconds)
            {
                if (
                    Physics.SphereCast(
                        transform.position,
                        Mathf.Max(0.05f, m_DamageRadius * 0.35f),
                        direction,
                        out RaycastHit hit,
                        stepDistance,
                        ~0,
                        QueryTriggerInteraction.Ignore
                        ) && !IsSourceCollider(hit.collider)
                )
                {
                    transform.position = hit.point;
                    Impact(hit.point);
                    return;
                }
            }

            transform.position += direction * stepDistance;
        }

        private void RefreshTargetTransform()
        {
            if (m_TargetTransform != null || m_TargetNetworkObjectId == 0)
            {
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (
                networkManager == null
                || !networkManager.IsListening
                || networkManager.SpawnManager == null
            )
            {
                return;
            }

            if (
                networkManager.SpawnManager.SpawnedObjects.TryGetValue(
                    m_TargetNetworkObjectId,
                    out NetworkObject targetObject
                )
                && targetObject != null
            )
            {
                m_TargetTransform = targetObject.transform;
            }
        }

        private bool IsSourceCollider(Collider col)
        {
            if (col == null || m_SourceColliders == null || m_SourceColliders.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < m_SourceColliders.Length; i++)
            {
                if (m_SourceColliders[i] == col)
                {
                    return true;
                }
            }

            return false;
        }

        private void Impact(Vector3 position)
        {
            if (HasServerAuthority())
            {
                ApplyDamageAt(position);
            }

            if (m_LogDebug)
            {
                NGLog.Info(
                    "DialogueFX",
                    NGLog.Format(
                        "Projectile impact",
                        ("position", position),
                        ("damage", m_DamageAmount.ToString("F2")),
                        ("radius", m_DamageRadius.ToString("F2"))
                    )
                );
            }

            Destroy(gameObject);
        }

        private void ApplyDamageAt(Vector3 position)
        {
            if (m_DamageAmount <= 0f)
            {
                return;
            }

            int hitCount = CollectOverlapHits(position, m_DamageRadius);
            if (hitCount <= 0)
            {
                return;
            }

            m_VisitedTargets.Clear();
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = m_OverlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                CombatHealth health = col.GetComponentInParent<CombatHealth>();
                if (health == null || !m_VisitedTargets.Add(health))
                {
                    continue;
                }

                NetworkObject targetNetworkObject = health.GetComponent<NetworkObject>();
                if (targetNetworkObject != null)
                {
                    if (targetNetworkObject.NetworkObjectId == m_SourceNetworkObjectId)
                    {
                        continue;
                    }

                    if (
                        m_RestrictDamageToTarget
                        && targetNetworkObject.NetworkObjectId != m_TargetNetworkObjectId
                    )
                    {
                        continue;
                    }

                    if (m_AffectPlayerOnly && !IsPlayerObject(targetNetworkObject.gameObject))
                    {
                        continue;
                    }
                }
                else if (m_AffectPlayerOnly || m_RestrictDamageToTarget)
                {
                    continue;
                }

                health.ApplyDamage(m_DamageAmount, m_SourceNetworkObjectId, m_DamageType);
            }
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

        private static bool HasServerAuthority()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                return true;
            }

            return networkManager.IsServer;
        }
    }
}
