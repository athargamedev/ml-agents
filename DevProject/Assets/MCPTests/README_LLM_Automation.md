# LLM Dialogue Automation Guide

This folder contains the runtime dialogue pipeline and effects integration. Use this guide to keep automation safe, server-authoritative, and prompt-efficient.

## Where to Start

- Runtime entry point: `Assets/Network_Game/Dialogue/NetworkDialogueService.cs`
- Effect routing: `Assets/Network_Game/Dialogue/DialogueSceneEffectsController.cs`
- Effect spatial resolution: `Assets/Network_Game/Dialogue/DialogueEffectSpatialResolver.cs`
- Prompt feedback loop: `Assets/Network_Game/Dialogue/DialogueEffectFeedbackRuntimeTuner.cs`

## Automation Principles (Required)

- Server-authoritative: effects are dispatched from the server; clients only receive via ClientRpc.
- Deterministic keys: effect keys are resolved from a fixed registry; no ad-hoc strings.
- Idempotent edits: repeatable operations must result in `no_op` when unchanged.
- Validate after edits: run `validate_script(level:"standard")` after any automation change.

## Safe Edit Workflow

1. Apply structured edits (insert/replace/delete) to dialogue or effect mapping code.
2. Validate scripts. If validation fails, revert and log the effect key as blocked.
3. Only after validation, allow the effect to be dispatched at runtime.

## Prompt Efficiency

- Use a strict response schema with minimal tokens.
- Keep effect vocabulary short and explicit.
- Avoid synonyms unless mapped in a deterministic registry.

## Effect Dispatch Checklist

- Resolve target anchor with `find_gameobjects`.
- Verify required components via `get_hierarchy` componentTypes.
- Dispatch from server via `NetworkDialogueService`.
- Replicate with ClientRpc only.

See also: `docs/LLM_Dialogue_Automation_Playbook.md`
