# Package Docs — 2026-03-01 15:08

Documented 4 core packages + tool packages batch.
Models: core=qwen3-8b / tools=llama-3.2-3b-instruct@q8_0

## Core Packages

---
### `com.unity.netcode.gameobjects` v2.9.2  _73.8s_


## Purpose  
This package provides the core networking infrastructure for multiplayer synchronization, enabling real-time interaction between clients and servers in this RPG project. It is essential for synchronizing game objects, variables, and events across networked entities like NPCs and player characters.

## Key APIs  
- `NetworkBehaviour`  
- `NetworkObject`  
- `NetworkVariable<T>`  
- `ClientRpc`  
- `ServerRpc`  

## Project Usage  
The project uses this package to synchronize NPC behaviors, such as dialogue triggers and state changes, across the network. Systems like `NetworkDialogueService` and `EffectDispatcher` rely on it for real-time updates between clients and the server.

## Gotchas  
- Unity 6's serialization system may require explicit `[SerializeField]` attributes on `NetworkVariable<T>` fields to ensure they are properly synchronized.  
- NetworkObject must be assigned via the Inspector or code before spawning in multiplayer; missing this can cause desynchronization issues.  
- RPCs (ClientRpc/ServerRpc) should avoid heavy computations on the server to prevent latency spikes in a large-scale RPG environment.  

## Related Packages  
- `com.unity.inputsystem` – For handling input synchronization across clients.  
- `NGO 2.9.2` – For managing game object ownership and replication logic.  
- `ModernHudController` – For updating UI elements based on networked state changes.

---
### `com.unity.cinemachine` v3.1.6  _154.1s_


## Purpose  
Cinemachine provides advanced camera control systems for dynamic and responsive camera behavior, essential for this project's complex multiplayer RPG with networked NPCs and immersive UI. It enables smooth transitions, focus adjustments, and prioritized blending between multiple camera setups.

## Key APIs  
- `CinemachineCamera`  
- `CinemachineBrain`  
- `CinemachineAutoFocus`  
- `PriorityBlend`  

## Project Usage  
The project uses Cinemachine to manage dynamic camera transitions during combat and dialogue sequences. The `CinemachineBrain` handles priority blending between the main camera and overlay cameras for UI elements, while `CinemachineAutoFocus` ensures NPCs remain in focus during interactions. This integrates with the `ModernHudController` for seamless UI transitions.

## Gotchas  
- Unity 6's VFX Graph 17.4 may cause compatibility issues with older Cinemachine versions; ensure all systems are properly versioned.  
- When using priority blending, ensure camera priorities are set correctly to avoid unexpected switching during critical gameplay moments.  
- `CinemachineAutoFocus` may require manual tuning for optimal performance in multiplayer environments due to network latency considerations.

## Related Packages  
- `com.unity.inputsystem` (for input-driven camera adjustments)  
- `com.unity.vfxgraph` (for integrating visual effects with camera transitions)  
- `ngo` (for managing game state and camera behavior during networked events)

---
### `com.unity.transport` v2.6.0  _160.4s_


## Purpose  
Provides low-level networking functionality essential for real-time multiplayer synchronization, enabling the project's custom SideChannel transport and internal use by NGO for reliable network communication.

## Key APIs  
- `Transport` class for managing network connections  
- `Packet` class for encapsulating serialized data  
- `TransportSystem` for handling packet transmission and reception  
- `TransportMessageHandler` for processing incoming messages  

## Project Usage  
The project uses `com.unity.transport` internally via NGO for synchronizing game state across clients, and also leverages its low-level APIs to implement a custom SideChannel transport for NPC dialogue and effect synchronization.

## Gotchas  
- Unity 6's networking stack may require explicit configuration of packet sizes and threading models to avoid desynchronization in real-time multiplayer scenarios.  
- Ensure `Transport` instances are properly disposed of to prevent memory leaks, especially when handling multiple simultaneous connections.  
- The `Packet` class must be serialized correctly with the InputSystem package to maintain data integrity across networked clients.  

