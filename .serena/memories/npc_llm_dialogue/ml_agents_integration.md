# ML-Agents Integration — Implementation Details

## NpcDialogueAgent.cs
Scene-level Agent (one per scene on "DialogueBridge" empty GameObject).

### DialogueRoutingMode enum
- `ObserveOnly` (default) — ML-Agents only observes outcomes and shapes rewards.
  Existing NetworkDialogueService → LM Studio path completely untouched.
- `SideChannelOverride` — experimental, routes ChatAsync() through Python bridge.

### Observations (5 floats — BehaviorParameters Space Size must be 5)
- [0] player proximity to NPC anchor (0=far, 1=adjacent)
- [1] player health normalized (0–1)
- [2] player in combat (0 or 1)
- [3] turn count / 10 (soft-normalized)
- [4] episode time / 60 (capped at 1)

### Actions (discrete branch size 3)
- 0 = idle
- 1 = engage / continue dialogue
- 2 = end conversation → EndEpisode()

### Reward signals subscribed via static events
- `NetworkDialogueService.OnDialogueResponse` → success/fail rewards
- `NetworkDialogueService.OnDialogueResponseTelemetry` → latency rewards, retry penalty
- `DialogueFeedbackCollector.OnFeedbackScored` → quality score reward

### Key design features
- HashSet<int> deduplication per episode to avoid double-rewarding same RequestId
- `WriteDiscreteActionMask` — engage disabled when player proximity < threshold
- `DecisionRequester` auto-added at runtime (configurable DecisionPeriod)
- `StatsRecorder` sends metrics to TensorBoard under `NpcDialogue/` namespace
- `IsUserInitiated` filter on responses — ignores ambient NPC chatter

## Inspector Setup
BehaviorParameters:
- Behavior Name: NpcDialogue
- Vector Observations Space Size: **5** (must match exactly)
- Discrete Branches: 1, Branch 0 Size: **3**
- Behavior Type: **Heuristic Only** (testing) / **Default** (Python bridge running)
- Model: empty until trained .onnx exists

## SideChannelDialogueClient.cs
IDialogueInferenceClient backed by LlmDialogueChannel SideChannel.
Key features:
- ConcurrentDictionary<string, PendingRequest> for async GUID-keyed routing
- Heartbeat protocol: npcId=`__bridge__`, responseText=`__bridge_ready__`
- Bridge freshness tracking (5s window) for non-blocking CheckConnectionAsync
- CancellationToken support — properly cancels pending requests on token cancel
- Non-blocking CheckConnectionAsync — avoids env.reset() deadlock
- `OnStructuredDialogueResponseReceived` event for confidence-based rewards

## NetworkDialogueService modifications
- `m_OverrideClient` field (IDialogueInferenceClient, line ~573)
- `SetMLAgentsSideChannelClient(client)` public method (line ~2847)
- `ResolveInferenceClient()` checks override first before normal path
