using System;
using UnityEngine;

namespace Network_Game.Dialogue
{
    public enum DialogueSemanticRole
    {
        Unknown = 0,
        Player = 1,
        Npc = 2,
        Boss = 3,
        Floor = 4,
        Terrain = 5,
        Structure = 6,
        Interactable = 7,
        Objective = 8,
        Hazard = 9,
        Prop = 10,
        Water = 11,
        SpawnPoint = 12,
        Cover = 13,
        VfxAnchor = 14,
    }

    /// <summary>
    /// Semantic identity metadata consumed by dialogue systems and probe snapshots.
    /// Attach this to scene roots/prefabs so LLM prompts can refer to stable roles.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueSemanticTag : MonoBehaviour
    {
        [SerializeField]
        private string m_SemanticId = string.Empty;

        [SerializeField]
        private string m_DisplayName = string.Empty;

        [SerializeField]
        private DialogueSemanticRole m_Role = DialogueSemanticRole.Unknown;

        [SerializeField]
        [Tooltip("Higher values are prioritized in scene snapshots.")]
        private int m_Priority;

        [SerializeField]
        [Tooltip("Include this object in LLM scene context snapshots.")]
        private bool m_IncludeInSceneSnapshots = true;

        [SerializeField]
        [Tooltip("Alternative names the LLM can use for this object.")]
        private string[] m_Aliases = Array.Empty<string>();

        [SerializeField]
        [TextArea(1, 3)]
        [Tooltip("Short usage hint for this object (optional).")]
        private string m_Description = string.Empty;

        public string SemanticId =>
            string.IsNullOrWhiteSpace(m_SemanticId) ? string.Empty : m_SemanticId.Trim();
        public string DisplayName =>
            string.IsNullOrWhiteSpace(m_DisplayName) ? string.Empty : m_DisplayName.Trim();
        public DialogueSemanticRole Role => m_Role;
        public int Priority => m_Priority;
        public bool IncludeInSceneSnapshots => m_IncludeInSceneSnapshots;
        public string Description =>
            string.IsNullOrWhiteSpace(m_Description) ? string.Empty : m_Description.Trim();
        public string[] Aliases => m_Aliases ?? Array.Empty<string>();

        public string ResolveDisplayName(GameObject fallbackObject)
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            return fallbackObject != null ? fallbackObject.name : gameObject.name;
        }

        public string RoleKey => m_Role.ToString().ToLowerInvariant();

        public string[] GetCompactAliases(int max = 3)
        {
            string[] aliases = Aliases;
            if (aliases.Length == 0 || max <= 0)
            {
                return Array.Empty<string>();
            }

            int count = Mathf.Min(max, aliases.Length);
            var result = new string[count];
            int written = 0;
            for (int i = 0; i < aliases.Length && written < count; i++)
            {
                string alias = aliases[i];
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                result[written++] = alias.Trim();
            }

            if (written == 0)
            {
                return Array.Empty<string>();
            }

            if (written == result.Length)
            {
                return result;
            }

            var trimmed = new string[written];
            Array.Copy(result, trimmed, written);
            return trimmed;
        }
    }
}
