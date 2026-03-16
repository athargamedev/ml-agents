using System;
using Network_Game.Auth;
using UnityEngine;
using UnityEngine.UIElements;

namespace Network_Game.UI
{
    /// <summary>
    /// Pure visibility controller for HUD panels.
    /// Each panel is a separate GameObject with its own UIDocument.
    /// You control everything in UI Builder - this just shows/hides.
    /// </summary>
    public sealed class ModernHudLayoutManager : MonoBehaviour
    {
        [Header("Panel References")]
        [Tooltip("Drag each panel GameObject here (Login, Profile, Dialogue, Combat, etc.)")]
        [SerializeField]
        private GameObject[] m_Panels;

        [Header("Default Visibility")]
        [SerializeField]
        private bool m_LoginVisible = true;
        [SerializeField]
        private bool m_ProfileVisible;
        [SerializeField]
        private bool m_DialogueVisible = true;
        [SerializeField]
        private bool m_CombatVisible;
        [SerializeField]
        private bool m_FeedbackVisible;

        private void Awake()
        {
            ApplyDefaultVisibility();
        }

        private void ApplyDefaultVisibility()
        {
            if (m_Panels == null) return;

            foreach (var panel in m_Panels)
            {
                if (panel == null) continue;
                panel.SetActive(false);
            }

            bool requireLoginOnly = RequiresLoginOnlyStartup();

            SetPanelVisible("Login", requireLoginOnly || m_LoginVisible);
            SetPanelVisible("Profile", !requireLoginOnly && m_ProfileVisible);
            SetPanelVisible("Dialogue", !requireLoginOnly && m_DialogueVisible);
            SetPanelVisible("Combat", !requireLoginOnly && m_CombatVisible);
            SetPanelVisible("Feedback", !requireLoginOnly && m_FeedbackVisible);
        }

        private static bool RequiresLoginOnlyStartup()
        {
            if (!Application.isPlaying)
            {
                return false;
            }

            LocalPlayerAuthService authService = LocalPlayerAuthService.Instance;
            return authService == null || !authService.HasCurrentPlayer;
        }

        /// <summary>
        /// Show/hide a panel by name. Name matches the GameObject name.
        /// </summary>
        public void SetPanelVisible(string panelName, bool visible)
        {
            if (m_Panels == null) return;

            foreach (var panel in m_Panels)
            {
                if (panel != null && panel.name.Contains(panelName, StringComparison.OrdinalIgnoreCase))
                {
                    panel.SetActive(visible);
                    return;
                }
            }
        }

        /// <summary>
        /// Toggle a panel by name.
        /// </summary>
        public void TogglePanel(string panelName)
        {
            if (m_Panels == null) return;

            foreach (var panel in m_Panels)
            {
                if (panel != null && panel.name.Contains(panelName, StringComparison.OrdinalIgnoreCase))
                {
                    panel.SetActive(!panel.activeSelf);
                    return;
                }
            }
        }

        /// <summary>
        /// Get panel by name.
        /// </summary>
        public GameObject GetPanel(string panelName)
        {
            if (m_Panels == null) return null;

            foreach (var panel in m_Panels)
            {
                if (panel != null && panel.name.Contains(panelName, StringComparison.OrdinalIgnoreCase))
                {
                    return panel;
                }
            }
            return null;
        }
    }
}
