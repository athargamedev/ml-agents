using System;
using Network_Game.Dialogue;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// Per-NPC ML-Agents policy for selecting lightweight dialogue-driven animation actions.
/// This is intentionally separate from NpcDialogueAgent so animation learning can evolve
/// independently from text/effect decision training.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BehaviorParameters))]
public class NpcDialogueAnimationAgent : Agent
{
    private const string DefaultBehaviorName = "NpcDialogueAnimation";
    private const int ObservationCount = 15;
    private enum AnimationAuthorityMode
    {
        ObserveOnly = 0,
        SuggestOnly = 1,
        AgentAuthority = 2,
    }

    private const int ActionCount = 5;

    [Header("References")]
    [SerializeField]
    private Transform m_PlayerTransform;

    [SerializeField]
    private GameStateProvider m_GameState;

    [SerializeField]
    private DialogueAnimationContextBuilder m_ContextBuilder;

    [SerializeField]
    private NpcDialogueAnimationController m_AnimationController;

    [Header("Mode")]
    [SerializeField]
    private AnimationAuthorityMode m_AuthorityMode = AnimationAuthorityMode.SuggestOnly;

    [Header("Decisioning")]
    [SerializeField]
    private bool m_AutoAddDecisionRequester = true;

    [SerializeField]
    [Range(1, 20)]
    private int m_DecisionPeriod = 5;

    [SerializeField]
    private bool m_TakeActionsBetweenDecisions;

    [SerializeField]
    private bool m_RequestDecisionOnFreshDialogue = true;

    [Header("Observations")]
    [SerializeField]
    [Min(1f)]
    private float m_MaxInteractionDistance = 10f;

    [SerializeField]
    [Min(0.25f)]
    private float m_ActionCadenceNormalizeSeconds = 2f;

    [Header("Training Rewards")]
    [SerializeField]
    private float m_ContextMatchReward = 0.02f;

    [SerializeField]
    private float m_ContextMismatchPenalty = 0.01f;

    [SerializeField]
    private float m_StaleContextPenalty = 0.008f;

    [SerializeField]
    private float m_ControllerSuccessReward = 0.01f;

    [SerializeField]
    private float m_ControllerFailurePenalty = 0.02f;

    [SerializeField]
    private float m_RepetitionPenalty = 0.005f;

    [Header("Episode Bounds")]
    [SerializeField]
    [Min(5f)]
    private float m_MaxEpisodeSeconds = 45f;

    [SerializeField]
    [Min(8)]
    private int m_MaxDecisionsPerEpisode = 256;

    [Header("Diagnostics")]
    [SerializeField]
    private bool m_LogRewardComponents = true;

    [SerializeField]
    private bool m_LogSuggestions;

    private DecisionRequester m_DecisionRequester;
    private StatsRecorder m_StatsRecorder;
    private Unity.Netcode.NetworkObject m_NetworkObject;
    private bool m_RequestDecisionQueued;
    private float m_EpisodeSeconds;
    private int m_DecisionCount;
    private DialogueAnimationAction m_LastChosenAction = DialogueAnimationAction.HoldNeutral;
    private int m_RepeatCount;

    private bool CanDriveAnimator => m_AuthorityMode == AnimationAuthorityMode.AgentAuthority;
    public bool DrivesAnimator => CanDriveAnimator;

    public override void Initialize()
    {
        m_StatsRecorder = Academy.Instance != null ? Academy.Instance.StatsRecorder : null;
        m_NetworkObject = GetComponent<Unity.Netcode.NetworkObject>();
        EnsureBehaviorParameters();

        if (m_ContextBuilder == null)
        {
            m_ContextBuilder = GetComponent<DialogueAnimationContextBuilder>();
            if (m_ContextBuilder == null)
            {
                m_ContextBuilder = gameObject.AddComponent<DialogueAnimationContextBuilder>();
            }
        }

        if (m_AnimationController == null)
        {
            m_AnimationController = GetComponent<NpcDialogueAnimationController>();
            if (m_AnimationController == null)
            {
                m_AnimationController = gameObject.AddComponent<NpcDialogueAnimationController>();
            }
        }

        if (m_GameState == null)
        {
            m_GameState = FindGameStateProvider();
        }

        TryResolvePlayerTransform();
        EnsureDecisionRequester();
        NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
    }

