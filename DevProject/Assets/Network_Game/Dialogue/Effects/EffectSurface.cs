using System;
using UnityEngine;

namespace Network_Game.Dialogue.Effects
{
    public enum EffectSurfaceType
    {
        Unspecified = 0,
        Floor = 1,
        Wall = 2,
        Ceiling = 3,
        Water = 4,
        Prop = 5,
        Character = 6,
        Custom = 7,
    }

    /// <summary>
    /// Marks a scene object as a valid gameplay surface for NPC effects.
    /// Provides stable metadata (surface type, material slot, capabilities)
    /// so effect resolution does not rely on fragile name/raycast heuristics.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Network Game/Effects/Effect Surface")]
    public sealed class EffectSurface : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Optional stable id used in logs/network payloads. Defaults to hierarchy path when empty.")]
        private string m_SurfaceId = "";

        [SerializeField]
        private EffectSurfaceType m_SurfaceType = EffectSurfaceType.Unspecified;

        [SerializeField]
        [Min(0)]
        [Tooltip("Default renderer material slot to mutate for surface effects.")]
        private int m_DefaultMaterialSlot = 0;

        [SerializeField]
        [Tooltip("Optional explicit renderers. Falls back to child renderers when empty.")]
        private Renderer[] m_Renderers = Array.Empty<Renderer>();

        [SerializeField]
        [Tooltip("Optional explicit colliders. Falls back to child colliders when empty.")]
        private Collider[] m_Colliders = Array.Empty<Collider>();

        [Header("Capabilities")]
        [SerializeField]
        private bool m_AllowFreeze = true;

        [SerializeField]
        private bool m_AllowBurn = true;

        [SerializeField]
        private bool m_AllowDecals = true;

        public string SurfaceId => string.IsNullOrWhiteSpace(m_SurfaceId) ? BuildHierarchyPath(transform) : m_SurfaceId.Trim();
        public EffectSurfaceType SurfaceType => m_SurfaceType;
        public int DefaultMaterialSlot => Mathf.Max(0, m_DefaultMaterialSlot);
        public bool AllowFreeze => m_AllowFreeze;
        public bool AllowBurn => m_AllowBurn;
        public bool AllowDecals => m_AllowDecals;

        public Renderer GetPrimaryRenderer()
        {
            if (m_Renderers != null)
            {
                for (int i = 0; i < m_Renderers.Length; i++)
                {
                    if (m_Renderers[i] != null)
                    {
                        return m_Renderers[i];
                    }
                }
            }

            return GetComponentInChildren<Renderer>(includeInactive: true);
        }

        public Collider GetPrimaryCollider()
        {
            if (m_Colliders != null)
            {
                for (int i = 0; i < m_Colliders.Length; i++)
                {
                    if (m_Colliders[i] != null)
                    {
                        return m_Colliders[i];
                    }
                }
            }

            return GetComponentInChildren<Collider>(includeInactive: true);
        }

        public bool TryResolveRenderer(Collider hitCollider, out Renderer renderer)
        {
            renderer = null;

            if (hitCollider != null)
            {
                if (m_Renderers != null)
                {
                    for (int i = 0; i < m_Renderers.Length; i++)
                    {
                        Renderer candidate = m_Renderers[i];
                        if (candidate == null)
                        {
                            continue;
                        }

                        if (
                            hitCollider.transform == candidate.transform
                            || hitCollider.transform.IsChildOf(candidate.transform)
                            || candidate.transform.IsChildOf(hitCollider.transform)
                        )
                        {
                            renderer = candidate;
                            return true;
                        }
                    }
                }

                renderer = hitCollider.GetComponent<Renderer>();
                if (renderer == null)
                {
                    renderer = hitCollider.GetComponentInParent<Renderer>();
                }

                if (renderer != null)
                {
                    return true;
                }
            }

            renderer = GetPrimaryRenderer();
            return renderer != null;
        }

        public int ResolveMaterialSlot(Renderer renderer, int requestedSlot = -1)
        {
            if (renderer == null)
            {
                return DefaultMaterialSlot;
            }

            int count = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
            if (count <= 0)
            {
                return 0;
            }

            int slot = requestedSlot >= 0 ? requestedSlot : DefaultMaterialSlot;
            return Mathf.Clamp(slot, 0, count - 1);
        }

        public bool MatchesSurfaceTypeHint(string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                return true;
            }

            string normalized = hint.Trim().ToLowerInvariant();
            if (normalized == "ground")
            {
                normalized = "floor";
            }

            return string.Equals(m_SurfaceType.ToString(), normalized, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
