---
name: unity-network-performance-audit
description: Audit and optimize DevProject's multiplayer and dialogue runtime performance. Use when the project needs better request throughput, lower dialogue latency, safer RPC volume, cleaner transport selection, or improved rendering/network behavior in WebGL and multiplayer sessions.
---

# Unity Network Performance Audit

## Focus Area

- `DevProject/Assets/Network_Game/Dialogue/NetworkDialogueService.cs`
- `DevProject/Assets/Network_Game/Core/WebGLTransportAdapter.cs`
- `DevProject/Assets/Network_Game/Scripts/PerformanceCullingSetup.cs`
- `DevProject/Assets/Network_Game/Editor/WebGLOptimizationTool.cs`
- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/NetworkBootstrap.cs`

## Workflow

1. Measure before changing:
   - queue depth
   - success/timeout rates
   - request rejection mix
   - warmup degradation
2. Audit dialogue throughput controls:
   - `m_MaxPendingRequests`
   - `m_MaxConcurrentRequests`
   - `m_MaxRequestsPerClient`
   - `m_MinSecondsBetweenRequests`
   - `m_RequestTimeoutSeconds`
   - `m_MaxRetries`
   - `m_BroadcastMaxCharacters`
3. Audit transport fit:
   - WebGL/browser -> WebSockets
   - native/editor -> default transport unless proven otherwise
4. Audit render-side cost that affects perceived multiplayer performance:
   - layer culling distances
   - expensive WebGL render settings
   - unnecessary broadcast text/effect spam

## Invariants

- Prefer bounded concurrency and short broadcasts over higher request fan-out.
- Keep dialogue request validation intact while tuning throughput.
- Keep perf fixes measurable; avoid speculative rewrites of stable netcode paths.
- Treat queue wait and model execution separately; backend latency and Unity RPC latency are not the same problem.

## Useful Checks

- `ng_pipeline_status` and `ng_get_full_diagnostics` already expose queue and runtime metrics.
- `OpenAIChatClient` backend reachability and warmup can dominate latency even when NGO is healthy.
- `PerformanceCullingSetup` affects client draw cost and therefore input/network feel under load.
- `WebGLOptimizationTool` is a valid build-side companion when browser performance is the bottleneck.

## Do Not Do

- Do not increase concurrency blindly without watching timeouts and queue churn.
- Do not ship long unbounded broadcast text to all clients.
- Do not blame NGO first when the remote inference backend is the real bottleneck.
