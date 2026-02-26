# ML-Agents Project Overview

## Purpose
Unity ML-Agents Toolkit v4.0.x — open-source framework for training intelligent agents in Unity. User is using the **DevProject** to develop and test an **LLM dialogue system for NPCs**.

## Tech Stack
- **C#** (Unity 2022.3+) — Unity side (agents, sensors, actuators, policies, inference)
- **Python** (PyTorch) — training framework (PPO, SAC, POCA, GAIL, BC)
- **ONNX** — model exchange format between training and Unity inference
- **Unity Inference Engine** (`com.unity.ai.inference` v2.5.0, formerly Sentis) — runs ONNX models in Unity at runtime

## DevProject Key Packages
- `com.unity.ai.inference` v2.5.0 — run neural nets (including LLMs) in Unity
- `com.unity.ml-agents` (local file reference)
- `com.unity.ai.navigation` v2.0.10 — NavMesh for NPC movement
- `com.coplaydev.unity-mcp` — MCP server integration for Unity
- `Unity.InferenceEngine.Tokenization` csproj — text tokenization (key for LLM)

## Project Structure
```
com.unity.ml-agents/
  Runtime/
    Agent.cs              # Base MonoBehaviour (OnEpisodeBegin, CollectObservations, OnActionReceived, Heuristic)
    Sensors/              # VectorSensor, BufferSensor, RayPerceptionSensor, CameraSensor, GridSensor
    Actuators/            # IActuator, VectorActuator, ActionSpec
    Policies/             # SentisPolicy, HeuristicPolicy, RemotePolicy, BehaviorParameters
    Inference/            # ModelRunner (wraps Unity Inference Engine), TensorGenerator, TensorApplier
    SideChannels/         # Bidirectional Python<->Unity comms (SideChannel, RawBytesChannel, FloatPropertiesChannel)
ml-agents/mlagents/
  trainers/
    ppo/, sac/, poca/     # Trainer implementations
    policy/               # Neural network policies
    buffer.py             # Experience replay
    settings.py           # Training config (YAML-driven)
DevProject/Assets/ML-Agents/Scripts/Tests/  # Dev tests
config/ppo/, sac/, poca/, imitation/        # YAML training configs
```

## Runtime Environment
- **LM Studio**: `127.0.0.1:7002`, model `llama-3.2-3b-instruct`, API key prefix `sk-lm-`
- **Python env**: `C:\Users\andre_wjgj23f\miniconda3\envs\mlagents\python.exe` (conda is NOT in bash PATH — always use full path)
- **mlagents package**: installed to site-packages (NOT editable). New trainers files need `sys.path.insert` in runner scripts.
- **UnityMCP**: `com.coplaydev.unity-mcp` — MCP server for Unity Editor automation

## ML-Agents Integration Status
The project has a fully integrated NPC dialogue training layer:
- `DevProject/Assets/ML-Agents/Scripts/NpcDialogueAgent.cs` — scene-level Agent (observer + reward shaper)
- `DevProject/Assets/ML-Agents/Scripts/SideChannelDialogueClient.cs` — optional Python bridge client
- `DevProject/Assets/ML-Agents/Scripts/GameStateProvider.cs` — player state capture
- `DevProject/Assets/ML-Agents/Scripts/LlmDialogueChannel.cs` — SideChannel (GUID: a1b2c3d4-e5f6-7890-abcd-ef1234567890)
- `ml-agents/mlagents/trainers/llm_dialogue_channel.py` — Python counterpart
- `ml-agents/mlagents/trainers/llm_bridge_server.py` — LLM handler factories
- `run_llm_bridge.py` — standalone bridge runner (repo root)
- `NetworkDialogueService.cs` — has `SetMLAgentsSideChannelClient()` + `m_OverrideClient` hook

## C# Naming Conventions
- PascalCase for classes, methods, properties
- m_ prefix for private member fields (e.g., m_Sensor)
- k_ prefix for constants
- Interfaces prefixed with I (ISensor, IActuator, IPolicy)
- Namespaces: Unity.MLAgents, Unity.MLAgents.Sensors, Unity.MLAgents.Actuators, Unity.MLAgents.Policies

## Python Conventions
- Snake_case, type hints used, dataclasses for config (attrs library)
- Training configs in YAML
