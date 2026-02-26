using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;

namespace Network_Game.Dialogue.MCP
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Network Game/MCP/Showcase Polish Controller")]
    public sealed class MCPShowcasePolishController : MonoBehaviour
    {
        [Serializable]
        private sealed class ShotBinding
        {
            public string ShotId;
            public Transform Anchor;
            public GameObject KeyLight;
            public GameObject Ring;
            public GameObject Aura;

            public IEnumerable<GameObject> EnumeratePolishObjects()
            {
                if (KeyLight != null)
                {
                    yield return KeyLight;
                }

                if (Ring != null)
                {
                    yield return Ring;
                }

                if (Aura != null)
                {
                    yield return Aura;
                }
            }
        }

        [SerializeField]
        private Camera m_ShowcaseCamera;

        [SerializeField]
        [Tooltip("Optional Cinemachine virtual camera used for showcase shots. If set, this is preferred over enabling a separate raw Camera.")]
        private CinemachineVirtualCameraBase m_ShowcaseVirtualCamera;

        [SerializeField]
        private Transform m_CameraAnchorsRoot;

        [SerializeField]
        private bool m_AutoDiscoverOnAwake = true;

        [SerializeField]
        private bool m_AutoRebuildBindingsWhenMissing = true;

        [SerializeField]
        [Min(0f)]
        private float m_DefaultShotDurationSeconds = 2.5f;

        [SerializeField]
        private bool m_EnableShowcaseCameraDuringShot = true;

        [SerializeField]
        [Tooltip("Prefer Cinemachine priority switching when a showcase virtual camera is available.")]
        private bool m_PreferCinemachineForShots = true;

        [SerializeField]
        [Min(1)]
        [Tooltip("Priority boost added above the highest active Cinemachine camera when a showcase shot goes live.")]
        private int m_CinemachinePriorityBoost = 50;

        [SerializeField]
        [Tooltip("If no showcase Cinemachine camera is assigned, auto-add a CinemachineCamera component to MCP_ShowcaseCamera in Play Mode.")]
        private bool m_AutoCreateShowcaseVirtualCameraInPlayMode = true;

        [SerializeField]
        [Tooltip("Disable the raw showcase Camera component while using the Cinemachine showcase camera to avoid double-rendering conflicts.")]
        private bool m_DisableRawShowcaseCameraWhenUsingCinemachine = true;

        [SerializeField]
        private bool m_IsolateNpcPolishDuringShot = true;

        [SerializeField]
        private bool m_LogDebug = true;

        [Header("Dialogue Hooks")]
        [SerializeField]
        [Tooltip("Automatically trigger showcase shots when dialogue responses complete.")]
        private bool m_EnableDialogueResponseShotHook = true;

        [SerializeField]
        [Tooltip("Only react to user-initiated dialogue responses (recommended for multiplayer).")]
        private bool m_DialogueHookUserInitiatedOnly = true;

        [SerializeField]
        [Tooltip("Only react when the request belongs to the local client (recommended for multiplayer).")]
        private bool m_DialogueHookLocalRequesterOnly = true;

        [SerializeField]
        [Tooltip("Enable the showcase camera when a dialogue hook triggers a shot. If false, snaps only the disabled rig camera.")]
        private bool m_DialogueHookEnableShowcaseCamera;

        [SerializeField]
        [Tooltip("Isolate the matching NPC polish set (light/ring/aura) when a dialogue hook triggers a shot.")]
        private bool m_DialogueHookIsolatePolish = true;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Override duration for dialogue-triggered shots. Set to 0 to use the controller default.")]
        private float m_DialogueHookShotDurationSeconds = 0f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Cooldown between auto-triggered dialogue shots to reduce camera thrash.")]
        private float m_DialogueHookCooldownSeconds = 0.35f;

        [SerializeField]
        private List<ShotBinding> m_Shots = new List<ShotBinding>();

        private readonly Dictionary<GameObject, bool> m_PreShotActiveStates =
            new Dictionary<GameObject, bool>();
        private Coroutine m_ActiveShotRoutine;
        private string m_ActiveShotId = string.Empty;
        private bool m_HadPreShotSnapshot;
        private bool m_PreShotCameraEnabled;
        private bool m_HadPreShotVirtualCameraPriority;
        private int m_PreShotVirtualCameraPriority;
        private int m_LastHandledDialogueRequestId = int.MinValue;
        private float m_LastDialogueHookAtRealtime = -100f;
        private bool m_DialogueHookSubscribed;

        public string ActiveShotId => m_ActiveShotId;

        public string[] GetAvailableShotIds()
        {
            EnsureSetup();
            if (m_Shots == null || m_Shots.Count == 0)
            {
                return Array.Empty<string>();
            }

            var results = new List<string>(m_Shots.Count);
            for (int i = 0; i < m_Shots.Count; i++)
            {
                ShotBinding shot = m_Shots[i];
                if (shot == null || string.IsNullOrWhiteSpace(shot.ShotId))
                {
                    continue;
                }

                results.Add(shot.ShotId.Trim());
            }

            return results.ToArray();
        }

        public bool TryPlayShot(
            string shotId,
            float durationSeconds = -1f,
            bool? enableShowcaseCamera = null,
            bool? isolateNpcPolish = null
        )
        {
            EnsureSetup();
            if (!TryGetShotBinding(shotId, out ShotBinding binding))
            {
                Warn($"Showcase shot '{shotId}' not found.");
                return false;
            }

            StopActiveShot(restoreState: true);
            CapturePreShotState();

            bool useShowcaseCamera = enableShowcaseCamera ?? m_EnableShowcaseCameraDuringShot;
            bool isolate = isolateNpcPolish ?? m_IsolateNpcPolishDuringShot;

            if (binding.Anchor != null)
            {
                ApplyAnchorToShowcaseCamera(binding.Anchor, useShowcaseCamera);
            }
            else
            {
                Warn($"Shot '{binding.ShotId}' has no anchor assigned.");
            }

            if (isolate)
            {
                ApplyPolishIsolation(binding);
            }
            else
            {
                EnsureAllBoundPolishObjectsActive();
            }

            m_ActiveShotId = binding.ShotId ?? string.Empty;

            float resolvedDuration = durationSeconds >= 0f ? durationSeconds : m_DefaultShotDurationSeconds;
            if (resolvedDuration > 0f)
            {
                m_ActiveShotRoutine = StartCoroutine(RestoreAfterDelay(resolvedDuration));
            }

            Info(
                $"Showcase shot '{m_ActiveShotId}' applied (duration={(resolvedDuration > 0f ? resolvedDuration.ToString("F2") : "manual")}s, camera={useShowcaseCamera}, isolate={isolate})."
            );
            return true;
        }

        public void StopActiveShot(bool restoreState = true)
        {
            if (m_ActiveShotRoutine != null)
            {
                StopCoroutine(m_ActiveShotRoutine);
                m_ActiveShotRoutine = null;
            }

            if (restoreState)
            {
                RestorePreShotState();
            }

            m_ActiveShotId = string.Empty;
        }

        public bool TrySnapShowcaseCameraToShot(string shotId, bool enableShowcaseCamera = false)
        {
            EnsureSetup();
            if (!TryGetShotBinding(shotId, out ShotBinding binding))
            {
                Warn($"Showcase shot '{shotId}' not found.");
                return false;
            }

            if (binding.Anchor == null)
            {
                Warn($"Shot '{binding.ShotId}' has no anchor assigned.");
                return false;
            }

            ApplyAnchorToShowcaseCamera(binding.Anchor, enableShowcaseCamera);
            return true;
        }

        [ContextMenu("Showcase/Play Wide Shot")]
        private void ContextPlayWideShot() => TryPlayShot("wide");

        [ContextMenu("Showcase/Play Storm Shot")]
        private void ContextPlayStormShot() => TryPlayShot("storm");

        [ContextMenu("Showcase/Play Forge Shot")]
        private void ContextPlayForgeShot() => TryPlayShot("forge");

        [ContextMenu("Showcase/Play Archivist Shot")]
        private void ContextPlayArchivistShot() => TryPlayShot("archivist");

        [ContextMenu("Showcase/Stop Active Shot")]
        private void ContextStopActiveShot() => StopActiveShot(restoreState: true);

        [ContextMenu("Showcase/Rebuild Bindings")]
        private void ContextRebuildBindings()
        {
            AutoDiscoverReferences();
            RebuildDefaultBindings();
        }

        private void Reset()
        {
            AutoDiscoverReferences();
            RebuildDefaultBindings();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                AutoDiscoverReferences();
                if (m_AutoRebuildBindingsWhenMissing && (m_Shots == null || m_Shots.Count == 0))
                {
                    RebuildDefaultBindings();
                }
            }
        }

        private void Awake()
        {
            if (m_AutoDiscoverOnAwake)
            {
                EnsureSetup();
            }

            RefreshDialogueHookSubscription();
        }

        private void OnEnable()
        {
            RefreshDialogueHookSubscription();
        }

        private void OnDisable()
        {
            RefreshDialogueHookSubscription(forceUnsubscribe: true);
            StopActiveShot(restoreState: true);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // Enter Play Mode options can skip domain/scene reload, so keep the hook subscription healthy.
            RefreshDialogueHookSubscription();
        }

        private void RefreshDialogueHookSubscription(bool forceUnsubscribe = false)
        {
            bool shouldSubscribe =
                !forceUnsubscribe
                && isActiveAndEnabled
                && Application.isPlaying
                && m_EnableDialogueResponseShotHook;

            if (shouldSubscribe)
            {
                if (!m_DialogueHookSubscribed)
                {
                    // Defensive de-dupe in case a prior play session left the static event subscribed.
                    NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
                    NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
                    m_DialogueHookSubscribed = true;

                    if (m_LogDebug)
                    {
                        Debug.Log("[MCPShowcasePolish] Dialogue hook subscribed.", this);
                    }
                }

                return;
            }

            if (m_DialogueHookSubscribed)
            {
                NetworkDialogueService.OnDialogueResponse -= HandleDialogueResponse;
                m_DialogueHookSubscribed = false;

                if (m_LogDebug)
                {
                    Debug.Log("[MCPShowcasePolish] Dialogue hook unsubscribed.", this);
                }
            }
        }

        private void EnsureSetup()
        {
            AutoDiscoverReferences();
            if (m_AutoRebuildBindingsWhenMissing && (m_Shots == null || m_Shots.Count == 0))
            {
                RebuildDefaultBindings();
            }
        }

        private void AutoDiscoverReferences()
        {
            if (m_ShowcaseCamera == null)
            {
                Transform cameraTransform = transform.Find("MCP_ShowcaseCamera");
                if (cameraTransform != null)
                {
                    m_ShowcaseCamera = cameraTransform.GetComponent<Camera>();
                }
            }

            if (m_ShowcaseVirtualCamera == null)
            {
                Transform cameraTransform = transform.Find("MCP_ShowcaseCamera");
                if (cameraTransform != null)
                {
                    m_ShowcaseVirtualCamera =
                        cameraTransform.GetComponent<CinemachineVirtualCameraBase>();

                    if (
                        m_ShowcaseVirtualCamera == null
                        && Application.isPlaying
                        && m_AutoCreateShowcaseVirtualCameraInPlayMode
                    )
                    {
                        var created = cameraTransform.gameObject.AddComponent<CinemachineCamera>();
                        if (created != null)
                        {
                            m_ShowcaseVirtualCamera = created;
                            if (m_LogDebug)
                            {
                                Debug.Log(
                                    "[MCPShowcasePolish] Auto-added CinemachineCamera to MCP_ShowcaseCamera for showcase shot blending.",
                                    this
                                );
                            }
                        }
                    }
                }
            }

            if (m_ShowcaseVirtualCamera == null)
            {
                m_ShowcaseVirtualCamera = GetComponentInChildren<CinemachineVirtualCameraBase>(true);
            }

            if (m_CameraAnchorsRoot == null)
            {
                m_CameraAnchorsRoot = transform.Find("MCP_ShowcaseCameraAnchors");
            }
        }

        private void RebuildDefaultBindings()
        {
            if (m_Shots == null)
            {
                m_Shots = new List<ShotBinding>(4);
            }
            else
            {
                m_Shots.Clear();
            }

            m_Shots.Add(BuildShot("wide", "Shot_Wide", null, null, null));
            m_Shots.Add(
                BuildShot(
                    "storm",
                    "Shot_Storm",
                    "StormOracle_KeyLight",
                    "StormOracle_Ring",
                    "StormOracle_Aura"
                )
            );
            m_Shots.Add(
                BuildShot(
                    "forge",
                    "Shot_Forge",
                    "ForgeKeeper_KeyLight",
                    "ForgeKeeper_Ring",
                    "ForgeKeeper_Aura"
                )
            );
            m_Shots.Add(
                BuildShot(
                    "archivist",
                    "Shot_Archivist",
                    "Archivist_KeyLight",
                    "Archivist_Ring",
                    "Archivist_Aura"
                )
            );
        }

        private ShotBinding BuildShot(
            string shotId,
            string anchorName,
            string lightName,
            string ringName,
            string auraName
        )
        {
            return new ShotBinding
            {
                ShotId = shotId,
                Anchor = FindAnchor(anchorName),
                KeyLight = FindChildObject(lightName),
                Ring = FindChildObject(ringName),
                Aura = FindChildObject(auraName),
            };
        }

        private Transform FindAnchor(string anchorName)
        {
            if (m_CameraAnchorsRoot == null || string.IsNullOrWhiteSpace(anchorName))
            {
                return null;
            }

            return m_CameraAnchorsRoot.Find(anchorName);
        }

        private GameObject FindChildObject(string childName)
        {
            if (string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            Transform child = transform.Find(childName);
            return child != null ? child.gameObject : null;
        }

        private bool TryGetShotBinding(string shotId, out ShotBinding binding)
        {
            binding = null;
            if (m_Shots == null || m_Shots.Count == 0)
            {
                return false;
            }

            string normalized = NormalizeShotId(shotId);
            for (int i = 0; i < m_Shots.Count; i++)
            {
                ShotBinding candidate = m_Shots[i];
                if (candidate == null)
                {
                    continue;
                }

                if (NormalizeShotId(candidate.ShotId) == normalized)
                {
                    binding = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeShotId(string shotId)
        {
            if (string.IsNullOrWhiteSpace(shotId))
            {
                return string.Empty;
            }

            string normalized = shotId.Trim().ToLowerInvariant();
            normalized = normalized.Replace("shot_", string.Empty);
            normalized = normalized.Replace("-", string.Empty);
            normalized = normalized.Replace("_", string.Empty);
            normalized = normalized.Replace(" ", string.Empty);
            return normalized;
        }

        private void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            if (!Application.isPlaying || !m_EnableDialogueResponseShotHook)
            {
                if (m_LogDebug && m_EnableDialogueResponseShotHook)
                {
                    Debug.Log(
                        "[MCPShowcasePolish] Dialogue hook ignored because application is not playing.",
                        this
                    );
                }
                return;
            }

            if (m_LogDebug)
            {
                Debug.Log(
                    $"[MCPShowcasePolish] Dialogue hook received response | requestId={response.RequestId} | status={response.Status} | speaker={response.Request.SpeakerNetworkId} | requester={response.Request.RequestingClientId} | userInitiated={response.Request.IsUserInitiated}",
                    this
                );
            }

            if (response.Status != NetworkDialogueService.DialogueStatus.Completed)
            {
                if (m_LogDebug)
                {
                    Debug.Log(
                        $"[MCPShowcasePolish] Dialogue hook skipped request {response.RequestId}: status is {response.Status} (requires Completed).",
                        this
                    );
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(response.ResponseText))
            {
                if (m_LogDebug)
                {
                    Debug.Log(
                        $"[MCPShowcasePolish] Dialogue hook skipped request {response.RequestId}: response text is empty.",
                        this
                    );
                }
                return;
            }

            if (m_DialogueHookUserInitiatedOnly && !response.Request.IsUserInitiated)
            {
                if (m_LogDebug)
                {
                    Debug.Log(
                        $"[MCPShowcasePolish] Dialogue hook skipped request {response.RequestId}: not user-initiated.",
                        this
                    );
                }
                return;
            }

            if (m_DialogueHookLocalRequesterOnly)
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening)
                {
                    if (m_LogDebug)
                    {
                        Debug.Log(
                            $"[MCPShowcasePolish] Dialogue hook skipped request {response.RequestId}: NetworkManager is unavailable or not listening.",
                            this
                        );
                    }
                    return;
                }

                ulong localClientId = manager.LocalClientId;
                if (response.Request.RequestingClientId != localClientId)
                {
                    if (m_LogDebug)
                    {
                        Debug.Log(
                            $"[MCPShowcasePolish] Dialogue hook skipped request {response.RequestId}: requester {response.Request.RequestingClientId} != local client {localClientId}.",
                            this
                        );
                    }
                    return;
                }
            }

            if (response.RequestId == m_LastHandledDialogueRequestId)
            {
                if (m_LogDebug)
                {
                    Debug.Log(
                        $"[MCPShowcasePolish] Dialogue hook skipped duplicate request {response.RequestId}.",
                        this
                    );
                }
                return;
            }

            if (
                m_DialogueHookCooldownSeconds > 0f
                && Time.realtimeSinceStartup - m_LastDialogueHookAtRealtime < m_DialogueHookCooldownSeconds
            )
            {
                if (m_LogDebug)
                {
                    Debug.Log(
                        $"[MCPShowcasePolish] Dialogue hook skipped by cooldown for request {response.RequestId}.",
                        this
                    );
                }
                return;
            }

            if (!TryResolveShotIdForDialogueResponse(response, out string shotId))
            {
                if (m_LogDebug)
                {
                    Debug.Log(
                        $"[MCPShowcasePolish] Dialogue hook skipped request {response.RequestId}: no matching shot resolved.",
                        this
                    );
                }
                return;
            }

            bool played = TryPlayShot(
                shotId,
                m_DialogueHookShotDurationSeconds > 0f ? m_DialogueHookShotDurationSeconds : -1f,
                m_DialogueHookEnableShowcaseCamera,
                m_DialogueHookIsolatePolish
            );

            if (played)
            {
                m_LastHandledDialogueRequestId = response.RequestId;
                m_LastDialogueHookAtRealtime = Time.realtimeSinceStartup;
                if (m_LogDebug)
                {
                    Debug.Log(
                        $"[MCPShowcasePolish] Dialogue hook triggered shot '{shotId}' from speaker {response.Request.SpeakerNetworkId} (request {response.RequestId}).",
                        this
                    );
                }
            }
            else if (m_LogDebug)
            {
                Debug.LogWarning(
                    $"[MCPShowcasePolish] Dialogue hook resolved shot '{shotId}' for request {response.RequestId}, but TryPlayShot returned false.",
                    this
                );
            }
        }

        private bool TryResolveShotIdForDialogueResponse(
            NetworkDialogueService.DialogueResponse response,
            out string shotId
        )
        {
            shotId = string.Empty;
            EnsureSetup();

            string[] availableShots = GetAvailableShotIds();
            if (availableShots == null || availableShots.Length == 0)
            {
                return false;
            }

            var candidateTokens = new List<string>(6);

            if (
                TryResolveSpeakerNpcActor(response.Request.SpeakerNetworkId, out NpcDialogueActor speakerActor)
                && speakerActor != null
            )
            {
                if (!string.IsNullOrWhiteSpace(speakerActor.ProfileId))
                {
                    candidateTokens.Add(speakerActor.ProfileId);
                }

                if (!string.IsNullOrWhiteSpace(speakerActor.name))
                {
                    candidateTokens.Add(speakerActor.name);
                }

                if (
                    speakerActor.Profile != null
                    && !string.IsNullOrWhiteSpace(speakerActor.Profile.DisplayName)
                )
                {
                    candidateTokens.Add(speakerActor.Profile.DisplayName);
                }
            }
            else
            {
                GameObject speakerObject = ResolveNetworkObjectGameObject(response.Request.SpeakerNetworkId);
                if (speakerObject != null)
                {
                    candidateTokens.Add(speakerObject.name);
                }
            }

            if (candidateTokens.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < availableShots.Length; i++)
            {
                string available = availableShots[i];
                string normalizedShot = NormalizeShotId(available);
                if (string.IsNullOrEmpty(normalizedShot) || normalizedShot == NormalizeShotId("wide"))
                {
                    continue;
                }

                for (int j = 0; j < candidateTokens.Count; j++)
                {
                    string token = NormalizeShotId(candidateTokens[j]);
                    if (string.IsNullOrEmpty(token))
                    {
                        continue;
                    }

                    if (token.Contains(normalizedShot) || normalizedShot.Contains(token))
                    {
                        shotId = available;
                        return true;
                    }
                }
            }

            // Known aliases for common NPC names/profile ids.
            for (int i = 0; i < candidateTokens.Count; i++)
            {
                string token = NormalizeShotId(candidateTokens[i]);
                if (token.Contains("storm"))
                {
                    shotId = "storm";
                    return true;
                }

                if (token.Contains("forge"))
                {
                    shotId = "forge";
                    return true;
                }

                if (token.Contains("archiv"))
                {
                    shotId = "archivist";
                    return true;
                }
            }

            if (m_LogDebug)
            {
                Debug.Log(
                    $"[MCPShowcasePolish] Dialogue hook found no shot mapping for speaker tokens: {string.Join(", ", candidateTokens)}",
                    this
                );
            }

            return false;
        }

        private static bool TryResolveSpeakerNpcActor(
            ulong speakerNetworkObjectId,
            out NpcDialogueActor actor
        )
        {
            actor = null;
            GameObject speakerObject = ResolveNetworkObjectGameObject(speakerNetworkObjectId);
            if (speakerObject == null)
            {
                return false;
            }

            actor = speakerObject.GetComponent<NpcDialogueActor>();
            if (actor == null)
            {
                actor = speakerObject.GetComponentInParent<NpcDialogueActor>();
            }
            if (actor == null)
            {
                actor = speakerObject.GetComponentInChildren<NpcDialogueActor>(true);
            }

            return actor != null;
        }

        private static GameObject ResolveNetworkObjectGameObject(ulong networkObjectId)
        {
            if (networkObjectId == 0)
            {
                return null;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.SpawnManager == null)
            {
                return null;
            }

            if (
                manager.SpawnManager.SpawnedObjects.TryGetValue(
                    networkObjectId,
                    out NetworkObject networkObject
                )
                && networkObject != null
            )
            {
                return networkObject.gameObject;
            }

            return null;
        }

        private bool ShouldUseCinemachineShowcaseCamera()
        {
            return m_PreferCinemachineForShots && m_ShowcaseVirtualCamera != null;
        }

        private void ApplyAnchorToShowcaseVirtualCamera(Transform anchor, bool makeLive)
        {
            if (m_ShowcaseVirtualCamera == null)
            {
                Warn("Showcase Cinemachine camera reference is missing.");
                return;
            }

            Transform vcamTransform = m_ShowcaseVirtualCamera.transform;
            vcamTransform.SetPositionAndRotation(anchor.position, anchor.rotation);

            if (m_ShowcaseVirtualCamera.gameObject != null && !m_ShowcaseVirtualCamera.gameObject.activeSelf)
            {
                m_ShowcaseVirtualCamera.gameObject.SetActive(true);
            }

            if (m_DisableRawShowcaseCameraWhenUsingCinemachine && m_ShowcaseCamera != null)
            {
                m_ShowcaseCamera.enabled = false;
            }

            if (makeLive)
            {
                int boostedPriority = GetBoostedShowcaseVirtualCameraPriority();
                SetVirtualCameraPriority(m_ShowcaseVirtualCamera, boostedPriority);
            }

            if (m_LogDebug)
            {
                Debug.Log(
                    $"[MCPShowcasePolish] Cinemachine showcase camera snapped to '{anchor.name}' (live={makeLive}, priority={GetVirtualCameraPriority(m_ShowcaseVirtualCamera)}).",
                    this
                );
            }
        }

        private int GetBoostedShowcaseVirtualCameraPriority()
        {
            int maxPriority = 0;
#if UNITY_2023_1_OR_NEWER
            CinemachineVirtualCameraBase[] all =
                FindObjectsByType<CinemachineVirtualCameraBase>(
                    FindObjectsInactive.Include
                );
#else
            CinemachineVirtualCameraBase[] all = FindObjectsOfType<CinemachineVirtualCameraBase>(true);
#endif
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    CinemachineVirtualCameraBase cam = all[i];
                    if (cam == null || cam == m_ShowcaseVirtualCamera)
                    {
                        continue;
                    }

                    int p = GetVirtualCameraPriority(cam);
                    if (p > maxPriority)
                    {
                        maxPriority = p;
                    }
                }
            }

            return maxPriority + Mathf.Max(1, m_CinemachinePriorityBoost);
        }

        private static int GetVirtualCameraPriority(CinemachineVirtualCameraBase camera)
        {
            if (camera == null)
            {
                return 0;
            }

            try
            {
                object value = camera.Priority;
                if (value is int intPriority)
                {
                    return intPriority;
                }

                if (value != null)
                {
                    var valueType = value.GetType();
                    var valueProperty = valueType.GetProperty("Value");
                    if (valueProperty != null && valueProperty.PropertyType == typeof(int))
                    {
                        return (int)valueProperty.GetValue(value);
                    }
                }
            }
            catch {}

            return 0;
        }

        private static void SetVirtualCameraPriority(CinemachineVirtualCameraBase camera, int priority)
        {
            if (camera == null)
            {
                return;
            }

            try
            {
                object value = camera.Priority;
                if (value is int)
                {
                    camera.Priority = priority;
                    return;
                }

                if (value != null)
                {
                    var valueType = value.GetType();
                    var valueProperty = valueType.GetProperty("Value");
                    if (valueProperty != null && valueProperty.PropertyType == typeof(int))
                    {
                        object boxed = value;
                        valueProperty.SetValue(boxed, priority);
                        camera.Priority = (PrioritySettings)boxed;
                    }
                }
            }
            catch {}
        }

        private void ApplyAnchorToShowcaseCamera(Transform anchor, bool enableShowcaseCamera)
        {
            if (anchor == null)
            {
                Warn("Showcase shot anchor is missing.");
                return;
            }

            if (ShouldUseCinemachineShowcaseCamera())
            {
                ApplyAnchorToShowcaseVirtualCamera(anchor, enableShowcaseCamera);
                return;
            }

            if (m_ShowcaseCamera == null)
            {
                Warn("Showcase camera reference is missing.");
                return;
            }

            Transform camTransform = m_ShowcaseCamera.transform;
            camTransform.SetPositionAndRotation(anchor.position, anchor.rotation);

            if (m_ShowcaseCamera.gameObject != null && !m_ShowcaseCamera.gameObject.activeSelf)
            {
                m_ShowcaseCamera.gameObject.SetActive(true);
            }

            if (enableShowcaseCamera)
            {
                m_ShowcaseCamera.enabled = true;
            }

            if (m_LogDebug)
            {
                Debug.Log(
                    $"[MCPShowcasePolish] Raw showcase camera snapped to '{anchor.name}' (enable={enableShowcaseCamera}).",
                    this
                );
            }
        }

        private void CapturePreShotState()
        {
            m_PreShotActiveStates.Clear();
            m_HadPreShotSnapshot = true;
            m_HadPreShotVirtualCameraPriority = false;

            if (m_ShowcaseCamera != null)
            {
                m_PreShotCameraEnabled = m_ShowcaseCamera.enabled;
                CacheActiveState(m_ShowcaseCamera.gameObject);
            }

            if (m_ShowcaseVirtualCamera != null)
            {
                m_PreShotVirtualCameraPriority = GetVirtualCameraPriority(m_ShowcaseVirtualCamera);
                m_HadPreShotVirtualCameraPriority = true;
                CacheActiveState(m_ShowcaseVirtualCamera.gameObject);
            }

            if (m_Shots == null)
            {
                return;
            }

            for (int i = 0; i < m_Shots.Count; i++)
            {
                ShotBinding shot = m_Shots[i];
                if (shot == null)
                {
                    continue;
                }

                foreach (GameObject go in shot.EnumeratePolishObjects())
                {
                    CacheActiveState(go);
                }
            }
        }

        private void CacheActiveState(GameObject go)
        {
            if (go == null || m_PreShotActiveStates.ContainsKey(go))
            {
                return;
            }

            m_PreShotActiveStates.Add(go, go.activeSelf);
        }

        private void ApplyPolishIsolation(ShotBinding selectedShot)
        {
            if (m_Shots == null)
            {
                return;
            }

            var selected = new HashSet<GameObject>();
            if (selectedShot != null)
            {
                foreach (GameObject go in selectedShot.EnumeratePolishObjects())
                {
                    if (go != null)
                    {
                        selected.Add(go);
                    }
                }
            }

            for (int i = 0; i < m_Shots.Count; i++)
            {
                ShotBinding shot = m_Shots[i];
                if (shot == null)
                {
                    continue;
                }

                foreach (GameObject go in shot.EnumeratePolishObjects())
                {
                    if (go == null)
                    {
                        continue;
                    }

                    bool shouldEnable = selectedShot == null || selected.Contains(go);
                    if (go.activeSelf != shouldEnable)
                    {
                        go.SetActive(shouldEnable);
                    }
                }
            }
        }

        private void EnsureAllBoundPolishObjectsActive()
        {
            if (m_Shots == null)
            {
                return;
            }

            for (int i = 0; i < m_Shots.Count; i++)
            {
                ShotBinding shot = m_Shots[i];
                if (shot == null)
                {
                    continue;
                }

                foreach (GameObject go in shot.EnumeratePolishObjects())
                {
                    if (go != null && !go.activeSelf)
                    {
                        go.SetActive(true);
                    }
                }
            }
        }

        private IEnumerator RestoreAfterDelay(float delaySeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, delaySeconds));
            m_ActiveShotRoutine = null;
            StopActiveShot(restoreState: true);
        }

        private void RestorePreShotState()
        {
            if (!m_HadPreShotSnapshot)
            {
                return;
            }

            foreach (KeyValuePair<GameObject, bool> kv in m_PreShotActiveStates)
            {
                if (kv.Key != null)
                {
                    kv.Key.SetActive(kv.Value);
                }
            }

            if (m_ShowcaseCamera != null)
            {
                m_ShowcaseCamera.enabled = m_PreShotCameraEnabled;
            }

            if (m_ShowcaseVirtualCamera != null && m_HadPreShotVirtualCameraPriority)
            {
                SetVirtualCameraPriority(m_ShowcaseVirtualCamera, m_PreShotVirtualCameraPriority);
            }

            m_PreShotActiveStates.Clear();
            m_HadPreShotSnapshot = false;
            m_HadPreShotVirtualCameraPriority = false;
        }

        private void Warn(string message)
        {
            Debug.LogWarning("[MCPShowcasePolish] " + message, this);
        }

        private void Info(string message)
        {
            if (m_LogDebug)
            {
                Debug.Log("[MCPShowcasePolish] " + message, this);
            }
        }
    }
}
