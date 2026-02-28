using Network_Game.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Network_Game.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ModernUISetup : MonoBehaviour
    {
        [Header("Assets")]
        public PanelSettings PanelSettings;
        public VisualTreeAsset LoginUxml;
        public VisualTreeAsset ProfileUxml;
        public VisualTreeAsset DialogueUxml;

        [Header("Components")]
        public UIDocument LoginDoc;
        public UIDocument ProfileDoc;
        public UIDocument DialogueDoc;

        private void Reset()
        {
#if UNITY_EDITOR
            if (PanelSettings == null)
            {
                PanelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Network_Game/UI/Theme/BlocksPanelSettings.asset"
                );
            }

            if (LoginUxml == null)
            {
                LoginUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Network_Game/UI/Login/PlayerLoginUI.uxml"
                );
            }

            if (ProfileUxml == null)
            {
                ProfileUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Network_Game/UI/Profile/PlayerProfileUI.uxml"
                );
            }

            if (DialogueUxml == null)
            {
                DialogueUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Network_Game/UI/Dialogue/ModernDialogueUI.uxml"
                );
            }
#endif
            ResolveDocumentReferences();
        }

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                ScheduleEditorApply();
                return;
            }
#endif
            Apply();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.delayCall -= ApplyEditorNow;
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ScheduleEditorApply();
        }
#endif

        [ContextMenu("Apply UI Assets")]
        public void Apply()
        {
            using (
                NGLog.BeginScope(
                    "UISetup",
                    "ApplyDocuments",
                    0,
                    this,
                    LogLevel.Debug,
                    LogLevel.Debug,
                    ("panelAssigned", PanelSettings != null),
                    ("loginDocAssigned", LoginDoc != null),
                    ("profileDocAssigned", ProfileDoc != null),
                    ("dialogueDocAssigned", DialogueDoc != null)
                )
            )
            {
                ResolveDocumentReferences();

                if (PanelSettings == null)
                {
                    NGLog.Warn(
                        "UISetup",
                        NGLog.Format(
                            "PanelSettings missing; skipped UI document binding",
                            ("loginDocResolved", LoginDoc != null),
                            ("profileDocResolved", ProfileDoc != null),
                            ("dialogueDocResolved", DialogueDoc != null),
                            ("loginUxmlAssigned", LoginUxml != null),
                            ("profileUxmlAssigned", ProfileUxml != null),
                            ("dialogueUxmlAssigned", DialogueUxml != null)
                        ),
                        this
                    );
                    return;
                }

                ApplyToDoc(LoginDoc, LoginUxml);
                ApplyToDoc(ProfileDoc, ProfileUxml);
                ApplyToDoc(DialogueDoc, DialogueUxml);
            }
        }

        private void ApplyToDoc(UIDocument doc, VisualTreeAsset uxml)
        {
            if (doc == null)
            {
                NGLog.Debug(
                    "UISetup",
                    "Skipped document apply because target UIDocument is null.",
                    this
                );
                return;
            }

            if (uxml == null)
            {
                NGLog.Warn(
                    "UISetup",
                    NGLog.Format("Skipped document apply because UXML is missing", ("doc", doc.name)),
                    doc
                );
                return;
            }

            bool changed = false;
            if (doc.panelSettings != PanelSettings)
            {
                doc.panelSettings = PanelSettings;
                changed = true;
            }

            if (doc.visualTreeAsset != uxml)
            {
                doc.visualTreeAsset = uxml;
                changed = true;
            }

            if (changed && Application.isPlaying)
            {
                NGLog.LogDataflowStep(
                    "UISetup",
                    "UIBind",
                    "ApplyToDocument",
                    LogLevel.Info,
                    0,
                    doc,
                    ("doc", doc.name),
                    ("panel", PanelSettings != null ? PanelSettings.name : "null"),
                    ("uxml", uxml.name)
                );
            }
        }

        private void ResolveDocumentReferences()
        {
            LoginDoc = ResolveChildDocument(LoginDoc, "Login_Screen");
            ProfileDoc = ResolveChildDocument(ProfileDoc, "Profile_Card");
            DialogueDoc = ResolveChildDocument(DialogueDoc, "Dialogue_Overlay");
        }

        private UIDocument ResolveChildDocument(UIDocument current, string childName)
        {
            if (current != null)
            {
                return current;
            }

            Transform child = transform.Find(childName);
            if (child == null)
            {
                return null;
            }

            return child.GetComponent<UIDocument>();
        }

#if UNITY_EDITOR
        private void ScheduleEditorApply()
        {
            EditorApplication.delayCall -= ApplyEditorNow;
            EditorApplication.delayCall += ApplyEditorNow;
        }

        private void ApplyEditorNow()
        {
            if (this == null || Application.isPlaying)
            {
                return;
            }

            Apply();
        }
#endif
    }
}
