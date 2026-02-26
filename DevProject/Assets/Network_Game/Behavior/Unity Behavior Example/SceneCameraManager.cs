using System.Collections;
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

            CinemachineVirtualCameraBase cmCamera = ResolveActiveVirtualCamera(brain);
            if (cmCamera == null)
            {
                NGLog.Warn("CameraManager", "No active Cinemachine virtual camera found.");
                return false;
            }

            // Normalize blend hints
            if (
                cmCamera is CinemachineCamera cinemachineCamera
                && (int)cinemachineCamera.BlendHint < 0
            )
            {
                cinemachineCamera.BlendHint = (CinemachineCore.BlendHints) 0;
                NGLog.Warn("CameraManager", $"Normalized invalid blend hint on '{cmCamera.name}'");
            }

            // Safeguard against asset targets
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
            }

            Transform cameraRoot = FindCameraTarget(player);
            if (cameraRoot == null)
            {
                NGLog.Warn("CameraManager", "Player camera target not found.");
                return false;
            }

            bool changed = false;
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

            return changed || (cmCamera.Follow == cameraRoot);
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
                    if (child.CompareTag("CinemachineTarget"))
                        return child;
                }
            }
            return cameraRoot;
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

                // In multiplayer, avoid binding camera/input to another client's tagged player
                // while waiting for the local player object to spawn.
                if (manager.IsListening)
                {
                    return null;
                }
            }

            return GameObject.FindGameObjectWithTag("Player");
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
