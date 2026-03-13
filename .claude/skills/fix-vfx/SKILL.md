---
name: fix-vfx
description: Diagnose and fix Unity VFX and particle effects using UnityMCP manage_vfx tool. Use when the user reports broken particle effects, wrong VFX scale/position/color, effects not playing, dialogue VFX issues, NPC effect tags not working, VisualEffect Graph problems, or any particle system issues.
---

# Fix VFX Skill — Unity Particle & VFX Repair via UnityMCP

Diagnoses and repairs ParticleSystem, VisualEffect, LineRenderer, and TrailRenderer components.
Project-aware: understands the `EffectDefinition → EffectCatalog → NpcDialogueProfile` pipeline.

## NON-NEGOTIABLE RULES

1. ALWAYS read `mcpforunity://editor/state` before any mutation.
2. ALWAYS `particle_get_info` (or `vfx_get_info`) before changing properties — read before write.
3. NEVER set VFX properties without knowing the current values first.
4. For dialogue effects: ALSO check the `EffectDefinition` C# asset — the source of truth for scale/placement.
5. Use `batch_execute` for 2+ property changes.
6. ALWAYS verify with `manage_scene(action="screenshot")` for visual effects.
7. If effect still looks wrong after VFX fix → check `EffectDefinition.defaultScale` in C# asset.

## Decision Tree

| User Says | First Action | Then |
|---|---|---|
| "Effect not playing" / "VFX invisible" | `particle_get_info` on target | Check emission rate, playOnAwake, object enabled |
| "Effect too big / too small" | `particle_get_info` → check startSize | `particle_set_properties` with correct startSize; also check `EffectDefinition.defaultScale` |
| "Effect in wrong position" | Check parent/attachment in hierarchy | Verify `EffectDefinition.placementMode` and `attachBone` |
| "Effect plays once then stops" | `particle_get_info` → check looping | `particle_set_properties` with `looping: true` |
| "Effect plays on startup but shouldn't" | `particle_get_info` → check playOnAwake | `particle_set_properties` with `playOnAwake: false` |
| "Wrong color / color not matching dialogue" | `particle_get_info` → startColor | `particle_set_properties` with correct color |
| "VisualEffect Graph broken" | `vfx_get_info` on target | Check event bindings, exposed properties |
| "Trail/Line renderer wrong" | `trail_get_info` / `line_get_info` | Set width, color, positions |
| "Dialogue effect tag X not working" | Check `EffectCatalog` asset | Verify effectTag matches key in catalog; check `EffectParser.cs` |

## Standard Workflow: Diagnose & Fix

### Step 1 — Pre-flight
```python
# Check editor is ready
resource: mcpforunity://editor/state

# Find the target VFX GameObject
find_gameobjects(search_type="by_name", name="<EffectName>")
# Or search by tag if effects use a tag
find_gameobjects(search_type="by_tag", tag="DialogueEffect")
```

### Step 2 — Inspect VFX
```python
manage_vfx(
    action="particle_get_info",
    target="<id_from_step1>",
    search_method="by_id"
)
```
Key fields to check:
- `isPlaying` — is the system currently emitting?
- `playOnAwake` — auto-starts on scene load
- `looping` — repeats vs. one-shot
- `duration` — system lifetime before stopping
- `emissionRate` — particles per second (0 = invisible!)
- `startSize` — size of each particle
- `startColor` — initial particle color (RGBA 0-1)
- `maxParticles` — cap on simultaneous particles
- `simulationSpace` — `World` vs `Local` (affects position behavior)

### Step 3 — Diagnose Common Issues

| Symptom | Root Cause | Fix |
|---|---|---|
| Effect invisible | `emissionRate=0` or `maxParticles=0` | Set `emissionRate` > 0 |
| Effect invisible | `startSize` too small (< 0.001) | Set to match `EffectDefinition.defaultScale` |
| Effect at origin (0,0,0) | `simulationSpace=World` + no parent | Switch to `Local` or fix parent attachment |
| Effect plays once and dies | `looping=false` + not re-triggered | Set `looping=true` or ensure trigger from dialogue |
| Effect floods scene | `emissionRate` too high | Reduce to match effect_feedback_tuning.json values |
| Effect starts on load | `playOnAwake=true` on idle NPC | Set `playOnAwake=false` |
| Color wrong / too bright | `startColor` alpha too high | Set alpha to 0.5-0.8 range |
| VisualEffect bindings missing | VFX graph event not wired | Use `vfx_set_properties` to bind events |

