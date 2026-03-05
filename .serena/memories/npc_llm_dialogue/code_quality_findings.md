# Code Quality Findings — LLM Scan Results

Source: `python dev_tools/run_dev_tools.py scan`
Last full scan: 2026-03-03 — 42 files, context injection active (14 packages), High=12, Med=21, Low=5

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

---

## 2026-03-03 Scan Findings (first scan with 14-package context injection)

### HIGH: NetworkObject access before OnNetworkSpawn — multiple files
**Category**: multiplayer_safety | Files: `DialogueClientUI.cs`, `DialogueParticleCollisionDamage.cs`, `EffectTargetResolverService.cs`, `CombatHealth.cs`, `DialogueEffectProjectile.cs`
The pattern: `GetComponent<NetworkObject>()` or `NetworkVariable.Value` written/read in `Awake()` before `OnNetworkSpawn()` is called. Confirmed TPs across 5 files.
```csharp
// WRONG — Awake fires before network spawn
void Awake() { m_CachedNetworkObject = GetComponent<NetworkObject>(); }
// RIGHT
public override void OnNetworkSpawn() { m_CachedNetworkObject = GetComponent<NetworkObject>(); }
```
**Note**: OnNetworkSpawn guard is ONLY required for `NetworkVariable` and `NetworkObject` accesses. Do NOT guard plain `[SerializeField]` MonoBehaviour/Transform/Cinemachine references — they don't need it (scanner FP guard added).

### HIGH: Hardcoded effect tag strings in EffectIntent.cs
**Category**: npc_dialogue | Pattern: [NPC001]
Effect tag names referenced as string literals instead of via `DialogueConstants.*`.
Fix: replace all hardcoded tag strings with constants from `DialogueConstants`.

### HIGH (overnight): DialogueAnimationContextBuilder.cs — new animation training file
**Category**: ml_agents — **FIRST ANIMATION TRAINING FILE detected 2026-03-03**
- `IsFresh` and `IsSpeaking` flags fed as observations without explicit `/ constant` normalization — may produce values outside [0,1] and destabilize PPO
- Reward calculation lacks `Mathf.Clamp` — spike risk confirmed
- NetworkObject access before `OnEnable()` / `OnNetworkSpawn()`
Fix: normalize flags with `isFresh ? 1f : 0f` (already binary, safe), clamp rewards via `AddRewardComponent()`.

### MEDIUM: Normalize speed in DialogueEffectProjectile.cs
**Category**: ml_agents
Speed value is clamped but not divided by a fixed denominator before being stored as an observation. Unnormalized speed in observation space destabilizes reward shaping.
```csharp
// WRONG
m_Speed = Mathf.Max(0.1f, speed);
// RIGHT — normalize against a known max speed constant
m_Speed = Mathf.Clamp01(speed / k_MaxSpeed);
```

### MEDIUM: StringComparer.Ordinal on Animator parameter dictionaries — CombatHealth.cs
**Category**: unity_best_practices
Animator parameter names can differ in casing across platforms. `StringComparer.Ordinal` on these dicts can cause missed lookups. Switch to `StringComparer.OrdinalIgnoreCase`.

### Scanner workflow improvements (2026-03-03)
- `docs_scanner.py` now documents all **14** manifest-present packages (was 4 — assembly filter bug fixed)
- `verify` now runs **31 checks** including package context health
- New FP guard in `unity_code_review.txt`: non-NetworkObject SerializeFields don't need OnNetworkSpawn guard
- Prompt guard eliminated the old `m_PlayerTransform` and `Mathf.Max` FPs from High severity
- Baseline comparison: H=17 (pre-context) → H=12 (post-context, −29%)

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
