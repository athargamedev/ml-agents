# NPC LLM Dialogue System - Architecture Notes

## Goal
Use ML-Agents to TRAIN a behavior policy that learns when/how NPCs engage in dialogue.
ML-Agents is NOT used to route LLM calls — the existing LM Studio path handles that.

## CORRECT Architecture (confirmed this session)
```
Player → NetworkDialogueService → OpenAIChatClient → LM Studio   ← UNCHANGED
                │
                └── fires static events → NpcDialogueAgent (ML-Agents)
                                              │
                                              ├── observes: proximity, health, combat, turns
                                              ├── rewards: response quality, latency, feedback
                                              └── trains: engagement timing policy
```

## WRONG approach (do not repeat)
Routing every LLM call through the Python SideChannel bridge as a proxy adds
latency and complexity with no benefit. The existing OpenAIChatClient already
reaches LM Studio directly. SideChannelOverride is kept as an optional
experimental mode only, not the default.

## Key Unity Components for LLM Dialogue
1. **Unity.InferenceEngine** (`com.unity.ai.inference` v2.5.0)
   - Runs ONNX models locally in Unity at runtime
   - Supports GPU/CPU backends
   - Can run quantized LLMs (e.g., ONNX export of small models)
   - `Unity.InferenceEngine.Tokenization` package for text tokenization

2. **BufferSensor** — variable-length observations, useful for token sequences
3. **SideChannels** — `RawBytesChannel` can bridge Python LLM API calls to Unity
4. **SentisPolicy** — shows pattern for using Inference Engine within agent framework
5. **ModelRunner** — wraps inference, good reference for custom LLM runners

## Approaches for LLM in NPCs
- **Local inference**: Run small quantized LLM (ONNX) via Unity Inference Engine
- **Remote/Python bridge**: Use SideChannels to call Python LLM backend
- **Hybrid RL+LLM**: Use RL for behavior, LLM for dialogue generation

## Relevant Files
- `com.unity.ml-agents/Runtime/Inference/ModelRunner.cs` — inference runner pattern
- `com.unity.ml-agents/Runtime/Policies/SentisPolicy.cs` — local model inference
- `com.unity.ml-agents/Runtime/SideChannels/RawBytesChannel.cs` — raw data comms
- `com.unity.ml-agents/Runtime/Sensors/BufferSensor.cs` — variable-length obs
- `com.unity.ml-agents/Runtime/Agent.cs` — base agent class
