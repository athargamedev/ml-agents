using System;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Network_Game.UI
{
    [ExecuteAlways]
    public class ModernUISetup : MonoBehaviour
    {
        private const float MinReadableTextFieldFontSize = 14f;
        private const float MinReadableTextFieldHeight = 40f;
        private const float MinReadableChatInputHeight = 44f;

        [Serializable]
        private sealed class ElementStyleOverride
        {
            public string ElementName = string.Empty;
            public string ElementClass = string.Empty;

            public bool OverrideWidth;
            public float Width = 120f;

            public bool OverrideHeight;
            public float Height = 40f;

            public bool OverrideFontSize;
            public float FontSize = 14f;

            public bool OverrideFontAsset;
            public Font FontAsset;

            public bool OverrideFontStyle;
            public FontStyle FontStyle = FontStyle.Normal;

            public bool OverrideTextColor;
            public Color TextColor = Color.white;

            public bool OverrideBackgroundColor;
            public Color BackgroundColor = new Color(0f, 0f, 0f, 0.2f);

            public bool OverrideOpacity;

            [Range(0f, 1f)]
            public float Opacity = 1f;
        }

        [Serializable]
        private sealed class DocumentStyleOverrides
        {
            public string RootElementName = string.Empty;

            public bool OverrideLeft;
            public float Left = 0f;

            public bool OverrideTop;
            public float Top = 0f;

            public bool OverrideRight;
            public float Right = 0f;

            public bool OverrideBottom;
            public float Bottom = 0f;

            public bool OverrideWidth;
            public float Width = 320f;

            public bool OverrideHeight;
            public float Height = 320f;

            public bool OverrideOpacity;

            [Range(0f, 1f)]
            public float Opacity = 1f;

            public bool OverrideBackgroundColor;
            public Color BackgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.94f);

            public bool OverrideBorderRadius;
            public float BorderRadius = 16f;

            public List<ElementStyleOverride> ElementOverrides = new List<ElementStyleOverride>();
        }

        [Header("Assets")]
        public PanelSettings PanelSettings;
        public VisualTreeAsset LoginUxml;
        public VisualTreeAsset ProfileUxml;
        public VisualTreeAsset DialogueUxml;

        [Header("Components")]
        public UIDocument LoginDoc;
        public UIDocument ProfileDoc;
        public UIDocument DialogueDoc;

        [Header("HUD Style Overrides")]
        [SerializeField]
        private bool m_EnableHudStyleOverrides = true;

        [SerializeField]
        public bool m_LivePreviewInPlayMode = true;

        [SerializeField]
        private bool m_LivePreviewInEditMode = true;

        [SerializeField]
        private DocumentStyleOverrides m_LoginStyle = new DocumentStyleOverrides
        {
            RootElementName = "root",
            OverrideOpacity = true,
            Opacity = 1f,
            ElementOverrides = new List<ElementStyleOverride>
            {
                new ElementStyleOverride
                {
                    ElementName = "login-button",
                    OverrideFontSize = true,
                    FontSize = 16f,
                },
                new ElementStyleOverride
                {
                    ElementName = "name-input",
                    OverrideFontSize = true,
                    FontSize = 16f,
                    OverrideTextColor = true,
                    TextColor = Color.white,
                },
                new ElementStyleOverride
                {
                    ElementName = "bio-input",
                    OverrideFontSize = true,
                    FontSize = 14f,
                    OverrideTextColor = true,
                    TextColor = Color.white,
                },
                new ElementStyleOverride
                {
                    ElementName = "status-label",
                    OverrideFontSize = true,
                    FontSize = 13f,
                    OverrideTextColor = true,
                    TextColor = new Color(1f, 1f, 1f, 0.58f),
                },
            },
        };

        [SerializeField]
        private DocumentStyleOverrides m_ProfileStyle = new DocumentStyleOverrides
        {
            RootElementName = "profile-card",
            OverrideWidth = true,
            Width = 320f,
            OverrideOpacity = true,
            Opacity = 1f,
            OverrideBackgroundColor = true,
            BackgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.94f),
            ElementOverrides = new List<ElementStyleOverride>
            {
                new ElementStyleOverride
                {
                    ElementName = "player-name",
                    OverrideFontSize = true,
                    FontSize = 14f,
                },
                new ElementStyleOverride
                {
                    ElementName = "player-bio",
                    OverrideFontSize = true,
                    FontSize = 14f,
                },
                new ElementStyleOverride
                {
                    ElementName = "player-status",
                    OverrideFontSize = true,
                    FontSize = 12f,
                },
            },
        };

        [SerializeField]
        private DocumentStyleOverrides m_DialogueStyle = new DocumentStyleOverrides
        {
            RootElementName = "chat-container",
            OverrideLeft = true,
            Left = 20f,
            OverrideBottom = true,
            Bottom = 20f,
            OverrideWidth = true,
            Width = 600f,
            OverrideHeight = true,
            Height = 400f,
            OverrideBackgroundColor = true,
            BackgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.94f),
            OverrideOpacity = true,
            Opacity = 1f,
            ElementOverrides = new List<ElementStyleOverride>
            {
                new ElementStyleOverride
                {
                    ElementName = "dialogue-header",
                    OverrideFontSize = true,
                    FontSize = 16f,
                },
                new ElementStyleOverride
                {
                    ElementName = "listener-status",
                    OverrideFontSize = true,
                    FontSize = 10f,
                },
                new ElementStyleOverride
                {
                    ElementName = "chat-input",
                    OverrideFontSize = true,
                    FontSize = 14f,
                },
                new ElementStyleOverride
                {
                    ElementName = "send-button",
                    OverrideFontSize = true,
                    FontSize = 14f,
                    OverrideWidth = true,
                    Width = 90f,
                },
                new ElementStyleOverride
                {
                    ElementName = "camera-switch",
                    OverrideFontSize = true,
                    FontSize = 10f,
                    OverrideWidth = true,
                    Width = 44f,
                },
            },
        };

        private void Reset()
        {
            // Try to auto-find assets in standard paths if in editor
#if UNITY_EDITOR
            if (PanelSettings == null)
                PanelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Network_Game/UI/Theme/BlocksPanelSettings.asset"
                );

            if (LoginUxml == null)
                LoginUxml = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Network_Game/UI/Login/PlayerLoginUI.uxml"
                );

            if (ProfileUxml == null)
                ProfileUxml = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Network_Game/UI/Profile/PlayerProfileUI.uxml"
                );

            if (DialogueUxml == null)
                DialogueUxml = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Network_Game/UI/Dialogue/ModernDialogueUI.uxml"
                );
