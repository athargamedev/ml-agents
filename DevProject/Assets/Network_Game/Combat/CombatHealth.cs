using System.Collections;
using System;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Combat
{
    [DisallowMultipleComponent]
    public class CombatHealth : NetworkBehaviour
    {
        public struct DamageEvent
        {
            public CombatHealth Target;
            public ulong SourceNetworkObjectId;
            public float DamageAmount;
            public float PreviousHealth;
            public float CurrentHealth;
            public string DamageType;
        }

        public struct HealthChangedEvent
        {
            public CombatHealth Target;
            public float PreviousHealth;
            public float CurrentHealth;
            public float MaxHealth;
        }

        public struct LifeStateEvent
        {
            public CombatHealth Target;
            public float CurrentHealth;
            public float MaxHealth;
        }

        public static event Action<DamageEvent> OnDamageApplied;
        public static event Action<HealthChangedEvent> OnHealthChanged;
        public static event Action<LifeStateEvent> OnDied;
        public static event Action<LifeStateEvent> OnRespawned;

        [SerializeField]
        [Min(1f)]
        private float m_MaxHealth = 100f;

        [SerializeField]
        private bool m_AutoRestoreOnDeath = true;

        [SerializeField]
        [Min(0f)]
        private float m_AutoRestoreDelaySeconds = 2f;

        [SerializeField]
        private bool m_LogDebug;

        [Header("Animation")]
        [SerializeField]
        [Tooltip("Trigger animator parameters when health changes.")]
        private bool m_EnableAnimationTriggers = true;

        [SerializeField]
        [Tooltip("Optional explicit animator reference. Falls back to child animator lookup.")]
        private Animator m_Animator;

        [SerializeField]
        private string m_DamageTrigger = "OnHit";

        [SerializeField]
        private string m_DeathTrigger = "Death";

        [SerializeField]
        private string m_RespawnTrigger = "Respawn";

        [SerializeField]
        [Min(0f)]
        private float m_MinDamageTriggerIntervalSeconds = 0.08f;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Pulse duration when hit/death/respawn parameter is a bool instead of a trigger.")]
        private float m_BoolAnimationPulseSeconds = 0.12f;

        private readonly NetworkVariable<float> m_CurrentHealth = new(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
        private float m_LastDamageTriggerAt = float.MinValue;
        private bool m_HealthCallbackRegistered;
        private readonly Dictionary<string, AnimatorControllerParameterType> m_AnimatorParameters =
            new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
        private readonly Dictionary<string, Coroutine> m_AnimatorBoolPulseCoroutines =
            new Dictionary<string, Coroutine>(StringComparer.Ordinal);
        private readonly HashSet<string> m_MissingAnimatorParamWarnings =
            new HashSet<string>(StringComparer.Ordinal);
        private Coroutine m_RestoreCoroutine;
        private NetworkObject m_CachedNetworkObject;

        public float MaxHealth => Mathf.Max(1f, m_MaxHealth);
        public float CurrentHealth => m_CurrentHealth.Value;
        public float NormalizedHealth => Mathf.Clamp01(CurrentHealth / MaxHealth);
        public bool IsDead => CurrentHealth <= 0f;
        public NetworkObject CachedNetworkObject => m_CachedNetworkObject;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                m_CurrentHealth.Value = MaxHealth;
            }

            RegisterHealthCallback();
        }

        private void Awake()
        {
            m_CachedNetworkObject = GetComponent<NetworkObject>();
            ResolveAnimator();
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                m_CurrentHealth.Value = MaxHealth;
            }
        }

        private void OnEnable()
        {
            CombatHealthRegistry.Register(this);
        }

        private void OnDisable()
        {
            CombatHealthRegistry.Unregister(this);
        }

        public void ApplyDamage(
            float amount,
            ulong sourceNetworkObjectId = 0,
            string damageType = "effect"
        )
        {
            if (amount <= 0f || !CanMutateHealth())
            {
                return;
            }

            float current = Mathf.Max(0f, m_CurrentHealth.Value);
            if (current <= 0f)
            {
                return;
            }

            float next = Mathf.Max(0f, current - amount);
            if (!SetHealthInternal(current, next))
            {
                return;
            }

            EmitDamageEvent(amount, sourceNetworkObjectId, damageType, current, next);
            HandleDamageAppliedSideEffects(amount, sourceNetworkObjectId, damageType, current, next);
        }

        public override void OnNetworkDespawn()
        {
            UnregisterHealthCallback();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            CancelRestoreCoroutine();
            StopAnimatorPulseCoroutines();
            UnregisterHealthCallback();
            CombatHealthRegistry.Unregister(this);
            base.OnDestroy();
        }

        private IEnumerator RestoreAfterDelay(float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }

            if (!CanMutateHealth())
            {
                m_RestoreCoroutine = null;
                yield break;
            }

            float previous = Mathf.Max(0f, m_CurrentHealth.Value);
            SetHealthInternal(previous, MaxHealth);
            m_RestoreCoroutine = null;

            if (m_LogDebug)
            {
                NGLog.Info(
                    "Combat",
                    NGLog.Format(
                        "Target auto-restored",
                        ("target", gameObject.name),
                        ("health", m_CurrentHealth.Value.ToString("F2"))
                    )
                );
            }
        }

        private bool CanMutateHealth()
        {
            return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || IsServer;
        }

        private bool SetHealthInternal(float previous, float next)
        {
            float clampedPrevious = Mathf.Clamp(previous, 0f, MaxHealth);
            float clampedNext = Mathf.Clamp(next, 0f, MaxHealth);
            if (Mathf.Approximately(clampedPrevious, clampedNext))
            {
                return false;
            }

            if (clampedNext > 0f)
            {
                CancelRestoreCoroutine();
            }

            m_CurrentHealth.Value = clampedNext;

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                HandleHealthValueChanged(clampedPrevious, clampedNext);
            }

            return true;
        }

        private void HandleDamageAppliedSideEffects(
            float amount,
            ulong sourceNetworkObjectId,
            string damageType,
            float previous,
            float next
        )
        {
            if (m_LogDebug)
            {
                NGLog.Info(
                    "Combat",
                    NGLog.Format(
                        "Damage applied",
                        ("target", gameObject.name),
                        ("source", sourceNetworkObjectId),
                        ("damage", amount.ToString("F2")),
                        ("type", damageType ?? "effect"),
                        ("health", next.ToString("F2"))
                    )
                );
            }

            if (next > 0f || previous <= 0f)
            {
                return;
            }

            NGLog.Warn(
                "Combat",
                NGLog.Format(
                    "Target defeated",
                    ("target", gameObject.name),
                    ("source", sourceNetworkObjectId),
                    ("type", damageType ?? "effect")
                )
            );

            ScheduleAutoRestoreIfEnabled();
        }

        private void ScheduleAutoRestoreIfEnabled()
        {
            if (!m_AutoRestoreOnDeath)
            {
                return;
            }

            CancelRestoreCoroutine();
            m_RestoreCoroutine = StartCoroutine(
                RestoreAfterDelay(Mathf.Max(0f, m_AutoRestoreDelaySeconds))
            );
        }

        private void CancelRestoreCoroutine()
        {
            if (m_RestoreCoroutine == null)
            {
                return;
            }

            StopCoroutine(m_RestoreCoroutine);
            m_RestoreCoroutine = null;
        }

        private void RegisterHealthCallback()
        {
            if (m_HealthCallbackRegistered)
            {
                return;
            }

            m_CurrentHealth.OnValueChanged += HandleHealthValueChanged;
            m_HealthCallbackRegistered = true;
        }

        private void UnregisterHealthCallback()
        {
            if (!m_HealthCallbackRegistered)
            {
                return;
            }

            m_CurrentHealth.OnValueChanged -= HandleHealthValueChanged;
            m_HealthCallbackRegistered = false;
        }

        private void HandleHealthValueChanged(float previousValue, float currentValue)
        {
            EmitHealthChangedEvent(previousValue, currentValue);

            if (!m_EnableAnimationTriggers)
            {
                EmitLifeStateEvents(previousValue, currentValue);
                return;
            }

            if (m_Animator == null)
            {
                EmitLifeStateEvents(previousValue, currentValue);
                return;
            }

            if (currentValue < previousValue)
            {
                if (currentValue <= 0f && previousValue > 0f)
                {
                    TriggerAnimatorWithAliases(m_DeathTrigger, "Death", "Die", "Dead");
                    EmitLifeStateEvents(previousValue, currentValue);
                    return;
                }

                float now = Time.time;
                if (now - m_LastDamageTriggerAt >= m_MinDamageTriggerIntervalSeconds)
                {
                    m_LastDamageTriggerAt = now;
                    TriggerAnimatorWithAliases(
                        m_DamageTrigger,
                        "OnHit",
                        "Hit",
                        "HitReaction",
                        "YourTrigger"
                    );
                }
                EmitLifeStateEvents(previousValue, currentValue);
                return;
            }

            if (previousValue <= 0f && currentValue > 0f)
            {
                TriggerAnimatorWithAliases(m_RespawnTrigger, "Respawn", "Revive", "Spawn");
            }

            EmitLifeStateEvents(previousValue, currentValue);
        }

        private void ResolveAnimator()
        {
            if (m_Animator != null)
            {
                if (m_AnimatorParameters.Count == 0)
                {
                    CacheAnimatorParameters();
                }
                return;
            }

            m_Animator = GetComponentInChildren<Animator>(true);
            CacheAnimatorParameters();
        }

        private void CacheAnimatorParameters()
        {
            m_AnimatorParameters.Clear();
            m_MissingAnimatorParamWarnings.Clear();

            if (m_Animator == null || m_Animator.parameters == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = m_Animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                m_AnimatorParameters[parameter.name] = parameter.type;
            }
        }

        private void TriggerAnimatorWithAliases(string primary, params string[] aliases)
        {
            if (m_Animator == null)
            {
                return;
            }

            if (TrySetAnimatorParameter(primary, suppressMissingWarning: true))
            {
                return;
            }

            if (aliases == null || aliases.Length == 0)
            {
                return;
            }

            for (int i = 0; i < aliases.Length; i++)
            {
                if (TrySetAnimatorParameter(aliases[i], suppressMissingWarning: true))
                {
                    return;
                }
            }

            TrySetAnimatorParameter(primary, suppressMissingWarning: false);
        }

        private bool TrySetAnimatorParameter(string parameterName, bool suppressMissingWarning)
        {
            if (m_Animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            if (
                !m_AnimatorParameters.TryGetValue(
                    parameterName,
                    out AnimatorControllerParameterType type
                )
            )
            {
                if (!suppressMissingWarning && !m_MissingAnimatorParamWarnings.Contains(parameterName))
                {
                    m_MissingAnimatorParamWarnings.Add(parameterName);
                    NGLog.Warn(
                        "Combat",
                        NGLog.Format(
                            "Animator parameter missing",
                            ("target", gameObject.name),
                            ("animator", m_Animator.name),
                            ("parameter", parameterName)
                        )
                    );
                }
                return false;
            }

            if (type == AnimatorControllerParameterType.Trigger)
            {
                m_Animator.SetTrigger(parameterName);
                return true;
            }

            if (type == AnimatorControllerParameterType.Bool)
            {
                if (
                    m_AnimatorBoolPulseCoroutines.TryGetValue(parameterName, out Coroutine existing)
                    && existing != null
                )
                {
                    StopCoroutine(existing);
                }

                Coroutine pulse = StartCoroutine(PulseAnimatorBool(parameterName));
                m_AnimatorBoolPulseCoroutines[parameterName] = pulse;
                return true;
            }

            return false;
        }

        private IEnumerator PulseAnimatorBool(string parameterName)
        {
            if (m_Animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                yield break;
            }

            m_Animator.SetBool(parameterName, true);
            yield return new WaitForSeconds(Mathf.Max(0.01f, m_BoolAnimationPulseSeconds));

            if (m_Animator != null)
            {
                m_Animator.SetBool(parameterName, false);
            }

            m_AnimatorBoolPulseCoroutines.Remove(parameterName);
        }

        private void StopAnimatorPulseCoroutines()
        {
            if (m_AnimatorBoolPulseCoroutines.Count == 0)
            {
                return;
            }

            foreach (Coroutine pulse in m_AnimatorBoolPulseCoroutines.Values)
            {
                if (pulse != null)
                {
                    StopCoroutine(pulse);
                }
            }

            m_AnimatorBoolPulseCoroutines.Clear();
        }

        private void EmitDamageEvent(
            float amount,
            ulong sourceNetworkObjectId,
            string damageType,
            float previous,
            float current
        )
        {
            Action<DamageEvent> handler = OnDamageApplied;
            if (handler == null)
            {
                return;
            }

            handler(
                new DamageEvent
                {
                    Target = this,
                    SourceNetworkObjectId = sourceNetworkObjectId,
                    DamageAmount = amount,
                    PreviousHealth = previous,
                    CurrentHealth = current,
                    DamageType = string.IsNullOrWhiteSpace(damageType) ? "effect" : damageType,
                }
            );
        }

        private void EmitHealthChangedEvent(float previous, float current)
        {
            Action<HealthChangedEvent> handler = OnHealthChanged;
            if (handler == null)
            {
                return;
            }

            handler(
                new HealthChangedEvent
                {
                    Target = this,
                    PreviousHealth = previous,
                    CurrentHealth = current,
                    MaxHealth = MaxHealth,
                }
            );
        }

        private void EmitLifeStateEvents(float previous, float current)
        {
            if (previous > 0f && current <= 0f)
            {
                Action<LifeStateEvent> diedHandler = OnDied;
                if (diedHandler != null)
                {
                    diedHandler(
                        new LifeStateEvent
                        {
                            Target = this,
                            CurrentHealth = current,
                            MaxHealth = MaxHealth,
                        }
                    );
                }
                return;
            }

            if (previous <= 0f && current > 0f)
            {
                Action<LifeStateEvent> respawnHandler = OnRespawned;
                if (respawnHandler == null)
                {
                    return;
                }

                respawnHandler(
                    new LifeStateEvent
                    {
                        Target = this,
                        CurrentHealth = current,
                        MaxHealth = MaxHealth,
                    }
                );
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_MinDamageTriggerIntervalSeconds < 0f)
            {
                m_MinDamageTriggerIntervalSeconds = 0f;
            }
        }

#endif
    }
}
