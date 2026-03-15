---
name: unity-auth-identity-guard
description: Diagnose and harden DevProject's player authentication, identity propagation, prompt-context initialization, and auth-gated dialogue flow. Use when login fails, identity is missing after spawn, prompt context does not sync, or dialogue requests are rejected for auth reasons.
---

# Unity Auth Identity Guard

## Focus Area

Target the path from login UI to dialogue identity snapshot:

- `DevProject/Assets/Network_Game/Auth/LocalPlayerAuthService.cs`
- `DevProject/Assets/Network_Game/Behavior/Unity Behavior Example/AuthBootstrap.cs`
- `DevProject/Assets/Network_Game/UI/Login/PlayerLoginController.cs`
- `DevProject/Assets/Network_Game/Dialogue/NetworkDialogueAuthGate.cs`
- `DevProject/Assets/Network_Game/Dialogue/NetworkDialogueService.cs`

## Workflow

1. Verify the player can log in locally and that `Login()` succeeds before any dialogue request is sent.
2. Check that `AttachLocalPlayer()` resolves a non-zero `NetworkObjectId`.
3. Confirm `EnsurePromptContextInitialized()` produced valid customization JSON with core identity fields.
4. Confirm the dialogue service sees the identity snapshot for the requesting client before user-initiated prompts.
5. If failure happens only after spawn, hand off to `$unity-player-spawn-authority`.

## Invariants

- Preserve `LocalPlayerAuthService.Login()` -> `EnsurePromptContextInitialized()` sequencing.
- Preserve `TryApplyCurrentMirrorLoraToDialogue()` prompt-context sync after login and after local-player attachment.
- Preserve `NetworkDialogueAuthGate.CanAccept(...)` strictness for authenticated user-initiated traffic.
- Login UI must only restore gameplay cursor/look after auth is satisfied.
- Prefer repairing missing identity propagation over disabling `m_RequireAuthenticatedPlayers`.

## Useful Checks

- Inspect logs in `NG:Auth` and `NG:Dialogue`.
- Use `ng_pipeline_status` or `ng_get_full_diagnostics` to confirm the dialogue side is live before blaming auth.
- When the host works and remote clients do not, inspect client identity snapshot creation and sender-client validation first.

## Do Not Do

- Do not bypass `PlayerLoginController` with temporary editor-only identities unless the user explicitly wants a test shim.
- Do not write prompt-context JSON shapes that omit `name_id` or player identity fields.
- Do not special-case host auth in a way that breaks real clients.
