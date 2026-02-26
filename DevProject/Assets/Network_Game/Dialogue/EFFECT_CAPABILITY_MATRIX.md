# Dialogue Effect Capability Matrix

Last updated: 2026-02-12
Scope: `Behavior_Scene` + multiplayer dialogue runtime

## 1) Runtime effect pipeline (current contract)
- Effect context input: combined player prompt + NPC response (`BuildEffectContextText`) in `Assets/Network_Game/Dialogue/NetworkDialogueService.cs:5061`.
- Effect trigger checks: keyword matching via `ContainsAnyKeyword` in `Assets/Network_Game/Dialogue/NetworkDialogueService.cs:4776`.
- Effect dispatch point: `ApplyContextEffects` in `Assets/Network_Game/Dialogue/NetworkDialogueService.cs:3988`.
- Multiplayer application: server determines effects, then sends per-effect `ClientRpc` calls (`Apply*EffectClientRpc` methods around `Assets/Network_Game/Dialogue/NetworkDialogueService.cs:4613`).
- Scene execution: `DialogueSceneEffectsController` methods (`ApplyRainBurst`, `ApplyShockwave`, `ApplyShieldBubble`, `ApplyWaypointPing`, `ApplyPrefabPower`) in `Assets/Network_Game/Dialogue/DialogueSceneEffectsController.cs:287` and below.

## 2) Dynamic parameter controls (global)
Source: `Assets/Network_Game/Dialogue/ParticleParameterExtractor.cs`.

Base multipliers start at `1.0` and are then scaled by language cues:
- Stronger terms: multiply by `1.35`.
- Weaker terms: multiply by `0.72`.
- "extreme/max/super/ultra": extra `x1.2`.
- "slight/slightly/a bit/little": extra `x0.92`.

Dynamic channels:
- `size`: bigger/larger/huge/massive/gigantic vs tiny/smaller/small.
- `radius`: wider/broader/expand/spread vs narrow/tight/shrink.
- `duration`: longer/lasting/sustain/extended vs brief/short/quick.
- `speed`: faster/rapid/swift vs slow/slower/calm.
- `count`: more/many/dense/heavy vs fewer/less/sparse.
- `force`: stronger/powerful/violent vs weaker/gentle/soft.
- `intensity`: bright/brighter/intense/glow vs dim/dimmer/subtle.
- Color override: recognized color words (blue/red/green/yellow/orange/purple/white/black variants).

Profile clamp range currently enabled on all three NPCs:
- `DynamicEffectMinMultiplier = 0.6`
- `DynamicEffectMaxMultiplier = 2.0`
- Profile files:
`Assets/Network_Game/Dialogue/Profiles/StormOracleProfile.asset`
`Assets/Network_Game/Dialogue/Profiles/ForgeKeeperProfile.asset`
`Assets/Network_Game/Dialogue/Profiles/ArchivistProfile.asset`

## 3) Effect matrix (non-prefab effects)

| Effect | Trigger field(s) in profile | Base parameters in profile | Runtime apply path | Dynamic controls applied |
|---|---|---|---|---|
| Bored lighting | `m_BoredKeywords` | `m_BoredLightColor`, `m_BoredLightIntensity`, `m_LightTransitionSeconds` | `ApplyBoredLightingClientRpc` -> `ApplyBoredLighting` | color, intensity, duration |
| Prop spawn | `m_PropSpawnKeywords` | primitive, count, scale, spacing, spawn distance, lifetime, color | `SpawnContextPropsClientRpc` -> `SpawnContextProps` | color, count, size(scale), radius(spacing), duration(lifetime) |
| Wall image | `m_WallImageKeywords` | width, height, lifetime, tint | `ApplyWallImageEffectClientRpc` -> `ApplyProceduralWallImage` | color(tint), size(width/height), duration |
| Rain burst | `m_RainBurstKeywords` | rain color, radius, duration, particle count | `ApplyRainBurstEffectClientRpc` -> `ApplyRainBurst` | color, radius, duration, count, speed |
| Shockwave | `m_ShockwaveKeywords` | radius, force, upward force, visual duration, color | `ApplyShockwaveEffectClientRpc` -> `ApplyShockwave` | color, radius, force, duration |
| Shield bubble | `m_ShieldKeywords` | radius, duration, color | `ApplyShieldEffectClientRpc` -> `ApplyShieldBubble` | color, radius, duration |
| Waypoint ping | `m_WaypointPingKeywords` | duration, color | `ApplyWaypointPingEffectClientRpc` -> `ApplyWaypointPing` | color, duration |

