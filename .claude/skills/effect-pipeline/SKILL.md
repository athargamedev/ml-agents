---
name: effect-pipeline
description: Create, test, and manage visual effects in the LLM dialogue pipeline. Use when the user is working on VFX, particle effects, effect definitions, effect catalog, or testing effects in the Unity scene.
---

# Effect Pipeline Skill

Focused workflow for authoring and testing dialogue-triggered visual effects.

## Quick Start

1. **See what exists**: `ng_catalog_summary` → lists all registered effects
2. **Add a new effect**: `ng_create_effect_definition` → creates SO + links prefab + registers
3. **Test it**: `ng_test_effect_tag` → validates + spawns preview in scene
4. **Validate all**: `ng_bulk_validate_effects` → catches broken links

## Effect Creation Workflow

### Step 1: Choose a prefab
Available in `Assets/Network_Game/ParticlePack/EffectExamples/`:

| Category | Prefabs |
|---|---|
| Fire & Explosion | FireBall, BigExplosion, EnergyExplosion, WildFire, FlameStream, LargeFlames, MediumFlames, TinyFlames, FlameThrower, TinyExplosion, SmallExplosion, PlasmaExplosionEffect |
| Ice | IceLance |
| Storm/Lightning | LightnigStormCloud, ElectricalSparks, ElectricalSparksEffect |
| Water | WaterFall, BigSplash, WaterLeak, Shower |
| Smoke/Steam | SmokeEffect, Steam, RisingSteam, DustStorm, GroundFog, PoisonGas, PressurisedSteam, RocketTrail |
| Earth | DustExplosion, EarthShatter, SandSwirlsEffect |
| Nature | FireFlies, DustMotesEffect |
| Misc | SparksEffect, Candles, HeatDistortion, Dissolve |

### Step 2: Create the EffectDefinition
```
ng_create_effect_definition(
  effect_tag="MyEffect",
  description="Short description for LLM prompt",
  prefab_name="FireBall",
  placement_mode="Projectile",  // Auto, AttachMesh, GroundAoe, SkyVolume, Projectile
  target_type="Player",          // Auto, Player, Floor, Npc, WorldPoint
  default_scale=1.0,
  default_duration=4.0,
  alternative_tags="synonym1,synonym2",
  enable_damage=false
)
```

### Step 3: Test
```
ng_test_effect_tag(effect_tag="MyEffect", spawn_preview=true)
```

### Step 4: Validate
```
ng_bulk_validate_effects()
```

## Placement Modes

| Mode | Use Case |
|---|---|
| `Auto` | System decides based on context |
| `AttachMesh` | Attaches to target's mesh (auras, enchantments) |
| `GroundAoe` | Spawns on ground (fire pools, earth effects) |
| `SkyVolume` | Spawns overhead (rain, lightning storms) |
| `Projectile` | Travels from caster to target (fireballs, ice lances) |

## Target Types

| Type | Description |
|---|---|
| `Auto` | Resolved from LLM target hint |
| `Player` | Always targets the player |
| `Floor` | Targets ground position |
| `Npc` | Targets the casting NPC |
| `WorldPoint` | Targets a named world position |

## LLM Tag Format

Effects are triggered by tags in LLM responses:
```
[EFFECT: TagName]
[EFFECT: TagName | Scale: 2.0 | Duration: 5.0 | Color: #FF0000 | Target: Player]
[FX: TagName]
[POWER: TagName]
EFFECT: TagName
```

Supported parameters: Scale, Duration, Color, Target, Anchor, Intensity, Radius, Speed, Damage, Emotion, PlacementType, GroundSnap, CollisionPolicy.

## Damage Configuration

Effects can apply gameplay damage when `enable_damage=true`:
- `damage_amount`: Base damage per impact
- `damageRadius`: AoE radius
- `affectPlayerOnly`: Only damage player actors
- `enableHoming`: Projectile steers toward target
- `projectileSpeed`: Travel speed (m/s)
- `homingTurnRateDegrees`: Steering speed (deg/s)

## Troubleshooting

| Problem | Check |
|---|---|
| Effect not spawning | `ng_test_effect_tag` → is it in the catalog? |
| LLM not triggering effect | `ng_simulate_llm_response` → does parser find the tag? |
| Prefab not found | Check ParticlePack paths, verify prefab name spelling |
| Effect too big/small | Adjust `defaultScale`, `minScale`, `maxScale` on EffectDefinition |
| Wrong position | Check `placementMode` and `targetType` on EffectDefinition |
| Full diagnostic | `ng_get_full_diagnostics` → pipeline + validation + console errors |
