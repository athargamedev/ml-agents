---
name: unity-dialogue-runtime-triage
description: Diagnose DevProject's live LLM dialogue pipeline, including LLM readiness, queue state, participant targeting, response routing, and scene effect dispatch. Use when NPC dialogue stops responding, requests queue up, responses are misrouted, or effect dispatch breaks.
---

# Unity Dialogue Runtime Triage

## Focus Area

- `DevProject/Assets/Network_Game/Dialogue/NetworkDialogueService.cs`
- `DevProject/Assets/Network_Game/UI/Dialogue/ModernDialogueController.cs`
- `DevProject/Assets/Network_Game/Dialogue/NpcDialogueActor.cs`
- `DevProject/Assets/Network_Game/Dialogue/OpenAIChatClient.cs`
- `DevProject/Assets/Network_Game/Editor/CustomTools/DialoguePipelineTools.cs`
- `DevProject/Assets/Network_Game/Editor/CustomTools/Services/DialoguePipelineInspector.cs`

## Preferred Tools

- Register custom tools first: `Network Game/MCP/Admin/Register All Custom Tools`
- Use `ng_pipeline_status` for quick runtime state
- Use `ng_get_full_diagnostics` for full dump
- Use `ng_probe_npc_dialogue` only in Play Mode when you need an end-to-end probe

## Workflow

1. Establish whether the issue is pre-request, queueing, model execution, or response delivery.
2. Check LLM readiness, warmup state, and queue depth before touching UI logic.
3. Validate canonical conversation-key generation and participant selection.
4. Validate the request path:
   - UI builds request
   - service validates sender and participants
   - server enqueues/processes
   - targeted `DialogueResponseClientRpc` returns
5. Validate scene effects separately from text generation if only FX are failing.

## Invariants

- Preserve `NetworkDialogueService` as the only server-authoritative router.
- Preserve canonical routing through `ResolveConversationKey(...)`.
- Preserve targeted `ServerRpc` -> server processing -> `ClientRpc` response flow.
- Preserve auth and participant validation for user-initiated prompts.
- Keep effect dispatch server -> all clients; do not move effect spawning to local UI scripts.

## Useful Checks

- `m_MaxPendingRequests`, `m_MaxConcurrentRequests`, `m_MaxRequestsPerClient`, and timeout settings heavily shape runtime behavior.
- `ModernDialogueController` only sends when it has a local player, a valid NPC in range, and a live service.
- `OpenAIChatClient` is the remote OpenAI-compatible transport; if warmup is degraded or the backend is unreachable, fix that before rewriting dialogue logic.

## Do Not Do

- Do not bypass the service with direct HTTP calls from UI components.
- Do not disable repeat/reply guards to hide loop bugs.
- Do not diagnose effect failures from the UI alone when the service already exposes runtime diagnostics.
