# Project Memory — ML-Agents (NPC LLM Dialogue)

## Purpose
Unity ML-Agents v4.0.x. User goal: develop an **LLM dialogue system for NPCs** using the DevProject.

## Key Files
- `com.unity.ml-agents/Runtime/Agent.cs` — base agent class
- `com.unity.ml-agents/Runtime/Inference/ModelRunner.cs` — inference engine runner
- `com.unity.ml-agents/Runtime/Policies/SentisPolicy.cs` — local ONNX inference policy
- `com.unity.ml-agents/Runtime/SideChannels/RawBytesChannel.cs` — Python<->Unity raw comms
- `com.unity.ml-agents/Runtime/Sensors/BufferSensor.cs` — variable-length observations

## DevProject Packages
- `com.unity.ai.inference` v2.5.0 — run ONNX/LLMs in Unity runtime (key for local LLM)
- `Unity.InferenceEngine.Tokenization` — text tokenization in Unity
- `com.unity.ai.navigation` v2.0.10 — NavMesh for NPC movement
- `com.coplaydev.unity-mcp` — Unity MCP server

## C# Style
- PascalCase for classes/methods; m_ prefix for private fields; k_ prefix for constants
- Namespaces: Unity.MLAgents, Unity.MLAgents.Sensors, Unity.MLAgents.Policies

## Notes
- See `memory/npc_llm_dialogue/architecture_notes.md` for LLM dialogue design notes
- See `memory/project_overview.md` for full structure
