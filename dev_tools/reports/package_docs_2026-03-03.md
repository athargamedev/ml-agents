# Package Docs — 2026-03-03 02:50

Documented 14 core packages + tool packages batch.
Models: core=qwen3-8b / tools=llama-3.2-3b-instruct@q8_0

## Core Packages

---
### `com.unity.netcode.gameobjects` v2.9.2  _71.6s_


## Purpose  
This package provides the core multiplayer networking functionality required for synchronizing game objects across clients and servers in this RPG project. It enables real-time interaction with NPCs and player entities through Unity's NetCode framework.

## Key APIs  
- `NetworkObject`  
- `NetworkBehaviour`  
- `NetworkVariable<T>`  
- `ClientRpc`  
- `ServerRpc`  

## Project Usage  
The **Networked NPCs** (StormOracle, ForgeKeeper, Archivist) use `NetworkObject` and `NetworkBehaviour` to synchronize their state across the network. The **NetworkDialogueService** leverages `ServerRpc` to trigger dialogue events from the server, ensuring consistency in LLM-driven conversations.

## Gotchas  
- Unity 6's NetCode 2.9.2 may have issues with certain `NetworkVariable<T>` types when used with ML-Agents; ensure all variables are properly serialized and synchronized.  
- `ClientRpc` calls can cause desynchronization if not properly timed or if the client is disconnected mid-call. Use `ServerAuthoritative` mode for critical state updates.  
- Ensure that `NetworkObject` components are correctly set up in the Inspector, as missing or incorrect configuration can lead to objects not syncing across the network.

## Related Packages  
- **NGO 2.9.2** (NetCode GameObjects) – Provides networking infrastructure for game objects.  
- **ML-Agents** – Used for training NpcDialogueAgent with reward-based dialogue systems.  
- **ModernHudController** – Synchronizes UI state across clients using NetworkVariables.

---
### `com.unity.ml-agents` vfile:../../com.unity.ml-agents  _45.3s_


## Purpose  
The `com.unity.ml-agents` package provides tools for implementing reinforcement learning (RL) agents in Unity, enabling the project's NpcDialogueAgent to learn dialogue patterns through reward-based training.

## Key APIs  
- **Agent**: Core class representing the RL agent that interacts with the environment.  
- **SideChannel**: Used for communication between the agent and other systems during training.  
- **CollectObservations**: Method to gather environmental data (observations) for the agent.  
- **AddReward**: Function to assign rewards based on the agent's actions during dialogue interactions.  
- **EndEpisode**: Called to terminate an episode and reset the environment state.

## Project Usage  
The project uses `com.unity.ml-agents` to train the NpcDialogueAgent via ML-Agents, with `CollectObservations` gathering 7 observations from the environment, `AddReward` reinforcing dialogue choices, and `EndEpisode` signaling the end of a training session. The `SideChannel` facilitates communication between the agent and the NetworkDialogueService.

## Gotchas  
- **Unity 6 Compatibility**: Ensure that all ML-Agents components are compatible with Unity 6 (6000.4.x) as some older versions may not work correctly.  
- **Observation Space Size**: The `CollectObservations` method must be configured to capture exactly 7 observations, or the training will fail due to mismatched input sizes.  
- **Reward Scaling**: Rewards should be carefully scaled to avoid saturation, which can hinder learning progress in the NpcDialogueAgent.  
- **Episode Length**: Long episodes may cause memory issues; ensure `EndEpisode` is called frequently enough to manage resource usage.

## Related Packages  
- **NGO 2.9.2**: Used for networked game objects and NPC behavior synchronization with the ML-Agents training system.  
- **NetworkDialogueService**: Manages dialogue flow and reward signals between the NpcDialogueAgent and the game world.  
- **ModernHudController**: Displays training metrics and agent performance during RL sessions.

---
### `com.unity.inputsystem` v1.18.0  _49.9s_


## Purpose  
The `com.unity.inputsystem` package provides a flexible and scalable input system for handling player and NPC interactions, essential for this project's complex multiplayer RPG with custom UI and networked AI behaviors.

## Key APIs  
- `InputAction`  
- `PlayerInput`  
- `Keyboard.current`  
- `Mouse.current`  
- `InputSystem.RegisterBindingOverride`  

## Project Usage  
This package is used to manage player input via `PlayerInput` and `InputAction`, supporting both keyboard/mouse and custom cursor management. It also integrates with the StarterAssetsInputs for movement and interaction, while `Keyboard.current` and `Mouse.current` are leveraged for precise UI interactions in the ModernHudController.

