using UnityEngine;

namespace Network_Game.Dialogue.Effects
{
    public enum EffectPlacementMode
    {
        Auto = 0,
        AttachMesh = 1,
        GroundAoe = 2,
        SkyVolume = 3,
        Projectile = 4,
    }

    public enum EffectTargetType
    {
        Auto = 0,
        Player = 1,
        Floor = 2,
        Npc = 3,
        WorldPoint = 4,
    }

    /// <summary>
    /// Data-driven effect definition that the LLM can reference.
    /// Replaces ad-hoc keyword definitions in NpcDialogueProfile.PrefabPowerEntry.
    /// </summary>
    [CreateAssetMenu(fileName = "EffectDefinition", menuName = "Dialogue/Effect Definition")]
    public class EffectDefinition : ScriptableObject
    {
        [Header("Tag that the LLM must emit")]
        [Tooltip("Exact name used in [EFFECT: TagName]")]
        public string effectTag;

        [Header("Human-readable description for the LLM prompt")]
        [TextArea(3, 6)]
        public string description;

        [Header("Prefab that will be instantiated")]
        public GameObject effectPrefab;

        [Header("Default Parameters")]
        [Tooltip("Default scale multiplier when not specified in the effect tag")]
        public float defaultScale = 1f;

        [Tooltip("Default duration in seconds when not specified")]
        public float defaultDuration = 4f;

        [Tooltip("Default color when not specified")]
        public Color defaultColor = Color.white;

        [Header("Parameter Schema")]
        [Tooltip("Allow the LLM to override scale via 'Scale: X' parameter")]
        public bool allowCustomScale = true;

        [Tooltip("Allow the LLM to override duration via 'Duration: X' parameter")]
        public bool allowCustomDuration = true;

        [Tooltip("Allow the LLM to override color via 'Color: X' parameter")]
        public bool allowCustomColor = true;

        [Header("Execution Profile")]
        [Tooltip("Default placement behavior for this effect when parser hints are ambiguous.")]
        public EffectPlacementMode placementMode = EffectPlacementMode.Auto;

        [Tooltip("Expected semantic target for this effect.")]
        public EffectTargetType targetType = EffectTargetType.Auto;

        [Tooltip("When true and placement is mesh-attached, try to fit to target render bounds.")]
        public bool preferFitTargetMesh = true;

        [Tooltip("Optional bone/transform hint for future attachment logic (e.g. Head, Spine).")]
        public string attachBone = string.Empty;

        [Min(0.1f)]
        public float minScale = 0.25f;

        [Min(0.1f)]
        public float maxScale = 20f;

        [Min(0.05f)]
        public float minDuration = 0.2f;

        [Min(0.1f)]
        public float maxDuration = 20f;

        [Min(0.1f)]
        public float minRadius = 0.25f;

        [Min(0.1f)]
        public float maxRadius = 25f;

        [Header("Gameplay (Optional)")]
        [Tooltip("When enabled, the spawned effect can apply gameplay damage")]
        public bool enableGameplayDamage;

        [Tooltip("When enabled, projectile movement steers toward the selected target")]
        public bool enableHoming;

        [Tooltip("Projectile travel speed in meters per second")]
        [Min(0.1f)]
        public float projectileSpeed = 10f;

        [Tooltip("Maximum steering rotation speed (degrees/second) for homing projectiles")]
        [Min(0f)]
        public float homingTurnRateDegrees = 240f;

        [Tooltip("Damage applied on impact")]
        [Min(0f)]
        public float damageAmount = 10f;

        [Tooltip("Impact overlap radius used for damage application")]
        [Min(0.1f)]
        public float damageRadius = 1f;

        [Tooltip("If true, damage is applied only to objects recognized as player actors")]
        public bool affectPlayerOnly = true;

        [Tooltip("Damage type label used for logs/telemetry")]
        public string damageType = "effect";

        [Header("Legacy Compatibility")]
        [Tooltip("Alternative tags that also trigger this effect (for backwards compatibility)")]
        public string[] alternativeTags = new string[0];

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(effectTag))
            {
                effectTag = name;
            }

            minScale = Mathf.Clamp(minScale, 0.1f, 100f);
            maxScale = Mathf.Clamp(maxScale, minScale, 200f);
            minDuration = Mathf.Clamp(minDuration, 0.05f, 120f);
            maxDuration = Mathf.Clamp(maxDuration, minDuration, 240f);
            minRadius = Mathf.Clamp(minRadius, 0.1f, 120f);
            maxRadius = Mathf.Clamp(maxRadius, minRadius, 240f);
        }
    }
}
