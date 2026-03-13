using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
#if UNITY_RENDER_PIPELINE_UNIVERSAL
using UnityEngine.Rendering.Universal;
#endif

namespace Network_Game.Editor
{
    public static class WebGLOptimizationTool
    {
        [MenuItem("Tools/Optimize For WebGL")]
        public static void OptimizeEverything()
        {
            Debug.Log("[WebGL Setup] Starting full project WebGL optimization...");

            OptimizeURPAsset();
            OptimizeQualitySettings();
            SetWebGLBuildSettings();

            Debug.Log("[WebGL Setup] WebGL Optimization complete! Textures can take a long time to compress, so we skipped auto-compressing all of them to save time, but the pipeline is now ready for WebGL builds.");
        }

        private static void OptimizeURPAsset()
        {
            RenderPipelineAsset currentSRP = GraphicsSettings.currentRenderPipeline;
#if UNITY_RENDER_PIPELINE_UNIVERSAL
            if (currentSRP != null && currentSRP is UniversalRenderPipelineAsset urpAsset)
            {
                Debug.Log($"[WebGL Setup] Optimizing URP Asset: {currentSRP.name}");
                SerializedObject so = new SerializedObject(urpAsset);
                
                // Disable unsupported/expensive WebGL features
                SetProp(so, "m_SupportsHDR", false);
                SetProp(so, "m_SupportsCameraOpaqueTexture", false);
                SetProp(so, "m_ColorSpace", 0); // Gamma if needed, but keeping linear is fine if supported
                SetProp(so, "m_ShadowDistance", 30f);
                SetProp(so, "m_ShadowCascadeCount", 1);
                
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(urpAsset);
            }
            else
            {
                Debug.LogWarning("[WebGL Setup] No UniversalRenderPipelineAsset found in GraphicsSettings.");
            }
#else
            Debug.LogWarning(
                "[WebGL Setup] URP package not installed. Skipping URP-specific optimization."
            );
#endif
        }

        private static void SetProp(SerializedObject so, string propName, bool value)
        {
            var prop = so.FindProperty(propName);
            if (prop != null) prop.boolValue = value;
        }
        
        private static void SetProp(SerializedObject so, string propName, float value)
        {
            var prop = so.FindProperty(propName);
            if (prop != null) prop.floatValue = value;
        }
        
        private static void SetProp(SerializedObject so, string propName, int value)
        {
            var prop = so.FindProperty(propName);
            if (prop != null) prop.intValue = value;
        }

        private static void OptimizeQualitySettings()
        {
            Debug.Log("[WebGL Setup] Optimizing Quality Settings...");
            QualitySettings.vSyncCount = 0; // Better for browser framerates
            QualitySettings.globalTextureMipmapLimit = 1; // Half res textures to save memory
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        }

        private static void SetWebGLBuildSettings()
        {
            Debug.Log("[WebGL Setup] Configuring Build Settings for WebGL...");
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None; // Maximum performance
            
            // Note: We are not explicitly calling SwitchActiveBuildTarget here because it
            // will block the editor for a very long time in a large project.
            // You will still need to manually go to Build Settings -> WebGL -> Switch Platform.
        }
    }
}
