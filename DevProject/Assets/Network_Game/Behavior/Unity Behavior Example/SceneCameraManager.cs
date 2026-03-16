using System;
using System.Collections;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Manages Cinemachine camera target binding and health monitoring.
    /// Extracted from BehaviorSceneBootstrap for modularity.
    /// </summary>
    public class SceneCameraManager : MonoBehaviour
    {
        [Header("Monitoring Settings")]
        [SerializeField]
        private bool m_EnableContinuousCameraRebind = true;

        [SerializeField]
        [Min(0.1f)]
        private float m_ContinuousCameraRebindIntervalSeconds = 0.75f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds after first camera sample before drift detection activates.")]
        private float m_CameraDriftGraceSeconds = 3f;

        private Coroutine m_CameraRebindMonitorRoutine;
        private Vector3 m_LastCameraSamplePlayerPos;
        private Vector3 m_LastCameraSampleCameraPos;
        private bool m_HasCameraMotionSample;
        private float m_CameraDriftGraceDeadline;

        public void StartMonitoring(NetworkManager manager)
        {
            if (!m_EnableContinuousCameraRebind)
                return;

            StopMonitoring();
            m_CameraRebindMonitorRoutine = StartCoroutine(MonitorCameraBinding(manager));
        }

        public void StopMonitoring()
        {
            if (m_CameraRebindMonitorRoutine != null)
            {
                StopCoroutine(m_CameraRebindMonitorRoutine);
                m_CameraRebindMonitorRoutine = null;
            }
        }

        public bool ConfigureCamera(GameObject player)
        {
            if (player == null)
                return false;

            var mainCamera = Camera.main;
            CinemachineBrain brain =
                mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;

            if (mainCamera == null || brain == null || !brain.enabled)
            {
#if UNITY_2023_1_OR_NEWER
                brain = FindAnyObjectByType<CinemachineBrain>();
#else
                brain = FindAnyObjectByType<CinemachineBrain>();
#endif
                if (brain != null)
                    mainCamera = brain.GetComponent<Camera>();
            }

            if (mainCamera == null || brain == null || !brain.enabled)
            {
                NGLog.Warn("CameraManager", "MainCamera or active CinemachineBrain not found.");
                return false;
            }

            Transform cameraRoot = FindCameraTarget(player);
            if (cameraRoot == null)
            {
                NGLog.Warn("CameraManager", "Player camera target not found.");
                return false;
            }

            List<CinemachineVirtualCameraBase> virtualCameras = ResolveCandidateVirtualCameras(
                brain,
                player.transform,
                cameraRoot
            );
            if (virtualCameras.Count == 0)
            {
                NGLog.Warn("CameraManager", "No active Cinemachine virtual camera found.");
                return false;
            }

            bool changed = false;
            bool bound = false;
            foreach (CinemachineVirtualCameraBase cmCamera in virtualCameras)
            {
                changed |= NormalizeAndSanitizeCamera(cmCamera);

                if (cmCamera.Follow != cameraRoot)
                {
                    NGLog.Info(
                        "CameraManager",
                        $"Assigning Follow target '{cameraRoot.name}' to '{cmCamera.name}'"
                    );
                    cmCamera.Follow = cameraRoot;
                    changed = true;
                }

                if (cmCamera.LookAt != cameraRoot)
                {
                    cmCamera.LookAt = cameraRoot;
                    changed = true;
                }

                if (cmCamera.Follow == cameraRoot)
                {
                    bound = true;
                }
            }

            return changed || bound;
        }

        private Transform FindCameraTarget(GameObject player)
        {
            Transform cameraRoot = player.transform.Find("PlayerCameraRoot");
            if (cameraRoot == null)
            {
                var controller =
                    player.GetComponent<Network_Game.ThirdPersonController.ThirdPersonController>();
                if (controller != null && controller.CinemachineCameraTarget != null)
                {
                    cameraRoot = controller.CinemachineCameraTarget.transform;
                }
            }

            if (cameraRoot == null)
            {
                Transform[] children = player.GetComponentsInChildren<Transform>(true);
                foreach (var child in children)
                {
                    if (child.name == "PlayerCameraRoot")
                        return child;

                    if (child.CompareTag("CinemachineTarget"))
                        return child;
                }
            }
            return cameraRoot;
        }

        private bool NormalizeAndSanitizeCamera(CinemachineVirtualCameraBase cmCamera)
        {
            bool changed = false;

            if (
                cmCamera is CinemachineCamera cinemachineCamera
                && (int)cinemachineCamera.BlendHint < 0
            )
            {
                cinemachineCamera.BlendHint = (CinemachineCore.BlendHints) 0;
                NGLog.Warn("CameraManager", $"Normalized invalid blend hint on '{cmCamera.name}'");
                changed = true;
            }

            if (
                cmCamera.Follow != null
                && string.IsNullOrEmpty(cmCamera.Follow.gameObject.scene.name)
            )
            {
                NGLog.Warn(
                    "CameraManager",
                    $"Camera '{cmCamera.name}' Follow target is an asset. Clearing."
                );
                cmCamera.Follow = null;
                changed = true;
            }

            if (
                cmCamera.LookAt != null
                && string.IsNullOrEmpty(cmCamera.LookAt.gameObject.scene.name)
            )
            {
                NGLog.Warn(
                    "CameraManager",
                    $"Camera '{cmCamera.name}' LookAt target is an asset. Clearing."
                );
                cmCamera.LookAt = null;
                changed = true;
            }

            return changed;
        }

        private List<CinemachineVirtualCameraBase> ResolveCandidateVirtualCameras(
            CinemachineBrain brain,
            Transform playerRoot,
            Transform cameraRoot
        )
        {
            var result = new List<CinemachineVirtualCameraBase>();
            CinemachineVirtualCameraBase activeCamera = ResolveActiveVirtualCamera(brain);
            if (activeCamera != null)
            {
                result.Add(activeCamera);
            }

#if UNITY_2023_1_OR_NEWER
            var virtualCameras = FindObjectsByType<CinemachineVirtualCameraBase>(
                FindObjectsInactive.Include
            );
#else
            var virtualCameras = FindObjectsByType<CinemachineVirtualCameraBase>(
                FindObjectsInactive.Include
            );
#endif
            foreach (CinemachineVirtualCameraBase vcam in virtualCameras)
            {
                if (
                    vcam == null
                    || !vcam.isActiveAndEnabled
                    || !vcam.gameObject.activeInHierarchy
                    || result.Contains(vcam)
                    || !IsPlayerCameraCandidate(vcam, playerRoot, cameraRoot)
                )
                {
                    continue;
                }

                result.Add(vcam);
            }

            return result;
        }

        private static bool IsPlayerCameraCandidate(
            CinemachineVirtualCameraBase camera,
            Transform playerRoot,
            Transform cameraRoot
        )
        {
            if (camera == null || playerRoot == null)
            {
                return false;
            }

            if (TargetsPlayer(camera, playerRoot, cameraRoot))
            {
                return true;
            }

            Transform explicitTarget = camera.Follow != null ? camera.Follow : camera.LookAt;
            if (explicitTarget != null)
            {
                return false;
            }

            return LooksLikePlayerCamera(camera.name);
        }

        private static bool TargetsPlayer(
            CinemachineVirtualCameraBase camera,
            Transform playerRoot,
            Transform cameraRoot
        )
        {
            return IsPlayerTarget(camera != null ? camera.Follow : null, playerRoot, cameraRoot)
                || IsPlayerTarget(camera != null ? camera.LookAt : null, playerRoot, cameraRoot);
        }

        private static bool IsPlayerTarget(Transform target, Transform playerRoot, Transform cameraRoot)
        {
            if (target == null || playerRoot == null)
            {
                return false;
            }

            if (target == playerRoot || target.IsChildOf(playerRoot) || playerRoot.IsChildOf(target))
            {
                return true;
            }

            if (cameraRoot == null)
            {
                return false;
            }

            return target == cameraRoot || target.IsChildOf(cameraRoot) || cameraRoot.IsChildOf(target);
        }

        private static bool LooksLikePlayerCamera(string cameraName)
        {
            return !string.IsNullOrWhiteSpace(cameraName)
                && cameraName.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private CinemachineVirtualCameraBase ResolveActiveVirtualCamera(CinemachineBrain brain)
        {
            if (brain != null)
            {
                ICinemachineCamera activeCamera = brain.ActiveVirtualCamera;
                if (
                    activeCamera is CinemachineVirtualCameraBase vcam
                    && vcam != null
                    && vcam.isActiveAndEnabled
                )
                {
                    return vcam;
                }
            }

#if UNITY_2023_1_OR_NEWER
            var virtualCameras = FindObjectsByType<CinemachineVirtualCameraBase>(
                FindObjectsInactive.Include
            );
#else
            var virtualCameras = FindObjectsByType<CinemachineVirtualCameraBase>(
                FindObjectsInactive.Include
            );
#endif
            foreach (var vcam in virtualCameras)
            {
                if (vcam != null && vcam.isActiveAndEnabled && vcam.gameObject.activeInHierarchy)
                    return vcam;
            }

            return virtualCameras.Length > 0 ? virtualCameras[0] : null;
        }

        private IEnumerator MonitorCameraBinding(NetworkManager manager)
        {
            float interval = Mathf.Max(0.1f, m_ContinuousCameraRebindIntervalSeconds);
            while (enabled)
            {
                GameObject player = ResolveLocalPlayer(manager);
                if (player != null)
                {
                    ConfigureCamera(player);
                    DetectCameraFollowDrift(player);
                }
                yield return new WaitForSeconds(interval);
            }
        }

        private void DetectCameraFollowDrift(GameObject player)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            Vector3 playerPos = player.transform.position;
            Vector3 cameraPos = mainCamera.transform.position;

            if (!m_HasCameraMotionSample)
            {
                m_LastCameraSamplePlayerPos = playerPos;
                m_LastCameraSampleCameraPos = cameraPos;
                m_HasCameraMotionSample = true;
                m_CameraDriftGraceDeadline = Time.realtimeSinceStartup + m_CameraDriftGraceSeconds;
                return;
            }

            float playerDelta = Vector3.Distance(playerPos, m_LastCameraSamplePlayerPos);
            float cameraDelta = Vector3.Distance(cameraPos, m_LastCameraSampleCameraPos);

            if (
                playerDelta > 0.15f
                && cameraDelta < 0.02f
                && Time.realtimeSinceStartup >= m_CameraDriftGraceDeadline
            )
            {
                NGLog.Warn(
                    "CameraManager",
                    $"Camera drift detected! Player moved {playerDelta:F3}, Camera moved {cameraDelta:F3}"
                );
            }

            m_LastCameraSamplePlayerPos = playerPos;
            m_LastCameraSampleCameraPos = cameraPos;
        }

        private GameObject ResolveLocalPlayer(NetworkManager manager)
        {
            if (manager != null)
            {
                GameObject localPlayer = manager.LocalClient?.PlayerObject?.gameObject;
                if (localPlayer != null)
                {
                    return localPlayer;
                }

                GameObject ownedTaggedPlayer = ResolveTaggedPlayerCandidate(manager, "Player");
                if (ownedTaggedPlayer != null)
                {
                    return ownedTaggedPlayer;
                }

                // In multiplayer, avoid binding camera/input to another client's tagged player
                // while waiting for the local player object to spawn.
                if (manager.IsListening)
                {
                    return null;
                }
            }

            return ResolveTaggedPlayerCandidate(null, "Player");
        }

        private static GameObject ResolveTaggedPlayerCandidate(NetworkManager manager, string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            GameObject[] candidates = GameObject.FindGameObjectsWithTag(tagName);
            if (candidates == null || candidates.Length == 0)
            {
                return null;
            }

            if (manager != null)
            {
                ulong localClientId = manager.LocalClientId;
                foreach (GameObject candidate in candidates)
                {
                    if (candidate == null || !candidate.activeInHierarchy)
                    {
                        continue;
                    }

                    NetworkObject networkObject = candidate.GetComponent<NetworkObject>();
                    if (
                        networkObject != null
                        && networkObject.IsSpawned
                        && networkObject.OwnerClientId == localClientId
                    )
                    {
                        return candidate;
                    }
                }
            }

            if (candidates.Length == 1 && candidates[0] != null && candidates[0].activeInHierarchy)
            {
                return candidates[0];
            }

            return null;
        }

        [ContextMenu("Log Camera Binding Snapshot")]
        public void LogCameraBindingSnapshot()
        {
            NetworkManager manager = NetworkManager.Singleton;
            GameObject player = ResolveLocalPlayer(manager);
            Camera mainCamera = Camera.main;
            CinemachineBrain brain =
                mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;
            CinemachineVirtualCameraBase activeVcam = ResolveActiveVirtualCamera(brain);

            Transform cameraRoot = player != null ? FindCameraTarget(player) : null;

            NGLog.Info(
                "CameraManager",
                NGLog.Format(
                    "Snapshot",
                    ("player", player != null ? player.name : "null"),
                    ("vcam", activeVcam != null ? activeVcam.name : "null"),
                    ("follow", activeVcam?.Follow != null ? activeVcam.Follow.name : "null")
                )
            );
        }
    }
}
