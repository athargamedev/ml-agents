using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Network_Game.Dialogue.MCP;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Network_Game.Editor.CustomTools
{
    [McpForUnityTool(
        "ng_showcase_scene_mode",
        Description = "Apply or manage non-destructive showcase polish for Behavior_Scene: post-processing volume, UI test mode (hide/show HUD panels), camera presets, and runtime showcase shots. Actions: apply, status, hide_ui, show_ui, camera_preset, runtime_shot, stop_runtime_shot, runtime_status."
     )]
    public static class ShowcaseSceneModeTool
    {
        public class Parameters
        {
            [ToolParameter("Action: apply, status, hide_ui, show_ui, camera_preset, runtime_shot, stop_runtime_shot, runtime_status", Required = false)]
            public string action { get; set; }

            [ToolParameter("Camera preset name for action=camera_preset (wide, storm, forge, archivist)", Required = false)]
            public string preset { get; set; }

            [ToolParameter("Runtime shot id for action=runtime_shot (wide, storm, forge, archivist)", Required = false)]
            public string shot { get; set; }

            [ToolParameter("Hide UI when applying showcase mode (default true)", Required = false)]
            public bool? hide_ui { get; set; }

            [ToolParameter("Enable showcase camera object on apply (default false)", Required = false)]
            public bool? enable_showcase_camera { get; set; }

            [ToolParameter("Isolate matching NPC polish set during runtime_shot (default true)", Required = false)]
            public bool? isolate_polish { get; set; }

            [ToolParameter("Post-FX volume weight (default 0.9)", Required = false)]
            public float? postfx_weight { get; set; }

            [ToolParameter("Shot duration seconds for runtime_shot (default controller value)", Required = false)]
            public float? duration_seconds { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            Parameters p = @params?.ToObject<Parameters>() ?? new Parameters();
            string action = string.IsNullOrWhiteSpace(p.action)
                ? "status"
                : p.action.Trim().ToLowerInvariant();

            switch (action)
            {
                case "apply":
                    return ShowcaseSceneAutomation.ApplyShowcaseMode(
                        hideUi: p.hide_ui ?? true,
                        enableShowcaseCamera: p.enable_showcase_camera ?? false,
                        postFxWeight: p.postfx_weight ?? 0.9f
                    );
                case "hide_ui":
                    return ShowcaseSceneAutomation.SetUiVisible(false);
                case "show_ui":
                    return ShowcaseSceneAutomation.SetUiVisible(true);
                case "camera_preset":
                    return ShowcaseSceneAutomation.ApplyCameraPreset(
                        string.IsNullOrWhiteSpace(p.preset) ? "wide" : p.preset
                    );
                case "runtime_shot":
                    return ShowcaseSceneAutomation.PlayRuntimeShot(
                        string.IsNullOrWhiteSpace(p.shot)
                        ? (string.IsNullOrWhiteSpace(p.preset) ? "wide" : p.preset)
                        : p.shot,
                        p.duration_seconds,
                        p.enable_showcase_camera,
                        p.isolate_polish
                    );
                case "stop_runtime_shot":
                    return ShowcaseSceneAutomation.StopRuntimeShot();
                case "runtime_status":
                    return new SuccessResponse(
                        "Showcase runtime shot status.",
                        ShowcaseSceneAutomation.GetRuntimeShotStatus()
                    );
                case "status":
                    return new SuccessResponse("Showcase scene mode status.", ShowcaseSceneAutomation.GetStatus());
                default:
                    return new ErrorResponse(
                        $"Unsupported action '{action}'. Expected apply, status, hide_ui, show_ui, camera_preset, runtime_shot, stop_runtime_shot, runtime_status."
                    );
            }
        }
    }

    internal static class ShowcaseSceneAutomation
    {
        private const string RootName = "MCP_ShowcasePolish";
        private const string ShowcaseCameraName = "MCP_ShowcaseCamera";
        private const string AnchorsRootName = "MCP_ShowcaseCameraAnchors";
        private const string VolumeGoName = "MCP_ShowcaseGlobalVolume";
        private const string DefaultProfilePath = "Assets/Network_Game/Scene/Profiles/DefaultProfile(URP).asset";
        private const string FallbackProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
        private const string UiRootPath = "Modern_HUD_Root";
        private const string LoginPath = "Modern_HUD_Root/Login_Screen";
        private const string ProfilePath = "Modern_HUD_Root/Profile_Card";
        private const string DialoguePath = "Modern_HUD_Root/Dialogue_Overlay";

        private const string SessionPrefix = "Network_Game.MCP.ShowcaseUI.";
        private const string KeyHasSnapshot = SessionPrefix + "HasSnapshot";
        private const string KeyLogin = SessionPrefix + "Login";
        private const string KeyProfile = SessionPrefix + "Profile";
        private const string KeyDialogue = SessionPrefix + "Dialogue";

        private struct Preset
        {
            public string Name;
            public Vector3 Position;
            public Vector3 LookAt;
            public float FieldOfView;
        }

        private static readonly Preset[] Presets =
        {
            new Preset
            {
                Name = "wide",
                Position = new Vector3(0f, 7.5f, -22f),
                LookAt = new Vector3(0f, 1.4f, 0f),
                FieldOfView = 34f,
            },
            new Preset
            {
                Name = "storm",
                Position = new Vector3(9.8f, 2.1f, -4.1f),
                LookAt = new Vector3(12.64f, 1.35f, 0f),
                FieldOfView = 40f,
            },
            new Preset
            {
                Name = "forge",
                Position = new Vector3(3.2f, 2.0f, -10.8f),
                LookAt = new Vector3(0.85f, 1.35f, -14.56f),
                FieldOfView = 40f,
            },
            new Preset
            {
                Name = "archivist",
                Position = new Vector3(-1.7f, 2.0f, 20.2f),
                LookAt = new Vector3(0.47f, 1.35f, 16.76f),
                FieldOfView = 40f,
            },
        };

        [MenuItem("Network Game/MCP/Showcase/Apply Scene Mode")]
        private static void MenuApplyShowcaseMode()
        {
            HandleMenuResult(ApplyShowcaseMode(hideUi: true, enableShowcaseCamera: false, postFxWeight: 0.9f));
        }

        [MenuItem("Network Game/MCP/Showcase/Hide UI")]
        private static void MenuHideUi()
        {
            HandleMenuResult(SetUiVisible(false));
        }

        [MenuItem("Network Game/MCP/Showcase/Show UI")]
        private static void MenuShowUi()
        {
            HandleMenuResult(SetUiVisible(true));
        }

        [MenuItem("Network Game/MCP/Showcase/Camera Preset/Wide")]
        private static void MenuPresetWide() => HandleMenuResult(ApplyCameraPreset("wide"));

        [MenuItem("Network Game/MCP/Showcase/Camera Preset/Storm Oracle")]
        private static void MenuPresetStorm() => HandleMenuResult(ApplyCameraPreset("storm"));

        [MenuItem("Network Game/MCP/Showcase/Camera Preset/Forge Keeper")]
        private static void MenuPresetForge() => HandleMenuResult(ApplyCameraPreset("forge"));

        [MenuItem("Network Game/MCP/Showcase/Camera Preset/Archivist")]
        private static void MenuPresetArchivist() => HandleMenuResult(ApplyCameraPreset("archivist"));

        [MenuItem("Network Game/MCP/Showcase/Runtime Shot/Wide")]
        private static void MenuRuntimeShotWide() =>
            HandleMenuResult(PlayRuntimeShot("wide", null, null, null));

        [MenuItem("Network Game/MCP/Showcase/Runtime Shot/Storm Oracle")]
        private static void MenuRuntimeShotStorm() =>
            HandleMenuResult(PlayRuntimeShot("storm", null, null, null));

        [MenuItem("Network Game/MCP/Showcase/Runtime Shot/Forge Keeper")]
        private static void MenuRuntimeShotForge() =>
            HandleMenuResult(PlayRuntimeShot("forge", null, null, null));

        [MenuItem("Network Game/MCP/Showcase/Runtime Shot/Archivist")]
        private static void MenuRuntimeShotArchivist() =>
            HandleMenuResult(PlayRuntimeShot("archivist", null, null, null));

        [MenuItem("Network Game/MCP/Showcase/Runtime Shot/Stop")]
        private static void MenuRuntimeShotStop() => HandleMenuResult(StopRuntimeShot());

        internal static object ApplyShowcaseMode(bool hideUi, bool enableShowcaseCamera, float postFxWeight)
        {
            var result = new Dictionary<string, object>();

            GameObject root = EnsureRoot();
            result["root"] = root != null ? root.name : null;

            var volumeResult = EnsureShowcaseVolume(root, Mathf.Clamp01(postFxWeight));
            result["postfx"] = volumeResult;

            var cameraRigResult = EnsureShowcaseCameraRig(root);
            result["camera_rig"] = cameraRigResult;

            if (enableShowcaseCamera)
            {
                GameObject showcaseCamera = GameObject.Find(ShowcaseCameraName);
                if (showcaseCamera != null)
                {
                    showcaseCamera.SetActive(true);
                    result["showcase_camera_enabled"] = true;
                }
            }

            if (hideUi)
            {
                result["ui"] = CoerceResponse(SetUiVisible(false));
            }

            return new SuccessResponse("Applied showcase scene mode.", result);
        }

        internal static object ApplyCameraPreset(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                presetName = "wide";
            }

            Preset? preset = FindPreset(presetName);
            if (!preset.HasValue)
            {
                return new ErrorResponse($"Unknown preset '{presetName}'. Use wide, storm, forge, archivist.");
            }

            EnsureShowcaseCameraRig(EnsureRoot());

            Camera targetCamera = Camera.main;
            if (targetCamera == null)
            {
                GameObject mainCameraGo = GameObject.Find("MainCamera");
                if (mainCameraGo != null)
                {
                    targetCamera = mainCameraGo.GetComponent<Camera>();
                }
            }

            if (targetCamera == null)
            {
                return new ErrorResponse("MainCamera not found.");
            }

            Undo.RecordObject(targetCamera.transform, "Apply Showcase Camera Preset");
            Undo.RecordObject(targetCamera, "Apply Showcase Camera Preset FOV");

            ApplyPresetToTransform(targetCamera.transform, preset.Value);
            targetCamera.fieldOfView = preset.Value.FieldOfView;
            EditorUtility.SetDirty(targetCamera);

            // Keep a disabled showcase camera in sync as a static reference rig.
            GameObject showcaseCamGo = GameObject.Find(ShowcaseCameraName);
            if (showcaseCamGo != null)
            {
                Camera showcaseCam = showcaseCamGo.GetComponent<Camera>();
                if (showcaseCam != null)
                {
                    ApplyPresetToTransform(showcaseCam.transform, preset.Value);
                    showcaseCam.fieldOfView = preset.Value.FieldOfView;
                    EditorUtility.SetDirty(showcaseCam);
                }
            }

            return new SuccessResponse(
                $"Applied showcase camera preset '{preset.Value.Name}' to MainCamera.",
                new
                {
                    preset = preset.Value.Name,
                    position = new[]
                    {
                        targetCamera.transform.position.x,
                        targetCamera.transform.position.y,
                        targetCamera.transform.position.z
                    },
                    fov = targetCamera.fieldOfView,
                }
            );
        }

        internal static object SetUiVisible(bool visible)
        {
            GameObject uiRoot = GameObject.Find(UiRootPath);
            if (uiRoot == null)
            {
                return new ErrorResponse($"UI root '{UiRootPath}' not found.");
            }

            GameObject login = GameObject.Find(LoginPath);
            GameObject profile = GameObject.Find(ProfilePath);
            GameObject dialogue = GameObject.Find(DialoguePath);

            if (!visible)
            {
                CaptureUiSnapshot(login, profile, dialogue);
            }

            int changed = 0;
            changed += SetActiveIfDifferent(login, visible);
            changed += SetActiveIfDifferent(profile, visible);
            changed += SetActiveIfDifferent(dialogue, visible);

            if (visible)
            {
                RestoreUiSnapshot(login, profile, dialogue);
            }

            return new SuccessResponse(
                visible ? "Showcase UI mode disabled (HUD restored)." : "Showcase UI mode enabled (HUD panels hidden).",
                new
                {
                    visible,
                    changed,
                    login = login != null ? login.activeSelf : (bool?)null,
                    profile = profile != null ? profile.activeSelf : (bool?)null,
                    dialogue = dialogue != null ? dialogue.activeSelf : (bool?)null,
                }
            );
        }

        internal static object GetStatus()
        {
            GameObject root = GameObject.Find(RootName);
            GameObject volumeGo = GameObject.Find(VolumeGoName);
            GameObject showcaseCamera = GameObject.Find(ShowcaseCameraName);
            GameObject login = GameObject.Find(LoginPath);
            GameObject profile = GameObject.Find(ProfilePath);
            GameObject dialogue = GameObject.Find(DialoguePath);

            Volume volume = volumeGo != null ? volumeGo.GetComponent<Volume>() : null;

            return new Dictionary<string, object>
            {
                ["root_exists"] = root != null,
                ["volume"] = volume == null ? null : new Dictionary<string, object>
                {
                    ["exists"] = true,
                    ["is_global"] = volume.isGlobal,
                    ["priority"] = volume.priority,
                    ["weight"] = volume.weight,
                    ["profile"] = volume.sharedProfile != null ? AssetDatabase.GetAssetPath(volume.sharedProfile) : null,
                    ["layer"] = volumeGo.layer,
                },
                ["showcase_camera"] = showcaseCamera == null ? null : new Dictionary<string, object>
                {
                    ["exists"] = true,
                    ["activeSelf"] = showcaseCamera.activeSelf,
                    ["position"] = ToFloatArray(showcaseCamera.transform.position),
                },
                ["ui"] = new Dictionary<string, object>
                {
                    ["login"] = login != null ? login.activeSelf : (bool?)null,
                    ["profile"] = profile != null ? profile.activeSelf : (bool?)null,
                    ["dialogue"] = dialogue != null ? dialogue.activeSelf : (bool?)null,
                },
                ["runtime_shot"] = GetRuntimeShotStatus(),
            };
        }

        internal static object PlayRuntimeShot(
            string shotId,
            float? durationSeconds,
            bool? enableShowcaseCamera,
            bool? isolatePolish
        )
        {
            MCPShowcasePolishController controller = GetOrCreateShowcasePolishController();
            if (controller == null)
            {
                return new ErrorResponse(
                    "MCPShowcasePolishController not found and could not be created."
                );
            }

            string resolvedShotId = string.IsNullOrWhiteSpace(shotId) ? "wide" : shotId.Trim();
            if (!EditorApplication.isPlaying)
            {
                bool snapped = controller.TrySnapShowcaseCameraToShot(
                    resolvedShotId,
                    enableShowcaseCamera: enableShowcaseCamera ?? false
                );
                if (!snapped)
                {
                    return new ErrorResponse(
                        $"Failed to snap showcase camera to shot '{resolvedShotId}'. (Editor not in Play Mode.)"
                    );
                }

                return new SuccessResponse(
                    $"Snapped showcase camera to shot '{resolvedShotId}' (Edit Mode preview only).",
                    GetRuntimeShotStatus()
                );
            }

            bool played = controller.TryPlayShot(
                resolvedShotId,
                durationSeconds ?? -1f,
                enableShowcaseCamera,
                isolatePolish
            );
            if (!played)
            {
                return new ErrorResponse($"Failed to play runtime showcase shot '{resolvedShotId}'.");
            }

            return new SuccessResponse(
                $"Triggered runtime showcase shot '{resolvedShotId}'.",
                GetRuntimeShotStatus()
            );
        }

        internal static object StopRuntimeShot()
        {
            MCPShowcasePolishController controller = GetShowcasePolishController();
            if (controller == null)
            {
                return new ErrorResponse("MCPShowcasePolishController not found.");
            }

            controller.StopActiveShot(restoreState: true);
            return new SuccessResponse("Stopped runtime showcase shot.", GetRuntimeShotStatus());
        }

        internal static Dictionary<string, object> GetRuntimeShotStatus()
        {
            MCPShowcasePolishController controller = GetShowcasePolishController();
            if (controller == null)
            {
                return new Dictionary<string, object>
                {
                    ["controller_exists"] = false,
                    ["play_mode"] = EditorApplication.isPlaying,
                };
            }

            return new Dictionary<string, object>
            {
                ["controller_exists"] = true,
                ["controller_gameobject"] = controller.gameObject.name,
                ["play_mode"] = EditorApplication.isPlaying,
                ["active_shot"] = string.IsNullOrWhiteSpace(controller.ActiveShotId)
                    ? null
                    : controller.ActiveShotId,
                ["available_shots"] = controller.GetAvailableShotIds(),
            };
        }

        private static GameObject EnsureRoot()
        {
            GameObject root = GameObject.Find(RootName);
            if (root != null)
            {
                return root;
            }

            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Showcase Root");
            return root;
        }

        private static MCPShowcasePolishController GetShowcasePolishController()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                return null;
            }

            return root.GetComponent<MCPShowcasePolishController>();
        }

        private static MCPShowcasePolishController GetOrCreateShowcasePolishController()
        {
            GameObject root = EnsureRoot();
            if (root == null)
            {
                return null;
            }

            MCPShowcasePolishController controller = root.GetComponent<MCPShowcasePolishController>();
            if (controller != null)
            {
                return controller;
            }

            Undo.RegisterCompleteObjectUndo(root, "Add Showcase Polish Controller");
            controller = Undo.AddComponent<MCPShowcasePolishController>(root);
            if (controller != null)
            {
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(controller);
            }

            return controller;
        }

        private static object EnsureShowcaseVolume(GameObject root, float weight)
        {
            GameObject volumeGo = GameObject.Find(VolumeGoName);
            if (volumeGo == null)
            {
                volumeGo = new GameObject(VolumeGoName);
                Undo.RegisterCreatedObjectUndo(volumeGo, "Create Showcase Volume");
                volumeGo.transform.SetParent(root.transform, false);
            }
            else if (volumeGo.transform.parent != root.transform)
            {
                Undo.SetTransformParent(volumeGo.transform, root.transform, "Reparent Showcase Volume");
            }

            volumeGo.layer = 10; // Camera layer to match MainCamera volume mask.

            Volume volume = volumeGo.GetComponent<Volume>();
            if (volume == null)
            {
                volume = Undo.AddComponent<Volume>(volumeGo);
            }

            Undo.RecordObject(volume, "Configure Showcase Volume");
            volume.isGlobal = true;
            volume.priority = 25f;
            volume.weight = weight;

            UnityEngine.Object profile = LoadShowcaseProfileAsset();
            if (profile != null)
            {
                var so = new SerializedObject(volume);
                SerializedProperty sharedProfileProp = so.FindProperty("sharedProfile");
                if (sharedProfileProp != null)
                {
                    sharedProfileProp.objectReferenceValue = profile;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorUtility.SetDirty(volumeGo);
            EditorUtility.SetDirty(volume);

            return new Dictionary<string, object>
            {
                ["gameObject"] = volumeGo.name,
                ["layer"] = volumeGo.layer,
                ["isGlobal"] = volume.isGlobal,
                ["priority"] = volume.priority,
                ["weight"] = volume.weight,
                ["profile"] = profile != null ? AssetDatabase.GetAssetPath(profile) : null,
            };
        }

        private static object EnsureShowcaseCameraRig(GameObject root)
        {
            GameObject anchorsRoot = GameObject.Find(AnchorsRootName);
            if (anchorsRoot == null)
            {
                anchorsRoot = new GameObject(AnchorsRootName);
                Undo.RegisterCreatedObjectUndo(anchorsRoot, "Create Showcase Anchors Root");
                anchorsRoot.transform.SetParent(root.transform, false);
            }
            else if (anchorsRoot.transform.parent != root.transform)
            {
                Undo.SetTransformParent(anchorsRoot.transform, root.transform, "Reparent Showcase Anchors Root");
            }

            for (int i = 0; i < Presets.Length; i++)
            {
                Preset preset = Presets[i];
                string anchorName = $"Shot_{CultureSafeTitle(preset.Name)}";
                GameObject anchor = FindChildByName(anchorsRoot, anchorName);
                if (anchor == null)
                {
                    anchor = new GameObject(anchorName);
                    Undo.RegisterCreatedObjectUndo(anchor, "Create Showcase Anchor");
                    anchor.transform.SetParent(anchorsRoot.transform, false);
                }
                ApplyPresetToTransform(anchor.transform, preset);
            }

            GameObject showcaseCameraGo = GameObject.Find(ShowcaseCameraName);
            bool created = false;
            if (showcaseCameraGo == null)
            {
                showcaseCameraGo = new GameObject(ShowcaseCameraName);
                Undo.RegisterCreatedObjectUndo(showcaseCameraGo, "Create Showcase Camera");
                showcaseCameraGo.transform.SetParent(root.transform, false);
                created = true;
            }
            else if (showcaseCameraGo.transform.parent != root.transform)
            {
                Undo.SetTransformParent(showcaseCameraGo.transform, root.transform, "Reparent Showcase Camera");
            }

            Camera cam = showcaseCameraGo.GetComponent<Camera>();
            if (cam == null)
            {
                cam = Undo.AddComponent<Camera>(showcaseCameraGo);
            }

            if (showcaseCameraGo.GetComponent<AudioListener>() != null)
            {
                // Avoid listener conflicts if someone duplicated a camera.
                var listener = showcaseCameraGo.GetComponent<AudioListener>();
                Undo.DestroyObjectImmediate(listener);
            }

            UniversalAdditionalCameraData urpData = showcaseCameraGo.GetComponent<UniversalAdditionalCameraData>();
            if (urpData == null)
            {
                urpData = Undo.AddComponent<UniversalAdditionalCameraData>(showcaseCameraGo);
            }

            Undo.RecordObject(cam, "Configure Showcase Camera");
            cam.enabled = false;
            cam.depth = 5f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 34f;

            var widePreset = FindPreset("wide").Value;
            ApplyPresetToTransform(showcaseCameraGo.transform, widePreset);
            cam.fieldOfView = widePreset.FieldOfView;

            EditorUtility.SetDirty(showcaseCameraGo);
            EditorUtility.SetDirty(cam);
            if (urpData != null)
            {
                urpData.renderPostProcessing = true;
                urpData.volumeLayerMask = 1 << 10; // Camera layer
                EditorUtility.SetDirty(urpData);
            }

            return new Dictionary<string, object>
            {
                ["anchorsRoot"] = anchorsRoot.name,
                ["anchorCount"] = Presets.Length,
                ["showcaseCamera"] = showcaseCameraGo.name,
                ["cameraCreated"] = created,
                ["cameraEnabled"] = cam.enabled,
                ["cameraPosition"] = ToFloatArray(showcaseCameraGo.transform.position),
            };
        }

        private static UnityEngine.Object LoadShowcaseProfileAsset()
        {
            UnityEngine.Object profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefaultProfilePath);
            if (profile != null)
            {
                return profile;
            }

            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(FallbackProfilePath);
        }

        private static Preset? FindPreset(string name)
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                if (string.Equals(Presets[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Presets[i];
                }
            }

            return null;
        }

        private static void ApplyPresetToTransform(Transform t, Preset preset)
        {
            Undo.RecordObject(t, "Apply Showcase Preset");
            t.position = preset.Position;
            Vector3 forward = (preset.LookAt - preset.Position);
            if (forward.sqrMagnitude > 0.0001f)
            {
                t.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
            EditorUtility.SetDirty(t);
        }

        private static string CultureSafeTitle(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "Preset";
            }

            if (input.Length == 1)
            {
                return input.ToUpperInvariant();
            }

            return char.ToUpperInvariant(input[0]) + input.Substring(1).ToLowerInvariant();
        }

        private static GameObject FindChildByName(GameObject parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            Transform pt = parent.transform;
            for (int i = 0; i < pt.childCount; i++)
            {
                Transform child = pt.GetChild(i);
                if (child != null && string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static int SetActiveIfDifferent(GameObject go, bool active)
        {
            if (go == null || go.activeSelf == active)
            {
                return 0;
            }

            Undo.RecordObject(go, "Toggle Showcase UI");
            go.SetActive(active);
            EditorUtility.SetDirty(go);
            return 1;
        }

        private static void CaptureUiSnapshot(GameObject login, GameObject profile, GameObject dialogue)
        {
            if (SessionState.GetBool(KeyHasSnapshot, false))
            {
                return;
            }

            SessionState.SetBool(KeyHasSnapshot, true);
            SessionState.SetBool(KeyLogin, login != null && login.activeSelf);
            SessionState.SetBool(KeyProfile, profile != null && profile.activeSelf);
            SessionState.SetBool(KeyDialogue, dialogue != null && dialogue.activeSelf);
        }

        private static void RestoreUiSnapshot(GameObject login, GameObject profile, GameObject dialogue)
        {
            if (!SessionState.GetBool(KeyHasSnapshot, false))
            {
                return;
            }

            SetActiveIfDifferent(login, SessionState.GetBool(KeyLogin, true));
            SetActiveIfDifferent(profile, SessionState.GetBool(KeyProfile, true));
            SetActiveIfDifferent(dialogue, SessionState.GetBool(KeyDialogue, true));

            SessionState.EraseBool(KeyHasSnapshot);
            SessionState.EraseBool(KeyLogin);
            SessionState.EraseBool(KeyProfile);
            SessionState.EraseBool(KeyDialogue);
        }

        private static void HandleMenuResult(object result)
        {
            JObject jo = result as JObject ?? JObject.FromObject(result ?? new ErrorResponse("null_result"));
            bool success = jo.Value<bool?>("success") ?? false;
            if (success)
            {
                Debug.Log($"[Showcase] {jo.Value<string>("message")}");
            }
            else
            {
                Debug.LogError($"[Showcase] {jo.Value<string>("error") ?? "Unknown error"}");
            }
        }

        private static object CoerceResponse(object value)
        {
            if (value == null)
            {
                return null;
            }

            return value is JObject jo ? jo : JObject.FromObject(value);
        }

        private static float[] ToFloatArray(Vector3 v)
        {
            return new[] { v.x, v.y, v.z };
        }
    }
}
