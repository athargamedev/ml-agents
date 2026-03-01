# Unity Assembly Definitions Map
_Generated: 2026-02-28 | Source: dev_tools/schemas/assembly_map.json_
_Refresh: `python dev_tools/run_dev_tools.py asmdef`_

## Project Assemblies (Assets/)

| Assembly | Platform | Folder | Auto-ref | Purpose |
|---|---|---|---|---|
| `Network_Game` | Runtime | `Assets/Network_Game/` | YES | Core runtime: dialogue, NPCs, effects, multiplayer game logic |
| `Network_Game.Editor` | Editor only | `Assets/Network_Game/Editor/` | YES | Editor tools / inspectors for Network_Game types |
| `Network_Game.Dialogue.Editor` | Editor only | `Assets/Network_Game/Dialogue/Editor/` | YES | Dialogue pipeline editor tools |
| `undream.llmunity.Runtime` | Runtime | `Assets/Network_Game/LLMUnity/Runtime/` | YES | LLMUnity package runtime |
| `undream.llmunity.Editor` | Editor only | `Assets/Network_Game/LLMUnity/Editor/` | YES | LLMUnity editor tools |
| `Network_Game.ParticlePack.Ramps.Editor` | Editor only | `Assets/Network_Game/ParticlePack/` | YES | Particle ramp editor |
| `NpcDialogue.Tests.Runtime` | Runtime | `Assets/ML-Agents/Scripts/Tests/Runtime/NpcDialogue/` | NO (test) | NPC dialogue runtime tests |
| `NpcDialogue.Tests.Editor` | Editor only | `Assets/ML-Agents/Scripts/Tests/Editor/NpcDialogue/` | NO (test) | NPC dialogue editor tests |
| `Unity.ML-Agents.DevTests.Runtime` | Runtime | `Assets/ML-Agents/Scripts/Tests/Runtime/` | NO (test) | Core ML-Agents runtime tests |
| `Unity.ML-Agents.DevTests.Editor` | Editor only | `Assets/ML-Agents/Scripts/Tests/Editor/` | NO (test) | Core ML-Agents editor tests |
| `TestAsmdef` | Runtime | `Assets/MCPTests/Scripts/TestAsmdef/` | NO | MCP integration test helper |
| `MCPForUnityTests.PlayMode` | Runtime | `Assets/MCPTests/Tests/PlayMode/` | NO (test) | MCP play mode tests |
| `MCPForUnityTests.EditMode` | Editor only | `Assets/MCPTests/Tests/EditMode/` | NO (test) | MCP edit mode tests |

## Dependency Graph (project assemblies)

```
undream.llmunity.Runtime
    <- Network_Game, Network_Game.Dialogue.Editor, undream.llmunity.Editor

Network_Game
    -> (project)  undream.llmunity.Runtime
    -> (package)  Unity.Netcode.Runtime, Unity.Networking.Transport
    -> (builtin)  Unity.Cinemachine, Unity.AI.Navigation, Unity.InputSystem,
                  Unity.TextMeshPro, UnityEngine.UI, Unity.Addressables, Unity.ResourceManager
    <- Network_Game.Dialogue.Editor, Network_Game.Editor,
       Network_Game.ParticlePack.Ramps.Editor, NpcDialogue.Tests.Runtime

Network_Game.Editor
    -> Network_Game, Unity.Netcode.Runtime, URP (Core+Universal), MCPForUnity.Editor
    <- MCPForUnityTests.EditMode

Network_Game.Dialogue.Editor
    -> Network_Game, undream.llmunity.Runtime, Unity.Netcode.Runtime, MCPForUnity.Editor

NpcDialogue.Tests.Runtime
    -> Network_Game, Unity.ML-Agents, UnityEngine.TestRunner, UnityEditor.TestRunner

NpcDialogue.Tests.Editor
    -> Unity.ML-Agents, UnityEngine.TestRunner, UnityEditor.TestRunner
```

## Key Package Assemblies (Packages/ — important for adding references)

| Assembly | Package | Purpose |
|---|---|---|
| `Unity.Netcode.Runtime` | `com.unity.netcode.gameobjects` | NetworkBehaviour, NetworkVariable, ClientRpc, ServerRpc |
| `Unity.Netcode.Editor` | `com.unity.netcode.gameobjects` | NGO editor tools |
| `Unity.Netcode.Editor.CodeGen` | `com.unity.netcode.gameobjects` | Source gen for RPCs |
| `Unity.Networking.Transport` | `com.unity.transport` | Low-level transport (refs: Burst, Collections, Mathematics) |
| `Unity.Multiplayer.Tools.Common` | `com.unity.multiplayer.tools` | Shared multiplayer tools base |
| `Unity.Multiplayer.Tools.NetStats` | `com.unity.multiplayer.tools` | Network statistics |
| `Unity.Multiplayer.Tools.NetStatsMonitor.Component` | `com.unity.multiplayer.tools` | Runtime stats monitor |
| `Unity.Multiplayer.Tools.NetworkSimulator.Runtime` | `com.unity.multiplayer.tools` | Network condition simulation |

## Critical Design Decision: ML-Agents ↔ Network_Game Decoupling

`Network_Game` does NOT reference `Unity.ML-Agents`.
`NpcDialogueAgent.cs` is in **default Assembly-CSharp** (no .asmdef in its folder).

This is intentional: dialogue and ML training communicate via **static C# events**
on `NetworkDialogueService` — not via direct type references across assemblies.
This keeps the game playable without ML-Agents installed.

If you ever need dialogue code to call ML-Agents API directly:
→ Add `Unity.ML-Agents` to `Network_Game.asmdef` references (consider runtime implications)
→ OR keep using the event-based bridge pattern

## Code Placement Guide

| New class type | Assembly | Folder |
|---|---|---|
| NPC dialogue runtime component | `Network_Game` | `Assets/Network_Game/Dialogue/` |
| Effect definition / catalog | `Network_Game` | `Assets/Network_Game/Dialogue/Effects/` |
| Dialogue editor window/inspector | `Network_Game.Dialogue.Editor` | `Assets/Network_Game/Dialogue/Editor/` |
| Multiplayer game runtime component | `Network_Game` | `Assets/Network_Game/` |
| Editor tool for Network_Game types | `Network_Game.Editor` | `Assets/Network_Game/Editor/` |
| ML-Agents agent script | Default (Assembly-CSharp) | `Assets/ML-Agents/Scripts/` |
| NPC dialogue runtime test | `NpcDialogue.Tests.Runtime` | `Assets/ML-Agents/Scripts/Tests/Runtime/NpcDialogue/` |

## CS0246 Fix Pattern

1. Find which assembly defines the missing type (check its source folder → .asmdef)
2. Find which assembly contains the failing .cs file
3. Add the defining assembly's `name` to the failing .asmdef's `"references"` array
4. Save .asmdef → Unity recompiles → check console for errors

## Gotchas

- `autoReferenced: true` assemblies are auto-visible to code NOT in any .asmdef (e.g. Assembly-CSharp)
- Test assemblies have `autoReferenced: false` — must be explicitly referenced
- Editor-only assemblies cannot be referenced by runtime assemblies
- Circular references are forbidden
- After editing .asmdef, ALL code in that assembly must compile clean before types are visible