#endif
        }

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && m_LivePreviewInEditMode)
            {
                ScheduleEditorPreviewApply();
            }
#endif
        }

        private void Update()
        {
            if (!Application.isPlaying || !m_LivePreviewInPlayMode || !m_EnableHudStyleOverrides)
            {
                return;
            }

            ApplyHudStylesOnly();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!m_EnableHudStyleOverrides)
            {
                return;
            }

            // Immediate visual feedback while tweaking style values in Play Mode.
            if (Application.isPlaying)
            {
                ApplyHudStylesOnly();
                return;
            }

            if (m_LivePreviewInEditMode)
            {
                ScheduleEditorPreviewApply();
            }
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

                if (m_EnableHudStyleOverrides)
                {
                    ApplyStyleToDoc(LoginDoc, m_LoginStyle);
                    ApplyStyleToDoc(ProfileDoc, m_ProfileStyle);
                    ApplyStyleToDoc(DialogueDoc, m_DialogueStyle);
                }
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
                    NGLog.Format(
                        "Skipped document apply because UXML is missing",
                        ("doc", doc.name)
                        ),
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

            if (m_EnableHudStyleOverrides)
            {
                DocumentStyleOverrides style = ResolveStyleOverrides(doc);
                if (style != null)
                {
                    ApplyStyleToDoc(doc, style);
                }
            }
        }

        [ContextMenu("Apply HUD Style Overrides")]
        public void ApplyHudStylesOnly()
        {
            ResolveDocumentReferences();

            if (!m_EnableHudStyleOverrides)
            {
                return;
            }

            ApplyStyleToDoc(LoginDoc, m_LoginStyle);
            ApplyStyleToDoc(ProfileDoc, m_ProfileStyle);
            ApplyStyleToDoc(DialogueDoc, m_DialogueStyle);
        }

        private void ResolveDocumentReferences()
        {
            if (LoginDoc == null)
            {
                Transform child = transform.Find("Login_Screen");
                if (child != null)
                {
                    LoginDoc = child.GetComponent<UIDocument>();
                }
            }

            if (ProfileDoc == null)
            {
                Transform child = transform.Find("Profile_Card");
                if (child != null)
                {
                    ProfileDoc = child.GetComponent<UIDocument>();
                }
            }

            if (DialogueDoc == null)
            {
                Transform child = transform.Find("Dialogue_Overlay");
                if (child != null)
                {
                    DialogueDoc = child.GetComponent<UIDocument>();
                }
            }
        }

