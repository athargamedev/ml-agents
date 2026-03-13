using System;
using System.Collections.Generic;
using Network_Game.Dialogue;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.SideChannels;
using Unity.MLAgents.NpcDialogue;
using UnityEngine;

/// <summary>
/// Scene-level ML-Agents bridge for the NPC dialogue system.
///
/// ── HOW TO SET UP (one per scene, NOT one per NPC) ───────────────────────────
///
///   1. Create an empty GameObject called "DialogueBridge" in your scene.
///   2. Add this component to it.
///   3. Assign in the Inspector:
///        • PlayerTransform  → your Player GameObject's Transform
///        • GameState        → add GameStateProvider to the same GameObject, then drag it here
///   4. BehaviorParameters (added automatically) — configure:
///        • Behavior Name        : NpcDialogue
///        • Vector Observations  : Space Size = 7
///        • Actions              : Discrete Branches = 1, Branch 0 Size = 3
///        • Behavior Type        : Default   (trains when Python connected, runs model otherwise)
///        • Max Step             : 1000      (safety net — prevents episodes from running forever
///                                             if the agent never selects action 2 / EndConversation)
///
/// ── PYTHON SIDE ──────────────────────────────────────────────────────────────
///
///   from mlagents_envs.environment import UnityEnvironment
///   from mlagents.trainers.llm_dialogue_channel import LlmDialogueChannel
///   from mlagents.trainers.llm_bridge_server import make_lmstudio_handler
///
///   channel = LlmDialogueChannel(handler=make_lmstudio_handler())
///   env = UnityEnvironment(side_channels=[channel])
///   env.reset()
///   while True:
///       env.step()
///       channel.flush_responses()   # push LLM replies back to Unity
///
/// ── OBSERVATIONS (7 floats) ──────────────────────────────────────────────────
///   [0] proximity of player to nearest active NPC  (0=far, 1=right next to it)
///   [1] player health normalised                   (0–1)
///   [2] player in combat                           (0 or 1)
///   [3] current conversation turn count / 10       (soft-normalised)
///   [4] time in episode / 60                       (soft-normalised, max 1)
///   [5] conversation phase                         (0=idle, 0.33=initiated, 0.67=responded, 1=resolved)
///   [6] active effect state                        (1=effect just fired, decays exponentially to 0)
///
/// ── ACTIONS (1 discrete branch, size 3) ──────────────────────────────────────
///   0 = idle
///   1 = engage / continue dialogue
///   2 = end conversation and close episode
/// </summary>
[RequireComponent(typeof(BehaviorParameters))]
public class NpcDialogueAgent : Agent
{
    private enum ConversationPhase
    {
        Idle      = 0,  // no active conversation
        Initiated = 1,  // player sent a message, awaiting NPC response
        Responded = 2,  // NPC replied, player can react
        Resolved  = 3,  // effect fired or conversation concluded cleanly
    }

    private enum DialogueRoutingMode
    {
        // Recommended default: preserve the existing NetworkDialogueService backend
        // (LM Studio/OpenAI client + effect tags) and use ML-Agents only for
        // observation/reward/policy decisions.
        ObserveOnly = 0,

        // Experimental: route dialogue text generation through the Python sidechannel.
        // This is useful for bridge testing, but can diverge from the production
        // dialogue/effect parsing path if prompts/response formats differ.
        SideChannelOverride = 1,
    }

    [Header("Profile (Optional)")]
    [Tooltip("Shared configuration for dialogue ML-Agents behaviour and reward shaping.")]
    [SerializeField] private DialogueAgentProfile m_Profile;

    [Header("Player")]
    [Tooltip("Drag your Player GameObject's Transform here.")]
    [SerializeField] private Transform m_PlayerTransform;

    [Header("Optional")]
    [Tooltip("Add GameStateProvider to this GameObject and drag it here for dynamic context.")]
    [SerializeField] private GameStateProvider m_GameState;

    [Tooltip("Distance at which proximity observation reaches 0 (fully out of range).")]
    [SerializeField][Min(1f)] private float m_MaxInteractionDistance = 10f;
    [Tooltip("Minimum proximity required before 'engage' remains an allowed action (0-1).")]
    [SerializeField][Range(0f, 1f)] private float m_MinEngageProximity = 0.05f;

