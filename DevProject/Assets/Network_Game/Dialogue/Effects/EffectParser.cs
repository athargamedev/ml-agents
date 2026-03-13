using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Network_Game.Diagnostics;
using UnityEngine;

namespace Network_Game.Dialogue.Effects
{
    /// <summary>
    /// Parses [EFFECT:] tags from LLM responses with whitelist validation.
    /// </summary>
    public static class EffectParser
    {
        private static readonly Regex TagRegex = new Regex(
            @"\[\s*(?:EFFECT|FX|POWER)\s*:\s*([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        private static readonly Regex BareTagRegex = new Regex(
            @"(?:^|\n)\s*(?:EFFECT|FX|POWER)\s*:\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline
        );
        private static readonly Regex UnterminatedTagRegex = new Regex(
            @"\[\s*(?:EFFECT|FX|POWER)\s*:[^\]\r\n]*(?:$|\r?\n)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        private static readonly Regex DanglingParameterTailRegex = new Regex(
            @"(?:\s*\|\s*[A-Za-z][A-Za-z0-9_ ]{0,32}\s*:\s*[^|\r\n\]]+)+\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        private static readonly Regex NumberRegex = new Regex(
            @"-?\d+(?:[.,]\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );
        private static readonly Regex RgbRegex = new Regex(
            @"rgba?\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})(?:\s*,\s*(?<a>[\d\.]+))?\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>
        /// Extract effect intents from LLM response text, validating against the catalog.
        /// </summary>
        /// <param name="responseText">Raw LLM response containing [EFFECT:] tags</param>
        /// <param name="catalog">Effect catalog for whitelist validation (null = skip validation)</param>
        /// <param name="stripTags">Whether to remove the tags from responseText</param>
        /// <returns>List of validated EffectIntent objects</returns>
        public static List<EffectIntent> ExtractIntents(
            string responseText,
            EffectCatalog catalog,
            bool stripTags = true
        )
        {
            var intents = new List<EffectIntent>();

            if (string.IsNullOrWhiteSpace(responseText))
                return intents;

            var matches = TagRegex.Matches(responseText);
            bool usedBracketSyntax = matches.Count > 0;
            if (!usedBracketSyntax)
            {
                matches = BareTagRegex.Matches(responseText);
                if (matches.Count == 0)
                    return intents;
            }

            // Strip tags from text if requested
            if (stripTags)
            {
                responseText = TagRegex.Replace(responseText, "").Trim();
                responseText = BareTagRegex.Replace(responseText, "").Trim();
            }

            foreach (Match match in matches)
            {
                string content = match.Groups[1].Value;
                var intent = ParseIntentContent(content, catalog);
                if (intent != null)
                {
                    intents.Add(intent);
                }
            }

            return intents;
        }

        /// <summary>
        /// Parse a single effect tag content (without the [EFFECT:] wrapper).
        /// </summary>
        private static EffectIntent ParseIntentContent(string content, EffectCatalog catalog)
        {
            var intent = new EffectIntent();
            string[] parts = SplitParts(content);
            if (parts.Length == 0)
                return intent;

            string tagName = string.Empty;
            var parsedParameters = new List<(string key, string value)>();

            for (int i = 0; i < parts.Length; i++)
            {
                string part = TrimWrappedToken(parts[i]);
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (TrySplitKeyValue(part, out string key, out string value))
                {
                    string normalizedKey = NormalizeKey(key);
                    if (IsTagNameKey(normalizedKey))
                    {
                        if (string.IsNullOrWhiteSpace(tagName))
                        {
                            tagName = TrimWrappedToken(value);
                        }
                        continue;
                    }

                    parsedParameters.Add((key, value));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tagName))
                {
                    tagName = part;
                    continue;
                }

                // Allow lightweight forms like: [EFFECT: Lightning | at Player]
                ParseLooseParameter(intent, part);
            }

            intent.rawTagName = tagName;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return intent;
            }

            // Validate against catalog
            if (catalog != null)
            {
                if (TryResolveDefinition(catalog, tagName, out var definition))
                {
                    intent.definition = definition;
                    // Initialize with definition defaults
                    intent.scale = definition.defaultScale;
                    intent.duration = definition.defaultDuration;
                    intent.color = definition.defaultColor;
                }
                else
                {
                    // Unknown effect - log warning
                    if (catalog.logUnknownTags)
                    {
                        NGLog.Warn(
                            "DialogueFX",
                            $"[EffectParser] Unknown effect tag '{tagName}'. Ignoring."
                        );
                    }
                    if (!catalog.allowUnknownTags)
                    {
                        intent.definition = null; // Mark as invalid
                    }
                }
            }

            // Parse key:value parameters
            for (int i = 0; i < parsedParameters.Count; i++)
            {
                var pair = parsedParameters[i];
                ParseParameter(intent, pair.key, pair.value);
            }

            return intent;
        }

