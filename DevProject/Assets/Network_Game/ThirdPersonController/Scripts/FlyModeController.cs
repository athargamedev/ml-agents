using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Network_Game.ThirdPersonController
{
    [RequireComponent(typeof(StarterAssetsInputs))]
    public class FlyModeController : MonoBehaviour
    {
        /// <summary>
        /// In multiplayer, only the owning client should process fly input.
        /// Cached on Awake to avoid per-frame GetComponent.
        /// </summary>
        private NetworkObject m_NetworkObject;

        // NOTE: ownership is NOT cached permanently — NGO supports authority transfer,
        // so we re-evaluate IsOwner each frame after spawn. We only skip the GetComponent
        // lookup by caching the NetworkObject reference itself.
        private bool m_IsLocalOwner = true; // default true for non-networked use

        [Header("Fly Mode")]
        [SerializeField]
        private KeyCode m_ToggleKey = KeyCode.F;

        [Header("Fly Speeds")]
        [SerializeField]
        [FormerlySerializedAs("flySpeed")]
        [FormerlySerializedAs("m_HorizontalSpeed")]
        [Min(0.1f)]
        private float m_CruiseSpeed = 9f;

        [SerializeField]
        [FormerlySerializedAs("verticalSpeed")]
        [Min(0.1f)]
        private float m_VerticalSpeed = 7f;

        [SerializeField]
        [Min(0f)]
        private float m_TakeoffImpulse = 4f;

        [SerializeField]
        [Min(0f)]
        private float m_GroundLiftAssist = 2.5f;

        [SerializeField]
        [Min(0.1f)]
        private float m_BoostMultiplier = 1.8f;

        [SerializeField]
        [Range(0.05f, 1f)]
        private float m_PrecisionMultiplier = 0.45f;

        [Header("Fly Feel")]
        [SerializeField]
        [Min(0.01f)]
        private float m_AccelerationTime = 0.10f;

        [SerializeField]
        [Min(0.01f)]
        private float m_DecelerationTime = 0.14f;

        [SerializeField]
        [Min(0.01f)]
        private float m_YawSmoothTime = 0.06f;

        [Header("Orientation")]
        [SerializeField]
        [FormerlySerializedAs("rotateWithCamera")]
        private bool m_RotateWithCamera = true;

        [SerializeField]
        private bool m_UseCameraPitchForForward = true;

        [Header("Advanced")]
        [SerializeField]
        private bool m_ApplyBoostToVertical = true;

        [SerializeField]
        private bool m_ApplyPrecisionToVertical = true;

        [SerializeField]
        [FormerlySerializedAs("debugLog")]
        private bool m_LogDebug;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Ignore fly toggle input briefly after enable/spawn to avoid startup key bleed.")]
        private float m_ToggleStartupGuardSeconds = 0.75f;

        public bool IsFlying => m_IsFlying;
        public bool ShouldAlignYawWithCamera => m_RotateWithCamera;
        public float YawSmoothTime => m_YawSmoothTime;
        public System.Action<bool> OnFlyModeChanged;

        private bool m_IsFlying;
        private Vector3 m_CurrentVelocity;
        private Vector3 m_VelocityDampRef;
        private StarterAssetsInputs m_Inputs;
        private Transform m_CameraTransform;
        private bool m_IsGroundedHint;
        private float m_ToggleGuardUntil;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput m_PlayerInput;
        private InputAction m_ToggleFlyAction;
        private InputAction m_FlyVerticalAction;
        private InputAction m_FlyBoostAction;
        private InputAction m_FlyPrecisionAction;
        private InputAction m_MoveAction;
#endif

        private void Awake()
        {
            m_Inputs = GetComponent<StarterAssetsInputs>();
            m_NetworkObject = GetComponent<NetworkObject>();
            SanitizeLegacyTuning();
            ResolveCameraTransform();

#if ENABLE_INPUT_SYSTEM
            m_PlayerInput = GetComponent<PlayerInput>();
            CacheOptionalActions();
#endif
        }

        private void OnEnable()
        {
            // Prevent stale state when domain reload is disabled between play sessions.
            if (m_IsFlying)
            {
                SetFlyMode(false);
            }
            m_CurrentVelocity = Vector3.zero;
            m_VelocityDampRef = Vector3.zero;
            m_ToggleGuardUntil = Time.unscaledTime + Mathf.Max(0f, m_ToggleStartupGuardSeconds);

#if ENABLE_INPUT_SYSTEM
            CacheOptionalActions();
#endif
        }

        private void OnDisable()
        {
            if (m_IsFlying)
            {
                SetFlyMode(false);
            }
        }

        private void OnDestroy()
        {
            OnFlyModeChanged = null;
        }

        private void Update()
        {
            // In multiplayer, only the owning client should process fly input.
            if (!CheckLocalOwnership())
            {
                return;
            }

            if (Time.unscaledTime >= m_ToggleGuardUntil && WasTogglePressed())
            {
                SetFlyMode(!m_IsFlying);
            }

            if (!m_IsFlying)
            {
                return;
            }

            ResolveCameraTransform();
            UpdateFlyMovement();
        }

        /// <summary>
        /// Returns true if this is a non-networked object or the local owner.
        /// Re-evaluates every frame to support NGO authority transfer.
        /// Only the NetworkObject component reference is cached (avoids GetComponent allocation).
        /// </summary>
        private bool CheckLocalOwnership()
        {
            if (m_NetworkObject == null)
            {
                m_NetworkObject = GetComponent<NetworkObject>();
            }

            if (m_NetworkObject == null || !m_NetworkObject.IsSpawned)
            {
                // Not networked or not yet spawned — allow (single player / pre-spawn)
                m_IsLocalOwner = true;
                return true;
            }

            m_IsLocalOwner = m_NetworkObject.IsOwner;
            return m_IsLocalOwner;
        }

        public void SetFlyMode(bool enabled)
        {
            if (m_IsFlying == enabled)
            {
                return;
            }

            m_IsFlying = enabled;
            m_VelocityDampRef = Vector3.zero;

            Vector3 resetVelocity = Vector3.zero;
            if (enabled)
            {
                float takeoff = m_TakeoffImpulse > 0f ? m_TakeoffImpulse : 3f;
                resetVelocity = new Vector3(0f, takeoff, 0f);
            }
            m_CurrentVelocity = resetVelocity;

            ApplyCursorStateForMode();
            OnFlyModeChanged?.Invoke(m_IsFlying);

            if (m_LogDebug)
            {
                UnityEngine.Debug.Log($"[FlyMode] Active={m_IsFlying}");
            }
        }

        public void ToggleFlyMode()
        {
            SetFlyMode(!m_IsFlying);
        }

        public void SetGroundedHint(bool grounded)
        {
            m_IsGroundedHint = grounded;
        }

        public Vector3 GetFlyVelocity()
        {
            return m_CurrentVelocity;
        }

        private void UpdateFlyMovement()
        {
            Vector2 moveInput = GetMoveInput();
            float verticalInput = GetVerticalInput();
            bool isBoosting = IsBoostPressed();
            bool isPrecision = IsPrecisionPressed();
            float horizontalSpeed = GetHorizontalSpeed(isBoosting, isPrecision);
            float verticalSpeed = GetVerticalSpeed(isBoosting, isPrecision);

            Vector3 forwardBasis;
            Vector3 rightBasis;
            ResolveMovementBasis(out forwardBasis, out rightBasis);

            Vector3 desiredPlanar = forwardBasis * moveInput.y + rightBasis * moveInput.x;
            if (desiredPlanar.sqrMagnitude > 1f)
            {
                desiredPlanar.Normalize();
            }

            float planarMagnitude = Mathf.Clamp01(moveInput.magnitude);
            Vector3 targetVelocity = Vector3.zero;
            if (desiredPlanar.sqrMagnitude > 0.0001f)
            {
                targetVelocity += desiredPlanar.normalized * (horizontalSpeed * planarMagnitude);
            }

            targetVelocity += Vector3.up * (verticalInput * verticalSpeed);

            // Ensure fly mode breaks floor contact quickly even with stale legacy tuning data.
            if (m_IsGroundedHint && targetVelocity.y <= 0f)
            {
                targetVelocity.y = Mathf.Max(targetVelocity.y, m_GroundLiftAssist);
            }

            bool hasInput = targetVelocity.sqrMagnitude > 0.0001f;
            float smoothTime = hasInput ? m_AccelerationTime : m_DecelerationTime;
            smoothTime = Mathf.Max(0.01f, smoothTime);

            m_CurrentVelocity = Vector3.SmoothDamp(
                m_CurrentVelocity,
                targetVelocity,
                ref m_VelocityDampRef,
                smoothTime,
                Mathf.Infinity,
                Time.deltaTime
            );

            if (!hasInput && m_CurrentVelocity.sqrMagnitude < 0.0004f)
            {
                m_CurrentVelocity = Vector3.zero;
            }
        }

        private Vector2 GetMoveInput()
        {
            // Primary source: StarterAssetsInputs — already populated by PlayerInput via SendMessage.
            if (m_Inputs != null && m_Inputs.move.sqrMagnitude > 0.0001f)
            {
                return Vector2.ClampMagnitude(m_Inputs.move, 1f);
            }

#if ENABLE_INPUT_SYSTEM
            // Fallback: explicit InputAction (e.g. when StarterAssetsInputs is absent).
            if (m_MoveAction != null && m_MoveAction.enabled)
            {
                Vector2 actionMove = m_MoveAction.ReadValue<Vector2>();
                if (actionMove.sqrMagnitude > 0.0001f)
                {
                    return Vector2.ClampMagnitude(actionMove, 1f);
                }
            }
#endif
            return Vector2.zero;
        }

        private float GetVerticalInput()
        {
            float vertical = 0f;

#if ENABLE_INPUT_SYSTEM
            if (m_FlyVerticalAction != null && m_FlyVerticalAction.enabled)
            {
                vertical = Mathf.Clamp(m_FlyVerticalAction.ReadValue<float>(), -1f, 1f);
                if (Mathf.Abs(vertical) > 0.001f)
                {
                    return vertical;
                }
            }

            if (m_Inputs != null && m_Inputs.jump)
            {
                vertical += 1f;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.eKey.isPressed || keyboard.spaceKey.isPressed)
                {
                    vertical += 1f;
                }
                if (
                    keyboard.qKey.isPressed
                    || keyboard.cKey.isPressed
                    || keyboard.leftCtrlKey.isPressed
                )
                {
                    vertical -= 1f;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.rightShoulder.isPressed)
                {
                    vertical += 1f;
                }
                if (gamepad.leftShoulder.isPressed)
                {
                    vertical -= 1f;
                }
            }
#else
            if (Input.GetKey(KeyCode.E))
            {
                vertical += 1f;
            }
            if (Input.GetKey(KeyCode.Q))
            {
                vertical -= 1f;
            }
#endif

            return Mathf.Clamp(vertical, -1f, 1f);
        }

        private float GetHorizontalSpeed(bool isBoosting, bool isPrecision)
        {
            float speed = m_CruiseSpeed;
            if (isBoosting)
            {
                speed *= Mathf.Max(1f, m_BoostMultiplier);
            }
            if (isPrecision)
            {
                speed *= Mathf.Clamp(m_PrecisionMultiplier, 0.05f, 1f);
            }
            return speed;
        }

        private float GetVerticalSpeed(bool isBoosting, bool isPrecision)
        {
            float speed = m_VerticalSpeed;
            if (m_ApplyBoostToVertical && isBoosting)
            {
                speed *= Mathf.Max(1f, m_BoostMultiplier);
            }
            if (m_ApplyPrecisionToVertical && isPrecision)
            {
                speed *= Mathf.Clamp(m_PrecisionMultiplier, 0.05f, 1f);
            }
            return speed;
        }

        private void ResolveMovementBasis(out Vector3 forwardBasis, out Vector3 rightBasis)
        {
            if (m_CameraTransform == null)
            {
                forwardBasis = transform.forward;
                rightBasis = transform.right;
                forwardBasis.y = 0f;
                rightBasis.y = 0f;
                forwardBasis.Normalize();
                rightBasis.Normalize();
                return;
            }

            if (m_UseCameraPitchForForward)
            {
                forwardBasis = m_CameraTransform.forward;
                if (forwardBasis.sqrMagnitude < 0.0001f)
                {
                    forwardBasis = transform.forward;
                }
                forwardBasis.Normalize();
            }
            else
            {
                forwardBasis = Vector3.ProjectOnPlane(m_CameraTransform.forward, Vector3.up);
                if (forwardBasis.sqrMagnitude < 0.0001f)
                {
                    forwardBasis = transform.forward;
                }
                forwardBasis.Normalize();
            }

            rightBasis = Vector3.ProjectOnPlane(m_CameraTransform.right, Vector3.up);
            if (rightBasis.sqrMagnitude < 0.0001f)
            {
                rightBasis = Vector3.Cross(Vector3.up, forwardBasis);
            }
            rightBasis.Normalize();
        }

        private bool WasTogglePressed()
        {
            // Ignore fly toggle while cursor is unlocked (UI/chat interaction).
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            if (m_ToggleFlyAction != null && m_ToggleFlyAction.enabled)
            {
                if (m_ToggleFlyAction.WasPressedThisFrame())
                {
                    return true;
                }
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                return IsToggleKeyPressed(keyboard);
            }

            return false;
#else
            return Input.GetKeyDown(m_ToggleKey);
#endif
        }

        private bool IsBoostPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (
                m_FlyBoostAction != null
                && m_FlyBoostAction.enabled
                && m_FlyBoostAction.IsPressed()
            )
            {
                return true;
            }

            if (m_Inputs != null && m_Inputs.sprint)
            {
                return true;
            }

            Keyboard keyboard = Keyboard.current;
            if (
                keyboard != null
                && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
            )
            {
                return true;
            }
#else
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                return true;
            }
