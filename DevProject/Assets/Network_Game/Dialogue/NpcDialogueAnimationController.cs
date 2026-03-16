using System.Collections;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Server-authoritative animation executor for dialogue-driven body language.
    /// Uses a small, deterministic action set over an existing AnimatorController.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NpcDialogueAnimationController : NetworkBehaviour
    {
        [Header("Animator")]
        [SerializeField]
        private Animator m_Animator;

        [SerializeField]
        private bool m_DisableRootMotion = true;

        [Header("Timing")]
        [SerializeField]
        [Min(0.05f)]
        private float m_MinActionIntervalSeconds = 0.35f;

        [SerializeField]
        [Min(0.05f)]
        private float m_TurnPulseSeconds = 0.2f;

        [SerializeField]
        [Min(0.01f)]
        private float m_CrossFadeSeconds = 0.12f;

        [Header("State Names")]
        [SerializeField]
        private string m_IdleVariantStateName = "IdleVariant1";

        [SerializeField]
        private string m_EmphasisStateName = "HitReaction";

        [Header("Parameter Names")]
        [SerializeField]
        private string m_SpeedParam = "Speed";

        [SerializeField]
        private string m_MotionSpeedParam = "MotionSpeed";

        [SerializeField]
        private string m_TurnDeltaParam = "TurnDelta";

        [SerializeField]
        private string m_IdleVariantParam = "IdleVariant";

        [SerializeField]
        private string m_EmphasisTriggerParam = "OnHit";

        [Header("Diagnostics")]
        [SerializeField]
        private bool m_LogDebug;

        private readonly Dictionary<string, AnimatorControllerParameterType> m_ParamTypes =
            new Dictionary<string, AnimatorControllerParameterType>();
        private readonly HashSet<string> m_MissingWarnings = new HashSet<string>();

        private DialogueAnimationAction m_CurrentAction = DialogueAnimationAction.HoldNeutral;
        private float m_LastActionTime = float.NegativeInfinity;
        private Coroutine m_TurnPulseRoutine;

        public DialogueAnimationAction CurrentAction => m_CurrentAction;

        public bool IsReadyForAction =>
            float.IsNegativeInfinity(m_LastActionTime)
            || Time.time - m_LastActionTime >= Mathf.Max(0.01f, m_MinActionIntervalSeconds);

        public float SecondsSinceLastAction =>
            float.IsNegativeInfinity(m_LastActionTime) ? float.PositiveInfinity : Mathf.Max(0f, Time.time - m_LastActionTime);

        private void Awake()
        {
            ResolveAnimator();
        }

        private void OnDisable()
        {
            if (m_TurnPulseRoutine != null)
            {
                StopCoroutine(m_TurnPulseRoutine);
                m_TurnPulseRoutine = null;
            }

            if (m_Animator != null)
            {
                TrySetFloat(m_TurnDeltaParam, 0f);
            }
        }

        public bool TryPlayAction(DialogueAnimationAction action, out string reason)
        {
            ResolveAnimator();
            if (m_Animator == null)
            {
                reason = "missing_animator";
                return false;
            }

            if (IsSpawned && !IsServer)
            {
                reason = "not_server";
                return false;
            }

            if (action != DialogueAnimationAction.HoldNeutral && !IsReadyForAction)
            {
                reason = "cooldown";
                return false;
            }

            bool applied = action switch
            {
                DialogueAnimationAction.HoldNeutral => ApplyHoldNeutral(),
                DialogueAnimationAction.IdleVariant => ApplyIdleVariant(),
                DialogueAnimationAction.TurnLeft => ApplyTurnPulse(-1f),
                DialogueAnimationAction.TurnRight => ApplyTurnPulse(1f),
                DialogueAnimationAction.EmphasisReact => ApplyEmphasisReact(),
                _ => false,
            };

            if (!applied)
            {
                reason = "apply_failed";
                return false;
            }

            m_CurrentAction = action;
            m_LastActionTime = Time.time;
            reason = string.Empty;

            if (m_LogDebug)
            {
                NGLog.Info(
                    "DialogueAnim",
                    NGLog.Format(
                        "Played animation action",
                        ("npc", gameObject.name),
                        ("action", action.ToString())
                    )
                );
            }

            return true;
        }

        private void ResolveAnimator()
        {
            if (m_Animator == null)
            {
                m_Animator = GetComponentInChildren<Animator>(true);
            }

            if (m_Animator == null)
            {
                return;
            }

            if (m_DisableRootMotion && m_Animator.applyRootMotion)
            {
                m_Animator.applyRootMotion = false;
            }

            NetworkAnimator networkAnimator = GetComponent<NetworkAnimator>();
            if (networkAnimator != null && networkAnimator.Animator == null)
            {
                networkAnimator.Animator = m_Animator;
            }

            if (m_ParamTypes.Count == 0)
            {
                CacheParameters();
            }
        }

        private void CacheParameters()
        {
            m_ParamTypes.Clear();
            m_MissingWarnings.Clear();
            if (m_Animator == null || m_Animator.parameters == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = m_Animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                m_ParamTypes[parameter.name] = parameter.type;
            }
        }

        private bool ApplyHoldNeutral()
        {
            if (m_TurnPulseRoutine != null)
            {
                StopCoroutine(m_TurnPulseRoutine);
                m_TurnPulseRoutine = null;
            }

            bool touched = false;
            touched |= TrySetFloat(m_SpeedParam, 0f);
            touched |= TrySetFloat(m_MotionSpeedParam, 0f);
            touched |= TrySetFloat(m_TurnDeltaParam, 0f);
            return touched;
        }

        private bool ApplyIdleVariant()
        {
            if (TryCrossFade(m_IdleVariantStateName))
            {
                return true;
            }

            if (TrySetIntLike(m_IdleVariantParam, 1))
            {
                return true;
            }

            return ApplyHoldNeutral();
        }

        private bool ApplyTurnPulse(float value)
        {
            if (!TrySetFloat(m_SpeedParam, 0f))
            {
                // Not a failure; some rigs may not expose this param.
            }

            if (!TrySetFloat(m_TurnDeltaParam, value))
            {
                return false;
            }

            if (m_TurnPulseRoutine != null)
            {
                StopCoroutine(m_TurnPulseRoutine);
            }

            m_TurnPulseRoutine = StartCoroutine(ResetTurnDeltaAfterDelay());
            return true;
        }

        private bool ApplyEmphasisReact()
        {
            if (TrySetTrigger(m_EmphasisTriggerParam))
            {
                return true;
            }

            return TryCrossFade(m_EmphasisStateName);
        }

        private IEnumerator ResetTurnDeltaAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, m_TurnPulseSeconds));
            TrySetFloat(m_TurnDeltaParam, 0f);
            m_TurnPulseRoutine = null;
        }

        private bool TryCrossFade(string stateName)
        {
            if (m_Animator == null || string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            int stateHash = Animator.StringToHash(stateName);
            if (m_Animator.HasState(0, stateHash))
            {
                m_Animator.CrossFadeInFixedTime(stateHash, Mathf.Max(0f, m_CrossFadeSeconds), 0);
                return true;
            }

            string baseLayerName = $"Base Layer.{stateName}";
            int baseLayerHash = Animator.StringToHash(baseLayerName);
            if (m_Animator.HasState(0, baseLayerHash))
            {
                m_Animator.CrossFadeInFixedTime(baseLayerHash, Mathf.Max(0f, m_CrossFadeSeconds), 0);
                return true;
            }

            WarnMissing(stateName);
            return false;
        }

        private bool TrySetTrigger(string name)
        {
            if (!TryGetParamType(name, out AnimatorControllerParameterType type))
            {
                return false;
            }

            if (type != AnimatorControllerParameterType.Trigger)
            {
                return false;
            }

            m_Animator.ResetTrigger(name);
            m_Animator.SetTrigger(name);
            return true;
        }

        private bool TrySetFloat(string name, float value)
        {
            if (!TryGetParamType(name, out AnimatorControllerParameterType type))
            {
                return false;
            }

            if (type != AnimatorControllerParameterType.Float)
            {
                return false;
            }

            m_Animator.SetFloat(name, value);
            return true;
        }

        private bool TrySetIntLike(string name, int value)
        {
            if (!TryGetParamType(name, out AnimatorControllerParameterType type))
            {
                return false;
            }

            if (type == AnimatorControllerParameterType.Int)
            {
                m_Animator.SetInteger(name, value);
                return true;
            }

            if (type == AnimatorControllerParameterType.Float)
            {
                m_Animator.SetFloat(name, value);
                return true;
            }

            if (type == AnimatorControllerParameterType.Bool)
            {
                m_Animator.SetBool(name, value != 0);
                return true;
            }

            return false;
        }

        private bool TryGetParamType(string name, out AnimatorControllerParameterType type)
        {
            type = AnimatorControllerParameterType.Float;
            if (m_Animator == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (m_ParamTypes.Count == 0)
            {
                CacheParameters();
            }

            if (m_ParamTypes.TryGetValue(name, out type))
            {
                return true;
            }

            WarnMissing(name);
            return false;
        }

        private void WarnMissing(string name)
        {
            if (!m_LogDebug || string.IsNullOrWhiteSpace(name) || m_MissingWarnings.Contains(name))
            {
                return;
            }

            m_MissingWarnings.Add(name);
            NGLog.Warn(
                "DialogueAnim",
                NGLog.Format(
                    "Animator parameter/state missing",
                    ("npc", gameObject.name),
                    ("name", name)
                )
            );
        }
    }
}