## 4) NPC ownership matrix (keywords and base values)

### Storm Oracle (`npc.storm_oracle`)
Profile: `Assets/Network_Game/Dialogue/Profiles/StormOracleProfile.asset`
- Rain keywords: `rain burst`, `make it rain`, `storm rain`
- Rain base: radius `2.8`, duration `3.5`, particles `300`
- Shockwave keywords: `shockwave`, `blast wave`, `push back`
- Shockwave base: radius `4.5`, force `9`, upward `0.45`, visual duration `0.5`
- Shield keywords: `shield up`, `barrier`, `defensive field`
- Shield base: radius `1.5`, duration `4.3`
- Waypoint keywords: `mark waypoint`, `show route`, `mark objective`

### Forge Keeper (`npc.forge_keeper`)
Profile: `Assets/Network_Game/Dialogue/Profiles/ForgeKeeperProfile.asset`
- Rain keywords: `rain burst`, `cool the forge`, `make it rain`
- Rain base: radius `2.3`, duration `2.9`, particles `220`
- Shockwave keywords: `shockwave`, `hammer shock`, `push back`
- Shockwave base: radius `4.0`, force `10`, upward `0.3`, visual duration `0.45`
- Shield keywords: `shield up`, `barrier`, `raise shield`
- Shield base: radius `1.45`, duration `4.0`
- Waypoint keywords: `mark waypoint`, `pin the forge`, `mark objective`

### Archivist (`npc.archivist`)
Profile: `Assets/Network_Game/Dialogue/Profiles/ArchivistProfile.asset`
- Rain keywords: `rain burst`, `memory rain`, `make it rain`
- Rain base: radius `2.4`, duration `3.2`, particles `240`
- Shockwave keywords: `shockwave`, `echo pulse`, `push back`
- Shockwave base: radius `3.8`, force `8.5`, upward `0.35`, visual duration `0.5`
- Shield keywords: `shield up`, `archive barrier`, `defensive field`
- Shield base: radius `1.5`, duration `4.6`
- Waypoint keywords: `mark waypoint`, `pin archive location`, `show route`

## 5) Prefab power dependency matrix

### Currently configured per NPC profile
- Storm Oracle:
  - Lightning Storm -> `LightnigStormCloud.prefab`
  - Ice Lance -> `IceLance.prefab`
- Forge Keeper:
  - Fire Blast -> `FireBall.prefab`
  - Forge Sparks -> `ElectricalSparks.prefab`
- Archivist:
  - Ground Fog -> `GroundFog.prefab`
  - Guide Fireflies -> `FireFlies.prefab`

All six prefabs resolve under:
- `Assets/Network_Game/Dialogue/Resources/DialoguePowers/`

### Scene-level prefab template registry (`DialogueSceneEffects`)
From `Assets/Network_Game/Behavior/Unity Behavior Example/Behavior_Scene.unity:17760`:
- Dialogue powers:
  - `LightnigStormCloud.prefab`
  - `IceLance.prefab`
  - `FireBall.prefab`
  - `ElectricalSparks.prefab`
  - `GroundFog.prefab`
  - `FireFlies.prefab`
- ParticlePack extras available for future profile powers:
  - `BigExplosion.prefab`
  - `WildFire.prefab`
  - `EarthShatter.prefab`
  - `Dissolve.prefab`
  - `SmokeEffect.prefab`
  - `Shower.prefab`
  - `MetalImpacts.prefab`
  - `WoodImpacts.prefab`
  - `RainEffect.prefab`

Runtime resolution fallback in `DialogueSceneEffectsController`:
1. `m_PrefabPowerTemplates` lookup
2. `Resources.Load("DialoguePowers/{prefabName}")`

## 6) Rain-specific scene dependency
`DialogueSceneEffects` currently binds rain template:
- `m_RainEffectTemplate` -> `Assets/Network_Game/ParticlePack/EffectExamples/Water Effects/Prefabs/Shower.prefab`
- Multipliers at scene level are currently neutral:
  - `m_RainSpeedMultiplier = 1`
  - `m_RainEmissionMultiplier = 1`
  - `m_RainSizeMultiplier = 1`

## 6b) Wall poster image source
`DialogueSceneEffectsController` now supports poster texture sourcing from:
- `Assets/Network_Game/Environment/Art/Img_for_Walls`

Behavior:
- If poster textures are available, wall-image power uses those textures.
- If not, it falls back to procedural image generation.
- Targeting supports `at wall <Name>` and `at object <Name>` phrases for chosen wall placement.