#endif

            return false;
        }

        private bool IsPrecisionPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (
                m_FlyPrecisionAction != null
                && m_FlyPrecisionAction.enabled
                && m_FlyPrecisionAction.IsPressed()
            )
            {
                return true;
            }

            Keyboard keyboard = Keyboard.current;
            if (
                keyboard != null
                && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)
            )
            {
                return true;
            }
#else
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                return true;
            }
#endif

            return false;
        }

#if ENABLE_INPUT_SYSTEM
        private bool IsToggleKeyPressed(Keyboard keyboard)
        {
            if (keyboard == null)
            {
                return false;
            }

            return m_ToggleKey switch
            {
                KeyCode.F => keyboard.fKey.wasPressedThisFrame,
                KeyCode.G => keyboard.gKey.wasPressedThisFrame,
                KeyCode.T => keyboard.tKey.wasPressedThisFrame,
                KeyCode.R => keyboard.rKey.wasPressedThisFrame,
                _ => keyboard.fKey.wasPressedThisFrame,
            };
        }
#endif

        private void ApplyCursorStateForMode()
        {
            if (m_IsFlying)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            bool lockCursor = m_Inputs != null && m_Inputs.cursorLocked;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }

        private void ResolveCameraTransform()
        {
            if (m_CameraTransform != null)
            {
                return;
            }

            Camera main = Camera.main;
            if (main != null)
            {
                m_CameraTransform = main.transform;
            }
        }

        private void SanitizeLegacyTuning()
        {
            // Legacy versions used acceleration as a rate (e.g. 10), while this script uses smooth-time.
            // Convert high legacy values into responsive smooth-times.
            if (m_AccelerationTime > 1f)
            {
                m_AccelerationTime = 1f / m_AccelerationTime;
            }
            if (m_DecelerationTime > 1f)
            {
                m_DecelerationTime = 1f / m_DecelerationTime;
            }

            m_AccelerationTime = Mathf.Clamp(m_AccelerationTime, 0.03f, 0.45f);
            m_DecelerationTime = Mathf.Clamp(m_DecelerationTime, 0.05f, 0.65f);
            m_YawSmoothTime = Mathf.Clamp(m_YawSmoothTime, 0.01f, 0.25f);
        }

