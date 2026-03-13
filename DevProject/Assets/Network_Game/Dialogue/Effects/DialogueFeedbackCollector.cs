using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Network_Game.Diagnostics;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Automatically scores every LLM response and writes feedback records to a JSONL file.
    /// The file is consumed by build_dataset_from_feedback.py to generate new LoRA training data.
    ///
    /// Scoring signals (server-observable, no human needed):
    ///   +2  Valid [EFFECT:] tag found in response
    ///   +2  Effect tag name resolves to a known profile power or catalog entry
    ///   +1  Response contains an explicit Target: field in the tag
    ///   +1  Response contains an explicit Placement: or Duration: field
    ///   -3  Response contains a refusal phrase ("I cannot", "I am unable", etc.)
    ///   -2  Response has zero [EFFECT:] tags (when request was effect-eligible)
    ///   -1  Response is very short (< 12 words), likely a non-answer
    ///
    /// Output record schema (one JSON object per line):
    /// {
    ///   "ts":         "2026-02-18T14:00:00Z",   // UTC ISO-8601
    ///   "npc_id":     "npc.archivist",           // ProfileId, e.g. npc.archivist
    ///   "prompt":     "make me invisible",       // raw player prompt
    ///   "response":   "I cannot...",             // raw LLM response
    ///   "score":      -3,                        // computed quality score
    ///   "signals":    ["refusal(-3)"],           // human-readable signal list
    ///   "has_effect": false,                     // true if [EFFECT:] tag found
    ///   "tag_name":   "",                        // e.g. "Dissolve"
    ///   "tag_valid":  false,                     // tag matched a known power
    ///   "status":     "Completed"                // DialogueStatus string
    /// }
    /// </summary>
    [DefaultExecutionOrder(-440)] // after NetworkDialogueService (-450)
    public class DialogueFeedbackCollector : MonoBehaviour
    {
        private const string k_LogCategory = "FeedbackCollector";

        public struct FeedbackScoreSummary
        {
            public int RequestId;
            public int Score;
            public bool HasEffect;
            public bool TagValid;
            public string TagName;
            public string[] Signals;
            public ulong SpeakerNetworkId;
            public bool IsUserInitiated;
        }

        public static event Action<FeedbackScoreSummary> OnFeedbackScored;

        // ── Inspector ────────────────────────────────────────────────────────────
        [Header("Output")]
        [Tooltip("Path to the feedback JSONL file. Relative paths are rooted at the project root.")]
        [SerializeField]
        private string m_OutputPath = "output/feedback_log.jsonl";

        [Tooltip(
            "Minimum absolute score threshold to write a record. "
            + "Set to -99 to capture everything (recommended during development)."
         )]
        [SerializeField]
        private int m_MinScoreToWrite = -99;

        [Tooltip("Only record responses from user-initiated requests (IsUserInitiated=true).")]
        [SerializeField]
        private bool m_UserInitiatedOnly = true;

        [Header("Known Power Names")]
        [Tooltip(
            "Auto-populated from NpcDialogueProfile assets at startup. "
            + "Used to validate [EFFECT:] tag names."
         )]
        [SerializeField]
        private string[] m_KnownPowerNames = Array.Empty<string>();

        // ── Regex patterns ───────────────────────────────────────────────────────
        // Matches: [EFFECT: SomeName | Target: Player | ...]
        private static readonly Regex s_EffectTagRegex = new Regex(
            @"\[EFFECT\s*:\s*(?<name>[^\|\]]+?)(?:\s*\|[^\]]*)?]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Matches explicit Target: field inside an EFFECT tag
        private static readonly Regex s_TargetFieldRegex = new Regex(
            @"Target\s*:\s*\S+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Matches explicit Placement: or Duration: field
        private static readonly Regex s_PlacementOrDurationRegex = new Regex(
            @"(Placement|Duration)\s*:\s*\S+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Refusal phrases — any match → refusal penalty
        private static readonly string[] s_RefusalPhrases = new[]
        {
            "i cannot",
            "i am unable",
            "i'm unable",
            "i will not",
            "i won't",
            "not permitted",
            "not allowed",
            "against my",
            "i must refuse",
            "i do not have the ability",
            "unable to grant",
            "cannot grant",
            "refusing to",
            "i decline",
        };

        // ── State ────────────────────────────────────────────────────────────────
        private string m_ResolvedOutputPath;
        private HashSet<string> m_KnownPowerSet;
        private int m_Written;
        private int m_Skipped;

        // ── Unity lifecycle ──────────────────────────────────────────────────────
        private void Awake()
        {
            // Resolve output path relative to project root
            m_ResolvedOutputPath = ResolveOutputPath(m_OutputPath);

            // Ensure directory exists
#if !UNITY_WEBGL
            string dir = Path.GetDirectoryName(m_ResolvedOutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
#endif

            // Build known-power lookup from all loaded NPC profiles
            m_KnownPowerSet = BuildKnownPowerSet();

            NGLog.Info(
                k_LogCategory,
                NGLog.Format(
                    "FeedbackCollector ready",
                    ("output", m_ResolvedOutputPath),
                    ("knownPowers", m_KnownPowerSet.Count),
                    ("userInitiatedOnly", m_UserInitiatedOnly)
                )
            );
        }

        private void OnEnable()
        {
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
        }

        private void OnDisable()
        {
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
        }

        // ── Core handler ─────────────────────────────────────────────────────────
        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            // Filter: only completed, non-empty responses
            if (response.Status != NetworkDialogueService.DialogueStatus.Completed)
                return;

            if (string.IsNullOrWhiteSpace(response.ResponseText))
                return;

            // Optional: only user-initiated dialogue (not ambient/bored NPC chatter)
            if (m_UserInitiatedOnly && !response.Request.IsUserInitiated)
                return;

            // Resolve NPC profile id for this speaker
            string npcId = ResolveNpcProfileId(response.Request.SpeakerNetworkId);

            // Score the response
            ScoreResult score = ScoreResponse(response.Request.Prompt, response.ResponseText);

            OnFeedbackScored?.Invoke(
                new FeedbackScoreSummary
                {
                    RequestId = response.RequestId,
                    Score = score.Total,
                    HasEffect = score.HasEffect,
                    TagValid = score.TagValid,
                    TagName = score.TagName ?? string.Empty,
                    Signals = score.Signals?.ToArray() ?? Array.Empty<string>(),
                    SpeakerNetworkId = response.Request.SpeakerNetworkId,
                    IsUserInitiated = response.Request.IsUserInitiated,
                }
            );

            if (score.Total < m_MinScoreToWrite)
            {
                m_Skipped++;
                return;
            }

            // Write record
            WriteRecord(
                npcId,
                response.Request.Prompt,
                response.ResponseText,
                score,
                response.Status.ToString()
            );
            m_Written++;

            NGLog.Info(
                k_LogCategory,
                NGLog.Format(
                    "Feedback recorded",
                    ("npc", npcId),
                    ("score", score.Total),
                    ("hasEffect", score.HasEffect),
                    ("tagName", score.TagName ?? ""),
                    ("total_written", m_Written)
                )
            );
        }

        // ── Scorer ───────────────────────────────────────────────────────────────
        private struct ScoreResult
        {
            public int Total;
            public List<string> Signals;
            public bool HasEffect;
            public string TagName;
            public bool TagValid;
        }

        private ScoreResult ScoreResponse(string prompt, string response)
        {
            var signals = new List<string>(8);
            int score = 0;
            string tagName = string.Empty;
            bool tagValid = false;

            // ── Check for [EFFECT:] tag ──────────────────────────────────────────
            Match effectMatch = s_EffectTagRegex.Match(response);
            bool hasEffect = effectMatch.Success;

            if (hasEffect)
            {
                score += 2;
                signals.Add("effect_tag(+2)");

                tagName = effectMatch.Groups["name"].Value.Trim();

                // Does the tag name match a known power?
                if (
                    !string.IsNullOrEmpty(tagName)
                    && m_KnownPowerSet.Contains(tagName.ToLowerInvariant())
                )
                {
                    score += 2;
                    signals.Add("known_power(+2)");
                    tagValid = true;
                }
                else
                {
                    // Partial credit: tag name not empty but not in known list (new effect)
                    if (!string.IsNullOrEmpty(tagName))
                    {
                        score += 1;
                        signals.Add("unknown_power(+1)");
                    }
                }

                // Does the tag have an explicit Target: field?
                if (s_TargetFieldRegex.IsMatch(response))
                {
                    score += 1;
                    signals.Add("has_target(+1)");
                }

                // Does the tag have Placement: or Duration: field?
                if (s_PlacementOrDurationRegex.IsMatch(response))
                {
                    score += 1;
                    signals.Add("has_placement_or_duration(+1)");
                }
            }
            else
            {
                // Prompt was effect-eligible — penalize missing tag
                // Only penalise when the prompt seems to be requesting an effect
                if (PromptLikelyRequestsEffect(prompt))
                {
                    score -= 2;
                    signals.Add("missing_effect_tag(-2)");
                }
            }

            // ── Refusal check ────────────────────────────────────────────────────
            string responseLower = response.ToLowerInvariant();
            foreach (string phrase in s_RefusalPhrases)
            {
                if (responseLower.Contains(phrase))
                {
                    score -= 3;
                    signals.Add($"refusal(-3)[\"{phrase}\"]");
                    break; // one penalty per response
                }
            }

            // ── Length / quality check ───────────────────────────────────────────
            int wordCount = CountWords(response);
            if (wordCount < 12 && !hasEffect)
            {
                score -= 1;
                signals.Add("too_short(-1)");
            }

            return new ScoreResult
            {
                Total = score,
                Signals = signals,
                HasEffect = hasEffect,
                TagName = tagName,
                TagValid = tagValid,
            };
        }

        // ── Helper: does this prompt ask for an effect? ──────────────────────────
        private static readonly string[] s_EffectRequestKeywords = new[]
        {
            "cast",
            "fire",
            "freeze",
            "burn",
            "lightning",
            "explode",
            "invisible",
            "vanish",
            "dissolve",
            "make me",
            "give me",
            "use",
            "activate",
            "spawn",
            "summon",
            "magic",
            "power",
            "ability",
            "effect",
            "spell",
            "attack",
        };

        private static bool PromptLikelyRequestsEffect(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return false;
            string lower = prompt.ToLowerInvariant();
            foreach (string kw in s_EffectRequestKeywords)
                if (lower.Contains(kw))
                    return true;
            return false;
        }

        // ── Write JSONL record ───────────────────────────────────────────────────
        private void WriteRecord(
            string npcId,
            string prompt,
            string response,
            ScoreResult score,
            string status
        )
        {
            // Build signals JSON array
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < score.Signals.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append('"');
                sb.Append(EscapeJson(score.Signals[i]));
                sb.Append('"');
            }
            sb.Append(']');
            string signalsJson = sb.ToString();

            // Build full record
            string record = string.Format(
                "{{\"ts\":\"{0}\",\"npc_id\":\"{1}\",\"prompt\":\"{2}\","
                + "\"response\":\"{3}\",\"score\":{4},\"signals\":{5},"
                + "\"has_effect\":{6},\"tag_name\":\"{7}\",\"tag_valid\":{8},"
                + "\"status\":\"{9}\"}}",
                DateTime.UtcNow.ToString("o"),
                EscapeJson(npcId ?? "unknown"),
                EscapeJson(prompt ?? ""),
                EscapeJson(response ?? ""),
                score.Total,
                signalsJson,
                score.HasEffect ? "true" : "false",
                EscapeJson(score.TagName ?? ""),
                score.TagValid ? "true" : "false",
                EscapeJson(status)
            );

            try
            {
#if !UNITY_WEBGL
                File.AppendAllText(m_ResolvedOutputPath, record + "\n", Encoding.UTF8);
#endif
            }
            catch (IOException ex)
            {
                NGLog.Error(
                    k_LogCategory,
                    NGLog.Format(
                        "Failed to write feedback record",
                        ("path", m_ResolvedOutputPath),
                        ("error", ex.Message)
                    )
                );
            }
            catch (UnauthorizedAccessException ex)
            {
                NGLog.Error(
                    k_LogCategory,
                    NGLog.Format(
                        "Failed to write feedback record",
                        ("path", m_ResolvedOutputPath),
                        ("error", ex.Message)
                    )
                );
            }
        }

        // ── Resolve NPC profile id from speaker network id ───────────────────────
        private static string ResolveNpcProfileId(ulong speakerNetworkId)
        {
            if (speakerNetworkId == 0)
                return "unknown";

            // Walk all NpcDialogueActor components in the scene
            NpcDialogueActor[] actors = FindObjectsByType<NpcDialogueActor>();
            foreach (NpcDialogueActor actor in actors)
            {
                if (actor == null || actor.Profile == null)
                    continue;
                var netObj = actor.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null && netObj.NetworkObjectId == speakerNetworkId)
                    return actor.Profile.ProfileId;
            }
            return "unknown";
        }

        // ── Build known-power lookup from all profiles ───────────────────────────
        private HashSet<string> BuildKnownPowerSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Inspector override list
            foreach (string name in m_KnownPowerNames)
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name.Trim().ToLowerInvariant());

            // Auto-discover from all loaded profiles
            NpcDialogueProfile[] profiles = NpcDialogueProfile.GetAllProfiles();
            foreach (NpcDialogueProfile profile in profiles)
            {
                if (profile == null)
                    continue;
                foreach (PrefabPowerEntry power in profile.PrefabPowers)
                {
                    if (!string.IsNullOrWhiteSpace(power.PowerName))
                        set.Add(power.PowerName.Trim().ToLowerInvariant());
                }
            }

            return set;
        }

        // ── Resolve output path ──────────────────────────────────────────────────
        private static string ResolveOutputPath(string configured)
        {
            if (Path.IsPathRooted(configured))
                return configured;

            // Relative → project root (parent of Assets)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, configured));
        }

        // ── Utilities ────────────────────────────────────────────────────────────
        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            return text.Split(
                new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries
                ).Length;
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

        // ── Editor summary (called from menu / debug) ────────────────────────────
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/Dialogue/Feedback Collector/Print Stats")]
        private static void PrintStats()
        {
            var col = FindAnyObjectByType<DialogueFeedbackCollector>();
            if (col == null)
            {
                Debug.Log("[FeedbackCollector] Not running.");
                return;
            }
            Debug.Log(
                $"[FeedbackCollector] Written={col.m_Written}  Skipped={col.m_Skipped}"
                + $"  Output={col.m_ResolvedOutputPath}"
            );
        }

#endif
    }
}
