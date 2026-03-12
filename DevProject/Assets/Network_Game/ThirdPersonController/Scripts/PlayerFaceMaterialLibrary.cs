using UnityEngine;

namespace Network_Game.ThirdPersonController
{
    [CreateAssetMenu(
        fileName = "PlayerFaceMaterialLibrary",
        menuName = "Network Game/Face Mapper/Face Material Library"
    )]
    public sealed class PlayerFaceMaterialLibrary : ScriptableObject
    {
        [SerializeField]
        private Material m_BaseBodyMaterial;

        [SerializeField]
        private Material[] m_FaceMaterials;

        [SerializeField]
        private int m_DefaultFaceIndex;

        public Material BaseBodyMaterial => m_BaseBodyMaterial;
        public int FaceCount => m_FaceMaterials != null ? m_FaceMaterials.Length : 0;
        public int DefaultFaceIndex => FaceCount > 0 ? Mathf.Clamp(m_DefaultFaceIndex, 0, FaceCount - 1) : 0;

        public Material[] FaceMaterials => m_FaceMaterials ?? new Material[0];

        public Material GetFaceMaterial(int index)
        {
            if (m_FaceMaterials == null || index < 0 || index >= m_FaceMaterials.Length)
                return null;

            return m_FaceMaterials[index];
        }

#if UNITY_EDITOR
        public void SetContents(Material baseBodyMaterial, Material[] faceMaterials, int defaultFaceIndex)
        {
            m_BaseBodyMaterial = baseBodyMaterial;
            m_FaceMaterials = faceMaterials ?? new Material[0];
            m_DefaultFaceIndex = FaceCount > 0
                ? Mathf.Clamp(defaultFaceIndex, 0, FaceCount - 1)
                : 0;
        }
#endif
    }
}