### Step 4 — Apply Fix
```python
# Example: Fix invisible effect (emission + size)
batch_execute(commands=[
    {
        "tool": "manage_vfx",
        "params": {
            "action": "particle_set_properties",
            "target": "<instance_id>",
            "search_method": "by_id",
            "properties": {
                "emissionRate": 15,
                "startSize": 0.3,
                "startColor": [1.0, 0.8, 0.2, 0.7],
                "playOnAwake": False,
                "looping": True,
                "duration": 2.0,
                "maxParticles": 50
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

## Project-Specific: Dialogue Effect Pipeline

This project uses a custom **LLM-driven effect system** that bridges NPC dialogue to VFX:

```
LLM response
    → EffectParser.cs (parses <effect:tag> tokens)
    → EffectCatalog (maps tag string → EffectDefinition asset)
    → EffectDefinition (defaultScale, placementMode, attachBone, particlePrefab)
    → ParticleSystem on NPC
```

### Key C# Classes (read-only reference)
| Class | File | Purpose |
|---|---|---|
| `EffectDefinition` | `Network_Game/Dialogue/` | Per-effect data: scale, placement, bone, prefab |
| `EffectCatalog` | `Network_Game/Dialogue/` | Dictionary: tag string → EffectDefinition |
| `EffectParser` | `Network_Game/Dialogue/Effects/EffectParser.cs` | Parses `<effect:tag>` from LLM text |
| `ParticleParameterExtractor` | `Network_Game/Dialogue/` | Reads particle system state for LLM feedback |

### EffectDefinition Fields (what drives VFX at runtime)
| Field | Type | Maps to Particle Property |
|---|---|---|
| `defaultScale` | float | `startSize` |
| `placementMode` | enum | determines parent GameObject |
| `attachBone` | string | bone name for body attachment |
| `loopDuration` | float | `duration` |
| `isLooping` | bool | `looping` |
| `baseColor` | Color | `startColor` |

**Important**: If particle properties look correct in Unity but the effect still looks wrong in play mode → the `EffectDefinition` asset is overriding them at runtime. Check `EffectCatalog` and the relevant `EffectDefinition`.

### Effect Feedback Tuning
- `output/effect_feedback_tuning.json` — LLM-rated effect quality scores
- `scaleMultiplier` field tracks how the LLM adjusts visual intensity over time
- When fixing scale: cross-reference `defaultScale` in EffectDefinition AND `scaleMultiplier` in this JSON

### Common Dialogue Effect Tags (from EffectParser)
| Tag | Expected Effect |
|---|---|
| `<effect:happy>` | Light sparkle particles around NPC head |
| `<effect:angry>` | Red burst particles |
| `<effect:thinking>` | Floating question marks / slow orbit |
| `<effect:resolved>` | Green checkmark burst |

If an effect tag produces no VFX → the tag is not registered in `EffectCatalog`. Check and add the mapping.

## Action Reference

### ParticleSystem (`particle_*`)
| Action | Key Properties | Notes |
|---|---|---|
| `particle_get_info` | — | target required |
| `particle_set_properties` | `emissionRate`, `duration`, `looping`, `playOnAwake`, `startSize`, `startColor`, `maxParticles`, `simulationSpace`, `startSpeed`, `startLifetime` | All optional — only set what needs changing |
| `particle_play` | — | Starts emission |
| `particle_stop` | — | Stops emission |
| `particle_clear` | — | Clears all particles immediately |

### VisualEffect Graph (`vfx_*`)
| Action | Key Properties | Notes |
|---|---|---|
| `vfx_get_info` | — | Returns exposed properties, event list |
| `vfx_set_properties` | `asset_path`, `exposed_properties` (dict) | For VisualEffect Graph exposed inputs |
| `vfx_send_event` | `event_name` | Triggers a VFX event manually |

### LineRenderer / TrailRenderer
| Action | Purpose |
|---|---|
| `line_get_info` | Get points, width, color |
| `line_set_properties` | Set positions, width, color |
| `trail_get_info` | Get trail properties |
| `trail_set_properties` | Set time, width, color |

## Known Gotchas

- `particle_set_properties` applies to the **root** ParticleSystem. Child sub-emitters must be targeted separately by their child GameObject IDs.
- `startColor` is RGBA in 0-1 range, NOT 0-255. Alpha=0 = completely invisible even with emissionRate > 0.
- `simulationSpace=World` means particles drift away from moving NPCs — almost always want `Local` for attached effects.
- VFX Graph (`.vfx` assets) uses `vfx_*` actions, NOT `particle_*`. The two are different components.
- Effects attached to bones via `attachBone` may detach if the NPC Animator changes state — verify parent hierarchy after animation changes.
- `playOnAwake=true` effects will fire on domain reload in Edit mode — expected behavior, not a bug.
- After changing `EffectDefinition` in C# → Unity must recompile before VFX changes take effect in play mode.
- `output/effect_feedback_tuning.json` may override visual parameters at runtime — check it if manual fixes are reverted on next play.
