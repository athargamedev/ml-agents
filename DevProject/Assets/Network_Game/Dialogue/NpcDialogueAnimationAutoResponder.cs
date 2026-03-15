using System.Reflection;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Production runtime bridge plays lightweight self-animations from dialogue context
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
            NetworkDialogueService.OnRawDialogueResponse += HandleDialogueResponse;
        }

        private void OnDisable()
        {
            NetworkDialogueService.OnRawDialogueResponse -= HandleDialogueResponse;
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

            if (m_TargetNpc != null && m_TargetNpc.IsSpawned
                && NetworkManager.Singleton != null
                && !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (m_SkipWhenMlAgentHasAuthority && HasMlAgentAuthority())
            {
                return;
            }

            bool prefersAnimationOnly = DialogueAnimationDecisionPolicy.IsLikelyAnimationIntentPrompt(
                response.Request.Prompt
            );
            bool containsEffectTag = DialogueAnimationDecisionPolicy.ContainsEffectTag(
                response.ResponseText
            );

            if (DialogueAnimationDecisionPolicy.TryParseFirstAnimationTag(
                response.ResponseText,
                out var animationIntent))
            {
                if (!animationIntent.TargetsSelf)
                {
                    if (m_LogDebug)
                    {
                        NGLog.Debug(
                            "DialogueAnim",
                            NGLog.Format(
                                "Skipped explicit animation tag because target is not Self",
                                ("npc", gameObject.name),
                                ("target", animationIntent.Target)
                            )
                        );
                    }

                    return;
                }

                if (TryPlayAction(animationIntent.Action, m_ContextBuilder != null
                    ? m_ContextBuilder.LastResponsePreview
                    : string.Empty))
                {
                    return;
                }
            }

            if (prefersAnimationOnly && containsEffectTag)
            {
                if (
                    TryPlayAction(
                        DialogueAnimationAction.EmphasisReact,
                        m_ContextBuilder != null
                        ? m_ContextBuilder.LastResponsePreview
                        : string.Empty
                    )
                )
                {
                    if (m_LogDebug)
                    {
                        NGLog.Debug(
                            "DialogueAnim",
                            NGLog.Format(
                                "Converted effect-like self-animation reply into EmphasisReact fallback",
                                ("npc", gameObject.name)
                            )
                        );
                    }

                    return;
                }
            }

            if (m_SuppressWhenEffectTagPresent
                && !prefersAnimationOnly
                && containsEffectTag)
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

            if (m_LogDebug
                && prefersAnimationOnly
                && containsEffectTag)
            {
                NGLog.Debug(
                    "DialogueAnim",
                    NGLog.Format(
                        "Ignoring effect tag because the user prompt requested self-animation",
                        ("npc", gameObject.name)
                    )
                );
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

            TryPlayAction(action, m_ContextBuilder.LastResponsePreview);
        }

        private bool TryPlayAction(DialogueAnimationAction action, string preview)
        {
            if (m_AnimationController == null)
            {
                return false;
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
                            ("preview", preview)
                        )
                    );
                }

                return true;
            }

            if (m_LogDebug && !string.Equals(reason, "cooldown", System.StringComparison.Ordinal))
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

            return false;
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
