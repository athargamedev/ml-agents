using UnityEngine;

namespace Network_Game
{
    /// <summary>
    /// Configures per-layer cull distances on the main camera to reduce draw calls.
    /// Attach to the camera rig or a bootstrap object in the scene.
    /// </summary>
    public class PerformanceCullingSetup : MonoBehaviour
    {
        [SerializeField]
        private Camera m_MainCamera;

        [Header("Cull Distances (0 = use camera far clip)")]
        [SerializeField]
        private float m_NpcDistance = 120f;

        [SerializeField]
        private float m_SmallPropsDistance = 40f;

        [SerializeField]
        private float m_VfxDistance = 80f;

        [SerializeField]
        private float m_EnvironmentDistance = 300f;

        [SerializeField]
        private float m_TerrainDistance = 500f;

        [SerializeField]
        private float m_WorldUiDistance = 30f;

        [SerializeField]
        private float m_InteractablesDistance = 60f;

        private void Start()
        {
            if (m_MainCamera == null)
                m_MainCamera = Camera.main;
            if (m_MainCamera == null)
                return;

            float[] distances = new float[32];

            SetLayerDistance(distances, "Players", 0f);
            SetLayerDistance(distances, "NPC", m_NpcDistance);
            SetLayerDistance(distances, "SmallProps", m_SmallPropsDistance);
            SetLayerDistance(distances, "VFX", m_VfxDistance);
            SetLayerDistance(distances, "Environment", m_EnvironmentDistance);
            SetLayerDistance(distances, "Terrain", m_TerrainDistance);
            SetLayerDistance(distances, "WorldUI", m_WorldUiDistance);
            SetLayerDistance(distances, "Interactables", m_InteractablesDistance);
            SetLayerDistance(distances, "AI_Infrastructure", 0f);

            m_MainCamera.layerCullDistances = distances;

            // layerCullSpherical is only supported on the built-in renderer;
            // URP/HDRP use frustum-based culling with layerCullDistances automatically.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
            {
                m_MainCamera.layerCullSpherical = true;
            }
        }

        private static void SetLayerDistance(float[] distances, string layerName, float distance)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0 && layer < distances.Length)
                distances[layer] = distance;
        }
    }
}
