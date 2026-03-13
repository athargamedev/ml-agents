# Dialogue Memory

## Scope
Canonical memory for the multiplayer LLM dialogue stack in this project.

## Runtime architecture
- `NetworkDialogueService` is the single server-authoritative request router.
- `NetworkDialogueService` now force-enables its resolved `LLMAgent` before warmup/chat, so disabled prefab defaults do not stall request processing.
- `LocalPlayerAuthService` provides local `name_id` login and per-player customization data.
- Default auth storage mode is JSON file (`Application.persistentDataPath/network_game_local_auth.json`) for crash-safe editor runtime.
- Native SQLite runtime path was removed; auth now uses provider abstraction with JSON provider active.
- Auth integration nodes are available:
`Auth Login`, `Auth Set Customization`, `Auth Get Customization`, `Auth Set Mirror LoRA`.
- `DialogueClientUI` sends player prompts with `IsUserInitiated = true` and `m_RequestNpcResponder = true`.
- `DialogueClientUI` now renders a transcript-style chat panel (player lines + NPC lines + pending status) with sentence wrapping, markdown cleanup, and optional timestamp/rich-label formatting.
- `DialogueClientUI` now auto-selects listener NPC per send (aimed NPC first, nearest NPC fallback) when `m_AutoSelectListener` is enabled.
- `DialogueClientUI` now auto-builds a readable `DialoguePanel_Auto` layout (header + scroll viewport + docked input row) and auto-scrolls to latest message.
- `DialogueClientUI` supports bubble-style message cards with per-speaker color tint and pending-state rendering.
- `DialogueClientUI` tracks recent NPC speakers and provides a header target-cycle button to switch between `Auto` listener selection and manual target lock.
- `DialogueClientUI` disables legacy root `VerticalLayoutGroup`/`ContentSizeFitter` at runtime and anchors the generated chat panel to the canvas root to prevent stretched or misplaced input controls.
- `DialogueClientUI` now reuses bubble row UI elements and performs deferred end-of-frame auto-scroll, preventing transcript drift and making new lines consistently follow to the bottom.
- Dialogue is initiated via `DialogueClientUI` (and/or direct code calls to `NetworkDialogueService`).
- `NpcDialogueActor` + `NpcDialogueProfile` drive per-NPC personality prompt + LoRA routing (legacy `NpcDialoguePersona` is supported as fallback).
- `DialogueLoraBootstrap` preloads LoRAs and logs missing files safely.
- `DialogueSceneEffectsController` applies context effects (for example bored -> blue lights).
- Context effects now support multiple channels from dialogue intent: lighting mood shifts, prop spawning, procedural wall-image placement, rain bursts, shockwaves, shield bubbles, and waypoint pings.
- `ParticleParameterExtractor` now extracts elemental type, emotional intensity, and explicit numeric parameters (radius, scale, count) from LLM text.
- `BuildEffectControlGuide` injects scene spatial context (ground plane size, object positions) into LLM prompts so effects can be scaled to match the environment.
- Effect clamp limits raised significantly (prefab scale up to 50x, rain/shockwave radius up to 100m) to support arena-sized effects.
- `ApplyPrefabPower` now auto-scales emission rate at large scales to maintain particle density.
- `PrefabPowerEntry` now supports `Element`, `VisualDescription`, and `CreativeTriggers` fields for richer LLM prompt context.
- Rain burst now prefers a preset particle template (`Particle_Effects/Shower` auto-bind) with runtime speed/emission/size scaling; procedural rain remains fallback only.
- `PersonaDialogueSanityRunner` performs automated per-persona runtime checks.

## Critical contracts
- Always resolve keys through `NetworkDialogueService.ResolveConversationKey(...)`.
- Keep server-authoritative request flow:
`RequestDialogue` -> server enqueue -> queue processing -> response RPC.
- Keep local auth attached to the spawned local player so mirror LoRA routing can map `name_id` profile data to `playerNetworkId`.
- Auto-trigger loop guard contract for NPC greeting:
`RunOncePerPrompt = true`, `RequireUserReply = true`, `MinRepeatDelaySeconds >= 1`.
- For auto-trigger rejections, treat these as non-fatal skip states:
`conversation_in_flight`, `awaiting_user_message`, `duplicate_prompt`, `repeat_delay`.
- Keep `DialogueClientUI.m_RequestNpcResponder = true` so player requests produce NPC responses.

## Multi-NPC persona model
- Scene supports 3 persona NPCs:
`NPC_StormOracle`, `NPC_ForgeKeeper`, `NPC_Archivist`.
- Keep one active `LLMAgent` service owner on `NetworkDialogueService` for stability; bootstrap disables any per-NPC/player `LLMAgent` components. Persona style changes are done by profile prompt and LoRA weights, not separate model services per NPC.
- `Setup Dialogue Scene Personas (3 NPCs)` tool handles scene wiring and component assignment.

