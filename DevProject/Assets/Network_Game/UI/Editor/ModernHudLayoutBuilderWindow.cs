using UnityEditor;
using UnityEngine;

namespace Network_Game.UI.Editor
{
    public sealed class ModernHudLayoutBuilderWindow : EditorWindow
    {
        private enum DragHandle
        {
            None,
            Margin,
            TopBarHeight,
            LeftDockWidth,
            RightDockWidth,
            BottomBarHeight,
            FeedbackSummarySplit,
            FeedbackActionsSplit,
        }

        private const string kDefaultAssetPath =
            "Assets/Network_Game/UI/Theme/Content/ModernHudLayoutProfile.asset";

        private ModernHudController m_Controller;
        private ModernHudLayoutProfile m_Profile;
        private SerializedObject m_ProfileSerializedObject;
        private bool m_EnableSceneOverlay = true;
        private DragHandle m_ActiveDragHandle;

        [MenuItem("Network Game/UI/Modern HUD Layout Builder")]
        private static void Open()
        {
            var window = GetWindow<ModernHudLayoutBuilderWindow>("HUD Layout Builder");
            window.minSize = new Vector2(560f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;
            SceneView.duringSceneGui += OnSceneViewGUI;
            AutoResolveTargets();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;
            m_ActiveDragHandle = DragHandle.None;
        }

        private void OnFocus()
        {
            AutoResolveTargets();
        }

        private void OnHierarchyChange()
        {
            AutoResolveTargets();
            Repaint();
        }

        private void OnSelectionChange()
        {
            AutoResolveTargets();
            Repaint();
        }

        private void OnGUI()
        {
            AutoResolveTargets();

            EditorGUILayout.HelpBox(
                "Edit the HUD as viewport-relative zones. The runtime HUD reads this profile, so you can tune layout without touching code.",
                MessageType.Info
            );

            m_EnableSceneOverlay = EditorGUILayout.ToggleLeft(
                "Enable Scene View layout overlay",
                m_EnableSceneOverlay
            );

            EditorGUI.BeginChangeCheck();
            m_Controller = (ModernHudController)
                EditorGUILayout.ObjectField(
                "Modern HUD Root",
                m_Controller,
                typeof(ModernHudController),
                true
                );
            if (EditorGUI.EndChangeCheck())
            {
                SyncProfileFromController();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            m_Profile = (ModernHudLayoutProfile)
                EditorGUILayout.ObjectField(
                "Layout Profile",
                m_Profile,
                typeof(ModernHudLayoutProfile),
                false
                );
            if (EditorGUI.EndChangeCheck())
            {
                AssignProfileToController();
            }

            if (GUILayout.Button("Create", GUILayout.Width(70f)))
            {
                CreateProfileAsset();
            }
            EditorGUILayout.EndHorizontal();

            if (m_Profile == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign or create a ModernHudLayoutProfile to drive the HUD zones.",
                    MessageType.Warning
                );
                DrawFallbackPreview();
                return;
            }

            EnsureSerializedProfile();

            m_ProfileSerializedObject.Update();

            EditorGUILayout.Space(6f);
            DrawSlider("Outer Margin %", "OuterMarginPercent", 0f, 0.05f);
            DrawSlider("Dock Gap %", "DockGapPercent", 0f, 0.05f);
            DrawSlider("Top Bar Top %", "TopBarTopPercent", 0f, 0.05f);
            DrawSlider("Top Bar Height %", "TopBarReservedHeightPercent", 0.08f, 0.35f);
            DrawFloat("Top Bar Min Px", "TopBarMinHeightPx");
            DrawSlider("Left Dock Width %", "LeftDockWidthPercent", 0.15f, 0.45f);
            DrawSlider("Right Dock Width %", "RightDockWidthPercent", 0.18f, 0.50f);
            DrawFloat("Min Dock Px", "MinDockWidthPx");
            DrawSlider("Bottom Bar Height %", "BottomBarHeightPercent", 0.12f, 0.40f);
            DrawSlider("Feedback Summary Row %", "FeedbackSummaryRowPercent", 0.15f, 0.70f);
            DrawSlider("Feedback Actions Row %", "FeedbackActionsRowPercent", 0.10f, 0.70f);
            DrawSlider("Feedback Notes Row %", "FeedbackNotesRowPercent", 0.10f, 0.70f);

            bool changed = m_ProfileSerializedObject.ApplyModifiedProperties();
            if (changed)
            {
                EditorUtility.SetDirty(m_Profile);
                AssetDatabase.SaveAssets();
                Repaint();
            }

            EditorGUILayout.Space(10f);
            DrawPreview();

            EditorGUILayout.Space(8f);
            if (m_Controller != null)
            {
                if (GUILayout.Button("Ping HUD Root"))
                {
                    EditorGUIUtility.PingObject(m_Controller.gameObject);
                    Selection.activeObject = m_Controller.gameObject;
                }
            }
        }

        private void AutoResolveTargets()
        {
            if (m_Controller == null)
            {
                m_Controller = FindAnyObjectByType<ModernHudController>();
            }

            SyncProfileFromController();
        }

        private void SyncProfileFromController()
        {
            if (m_Controller != null && m_Controller.LayoutProfile != m_Profile)
            {
                m_Profile = m_Controller.LayoutProfile;
                m_ProfileSerializedObject = null;
            }
        }

        private void AssignProfileToController()
        {
            m_ProfileSerializedObject = null;

            if (m_Controller == null)
            {
                return;
            }

            Undo.RecordObject(m_Controller, "Assign HUD Layout Profile");
            m_Controller.SetLayoutProfile(m_Profile);
            EditorUtility.SetDirty(m_Controller);
        }

        private void CreateProfileAsset()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(kDefaultAssetPath);
            var asset = CreateInstance<ModernHudLayoutProfile>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            m_Profile = asset;
            m_ProfileSerializedObject = null;
            AssignProfileToController();
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        private void EnsureSerializedProfile()
        {
            if (
                m_ProfileSerializedObject == null
                || m_ProfileSerializedObject.targetObject != m_Profile
            )
            {
                m_ProfileSerializedObject = new SerializedObject(m_Profile);
            }
        }

        private void DrawSlider(string label, string propertyName, float min, float max)
        {
            SerializedProperty property = m_ProfileSerializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            EditorGUILayout.Slider(property, min, max, label);
        }

        private void DrawFloat(string label, string propertyName)
        {
            SerializedProperty property = m_ProfileSerializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            EditorGUILayout.PropertyField(property, new GUIContent(label));
        }

        private void DrawFallbackPreview()
        {
            Rect previewRect = GUILayoutUtility.GetRect(520f, 280f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.09f));
            GUI.Label(previewRect, "No layout profile assigned", CenteredLabel());
        }

