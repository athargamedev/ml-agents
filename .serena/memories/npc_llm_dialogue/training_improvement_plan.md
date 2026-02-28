# NPC Dialogue Training System — 10x Improvement Plan

## Status Baseline (analysed 2026-02-27)
- 5 training runs completed, all manually stopped at ~31-34K steps (max_steps = 2M)
- Best reward: 0.075 after 34K steps (policy barely learning)
- Today's run (`npc_dialogue_2702Fri_202619`) died before initialization (no config.yaml written)
- Effect tuner has runaway scaleMultiplier: ElectricalSparks=2.61×, FireBall=2.57×, SmokeEffect=2.19×
- Root cause: effects spawn at y=27-32 (out of camera), scale can't fix visibility
- NpcDialogueAgent code is correct (7 obs, tests pass), training infra is the bottleneck

---

## Phase 1 — Training Stability (do first, unblocks everything)

### P1.1 — Always resume from last checkpoint
- Add `--resume` flag to the `mlagents-learn` call in `run_llm_bridge.py`
  or a new `run_training.py` wrapper
- Fallback: if no checkpoint, start fresh — never lose progress again

### P1.2 — Port collision auto-cleaner
- Detect and kill orphan Python processes holding port 5004 before trainer starts
- Add to `run_llm_bridge.py` startup: `netstat -ano | findstr :5004` → kill stale PIDs

### P1.3 — Trainer watchdog / auto-restart
- Shell script (or Python subprocess loop) that relaunches `mlagents-learn` if it dies
- Pass `--resume` so each restart continues from last checkpoint

### P1.4 — Unity readiness gate
- Before launching trainer, poll Unity's HTTP health endpoint
  (`GET http://localhost:8009/mcp`) to confirm bridge is alive
- Log clear instructions if Unity not connected

### P1.5 — Training YAML: tighten checkpointing
- `summary_freq: 1000` (was 5000) — finer TensorBoard resolution
- `time_horizon: 128` (was 64) — covers full realistic conversations
- `max_steps: 5000000` (was 2M) — commit to real training runs
- Files: `config/ppo/NpcDialogue.yaml`

---

## Phase 2 — Effect Tuning Pipeline Fix

### P2.1 — scaleMultiplier hard cap
- Add `MaxScaleMultiplier` constant (e.g. 3.5) to effect tuner
- If `attachScore < 0.1` AND `sampleCount > 5` → flag effect as "positionally broken"
  rather than keep scaling
- File: wherever EffectFeedbackTuner/EffectTuningTable is written

### P2.2 — Fix probe position anchor for non-attached effects
- Effects with `attach_to_target=false` spawn at stale probe coordinates (y=27-32)
- Fix: when `attach_to_target=false`, use `target_position + Vector3.forward * 2f`
  relative to target rather than a cached world position
- File: wherever automation probe places effects in the scene

### P2.3 — Placement disfavor → hard exclusion
- `automation_probe_placement_memory.json` marks placements as "disfavored" but
  they're still tried; convert disfavored placements to probability=0 (skip entirely)

