using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Network_Game.Dialogue.Effects;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Dialogue.Editor
{
    /// <summary>
    /// Editor utility to run ALL dialogue FX effects sequentially in Play Mode.
    /// Automatically tests every effect keyword to diagnose what's working vs broken.
    /// Results are printed to console for easy analysis.
    /// </summary>
    public static class DialogueEffectAutoTestMenu
    {
        private const string MenuPath = "Network Game/MCP/Auto Test All Effects";

        private static bool s_Subscribed;
        private static bool s_Running;
        private static int s_CurrentEffectIndex;
        private static int s_ClientRequestId;
        private static int s_NextClientRequestId = 99200;
        private static List<EffectTestCase> s_EffectTestCases;
        private static List<EffectTestResult> s_Results;
        private static NpcDialogueActor s_TargetActor;
        private static NetworkDialogueService s_Service;
        private static string s_ConversationKey;
        private static bool s_DirectSuiteRunning;
        private static Coroutine s_DirectSuiteCoroutine;
        private static int s_DirectSuiteObservedEffects;
        private static int s_DirectSuiteDispatchedSteps;
        private static int s_DirectSuiteFailedSteps;

        private sealed class DirectEffectStep
        {
            public string Label;
            public float WaitSeconds;
            public Func<bool> Execute;
        }

        /// <summary>
        /// Represents a single effect test case
        /// </summary>
        private class EffectTestCase
        {
            public string EffectName;
            public string[] Keywords;
            public string EffectType; // "prefab_power", "bored_lighting", etc.
            public string FormattedPrompt;
        }

        /// <summary>
        /// Result of a single effect test
        /// </summary>
        private class EffectTestResult
        {
            public string EffectName;
            public string Keyword;
            public bool LlmEmittedEffectTag;
            public bool EffectParsed;
            public bool EffectDispatched;
            public string RawResponse;
            public string Error;
        }

        [MenuItem(MenuPath)]
        public static void RunAutoTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[EffectAutoTest] ENTER PLAY MODE FIRST!");
                return;
            }

            if (s_Running)
            {
                Debug.LogWarning("[EffectAutoTest] Test already in progress.");
                return;
            }

            // Get service
            s_Service = NetworkDialogueService.Instance;
            if (s_Service == null)
            {
                Debug.LogError("[EffectAutoTest] NetworkDialogueService.Instance is null.");
                return;
            }

            // Get local player
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            {
                Debug.LogError("[EffectAutoTest] No local Netcode client.");
                return;
            }

            NetworkObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayer == null)
            {
                Debug.LogError("[EffectAutoTest] Local player object is null.");
                return;
            }

            // Find NPC actor
