# Latest Code Scan Summary (2026-03-03)

Scanned 42 files | High: 12 | Medium: 21 | Low: 5

## Top Issues

- **HIGH** `CombatHealth.cs` — [multiplayer_safety] Accessing NetworkObject component in Awake() before OnNetworkSpawn() may cause issues if the object hasn't been spawned yet. Safe to access only after OnEnable() or OnNetworkSpawn().
- **HIGH** `CombatHealth.cs` — [multiplayer_safety] Setting CurrentHealth in Awake() may not be safe if the network isn't ready. Prefer setting it in OnEnable() or later after ensuring the scene is fully initialized.
- **MEDIUM** `CombatHealth.cs` — [unity_best_practices] Using StringComparer.Ordinal for dictionary keys may lead to unexpected behavior if the animator parameters are case-insensitive or use different casing. Consider using StringComparer.OrdinalIgnoreCase for consistency.
- **MEDIUM** `CombatHealth.cs` — [unity_best_practices] Same as above; using StringComparer.Ordinal may cause issues with case sensitivity in coroutine keys. Use StringComparer.OrdinalIgnoreCase for consistency.
- **HIGH** `DialogueClientUI.cs` — [multiplayer_safety] NetworkObject field accessed in Awake() without OnNetworkSpawn guard. Accessing NetworkObject before OnNetworkSpawn can lead to desynchronization issues.
- **HIGH** `DialogueClientUI.cs` — [multiplayer_safety] NetworkObject field accessed in Awake() without OnNetworkSpawn guard. Accessing NetworkObject before OnNetworkSpawn can lead to desynchronization issues.
- **MEDIUM** `DialogueClientUI.cs` — [ml_agents] Directly setting TextMeshPro text without normalization. This may include raw world-space values or unnormalized observations, which can destabilize ML-Agents training.
- **MEDIUM** `DialogueClientUI.cs` — [unity_best_practices] Using onEndEdit listener in Awake() may cause issues if the input field is not yet active. Consider using a flag or checking for null before adding listeners.
- **MEDIUM** `DialogueDebugPanel.cs` — [unity_best_practices] GetComponent<T>() inside Update() or similar hot methods. The code uses OnGUI(), which is also a performance concern, but the main issue here is that UI updates are happening in Update(). Consider using Unity's built-in UI system (e.g., Canvas + TextMeshPro) for better performance and maintainability.
- **MEDIUM** `DialogueDebugPanel.cs` — [unity_best_practices] Using Keyboard.current inside Update() can be performance-intensive. Consider using InputSystem's low-level methods or caching the key state to reduce overhead.
- **MEDIUM** `DialogueDebugPanel.cs` — [unity_best_practices] OnGUI() is deprecated and can cause rendering issues, especially in Unity 6. Consider migrating to the new UI system (e.g., Canvas, TextMeshPro) for better compatibility and performance.
- **MEDIUM** `DialogueDebugPanel.cs` — [unity_best_practices] Performing heavy operations inside Update() can lead to performance issues. Consider using a coroutine or event-based system for periodic updates.
- **HIGH** `DialogueEffectProjectile.cs` — [multiplayer_safety] Accessing NetworkManager.Singleton without checking if it's the server. This can lead to desynchronization issues in multiplayer scenarios.
- **MEDIUM** `DialogueEffectProjectile.cs` — [ml_agents] The speed value is clamped but not normalized for the observation space. This could affect reward shaping and training stability.
- **MEDIUM** `DialogueEffectProjectile.cs` — [unity_best_practices] Using a fixed-size array for overlap buffer may not be efficient. Consider using a dynamic list or resizing based on demand.
- **HIGH** `DialogueFeedbackCollector.cs` — [multiplayer_safety] The Awake() method may be called before the NetworkDialogueService has been initialized, which could lead to issues if any dependencies on it are used. The NetworkFeedbackCollector should ensure that NetworkDialogueService is available before performing operations that depend on it.
- **HIGH** `DialogueParticleCollisionDamage.cs` — [multiplayer_safety] The method ApplyDamage is called on a NetworkObject's CombatHealth component without checking if the target has ownership. This can lead to desynchronization issues in multiplayer environments.
- **MEDIUM** `DialogueParticleCollisionDamage.cs` — [unity_best_practices] The dictionary is initialized in a field declaration. It's better to initialize it in the constructor or during setup for clarity and memory management.
- **MEDIUM** `DialogueParticleCollisionDamage.cs` — [unity_best_practices] The collider array is initialized in a field declaration. It's better to initialize it in the constructor or during setup for clarity and memory management.
- **MEDIUM** `EffectParser.cs` — [npc_dialogue] SplitParts is not defined in the provided code snippet. This could lead to unexpected behavior or compilation errors.

Full report: `dev_tools/reports/code_scan_2026-03-03.md`