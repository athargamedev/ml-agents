# Remote Runtime Refactor — 2026-03-03

## Summary
This memory records the major dialogue/runtime improvements completed during the March 3, 2026 refactor pass.

## 1. Dialogue runtime moved to project-owned remote config
- Added `DialogueBackendConfig` as the project-owned runtime config for:
  - LM Studio host/port
  - model name
  - sampling
  - grammar
  - stop sequences
  - system prompt
- `NetworkDialogueService` was migrated to use `DialogueBackendConfig` instead of treating vendor `LLMAgent` data as the source of truth.

## 2. Gameplay dialogue is now remote-only
- `NetworkDialogueService` now runs as a remote-only gameplay backend.
- `OpenAIChatClient` is the normal production backend.
- `SideChannelDialogueClient` remains as an explicit ML-Agents override only.
- The old local fallback path was removed from the live runtime.

## 3. Vendor LLMUnity dependency was removed
- The project first removed compile-time dependency on `undream.llmunity.Runtime`.
- Then the legacy bridge layer was removed.
- Then `Assets/Network_Game/LLMUnity` was deleted.
- `Behavior_Scene` was cleaned so `NetworkDialogueService` no longer carries the old `LLMAgent` component.

## 4. Effect-probe path was hardened for Qwen / LM Studio
- Effect-validation requests now use a dedicated low-latency constrained path.
- The effect probe token cap was reduced to `96`.
- Prompting was tightened to require only the final user-facing result.
- Structured output now uses `json_schema`, which matches this LM Studio server's accepted formats.
- This fixed the earlier `response_format.type` errors and reduced excessive hidden reasoning.

## 5. Training launch path was hardened
- `train_npc_dialogue.bat` now delegates to `run_training.py`.
- `run_training.py` is the canonical launcher.
- It handles stale port cleanup and avoids the previous fragile direct-trainer launch path.
- The Unity HTTP MCP readiness poll is now skipped by default and only enabled with `--unity-check`.

## 6. HUD cleanup
- `ModernHudController` was removed from the active HUD path.
- Runtime HUD references were migrated to `ModernHudManager`.

## 7. Operational architecture to preserve
- Dedicated server owns NPC dialogue generation.
- LM Studio is the primary inference gateway.
- Qwen models are the default runtime target.
- Clients should not regain local inference responsibilities.

## Recommended future rule
- Do not reintroduce `LLMUnity` or per-client local model inference unless there is a deliberate offline-only feature with a separate compatibility boundary.