## Related Packages  
- **NGO 2.9.2**: Uses `com.unity.transport` internally for network synchronization.  
- **NetworkDialogueService**: Relies on transport layer for NPC dialogue and LLM message delivery.  
- **EffectDispatcher**: Utilizes transport for VFX synchronization across clients.

---
### `com.unity.multiplayer.tools` v2.2.8  _171.5s_


## Purpose  
This package provides tools for diagnosing and simulating network conditions in multiplayer applications, enabling runtime monitoring of network stats and controlled testing of latency and packet loss scenarios.

## Key APIs  
- `NetworkStatsMonitor` – Monitors real-time network statistics like bandwidth usage and latency.  
- `NetworkSimulator` – Simulates network lag, packet loss, and other environmental factors for testing.  
- `NetStatsReporter` – Aggregates and reports network performance metrics to the UI or logs.  

## Project Usage  
The **Networked NPCs** system uses `NetworkStatsMonitor` to track connection quality during dialogue interactions, while the **ModernHudController** displays real-time stats via the UI. The **NetworkSimulator** is used in QA environments to stress-test **ML-Agents** and **EffectDispatcher** under simulated poor network conditions.

## Gotchas  
- `NetworkStatsMonitor` may not accurately reflect real-world performance on Windows due to Unity 6's networking stack changes.  
- Simulating high latency with `NetworkSimulator` can cause desynchronization issues if not paired with proper state reconciliation logic.  
- Ensure `NetStatsReporter` is configured correctly in the build settings for URP 17.4 to avoid UI rendering glitches.

## Related Packages  
- **com.unity.inputsystem** – Used alongside `NetworkSimulator` to handle input latency testing.  
- **NGO 2.9.2** – Integrates with `NetworkStatsMonitor` for centralized logging and diagnostics.  
- **ModernHudController** – Displays network stats reported by `NetStatsReporter` in the UI.

---
## Tool Packages (batch)

* com.unity.probuilder vX.Y — Level geometry prototyping tool. Gotcha: When using ProBuilder in a multiplayer project, ensure that the ProBuilder server is properly configured to handle concurrent editing sessions.
* com.unity.recorder vX.Y — Video/image capture for training session recording. Gotcha: The Recorder package does not support capturing audio from Unity's Input System; use an external audio source or adjust your recording settings accordingly.
* com.unity.terrain-tools vX.Y — Terrain sculpting and painting tool. Gotcha: When using the Terrain Tools in a multiplayer project, be aware that changes made by one player may not be immediately visible to other players due to the asynchronous nature of the tools' updates.
* com.unity.performance.profile-analyzer vX.Y — Frame time comparison between builds. Gotcha: The Performance Profile Analyzer does not account for GPU rendering overhead; use it in conjunction with a CPU-focused profiler for more accurate results.
* com.unity.multiplayer.playmode v2.0.1 — Multi-instance editor testing for NGO multiplayer. Gotcha: When using the Multi-Instance Editor in a multiplayer project, ensure that each instance is properly configured to handle its own network connection and NPC instances.
* com.unity.test-framework vX.Y — Unity Test Runner — NpcDialogueAgentPlayModeTests. Gotcha: The Test Framework does not support testing asynchronous code; use coroutines or async/await when writing test cases for NpcDialogueAgent.
* com.unity.testtools.codecoverage vX.Y — Test coverage reporting. Gotcha: CodeCoverage reports may not accurately reflect the actual execution of your tests due to Unity's Just-In-Time (JIT) compiler optimizations; consider using a different code analysis tool for more accurate results.
* com.unity.nuget.newtonsoft-json vX.Y — JSON.NET — available as fallback to JsonUtility. Gotcha: When using Newtonsoft.Json in a multiplayer project, be aware that the package's serialization format may not be compatible with all network protocols; use a custom serializer or adjust your data formats accordingly.
* com.unity.formats.fbx vX.Y — FBX import/export for Mixamo animations. Gotcha: The FBX importer does not support importing Mixamo animations with complex physics simulations; consider using an alternative animation format or adjusting the simulation settings in Unity.