## Diagnostics memory
- Core log categories:
`NG:Dialogue`, `NG:LLMChat`, `NG:DialogueUI`, `NG:DialogueLoRA`, `NG:DialogueSanity`, `NG:Bootstrap`.
- Scene-effect diagnostics now log through `NG:DialogueFX` for match/skip/apply visibility.
- Dialogue UI now blocks send while a pending placeholder exists when `m_DisableSendWhilePending` is enabled.
- `NetworkDialogueService` broadcast now uses short preview text via `NpcDialogueActor` (fallback: legacy `TalkNetworkSync`) instead of full LLM paragraphs (improves in-world legibility).
- Common rejection meanings:
`awaiting_user_message` means loop guard working.
`conversation_in_flight` means previous request still active.
`duplicate_prompt` and `repeat_delay` mean anti-loop throttling working.

## Recent runtime findings (2026-02-09)
- Dialogue completed at least once (`Completed request | status=Completed`), then a later request timed out (`Request timed out | id=6`).
- Scene-text broadcast can be skipped if the speaker NPC is missing `NpcDialogueActor` (or legacy `TalkNetworkSync`).
- LoRA preload reported missing files:
`Assets/LLMUnity/Loras/storm_oracle.gguf`
`Assets/LLMUnity/Loras/forge_keeper.gguf`
`Assets/LLMUnity/Loras/archivist.gguf`
- Context lighting effect path was hardened to evaluate both prompt + response text and emit explicit `NG:DialogueFX` logs.

## Persona sanity acceptance criteria
- Baseline suite runs fixed per-persona cases (Storm Oracle, Forge Keeper, Archivist), each with short prompts and style keyword constraints.
- Each case must return `Completed` status before timeout, with non-empty response text.
- Wrong-speaker routing fails the case when response request speaker does not match the persona NPC requested by the test.
- Timeout or empty-response conditions are explicit failure reasons and must be zero across all cases for a pass.
- Machine-readable report output (`.json` + `.txt`) is generated per run and should be attached to triage/review when failures occur.
- Suite pass bar: `failedCases == 0` and every case records latency (`LatencySeconds`) for regression tracking.

## Verification flow
1. Run menu `Network Game/Dialogue/Setup Dialogue Scene Personas (3 NPCs)`.
2. Enter Play Mode in `Behavior_Scene`.
3. Use `PersonaDialogueSanityRunner` context menu `Run Persona Sanity Check`.
4. Inspect `Last Report` and unified logs for pass/fail lines.

## Related memories
- `Assets/Network_Game/Behavior/Unity Behavior Example/MEMORY.md`
- `Tools/lora/MEMORY.md`

## Effect instantiation updates (2026-02-17/18)
- `[EFFECT:]` tags are parsed even if `EffectCatalog` is unavailable, so profile fallback still works.
- Unknown effect tags now map more reliably to profile powers using fuzzy matching:
`PowerName`, prefab name, compact token matching, keywords, and creative triggers.
- `Target: Player` resolves through connected player lookup first (`ResolvePlayerNetworkIdForRequest`) before listener fallback.
- Added `NG:DialogueFX` trace `Effect target resolution` to log requested vs resolved target.

### Invisibility/restore behavior
- Special player effects now run as explicit modes:
`Dissolve` and `Respawn`.
- Dissolve/respawn are dispatched server -> all clients through dedicated ClientRpc methods.
- Dissolve has priority and suppresses regular prefab/catalog effects for invisible requests.
- Dissolve on mesh is animated:
fade-out -> hold -> fade-in, with tunables in `DialogueSceneEffectsController`:
`m_DissolveFadeOutSeconds`, `m_DissolveFadeInSeconds`, `m_DissolveCurve`.

### Gameplay damage behavior
- Projectile impact damage remains server-authoritative via `DialogueEffectProjectile`.
- Added particle-hit damage component:
`DialogueParticleCollisionDamage`.
- Particle damage supports:
`OnParticleCollision` hits, per-target cooldown, and proximity fallback sweep for sparse collision callbacks.
- `DialogueSceneEffectsController.ApplyPrefabPower` auto-configures particle damage for gameplay-enabled effects.
- Tunables:
`m_EnableParticleCollisionDamage`,
`m_ParticleCollisionDamageScale`,
`m_ParticleCollisionHitCooldownSeconds`.

### Runtime prerequisites
- Player actor must include `CombatHealth` (bootstrap ensures this in `Behavior_Scene`).
- Damage application remains server-authoritative for projectile and particle paths.

### Verification logs
- `EffectParser dispatch`
- `Effect target resolution`
- `Configured particle collision damage`
- `Particle collision damage`
- `Combat Damage applied`
