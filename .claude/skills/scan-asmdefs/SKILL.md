---
name: scan-asmdefs
description: Scan all Unity .asmdef files and build/refresh the assembly dependency map. Use when the user asks about assembly references, gets a 'type not found' CS0246 error, wants to add a new C# class and needs to know which assembly it belongs to, or after adding/removing a Unity package. Also use to check for missing or broken assembly references.
---

# Scan AsmdDefs Skill — Unity Assembly Dependency Tracker

Scans all `.asmdef` files in `Assets/` and `Packages/` and builds a dependency graph.
No LM Studio needed — pure JSON parsing, runs in seconds.

## NON-NEGOTIABLE RULES

1. Run `asmdef` command BEFORE answering any "which assembly?" or "why type not found?" question.
2. ALWAYS read `dev_tools/schemas/assembly_map.json` after scanning — it's the source of truth.
3. When adding a reference to an `.asmdef`, ALWAYS verify the target assembly name from the map.
4. NEVER edit an `.asmdef` file without reading it first (it is valid JSON — treat it as such).
5. After editing any `.asmdef`, Unity must recompile — check `mcpforunity://editor/state` and then `read_console(types=["error"])`.

## Decision Tree

| User Says | Action | Notes |
|---|---|---|
| "CS0246 type not found" | Run `asmdef` → find which assembly has the type → add reference | Most common assembly error |
| "Where should my new script go?" | Read map → find nearest .asmdef to the target folder | Match folder location to assembly |
| "Add X package to my project code" | Find package assembly name in map → add to correct .asmdef | Use exact `name` field |
| "Refresh the assembly map" | Run `asmdef` command | Regenerates assembly_map.json |
| "Check for broken references" | Run `asmdef` → read the WARNING lines | Lists unresolved refs |
| "Which assembly is NetworkDialogueService in?" | Read map → `Network_Game` contains all Network_Game/Dialogue/ code | |
| "Can dialogue code use ML-Agents types?" | No — `Network_Game` does NOT reference `Unity.ML-Agents` | Add ref or use events instead |

## Run Command

```bash
C:/Users/andre_wjgj23f/miniconda3/envs/mlagents/python.exe dev_tools/run_dev_tools.py asmdef
```

Output: `dev_tools/schemas/assembly_map.json`

## Current Project Assembly Map (as of 2026-02-28)

### Core Project Assemblies

| Assembly | Location | Platform | Purpose |
|---|---|---|---|
| `Network_Game` | `Assets/Network_Game/` | Runtime | Main game logic: dialogue, NPCs, effects, multiplayer |
| `Network_Game.Editor` | `Assets/Network_Game/Editor/` | Editor only | Editor tools, inspectors for Network_Game types |
| `Network_Game.Dialogue.Editor` | `Assets/Network_Game/Dialogue/Editor/` | Editor only | Dialogue pipeline editor tools |
| `undream.llmunity.Runtime` | `Assets/Network_Game/LLMUnity/Runtime/` | Runtime | LLMUnity package runtime (used by Network_Game) |
| `undream.llmunity.Editor` | `Assets/Network_Game/LLMUnity/Editor/` | Editor only | LLMUnity editor tools |
| `Network_Game.ParticlePack.Ramps.Editor` | `Assets/Network_Game/ParticlePack/` | Editor only | Particle ramp editor |

### ML-Agents Test Assemblies

| Assembly | Location | Purpose |
|---|---|---|
| `NpcDialogue.Tests.Runtime` | `Assets/ML-Agents/Scripts/Tests/Runtime/NpcDialogue/` | NPC dialogue runtime tests (refs: ML-Agents, Network_Game) |
| `NpcDialogue.Tests.Editor` | `Assets/ML-Agents/Scripts/Tests/Editor/NpcDialogue/` | NPC dialogue editor tests (refs: ML-Agents only) |
| `Unity.ML-Agents.DevTests.Runtime` | `Assets/ML-Agents/Scripts/Tests/Runtime/` | Core ML-Agents runtime tests |
| `Unity.ML-Agents.DevTests.Editor` | `Assets/ML-Agents/Scripts/Tests/Editor/` | Core ML-Agents editor tests |

### Key Package Assemblies (from Packages/)

