using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Extracts lightweight particle/effect parameter intents from dialogue text.
    /// This is intentionally heuristic and deterministic to keep runtime stable.
    /// </summary>
    public static class ParticleParameterExtractor
    {
        public struct ParticleParameterIntent
        {
            public bool HasAnyOverride;
            public bool HasColorOverride;
            public Color ColorOverride;

            public float IntensityMultiplier;
            public float DurationMultiplier;
            public float RadiusMultiplier;
            public float SizeMultiplier;
            public float SpeedMultiplier;
            public float CountMultiplier;
            public float ForceMultiplier;
            public bool HasExplicitDurationSeconds;
            public float ExplicitDurationSeconds;
            public bool HasExplicitIntensityMultiplier;
            public float ExplicitIntensityMultiplier;
            public string DetectedElement;
            public float EmotionalMultiplier;
            public bool HasExplicitRadius;
            public float ExplicitRadius;
            public bool HasExplicitScale;
            public float ExplicitScale;
            public bool HasExplicitCount;
            public int ExplicitCount;

            public static ParticleParameterIntent Default =>
                new ParticleParameterIntent
            {
                HasAnyOverride = false,
                HasColorOverride = false,
                ColorOverride = Color.white,
                IntensityMultiplier = 1f,
                DurationMultiplier = 1f,
                RadiusMultiplier = 1f,
                SizeMultiplier = 1f,
                SpeedMultiplier = 1f,
                CountMultiplier = 1f,
                ForceMultiplier = 1f,
                HasExplicitDurationSeconds = false,
                ExplicitDurationSeconds = 0f,
                HasExplicitIntensityMultiplier = false,
                ExplicitIntensityMultiplier = 1f,
                DetectedElement = "",
                EmotionalMultiplier = 1f,
                HasExplicitRadius = false,
                ExplicitRadius = 0f,
                HasExplicitScale = false,
                ExplicitScale = 1f,
                HasExplicitCount = false,
                ExplicitCount = 0,
            };
        }

        private static readonly Regex s_DurationSecondsRegex = new Regex(
            @"(?:(?:for|duration|last(?:ing)?)\s*)?(?<value>\d+(?:\.\d+)?)\s*(?:s|sec|secs|second|seconds)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private static readonly Regex s_IntensityPercentRegex = new Regex(
            @"(?:intensity|power|strength|brightness)\s*(?:to|at)?\s*(?<value>\d+(?:\.\d+)?)\s*%|(?<value>\d+(?:\.\d+)?)\s*%\s*(?:intensity|power|strength|brightness)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private static readonly Regex s_IntensityMultiplierRegex = new Regex(
            @"(?:intensity|power|strength|brightness)\s*(?:to|at)?\s*(?<value>\d+(?:\.\d+)?)\s*x|(?<value>\d+(?:\.\d+)?)\s*x\s*(?:intensity|power|strength|brightness)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private static readonly Regex s_RadiusRegex = new Regex(
            @"(?:radius|spread|area|range|coverage)\s*(?:of|to|at|=|:)?\s*(?<value>\d+(?:\.\d+)?)\s*(?:m(?:eters?)?|units?)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private static readonly Regex s_ScaleRegex = new Regex(
            @"(?:scale|size|magnitude)\s*(?:of|to|at|=|:)?\s*(?<value>\d+(?:\.\d+)?)\s*(?:x|times)?|(?<value2>\d+(?:\.\d+)?)\s*x\s*(?:scale|size|bigger|larger)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private static readonly Regex s_CountRegex = new Regex(
            @"(?:count|number|amount|quantity)\s*(?:of|to|at|=|:)?\s*(?<value>\d+)|(?<value2>\d+)\s*(?:particles?|bolts?|projectiles?|drops?|streaks?)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private static readonly (string[] terms, string element)[] s_ElementalMappings = new[]
        {
            (
                new[]
                {
                    "fire",
                    "flame",
                    "blaze",
                    "inferno",
                    "ember",
                    "scorch",
                    "molten",
                    "burn",
                    "ignite",
                    "incinerate",
                },
                "fire"
            ),
            (
                new[]
                {
                    "ice",
                    "frost",
                    "freeze",
                    "chill",
                    "glacier",
                    "cold",
                    "frozen",
                    "blizzard",
                    "hail",
                },
                "ice"
            ),
            (
                new[]
                {
                    "lightning",
                    "thunder",
                    "bolt",
                    "storm",
                    "tempest",
                    "surge",
                    "electric",
                    "voltage",
                    "thunder",
                },
                "storm"
            ),
            (
                new[]
                {
                    "water",
                    "rain",
                    "torrent",
                    "deluge",
                    "mist",
                    "flood",
                    "drown",
                    "tsunami",
                    "wave",
                },
                "water"
            ),
            (
                new[]
                {
                    "earth",
                    "quake",
                    "rumble",
                    "dust",
                    "crumble",
                    "stone",
                    "boulder",
                    "sand",
                    "rock",
                },
                "earth"
            ),
            (
                new[]
                {
                    "nature",
                    "forest",
                    "growth",
                    "bloom",
                    "verdant",
                    "life",
                    "vine",
                    "root",
                    "flora",
                },
                "nature"
            ),
            (
                new[]
                {
                    "magic",
                    "mystical",
                    "ethereal",
                    "ghostly",
                    "ancient",
                    "spirit",
                    "arcane",
                    "enchant",
                    "rune",
                },
                "mystic"
            ),
            (
                new[]
                {
                    "void",
                    "shadow",
                    "darkness",
                    "abyss",
                    "null",
                    "obliterate",
                    "consume",
                    "devour",
                },
                "void"
            ),
            (
                new[]
                {
                    "explosion",
                    "explode",
                    "detonate",
                    "erupt",
                    "bomb",
                    "plasma",
                    "detonation",
                    "eruption",
                    "combust",
                    "implode",
                },
                "explosion"
            ),
        };

        private static readonly (string[] terms, float multiplier)[] s_EmotionalIntensity = new[]
        {
            (
                new[]
                {
                    "menacing",
                    "threatening",
                    "dangerous",
                    "fearful",
                    "terrifying",
                    "dreadful",
                },
                1.4f
            ),
            (new[] { "peaceful", "calm", "tranquil", "serene", "gentle", "soothing" }, 0.65f),
            (
                new[] { "joyful", "triumphant", "celebration", "victory", "glorious", "heroic" },
                1.3f
            ),
            (new[] { "sad", "tragic", "mournful", "grief", "sorrowful", "melancholy" }, 0.7f),
            (new[] { "chaotic", "madness", "wild", "unpredictable", "frenzy", "rampage" }, 1.5f),
            (
                new[] { "epic", "legendary", "colossal", "titanic", "world-ending", "apocalyptic" },
                1.6f
            ),
        };

        public static ParticleParameterIntent Extract(string dialogueText)
        {
            ParticleParameterIntent intent = ParticleParameterIntent.Default;
            if (string.IsNullOrWhiteSpace(dialogueText))
            {
                return intent;
            }

            string lower = dialogueText.ToLowerInvariant();

            if (TryExtractColor(lower, out Color color))
            {
                intent.HasColorOverride = true;
                intent.ColorOverride = color;
                intent.HasAnyOverride = true;
            }

            ApplyScaleIntent(
                lower,
                ref intent.SizeMultiplier,
                new[] { "bigger", "larger", "huge", "massive", "gigantic" },
                new[] { "tiny", "smaller", "small" }
            );

            ApplyScaleIntent(
                lower,
                ref intent.RadiusMultiplier,
                new[] { "wider", "broader", "expand", "spread" },
                new[] { "narrow", "tight", "shrink" }
            );

            ApplyScaleIntent(
                lower,
                ref intent.DurationMultiplier,
                new[] { "longer", "lasting", "sustain", "extended" },
                new[] { "brief", "short", "quick" }
            );

            ApplyScaleIntent(
                lower,
                ref intent.SpeedMultiplier,
                new[] { "faster", "rapid", "swift" },
                new[] { "slow", "slower", "calm" }
            );

            ApplyScaleIntent(
                lower,
                ref intent.CountMultiplier,
                new[] { "more", "many", "dense", "heavy" },
                new[] { "fewer", "less", "sparse" }
            );

            ApplyScaleIntent(
                lower,
                ref intent.ForceMultiplier,
                new[] { "stronger", "powerful", "violent" },
                new[] { "weaker", "gentle", "soft" }
            );

            ApplyScaleIntent(
                lower,
                ref intent.IntensityMultiplier,
                new[] { "bright", "brighter", "intense", "glow" },
                new[] { "dim", "dimmer", "subtle" }
            );

            if (TryExtractDurationSeconds(dialogueText, out float explicitDuration))
            {
                intent.HasExplicitDurationSeconds = true;
                intent.ExplicitDurationSeconds = explicitDuration;
                intent.HasAnyOverride = true;
            }

            if (TryExtractIntensityMultiplier(dialogueText, out float explicitIntensity))
            {
                intent.HasExplicitIntensityMultiplier = true;
                intent.ExplicitIntensityMultiplier = explicitIntensity;
                intent.IntensityMultiplier = explicitIntensity;
                intent.HasAnyOverride = true;
            }

            intent.DetectedElement = ExtractElement(lower);

            intent.EmotionalMultiplier = ExtractEmotionalMultiplier(lower);
            if (!Mathf.Approximately(intent.EmotionalMultiplier, 1f))
            {
                intent.HasAnyOverride = true;
            }

            if (TryExtractExplicitRadius(dialogueText, out float explicitRadius))
            {
                intent.HasExplicitRadius = true;
                intent.ExplicitRadius = explicitRadius;
                intent.HasAnyOverride = true;
            }

            if (TryExtractExplicitScale(dialogueText, out float explicitScale))
            {
                intent.HasExplicitScale = true;
                intent.ExplicitScale = explicitScale;
                intent.HasAnyOverride = true;
            }

            if (TryExtractExplicitCount(dialogueText, out int explicitCount))
            {
                intent.HasExplicitCount = true;
                intent.ExplicitCount = explicitCount;
                intent.HasAnyOverride = true;
            }

            if (
                !intent.HasAnyOverride
                && (
                    !Mathf.Approximately(intent.IntensityMultiplier, 1f)
                    || !Mathf.Approximately(intent.DurationMultiplier, 1f)
                    || !Mathf.Approximately(intent.RadiusMultiplier, 1f)
                    || !Mathf.Approximately(intent.SizeMultiplier, 1f)
                    || !Mathf.Approximately(intent.SpeedMultiplier, 1f)
                    || !Mathf.Approximately(intent.CountMultiplier, 1f)
                    || !Mathf.Approximately(intent.ForceMultiplier, 1f)
                )
            )
            {
                intent.HasAnyOverride = true;
            }

            return intent;
        }

        private static void ApplyScaleIntent(
            string lower,
            ref float targetMultiplier,
            string[] strongerTerms,
            string[] weakerTerms
        )
        {
            // Count non-negated votes in each direction
            int upVotes = CountNonNegated(lower, strongerTerms);
            int downVotes = CountNonNegated(lower, weakerTerms);

            if (upVotes > downVotes)
            {
                targetMultiplier *= 1.35f;
            }
            else if (downVotes > upVotes)
            {
                targetMultiplier *= 0.72f;
            }
            // Tied or zero → no change (neutral)

            if (ContainsAny(lower, "extreme", "max", "maximum", "super", "ultra"))
            {
                targetMultiplier *= 1.2f;
            }
            else if (ContainsAny(lower, "slight", "slightly", "a bit", "little"))
            {
                targetMultiplier *= 0.92f;
            }
        }

        // Count keyword matches that are not immediately preceded by a negation word.
        private static int CountNonNegated(string lower, string[] terms)
        {
            if (string.IsNullOrWhiteSpace(lower) || terms == null)
                return 0;
            int count = 0;
            string[] negationPrefixes =
            {
                "not ",
                "no ",
                "never ",
                "stop ",
                "cease ",
                "don't ",
                "dont ",
                "without ",
            };
            for (int i = 0; i < terms.Length; i++)
            {
                string term = terms[i];
                if (string.IsNullOrWhiteSpace(term))
                    continue;
                int idx = lower.IndexOf(term, System.StringComparison.Ordinal);
                if (idx < 0)
                    continue;
                // Check up to 20 chars before the keyword for negation words
                int scanStart = Mathf.Max(0, idx - 20);
                string before = lower.Substring(scanStart, idx - scanStart);
                bool negated = false;
                for (int n = 0; n < negationPrefixes.Length; n++)
                {
                    if (before.Contains(negationPrefixes[n]))
                    {
                        negated = true;
                        break;
                    }
                }
                if (!negated)
                    count++;
            }
            return count;
        }

        private static bool ContainsAny(string lower, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(lower) || terms == null || terms.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < terms.Length; i++)
            {
                string term = terms[i];
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                if (lower.Contains(term))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractDurationSeconds(string text, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            MatchCollection matches = s_DurationSecondsRegex.Matches(text);
            if (matches == null || matches.Count == 0)
            {
                return false;
            }

            // Use the last explicit duration in the phrase so users can refine mid-sentence.
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Match match = matches[i];
                if (!match.Success)
                {
                    continue;
                }

                Group group = match.Groups["value"];
                if (!group.Success)
                {
                    continue;
                }

                if (
                    float.TryParse(
                        group.Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed
                    )
                )
                {
                    seconds = Mathf.Max(0f, parsed);
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractIntensityMultiplier(string text, out float multiplier)
        {
            multiplier = 1f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (TryExtractRegexFloat(s_IntensityPercentRegex, text, out float percentValue))
            {
                multiplier = Mathf.Max(0f, percentValue / 100f);
                return true;
            }

            if (TryExtractRegexFloat(s_IntensityMultiplierRegex, text, out float xValue))
            {
                multiplier = Mathf.Max(0f, xValue);
                return true;
            }

            return false;
        }

        private static bool TryExtractRegexFloat(Regex regex, string text, out float value)
        {
            value = 0f;
            if (regex == null || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            MatchCollection matches = regex.Matches(text);
            if (matches == null || matches.Count == 0)
            {
                return false;
            }

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Match match = matches[i];
                if (!match.Success)
                {
                    continue;
                }

                Group group = match.Groups["value"];
                if (!group.Success)
                {
                    continue;
                }

                if (
                    float.TryParse(
                        group.Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed
                    )
                )
                {
                    value = parsed;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractColor(string lower, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(lower))
            {
                return false;
            }

            if (ContainsAny(lower, "electric blue", "azure"))
            {
                color = new Color(0.35f, 0.68f, 1f, 1f);
                return true;
            }

            if (ContainsAny(lower, "cyan", "teal"))
            {
                color = new Color(0.22f, 0.95f, 0.9f, 1f);
                return true;
            }

            if (ContainsAny(lower, "blue", "navy"))
            {
                color = new Color(0.32f, 0.58f, 1f, 1f);
                return true;
            }

            if (ContainsAny(lower, "red", "crimson", "scarlet"))
            {
                color = new Color(1f, 0.3f, 0.3f, 1f);
                return true;
            }

            if (ContainsAny(lower, "green", "emerald", "lime"))
            {
                color = new Color(0.35f, 1f, 0.45f, 1f);
                return true;
            }

            if (ContainsAny(lower, "yellow", "gold"))
            {
                color = new Color(1f, 0.9f, 0.25f, 1f);
                return true;
            }

            if (ContainsAny(lower, "orange", "amber"))
            {
                color = new Color(1f, 0.62f, 0.2f, 1f);
                return true;
            }

            if (ContainsAny(lower, "purple", "violet", "magenta"))
            {
                color = new Color(0.74f, 0.45f, 1f, 1f);
                return true;
            }

            if (ContainsAny(lower, "white", "silver"))
            {
                color = new Color(0.95f, 0.97f, 1f, 1f);
                return true;
            }

            if (ContainsAny(lower, "black", "dark"))
            {
                color = new Color(0.12f, 0.12f, 0.16f, 1f);
                return true;
            }

            return false;
        }

        private static string ExtractElement(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
                return "";
            for (int i = 0; i < s_ElementalMappings.Length; i++)
            {
                var(terms, element) = s_ElementalMappings[i];
                if (ContainsAny(lower, terms))
                {
                    return element;
                }
            }
            return "";
        }

        private static float ExtractEmotionalMultiplier(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower))
                return 1f;
            // Compound state: weighted average of ALL matched emotional terms
            float sum = 0f;
            int matches = 0;
            for (int i = 0; i < s_EmotionalIntensity.Length; i++)
            {
                var(terms, multiplier) = s_EmotionalIntensity[i];
                if (ContainsAny(lower, terms))
                {
                    sum += multiplier;
                    matches++;
                }
            }
            return matches == 0 ? 1f : sum / matches;
        }

        // Resolve an emotional multiplier from a canonical LLM-supplied emotion keyword.
        // Used when EffectIntent.emotion is set by the LLM directly.
        internal static float EmotionKeywordToMultiplier(string emotion)
        {
            if (string.IsNullOrWhiteSpace(emotion))
                return 1f;
            string lower = emotion.ToLowerInvariant().Trim();
            for (int i = 0; i < s_EmotionalIntensity.Length; i++)
            {
                var(terms, multiplier) = s_EmotionalIntensity[i];
                for (int t = 0; t < terms.Length; t++)
                {
                    if (lower == terms[t])
                        return multiplier;
                }
            }
            // Fallback partial scan
            for (int i = 0; i < s_EmotionalIntensity.Length; i++)
            {
                var(terms, multiplier) = s_EmotionalIntensity[i];
                if (ContainsAny(lower, terms))
                    return multiplier;
            }
            return 1f;
        }

        // Apply element-specific parameter bonuses to the intent.
        // Called from NetworkDialogueService after extraction.
        internal static void ApplyElementBonus(
            string element,
            ref float scale,
            ref float radius,
            ref float duration,
            ref float damage
        )
        {
            if (string.IsNullOrWhiteSpace(element))
                return;
            switch (element.ToLowerInvariant())
            {
                case "fire":
                    scale *= 1.2f;
                    damage *= 1.15f;
                    break;
                case "ice":
                    radius *= 1.3f;
                    duration *= 1.2f;
                    break;
                case "storm":
                    scale *= 1.1f;
                    radius *= 1.15f;
                    damage *= 1.1f;
                    break;
                case "void":
                    damage *= 1.4f;
                    scale *= 0.9f;
                    break;
                case "nature":
                    duration *= 1.3f;
                    radius *= 1.1f;
                    break;
                case "water":
                    radius *= 1.2f;
                    scale *= 1.05f;
                    break;
                case "earth":
                    scale *= 1.15f;
                    damage *= 1.1f;
                    break;
                case "mystic":
                    duration *= 1.25f;
                    scale *= 1.1f;
                    break;
            }
        }

        private static bool TryExtractExplicitRadius(string text, out float radius)
        {
            radius = 0f;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (TryExtractRegexFloat(s_RadiusRegex, text, out float parsed))
            {
                radius = Mathf.Clamp(parsed, 0.1f, 100f);
                return true;
            }
            return false;
        }

        private static bool TryExtractExplicitScale(string text, out float scale)
        {
            scale = 1f;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            MatchCollection matches = s_ScaleRegex.Matches(text);
            if (matches == null || matches.Count == 0)
                return false;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Match match = matches[i];
                if (!match.Success)
                    continue;
                Group g1 = match.Groups["value"];
                Group g2 = match.Groups["value2"];
                Group active = g1.Success ? g1 : (g2.Success ? g2 : null);
                if (active == null)
                    continue;
                if (
                    float.TryParse(
                        active.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float parsed
                    )
                )
                {
                    scale = Mathf.Clamp(parsed, 0.1f, 50f);
                    return true;
                }
            }
            return false;
        }

        private static bool TryExtractExplicitCount(string text, out int count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            MatchCollection matches = s_CountRegex.Matches(text);
            if (matches == null || matches.Count == 0)
                return false;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Match match = matches[i];
                if (!match.Success)
                    continue;
                Group g1 = match.Groups["value"];
                Group g2 = match.Groups["value2"];
                Group active = g1.Success ? g1 : (g2.Success ? g2 : null);
                if (active == null)
                    continue;
                if (
                    float.TryParse(
                        active.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float parsed
                    )
                )
                {
                    count = Mathf.Clamp(Mathf.RoundToInt(parsed), 1, 2000);
                    return true;
                }
            }
            return false;
        }
    }
}