## 7) Observability (for demo verification)
Use these logs while testing prompts:
- `NG:Dialogue` for request lifecycle/completion.
- `NG:DialogueFX` for effect match/apply/skip diagnostics.

If a prompt fails to demonstrate a power, check in order:
1. request completed (`NG:Dialogue Completed request`)
2. effect keyword matched (`NG:DialogueFX` apply logs)
3. prefab resolved (`Applying prefab power` includes prefab name)
4. client rpc applied (effect visible on all clients)

## 8) Enhanced parameter pipeline (2026-02-12)

### Scene context injection
`BuildEffectControlGuide()` in `NpcDialogueActor` now injects a `[SceneContext]` block into the LLM prompt containing:
- Ground plane name, dimensions (meters), and position
- A hint like "To cover the entire ground, use radius 25 or scale 5x"
- Up to 12 active scene objects with their sizes and positions

### Explicit numeric extraction
`ParticleParameterExtractor` now parses explicit numeric parameters from LLM text:
- `radius <N>` (0.1–100m) — overrides radius on rain, shockwave, shield
- `scale <N>x` (0.1–50x) — overrides prefab power scale
- `<N> particles/bolts/drops` (1–2000) — overrides particle count
- These take precedence over heuristic multiplier words

### Elemental detection
Extracts element type from narrative text: fire, ice, storm, water, earth, nature, mystic, void.
Stored in `ParticleParameterIntent.DetectedElement` for future elemental fallback matching.

### Emotional intensity modulation
Mood words apply a global multiplier to all parameter channels:
- epic/legendary/colossal/apocalyptic: 1.6x
- chaotic/wild/frenzy: 1.5x
- menacing/threatening: 1.4x
- triumphant/heroic: 1.3x
- sad/tragic: 0.7x
- peaceful/serene: 0.65x

### Raised clamp limits
| Parameter | Old max | New max |
|---|---|---|
| Prefab power scale | 5x | 50x |
| Rain radius | 10m | 100m |
| Rain particle count | 1400 | 2000 |
| Shockwave radius | 14m | 100m |
| Shield radius | 8m | 50m |
| Dynamic multiplier clamp | 4x | 10x |
| Profile `DynamicEffectMaxMultiplier` slider | 4 | 10 |

### Emission scaling for large prefabs
`ApplyPrefabPower` now scales emission rate by `sqrt(scale)` when scale > 1.5x, preventing sparse particle density at large scales.

### PrefabPowerEntry semantic fields
New optional fields on `PrefabPowerEntry`:
- `Element` — element tag for catalog matching
- `VisualDescription` — injected into LLM prompt as effect description
- `CreativeTriggers` — alternative narrative invocation phrases

## 9) Immediate customization levers to tune first
- Per profile (`NpcDialogueProfile` assets):
  - keyword sets (trigger reliability)
  - base magnitude (`radius`, `duration`, `count`, `force`)
  - dynamic clamp range (`min/max multiplier`)
- Scene-level (`DialogueSceneEffects` in `Behavior_Scene`):
  - rain template + rain speed/emission/size multipliers
  - prefab template registry completeness

## 10) Step 2 command normalization and targeting directives
Implemented keywords now support standardized prompts across all NPCs:
- `demonstrate rain`
- `demonstrate shockwave`
- `demonstrate shield`
- `demonstrate waypoint`
- `spawn at player`
- `spawn at object <ObjectName>`

Prefab power examples:
- Storm Oracle: `demonstrate lightning`, `lightning at player`, `lightning at object <ObjectName>`
- Forge Keeper: `demonstrate fire`, `fire at player`, `fire at object <ObjectName>`
- Archivist: `demonstrate fog`, `fog at player`, `fog at object <ObjectName>`

Explicit runtime controls supported by parser:
- Duration seconds: e.g. `for 8 seconds`
- Intensity percent/multiplier: e.g. `intensity 150%`, `power 1.5x`

## 11) Particle playground label inventory (Behavior_Scene)
Source: live hierarchy under `UI/Labels` (43 label objects), each with `ProximityActivate` + collider.

### Explosions
- `TinyExplosionLabel`
- `SmallExplosionLabel`
- `DustExplosionLabel`
- `BigExplosionLabel`
- `EnergyExplosionLabel`
- `Legacy Plasma Explosion`

### Fire / combustion
- `TinyFlamesLabel`
- `MediumFlamesLabel`
- `LargeFlamesLabel`
- `WildFireLabel`
- `FireBallLabel`
- `FlameThrowrLabel` (scene typo; likely FlameThrower)
- `FlameSteamLabel` (scene typo; likely FlameStream)