        /// <summary>
        /// Parse a single key:value parameter.
        /// </summary>
        private static void ParseParameter(EffectIntent intent, string key, string value)
        {
            if (intent == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string normalizedKey = NormalizeKey(key);

            // Check if this parameter is allowed by the definition
            bool allowScale = intent.definition?.allowCustomScale ?? true;
            bool allowDuration = intent.definition?.allowCustomDuration ?? true;
            bool allowColor = intent.definition?.allowCustomColor ?? true;

            switch (normalizedKey)
            {
                case "anchor":
                    intent.anchor = CleanTargetValue(value);
                    if (string.IsNullOrWhiteSpace(intent.target))
                    {
                        intent.target = intent.anchor;
                    }
                    break;

                case "scale":
                case "size":
                case "magnitude":
                    if (allowScale && TryParseFloat(value, out float scale))
                        intent.scale = scale;
                    break;

                case "duration":
                case "time":
                case "lifetime":
                    if (allowDuration && TryParseFloat(value, out float duration))
                        intent.duration = duration;
                    break;

                case "intensity":
                case "power":
                case "strength":
                case "brightness":
                    if (TryParseFloat(value, out float intensity))
                        intent.intensity = intensity;
                    break;

                case "color":
                case "tint":
                case "hue":
                    if (allowColor)
                        intent.color = ParseColor(value);
                    break;

                case "target":
                case "at":
                case "on":
                case "near":
                case "to":
                case "object":
                case "targetname":
                case "targetid":
                    intent.target = CleanTargetValue(value);
                    break;

                case "collision":
                case "collisionpolicy":
                case "collisionmode":
                case "collider":
                case "colliders":
                    intent.collisionPolicy = TrimWrappedToken(value);
                    break;

                case "groundsnap":
                case "snapground":
                case "snaptoground":
                case "grounded":
                    if (TryParseBool(value, out bool groundSnap))
                    {
                        intent.groundSnap = groundSnap;
                    }
                    break;

                case "los":
                case "lineofsight":
                case "requirelos":
                case "requireslineofsight":
                    if (TryParseBool(value, out bool requiresLos))
                    {
                        intent.requireLineOfSight = requiresLos;
                    }
                    break;

                case "placement":
                case "mode":
                case "spawnmode":
                case "effectmode":
                case "effecttype":
                case "type":
                    if (TryNormalizePlacementType(value, out string placementType))
                    {
                        intent.placementType = placementType;
                    }
                    break;

                case "radius":
                case "range":
                case "spread":
                case "area":
                case "coverage":
                case "aoe":
                    if (TryParseFloat(value, out float radius))
                    {
                        intent.radius = radius;
                    }
                    break;

                case "speed":
                case "velocity":
                case "projectilespeed":
                case "travelspeed":
                case "shotspeed":
                    if (TryParseFloat(value, out float speed))
                    {
                        intent.speed = speed;
                    }
                    break;

                case "emotion":
                case "tone":
                case "mood":
                case "feeling":
                    intent.emotion = value.Trim().Trim('"', '\'').ToLowerInvariant();
                    break;

                case "damage":
                case "damageamount":
                case "dmg":
                case "hurt":
                    if (TryParseFloat(value, out float damage))
                    {
                        intent.damage = damage;
                    }
                    break;

            }
        }

        /// <summary>
        /// Try to parse a float with invariant culture.
        /// </summary>
        private static bool TryParseFloat(string value, out float result)
        {
            result = 0f;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            bool isPercent = trimmed.Contains("%");
            if (trimmed.StartsWith("x", System.StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1).Trim();
            }
            if (trimmed.EndsWith("x", System.StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
            }

            if (
                float.TryParse(
                    trimmed,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float parsed
                )
            )
            {
                result = isPercent ? parsed / 100f : parsed;
                return true;
            }

            Match numericMatch = NumberRegex.Match(trimmed);
            if (!numericMatch.Success)
            {
                return false;
            }

            string numericToken = numericMatch.Value.Replace(',', '.');
            if (
                float.TryParse(
                    numericToken,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed
                )
            )
            {
                result = isPercent ? parsed / 100f : parsed;
                return true;
            }

            return false;
        }

        private static bool TryParseBool(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string lower = value.Trim().Trim('"', '\'').ToLowerInvariant();
            switch (lower)
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                case "enable":
                case "enabled":
                case "required":
                case "strict":
                    result = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                case "disable":
                case "disabled":
                    result = false;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryNormalizePlacementType(string value, out string placementType)
        {
            placementType = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().Trim('"', '\'').ToLowerInvariant();
            normalized = normalized.Replace("-", "_").Replace(" ", "_");
            switch (normalized)
            {
                case "projectile":
                case "bolt":
                case "missile":
                case "shot":
                    placementType = "projectile";
                    return true;
                case "aoe":
                case "area":
                case "area_of_effect":
                case "blast":
                case "explosion":
                    placementType = "area";
                    return true;
                case "attached":
                case "attach":
                case "self":
                case "aura":
                    placementType = "attached";
                    return true;
                case "ambient":
                case "world":
                case "scene":
                    placementType = "ambient";
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Parse a color from string (hex or named color).
        /// </summary>
        private static Color ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Color.white;

            Match rgbMatch = RgbRegex.Match(value);
            if (rgbMatch.Success)
            {
                float r = Mathf.Clamp(int.Parse(rgbMatch.Groups["r"].Value), 0, 255) / 255f;
                float g = Mathf.Clamp(int.Parse(rgbMatch.Groups["g"].Value), 0, 255) / 255f;
                float b = Mathf.Clamp(int.Parse(rgbMatch.Groups["b"].Value), 0, 255) / 255f;
                float a = 1f;
                if (rgbMatch.Groups["a"].Success)
                {
                    if (
                        float.TryParse(
                            rgbMatch.Groups["a"].Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out float parsedA
                        )
                    )
                    {
                        a = Mathf.Clamp01(parsedA);
                    }
                }

                return new Color(r, g, b, a);
            }

            // Try hex format (#RRGGBB or RRGGBB)
            if (value.StartsWith("#"))
            {
                if (ColorUtility.TryParseHtmlString(value, out Color color))
                    return color;
            }
            else
            {
                // Try with # prefix
                if (ColorUtility.TryParseHtmlString("#" + value, out Color color))
                    return color;
            }

            // Try named colors
            value = value.ToLowerInvariant().Trim();
            return value switch
            {
                // Standard named colors
                "red" => Color.red,
                "blue" => Color.blue,
                "green" => Color.green,
                "yellow" => Color.yellow,
                "white" => Color.white,
                "black" => Color.black,
                "cyan" => Color.cyan,
                "magenta" => Color.magenta,
                "gray" or "grey" => Color.gray,
                "crimson" => new Color(0.86f, 0.08f, 0.24f),
                "scarlet" => new Color(1f, 0.14f, 0f),
                "azure" => new Color(0.0f, 0.5f, 1f),
                "teal" => new Color(0f, 0.5f, 0.5f),
                "gold" => new Color(1f, 0.84f, 0f),
                "silver" => new Color(0.75f, 0.75f, 0.75f),
                "orange" => new Color(1f, 0.5f, 0f),
                "purple" => new Color(0.5f, 0f, 0.5f),
                "pink" => new Color(1f, 0.5f, 0.7f),

                // Element-themed keywords (matched to prompt guide: Color: fire/ice/storm/nature)
                "fire" or "flame" or "inferno" => new Color(1f, 0.35f, 0f),
                "ice" or "frost" or "frozen" => new Color(0.6f, 0.85f, 1f),
                "storm" or "lightning" or "thunder" or "electric" => new Color(0.55f, 0.35f, 1f),
                "nature" or "earth" or "flora" => new Color(0.2f, 0.7f, 0.2f),
                "shadow" or "dark" or "void" or "darkness" => new Color(0.15f, 0.05f, 0.2f),
                "holy" or "light" or "divine" or "radiant" => new Color(1f, 0.95f, 0.7f),
                "arcane" or "magic" or "mystic" => new Color(0.6f, 0.2f, 1f),
                "blood" => new Color(0.55f, 0f, 0.05f),
                "poison" or "toxic" or "venom" => new Color(0.4f, 0.9f, 0.1f),
                "water" or "ocean" or "aqua" => new Color(0.1f, 0.4f, 0.8f),

                _ => Color.white,
            };
        }

        private static bool IsTagNameKey(string normalizedKey)
        {
            return normalizedKey
                is "name"
                    or "effect"
                    or "effectname"
                    or "effecttag"
                    or "power"
                    or "powername"
                    or "tag"
                    or "fx";
        }

        private static bool TrySplitKeyValue(string part, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(part))
            {
                return false;
            }

            int separatorIndex = part.IndexOf(':');
            if (separatorIndex <= 0)
            {
                separatorIndex = part.IndexOf('=');
            }

            if (separatorIndex <= 0)
            {
                return false;
            }

            key = part.Substring(0, separatorIndex).Trim();
            value = part.Substring(separatorIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value);
        }

        private static string TrimWrappedToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Trim('"', '\'', '[', ']', '{', '}', '(', ')');
        }

        private static string CleanTargetValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = TrimWrappedToken(value);
            string lower = cleaned.ToLowerInvariant();
            string[] prefixes = { "at ", "on ", "near ", "around ", "to " };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (lower.StartsWith(prefixes[i]))
                {
                    cleaned = cleaned.Substring(prefixes[i].Length).Trim();
                    break;
                }
            }

            return cleaned;
        }