    [Header("Dialogue Routing")]
    [Tooltip("How ML-Agents integrates with dialogue. ObserveOnly keeps the normal dialogue backend/effects path and only learns from outcomes.")]
    [SerializeField] private DialogueRoutingMode m_DialogueRoutingMode = DialogueRoutingMode.ObserveOnly;

    [Header("Decisioning")]
    [Tooltip("Auto-add a DecisionRequester at runtime so Python env.reset()/env.step() receives decisions.")]
    [SerializeField] private bool m_AutoAddDecisionRequester = true;
    // DecisionPeriod=5: make one decision every 5 FixedUpdate frames (~100ms at 50Hz).
    // Was 1 (every frame). At 11s LLM latency the agent made ~550 idle decisions per
    // LLM wait — pure noise. At period=5 that drops to ~110, improving credit assignment.
    [SerializeField][Range(1, 20)] private int m_DecisionPeriod = 5;
    [SerializeField] private bool m_TakeActionsBetweenDecisions = true;

    [Header("Training Reward Shaping")]
    [SerializeField] private float m_OutcomeRewardScale = 1f;
    [SerializeField] private float m_FeedbackScoreRewardScale = 0.1f;
    [SerializeField] private float m_FastResponseBonus = 0.03f;
    [SerializeField] private float m_AcceptableResponseBonus = 0.01f;
    [SerializeField] private float m_SlowResponsePenalty = 0.03f;
    [SerializeField] private float m_TimeoutPenalty = 0.08f;
    [SerializeField] private float m_RetryPenaltyPerAttempt = 0.02f;
    [SerializeField][Min(100f)] private float m_FastResponseThresholdMs = 2000f;
    [SerializeField][Min(100f)] private float m_AcceptableResponseThresholdMs = 6000f;
    [SerializeField][Min(100f)] private float m_SlowResponseThresholdMs = 15000f;

    [Header("Effect Observation")]
    [Tooltip("How quickly the effect-fired observation decays back to 0 after an effect triggers (seconds).")]
    [SerializeField][Min(0.5f)] private float m_EffectDecaySeconds = 8f;

    [Header("Diagnostics")]
    [Tooltip("Logs each reward component added by this agent (source + amount + cumulative reward).")]
    [SerializeField] private bool m_LogRewardComponents = true;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const int   MaxTurnsBeforeForceEnd       = 20;    // P4.1 — action mask
    private const float ConversationTimeoutSeconds   = 30f;   // P4.2 — LM Studio crash guard
    private const float FullArcBonus                 = 0.02f; // P3.4 — Idle→Initiated→Responded→Resolved
    private const float MaxSingleRewardComponent     = 0.5f;  // P3.2 — clip magnitude
    // Small per-step cost for idling within interaction range when no conversation is active.
    // Without this cost, the policy finds the trivially easy strategy: idle forever then end
    // for the small EndConversation reward, never discovering that engaging leads to larger rewards.
    private const float IdleNearNpcCostPerStep       = 0.001f;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private LlmDialogueChannel        m_Channel;
    private SideChannelDialogueClient m_Client;
    private DecisionRequester m_DecisionRequester;
    private StatsRecorder m_StatsRecorder;
    private int   m_TurnCount;
    private float m_EpisodeTime;
    private bool  m_ConversationActive;
    private bool  m_OverrideClientEnabled;
    private readonly HashSet<int> m_RewardedTerminalRequestIds = new HashSet<int>();
    private readonly HashSet<int> m_RewardedTelemetryRequestIds = new HashSet<int>();
    private readonly HashSet<int> m_RewardedFeedbackRequestIds = new HashSet<int>();
    private ConversationPhase m_ConversationPhase = ConversationPhase.Idle;
    private float m_LastEffectFireTime    = float.NegativeInfinity;
    private float m_PhaseInitiatedTime   = float.NegativeInfinity; // P4.2
    private bool m_RuntimeHandlersRegistered;
    private int   m_PlayerResolveAttempts;

    // ── ML-Agents lifecycle ───────────────────────────────────────────────────

