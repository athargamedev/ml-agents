# Dialogue System AGENTS

## Scope

`Assets/Network_Game/Dialogue/` — LLM dialogue pipeline, NPC personas, scene effects, parameter extraction.

## Read first

- `Assets/Network_Game/AGENTS.md`
- `Assets/Network_Game/Dialogue/MEMORY.md`
- `Assets/Network_Game/Behavior/Unity Behavior Example/MEMORY.md` (when scene wiring is involved)

## Use these skills first

- `unity-dialogue-runtime-triage` — diagnose live failures
- `unity-dialogue-persona-lora` — persona/LoRA/effect changes
- `unity-network-performance-audit` — throughput, latency, transport, broadcast tuning
- `unity-auth-identity-guard` — auth-gated dialogue and missing identity snapshots
- `unity-player-spawn-authority` — local-player and participant resolution failures

## Key files

| File | Role | Lines |
|------|------|-------|
| `NetworkDialogueService.cs` | Server request router, queue, lifecycle | ~5200 |
| `DialogueSceneEffectsController.cs` | Effect dispatch + rendering (8 types) | ~900 |
| `ParticleParameterExtractor.cs` | Language-to-parameter heuristic mapping | ~574 |
| `NpcDialogueProfile.cs` | Per-NPC ScriptableObject (keywords, params, powers) | ~200 |
| `NpcDialogueActor.cs` | Networked speech bubble + persona binding | ~150 |
| `NpcDialoguePersona.cs` | Legacy persona fallback (system prompt builder) | ~180 |
| `DialogueClientUI.cs` | Player chat panel (transcript, bubbles, targeting) | ~800 |
| `DialogueLoraBootstrap.cs` | LoRA preload and weight init | ~100 |
| `PersonaDialogueSanityRunner.cs` | Automated per-persona runtime checks | ~200 |
| `OpenAIChatClient.cs` | Remote OpenAI-compatible inference path | ~206 |

## NPC powers: effect pipeline

```
LLM response text
  -> ApplyContextEffects()
    -> ParticleParameterExtractor.ExtractIntent(text) -> ParticleParameterIntent
    -> Match keywords from NpcDialogueProfile against text
    -> Dispatch matched effect via ClientRpc to all clients
```

### 8 effect types

| Effect | Key parameters | RPC method |
|--------|---------------|------------|
| Bored lighting | color, intensity, transition duration | `ApplyBoredLightingEffectClientRpc` |
| Prop spawn | type, count, scale, spacing, lifetime, color | `SpawnContextPropsClientRpc` |
| Wall image | width, height, lifetime, tint | `ApplyProceduralWallImageClientRpc` |
| Rain burst | radius, duration, count, speed, emission, size | `ApplyRainBurstEffectClientRpc` |
| Shockwave | force, radius, upward force, visual duration, color | `ApplyShockwaveEffectClientRpc` |
| Shield bubble | mesh radius, shader props, duration | `ApplyShieldBubbleEffectClientRpc` |
| Waypoint ping | position, color, duration | `ApplyWaypointPingEffectClientRpc` |
| Prefab power | scale, emission scaling, lifetime, color | `ApplyPrefabPowerEffectClientRpc` |

### Parameter extraction contract

`ParticleParameterExtractor` produces a `ParticleParameterIntent` struct with:
- **Multipliers** (1.0 baseline): intensity, duration, radius, size, speed, count, force
- **Explicit numerics**: `ExplicitDurationSeconds`, `ExplicitRadius`, `ExplicitScale`, `ExplicitCount`
- **Emotional multiplier**: epic=1.6x, legendary=1.5x, chaotic=1.45x, peaceful=0.65x
- **Element detection**: fire, ice, storm, water, earth, nature, mystic, void
- **Color override**: blue, red, green, yellow, orange, purple, white, black

Profile clamps apply via `DynamicEffectMinMultiplier` (default 0.6) and `DynamicEffectMaxMultiplier` (default 2.0).

### Prefab power assets

Primary (Addressables): `Assets/Network_Game/Dialogue/Addressables/DialoguePowers/`
Fallback (Resources): `Assets/Network_Game/Dialogue/Resources/DialoguePowers/`

```
DialoguePowers/
├── LightnigStormCloud.prefab   (Storm Oracle — storm)
├── IceLance.prefab             (Storm Oracle — ice)
├── FireBall.prefab             (Forge Keeper — fire)
├── ElectricalSparks.prefab     (Forge Keeper — storm)
├── GroundFog.prefab            (Archivist — nature)
├── FireFlies.prefab            (Archivist — nature)
├── BigExplosion.prefab, EarthShatter.prefab, EnergyExplosion.prefab
├── PlasmaExplosionEffect.prefab, WildFire.prefab
```

### Addressables loading

- `EffectCatalog.LoadAsync()` — preferred async path, falls back to `Resources.Load`
- `DialogueSceneEffectsController.TryLoadFromAddressables()` — sync Addressable load with handle caching
- `OnDestroy()` releases all cached handles
- Assets must be marked Addressable in Unity Editor for Addressables path to work

## Invariants to preserve

- Keep canonical key routing through `NetworkDialogueService.ResolveConversationKey(...)`.
- Keep server-authoritative generation path (`RequestDialogue` -> enqueue -> process -> response RPC).
- Keep loop guard contract for auto NPC greeting:
  - `RunOncePerPrompt = true`, `RequireUserReply = true`
  - Non-fatal rejections treated as `Success` for trigger actions.
- Keep `DialogueClientUI.m_RequestNpcResponder = true` for player->NPC flow.
- Keep one active `LLMAgent` on `NetworkDialogueService`; disable per-NPC agents.
- Effect dispatch always server -> all clients via ClientRpc.
- Preserve `UnifiedLog` categories: `NG:Dialogue`, `NG:LLMChat`, `NG:DialogueUI`, `NG:DialogueLoRA`, `NG:DialogueSanity`, `NG:DialogueFX`.

## Preferred workflow

1. Reproduce issue in `Behavior_Scene.unity`.
2. Capture exact logs and classify failure type.
3. Patch smallest reliable fix.
4. Validate no regression in request lifecycle.
5. Run persona sanity: `PersonaDialogueSanityRunner` context menu -> `Run Persona Sanity Check`.

## Scene and tooling references

- Scene setup: menu `Network Game/Dialogue/Setup Dialogue Scene Personas (3 NPCs)`.
- Profile generation: menu `Network Game/Dialogue/Create 3 NPC Profiles`.
- Sanity runner: `Assets/Network_Game/Dialogue/PersonaDialogueSanityRunner.cs`.
- LoRA scripts: `Tools/lora/scripts/*.ps1`.
- Profile assets: `Assets/Network_Game/Dialogue/Profiles/*.asset`.

## Do not do

- Do not bypass `NetworkDialogueService` with direct LLM calls from random MonoBehaviours.
- Do not hardcode non-canonical conversation keys in actions.
- Do not disable guards to hide loops; fix root conditions/state.
- Do not keep enabled per-NPC `LLMAgent` components in scene.
- Do not add new effect types without a corresponding ClientRpc and profile keyword entry.
- Do not extract parameters without respecting profile clamp bounds.
