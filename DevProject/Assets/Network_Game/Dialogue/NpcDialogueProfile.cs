using System;
using System.Collections.Generic;
using Network_Game.Dialogue.Effects;
using UnityEngine;

namespace Network_Game.Dialogue
{
    [Serializable]
    public class PrefabPowerEntry
    {
        public string PowerName = "New Power";
        public bool Enabled = true;
        public string[] Keywords;
        public GameObject EffectPrefab;

        [Min(0f)]
        public float DurationSeconds = 4f;

        [Min(0.1f)]
        public float Scale = 1f;
        public Vector3 SpawnOffset = new Vector3(0f, 0.5f, 0f);
        public bool SpawnInFrontOfNpc = true;

        [Min(0f)]
        public float ForwardDistance = 2f;
        public Color ColorOverride = Color.white;
        public bool UseColorOverride;

        [Tooltip(
            "Element type for LLM creative matching (fire, ice, storm, water, earth, nature, mystic, void)."
         )]
        public string Element = "";

        [Tooltip(
            "Short visual description injected into LLM prompt for creative effect decisions."
         )]
        public string VisualDescription = "";

        [Tooltip(
            "Alternative narrative phrases that can trigger this power beyond exact keyword matches."
         )]
        public string[] CreativeTriggers;

        [Header("Gameplay (Optional)")]
        [Tooltip("When enabled, the spawned effect can apply gameplay damage.")]
        public bool EnableGameplayDamage;

        [Tooltip("When enabled, projectile movement steers toward the selected target.")]
        public bool EnableHoming;

        [Min(0.1f)]
        [Tooltip("Projectile travel speed in meters per second.")]
        public float ProjectileSpeed = 10f;

        [Min(0f)]
        [Tooltip("Maximum steering rotation speed (degrees/second) for homing projectiles.")]
        public float HomingTurnRateDegrees = 240f;

        [Min(0f)]
        [Tooltip("Damage applied on impact.")]
        public float DamageAmount = 10f;

        [Min(0.1f)]
        [Tooltip("Impact overlap radius used for damage application.")]
        public float DamageRadius = 1f;

        [Tooltip("If true, damage is applied only to objects recognized as player actors.")]
        public bool AffectPlayerOnly = true;

        [Tooltip("Damage type label used for logs/telemetry.")]
        public string DamageType = "effect";


        /// <summary>
        /// Creates a runtime EffectDefinition from this profile entry so it can be
        /// registered with EffectCatalog and dispatched through the structured tag path.
        /// </summary>
        public EffectDefinition ToEffectDefinition()
        {
            var def = ScriptableObject.CreateInstance<EffectDefinition>();
            def.name = PowerName ?? string.Empty;
            def.effectTag = string.IsNullOrWhiteSpace(PowerName)
                ? (EffectPrefab != null ? EffectPrefab.name : string.Empty)
                : PowerName.Trim();
            def.description = VisualDescription ?? string.Empty;
            def.effectPrefab = EffectPrefab;
            def.defaultScale = Scale;
            def.defaultDuration = DurationSeconds;
            def.defaultColor = UseColorOverride ? ColorOverride : Color.white;
            def.allowCustomColor = true;
            def.allowCustomScale = true;
            def.allowCustomDuration = true;
            def.enableGameplayDamage = EnableGameplayDamage;
            def.enableHoming = EnableHoming;
            def.projectileSpeed = ProjectileSpeed;
            def.homingTurnRateDegrees = HomingTurnRateDegrees;
            def.damageAmount = DamageAmount;
            def.damageRadius = DamageRadius;
            def.affectPlayerOnly = AffectPlayerOnly;
            def.damageType = DamageType;
            def.minScale = 0.1f;
            def.maxScale = 50f;
            def.minDuration = 0.1f;
            def.maxDuration = 45f;
            def.minRadius = 0.1f;
            def.maxRadius = 40f;
            return def;
        }
    }

    [CreateAssetMenu(
        fileName = "NpcDialogueProfile",
        menuName = "Network Game/Dialogue/NPC Dialogue Profile"
     )]
    public class NpcDialogueProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string m_ProfileId = "npc.default";

        [SerializeField]
        private string m_DisplayName = "NPC";

        [Header("Personality")]
        [SerializeField]
        [TextArea(5, 12)]
        private string m_SystemPrompt =
            "You are {npc_name}. Keep responses concise, in-character, and useful to the player.";

        [Header("Lore")]
        [SerializeField]
        [TextArea(10, 20)]
        [Tooltip(
            "Comprehensive NPC background and world-building data injected into the system prompt."
         )]
        private string m_Lore = "";

        [Header("Context Effects - Bored Lighting")]
        [SerializeField]
        private bool m_EnableBoredLightEffect = true;

        [SerializeField]
        private string[] m_BoredKeywords =
        {
            "bored",
            "boring",
            "not interested",
            "nothing to do",
            "tired of this",
        };

        [SerializeField]
        private Color m_BoredLightColor = Color.blue;

        [SerializeField]
        [Min(0f)]
        private float m_BoredLightIntensity = 1.2f;

        [SerializeField]
        [Min(0f)]
        private float m_LightTransitionSeconds = 0.4f;

        [Header("Prefab Powers")]
        [SerializeField]
        [Tooltip("Prefab-based powers that instantiate ParticlePack effects during dialogue.")]
        private PrefabPowerEntry[] m_PrefabPowers = new PrefabPowerEntry[0];