        private static void ParseLooseParameter(EffectIntent intent, string token)
        {
            if (intent == null || string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            string cleaned = TrimWrappedToken(token);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return;
            }

            string lower = cleaned.ToLowerInvariant();
            if (lower is "player" or "listener" or "self" or "npc" or "caster")
            {
                intent.target = cleaned;
                return;
            }

            if (TryNormalizePlacementType(cleaned, out string placementType))
            {
                intent.placementType = placementType;
                return;
            }

            string[] targetPrefixes = { "target ", "at ", "on ", "near ", "around " };
            for (int i = 0; i < targetPrefixes.Length; i++)
            {
                string prefix = targetPrefixes[i];
                if (lower.StartsWith(prefix))
                {
                    string target = cleaned.Substring(prefix.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        intent.target = target;
                    }
                    return;
                }
            }
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return key.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        }

        private static string[] SplitParts(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new string[0];
            }

            char[] separators = null;
            if (content.Contains("|"))
            {
                separators = new[] { '|' };
            }
            else if (content.Contains(";"))
            {
                separators = new[] { ';' };
            }
            else if (content.Contains("\n"))
            {
                separators = new[] { '\n' };
            }
            else if (content.Contains(","))
            {
                separators = new[] { ',' };
            }

            if (separators == null)
            {
                return new[] { content.Trim() };
            }