| Assembly | Package | Purpose |
|---|---|---|
| `Unity.Netcode.Runtime` | `com.unity.netcode.gameobjects` | Netcode for GameObjects runtime (NetworkBehaviour, NetworkVariable, etc.) |
| `Unity.Netcode.Editor` | `com.unity.netcode.gameobjects` | NGO editor tools |
| `Unity.Networking.Transport` | `com.unity.transport` | Low-level transport layer |
| `Unity.Multiplayer.Tools.Common` | `com.unity.multiplayer.tools` | Multiplayer Tools shared |
| `Unity.Multiplayer.Tools.NetStats` | `com.unity.multiplayer.tools` | Network statistics |

## Dependency Rules (what can reference what)

```
Network_Game  <-- most runtime code lives here
    references: undream.llmunity.Runtime, Unity.Netcode.Runtime, Unity.Networking.Transport
    does NOT reference: Unity.ML-Agents  ← dialogue/ML are decoupled via events

Network_Game.Editor  <-- editor-only tools
    references: Network_Game, Unity.Netcode.Runtime, MCPForUnity.Editor, URP

Network_Game.Dialogue.Editor  <-- dialogue inspector/editor tools
    references: Network_Game, undream.llmunity.Runtime, Unity.Netcode.Runtime, MCPForUnity.Editor

NpcDialogueAgent.cs lives in: Default assembly (Assembly-CSharp)
    → because ML-Agents/Scripts/ has no .asmdef of its own
    → this is why it can reference both Network_Game types AND Unity.ML-Agents
```

## Code Placement Guide

| New class type | Which assembly | Folder |
|---|---|---|
| New NPC dialogue runtime class | `Network_Game` | `Assets/Network_Game/Dialogue/` |
| New NPC effect runtime class | `Network_Game` | `Assets/Network_Game/Dialogue/Effects/` |
| New dialogue Editor window/inspector | `Network_Game.Dialogue.Editor` | `Assets/Network_Game/Dialogue/Editor/` |
| New multiplayer game runtime component | `Network_Game` | `Assets/Network_Game/` |
| New Network_Game Editor tool | `Network_Game.Editor` | `Assets/Network_Game/Editor/` |
| New ML-Agents agent script | Default assembly | `Assets/ML-Agents/Scripts/` |
| New NPC dialogue runtime test | `NpcDialogue.Tests.Runtime` | `Assets/ML-Agents/Scripts/Tests/Runtime/NpcDialogue/` |
| New NPC dialogue editor test | `NpcDialogue.Tests.Editor` | `Assets/ML-Agents/Scripts/Tests/Editor/NpcDialogue/` |

## Fixing "CS0246: Type not found" — Step by Step

```
1. Run: python dev_tools/run_dev_tools.py asmdef
2. Read: dev_tools/schemas/assembly_map.json
3. Find which assembly DEFINES the missing type (grep source file for namespace)
4. Find which assembly USES the missing type (the failing .cs file's folder → .asmdef lookup)
5. Add the defining assembly name to the using assembly's "references" array
6. Save the .asmdef (it's JSON — use Edit tool)
7. Unity recompiles automatically; check read_console(types=["error"])
```

Example fix: `NpcDialogueAgent.cs` wants `NetworkDialogueService` (in `Network_Game`):
- `NpcDialogueAgent.cs` is in default assembly (no .asmdef) → it auto-references all assemblies with `autoReferenced: true`
- `Network_Game` has `autoReferenced: true` → ALREADY available, no fix needed

Example fix: New Editor script wants `EffectCatalog` (in `Network_Game`):
- Add `"Network_Game"` to the `.asmdef` file's `"references"` array

## Known Gotchas

- **`autoReferenced: true`** assemblies are automatically available to all code without needing explicit `references` entries — but ONLY for code NOT in its own `.asmdef`.
- **`autoReferenced: false`** assemblies (tagged `no-autoref` in output) MUST be explicitly referenced — this applies to all test assemblies.
- **Editor-only assemblies** (`is_editor_only: true`) cannot be referenced by runtime assemblies — compiler error.
- **Circular references** are NOT allowed. If A references B, B cannot reference A.
- **GUID-based references** (e.g. `"GUID:abc123"`) are valid but fragile — prefer name-based references.
- `NpcDialogueAgent.cs` is in the **default Assembly-CSharp** (no .asmdef in its folder) — it can freely use `Network_Game` types because `autoReferenced: true`.
- After editing `.asmdef`, any **compilation error** in any file in that assembly will prevent ALL types in that assembly from being visible.
