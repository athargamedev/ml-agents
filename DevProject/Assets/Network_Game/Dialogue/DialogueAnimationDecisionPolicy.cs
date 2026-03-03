using System;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Shared heuristic policy used by both runtime playback and ML-Agents training.
    /// This keeps the production fallback and the training target aligned.
    /// </summary>
    public static class DialogueAnimationDecisionPolicy
    {
        public static bool ContainsEffectTag(string responseText)
        {
            return !string.IsNullOrWhiteSpace(responseText)
                && responseText.IndexOf("[EFFECT:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static DialogueAnimationAction RecommendAction(
            DialogueAnimationContextSnapshot snapshot,
            DialogueAnimationAction lastChosenAction)
        {
            if (!snapshot.IsFresh)
            {
                return DialogueAnimationAction.HoldNeutral;
            }

            if (
                snapshot.Tone == DialogueAnimationTone.Warning
                || snapshot.Tone == DialogueAnimationTone.Aggressive
                || snapshot.Intensity >= 0.75f
                || snapshot.HasExclamation
            )
            {
                return DialogueAnimationAction.EmphasisReact;
            }

            if (
                snapshot.Tone == DialogueAnimationTone.Greeting
                || snapshot.Tone == DialogueAnimationTone.Positive
            )
            {
                return DialogueAnimationAction.IdleVariant;
            }

            if (snapshot.Tone == DialogueAnimationTone.Question || snapshot.HasQuestion)
            {
                return lastChosenAction == DialogueAnimationAction.TurnLeft
                    ? DialogueAnimationAction.TurnRight
                    : DialogueAnimationAction.TurnLeft;
            }

            if (snapshot.Intensity >= 0.35f)
            {
                return DialogueAnimationAction.TurnRight;
            }

            return DialogueAnimationAction.HoldNeutral;
        }
    }
}
