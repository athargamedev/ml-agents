using System;
using System.IO;
using LLMUnity;
using Network_Game.Diagnostics;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Keeps the legacy local LLMUnity runtime path isolated from the remote-first dialogue service.
    /// This is transitional compatibility code for the old in-process LLMAgent/LLM flow only.
    /// </summary>
    internal static class LegacyLocalLlmRuntime
    {
        public static LLMAgent ResolveAgent(
            ref LLMAgent cachedAgent,
            bool useRemoteInference,
            Component owner,
            bool allowSceneSearch
        )
        {
            if (useRemoteInference)
            {
                return cachedAgent;
            }

            if (cachedAgent != null)
            {
                return cachedAgent;
            }

            cachedAgent = owner != null ? owner.GetComponent<LLMAgent>() : null;
            if (cachedAgent != null || !allowSceneSearch)
            {
                return cachedAgent;
            }

#if UNITY_2023_1_OR_NEWER
            cachedAgent = UnityEngine.Object.FindAnyObjectByType<LLMAgent>(
                FindObjectsInactive.Exclude
            );
#else
            cachedAgent = UnityEngine.Object.FindObjectOfType<LLMAgent>();
#endif

            return cachedAgent;
        }

        public static LLMAgent EnsureAgentReady(
            ref LLMAgent agent,
            bool useRemoteInference,
            Component owner,
            bool logDebug,
            string context = "EnsureLlmAgentReady"
        )
        {
            agent = ResolveAgent(ref agent, useRemoteInference, owner, allowSceneSearch: true);
            if (agent == null)
            {
                return null;
            }

            agent = EnsureAgentEnabled(ref agent, useRemoteInference, owner, logDebug, context);

            if (agent.llm == null || !agent.llm.started || agent.llm.failed)
            {
#if UNITY_2023_1_OR_NEWER
                LLM llm = UnityEngine.Object.FindAnyObjectByType<LLM>(FindObjectsInactive.Exclude);
#else
                LLM llm = UnityEngine.Object.FindObjectOfType<LLM>();
#endif
                if (llm != null)
                {
                    agent.llm = llm;
                    NGLog.Info("Dialogue", "LLM assigned to LLMAgent.");
                    return EnsureAgentEnabled(
                        ref agent,
                        useRemoteInference,
                        owner,
                        logDebug,
                        $"{context}.AssignLlm"
                    );
                }

                if (agent.llm != null && !agent.llm.gameObject.scene.IsValid())
                {
                    try
                    {
                        GameObject instance = UnityEngine.Object.Instantiate(agent.llm.gameObject);
                        instance.name = "LLM";
                        LLM instanceLlm = instance.GetComponent<LLM>();
                        if (instanceLlm != null)
                        {
                            agent.llm = instanceLlm;
                            NGLog.Info("Dialogue", "Instantiated LLM prefab for runtime.");
                            return EnsureAgentEnabled(
                                ref agent,
                                useRemoteInference,
                                owner,
                                logDebug,
                                $"{context}.InstantiatePrefab"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        NGLog.Warn(
                            "Dialogue",
                            NGLog.Format(
                                "Failed to instantiate LLM prefab",
                                ("error", ex.Message)
                            )
                        );
                    }
                }
                else if (logDebug)
                {
                    NGLog.Warn(category: "Dialogue", message: "LLM not found in scene.");
                }
            }

            return agent;
        }

        public static LLMAgent EnsureAgentEnabled(
            ref LLMAgent agent,
            bool useRemoteInference,
            Component owner,
            bool logDebug,
            string context
        )
        {
            agent = ResolveAgent(ref agent, useRemoteInference, owner, allowSceneSearch: false);
            if (agent == null || agent.enabled)
            {
                return agent;
            }

            agent.enabled = true;
            if (logDebug)
            {
                string agentName = agent.gameObject != null ? agent.gameObject.name : "unknown";
                NGLog.Info(
                    category: "Dialogue",
                    message: NGLog.Format(
                        message: "Enabled LLMAgent component",
                        ("context", context ?? string.Empty),
                        ("agent", agentName)
                    )
                );
            }

            return agent;
        }

        public static bool TryValidateModelReady(
            ref LLMAgent agent,
            bool useRemoteInference,
            Component owner,
            out string failureReason
        )
        {
            failureReason = string.Empty;

            if (useRemoteInference)
            {
                return true;
            }

            agent = ResolveAgent(ref agent, useRemoteInference, owner, allowSceneSearch: true);
            if (agent == null)
            {
                failureReason = "llm_agent_missing";
                return false;
            }

            if (agent.llm == null)
            {
                failureReason = "llm_component_missing";
                return false;
            }

            string model = agent.llm.model;
            if (string.IsNullOrWhiteSpace(model))
            {
                failureReason = "model_not_set";
                return false;
            }

            string resolved = LLM.GetLLMManagerAssetRuntime(model);
            string resolvedFullPath = LLMUnitySetup.GetFullPath(resolved);
            if (!File.Exists(resolvedFullPath))
            {
                string fileName = Path.GetFileName(model);
                failureReason = string.IsNullOrWhiteSpace(fileName)
                    ? "model_file_not_found"
                    : $"model_file_not_found:{fileName}";

                NGLog.Warn(
                    "Dialogue",
                    NGLog.Format(
                        "LLM model file not found",
                        ("model", model),
                        ("resolved", resolvedFullPath)
                    )
                );
                return false;
            }

            return true;
        }
    }
}
