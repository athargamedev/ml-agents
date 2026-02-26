using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Behavior
{
    /// <summary>
    /// Lightweight server-authoritative NPC chase controller with a simple state machine.
    /// Attach to a networked NPC to make it chase flying players in 3D space.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [AddComponentMenu("Network Game/Behavior/NPC Flight Chase State Machine")]
    public sealed class NpcFlightChaseStateMachine : NetworkBehaviour
    {
        public enum ChaseState
        {
            Disabled,
            Idle,
            AcquireTarget,
            Chase,
        }

        [Header("Enable")]
        [SerializeField]
        private bool m_EnableChase = true;

        [Header("Targeting")]
        [SerializeField]
        [Min(1f)]
        private float m_SearchRadius = 90f;

        [SerializeField]
        [Min(1f)]
        private float m_LoseTargetDistance = 130f;

        [SerializeField]
        [Min(0.02f)]
        private float m_ReacquireIntervalSeconds = 0.25f;

        [SerializeField]
        [Min(0f)]
        private float m_TargetHeightOffset = 1.1f;

        [SerializeField]
        private string m_PlayerTag = "Player";

        [Header("Movement")]
        [SerializeField]
        [Min(0.1f)]
        private float m_MaxHorizontalSpeed = 9f;

        [SerializeField]
        [Min(0.1f)]
        private float m_MaxClimbSpeed = 7f;

        [SerializeField]
        [Min(0.1f)]
        private float m_MaxDiveSpeed = 9f;

        [SerializeField]
        [Min(0.1f)]
        private float m_Acceleration = 18f;

        [SerializeField]
        [Min(0f)]
        private float m_StopDistance = 2.2f;

        [SerializeField]
        [Range(0f, 1080f)]
        private float m_RotationDegreesPerSecond = 420f;

        [SerializeField]
        [Tooltip("If true, NPC follows player vertically as well as horizontally.")]
        private bool m_ChaseVertical = true;

        [Header("Integration")]
        [SerializeField]
        [Tooltip("Disable NavMeshAgent while this chase controller is active.")]
        private bool m_DisableNavMeshAgent = true;

        [SerializeField]
        [Tooltip(
            "Optional components to disable while chase runs (for example BehaviorGraphAgent)."
        )]
        private MonoBehaviour[] m_DisableWhileChasing;

        [Header("Diagnostics")]
        [SerializeField]
        private bool m_LogDebug;

        public ChaseState State => m_State;
        public NetworkObject CurrentTarget => m_CurrentTarget;

        private ChaseState m_State = ChaseState.Idle;
        private NetworkObject m_CurrentTarget;
        private Vector3 m_CurrentVelocity;
        private float m_NextReacquireAt;
        private bool m_LastLoggedEnabledState;

        private Animator m_Animator;
        private UnityEngine.AI.NavMeshAgent m_NavMeshAgent;

        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");
        private static readonly int AnimFlying = Animator.StringToHash("Flying");
        private static readonly int AnimGrounded = Animator.StringToHash("Grounded");
        private static readonly int AnimFreeFall = Animator.StringToHash("FreeFall");
        private static readonly int AnimInputX = Animator.StringToHash("InputX");
        private static readonly int AnimInputY = Animator.StringToHash("InputY");

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            m_Animator = GetComponentInChildren<Animator>();
            m_NavMeshAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();

            ApplyIntegrationFlags(IsServer && m_EnableChase);
            SetState(m_EnableChase ? ChaseState.Idle : ChaseState.Disabled);
        }

        public override void OnNetworkDespawn()
        {
            ApplyIntegrationFlags(false);
            m_CurrentTarget = null;
            m_CurrentVelocity = Vector3.zero;
            base.OnNetworkDespawn();
        }

        private void OnDisable()
        {
            if (!IsServer)
            {
                return;
            }

            m_CurrentTarget = null;
            m_CurrentVelocity = Vector3.zero;
            ApplyIntegrationFlags(false);
            SetState(ChaseState.Disabled);
            UpdateAnimator(Vector3.zero, false);
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            if (!m_EnableChase)
            {
                if (m_State != ChaseState.Disabled)
                {
                    SetState(ChaseState.Disabled);
                }

                if (m_CurrentVelocity.sqrMagnitude > 0.0001f)
                {
                    m_CurrentVelocity = Vector3.MoveTowards(
                        m_CurrentVelocity,
                        Vector3.zero,
                        m_Acceleration * Time.deltaTime
                    );
                }
                UpdateAnimator(Vector3.zero, false);
                ApplyIntegrationFlags(false);
                return;
            }

            ApplyIntegrationFlags(true);
            TickStateMachine();
        }

        private void TickStateMachine()
        {
            if (Time.unscaledTime >= m_NextReacquireAt)
            {
                m_NextReacquireAt =
                    Time.unscaledTime + Mathf.Max(0.02f, m_ReacquireIntervalSeconds);
                AcquireBestTarget();
            }

            if (m_CurrentTarget == null || !m_CurrentTarget.IsSpawned)
            {
                m_CurrentTarget = null;
                SetState(ChaseState.AcquireTarget);
                UpdateAnimator(Vector3.zero, false);
                return;
            }

            Vector3 toTarget = GetTargetAimPoint(m_CurrentTarget) - transform.position;
            float distance = toTarget.magnitude;
            if (distance > Mathf.Max(m_SearchRadius, m_LoseTargetDistance))
            {
                m_CurrentTarget = null;
                SetState(ChaseState.AcquireTarget);
                UpdateAnimator(Vector3.zero, false);
                return;
            }

            SetState(ChaseState.Chase);
            Vector3 desiredVelocity = ComputeDesiredVelocity(toTarget, distance);
            m_CurrentVelocity = Vector3.MoveTowards(
                m_CurrentVelocity,
                desiredVelocity,
                Mathf.Max(0.1f, m_Acceleration) * Time.deltaTime
            );

            transform.position += m_CurrentVelocity * Time.deltaTime;
            UpdateRotation(m_CurrentVelocity);
            UpdateAnimator(m_CurrentVelocity, true);
        }

        private void AcquireBestTarget()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
            {
                m_CurrentTarget = null;
                return;
            }

            float bestSqr = float.PositiveInfinity;
            NetworkObject best = null;
            float maxRangeSqr = m_SearchRadius * m_SearchRadius;

            foreach (var kvp in manager.ConnectedClients)
            {
                NetworkClient client = kvp.Value;
                if (client == null || client.PlayerObject == null || !client.PlayerObject.IsSpawned)
                {
                    continue;
                }

                NetworkObject candidate = client.PlayerObject;
                if (!string.IsNullOrWhiteSpace(m_PlayerTag) && !candidate.CompareTag(m_PlayerTag))
                {
                    continue;
                }

                Vector3 diff = candidate.transform.position - transform.position;
                float sqr = diff.sqrMagnitude;
                if (sqr > maxRangeSqr || sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                best = candidate;
            }

            m_CurrentTarget = best;
            SetState(best != null ? ChaseState.Chase : ChaseState.Idle);
        }

        private Vector3 ComputeDesiredVelocity(Vector3 toTarget, float distance)
        {
            if (distance <= Mathf.Max(0f, m_StopDistance))
            {
                return Vector3.zero;
            }

            Vector3 planar = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            Vector3 planarDir = planar.sqrMagnitude > 0.0001f ? planar.normalized : Vector3.zero;

            Vector3 desired = planarDir * Mathf.Max(0.1f, m_MaxHorizontalSpeed);
            if (m_ChaseVertical)
            {
                float verticalDelta = toTarget.y;
                float verticalSign = Mathf.Sign(verticalDelta);
                float verticalScale = Mathf.Clamp01(Mathf.Abs(verticalDelta) / 5f);
                float verticalSpeed =
                    verticalSign >= 0f
                        ? verticalScale * Mathf.Max(0.1f, m_MaxClimbSpeed)
                        : -verticalScale * Mathf.Max(0.1f, m_MaxDiveSpeed);
                desired.y = verticalSpeed;
            }

            return desired;
        }

        private void UpdateRotation(Vector3 velocity)
        {
            Vector3 lookDirection = velocity;
            if (lookDirection.sqrMagnitude < 0.0001f && m_CurrentTarget != null)
            {
                lookDirection = GetTargetAimPoint(m_CurrentTarget) - transform.position;
            }

            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (!m_ChaseVertical)
            {
                lookDirection.y = 0f;
            }

            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            float step = Mathf.Max(1f, m_RotationDegreesPerSecond) * Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, step);
        }

        private void UpdateAnimator(Vector3 velocity, bool chasing)
        {
            if (m_Animator == null)
            {
                return;
            }

            float speed = velocity.magnitude;
            float normalizedSpeed = Mathf.Clamp01(speed / Mathf.Max(0.1f, m_MaxHorizontalSpeed));
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            float inputX = Mathf.Clamp(
                localVelocity.x / Mathf.Max(0.1f, m_MaxHorizontalSpeed),
                -1f,
                1f
            );
            float inputY = Mathf.Clamp(
                localVelocity.z / Mathf.Max(0.1f, m_MaxHorizontalSpeed),
                -1f,
                1f
            );

            m_Animator.SetFloat(AnimSpeed, normalizedSpeed, 0.1f, Time.deltaTime);
            m_Animator.SetFloat(AnimMotionSpeed, normalizedSpeed, 0.1f, Time.deltaTime);
            m_Animator.SetFloat(AnimInputX, inputX, 0.1f, Time.deltaTime);
            m_Animator.SetFloat(AnimInputY, inputY, 0.1f, Time.deltaTime);

            bool flying = chasing && (m_ChaseVertical || Mathf.Abs(velocity.y) > 0.25f);
            m_Animator.SetBool(AnimFlying, flying);
            m_Animator.SetBool(AnimGrounded, !flying);
            m_Animator.SetBool(AnimFreeFall, flying);
        }

        private Vector3 GetTargetAimPoint(NetworkObject target)
        {
            if (target == null)
            {
                return transform.position;
            }

            return target.transform.position
                + new Vector3(0f, Mathf.Max(0f, m_TargetHeightOffset), 0f);
        }

        private void SetState(ChaseState next)
        {
            if (m_State == next)
            {
                return;
            }

            m_State = next;
            if (m_LogDebug)
            {
                NGLog.Debug(
                    "NPCChase",
                    NGLog.Format(
                        "State changed",
                        ("npc", name),
                        ("state", m_State.ToString()),
                        ("target", m_CurrentTarget != null ? m_CurrentTarget.name : "<none>")
                    )
                );
            }
        }

        private void ApplyIntegrationFlags(bool chasingActive)
        {
            if (m_DisableNavMeshAgent && m_NavMeshAgent != null)
            {
                m_NavMeshAgent.enabled = !chasingActive;
            }

            if (m_DisableWhileChasing == null || m_DisableWhileChasing.Length == 0)
            {
                return;
            }

            for (int i = 0; i < m_DisableWhileChasing.Length; i++)
            {
                MonoBehaviour behaviour = m_DisableWhileChasing[i];
                if (behaviour == null || behaviour == this)
                {
                    continue;
                }

                behaviour.enabled = !chasingActive;
            }

            if (m_LogDebug && m_LastLoggedEnabledState != chasingActive)
            {
                m_LastLoggedEnabledState = chasingActive;
                NGLog.Debug(
                    "NPCChase",
                    NGLog.Format(
                        "Integration flags applied",
                        ("npc", name),
                        ("chaseActive", chasingActive),
                        ("disableNavMesh", m_DisableNavMeshAgent)
                    )
                );
            }
        }
    }
}
