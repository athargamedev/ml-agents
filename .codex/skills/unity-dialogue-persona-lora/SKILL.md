---
name: unity-dialogue-persona-lora
description: Manage DevProject's NPC dialogue personas, profile assets, prompt templates, powers, and player mirror-LoRA identity bindings. Use when changing NPC behavior, editing profile assets, syncing prompt context to dialogue, or testing persona-specific powers.
---

# Unity Dialogue Persona LoRA

## Focus Area

- `DevProject/Assets/Network_Game/Dialogue/NpcDialogueProfile.cs`
- `DevProject/Assets/Network_Game/Auth/LocalPlayerAuthService.cs`
- `DevProject/Assets/Network_Game/Editor/CustomTools/ProfileManagementTools.cs`
- `DevProject/Assets/Network_Game/Editor/CustomTools/Services/ProfileAutomationService.cs`
- `DevProject/Assets/Network_Game/Dialogue/Effects/*`

## Preferred Tools

- `ng_create_npc_profile`
- `ng_modify_npc_profile`
- `ng_probe_npc_dialogue`
- `ng_get_full_diagnostics`

## Workflow

1. Confirm whether the change is player identity, NPC persona, or power/effect authoring.
2. For player identity, inspect `LocalPlayerAuthService` prompt-context generation and mirror-LoRA sync first.
3. For NPC personas, edit `NpcDialogueProfile` assets or use the MCP profile tools.
4. For powers/effects, keep keywords, prefabs, and profile bindings aligned.
5. Run a targeted dialogue probe after any persona or LoRA change.

## Invariants

- Keep one active dialogue service owner; do not re-enable per-NPC `LLMAgent` ownership paths.
- Keep player prompt-context JSON rich enough for persona/mirror binding.
- Keep profile power keywords aligned with actual effect assets and dispatch paths.
- Keep profile changes additive and explicit; avoid hidden magic defaults that make authored personas hard to reason about.

## Do Not Do

- Do not stuff large ad hoc persona state directly into gameplay MonoBehaviours when profile assets already exist.
- Do not sync LoRA state by bypassing `LocalPlayerAuthService`.
- Do not add new effect semantics without matching profile keywords and runtime dispatch support.