## Gotchas  
- Unity 6's Input System may have subtle binding conflicts when combined with legacy input systems; ensure all bindings are explicitly defined.  
- `InputAction` requires proper activation via `Enable()` and `Disable()` to avoid unexpected behavior in multiplayer contexts.  
- The `Keyboard.current` and `Mouse.current` properties can be unreliable if not used within an `InputSystem.Update()` context, leading to input lag or missed events.

## Related Packages  
- `com.unity.starterassets` (for movement and interaction handling)  
- `com.unity.cinemachine` (for camera input integration)  
- `com.unity.ui-toolkit` (ModernHudController UI interactions)

---
### `com.unity.ai.navigation` v2.0.11  _45.2s_


## Purpose  
This package provides essential tools for implementing AI navigation in Unity, including NavMesh baking and agent movement systems, which are critical for the project's multiplayer RPG with Networked NPCs.

## Key APIs  
- `NavMeshSurface`  
- `NavMeshAgent`  
- `NavMeshBuildSettings`  
- `NavMeshData`  

## Project Usage  
The project uses `NavMeshSurface` to bake navigation data asynchronously, enabling efficient pathfinding for NPCs like StormOracle and ForgeKeeper. `NavMeshAgent` is used to control the movement of AI entities in the game world, ensuring they navigate terrain and avoid obstacles during LLM-driven dialogue interactions.

## Gotchas  
- Async baking may cause issues with scene loading if not properly synchronized with Unity 6's new asset pipeline.  
- NavMeshAgent performance can degrade on complex terrains without proper `NavMeshBuildSettings` optimization.  
- Ensure `NavMeshSurface` is correctly set up in the Editor to avoid runtime errors during multiplayer synchronization.

## Related Packages  
- **InputSystem 1.18**: Used for handling player input that may influence NPC navigation behavior.  
- **Cinemachine 3.1.6**: Integrates with NavMeshAgent for dynamic camera movement around navigating NPCs.  
- **ModernHudController**: Displays navigation status or pathfinding-related UI feedback to players.

---
### `com.unity.visualeffectgraph` v17.4.0  _40.7s_


## Purpose  
The `com.unity.visualeffectgraph` package provides tools for creating and managing visual effects using the Visual Effect Graph system, which is essential for this project's particle and animation effects via the `EffectDispatcher` system.

## Key APIs  
- `VisualEffect`  
- `SetFloat`, `SetVector`  
- `OutputEvent`  
- `VisualEffectGraph`  

## Project Usage  
The `EffectDispatcher` system integrates with `VisualEffect` to trigger and control particle effects, using `SetFloat`/`SetVector` for dynamic parameter adjustments and `OutputEvent` for event-driven animations.

## Gotchas  
- In Unity 6 (6000.4.x), some VFX Graph features may have limited support or require specific shader compatibility checks.  
- Ensure all Visual Effect Graphs are compiled with the correct rendering pipeline (URP 17.4) to avoid runtime errors.  
- `OutputEvent` may not trigger reliably if used in conjunction with legacy particle systems; prefer VFX Graph-only solutions for consistency.

## Related Packages  
- `com.unity.render-pipelines.universal` (URP 17.4)  
- `EffectDispatcher`  
- `ModernHudController` (for UI Toolkit integration with visual effects)

---
### `com.unity.render-pipelines.universal` v17.4.0  _44.3s_


## Purpose  
The `com.unity.render-pipelines.universal` package provides the core Universal Render Pipeline (URP) system for Unity 6, enabling efficient and flexible rendering in this project's multiplayer RPG with URP 17.4. It is essential for achieving consistent shader compatibility and managing the camera stack for networked NPCs and UI systems.

## Key APIs  
- `UniversalRenderPipelineAsset`  
- `CameraStack`  
- `ShaderGraphCompatibilityLayer`  
- `RendererFeatures`  

## Project Usage  
This package is used to configure the Universal Render Pipeline via `UniversalRenderPipelineAsset`, manage the camera stack for URP 17.4, and ensure shader compatibility with custom VFX Graphs and UI Toolkit elements like `ModernHudController`. It also supports rendering for networked NPCs and their LLM-driven dialogue systems.

## Gotchas  
- Ensure all shaders are compatible with URP 17.4 to avoid runtime errors; some legacy shaders may not work without conversion.  
- Camera stack configuration in Unity 6 can be fragile—verify that the main camera is correctly set up as a Universal Renderer and that post-processing effects are properly assigned.  
- Shader Graph compatibility might require manual adjustments for custom VFX used in `EffectDispatcher`.  

