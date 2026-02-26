using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Network_Game.ThirdPersonController
{
    /// <summary>
    /// Third Person Character Controller supporting a full Mixamo animation set.
    ///
    /// Animator Parameters driven by this controller:
    ///   Float  Speed        – 0 (idle) → walk blend → sprint blend (normalised 0-1)
    ///   Float  MotionSpeed  – input magnitude for blend-tree playback rate
    ///   Bool   Grounded     – true when on the ground
    ///   Bool   Jump         – pulse true on jump frame
    ///   Bool   FreeFall     – true while falling after the fall-timeout
    ///   Bool   Crouch       – true while crouched
    ///   Float  TurnDelta    – signed yaw change per frame (for turn-in-place clips)
    ///   Bool   Flying       – true when FlyMode is active
    ///   Float  InputX       – raw horizontal input (-1..1) for strafe blend trees
    ///   Float  InputY       – raw vertical   input (-1..1) for forward/back blend trees
    ///   Int    IdleVariant  – random 0-4 for random idle selection
    ///   Bool   HardLanding  – true when landing from a high fall
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class ThirdPersonController : NetworkBehaviour
    {
        // ───────────────────────── Serialized Fields ─────────────────────────

        [Header("Player")]
        [Tooltip("Walk speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 5.335f;

        [Tooltip("Crouch speed of the character in m/s")]
        public float CrouchSpeed = 1.2f;

        [Tooltip("How fast the character turns to face movement direction")]
        [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;

        [Range(0, 1)]
        public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip(
            "Time required to pass before being able to jump again. Set to 0f to instantly jump again"
        )]
        public float JumpTimeout = 0.50f;

        [Tooltip(
            "Time required to pass before entering the fall state. Useful for walking down stairs"
        )]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not")]
        public bool Grounded = true;

        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;

        [Tooltip(
            "The radius of the grounded check. Should match the radius of the CharacterController"
        )]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Tooltip("Layers checked for ceiling clearance when standing up from crouch. Defaults to GroundLayers if left empty.")]
        public LayerMask CeilingLayers;

        [Header("Crouch")]
        [Tooltip("CharacterController height when crouching")]
        public float CrouchHeight = 1.0f;

        [Tooltip("CharacterController center Y offset when crouching")]
        public float CrouchCenterY = 0.5f;

        [Tooltip("Speed to interpolate crouch height changes")]
        public float CrouchTransitionSpeed = 10f;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 70.0f;

        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -30.0f;

        [Tooltip("Additional degrees to override the camera")]
        public float CameraAngleOverride = 0.0f;

        [Tooltip("For locking the camera position on all axis")]
        public bool LockCameraPosition = false;

        [Header("Landing")]
        [Tooltip("Fall time (seconds) before a landing counts as hard")]
        public float HardLandingFallTime = 0.8f;

        [Header("Idle Variants")]
        [Tooltip("Seconds of standing still before cycling to a random idle variant")]
        public float IdleVariantInterval = 8f;

        [Header("Fly Orientation")]
        [Tooltip("Maximum up/down body pitch while flying (degrees).")]
        [Range(0f, 75f)]
        public float FlyPitchMax = 32f;

        [Tooltip("How quickly body pitch follows vertical flight velocity.")]
        [Min(0.01f)]
        public float FlyPitchSmoothTime = 0.12f;

        [Tooltip("Maximum body bank while turning/curving in fly mode (degrees).")]
        [Range(0f, 60f)]
        public float FlyBankMax = 24f;

        [Tooltip("Bank amount from lateral flight velocity.")]
        [Min(0f)]
        public float FlyBankFromStrafe = 18f;

        [Tooltip("Additional bank from yaw turn correction while camera-aligning.")]
        [Min(0f)]
        public float FlyBankFromTurn = 12f;

        [Tooltip("How quickly body bank follows turn/strafe changes.")]
        [Min(0.01f)]
        public float FlyBankSmoothTime = 0.10f;

        [Tooltip("Invert flight banking direction if the rig feels mirrored.")]
        public bool InvertFlyBank;

        // ───────────────────────── Private State ─────────────────────────

        // cinemachine
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        // player
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _flyPitch;
        private float _flyPitchVelocity;
        private float _flyBank;
        private float _flyBankVelocity;
        private float _nextMainCameraResolveAt;

        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        // crouch
        private bool _isCrouching;
        private float _standingHeight;
        private float _standingCenterY;

        // landing
        private float _airTime;
        private bool _hardLandingActive;

        // idle variants
        private float _idleTimer;
        private int _currentIdleVariant;

        // turn detection
        private float _previousYaw;

        // animation IDs — cached Animator.StringToHash values
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        private int _animIDFlying;
        private int _animIDCrouch;
        private int _animIDTurnDelta;
        private int _animIDInputX;
        private int _animIDInputY;
        private int _animIDFlyPitch;
        private int _animIDFlyBank;
        private int _animIDIdleVariant;
        private int _animIDHardLanding;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;
        private FlyModeController _flyModeController;
        private RuntimeAnimatorController _cachedAnimatorController;
        private MovementMode _movementMode = MovementMode.Grounded;

        private const float _threshold = 0.01f;
        private const float MainCameraResolveInterval = 0.5f;

        private const float FlyVisualSyncInterval = 1f / 30f;
        private const float FlyVisualSyncAngleThreshold = 0.35f;

        private float _nextFlyVisualSyncAt;
        private float _lastSyncedFlyPitch;
        private float _lastSyncedFlyBank;

        // Cached Cinemachine camera — resolved once per spawn to avoid FindAnyObjectByType every 0.75s.
        private CinemachineVirtualCameraBase _cachedVcam;
        private bool _vcamAssigned;

        // ───────────────────────── Animator Param Presence Cache ─────────────────────────
        private bool _hasAnimator;
        private bool _animHasFlyingParam;
        private bool _animHasCrouchParam;
        private bool _animHasTurnDeltaParam;
        private bool _animHasInputXParam;
        private bool _animHasInputYParam;
        private bool _animHasFlyPitchParam;
        private bool _animHasFlyBankParam;
        private bool _animHasIdleVariantParam;
        private bool _animHasHardLandingParam;

        private Transform _flightVisualTransform;
        private Quaternion _flightVisualBaseLocalRotation = Quaternion.identity;
        private bool _flightVisualBaseCached;

        private readonly NetworkVariable<bool> _networkFlyMode = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );
        private readonly NetworkVariable<float> _networkFlyPitchDegrees = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );
        private readonly NetworkVariable<float> _networkFlyBankDegrees = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private enum MovementMode
        {
            Grounded,
            Flying,
        }

        /// <summary>
        /// Whether the player is currently in fly mode.
        /// Checks for FlyModeController component at runtime.
        /// </summary>
        private bool IsFlying
        {
            get
            {
                if (_flyModeController == null)
                {
                    _flyModeController = GetComponent<FlyModeController>();
                }
                return _flyModeController != null && _flyModeController.IsFlying;
            }
        }

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (_playerInput != null)
                {
                    if (string.Equals(_playerInput.currentControlScheme, "KeyboardMouse"))
                    {
                        return true;
                    }
                }
                return Mouse.current != null;
#else
                return false;
#endif
            }
        }

        // ───────────────────────── Unity Lifecycle ─────────────────────────

        private void Awake()
        {
            TryResolveMainCamera();

            // Acquire component references in Awake so they are available when
            // OnNetworkSpawn runs (which fires before Start in NGO).
            _animator = GetComponentInChildren<Animator>();
            _hasAnimator = _animator != null;
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#endif
        }

        /// <summary>
        /// Lazily resolves the main camera reference with a retry interval.
        /// </summary>
        private void TryResolveMainCamera()
        {
            if (_mainCamera != null && _mainCamera.activeInHierarchy)
            {
                return;
            }

            if (Time.unscaledTime < _nextMainCameraResolveAt)
            {
                return;
            }

            _nextMainCameraResolveAt = Time.unscaledTime + MainCameraResolveInterval;

            Camera main = Camera.main;
            if (main != null)
            {
                _mainCamera = main.gameObject;
                return;
            }

            _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
        }

        private void Start()
        {
            EnforceAnimatorSettingsOnce();
            EnsureFlyControllerReady();

            AssignAnimationIDs();
            CacheAnimatorParameterSupport();

            // Cache standing dimensions for crouch toggle
            _standingHeight = _controller.height;
            _standingCenterY = _controller.center.y;

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

            _previousYaw = transform.eulerAngles.y;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyRuntimeNetworkName();
            EnsureCameraTargetAssigned();
            if (CinemachineCameraTarget != null)
            {
                _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
            }

            ApplyOwnershipRuntimeState(IsOwner);
        }

        public override void OnGainedOwnership()
        {
            base.OnGainedOwnership();
            ApplyOwnershipRuntimeState(true);
        }

        public override void OnLostOwnership()
        {
            base.OnLostOwnership();
            ApplyOwnershipRuntimeState(false);
        }

        public override void OnNetworkDespawn()
        {
            if (_flyModeController != null)
            {
                _flyModeController.SetFlyMode(false);
            }
            SyncFlyModeState(false);
            ResetFlightVisualTilt();
            ApplyFlyAnimationAttitude(0f, 0f);
            base.OnNetworkDespawn();
        }

        private void ApplyRuntimeNetworkName()
        {
            if (NetworkObject == null || !NetworkObject.IsSpawned)
            {
                return;
            }

            string baseName = gameObject.name;
            int suffixIndex = baseName.IndexOf(" [C", System.StringComparison.Ordinal);
            if (suffixIndex > 0)
            {
                baseName = baseName.Substring(0, suffixIndex);
            }
            baseName = baseName.Replace("(Clone)", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "Player";
            }

            gameObject.name =
                $"{baseName} [C{NetworkObject.OwnerClientId}:N{NetworkObject.NetworkObjectId}]";
        }

        private void EnsureCameraTargetAssigned()
        {
            if (CinemachineCameraTarget != null)
            {
                return;
            }

            Transform target = transform.Find("PlayerCameraRoot");
            if (target != null)
            {
                CinemachineCameraTarget = target.gameObject;
                return;
            }

            CinemachineCameraTarget = gameObject;
        }

        private bool ShouldProcessLocalControl()
        {
            return NetworkObject == null || !NetworkObject.IsSpawned || IsOwner;
        }

        private void ApplyOwnershipRuntimeState(bool isOwner)
        {
#if ENABLE_INPUT_SYSTEM
            if (_playerInput != null)
            {
                _playerInput.enabled = isOwner;
            }
#endif
            if (_input != null)
            {
                _input.enabled = isOwner;
            }

            EnsureFlyControllerReady();
            if (_flyModeController != null)
            {
                _flyModeController.SetFlyMode(false);
                _flyModeController.enabled = isOwner;
            }

            if (!isOwner)
            {
                SyncFlyModeState(false);
                _flyPitch = 0f;
                _flyBank = 0f;
                _flyPitchVelocity = 0f;
                _flyBankVelocity = 0f;
                ResetFlightVisualTilt();
                ApplyFlyAnimationAttitude(0f, 0f);
                return;
            }

            if (_input != null)
            {
                _input.cursorLocked = true;
                _input.SetCursorState(true);
            }

            AssignCinemachineFollow();
        }

        private void EnsureFlightVisualTransform()
        {
            Transform candidate = null;
            if (_animator != null && _animator.transform != transform)
            {
                candidate = _animator.transform;
            }

            if (_flightVisualTransform != candidate)
            {
                _flightVisualTransform = candidate;
                _flightVisualBaseCached = false;
            }

            if (_flightVisualTransform != null && !_flightVisualBaseCached)
            {
                _flightVisualBaseLocalRotation = _flightVisualTransform.localRotation;
                _flightVisualBaseCached = true;
            }
        }

        private void ApplyFlightVisualTilt(float pitchDegrees, float bankDegrees)
        {
            EnsureFlightVisualTransform();
            if (_flightVisualTransform == null || !_flightVisualBaseCached)
            {
                return;
            }

            Quaternion tilt = Quaternion.Euler(pitchDegrees, 0f, bankDegrees);
            _flightVisualTransform.localRotation = _flightVisualBaseLocalRotation * tilt;
        }

        private void ResetFlightVisualTilt()
        {
            EnsureFlightVisualTransform();
            if (_flightVisualTransform == null || !_flightVisualBaseCached)
            {
                return;
            }

            _flightVisualTransform.localRotation = _flightVisualBaseLocalRotation;
        }

        private void UpdateRemoteFlightVisuals()
        {
            if (ShouldProcessLocalControl())
            {
                return;
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
            _hasAnimator = _animator != null;
            if (_hasAnimator && _animator.runtimeAnimatorController != _cachedAnimatorController)
            {
                CacheAnimatorParameterSupport();
            }

            bool remoteFlying = _networkFlyMode.Value;
            if (!remoteFlying)
            {
                _flyPitch = 0f;
                _flyBank = 0f;
                ResetFlightVisualTilt();
                return;
            }

            _flyPitch = Mathf.SmoothDampAngle(
                _flyPitch,
                _networkFlyPitchDegrees.Value,
                ref _flyPitchVelocity,
                Mathf.Max(0.01f, FlyPitchSmoothTime)
            );
            _flyBank = Mathf.SmoothDampAngle(
                _flyBank,
                _networkFlyBankDegrees.Value,
                ref _flyBankVelocity,
                Mathf.Max(0.01f, FlyBankSmoothTime)
            );

            ApplyFlightVisualTilt(_flyPitch, _flyBank);
            ApplyFlyAnimationAttitude(_flyPitch, _flyBank);
        }

        private void TrySyncFlyVisualState(float pitchDegrees, float bankDegrees)
        {
            if (NetworkObject == null || !NetworkObject.IsSpawned || !IsOwner)
            {
                return;
            }

            bool enoughTimeElapsed = Time.unscaledTime >= _nextFlyVisualSyncAt;
            bool pitchChanged =
                Mathf.Abs(pitchDegrees - _lastSyncedFlyPitch) >= FlyVisualSyncAngleThreshold;
            bool bankChanged =
                Mathf.Abs(bankDegrees - _lastSyncedFlyBank) >= FlyVisualSyncAngleThreshold;

            if (!enoughTimeElapsed && !pitchChanged && !bankChanged)
            {
                return;
            }

            _nextFlyVisualSyncAt = Time.unscaledTime + FlyVisualSyncInterval;
            _lastSyncedFlyPitch = pitchDegrees;
            _lastSyncedFlyBank = bankDegrees;
            _networkFlyPitchDegrees.Value = pitchDegrees;
            _networkFlyBankDegrees.Value = bankDegrees;
        }

        /// <summary>
        /// Finds the active CinemachineCamera in the scene and points it at this player's camera target.
        /// Uses a cached reference after the first successful assignment to avoid expensive
        /// FindAnyObjectByType calls every 0.75 s. Re-searches only if the cached vcam is destroyed.
        /// </summary>
        private void AssignCinemachineFollow()
        {
            EnsureCameraTargetAssigned();
            if (CinemachineCameraTarget == null)
            {
                return;
            }

            Transform target = CinemachineCameraTarget.transform;

            // Fast path — vcam already assigned and still alive.
            if (_vcamAssigned && _cachedVcam != null)
            {
                _cachedVcam.Follow = target;
                _cachedVcam.LookAt = target;
                return;
            }

            // Reset cache if the object was destroyed.
            _vcamAssigned = false;
            _cachedVcam = null;

            // Try Brain's active vcam first (cheapest).
            CinemachineBrain brain = null;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                brain = mainCamera.GetComponent<CinemachineBrain>();
            }

            if (
                brain != null
                && brain.ActiveVirtualCamera is CinemachineVirtualCameraBase activeVcam
                && activeVcam != null
            )
            {
                activeVcam.Follow = target;
                activeVcam.LookAt = target;
                _cachedVcam = activeVcam;
                _vcamAssigned = true;
                return;
            }

            // Fallback: scene search (Cinemachine 3.x / Unity 6). Expensive but only runs until found.
            var cmCam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
            if (cmCam != null)
            {
                cmCam.Follow = target;
                cmCam.LookAt = target;
                _cachedVcam = cmCam;
                _vcamAssigned = true;
            }
        }

        private void Update()
        {
            // Only the owning client drives movement, input and animation params
            if (!ShouldProcessLocalControl())
            {
                UpdateRemoteFlightVisuals();
                return;
            }

            // Re-resolve camera if it was not available at Awake (late-joining clients)
            TryResolveMainCamera();

            // Re-assign vcam only if our cached reference has gone stale (destroyed or not yet found).
            if (!_vcamAssigned || _cachedVcam == null)
            {
                AssignCinemachineFollow();
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
                if (_animator != null)
                {
                    _hasAnimator = true;
                    EnforceAnimatorSettingsOnce();
                }
            }
            _hasAnimator = _animator != null;
            EnsureFlyControllerReady();
            if (_hasAnimator && _animator.runtimeAnimatorController != _cachedAnimatorController)
            {
                CacheAnimatorParameterSupport();
            }

            GroundedCheck();
            SyncFlyModeState(IsFlying);
            if (_flyModeController != null)
            {
                _flyModeController.SetGroundedHint(Grounded);
            }

            UpdateCrouch();
            Move();
            UpdateTurnAnimation();
            UpdateIdleVariant();
        }

        private void LateUpdate()
        {
            if (!ShouldProcessLocalControl())
                return;
            CameraRotation();
        }

        // ───────────────────────── Animator Setup ─────────────────────────

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
            _animIDFlying = Animator.StringToHash("Flying");
            _animIDCrouch = Animator.StringToHash("Crouch");
            _animIDTurnDelta = Animator.StringToHash("TurnDelta");
            _animIDInputX = Animator.StringToHash("InputX");
            _animIDInputY = Animator.StringToHash("InputY");
            _animIDFlyPitch = Animator.StringToHash("FlyPitch");
            _animIDFlyBank = Animator.StringToHash("FlyBank");
            _animIDIdleVariant = Animator.StringToHash("IdleVariant");
            _animIDHardLanding = Animator.StringToHash("HardLanding");
        }

        /// <summary>
        /// Scans the animator's parameter list so we only set parameters that actually
        /// exist in the current controller. This makes the script backwards-compatible
        /// with simpler animator controllers that don't have all parameters.
        /// </summary>
        private void CacheAnimatorParameterSupport()
        {
            _animHasFlyingParam = false;
            _animHasCrouchParam = false;
            _animHasTurnDeltaParam = false;
            _animHasInputXParam = false;
            _animHasInputYParam = false;
            _animHasFlyPitchParam = false;
            _animHasFlyBankParam = false;
            _animHasIdleVariantParam = false;
            _animHasHardLandingParam = false;
            _cachedAnimatorController =
                _animator != null ? _animator.runtimeAnimatorController : null;

            if (!_hasAnimator || _animator == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                int hash = parameters[i].nameHash;

                if (hash == _animIDFlying)
                    _animHasFlyingParam = true;
                if (hash == _animIDCrouch)
                    _animHasCrouchParam = true;
                if (hash == _animIDTurnDelta)
                    _animHasTurnDeltaParam = true;
                if (hash == _animIDInputX)
                    _animHasInputXParam = true;
                if (hash == _animIDInputY)
                    _animHasInputYParam = true;
                if (hash == _animIDFlyPitch)
                    _animHasFlyPitchParam = true;
                if (hash == _animIDFlyBank)
                    _animHasFlyBankParam = true;
                if (hash == _animIDIdleVariant)
                    _animHasIdleVariantParam = true;
                if (hash == _animIDHardLanding)
                    _animHasHardLandingParam = true;
            }
        }

        private void EnsureAnimatorRuntimeSettings()
        {
            if (!_hasAnimator || _animator == null)
            {
                return;
            }

            // CharacterController movement is script-driven; root motion causes conflicting movement.
            // Only enforce once — checking every frame wastes cycles and silently overrides designers.
            if (_animator.applyRootMotion)
            {
                _animator.applyRootMotion = false;
                UnityEngine.Debug.LogWarning(
                    $"[ThirdPersonController] Disabled applyRootMotion on '{_animator.name}'. CharacterController drives movement.",
                    this
                );
            }

            if (_animator.updateMode != AnimatorUpdateMode.Normal)
            {
                _animator.updateMode = AnimatorUpdateMode.Normal;
            }
        }

        // Called once from Start — enforces animator settings without per-frame cost.
        private void EnforceAnimatorSettingsOnce()
        {
            EnsureAnimatorRuntimeSettings();
        }

        private void EnsureFlyControllerReady()
        {
            if (_flyModeController == null)
            {
                _flyModeController = GetComponent<FlyModeController>();
            }

            if (_flyModeController == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[ThirdPersonController] FlyModeController not found — adding one at runtime with default settings. Add it manually to suppress this warning.",
                    this
                );
                _flyModeController = gameObject.AddComponent<FlyModeController>();
            }

            if (_flyModeController != null)
            {
                bool shouldEnable = ShouldProcessLocalControl();
                if (_flyModeController.enabled != shouldEnable)
                {
                    _flyModeController.enabled = shouldEnable;
                }
            }
        }

        // ───────────────────────── Ground Check ─────────────────────────

        // Reusable buffer for OverlapSphereNonAlloc (avoids per-frame allocation)
        private static readonly Collider[] s_GroundCheckResults = new Collider[8];

        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(
                transform.position.x,
                transform.position.y - GroundedOffset,
                transform.position.z
            );
            bool wasGrounded = Grounded;

            // Use OverlapSphere + filter out own collider instead of layer-based
            // self-exclusion, which breaks when player and ground share a layer.
            int count = Physics.OverlapSphereNonAlloc(
                spherePosition,
                GroundedRadius,
                s_GroundCheckResults,
                GroundLayers,
                QueryTriggerInteraction.Ignore
            );
            bool foundGround = false;
            for (int i = 0; i < count; i++)
            {
                if (s_GroundCheckResults[i] != null && s_GroundCheckResults[i] != _controller)
                {
                    foundGround = true;
                    break;
                }
            }
            Grounded = foundGround;

            // Track air time for hard landing detection
            if (!Grounded)
            {
                _airTime += Time.deltaTime;
            }

            // Detect landing frame
            if (Grounded && !wasGrounded)
            {
                _hardLandingActive = _airTime >= HardLandingFallTime;
                if (_hardLandingActive && _hasAnimator && _animHasHardLandingParam)
                {
                    _animator.SetBool(_animIDHardLanding, true);
                }
                _airTime = 0f;
            }
            else if (Grounded && _hardLandingActive)
            {
                // Clear after one frame — pulse complete
                if (_hasAnimator && _animHasHardLandingParam)
                {
                    _animator.SetBool(_animIDHardLanding, false);
                }
                _hardLandingActive = false;
            }

            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        // ───────────────────────── Camera ─────────────────────────

        private void CameraRotation()
        {
            EnsureCameraTargetAssigned();
            if (CinemachineCameraTarget == null || _input == null)
            {
                return;
            }

            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            _cinemachineTargetYaw = ClampAngle(
                _cinemachineTargetYaw,
                float.MinValue,
                float.MaxValue
            );
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(
                _cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw,
                0.0f
            );
        }

        // ───────────────────────── Crouch ─────────────────────────

        private void UpdateCrouch()
        {
            // Can only crouch while grounded and not flying
            bool wantCrouch = _input.crouch && Grounded && _movementMode != MovementMode.Flying;

            // If we want to stand but there's something above, stay crouched
            if (_isCrouching && !wantCrouch)
            {
                if (!CanStandUp())
                {
                    wantCrouch = true;
                }
            }

            // Jumping cancels crouch
            if (_input.jump && _isCrouching)
            {
                _input.crouch = false;
                wantCrouch = false;
            }

            _isCrouching = wantCrouch;

            // Smoothly interpolate CharacterController height
            float targetHeight = _isCrouching ? CrouchHeight : _standingHeight;
            float targetCenterY = _isCrouching ? CrouchCenterY : _standingCenterY;

            _controller.height = Mathf.Lerp(
                _controller.height,
                targetHeight,
                Time.deltaTime * CrouchTransitionSpeed
            );
            _controller.center = new Vector3(
                _controller.center.x,
                Mathf.Lerp(
                    _controller.center.y,
                    targetCenterY,
                    Time.deltaTime * CrouchTransitionSpeed
                ),
                _controller.center.z
            );

            if (_hasAnimator && _animHasCrouchParam)
            {
                _animator.SetBool(_animIDCrouch, _isCrouching);
            }
        }

        /// <summary>
        /// Checks if there is enough clearance above to stand up from crouch.
        /// </summary>
        private bool CanStandUp()
        {
            float clearanceNeeded = _standingHeight - _controller.height;
            if (clearanceNeeded <= 0f)
                return true;

            Vector3 origin = transform.position + Vector3.up * _controller.height;
            LayerMask ceilingMask = CeilingLayers.value != 0 ? CeilingLayers : GroundLayers;
            return !Physics.Raycast(
                origin,
                Vector3.up,
                clearanceNeeded + 0.1f,
                ceilingMask,
                QueryTriggerInteraction.Ignore
            );
        }

        // ───────────────────────── Movement ─────────────────────────

        private void Move()
        {
            if (_movementMode == MovementMode.Flying)
            {
                MoveFlying();
                return;
            }

            JumpAndGravity();

            // Determine target speed: crouch → walk → sprint
            float targetSpeed;
            if (_isCrouching)
            {
                targetSpeed = CrouchSpeed;
            }
            else
            {
                targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;
            }

            // If there is no input, set the target speed to 0
            if (_input.move == Vector2.zero)
                targetSpeed = 0.0f;

            // Current horizontal velocity
            float currentHorizontalSpeed = new Vector3(
                _controller.velocity.x,
                0.0f,
                _controller.velocity.z
            ).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // Accelerate or decelerate to target speed
            if (
                currentHorizontalSpeed < targetSpeed - speedOffset
                || currentHorizontalSpeed > targetSpeed + speedOffset
            )
            {
                _speed = Mathf.Lerp(
                    currentHorizontalSpeed,
                    targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate
                );
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(
                _animationBlend,
                targetSpeed,
                Time.deltaTime * SpeedChangeRate
            );
            if (_animationBlend < 0.01f)
                _animationBlend = 0f;

            // Normalise input direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            // Rotate player to face movement direction
            if (_input.move != Vector2.zero)
            {
                float cameraYaw =
                    _mainCamera != null
                        ? _mainCamera.transform.eulerAngles.y
                        : transform.eulerAngles.y;
                _targetRotation =
                    Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cameraYaw;
                float rotation = Mathf.SmoothDampAngle(
                    transform.eulerAngles.y,
                    _targetRotation,
                    ref _rotationVelocity,
                    RotationSmoothTime
                );
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }

            Vector3 targetDirection =
                Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

            // Move the player
            _controller.Move(
                targetDirection.normalized * (_speed * Time.deltaTime)
                    + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime
            );

            // Update animator
            if (_hasAnimator)
            {
                // Normalize speed for blend tree:
                //   0.0 = idle  (Speed param = 0)
                //   0.5 = walk  (Speed param = MoveSpeed / SprintSpeed * 0.5 … mapped to exactly 0.5)
                //   1.0 = sprint (Speed param = 1)
                // We map: [0..MoveSpeed] → [0..0.5]  and  [MoveSpeed..SprintSpeed] → [0.5..1.0]
                float normalizedSpeed = 0f;
                if (_animationBlend > 0.01f && MoveSpeed > 0.01f && SprintSpeed > MoveSpeed)
                {
                    if (_animationBlend <= MoveSpeed)
                    {
                        normalizedSpeed = (_animationBlend / MoveSpeed) * 0.5f;
                    }
                    else
                    {
                        normalizedSpeed =
                            0.5f
                            + ((_animationBlend - MoveSpeed) / (SprintSpeed - MoveSpeed)) * 0.5f;
                    }
                    normalizedSpeed = Mathf.Clamp01(normalizedSpeed);
                }
                _animator.SetFloat(_animIDSpeed, normalizedSpeed, 0.1f, Time.deltaTime);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude, 0.1f, Time.deltaTime);

                // Directional inputs for strafe blend trees
                if (_animHasInputXParam)
                {
                    _animator.SetFloat(_animIDInputX, _input.move.x, 0.1f, Time.deltaTime);
                }
                if (_animHasInputYParam)
                {
                    _animator.SetFloat(_animIDInputY, _input.move.y, 0.1f, Time.deltaTime);
                }
            }
        }

        private void MoveFlying()
        {
            Vector3 flyVelocity =
                _flyModeController != null ? _flyModeController.GetFlyVelocity() : Vector3.zero;

            _controller.Move(flyVelocity * Time.deltaTime);

            float baseYaw = transform.eulerAngles.y;
            float currentYaw = baseYaw;
            float targetYaw = currentYaw;
            if (_flyModeController != null && _flyModeController.ShouldAlignYawWithCamera)
            {
                targetYaw = _mainCamera != null ? _mainCamera.transform.eulerAngles.y : currentYaw;
                float yawSmooth = Mathf.Max(0.01f, _flyModeController.YawSmoothTime);
                currentYaw = Mathf.SmoothDampAngle(
                    currentYaw,
                    targetYaw,
                    ref _rotationVelocity,
                    yawSmooth
                );
            }

            float targetPitch = ComputeFlyPitchDegrees(flyVelocity);
            float yawErrorForBank = Mathf.DeltaAngle(baseYaw, targetYaw);
            float targetBank = ComputeFlyBankDegrees(flyVelocity, currentYaw, yawErrorForBank);

            _flyPitch = Mathf.SmoothDampAngle(
                _flyPitch,
                targetPitch,
                ref _flyPitchVelocity,
                Mathf.Max(0.01f, FlyPitchSmoothTime)
            );
            _flyBank = Mathf.SmoothDampAngle(
                _flyBank,
                targetBank,
                ref _flyBankVelocity,
                Mathf.Max(0.01f, FlyBankSmoothTime)
            );

            // Keep network-synced root orientation yaw-only for stable replication.
            transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
            ApplyFlightVisualTilt(_flyPitch, _flyBank);
            ApplyFlyAnimationAttitude(_flyPitch, _flyBank);
            if (NetworkObject != null && NetworkObject.IsSpawned && IsOwner)
            {
                _networkFlyMode.Value = true;
            }
            TrySyncFlyVisualState(_flyPitch, _flyBank);

            UpdateFlyAnimation();
        }

        // ───────────────────────── Fly Mode ─────────────────────────

        private void SyncFlyModeState(bool flying)
        {
            MovementMode nextMode = flying ? MovementMode.Flying : MovementMode.Grounded;
            if (nextMode == _movementMode)
            {
                return;
            }

            if (nextMode == MovementMode.Flying)
            {
                _verticalVelocity = 0f;
                _jumpTimeoutDelta = JumpTimeout;
                _fallTimeoutDelta = FallTimeout;
                _flyPitchVelocity = 0f;
                _flyBankVelocity = 0f;

                // Cancel crouch when entering fly mode
                _isCrouching = false;
                _input.crouch = false;
            }
            else
            {
                _verticalVelocity = Mathf.Min(_verticalVelocity, -2f);
                _flyPitch = 0f;
                _flyBank = 0f;
                _flyPitchVelocity = 0f;
                _flyBankVelocity = 0f;
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                ResetFlightVisualTilt();
                ApplyFlyAnimationAttitude(0f, 0f);
                if (_hasAnimator && _animHasFlyingParam)
                {
                    _animator.SetBool(_animIDFlying, false);
                }
            }

            _movementMode = nextMode;

            if (NetworkObject != null && NetworkObject.IsSpawned && IsOwner)
            {
                bool isFlyingNow = nextMode == MovementMode.Flying;
                _networkFlyMode.Value = isFlyingNow;
                if (!isFlyingNow)
                {
                    _networkFlyPitchDegrees.Value = 0f;
                    _networkFlyBankDegrees.Value = 0f;
                    _lastSyncedFlyPitch = 0f;
                    _lastSyncedFlyBank = 0f;
                    _nextFlyVisualSyncAt = 0f;
                }
            }
        }

        private float ComputeFlyPitchDegrees(Vector3 flyVelocity)
        {
            float speed = flyVelocity.magnitude;
            if (speed < 0.05f)
            {
                return 0f;
            }

            float verticalRatio = Mathf.Clamp(flyVelocity.y / speed, -1f, 1f);

            // Positive X pitch looks down in Unity, so invert to lift the head/nose when climbing.
            float pitch = -Mathf.Asin(verticalRatio) * Mathf.Rad2Deg;
            return Mathf.Clamp(pitch, -Mathf.Abs(FlyPitchMax), Mathf.Abs(FlyPitchMax));
        }

        private float ComputeFlyBankDegrees(
            Vector3 flyVelocity,
            float currentYaw,
            float yawErrorForBank
        )
        {
            float speed = flyVelocity.magnitude;
            float lateralRatio = 0f;
            if (speed > 0.05f)
            {
                Quaternion yawRotation = Quaternion.Euler(0f, currentYaw, 0f);
                Vector3 localVelocity = Quaternion.Inverse(yawRotation) * flyVelocity;
                lateralRatio = Mathf.Clamp(localVelocity.x / speed, -1f, 1f);
            }

            float bankFromStrafe = -lateralRatio * Mathf.Max(0f, FlyBankFromStrafe);
            float normalizedTurn = Mathf.Clamp(yawErrorForBank / 45f, -1f, 1f);
            float bankFromTurn = -normalizedTurn * Mathf.Max(0f, FlyBankFromTurn);

            float bank = bankFromStrafe + bankFromTurn;
            if (InvertFlyBank)
            {
                bank = -bank;
            }

            float maxBank = Mathf.Abs(FlyBankMax);
            return Mathf.Clamp(bank, -maxBank, maxBank);
        }

        private void ApplyFlyAnimationAttitude(float pitchDegrees, float bankDegrees)
        {
            if (!_hasAnimator || _animator == null)
            {
                return;
            }

            if (_animHasFlyPitchParam)
            {
                float normalizedPitch = Mathf.Clamp(
                    pitchDegrees / Mathf.Max(1f, Mathf.Abs(FlyPitchMax)),
                    -1f,
                    1f
                );
                _animator.SetFloat(_animIDFlyPitch, normalizedPitch, 0.10f, Time.deltaTime);
            }

            if (_animHasFlyBankParam)
            {
                float normalizedBank = Mathf.Clamp(
                    bankDegrees / Mathf.Max(1f, Mathf.Abs(FlyBankMax)),
                    -1f,
                    1f
                );
                _animator.SetFloat(_animIDFlyBank, normalizedBank, 0.10f, Time.deltaTime);
            }
        }

        private void UpdateFlyAnimation()
        {
            if (!_hasAnimator || _animator == null)
            {
                return;
            }

            if (_flyModeController == null)
            {
                _flyModeController = GetComponent<FlyModeController>();
            }

            if (_animHasFlyingParam)
            {
                _animator.SetBool(_animIDFlying, true);
            }

            _animator.SetBool(_animIDGrounded, false);
            _animator.SetBool(_animIDJump, false);
            _animator.SetBool(_animIDFreeFall, false);

            Vector3 flyVelocity =
                _flyModeController != null ? _flyModeController.GetFlyVelocity() : Vector3.zero;
            float totalSpeed = flyVelocity.magnitude;
            float planarSpeed = new Vector2(flyVelocity.x, flyVelocity.z).magnitude;
            float normalizationSpeed = Mathf.Max(0.01f, SprintSpeed);

            float speedBlend = Mathf.Clamp01(totalSpeed / normalizationSpeed);
            float motionBlend = Mathf.Clamp01(planarSpeed / normalizationSpeed);

            _animator.SetFloat(_animIDSpeed, speedBlend, 0.10f, Time.deltaTime);
            _animator.SetFloat(_animIDMotionSpeed, motionBlend, 0.12f, Time.deltaTime);
        }

        // ───────────────────────── Jump & Gravity ─────────────────────────

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump (cannot jump while crouching)
                if (_input.jump && _jumpTimeoutDelta <= 0.0f && !_isCrouching)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    _input.jump = false;

                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;

                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                _input.jump = false;
            }

            if (_verticalVelocity > -_terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        // ───────────────────────── Turn Animation ─────────────────────────

        /// <summary>
        /// Computes per-frame yaw delta and sends it to the animator so idle
        /// turn-in-place animations can be triggered by the state machine.
        /// </summary>
        private void UpdateTurnAnimation()
        {
            if (!_hasAnimator || !_animHasTurnDeltaParam)
                return;

            float currentYaw = transform.eulerAngles.y;
            float delta = Mathf.DeltaAngle(_previousYaw, currentYaw);
            _previousYaw = currentYaw;

            // Smooth it so small jitter doesn't flicker the animation
            _animator.SetFloat(_animIDTurnDelta, delta, 0.08f, Time.deltaTime);
        }

        // ───────────────────────── Idle Variants ─────────────────────────

        /// <summary>
        /// When standing still on the ground, periodically cycles through idle
        /// animation variants (fidget, look around, etc.).
        /// </summary>
        private void UpdateIdleVariant()
        {
            if (!_hasAnimator || !_animHasIdleVariantParam)
                return;

            bool isIdle =
                Grounded
                && _input.move == Vector2.zero
                && !_isCrouching
                && _movementMode != MovementMode.Flying;

            if (isIdle)
            {
                _idleTimer += Time.deltaTime;
                if (_idleTimer >= IdleVariantInterval)
                {
                    _idleTimer = 0f;
                    _currentIdleVariant = Random.Range(0, 5); // 0-4 for your 5 idle variants
                    _animator.SetInteger(_animIDIdleVariant, _currentIdleVariant);
                }
            }
            else
            {
                _idleTimer = 0f;
                // Reset to default idle when moving
                if (_currentIdleVariant != 0)
                {
                    _currentIdleVariant = 0;
                    _animator.SetInteger(_animIDIdleVariant, 0);
                }
            }
        }

        // ───────────────────────── Utilities ─────────────────────────

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f)
                lfAngle += 360f;
            if (lfAngle > 360f)
                lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded)
                Gizmos.color = transparentGreen;
            else
                Gizmos.color = transparentRed;

            Gizmos.DrawSphere(
                new Vector3(
                    transform.position.x,
                    transform.position.y - GroundedOffset,
                    transform.position.z
                ),
                GroundedRadius
            );
        }

        // ───────────────────────── Animation Events ─────────────────────────

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = Random.Range(0, FootstepAudioClips.Length);
                    AudioSource.PlayClipAtPoint(
                        FootstepAudioClips[index],
                        transform.TransformPoint(_controller.center),
                        FootstepAudioVolume
                    );
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioSource.PlayClipAtPoint(
                    LandingAudioClip,
                    transform.TransformPoint(_controller.center),
                    FootstepAudioVolume
                );
            }
        }
    }
}
