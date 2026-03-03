using System;
using System.Collections.Generic;
using System.IO;
using Network_Game.Diagnostics;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Learns lightweight visual tuning hints from effect feedback submissions.
    /// Used to adapt future effect scale/duration/attach-fit choices during runtime tests.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(4900)]
    public sealed class DialogueEffectFeedbackRuntimeTuner : MonoBehaviour
    {
        private const string kLogCategory = "DialogueFX";
        private static DialogueEffectFeedbackRuntimeTuner s_Instance;

        public struct TuningAdjustment
        {
            public float ScaleMultiplier;
            public float DurationMultiplier;
            public bool PreferAttachToTarget;
            public bool PreferFitToTargetMesh;
            public int SampleCount;
        }

        [Serializable]
        private sealed class TuningEntry
        {
            public string key = string.Empty;
            public string effectType = string.Empty;
            public float scaleMultiplier = 1f;
            public float durationMultiplier = 1f;
            public float attachScore;
            public float fitScore;
            public int sampleCount;
            public int looksCorrectCount; // P2.4: visibility ratio numerator
            public string lastOutcome = string.Empty;
            public string lastUpdatedUtc = string.Empty;
        }

        [Serializable]
        private sealed class TuningStateFile
        {
            public int schemaVersion = 1;
            public string updatedUtc = string.Empty;
            public List<TuningEntry> entries = new List<TuningEntry>();
        }

        [Header("Adaptive Tuning")]
        [SerializeField]
        private bool m_EnableAdaptiveTuning = true;

        [SerializeField]
        [Min(0f)]
        [Tooltip("How strongly each feedback submission changes tuning values.")]
        private float m_LearningRate = 0.05f;

        [SerializeField]
        [Min(0f)]
        private float m_MinScaleMultiplier = 0.35f;

        [SerializeField]
        [Min(0f)]
        private float m_MaxScaleMultiplier = 3.0f;

        [SerializeField]
        [Min(0f)]
        private float m_MinDurationMultiplier = 0.5f;

        [SerializeField]
        [Min(0f)]
        private float m_MaxDurationMultiplier = 2.5f;

        [SerializeField]
        [Range(0f, 1f)]
        private float m_AttachPreferenceThreshold = 0.4f;

        [SerializeField]
        [Range(0f, 1f)]
        private float m_FitPreferenceThreshold = 0.3f;

        [Header("Persistence")]
        [SerializeField]
        [Tooltip("Path for saved adaptive tuning data. Relative path resolves from project root.")]
        private string m_OutputPath = "output/effect_feedback_tuning.json";

        [SerializeField]
        [Min(0.1f)]
        private float m_SaveDebounceSeconds = 1.0f;

        [SerializeField]
        private bool m_LogUpdates = true;

        private readonly Dictionary<string, TuningEntry> m_ByKey = new Dictionary<
            string,
            TuningEntry
            >(StringComparer.OrdinalIgnoreCase);
        private readonly List<TuningEntry> m_Entries = new List<TuningEntry>();
        private string m_ResolvedOutputPath = string.Empty;
        private bool m_Dirty;
        private float m_NextSaveTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeTuner()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (FindExistingInstance() != null)
            {
                return;
            }

            var go = new GameObject("DialogueEffectFeedbackRuntimeTuner");
            DontDestroyOnLoad(go);
            go.AddComponent<DialogueEffectFeedbackRuntimeTuner>();
        }

        private static DialogueEffectFeedbackRuntimeTuner FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<DialogueEffectFeedbackRuntimeTuner>();
#else
            return FindAnyObjectByType<DialogueEffectFeedbackRuntimeTuner>();