    public override void Initialize()
    {
        m_StatsRecorder = Academy.Instance != null ? Academy.Instance.StatsRecorder : null;
        TryResolvePlayerTransform();

        ApplyProfileIfAvailable();

        // Create/register the dialogue sidechannel only when explicitly routing
        // dialogue generation through the Python bridge.
        if (UseSideChannelOverride)
        {
            m_Channel = new LlmDialogueChannel();
            SideChannelManager.RegisterSideChannel(m_Channel);

            m_Client = new SideChannelDialogueClient(m_Channel);
            m_Client.SetGameStateProvider(m_GameState);
            m_Client.OnStructuredDialogueResponseReceived += HandleStructuredDialogueResponse;
        }

        RegisterRuntimeHandlers();

        EnsureDecisionRequester();

        // Inject into the scene's dialogue service via the singleton.
        if (!ApplyRoutingModeToDialogueService())
        {
            Debug.LogWarning(
                "[NpcDialogueAgent] NetworkDialogueService.Instance not found. " +
                "Make sure it is in the scene and has started before this agent."
            );
        }

        Debug.Log(
            $"[NpcDialogueAgent] ML-Agents initialised. RoutingMode={m_DialogueRoutingMode} " +
            $"(sidechannelOverride={UseSideChannelOverride})"
        );
    }

