using System;
using Network_Game.Diagnostics;
using Network_Game.Dialogue.Effects;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Converts the last completed dialogue response for a specific NPC into a compact,
    /// deterministic animation context snapshot suitable for ML-Agents observations.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueAnimationContextBuilder : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField]
        private NetworkObject m_TargetNpc;

        [SerializeField]
        private bool m_OnlyUserInitiated = true;

        [Header("Timing")]
        [SerializeField]
        [Min(0.25f)]
        private float m_FreshWindowSeconds = 4f;

        [SerializeField]
        [Min(0.1f)]
        private float m_SpeakingWindowSeconds = 2.25f;

        [Header("Diagnostics")]
        [SerializeField]
        private bool m_LogDebug;

        private DialogueAnimationContextSnapshot m_LastSnapshot =
            DialogueAnimationContextSnapshot.Empty;
        private float m_LastUpdateTime = float.NegativeInfinity;
        private string m_LastResponsePreview = string.Empty;

        public DialogueAnimationContextSnapshot CurrentSnapshot => BuildLiveSnapshot();

        public string LastResponsePreview => m_LastResponsePreview;

        private void Awake()
        {
            ResolveTargetNpc();
        }

        private void OnEnable()
        {
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
        }

        private void OnDisable()
        {
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
        }

        public void InjectSyntheticContext(string responseText)
        {
            ApplyResponseText(responseText);
        }

        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (response.Status != NetworkDialogueService.DialogueStatus.Completed)
            {
                return;
            }

            if (m_OnlyUserInitiated && !response.Request.IsUserInitiated)
            {
                return;
            }

            if (!MatchesSpeaker(response.Request.SpeakerNetworkId))
            {
                return;
            }

            ApplyResponseText(response.ResponseText);
        }

        private void ApplyResponseText(string responseText)
        {
            string stripped = responseText ?? string.Empty;
            try
            {
                stripped = EffectParser.StripTags(stripped);
            }
            catch
            {
                // If tag stripping fails, still use the raw response as context.
            }

            stripped = DialogueAnimationDecisionPolicy.StripAnimationTags(stripped);

            stripped = stripped.Trim();
            m_LastUpdateTime = Time.time;
            m_LastResponsePreview = BuildPreview(stripped);
            m_LastSnapshot = new DialogueAnimationContextSnapshot
            {
                Tone = ClassifyTone(stripped),
                Intensity = EstimateIntensity(stripped),
                SecondsSinceUpdate = 0f,
                ResponseLengthNormalized = Mathf.Clamp01(stripped.Length / 160f),
                IsFresh = true,
                IsSpeaking = true,
                HasQuestion = stripped.IndexOf('?') >= 0,
                HasExclamation = stripped.IndexOf('!') >= 0,
            };

            if (m_LogDebug)
            {
                NGLog.Info(
                    "DialogueAnim",
                    NGLog.Format(
                        "Updated animation context",
                        ("npc", gameObject.name),
                        ("tone", m_LastSnapshot.Tone.ToString()),
                        ("intensity", m_LastSnapshot.Intensity.ToString("F2")),
                        ("preview", m_LastResponsePreview)
                    )
                );
            }
        }

        private DialogueAnimationContextSnapshot BuildLiveSnapshot()
        {
            if (float.IsNegativeInfinity(m_LastUpdateTime))
            {
                return DialogueAnimationContextSnapshot.Empty;
            }

            float age = Mathf.Max(0f, Time.time - m_LastUpdateTime);
            DialogueAnimationContextSnapshot live = m_LastSnapshot;
            live.SecondsSinceUpdate = age;
            live.IsFresh = age <= Mathf.Max(0.1f, m_FreshWindowSeconds);
            live.IsSpeaking = age <= Mathf.Max(0.05f, m_SpeakingWindowSeconds);
            return live;
        }

        private bool MatchesSpeaker(ulong speakerNetworkId)
        {
            ResolveTargetNpc();
            return m_TargetNpc != null && m_TargetNpc.IsSpawned && m_TargetNpc.NetworkObjectId == speakerNetworkId;
        }

        private void ResolveTargetNpc()
        {
            if (m_TargetNpc != null)
            {
                return;
            }

            m_TargetNpc = GetComponent<NetworkObject>();
            if (m_TargetNpc == null)
            {
                m_TargetNpc = GetComponentInParent<NetworkObject>();
            }
        }

        private static string BuildPreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string compact = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            while (compact.Contains("  ", StringComparison.Ordinal))
            {
                compact = compact.Replace("  ", " ", StringComparison.Ordinal);
            }

            return compact.Length <= 72 ? compact : compact.Substring(0, 72).TrimEnd() + "...";
        }

        private static DialogueAnimationTone ClassifyTone(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return DialogueAnimationTone.Neutral;
            }

            string lower = text.ToLowerInvariant();
            if (ContainsAny(lower, "beware", "warning", "careful", "danger", "caution"))
            {
                return DialogueAnimationTone.Warning;
            }

            if (ContainsAny(lower, "attack", "destroy", "burn", "fool", "enemy", "now"))
            {
                return DialogueAnimationTone.Aggressive;
            }

            if (
                lower.IndexOf('?') >= 0
                || ContainsAny(lower, "why", "how", "what", "where", "when")
            )
            {
                return DialogueAnimationTone.Question;
            }

            if (ContainsAny(lower, "hello", "greetings", "welcome", "well met", "hail"))
            {
                return DialogueAnimationTone.Greeting;
            }

            if (ContainsAny(lower, "thank", "good", "peace", "excellent", "glad", "well done"))
            {
                return DialogueAnimationTone.Positive;
            }

            return DialogueAnimationTone.Neutral;
        }

        private static float EstimateIntensity(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0f;
            }

            float intensity = 0.15f;
            int exclamations = CountChar(text, '!');
            int questions = CountChar(text, '?');
            if (exclamations > 0)
            {
                intensity += Mathf.Min(0.45f, exclamations * 0.15f);
            }

            if (questions > 0)
            {
                intensity += Mathf.Min(0.2f, questions * 0.06f);
            }

            if (text.Length > 120)
            {
                intensity += 0.15f;
            }

            if (ContainsAny(text.ToLowerInvariant(), "must", "now", "immediately", "never", "always"))
            {
                intensity += 0.2f;
            }

            return Mathf.Clamp01(intensity);
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.IndexOf(needles[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountChar(string text, char value)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == value)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
