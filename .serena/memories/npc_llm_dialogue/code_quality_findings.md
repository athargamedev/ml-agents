# Code Quality Findings — LLM Scan Results

Source: `python dev_tools/run_dev_tools.py scan`
Last full scan: 2026-02-28 (1 file piloted, 44 files total available)

---

## Confirmed Issues in NpcDialogueAgent.cs

### HIGH: NetworkDialogueService.Instance accessed without null check
**Category**: multiplayer_safety + npc_dialogue (duplicate finding, two domains flagged it)
**Pattern**: [NPC002] 
```csharp
// WRONG — Instance can be null during early init
NetworkDialogueService.Instance.SendDialogue(request);

// RIGHT
NetworkDialogueService.Instance?.SendDialogue(request);
// or: if (NetworkDialogueService.Instance != null) ...
```
The `WriteDiscreteActionMask` method already does a null check correctly:
```csharp
bool serviceAvailable = NetworkDialogueService.Instance != null && (...);
```
Other call sites may not. Audit all `NetworkDialogueService.Instance` usages.

### MEDIUM: Observation values not fully normalised
**Category**: ml_agents | Pattern: [ML002]
Turn count uses `Mathf.Min(m_TurnCount / 10f, 1f)` — soft-normalised but unbounded past 10 turns.
Episode time uses `Mathf.Min(m_EpisodeTime / 60f, 1f)` — can exceed 1.0 during long episodes.
For PPO stability, all observations should be in [0, 1] or [-1, 1]. Hard-clamp both:
```csharp
sensor.AddObservation(Mathf.Clamp01(m_TurnCount / 10f));   // [3]
sensor.AddObservation(Mathf.Clamp01(m_EpisodeTime / 60f)); // [4]
```

### LOW: Reward spike risk
**Category**: ml_agents | Pattern: [ML001]
Already mitigated — `AddRewardComponent()` wraps all rewards with `Mathf.Clamp(amount, -0.5f, 0.5f)`.
But the EndConversation reward `m_TurnCount * 0.05f` can reach 0.5 at 10 turns (clamped) and
is proportional to turns, which rewards long conversations over good ones. Monitor during training.

### LOW: ClientRpc pattern check
**Category**: multiplayer_safety | Pattern: [MP001]
NpcDialogueAgent itself doesn't use ClientRpc, but `NetworkDialogueService` does.
The scanner correctly flags the *pattern* to watch for when extending these classes.

### LOW: Hardcoded effect tag string risk
**Category**: npc_dialogue | Pattern: [NPC001]
NpcDialogueAgent does not hardcode effect tags directly. The scan is pre-emptive.
Effects are resolved through `DialogueFeedbackCollector.FeedbackScoreSummary.TagName` which
comes from the service layer — safe as long as `EffectCatalog.TryGet()` is the lookup path.

---

## Static Pattern Catalog (dev_tools/schemas/unity_patterns.json)
Quick reference for what the scanner looks for:

| ID | Pattern | Severity | Domain |
|----|---------|----------|--------|
| MP001 | ClientRpc without IsOwner check | high | multiplayer |
| MP002 | NetworkVariable write outside server authority | high | multiplayer |
| MP003 | Missing null check on NetworkObject before RPC | medium | multiplayer |
| ML001 | AddReward without Mathf.Clamp | medium | ml_agents |
| ML002 | Observation not normalised | medium | ml_agents |
| ML003 | EndEpisode inside CollectObservations | high | ml_agents |
| ML004 | RequestDecision called manually when DecisionRequester present | low | ml_agents |
| ML005 | StatsRecorder without Academy.Instance null check | medium | ml_agents |
| NPC001 | Hardcoded effect tag string | low | npc_dialogue |
| NPC002 | NetworkDialogueService.Instance without null check | high | npc_dialogue |
| NPC003 | LLM response in UI without sanitisation (WebGL XSS) | medium | npc_dialogue |
| UP001 | GetComponent in Update | medium | unity |
| UP002 | FindObjectOfType in hot path | high | unity |
| UP003 | String allocation in Update | low | unity |
| UP004 | Missing [SerializeField] on Inspector-assigned private | low | unity |

---

## Recommended Next Scans
Run full scan after implementing fixes:
```
python dev_tools/run_dev_tools.py scan
python dev_tools/run_dev_tools.py update
```
Focus files for immediate review (highest complexity/risk):
- `NetworkDialogueService.cs` (~8569 lines, NetworkBehaviour singleton)
- `EffectParser.cs` (LLM output parsing, injection risk)
- `DialogueFeedbackCollector.cs` (reward signal source)
- `OpenAIChatClient.cs` (LLM backend, error handling)
