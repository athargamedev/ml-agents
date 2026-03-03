using System.Reflection;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Production runtime bridge that plays lightweight self-animations from dialogue context
    /// while leaving the existing [EFFECT:] pipeline untouched. Explicit effect tags take priority.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NpcDialogueAnimationAutoResponder : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private NetworkObject m_TargetNpc;

        [SerializeField]
        private DialogueAnimationContextBuilder m_ContextBuilder;

        [SerializeField]
        private NpcDialogueAnimationController m_AnimationController;

        [Header("Behavior")]
        [SerializeField]
        private bool m_OnlyUserInitiated = true;

        [SerializeField]
        private bool m_SuppressWhenEffectTagPresent = true;

        [SerializeField]
        private bool m_SkipWhenMlAgentHasAuthority = true;

        [Header("Diagnostics")]
        [SerializeField]
        private bool m_LogDebug = true;

        private DialogueAnimationAction m_LastAutoAction = DialogueAnimationAction.HoldNeutral;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
        }

        private void OnDisable()
        {
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
        }

        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            if (!isActiveAndEnabled || response.Status != NetworkDialogueService.DialogueStatus.Completed)
            {
                return;
            }

            if (m_OnlyUserInitiated && !response.Request.IsUserInitiated)
            {
                return;
            }

            ResolveReferences();
            if (!MatchesSpeaker(response.Request.SpeakerNetworkId))
            {
                return;
            }

            if (m_TargetNpc != null && m_TargetNpc.IsSpawned && !m_TargetNpc.IsServer)
            {
                return;
            }

            if (m_SkipWhenMlAgentHasAuthority && HasMlAgentAuthority())
            {
                return;
            }

            if (m_SuppressWhenEffectTagPresent
                && DialogueAnimationDecisionPolicy.ContainsEffectTag(response.ResponseText))
            {
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "DialogueAnim",
                        NGLog.Format(
                            "Skipped auto-animation because response contains effect tag",
                            ("npc", gameObject.name)
                        )
                    );
                }

                return;
            }

            if (m_ContextBuilder == null || m_AnimationController == null)
            {
                return;
            }

            m_ContextBuilder.InjectSyntheticContext(response.ResponseText);
            DialogueAnimationContextSnapshot snapshot = m_ContextBuilder.CurrentSnapshot;
            DialogueAnimationAction action =
                DialogueAnimationDecisionPolicy.RecommendAction(snapshot, m_LastAutoAction);

            if (action == DialogueAnimationAction.HoldNeutral)
            {
                return;
            }

            if (m_AnimationController.TryPlayAction(action, out string reason))
            {
                m_LastAutoAction = action;

                if (m_LogDebug)
                {
                    NGLog.Info(
                        "DialogueAnim",
                        NGLog.Format(
                            "Auto-triggered dialogue animation",
                            ("npc", gameObject.name),
                            ("action", action.ToString()),
                            ("preview", m_ContextBuilder.LastResponsePreview)
                        )
                    );
                }
            }
            else if (m_LogDebug && !string.Equals(reason, "cooldown", System.StringComparison.Ordinal))
            {
                NGLog.Debug(
                    "DialogueAnim",
                    NGLog.Format(
                        "Auto-animation skipped",
                        ("npc", gameObject.name),
                        ("action", action.ToString()),
                        ("reason", reason)
                    )
                );
            }
        }

        private void ResolveReferences()
        {
            if (m_TargetNpc == null)
            {
                m_TargetNpc = GetComponent<NetworkObject>();
            }

            if (m_ContextBuilder == null)
            {
                m_ContextBuilder = GetComponent<DialogueAnimationContextBuilder>();
            }

            if (m_AnimationController == null)
            {
                m_AnimationController = GetComponent<NpcDialogueAnimationController>();
            }

        }

        private bool MatchesSpeaker(ulong speakerNetworkId)
        {
            return m_TargetNpc != null
                && m_TargetNpc.IsSpawned
                && m_TargetNpc.NetworkObjectId == speakerNetworkId;
        }

        private bool HasMlAgentAuthority()
        {
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component == null || component.GetType().Name != "NpcDialogueAnimationAgent")
                {
                    continue;
                }

                PropertyInfo property = component.GetType().GetProperty(
                    "DrivesAnimator",
                    BindingFlags.Instance | BindingFlags.Public
                );
                if (property == null || property.PropertyType != typeof(bool))
                {
                    return false;
                }

                object value = property.GetValue(component, null);
                return value is bool drivesAnimator && drivesAnimator;
            }

            return false;
        }
    }
}
