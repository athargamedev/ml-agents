---
name: unity-player-spawn-authority
description: Diagnose and fix DevProject's connection approval, player spawning, local-player resolution, and ownership handoff. Use when players fail to spawn, spawn at the wrong place, lose ownership, cannot be resolved locally, or host/client authority diverges.
---

# Unity Player Spawn Authority

## Focus Area

- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/NetworkBootstrap.cs`
- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/PlayerBootstrap.cs`
- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/NetworkBootstrapEvents.cs`
- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/SceneCameraManager.cs`
- `DevProject/Assets/Network_Game/Core/WebGLTransportAdapter.cs`

## Workflow

1. Confirm `NetworkManager` exists and `ConnectionApproval` is enabled.
2. Validate spawn-point resolution and approved spawn transform.
3. Validate host/client mode selection and transport configuration.
4. Validate local-player resolution order:
   - `NetworkManager.LocalClient.PlayerObject`
   - tagged owned player
   - single tagged fallback
5. Validate host fallback spawn only runs on host/server paths.
6. Validate ownership and local-ready events before touching controller code.

## Invariants

- Keep spawn approval in `NetworkBootstrap.OnConnectionApproval(...)`.
- Keep host fallback spawn in `PlayerBootstrap.SpawnFallbackPlayer()` as a host-only safety net.
- Keep `PlayerBootstrap.ConfigureLocalPlayerNetworking(...)` responsible for owner-side `NetworkTransform` and `NetworkAnimator` tuning.
- Keep WebGL/browser paths on WebSockets and native/editor paths on the default transport unless there is a confirmed transport bug.
- Prefer fixing local-player discovery over adding duplicate spawn paths.

## Useful Checks

- Inspect `NG:NetworkBootstrap` and `NG:PlayerBootstrap` logs first.
- Confirm `PublishLocalPlayerReady(...)` only happens after spawn, alignment, networking, and local input enablement.
- If the player exists but the camera/UI bind to the wrong object, inspect `SceneCameraManager` and the owner-client filtering path.

## Do Not Do

- Do not spawn extra player prefabs on clients to hide host approval bugs.
- Do not assign ownership to whichever tagged player is easiest to find.
- Do not change transport defaults without checking WebGL and dedicated-server behavior.
