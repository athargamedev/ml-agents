using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using Network_Game.Dialogue;
using UnityEngine;
using UnityEngine.TestTools;

namespace NpcDialogue.Tests.Runtime
{
    /// <summary>
    /// PlayMode tests for NpcDialogueAgent.
    ///
    /// Validates observations, phase transitions, reward signals, and effect state decay
    /// without requiring a Python trainer or LM Studio connection.
    ///
    /// NpcDialogueAgent lives in Assembly-CSharp (no explicit asmdef).
    /// Private state is accessed via reflection. Event handlers are invoked directly
    /// via reflection to simulate dialogue system callbacks in isolation.
    ///
    /// Run via: Unity Test Runner → PlayMode → NpcDialogue.Tests.Runtime
    /// </summary>
    public class NpcDialogueAgentPlayModeTests
    {
        // ── Reflected types and members ───────────────────────────────────────

        private static readonly Type k_AgentType =
            Type.GetType("NpcDialogueAgent, Assembly-CSharp");

        private static readonly Type k_PhaseEnumType =
            k_AgentType?.GetNestedType("ConversationPhase", BindingFlags.NonPublic);

        private static readonly FieldInfo k_PhaseField =
            k_AgentType?.GetField("m_ConversationPhase",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo k_EffectTimeField =
            k_AgentType?.GetField("m_LastEffectFireTime",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo k_TurnCountField =
            k_AgentType?.GetField("m_TurnCount",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo k_ConversationActiveField =
            k_AgentType?.GetField("m_ConversationActive",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo k_EffectDecaySecondsField =
            k_AgentType?.GetField("m_EffectDecaySeconds",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo k_HandleFeedbackMethod =
            k_AgentType?.GetMethod("HandleFeedbackScore",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo k_HandleResponseMethod =
            k_AgentType?.GetMethod("HandleNetworkDialogueResponse",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo k_HandleTelemetryMethod =
            k_AgentType?.GetMethod("HandleDialogueTelemetry",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // VectorSensor internal obs list — same field name across all ml-agents versions.
        private static readonly FieldInfo k_SensorObsField =
            typeof(VectorSensor).GetField("m_Observations",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // ── Test state ────────────────────────────────────────────────────────

        private GameObject m_AgentGO;
        private Agent m_Agent;

        // ── Setup / Teardown ─────────────────────────────────────────────────

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(k_AgentType,
                "NpcDialogueAgent type not found in Assembly-CSharp. " +
                "Ensure NpcDialogueAgent.cs has no explicit asmdef.");

            m_AgentGO = new GameObject("TestDialogueBridge");

            // Pre-configure BehaviorParameters before NpcDialogueAgent.Awake() runs.
            // RequireComponent will also add it, but we want our values set first.
            var bp = m_AgentGO.AddComponent<BehaviorParameters>();
            bp.BrainParameters.VectorObservationSize = 7;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(3);
            bp.BehaviorName = "NpcDialogue";
            bp.BehaviorType = BehaviorType.HeuristicOnly; // no Python needed

            m_Agent = (Agent)m_AgentGO.AddComponent(k_AgentType);

            yield return null; // let Awake + Initialize complete
        }

        [TearDown]
        public void TearDown()
        {
            if (m_AgentGO != null)
                UnityEngine.Object.Destroy(m_AgentGO);
        }

        // ── A. Observation correctness ────────────────────────────────────────

        [UnityTest]
        public IEnumerator A1_Observations_EmitExactlySevenFloats()
        {
            var sensor = new VectorSensor(7, "TestSensor");
            m_Agent.CollectObservations(sensor);

            var obs = GetSensorObservations(sensor);
            Assert.AreEqual(7, obs.Count,
                "Agent must emit exactly 7 observations to match BehaviorParameters Space Size.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator A2_PhaseObservation_IsZero_InInitialIdleState()
        {
            var obs = CollectObs();
            Assert.AreEqual(0f, obs[5], 0.001f,
                "Obs[5] conversationPhase must be 0.0 (Idle) at start of episode.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator A3_EffectStateObservation_IsZero_WhenNoEffectHasFired()
        {
            var obs = CollectObs();
            Assert.AreEqual(0f, obs[6], 0.001f,
                "Obs[6] effectState must be 0.0 before any effect fires.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator A4_EffectStateObservation_IsOne_ImmediatelyAfterEffectFires()
        {
            // Set m_LastEffectFireTime to right now so decay = exp(0) = 1
            k_EffectTimeField.SetValue(m_Agent, Time.time);

            var obs = CollectObs();
            Assert.AreEqual(1f, obs[6], 0.05f,
                "Obs[6] effectState must be ~1.0 immediately after effect fires.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator A5_EffectStateObservation_Decays_AfterEffectFires()
        {
            // Set effect to have fired 20 seconds ago (> one decay constant of 8s)
            k_EffectTimeField.SetValue(m_Agent, Time.time - 20f);

            var obs = CollectObs();
            Assert.Less(obs[6], 0.1f,
                "Obs[6] effectState must decay significantly below 0.1 after 20 seconds.");
            yield return null;
        }

        // ── B. Phase transitions ──────────────────────────────────────────────

        [UnityTest]
        public IEnumerator B1_EngageAction_SetsPhase_ToInitiated()
        {
            DispatchAction(1); // Engage
            Assert.AreEqual(1, GetPhase(), "Phase must be Initiated (1) after Engage action.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator B2_EndConversation_AfterEngage_SetsPhase_ToIdle()
        {
            DispatchAction(1); // Engage → Initiated
            k_ConversationActiveField.SetValue(m_Agent, true);
            DispatchAction(2); // End
            Assert.AreEqual(0, GetPhase(), "Phase must reset to Idle (0) after End action.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator B3_CompletedDialogueResponse_SetsPhase_ToResponded()
        {
            InvokeHandleResponse(requestId: 1,
                status: NetworkDialogueService.DialogueStatus.Completed,
                responseText: "Hello adventurer!",
                isUserInitiated: true);

            Assert.AreEqual(2, GetPhase(),
                "Phase must be Responded (2) after a completed dialogue response.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator B4_FeedbackWithEffect_SetsPhase_ToResolved()
        {
            InvokeHandleFeedback(requestId: 2, score: 4, hasEffect: true,
                tagValid: true, isUserInitiated: true);

            Assert.AreEqual(3, GetPhase(),
                "Phase must be Resolved (3) after feedback with HasEffect=true.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator B5_FeedbackWithoutEffect_DoesNotAdvance_ToResolved()
        {
            // Put agent in Responded state first
            InvokeHandleResponse(requestId: 3,
                status: NetworkDialogueService.DialogueStatus.Completed,
                responseText: "A reply.",
                isUserInitiated: true);
            Assert.AreEqual(2, GetPhase());

            // Feedback without effect should NOT advance to Resolved
            InvokeHandleFeedback(requestId: 3, score: 2, hasEffect: false,
                tagValid: false, isUserInitiated: true);

            Assert.AreEqual(2, GetPhase(),
                "Phase must stay at Responded (2) when feedback has no effect.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator B6_EpisodeBegin_Resets_AllTrackingState()
        {
            // Drive agent into a mid-conversation state
            DispatchAction(1); // Engage
            k_TurnCountField.SetValue(m_Agent, 5);
            k_EffectTimeField.SetValue(m_Agent, Time.time);
            SetPhase(3); // Resolved

            m_Agent.OnEpisodeBegin();

            Assert.AreEqual(0, GetPhase(),
                "Phase must reset to Idle (0) on episode begin.");
            Assert.AreEqual(0, (int)k_TurnCountField.GetValue(m_Agent),
                "TurnCount must reset to 0 on episode begin.");
            Assert.AreEqual(float.NegativeInfinity, (float)k_EffectTimeField.GetValue(m_Agent),
                "m_LastEffectFireTime must reset to -∞ on episode begin.");
            yield return null;
        }

        // ── C. Reward signals ─────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator C1_EngageAction_AddsPositiveReward()
        {
            float before = m_Agent.GetCumulativeReward();
            DispatchAction(1); // Engage
            Assert.Greater(m_Agent.GetCumulativeReward(), before,
                "Engage action must add a positive reward component.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator C2_SuccessfulTurn_AddsPositiveReward()
        {
            float before = m_Agent.GetCumulativeReward();
            m_Agent.GetType()
                .GetMethod("OnDialogueTurnComplete", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(m_Agent, new object[] { true });

            Assert.Greater(m_Agent.GetCumulativeReward(), before,
                "A successful dialogue turn (playerReplied=true) must add positive reward.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator C3_FailedDialogueResponse_AddsNegativeReward()
        {
            float before = m_Agent.GetCumulativeReward();
            InvokeHandleResponse(requestId: 10,
                status: NetworkDialogueService.DialogueStatus.Failed,
                responseText: null,
                isUserInitiated: true);

            Assert.Less(m_Agent.GetCumulativeReward(), before,
                "A failed dialogue response must add a negative reward component.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator C4_FastTelemetry_AddsLatencyBonus()
        {
            float before = m_Agent.GetCumulativeReward();
            InvokeHandleTelemetry(requestId: 20,
                status: NetworkDialogueService.DialogueStatus.Completed,
                totalLatencyMs: 800f,   // under 2000ms fast threshold
                retryCount: 0,
                isUserInitiated: true);

            Assert.Greater(m_Agent.GetCumulativeReward(), before,
                "A fast response (< 2000ms) must add a latency bonus reward.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator C5_SlowTelemetry_AddsLatencyPenalty()
        {
            float before = m_Agent.GetCumulativeReward();
            InvokeHandleTelemetry(requestId: 21,
                status: NetworkDialogueService.DialogueStatus.Completed,
                totalLatencyMs: 20000f,  // over 15000ms slow threshold
                retryCount: 0,
                isUserInitiated: true);

            Assert.Less(m_Agent.GetCumulativeReward(), before,
                "A slow response (> 15000ms) must add a latency penalty.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator C6_RetryPenalty_ScalesWithRetryCount()
        {
            // Two retries should produce a larger penalty than one retry.
            // Test on fresh agent instances via reward delta.
            float before1 = m_Agent.GetCumulativeReward();
            InvokeHandleTelemetry(requestId: 30,
                status: NetworkDialogueService.DialogueStatus.Completed,
                totalLatencyMs: 3000f,
                retryCount: 1,
                isUserInitiated: true);
            float delta1 = m_Agent.GetCumulativeReward() - before1;

            // End episode to reset reward accumulator, then test with 2 retries
            m_Agent.OnEpisodeBegin();
            float before2 = m_Agent.GetCumulativeReward();
            InvokeHandleTelemetry(requestId: 31,
                status: NetworkDialogueService.DialogueStatus.Completed,
                totalLatencyMs: 3000f,
                retryCount: 2,
                isUserInitiated: true);
            float delta2 = m_Agent.GetCumulativeReward() - before2;

            Assert.Less(delta2, delta1,
                "2 retries must produce a larger penalty than 1 retry.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator C7_AmbientChatter_IsIgnored_DoesNotAffectReward()
        {
            float before = m_Agent.GetCumulativeReward();
            InvokeHandleResponse(requestId: 99,
                status: NetworkDialogueService.DialogueStatus.Completed,
                responseText: "I am just talking to myself...",
                isUserInitiated: false); // ambient — must be filtered

            Assert.AreEqual(before, m_Agent.GetCumulativeReward(), 0.0001f,
                "Ambient (non-user) dialogue must not affect reward.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator C8_DuplicateRequestId_IsNotDoubleRewarded()
        {
            InvokeHandleResponse(requestId: 50,
                status: NetworkDialogueService.DialogueStatus.Completed,
                responseText: "First time.",
                isUserInitiated: true);
            float afterFirst = m_Agent.GetCumulativeReward();

            // Exact same requestId — must be deduped
            InvokeHandleResponse(requestId: 50,
                status: NetworkDialogueService.DialogueStatus.Completed,
                responseText: "First time.",
                isUserInitiated: true);

            Assert.AreEqual(afterFirst, m_Agent.GetCumulativeReward(), 0.0001f,
                "Same RequestId must not trigger reward twice (deduplication).");
            yield return null;
        }

        // ── D. Effect decay math ──────────────────────────────────────────────

        [Test]
        public void D1_EffectDecay_IsOne_AtTimeZero()
        {
            float decaySeconds = 8f;
            float effectState = Mathf.Exp(-0f / decaySeconds);
            Assert.AreEqual(1f, effectState, 0.0001f,
                "Effect state must be exactly 1.0 at t=0 (immediately after firing).");
        }

        [Test]
        public void D2_EffectDecay_ApproachesZero_AfterThreeDecayConstants()
        {
            float decaySeconds = 8f;
            float effectState = Mathf.Exp(-(decaySeconds * 3f) / decaySeconds);
            Assert.Less(effectState, 0.05f,
                "Effect state must be < 0.05 after 3 decay constants (24s with default 8s).");
        }

        [Test]
        public void D3_EffectDecay_IsStrictlyMonotonicallyDecreasing()
        {
            float decaySeconds = 8f;
            float prev = 1f;
            for (float t = 0.5f; t <= 60f; t += 0.5f)
            {
                float curr = Mathf.Exp(-t / decaySeconds);
                Assert.Less(curr, prev,
                    $"Effect state must decrease monotonically. Failed at t={t}s.");
                prev = curr;
            }
        }

        [Test]
        public void D4_EffectDecay_NeverGoesNegative()
        {
            float decaySeconds = 8f;
            for (float t = 0f; t <= 300f; t += 1f)
            {
                float effectState = Mathf.Exp(-t / decaySeconds);
                Assert.GreaterOrEqual(effectState, 0f,
                    $"Effect state must never go negative. Failed at t={t}s.");
            }
        }

        [Test]
        public void D5_EffectDecay_HalfLife_IsCorrect()
        {
            // Half-life of exponential decay: t_half = ln(2) * decaySeconds
            float decaySeconds = 8f;
            float halfLife = Mathf.Log(2f) * decaySeconds; // ~5.545s
            float effectAtHalfLife = Mathf.Exp(-halfLife / decaySeconds);
            Assert.AreEqual(0.5f, effectAtHalfLife, 0.001f,
                "Effect state must be ~0.5 at the half-life point.");
        }

        // ── E. Training efficiency — observation diversity ────────────────────

        [UnityTest]
        public IEnumerator E1_Observations_HaveExpectedRanges_InIdleState()
        {
            var obs = CollectObs();

            // All obs should be in [0, 1] range
            for (int i = 0; i < obs.Count; i++)
            {
                Assert.GreaterOrEqual(obs[i], 0f, $"Obs[{i}] must be >= 0");
                Assert.LessOrEqual(obs[i], 1f,    $"Obs[{i}] must be <= 1");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator E2_PhaseObs_SpansFullRange_AcrossAllPhases()
        {
            var expectedValues = new[] { 0f, 1f / 3f, 2f / 3f, 1f };
            var observedValues = new List<float>();

            foreach (var phaseInt in new[] { 0, 1, 2, 3 })
            {
                SetPhase(phaseInt);
                var obs = CollectObs();
                observedValues.Add(obs[5]);
            }

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(expectedValues[i], observedValues[i], 0.001f,
                    $"Phase {i} must map obs[5] to {expectedValues[i]:F3}, got {observedValues[i]:F3}");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator E3_TurnCount_IncreasesObs3_Linearly()
        {
            for (int turn = 0; turn <= 5; turn++)
            {
                k_TurnCountField.SetValue(m_Agent, turn);
                var obs = CollectObs();
                float expected = turn / 10f;
                Assert.AreEqual(expected, obs[3], 0.001f,
                    $"Obs[3] must be {expected} for turn count {turn}.");
            }
            yield return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private List<float> CollectObs()
        {
            var sensor = new VectorSensor(7, "TestSensor");
            m_Agent.CollectObservations(sensor);
            return GetSensorObservations(sensor);
        }

        private static List<float> GetSensorObservations(VectorSensor sensor)
        {
            Assert.IsNotNull(k_SensorObsField,
                "VectorSensor.m_Observations field not found. Check ML-Agents version.");
            return (List<float>)k_SensorObsField.GetValue(sensor);
        }

        private int GetPhase() => (int)k_PhaseField.GetValue(m_Agent);

        private void SetPhase(int value)
        {
            object boxedEnum = Enum.ToObject(k_PhaseEnumType, value);
            k_PhaseField.SetValue(m_Agent, boxedEnum);
        }

        private void DispatchAction(int actionIndex)
        {
            var actions = new ActionBuffers(ActionSegment<float>.Empty,
                new ActionSegment<int>(new[] { actionIndex }));
            m_Agent.OnActionReceived(actions);
        }

        // Invoke the private HandleNetworkDialogueResponse handler directly.
        // Constructs the DialogueResponse struct fields via reflection so the test
        // doesn't depend on public constructors (struct layout may change).
        private void InvokeHandleResponse(
            int requestId,
            NetworkDialogueService.DialogueStatus status,
            string responseText,
            bool isUserInitiated)
        {
            var responseType = typeof(NetworkDialogueService.DialogueResponse);
            var requestType  = typeof(NetworkDialogueService.DialogueRequest);

            object reqBox = Activator.CreateInstance(requestType);
            SetStructField(reqBox, requestType, "IsUserInitiated", isUserInitiated);

            object resBox = Activator.CreateInstance(responseType);
            SetStructField(resBox, responseType, "RequestId",    requestId);
            SetStructField(resBox, responseType, "Status",       status);
            SetStructField(resBox, responseType, "ResponseText", responseText ?? string.Empty);
            SetStructField(resBox, responseType, "Request",      Unbox<NetworkDialogueService.DialogueRequest>(reqBox));

            k_HandleResponseMethod.Invoke(m_Agent,
                new object[] { Unbox<NetworkDialogueService.DialogueResponse>(resBox) });
        }

        private void InvokeHandleFeedback(
            int requestId, int score, bool hasEffect, bool tagValid, bool isUserInitiated)
        {
            var feedbackType = typeof(DialogueFeedbackCollector.FeedbackScoreSummary);
            object box = Activator.CreateInstance(feedbackType);
            SetStructField(box, feedbackType, "RequestId",       requestId);
            SetStructField(box, feedbackType, "Score",           score);
            SetStructField(box, feedbackType, "HasEffect",       hasEffect);
            SetStructField(box, feedbackType, "TagValid",        tagValid);
            SetStructField(box, feedbackType, "IsUserInitiated", isUserInitiated);

            k_HandleFeedbackMethod.Invoke(m_Agent,
                new object[] { Unbox<DialogueFeedbackCollector.FeedbackScoreSummary>(box) });
        }

        private void InvokeHandleTelemetry(
            int requestId,
            NetworkDialogueService.DialogueStatus status,
            float totalLatencyMs,
            int retryCount,
            bool isUserInitiated)
        {
            var telType     = typeof(NetworkDialogueService.DialogueResponseTelemetry);
            var requestType = typeof(NetworkDialogueService.DialogueRequest);

            object reqBox = Activator.CreateInstance(requestType);
            SetStructField(reqBox, requestType, "IsUserInitiated", isUserInitiated);

            object telBox = Activator.CreateInstance(telType);
            SetStructField(telBox, telType, "RequestId",      requestId);
            SetStructField(telBox, telType, "Status",         status);
            SetStructField(telBox, telType, "TotalLatencyMs", totalLatencyMs);
            SetStructField(telBox, telType, "RetryCount",     retryCount);
            SetStructField(telBox, telType, "Request",
                Unbox<NetworkDialogueService.DialogueRequest>(reqBox));

            k_HandleTelemetryMethod.Invoke(m_Agent,
                new object[] { Unbox<NetworkDialogueService.DialogueResponseTelemetry>(telBox) });
        }

        // Sets a field on a boxed struct (works for both public and private fields).
        private static void SetStructField(object boxed, Type type, string fieldName, object value)
        {
            var field = type.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field,
                $"Field '{fieldName}' not found on {type.Name}. Update test if struct changed.");
            field.SetValue(boxed, value);
        }

        // Unboxes a struct value-type from an object reference (required after SetValue on boxed struct).
        private static T Unbox<T>(object boxed) where T : struct => (T)boxed;
    }
}
