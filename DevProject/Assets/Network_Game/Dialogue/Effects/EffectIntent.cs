using UnityEngine;

namespace Network_Game.Dialogue.Effects
{
    /// <summary>
    /// Typed effect intent produced by the parser.
    /// Contains resolved EffectDefinition reference and parsed parameters.
    /// </summary>
    public class EffectIntent
    {
        /// <summary>
        /// Resolved effect definition from the catalog (null if unknown).
        /// </summary>
        public EffectDefinition definition;

        /// <summary>
        /// Raw tag name as emitted by the LLM.
        /// </summary>
        public string rawTagName;

        /// <summary>
        /// Parsed target (e.g., "Player", object name).
        /// </summary>
        public string target;

        /// <summary>
        /// Optional anchor hint (for example "ground", "player", object name).
        /// </summary>
        public string anchor;

        /// <summary>
        /// Optional collision policy hint ("strict", "relaxed", "allow_overlap").
        /// </summary>
        public string collisionPolicy;

        /// <summary>
        /// Optional ground snap override.
        /// </summary>
        public bool? groundSnap;

        /// <summary>
        /// Optional line-of-sight requirement override.
        /// </summary>
        public bool? requireLineOfSight;

        /// <summary>
        /// Optional placement type hint ("projectile", "area", "attached", "ambient").
        /// </summary>
        public string placementType;

        /// <summary>
        /// Scale multiplier (1.0 = default).
        /// </summary>
        public float scale = 1f;

        /// <summary>
        /// Duration in seconds (0 = use definition default).
        /// </summary>
        public float duration = 0f;

        /// <summary>
        /// Intensity multiplier (1.0 = default).
        /// </summary>
        public float intensity = 1f;

        /// <summary>
        /// Optional explicit radius override for gameplay AoE.
        /// 0 means "use definition default".
        /// </summary>
        public float radius = 0f;

        /// <summary>
        /// Optional explicit projectile speed override.
        /// 0 means "use definition default".
        /// </summary>
        public float speed = 0f;

        /// <summary>
        /// Emotional tone keyword emitted by the LLM (e.g. "epic", "peaceful", "threatening").
        /// Empty means no emotion was specified; heuristic extractor is used as fallback.
        /// </summary>
        public string emotion = string.Empty;

        /// <summary>
        /// Damage multiplier relative to base (emitted by LLM as "damage=1.5").
        /// 0 means use profile default.
        /// </summary>
        public float damage = 0f;

        /// <summary>
        /// Color override (white = use definition default).
        /// </summary>
        public Color color = Color.white;

        /// <summary>
        /// Whether this intent was successfully resolved against the catalog.
        /// </summary>
        public bool isValid => definition != null;

        /// <summary>
        /// Backward-compatible name property (same as rawTagName).
        /// </summary>
        public string Name => rawTagName;

        /// <summary>
        /// Backward-compatible Target property.
        /// </summary>
        public string Target => target;

        /// <summary>
        /// Optional anchor hint (legacy-friendly accessor).
        /// </summary>
        public string Anchor => anchor;

        /// <summary>
        /// Optional collision policy hint (legacy-friendly accessor).
        /// </summary>
        public string CollisionPolicy => collisionPolicy;

        /// <summary>
        /// Optional ground snap override (legacy-friendly accessor).
        /// </summary>
        public bool? GroundSnap => groundSnap;

        /// <summary>
        /// Optional line-of-sight override (legacy-friendly accessor).
        /// </summary>
        public bool? RequireLineOfSight => requireLineOfSight;

        /// <summary>
        /// Optional placement type hint (legacy-friendly accessor).
        /// </summary>
        public string PlacementType => placementType;

        /// <summary>
        /// Backward-compatible Duration (nullable, for legacy code).
        /// </summary>
        public float? Duration => duration > 0 ? duration : null;

        /// <summary>
        /// Backward-compatible Scale (nullable, for legacy code).
        /// </summary>
        public float? Scale => scale > 0 && scale != 1f ? scale : null;

        /// <summary>
        /// Backward-compatible Intensity (nullable, for legacy code).
        /// </summary>
        public float? Intensity => intensity > 0 && intensity != 1f ? intensity : null;

        /// <summary>
        /// Backward-compatible ColorOverride string.
        /// </summary>
        public string ColorOverride =>
            color != Color.white && color != default ? ColorToString(color) : null;

        /// <summary>
        /// Backward-compatible Radius (nullable, for legacy code).
        /// </summary>
        public float? Radius => radius > 0 ? radius : null;

        /// <summary>
        /// Backward-compatible Speed (nullable, for legacy code).
        /// </summary>
        public float? Speed => speed > 0 ? speed : null;

        /// <summary>
        /// Get effective scale (fallback to definition default).
        /// </summary>
        public float GetEffectiveScale() => scale > 0 ? scale : (definition?.defaultScale ?? 1f);

        /// <summary>
        /// Get effective duration (fallback to definition default).
        /// </summary>
        public float GetEffectiveDuration() =>
            duration > 0 ? duration : (definition?.defaultDuration ?? 4f);

        /// <summary>
        /// Get effective color (fallback to definition default).
        /// </summary>
        public Color GetEffectiveColor() =>
            color != Color.white && color != default
                ? color
                : (definition?.defaultColor ?? Color.white);

        /// <summary>
        /// Get effective projectile speed (fallback to definition default).
        /// </summary>
        public float GetEffectiveSpeed() => speed > 0 ? speed : (definition?.projectileSpeed ?? 0f);

        /// <summary>
        /// Get effective damage radius (fallback to definition default).
        /// </summary>
        public float GetEffectiveRadius() => radius > 0 ? radius : (definition?.damageRadius ?? 0f);

        /// <summary>
        /// Convert color to hex string for backward compatibility.
        /// </summary>
        private static string ColorToString(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }
    }
}