        private void DrawPreview()
        {
            Rect previewRect = GUILayoutUtility.GetRect(520f, 300f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.09f));
            LayoutRects rects = BuildLayoutRects(previewRect, m_Profile, false);
            DrawLayoutRects(rects);
        }

        private void OnSceneViewGUI(SceneView sceneView)
        {
            if (!m_EnableSceneOverlay || m_Profile == null)
            {
                m_ActiveDragHandle = DragHandle.None;
                return;
            }

            Rect sceneRect = new Rect(16f, 28f, sceneView.position.width - 32f, sceneView.position.height - 46f);
            if (sceneRect.width < 120f || sceneRect.height < 120f)
            {
                return;
            }

            Event evt = Event.current;
            LayoutRects rects = BuildLayoutRects(sceneRect, m_Profile, true);

            Handles.BeginGUI();
            DrawLayoutRects(rects);
            HandleSceneDrag(evt, sceneView, sceneRect, rects);
            Handles.EndGUI();
        }

        private void HandleSceneDrag(Event evt, SceneView sceneView, Rect bounds, LayoutRects rects)
        {
            Rect marginHandle = new Rect(
                rects.TopBar.x - 6f,
                rects.TopBar.y - 6f,
                12f,
                12f
            );
            Rect topBarHandle = new Rect(
                rects.TopBar.center.x - 10f,
                rects.TopBar.yMax - 6f,
                20f,
                12f
            );
            Rect leftDockHandle = new Rect(
                rects.TopLeft.xMax - 6f,
                rects.TopLeft.center.y - 10f,
                12f,
                20f
            );
            Rect rightDockHandle = new Rect(
                rects.RightDock.x - 6f,
                rects.RightDock.center.y - 10f,
                12f,
                20f
            );
            Rect bottomBarHandle = new Rect(
                rects.BottomBar.center.x - 10f,
                rects.BottomBar.y - 6f,
                20f,
                12f
            );
            Rect feedbackSummaryHandle = new Rect(
                rects.FeedbackSummaryRow.center.x - 12f,
                rects.FeedbackSummaryRow.yMax - 5f,
                24f,
                10f
            );
            Rect feedbackActionsHandle = new Rect(
                rects.FeedbackActionsRow.center.x - 12f,
                rects.FeedbackActionsRow.yMax - 5f,
                24f,
                10f
            );

            DrawHandleRect(marginHandle, MouseCursor.ResizeUpLeft);
            DrawHandleRect(topBarHandle, MouseCursor.ResizeVertical);
            DrawHandleRect(leftDockHandle, MouseCursor.ResizeHorizontal);
            DrawHandleRect(rightDockHandle, MouseCursor.ResizeHorizontal);
            DrawHandleRect(bottomBarHandle, MouseCursor.ResizeVertical);
            DrawHandleRect(feedbackSummaryHandle, MouseCursor.ResizeVertical);
            DrawHandleRect(feedbackActionsHandle, MouseCursor.ResizeVertical);

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (marginHandle.Contains(evt.mousePosition))
                {
                    m_ActiveDragHandle = DragHandle.Margin;
                }
                else if (topBarHandle.Contains(evt.mousePosition))
                {
                    m_ActiveDragHandle = DragHandle.TopBarHeight;
                }
                else if (leftDockHandle.Contains(evt.mousePosition))
                {
                    m_ActiveDragHandle = DragHandle.LeftDockWidth;
                }
                else if (rightDockHandle.Contains(evt.mousePosition))
                {
                    m_ActiveDragHandle = DragHandle.RightDockWidth;
                }
                else if (bottomBarHandle.Contains(evt.mousePosition))
                {
                    m_ActiveDragHandle = DragHandle.BottomBarHeight;
                }
                else if (feedbackSummaryHandle.Contains(evt.mousePosition))
                {
                    m_ActiveDragHandle = DragHandle.FeedbackSummarySplit;
                }
                else if (feedbackActionsHandle.Contains(evt.mousePosition))
                {
                    m_ActiveDragHandle = DragHandle.FeedbackActionsSplit;
                }

                if (m_ActiveDragHandle != DragHandle.None)
                {
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && m_ActiveDragHandle != DragHandle.None)
            {
                Undo.RecordObject(m_Profile, "Adjust HUD Layout");

                switch (m_ActiveDragHandle)
                {
                    case DragHandle.Margin:
                        m_Profile.OuterMarginPercent = Mathf.Clamp(
                            evt.mousePosition.x / Mathf.Max(1f, bounds.width),
                            0f,
                            0.05f
                        );
                        break;
                    case DragHandle.TopBarHeight:
                        m_Profile.TopBarReservedHeightPercent = Mathf.Clamp(
                            (evt.mousePosition.y - rects.Bounds.y) / Mathf.Max(1f, bounds.height),
                            0.08f,
                            0.35f
                        );
                        break;
                    case DragHandle.LeftDockWidth:
                        m_Profile.LeftDockWidthPercent = Mathf.Clamp(
                            (evt.mousePosition.x - rects.Bounds.x) / Mathf.Max(1f, bounds.width),
                            0.15f,
                            0.45f
                        );
                        break;
                    case DragHandle.RightDockWidth:
                        m_Profile.RightDockWidthPercent = Mathf.Clamp(
                            (rects.Bounds.xMax - evt.mousePosition.x) / Mathf.Max(1f, bounds.width),
                            0.18f,
                            0.50f
                        );
                        break;
                    case DragHandle.BottomBarHeight:
                        m_Profile.BottomBarHeightPercent = Mathf.Clamp(
                            (rects.Bounds.yMax - evt.mousePosition.y) / Mathf.Max(1f, bounds.height),
                            0.12f,
                            0.40f
                        );
                        break;
                    case DragHandle.FeedbackSummarySplit:
                        ApplyFeedbackSummaryDrag(rects, evt.mousePosition.y);
                        break;
                    case DragHandle.FeedbackActionsSplit:
                        ApplyFeedbackActionsDrag(rects, evt.mousePosition.y);
                        break;
                }

                EditorUtility.SetDirty(m_Profile);
                Repaint();
                sceneView.Repaint();
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp)
            {
                if (m_ActiveDragHandle != DragHandle.None)
                {
                    AssetDatabase.SaveAssets();
                    m_ActiveDragHandle = DragHandle.None;
                    evt.Use();
                }
            }
        }

