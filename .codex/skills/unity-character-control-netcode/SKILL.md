---
name: unity-character-control-netcode
description: Diagnose and optimize DevProject's owner-authoritative player movement, camera binding, fly mode, and input suppression. Use when characters cannot move, remote players steal control, camera follow breaks, or dialogue/UI interferes with local control.
---

# Unity Character Control Netcode

## Focus Area

- `DevProject/Assets/Network_Game/ThirdPersonController/Scripts/ThirdPersonController.cs`
- `DevProject/Assets/Network_Game/ThirdPersonController/Scripts/FlyModeController.cs`
- `DevProject/Assets/Network_Game/ThirdPersonController/InputSystem/StarterAssetsInputs.cs`
- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/SceneCameraManager.cs`
- `DevProject/Assets/Network_Game/UI/Dialogue/ModernDialogueController.cs`

## Workflow

1. Confirm the player instance is spawned and owned locally before inspecting movement.
2. Validate `OnNetworkSpawn()`, `OnGainedOwnership()`, and `OnLostOwnership()` owner-state transitions.
3. Validate owner-only enablement for:
   - `PlayerInput`
   - `StarterAssetsInputs`
   - `ThirdPersonController`
   - `FlyModeController`
4. Validate camera target assignment and follow camera selection after ownership changes.
5. Validate dialogue/login UI cursor suppression only affects the local owner.

## Invariants

- Preserve `ThirdPersonController.ApplyOwnershipRuntimeState(...)` as the owner gate for runtime control.
- Preserve fly-mode sync through network variables and thresholded visual updates instead of high-frequency RPC spam.
- Keep root motion disabled when `NetworkAnimator` is owner authoritative.
- Keep non-owner instances visually updated but input-disabled.
- When UI typing is active, suppress local look/input intentionally; do not treat that as a network bug.

## Useful Checks

- Start at `ThirdPersonController.OnNetworkSpawn()` and confirm `ApplyOwnershipRuntimeState(IsOwner)` executes.
- If only fly mode looks wrong over the network, inspect the thresholded sync around `TrySyncFlyVisualState(...)`.
- If movement works but camera is wrong, inspect `SceneCameraManager` before touching controller motion.

## Do Not Do

- Do not enable owner input for all spawned players.
- Do not add extra RPCs for per-frame movement that NGO already synchronizes.
- Do not patch around control bugs by forcing camera state globally.
