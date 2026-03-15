---
name: unity-project-self-diagnostics
description: Run first-pass diagnostics for DevProject's Unity multiplayer stack. Use when the user wants a holistic health check, root-cause triage, or a safe starting point across authentication, spawning, player control, dialogue, and network performance.
---

# Unity Project Self Diagnostics

## Overview

Use this skill as the default first responder for `DevProject/Assets/Network_Game/`.
It decides which subsystem specialist to pull in after establishing runtime health.

## Read First

- `DevProject/Assets/Network_Game/AGENTS.md`
- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/AGENTS.md` when spawn/bootstrap flow matters
- `DevProject/Assets/Network_Game/Dialogue/AGENTS.md` when dialogue or effects are involved

## Primary Workflow

1. Confirm the active target is `DevProject`, not package samples or trainer code.
2. Check the Unity editor/runtime state before changing anything:
   - editor ready/compiling
   - Play Mode vs Edit Mode
   - current scene
   - recent console errors
3. Prefer project-native MCP tools before manual spelunking:
   - `Network Game/MCP/Admin/Register All Custom Tools`
   - `ng_pipeline_status`
   - `ng_get_full_diagnostics`
4. Classify the failure surface:
   - login, identity, prompt context -> use `$unity-auth-identity-guard`
   - connection approval, spawn, local-player resolution, ownership -> use `$unity-player-spawn-authority`
   - input, camera, movement, fly mode, owner-only control -> use `$unity-character-control-netcode`
   - LLM readiness, queue, NPC targeting, response lifecycle -> use `$unity-dialogue-runtime-triage`
   - persona/profile/LoRA/effect authoring -> use `$unity-dialogue-persona-lora`
   - throughput, rate limits, transport, broadcast size, culling, WebGL perf -> use `$unity-network-performance-audit`

## Project Contracts

- `NetworkBootstrap` owns transport startup and connection approval.
- `PlayerBootstrap` owns local-player discovery, host fallback spawn, and initial ownership/runtime wiring.
- `LocalPlayerAuthService` owns login state and prompt-context initialization.
- `ThirdPersonController` is NGO owner-authoritative and must only accept local input for the owner.
- `NetworkDialogueService` is the only supported server-authoritative dialogue router.
- Dialogue scene effects stay server -> `ClientRpc`; do not bypass that path.

## Do Not Do

- Do not weaken auth checks just to get dialogue requests through.
- Do not bypass `NetworkBootstrap`/`PlayerBootstrap` with ad hoc spawn code.
- Do not enable player control on non-owner instances.
- Do not call the LLM backend directly from random gameplay scripts when the service already routes requests.
