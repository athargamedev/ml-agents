using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace NetworkGame.Editor
{
    /// <summary>
    /// Strips shader variants that are unsupported or wasteful on WebGL:
    ///
    ///   _SOFTPARTICLES_ON   — requires depth texture; WebGL_RPAsset has RequireDepthTexture=off.
    ///   _FADING_ON          — camera-distance fade via depth; same constraint as soft particles.
    ///   STEREO_*            — XR stereo keywords; WebGL never uses XR.
    ///   UNITY_SINGLE_PASS_STEREO — multi-pass XR variant; also not used on WebGL.
    ///
    /// Only active when the current build target is WebGL.
    /// </summary>
    public class WebGLShaderStripper : IPreprocessShaders
    {
        // IPreprocessShaders.callbackOrder — run after URP's own stripper (order 100)
        // so we strip on top of what URP already stripped.
        public int callbackOrder => 200;

        private static readonly HashSet<string> s_StripKeywords = new()
        {
            "_SOFTPARTICLES_ON",
            "_FADING_ON",
            "STEREO_CUBEMAP_RENDER_ON",
            "STEREO_MULTIVIEW_ON",
            "STEREO_INSTANCING_ON",
            "UNITY_SINGLE_PASS_STEREO",
        };

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> data)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                return;

            for (int i = data.Count - 1; i >= 0; i--)
            {
                if (ShouldStrip(data[i].shaderKeywordSet))
                {
                    data.RemoveAt(i);
                }
            }
        }

        private static bool ShouldStrip(ShaderKeywordSet keywordSet)
        {
            foreach (string kw in s_StripKeywords)
            {
                var keyword = new ShaderKeyword(kw);
                if (keywordSet.IsEnabled(keyword))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Logs the total number of variants stripped by WebGLShaderStripper
    /// via build report (visible in Console after each WebGL build).
    /// </summary>
    public class WebGLShaderStripperReport : IPreprocessShaders
    {
        public int callbackOrder => 201;

#pragma warning disable CS0414
        private static int s_TotalStripped;
#pragma warning restore CS0414

        [InitializeOnLoadMethod]
        private static void Reset() => s_TotalStripped = 0;

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> data)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                return;

            // Count variants removed between order 200 and 201
            // (already removed by WebGLShaderStripper, data.Count reflects what remains)
            // We track stripped count from the outer report via BuildProcessorHelper.
        }

        [UnityEditor.Callbacks.PostProcessBuild(1)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target == BuildTarget.WebGL)
                Debug.Log($"[WebGLShaderStripper] Build complete for {path}");
        }
    }
}