#if UNITY_2023_1_OR_NEWER
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif

            if (actors == null || actors.Length == 0)
            {
                Debug.LogError("[EffectAutoTest] No NpcDialogueActor found in scene.");
                return;
            }

            s_TargetActor = actors[0];
            if (s_TargetActor == null || s_TargetActor.NetworkObject == null)
            {
                Debug.LogError("[EffectAutoTest] Selected NPC actor invalid.");
                return;
            }

            // Resolve conversation key
            ulong speakerId = s_TargetActor.NetworkObjectId;
            ulong listenerId = localPlayer.NetworkObjectId;
            ulong clientId = localPlayer.OwnerClientId;
            s_ConversationKey = s_Service.ResolveConversationKey(
                speakerId,
                listenerId,
                clientId,
                null
            );

            // Build test cases from NPC profile
            BuildTestCases();

            if (s_EffectTestCases.Count == 0)
            {
                Debug.LogError(
                    "[EffectAutoTest] No effect test cases found. Check NPC profile powers."
                );
                return;
            }

            // Subscribe to responses
            if (!s_Subscribed)
            {
                NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
                s_Subscribed = true;
            }

            // Start testing
            s_Running = true;
            s_CurrentEffectIndex = 0;
            s_Results = new List<EffectTestResult>();

            Debug.Log($"[EffectAutoTest] Starting {s_EffectTestCases.Count} effect tests...");
            string actorName =
                s_TargetActor?.Profile != null ? s_TargetActor.Profile.DisplayName
                : s_TargetActor != null ? s_TargetActor.ProfileId
                : "Unknown";
            Debug.Log($"[EffectAutoTest] Target NPC: {actorName}");
            Debug.Log($"[EffectAutoTest] Conversation key: {s_ConversationKey}");

            // Start first test
            RunNextTest();
        }

        private static void BuildTestCases()
        {
            s_EffectTestCases = new List<EffectTestCase>();

            // Get NPC profile
            var profile = s_TargetActor?.Profile;
            if (profile == null)
            {
                Debug.LogError("[EffectAutoTest] NPC has no profile assigned.");
                return;
            }

            // Add prefab powers from profile
            var powers = profile.PrefabPowers;
            if (powers != null)
            {
                foreach (var power in powers)
                {
                    if (
                        power == null
                        || !power.Enabled
                        || power.Keywords == null
                        || power.Keywords.Length == 0
                    )
                        continue;

                    foreach (var keyword in power.Keywords)
                    {
                        if (string.IsNullOrWhiteSpace(keyword))
                            continue;

                        s_EffectTestCases.Add(
                            new EffectTestCase
                            {
                                EffectName = power.PowerName,
                                Keywords = new[] { keyword },
                                EffectType = "prefab_power",
                                FormattedPrompt =
                                    $"Use your {keyword} power on the player. Reply in-character with one short sentence, then include [EFFECT: {power.PowerName} | Target: Player | Intensity: 1.0] at the end.",
                            }
                        );
                    }
                }
            }

            // Add bored lighting keywords
            if (profile.EnableBoredLightEffect && profile.BoredKeywords != null)
            {
                foreach (var keyword in profile.BoredKeywords)
                {
                    if (string.IsNullOrWhiteSpace(keyword))
                        continue;

                    s_EffectTestCases.Add(
                        new EffectTestCase
                        {
                            EffectName = "Bored Lighting",
                            Keywords = new[] { keyword },
                            EffectType = "bored_lighting",
                            FormattedPrompt =
                                $"Pretend you're {keyword}. Reply in-character, then trigger the bored lighting effect. Include [EFFECT: bored_lighting] at the end.",
                        }
                    );
                }
            }

            Debug.Log(
                $"[EffectAutoTest] Built {s_EffectTestCases.Count} test cases from profile: {profile.DisplayName}"
            );
        }

        private static void RunNextTest()
        {
            if (s_CurrentEffectIndex >= s_EffectTestCases.Count)
            {
                FinishAllTests();
                return;
            }

            var testCase = s_EffectTestCases[s_CurrentEffectIndex];
            s_ClientRequestId = ++s_NextClientRequestId;

            Debug.Log(
                $"\n[EffectAutoTest] === Test {s_CurrentEffectIndex + 1}/{s_EffectTestCases.Count}: {testCase.EffectName} (keyword: {testCase.Keywords[0]}) ==="
            );

            s_Service.RequestDialogue(
                new NetworkDialogueService.DialogueRequest
                {
                    Prompt = testCase.FormattedPrompt,
                    ConversationKey = s_ConversationKey,
                    SpeakerNetworkId = s_TargetActor.NetworkObjectId,
                    ListenerNetworkId = NetworkManager
                        .Singleton
                        .LocalClient
                        .PlayerObject
                        .NetworkObjectId,
                    RequestingClientId = NetworkManager.Singleton.LocalClientId,
                    Broadcast = false,
                    BroadcastDuration = 0f,
                    NotifyClient = true,
                    ClientRequestId = s_ClientRequestId,
                    IsUserInitiated = true,
                    BlockRepeatedPrompt = false,
                    MinRepeatDelaySeconds = 0f,
                    RequireUserReply = false,
                }
            );
        }

        private static void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            if (response.Request.ClientRequestId != s_ClientRequestId)
                return;

            var testCase = s_EffectTestCases[s_CurrentEffectIndex];
            var result = new EffectTestResult
            {
                EffectName = testCase.EffectName,
                Keyword = testCase.Keywords[0],
                RawResponse = response.ResponseText ?? string.Empty,
                Error = response.Error,
            };

            // Check if LLM emitted effect tag
            string text = response.ResponseText ?? string.Empty;
            bool hasEffectTag =
                text.IndexOf("[EFFECT:", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("[FX:", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("EFFECT:", StringComparison.OrdinalIgnoreCase) >= 0;

            result.LlmEmittedEffectTag = hasEffectTag;

            // Try to parse effect
            if (hasEffectTag)
            {
                try
                {
                    EffectCatalog catalog = EffectCatalog.Load();
                    var intents = EffectParser.ExtractIntents(text, catalog, stripTags: false);
                    result.EffectParsed =
                        intents != null && intents.Count > 0 && intents[0].definition != null;

                    Debug.Log($"[EffectAutoTest]   → LLM emitted tag: YES");
                    Debug.Log(
                        $"[EffectAutoTest]   → Parsed: {(result.EffectParsed ? "YES" : "NO")}"
                    );
                    if (!result.EffectParsed && intents.Count > 0)
                    {
                        Debug.Log(
                            $"[EffectAutoTest]   → First intent: tag='{intents[0].rawTagName}', definition={(intents[0].definition == null ? "NULL" : "OK")}"
                        );
                    }
                }
                catch (InvalidOperationException ex)
                {
                    result.EffectParsed = false;
                    result.Error = ex.Message;
                    Debug.LogError($"[EffectAutoTest]   → Parse error: {ex.Message}");
                }
                catch (ArgumentException ex)
                {
                    result.EffectParsed = false;
                    result.Error = ex.Message;
                    Debug.LogError($"[EffectAutoTest]   → Parse error: {ex.Message}");
                }
            }
            else
            {
                Debug.Log(
                    $"[EffectAutoTest]   → LLM emitted tag: NO (LLM may not understand format)"
                );
                Debug.Log(
                    $"[EffectAutoTest]   → Response: {text.Substring(0, Math.Min(200, text.Length))}..."
                );
            }

            s_Results.Add(result);
            s_CurrentEffectIndex++;

            // Delay before next test to avoid rate limiting
            s_Service.StartCoroutine(DelayNextTest());
        }

        private static IEnumerator DelayNextTest()
        {
            yield return new WaitForSeconds(1.5f); // Wait for effects to play
            RunNextTest();
        }

        private static void FinishAllTests()
        {
            s_Running = false;

            Debug.Log("\n" + "=".PadRight(60, '='));
            Debug.Log("[EffectAutoTest] ===== FINAL RESULTS =====");
            Debug.Log("=".PadRight(60, '='));

            int total = s_Results.Count;
            int llmEmitted = s_Results.Count(r => r.LlmEmittedEffectTag);
            int parsed = s_Results.Count(r => r.EffectParsed);

            Debug.Log(
                $"\nSummary: {llmEmitted}/{total} LLM emitted tags, {parsed}/{total} successfully parsed\n"
            );

            // Group by effectiveness
            var working = s_Results.Where(r => r.EffectParsed).ToList();
            var llmWorksButNotParsed = s_Results
                .Where(r => r.LlmEmittedEffectTag && !r.EffectParsed)
                .ToList();
            var llmFails = s_Results.Where(r => !r.LlmEmittedEffectTag).ToList();

            if (working.Count > 0)
            {
                Debug.Log("✅ WORKING EFFECTS (LLM emits + parses correctly):");
                foreach (var r in working)
                {
                    Debug.Log($"   • {r.EffectName} (keyword: {r.Keyword})");
                }
            }

            if (llmWorksButNotParsed.Count > 0)
            {
                Debug.Log(
                    "\n⚠️ LLM EMITS BUT NOT PARSED (LLM understands format, but tag not in catalog):"
                );
                foreach (var r in llmWorksButNotParsed)
                {
                    Debug.Log($"   • {r.EffectName} (keyword: {r.Keyword})");
                    Debug.Log(
                        $"     Response excerpt: {r.RawResponse.Substring(0, Math.Min(150, r.RawResponse.Length))}..."
                    );
                }
            }

            if (llmFails.Count > 0)
            {
                Debug.Log("\n❌ LLM NOT EMITTING TAGS (LLM doesn't understand effect format):");
                foreach (var r in llmFails)
                {
                    Debug.Log($"   • {r.EffectName} (keyword: {r.Keyword})");
                    Debug.Log(
                        $"     Response: {r.RawResponse.Substring(0, Math.Min(100, r.RawResponse.Length))}..."
                    );
                }
            }

            Debug.Log("\n" + "=".PadRight(60, '='));
            Debug.Log("[EffectAutoTest] RECOMMENDATIONS:");
            Debug.Log("=".PadRight(60, '='));

            if (llmFails.Count > 0)
            {
                Debug.Log("\n🔧 TO FIX 'LLM NOT EMITTING TAGS':");
                Debug.Log(
                    "   1. The NPC's system prompt needs explicit effect format instructions"
                );
                Debug.Log("   2. Add this to the NPC's SystemPrompt or Lore:");
                Debug.Log("   ---");
                Debug.Log("   When you use powers, you MUST include effect tags in your response.");
                Debug.Log("   Format: [EFFECT: EffectName | Target: Player | Intensity: 1.0]");
                Debug.Log(
                    "   Example: 'Feel the storm!' [EFFECT: Lightning Storm | Target: Player | Intensity: 1.5]"
                );
                Debug.Log("   ---");
            }

            if (llmWorksButNotParsed.Count > 0)
            {
                Debug.Log("\n🔧 TO FIX 'EMITS BUT NOT PARSED':");
                Debug.Log("   1. The effect tag name doesn't match any EffectCatalog definition");
                Debug.Log("   2. Check EffectCatalog.asset has entries matching these names:");
                HashSet<string> seenResponses = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in llmWorksButNotParsed)
                {
                    if (!seenResponses.Add(r.RawResponse ?? string.Empty))
                    {
                        continue;
                    }

                    var match = System.Text.RegularExpressions.Regex.Match(
                        r.RawResponse,
                        @"\[EFFECT:\s*(\w+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    );
                    if (match.Success)
                    {
                        Debug.Log(
                            $"   • LLM used: '{match.Groups[1].Value}' - check if this matches an EffectDefinition.effectTag"
                        );
                    }
                }
            }

            Debug.Log("\n[EffectAutoTest] Test complete! Check console for details.");
        }

        /// <summary>
        /// Print the current effect format instructions that should be in NPC prompts
        /// </summary>
        [MenuItem("Network Game/MCP/Legacy/Print Effect Format Instructions")]
        public static void PrintEffectInstructions()
        {
            Debug.Log("\n" + "============================================================");
            Debug.Log("EFFECT FORMAT INSTRUCTIONS - COPY TO NPC SYSTEM PROMPT");
            Debug.Log("============================================================\n");

            Debug.Log(
@"When you use powers, include effect tags in your response.
Format: [EFFECT: EffectName | Target: Player | Intensity: 1.0 | Scale: 1.0 | Duration: 4 | Emotion: epic | Damage: 1.0]

Available parameters (all optional except EffectName):
- Target:   Player, NPC, or object name
- Intensity: 0.1 to 3.0 (how powerful / particle density)
- Duration:  seconds the effect lasts (1 to 20)
- Scale:     visual size multiplier (0.5 to 3.0)
- Color:     red | blue | green | yellow | orange | purple | fire | ice | storm | void | #RRGGBB
- Radius:    area-of-effect radius multiplier (0.5 to 3.0)
- Speed:     projectile or particle speed multiplier (0.5 to 3.0)
- Emotion:   peaceful | threatening | triumphant | chaotic | epic | sad
- Damage:    damage multiplier relative to base (0.5 to 2.0)

Example responses:
- 'Lightning strikes!' [EFFECT: Lightning Storm | Target: Player | Intensity: 2.0 | Emotion: epic]
- 'Ice freezes all!'   [EFFECT: Ice Lance | Target: Player | Duration: 5 | Color: ice | Damage: 1.5]
- 'Fireball incoming!' [EFFECT: Fire Ball | Target: Player | Color: fire | Scale: 1.8 | Emotion: threatening]
- 'Healing light...'   [EFFECT: Holy Light | Target: Player | Emotion: peaceful | Duration: 8]
"
            );

            Debug.Log("============================================================\n");

            // Also print available effects
            var catalog = EffectCatalog.Load();
            if (catalog != null)
            {
                Debug.Log("Registered EffectCatalog effects:");
                foreach (var effect in catalog.allEffects)
                {
                    if (effect == null)
                        continue;
                    Debug.Log($"  • {effect.effectTag}: {effect.description}");
                }
            }

            // Print profile powers
            var profiles = Resources.LoadAll<NpcDialogueProfile>("");
            foreach (var profile in profiles)
            {
                Debug.Log($"\nProfile '{profile.DisplayName}' available powers:");
                foreach (var power in profile.PrefabPowers)
                {
                    if (power == null || !power.Enabled)
                        continue;
                    Debug.Log($"  • {power.PowerName}");
                    Debug.Log(
                        $"    Keywords: {string.Join(", ", power.Keywords ?? Array.Empty<string>())}"
                    );
                    if (!string.IsNullOrEmpty(power.VisualDescription))
                    {
                        Debug.Log($"    Visual: {power.VisualDescription}");
                    }
                }
            }
        }

        /// <summary>
        /// Quick test - force a specific effect tag without LLM
        /// </summary>
        [MenuItem("Network Game/MCP/Legacy/Force Effect Tag Test (LLM)")]
        public static void ForceEffectTagTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[EffectAutoTest] ENTER PLAY MODE FIRST!");
                return;
            }

            var service = NetworkDialogueService.Instance;
            if (service == null)
            {
                Debug.LogError("[EffectAutoTest] No service!");
                return;
            }