    public override void OnEpisodeBegin()
    {
        TryResolvePlayerTransform();
        m_TurnCount              = 0;
        m_EpisodeTime            = 0f;
        m_ConversationActive     = false;
        m_ConversationPhase      = ConversationPhase.Idle;
        m_LastEffectFireTime     = float.NegativeInfinity;
        m_PhaseInitiatedTime     = float.NegativeInfinity; // P4.2
        m_RewardedTerminalRequestIds.Clear();
        m_RewardedTelemetryRequestIds.Clear();
        m_RewardedFeedbackRequestIds.Clear();
        ApplyRoutingModeToDialogueService();
        // DecisionRequester drives all RequestDecision calls automatically.
        // Do NOT call RequestDecision() here — it causes a double-request at episode start.
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // [0] Player proximity to this GameObject (treat as NPC anchor point)
        float proximity = 0f;
        if (m_PlayerTransform != null)
        {
            float dist = Vector3.Distance(transform.position, m_PlayerTransform.position);
            proximity  = 1f - Mathf.Clamp01(dist / m_MaxInteractionDistance);
        }
        sensor.AddObservation(proximity);                                   // [0]
        sensor.AddObservation(m_GameState != null
            ? m_GameState.NormalizedPlayerHealth : 1f);                     // [1]
        sensor.AddObservation(m_GameState != null
            && m_GameState.IsPlayerInCombat ? 1f : 0f);                    // [2]
        sensor.AddObservation(Mathf.Min(m_TurnCount / 10f, 1f));            // [3]
        sensor.AddObservation(Mathf.Min(m_EpisodeTime / 60f, 1f));         // [4]

        // [5] Conversation phase — where in the dialogue cycle are we right now.
        sensor.AddObservation((int)m_ConversationPhase / 3f);              // [5]

        // [6] Active effect state — 1 when an effect just fired, decays to 0.
        // Uses exponential decay so the policy sees a gradient, not a cliff.
        float timeSinceEffect = Time.time - m_LastEffectFireTime;
        float effectState = float.IsNegativeInfinity(m_LastEffectFireTime)
            ? 0f
            : Mathf.Exp(-timeSinceEffect / m_EffectDecaySeconds);
        sensor.AddObservation(effectState);                                 // [6]
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        m_EpisodeTime += Time.fixedDeltaTime;

        // Per-step idle cost: penalise staying idle when the player is within reach
        // and no conversation is running. This prevents entropy collapse where the
        // policy learns "idle → end" as the dominant strategy.
        if (actions.DiscreteActions[0] == 0
            && !m_ConversationActive
            && GetProximityObservation() >= m_MinEngageProximity)
        {
            AddRewardComponent(-IdleNearNpcCostPerStep, "Action/IdleNearNpcCost");
        }

        switch (actions.DiscreteActions[0])
        {
            case 1: // Engage
            {
                bool overrideChanged = false;
                if (UseSideChannelOverride)
                    overrideChanged = SetOverrideClientEnabled(true);

                if (!m_ConversationActive)
                {
                    m_ConversationActive = true;
                    m_ConversationPhase  = ConversationPhase.Initiated;
                    m_PhaseInitiatedTime = Time.time; // P4.2 — start timeout timer
                    AddRewardComponent(0.02f, "Action/Engage");
                }
                else if (overrideChanged)
                {
                    // Reward restoring the ML-routed backend while a dialogue is active.
                    AddRewardComponent(0.005f, "Action/RestoreOverride");
                }
                break;
            }

            case 2: // End conversation
            {
                if (m_ConversationActive)
                {
                    if (UseSideChannelOverride)
                        SetOverrideClientEnabled(false);
                    m_ConversationActive = false;
                    m_ConversationPhase  = ConversationPhase.Idle;

                    // Do not reward ending immediately after the first turn; that
                    // creates a trivial "one reply then stop" local optimum.
                    if (m_TurnCount > 1)
                    {
                        AddRewardComponent(m_TurnCount * 0.05f, "Action/EndConversation");
                    }

                    // P3.3 — capture stats before EndEpisode() resets them via OnEpisodeBegin().
                    float episodeCumulative = GetCumulativeReward();
                    int   episodeTurns      = m_TurnCount;
                    int   episodeEffects    = float.IsNegativeInfinity(m_LastEffectFireTime) ? 0 : 1;

                    EndEpisode();

                    Debug.Log(
                        $"[NpcDialogueAgent][EpisodeSummary] " +
                        $"cumulative={episodeCumulative:+0.000;-0.000;0.000} " +
                        $"turns={episodeTurns} " +
                        $"effects={episodeEffects}"
                    );
                }
                break;
            }
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (actionMask == null)
            return;

        bool serviceAvailable = NetworkDialogueService.Instance != null
            && (!UseSideChannelOverride || m_Client != null);
        if (!serviceAvailable)
        {
            actionMask.SetActionEnabled(0, 1, false); // engage
            actionMask.SetActionEnabled(0, 2, false); // end
            return;
        }

        if (!m_ConversationActive)
            actionMask.SetActionEnabled(0, 2, false); // cannot end before any dialogue is active

        if (!m_ConversationActive && GetProximityObservation() < m_MinEngageProximity)
            actionMask.SetActionEnabled(0, 1, false); // don't "engage" when player is far away

        // P4.1 — prevent the policy from looping forever in unproductive long conversations.
        if (m_TurnCount >= MaxTurnsBeforeForceEnd)
            actionMask.SetActionEnabled(0, 1, false);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[0] = 1; // always engage during manual testing
    }

    // ── P4.2 — Conversation timeout (LM Studio crash guard) ──────────────────

    private void Update()
    {
        // Retry player resolution until the spawned instance is found (max 300 attempts ≈ 5s).
        // Initialize() runs before BehaviorSceneBootstrap spawns the player.
        if (m_PlayerResolveAttempts < 300
            && (m_PlayerTransform == null || !m_PlayerTransform.gameObject.scene.IsValid()))
        {
            m_PlayerResolveAttempts++;
            TryResolvePlayerTransform();
        }

        // If we've been waiting in Initiated phase for longer than the timeout,
        // LM Studio has likely crashed or hung. Fire a penalty and reset to Idle
        // so the agent can recover and attempt a new conversation.
        if (m_ConversationPhase == ConversationPhase.Initiated
            && !float.IsNegativeInfinity(m_PhaseInitiatedTime)
            && Time.time - m_PhaseInitiatedTime > ConversationTimeoutSeconds)
        {
            Debug.LogWarning(
                $"[NpcDialogueAgent] Conversation timeout after {ConversationTimeoutSeconds}s " +
                "with no LLM response — resetting phase to Idle. " +
                "Check that LM Studio / run_llm_bridge.py is running."
            );
            AddRewardComponent(-m_TimeoutPenalty, "Latency/ConversationTimeout");
            m_ConversationActive = false;
            m_ConversationPhase  = ConversationPhase.Idle;
            m_PhaseInitiatedTime = float.NegativeInfinity;
        }
    }

    // ── Called by the dialogue system after each LLM response ─────────────────

    /// <summary>
    /// Call this from NetworkDialogueService (or a dialogue event) after each
    /// completed turn to shape the reward signal.
    /// </summary>
    public void OnDialogueTurnComplete(bool playerReplied)
    {
        m_TurnCount++;
        float reward = (playerReplied ? 0.1f : -0.05f) * m_OutcomeRewardScale;
        AddRewardComponent(reward, "Outcome/TurnComplete");
        RecordStat("Dialogue/Turns", m_TurnCount);
    }

    private bool SetOverrideClientEnabled(bool enabled)
    {
        NetworkDialogueService service = NetworkDialogueService.Instance;
        if (service == null)
            return false;

        service.SetMLAgentsSideChannelClient(enabled && m_Client != null ? m_Client : null);
        m_OverrideClientEnabled = enabled;
        return true;
    }

    private bool ApplyRoutingModeToDialogueService()
    {
        if (!UseSideChannelOverride)
            return SetOverrideClientEnabled(false);

        if (m_Client == null)
        {
            Debug.LogWarning(
                "[NpcDialogueAgent] SideChannelOverride mode selected but sidechannel client is unavailable. " +
                "Falling back to normal dialogue backend."
            );
            return SetOverrideClientEnabled(false);
        }

        return SetOverrideClientEnabled(true);
    }

    private void EnsureDecisionRequester()
    {
        m_DecisionRequester = GetComponent<DecisionRequester>();
        if (m_DecisionRequester == null && m_AutoAddDecisionRequester)
        {
            m_DecisionRequester = gameObject.AddComponent<DecisionRequester>();
            Debug.Log("[NpcDialogueAgent] Added DecisionRequester automatically to drive ML-Agents stepping.");
        }

        if (m_DecisionRequester == null)
            return;

        m_DecisionRequester.DecisionPeriod = Mathf.Max(1, m_DecisionPeriod);
        m_DecisionRequester.DecisionStep = Mathf.Clamp(
            m_DecisionRequester.DecisionStep,
            0,
            m_DecisionRequester.DecisionPeriod - 1
        );
        m_DecisionRequester.TakeActionsBetweenDecisions = m_TakeActionsBetweenDecisions;
    }

    private float GetProximityObservation()
    {
        if (m_PlayerTransform == null)
            return 0f;

        float dist = Vector3.Distance(transform.position, m_PlayerTransform.position);
        return 1f - Mathf.Clamp01(dist / m_MaxInteractionDistance);
    }

    private void HandleNetworkDialogueResponse(NetworkDialogueService.DialogueResponse response)
    {
        if (!isActiveAndEnabled)
            return;

        // Reward only user-driven turns; ambient chatter would add noise to training.
        if (!response.Request.IsUserInitiated)
            return;

        switch (response.Status)
        {
            case NetworkDialogueService.DialogueStatus.Pending:
            case NetworkDialogueService.DialogueStatus.InProgress:
                return;
        }

        if (response.RequestId > 0 && !m_RewardedTerminalRequestIds.Add(response.RequestId))
            return;

        switch (response.Status)
        {
            case NetworkDialogueService.DialogueStatus.Completed:
                // Turn completion and state are driven by HandleDialogueTelemetry, which
                // has the original request with IsUserInitiated preserved. Only penalise
                // here for empty responses that slip through.
                if (string.IsNullOrWhiteSpace(response.ResponseText))
                    AddRewardComponent(-0.1f * m_OutcomeRewardScale, "Outcome/EmptyCompleted");
                break;

            case NetworkDialogueService.DialogueStatus.Failed:
            case NetworkDialogueService.DialogueStatus.Cancelled:
                AddRewardComponent(-0.15f * m_OutcomeRewardScale, "Outcome/TerminalFailure");
                break;
        }
    }

    private void HandleDialogueTelemetry(NetworkDialogueService.DialogueResponseTelemetry telemetry)
    {
        if (!isActiveAndEnabled)
            return;

        if (!telemetry.Request.IsUserInitiated)
            return;

        if (telemetry.RequestId > 0 && !m_RewardedTelemetryRequestIds.Add(telemetry.RequestId))
            return;

        // Drive turn completion from telemetry — the ClientRpc response path reconstructs
        // DialogueRequest without IsUserInitiated (always false), so we can't rely on
        // HandleNetworkDialogueResponse for this. Telemetry uses state.Request directly
        // and correctly preserves IsUserInitiated=true for player-initiated turns.
        if (telemetry.Status == NetworkDialogueService.DialogueStatus.Completed)
        {
            m_ConversationActive = true;
            m_ConversationPhase  = ConversationPhase.Responded;
            m_PhaseInitiatedTime = float.NegativeInfinity; // P4.2 — got response, clear timeout
            OnDialogueTurnComplete(playerReplied: true);
        }

        RecordStat("Latency/QueueMs", telemetry.QueueLatencyMs);
        RecordStat("Latency/ModelMs", telemetry.ModelLatencyMs);
        RecordStat("Latency/TotalMs", telemetry.TotalLatencyMs);
        RecordStat("Retries/Count", telemetry.RetryCount, StatAggregationMethod.Sum);

        if (telemetry.RetryCount > 0)
        {
            AddRewardComponent(
                -m_RetryPenaltyPerAttempt * telemetry.RetryCount,
                "Reliability/RetryPenalty"
            );
        }

        if (LooksLikeTimeoutError(telemetry.Error))
        {
            AddRewardComponent(-m_TimeoutPenalty, "Latency/TimeoutPenalty");
        }

        if (telemetry.Status != NetworkDialogueService.DialogueStatus.Completed)
            return;

        float totalLatencyMs = telemetry.TotalLatencyMs > 0f
            ? telemetry.TotalLatencyMs
            : Mathf.Max(telemetry.ModelLatencyMs, telemetry.QueueLatencyMs);

        if (totalLatencyMs <= m_FastResponseThresholdMs)
        {
            AddRewardComponent(m_FastResponseBonus, "Latency/FastBonus");
        }
        else if (totalLatencyMs <= m_AcceptableResponseThresholdMs)
        {
            AddRewardComponent(m_AcceptableResponseBonus, "Latency/AcceptableBonus");
        }
        else if (totalLatencyMs >= m_SlowResponseThresholdMs)
        {
            AddRewardComponent(-m_SlowResponsePenalty, "Latency/SlowPenalty");
        }
    }

    private void HandleFeedbackScore(DialogueFeedbackCollector.FeedbackScoreSummary feedback)
    {
        if (!isActiveAndEnabled)
            return;

        if (!feedback.IsUserInitiated)
            return;

        if (feedback.RequestId > 0 && !m_RewardedFeedbackRequestIds.Add(feedback.RequestId))
            return;

        RecordStat("Feedback/ScoreRaw", feedback.Score);
        RecordStat("Feedback/HasEffect", feedback.HasEffect ? 1f : 0f, StatAggregationMethod.Sum);
        RecordStat("Feedback/TagValid",  feedback.TagValid  ? 1f : 0f, StatAggregationMethod.Sum);

        if (feedback.HasEffect)
        {
            // P3.4 — full arc bonus: only award if we came through Responded,
            // confirming the complete Idle→Initiated→Responded→Resolved sequence.
            bool completedFullArc = m_ConversationPhase == ConversationPhase.Responded;
            m_LastEffectFireTime  = Time.time;
            m_ConversationPhase   = ConversationPhase.Resolved;
            if (completedFullArc)
                AddRewardComponent(FullArcBonus, "Quality/FullArcBonus");
        }

        float normalizedScore = Mathf.Clamp(feedback.Score / 6f, -1f, 1f);
        AddRewardComponent(
            normalizedScore * m_FeedbackScoreRewardScale,
            "Quality/FeedbackScore"
        );
    }

    private void HandleStructuredDialogueResponse(DialogueResponse response)
    {
        if (!isActiveAndEnabled || response == null)
            return;

        if (string.Equals(response.responseText, "[LLM error — check Python console]", StringComparison.Ordinal))
        {
            AddRewardComponent(-0.2f, "Bridge/ErrorResponse");
            return;
        }

        if (!m_ConversationActive)
            return;

        float confidence = Mathf.Clamp01(response.confidence);
        if (confidence >= 0.8f)
            AddRewardComponent(0.01f, "Bridge/ConfidenceHigh");
        else if (confidence <= 0.35f)
            AddRewardComponent(-0.02f, "Bridge/ConfidenceLow");
    }

    private void AddRewardComponent(float amount, string statName)
    {
        if (Mathf.Approximately(amount, 0f))
            return;

        // P3.2 — prevent rare spikes from dominating the gradient.
        amount = Mathf.Clamp(amount, -MaxSingleRewardComponent, MaxSingleRewardComponent);

        AddReward(amount);
        RecordStat($"Reward/{statName}", amount);
        RecordStat("Reward/TotalComponents", amount);

        if (m_LogRewardComponents)
        {
            Debug.Log(
                $"[NpcDialogueAgent][Reward] component={statName} " +
                $"amount={amount:+0.000;-0.000;0.000} " +
                $"cumulative={GetCumulativeReward():0.000} " +
                $"turns={m_TurnCount} active={m_ConversationActive}"
            );
        }
    }

    private void RecordStat(string statName, float value,
        StatAggregationMethod aggregation = StatAggregationMethod.Average)
    {
        if (m_StatsRecorder == null || float.IsNaN(value) || float.IsInfinity(value))
            return;

        m_StatsRecorder.Add($"NpcDialogue/{statName}", value, aggregation);
    }

    private static bool LooksLikeTimeoutError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        return error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ── Player transform resolution ───────────────────────────────────────────

    /// <summary>
    /// If the serialised m_PlayerTransform is a prefab asset (not a live scene
    /// instance), fall back to finding the spawned player by tag.  Also pushes
    /// the resolved transform into GameStateProvider so health/position are live.
    /// </summary>
    private void TryResolvePlayerTransform()
    {
        // Already pointing at a valid scene object — nothing to do.
        if (m_PlayerTransform != null && m_PlayerTransform.gameObject.scene.IsValid())
            return;

        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null)
        {
            // Only warn once — after that, retry silently each frame via Update().
            if (m_PlayerResolveAttempts <= 1)
                Debug.LogWarning("[NpcDialogueAgent] Could not find 'Player' tag — " +
                    "retrying each frame. Ensure the player prefab is tagged 'Player'.");
            return;
        }

        m_PlayerTransform = playerGo.transform;
        if (m_GameState != null)
            m_GameState.SetPlayerTransform(m_PlayerTransform);

        Debug.Log($"[NpcDialogueAgent] Resolved PlayerTransform via tag 'Player': {playerGo.name}");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        UnregisterRuntimeHandlers();

        if (m_Channel != null)
        {
            SideChannelManager.UnregisterSideChannel(m_Channel);
            m_Channel = null;
        }
        if (m_Client != null)
        {
            m_Client.OnStructuredDialogueResponseReceived -= HandleStructuredDialogueResponse;
            m_Client.Dispose();
        }

        if (m_OverrideClientEnabled)
            SetOverrideClientEnabled(false);
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        if (!Application.isPlaying)
            return;

        RegisterRuntimeHandlers();

        if (m_StatsRecorder != null)
            ApplyRoutingModeToDialogueService();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        UnregisterRuntimeHandlers();

        if (m_OverrideClientEnabled)
            SetOverrideClientEnabled(false);
    }