## Related Packages  
- `com.unity.render-pipelines.core` (core URP functionality)  
- `com.unity.shadergraph` (for shader graph compatibility and VFX Graph integration)  
- `com.unity.ui` (UI Toolkit support for `ModernHudController`)

---
### `com.unity.cinemachine` v3.1.6  _57.5s_


## Purpose  
Cinemachine provides advanced camera control systems for dynamic and responsive camera behavior, essential for this multiplayer RPG project's complex scene transitions and combat camera logic.

## Key APIs  
- `CinemachineCamera`  
- `CinemachineBrain`  
- `CinemachineAutoFocus`  
- `PriorityBlend`  

## Project Usage  
The project uses Cinemachine to manage dynamic camera transitions during combat, with `CinemachineBrain` handling priority blending between different camera modes. `Cinem.Lens` and `CinemachineAutoFocus` are used to ensure the player and action targets remain in focus during fast-paced multiplayer interactions.

## Gotchas  
- Unity 6's new camera system may cause conflicts if not properly configured with Cinemachine 3.1.6; ensure all camera components are set to use Cinemachine as the primary camera system.  
- Priority blending can sometimes lead to unexpected behavior when multiple `CinemachineCamera` instances are active; test thoroughly in multiplayer scenarios.  
- The `CinemachineAutoFocus` feature may require manual tuning of focus distance and subject settings for optimal performance in a fast-paced RPG environment.

## Related Packages  
- **InputSystem 1.18**: Used alongside Cinemachine to handle camera input during player movement and targeting.  
- **NGO 2.9.2**: Integrates with Cinemachine for UI camera transitions and overlay management.  
- **ModernHudController**: Utilizes Cinemachine's focus system to ensure HUD elements remain in view during dynamic camera changes.

---
### `com.unity.transport` v2.6.0  _59.1s_


## Purpose  
Com.unity.transport provides low-level networking capabilities essential for enabling multiplayer features in this project, including support for the NGO package and custom SideChannel transport systems.

## Key APIs  
- `Transport` class — Manages network communication and message routing  
- `TransportSystem` — Central system for initializing and managing transports  
- `MessageHandler` — Interface for handling incoming messages  
- `SideChannel` — Enables additional data channels for custom networking needs  

## Project Usage  
The project uses `com.unity.transport` internally via NGO for multiplayer synchronization, and also leverages its SideChannel API to implement custom networked features like NPC dialogue and effect dispatching.

## Gotchas  
- Unity 6's transport system may require explicit configuration of message handlers to avoid deserialization errors with complex data types used in LLM dialogue.  
- SideChannel usage must be carefully synchronized with the main transport to prevent race conditions in multiplayer RPG state updates.  
- Ensure `TransportSystem` is initialized before any networked systems (like `NetworkDialogueService`) to avoid missing initialization steps in Unity 6.  

## Related Packages  
- **NGO 2.9.2** — Uses this package for low-level networking under the hood  
- **ModernHudController** — May rely on transport for synchronized UI state updates across clients  
- **EffectDispatcher** — Could use SideChannel for networked VFX synchronization

---
### `com.unity.addressables` v2.8.1  _61.2s_


## Purpose  
The `com.unity.addressables` package enables efficient and flexible asset management for this project's multiplayer RPG, supporting async loading of assets like NPCs, VFX, and UI elements while maintaining memory efficiency.

## Key APIs  
- `AsyncOperationHandle<T>`: Represents a handle to an asynchronous operation for loading assets.  
- `Addressables.LoadAssetAsync<T>(string key)`: Loads an asset asynchronously using its Addressable label.  
- `Addressables.ReleaseAsync(AsyncOperationHandle handle)`: Releases the memory used by a loaded asset.  
- `ContentCatalog`: Manages catalog entries for assets, enabling dynamic loading based on scene or context.

## Project Usage  
This project uses `com.unity.addressables` to load and manage assets such as NPCs, VFX effects, and UI elements dynamically during runtime. Systems like `EffectDispatcher` and `ModernHudController` rely on Addressables for efficient asset retrieval and memory management.

## Gotchas  
- **Memory leaks**: Ensure all `AsyncOperationHandle` instances are properly released using `ReleaseAsync()` to avoid memory bloat in a multiplayer environment.  
- **Label conflicts**: Be cautious with label naming to prevent unintended asset loading, especially when multiple systems (e.g., `NetworkDialogueService`, `ML-Agents`) use Addressables.  
- **Scene transitions**: Addressables may not unload assets immediately during scene changes; use `UnloadAssetsAsync()` for deterministic cleanup.  
- **Versioning issues**: Ensure all scenes and assets are correctly versioned to avoid loading errors in Unity 6.