#endif
        }

        public static bool TryGetAdjustment(
            string effectName,
            string effectType,
            out TuningAdjustment adjustment
        )
        {
            adjustment = default;
            DialogueEffectFeedbackRuntimeTuner instance = s_Instance;
            if (instance == null || !instance.m_EnableAdaptiveTuning)
            {
                return false;
            }

            string key = BuildEffectKey(effectName, effectType);
            if (
                string.IsNullOrWhiteSpace(key)
                || !instance.m_ByKey.TryGetValue(key, out TuningEntry entry)
            )
            {
                return false;
            }

            adjustment = new TuningAdjustment
            {
                ScaleMultiplier = Mathf.Clamp(
                    entry.scaleMultiplier,
                    instance.m_MinScaleMultiplier,
                    instance.m_MaxScaleMultiplier
                    ),
                DurationMultiplier = Mathf.Clamp(
                    entry.durationMultiplier,
                    instance.m_MinDurationMultiplier,
                    instance.m_MaxDurationMultiplier
                    ),
                PreferAttachToTarget = entry.attachScore >= instance.m_AttachPreferenceThreshold,
                PreferFitToTargetMesh = entry.fitScore >= instance.m_FitPreferenceThreshold,
                SampleCount = Mathf.Max(0, entry.sampleCount),
            };
            return true;
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            ResolveOutputPath();
            LoadState();
        }

        private void OnEnable()
        {
            DialogueEffectFeedbackPrompt.OnFeedbackSubmitted += HandleFeedbackSubmitted;
        }

        private void OnDisable()
        {
            DialogueEffectFeedbackPrompt.OnFeedbackSubmitted -= HandleFeedbackSubmitted;
            if (s_Instance == this)
            {
                TrySaveState(force: true);
            }
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                DialogueEffectFeedbackPrompt.OnFeedbackSubmitted -= HandleFeedbackSubmitted;
                TrySaveState(force: true);
                s_Instance = null;
            }
        }

        private void Update()
        {
            if (!m_Dirty || Time.unscaledTime < m_NextSaveTime)
            {
                return;
            }

            TrySaveState(force: false);
        }

        private void HandleFeedbackSubmitted(
            DialogueEffectFeedbackPrompt.FeedbackSubmission submission
        )
        {
            if (!m_EnableAdaptiveTuning)
            {
                return;
            }

            string key = BuildEffectKey(submission.Effect.EffectName, submission.Effect.EffectType);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!m_ByKey.TryGetValue(key, out TuningEntry entry))
            {
                entry = new TuningEntry
                {
                    key = key,
                    effectType = submission.Effect.EffectType ?? string.Empty,
                    scaleMultiplier = 1f,
                    durationMultiplier = 1f,
                    attachScore = 0f,
                    fitScore = 0f,
                    sampleCount = 0,
                };
                m_ByKey[key] = entry;
                m_Entries.Add(entry);
            }

            ApplyOutcome(entry, submission.Outcome);
            entry.sampleCount++;
            entry.lastOutcome = submission.Outcome ?? string.Empty;
            entry.lastUpdatedUtc = DateTime.UtcNow.ToString("o");

            m_Dirty = true;
            m_NextSaveTime = Time.unscaledTime + Mathf.Max(0.1f, m_SaveDebounceSeconds);

            if (m_LogUpdates)
            {
                NGLog.Info(
                    kLogCategory,
                    NGLog.Format(
                        "Adaptive FX tuning updated",
                        ("key", key),
                        ("outcome", submission.Outcome ?? string.Empty),
                        ("scaleMul", entry.scaleMultiplier.ToString("F3")),
                        ("durationMul", entry.durationMultiplier.ToString("F3")),
                        ("attachScore", entry.attachScore.ToString("F3")),
                        ("fitScore", entry.fitScore.ToString("F3")),
                        ("samples", entry.sampleCount)
                    )
                );
            }
        }

        private void ApplyOutcome(TuningEntry entry, string outcome)
        {
            float lr = Mathf.Clamp(m_LearningRate, 0.01f, 0.5f);
            string normalized = string.IsNullOrWhiteSpace(outcome)
                ? string.Empty
                : outcome.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "looks_correct":
                    entry.looksCorrectCount++; // P2.4: track visibility ratio
                    entry.scaleMultiplier = Mathf.Lerp(entry.scaleMultiplier, 1f, lr);
                    entry.durationMultiplier = Mathf.Lerp(entry.durationMultiplier, 1f, lr);
                    entry.attachScore = Mathf.Lerp(entry.attachScore, 0f, lr * 0.5f);
                    entry.fitScore = Mathf.Lerp(entry.fitScore, 0f, lr * 0.5f);
                    break;
                case "not_visible":
                    // P2.1 / P2.4 — positionally broken guard.
                    // If the effect has never been seen correctly in 6+ samples AND has a low
                    // attach score, scaling is the wrong fix — the spawn position is the problem.
                    // Continuing to scale here was producing ElectricalSparks=2.61×, FireBall=2.57×.
                    if (IsPositionallyBroken(entry))
                    {
                        NGLog.Warn(
                            kLogCategory,
                            NGLog.Format(
                                "Skipped scale-up: effect may be positionally broken (never visible in scene)",
                                ("key", entry.key),
                                ("samples", entry.sampleCount),
                                ("looksCorrect", entry.looksCorrectCount),
                                ("attachScore", entry.attachScore.ToString("F3"))
                            )
                        );
                        break;
                    }
                    entry.scaleMultiplier *= 1f + (0.14f * lr / 0.18f);
                    entry.durationMultiplier *= 1f + (0.10f * lr / 0.18f);
                    break;
                case "wrong_target":
                    entry.attachScore = Mathf.Clamp01(entry.attachScore + (0.28f * lr / 0.18f));
                    break;
                case "wrong_placement":
                    entry.attachScore = Mathf.Clamp01(entry.attachScore + (0.35f * lr / 0.18f));
                    entry.scaleMultiplier *= 1f - (0.05f * lr / 0.18f);
                    break;
                case "wrong_mesh_fit":
                    entry.fitScore = Mathf.Clamp01(entry.fitScore + (0.45f * lr / 0.18f));
                    entry.attachScore = Mathf.Clamp01(entry.attachScore + (0.20f * lr / 0.18f));
                    entry.scaleMultiplier *= 1f - (0.08f * lr / 0.18f);
                    break;
                case "skipped":
                case "note_only":
                default:
                    break;
            }

            entry.scaleMultiplier = Mathf.Clamp(
                entry.scaleMultiplier,
                m_MinScaleMultiplier,
                m_MaxScaleMultiplier
            );
            entry.durationMultiplier = Mathf.Clamp(
                entry.durationMultiplier,
                m_MinDurationMultiplier,
                m_MaxDurationMultiplier
            );
        }

        /// <summary>
        /// P2.1 — Returns true when the effect has been submitted enough times to be
        /// statistically confident, but has never once appeared correct on screen AND
        /// shows no preference for attach targeting. Under these conditions scaling will
        /// never fix visibility — the spawn anchor/position needs to be corrected instead.
        /// </summary>
        private static bool IsPositionallyBroken(TuningEntry entry)
        {
            return entry.sampleCount > 5
                && entry.looksCorrectCount == 0
                && entry.attachScore < 0.1f;
        }

        private static string BuildEffectKey(string effectName, string effectType)
        {
            string name = NormalizeToken(effectName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return NormalizeToken(effectType);
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().ToLowerInvariant();
        }

        private void ResolveOutputPath()
        {
            string configured = string.IsNullOrWhiteSpace(m_OutputPath)
                ? "output/effect_feedback_tuning.json"
                : m_OutputPath.Trim();

            if (Path.IsPathRooted(configured))
            {
                m_ResolvedOutputPath = configured;
            }
            else
            {
                string baseDir = Application.isEditor
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                    : Application.persistentDataPath;
                m_ResolvedOutputPath = Path.GetFullPath(Path.Combine(baseDir, configured));
            }

            string dir = Path.GetDirectoryName(m_ResolvedOutputPath);
#if !UNITY_WEBGL
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
#endif
        }

        private void LoadState()
        {
#if !UNITY_WEBGL
            if (
                string.IsNullOrWhiteSpace(m_ResolvedOutputPath)
                || !File.Exists(m_ResolvedOutputPath)
            )
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(m_ResolvedOutputPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                TuningStateFile file = JsonUtility.FromJson<TuningStateFile>(json);
                if (file == null || file.entries == null)
                {
                    return;
                }

                m_ByKey.Clear();
                m_Entries.Clear();
                for (int i = 0; i < file.entries.Count; i++)
                {
                    TuningEntry entry = file.entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                    {
                        continue;
                    }

                    entry.key = entry.key.Trim().ToLowerInvariant();
                    entry.scaleMultiplier = Mathf.Clamp(
                        entry.scaleMultiplier,
                        m_MinScaleMultiplier,
                        m_MaxScaleMultiplier
                    );
                    entry.durationMultiplier = Mathf.Clamp(
                        entry.durationMultiplier,
                        m_MinDurationMultiplier,
                        m_MaxDurationMultiplier
                    );
                    entry.attachScore = Mathf.Clamp01(entry.attachScore);
                    entry.fitScore = Mathf.Clamp01(entry.fitScore);
                    entry.sampleCount = Mathf.Max(0, entry.sampleCount);
                    entry.looksCorrectCount = Mathf.Max(0, entry.looksCorrectCount); // P2.4
                    m_ByKey[entry.key] = entry;
                    m_Entries.Add(entry);
                }

                if (m_LogUpdates)
                {
                    NGLog.Info(
                        kLogCategory,
                        NGLog.Format(
                            "Adaptive FX tuning loaded",
                            ("entries", m_Entries.Count),
                            ("path", m_ResolvedOutputPath)
                        )
                    );
                }
            }
            catch (IOException ex)
            {
                NGLog.Warn(kLogCategory, $"Failed to load adaptive FX tuning: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                NGLog.Warn(kLogCategory, $"Failed to load adaptive FX tuning: {ex.Message}");
            }
#endif // !UNITY_WEBGL
        }

        private void TrySaveState(bool force)
        {
            if (!m_Dirty && !force)
            {
                return;
            }

#if !UNITY_WEBGL
            if (string.IsNullOrWhiteSpace(m_ResolvedOutputPath))
            {
                ResolveOutputPath();
            }

            try
            {
                var file = new TuningStateFile
                {
                    schemaVersion = 1,
                    updatedUtc = DateTime.UtcNow.ToString("o"),
                    entries = new List<TuningEntry>(m_Entries),
                };
                string json = JsonUtility.ToJson(file, true);
                File.WriteAllText(m_ResolvedOutputPath, json);
                m_Dirty = false;
            }
            catch (IOException ex)
            {
                NGLog.Warn(kLogCategory, $"Failed to save adaptive FX tuning: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                NGLog.Warn(kLogCategory, $"Failed to save adaptive FX tuning: {ex.Message}");
            }
#endif // !UNITY_WEBGL
        }
    }
}
