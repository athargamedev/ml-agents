using System;
using System.Collections.Generic;
using System.Text;
using Network_Game.Diagnostics;
using Network_Game.Dialogue.Effects;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Consolidated per-NPC dialogue component.
    /// - Persona (system prompt + profile selection)
    /// - Networked in-world speech bubble text (replaces TalkNetworkSync usage)
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NpcDialogueActor : NetworkBehaviour
    {
        [Header("Persona")]
        [SerializeField]
        private NpcDialogueProfile m_Profile;

        [SerializeField]
        [Tooltip("Optional runtime id override when multiple NPCs share one profile asset.")]
        private string m_ProfileIdOverride = "";

        [Header("Speech Bubble")]
        [SerializeField]
        [Tooltip(
            "Optional in-scene TextMeshPro reference. If missing, one is auto-created as a child."
         )]
        private TextMeshPro m_SpeechText;

        [SerializeField]
        private float m_TextOffsetPadding = 0.05f;

        [SerializeField]
        private float m_DefaultTextHeight = 1.5f;

        [SerializeField]
        [Tooltip("Rotate the spawned speech bubble to face the main camera.")]
        private bool m_FaceMainCamera = true;

        [SerializeField]
        [Tooltip("Auto-create a child TextMeshPro when no reference is assigned.")]
        private bool m_AutoCreateSpeechText = true;

        [SerializeField]
        [Min(0.5f)]
        private float m_SpeechFontSize = 2f;

        [SerializeField]
        private Color m_SpeechTextColor = Color.white;

        [SerializeField]
        private Vector3 m_SpeechLocalScale = new Vector3(0.08f, 0.08f, 0.08f);

        private float m_RemainingDuration;
        private bool m_IsShowingText;
        private Camera m_CachedCamera;

        private static string s_CachedSceneContext;
        private static float s_SceneContextCacheTime = -1f;
        private const float SceneContextCacheDuration = 5f;

        public NpcDialogueProfile Profile => m_Profile;

        public string ProfileId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(m_ProfileIdOverride))
                {
                    return m_ProfileIdOverride.Trim();
                }

                if (m_Profile != null && !string.IsNullOrWhiteSpace(m_Profile.ProfileId))
                {
                    return m_Profile.ProfileId.Trim();
                }

                return gameObject.name;
            }
        }

        private void Awake()
        {
            EnsureSpeechTextReference();
        }

        private void Start()
        {
            RegisterProfilePowersIntoCatalog();
        }

        /// <summary>
        /// Bridges profile PrefabPowerEntry items into the EffectCatalog so the EffectParser
        /// can resolve tags like [EFFECT: BigExplosion] that are defined on the profile but
        /// have no standalone EffectDefinition asset.
        /// </summary>
        private void RegisterProfilePowersIntoCatalog()
        {
            if (m_Profile == null || m_Profile.PrefabPowers == null || m_Profile.PrefabPowers.Length == 0)
                return;

            var catalog = EffectCatalog.Load();
            if (catalog == null)
                return;

            int registered = 0;
            foreach (var power in m_Profile.PrefabPowers)
            {
                if (power == null || !power.Enabled || string.IsNullOrWhiteSpace(power.PowerName))
                    continue;

                if (catalog.TryGet(power.PowerName, out _))
                    continue;

                var def = ScriptableObject.CreateInstance<EffectDefinition>();
                def.effectTag = power.PowerName;
                def.effectPrefab = power.EffectPrefab;
                def.description = $"Profile power: {power.PowerName}";
                catalog.RegisterRuntimeEffect(def);
                registered++;
            }

            if (registered > 0)
                NGLog.Info(
                    "DialogueFX",
                    $"[NpcDialogueActor] Registered {registered} profile power(s) into catalog | npc={gameObject.name}"
                );
        }

        private void LateUpdate()
        {
            if (!m_IsShowingText)
            {
                return;
            }

            if (m_SpeechText != null && m_SpeechText.gameObject.activeSelf && m_FaceMainCamera)
            {
                m_SpeechText.transform.rotation = GetTextLookRotation();
            }

            if (m_RemainingDuration > 0f)
            {
                m_RemainingDuration -= Time.deltaTime;
                if (m_RemainingDuration <= 0f)
                {
                    ClearText();
                }
            }
        }

        public string BuildSystemPrompt(
            string basePrompt,
            string userPrompt,
            GameObject listenerObject
        )
        {
            if (
                m_Profile == null
                || (
                    string.IsNullOrWhiteSpace(m_Profile.SystemPrompt)
                    && string.IsNullOrWhiteSpace(m_Profile.Lore)
                )
            )
            {
                return basePrompt ?? string.Empty;
            }

            string listenerName = listenerObject != null ? listenerObject.name : "Player";
            // NOTE: {player_input} was intentionally removed here.
            // The user's message is already sent separately as the userPrompt parameter
            // in the LLM chat request. Injecting it into the system prompt duplicated
            // tokens and created a prompt injection surface.
            string personaPrompt = m_Profile
                .SystemPrompt.Replace("{npc_name}", gameObject.name ?? "NPC")
                .Replace("{listener_name}", listenerName);

            if (!string.IsNullOrWhiteSpace(m_Profile.Lore))
            {
                string lore = m_Profile
                    .Lore.Replace("{npc_name}", gameObject.name ?? "NPC")
                    .Replace("{listener_name}", listenerName);
                personaPrompt = $"{personaPrompt}\n\n[Lore]\n{lore}";
            }

            string composedPrompt = string.IsNullOrWhiteSpace(basePrompt)
                ? personaPrompt
                : $"{basePrompt}\n\n[Persona:{ProfileId}]\n{personaPrompt}";
            string effectGuide = BuildEffectControlGuide(listenerName);
            if (!string.IsNullOrWhiteSpace(effectGuide))
            {
                composedPrompt = $"{composedPrompt}\n\n{effectGuide}";
            }

            return composedPrompt;
        }

        public bool TryGetBoredLighting(
            string responseText,
            out Color color,
            out float intensity,
            out float transitionSeconds
        )
        {
            color = Color.blue;
            intensity = 1f;
            transitionSeconds = 0.35f;

            if (
                m_Profile == null
                || !m_Profile.EnableBoredLightEffect
                || string.IsNullOrWhiteSpace(responseText)
            )
            {
                return false;
            }

            string lower = responseText.ToLowerInvariant();
            string[] keywords = m_Profile.BoredKeywords;
            if (keywords == null || keywords.Length == 0)
            {
                return false;
            }

            bool matched = false;
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (lower.Contains(keyword.Trim().ToLowerInvariant()))
                {
                    matched = true;
                    break;
                }
            }

            // Fallback intent: allow direct "make lights blue" style requests even
            // when the model answer does not echo a boredom keyword verbatim.
            if (!matched && ContainsBlueLightIntent(lower))
            {
                matched = true;
            }

            if (!matched)
            {
                return false;
            }

            color = m_Profile.BoredLightColor;
            intensity = Mathf.Max(0f, m_Profile.BoredLightIntensity);
            transitionSeconds = Mathf.Max(0f, m_Profile.LightTransitionSeconds);
            return true;
        }

        private static bool ContainsBlueLightIntent(string lowerText)
        {
            if (string.IsNullOrWhiteSpace(lowerText))
            {
                return false;
            }

            bool hasBlue = lowerText.Contains("blue");
            bool hasLight = lowerText.Contains("light");
            if (!hasBlue || !hasLight)
            {
                return false;
            }

            return true;
        }

        private string BuildEffectControlGuide(string listenerName)
        {
            if (m_Profile == null)
            {
                return string.Empty;
            }

            string guide = m_Profile.BuildCompressedEffectGuide(listenerName, 5);
            string sceneInfo = BuildSceneContextInfo();

            var sb = new StringBuilder(600);
            if (!string.IsNullOrWhiteSpace(guide))
            {
                sb.AppendLine(guide.Trim());
            }

            if (!string.IsNullOrWhiteSpace(sceneInfo))
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine(sceneInfo.Trim());
            }

            TryAppendCatalogPowers(sb);

            string animationGuide = BuildAnimationControlGuide();
            if (!string.IsNullOrWhiteSpace(animationGuide))
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine(animationGuide.Trim());
            }

            return sb.ToString().Trim();
        }

        private string BuildAnimationControlGuide()
        {
            if (
                GetComponent<NpcDialogueAnimationController>() == null
                && GetComponent<NpcDialogueAnimationAutoResponder>() == null
            )
            {
                return string.Empty;
            }

            var sb = new StringBuilder(256);
            sb.AppendLine("[Animations] For body language on yourself, use an animation tag instead of an effect.");
            sb.AppendLine("Decision rule: if the user asks you to animate, gesture, pose, turn, react, or play a clip on yourself, use [ANIM:] and do NOT use [EFFECT:].");
            sb.AppendLine("Use [EFFECT:] only for powers, particles, projectiles, explosions, or world/player-facing visuals.");
            sb.AppendLine("Use at most ONE structured tag at the end: either [EFFECT: ...] OR [ANIM: ...].");
            sb.AppendLine("Do not use [ANIM:] for the player or world. [ANIM:] is for your own model only.");
            sb.AppendLine("Format: [ANIM: EmphasisReact | Target: Self]");
            sb.AppendLine("Examples:");
            sb.AppendLine("  [ANIM: EmphasisReact | Target: Self] for a heavy blow, hit reaction, or forceful emphasis.");
            sb.AppendLine("  [ANIM: IdleVariant | Target: Self] for a calm flourish or friendly idle shift.");
            sb.AppendLine("  [ANIM: TurnLeft | Target: Self] or [ANIM: TurnRight | Target: Self] for curious/questioning looks.");
            return sb.ToString().Trim();
        }

        private static string BuildKeywordPreview(string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
            {
                return string.Empty;
            }

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var picked = new List<string>(4);
            for (int i = 0; i < keywords.Length && picked.Count < 4; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                string normalized = keyword.Trim();
                if (!unique.Add(normalized))
                {
                    continue;
                }

                picked.Add(normalized);
            }

            return picked.Count == 0 ? string.Empty : string.Join(", ", picked);
        }

        private static string BuildSceneContextInfo()
        {
            if (
                s_CachedSceneContext != null
                && Time.realtimeSinceStartup - s_SceneContextCacheTime < SceneContextCacheDuration
            )
            {
                return s_CachedSceneContext;
            }

            string result = BuildSceneContextInfoUncached();
            s_CachedSceneContext = result;
            s_SceneContextCacheTime = Time.realtimeSinceStartup;
            return result;
        }

        private static string BuildSceneContextInfoUncached()
        {
            var sb = new StringBuilder(512);

            AppendSemanticSceneRoles(sb);

            // Find ground plane/terrain dimensions
            GameObject ground = GameObject.Find("Ground");
            if (ground == null)
                ground = GameObject.Find("Plane");
            if (ground == null)
                ground = GameObject.Find("Floor");
            if (ground == null)
                ground = GameObject.Find("Arena_Floor");
            if (ground == null)
                ground = GameObject.Find("Terrain");

            if (ground != null)
            {
                Vector3 scale = ground.transform.localScale;
                Vector3 pos = ground.transform.position;
                Renderer rend = ground.GetComponent<Renderer>();
                if (rend != null)
                {
                    Vector3 size = rend.bounds.size;
                    sb.AppendLine(
                        $"Ground: \"{ground.name}\" size={size.x:F1}x{size.z:F1}m at y={pos.y:F1}."
                    );
                    sb.AppendLine(
                        $"To cover the entire ground, use radius {Mathf.Max(size.x, size.z) * 0.5f:F0} or scale {Mathf.Max(size.x, size.z) / 10f:F1}x."
                    );
                }
                else
                {
                    // Unity default plane is 10x10 at scale 1
                    float estimatedSize = Mathf.Max(scale.x, scale.z) * 10f;
                    sb.AppendLine(
                        $"Ground: \"{ground.name}\" estimated size ~{estimatedSize:F0}x{estimatedSize:F0}m at y={pos.y:F1}."
                    );
                    sb.AppendLine(
                        $"To cover the entire ground, use radius {estimatedSize * 0.5f:F0} or scale {estimatedSize / 10f:F1}x."
                    );
                }
            }

            // Find key scene objects the LLM can target
            var interestingObjects = new List<string>(8);
            GameObject[] rootObjects = UnityEngine
                .SceneManagement.SceneManager.GetActiveScene()
                .GetRootGameObjects();
            if (rootObjects != null)
            {
                for (int i = 0; i < rootObjects.Length && interestingObjects.Count < 12; i++)
                {
                    GameObject obj = rootObjects[i];
                    if (obj == null || !obj.activeInHierarchy)
                    {
                        continue;
                    }

                    string objName = obj.name;
                    // Skip system objects
                    if (
                        objName.StartsWith("Directional", StringComparison.OrdinalIgnoreCase)
                        || objName.StartsWith("EventSystem", StringComparison.OrdinalIgnoreCase)
                        || objName.StartsWith("Canvas", StringComparison.OrdinalIgnoreCase)
                        || objName.StartsWith("Dialogue", StringComparison.OrdinalIgnoreCase)
                        || objName.StartsWith("Network", StringComparison.OrdinalIgnoreCase)
                        || objName.StartsWith("---", StringComparison.Ordinal)
                        || objName.StartsWith("_", StringComparison.Ordinal)
                    )
                    {
                        continue;
                    }

                    Renderer r = obj.GetComponentInChildren<Renderer>();
                    if (r != null)
                    {
                        Vector3 s = r.bounds.size;
                        Vector3 p = obj.transform.position;
                        interestingObjects.Add(
                            $"\"{objName}\" ({s.x:F1}x{s.y:F1}x{s.z:F1}m at [{p.x:F1},{p.y:F1},{p.z:F1}])"
                        );
                    }
                    else
                    {
                        Vector3 p = obj.transform.position;
                        interestingObjects.Add($"\"{objName}\" (at [{p.x:F1},{p.y:F1},{p.z:F1}])");
                    }
                }
            }

            if (interestingObjects.Count > 0)
            {
                sb.Append("Scene objects: ");
                sb.AppendLine(string.Join(", ", interestingObjects) + ".");
            }

            return sb.ToString().TrimEnd();
        }

        private static void AppendSemanticSceneRoles(StringBuilder sb)
        {
            if (sb == null)
            {
                return;
            }

            DialogueSemanticTag[] semanticTags = FindSemanticTags();
            if (semanticTags == null || semanticTags.Length == 0)
            {
                return;
            }

            var entries = new List<string>(12);
            for (int i = 0; i < semanticTags.Length && entries.Count < 12; i++)
            {
                DialogueSemanticTag semantic = semanticTags[i];
                if (semantic == null || !semantic.IncludeInSceneSnapshots)
                {
                    continue;
                }

                string name = semantic.ResolveDisplayName(semantic.gameObject);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string role = semantic.RoleKey;
                string[] aliases = semantic.GetCompactAliases(2);
                string aliasPart =
                    aliases.Length > 0 ? $" aliases={string.Join("/", aliases)}" : string.Empty;
                string desc = semantic.Description;
                string descPart = !string.IsNullOrEmpty(desc) ? $" — {desc}" : string.Empty;
                entries.Add($"\"{name}\" role={role}{aliasPart}{descPart}");
            }

            if (entries.Count == 0)
            {
                return;
            }

            sb.Append("Semantic roles: ");
            sb.AppendLine(string.Join(", ", entries) + ".");
        }

        private static DialogueSemanticTag[] FindSemanticTags()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<DialogueSemanticTag>(
                FindObjectsInactive.Exclude
            );
