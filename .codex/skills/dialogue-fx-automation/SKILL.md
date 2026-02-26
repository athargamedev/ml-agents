---
name: dialogue-fx-automation
description: Automate the LLM dialogue to effect dispatch workflow in this project with deterministic effect keys, server-authoritative checks, and validation steps. Use for dialogue/effect pipeline changes and runtime verification.
---

# Dialogue FX Automation

Use this skill when working on dialogue parsing, effect key mapping, runtime dispatch, or effect quality verification.

## Core Rules (Project-Specific)

- Server-authoritative dispatch only
- Deterministic effect keys (no free-form effect names)
- Structured edits first, then validation
- Idempotent operations must return `no_op` on repeat

## Safe Change Sequence

1. Read current runtime entry points:
   - `NetworkDialogueService`
   - `DialogueSceneEffectsController`
2. Apply structured edits (avoid free-form whole-file edits).
3. Validate edited scripts.
4. Run a focused runtime test:
   - `ng_spawn_direct_npc_power` or `ng_effects_test_mode(action="spawn_once")`
5. Review logs for:
   - dispatch started
   - target resolved
   - client instantiate success/failure
   - prompt blocking/defer/drop

## Prompt Efficiency Constraints

- Keep response schema minimal (`text`, `effect keys`)
- Prefer fixed effect registry vocabulary
- Reject unmapped synonyms unless added to registry explicitly

## Failure Triage Order

1. Prompt blocker / feedback UI interference
2. Invalid effect key
3. Target resolution / missing component
4. Server dispatch path
5. Client RPC receipt
6. Prefab/VFX configuration errors

## Project References

- `Assets/MCPTests/README_LLM_Automation.md`
- `Assets/MCPTests/LLM_Dialogue_Automation_Playbook.md`
- `Assets/MCPTests/LLM_Dialogue_Automation_Checklist.md`

