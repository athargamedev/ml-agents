# Behavior Scene Memory

## Scope
Memory for `Behavior_Scene.unity` runtime wiring and NPC behavior graph integration.

## Scene integration map
- Scene bootstrap: `BehaviorSceneBootstrap`.
- Dialogue server object: `NetworkDialogueService` with persona routing enabled.
- Dialogue UI object: `Canvas_Dialogue` + `DialogueClientUI`.
- NPC prefab source: `Assets/StarterAssets/ThirdPersonController/Prefabs/NPCArmature.prefab`.
- NPC graph runtime: `NPC_Behavior_Graph.asset` on `BehaviorGraphAgent`.
- Waypoint root object: `Waypoints`.
- Spawn anchor object: `SpawnPoint`.

## Current bootstrap behavior
- Starts host if needed.
- Resolves or spawns local player.
- Runs a post-spawn rebind loop for player-dependent references (camera follow, dialogue participants, auth attach, blackboard player) to handle delayed Netcode player spawn.
- Runs an optional continuous camera rebind monitor after initialization so Cinemachine follow/look-at stays bound even if local player object changes later.
- Ensures local auth service login (`name_id`) and attaches auth service to the spawned local player.
- Ensures a dedicated local auth login UI exists on `Canvas_Dialogue`.
- Collects NPC agents tagged `NPC`.
- Wires blackboard values per NPC.
- Sets first sorted NPC as primary scene NPC reference.
- Enforces one active `LLMAgent` runtime owner on `NetworkDialogueService`: disables any per-NPC `LLMAgent` components and disables player `LLMAgent`.
- Rebinds `DialogueClientUI` participants to local player and primary NPC.

## Graph variable contract
- `Self` (`GameObject`)
- `Player` (`GameObject`)
- `Waypoints` (`GameObject List`)
- `Speed` (`float`)
- `DistanceThreshold` (`float`)
- `WaitSeconds` (`float`)
- Dialogue variables where used:
`Prompt`, `ConversationKey`, `OutputText`, `LastBlockReason`

## Dialogue usage
- Dialogue is initiated via `DialogueClientUI` and routed through `NetworkDialogueService` (server-authoritative).
- Unity Behavior graph dialogue actions are optional and are disabled by default (`NetworkDialogueService.EnableBehaviorGraphDialogueActions == false`).

## Fast validation
1. Enter Play Mode.
2. Confirm player movement and camera bind.
3. Approach primary NPC and confirm single auto greeting.
4. Send a player message in UI and confirm NPC response on canvas.
5. Run `PersonaDialogueSanityRunner`.

## Script execution order contract
- `DialogueLoraBootstrap`: `-500`
- `NetworkDialogueService`: `-450`
- `LocalPlayerAuthService`: `-220`
- `BehaviorSceneBootstrap`: `-100`
- `LocalPlayerAuthUI`: `-80`
- `DialogueClientUI`: `-60`

## Local auth storage mode
- `LocalPlayerAuthService` uses provider-based local auth storage with a JSON provider (`StorageBackend.JsonFile`) by default.
- JSON store path: `Application.persistentDataPath/network_game_local_auth.json`.
- Native SQLite code path was removed from gameplay runtime to avoid editor native crashes.
- `StorageBackend.BackendApi` is reserved for a future service-backed persistence provider and currently falls back to JSON.

## Related memory
- `Assets/Network_Game/Dialogue/MEMORY.md`
- Local auth JSON store file: `Application.persistentDataPath/network_game_local_auth.json`