## Related Packages  
- `NGO 2.9.2`: Used alongside Addressables for object pooling and asset management.  
- `InputSystem 1.18`: Integrates with Addressables for dynamic UI element loading based on input context.  
- `Cinemachine 3.1.6`: Leverages Addressables to load camera effects and transitions asynchronously.

---
### `com.unity.ai.inference` v2.5.0  _81.4s_


## Purpose  
This package provides a runtime inference engine for executing ONNX models in Unity 6, enabling the project to run AI-driven behaviors like Walker and Agent models used by NPCs with LLM dialogue.

## Key APIs  
- `InferenceSession` – Manages the execution of an ONNX model.  
- `Tensor` – Represents input/output data for model inference.  
- `ModelConfig` – Configuration settings for loading and running the ONNX model.  
- `InferenceManager` – Central manager for handling multiple inference sessions.

## Project Usage  
The project uses this package to load and execute ONNX models for NPC movement and behavior, such as Walker and Agent models, which are integrated with ML-Agents for reward-based dialogue training. The `NetworkDialogueService` interacts with these models to generate dynamic NPC responses based on player interactions.

## Gotchas  
- Ensure the model is compatible with Unity 6 and Barracuda's ONNX runtime; some older models may require conversion or retraining.  
- Be cautious with memory usage when running multiple inference sessions simultaneously, as this can impact performance in a multiplayer environment.  
- The package may not support all ONNX opsets; verify compatibility with the version used in your project.

## Related Packages  
- `com.unity.ml-agents` – Used for training and integrating AI models with reward-based dialogue systems.  
- `NGO 2.9.2` – Integrates with inference sessions to manage NPC behaviors and state updates.  
- `ModernHudController` – Displays AI model status or inference results in the UI.

---
### `com.unity.multiplayer.tools` v2.2.8  _92.6s_


## Purpose  
This package provides tools for diagnosing and simulating network conditions in multiplayer applications, enabling runtime monitoring of network stats and controlled testing of latency and packet loss scenarios.

## Key APIs  
- `NetworkStatsMonitor` – Monitors real-time network statistics like bandwidth usage and latency.  
- `NetworkSimulator` – Simulates network lag, packet loss, and other environmental factors for testing.  
- `NetStatsReporter` – Reports detailed network performance metrics to the console or UI.  

## Project Usage  
The **Networked NPCs** system uses `NetworkStatsMonitor` to track connection quality during dialogue interactions, while the **NetworkSimulator** is used in QA environments to test how NPCs behave under simulated lag conditions. The **ModernHudController** integrates `NetStatsReporter` to display network health to players.

## Gotchas  
- In Unity 6, `NetworkStatsMonitor` may require manual setup of the `UnityTransport` system before it can collect accurate data.  
- Simulating high latency with `NetworkSimulator` can cause desynchronization issues if not paired with proper interpolation or prediction logic in the multiplayer systems.  
- The package’s diagnostic tools are disabled by default in release builds; ensure they are enabled for testing environments.  

## Related Packages  
- **com.unity.transport** – Provides the underlying networking stack used by `NetworkStatsMonitor` and `NetworkSimulator`.  
- **ModernHudController** – Displays network diagnostics to players via UI Toolkit.  
- **NGO 2.9.2** – Integrates with network tools for multiplayer state synchronization.

---
### `com.unity.physics` v1.4.5  _75.0s_


## Purpose  
This package provides DOTS-based physics systems for simulating rigid body dynamics, enabling realistic interactions between game objects in the multiplayer RPG project.

## Key APIs  
- `PhysicsBody`  
- `PhysicsShape`  
- `ICollisionEventsJob`  
- `RigidBodyState`  

## Project Usage  
The **Networked NPCs** use `PhysicsBody` and `PhysicsShape` to handle movement and collision detection, while `ICollisionEventsJob` is used by the **EffectDispatcher** system to trigger VFX on collisions. The **ModernHudController** also relies on physics data for dynamic UI interactions.

## Gotchas  
- DOTS physics requires careful setup of job systems and dependencies; ensure all jobs are properly scheduled in the Unity 6 ECS framework.  
- Physics simulations may experience performance issues if not optimized, especially with large numbers of rigid bodies in multiplayer environments.  
- Ensure compatibility between `PhysicsBody` and existing **NGO 2.9.2** systems when integrating physics-based animations or interactions.

