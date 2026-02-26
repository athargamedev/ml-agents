---
name: multiplayer-dialogue-debugger
description: Debug multiplayer dialogue/effect failures in this project by tracing UI blockers, parser output, server dispatch, ClientRpc replication, and effect spawn logs in a fixed order.
---

# Multiplayer Dialogue Debugger

Use this skill when a player/NPC dialogue interaction does not produce the expected effect or UI behavior.

## Fixed Debug Order (Do Not Skip)

1. Confirm runtime mode and players:
   - MPPM / player count / host role
2. Check UI blockers:
   - `DialogueEffectFeedbackPrompt`
   - debug overlays capturing input
3. Check dialogue response parse output:
   - schema validity
   - effect keys only
4. Check server dispatch path:
   - `NetworkDialogueService`
5. Check client replication:
   - ClientRpc logs
6. Check scene effect instantiate path:
   - `DialogueSceneEffectsController`
7. Check prefab/VFX runtime errors:
   - particle config / sub-emitter warnings

## Fast Commands / Tools

- `ng_effects_test_mode(status/start/stop/spawn_once)`
- `ng_get_effect_feedback_prompt_status`
- `read_console` with filtered categories
- `find_gameobjects` + object/component resources for target validation

## Deliverable Format

- Symptom
- Repro steps
- First failing stage in pipeline
- Evidence (logs/tool results)
- Fix candidate
- Verification step

## Project References

- `Assets/MCPTests/README_LLM_Automation.md`
- `Assets/Network_Game/Dialogue/NetworkDialogueService.cs`
- `Assets/Network_Game/Dialogue/DialogueSceneEffectsController.cs`