### Magic
- `MagicIceLanceLabel`
- `MagicEarthShatterLabel`

### Smoke / gas / steam
- `SteamLabel`
- `RisingSteamLabel`
- `PressureisedSteamLabel` (scene typo; likely PressurisedSteam)
- `PoisonGasLabel`
- `GroundFogLabel`
- `DustStormLabel`
- `BlackSmokeLabel`
- `RocketTrailLabel`

### Water
- `WaterLeakLabel`
- `BigSplashLabel`
- `ShowerLabel`
- `Legacy Rain`
- `Legacy Waterfall`

### Impacts / weapons
- `MuzzleFlashLabel`
- `GoopSprayLabel`
- `GoopStreamLabel`
- `WoodImpactLabel`
- `StoneImpactLabel`
- `SandImpactLabel`
- `FleshImpactLabel`
- `MetalImpactLabel`

### Misc
- `SparksLabel`
- `HeatDistortionLabel`
- `FireFliesLabel`
- `CandlesLabel`
- `DissolveLabel`
- `TeleportLabel`
- `Legacy Thunder Cloud`

## 12) NPC-trigger coverage against playground
Current NPC effect path is split in two:
- Procedural/utility effects via direct methods (rain/shockwave/shield/waypoint/props/wall image/lighting).
- Prefab powers via profile keyword mapping + `ApplyPrefabPower`.

### Prefab templates currently registered on `DialogueSceneEffects`
From `Behavior_Scene` serialized `m_PrefabPowerTemplates`:
- `LightnigStormCloud`
- `IceLance`
- `FireBall`
- `ElectricalSparks`
- `GroundFog`
- `FireFlies`
- `BigExplosion`
- `WildFire`
- `EarthShatter`
- `Dissolve`
- `SmokeEffect`
- `Shower`
- `MetalImpacts`
- `WoodImpacts`
- `RainEffect`
- `RocketTrail`

### Prefab powers currently enabled in NPC profiles
- Storm Oracle: `LightnigStormCloud`, `IceLance`
- Forge Keeper: `FireBall`, `ElectricalSparks`
- Archivist: `GroundFog`, `FireFlies`

Implication:
- The playground contains many labeled effects, but only six are currently reachable through profile keyword triggers without extra profile entries.
- Ten additional prefabs are already template-registered and can be enabled quickly by adding `PrefabPowerEntry` rows to profiles.

## 13) Gameplay extensions requested (damage + homing missile)
Requested examples:
- "Particles can harm the player"
- "Missile launch follows the player target"

Current status:
- No damage/health gameplay pipeline is present in `Assets/Network_Game` (no `TakeDamage`/`Health` handlers found).
- `ApplyPrefabPower` currently spawns visual-only one-shot instances, with no collision/damage/homing behavior.

Recommended implementation contract:
1. Add a network-safe damage receiver on actors
- New component: `CombatHealth` (or `DamageReceiver`) with server-authoritative HP.
- API: `ApplyDamage(float amount, ulong sourceNetworkId, string damageType)`.

2. Add an optional effect gameplay profile on prefab powers
- Extend `PrefabPowerEntry` with:
  - `bool IsProjectile`
  - `bool EnableHoming`
  - `float HomingTurnRate`
  - `float ProjectileSpeed`
  - `float LifetimeSeconds`
  - `float DamageAmount`
  - `float DamageRadius`
  - `string DamageType`
  - `bool AffectPlayer`

3. Add runtime projectile handler
- New script for spawned powers (e.g. `DialogueEffectProjectile`):
  - server-owned movement
  - optional homing toward listener/local player anchor
  - overlap/trigger on hit
  - server-side damage application
  - optional impact VFX spawn

4. Keep visual and gameplay paths separable
- Non-projectile powers remain visual-only (current behavior).
- Projectile powers route through new projectile path.

5. Preserve dialogue controls
- Reuse existing parser for `duration/scale/radius/count/intensity`.
- Add optional parser terms for gameplay:
  - `damage 25`
  - `homing on/off`
  - `speed 12`
  - `blast radius 3`

## 14) Low-risk activation roadmap (recommended order)
1. Expand profile coverage to include the remaining template-registered prefabs first (no gameplay changes).
2. Add `CombatHealth` and damage events (authoritative server path).
3. Add `DialogueEffectProjectile` for `RocketTrail` first as the homing reference implementation.
4. Add profile-level safety clamps for damage/speed/radius to prevent unstable LLM outputs.