        [Header("Dynamic Effect Parameters")]
        [SerializeField]
        [Tooltip("Allow dialogue wording to modulate effect parameters at runtime.")]
        private bool m_EnableDynamicEffectParameters = true;

        [SerializeField]
        [Range(0.25f, 1f)]
        [Tooltip("Lower clamp for dynamic multipliers extracted from dialogue.")]
        private float m_DynamicEffectMinMultiplier = 0.6f;

        [SerializeField]
        [Range(1f, 10f)]
        [Tooltip("Upper clamp for dynamic multipliers extracted from dialogue.")]
        private float m_DynamicEffectMaxMultiplier = 2f;

        public string ProfileId => m_ProfileId;
        public string DisplayName => m_DisplayName;
        public string SystemPrompt => m_SystemPrompt;
        public string Lore => m_Lore;
        public bool EnableBoredLightEffect => m_EnableBoredLightEffect;
        public string[] BoredKeywords => m_BoredKeywords;
        public Color BoredLightColor => m_BoredLightColor;
        public float BoredLightIntensity => m_BoredLightIntensity;
        public float LightTransitionSeconds => m_LightTransitionSeconds;
        public PrefabPowerEntry[] PrefabPowers => m_PrefabPowers;
        public bool EnableDynamicEffectParameters => m_EnableDynamicEffectParameters;
        public float DynamicEffectMinMultiplier => m_DynamicEffectMinMultiplier;
        public float DynamicEffectMaxMultiplier => m_DynamicEffectMaxMultiplier;

        /// <summary>
        /// Get all keywords from profile (bored keywords + all power keywords)
        /// </summary>
        public string[] GetKeywords()
        {
            var keywords = new List<string>();

            // Add bored keywords
            if (m_BoredKeywords != null)
                keywords.AddRange(m_BoredKeywords);

            // Add power keywords
            if (m_PrefabPowers != null)
            {
                foreach (var power in m_PrefabPowers)
                {
                    if (power.Keywords != null)
                        keywords.AddRange(power.Keywords);
                }
            }

            return keywords.ToArray();
        }

        /// <summary>
        /// Find all dialogue profiles in Resources folders
        /// </summary>
        public static NpcDialogueProfile[] GetAllProfiles()
        {
            return Resources.LoadAll<NpcDialogueProfile>("");
        }

        /// <summary>
        /// Find profile by its ProfileId
        /// </summary>
        public static NpcDialogueProfile GetProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return null;

            var profiles = Resources.LoadAll<NpcDialogueProfile>("");
            foreach (var profile in profiles)
            {
                if (profile.ProfileId == profileId)
                    return profile;
            }
            return null;
        }

        /// <summary>
        /// Get all keywords as a HashSet for O(1) lookup.
        /// Call this once and cache the result for fast keyword checking.
        /// </summary>
        public HashSet<string> GetKeywordIndex()
        {
            var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add bored keywords
            if (m_BoredKeywords != null)
            {
                foreach (var kw in m_BoredKeywords)
                {
                    if (!string.IsNullOrWhiteSpace(kw))
                        index.Add(kw.Trim().ToLowerInvariant());
                }
            }

            // Add power keywords
            if (m_PrefabPowers != null)
            {
                foreach (var power in m_PrefabPowers)
                {
                    if (power.Keywords != null)
                    {
                        foreach (var kw in power.Keywords)
                        {
                            if (!string.IsNullOrWhiteSpace(kw))
                                index.Add(kw.Trim().ToLowerInvariant());
                        }
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// Fast O(1) keyword containment check using pre-built index.
        /// </summary>
        public bool HasKeyword(string keyword, HashSet<string> cachedIndex)
        {
            if (cachedIndex == null || string.IsNullOrWhiteSpace(keyword))
                return false;
            return cachedIndex.Contains(keyword.Trim().ToLowerInvariant());
        }

        /// <summary>
        /// Build a compressed effect guide for the LLM system prompt.
        /// Limits output to maxPowers to prevent prompt bloat.
        /// </summary>
        /// <param name="listenerName">Name of the listener/player</param>
        /// <param name="maxPowers">Maximum number of powers to include</param>
        /// <returns>Compressed effect guide string</returns>
        public string BuildCompressedEffectGuide(string listenerName, int maxPowers = 5)
        {
            if (m_PrefabPowers == null || m_PrefabPowers.Length == 0)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Effects] You have visual powers. Use them. Append ONE tag at the END of your response.");
            sb.AppendLine($"Format: [EFFECT: EffectName | Target: {listenerName}]");
            sb.AppendLine("Include a tag whenever you react with emotion, cast, warn, reward or punish. Skip only for pure exposition.");
            sb.AppendLine();

            // Limit powers
            int count = 0;
            foreach (var power in m_PrefabPowers)
            {
                if (power == null || !power.Enabled || count >= maxPowers)
                    continue;

                string label = string.IsNullOrWhiteSpace(power.PowerName)
                    ? (power.EffectPrefab?.name ?? $"power_{count + 1}")
                    : power.PowerName.Trim();

                string desc = !string.IsNullOrWhiteSpace(power.VisualDescription)
                    ? power.VisualDescription
                    : $"Particle effect: {label}";

                sb.AppendLine($"- **{label}**: {desc}");
                if (!string.IsNullOrWhiteSpace(power.Element))
                    sb.AppendLine($"  Element: {power.Element}");
                sb.AppendLine($"  → [EFFECT: {label} | Target: {listenerName}]");
                count++;
            }

            return sb.ToString().Trim();
        }
    }
}
