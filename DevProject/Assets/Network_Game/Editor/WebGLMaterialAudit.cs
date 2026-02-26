using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NetworkGame.Editor
{
    /// <summary>
    /// Scans all project materials and reports shaders incompatible with WebGL / URP.
    /// Menu: Tools > Performance > WebGL Material Audit
    /// </summary>
    public class WebGLMaterialAudit : EditorWindow
    {
        private enum IssueKind
        {
            HdrpShader, // HDRP/* — HDRP not installed
            LegacyStandard, // Standard / Standard (Specular) — needs URP/Lit
            LegacyParticle, // Particles/Standard Surface or Standard Unlit
            LegacyDiffuse, // Diffuse / Bumped Diffuse etc.
            SoftParticles, // _SOFTPARTICLES_ON keyword — requires depth texture
            MissingShader, // null or error shader
        }

        private struct Issue
        {
            public Material mat;
            public string path;
            public IssueKind kind;
            public string detail;
        }

        // Known legacy built-in fileIDs (guid 0000000000000000f000000000000000)
        // Obtained from Unity internal shader list; fileID < 1000 = built-in
        private static readonly HashSet<string> s_LegacyShaderNames = new HashSet<string>
        {
            "Standard",
            "Standard (Specular setup)",
            "Diffuse",
            "Bumped Diffuse",
            "Diffuse Detail",
            "Particles/Standard Surface",
            "Particles/Standard Unlit",
            "Particles/Additive",
            "Particles/Alpha Blended",
            "Particles/Additive (Soft)",
            "Particles/Alpha Blended Premultiply",
            "Legacy Shaders/Diffuse",
            "Legacy Shaders/VertexLit",
        };

        private List<Issue> m_Issues = new List<Issue>();
        private Vector2 m_Scroll;
        private bool m_IncludeExamples = false;
#pragma warning disable CS0414
        private bool m_ShowFixed = false;
#pragma warning restore CS0414
        private int m_TotalScanned;

        [MenuItem("Tools/Performance/WebGL Material Audit")]
        public static void Open()
        {
            GetWindow<WebGLMaterialAudit>("WebGL Material Audit").minSize = new Vector2(620, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("WebGL Material Audit", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Finds shaders incompatible with WebGL / URP builds.",
                EditorStyles.miniLabel
            );
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            m_IncludeExamples = EditorGUILayout.ToggleLeft(
                "Include Examples & Extras folders",
                m_IncludeExamples,
                GUILayout.Width(250)
            );
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Scan All Materials", GUILayout.Height(28)))
                RunScan();

            if (m_TotalScanned > 0)
            {
                EditorGUILayout.Space(4);
                string summary =
                    m_Issues.Count == 0
                        ? $"✓ No issues in {m_TotalScanned} materials"
                        : $"⚠ {m_Issues.Count} issue(s) in {m_TotalScanned} materials";
                EditorGUILayout.HelpBox(
                    summary,
                    m_Issues.Count == 0 ? MessageType.Info : MessageType.Warning
                );

                if (m_Issues.Count > 0)
                {
                    DrawIssueTable();
                    DrawAutoFixSection();
                }
            }
        }

        private void RunScan()
        {
            m_Issues.Clear();
            string[] guids = AssetDatabase.FindAssets("t:Material");
            m_TotalScanned = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (
                    !m_IncludeExamples
                    && (path.Contains("Examples & Extras") || path.Contains("EffectExamples"))
                )
                    continue;

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                    continue;
                m_TotalScanned++;

                if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                {
                    m_Issues.Add(
                        new Issue
                        {
                            mat = mat,
                            path = path,
                            kind = IssueKind.MissingShader,
                            detail = "null/error shader",
                        }
                    );
                    continue;
                }

                string sName = mat.shader.name;

                if (sName.StartsWith("HDRenderPipeline/") || sName.StartsWith("HDRP/"))
                {
                    m_Issues.Add(
                        new Issue
                        {
                            mat = mat,
                            path = path,
                            kind = IssueKind.HdrpShader,
                            detail = sName,
                        }
                    );
                    continue;
                }

                if (s_LegacyShaderNames.Contains(sName))
                {
                    IssueKind kind =
                        sName.StartsWith("Particles/") || sName.StartsWith("Legacy")
                            ? IssueKind.LegacyParticle
                            : IssueKind.LegacyStandard;
                    if (sName == "Diffuse" || sName.Contains("Diffuse"))
                        kind = IssueKind.LegacyDiffuse;
                    m_Issues.Add(
                        new Issue
                        {
                            mat = mat,
                            path = path,
                            kind = kind,
                            detail = sName,
                        }
                    );
                    continue;
                }

                if (mat.IsKeywordEnabled("_SOFTPARTICLES_ON"))
                {
                    m_Issues.Add(
                        new Issue
                        {
                            mat = mat,
                            path = path,
                            kind = IssueKind.SoftParticles,
                            detail = sName,
                        }
                    );
                }
            }

            m_Issues.Sort((a, b) => a.kind.CompareTo(b.kind));
            Repaint();
        }

        private void DrawIssueTable()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Issues", EditorStyles.boldLabel);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll, GUILayout.MaxHeight(260));
            foreach (var issue in m_Issues)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Severity indicator
                Color labelColor = issue.kind switch
                {
                    IssueKind.HdrpShader => Color.red,
                    IssueKind.MissingShader => Color.red,
                    IssueKind.LegacyStandard => new Color(1f, 0.5f, 0f),
                    IssueKind.LegacyParticle => new Color(1f, 0.5f, 0f),
                    IssueKind.LegacyDiffuse => new Color(1f, 0.7f, 0f),
                    _ => Color.yellow,
                };
                var prevColor = GUI.contentColor;
                GUI.contentColor = labelColor;
                EditorGUILayout.LabelField(issue.kind.ToString(), GUILayout.Width(130));
                GUI.contentColor = prevColor;

                EditorGUILayout.LabelField(issue.detail, GUILayout.Width(200));

                if (GUILayout.Button("Select", GUILayout.Width(55)))
                    Selection.activeObject = issue.mat;

                string shortPath = issue.path.Replace("Assets/", "");
                EditorGUILayout.LabelField(shortPath, EditorStyles.miniLabel);

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawAutoFixSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Auto-Fix", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Auto-fix replaces shader references only. Review affected materials after fixing.",
                MessageType.Info
            );

            int softParticleCount = m_Issues.Count(i => i.kind == IssueKind.SoftParticles);
            if (softParticleCount > 0)
            {
                if (
                    GUILayout.Button(
                        $"Disable _SOFTPARTICLES_ON on {softParticleCount} material(s)"
                    )
                )
                    FixSoftParticles();
            }

            int legacyCount = m_Issues.Count(i =>
                i.kind == IssueKind.LegacyStandard || i.kind == IssueKind.LegacyDiffuse
            );
            if (legacyCount > 0)
            {
                if (
                    GUILayout.Button(
                        $"Replace Standard/Diffuse → URP/Lit on {legacyCount} material(s)"
                    )
                )
                    FixLegacyStandard();
            }

            int particleCount = m_Issues.Count(i => i.kind == IssueKind.LegacyParticle);
            if (particleCount > 0)
            {
                if (
                    GUILayout.Button(
                        $"Replace legacy Particles → Universal Render Pipeline/Particles/Unlit on {particleCount} material(s)"
                    )
                )
                    FixLegacyParticles();
            }
        }

        private void FixSoftParticles()
        {
            foreach (var issue in m_Issues.Where(i => i.kind == IssueKind.SoftParticles))
            {
                issue.mat.DisableKeyword("_SOFTPARTICLES_ON");
                EditorUtility.SetDirty(issue.mat);
            }
            AssetDatabase.SaveAssets();
            RunScan();
        }

        private void FixLegacyStandard()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogError("[WebGLAudit] Cannot find 'Universal Render Pipeline/Lit' shader.");
                return;
            }
            foreach (
                var issue in m_Issues.Where(i =>
                    i.kind == IssueKind.LegacyStandard || i.kind == IssueKind.LegacyDiffuse
                )
            )
            {
                issue.mat.shader = urpLit;
                EditorUtility.SetDirty(issue.mat);
                Debug.Log($"[WebGLAudit] Fixed: {issue.path} → URP/Lit");
            }
            AssetDatabase.SaveAssets();
            RunScan();
        }

        private void FixLegacyParticles()
        {
            Shader urpParticles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (urpParticles == null)
            {
                Debug.LogError(
                    "[WebGLAudit] Cannot find 'Universal Render Pipeline/Particles/Unlit'."
                );
                return;
            }
            foreach (var issue in m_Issues.Where(i => i.kind == IssueKind.LegacyParticle))
            {
                issue.mat.shader = urpParticles;
                EditorUtility.SetDirty(issue.mat);
                Debug.Log($"[WebGLAudit] Fixed: {issue.path} → URP Particles/Unlit");
            }
            AssetDatabase.SaveAssets();
            RunScan();
        }
    }
}