## Related Packages  
- **NGO 2.9.2** (for animation and game object management)  
- **InputSystem 1.18** (for physics-based movement input handling)  
- **Cinemachine 3.1.6** (for camera interactions with physics objects)

---
### `com.unity.entities.graphics` v6.4.0  _51.2s_


## Purpose  
This package enables efficient GPU instancing and render batching for static meshes in a DOTS hybrid rendering setup, which is critical for optimizing performance in the multiplayer RPG project with large numbers of networked NPCs.

## Key APIs  
- `RenderMeshArray`  
- `GraphicsJob`  
- `InstanceData`  
- `RendererGroup`  

## Project Usage  
The ModernHudController and NetworkDialogueService use this package to render static UI elements and NPC visual effects efficiently, leveraging GPU instancing for performance gains in the URP 17.4 pipeline.

## Gotchas  
- Ensure all static meshes used with `RenderMeshArray` are properly configured for GPU instancing in Unity 6.  
- Be cautious of memory usage when using `GraphicsJob` in a DOTS hybrid setup; improper configuration can lead to GC pressure.  
- `RendererGroup` may require manual synchronization in multi-threaded environments, especially when combined with ML-Agents systems.

## Related Packages  
- `com.unity.entities` (for DOTS hybrid rendering foundation)  
- `com.unity.render-pipelines.universal` (URP 17.4 pipeline integration)  
- `com.unity.inputsystem` (for input-driven instancing updates in multiplayer contexts)

---
### `com.unity.modules.uielements` v1.0.0  _47.7s_


## Purpose  
The `com.unity.modules.uielements` package provides the foundational UI components and tools required for building modern UI systems in Unity, specifically supporting UI Toolkit features like UIDocument, VisualElement, USS styling, and UxmlElement definitions. This project uses it to implement its custom HUD system via the ModernHudController.

## Key APIs  
- `UIDocument`  
- `VisualElement`  
- `USS` (Unity Style Sheets)  
- `UxmlElement`  
- `IVisualElementProperty`  

## Project Usage  
The ModernHudController leverages UIDocument and VisualElement to create a stack-based UI system for the game's HUD, using USS for styling. UxmlElement is used to define reusable UI components within the UI Toolkit framework.

## Gotchas  
- **USS styling may not cascade correctly in nested VisualElements** when using Unity 6 and older versions of UI Toolkit; ensure proper use of `@import` and scope selectors.  
- **UIDocument can have performance issues with large numbers of VisualElements** due to the way UI Toolkit handles rendering — optimize by batching or reducing unnecessary elements.  
- **UxmlElement definitions must be properly registered** in the project's UXML files; missing registrations can lead to runtime errors when loading UI documents.

## Related Packages  
- `com.unity.ui` (UI Toolkit core package)  
- `ModernHudController` (custom UI system built using this package)  
- `InputSystem 1.18` (used alongside UI elements for input handling in the HUD)

---
## Tool Packages (batch)

* com.unity.probuilder vX.Y — Level geometry prototyping tool. Gotcha: In Unity 6, ProBuilder's physics simulation can be slow due to the lack of a built-in physics engine.
* com.unity.recorder vX.Y — Video/image capture for training session recording. Gotcha: The Recorder package does not support capturing audio in Unity 6, resulting in incomplete recordings.
* com.unity.terrain-tools vX.Y — Terrain sculpting and painting tool. Gotcha: In Unity 6, the Terrain Tools package's terrain generation can be affected by the Editor's performance issues with large scenes.
* com.unity.performance.profile-analyzer vX.Y — Frame time comparison between builds. Gotcha: The Performance Analyzer in Unity 6 may not accurately represent the frame times of certain builds due to the Editor's caching mechanisms.
* com.unity.multiplayer.playmode v2.0.1 — Multi-instance editor testing for NGO multiplayer. 
* com.unity.test-framework vX.Y — Unity Test Runner — NpcDialogueAgentPlayModeTests. Gotcha: In Unity 6, the Test Framework package may not properly handle asynchronous tests due to its reliance on the Editor's synchronous execution model.
* com.unity.testtools.codecoverage vX.Y — Test coverage reporting. Gotcha: The CodeCoverage tool in Unity 6 has issues with reporting code coverage for certain types of assets, such as scripts with complex dependencies.
* com.unity.nuget.newtonsoft-json vX.Y — JSON.NET — available as fallback to JsonUtility. 
* com.unity.formats.fbx vX.Y — FBX import/export for Mixamo animations. Gotcha: In Unity 6, the FBX import/export process can be slow due to the Editor's lack of multi-threading support for large files.
