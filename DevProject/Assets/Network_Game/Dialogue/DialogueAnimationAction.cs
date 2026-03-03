using System;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Small, deterministic dialogue-driven animation action set used by the
    /// animation training path. Keep this intentionally narrow at first.
    /// </summary>
    public enum DialogueAnimationAction
    {
        HoldNeutral = 0,
        IdleVariant = 1,
        TurnLeft = 2,
        TurnRight = 3,
        EmphasisReact = 4,
    }

    public enum DialogueAnimationTone
    {
        Neutral = 0,
        Greeting = 1,
        Question = 2,
        Warning = 3,
        Aggressive = 4,
        Positive = 5,
    }

    [Serializable]
    public struct DialogueAnimationContextSnapshot
    {
        public DialogueAnimationTone Tone;
        public float Intensity;
        public float SecondsSinceUpdate;
        public float ResponseLengthNormalized;
        public bool IsFresh;
        public bool IsSpeaking;
        public bool HasQuestion;
        public bool HasExclamation;

        public static DialogueAnimationContextSnapshot Empty =>
            new DialogueAnimationContextSnapshot
            {
                Tone = DialogueAnimationTone.Neutral,
                Intensity = 0f,
                SecondsSinceUpdate = float.PositiveInfinity,
                ResponseLengthNormalized = 0f,
                IsFresh = false,
                IsSpeaking = false,
                HasQuestion = false,
                HasExclamation = false,
            };
    }
}