            return content
                .Split(separators, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
        }

        private static bool TryResolveDefinition(
            EffectCatalog catalog,
            string rawTag,
            out EffectDefinition definition
        )
        {
            definition = null;
            if (catalog == null || string.IsNullOrWhiteSpace(rawTag))
            {
                return false;
            }

            if (catalog.TryGet(rawTag.Trim(), out definition))
            {
                return true;
            }

            string normalized = NormalizeTag(rawTag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            int bestScore = -1;
            EffectDefinition bestDefinition = null;
            for (int i = 0; i < catalog.allEffects.Count; i++)
            {
                EffectDefinition effect = catalog.allEffects[i];
                if (effect == null || string.IsNullOrWhiteSpace(effect.effectTag))
                {
                    continue;
                }

                int score = ScoreTagMatch(normalized, effect.effectTag);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDefinition = effect;
                }

                if (effect.alternativeTags == null)
                {
                    continue;
                }

                for (int j = 0; j < effect.alternativeTags.Length; j++)
                {
                    string alt = effect.alternativeTags[j];
                    if (string.IsNullOrWhiteSpace(alt))
                    {
                        continue;
                    }

                    score = ScoreTagMatch(normalized, alt);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDefinition = effect;
                    }
                }
            }

            if (bestDefinition != null && bestScore >= 5)
            {
                definition = bestDefinition;
                return true;
            }

            return false;
        }

        private static string NormalizeTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private static int ScoreTagMatch(string normalizedInput, string candidate)
        {
            string normalizedCandidate = NormalizeTag(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return -1;
            }

            if (normalizedInput == normalizedCandidate)
            {
                return 100;
            }

            if (
                normalizedInput.StartsWith(normalizedCandidate)
                || normalizedCandidate.StartsWith(normalizedInput)
            )
            {
                return Mathf.Min(normalizedInput.Length, normalizedCandidate.Length) + 20;
            }

            if (
                normalizedInput.Contains(normalizedCandidate)
                || normalizedCandidate.Contains(normalizedInput)
            )
            {
                return Mathf.Min(normalizedInput.Length, normalizedCandidate.Length) + 10;
            }

            int prefix = 0;
            int minLength = Mathf.Min(normalizedInput.Length, normalizedCandidate.Length);
            while (prefix < minLength && normalizedInput[prefix] == normalizedCandidate[prefix])
            {
                prefix++;
            }

            return prefix;
        }

        /// <summary>
        /// Strip all [EFFECT:] tags from text.
        /// </summary>
        public static string StripTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            bool hadEffectMarker =
                text.IndexOf("EFFECT", System.StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("FX", System.StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("POWER", System.StringComparison.OrdinalIgnoreCase) >= 0;

            string stripped = TagRegex.Replace(text, "");
            stripped = BareTagRegex.Replace(stripped, "");
            stripped = UnterminatedTagRegex.Replace(stripped, "");
            if (hadEffectMarker)
            {
                stripped = DanglingParameterTailRegex.Replace(stripped, "");
            }
            return stripped.Trim();
        }

    }
}
