using System.Text;
using Network_Game.Dialogue.Effects;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Dialogue.Editor
{
    /// <summary>
    /// Designer-friendly editor window for creating and previewing effect tags.
    /// </summary>
    public class EffectEditorWindow : EditorWindow
    {
        private EffectCatalog _catalog;
        private int _selectedIndex = -1;
        private EffectDefinition _selectedEffect;

        // Parameter overrides
        private float _scale = 1f;
        private float _duration = 4f;
        private Color _color = Color.white;
        private string _target = "";

        // Generated tag
        private string _generatedTag = "";

        // Preview state
        private GameObject _previewInstance;

        [MenuItem("Window/Dialogue/Effect Authoring")]
        public static void ShowWindow()
        {
            GetWindow<EffectEditorWindow>("Effect Authoring");
        }

        private void OnEnable()
        {
            _catalog = EffectCatalog.Load();
            if (_catalog == null)
            {
                // Try to find it in project
                var guids = AssetDatabase.FindAssets("t:EffectCatalog");
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _catalog = AssetDatabase.LoadAssetAtPath<EffectCatalog>(path);
                }
            }

            if (_catalog != null && _catalog.allEffects.Count > 0)
            {
                SelectEffect(0);
            }
        }

        private void OnDisable()
        {
            CleanupPreview();
        }

        private void SelectEffect(int index)
        {
            if (_catalog == null || index < 0 || index >= _catalog.allEffects.Count)
                return;

            _selectedIndex = index;
            _selectedEffect = _catalog.allEffects[index];

            // Reset parameters to defaults
            _scale = _selectedEffect.defaultScale;
            _duration = _selectedEffect.defaultDuration;
            _color = _selectedEffect.defaultColor;
            _target = "";

            UpdateGeneratedTag();
        }

        private void UpdateGeneratedTag()
        {
            if (_selectedEffect == null)
            {
                _generatedTag = "";
                return;
            }

            var sb = new StringBuilder();
            sb.Append("[EFFECT: ");
            sb.Append(_selectedEffect.effectTag);

            // Add parameters if allowed and non-default
            if (
                _selectedEffect.allowCustomScale
                && Mathf.Abs(_scale - _selectedEffect.defaultScale) > 0.01f
            )
            {
                sb.Append($" | Scale: {_scale:F1}");
            }
            if (
                _selectedEffect.allowCustomDuration
                && Mathf.Abs(_duration - _selectedEffect.defaultDuration) > 0.01f
            )
            {
                sb.Append($" | Duration: {_duration:F1}");
            }
            if (_selectedEffect.allowCustomColor && _color != _selectedEffect.defaultColor)
            {
                sb.Append($" | Color: #{ColorUtility.ToHtmlStringRGB(_color)}");
            }
            if (!string.IsNullOrWhiteSpace(_target))
            {
                sb.Append($" | Target: {_target}");
            }

            sb.Append("]");
            _generatedTag = sb.ToString();
        }

        private void OnGUI()
        {
            if (_catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "No EffectCatalog found. Create one at Assets/Resources/Dialogue/EffectCatalog.asset",
                    MessageType.Warning
                );
                if (GUILayout.Button("Create EffectCatalog"))
                {
                    CreateCatalog();
                }
                return;
            }

            if (_catalog.allEffects.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "EffectCatalog is empty. Add EffectDefinition assets or import from NPC profiles.",
                    MessageType.Info
                );
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pick an Effect", EditorStyles.boldLabel);

            // Effect dropdown
            string[] effectNames = new string[_catalog.allEffects.Count];
            for (int i = 0; i < _catalog.allEffects.Count; i++)
            {
                effectNames[i] = _catalog.allEffects[i]?.effectTag ?? $"Effect {i}";
            }

            int newIndex = EditorGUILayout.Popup("Effect", _selectedIndex, effectNames);
            if (newIndex != _selectedIndex)
            {
                SelectEffect(newIndex);
            }

            if (_selectedEffect == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Description", EditorStyles.label);
            EditorGUILayout.HelpBox(_selectedEffect.description, MessageType.Info);

            // Parameters section
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parameters (Optional)", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            if (_selectedEffect.allowCustomScale)
            {
                _scale = EditorGUILayout.Slider("Scale", _scale, 0.1f, 10f);
            }
            if (_selectedEffect.allowCustomDuration)
            {
                _duration = EditorGUILayout.Slider("Duration (s)", _duration, 0.5f, 30f);
            }
            if (_selectedEffect.allowCustomColor)
            {
                _color = EditorGUILayout.ColorField("Color", _color);
            }

            _target = EditorGUILayout.TextField("Target", _target);

            EditorGUI.indentLevel--;

            // Update tag when parameters change
            if (GUI.changed)
            {
                UpdateGeneratedTag();
            }

            // Generated tag section
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated Tag", EditorStyles.boldLabel);

            EditorGUILayout.SelectableLabel(
                _generatedTag,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight)
            );

            EditorGUILayout.Space();

            // Action buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy to Clipboard"))
            {
                EditorGUIUtility.systemCopyBuffer = _generatedTag;
                Debug.Log($"[EffectEditor] Copied: {_generatedTag}");
            }

            if (GUILayout.Button("Preview in Scene"))
            {
                PreviewEffect();
            }
            EditorGUILayout.EndHorizontal();

            // Instructions
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "1. Select an effect and tweak parameters\n"
                    + "2. Click 'Copy to Clipboard' and paste into NPC system prompt\n"
                    + "3. Click 'Preview in Scene' to see the effect instantly",
                MessageType.Info
            );

            // Preview cleanup
            if (_previewInstance != null && !_previewInstance.activeSelf)
            {
                DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }
        }

        private void PreviewEffect()
        {
            if (_selectedEffect == null || _selectedEffect.effectPrefab == null)
            {
                Debug.LogWarning("[EffectEditor] No effect prefab assigned.");
                return;
            }

            CleanupPreview();

            // Instantiate at scene view camera
            Camera sceneCam = SceneView.lastActiveSceneView?.camera;
            Vector3 spawnPos;
            if (sceneCam != null)
            {
                spawnPos = sceneCam.transform.position + sceneCam.transform.forward * 3f;
            }
            else
            {
                spawnPos = new Vector3(0, 2, 0);
            }

            _previewInstance = Instantiate(
                _selectedEffect.effectPrefab,
                spawnPos,
                Quaternion.identity
            );
            _previewInstance.name = $"Preview_{_selectedEffect.effectTag}";

            // Apply parameters
            ApplyPreviewParameters(_previewInstance);

            // Auto-destroy after duration
            float lifetime = _duration + 2f;
            EditorApplication.delayCall += () =>
            {
                if (_previewInstance != null)
                {
                    DestroyImmediate(_previewInstance);
                    _previewInstance = null;
                }
            };

            Debug.Log($"[EffectEditor] Preview spawned at {spawnPos}");
            SceneView.RepaintAll();
        }

        private void ApplyPreviewParameters(GameObject go)
        {
            var systems = go.GetComponentsInChildren<ParticleSystem>();
            float scaleFactor = _scale;

            foreach (var ps in systems)
            {
                var main = ps.main;
                main.startSize = main.startSize.constant * scaleFactor;
                main.duration = _duration;
                main.startColor = _color;
                main.loop = false;
            }

            go.transform.localScale = go.transform.localScale * scaleFactor;
        }

        private void CleanupPreview()
        {
            if (_previewInstance != null)
            {
                DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }
        }

        private void CreateCatalog()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Effect Catalog",
                "EffectCatalog",
                "asset",
                "Save effect catalog"
            );

            if (!string.IsNullOrEmpty(path))
            {
                _catalog = CreateInstance<EffectCatalog>();
                AssetDatabase.CreateAsset(_catalog, path);
                AssetDatabase.SaveAssets();

                // Ensure Resources directory exists
                string resourcesPath = Application.dataPath + "/Resources/Dialogue";
                if (!System.IO.Directory.Exists(resourcesPath))
                {
                    System.IO.Directory.CreateDirectory(resourcesPath);
                }

                // Move to Resources
                string resourcePath = "Assets/Resources/Dialogue/EffectCatalog.asset";
                AssetDatabase.MoveAsset(path, resourcePath);
                AssetDatabase.SaveAssets();

                _catalog = AssetDatabase.LoadAssetAtPath<EffectCatalog>(resourcePath);
            }
        }
    }
}
