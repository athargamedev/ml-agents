using UnityEngine;

namespace Network_Game.UI
{
    [CreateAssetMenu(
        fileName = "ModernHudLayoutProfile",
        menuName = "Network Game/UI/Modern HUD Layout Profile"
     )]
    public sealed class ModernHudLayoutProfile : ScriptableObject
    {
        [Header("Outer Frame")]
        [Range(0f, 0.05f)]
        public float OuterMarginPercent = 0.0125f;

        [Range(0f, 0.05f)]
        public float DockGapPercent = 0.008f;

        [Header("Top Bar")]
        [Range(0f, 0.05f)]
        public float TopBarTopPercent = 0.008f;

        [Range(0.08f, 0.35f)]
        public float TopBarReservedHeightPercent = 0.18f;

        [Min(72f)]
        public float TopBarMinHeightPx = 118f;

        [Header("Side Docks")]
        [Range(0.15f, 0.45f)]
        public float LeftDockWidthPercent = 0.28f;

        [Range(0.18f, 0.50f)]
        public float RightDockWidthPercent = 0.36f;

        [Min(180f)]
        public float MinDockWidthPx = 260f;

        [Header("Bottom Bar")]
        [Range(0.12f, 0.40f)]
        public float BottomBarHeightPercent = 0.24f;

        [Header("Feedback Bar Internal Rows")]
        [Range(0.15f, 0.70f)]
        public float FeedbackSummaryRowPercent = 0.34f;

        [Range(0.10f, 0.70f)]
        public float FeedbackActionsRowPercent = 0.31f;

        [Range(0.10f, 0.70f)]
        public float FeedbackNotesRowPercent = 0.35f;

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
    }
}
