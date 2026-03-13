using UnityEngine;

/// <summary>
/// Captures dynamic player state each frame for injection into NPC system prompts.
///
/// Attach to the same GameObject as NpcDialogueAgent, or to a scene manager.
/// Wire references in the Inspector, or call SetPlayerHealth/SetInCombat from
/// your health and combat systems.
/// </summary>
public class GameStateProvider : MonoBehaviour
{
    [Header("Player")]
    [SerializeField]
    private Transform m_PlayerTransform;

    [Tooltip("Max HP used to normalise current HP to 0-1 range.")]
    [SerializeField]
    [Min(1f)]
    private float m_MaxPlayerHealth = 100f;

    // Pushed each frame by the health system via SetPlayerHealth()
    private float m_CurrentPlayerHealth;

    [Header("Combat")]
    // Pushed by the combat system via SetInCombat()
    private bool m_IsPlayerInCombat;

    private void Awake()
    {
        m_CurrentPlayerHealth = m_MaxPlayerHealth;
    }

    // ── Public read accessors ─────────────────────────────────────────────────

    public float NormalizedPlayerHealth => Mathf.Clamp01(m_CurrentPlayerHealth / m_MaxPlayerHealth);

    public bool IsPlayerInCombat => m_IsPlayerInCombat;

    public Vector3 PlayerPosition =>
        m_PlayerTransform != null ? m_PlayerTransform.position : Vector3.zero;

    /// <summary>
    /// Returns a compact string appended to NPC system prompts so the LLM
    /// can factor in real-time game state when choosing how to respond.
    /// Example: "[GameState] player_health=72% in_combat=false pos=(12,8)"
    /// </summary>
    public string BuildDynamicContext()
    {
        int healthPct = Mathf.RoundToInt(NormalizedPlayerHealth * 100f);
        Vector3 pos = PlayerPosition;
        return $"[GameState] player_health={healthPct}% in_combat={m_IsPlayerInCombat} pos=({pos.x:F0},{pos.z:F0})";
    }

    // ── Write accessors (called by health/combat systems) ─────────────────────

    /// <summary>Push current HP from your health system each frame or on change.</summary>
    public void SetPlayerHealth(float current) => m_CurrentPlayerHealth = Mathf.Max(0f, current);

    /// <summary>Push combat state from your combat system.</summary>
    public void SetInCombat(bool inCombat) => m_IsPlayerInCombat = inCombat;

    /// <summary>
    /// Override the serialised player transform at runtime — called by NpcDialogueAgent
    /// when it resolves the spawned player instance by tag after scene load.
    /// </summary>
    public void SetPlayerTransform(Transform t) => m_PlayerTransform = t;
}
