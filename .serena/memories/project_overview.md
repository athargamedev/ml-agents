# ML-Agents Project Overview

## Purpose
This repository contains Unity ML-Agents plus the `DevProject` game, where the active focus is a multiplayer NPC dialogue system that uses LM Studio-hosted Qwen models through a server-authoritative runtime.

## Current runtime direction
- Production dialogue is **remote-only**.
- NPC dialogue is generated on the game/server side through LM Studio's OpenAI-compatible API.
- Clients send dialogue intents and render results; they do not run local inference.
- The old `Assets/Network_Game/LLMUnity` vendor package was removed from the project.

## Active stack
- **Unity**: `DevProject` (current editor observed on Unity 6 beta / `6000.4.0b9`)
- **C#**: gameplay, netcode, dialogue routing, UI
- **Python**: ML-Agents trainers, side-channel bridge utilities, training launcher
- **LM Studio**: primary inference gateway, default local endpoint `127.0.0.1:7002`
- **Qwen models**: default runtime family, with `qwen3-8b` as the main gameplay target

## Dialogue architecture (current)
- `DialogueBackendConfig` is the project-owned source of truth for LM Studio host/port/model/sampling/system prompt.
- `NetworkDialogueService` is the single server-authoritative dialogue router.
- `OpenAIChatClient` is the default runtime backend for gameplay dialogue.
- `SideChannelDialogueClient` is a training/testing override used by ML-Agents when explicitly injected.
- `NpcDialogueActor` + `NpcDialogueProfile` provide per-NPC persona behavior.

## Important repo state
- `Behavior_Scene` was migrated off the legacy `LLMAgent` component.
- `NetworkDialogueService` in-scene now relies on `DialogueBackendConfig`, not `LLMUnity`.
- The repo no longer has a compile-time dependency on `undream.llmunity.Runtime`.
- `ModernHudController` was retired; `ModernHudManager` is the active HUD path.

## Training launcher status
- `train_npc_dialogue.bat` now delegates to `run_training.py`.
- `run_training.py` performs the safer training launch path (including port cleanup).
- The old Unity HTTP MCP readiness check is **opt-in** now (`--unity-check`) and skipped by default.

## Environment notes
- Use the explicit ML-Agents Python environment path on this machine when needed:
  `C:\Users\andre_wjgj23f\miniconda3\envs\mlagents\python.exe`
- LM Studio model availability still matters: the requested model must already be loaded if a specific `model=` is sent.

## Key files to read first for dialogue work
- `DevProject/Assets/Network_Game/Dialogue/NetworkDialogueService.cs`
- `DevProject/Assets/Network_Game/Dialogue/DialogueBackendConfig.cs`
- `DevProject/Assets/Network_Game/Dialogue/OpenAIChatClient.cs`
- `DevProject/Assets/Network_Game/NetrokGame_ML-agents/Scripts/SideChannelDialogueClient.cs`
- `run_training.py`
