# Latest Code Scan Summary (2026-03-04)

Scanned 46 files | High: 18 | Medium: 19 | Low: 6

## Top Issues

- **HIGH** `CombatHealth.cs` — [multiplayer_safety] Accessing NetworkObject in Awake() may be called before the object is spawned on the network, leading to potential null references or incorrect state synchronization. Use OnNetworkSpawn() for initialization that depends on network state.
- **HIGH** `CombatHealth.cs` — [multiplayer_safety] Setting NetworkVariable.Value in Awake() without checking if the object is owned by the server can lead to desynchronization. Ensure that any writes to NetworkVariables are guarded with IsServer/IsOwner checks.
- **MEDIUM** `CombatHealth.cs` — [ml_agents] The NormalizedHealth property uses a direct division without applying the required denominator normalization. This may lead to inconsistent observations for ML-Agents training.
- **HIGH** `CombatRuntimeOverlay.cs` — [multiplayer_safety] The toggle key is serialized but not protected against network desynchronization. Since this value can be modified by clients, it should use [SyncVar] or [ClientRpc] to ensure consistency across the network.
- **MEDIUM** `CombatRuntimeOverlay.cs` — [unity_best_practices] The EnsureInstance method uses FindAnyObjectByType with FindObjectsInactive.Include, which can lead to unexpected behavior if multiple instances exist or during scene transitions. Consider using a static instance variable and singleton pattern for better control.
- **MEDIUM** `CombatRuntimeOverlay.cs` — [unity_best_practices] Awake is called before the object is added to the scene, which may lead to issues with UI Toolkit or other systems that require the object to be active. Consider moving initialization logic to OnEnable or a later stage.
- **MEDIUM** `CombatRuntimeOverlay.cs` — [unity_best_practices] The Update method contains a potential typo (m_Show instead of m_ShowOverlay) and performs frequent UI updates which can impact performance. Consider adding change guards or optimizing the refresh logic.
- **HIGH** `DialogueAnimationContextBuilder.cs` — [multiplayer_safety] Accessing NetworkObject via GetComponent/GetComponentInParent before OnEnable() can lead to incorrect references. Ensure network object is properly initialized by checking IsSpawned or using OnNetworkSpawn for critical operations.
- **MEDIUM** `DialogueAnimationContextBuilder.cs` — [unity_best_practices] Awake() is not guaranteed to be called after NetworkObject initialization. Move network-related setup to OnEnable() or later.
- **MEDIUM** `DialogueAnimationContextBuilder.cs` — [ml_agents] The 'IsSpeaking' flag is derived from a time-based threshold without considering the actual dialogue content or speech duration. This may lead to inaccurate observations for ML-Agents training.
- **MEDIUM** `DialogueAnimationContextBuilder.cs` — [unity_best_practices] Repeated calls to GetComponent/GetComponentInParent inside ResolveTargetNpc() can be inefficient. Cache the result or use a single call with fallback.
- **HIGH** `DialogueClientUI.cs` — [multiplayer_safety] Accessing a NetworkObject field before OnNetworkSpawn() can lead to desynchronization. Ensure all NetworkObject references are accessed only after OnNetworkSpawn().
- **HIGH** `DialogueClientUI.cs` — [multiplayer_safety] Accessing a NetworkObject field before OnNetworkSpawn() can lead to desynchronization. Ensure all NetworkObject references are accessed only after OnNetworkSpawn().
- **HIGH** `DialogueClientUI.cs` — [ml_agents] Setting TextMeshPro.text directly without sanitization can introduce rich-text tags which may cause issues in WebGL builds. Use a sanitized version of the input.
- **MEDIUM** `DialogueClientUI.cs` — [unity_best_practices] Updating TextMeshPro.text in a loop without checking for changes can cause unnecessary UI updates. Consider adding a change guard.
- **HIGH** `DialogueEffectProjectile.cs` — [multiplayer_safety] Accessing NetworkObject without checking if the object is spawned. This can lead to desynchronization issues or invalid state access on clients.
- **MEDIUM** `DialogueEffectProjectile.cs` — [unity_best_practices] Using a fixed-size array for overlap buffer may lead to inefficiency if the number of overlapping objects exceeds the buffer size. Consider using a dynamic list or increasing the buffer size appropriately.
- **MEDIUM** `DialogueEffectProjectile.cs` — [ml_agents] Clamping values is good practice, but ensure that the clamped value does not introduce unintended behavior in reward shaping or observation normalization. Verify if this clamping aligns with your PPO training requirements.
- **HIGH** `DialogueFeedbackCollector.cs` — [multiplayer_safety] The Awake() method accesses the NetworkDialogueService.Instance in its logic, which might not be fully initialized at this stage. This could lead to race conditions or null reference exceptions when trying to access services that are only available after OnEnable().
- **HIGH** `DialogueParticleCollisionDamage.cs` — [multiplayer_safety] The ApplyDamage method is called on a CombatHealth component without checking if the target NetworkObject has authority. This can lead to desynchronization issues in multiplayer environments.

Full report: `dev_tools/reports/code_scan_2026-03-04.md`