    public override void OnEpisodeBegin()
    {
        m_RequestDecisionQueued = false;
        m_EpisodeSeconds = 0f;
        m_DecisionCount = 0;
        m_LastChosenAction = DialogueAnimationAction.HoldNeutral;
        m_RepeatCount = 0;
        TryResolvePlayerTransform();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        DialogueAnimationContextSnapshot snapshot =
            m_ContextBuilder != null ? m_ContextBuilder.CurrentSnapshot : DialogueAnimationContextSnapshot.Empty;

        sensor.AddObservation(GetProximityObservation());                                // [0]
        sensor.AddObservation(m_GameState != null ? m_GameState.NormalizedPlayerHealth : 1f); // [1]
        sensor.AddObservation(m_GameState != null && m_GameState.IsPlayerInCombat ? 1f : 0f); // [2]
        sensor.AddObservation(snapshot.IsFresh ? 1f : 0f);                              // [3]
        sensor.AddObservation(snapshot.IsSpeaking ? 1f : 0f);                           // [4]
        sensor.AddObservation(snapshot.Intensity);                                       // [5]
        sensor.AddObservation(snapshot.ResponseLengthNormalized);                        // [6]
        sensor.AddObservation(snapshot.Tone == DialogueAnimationTone.Greeting ? 1f : 0f);   // [7]
        sensor.AddObservation(snapshot.Tone == DialogueAnimationTone.Question ? 1f : 0f);   // [8]
        sensor.AddObservation(snapshot.Tone == DialogueAnimationTone.Warning ? 1f : 0f);    // [9]
        sensor.AddObservation(snapshot.Tone == DialogueAnimationTone.Aggressive ? 1f : 0f); // [10]
        sensor.AddObservation(snapshot.Tone == DialogueAnimationTone.Positive ? 1f : 0f);   // [11]
        sensor.AddObservation(
            m_AnimationController != null
            ? (float)m_AnimationController.CurrentAction / (ActionCount - 1)
            : 0f
        );                                                                               // [12]
        sensor.AddObservation(
            m_AnimationController != null && m_AnimationController.IsReadyForAction ? 1f : 0f
        );                                                                               // [13]
        sensor.AddObservation(
            m_AnimationController == null
            ? 1f
            : Mathf.Clamp01(
                m_AnimationController.SecondsSinceLastAction
                / Mathf.Max(0.1f, m_ActionCadenceNormalizeSeconds)
            )
        );                                                                               // [14]
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        m_EpisodeSeconds += Time.fixedDeltaTime;
        m_DecisionCount++;

        int actionIndex = 0;
        if (actions.DiscreteActions.Length > 0)
        {
            actionIndex = Mathf.Clamp(actions.DiscreteActions[0], 0, ActionCount - 1);
        }

        DialogueAnimationAction chosenAction = (DialogueAnimationAction)actionIndex;
        DialogueAnimationContextSnapshot snapshot =
            m_ContextBuilder != null ? m_ContextBuilder.CurrentSnapshot : DialogueAnimationContextSnapshot.Empty;
        DialogueAnimationAction preferredAction = RecommendAction(snapshot);

        RecordStat("Anim/ChosenAction", actionIndex);
        RecordStat("Anim/PreferredAction", (int)preferredAction);
        RecordStat("Anim/ContextFresh", snapshot.IsFresh ? 1f : 0f);

        if (!snapshot.IsFresh && chosenAction != DialogueAnimationAction.HoldNeutral)
        {
            AddRewardComponent(-m_StaleContextPenalty, "Anim/StaleContextPenalty");
        }
        else if (snapshot.IsFresh && chosenAction == preferredAction)
        {
            AddRewardComponent(m_ContextMatchReward, "Anim/ContextMatch");
        }
        else if (snapshot.IsFresh && chosenAction != DialogueAnimationAction.HoldNeutral)
        {
            AddRewardComponent(-m_ContextMismatchPenalty, "Anim/ContextMismatch");
        }

        if (chosenAction == m_LastChosenAction && chosenAction != DialogueAnimationAction.HoldNeutral)
        {
            m_RepeatCount++;
            if (m_RepeatCount > 1)
            {
                AddRewardComponent(-m_RepetitionPenalty, "Anim/RepetitionPenalty");
            }
        }
        else
        {
            m_LastChosenAction = chosenAction;
            m_RepeatCount = 0;
        }

        if (m_AuthorityMode == AnimationAuthorityMode.SuggestOnly && m_LogSuggestions)
        {
            Debug.Log(
                $"[NpcDialogueAnimationAgent] SuggestOnly | npc={gameObject.name} " +
                $"chosen={chosenAction} preferred={preferredAction} fresh={snapshot.IsFresh}"
            );
        }

        if (CanDriveAnimator && m_AnimationController != null)
        {
            if (m_AnimationController.TryPlayAction(chosenAction, out string reason))
            {
                AddRewardComponent(m_ControllerSuccessReward, "Anim/ControllerSuccess");
            }
            else if (!string.Equals(reason, "cooldown", StringComparison.Ordinal))
            {
                AddRewardComponent(-m_ControllerFailurePenalty, "Anim/ControllerFailure");
            }
        }

        if (m_EpisodeSeconds >= m_MaxEpisodeSeconds || m_DecisionCount >= m_MaxDecisionsPerEpisode)
        {
            EndEpisode();
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (actionMask == null)
        {
            return;
        }

        if (CanDriveAnimator && (m_AnimationController == null || !m_AnimationController.IsReadyForAction))
        {
            for (int i = 1; i < ActionCount; i++)
            {
                actionMask.SetActionEnabled(0, i, false);
            }
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        DialogueAnimationContextSnapshot snapshot =
            m_ContextBuilder != null ? m_ContextBuilder.CurrentSnapshot : DialogueAnimationContextSnapshot.Empty;
        actionsOut.DiscreteActions.Array[0] = (int)RecommendAction(snapshot);
    }

    private void Update()
    {
        if (m_RequestDecisionQueued)
        {
            m_RequestDecisionQueued = false;
            RequestDecision();
        }

        if (m_PlayerTransform == null || !m_PlayerTransform.gameObject.scene.IsValid())
        {
            TryResolvePlayerTransform();
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
    }

    private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
    {
        if (!isActiveAndEnabled || !m_RequestDecisionOnFreshDialogue)
        {
            return;
        }

        if (response.Status != NetworkDialogueService.DialogueStatus.Completed)
        {
            return;
        }

        if (!response.Request.IsUserInitiated)
        {
            return;
        }

        if (!MatchesSpeaker(response.Request.SpeakerNetworkId))
        {
            return;
        }

        m_RequestDecisionQueued = true;
    }

    private bool MatchesSpeaker(ulong speakerNetworkId)
    {
        return m_NetworkObject != null && m_NetworkObject.IsSpawned && m_NetworkObject.NetworkObjectId == speakerNetworkId;
    }

    private DialogueAnimationAction RecommendAction(DialogueAnimationContextSnapshot snapshot)
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
            return m_LastChosenAction == DialogueAnimationAction.TurnLeft
                ? DialogueAnimationAction.TurnRight
                : DialogueAnimationAction.TurnLeft;
        }

        if (snapshot.Intensity >= 0.35f)
        {
            return DialogueAnimationAction.TurnRight;
        }

        return DialogueAnimationAction.HoldNeutral;
    }

    private void EnsureDecisionRequester()
    {
        m_DecisionRequester = GetComponent<DecisionRequester>();
        if (m_DecisionRequester == null && m_AutoAddDecisionRequester)
        {
            m_DecisionRequester = gameObject.AddComponent<DecisionRequester>();
        }

        if (m_DecisionRequester == null)
        {
            return;
        }

        m_DecisionRequester.DecisionPeriod = Mathf.Max(1, m_DecisionPeriod);
        m_DecisionRequester.DecisionStep = Mathf.Clamp(
            m_DecisionRequester.DecisionStep,
            0,
            m_DecisionRequester.DecisionPeriod - 1
        );
        m_DecisionRequester.TakeActionsBetweenDecisions = m_TakeActionsBetweenDecisions;
    }

    private void EnsureBehaviorParameters()
    {
        BehaviorParameters behaviorParameters = GetComponent<BehaviorParameters>();
        if (behaviorParameters == null)
        {
            return;
        }

        bool updated = false;

        if (behaviorParameters.BrainParameters.VectorObservationSize != ObservationCount)
        {
            behaviorParameters.BrainParameters.VectorObservationSize = ObservationCount;
            updated = true;
        }

        ActionSpec actionSpec = behaviorParameters.BrainParameters.ActionSpec;
        bool invalidActionSpec =
            actionSpec.NumContinuousActions != 0
            || actionSpec.NumDiscreteActions != 1
            || actionSpec.BranchSizes == null
            || actionSpec.BranchSizes.Length != 1
            || actionSpec.BranchSizes[0] != ActionCount;

        if (invalidActionSpec)
        {
            behaviorParameters.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(ActionCount);
            updated = true;
        }

        if (string.IsNullOrWhiteSpace(behaviorParameters.BehaviorName))
        {
            behaviorParameters.BehaviorName = DefaultBehaviorName;
            updated = true;
        }

        if (updated)
        {
            Debug.Log(
                $"[NpcDialogueAnimationAgent] Corrected BehaviorParameters on {gameObject.name} " +
                $"(obs={ObservationCount}, actions={ActionCount}, behavior={behaviorParameters.BehaviorName})."
            );
        }
    }

    private void TryResolvePlayerTransform()
    {
        if (m_PlayerTransform != null && m_PlayerTransform.gameObject.scene.IsValid())
        {
            return;
        }

        if (m_GameState == null)
        {
            m_GameState = FindGameStateProvider();
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            m_PlayerTransform = player.transform;
        }
    }

    private float GetProximityObservation()
    {
        if (m_PlayerTransform == null)
        {
            return 0f;
        }

        float distance = Vector3.Distance(transform.position, m_PlayerTransform.position);
        return 1f - Mathf.Clamp01(distance / Mathf.Max(1f, m_MaxInteractionDistance));
    }

    private void AddRewardComponent(float amount, string statName)
    {
        if (Mathf.Approximately(amount, 0f))
        {
            return;
        }

        AddReward(amount);
        RecordStat(statName, amount, StatAggregationMethod.Sum);

        if (m_LogRewardComponents)
        {
            Debug.Log(
                $"[NpcDialogueAnimationAgent][Reward] npc={gameObject.name} " +
                $"amount={amount:+0.000;-0.000;0.000} source={statName} " +
                $"cumulative={GetCumulativeReward():+0.000;-0.000;0.000}"
            );
        }
    }

    private void RecordStat(
        string name,
        float value,
        StatAggregationMethod aggregation = StatAggregationMethod.Average
    )
    {
        if (m_StatsRecorder == null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        m_StatsRecorder.Add($"NpcDialogueAnimation/{name}", value, aggregation);
    }

    private static GameStateProvider FindGameStateProvider()
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindAnyObjectByType<GameStateProvider>(FindObjectsInactive.Exclude);
#else
        return UnityEngine.Object.FindObjectOfType<GameStateProvider>();
#endif
    }
}
