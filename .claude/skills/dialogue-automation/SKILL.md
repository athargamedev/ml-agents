---
name: dialogue-automation
description: Automate the Unity multiplayer dialogue pipeline (NPC profiles, LLM effects, effect catalog) via domain-specific MCP tools. Use when the user mentions dialogue, NPC profiles, effect tags, effect catalog, LLM response testing, or dialogue pipeline status.
---

# Dialogue Pipeline Automation Skill

This skill routes dialogue-related tasks to domain-specific compound MCP tools that replace 5-15 generic calls with 1-2 targeted calls.

## NON-NEGOTIABLE RULES

1. ALWAYS prefer `ng_*` tools over generic `manage_scriptable_object`, `manage_asset`, or `manage_components` for dialogue work.
2. NEVER guess effect tag names — use `ng_catalog_summary` to list available tags first.
3. NEVER modify dialogue scripts without validating: `refresh_unity` → `read_console(types=["error"])`.
4. Effects are server-authoritative: NEVER bypass `NetworkDialogueService` for dispatch.
5. For Play Mode tools (`ng_probe_npc_dialogue`), always check `ng_pipeline_status` first to confirm readiness.

## Decision Tree

| User Intent | Tool to Use | Params |
|---|---|---|
| "What's the pipeline state?" / "Is the LLM ready?" | `ng_pipeline_status` | (none) |
| "Show me all effects" / "What effects exist?" | `ng_catalog_summary` | (none) |
| "Test the X effect" / "Preview X" | `ng_test_effect_tag` | `effect_tag` |
| "Add a new effect" / "Create effect for X" | `ng_create_effect_definition` | `effect_tag`, `description`, `prefab_name` |
| "Parse this LLM response" / "What tags are in this text?" | `ng_simulate_llm_response` | `response_text` |
| "Are all effects valid?" / "Health check" | `ng_bulk_validate_effects` | (none) |
| "Full diagnostic" / "Debug the pipeline" | `ng_get_full_diagnostics` | (none) |
| "Create a new NPC" | `ng_create_npc_profile` | `profile_id`, `display_name`, `system_prompt` |
| "Change NPC personality/prompt" | `ng_modify_npc_profile` | `profile_id`, `fields` |
| "Talk to the NPC" / "Send a message" | `ng_probe_npc_dialogue` | `player_message` |
| "Toggle effect feedback prompt" | `ng_toggle_effect_feedback_prompt` | (none) |
| "Run all effects test" | `ng_run_all_effects_direct` | (none) |

## Workflow Recipes

### Recipe: "Add New Effect End-to-End" (2 calls)
```
1. ng_create_effect_definition(effect_tag="Meteor", description="Massive meteor impact", prefab_name="BigExplosion", placement_mode="GroundAoe")
2. ng_test_effect_tag(effect_tag="Meteor")
```

### Recipe: "Create and Deploy New NPC" (1-2 calls)
```
1. ng_create_npc_profile(profile_id="npc.blacksmith", display_name="Grim the Blacksmith",
     system_prompt="You are Grim, a gruff blacksmith...",
     powers=[{"name":"Forge Fire","keywords":"fire,forge,flame","prefab_name":"FlameStream","element":"fire"}])
2. ng_pipeline_status()  // optional: confirm registration
```

### Recipe: "Debug Why an Effect Isn't Firing" (1 call)
```
1. ng_get_full_diagnostics()  // Shows catalog health, missing prefabs, console errors, LLM status
```

### Recipe: "Iterate on NPC Personality" (2 calls)
```
1. ng_modify_npc_profile(profile_id="npc.elder", fields={"system_prompt": "You are a wise elder who speaks in riddles..."})
2. ng_probe_npc_dialogue(player_message="Tell me about the dragon", npc_profile_id="npc.elder")
```

### Recipe: "Test LLM Response Parsing" (1 call)
```
1. ng_simulate_llm_response(response_text="I shall unleash my fury! [EFFECT: Fireball | Scale: 2.0 | Target: Player]", spawn_effects=true)
```

