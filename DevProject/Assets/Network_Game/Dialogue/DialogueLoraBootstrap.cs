using System;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using UnityEngine;
#if !UNITY_WEBGL
using System.IO;
using LLMUnity;
#endif

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Preloads LoRA adapter files and establishes persona-to-LoRA routing before NetworkDialogueService initializes.
    /// Execution order: -500 (before NetworkDialogueService at -450)
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class DialogueLoraBootstrap : MonoBehaviour
    {
        private const string k_LogCategory = "NG:DialogueLoRA";

        [Header("LoRA Configuration")]
        [SerializeField]
        [Tooltip(
            "Directory containing LoRA adapter files (relative to project root or absolute path)"
        )]
        private string m_LoraDirectory = "Assets/LLMUnity/Loras/";

        [Serializable]
        public struct LoraPersonaMapping
        {
            [Tooltip("Unique identifier for the persona (matches NpcDialogueProfile.PersonaId)")]
            public string PersonaId;

            [Tooltip("Filename of the LoRA adapter (e.g., 'storm_oracle.gguf')")]
            public string LoraFileName;

            [Tooltip("Optional display name for logging")]
            public string DisplayName;
        }

        [SerializeField]
        [Tooltip("Mappings between persona IDs and their LoRA adapter files")]
        private LoraPersonaMapping[] m_PersonaMappings = new[]
        {
            new LoraPersonaMapping
            {
                PersonaId = "storm_oracle",
                LoraFileName = "storm_oracle.gguf",
                DisplayName = "Storm Oracle",
            },
            new LoraPersonaMapping
            {
                PersonaId = "forge_keeper",
                LoraFileName = "forge_keeper.gguf",
                DisplayName = "Forge Keeper",
            },
            new LoraPersonaMapping
            {
                PersonaId = "archivist",
                LoraFileName = "archivist.gguf",
                DisplayName = "Archivist",
            },
        };

        [Header("Validation")]
        [SerializeField]
        [Tooltip("If true, logs errors for missing LoRA files. If false, only logs warnings.")]
        private bool m_TreatMissingFilesAsError = false;

        [SerializeField]
        [Tooltip(
            "If true, verifies SHA256 checksums for LoRA files (requires .sha256 sidecar files)"
        )]
        private bool m_VerifyChecksums = true;

        [Header("Runtime State")]
        [SerializeField]
        [Tooltip("Read-only: Loaded LoRA adapters")]
        private List<string> m_LoadedAdapters = new List<string>();

        // Static registry for NetworkDialogueService to query
        private static Dictionary<string, string> s_PersonaToLoraPath =
            new Dictionary<string, string>();

        private void Awake()
        {
#if UNITY_WEBGL
            NGLog.LogWarning(
                k_LogCategory,
                "[DialogueLoraBootstrap] LoRA not supported on WebGL; skipping preload."
            );
#else
            NGLog.Log(
                k_LogCategory,
                $"[DialogueLoraBootstrap] Starting LoRA preload | directory={m_LoraDirectory}"
            );
            PreloadLoraAdapters();
#endif
        }

        /// <summary>
        /// Preloads all LoRA adapters and builds the persona routing map.
        /// </summary>
        private void PreloadLoraAdapters()
        {
#if UNITY_WEBGL
            // Not supported on WebGL — entire method body excluded by compiler
#else
            if (m_PersonaMappings == null || m_PersonaMappings.Length == 0)
            {
                NGLog.LogWarning(
                    k_LogCategory,
                    "[DialogueLoraBootstrap] No persona mappings configured!"
                );
                return;
            }

            s_PersonaToLoraPath.Clear();
            m_LoadedAdapters.Clear();

            int successCount = 0;
            int missingCount = 0;
            int checksumFailCount = 0;

            foreach (var mapping in m_PersonaMappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.PersonaId))
                {
                    NGLog.LogWarning(
                        k_LogCategory,
                        "[DialogueLoraBootstrap] Skipping mapping with empty PersonaId"
                    );
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mapping.LoraFileName))
                {
                    NGLog.LogWarning(
                        k_LogCategory,
                        $"[DialogueLoraBootstrap] Skipping {mapping.PersonaId} - no LoRA filename specified"
                    );
                    continue;
                }

                // Resolve full path
                string loraPath = Path.Combine(m_LoraDirectory, mapping.LoraFileName);
                string displayName = string.IsNullOrWhiteSpace(mapping.DisplayName)
                    ? mapping.PersonaId
                    : mapping.DisplayName;

                // Check if file exists
                if (!File.Exists(loraPath))
                {
                    string message =
                        $"[DialogueLoraBootstrap] LoRA file not found | persona={displayName} | path={loraPath}";
                    if (m_TreatMissingFilesAsError)
                    {
                        NGLog.LogError(k_LogCategory, message);
                    }
                    else
                    {
                        NGLog.LogWarning(k_LogCategory, message);
                    }
                    missingCount++;
                    continue;
                }

                // Verify checksum if enabled
                if (m_VerifyChecksums)
                {
                    string checksumPath = loraPath + ".sha256";
                    if (File.Exists(checksumPath))
                    {
                        bool valid = VerifyChecksum(loraPath, checksumPath);
                        if (!valid)
                        {
                            NGLog.LogError(
                                k_LogCategory,
                                $"[DialogueLoraBootstrap] Checksum verification failed | persona={displayName} | file={mapping.LoraFileName}"
                            );
                            checksumFailCount++;
                            continue;
                        }
                    }
                    else
                    {
                        NGLog.LogWarning(
                            k_LogCategory,
                            $"[DialogueLoraBootstrap] No checksum file found (skipping verification) | file={mapping.LoraFileName}"
                        );
                    }
                }

                // Register mapping
                s_PersonaToLoraPath[mapping.PersonaId] = loraPath;
                m_LoadedAdapters.Add($"{displayName} ({mapping.LoraFileName})");
                successCount++;

                NGLog.Log(
                    k_LogCategory,
                    $"[DialogueLoraBootstrap] Registered LoRA | persona={displayName} | file={mapping.LoraFileName}"
                );
            }

            // Summary log
            string summary =
                $"[DialogueLoraBootstrap] LoRA preload complete | success={successCount} | missing={missingCount} | checksum_fail={checksumFailCount} | total={m_PersonaMappings.Length}";
            if (
                successCount == m_PersonaMappings.Length
                && missingCount == 0
                && checksumFailCount == 0
            )
            {
                NGLog.Log(k_LogCategory, summary + " ✓");
            }
            else
            {
                NGLog.LogWarning(k_LogCategory, summary);
            }