#if UNITY_EDITOR
        private void ScheduleEditorPreviewApply()
        {
            EditorApplication.delayCall -= ApplyEditorPreviewNow;
            EditorApplication.delayCall += ApplyEditorPreviewNow;
        }

        private void ApplyEditorPreviewNow()
        {
            if (this == null)
            {
                return;
            }

            if (Application.isPlaying || !m_EnableHudStyleOverrides || !m_LivePreviewInEditMode)
            {
                return;
            }

            ResolveDocumentReferences();

            if (PanelSettings == null)
            {
                return;
            }

            ApplyToDoc(LoginDoc, LoginUxml);
            ApplyToDoc(ProfileDoc, ProfileUxml);
            ApplyToDoc(DialogueDoc, DialogueUxml);

            ApplyHudStylesOnly();
        }

#endif

        private DocumentStyleOverrides ResolveStyleOverrides(UIDocument doc)
        {
            if (doc == null)
            {
                return null;
            }

            if (doc == LoginDoc)
            {
                return m_LoginStyle;
            }

            if (doc == ProfileDoc)
            {
                return m_ProfileStyle;
            }

            if (doc == DialogueDoc)
            {
                return m_DialogueStyle;
            }

            return null;
        }

        private void ApplyStyleToDoc(UIDocument doc, DocumentStyleOverrides overrides)
        {
            if (doc == null || overrides == null)
            {
                return;
            }

            VisualElement root = doc.rootVisualElement;
            if (root == null)
            {
                return;
            }

            VisualElement target = root;
            if (!string.IsNullOrWhiteSpace(overrides.RootElementName))
            {
                VisualElement named = root.Q<VisualElement>(overrides.RootElementName);
                if (named != null)
                {
                    target = named;
                }
            }

            ApplyContainerOverrides(target, overrides);

            List<ElementStyleOverride> elementOverrides = overrides.ElementOverrides;
            if (elementOverrides == null || elementOverrides.Count == 0)
            {
                return;
            }

            for (int i = 0; i < elementOverrides.Count; i++)
            {
                ElementStyleOverride style = elementOverrides[i];
                if (style == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(style.ElementName))
                {
                    VisualElement named = root.Q<VisualElement>(style.ElementName);
                    if (named != null)
                    {
                        ApplyElementOverrides(named, style);
                    }
                }

                if (!string.IsNullOrWhiteSpace(style.ElementClass))
                {
                    List<VisualElement> matches = root.Query<VisualElement>(
                        className: style.ElementClass
                    )
                        .ToList();
                    for (int j = 0; j < matches.Count; j++)
                    {
                        if (matches[j] != null)
                        {
                            ApplyElementOverrides(matches[j], style);
                        }
                    }
                }
            }
        }

        private static void ApplyContainerOverrides(
            VisualElement target,
            DocumentStyleOverrides overrides
        )
        {
            if (target == null || overrides == null)
            {
                return;
            }

            if (overrides.OverrideLeft)
            {
                target.style.left = overrides.Left;
            }

            if (overrides.OverrideTop)
            {
                target.style.top = overrides.Top;
            }

            if (overrides.OverrideRight)
            {
                target.style.right = overrides.Right;
            }

            if (overrides.OverrideBottom)
            {
                target.style.bottom = overrides.Bottom;
            }

            if (overrides.OverrideWidth)
            {
                target.style.width = overrides.Width;
            }

            if (overrides.OverrideHeight)
            {
                target.style.height = overrides.Height;
            }

            if (overrides.OverrideOpacity)
            {
                target.style.opacity = overrides.Opacity;
            }

            if (overrides.OverrideBackgroundColor)
            {
                target.style.backgroundColor = overrides.BackgroundColor;
            }

            if (overrides.OverrideBorderRadius)
            {
                Length radius = new Length(overrides.BorderRadius, LengthUnit.Pixel);
                target.style.borderTopLeftRadius = radius;
                target.style.borderTopRightRadius = radius;
                target.style.borderBottomLeftRadius = radius;
                target.style.borderBottomRightRadius = radius;
            }
        }

        private static void ApplyElementOverrides(VisualElement target, ElementStyleOverride style)
        {
            if (target == null || style == null)
            {
                return;
            }

            if (style.OverrideWidth)
            {
                target.style.width = style.Width;
            }

            if (style.OverrideHeight)
            {
                target.style.height = style.Height;
            }

            if (style.OverrideBackgroundColor)
            {
                target.style.backgroundColor = style.BackgroundColor;
            }

            if (style.OverrideOpacity)
            {
                target.style.opacity = style.Opacity;
            }

            if (style.OverrideFontSize)
            {
                target.style.fontSize = style.FontSize;
            }

            if (style.OverrideFontAsset && style.FontAsset != null)
            {
                target.style.unityFont = style.FontAsset;
            }

            if (style.OverrideFontStyle)
            {
                target.style.unityFontStyleAndWeight = style.FontStyle;
            }

            if (style.OverrideTextColor)
            {
                target.style.color = style.TextColor;
            }

            if (target is TextField field)
            {
                bool isChatInput = string.Equals(
                    style.ElementName,
                    "chat-input",
                    StringComparison.OrdinalIgnoreCase
                );
                float minHeight = isChatInput
                    ? MinReadableChatInputHeight
                    : MinReadableTextFieldHeight;
                float clampedHeight = style.OverrideHeight
                    ? Mathf.Max(minHeight, style.Height)
                    : 0f;
                float clampedFontSize = style.OverrideFontSize
                    ? Mathf.Max(MinReadableTextFieldFontSize, style.FontSize)
                    : 0f;

                if (style.OverrideHeight)
                {
                    target.style.height = clampedHeight;
                    target.style.minHeight = clampedHeight;
                }

                if (style.OverrideFontSize)
                {
                    target.style.fontSize = clampedFontSize;
                }

                VisualElement input = ResolveTextFieldInput(field);
                if (input != null)
                {
                    if (style.OverrideFontSize)
                    {
                        input.style.fontSize = clampedFontSize;
                    }

                    if (style.OverrideFontAsset && style.FontAsset != null)
                    {
                        input.style.unityFont = style.FontAsset;
                    }

                    if (style.OverrideFontStyle)
                    {
                        input.style.unityFontStyleAndWeight = style.FontStyle;
                    }

                    if (style.OverrideTextColor)
                    {
                        input.style.color = style.TextColor;
                    }

                    if (style.OverrideBackgroundColor)
                    {
                        input.style.backgroundColor = style.BackgroundColor;
                    }

                    if (style.OverrideHeight)
                    {
                        input.style.height = clampedHeight;
                        input.style.minHeight = clampedHeight;
                    }

                    if (isChatInput)
                    {
                        input.style.paddingTop = 8f;
                        input.style.paddingBottom = 8f;
                        input.style.paddingLeft = 10f;
                        input.style.paddingRight = 10f;
                        input.style.unityTextAlign = TextAnchor.MiddleLeft;
                        input.style.whiteSpace = WhiteSpace.NoWrap;
                    }
                }
            }
        }

        private static VisualElement ResolveTextFieldInput(TextField field)
        {
            if (field == null)
            {
                return null;
            }

            VisualElement input = field.Q(className: "unity-text-input");
            if (input == null)
            {
                input = field.Q(className: "unity-base-field__input");
            }
            if (input == null)
            {
                input = field.Q(className: "unity-text-field__input");
            }
            return input;
        }
    }
}
