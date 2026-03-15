# Network Game AGENTS

## Scope

Applies to `DevProject/Assets/Network_Game/` and its gameplay, auth, UI, dialogue, bootstrap, controller, and editor tooling.

## System Map

- Auth and identity: `Auth/LocalPlayerAuthService.cs`, `UI/Login/PlayerLoginController.cs`
- Spawn and transport: `Behavior/Unity Behavior Example/NetworkBootstrap.cs`, `Behavior/Unity Behavior Example/PlayerBootstrap.cs`, `Core/WebGLTransportAdapter.cs`
- Player control: `ThirdPersonController/Scripts/ThirdPersonController.cs`, `ThirdPersonController/Scripts/FlyModeController.cs`
- Dialogue runtime: `Dialogue/NetworkDialogueService.cs`, `UI/Dialogue/ModernDialogueController.cs`, `Dialogue/OpenAIChatClient.cs`
- Performance: `Scripts/PerformanceCullingSetup.cs`, `Editor/WebGLOptimizationTool.cs`
- Editor diagnostics/tools: `Editor/CustomTools/*`, `Editor/CustomTools/Services/*`

## Use These Skills First

- `unity-project-self-diagnostics` — first responder for holistic DevProject triage
- `unity-auth-identity-guard` — login, identity, prompt-context, auth-gated dialogue
- `unity-player-spawn-authority` — connection approval, spawn, local-player resolution, ownership
- `unity-character-control-netcode` — owner-only input, camera binding, movement, fly mode
- `unity-dialogue-runtime-triage` — LLM queue, routing, NPC targeting, response path
- `unity-dialogue-persona-lora` — profile assets, persona tuning, powers, mirror-LoRA state
- `unity-network-performance-audit` — throughput, latency, WebGL, culling, broadcast safety

## Shared Contracts

1. `NetworkBootstrap` owns transport mode and connection approval.
2. `PlayerBootstrap` owns local-player discovery/readiness and host fallback spawn.
3. `LocalPlayerAuthService` owns login state and prompt-context initialization.
4. `ThirdPersonController` remains owner-authoritative for runtime control.
5. `NetworkDialogueService` remains the only supported server-authoritative dialogue router.
6. Editor custom tools are the preferred fast path for dialogue diagnostics and profile automation.

## Preferred Tooling

- Register project custom tools first: `Network Game/MCP/Admin/Register All Custom Tools`
- Use `ng_pipeline_status` and `ng_get_full_diagnostics` before manual dialogue deep-dives
- Use Unity console/log categories: `NG:Auth`, `NG:NetworkBootstrap`, `NG:PlayerBootstrap`, `NG:Dialogue`, `NG:DialogueUI`, `NG:DialogueFX`

## Do Not Do

- Do not bypass auth, spawn, or dialogue routers with temporary side paths unless the task is explicitly a test harness.
- Do not fix owner/authority bugs by enabling systems on every client.
- Do not trade correctness for temporary convenience in multiplayer flow.