    private void RegisterRuntimeHandlers()
    {
        if (m_RuntimeHandlersRegistered)
            return;

        NetworkDialogueService.OnDialogueResponse += HandleNetworkDialogueResponse;
        NetworkDialogueService.OnDialogueResponseTelemetry += HandleDialogueTelemetry;
        DialogueFeedbackCollector.OnFeedbackScored += HandleFeedbackScore;
        m_RuntimeHandlersRegistered = true;
    }

    private void UnregisterRuntimeHandlers()
    {
        if (!m_RuntimeHandlersRegistered)
            return;

        NetworkDialogueService.OnDialogueResponse -= HandleNetworkDialogueResponse;
        NetworkDialogueService.OnDialogueResponseTelemetry -= HandleDialogueTelemetry;
        DialogueFeedbackCollector.OnFeedbackScored -= HandleFeedbackScore;
        m_RuntimeHandlersRegistered = false;
    }

    /// <summary>
    /// Apply values from the optional DialogueAgentProfile to this instance so that
    /// multiple scenes/prefabs can share consistent tuning without hand-editing
    /// every NpcDialogueAgent component.
    /// </summary>
    private void ApplyProfileIfAvailable()
    {
        if (m_Profile == null)
            return;

        // Decision cadence
        if (m_Profile.DecisionPeriod > 0)
            m_DecisionPeriod = m_Profile.DecisionPeriod;
        m_TakeActionsBetweenDecisions = m_Profile.TakeActionsBetweenDecisions;

        // Reward shaping scales
        m_OutcomeRewardScale          = m_Profile.OutcomeRewardScale;
        m_FeedbackScoreRewardScale    = m_Profile.FeedbackScoreRewardScale;
        m_FastResponseBonus           = m_Profile.FastResponseBonus;
        m_AcceptableResponseBonus     = m_Profile.AcceptableResponseBonus;
        m_SlowResponsePenalty         = m_Profile.SlowResponsePenalty;
        m_TimeoutPenalty              = m_Profile.TimeoutPenalty;
        m_RetryPenaltyPerAttempt      = m_Profile.RetryPenaltyPerAttempt;
    }

    private bool UseSideChannelOverride => m_DialogueRoutingMode == DialogueRoutingMode.SideChannelOverride;
}