#if UNITY_2023_1_OR_NEWER
            var actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            var actors = UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif

            if (actors.Length == 0)
            {
                Debug.LogError("[EffectAutoTest] No actors!");
                return;
            }

            var actor = actors[0];
            var profile = actor.Profile;

            // Get first power
            if (profile?.PrefabPowers?.Length > 0)
            {
                var power = profile.PrefabPowers[0];
                string forcedTag = $"[EFFECT: {power.PowerName} | Target: Player | Intensity: 1.5]";

                string prompt =
                    $"Reply with exactly this (for testing): 'Test response.' {forcedTag}";

                var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
                var key = service.ResolveConversationKey(
                    actor.NetworkObjectId,
                    localPlayer.NetworkObjectId,
                    localPlayer.OwnerClientId,
                    null
                );

                service.RequestDialogue(
                    new NetworkDialogueService.DialogueRequest
                    {
                        Prompt = prompt,
                        ConversationKey = key,
                        SpeakerNetworkId = actor.NetworkObjectId,
                        ListenerNetworkId = localPlayer.NetworkObjectId,
                        RequestingClientId = localPlayer.OwnerClientId,
                        Broadcast = false,
                        NotifyClient = true,
                        ClientRequestId = 99999,
                        IsUserInitiated = true,
                        RequireUserReply = false,
                    }
                );

                Debug.Log($"[EffectAutoTest] Forced test with tag: {forcedTag}");
            }
        }

        /// <summary>
        /// Deterministic runtime effect test for MCP automation.
        /// Bypasses dialogue/LLM queueing and directly spawns a real NPC profile power
        /// through DialogueSceneEffectsController using the local player as target.
        /// </summary>
        [MenuItem("Network Game/MCP/Spawn Direct NPC Power (No LLM)")]
        public static void SpawnDirectNpcPowerNoLlm()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[EffectAutoTest] ENTER PLAY MODE FIRST!");
                return;
            }

            DialogueSceneEffectsController controller = DialogueSceneEffectsController.Instance;
            if (controller == null)
            {
                Debug.LogError("[EffectAutoTest] DialogueSceneEffectsController.Instance is null.");
                return;
            }

            if (!TryGetLocalPlayer(out NetworkObject localPlayer))
            {
                Debug.LogError("[EffectAutoTest] Local player NetworkObject not available.");
                return;
            }

            if (!TryResolveNpcPowerForDirectSpawn(out NpcDialogueActor actor, out PrefabPowerEntry power))
            {
                Debug.LogError(
                    "[EffectAutoTest] Could not find an NPC with an enabled prefab power and assigned effect prefab."
                );
                return;
            }

            ExecuteDirectNpcPower(controller, localPlayer, actor, power, logResult: true);
        }

        [MenuItem("Network Game/MCP/Auto Test All Effects Direct (No LLM)")]
        public static void RunDirectAutoTestAllEffectsNoLlm()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[EffectAutoTest] ENTER PLAY MODE FIRST!");
                return;
            }

            if (s_DirectSuiteRunning)
            {
                Debug.LogWarning("[EffectAutoTest] Direct auto test already running.");
                return;
            }

            DialogueSceneEffectsController controller = DialogueSceneEffectsController.Instance;
            if (controller == null)
            {
                Debug.LogError("[EffectAutoTest] DialogueSceneEffectsController.Instance is null.");
                return;
            }

            s_DirectSuiteRunning = true;
            s_DirectSuiteObservedEffects = 0;
            s_DirectSuiteDispatchedSteps = 0;
            s_DirectSuiteFailedSteps = 0;
            DialogueSceneEffectsController.OnEffectApplied += HandleDirectSuiteEffectObserved;
            s_DirectSuiteCoroutine = controller.StartCoroutine(RunDirectSuiteCoroutine(controller));
            Debug.Log("[EffectAutoTest] Direct auto test (no LLM) started.");
        }

        [MenuItem("Network Game/MCP/Stop Direct Auto Test All Effects")]
        public static void StopDirectAutoTestAllEffects()
        {
            if (!s_DirectSuiteRunning)
            {
                Debug.Log("[EffectAutoTest] No direct auto test is running.");
                return;
            }

            DialogueSceneEffectsController controller = DialogueSceneEffectsController.Instance;
            if (controller != null && s_DirectSuiteCoroutine != null)
            {
                controller.StopCoroutine(s_DirectSuiteCoroutine);
            }

            EndDirectSuite("stopped");
        }

        private static IEnumerator RunDirectSuiteCoroutine(DialogueSceneEffectsController controller)
        {
            float waitStart = Time.realtimeSinceStartup;
            NetworkObject localPlayer = null;
            while ((localPlayer == null) && (Time.realtimeSinceStartup - waitStart) < 12f)
            {
                TryGetLocalPlayer(out localPlayer);
                if (localPlayer != null)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (localPlayer == null)
            {
                Debug.LogError(
                    "[EffectAutoTest] Direct auto test aborted: local player NetworkObject not available after waiting."
                );
                EndDirectSuite("no_local_player");
                yield break;
            }

            List<DirectEffectStep> steps = BuildDirectEffectSteps(controller, localPlayer);
            if (steps.Count == 0)
            {
                Debug.LogError(
                    "[EffectAutoTest] Direct auto test aborted: no executable effect steps were found."
                );
                EndDirectSuite("no_steps");
                yield break;
            }

            Debug.Log($"[EffectAutoTest] Direct auto test built {steps.Count} steps.");

            for (int i = 0; i < steps.Count; i++)
            {
                if (!s_DirectSuiteRunning)
                {
                    yield break;
                }

                DirectEffectStep step = steps[i];
                bool ok = false;
                try
                {
                    ok = step.Execute != null && step.Execute();
                }
                catch (Exception ex)
                {
                    ok = false;
                    Debug.LogError(
                        $"[EffectAutoTest] Direct step threw exception ({i + 1}/{steps.Count}) {step.Label}: {ex.Message}"
                    );
                }

                s_DirectSuiteDispatchedSteps++;
                if (!ok)
                {
                    s_DirectSuiteFailedSteps++;
                }

                Debug.Log(
                    $"[EffectAutoTest] Direct step {i + 1}/{steps.Count}: {(ok ? "OK" : "FAIL")} | {step.Label}"
                );

                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, step.WaitSeconds));
            }

            EndDirectSuite("completed");
        }

        private static List<DirectEffectStep> BuildDirectEffectSteps(
            DialogueSceneEffectsController controller,
            NetworkObject localPlayer
        )
        {
            var steps = new List<DirectEffectStep>(64);

#if UNITY_2023_1_OR_NEWER
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif

            IEnumerable<NpcDialogueActor> orderedActors = (actors ?? Array.Empty<NpcDialogueActor>())
                .Where(a => a != null && a.NetworkObject != null)
                .OrderBy(a => a.ProfileId ?? a.name, StringComparer.Ordinal);

            foreach (NpcDialogueActor actor in orderedActors)
            {
                PrefabPowerEntry[] powers = actor.Profile != null ? actor.Profile.PrefabPowers : null;
                if (powers == null || powers.Length == 0)
                {
                    continue;
                }

                for (int i = 0; i < powers.Length; i++)
                {
                    PrefabPowerEntry power = powers[i];
                    if (power == null || !power.Enabled || power.EffectPrefab == null)
                    {
                        continue;
                    }

                    NpcDialogueActor capturedActor = actor;
                    PrefabPowerEntry capturedPower = power;
                    float wait = Mathf.Clamp(
                        Mathf.Max(0.4f, capturedPower.DurationSeconds * 0.35f),
                        0.5f,
                        2.25f
                    );
                    steps.Add(
                        new DirectEffectStep
                        {
                            Label =
                                $"NPC:{(capturedActor.ProfileId ?? capturedActor.name)} Power:{capturedPower.PowerName} Prefab:{capturedPower.EffectPrefab.name}",
                            WaitSeconds = wait,
                            Execute = () =>
                                ExecuteDirectNpcPower(
                                    controller,
                                    localPlayer,
                                    capturedActor,
                                    capturedPower,
                                    logResult: false
                                ),
                        }
                    );
                }
            }

            ulong playerId = localPlayer.NetworkObjectId;
            steps.Add(
                new DirectEffectStep
                {
                    Label = "SceneFX:BoredLighting warm pulse",
                    WaitSeconds = 0.7f,
                    Execute = () =>
                    {
                        controller.ApplyBoredLighting(new Color(1f, 0.55f, 0.15f), 1.15f, 0.35f);
                        return true;
                    },
                }
            );
            steps.Add(
                new DirectEffectStep
                {
                    Label = "SceneFX:BoredLighting cool pulse",
                    WaitSeconds = 0.7f,
                    Execute = () =>
                    {
                        controller.ApplyBoredLighting(new Color(0.2f, 0.65f, 1f), 0.95f, 0.35f);
                        return true;
                    },
                }
            );
            steps.Add(
                new DirectEffectStep
                {
                    Label = $"SceneFX:Dissolve player:{playerId}",
                    WaitSeconds = 1.2f,
                    Execute = () =>
                    {
                        controller.ApplyDissolveEffect(playerId, 1.2f);
                        return true;
                    },
                }
            );
            steps.Add(
                new DirectEffectStep
                {
                    Label = $"SceneFX:Respawn player:{playerId}",
                    WaitSeconds = 0.6f,
                    Execute = () =>
                    {
                        controller.ApplyRespawnEffect(playerId);
                        return true;
                    },
                }
            );

            return steps;
        }

        private static void HandleDirectSuiteEffectObserved(DialogueSceneEffectsController.AppliedEffectInfo _)
        {
            s_DirectSuiteObservedEffects++;
        }

        private static void EndDirectSuite(string reason)
        {
            if (!s_DirectSuiteRunning)
            {
                return;
            }

            s_DirectSuiteRunning = false;
            s_DirectSuiteCoroutine = null;
            DialogueSceneEffectsController.OnEffectApplied -= HandleDirectSuiteEffectObserved;

            Debug.Log(
                $"[EffectAutoTest] Direct auto test {reason} | dispatched={s_DirectSuiteDispatchedSteps} failed={s_DirectSuiteFailedSteps} observedEvents={s_DirectSuiteObservedEffects}"
            );
        }

        private static bool TryGetLocalPlayer(out NetworkObject localPlayer)
        {
            localPlayer = null;
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            {
                return false;
            }

            localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            return localPlayer != null;
        }

        private static bool ExecuteDirectNpcPower(
            DialogueSceneEffectsController controller,
            NetworkObject localPlayer,
            NpcDialogueActor actor,
            PrefabPowerEntry power,
            bool logResult
        )
        {
            if (controller == null || localPlayer == null || actor == null || power == null)
            {
                return false;
            }

            if (actor.NetworkObject == null || power.EffectPrefab == null)
            {
                return false;
            }

            Transform npcTransform = actor.transform;
            Transform playerTransform = localPlayer.transform;
            Vector3 targetPosition = playerTransform.position + Vector3.up * 0.9f;
            Vector3 spawnBase = npcTransform.position + power.SpawnOffset;

            Vector3 toTarget = targetPosition - spawnBase;
            Vector3 spawnForward =
                toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : npcTransform.forward;
            Vector3 spawnPosition = spawnBase;
            if (power.SpawnInFrontOfNpc)
            {
                spawnPosition += spawnForward * Mathf.Max(0f, power.ForwardDistance);
            }

            controller.ApplyPrefabPower(
                power.EffectPrefab.name,
                spawnPosition,
                spawnForward,
                Mathf.Max(0.1f, power.Scale),
                Mathf.Max(0.25f, power.DurationSeconds),
                power.ColorOverride,
                power.UseColorOverride,
                power.EnableGameplayDamage,
                power.EnableHoming,
                Mathf.Max(0.1f, power.ProjectileSpeed),
                Mathf.Max(0f, power.HomingTurnRateDegrees),
                Mathf.Max(0f, power.DamageAmount),
                Mathf.Max(0.1f, power.DamageRadius),
                power.AffectPlayerOnly,
                string.IsNullOrWhiteSpace(power.DamageType) ? "effect" : power.DamageType,
                actor.NetworkObjectId,
                localPlayer.NetworkObjectId,
                attachToTarget: false,
                fitToTargetMesh: false,
                serverSpawnTimeSeconds: Time.realtimeSinceStartup,
                effectSeed: (uint)UnityEngine.Random.Range(1, int.MaxValue)
            );

            if (logResult)
            {
                Debug.Log(
                    $"[EffectAutoTest] Direct NPC power spawn (no LLM): npc={actor.ProfileId} power={power.PowerName} prefab={power.EffectPrefab.name} target={localPlayer.NetworkObjectId}"
                );
            }

            return true;
        }

        private static bool TryResolveNpcPowerForDirectSpawn(
            out NpcDialogueActor actor,
            out PrefabPowerEntry power
        )
        {
            actor = null;
            power = null;

#if UNITY_2023_1_OR_NEWER
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif

            if (actors == null || actors.Length == 0)
            {
                return false;
            }

            // Prefer the nearest NPC to the local player so MCP-triggered tests feel like
            // in-world interactions instead of arbitrary spawns.
            Vector3 playerPos = Vector3.zero;
            bool hasPlayer = false;
            if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null)
            {
                playerPos = NetworkManager.Singleton.LocalClient.PlayerObject.transform.position;
                hasPlayer = true;
            }

            List<NpcDialogueActor> orderedActors = actors
                .Where(a => a != null && a.NetworkObject != null)
                .OrderBy(a => hasPlayer ? (a.transform.position - playerPos).sqrMagnitude : 0f)
                .ToList();

            for (int i = 0; i < orderedActors.Count; i++)
            {
                NpcDialogueActor candidate = orderedActors[i];
                NpcDialogueProfile profile = candidate.Profile;
                PrefabPowerEntry[] powers = profile != null ? profile.PrefabPowers : null;
                if (powers == null || powers.Length == 0)
                {
                    continue;
                }

                for (int p = 0; p < powers.Length; p++)
                {
                    PrefabPowerEntry entry = powers[p];
                    if (entry == null || !entry.Enabled || entry.EffectPrefab == null)
                    {
                        continue;
                    }

                    actor = candidate;
                    power = entry;
                    return true;
                }
            }

            return false;
        }
    }
}
