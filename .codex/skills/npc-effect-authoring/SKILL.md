---
name: npc-effect-authoring
description: Safely author and validate NPC dialogue effect mappings and runtime effect behavior in this project using deterministic keys, direct spawn tests, and multiplayer-safe verification.
---

# NPC Effect Authoring

Use this skill when changing NPC effect mappings, prefab powers, or dialogue-triggered FX behavior.

## Authoring Rules

- Prefer deterministic effect keys and registry-backed lookups
- Avoid introducing synonyms without explicit mapping
- Preserve server-authoritative dispatch path
- Keep changes idempotent and validate after edits

## Fast Validation Loop

1. Apply structured edit to NPC effect mapping / profile.
2. Validate scripts.
3. Enter Play Mode / MPPM if needed.
4. Disable feedback prompt via `ng_effects_test_mode(start)` or `ng_disable_effect_feedback_prompt`.
5. Trigger direct effect:
   - `ng_spawn_direct_npc_power`
   - or `ng_effects_test_mode(action="spawn_once")`
6. Review `DialogueFX` and `EffectAutoTest` logs.

## Failure Checklist

- Wrong target / missing anchor
- Prompt blocked / deferred effect
- Invalid prefab or VFX config
- Client instantiate failed after server dispatch
- Collision/trail/light module config mismatch

## References

- `Assets/Network_Game/Dialogue/DialogueEffectAutoTestMenu.cs`
- `Assets/Network_Game/Dialogue/DialogueSceneEffectsController.cs`
- `Assets/MCPTests/LLM_Dialogue_Automation_Playbook.md`

