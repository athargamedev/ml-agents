using UnityEngine;
using UnityEngine.UIElements;

namespace Network_Game.UI
{
    /// <summary>
    /// Layout profile for the Modern HUD system.
    /// Contains both sizing percentages and USS custom property definitions
    /// for full UI Builder integration.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ModernHudLayoutProfile",
        menuName = "Network Game/UI/Modern HUD Layout Profile"
     )]
    public sealed class ModernHudLayoutProfile : ScriptableObject
    {
        #region Outer Frame

        [Header("Outer Frame")]
        [Range(0f, 0.05f)]
        public float OuterMarginPercent = 0.0125f;

        [Range(0f, 0.05f)]
        public float DockGapPercent = 0.008f;

        #endregion

        #region Top Bar

        [Header("Top Bar")]
        [Range(0f, 0.05f)]
        public float TopBarTopPercent = 0.008f;

        [Range(0.08f, 0.35f)]
        public float TopBarReservedHeightPercent = 0.18f;

        [Min(72f)]
        public float TopBarMinHeightPx = 118f;

        #endregion

        #region Side Docks

        [Header("Side Docks")]
        [Range(0.15f, 0.45f)]
        public float LeftDockWidthPercent = 0.28f;

        [Range(0.18f, 0.50f)]
        public float RightDockWidthPercent = 0.36f;

        [Min(180f)]
        public float MinDockWidthPx = 260f;

        #endregion

        #region Bottom Bar

        [Header("Bottom Bar")]
        [Range(0.12f, 0.40f)]
        public float BottomBarHeightPercent = 0.24f;

        #endregion

        #region Feedback Bar

        [Header("Feedback Bar Internal Rows")]
        [Range(0.15f, 0.70f)]
        public float FeedbackSummaryRowPercent = 0.34f;

        [Range(0.10f, 0.70f)]
        public float FeedbackActionsRowPercent = 0.31f;

        [Range(0.10f, 0.70f)]
        public float FeedbackNotesRowPercent = 0.35f;

        #endregion

        #region USS Custom Properties

        [Header("USS Custom Properties")]
        [Tooltip("USS custom property name for primary accent color")]
        public string AccentColorProperty = "--hud-accent-color";

        [Tooltip("Primary accent color value")]
        public Color AccentColor = new Color(0.2f, 0.6f, 1f, 1f);

        [Tooltip("USS custom property name for background opacity")]
        public string BackgroundOpacityProperty = "--hud-background-opacity";

        [Range(0f, 1f)]
        public float BackgroundOpacity = 0.85f;

        [Tooltip("USS custom property name for font scale")]
        public string FontScaleProperty = "--hud-font-scale";

        [Range(0.5f, 2f)]
        public float FontScale = 1f;

        [Tooltip("USS custom property name for border radius")]
        public string BorderRadiusProperty = "--hud-border-radius";

        [Range(0f, 20f)]
        public float BorderRadius = 10f;

        [Tooltip("USS custom property name for transition duration")]
        public string TransitionDurationProperty = "--hud-transition-duration";

        [Range(0f, 1f)]
        public float TransitionDuration = 0.2f;

        #endregion

        /// <summary>
        /// Gets normalized feedback row weights.
        /// </summary>
        public void GetNormalizedFeedbackRowWeights(
            out float summary,
            out float actions,
            out float notes
        )
        {
            summary = Mathf.Max(0.01f, FeedbackSummaryRowPercent);
            actions = Mathf.Max(0.01f, FeedbackActionsRowPercent);
            notes = Mathf.Max(0.01f, FeedbackNotesRowPercent);

            float total = summary + actions + notes;
            if (total <= 0f)
            {
                summary = 0.34f;
                actions = 0.31f;
                notes = 0.35f;
                return;
            }

            float invTotal = 1f / total;
            summary *= invTotal;
            actions *= invTotal;
            notes *= invTotal;
        }

        /// <summary>
        /// Applies USS custom properties to a VisualElement.
        /// Call this after the VisualElement is created to apply theming.
        /// Note: USS custom properties should be defined in USS files using --property-name syntax.
        /// This method is a placeholder for runtime theming if needed.
        /// </summary>
        [System.Obsolete("USS custom properties should be defined in USS files. This method is deprecated.")]
        public void ApplyUssCustomProperties(VisualElement element)
        {
            // USS custom properties are defined in .uss files with --property-name syntax
            // e.g., --hud-accent-color: rgb(50, 150, 255);
            // To apply at runtime, use element.style.backgroundColor or other standard properties
            // or modify the USS file directly
        }
    }
}
