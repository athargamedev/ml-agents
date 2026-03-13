---
name: fix-animation
description: Diagnose and fix Unity Animator issues on player characters and NPCs using UnityMCP manage_animation tool. Use when the user reports broken animations, missing transitions, wrong parameters, Animator controller issues, or animation state machine problems.
---

# Fix Animation Skill — Unity Animator Repair via UnityMCP

Diagnoses and repairs Animator components, AnimatorController assets, and AnimationClip issues using `manage_animation`.

## NON-NEGOTIABLE RULES

1. ALWAYS read `mcpforunity://editor/state` first — never touch animations while compiling.
2. ALWAYS `animator_get_info` before changing anything — read before write.
3. NEVER guess parameter names — get them via `animator_get_info` or `controller_get_info`.
4. For 2+ operations, use `batch_execute` (10-100x faster than individual calls).
5. ALWAYS verify with `manage_scene(action="screenshot")` after a visual fix.
6. If adding a transition: always check both source and destination states exist first.

## Decision Tree

| User Says | First Action | Then |
|---|---|---|
| "Animation not playing" / "stuck in T-pose" | `animator_get_info` on target | Check if controller assigned, state exists |
| "Transition not firing" / "wrong state" | `animator_get_info` → check conditions | `controller_add_transition` with correct conditions |
| "Parameter missing" / "Animator parameter not found" | `animator_get_info` → check parameters | `controller_add_parameter` |
| "Create new animation clip" | `clip_create` with path | `clip_add_curve` → `clip_assign` to state |
| "Create Animator Controller from scratch" | `controller_create` | Add states → transitions → parameters |
| "Player idle/run/jump broken" | `animator_get_info` on Player/Agent | Check Blend Tree and speed parameter |
| "NPC animation broken" | `find_gameobjects` by tag "NPC" → `animator_get_info` | Check dialogue state triggers |

## Standard Workflow: Diagnose & Fix

### Step 1 — Pre-flight
```python
# Check editor is ready
resource: mcpforunity://editor/state

# Find the target GameObject (use ID once found — avoids name collisions)
find_gameobjects(search_type="by_name", name="PlayerAgent")   # or Agent, NPC, etc.
```

### Step 2 — Inspect Animator
```python
manage_animation(
    action="animator_get_info",
    target="<id_from_step1>",
    search_method="by_id"
)
```
Key fields to check in the response:
- `controller` — path to AnimatorController asset (null = no controller assigned!)
- `currentStateName` — which state is currently active
- `parameters` — list of `{name, type, value}` (trigger/bool/int/float)
- `layers` — layer names and weights
- `isPlaying` — whether Animator is running

### Step 3 — Diagnose Common Issues

| Symptom | Root Cause | Fix Action |
|---|---|---|
| `controller` is null | No AnimatorController assigned | `controller_create` + assign via `manage_components` |
| State exists but won't play | Missing entry transition | `controller_add_transition` from `AnyState` or `Entry` |
| Parameter not in list | Script calls `SetTrigger("X")` but "X" not declared | `controller_add_parameter` |
| Wrong animation plays | Transition conditions wrong | `controller_add_transition` with corrected conditions |
| Animation loops when shouldn't | Clip loop settings | `clip_create` with `loop=false` |
| Animation frozen at frame 0 | Animator not enabled or speed=0 | `animator_set_parameter(name="Speed", value=1.0)` |

### Step 4 — Apply Fix (batch when possible)
```python
# Example: Add missing parameter + fix transition in one batch
batch_execute(commands=[
    {
        "tool": "manage_animation",
        "params": {
            "action": "controller_add_parameter",
            "controller_path": "Assets/Animators/PlayerAgent.controller",
            "properties": {"name": "IsRunning", "type": "Bool", "default_value": False}
        }
    },
    {
        "tool": "manage_animation",
        "params": {
            "action": "controller_add_transition",
            "controller_path": "Assets/Animators/PlayerAgent.controller",
            "properties": {
                "source_state": "Idle",
                "destination_state": "Run",
                "conditions": [{"parameter": "IsRunning", "mode": "If", "threshold": 0}],
                "has_exit_time": False,
                "transition_duration": 0.1
            }
        }
    }
], fail_fast=True)
```

### Step 5 — Verify
```python
manage_scene(action="screenshot")
read_console(types=["error", "warning"], count=10)
```

## Project-Specific Context

### Player/Agent Animator (ML-Agents)
- Agent GameObjects are in `DevProject/Assets/ML-Agents/Examples/`
- The `NpcDialogueAgent` does NOT drive animation directly — it drives ML behavior
- If the agent cube (pyramids) has VFX artifacts, the issue is likely **material/VFX**, not Animator
- Typical parameter names: `Speed` (float), `IsGrounded` (bool), `Attack` (trigger)

### NPC Animator
- NPCs are linked to the `NetworkDialogueService` dialogue pipeline
- Dialogue state transitions may trigger Animator triggers via C# events
- Relevant C# events that fire triggers: `OnDialogueStarted`, `OnPhaseChanged`, `OnDialogueResolved`
- If NPC animation stops during dialogue → check if the trigger name matches the parameter declared in controller

### AnimatorController Asset Paths
- Player/Agent: typically `Assets/ML-Agents/Examples/*/Prefabs/*.controller`
- NPCs: `Assets/Network_Game/Prefabs/*.controller` (check with `manage_asset(action="search", search="*.controller")`)

## Action Reference

| Action | Required Properties | Notes |
|---|---|---|
| `animator_get_info` | — | target required |
| `animator_play` | `state_name` | optional `layer` |
| `animator_crossfade` | `state_name`, `transition_duration` | smooth blend |
| `animator_set_parameter` | `name`, `value` | type inferred from value |
| `controller_create` | `name` | controller_path sets output location |
| `controller_add_state` | `state_name` | optional `clip_path`, `is_default` |
| `controller_add_transition` | `source_state`, `destination_state` | optional `conditions`, `has_exit_time`, `transition_duration` |
| `controller_add_parameter` | `name`, `type` | type: `Float`, `Int`, `Bool`, `Trigger` |
| `clip_create` | `name` | clip_path sets output location; optional `length`, `loop` |
| `clip_add_curve` | `property_path`, `keyframes` | keyframes: `[{time, value}, ...]` |
| `clip_assign` | `state_name`, `clip_path` | assigns clip to a controller state |

## Known Gotchas

- `animator_get_info` returns the **runtime** state. If Animator is disabled or in Edit mode, some fields may be null.
- AnimatorController must be **saved** as an asset before states/transitions can be added — always provide `controller_path`.
- `controller_add_transition` with `has_exit_time=true` will wait for the full animation to finish before transitioning — often wrong for responsive gameplay.
- Name collisions: multiple NPCs may share the same Animator name — always resolve by `instance_id` after `find_gameobjects`.
- After creating a new AnimatorController, assign it via `manage_components(action="set_property")` on the Animator component.
- ML-Agents `DecisionPeriod=5` means animation updates may lag by 5 frames — this is expected, not a bug.
