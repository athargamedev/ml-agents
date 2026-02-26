using System.Collections.Generic;
using System.Linq;
using Network_Game.Dialogue;
using Network_Game.Dialogue.Effects;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Editor.CustomTools.Services
{
    /// <summary>
    /// Validates the EffectCatalog and NPC profile effect configurations.
    /// </summary>
    public static class EffectValidationService
    {
        public sealed class ValidationReport
        {
            public int TotalEffects;
            public int ValidEffects;
            public int TotalProfiles;
            public int ValidProfiles;
            public List<ValidationIssue> Issues = new List<ValidationIssue>();
        }

        public sealed class ValidationIssue
        {
            public string Severity; // "error", "warning"
            public string Source;   // asset name or path
            public string Message;
        }

        /// <summary>
        /// Validate all effect definitions in the catalog and all NPC profiles.
        /// </summary>
        public static ValidationReport ValidateAll()
        {
            var report = new ValidationReport();

            // Validate catalog
            ValidateCatalog(report);

            // Validate profiles
            ValidateProfiles(report);

            return report;
        }

        /// <summary>
        /// Validate a single effect tag against the catalog.
        /// </summary>
        public static (bool valid, string resolvedTag, List<string> issues) ValidateTag(string tag)
        {
            var issues = new List<string>();
            var catalog = EffectCatalog.Load();

            if (catalog == null)
            {
                issues.Add("EffectCatalog not loaded.");
                return (false, null, issues);
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                issues.Add("Tag is empty or null.");
                return (false, null, issues);
            }

            if (catalog.TryGet(tag, out EffectDefinition def))
            {
                if (def.effectPrefab == null)
                {
                    issues.Add($"Tag '{tag}' resolved to '{def.effectTag}' but prefab is missing.");
                    return (true, def.effectTag, issues);
                }

                return (true, def.effectTag, issues);
            }

            issues.Add($"Tag '{tag}' not found in catalog (no primary or alternative match).");
            return (false, null, issues);
        }

        private static void ValidateCatalog(ValidationReport report)
        {
            EffectCatalog catalog = null;

            string[] guids = AssetDatabase.FindAssets("t:EffectCatalog");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                catalog = AssetDatabase.LoadAssetAtPath<EffectCatalog>(path);
            }

            if (catalog == null)
            {
                report.Issues.Add(new ValidationIssue
                {
                    Severity = "error",
                    Source = "EffectCatalog",
                    Message = "No EffectCatalog asset found in the project.",
                });
                return;
            }

            if (catalog.allEffects == null || catalog.allEffects.Count == 0)
            {
                report.Issues.Add(new ValidationIssue
                {
                    Severity = "warning",
                    Source = "EffectCatalog",
                    Message = "Catalog is empty (no EffectDefinitions).",
                });
                return;
            }

            var seenTags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < catalog.allEffects.Count; i++)
            {
                var effect = catalog.allEffects[i];
                report.TotalEffects++;

                if (effect == null)
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = "error",
                        Source = $"EffectCatalog[{i}]",
                        Message = "Null entry in catalog allEffects list.",
                    });
                    continue;
                }

                bool valid = true;

                if (string.IsNullOrWhiteSpace(effect.effectTag))
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = "error",
                        Source = effect.name,
                        Message = "Effect has empty effectTag.",
                    });
                    valid = false;
                }
                else if (!seenTags.Add(effect.effectTag))
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = "warning",
                        Source = effect.name,
                        Message = $"Duplicate effectTag '{effect.effectTag}'.",
                    });
                }

                if (effect.effectPrefab == null)
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = "error",
                        Source = effect.name,
                        Message = $"Missing prefab for effect '{effect.effectTag}'.",
                    });
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(effect.description))
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = "warning",
                        Source = effect.name,
                        Message = $"No description for '{effect.effectTag}' (LLM prompt may be unclear).",
                    });
                }

                if (valid)
                {
                    report.ValidEffects++;
                }
            }
        }

        private static void ValidateProfiles(ValidationReport report)
        {
            string[] guids = AssetDatabase.FindAssets("t:NpcDialogueProfile");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var profile = AssetDatabase.LoadAssetAtPath<NpcDialogueProfile>(path);
                if (profile == null)
                {
                    continue;
                }

                report.TotalProfiles++;
                bool valid = true;

                if (string.IsNullOrWhiteSpace(profile.ProfileId))
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = "error",
                        Source = path,
                        Message = "Profile has empty ProfileId.",
                    });
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(profile.SystemPrompt))
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Severity = "warning",
                        Source = profile.DisplayName,
                        Message = "Profile has empty SystemPrompt.",
                    });
                }

                var powers = profile.PrefabPowers;
                if (powers != null)
                {
                    for (int p = 0; p < powers.Length; p++)
                    {
                        var power = powers[p];
                        if (power == null)
                        {
                            continue;
                        }

                        if (power.Enabled && power.EffectPrefab == null)
                        {
                            report.Issues.Add(new ValidationIssue
                            {
                                Severity = "error",
                                Source = $"{profile.DisplayName}/{power.PowerName}",
                                Message = "Enabled power has no prefab assigned.",
                            });
                            valid = false;
                        }

                        if (power.Keywords == null || power.Keywords.Length == 0)
                        {
                            report.Issues.Add(new ValidationIssue
                            {
                                Severity = "warning",
                                Source = $"{profile.DisplayName}/{power.PowerName}",
                                Message = "Power has no keywords.",
                            });
                        }
                    }
                }

                if (valid)
                {
                    report.ValidProfiles++;
                }
            }
        }

        /// <summary>
        /// Convert validation report to a dictionary for MCP tool response.
        /// </summary>
        public static Dictionary<string, object> ReportToDict(ValidationReport report)
        {
            return new Dictionary<string, object>
            {
                ["total_effects"] = report.TotalEffects,
                ["valid_effects"] = report.ValidEffects,
                ["total_profiles"] = report.TotalProfiles,
                ["valid_profiles"] = report.ValidProfiles,
                ["issue_count"] = report.Issues.Count,
                ["issues"] = report.Issues.Select(issue => new Dictionary<string, string>
                {
                    ["severity"] = issue.Severity,
                    ["source"] = issue.Source,
                    ["message"] = issue.Message,
                }).ToList(),
            };
        }
    }
}
