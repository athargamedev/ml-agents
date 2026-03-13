using UnityEngine;

/// <summary>
/// Configurable profile for dialogue-focused ML-Agents behaviours.
///
/// This ScriptableObject is intended to act as a single place where designers
/// can tune the high-level behaviour configuration for dialogue agents:
/// - Behaviour name / identity
/// - Decision frequency
/// - Reward shaping scales
///
/// The profile is consumed by NpcDialogueAgent (and future dialogue agents)
/// so that multiple scenes / prefabs can share the same tuning.
/// </summary>
[CreateAssetMenu(
    fileName = "DialogueAgentProfile",
    menuName = "Network Game/Dialogue/Dialogue Agent Profile",
    order = 0)]
public class DialogueAgentProfile : ScriptableObject
{
    [Header("Behavior")]
    [Tooltip("ML-Agents behavior name expected by the trainer config (e.g. NpcDialogue).")]
    public string BehaviorName = "NpcDialogue";

    [Header("Decisioning")]
    [Tooltip("How often the agent makes decisions (frames between decisions).")]
    [Min(1)]
    public int DecisionPeriod = 5;

    [Tooltip("Whether to apply actions on intermediate steps between decisions.")]
    public bool TakeActionsBetweenDecisions = true;

    [Header("Training Reward Shaping")]
    [Tooltip("Scale for outcome-based rewards (success/failure of dialogue turns).")]
    public float OutcomeRewardScale = 1f;

    [Tooltip("Scale for normalized player feedback scores (-1..1).")]
    public float FeedbackScoreRewardScale = 0.1f;

    [Tooltip("Bonus for very fast LLM responses (under FastResponseThresholdMs).")]
    public float FastResponseBonus = 0.03f;

    [Tooltip("Small bonus for acceptable-but-not-fast responses.")]
    public float AcceptableResponseBonus = 0.01f;

    [Tooltip("Penalty for very slow responses (over SlowResponseThresholdMs).")]
    public float SlowResponsePenalty = 0.03f;

    [Tooltip("Penalty when a request times out entirely.")]
    public float TimeoutPenalty = 0.08f;

    [Tooltip("Penalty applied per LLM retry attempt.")]
    public float RetryPenaltyPerAttempt = 0.02f;
}