#if ENABLE_INPUT_SYSTEM
        private void CacheOptionalActions()
        {
            m_ToggleFlyAction = null;
            m_FlyVerticalAction = null;
            m_FlyBoostAction = null;
            m_FlyPrecisionAction = null;
            m_MoveAction = null;

            if (m_PlayerInput == null || m_PlayerInput.actions == null)
            {
                return;
            }

            // Optional: if these actions exist in the input asset they are used automatically.
            m_ToggleFlyAction = FindActionByNames("FlyToggle", "ToggleFly", "FlyMode", "Fly");
            m_FlyVerticalAction = FindActionByNames("FlyVertical", "VerticalFly", "FlyUpDown");
            m_FlyBoostAction = FindActionByNames("FlyBoost", "BoostFly");
            m_FlyPrecisionAction = FindActionByNames("FlyPrecision", "PrecisionFly", "SlowFly");
            m_MoveAction = FindActionByNames("Move", "Movement");
        }

        private InputAction FindActionByNames(params string[] actionNames)
        {
            if (m_PlayerInput == null || m_PlayerInput.actions == null || actionNames == null)
            {
                return null;
            }

            for (int i = 0; i < actionNames.Length; i++)
            {
                string actionName = actionNames[i];
                if (string.IsNullOrWhiteSpace(actionName))
                {
                    continue;
                }

                InputAction action = m_PlayerInput.actions.FindAction(actionName, false);
                if (action != null)
                {
                    return action;
                }
            }

            return null;
        }
#endif
    }
}
