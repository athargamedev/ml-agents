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
        [SerializeField] private SkinnedMeshRenderer m_Renderer;

        [Tooltip("Which material slot on the renderer holds the face/body material.")]
        [SerializeField] private int m_FaceMaterialIndex;

        [Header("Face Library")]
        [Tooltip("Pre-baked face materials. Populated automatically by the Editor bake tool.")]
        [SerializeField] private Material[] m_FaceMaterials;

        [Tooltip("Default face index applied to NPCs or when no selection has been made.")]
        [SerializeField] private int m_DefaultFaceIndex;

        // Synced across all clients: which face this player is wearing.
        private readonly NetworkVariable<int> m_FaceIndex = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        public int FaceCount => m_FaceMaterials != null ? m_FaceMaterials.Length : 0;
        public int CurrentFaceIndex => m_FaceIndex.Value;

        public override void OnNetworkSpawn()
        {
            m_FaceIndex.OnValueChanged += OnFaceChanged;

            if (m_Renderer == null)
                m_Renderer = FindBodyRenderer();

            // Owner sets default; remote clients receive via OnValueChanged after spawn.
            if (IsOwner)
                m_FaceIndex.Value = Mathf.Clamp(m_DefaultFaceIndex, 0, FaceCount - 1);
            else
                ApplyFaceMaterial(m_FaceIndex.Value);
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
            if (!IsOwner) return;
            m_FaceIndex.Value = Mathf.Clamp(index, 0, FaceCount - 1);
        }

        /// <summary>Cycle to the next face (wraps around). Owner only.</summary>
        public void CycleNext()
        {
            if (!IsOwner || FaceCount == 0) return;
            SelectFace((m_FaceIndex.Value + 1) % FaceCount);
        }

        private void OnFaceChanged(int _, int next) => ApplyFaceMaterial(next);

        private void ApplyFaceMaterial(int index)
        {
            if (m_Renderer == null || m_FaceMaterials == null) return;
            if (index < 0 || index >= m_FaceMaterials.Length) return;
            if (m_FaceMaterials[index] == null) return;

            // SkinnedMeshRenderer.materials creates per-instance copies automatically.
            var mats = m_Renderer.materials;
            if (m_FaceMaterialIndex >= mats.Length) return;

            mats[m_FaceMaterialIndex] = m_FaceMaterials[index];
            m_Renderer.materials = mats;
        }

        // ── Non-networked fallback (single-player / editor preview) ────────────
        private void Start()
        {
            if (IsSpawned) return; // handled by OnNetworkSpawn
            if (m_Renderer == null) m_Renderer = FindBodyRenderer();
            ApplyFaceMaterial(m_DefaultFaceIndex);
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
                if (smr.gameObject.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return smr;
            return all.Length > 0 ? all[0] : null;
        }
    }
}