#endif // !UNITY_WEBGL
        }

        /// <summary>
        /// Verifies SHA256 checksum for a LoRA file.
        /// </summary>
        private bool VerifyChecksum(string filePath, string checksumPath)
        {
#if UNITY_WEBGL
            return false;
#else
            try
            {
                // Read expected checksum
                string expectedChecksum = File.ReadAllText(checksumPath)
                    .Trim()
                    .Split(' ')[0]
                    .ToLowerInvariant();

                // Compute actual checksum
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        byte[] hash = sha256.ComputeHash(stream);
                        string actualChecksum = BitConverter
                            .ToString(hash)
                            .Replace("-", "")
                            .ToLowerInvariant();

                        if (expectedChecksum == actualChecksum)
                        {
                            return true;
                        }
                        else
                        {
                            NGLog.LogWarning(
                                k_LogCategory,
                                $"[DialogueLoraBootstrap] Checksum mismatch | expected={expectedChecksum.Substring(0, 16)}... | actual={actualChecksum.Substring(0, 16)}..."
                            );
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NGLog.LogError(
                    k_LogCategory,
                    $"[DialogueLoraBootstrap] Checksum verification exception: {ex.Message}"
                );
                return false;
            }
#endif // !UNITY_WEBGL
        }

        /// <summary>
        /// Gets the LoRA file path for a given persona ID.
        /// Returns null if no mapping exists.
        /// </summary>
        public static string GetLoraPathForPersona(string personaId)
        {
            if (string.IsNullOrWhiteSpace(personaId))
            {
                return null;
            }

            return s_PersonaToLoraPath.TryGetValue(personaId, out string path) ? path : null;
        }

        /// <summary>
        /// Gets all registered persona IDs.
        /// </summary>
        public static IEnumerable<string> GetRegisteredPersonas()
        {
            return s_PersonaToLoraPath.Keys;
        }

        /// <summary>
        /// Checks if a persona ID is registered.
        /// </summary>
        public static bool IsPersonaRegistered(string personaId)
        {
            return !string.IsNullOrWhiteSpace(personaId)
                && s_PersonaToLoraPath.ContainsKey(personaId);
        }

#if UNITY_EDITOR
        [ContextMenu("Validate LoRA Files")]
        private void ValidateLoraFiles()
        {
            NGLog.Log(k_LogCategory, "[DialogueLoraBootstrap] Manual validation triggered");
            PreloadLoraAdapters();
        }

        [ContextMenu("List Registered Personas")]
        private void ListRegisteredPersonas()
        {
            NGLog.Log(
                k_LogCategory,
                $"[DialogueLoraBootstrap] Registered personas: {string.Join(", ", GetRegisteredPersonas())}"
            );
        }
#endif
    }
}
