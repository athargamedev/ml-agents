using System;
using System.Collections;
using System.IO;
using System.Text;
using Network_Game.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// In-play feedback popup that appears shortly after an effect is dispatched.
    /// Asks "Did the effect work?" with ✓ Yes / ✗ No buttons and appends a
    /// visual_feedback record to the same feedback_log.jsonl used by
    /// DialogueFeedbackCollector — so build_dataset_from_feedback.py can join on
    /// timestamp + effect_name to know whether the visual actually fired correctly.
    ///
    /// Hook point : DialogueSceneEffectsController.OnEffectApplied (static event).
    /// No existing files are modified — just add this component to any active
    /// Canvas child in the scene (e.g. the same GameObject as DialogueClientUI).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class EffectFeedbackPopup : MonoBehaviour
    {
        // Legacy green/red popup is kept only for rollback history. Force-disable it.
        private const bool k_ForceDisableLegacyPopup = true;
        private const string k_LogCategory = "EffectFeedbackPopup";
        private const float k_PopupWidth = 320f;
        private const float k_PopupHeight = 76f;
        private const float k_ButtonWidth = 118f;
        private const float k_ButtonHeight = 28f;
        private const float k_LabelHeight = 20f;
        private const float k_TopOffset = -24f;
        private const float k_FontSizeLbl = 14f;
        private const float k_FontSizeBtn = 13f;

        // ── Inspector ────────────────────────────────────────────────────────
        [Header("Timing")]
        [Tooltip(
            "Legacy popup path. Keep disabled (new system uses DialogueEffectFeedbackPrompt)."
         )]
        [SerializeField]
        private bool m_EnableLegacyPopup = false;

        [Tooltip("Seconds after effect fires before the popup appears.")]
        [SerializeField]
        [Range(0.3f, 4f)]
        private float m_ShowDelaySeconds = 1.2f;

        [Tooltip("Seconds visible before auto-dismissing with no vote recorded.")]
        [SerializeField]
        [Range(3f, 15f)]
        private float m_AutoDismissSeconds = 7f;

        [Header("Output")]
        [Tooltip("Relative to project root. Must match DialogueFeedbackCollector path.")]
        [SerializeField]
        private string m_OutputPath = "output/feedback_log.jsonl";

        [Header("Style")]
        [SerializeField]
        private Color m_BgColor = new Color(0.05f, 0.06f, 0.08f, 0.92f);

        [SerializeField]
        private Color m_BorderColor = new Color(0.82f, 0.84f, 0.88f, 0.34f);

        [SerializeField]
        private Color m_YesColor = new Color(0.22f, 0.72f, 0.42f, 1f);

        [SerializeField]
        private Color m_NoColor = new Color(0.85f, 0.28f, 0.22f, 1f);

        [SerializeField]
        private Color m_LabelColor = new Color(0.93f, 0.93f, 0.93f, 1f);

        // ── Runtime ──────────────────────────────────────────────────────────
        private string m_ResolvedOutputPath;
        private Canvas m_Canvas;
        private RectTransform m_PopupRoot;
        private TMP_Text m_Label;

        private Coroutine m_ShowCoroutine;
        private Coroutine m_DismissCoroutine;
        private PendingData m_Pending;

        private struct PendingData
        {
            public string EffectName;
            public string EffectType;
            public string Timestamp;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────
        private void Awake()
        {
            if (ShouldDisableLegacyPopup())
            {
                DisableLegacyPopup("awake_guard");
                return;
            }

            m_ResolvedOutputPath = ResolveOutputPath(m_OutputPath);
            EnsureOutputDir(m_ResolvedOutputPath);

            m_Canvas = GetComponentInParent<Canvas>();
            if (m_Canvas == null)
                m_Canvas = FindAnyObjectByType<Canvas>();

            if (m_Canvas == null)
            {
                NGLog.Error(
                    k_LogCategory,
                    NGLog.Format("No Canvas found. Add this component under a Canvas.")
                );
                enabled = false;
                return;
            }

            BuildUI();
            SetVisible(false);
        }

        private void OnEnable()
        {
            if (ShouldDisableLegacyPopup())
            {
                DisableLegacyPopup("on_enable_guard");
                return;
            }

            DialogueSceneEffectsController.OnEffectApplied += OnEffectApplied;
        }

        private void OnDisable() =>
            DialogueSceneEffectsController.OnEffectApplied -= OnEffectApplied;

        private void Update()
        {
            if (HasModernPrompt())
            {
                DisableLegacyPopup("modern_prompt_detected");
            }
        }

        // ── Effect handler ───────────────────────────────────────────────────
        private void OnEffectApplied(DialogueSceneEffectsController.AppliedEffectInfo info)
        {
            if (ShouldDisableLegacyPopup())
            {
                DisableLegacyPopup("effect_received_but_disabled");
                return;
            }

            // Bored lighting is ambient — not a player-requested effect, skip it
            if (
                string.Equals(info.EffectType, "bored_lighting", StringComparison.OrdinalIgnoreCase)
            )
                return;

            CancelPending();
            SetVisible(false);

            m_Pending = new PendingData
            {
                EffectName = string.IsNullOrEmpty(info.EffectName)
                    ? info.EffectType
                    : info.EffectName,
                EffectType = info.EffectType ?? string.Empty,
                Timestamp = DateTime.UtcNow.ToString("o"),
            };

            m_ShowCoroutine = StartCoroutine(ShowAfterDelay());
        }

        // ── Show / hide ──────────────────────────────────────────────────────
        private IEnumerator ShowAfterDelay()
        {
            yield return new WaitForSecondsRealtime(m_ShowDelaySeconds);
            m_ShowCoroutine = null;

            if (ShouldDisableLegacyPopup())
            {
                DisableLegacyPopup("show_delay_guard");
                yield break;
            }

            if (m_Label != null)
                m_Label.text = $"Did <b>{m_Pending.EffectName}</b> work?";

            SetVisible(true);
            m_DismissCoroutine = StartCoroutine(AutoDismiss());
        }

        private IEnumerator AutoDismiss()
        {
            yield return new WaitForSecondsRealtime(m_AutoDismissSeconds);
            m_DismissCoroutine = null;
            SetVisible(false);
        }

        private void SetVisible(bool v)
        {
            if (m_PopupRoot != null)
                m_PopupRoot.gameObject.SetActive(v);
        }

        private void CancelPending()
        {
            if (m_ShowCoroutine != null)
            {
                StopCoroutine(m_ShowCoroutine);
                m_ShowCoroutine = null;
            }
            if (m_DismissCoroutine != null)
            {
                StopCoroutine(m_DismissCoroutine);
                m_DismissCoroutine = null;
            }
        }

        // ── Vote callbacks ───────────────────────────────────────────────────
        private void VoteYes()
        {
            Vote(true);
        }

        private void VoteNo()
        {
            Vote(false);
        }

        private void Vote(bool ok)
        {
            CancelPending();
            WriteRecord(ok);
            SetVisible(false);
        }

        // ── JSONL writer ─────────────────────────────────────────────────────
        private void WriteRecord(bool visualOk)
        {
            string line = string.Format(
                "{{\"ts\":\"{0}\",\"record_type\":\"visual_feedback\","
                + "\"effect_name\":\"{1}\",\"effect_type\":\"{2}\","
                + "\"visual_ok\":{3}}}",
                EscapeJson(m_Pending.Timestamp),
                EscapeJson(m_Pending.EffectName),
                EscapeJson(m_Pending.EffectType),
                visualOk ? "true" : "false"
            );

            try
            {
#if !UNITY_WEBGL
                File.AppendAllText(m_ResolvedOutputPath, line + "\n", Encoding.UTF8);
#endif
                NGLog.Info(
                    k_LogCategory,
                    NGLog.Format(
                        "Visual feedback recorded",
                        ("effect", m_Pending.EffectName),
                        ("visual_ok", visualOk)
                    )
                );
            }
            catch (IOException ex)
            {
                NGLog.Error(k_LogCategory, NGLog.Format("Write failed", ("error", ex.Message)));
            }
            catch (UnauthorizedAccessException ex)
            {
                NGLog.Error(k_LogCategory, NGLog.Format("Write failed", ("error", ex.Message)));
            }
        }

        // ── UI builder ───────────────────────────────────────────────────────
        private void BuildUI()
        {
            RectTransform canvasRect = m_Canvas.GetComponent<RectTransform>();
            m_PopupRoot = BuildPanel(canvasRect);
            m_Label = BuildLabel(m_PopupRoot);
            BuildButtonRow(m_PopupRoot);
        }

        private bool ShouldDisableLegacyPopup()
        {
            return k_ForceDisableLegacyPopup || !m_EnableLegacyPopup || HasModernPrompt();
        }

        private static bool HasModernPrompt()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<DialogueEffectFeedbackPrompt>() != null;
#else
            return FindAnyObjectByType<DialogueEffectFeedbackPrompt>() != null;
#endif
        }

        private void DisableLegacyPopup(string reason)
        {
            CancelPending();
            SetVisible(false);
            DialogueSceneEffectsController.OnEffectApplied -= OnEffectApplied;
            enabled = false;
            NGLog.Info(k_LogCategory, NGLog.Format("Legacy popup disabled", ("reason", reason)));
        }

        private RectTransform BuildPanel(RectTransform canvasRect)
        {
            GameObject go = new GameObject(
                "EffectFeedbackPopup",
                typeof(RectTransform),
                typeof(Image),
                typeof(Outline)
            );
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvasRect, false);
            rt.SetAsLastSibling();

            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, k_TopOffset);
            rt.sizeDelta = new Vector2(k_PopupWidth, k_PopupHeight);

            go.GetComponent<Image>().color = m_BgColor;

            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = m_BorderColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.padding = new RectOffset(12, 12, 8, 8);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            return rt;
        }

        private TMP_Text BuildLabel(RectTransform parent)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "Did the effect work?";
            tmp.color = m_LabelColor;
            tmp.fontSize = k_FontSizeLbl;
            tmp.alignment = TextAlignmentOptions.Center;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = k_LabelHeight;

            return tmp;
        }

        private void BuildButtonRow(RectTransform parent)
        {
            GameObject row = new GameObject("ButtonRow", typeof(RectTransform));
            row.transform.SetParent(parent, false);

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 12f;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = k_ButtonHeight + 2f;

            MakeButton(row.transform, "YesBtn", "✓  Yes", m_YesColor, VoteYes);
            MakeButton(row.transform, "NoBtn", "✗  No", m_NoColor, VoteNo);
        }

        private void MakeButton(
            Transform parent,
            string goName,
            string label,
            Color bgColor,
            Action onClick
        )
        {
            GameObject go = new GameObject(
                goName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button)
            );
            go.transform.SetParent(parent, false);

            go.GetComponent<RectTransform>().sizeDelta = new Vector2(k_ButtonWidth, k_ButtonHeight);

            Image img = go.GetComponent<Image>();
            img.color = bgColor;

            Button btn = go.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = bgColor;
            cb.highlightedColor = bgColor * 1.18f;
            cb.pressedColor = bgColor * 0.80f;
            cb.selectedColor = bgColor;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick());

            // Text child
            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.color = Color.white;
            tmp.fontSize = k_FontSizeBtn;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static string ResolveOutputPath(string configured)
        {
            if (Path.IsPathRooted(configured))
                return configured;
            return Path.GetFullPath(
                Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), configured)
            );
        }

        private static void EnsureOutputDir(string filePath)
        {
#if !UNITY_WEBGL
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
#endif
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        // ── Editor test ──────────────────────────────────────────────────────
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Dialogue/Effect Feedback Popup/Test Fire")]
        private static void EditorTestFire()
        {
            var inst = FindAnyObjectByType<EffectFeedbackPopup>();
            if (inst == null)
            {
                Debug.Log("[EffectFeedbackPopup] Not in scene.");
                return;
            }
            inst.OnEffectApplied(
                new DialogueSceneEffectsController.AppliedEffectInfo
                {
                    EffectName = "Dissolve",
                    EffectType = "prefab_power",
                    AppliedAtRealtime = Time.realtimeSinceStartup,
                }
            );
        }

#endif
    }
}