### P2.4 — Visibility ratio tracking
- Track `looks_correct_count / total_count` per effect in tuning JSON
- Only tune scaleMultiplier if visibility ratio > 0 (otherwise it's a position issue)

---

## Phase 3 — Reward Signal Quality

### P3.1 — Effect outcome feeds reward directly
- When `FeedbackScoreSummary.HasEffect=true` AND visual feedback `outcome=looks_correct`
  → add bonus reward (e.g. +0.15 * OutcomeRewardScale)
- When `outcome=not_visible` or `wrong_target` → add penalty (-0.1)
- Currently only feedback Score (quality) flows in, not the visual outcome string
- File: `NpcDialogueAgent.cs` → `HandleFeedbackScore()`

### P3.2 — Reward component clip
- No single `AddRewardComponent()` call should exceed ±0.5 in magnitude
- Add `Mathf.Clamp(amount, -0.5f, 0.5f)` inside `AddRewardComponent()`
- Prevents rare spikes from dominating gradient

### P3.3 — Episode-end reward summary
- On `EndEpisode()` (action 2 path), log a structured summary:
  `[NpcDialogueAgent][EpisodeSummary] cumulative=X turns=Y effects=Z phases=[...]`
- Helps manual debugging without TensorBoard

### P3.4 — Shaped reward: conversation arc
- Small bonus (+0.02) when phase transitions Idle→Initiated→Responded→Resolved in order
  (i.e. completing the full arc)
- Encourages the policy to drive complete conversations, not just idle

---

## Phase 4 — NpcDialogueAgent Code Improvements

### P4.1 — Max-turns action mask
- When `m_TurnCount >= 20`, mask out action 1 (Engage)
- Prevents policy from looping endlessly in long low-reward conversations
- File: `NpcDialogueAgent.cs` → `WriteDiscreteActionMask()`

### P4.2 — Conversation timeout detection
- If `m_ConversationPhase == Initiated` for more than 30 real seconds (Time.time delta)
  with no telemetry arriving → fire penalty (-0.08) and reset phase to Idle
- Handles LM Studio crashes mid-conversation gracefully

### P4.3 — Extract reward config to ScriptableObject
- Create `NpcDialogueRewardConfig.asset` (ScriptableObject)
- Move all 8 reward scale fields from Inspector serialized fields to this asset
- Reference from NpcDialogueAgent via `[SerializeField] NpcDialogueRewardConfig m_RewardConfig`
- Allows tweaking reward weights at runtime without recompile

### P4.4 — DialogueRewardShaper static helper
- Move all AddReward math into `DialogueRewardShaper.cs`
- Pure static methods: `ComputeLatencyReward(float ms, NpcDialogueRewardConfig cfg)`
  `ComputeFeedbackReward(FeedbackScoreSummary feedback, NpcDialogueRewardConfig cfg)` etc.
- Makes unit testing reward logic easy without spinning up a full Agent

---

## Phase 5 — Python Bridge Improvements

### P5.1 — Async queue in run_llm_bridge.py
- Replace synchronous LLM calls with `asyncio.Queue` producer-consumer
- `flush_responses()` pops from queue — no more blocking per-step
- Handles multiple in-flight LLM requests without blocking `env.step()`

### P5.2 — Trainer metrics logging
- Bridge logs to `.codex/tmp/training_<run_id>.jsonl`:
  `{"step": N, "cumulative_reward": X, "episode": E, "ts": "..."}` per episode
- Gives training progress without needing TensorBoard open

### P5.3 — Run-id continuity
- Auto-detect last run_id from `results/` directory (sort by mtime)
- Pass `--resume --run-id <last_id>` automatically
- New `--fresh` flag to force a new run

### P5.4 — LLM handler registry
- Replace `make_lmstudio_handler()` / `make_openai_handler()` with a dict registry
- `HANDLER_REGISTRY = {"lmstudio": ..., "openai": ..., "mock": ...}`
- Selected via `--backend lmstudio` CLI flag

---

## Phase 6 — Training Config (NpcDialogue.yaml) Target State

```yaml
hyperparameters:
  batch_size: 256         # was 128 — more data per update
  buffer_size: 4096       # was 2048
  learning_rate: 0.0002   # slightly reduced for stability
  beta: 0.005             # slightly reduced after early training
  
network_settings:
  hidden_units: 256       # was 128 — richer representations
  num_layers: 3           # was 2
  memory:
    sequence_length: 64   # was 32 — covers full conversations
    memory_size: 256      # was 128

reward_signals:
  extrinsic: {gamma: 0.99, strength: 1.0}
  curiosity: {gamma: 0.99, strength: 0.005}  # reduce as policy matures

max_steps: 5000000
time_horizon: 128
summary_freq: 1000

# Enable curriculum (uncomment when reward > 0.3 consistently)
```

---

## Phase 7 — New Support Files

### P7.1 — `run_training.py` (new, repo root)
- Unified entry point replacing raw `mlagents-learn` calls
- Handles: port cleanup, Unity readiness check, --resume logic, watchdog restart
- Usage: `python run_training.py [--fresh] [--backend lmstudio|mock]`

### P7.2 — `NpcTrainingStateDisplay.cs` (new)
- In-scene MonoBehaviour rendering live stats (IMGUI or TextMeshPro overlay):
  Episode #, Cumulative Reward, Turn Count, Phase, Effect State
- Only active in Editor (wrapped in `#if UNITY_EDITOR`)

### P7.3 — `NpcDialogueRewardConfig.asset` (new ScriptableObject)
- Central store for all reward weights
- Ship with sensible defaults; committed to repo

---

## Implementation Order (recommended)

```
Week 1:  P1.1 + P1.2 + P1.4 + P1.5  ← get training running stably
Week 2:  P2.1 + P2.2 + P2.3          ← fix the not_visible plague
Week 3:  P3.1 + P3.2 + P4.1 + P4.2  ← improve reward signal
Week 4:  P4.3 + P4.4 + P5.1 + P5.3  ← code quality + async bridge
Week 5:  P6 (YAML update) + P7       ← full config + tooling
```

---

## Key Files Modified by This Plan

| File | Change |
|------|--------|
| `config/ppo/NpcDialogue.yaml` | bigger network, longer sequences, 5M steps |
| `DevProject/Assets/ML-Agents/Scripts/NpcDialogueAgent.cs` | P4.1, P4.2, P3.2, P3.3, P3.4 |
| `run_llm_bridge.py` | P1.2, P5.1, P5.3 |
| Effect tuner source | P2.1, P2.2, P2.3, P2.4 |
| **new** `run_training.py` | P7.1 |
| **new** `NpcDialogueRewardConfig.cs/.asset` | P4.3 |
| **new** `DialogueRewardShaper.cs` | P4.4 |
| **new** `NpcTrainingStateDisplay.cs` | P7.2 |