        private static void DrawHandleRect(Rect rect, MouseCursor cursor)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.9f));
            EditorGUIUtility.AddCursorRect(rect, cursor);
        }

        private static void DrawLayoutRects(LayoutRects rects)
        {
            DrawZone(rects.TopBar, new Color(0.18f, 0.34f, 0.48f, 0.72f), "Top Bar");
            DrawZone(rects.FeedbackSummaryRow, new Color(0.24f, 0.49f, 0.68f, 0.64f), "Feedback Summary");
            DrawZone(rects.FeedbackActionsRow, new Color(0.20f, 0.41f, 0.60f, 0.64f), "Feedback Actions");
            DrawZone(rects.FeedbackNotesRow, new Color(0.16f, 0.32f, 0.52f, 0.64f), "Feedback Notes");
            DrawZone(rects.TopLeft, new Color(0.20f, 0.44f, 0.27f, 0.72f), "Top Left");
            DrawZone(rects.RightDock, new Color(0.46f, 0.30f, 0.17f, 0.72f), "Right Dock");
            DrawZone(rects.BottomBar, new Color(0.32f, 0.22f, 0.46f, 0.72f), "Bottom Bar");
            DrawZone(rects.SafeArea, new Color(0.14f, 0.14f, 0.14f, 0.55f), "Gameplay Safe Area");
        }

        private static LayoutRects BuildLayoutRects(
            Rect bounds,
            ModernHudLayoutProfile profile,
            bool sceneOverlay
        )
        {
            float width = bounds.width;
            float height = bounds.height;
            float minMargin = sceneOverlay ? 10f : 8f;
            float margin = Mathf.Max(minMargin, width * profile.OuterMarginPercent);
            float gap = Mathf.Max(6f, width * profile.DockGapPercent);
            float topInset = Mathf.Max(6f, height * profile.TopBarTopPercent);
            float minTopBar = sceneOverlay ? profile.TopBarMinHeightPx * 0.60f : profile.TopBarMinHeightPx * 0.45f;
            float topBarHeight = Mathf.Max(minTopBar, height * profile.TopBarReservedHeightPercent);
            float bottomBarHeight = Mathf.Max(sceneOverlay ? 72f : 58f, height * profile.BottomBarHeightPercent);

            float centerReserve = Mathf.Max(sceneOverlay ? 180f : 120f, width * 0.20f);
            float availableDockWidth = Mathf.Max(220f, width - centerReserve - (margin * 2f) - gap);
            float leftDockWidth = Mathf.Max(profile.MinDockWidthPx * (sceneOverlay ? 0.70f : 0.54f), width * profile.LeftDockWidthPercent);
            float rightDockWidth = Mathf.Max(profile.MinDockWidthPx * (sceneOverlay ? 0.70f : 0.54f), width * profile.RightDockWidthPercent);

            float totalDockWidth = leftDockWidth + rightDockWidth;
            if (totalDockWidth > availableDockWidth && totalDockWidth > 0f)
            {
                float scale = availableDockWidth / totalDockWidth;
                leftDockWidth *= scale;
                rightDockWidth *= scale;
            }

            Rect topBar = new Rect(
                bounds.x + margin,
                bounds.y + topInset,
                width - (margin * 2f),
                topBarHeight
            );
            profile.GetNormalizedFeedbackRowWeights(
                out float summaryWeight,
                out float actionsWeight,
                out float notesWeight
            );
            float innerTopPadding = Mathf.Max(6f, topBar.height * 0.08f);
            float innerBottomPadding = Mathf.Max(6f, topBar.height * 0.08f);
            float rowGap = Mathf.Max(3f, topBar.height * 0.03f);
            float availableRowHeight = Mathf.Max(
                24f,
                topBar.height - innerTopPadding - innerBottomPadding - (rowGap * 2f)
            );
            float summaryHeight = availableRowHeight * summaryWeight;
            float actionsHeight = availableRowHeight * actionsWeight;
            float notesHeight = availableRowHeight * notesWeight;
            float rowX = topBar.x + 10f;
            float rowWidth = Mathf.Max(40f, topBar.width - 20f);
            float rowY = topBar.y + innerTopPadding;
            Rect feedbackSummaryRow = new Rect(rowX, rowY, rowWidth, summaryHeight);
            rowY += summaryHeight + rowGap;
            Rect feedbackActionsRow = new Rect(rowX, rowY, rowWidth, actionsHeight);
            rowY += actionsHeight + rowGap;
            Rect feedbackNotesRow = new Rect(rowX, rowY, rowWidth, notesHeight);
            Rect topLeft = new Rect(
                bounds.x + margin,
                bounds.y + topInset + topBarHeight + gap,
                leftDockWidth,
                Mathf.Max(70f, height * 0.24f)
            );
            Rect rightDock = new Rect(
                bounds.xMax - margin - rightDockWidth,
                bounds.y + topInset + topBarHeight + gap,
                rightDockWidth,
                Mathf.Max(70f, height * 0.24f)
            );
            Rect bottomBar = new Rect(
                bounds.x + margin,
                bounds.yMax - margin - bottomBarHeight,
                width - (margin * 2f),
                bottomBarHeight
            );

            Rect safeArea = new Rect(
                topLeft.xMax + gap,
                topLeft.y,
                Mathf.Max(48f, rightDock.xMin - topLeft.xMax - (gap * 2f)),
                Mathf.Max(42f, bottomBar.yMin - topLeft.y - gap)
            );

            return new LayoutRects
            {
                Bounds = bounds,
                Margin = margin,
                Gap = gap,
                FeedbackRowGap = rowGap,
                TopBar = topBar,
                FeedbackSummaryRow = feedbackSummaryRow,
                FeedbackActionsRow = feedbackActionsRow,
                FeedbackNotesRow = feedbackNotesRow,
                TopLeft = topLeft,
                RightDock = rightDock,
                BottomBar = bottomBar,
                SafeArea = safeArea,
            };
        }

        private void ApplyFeedbackSummaryDrag(LayoutRects rects, float mouseY)
        {
            float top = rects.FeedbackSummaryRow.yMin;
            float bottom = rects.FeedbackNotesRow.yMax;
            float boundaryY = Mathf.Clamp(mouseY, top + 12f, bottom - 12f);

            float summaryHeight = boundaryY - top;
            float notesHeight = rects.FeedbackNotesRow.height;
            float actionsHeight = Mathf.Max(
                12f,
                rects.FeedbackNotesRow.yMin - boundaryY - (rects.FeedbackRowGap * 2f)
            );
            float total = Mathf.Max(1f, summaryHeight + actionsHeight + notesHeight);

            m_Profile.FeedbackSummaryRowPercent = summaryHeight / total;
            m_Profile.FeedbackActionsRowPercent = actionsHeight / total;
            m_Profile.FeedbackNotesRowPercent = notesHeight / total;
        }

        private void ApplyFeedbackActionsDrag(LayoutRects rects, float mouseY)
        {
            float top = rects.FeedbackActionsRow.yMin;
            float bottom = rects.FeedbackNotesRow.yMax;
            float boundaryY = Mathf.Clamp(mouseY, top + 12f, bottom - 12f);

            float actionsHeight = boundaryY - top;
            float notesHeight = Mathf.Max(12f, bottom - boundaryY - rects.FeedbackRowGap);
            float summaryHeight = rects.FeedbackSummaryRow.height;
            float total = Mathf.Max(1f, summaryHeight + actionsHeight + notesHeight);

            m_Profile.FeedbackSummaryRowPercent = summaryHeight / total;
            m_Profile.FeedbackActionsRowPercent = actionsHeight / total;
            m_Profile.FeedbackNotesRowPercent = notesHeight / total;
        }

        private static void DrawZone(Rect rect, Color color, string label)
        {
            EditorGUI.DrawRect(rect, color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.18f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.18f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.18f));
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.18f));
            GUI.Label(rect, label, CenteredLabel());
        }

        private static GUIStyle CenteredLabel()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
        }

        private struct LayoutRects
        {
            public Rect Bounds;
            public float Margin;
            public float Gap;
            public float FeedbackRowGap;
            public Rect TopBar;
            public Rect FeedbackSummaryRow;
            public Rect FeedbackActionsRow;
            public Rect FeedbackNotesRow;
            public Rect TopLeft;
            public Rect RightDock;
            public Rect BottomBar;
            public Rect SafeArea;
        }
    }
}
