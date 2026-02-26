using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Network_Game.Dialogue
{
    [DisallowMultipleComponent]
    public class PersonaDialogueSanityRunner : MonoBehaviour
    {
        [Serializable]
        private class PersonaExpectation
        {
            public string ProfileId;

            [TextArea(2, 4)]
            public string Prompt;
            public string[] ExpectedKeywords;
        }

        [Serializable]
        private class BaselineScenarioCase
        {
            public string CaseId;
            public string Prompt;
            public string[] ExpectedKeywords;
        }

        [Serializable]
        private class BaselinePersonaScenario
        {
            public string ProfileId;
            public BaselineScenarioCase[] Cases;
        }

        [Serializable]
        private class CaseResult
        {
            public string ProfileId;
            public string CaseId;
            public string Prompt;
            public bool Passed;
            public float LatencySeconds;
            public bool TimedOut;
            public bool EmptyResponse;
            public bool WrongSpeakerRouting;
            public string Status;
            public string Error;
            public string ResponseText;
            public string[] ExpectedKeywords;
            public ulong ExpectedSpeakerNetworkId;
            public ulong ActualSpeakerNetworkId;
            public int ClientRequestId;
            public string ConversationKey;
        }

        [Serializable]
        private class SanityReportData
        {
            public string StartedAtUtc;
            public string FinishedAtUtc;
            public bool Passed;
            public int TotalCases;
            public int PassedCases;
            public int FailedCases;
            public float TimeoutSeconds;
            public List<CaseResult> Cases = new();
        }

        [Header("Execution")]
        [SerializeField]
        private bool m_RunOnStart;

        [SerializeField]
        [Min(0f)]
        private float m_StartDelaySeconds = 1.5f;

        [SerializeField]
        [Min(1f)]
        private float m_RequestTimeoutSeconds = 30f;

        [SerializeField]
        [Min(0f)]
        private float m_DelayBetweenRequestsSeconds = 0.35f;

        [SerializeField]
        private bool m_LogResponseText = true;

        [SerializeField]
        private bool m_WriteReportToPersistentDataPath = true;

#if UNITY_EDITOR
        [SerializeField]
        private bool m_WriteReportTextAssetInProject = true;

        [SerializeField]
        private string m_ProjectReportFolder = "Assets/Network_Game/Dialogue/Reports";
#endif

        [Header("Validation")]
        [SerializeField]
        private bool m_RequireKeywordMatch = true;

        [SerializeField]
        private PersonaExpectation[] m_Expectations;

        [Header("Last Result")]
        [SerializeField]
        private bool m_LastRunPassed;

        [SerializeField]
        [TextArea(8, 20)]
        private string m_LastReport = "Not executed.";

        [SerializeField]
        private string m_LastJsonReportPath = string.Empty;

        private readonly Dictionary<
            int,
            NetworkDialogueService.DialogueResponse
        > m_ResponsesByClientRequest = new();
        private static readonly BaselinePersonaScenario[] BaselineScenarios =
        {
            new BaselinePersonaScenario
            {
                ProfileId = "npc.storm_oracle",
                Cases = new[]
                {
                    new BaselineScenarioCase
                    {
                        CaseId = "storm_tactical_tip",
                        Prompt = "Give one short tactical tip using storm or wind language.",
                        ExpectedKeywords = new[] { "storm", "wind", "timing", "pressure" },
                    },
                    new BaselineScenarioCase
                    {
                        CaseId = "storm_positioning",
                        Prompt = "One short combat positioning hint with weather imagery.",
                        ExpectedKeywords = new[] { "gust", "storm", "angle", "timing" },
                    },
                },
            },
            new BaselinePersonaScenario
            {
                ProfileId = "npc.forge_keeper",
                Cases = new[]
                {
                    new BaselineScenarioCase
                    {
                        CaseId = "forge_crafting_tip",
                        Prompt = "Give one short practical crafting recommendation for survival.",
                        ExpectedKeywords = new[] { "craft", "forge", "upgrade", "repair" },
                    },
                    new BaselineScenarioCase
                    {
                        CaseId = "forge_materials",
                        Prompt = "One short gear maintenance tip focused on durability.",
                        ExpectedKeywords = new[] { "temper", "repair", "steel", "durability" },
                    },
                },
            },
            new BaselinePersonaScenario
            {
                ProfileId = "npc.archivist",
                Cases = new[]
                {
                    new BaselineScenarioCase
                    {
                        CaseId = "archivist_lore_summary",
                        Prompt = "Give one short factual lore summary with context.",
                        ExpectedKeywords = new[]
                        {
                            "record",
                            "archive",
                            "history",
                            "context",
                            "fact",
                        },
                    },
                    new BaselineScenarioCase
                    {
                        CaseId = "archivist_briefing",
                        Prompt = "One short historical briefing about the region.",
                        ExpectedKeywords = new[] { "history", "chronicle", "record", "era" },
                    },
                },
            },
        };

        private int m_RequestCounter;
        private bool m_IsRunning;

        private void OnEnable()
        {
            NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
        }

        private void OnDisable()
        {
            NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
        }

        private void Reset()
        {
            EnsureDefaultExpectations();
        }

        private void Start()
        {
            EnsureDefaultExpectations();
            if (m_RunOnStart)
            {
                StartCoroutine(RunAfterDelay());
            }
        }

        [ContextMenu("Run Persona Sanity Check")]
        public void RunPersonaSanityCheck()
        {
            if (m_IsRunning)
            {
                NGLog.Warn("DialogueSanity", "Sanity check already running.");
                return;
            }

            StartCoroutine(RunRoutine());
        }

        private IEnumerator RunAfterDelay()
        {
            if (m_StartDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(m_StartDelaySeconds);
            }

            RunPersonaSanityCheck();
        }

        private IEnumerator RunRoutine()
        {
            m_IsRunning = true;
            m_ResponsesByClientRequest.Clear();
            m_LastRunPassed = false;
            m_LastReport = "Running...";

            var report = new StringBuilder();
            DateTime startedUtc = DateTime.UtcNow;
            var reportData = new SanityReportData
            {
                StartedAtUtc = startedUtc.ToString("O"),
                TimeoutSeconds = m_RequestTimeoutSeconds,
            };
            report.AppendLine("Persona Dialogue Sanity Check");
            report.AppendLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            var service = NetworkDialogueService.Instance;
            if (service == null)
            {
                report.AppendLine("FAIL: NetworkDialogueService not found.");
                CompleteRun(false, report);
                yield break;
            }

            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
            {
                report.AppendLine("FAIL: NetworkManager is not listening.");
                CompleteRun(false, report);
                yield break;
            }

            NetworkObject localPlayer = manager.LocalClient?.PlayerObject;
            if (localPlayer == null)
            {
                report.AppendLine("FAIL: Local player object not available.");
                CompleteRun(false, report);
                yield break;
            }

            NpcDialogueActor[] actors = FindActors();

            if (actors.Length == 0)
            {
                report.AppendLine("FAIL: No NpcDialogueActor components found in scene.");
                CompleteRun(false, report);
                yield break;
            }

            bool allPassed = true;
            report.AppendLine($"NPC Actors Found: {actors.Length}");

            for (int personaIndex = 0; personaIndex < actors.Length; personaIndex++)
            {
                NpcDialogueActor actor = actors[personaIndex];
                if (actor == null)
                {
                    continue;
                }

                string profileId = actor.ProfileId;
                string personaName = actor.name;
                NetworkObject speaker = actor.GetComponent<NetworkObject>();

                if (speaker == null || !speaker.IsSpawned)
                {
                    report.AppendLine($"FAIL: {personaName} is missing spawned NetworkObject.");
                    allPassed = false;
                    continue;
                }

                BaselineScenarioCase[] baselineCases = ResolveBaselineCases(profileId);
                if (baselineCases == null || baselineCases.Length == 0)
                {
                    baselineCases = new[]
                    {
                        new BaselineScenarioCase
                        {
                            CaseId = "legacy_single_case",
                            Prompt = BuildPromptFromLegacyExpectation(profileId),
                            ExpectedKeywords = ResolveExpectation(profileId)?.ExpectedKeywords,
                        },
                    };
                }

                for (int caseIndex = 0; caseIndex < baselineCases.Length; caseIndex++)
                {
                    BaselineScenarioCase baselineCase = baselineCases[caseIndex];
                    CaseResult caseResult = new CaseResult
                    {
                        ProfileId = profileId,
                        CaseId = string.IsNullOrWhiteSpace(baselineCase.CaseId)
                            ? $"{profileId}_case_{caseIndex + 1}"
                            : baselineCase.CaseId,
                        Prompt = string.IsNullOrWhiteSpace(baselineCase.Prompt)
                            ? BuildFallbackPrompt(profileId)
                            : baselineCase.Prompt.Trim(),
                        ExpectedKeywords = baselineCase.ExpectedKeywords,
                        ExpectedSpeakerNetworkId = speaker.NetworkObjectId,
                    };

                    yield return ExecuteCase(service, manager, localPlayer, report, caseResult);

                    if (!caseResult.Passed)
                    {
                        allPassed = false;
                    }

                    reportData.Cases.Add(caseResult);
                    if (m_DelayBetweenRequestsSeconds > 0f)
                    {
                        yield return new WaitForSeconds(m_DelayBetweenRequestsSeconds);
                    }
                }
            }

            reportData.FinishedAtUtc = DateTime.UtcNow.ToString("O");
            reportData.TotalCases = reportData.Cases.Count;
            for (int i = 0; i < reportData.Cases.Count; i++)
            {
                if (reportData.Cases[i].Passed)
                {
                    reportData.PassedCases++;
                }
            }
            reportData.FailedCases = reportData.TotalCases - reportData.PassedCases;
            reportData.Passed = allPassed;

            WriteReportArtifacts(reportData, report);

            CompleteRun(allPassed, report);
        }

        private IEnumerator ExecuteCase(
            NetworkDialogueService service,
            NetworkManager manager,
            NetworkObject localPlayer,
            StringBuilder report,
            CaseResult caseResult
        )
        {
            int clientRequestId = ++m_RequestCounter;
            string conversationKey =
                $"client:{manager.LocalClientId}:sanity:{caseResult.ProfileId}:{caseResult.CaseId}:{clientRequestId}";
            caseResult.ClientRequestId = clientRequestId;
            caseResult.ConversationKey = conversationKey;

            var request = new NetworkDialogueService.DialogueRequest
            {
                Prompt = caseResult.Prompt,
                ConversationKey = conversationKey,
                SpeakerNetworkId = caseResult.ExpectedSpeakerNetworkId,
                ListenerNetworkId = localPlayer.NetworkObjectId,
                RequestingClientId = manager.LocalClientId,
                Broadcast = false,
                BroadcastDuration = 0f,
                NotifyClient = true,
                ClientRequestId = clientRequestId,
                IsUserInitiated = true,
                BlockRepeatedPrompt = false,
                MinRepeatDelaySeconds = 0f,
                RequireUserReply = false,
            };

            service.RequestDialogue(request);
            NGLog.Info(
                "DialogueSanity",
                NGLog.Format(
                    "Request sent",
                    ("profile", caseResult.ProfileId),
                    ("case", caseResult.CaseId),
                    ("clientRequest", clientRequestId)
                )
            );

            float elapsed = 0f;
            bool received = false;
            NetworkDialogueService.DialogueResponse response = default;
            while (elapsed < m_RequestTimeoutSeconds)
            {
                if (m_ResponsesByClientRequest.TryGetValue(clientRequestId, out response))
                {
                    m_ResponsesByClientRequest.Remove(clientRequestId);
                    received = true;
                    break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            caseResult.LatencySeconds = elapsed;
            if (!received)
            {
                caseResult.Passed = false;
                caseResult.TimedOut = true;
                caseResult.Status = "TimedOut";
                caseResult.Error = $"Timed out after {m_RequestTimeoutSeconds:0.#}s";
                report.AppendLine(
                    $"FAIL: {caseResult.ProfileId}/{caseResult.CaseId} timed out after {m_RequestTimeoutSeconds:0.#}s."
                );
                yield break;
            }

            caseResult.Status = response.Status.ToString();
            caseResult.Error = response.Error;
            caseResult.ActualSpeakerNetworkId = response.Request.SpeakerNetworkId;
            caseResult.WrongSpeakerRouting =
                response.Request.SpeakerNetworkId != caseResult.ExpectedSpeakerNetworkId;

            if (response.Status != NetworkDialogueService.DialogueStatus.Completed)
            {
                caseResult.Passed = false;
                report.AppendLine(
                    $"FAIL: {caseResult.ProfileId}/{caseResult.CaseId} status={response.Status} error={response.Error}"
                );
                yield break;
            }

            string responseText = response.ResponseText ?? string.Empty;
            caseResult.ResponseText = responseText;
            caseResult.EmptyResponse = string.IsNullOrWhiteSpace(responseText);
            if (m_LogResponseText)
            {
                NGLog.Info(
                    "DialogueSanity",
                    NGLog.Format(
                        "Response",
                        ("profile", caseResult.ProfileId),
                        ("case", caseResult.CaseId),
                        ("text", responseText)
                    )
                );
            }

            bool keywordMatch = true;
            if (
                m_RequireKeywordMatch
                && caseResult.ExpectedKeywords != null
                && caseResult.ExpectedKeywords.Length > 0
            )
            {
                keywordMatch = ContainsAnyKeyword(responseText, caseResult.ExpectedKeywords);
            }

            caseResult.Passed =
                !caseResult.EmptyResponse && !caseResult.WrongSpeakerRouting && keywordMatch;

            if (!caseResult.Passed)
            {
                report.AppendLine(
                    $"FAIL: {caseResult.ProfileId}/{caseResult.CaseId} "
                    + BuildFailureReason(caseResult, keywordMatch)
                );
                yield break;
            }

            report.AppendLine(
                $"PASS: {caseResult.ProfileId}/{caseResult.CaseId} ({caseResult.LatencySeconds:0.00}s)."
            );
        }

        private string BuildFailureReason(CaseResult caseResult, bool keywordMatch)
        {
            var reasons = new List<string>();
            if (caseResult.EmptyResponse)
            {
                reasons.Add("empty_response");
            }

            if (caseResult.WrongSpeakerRouting)
            {
                reasons.Add(
                    $"wrong_speaker expected={caseResult.ExpectedSpeakerNetworkId} actual={caseResult.ActualSpeakerNetworkId}"
                );
            }

            if (!keywordMatch)
            {
                reasons.Add("missing_expected_keyword");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("unknown_failure");
            }

            return string.Join(", ", reasons) + $". Response: {caseResult.ResponseText}";
        }

        private void WriteReportArtifacts(SanityReportData data, StringBuilder report)
        {
            string fileStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string json = JsonUtility.ToJson(data, true);
            m_LastJsonReportPath = string.Empty;

#if !UNITY_WEBGL
            if (m_WriteReportToPersistentDataPath)
            {
                string persistentDirectory = Path.Combine(
                    Application.persistentDataPath,
                    "dialogue_sanity_reports"
                );
                Directory.CreateDirectory(persistentDirectory);
                string persistentPath = Path.Combine(
                    persistentDirectory,
                    $"persona_sanity_{fileStamp}.json"
                );
                File.WriteAllText(persistentPath, json);
                m_LastJsonReportPath = persistentPath;
                report.AppendLine($"Report JSON: {persistentPath}");
            }
#endif // !UNITY_WEBGL

#if UNITY_EDITOR
            if (m_WriteReportTextAssetInProject)
            {
                string projectFolder = string.IsNullOrWhiteSpace(m_ProjectReportFolder)
                    ? "Assets/Network_Game/Dialogue/Reports"
                    : m_ProjectReportFolder.Trim();
                Directory.CreateDirectory(projectFolder);

                string jsonAssetPath = Path.Combine(
                    projectFolder,
                    $"persona_sanity_{fileStamp}.json"
                );
                string textAssetPath = Path.Combine(
                    projectFolder,
                    $"persona_sanity_{fileStamp}.txt"
                );
                File.WriteAllText(jsonAssetPath, json);
                File.WriteAllText(textAssetPath, report.ToString());
                AssetDatabase.Refresh();
                report.AppendLine($"Report Assets: {jsonAssetPath}, {textAssetPath}");

                if (string.IsNullOrEmpty(m_LastJsonReportPath))
                {
                    m_LastJsonReportPath = jsonAssetPath;
                }
            }
#endif
        }

        private void CompleteRun(bool passed, StringBuilder report)
        {
            m_LastRunPassed = passed;
            report.AppendLine($"Result: {(passed ? "PASS" : "FAIL")}");
            m_LastReport = report.ToString();

            if (passed)
            {
                NGLog.Info("DialogueSanity", m_LastReport, this);
            }
            else
            {
                NGLog.Warn("DialogueSanity", m_LastReport, this);
            }

            m_IsRunning = false;
        }

        private NpcDialogueActor[] FindActors()
        {
#if UNITY_2023_1_OR_NEWER
            NpcDialogueActor[] actors = FindObjectsByType<NpcDialogueActor>(FindObjectsInactive.Exclude);
#else
            NpcDialogueActor[] actors = FindObjectsOfType<NpcDialogueActor>();
#endif

            Array.Sort(
                actors,
                (left, right) => string.CompareOrdinal(left.gameObject.name, right.gameObject.name)
            );
            return actors;
        }

        private PersonaExpectation ResolveExpectation(string profileId)
        {
            if (
                m_Expectations == null
                || m_Expectations.Length == 0
                || string.IsNullOrWhiteSpace(profileId)
            )
            {
                return null;
            }

            string target = profileId.Trim();
            for (int i = 0; i < m_Expectations.Length; i++)
            {
                PersonaExpectation expectation = m_Expectations[i];
                if (expectation == null || string.IsNullOrWhiteSpace(expectation.ProfileId))
                {
                    continue;
                }

                if (
                    string.Equals(
                        expectation.ProfileId.Trim(),
                        target,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return expectation;
                }
            }

            return null;
        }

        private BaselineScenarioCase[] ResolveBaselineCases(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            string target = profileId.Trim();
            for (int i = 0; i < BaselineScenarios.Length; i++)
            {
                BaselinePersonaScenario scenario = BaselineScenarios[i];
                if (scenario == null || string.IsNullOrWhiteSpace(scenario.ProfileId))
                {
                    continue;
                }

                if (
                    string.Equals(
                        scenario.ProfileId.Trim(),
                        target,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return scenario.Cases;
                }
            }

            return null;
        }

        private string BuildPromptFromLegacyExpectation(string profileId)
        {
            PersonaExpectation expectation = ResolveExpectation(profileId);
            if (expectation != null && !string.IsNullOrWhiteSpace(expectation.Prompt))
            {
                return expectation.Prompt.Trim();
            }

            return BuildFallbackPrompt(profileId);
        }

        private bool ContainsAnyKeyword(string text, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            string lower = text.ToLowerInvariant();
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (lower.Contains(keyword.Trim().ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildFallbackPrompt(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return "Give one short in-character gameplay tip.";
            }

            string id = profileId.Trim().ToLowerInvariant();
            if (id.Contains("storm"))
            {
                return "Give one short tactical tip using wind or timing language.";
            }

            if (id.Contains("forge"))
            {
                return "Give one short crafting and upgrade recommendation.";
            }

            if (id.Contains("archiv"))
            {
                return "Give one short factual lore briefing with context.";
            }

            return "Give one short in-character gameplay tip.";
        }

        private void EnsureDefaultExpectations()
        {
            if (m_Expectations != null && m_Expectations.Length > 0)
            {
                return;
            }

            m_Expectations = new[]
            {
                new PersonaExpectation
                {
                    ProfileId = "npc.storm_oracle",
                    Prompt = "Give one short tactical tip that uses storm or wind language.",
                    ExpectedKeywords = new[] { "storm", "wind", "timing", "pressure" },
                },
                new PersonaExpectation
                {
                    ProfileId = "npc.forge_keeper",
                    Prompt = "Give one short practical crafting recommendation for survival.",
                    ExpectedKeywords = new[] { "craft", "forge", "upgrade", "repair" },
                },
                new PersonaExpectation
                {
                    ProfileId = "npc.archivist",
                    Prompt = "Give one short factual lore summary with context.",
                    ExpectedKeywords = new[] { "record", "archive", "history", "context", "fact" },
                },
            };
        }

        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            int clientRequestId = response.Request.ClientRequestId;
            if (clientRequestId <= 0)
            {
                return;
            }

            m_ResponsesByClientRequest[clientRequestId] = response;
        }
    }
}