#else
            return UnityEngine.Object.FindObjectsOfType<DialogueSemanticTag>();
#endif
        }

        public void ShowSpeechText(string text, float durationSeconds)
        {
            if (IsSpawned && IsServer)
            {
                ShowSpeechTextClientRpc(text ?? string.Empty, durationSeconds);
                return;
            }

            DisplayText(text, durationSeconds);
        }

        public void HideSpeechText()
        {
            if (IsSpawned && IsServer)
            {
                HideSpeechTextClientRpc();
                return;
            }

            ClearText();
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ShowSpeechTextClientRpc(string text, float durationSeconds)
        {
            DisplayText(text, durationSeconds);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void HideSpeechTextClientRpc()
        {
            ClearText();
        }

        private void DisplayText(string text, float durationSeconds)
        {
            EnsureSpeechTextReference();
            if (m_SpeechText == null)
            {
                return;
            }

            Vector3 offset = CalculateOffset();
            m_SpeechText.transform.localPosition = offset;
            m_SpeechText.text = text ?? string.Empty;
            m_SpeechText.gameObject.SetActive(true);

            if (m_FaceMainCamera)
            {
                m_SpeechText.transform.rotation = GetTextLookRotation();
            }

            m_RemainingDuration = durationSeconds > 0f ? durationSeconds : 0f;
            m_IsShowingText = true;
        }

        private void ClearText()
        {
            if (m_SpeechText != null)
            {
                m_SpeechText.text = string.Empty;
                m_SpeechText.gameObject.SetActive(false);
            }

            m_RemainingDuration = 0f;
            m_IsShowingText = false;
        }

        private void EnsureSpeechTextReference()
        {
            if (m_SpeechText == null)
            {
                m_SpeechText = GetComponentInChildren<TextMeshPro>(true);
            }

            if (m_SpeechText == null && m_AutoCreateSpeechText)
            {
                var textObject = new GameObject("SpeechText", typeof(TextMeshPro));
                textObject.transform.SetParent(transform, false);
                m_SpeechText = textObject.GetComponent<TextMeshPro>();
                if (m_SpeechText != null)
                {
                    m_SpeechText.alignment = TextAlignmentOptions.Center;
                    m_SpeechText.horizontalAlignment = HorizontalAlignmentOptions.Center;
                    m_SpeechText.verticalAlignment = VerticalAlignmentOptions.Middle;
                    m_SpeechText.textWrappingMode = TextWrappingModes.Normal;
                    m_SpeechText.overflowMode = TextOverflowModes.Truncate;
                    m_SpeechText.enableAutoSizing = true;
                    m_SpeechText.fontSizeMin = Mathf.Max(0.5f, m_SpeechFontSize * 0.65f);
                    m_SpeechText.fontSizeMax = Mathf.Max(0.8f, m_SpeechFontSize);
                }
            }

            if (m_SpeechText == null)
            {
                return;
            }

            m_SpeechText.transform.localScale = m_SpeechLocalScale;
            m_SpeechText.transform.localPosition = CalculateOffset();
            m_SpeechText.color = m_SpeechTextColor;
            m_SpeechText.fontSize = m_SpeechFontSize;
            if (m_SpeechText.rectTransform != null)
            {
                m_SpeechText.rectTransform.sizeDelta = new Vector2(8f, 3f);
            }

            if (!m_IsShowingText)
            {
                m_SpeechText.text = string.Empty;
                m_SpeechText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Runtime fallback for misconfigured scenes: if profile is missing, attempt to bind one by name/id heuristics.
        /// </summary>
        public bool TryAutoAssignProfileFromName()
        {
            if (m_Profile != null)
            {
                return true;
            }

            NpcDialogueProfile[] profiles = NpcDialogueProfile.GetAllProfiles();
            if (profiles == null || profiles.Length == 0)
            {
                return false;
            }

            string actorName =
                (gameObject != null ? gameObject.name : string.Empty) ?? string.Empty;
            string actorLower = actorName.Trim().ToLowerInvariant();
            string profileHint = (m_ProfileIdOverride ?? string.Empty).Trim().ToLowerInvariant();

            NpcDialogueProfile best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < profiles.Length; i++)
            {
                NpcDialogueProfile candidate = profiles[i];
                if (candidate == null)
                {
                    continue;
                }

                string id = (candidate.ProfileId ?? string.Empty).Trim().ToLowerInvariant();
                string idCore = id.Replace("npc.", string.Empty);
                string display = (candidate.DisplayName ?? string.Empty).Trim().ToLowerInvariant();

                int score = 0;
                if (!string.IsNullOrWhiteSpace(profileHint))
                {
                    if (profileHint == id || profileHint == idCore)
                    {
                        score += 100;
                    }
                    if (!string.IsNullOrWhiteSpace(display) && profileHint.Contains(display))
                    {
                        score += 20;
                    }
                }

                if (!string.IsNullOrWhiteSpace(actorLower))
                {
                    if (actorLower.Contains(id))
                    {
                        score += 90;
                    }
                    if (!string.IsNullOrWhiteSpace(idCore) && actorLower.Contains(idCore))
                    {
                        score += 80;
                    }
                    if (!string.IsNullOrWhiteSpace(display) && actorLower.Contains(display))
                    {
                        score += 60;
                    }

                    if (
                        actorLower.Contains("storm")
                        && (idCore.Contains("storm") || display.Contains("storm"))
                    )
                    {
                        score += 40;
                    }
                    if (
                        actorLower.Contains("forge")
                        && (idCore.Contains("forge") || display.Contains("forge"))
                    )
                    {
                        score += 40;
                    }
                    if (
                        actorLower.Contains("archiv")
                        && (idCore.Contains("archiv") || display.Contains("archiv"))
                    )
                    {
                        score += 40;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null || bestScore <= 0)
            {
                return false;
            }

            m_Profile = best;
            NGLog.Warn(
                "Dialogue",
                NGLog.Format(
                    "Auto-assigned missing NPC profile",
                    ("actor", actorName),
                    ("profileId", best.ProfileId ?? string.Empty),
                    ("score", bestScore)
                    ),
                this
            );
            return true;
        }

        private Vector3 CalculateOffset()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.mesh != null)
            {
                return new Vector3(0f, mf.mesh.bounds.max.y + m_TextOffsetPadding, 0f);
            }

            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                // bounds.max.y is world-space top; subtract transform.position.y for local offset
                float topLocal = col.bounds.max.y - transform.position.y;
                return new Vector3(0f, topLocal + m_TextOffsetPadding, 0f);
            }

            // Animated characters: SkinnedMeshRenderer on child, no MeshFilter on root
            SkinnedMeshRenderer smr = GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                float topLocal = smr.bounds.max.y - transform.position.y;
                return new Vector3(0f, topLocal + m_TextOffsetPadding, 0f);
            }

            // CharacterController height is a reliable top-of-capsule fallback
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                return new Vector3(0f, cc.height + m_TextOffsetPadding, 0f);
            }

            return new Vector3(0f, m_DefaultTextHeight, 0f);
        }

        private Quaternion GetTextLookRotation()
        {
            if (m_CachedCamera == null)
                m_CachedCamera = Camera.main;
            Camera cam = m_CachedCamera;
            if (cam == null)
            {
                return transform.rotation;
            }

            Vector3 cameraForward = cam.transform.forward;
            cameraForward.y = 0.0f;
            if (cameraForward.sqrMagnitude < 0.0001f)
            {
                return transform.rotation;
            }

            cameraForward.Normalize();
            return Quaternion.LookRotation(cameraForward);
        }

        public bool IsShowingText => m_IsShowingText;

        public override void OnNetworkDespawn()
        {
            ClearText();
            base.OnNetworkDespawn();
        }

        [ContextMenu("Dialogue/Send Test Prompt (Server)")]
        private void SendTestPromptContextMenu()
        {
            if (!Application.isPlaying)
            {
                NGLog.Warn("NpcDialogueActor", "Enter Play Mode to send a test prompt.");
                return;
            }

            if (!IsServer)
            {
                NGLog.Warn("NpcDialogueActor", "Test prompt must be sent from server/host.");
                return;
            }

            NetworkDialogueService service = NetworkDialogueService.Instance;
            if (service == null)
            {
                NGLog.Warn("NpcDialogueActor", "NetworkDialogueService not found in scene.");
                return;
            }

            NetworkObject player =
                NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClient?.PlayerObject
                : null;
            if (player == null)
            {
                NGLog.Warn("NpcDialogueActor", "Local player object not available.");
                return;
            }

            ulong speakerId = NetworkObjectId;
            ulong listenerId = player.NetworkObjectId;
            string key = service.ResolveConversationKey(
                speakerId,
                listenerId,
                player.OwnerClientId,
                null
            );

            service.RequestDialogue(
                new NetworkDialogueService.DialogueRequest
                {
                    Prompt = "Hello.",
                    ConversationKey = key,
                    SpeakerNetworkId = speakerId,
                    ListenerNetworkId = listenerId,
                    RequestingClientId = player.OwnerClientId,
                    Broadcast = true,
                    BroadcastDuration = 2f,
                    NotifyClient = true,
                    ClientRequestId = 0,
                    IsUserInitiated = false,
                    BlockRepeatedPrompt = false,
                    MinRepeatDelaySeconds = 0f,
                    RequireUserReply = false,
                }
            );

            NGLog.Info(
                "Dialogue",
                NGLog.Format(
                    "NpcDialogueActor test prompt sent",
                    ("npc", name),
                    ("speaker", speakerId),
                    ("listener", listenerId),
                    ("key", key)
                    ),
                this
            );
        }

        private static void BuildEffectCard(
            StringBuilder sb,
            string label,
            string description,
            PrefabPowerEntry entry
        )
        {
            sb.Append($"- **{label}**: {description}");

            var tags = new List<string>();
            if (entry.EnableGameplayDamage)
            {
                tags.Add("Damage");
            }
            if (entry.EnableHoming)
            {
                tags.Add("Homing");
            }
            if (!string.IsNullOrEmpty(entry.Element))
            {
                tags.Add(entry.Element);
            }

            if (tags.Count > 0)
            {
                sb.Append($" ({string.Join(", ", tags)})");
            }
            sb.AppendLine();

            if (entry.Keywords != null && entry.Keywords.Length > 0)
            {
                sb.Append("  Keywords: ").AppendLine(string.Join(", ", entry.Keywords));
            }

            // Include actual defaults so the LLM knows what "normal" looks like for this effect
            var exParts = new List<string> { "Target: Player" };
            if (entry.Scale != 1f)
                exParts.Add($"Scale: {entry.Scale:0.#}");
            if (entry.DurationSeconds > 0f)
                exParts.Add($"Duration: {entry.DurationSeconds:0.#}");
            sb.AppendLine($"  → [EFFECT: {label} | {string.Join(" | ", exParts)}]");
        }

        /// <summary>
        /// Append powers from the central EffectCatalog to the prompt.
        /// This provides NPCs access to all global effects in addition to their profile-specific ones.
        /// </summary>
        private void TryAppendCatalogPowers(StringBuilder sb)
        {
            try
            {
                var catalog = Effects.EffectCatalog.Instance ?? Effects.EffectCatalog.Load();
                if (catalog == null || catalog.allEffects.Count == 0)
                    return;

                // Only append if there are effects not already in the profile
                var profilePowerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (m_Profile.PrefabPowers != null)
                {
                    foreach (var p in m_Profile.PrefabPowers)
                    {
                        if (p != null && !string.IsNullOrWhiteSpace(p.PowerName))
                            profilePowerNames.Add(p.PowerName.Trim());
                    }
                }

                // Use GetAllRegisteredEffects() so runtime-registered NPC powers from
                // other actors (and any future catalog entries) appear in this NPC's prompt.
                bool addedAny = false;
                foreach (var effect in catalog.GetAllRegisteredEffects())
                {
                    if (effect == null || string.IsNullOrWhiteSpace(effect.effectTag))
                        continue;

                    if (profilePowerNames.Contains(effect.effectTag))
                        continue;

                    sb.AppendLine();
                    AppendCatalogEffectCard(sb, effect);
                    addedAny = true;
                }

                if (addedAny)
                {
                    sb.AppendLine("(Additional effects from EffectCatalog)");
                }
            }
            catch (System.Exception ex)
            {
                // Non-fatal - catalog might not exist yet
                NGLog.Warn("NpcDialogueActor", $"Could not load EffectCatalog: {ex.Message}");
            }
        }

        private static void AppendCatalogEffectCard(StringBuilder sb, Effects.EffectDefinition effect)
        {
            sb.Append($"- **{effect.effectTag}**: {effect.description}");

            var tags = new List<string>();
            if (effect.enableGameplayDamage) tags.Add("Damage");
            if (effect.enableHoming) tags.Add("Homing");
            if (tags.Count > 0)
                sb.Append($" ({string.Join(", ", tags)})");
            sb.AppendLine();

            // Use the effect's intended target type for a realistic example
            string targetExample;
            switch (effect.targetType)
            {
                case Effects.EffectTargetType.Floor:
                case Effects.EffectTargetType.WorldPoint:
                    targetExample = "Floor";
                    break;
                case Effects.EffectTargetType.Npc:
                    targetExample = "Self";
                    break;
                default:
                    targetExample = "Player";
                    break;
            }

            var exParts = new List<string> { $"Target: {targetExample}" };
            if (effect.allowCustomScale)
                exParts.Add($"Scale: {effect.defaultScale:0.#}");
            if (effect.allowCustomDuration)
                exParts.Add($"Duration: {effect.defaultDuration:0.#}");
            if (effect.placementMode == Effects.EffectPlacementMode.GroundAoe)
                exParts.Add($"Radius: {(effect.minRadius + effect.maxRadius) * 0.4f:0.#}");
            sb.AppendLine($"  → [EFFECT: {effect.effectTag} | {string.Join(" | ", exParts)}]");

            // Show customizable parameter ranges so LLM knows what values are valid
            var hints = new List<string>();
            if (effect.allowCustomScale)
                hints.Add($"Scale {effect.minScale:0.#}–{effect.maxScale:0.#}");
            if (effect.allowCustomDuration)
                hints.Add($"Duration {effect.minDuration:0.#}–{effect.maxDuration:0.#}s");
            if (effect.allowCustomColor)
                hints.Add("Color");
            if (effect.placementMode != Effects.EffectPlacementMode.Auto)
                hints.Add(effect.placementMode.ToString());
            if (hints.Count > 0)
                sb.AppendLine($"  Params: [{string.Join("] [", hints)}]");
        }
    }
}
