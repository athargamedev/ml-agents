using Unity.Netcode;
using UnityEngine;

namespace Network_Game.ThirdPersonController
{
    /// <summary>
    /// Syncs a player's face selection across the network and applies the
    /// pre-baked face material to the character's SkinnedMeshRenderer.
    ///
    /// Setup: use Tools > Face Mapper > Auto-Setup Characters in the Editor
    /// to auto-assign this component and populate the material array.
    /// </summary>
    public class PlayerFaceMapper : NetworkBehaviour
    {
        [Header("Renderer")]
        [Tooltip("Auto-found in children if left empty. Must be the body mesh (Ch33_Body).")]
        [SerializeField]
        private SkinnedMeshRenderer m_Renderer;

        [Tooltip("Which material slot on the renderer holds the face/body material.")]
        [SerializeField]
        private int m_FaceMaterialIndex;

        [Tooltip(
            "Fallback base body material used if a selected face material is missing/invalid."
         )]
        [SerializeField]
        private Material m_BaseBodyMaterial;

        [Header("Face Library")]
        [Tooltip("Pre-baked face materials. Populated automatically by the Editor bake tool.")]
        [SerializeField]
        private Material[] m_FaceMaterials;

        [Tooltip("Default face index applied to NPCs or when no selection has been made.")]
        [SerializeField]
        private int m_DefaultFaceIndex;

        // Synced across all clients: which face this player is wearing.
        private readonly NetworkVariable<int> m_FaceIndex = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        public int FaceCount => m_FaceMaterials != null ? m_FaceMaterials.Length : 0;
        public int CurrentFaceIndex => m_FaceIndex.Value;

        public override void OnNetworkSpawn()
        {
            m_FaceIndex.OnValueChanged += OnFaceChanged;

            m_Renderer = ResolveRendererReference(m_Renderer);

            if (FaceCount == 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[{nameof(PlayerFaceMapper)}] No face materials configured on '{name}'."
                );
                return;
            }

            // Owner sets default; remote clients receive via OnValueChanged after spawn.
            if (IsOwner)
            {
                int clampedDefault = ClampToValidFaceIndex(m_DefaultFaceIndex);
                if (clampedDefault >= 0)
                    m_FaceIndex.Value = clampedDefault;

                // Ensure local owner applies the selected face even when value did not change.
                ApplyFaceMaterial(m_FaceIndex.Value);
            }
            else
            {
                ApplyFaceMaterial(ClampToValidFaceIndex(m_FaceIndex.Value));
            }
        }

        public override void OnNetworkDespawn()
        {
            m_FaceIndex.OnValueChanged -= OnFaceChanged;
        }

        /// <summary>
        /// Select a face by index. Only the owning client can call this.
        /// Change is automatically replicated to all observers.
        /// </summary>
        public void SelectFace(int index)
        {
            if (!IsOwner || FaceCount == 0)
                return;

            int clamped = ClampToValidFaceIndex(index);
            if (clamped < 0)
                return;
            if (clamped == m_FaceIndex.Value)
            {
                ApplyFaceMaterial(clamped);
                return;
            }

            m_FaceIndex.Value = clamped;
        }

        /// <summary>Cycle to the next face (wraps around). Owner only.</summary>
        public void CycleNext()
        {
            if (!IsOwner || FaceCount == 0)
                return;
            SelectFace((m_FaceIndex.Value + 1) % FaceCount);
        }

        private void OnFaceChanged(int _, int next) =>
            ApplyFaceMaterial(ClampToValidFaceIndex(next));

        private void ApplyFaceMaterial(int index)
        {
            m_Renderer = ResolveRendererReference(m_Renderer);

            if (m_Renderer == null || m_FaceMaterials == null || m_FaceMaterials.Length == 0)
                return;

            int clamped = ClampToValidFaceIndex(index);
            if (clamped < 0)
                return;

            var mats = m_Renderer.sharedMaterials;
            if (m_FaceMaterialIndex < 0 || m_FaceMaterialIndex >= mats.Length)
                return;

            Material target = ResolveTargetMaterial(clamped);
            if (target == null)
                return;
            if (mats[m_FaceMaterialIndex] == target)
                return;

            mats[m_FaceMaterialIndex] = target;
            m_Renderer.sharedMaterials = mats;
        }

        // ── Non-networked fallback (single-player / editor preview) ────────────
        private void Start()
        {
            if (IsSpawned)
                return; // handled by OnNetworkSpawn
            m_Renderer = ResolveRendererReference(m_Renderer);
            ApplyFaceMaterial(ClampToValidFaceIndex(m_DefaultFaceIndex));
        }

        private int ClampToValidFaceIndex(int index)
        {
            return FaceCount > 0 ? Mathf.Clamp(index, 0, FaceCount - 1) : -1;
        }

        private Material ResolveTargetMaterial(int index)
        {
            Material candidate =
                (m_FaceMaterials != null && index >= 0 && index < m_FaceMaterials.Length)
                ? m_FaceMaterials[index]
                : null;

            if (IsMaterialUsable(candidate))
                return candidate;

            if (m_BaseBodyMaterial == null && m_Renderer != null)
            {
                var shared = m_Renderer.sharedMaterials;
                if (m_FaceMaterialIndex >= 0 && m_FaceMaterialIndex < shared.Length)
                    m_BaseBodyMaterial = shared[m_FaceMaterialIndex];
            }

            return IsMaterialUsable(m_BaseBodyMaterial) ? m_BaseBodyMaterial : null;
        }

        private static bool IsMaterialUsable(Material mat)
        {
            return mat != null && mat.shader != null && mat.shader.isSupported;
        }

        private SkinnedMeshRenderer ResolveRendererReference(SkinnedMeshRenderer current)
        {
            if (IsRendererCandidateUsable(current))
                return current;

            SkinnedMeshRenderer resolved = FindBodyRenderer();
            return IsRendererCandidateUsable(resolved) ? resolved : current;
        }

        private static bool IsRendererCandidateUsable(SkinnedMeshRenderer smr)
        {
            return smr != null && smr.gameObject != null && smr.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Finds the SkinnedMeshRenderer whose GameObject name contains "body".
        /// Targets Ch33_Body (face+body UV atlas) rather than Ch33_Belt/Hair/etc.
        /// which GetComponentInChildren would grab alphabetically first.
        /// </summary>
        private SkinnedMeshRenderer FindBodyRenderer()
        {
            var all = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in all)
                if (
                    IsRendererCandidateUsable(smr)
                    && smr.gameObject.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase)
                    >= 0
                )
                    return smr;

            foreach (var smr in all)
                if (
                    smr.gameObject.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase)
                    >= 0
                )
                    return smr;

            foreach (var smr in all)
                if (IsRendererCandidateUsable(smr))
                    return smr;

            return all.Length > 0 ? all[0] : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_FaceMaterialIndex < 0)
                m_FaceMaterialIndex = 0;
            if (m_DefaultFaceIndex < 0)
                m_DefaultFaceIndex = 0;
            if (m_BaseBodyMaterial == null && m_Renderer != null)
            {
                var shared = m_Renderer.sharedMaterials;
                if (m_FaceMaterialIndex >= 0 && m_FaceMaterialIndex < shared.Length)
                    m_BaseBodyMaterial = shared[m_FaceMaterialIndex];
            }
        }

#endif
    }
}