### Recipe: "Pre-Merge Health Check" (1 call)
```
1. ng_bulk_validate_effects()  // Catches missing prefabs, duplicate tags, orphan definitions
```

## Available Prefab Names (ParticlePack)

Fire: FireBall, BigExplosion, WildFire, FlameStream, LargeFlames, MediumFlames, TinyFlames, FlameThrower, Candles
Ice: IceLance
Storm: LightnigStormCloud, ElectricalSparks, ElectricalSparksEffect
Water: WaterFall, BigSplash, WaterLeak, Shower
Smoke: SmokeEffect, Steam, RisingSteam, DustStorm, GroundFog, PoisonGas, PressurisedSteam
Earth: DustExplosion, EarthShatter, SandSwirlsEffect
Explosion: EnergyExplosion, PlasmaExplosionEffect, TinyExplosion, SmallExplosion
Magic: Dissolve
Misc: SparksEffect, FireFlies, DustMotesEffect, HeatDistortion, RocketTrail

## Effect Tag Syntax (for LLM responses)

```
[EFFECT: TagName | Scale: 1.5 | Duration: 3.0 | Color: #FF0000 | Target: Player]
```

Parameters are optional. All are pipe-delimited. Case-insensitive matching.

## ScriptableObject Field Map

### NpcDialogueProfile
| Field | SerializedProperty | Type |
|---|---|---|
| Profile ID | `m_ProfileId` | string |
| Display Name | `m_DisplayName` | string |
| System Prompt | `m_SystemPrompt` | string (TextArea) |
| Lore | `m_Lore` | string (TextArea) |
| Bored Keywords | `m_BoredKeywords` | string[] |
| Enable Bored Light | `m_EnableBoredLightEffect` | bool |
| Bored Light Color | `m_BoredLightColor` | Color |
| Prefab Powers | `m_PrefabPowers` | PrefabPowerEntry[] |
| Dynamic Effect Params | `m_EnableDynamicEffectParameters` | bool |

### EffectDefinition
| Field | Type | Notes |
|---|---|---|
| effectTag | string | Exact name used in [EFFECT: X] |
| description | string | Shown in LLM prompt catalog |
| effectPrefab | GameObject | Particle effect prefab |
| placementMode | enum | Auto, AttachMesh, GroundAoe, SkyVolume, Projectile |
| targetType | enum | Auto, Player, Floor, Npc, WorldPoint |
| defaultScale/Duration/Color | float/float/Color | Defaults when LLM doesn't specify |
| alternativeTags | string[] | Legacy/synonym triggers |
| enableGameplayDamage | bool | Enables combat damage |

## Key Files

| System | Path |
|---|---|
| Pipeline Entry | `Assets/Network_Game/Dialogue/NetworkDialogueService.cs` |
| Effect Controller | `Assets/Network_Game/Dialogue/DialogueSceneEffectsController.cs` |
| Effect Catalog | `Assets/Network_Game/Dialogue/Effects/EffectCatalog.cs` |
| Effect Parser | `Assets/Network_Game/Dialogue/Effects/EffectParser.cs` |
| NPC Profile | `Assets/Network_Game/Dialogue/NpcDialogueProfile.cs` |
| MCP Bridge | `Assets/Network_Game/Dialogue/MCP/DialogueMCPBridge.cs` |
| MCP Custom Tools | `Assets/Network_Game/Editor/CustomTools/` |

## Anti-Patterns

- Do NOT use `manage_scriptable_object` for NPC profiles — use `ng_modify_npc_profile` instead.
- Do NOT parse effect tags manually — use `ng_simulate_llm_response` to run through the real parser.
- Do NOT hardcode conversation keys — the service resolves them from network IDs.
- Do NOT enable per-NPC `LLMAgent` components (single agent on NetworkDialogueService by design).
- Do NOT add new effect types without also registering them in EffectCatalog.
