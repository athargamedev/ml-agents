# Latest Code Scan Summary (2026-03-01)

Scanned 44 files | High: 60 | Medium: 75 | Low: 44

## Top Issues

- **HIGH** `AcademyStepperTest.cs` — [multiplayer_safety] ClientRpc called without checking IsOwner or IsServer
- **MEDIUM** `AcademyStepperTest.cs` — [ml_agents] [ML001] Reward spike without clamp (medium): Single large reward spikes cause PPO gradient instability. Cap per-component rewards at ±0.5.
- **MEDIUM** `AcademyStepperTest.cs` — [ml_agents] [ML002] Observation not normalised (medium): Neural network inputs work best in [0,1] or [-1,1]. Unnormalised observations cause slow convergence.
- **HIGH** `DialogueClientUI.cs` — [ml_agents] 
- **MEDIUM** `DialogueClientUI.cs` — [npc_dialogue] 
- **HIGH** `DialogueConstants.cs` — [multiplayer_safety] [MP001] ClientRpc without ownership check. All NetworkVariable writes must happen on the server or owner.
- **HIGH** `DialogueConstants.cs` — [multiplayer_safety] [MP002] NetworkVariable write outside server authority. NetworkVariable.Value can only be set by the server (default) or owner depending on write permission.
- **MEDIUM** `DialogueConstants.cs` — [ml_agents] [ML001] Reward spike without clamp. Single large reward spikes cause PPO gradient instability. Cap per-component rewards at ±0.5.
- **MEDIUM** `DialogueConstants.cs` — [ml_agents] [ML002] Observation not normalised. Neural network inputs work best in [0,1] or [-1,1]. Unnormalised observations cause slow convergence.
- **HIGH** `DialogueDebugPanel.cs` — [multiplayer_safety] [MP001] ClientRpc without ownership check. All NetworkVariable writes must happen on the server or owner.
- **HIGH** `DialogueDebugPanel.cs` — [multiplayer_safety] [MP002] NetworkVariable write outside server authority. NetworkVariable.Value can only be set by the server (default) or owner depending on write permission.
- **MEDIUM** `DialogueDebugPanel.cs` — [ml_agents] [ML001] Reward spike without clamp. Single large reward spikes cause PPO gradient instability. Cap per-component rewards at ±0.5.
- **MEDIUM** `DialogueDebugPanel.cs` — [ml_agents] [ML002] Observation not normalised. Neural network inputs work best in [0,1] or [-1,1]. Unnormalised observations cause slow convergence.
- **HIGH** `DialogueDebugPanel.cs` — [npc_dialogue] [NPC002] NetworkDialogueService.Instance without null check. Service is a scene singleton and may not be initialised during early Awake() calls.
- **MEDIUM** `DialogueEffectAutoTestMenu.cs` — [multiplayer_safety] [MP001] ClientRpc without ownership check: All NetworkVariable writes must happen on the server or owner. ClientRpc only runs on clients.
- **MEDIUM** `DialogueEffectAutoTestMenu.cs` — [multiplayer_safety] [MP002] NetworkVariable write outside server authority: NetworkVariable.Value can only be set by the server (default) or owner depending on write permission.
- **MEDIUM** `DialogueEffectAutoTestMenu.cs` — [ml_agents] [ML002] Observation not normalised: Neural network inputs work best in [0,1] or [-1,1]. Unnormalised observations cause slow convergence.
- **HIGH** `DialogueEffectAutoTestMenu.cs` — [npc_dialogue] [NPC002] NetworkDialogueService.Instance without null check: Service is a scene singleton and may not be initialised during early Awake() calls.
- **HIGH** `DialogueEffectBulkImporter.cs` — [multiplayer_safety] ClientRpc called without checking IsOwner or IsServer. All NetworkVariable writes must happen on the server or owner.
- **MEDIUM** `DialogueEffectBulkImporter.cs` — [ml_agents] [ML001] Reward spike without clamp. Single large reward spikes cause PPO gradient instability. Cap per-component rewards at ±0.5.

Full report: `dev_tools/reports/code_scan_2026-03-01.md`