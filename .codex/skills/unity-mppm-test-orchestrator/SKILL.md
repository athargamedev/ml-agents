---
name: unity-mppm-test-orchestrator
description: Run and monitor Multiplayer Play Mode (MPPM) gameplay automation in this project using UnityMCP and Network Game custom tools. Use for 2-player dialogue/effects tests, start/stop scenarios, prompt suppression, and result collection.
---

# Unity MPPM Test Orchestrator

Use this skill when the task is to run or debug a multiplayer gameplay scenario in Unity using MCP tools.

## Scope

- Multiplayer Play Mode (2-player) runtime orchestration
- Dialogue/effect scenario setup
- Effect feedback prompt suppression during visual tests
- Log capture and test summary

## Required Workflow

1. Check `mcpforunity://editor/state` and `mcpforunity://instances`.
2. Ensure Unity is in Play Mode (or start it with `manage_editor(play)`).
3. Query `ng_effects_test_mode` with `action="status"`.
4. For visual effect validation, start test mode:
   - `ng_effects_test_mode` with `action="start"`
   - defaults should disable feedback prompt and start direct all-effects suite
5. Poll logs with `read_console` and filter by:
   - `DialogueFX`
   - `EffectAutoTest`
   - `NetworkDialogueService`
6. On stop:
   - call `ng_effects_test_mode` with `action="stop"`
   - verify prompt state restored

## Default Log Filters

- `DialogueFX`
- `EffectAutoTest`
- `DialogueMCP`
- `NetworkDialogueService`
- `Combat`

## Output Format (Recommended)

- Scenario name / intent
- Players / mode (MPPM)
- Tool actions executed
- Key logs (filtered)
- Failures and likely layer (parser, dispatch, prompt blocker, target resolution, prefab spawn)
- Next action

## Project References

- `Assets/MCPTests/README_LLM_Automation.md`
- `Assets/MCPTests/LLM_Dialogue_Automation_Playbook.md`
- `Assets/MCPTests/LLM_Dialogue_Automation_Checklist.md`

