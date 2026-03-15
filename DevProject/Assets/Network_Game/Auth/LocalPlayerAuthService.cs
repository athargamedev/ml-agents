using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Network_Game.Diagnostics;
using Network_Game.Dialogue;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Auth
{
    [DefaultExecutionOrder(-220)]
    public class LocalPlayerAuthService : MonoBehaviour
    {
        [Serializable]
        public struct LocalPlayerRecord
        {
            public long PlayerId;
            public string NameId;
            public string MirrorLoraPath;
            public float MirrorLoraWeight;
            public string ProfileVersion;
            public string BaseModelId;
        }

        private enum StorageBackend
        {
            JsonFile = 0,
            BackendApi = 1,
        }

        private interface ILocalAuthStoreProvider
        {
            string Description { get; }
            bool Initialize();
            bool TryLogin(
                string normalizedNameId,
                float defaultMirrorLoraWeight,
                out LocalPlayerRecord record
            );
            bool SetCustomizationValue(long playerId, string key, string value);
            bool TryGetCustomizationValue(long playerId, string key, out string value);
            Dictionary<string, string> GetAllCustomization(long playerId);
            bool UpdateMirror(
                long playerId,
                string loraPath,
                float mirrorLoraWeight,
                string profileVersion,
                string baseModelId
            );
        }

        [Serializable]
        private class LocalAuthStoreData
        {
            public long NextPlayerId = 1;
            public List<LocalAuthPlayerRow> Players = new List<LocalAuthPlayerRow>();
            public List<LocalAuthCustomizationRow> Customization =
                new List<LocalAuthCustomizationRow>();
        }

        [Serializable]
        private class LocalAuthPlayerRow
        {
            public long PlayerId;
            public string NameId;
            public string MirrorLoraPath;
            public float MirrorLoraWeight;
            public string ProfileVersion;
            public string BaseModelId;
            public string CreatedUtc;
            public string UpdatedUtc;
        }

        [Serializable]
        private class LocalAuthCustomizationRow
        {
            public long PlayerId;
            public string Key;
            public string Value;
            public string UpdatedUtc;
        }

        private sealed class JsonLocalAuthStoreProvider : ILocalAuthStoreProvider
        {
            private readonly string m_StorePath;
            private readonly bool m_LogDebug;
            private LocalAuthStoreData m_Data = new LocalAuthStoreData();

            public string Description => "JSON local auth store";

            public JsonLocalAuthStoreProvider(string storePath, bool logDebug)
            {
                m_StorePath = storePath;
                m_LogDebug = logDebug;
            }

            public bool Initialize()
            {
                m_Data = new LocalAuthStoreData();
#if UNITY_WEBGL
                return true; // No persistent storage on WebGL; use in-memory only
#else
                if (!File.Exists(m_StorePath))
                {
                    return Save();
                }

                try
                {
                    string json = File.ReadAllText(m_StorePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        LocalAuthStoreData loaded = JsonUtility.FromJson<LocalAuthStoreData>(json);
                        if (loaded != null)
                        {
                            m_Data = loaded;
                        }
                    }
                }
                catch (IOException ex)
                {
                    NGLog.Error("Auth", $"Failed reading JSON auth store: {ex.Message}");
                    m_Data = new LocalAuthStoreData();
                }
                catch (UnauthorizedAccessException ex)
                {
                    NGLog.Error("Auth", $"Failed reading JSON auth store: {ex.Message}");
                    m_Data = new LocalAuthStoreData();
                }

                if (m_Data.Players == null)
                {
                    m_Data.Players = new List<LocalAuthPlayerRow>();
                }

                if (m_Data.Customization == null)
                {
                    m_Data.Customization = new List<LocalAuthCustomizationRow>();
                }

                long maxPlayerId = 0;
                for (int i = 0; i < m_Data.Players.Count; i++)
                {
                    LocalAuthPlayerRow player = m_Data.Players[i];
                    if (player != null)
                    {
                        maxPlayerId = Math.Max(maxPlayerId, player.PlayerId);
                    }
                }

                m_Data.NextPlayerId = Math.Max(m_Data.NextPlayerId, maxPlayerId + 1);
                return true;
#endif // !UNITY_WEBGL
            }

            public bool TryLogin(
                string normalizedNameId,
                float defaultMirrorLoraWeight,
                out LocalPlayerRecord record
            )
            {
                record = default;
                LocalAuthPlayerRow player = FindPlayerByNameId(normalizedNameId);
                string utcNow = DateTime.UtcNow.ToString("o");
                if (player == null)
                {
                    player = new LocalAuthPlayerRow
                    {
                        PlayerId = Math.Max(1, m_Data.NextPlayerId++),
                        NameId = normalizedNameId,
                        MirrorLoraPath = string.Empty,
                        MirrorLoraWeight = Mathf.Clamp(defaultMirrorLoraWeight, 0f, 2f),
                        ProfileVersion = string.Empty,
                        BaseModelId = string.Empty,
                        CreatedUtc = utcNow,
                        UpdatedUtc = utcNow,
                    };
                    m_Data.Players.Add(player);
                }
                else
                {
                    player.UpdatedUtc = utcNow;
                }

                if (!Save())
                {
                    return false;
                }

                record = ToRecord(player);
                return true;
            }

            public bool SetCustomizationValue(long playerId, string key, string value)
            {
                LocalAuthCustomizationRow entry = FindCustomization(playerId, key);
                string utcNow = DateTime.UtcNow.ToString("o");
                if (entry == null)
                {
                    entry = new LocalAuthCustomizationRow
                    {
                        PlayerId = playerId,
                        Key = key,
                        Value = value,
                        UpdatedUtc = utcNow,
                    };
                    m_Data.Customization.Add(entry);
                }
                else
                {
                    entry.Value = value;
                    entry.UpdatedUtc = utcNow;
                }

                return Save();
            }

            public bool TryGetCustomizationValue(long playerId, string key, out string value)
            {
                value = string.Empty;
                LocalAuthCustomizationRow entry = FindCustomization(playerId, key);
                if (entry == null)
                {
                    return false;
                }

                value = entry.Value ?? string.Empty;
                return true;
            }

            public Dictionary<string, string> GetAllCustomization(long playerId)
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < m_Data.Customization.Count; i++)
                {
                    LocalAuthCustomizationRow row = m_Data.Customization[i];
                    if (
                        row == null
                        || row.PlayerId != playerId
                        || string.IsNullOrWhiteSpace(row.Key)
                    )
                    {
                        continue;
                    }

                    values[row.Key] = row.Value ?? string.Empty;
                }

                return values;
            }

            public bool UpdateMirror(
                long playerId,
                string loraPath,
                float mirrorLoraWeight,
                string profileVersion,
                string baseModelId
            )
            {
                LocalAuthPlayerRow player = FindPlayerById(playerId);
                if (player == null)
                {
                    return false;
                }

                player.MirrorLoraPath = loraPath ?? string.Empty;
                player.MirrorLoraWeight = Mathf.Clamp(mirrorLoraWeight, 0f, 2f);
                player.ProfileVersion = profileVersion ?? string.Empty;
                player.BaseModelId = baseModelId ?? string.Empty;
                player.UpdatedUtc = DateTime.UtcNow.ToString("o");
                return Save();
            }

            private bool Save()
            {
#if UNITY_WEBGL
                return true; // No persistent file storage on WebGL; data lives in memory only
#else
                try
                {
                    string directory = Path.GetDirectoryName(m_StorePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    string json = JsonUtility.ToJson(m_Data, true);
                    File.WriteAllText(m_StorePath, json);
                    if (m_LogDebug)
                    {
                        NGLog.Debug(
                            "Auth",
                            NGLog.Format("JSON store saved", ("path", m_StorePath))
                        );
                    }
                    return true;
                }
                catch (IOException ex)
                {
                    NGLog.Error("Auth", $"Failed writing JSON auth store: {ex.Message}");
                    return false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    NGLog.Error("Auth", $"Failed writing JSON auth store: {ex.Message}");
                    return false;
                }
#endif // !UNITY_WEBGL
            }

            private LocalAuthPlayerRow FindPlayerByNameId(string nameId)
            {
                for (int i = 0; i < m_Data.Players.Count; i++)
                {
                    LocalAuthPlayerRow row = m_Data.Players[i];
                    if (
                        row != null
                        && string.Equals(row.NameId, nameId, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return row;
                    }
                }

                return null;
            }

            private LocalAuthPlayerRow FindPlayerById(long playerId)
            {
                for (int i = 0; i < m_Data.Players.Count; i++)
                {
                    LocalAuthPlayerRow row = m_Data.Players[i];
                    if (row != null && row.PlayerId == playerId)
                    {
                        return row;
                    }
                }

                return null;
            }

            private LocalAuthCustomizationRow FindCustomization(long playerId, string key)
            {
                for (int i = 0; i < m_Data.Customization.Count; i++)
                {
                    LocalAuthCustomizationRow row = m_Data.Customization[i];
                    if (
                        row != null
                        && row.PlayerId == playerId
                        && string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return row;
                    }
                }

                return null;
            }

            private static LocalPlayerRecord ToRecord(LocalAuthPlayerRow player)
            {
                return new LocalPlayerRecord
                {
                    PlayerId = player.PlayerId,
                    NameId = player.NameId ?? string.Empty,
                    MirrorLoraPath = player.MirrorLoraPath ?? string.Empty,
                    MirrorLoraWeight = player.MirrorLoraWeight,
                    ProfileVersion = player.ProfileVersion ?? string.Empty,
                    BaseModelId = player.BaseModelId ?? string.Empty,
                };
            }
        }

        public static LocalPlayerAuthService Instance { get; private set; }
        public static event Action<LocalPlayerRecord> OnPlayerLoggedIn;
        public static event Action OnPlayerLoggedOut;

        [Header("Bootstrap")]
        [SerializeField]
        private bool m_AutoLoginOnStart = true;

        [SerializeField]
        [Tooltip(
            "If enabled, startup never auto-logs in; user must explicitly press Login each run."
         )]
        private bool m_RequireExplicitLoginEachSession = true;

        [SerializeField]
        private string m_DefaultNameId = "player_local";

        [SerializeField]
        private bool m_DontDestroyOnLoad = true;

        [Header("Storage")]
        [SerializeField]
        private StorageBackend m_StorageBackend = StorageBackend.JsonFile;

        [SerializeField]
        private string m_LocalStoreFileName = "network_game_local_auth.json";

        [Header("Mirror LoRA Defaults")]
        [SerializeField]
        [Range(0f, 2f)]
        private float m_DefaultMirrorLoraWeight = 0.25f;

        [SerializeField]
        private bool m_LogDebug = true;

        private const string LastNameIdKey = "NG.Auth.LastNameId";
        private const string CustomizationJsonKey = "customization_json";

        private string m_LocalStorePath;
        private ILocalAuthStoreProvider m_Store;
        private LocalPlayerRecord m_CurrentPlayer;
        private bool m_HasCurrentPlayer;
        private ulong m_LocalPlayerNetworkId;
        private bool m_PendingMirrorApply;

        public bool HasCurrentPlayer => m_HasCurrentPlayer;
        public LocalPlayerRecord CurrentPlayer => m_CurrentPlayer;
        public ulong LocalPlayerNetworkId => m_LocalPlayerNetworkId;
        public string LastLoginNameId => PlayerPrefs.GetString(LastNameIdKey, m_DefaultNameId);

        public static LocalPlayerAuthService EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

#if UNITY_2023_1_OR_NEWER
            Instance = FindAnyObjectByType<LocalPlayerAuthService>();
#else
            Instance = FindAnyObjectByType<LocalPlayerAuthService>();
#endif
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("LocalPlayerAuthService");
            Instance = go.AddComponent<LocalPlayerAuthService>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (m_DontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            if (!InitializeStoreProvider())
            {
                NGLog.Error("Auth", "Auth provider initialization failed.");
                return;
            }

            if (m_AutoLoginOnStart && !m_RequireExplicitLoginEachSession)
            {
                EnsureLoggedIn();
            }
        }

        private void Update()
        {
            if (!m_PendingMirrorApply || !m_HasCurrentPlayer)
            {
                return;
            }

            TryApplyCurrentMirrorLoraToDialogue();
        }

        [ContextMenu("Login With Default NameId")]
        private void LoginWithDefaultNameIdFromContext()
        {
            Login(m_DefaultNameId);
        }

        public bool EnsureLoggedIn()
        {
            if (m_HasCurrentPlayer)
            {
                return true;
            }

            string targetNameId = LastLoginNameId;
            if (string.IsNullOrWhiteSpace(targetNameId))
            {
                targetNameId = m_DefaultNameId;
            }

            return Login(targetNameId);
        }

        public bool Login(string nameId)
        {
            string normalizedNameId = NormalizeNameId(nameId);
            if (string.IsNullOrWhiteSpace(normalizedNameId))
            {
                NGLog.Warn("Auth", "Login failed: invalid name_id.");
                return false;
            }

            if (m_Store == null)
            {
                NGLog.Warn("Auth", "Login failed: auth store unavailable.");
                return false;
            }

            if (
                !m_Store.TryLogin(
                    normalizedNameId,
                    Mathf.Clamp(m_DefaultMirrorLoraWeight, 0f, 2f),
                    out LocalPlayerRecord loaded
                )
            )
            {
                NGLog.Warn("Auth", "Login failed: unable to load player record.");
                return false;
            }

            m_CurrentPlayer = loaded;
            m_HasCurrentPlayer = true;
            m_PendingMirrorApply = true;
            EnsurePromptContextInitialized();

            PlayerPrefs.SetString(LastNameIdKey, normalizedNameId);
            PlayerPrefs.Save();

            if (m_LogDebug)
            {
                NGLog.Info(
                    "Auth",
                    NGLog.Format(
                        "Login success",
                        ("name_id", normalizedNameId),
                        ("player_id", loaded.PlayerId)
                    )
                );
            }

            OnPlayerLoggedIn?.Invoke(m_CurrentPlayer);
            return true;
        }

        public void Logout()
        {
            if (!m_HasCurrentPlayer)
            {
                return;
            }

            NetworkDialogueService dialogueService = NetworkDialogueService.Instance;
            if (dialogueService != null)
            {
                if (dialogueService.IsServer && m_LocalPlayerNetworkId != 0)
                {
                    dialogueService.ClearPlayerPromptContext(m_LocalPlayerNetworkId);
                }
                else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                {
                    dialogueService.RequestClearPlayerPromptContextFromClient();
                }
            }

            m_CurrentPlayer = default;
            m_HasCurrentPlayer = false;
            m_PendingMirrorApply = false;
            OnPlayerLoggedOut?.Invoke();
            NGLog.Info("Auth", "Logged out.");
        }

        public void AttachLocalPlayer(GameObject playerObject)
        {
            ulong networkId = 0;
            if (playerObject != null)
            {
                NetworkObject netObj = playerObject.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    networkId = netObj.NetworkObjectId;
                }
            }

            if (networkId == 0)
            {
                networkId = ResolveLocalPlayerNetworkId();
            }

            m_LocalPlayerNetworkId = networkId;
            if (m_HasCurrentPlayer)
            {
                m_PendingMirrorApply = true;
            }

            if (m_LogDebug)
            {
                NGLog.Debug(
                    "Auth",
                    NGLog.Format("Attached local player", ("networkId", m_LocalPlayerNetworkId))
                );
            }
        }

        public bool SetCustomizationValue(string key, string value)
        {
            if (!m_HasCurrentPlayer || m_Store == null)
            {
                return false;
            }

            string normalizedKey = NormalizeCustomizationKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return false;
            }

            bool updated = m_Store.SetCustomizationValue(
                m_CurrentPlayer.PlayerId,
                normalizedKey,
                value ?? string.Empty
            );
            if (updated)
            {
                m_PendingMirrorApply = true;
            }

            return updated;
        }

        public bool TryGetCustomizationValue(string key, out string value)
        {
            value = string.Empty;
            if (!m_HasCurrentPlayer || m_Store == null)
            {
                return false;
            }

            string normalizedKey = NormalizeCustomizationKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return false;
            }

            return m_Store.TryGetCustomizationValue(
                m_CurrentPlayer.PlayerId,
                normalizedKey,
                out value
            );
        }

        public Dictionary<string, string> GetAllCustomization()
        {
            if (!m_HasCurrentPlayer || m_Store == null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return m_Store.GetAllCustomization(m_CurrentPlayer.PlayerId);
        }

        public bool SetCustomizationJson(string json)
        {
            string normalizedJson = string.IsNullOrWhiteSpace(json) ? "{}" : json.Trim();
            return SetCustomizationValue(CustomizationJsonKey, normalizedJson);
        }

        public string GetCustomizationJson()
        {
            if (!m_HasCurrentPlayer || m_Store == null)
            {
                return "{}";
            }

            if (!TryGetCustomizationValue(CustomizationJsonKey, out string json))
            {
                if (!EnsurePromptContextInitialized())
                {
                    return "{}";
                }

                return TryGetCustomizationValue(CustomizationJsonKey, out string initializedJson)
                    ? initializedJson
                    : "{}";
            }

            if (NeedsPromptContextInitialization(json) || !HasPromptContextCoreFields(json))
            {
                if (!EnsurePromptContextInitialized())
                {
                    return string.IsNullOrWhiteSpace(json) ? "{}" : json;
                }

                return TryGetCustomizationValue(CustomizationJsonKey, out string refreshedJson)
                    ? refreshedJson
                    : "{}";
            }

            return string.IsNullOrWhiteSpace(json) ? "{}" : json;
        }

        public bool EnsurePromptContextInitialized()
        {
            if (!m_HasCurrentPlayer || m_Store == null)
            {
                return false;
            }

            string current = "{}";
            if (!TryGetCustomizationValue(CustomizationJsonKey, out current))
            {
                current = "{}";
            }

            if (!NeedsPromptContextInitialization(current) && HasPromptContextCoreFields(current))
            {
                return true;
            }

            string generated = BuildDefaultPromptContextJson(current);
            if (string.IsNullOrWhiteSpace(generated))
            {
                return false;
            }

            bool saved = SetCustomizationJson(generated);
            if (saved && m_LogDebug)
            {
                NGLog.Info(
                    "Auth",
                    NGLog.Format(
                        "Initialized prompt context JSON",
                        ("name_id", m_CurrentPlayer.NameId),
                        ("chars", generated.Length)
                    )
                );
            }

            return saved;
        }

        // ── Player Data Helpers ─────────────────────────────────────

        public bool SetPlayerClass(string className) =>
            SetCustomizationValue("class", (className ?? string.Empty).Trim().ToLowerInvariant());

        public bool SetReputation(string npcId, int value)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return false;
            int clamped = Mathf.Clamp(value, 0, 100);
            return SetCustomizationValue(
                $"reputation_{npcId.Trim().ToLowerInvariant()}",
                clamped.ToString()
            );
        }

        public int GetReputation(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return 50;
            if (
                !TryGetCustomizationValue(
                    $"reputation_{npcId.Trim().ToLowerInvariant()}",
                    out string raw
                )
            )
                return 50;
            return int.TryParse(raw, out int v) ? Mathf.Clamp(v, 0, 100) : 50;
        }

        public bool AddInventoryTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            string t = tag.Trim().ToLowerInvariant();
            TryGetCustomizationValue("inventory_tags", out string existing);
            var tags = ParseCsvSet(existing);
            if (!tags.Add(t))
                return true; // already present
            return SetCustomizationValue("inventory_tags", string.Join(",", tags));
        }

        public bool RemoveInventoryTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            TryGetCustomizationValue("inventory_tags", out string existing);
            var tags = ParseCsvSet(existing);
            if (!tags.Remove(tag.Trim().ToLowerInvariant()))
                return true;
            return SetCustomizationValue("inventory_tags", string.Join(",", tags));
        }

        public bool ClearInventoryTags() => SetCustomizationValue("inventory_tags", string.Empty);

        public bool SetQuestFlag(string flag)
        {
            if (string.IsNullOrWhiteSpace(flag))
                return false;
            string f = flag.Trim().ToLowerInvariant();
            TryGetCustomizationValue("quest_flags", out string existing);
            var flags = ParseCsvSet(existing);
            if (!flags.Add(f))
                return true;
            return SetCustomizationValue("quest_flags", string.Join(",", flags));
        }

        public bool ClearQuestFlag(string flag)
        {
            if (string.IsNullOrWhiteSpace(flag))
                return false;
            TryGetCustomizationValue("quest_flags", out string existing);
            var flags = ParseCsvSet(existing);
            if (!flags.Remove(flag.Trim().ToLowerInvariant()))
                return true;
            return SetCustomizationValue("quest_flags", string.Join(",", flags));
        }

        public bool HasQuestFlag(string flag)
        {
            if (string.IsNullOrWhiteSpace(flag))
                return false;
            TryGetCustomizationValue("quest_flags", out string existing);
            return ParseCsvSet(existing).Contains(flag.Trim().ToLowerInvariant());
        }

        public bool SetLastAction(string action) =>
            SetCustomizationValue("last_action", (action ?? string.Empty).Trim());

        private static HashSet<string> ParseCsvSet(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in csv.Split(','))
            {
                string t = part.Trim();
                if (!string.IsNullOrEmpty(t))
                    set.Add(t);
            }
            return set;
        }

        public bool SetCurrentPlayerMirrorLora(
            string loraPath,
            float weight = -1f,
            string profileVersion = null,
            string baseModelId = null
        )
        {
            if (!m_HasCurrentPlayer || m_Store == null)
            {
                return false;
            }

            string normalizedPath = string.IsNullOrWhiteSpace(loraPath)
                ? string.Empty
                : loraPath.Trim();
            float resolvedWeight =
                weight >= 0f ? Mathf.Clamp(weight, 0f, 2f) : m_DefaultMirrorLoraWeight;
            if (
                !m_Store.UpdateMirror(
                    m_CurrentPlayer.PlayerId,
                    normalizedPath,
                    resolvedWeight,
                    profileVersion,
                    baseModelId
                )
            )
            {
                return false;
            }

            m_CurrentPlayer.MirrorLoraPath = normalizedPath;
            m_CurrentPlayer.MirrorLoraWeight = resolvedWeight;
            m_CurrentPlayer.ProfileVersion = profileVersion ?? string.Empty;
            m_CurrentPlayer.BaseModelId = baseModelId ?? string.Empty;
            m_PendingMirrorApply = true;

            if (m_LogDebug)
            {
                NGLog.Info(
                    "Auth",
                    NGLog.Format(
                        "Updated mirror LoRA",
                        ("name_id", m_CurrentPlayer.NameId),
                        ("path", normalizedPath),
                        ("weight", resolvedWeight)
                    )
                );
            }

            return true;
        }

        private void TryApplyCurrentMirrorLoraToDialogue()
        {
            if (!m_HasCurrentPlayer)
            {
                return;
            }

            if (m_LocalPlayerNetworkId == 0)
            {
                m_LocalPlayerNetworkId = ResolveLocalPlayerNetworkId();
                if (m_LocalPlayerNetworkId == 0)
                {
                    return;
                }
            }

            NetworkDialogueService dialogueService = NetworkDialogueService.Instance;
            if (dialogueService == null)
            {
                return;
            }

            bool contextApplied = true;
            string mode = "server";
            if (dialogueService.IsServer)
            {
                contextApplied = dialogueService.SetPlayerPromptContext(
                    m_LocalPlayerNetworkId,
                    m_CurrentPlayer.NameId,
                    GetCustomizationJson()
                );
            }
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
            {
                mode = "rpc";
                contextApplied = dialogueService.RequestSetPlayerPromptContextFromClient(
                    m_CurrentPlayer.NameId,
                    GetCustomizationJson()
                );
            }
            else
            {
                return;
            }

            if (m_LogDebug)
            {
                NGLog.Info(
                    "Auth",
                    NGLog.Format(
                        "Identity prompt-context sync",
                        ("networkId", m_LocalPlayerNetworkId),
                        ("contextApplied", contextApplied),
                        ("mode", mode),
                        ("name_id", m_CurrentPlayer.NameId),
                        ("path", m_CurrentPlayer.MirrorLoraPath ?? string.Empty)
                    )
                );
            }

            m_PendingMirrorApply = !contextApplied;
        }

        private bool InitializeStoreProvider()
        {
            StorageBackend selected = m_StorageBackend;
            if (selected != StorageBackend.JsonFile)
            {
                NGLog.Warn(
                    "Auth",
                    NGLog.Format(
                        "Storage backend not implemented yet; falling back to JSON",
                        ("requested", selected)
                    )
                );
                selected = StorageBackend.JsonFile;
            }

            string fileName = string.IsNullOrWhiteSpace(m_LocalStoreFileName)
                ? "network_game_local_auth.json"
                : m_LocalStoreFileName.Trim();
#if UNITY_WEBGL
            m_LocalStorePath = fileName; // Path unused on WebGL (in-memory only)
#else
            m_LocalStorePath = Path.Combine(Application.persistentDataPath, fileName);
#endif
            m_Store = new JsonLocalAuthStoreProvider(m_LocalStorePath, m_LogDebug);
            if (!m_Store.Initialize())
            {
                return false;
            }

            if (m_LogDebug)
            {
                NGLog.Info(
                    "Auth",
                    NGLog.Format(
                        "Auth store initialized",
                        ("provider", m_Store.Description),
                        ("path", m_LocalStorePath)
                    )
                );
            }

            return true;
        }

        private ulong ResolveLocalPlayerNetworkId()
        {
            NetworkObject localPlayer = ResolveLocalPlayerNetworkObject();
            return localPlayer != null ? localPlayer.NetworkObjectId : 0;
        }

        private NetworkObject ResolveLocalPlayerNetworkObject()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                NetworkObject localClientPlayer = manager.LocalClient?.PlayerObject;
                if (IsLocalPlayerNetworkObject(manager, localClientPlayer))
                {
                    return localClientPlayer;
                }
            }

            NetworkObject taggedPlayer = ResolveTaggedLocalPlayerNetworkObject(manager);
            if (taggedPlayer != null)
            {
                return taggedPlayer;
            }

            if (manager != null && manager.SpawnManager != null)
            {
                NetworkObject singlePlayerCandidate = null;
                int playerCandidateCount = 0;
                foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
                {
                    if (spawned == null || !spawned.IsPlayerObject)
                    {
                        continue;
                    }

                    if (IsLocalPlayerNetworkObject(manager, spawned))
                    {
                        return spawned;
                    }

                    playerCandidateCount++;
                    if (singlePlayerCandidate == null)
                    {
                        singlePlayerCandidate = spawned;
                    }
                }

                if (playerCandidateCount == 1)
                {
                    return singlePlayerCandidate;
                }
            }

            return null;
        }

        private NetworkObject ResolveTaggedLocalPlayerNetworkObject(NetworkManager manager)
        {
            GameObject[] taggedPlayers;
            try
            {
                taggedPlayers = GameObject.FindGameObjectsWithTag("Player");
            }
            catch (UnityException)
            {
                return null;
            }

            if (taggedPlayers == null || taggedPlayers.Length == 0)
            {
                return null;
            }

            NetworkObject singleTaggedCandidate = null;
            int taggedCandidateCount = 0;
            foreach (GameObject taggedPlayer in taggedPlayers)
            {
                if (taggedPlayer == null)
                {
                    continue;
                }

                NetworkObject taggedNetObj = taggedPlayer.GetComponent<NetworkObject>();
                if (taggedNetObj == null)
                {
                    continue;
                }

                if (IsLocalPlayerNetworkObject(manager, taggedNetObj))
                {
                    return taggedNetObj;
                }

                taggedCandidateCount++;
                if (singleTaggedCandidate == null)
                {
                    singleTaggedCandidate = taggedNetObj;
                }
            }

            return taggedCandidateCount == 1 ? singleTaggedCandidate : null;
        }

        private static bool IsLocalPlayerNetworkObject(
            NetworkManager manager,
            NetworkObject networkObject
        )
        {
            if (manager == null || networkObject == null || !networkObject.IsSpawned)
            {
                return false;
            }

            if (networkObject.IsOwner)
            {
                return true;
            }

            return manager.IsListening && networkObject.OwnerClientId == manager.LocalClientId;
        }

        private static string NormalizeNameId(string nameId)
        {
            if (string.IsNullOrWhiteSpace(nameId))
            {
                return string.Empty;
            }

            string raw = nameId.Trim().ToLowerInvariant();
            var chars = new List<char>(Mathf.Min(raw.Length, 32));
            for (int i = 0; i < raw.Length; i++)
            {
                if (chars.Count >= 32)
                {
                    break;
                }

                char c = raw[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    chars.Add(c);
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    chars.Add('_');
                }
            }

            return new string(chars.ToArray()).Trim('_');
        }

        private static string NormalizeCustomizationKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return key.Trim().ToLowerInvariant();
        }

        private static bool NeedsPromptContextInitialization(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            string trimmed = json.Trim();
            return trimmed == "{}" || trimmed == "{ }" || trimmed == "null";
        }

        private static bool HasPromptContextCoreFields(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            string normalized = json.Trim();
            if (normalized.Length < 2)
            {
                return false;
            }

            return normalized.Contains("\"name_id\"", StringComparison.OrdinalIgnoreCase)
                && normalized.Contains("\"player_id\"", StringComparison.OrdinalIgnoreCase)
                && normalized.Contains("\"customization\"", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildDefaultPromptContextJson(string existingCustomizationJson)
        {
            var allCustomization = GetAllCustomization();
            if (allCustomization.ContainsKey(CustomizationJsonKey))
            {
                allCustomization.Remove(CustomizationJsonKey);
            }

            string resolvedProfileVersion = string.IsNullOrWhiteSpace(
                m_CurrentPlayer.ProfileVersion
            )
                ? "v1-local"
                : m_CurrentPlayer.ProfileVersion;
            string resolvedBaseModelId = string.IsNullOrWhiteSpace(m_CurrentPlayer.BaseModelId)
                ? "default"
                : m_CurrentPlayer.BaseModelId;
            string resolvedMirrorPath = string.IsNullOrWhiteSpace(m_CurrentPlayer.MirrorLoraPath)
                ? "none"
                : m_CurrentPlayer.MirrorLoraPath;

            var builder = new StringBuilder(256);
            builder.Append("{\n");
            builder
                .Append("  \"name_id\": \"")
                .Append(EscapeJson(m_CurrentPlayer.NameId))
                .Append("\",\n");
            builder.Append("  \"player_id\": ").Append(m_CurrentPlayer.PlayerId).Append(",\n");
            builder
                .Append("  \"profile_version\": \"")
                .Append(EscapeJson(resolvedProfileVersion))
                .Append("\",\n");
            builder
                .Append("  \"base_model_id\": \"")
                .Append(EscapeJson(resolvedBaseModelId))
                .Append("\",\n");
            builder
                .Append("  \"mirror_lora_path\": \"")
                .Append(EscapeJson(resolvedMirrorPath))
                .Append("\",\n");
            builder
                .Append("  \"mirror_lora_weight\": ")
                .Append(m_CurrentPlayer.MirrorLoraWeight.ToString("0.###"))
                .Append(",\n");
            builder.Append("  \"session_source\": \"local_auth_store\",\n");
            builder
                .Append("  \"auth_loaded_utc\": \"")
                .Append(EscapeJson(DateTime.UtcNow.ToString("o")))
                .Append("\",\n");

            string existingPayload = string.IsNullOrWhiteSpace(existingCustomizationJson)
                ? string.Empty
                : existingCustomizationJson.Trim();
            if (
                !string.IsNullOrWhiteSpace(existingPayload)
                && !NeedsPromptContextInitialization(existingPayload)
            )
            {
                builder
                    .Append("  \"profile_blob_json\": \"")
                    .Append(EscapeJson(existingPayload))
                    .Append("\",\n");
            }

            builder.Append("  \"customization\": {");

            int written = 0;
            foreach (var pair in allCustomization)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                if (written == 0)
                {
                    builder.Append('\n');
                }
                else
                {
                    builder.Append(",\n");
                }

                builder
                    .Append("    \"")
                    .Append(EscapeJson(pair.Key))
                    .Append("\": \"")
                    .Append(EscapeJson(pair.Value))
                    .Append("\"");
                written++;
            }

            if (written > 0)
            {
                builder.Append('\n').Append("  ");
            }

            builder.Append("}\n");
            builder.Append("}");
            return builder.ToString();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
        }
    }
}
