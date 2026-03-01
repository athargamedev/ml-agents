# C# Coding Conventions — Confirmed for This Project

Confirmed by reading source files + LLM scan (2026-02-28).
Enforce when writing or reviewing any C# in DevProject/Assets/.

---

## Naming

| Kind | Convention | Example |
|------|-----------|---------|
| Private fields | `m_` prefix | `m_PlayerTransform`, `m_TurnCount` |
| Constants | `k_` prefix | `k_MaxTurns`, `k_DecaySeconds` |
| Classes / Methods / Properties | PascalCase | `NpcDialogueAgent`, `OnEpisodeBegin()` |
| Interfaces | `I` prefix | `IDialogueInferenceClient`, `IActuator` |
| Namespaces | PascalCase, dots | `Network_Game.Dialogue.Effects` |
| Enums | PascalCase values | `ConversationPhase.Idle` |
| Inspector-assigned private fields | MUST have `[SerializeField]` | `[SerializeField] private Transform m_PlayerTransform;` |

---

## ML-Agents Reward Shaping Pattern

All rewards go through `AddRewardComponent(float amount, string statName)`:
1. Clamps to `Mathf.Clamp(amount, -0.5f, 0.5f)` — prevents PPO gradient spikes
2. Calls `AddReward(amount)`
3. Records TensorBoard stat: `NpcDialogue/Reward/{statName}`

**Naming pattern for statName**: `Category/SpecificName`
Examples: `"Latency/FastBonus"`, `"Quality/FeedbackScore"`, `"Action/Engage"`, `"Outcome/TurnComplete"`

Never call `AddReward()` directly — always use `AddRewardComponent()`.

---

## ML-Agents Safety Rules

1. **Never call EndEpisode() inside CollectObservations()** — triggers double-reset
2. **Never call RequestDecision() manually** when `DecisionRequester` is present — causes double request
3. **Always null-check Academy.Instance** before accessing `StatsRecorder`
   ```csharp
   if (Academy.Instance != null) Academy.Instance.StatsRecorder.Add("stat", value);
   ```
4. **All observations must be normalised** to [0, 1]:
   ```csharp
   sensor.AddObservation(Mathf.Clamp01(value / maxValue)); // not value / maxValue alone
   ```
5. **DecisionPeriod = 5** (not 1) — at 11s LLM latency, period=1 produces 550 idle decisions per wait
6. **Space Size = 7** — exact value required by BehaviorParameters. Changing obs count requires Inspector update

---

## Multiplayer Safety Rules (Unity Netcode for GameObjects)

1. **ClientRpc writes**: never modify authoritative state inside `[ClientRpc]` — must check `IsOwner`
2. **NetworkVariable writes**: only on server (or owner if permission=OwnerWritable)
3. **NetworkObject access**: always null-check AND check `IsSpawned` before calling methods
4. **Singleton access pattern**:
   ```csharp
   // Wrong
   NetworkDialogueService.Instance.DoThing();
   // Right
   NetworkDialogueService.Instance?.DoThing();
   // Or: if (NetworkDialogueService.Instance != null) NetworkDialogueService.Instance.DoThing();
   ```

---

## Unity Performance Rules

1. **Cache GetComponent<T>()** in `Awake()` or `Start()` — never in `Update()` or `OnActionReceived()`
2. **Cache service references** — never `FindObjectOfType<T>()` in hot paths
3. **Avoid string allocation in Update** — only update text when value changes
4. **Effect lookup**: always use `EffectCatalog.TryGet(tag, out effect)` and check return value first

---

## LM Studio Integration Rules (confirmed by bridge experience)

1. **No `response_format=json_object`** — LM Studio logs "Unexpected endpoint" error
   - Fix: request JSON explicitly in system prompt, parse manually with fallback to raw text
2. **Token limit for code review**: use 900+ tokens — 600 causes JSON truncation mid-response
3. **Model detection**: use `client.models.list()` to auto-detect; don't hardcode model name
   - Current loaded model: `llama-3.2-3b-instruct@q4_k_s`
4. **API key format**: `sk-lm-...` (see run_llm_bridge.py for actual key)
5. **Port 7002** — always the LM Studio port for this project

---

## Assembly / Namespace Rules

- `Network_Game.asmdef`: autoReferenced=true — no explicit reference needed
- `Unity.ML-Agents.asmdef`: autoReferenced=true
- New scripts go in Assembly-CSharp unless creating a new asmdef
- Imports needed for ML-Agents: `using Unity.MLAgents;` + `using Unity.MLAgents.Policies;` (separate)
- Netcode namespace: `using Unity.Netcode;`
