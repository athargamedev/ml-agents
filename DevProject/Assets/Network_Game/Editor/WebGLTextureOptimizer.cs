using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NetworkGame.Editor
{
    /// <summary>
    /// Adds WebGL-specific platform overrides to textures based on path category.
    /// Caps: particles ≤ 512, environment ≤ 1024, UI ≤ 512, characters ≤ 2048.
    /// Lightmaps and reflection probes are skipped (baked data).
    /// Menu: Tools > Performance > WebGL Texture Optimizer
    /// </summary>
    public class WebGLTextureOptimizer : EditorWindow
    {
        private enum Category
        {
            Character,
            Environment,
            Particle,
            UI,
            Lightmap,
            ReflectionProbe,
            Other,
        }

        private struct TextureReport
        {
            public string path;
            public Category category;
            public int currentDefault; // DefaultTexturePlatform maxTextureSize
            public int recommendedWebGL; // target cap for WebGL
            public bool mipmapOff; // mipmap disabled on non-sprite 3D texture
            public bool hasWebGLOverride; // already has a WebGL platform block
            public bool needsFix; // recommendedWebGL < currentDefault || mipmapOff
        }

        // Path patterns → category
        private static readonly (string keyword, Category cat)[] s_PathRules =
        {
            ("Lightmap", Category.Lightmap),
            ("ReflectionProbe", Category.ReflectionProbe),
            ("Character", Category.Character),
            ("ThirdPerson", Category.Character),
            ("Armature", Category.Character),
            ("Ch33", Category.Character),
            ("ParticlePack", Category.Particle),
            ("Particle", Category.Particle),
            ("VFX", Category.Particle),
            ("UI", Category.UI),
            ("Theme", Category.UI),
            ("Sprites", Category.UI),
            ("Environment", Category.Environment),
            ("Img_for_Walls", Category.Environment),
            ("Textures", Category.Environment),
        };

        private static readonly Dictionary<Category, int> s_MaxSize = new()
        {
            { Category.Character, 2048 },
            { Category.Environment, 1024 },
            { Category.Particle, 512 },
            { Category.UI, 512 },
            { Category.Other, 1024 },
            // Lightmap and ReflectionProbe → skip (never modify)
        };

        private List<TextureReport> m_Reports = new();
        private Vector2 m_Scroll;
        private int m_TotalScanned;
        private bool m_ShowSkipped;

        [MenuItem("Tools/Performance/WebGL Texture Optimizer")]
        public static void Open() =>
            GetWindow<WebGLTextureOptimizer>("WebGL Texture Optimizer").minSize = new Vector2(
                700,
                420
            );

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("WebGL Texture Optimizer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Adds WebGL platform overrides (size cap + mipmap) without affecting PC/mobile imports.",
                EditorStyles.miniLabel
            );
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan All Textures", GUILayout.Height(26), GUILayout.Width(160)))
                RunScan();
            m_ShowSkipped = EditorGUILayout.ToggleLeft(
                "Show skipped (lightmaps / probes)",
                m_ShowSkipped
            );
            EditorGUILayout.EndHorizontal();

            if (m_TotalScanned == 0)
                return;

            var issues = m_Reports.Where(r => r.needsFix).ToList();
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"Scanned {m_TotalScanned} textures — {issues.Count} need WebGL override",
                issues.Count == 0 ? MessageType.Info : MessageType.Warning
            );

            if (
                issues.Count > 0
                && GUILayout.Button(
                    $"Apply WebGL Overrides to All {issues.Count} Textures",
                    GUILayout.Height(26)
                )
            )
                ApplyAll(issues);

            DrawTable();
        }

        private void RunScan()
        {
            m_Reports.Clear();
            string[] guids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { "Assets/Network_Game" }
            );
            m_TotalScanned = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                m_TotalScanned++;

                Category cat = ClassifyPath(path);

                // Skip baked assets
                if (cat == Category.Lightmap || cat == Category.ReflectionProbe)
                {
                    if (m_ShowSkipped)
                        m_Reports.Add(
                            new TextureReport
                            {
                                path = path,
                                category = cat,
                                needsFix = false,
                            }
                        );
                    continue;
                }

                int defaultMax = GetDefaultMaxSize(importer);
                int recommended = s_MaxSize.TryGetValue(cat, out int cap) ? cap : 1024;
                bool hasWebGL = HasWebGLOverride(importer);

                // Mipmap issue: off on a non-sprite, non-lightmap 3D texture
                bool mipmapIssue =
                    !importer.mipmapEnabled
                    && importer.textureType != TextureImporterType.Sprite
                    && importer.textureType != TextureImporterType.GUI
                    && cat != Category.UI;

                bool sizeFix = recommended < defaultMax && !hasWebGL;

                m_Reports.Add(
                    new TextureReport
                    {
                        path = path,
                        category = cat,
                        currentDefault = defaultMax,
                        recommendedWebGL = recommended,
                        mipmapOff = mipmapIssue,
                        hasWebGLOverride = hasWebGL,
                        needsFix = sizeFix || mipmapIssue,
                    }
                );
            }

            m_Reports.Sort(
                (a, b) =>
                {
                    int cmp = b.needsFix.CompareTo(a.needsFix);
                    return cmp != 0 ? cmp : a.category.CompareTo(b.category);
                }
            );
            Repaint();
        }

        private void ApplyAll(List<TextureReport> issues)
        {
            int count = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var report in issues)
                {
                    var importer = AssetImporter.GetAtPath(report.path) as TextureImporter;
                    if (importer == null)
                        continue;

                    bool changed = false;

                    // Size cap via WebGL platform override
                    if (report.recommendedWebGL < report.currentDefault && !report.hasWebGLOverride)
                    {
                        var settings = new TextureImporterPlatformSettings
                        {
                            name = "WebGL",
                            overridden = true,
                            maxTextureSize = report.recommendedWebGL,
                            format = TextureImporterFormat.Automatic,
                            textureCompression = TextureImporterCompression.Compressed,
                            compressionQuality = 50,
                            crunchedCompression = false,
                            allowsAlphaSplitting = false,
                        };
                        importer.SetPlatformTextureSettings(settings);
                        changed = true;
                    }

                    // Enable mipmaps on 3D scene textures that have them off
                    if (report.mipmapOff)
                    {
                        importer.mipmapEnabled = true;
                        changed = true;
                    }

                    if (changed)
                    {
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                        count++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[WebGLTextureOptimizer] Applied WebGL overrides to {count} textures.");
            RunScan();
        }

        private void DrawTable()
        {
            EditorGUILayout.Space(4);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var r in m_Reports)
            {
                if (!m_ShowSkipped && !r.needsFix)
                    continue;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Category chip
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = CategoryColor(r.category);
                EditorGUILayout.LabelField(
                    r.category.ToString(),
                    EditorStyles.miniButton,
                    GUILayout.Width(100)
                );
                GUI.backgroundColor = prevBg;

                // Current / recommended sizes
                string sizeLabel =
                    r.needsFix && r.recommendedWebGL < r.currentDefault
                        ? $"{r.currentDefault} → {r.recommendedWebGL}"
                        : $"{r.currentDefault} ✓";
                EditorGUILayout.LabelField(sizeLabel, GUILayout.Width(90));

                // Mip flag
                string mipLabel = r.mipmapOff ? "mip OFF!" : "";
                var prevColor = GUI.contentColor;
                GUI.contentColor = r.mipmapOff ? Color.red : Color.white;
                EditorGUILayout.LabelField(mipLabel, GUILayout.Width(62));
                GUI.contentColor = prevColor;

                // Has override already?
                EditorGUILayout.LabelField(
                    r.hasWebGLOverride ? "✓ override" : "",
                    GUILayout.Width(72)
                );

                // Short path
                string shortPath = r.path.Replace("Assets/Network_Game/", "");
                EditorGUILayout.LabelField(
                    Path.GetFileName(shortPath),
                    EditorStyles.miniLabel,
                    GUILayout.Width(200)
                );

                if (r.needsFix && GUILayout.Button("Fix", GUILayout.Width(38)))
                    ApplyAll(new List<TextureReport> { r });

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private static Category ClassifyPath(string path)
        {
            string lower = path.ToLowerInvariant();
            foreach (var (kw, cat) in s_PathRules)
                if (lower.Contains(kw.ToLowerInvariant()))
                    return cat;
            return Category.Other;
        }

        private static int GetDefaultMaxSize(TextureImporter importer)
        {
            var def = importer.GetPlatformTextureSettings("DefaultTexturePlatform");
            return def.maxTextureSize > 0 ? def.maxTextureSize : importer.maxTextureSize;
        }

        private static bool HasWebGLOverride(TextureImporter importer)
        {
            var settings = importer.GetPlatformTextureSettings("WebGL");
            return settings.overridden;
        }

        private static Color CategoryColor(Category cat) =>
            cat switch
            {
                Category.Character => new Color(0.4f, 0.7f, 1f),
                Category.Environment => new Color(0.5f, 0.9f, 0.5f),
                Category.Particle => new Color(1f, 0.7f, 0.3f),
                Category.UI => new Color(0.9f, 0.5f, 0.9f),
                Category.Lightmap => new Color(0.6f, 0.6f, 0.6f),
                Category.ReflectionProbe => new Color(0.6f, 0.6f, 0.6f),
                _ => Color.white,
            };
    }
}
