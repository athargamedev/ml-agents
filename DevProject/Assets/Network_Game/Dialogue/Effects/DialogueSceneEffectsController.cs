using System;
using System.Collections;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using Network_Game.Dialogue.Effects;
using Network_Game.Dialogue.MCP;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Network_Game.Dialogue
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-460)]
    public class DialogueSceneEffectsController : MonoBehaviour
    {
        public static DialogueSceneEffectsController Instance { get; private set; }
        public static event Action<AppliedEffectInfo> OnEffectApplied;

        public struct AppliedEffectInfo
        {
            public string EffectType;
            public string EffectName;
            public ulong SourceNetworkObjectId;
            public ulong TargetNetworkObjectId;
            public Vector3 Position;
            public float Scale;
            public float DurationSeconds;
            public bool AttachToTarget;
            public bool FitToTargetMesh;
            public float AppliedAtRealtime;
            public float FeedbackDelaySeconds;
        }

        /// <summary>
        /// Get list of available effect types. Reads from EffectCatalog when available.
        /// </summary>
        public static string[] GetAvailableEffects()
        {
            var catalog = Effects.EffectCatalog.Instance ?? Effects.EffectCatalog.Load();
            if (catalog != null && catalog.allEffects != null && catalog.allEffects.Count > 0)
            {
                var tags = new List<string>(catalog.allEffects.Count);
                for (int i = 0; i < catalog.allEffects.Count; i++)
                {
                    var effect = catalog.allEffects[i];
                    if (effect != null && !string.IsNullOrWhiteSpace(effect.effectTag))
                        tags.Add(effect.effectTag);
                }
                if (tags.Count > 0)
                    return tags.ToArray();
            }
            // Fallback when catalog is unavailable at call time
            return new[]
            {
                "bored_lighting",
                "prop_spawn",
                "wall_image",
                "rain_burst",
                "shockwave",
                "shield_bubble",
                "waypoint_ping",
                "prefab_power",
                "surface_material",
            };
        }

        [SerializeField]
        private Light[] m_TargetLights;

        [SerializeField]
        private bool m_FindLightsIfEmpty = true;

        [SerializeField]
        [Min(0f)]
        private float m_DefaultTransitionSeconds = 0.35f;

        [Header("Prefab Power Templates")]
        [SerializeField]
        [Tooltip("Drag ParticlePack prefabs here. Resolved by name at runtime.")]
        private GameObject[] m_PrefabPowerTemplates = new GameObject[0];

        [SerializeField]
        private bool m_LogDebug;

        [Header("Gameplay Damage")]
        [SerializeField]
        [Tooltip(
            "When enabled, gameplay-damage effects also damage targets on particle collisions."
         )]
        private bool m_EnableParticleCollisionDamage = true;

        [SerializeField]
        [Range(0.05f, 2f)]
        [Tooltip("Multiplier applied to configured effect damage for each collision hit.")]
        private float m_ParticleCollisionDamageScale = 0.35f;

        [SerializeField]
        [Min(0.02f)]
        [Tooltip("Minimum delay between collision damage ticks per target.")]
        private float m_ParticleCollisionHitCooldownSeconds = 0.2f;

        [SerializeField]
        [Tooltip(
            "Layer mask used by particle collision module when gameplay damage is enabled. Use Player/Environment layers here."
         )]
        private LayerMask m_ParticleCollisionLayerMask = ~0;

        [SerializeField]
        [Tooltip(
            "Allow particle collisions against moving colliders (players, projectiles, rigidbodies)."
         )]
        private bool m_ParticleCollisionEnableDynamicColliders = true;

        [SerializeField]
        [HideInInspector]
        private bool m_ParticleCollisionDamageInitialized;

        [Header("Player Special FX")]
        [SerializeField]
        [Min(0.05f)]
        private float m_DissolveFadeOutSeconds = 1.4f;

        [SerializeField]
        [Min(0.05f)]
        private float m_DissolveFadeInSeconds = 1.2f;

        [SerializeField]
        private AnimationCurve m_DissolveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Surface Material FX")]
        [SerializeField]
        [Tooltip(
            "Material used for deterministic floor-freeze swaps (for example IceBall or a floor-tuned IceBall copy)."
         )]
        private Material m_FloorFreezeMaterial;

        [SerializeField]
        [Min(0.25f)]
        [Tooltip(
            "Default duration for floor-freeze material overrides when no explicit duration is provided."
         )]
        private float m_FloorFreezeDefaultDurationSeconds = 8f;

        [Header("Feedback Prompt Blocking")]
        [SerializeField]
        [Tooltip(
            "Queue scene effects while the feedback prompt is open and replay them when the prompt closes."
         )]
        private bool m_QueueEffectsWhileFeedbackPromptOpen = true;

        [SerializeField]
        [Min(1)]
        [Tooltip("Maximum number of scene effects buffered while the feedback prompt is blocking.")]
        private int m_MaxDeferredEffects = 48;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Drop buffered effects older than this many seconds. Set to 0 to disable aging.")]
        private float m_DeferredEffectMaxAgeSeconds = 10f;

        [SerializeField]
        [Min(1)]
        [Tooltip("How many buffered effects may be replayed per frame after the prompt closes.")]
        private int m_MaxDeferredEffectsToDrainPerFrame = 8;

        private Color[] m_DefaultColors = new Color[0];
        private float[] m_DefaultIntensities = new float[0];
        private Coroutine m_TransitionRoutine;
        private bool m_WarnedMissingLights;
        private Dictionary<string, GameObject> m_PrefabPowerLookup;
        private bool m_ProfilePrefabCacheBuilt;
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> m_AddressableHandles =
            new Dictionary<string, AsyncOperationHandle<GameObject>>(
                StringComparer.OrdinalIgnoreCase
            );
        private readonly Dictionary<ulong, Coroutine> m_ActiveDissolveRoutines =
            new Dictionary<ulong, Coroutine>();
        private readonly Dictionary<ulong, RendererFadeState[]> m_ActiveDissolveStates =
            new Dictionary<ulong, RendererFadeState[]>();
        private readonly Dictionary<int, Coroutine> m_ActiveObjectHideRoutines =
            new Dictionary<int, Coroutine>();
        private readonly Dictionary<int, RendererFadeState[]> m_ActiveObjectHideStates =
            new Dictionary<int, RendererFadeState[]>();
        private readonly Dictionary<string, Coroutine> m_ActiveSurfaceMaterialRoutines =
            new Dictionary<string, Coroutine>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            SurfaceMaterialOverrideState
        > m_ActiveSurfaceMaterialStates = new Dictionary<string, SurfaceMaterialOverrideState>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, EffectDefinition> m_EffectDefinitionByPrefabName =
            new Dictionary<string, EffectDefinition>(StringComparer.OrdinalIgnoreCase);
        private bool m_EffectDefinitionLookupBuilt;
        private readonly Queue<DeferredEffectRequest> m_DeferredEffects =
            new Queue<DeferredEffectRequest>();
        private bool m_WasFeedbackPromptBlockingLastFrame;
        private static readonly string[] s_FloorNameHints =
        {
            "floor",
            "ground",
            "terrain",
            "stairs",
            "stair",
        };
        private const float kMinDissolveFadeOutSeconds = 1.2f;
        private const float kMinDissolveFadeInSeconds = 1.0f;
        private const float kMinDissolveHoldSeconds = 0.2f;

        private sealed class DeferredEffectRequest
        {
            public string EffectType;
            public string EffectName;
            public ulong SourceNetworkObjectId;
            public ulong TargetNetworkObjectId;
            public float EnqueuedAtRealtime;
            public Action Execute;
        }

        private sealed class RendererFadeState
        {
            public Renderer Renderer;
            public bool WasEnabled;
            public MaterialFadeState[] Materials;
        }

        private sealed class MaterialFadeState
        {
            public Material Material;
            public string ColorPropertyName;
            public Color OriginalColor;
            public int OriginalRenderQueue;
            public bool HasSurface;
            public float Surface;
            public bool HasMode;
            public float Mode;
            public bool HasSrcBlend;
            public float SrcBlend;
            public bool HasDstBlend;
            public float DstBlend;
            public bool HasZWrite;
            public float ZWrite;
            public bool KeywordAlphaTest;
            public bool KeywordAlphaBlend;
            public bool KeywordAlphaPremultiply;
        }

        private sealed class SurfaceMaterialOverrideState
        {
            public string Key;
            public string SurfaceId;
            public string RendererPath;
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public int MaterialSlotIndex;
            public float AppliedAtRealtime;
        }

        private struct EffectPreflightState
        {
            public Vector3 Position;
            public Vector3 Forward;
            public float Scale;
            public float DurationSeconds;
            public float DamageRadius;
            public bool AttachToTarget;
            public bool FitToTargetMesh;
            public Transform TargetTransform;
            public string Reason;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                return;
            }

            // One-time migration for existing scene instances so new collision damage
            // settings default to enabled without requiring manual inspector edits.
            if (!m_ParticleCollisionDamageInitialized)
            {
                m_EnableParticleCollisionDamage = true;
                m_ParticleCollisionDamageScale = 0.35f;
                m_ParticleCollisionHitCooldownSeconds = 0.2f;
                m_ParticleCollisionDamageInitialized = true;
            }

            Instance = this;
            EnsureLights();
            CaptureDefaults();
        }

        private void Update()
        {
            DrainDeferredEffectsIfReady();
        }

        private void DrainDeferredEffectsIfReady()
        {
            bool isBlocking = DialogueEffectFeedbackPrompt.IsBlockingPromptActive;
            if (isBlocking)
            {
                m_WasFeedbackPromptBlockingLastFrame = true;
                return;
            }

            if (m_DeferredEffects.Count == 0)
            {
                m_WasFeedbackPromptBlockingLastFrame = false;
                return;
            }

            int drained = 0;
            int maxPerFrame = Mathf.Max(1, m_MaxDeferredEffectsToDrainPerFrame);
            while (drained < maxPerFrame && m_DeferredEffects.Count > 0)
            {
                if (DialogueEffectFeedbackPrompt.IsBlockingPromptActive)
                {
                    m_WasFeedbackPromptBlockingLastFrame = true;
                    break;
                }

                DeferredEffectRequest request = m_DeferredEffects.Peek();
                if (IsDeferredEffectExpired(request))
                {
                    m_DeferredEffects.Dequeue();
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "Dropped stale deferred effect",
                            ("type", request.EffectType ?? string.Empty),
                            ("name", request.EffectName ?? string.Empty),
                            (
                                "ageSec",
                                (Time.realtimeSinceStartup - request.EnqueuedAtRealtime).ToString(
                                    "F2"
                                )
                            )
                        )
                    );
                    continue;
                }

                m_DeferredEffects.Dequeue();
                try
                {
                    request.Execute?.Invoke();
                }
                catch (Exception ex)
                {
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "Deferred effect replay failed",
                            ("type", request.EffectType ?? string.Empty),
                            ("name", request.EffectName ?? string.Empty),
                            ("error", ex.Message ?? string.Empty)
                        )
                    );
                }

                drained++;
            }

            if (m_LogDebug && drained > 0)
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Replayed deferred effects",
                        ("count", drained),
                        ("remaining", m_DeferredEffects.Count),
                        ("afterPrompt", m_WasFeedbackPromptBlockingLastFrame)
                    )
                );
            }

            m_WasFeedbackPromptBlockingLastFrame = false;
        }

        private bool TryHandleFeedbackPromptBlocking(
            string effectType,
            string effectName,
            Action execute,
            ulong sourceNetworkObjectId = 0,
            ulong targetNetworkObjectId = 0
        )
        {
            if (!DialogueEffectFeedbackPrompt.IsBlockingPromptActive)
            {
                return false;
            }

            if (!m_QueueEffectsWhileFeedbackPromptOpen)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Skipped effect while feedback prompt is active",
                        ("type", effectType ?? string.Empty),
                        ("name", effectName ?? string.Empty),
                        ("source", sourceNetworkObjectId),
                        ("target", targetNetworkObjectId)
                    )
                );
                return true;
            }

            EnqueueDeferredEffect(
                effectType,
                effectName,
                execute,
                sourceNetworkObjectId,
                targetNetworkObjectId
            );
            return true;
        }

        private void EnqueueDeferredEffect(
            string effectType,
            string effectName,
            Action execute,
            ulong sourceNetworkObjectId,
            ulong targetNetworkObjectId
        )
        {
            if (execute == null)
            {
                return;
            }

            int maxDeferred = Mathf.Max(1, m_MaxDeferredEffects);
            while (m_DeferredEffects.Count >= maxDeferred)
            {
                DeferredEffectRequest dropped = m_DeferredEffects.Dequeue();
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Deferred effect queue full; dropped oldest",
                        ("type", dropped.EffectType ?? string.Empty),
                        ("name", dropped.EffectName ?? string.Empty),
                        ("queued", maxDeferred)
                    )
                );
            }

            m_DeferredEffects.Enqueue(
                new DeferredEffectRequest
                {
                    EffectType = effectType ?? string.Empty,
                    EffectName = effectName ?? string.Empty,
                    SourceNetworkObjectId = sourceNetworkObjectId,
                    TargetNetworkObjectId = targetNetworkObjectId,
                    EnqueuedAtRealtime = Time.realtimeSinceStartup,
                    Execute = execute,
                }
            );

            if (m_LogDebug)
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Queued effect while feedback prompt is active",
                        ("type", effectType ?? string.Empty),
                        ("name", effectName ?? string.Empty),
                        ("source", sourceNetworkObjectId),
                        ("target", targetNetworkObjectId),
                        ("queued", m_DeferredEffects.Count)
                    )
                );
            }
        }

        private bool IsDeferredEffectExpired(DeferredEffectRequest request)
        {
            float maxAgeSeconds = Mathf.Max(0f, m_DeferredEffectMaxAgeSeconds);
            if (maxAgeSeconds <= 0f)
            {
                return false;
            }

            return (Time.realtimeSinceStartup - request.EnqueuedAtRealtime) > maxAgeSeconds;
        }

        public void ApplyBoredLighting(Color color, float intensity, float transitionSeconds = 0f)
        {
            if (
                TryHandleFeedbackPromptBlocking(
                    "bored_lighting",
                    "bored_lighting",
                    () => ApplyBoredLighting(color, intensity, transitionSeconds)
                )
            )
            {
                return;
            }

            EnsureLights();
            if (m_TargetLights == null || m_TargetLights.Length == 0)
            {
                if (!m_WarnedMissingLights)
                {
                    m_WarnedMissingLights = true;
                    NGLog.Warn(
                        "DialogueFX",
                        "No lights available for DialogueSceneEffectsController."
                    );
                }
                return;
            }

            float duration =
                transitionSeconds > 0f ? transitionSeconds : m_DefaultTransitionSeconds;
            StartTransition(color, Mathf.Max(0f, intensity), duration);
            EmitEffectApplied(
                new AppliedEffectInfo
                {
                    EffectType = "bored_lighting",
                    EffectName = "bored_lighting",
                    Scale = Mathf.Max(0f, intensity),
                    DurationSeconds = duration,
                    AppliedAtRealtime = Time.realtimeSinceStartup,
                    FeedbackDelaySeconds = Mathf.Clamp(duration * 0.35f, 0.05f, 0.3f),
                }
            );

            if (m_LogDebug)
            {
                NGLog.Info(
                    "DialogueFX",
                    NGLog.Format(
                        "Applied bored lighting",
                        ("lights", m_TargetLights.Length),
                        ("color", color),
                        ("intensity", intensity),
                        ("duration", duration)
                    )
                );
            }
        }

        public void ApplyPrefabPower(
            string prefabName,
            Vector3 position,
            Vector3 forward,
            float scale,
            float durationSeconds,
            Color color,
            bool useColorOverride,
            bool enableGameplayDamage = false,
            bool enableHoming = false,
            float projectileSpeed = 10f,
            float homingTurnRateDegrees = 240f,
            float damageAmount = 0f,
            float damageRadius = 1f,
            bool affectPlayerOnly = false,
            string damageType = "effect",
            ulong sourceNetworkObjectId = 0,
            ulong targetNetworkObjectId = 0,
            bool attachToTarget = false,
            bool fitToTargetMesh = false,
            float serverSpawnTimeSeconds = -1f,
            uint effectSeed = 0
        )
        {
            if (
                TryHandleFeedbackPromptBlocking(
                    "prefab_power",
                    prefabName,
                    () =>
                        ApplyPrefabPower(
                            prefabName,
                            position,
                            forward,
                            scale,
                            durationSeconds,
                            color,
                            useColorOverride,
                            enableGameplayDamage,
                            enableHoming,
                            projectileSpeed,
                            homingTurnRateDegrees,
                            damageAmount,
                            damageRadius,
                            affectPlayerOnly,
                            damageType,
                            sourceNetworkObjectId,
                            targetNetworkObjectId,
                            attachToTarget,
                            fitToTargetMesh,
                            serverSpawnTimeSeconds,
                            effectSeed
                        ),
                    sourceNetworkObjectId,
                    targetNetworkObjectId
                )
            )
            {
                return;
            }

            GameObject prefab = ResolvePrefabPower(prefabName);
            if (prefab == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    $"Prefab power '{prefabName}' not found. Skipping effect."
                );
                return;
            }

            Vector3 spawnForward =
                forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(spawnForward, Vector3.up);

            GameObject instance = Instantiate(prefab, position, rotation);
            if (instance == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Prefab instantiate returned null",
                        ("prefab", prefabName ?? string.Empty)
                    )
                );
                return;
            }

            instance.name = $"DialoguePrefabPower_{prefabName}";
            instance.SetActive(false);

            float clampedScale = Mathf.Max(0.1f, scale);
            float tunedDurationSeconds = Mathf.Max(0.5f, durationSeconds);
            Transform targetTransform = ResolveNetworkTargetTransform(targetNetworkObjectId);
            if (
                DialogueEffectFeedbackRuntimeTuner.TryGetAdjustment(
                    prefabName,
                    "prefab_power",
                    out DialogueEffectFeedbackRuntimeTuner.TuningAdjustment tuning
                )
            )
            {
                clampedScale = Mathf.Clamp(clampedScale * tuning.ScaleMultiplier, 0.1f, 50f);
                tunedDurationSeconds = Mathf.Clamp(
                    tunedDurationSeconds * tuning.DurationMultiplier,
                    0.5f,
                    30f
                );
                // Keep placement authority server-side: runtime tuner can refine
                // scale/duration, but must not flip area/projectile effects into
                // mesh-attached effects.
                if (targetTransform != null && attachToTarget && tuning.PreferAttachToTarget)
                {
                    attachToTarget = true;
                }
                if (targetTransform != null && fitToTargetMesh && tuning.PreferFitToTargetMesh)
                {
                    attachToTarget = true;
                    fitToTargetMesh = true;
                }

                if (m_LogDebug)
                {
                    NGLog.Info(
                        "DialogueFX",
                        NGLog.Format(
                            "Applied adaptive visual tuning",
                            ("prefab", prefabName ?? string.Empty),
                            ("samples", tuning.SampleCount),
                            ("scaleMul", tuning.ScaleMultiplier.ToString("F3")),
                            ("durationMul", tuning.DurationMultiplier.ToString("F3")),
                            ("preferAttach", tuning.PreferAttachToTarget),
                            ("preferFitMesh", tuning.PreferFitToTargetMesh)
                        )
                    );
                }
            }

            EffectPreflightState preflight = ValidatePrefabPowerPreflight(
                prefabName,
                prefab,
                position,
                spawnForward,
                clampedScale,
                tunedDurationSeconds,
                damageRadius,
                targetTransform,
                attachToTarget,
                fitToTargetMesh
            );

            position = preflight.Position;
            spawnForward = preflight.Forward;
            clampedScale = preflight.Scale;
            tunedDurationSeconds = preflight.DurationSeconds;
            damageRadius = preflight.DamageRadius;
            targetTransform = preflight.TargetTransform;
            attachToTarget = preflight.AttachToTarget;
            fitToTargetMesh = preflight.FitToTargetMesh;
            rotation = Quaternion.LookRotation(spawnForward, Vector3.up);

            if (
                attachToTarget
                && fitToTargetMesh
                && targetTransform != null
                && TryResolveTargetBounds(targetTransform, out Bounds targetBounds)
            )
            {
                clampedScale = Mathf.Clamp(
                    clampedScale * ComputeMeshFitScale(targetBounds),
                    0.1f,
                    60f
                );
                position = targetBounds.center;
            }
            else if (attachToTarget && targetTransform != null)
            {
                position = targetTransform.position;
            }
            else if (targetTransform != null && position.y - targetTransform.position.y > 5f)
            {
                // Non-attached effects should not inherit stale elevated probe positions.
                Vector3 anchorForward = Vector3.ProjectOnPlane(targetTransform.forward, Vector3.up);
                if (anchorForward.sqrMagnitude < 0.0001f)
                {
                    anchorForward = Vector3.ProjectOnPlane(spawnForward, Vector3.up);
                }

                if (anchorForward.sqrMagnitude < 0.0001f)
                {
                    anchorForward = Vector3.forward;
                }

                position = targetTransform.position + anchorForward.normalized * 2f;
            }

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = prefab.transform.localScale * clampedScale;

            if (attachToTarget && targetTransform != null)
            {
                instance.transform.SetParent(targetTransform, true);
            }

            // When scale is significantly larger than default, also boost emission rate
            // and shape radius so the particle density matches the visual coverage.
            float emissionScaleFactor = clampedScale > 1.5f ? Mathf.Sqrt(clampedScale) : 1f;
            bool enableProjectileBehavior = enableGameplayDamage || enableHoming;
            bool enableParticleCollisionDamage =
                m_EnableParticleCollisionDamage && enableGameplayDamage && damageAmount > 0f;
            bool restrictDamageToTarget =
                targetNetworkObjectId != 0UL
                && (attachToTarget || fitToTargetMesh || enableHoming || affectPlayerOnly);
            LayerMask particleCollisionLayerMask = ResolveDamageCollisionLayerMask(
                targetTransform,
                affectPlayerOnly,
                restrictDamageToTarget
            );
            float catchUpSeconds = ResolveEffectCatchUpSeconds(serverSpawnTimeSeconds);
            uint baseSeed =
                effectSeed == 0U ? (uint)UnityEngine.Random.Range(1, int.MaxValue) : effectSeed;

            float duration = tunedDurationSeconds;
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            float maxLifetime = duration;

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                ParticleSystem.MainModule main = ps.main;
                main.loop = enableProjectileBehavior;
                main.playOnAwake = false;
                main.duration = duration;

                if (useColorOverride)
                {
                    main.startColor = color;
                }

                ps.useAutoRandomSeed = false;
                ps.randomSeed = baseSeed + (uint)(i * 9973);

                if (enableParticleCollisionDamage)
                {
                    EnsureParticleCollisionMessaging(
                        ps,
                        particleCollisionLayerMask,
                        m_ParticleCollisionEnableDynamicColliders
                    );
                }

                // Scale emission to maintain particle density at larger scales
                if (emissionScaleFactor > 1f)
                {
                    ParticleSystem.EmissionModule emission = ps.emission;
                    emission.rateOverTime = ScaleCurve(emission.rateOverTime, emissionScaleFactor);
                    emission.rateOverDistance = ScaleCurve(
                        emission.rateOverDistance,
                        emissionScaleFactor
                    );
                    main.maxParticles = Mathf.Max(
                        main.maxParticles,
                        Mathf.RoundToInt(main.maxParticles * emissionScaleFactor)
                    );
                }

                float systemLifetime =
                    duration + ResolveMaxStartLifetimeSeconds(main.startLifetime);
                if (systemLifetime > maxLifetime)
                {
                    maxLifetime = systemLifetime;
                }
            }

            instance.SetActive(true);
            NGLog.Info(
                "DialogueFX",
                NGLog.Format(
                    "Prefab power instance spawned",
                    ("prefab", prefabName ?? string.Empty),
                    ("name", instance.name),
                    ("source", sourceNetworkObjectId),
                    ("target", targetNetworkObjectId)
                )
            );

            // Tag this instance so DialogueMCPBridge.GetActiveVfxState() can discover it.
            var spawnedMarker = instance.AddComponent<DialogueSpawnedMarker>();
            spawnedMarker.EffectTag = prefabName ?? string.Empty;
            spawnedMarker.SourceNetworkObjectId = sourceNetworkObjectId;
            spawnedMarker.TargetNetworkObjectId = targetNetworkObjectId;
            spawnedMarker.ConfiguredScale = clampedScale;
            spawnedMarker.ConfiguredDurationSeconds = duration;
            spawnedMarker.AttachToTarget = attachToTarget;
            spawnedMarker.FitToTargetMesh = fitToTargetMesh;
            spawnedMarker.EffectSeed = baseSeed;

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }

                ps.Clear(true);
                ps.Play(true);
                if (catchUpSeconds > 0.001f)
                {
                    float simulateSeconds = Mathf.Clamp(catchUpSeconds, 0f, maxLifetime);
                    ps.Simulate(simulateSeconds, true, false, true);
                }
            }

            if (enableParticleCollisionDamage)
            {
                ConfigureParticleCollisionDamage(
                    systems,
                    damageAmount,
                    affectPlayerOnly,
                    damageType,
                    sourceNetworkObjectId,
                    targetNetworkObjectId,
                    restrictDamageToTarget
                );
            }

            if (enableProjectileBehavior)
            {
                DialogueEffectProjectile projectile =
                    instance.GetComponent<DialogueEffectProjectile>();
                if (projectile == null)
                {
                    projectile = instance.AddComponent<DialogueEffectProjectile>();
                }

                projectile.Configure(
                    sourceNetworkObjectId,
                    targetNetworkObjectId,
                    targetTransform,
                    enableHoming,
                    projectileSpeed,
                    homingTurnRateDegrees,
                    damageAmount,
                    damageRadius,
                    duration,
                    affectPlayerOnly,
                    restrictDamageToTarget,
                    damageType,
                    m_LogDebug
                );
            }
            else
            {
                StartCoroutine(DestroyParticleSystemAfter(instance, maxLifetime + 1f));
            }

            if (m_LogDebug)
            {
                NGLog.Info(
                    "DialogueFX",
                    NGLog.Format(
                        "Applied prefab power",
                        ("prefab", prefabName),
                        ("scale", clampedScale),
                        ("duration", duration),
                        ("systems", systems.Length),
                        ("colorOverride", useColorOverride),
                        ("gameplay", enableGameplayDamage),
                        ("homing", enableHoming),
                        ("source", sourceNetworkObjectId),
                        ("target", targetNetworkObjectId),
                        ("strictTarget", restrictDamageToTarget),
                        ("seed", baseSeed),
                        ("catchUp", catchUpSeconds.ToString("F3")),
                        ("damage", damageAmount),
                        ("attached", attachToTarget),
                        ("fitToMesh", fitToTargetMesh)
                    )
                );
            }

            float feedbackDelaySeconds = Mathf.Clamp(duration * 0.25f, 0.35f, 1.1f);
            EmitEffectApplied(
                new AppliedEffectInfo
                {
                    EffectType = "prefab_power",
                    EffectName = prefabName ?? string.Empty,
                    SourceNetworkObjectId = sourceNetworkObjectId,
                    TargetNetworkObjectId = targetNetworkObjectId,
                    Position = position,
                    Scale = clampedScale,
                    DurationSeconds = duration,
                    AttachToTarget = attachToTarget,
                    FitToTargetMesh = fitToTargetMesh,
                    AppliedAtRealtime = Time.realtimeSinceStartup,
                    FeedbackDelaySeconds = feedbackDelaySeconds,
                }
            );
            NGLog.Info(
                "DialogueFX",
                NGLog.Format(
                    "Prefab power feedback emitted",
                    ("prefab", prefabName ?? string.Empty),
                    ("feedbackDelay", feedbackDelaySeconds.ToString("F2"))
                )
            );
        }

        private static bool TryResolveTargetBounds(Transform targetTransform, out Bounds bounds)
        {
            bounds = default;
            if (targetTransform == null)
            {
                return false;
            }

            Renderer[] renderers = targetTransform.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private static float ComputeMeshFitScale(Bounds bounds)
        {
            float height = Mathf.Max(0.1f, bounds.size.y);
            float width = Mathf.Max(0.1f, Mathf.Max(bounds.size.x, bounds.size.z));
            float fit = Mathf.Max(height * 0.45f, width * 0.8f);
            return Mathf.Clamp(fit, 0.5f, 4f);
        }

        private static void EnsureParticleCollisionMessaging(
            ParticleSystem particleSystem,
            LayerMask collisionLayerMask,
            bool enableDynamicColliders
        )
        {
            if (particleSystem == null)
            {
                return;
            }

            ParticleSystem.CollisionModule collision = particleSystem.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = collisionLayerMask;
            collision.enableDynamicColliders = enableDynamicColliders;
            collision.sendCollisionMessages = true;
        }

        private LayerMask ResolveDamageCollisionLayerMask(
            Transform targetTransform,
            bool affectPlayerOnly,
            bool restrictDamageToTarget
        )
        {
            if (m_ParticleCollisionLayerMask.value == 0)
            {
                m_ParticleCollisionLayerMask = ~0;
            }

            bool preferTargetLayer = affectPlayerOnly || restrictDamageToTarget;
            if (!preferTargetLayer || targetTransform == null)
            {
                return m_ParticleCollisionLayerMask;
            }

            int targetLayer = targetTransform.gameObject.layer;
            if (targetLayer < 0 || targetLayer > 31)
            {
                return m_ParticleCollisionLayerMask;
            }

            return 1 << targetLayer;
        }

        private void ConfigureParticleCollisionDamage(
            ParticleSystem[] systems,
            float damageAmount,
            bool affectPlayerOnly,
            string damageType,
            ulong sourceNetworkObjectId,
            ulong targetNetworkObjectId,
            bool restrictDamageToTarget
        )
        {
            if (systems == null || systems.Length == 0)
            {
                return;
            }

            float damagePerHit = Mathf.Max(0.05f, damageAmount * m_ParticleCollisionDamageScale);
            float hitCooldown = Mathf.Max(0.02f, m_ParticleCollisionHitCooldownSeconds);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }

                DialogueParticleCollisionDamage collisionDamage =
                    ps.GetComponent<DialogueParticleCollisionDamage>();
                if (collisionDamage == null)
                {
                    collisionDamage = ps.gameObject.AddComponent<DialogueParticleCollisionDamage>();
                }

                float proximityRadius = EstimateParticleDamageRadius(ps);
                collisionDamage.Configure(
                    sourceNetworkObjectId,
                    targetNetworkObjectId,
                    restrictDamageToTarget,
                    damagePerHit,
                    hitCooldown,
                    proximityRadius,
                    affectPlayerOnly,
                    damageType,
                    m_LogDebug
                );
            }

            if (m_LogDebug)
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format(
                        "Configured particle collision damage",
                        ("systems", systems.Length),
                        ("damagePerHit", damagePerHit.ToString("F2")),
                        ("cooldown", hitCooldown.ToString("F2")),
                        ("source", sourceNetworkObjectId),
                        ("target", targetNetworkObjectId),
                        ("strictTarget", restrictDamageToTarget),
                        ("type", damageType ?? "effect")
                    )
                );
            }
        }

        private static float EstimateParticleDamageRadius(ParticleSystem ps)
        {
            if (ps == null)
            {
                return 0.5f;
            }

            float lossyScale = Mathf.Max(
                ps.transform.lossyScale.x,
                Mathf.Max(ps.transform.lossyScale.y, ps.transform.lossyScale.z)
            );
            if (lossyScale <= 0f)
            {
                lossyScale = 1f;
            }

            float sizeRadius = 0.35f;
            ParticleSystem.MainModule main = ps.main;
            sizeRadius = Mathf.Max(sizeRadius, ResolveCurveMax(main.startSize) * 0.8f * lossyScale);

            ParticleSystem.ShapeModule shape = ps.shape;
            if (shape.enabled)
            {
                sizeRadius = Mathf.Max(sizeRadius, shape.radius * lossyScale);
            }

            return Mathf.Clamp(sizeRadius, 0.2f, 4f);
        }

        private static float ResolveEffectCatchUpSeconds(float serverSpawnTimeSeconds)
        {
            if (serverSpawnTimeSeconds <= 0f)
            {
                return 0f;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                return 0f;
            }

            float localServerTime = networkManager.ServerTime.TimeAsFloat;
            return Mathf.Clamp(localServerTime - serverSpawnTimeSeconds, 0f, 2f);
        }

        public void RestoreDefaults(float transitionSeconds = 0f)
        {
            EnsureLights();
            if (
                m_TargetLights == null
                || m_TargetLights.Length == 0
                || m_DefaultColors == null
                || m_DefaultColors.Length != m_TargetLights.Length
            )
            {
                return;
            }

            float duration =
                transitionSeconds > 0f ? transitionSeconds : m_DefaultTransitionSeconds;
            if (m_TransitionRoutine != null)
            {
                StopCoroutine(m_TransitionRoutine);
            }
            m_TransitionRoutine = StartCoroutine(TransitionToDefaults(duration));
        }

        private static Transform ResolveNetworkTargetTransform(ulong networkObjectId)
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
                return networkObject.transform;
            }

            return null;
        }

        private EffectPreflightState ValidatePrefabPowerPreflight(
            string prefabName,
            GameObject prefab,
            Vector3 position,
            Vector3 forward,
            float scale,
            float durationSeconds,
            float damageRadius,
            Transform targetTransform,
            bool attachToTarget,
            bool fitToTargetMesh
        )
        {
            EffectPreflightState state = new EffectPreflightState
            {
                Position = position,
                Forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward,
                Scale = Mathf.Clamp(scale, 0.1f, 60f),
                DurationSeconds = Mathf.Clamp(durationSeconds, 0.1f, 60f),
                DamageRadius = Mathf.Clamp(damageRadius, 0.1f, 60f),
                AttachToTarget = attachToTarget,
                FitToTargetMesh = fitToTargetMesh,
                TargetTransform = targetTransform,
                Reason = string.Empty,
            };

            List<string> reasons = null;
            if (TryResolveEffectDefinitionForPrefab(prefabName, prefab, out EffectDefinition def))
            {
                state.Scale = Mathf.Clamp(state.Scale, def.minScale, def.maxScale);
                state.DurationSeconds = Mathf.Clamp(
                    state.DurationSeconds,
                    def.minDuration,
                    def.maxDuration
                );
                state.DamageRadius = Mathf.Clamp(state.DamageRadius, def.minRadius, def.maxRadius);

                switch (def.placementMode)
                {
                    case EffectPlacementMode.AttachMesh:
                        state.AttachToTarget = true;
                        state.FitToTargetMesh = def.preferFitTargetMesh;
                        break;
                    case EffectPlacementMode.GroundAoe:
                        state.AttachToTarget = false;
                        state.FitToTargetMesh = false;
                        if (TryProjectToGround(state.Position, out Vector3 groundPos))
                        {
                            state.Position = groundPos;
                        }
                        else if (state.TargetTransform != null)
                        {
                            Vector3 targetGroundProbe = state.TargetTransform.position;
                            if (TryProjectToGround(targetGroundProbe, out groundPos))
                            {
                                state.Position = groundPos;
                            }
                        }
                        break;
                    case EffectPlacementMode.SkyVolume:
                        state.AttachToTarget = false;
                        state.FitToTargetMesh = false;
                        Vector3 skyAnchor =
                            state.TargetTransform != null
                            ? state.TargetTransform.position
                            : state.Position;
                        state.Position = skyAnchor + Vector3.up * Mathf.Max(6f, state.Scale * 2f);
                        break;
                    case EffectPlacementMode.Projectile:
                        state.AttachToTarget = false;
                        state.FitToTargetMesh = false;
                        if (state.TargetTransform != null)
                        {
                            Vector3 toTarget = state.TargetTransform.position - state.Position;
                            if (toTarget.sqrMagnitude > 0.0001f)
                            {
                                state.Forward = toTarget.normalized;
                            }
                        }
                        break;
                }

                switch (def.targetType)
                {
                    case EffectTargetType.Floor:
                        state.AttachToTarget = false;
                        state.FitToTargetMesh = false;
                        Vector3 floorAnchor =
                            state.TargetTransform != null
                            ? state.TargetTransform.position
                            : state.Position;
                        if (TryProjectToGround(floorAnchor, out Vector3 floorPos))
                        {
                            state.Position = floorPos;
                        }
                        break;
                    case EffectTargetType.WorldPoint:
                        state.AttachToTarget = false;
                        state.FitToTargetMesh = false;
                        break;
                }
            }

            if (state.AttachToTarget && state.TargetTransform == null)
            {
                state.AttachToTarget = false;
                state.FitToTargetMesh = false;
                reasons ??= new List<string>(2);
                reasons.Add("missing_target_for_attach");
            }

            if (state.FitToTargetMesh)
            {
                if (
                    state.TargetTransform == null
                    || !TryResolveTargetBounds(state.TargetTransform, out _)
                )
                {
                    state.FitToTargetMesh = false;
                    reasons ??= new List<string>(2);
                    reasons.Add("mesh_fit_unavailable");
                }
            }

            if (!state.AttachToTarget && LooksLikeGroundEffect(prefabName))
            {
                if (TryProjectToGround(state.Position, out Vector3 projectedGround))
                {
                    state.Position = projectedGround;
                }
            }

            if (state.Forward.sqrMagnitude < 0.0001f)
            {
                state.Forward = Vector3.forward;
            }

            if (reasons != null && reasons.Count > 0)
            {
                state.Reason = string.Join(",", reasons);
                if (m_LogDebug)
                {
                    NGLog.Info(
                        "DialogueFX",
                        NGLog.Format(
                            "Prefab preflight fallback applied",
                            ("prefab", prefabName ?? string.Empty),
                            ("reason", state.Reason),
                            ("attach", state.AttachToTarget),
                            ("fitMesh", state.FitToTargetMesh)
                        )
                    );
                }
            }

            return state;
        }

        private static bool LooksLikeGroundEffect(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return false;
            }

            string lower = prefabName.ToLowerInvariant();
            return lower.Contains("ground")
                || lower.Contains("floor")
                || lower.Contains("freeze")
                || lower.Contains("frost")
                || lower.Contains("ice")
                || lower.Contains("snow")
                || lower.Contains("shockwave")
                || lower.Contains("blast")
                || lower.Contains("explosion");
        }

        private static bool TryProjectToGround(Vector3 source, out Vector3 groundedPosition)
        {
            Vector3 rayOrigin = source + Vector3.up * 6f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 30f))
            {
                groundedPosition = hit.point;
                return true;
            }

            groundedPosition = source;
            return false;
        }

        private bool TryResolveEffectDefinitionForPrefab(
            string prefabName,
            GameObject prefab,
            out EffectDefinition definition
        )
        {
            definition = null;
            EnsureEffectDefinitionLookup(false);

            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                if (m_EffectDefinitionByPrefabName.TryGetValue(prefabName, out definition))
                {
                    return true;
                }
            }

            if (
                prefab != null
                && m_EffectDefinitionByPrefabName.TryGetValue(prefab.name, out definition)
            )
            {
                return true;
            }

            EnsureEffectDefinitionLookup(true);
            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                if (m_EffectDefinitionByPrefabName.TryGetValue(prefabName, out definition))
                {
                    return true;
                }
            }

            if (
                prefab != null
                && m_EffectDefinitionByPrefabName.TryGetValue(prefab.name, out definition)
            )
            {
                return true;
            }

            return false;
        }

        private void EnsureEffectDefinitionLookup(bool forceRefresh)
        {
            if (m_EffectDefinitionLookupBuilt && !forceRefresh)
            {
                return;
            }

            m_EffectDefinitionLookupBuilt = true;
            m_EffectDefinitionByPrefabName.Clear();

            EffectCatalog catalog = EffectCatalog.Instance ?? EffectCatalog.Load();
            if (catalog == null || catalog.allEffects == null)
            {
                return;
            }

            for (int i = 0; i < catalog.allEffects.Count; i++)
            {
                EffectDefinition def = catalog.allEffects[i];
                if (def == null)
                {
                    continue;
                }

                if (def.effectPrefab != null && !string.IsNullOrWhiteSpace(def.effectPrefab.name))
                {
                    m_EffectDefinitionByPrefabName[def.effectPrefab.name] = def;
                }

                if (!string.IsNullOrWhiteSpace(def.effectTag))
                {
                    m_EffectDefinitionByPrefabName[def.effectTag.Trim()] = def;
                }
            }
        }

        private GameObject ResolvePrefabPower(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return null;
            }

            EnsurePrefabPowerLookup();

            if (m_PrefabPowerLookup.TryGetValue(prefabName, out GameObject cached))
            {
                return cached;
            }

            // NPC actors may be spawned after the first cache pass; refresh once on miss.
            EnsurePrefabPowerLookup(refreshProfileCache: true);
            if (m_PrefabPowerLookup.TryGetValue(prefabName, out cached))
            {
                return cached;
            }

            // Try Addressables first (sync wait — already loaded or fast local)
            GameObject loaded = TryLoadFromAddressables($"DialoguePowers/{prefabName}");
            if (loaded == null)
                loaded = TryLoadFromAddressables(prefabName);

            // Fallback to Resources.Load
            if (loaded == null)
                loaded = Resources.Load<GameObject>($"DialoguePowers/{prefabName}");
            if (loaded == null)
                loaded = Resources.Load<GameObject>(prefabName);

            if (loaded != null)
            {
                m_PrefabPowerLookup[prefabName] = loaded;
                if (!m_PrefabPowerLookup.ContainsKey(loaded.name))
                {
                    m_PrefabPowerLookup[loaded.name] = loaded;
                }
            }

            return loaded;
        }

        private void EnsurePrefabPowerLookup(bool refreshProfileCache = false)
        {
            if (m_PrefabPowerLookup == null)
            {
                m_PrefabPowerLookup = new Dictionary<string, GameObject>(
                    StringComparer.OrdinalIgnoreCase
                );
                refreshProfileCache = true;
            }

            if (m_PrefabPowerTemplates != null)
            {
                for (int i = 0; i < m_PrefabPowerTemplates.Length; i++)
                {
                    GameObject template = m_PrefabPowerTemplates[i];
                    if (template != null && !m_PrefabPowerLookup.ContainsKey(template.name))
                    {
                        m_PrefabPowerLookup[template.name] = template;
                    }
                }
            }

            if (!m_ProfilePrefabCacheBuilt || refreshProfileCache)
            {
                CacheProfilePrefabPowers();
            }
        }

        private void CacheProfilePrefabPowers()
        {
            m_ProfilePrefabCacheBuilt = true;
            int added = 0;

#if UNITY_2023_1_OR_NEWER
            NpcDialogueActor[] actors = FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            NpcDialogueActor[] actors = FindObjectsOfType<NpcDialogueActor>();
#endif
            if (actors == null || actors.Length == 0)
            {
                return;
            }

            for (int i = 0; i < actors.Length; i++)
            {
                NpcDialogueActor actor = actors[i];
                NpcDialogueProfile profile = actor != null ? actor.Profile : null;
                PrefabPowerEntry[] powers = profile != null ? profile.PrefabPowers : null;
                if (powers == null || powers.Length == 0)
                {
                    continue;
                }

                for (int j = 0; j < powers.Length; j++)
                {
                    PrefabPowerEntry entry = powers[j];
                    if (entry == null || !entry.Enabled || entry.EffectPrefab == null)
                    {
                        continue;
                    }

                    string prefabName = entry.EffectPrefab.name;
                    if (string.IsNullOrWhiteSpace(prefabName))
                    {
                        continue;
                    }

                    if (!m_PrefabPowerLookup.ContainsKey(prefabName))
                    {
                        m_PrefabPowerLookup[prefabName] = entry.EffectPrefab;
                        added++;
                    }
                }
            }

            if (m_LogDebug && added > 0)
            {
                NGLog.Debug(
                    "DialogueFX",
                    NGLog.Format("Cached profile prefab powers", ("added", added))
                );
            }
        }

        private GameObject TryLoadFromAddressables(string address)
        {
            try
            {
                if (m_AddressableHandles.TryGetValue(address, out var existing))
                    return existing.Status == AsyncOperationStatus.Succeeded
                        ? existing.Result
                        : null;

                var handle = Addressables.LoadAssetAsync<GameObject>(address);
                var result = handle.WaitForCompletion();
                if (handle.Status == AsyncOperationStatus.Succeeded && result != null)
                {
                    m_AddressableHandles[address] = handle;
                    return result;
                }

                Addressables.Release(handle);
            }
            catch
            {
                // Addressable key not found — fall through to Resources
            }
            return null;
        }

        private void OnDestroy()
        {
            var activeIds = new List<ulong>(m_ActiveDissolveStates.Keys);
            for (int i = 0; i < activeIds.Count; i++)
            {
                StopActiveDissolve(activeIds[i], restoreVisible: true);
            }

            var activeObjectHideIds = new List<int>(m_ActiveObjectHideStates.Keys);
            for (int i = 0; i < activeObjectHideIds.Count; i++)
            {
                StopActiveObjectHide(activeObjectHideIds[i], restoreVisible: true);
            }

            var activeSurfaceKeys = new List<string>(m_ActiveSurfaceMaterialStates.Keys);
            for (int i = 0; i < activeSurfaceKeys.Count; i++)
            {
                StopActiveSurfaceMaterialOverride(
                    activeSurfaceKeys[i],
                    restoreOriginalMaterial: true
                );
            }

            foreach (var handle in m_AddressableHandles.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            m_AddressableHandles.Clear();
            m_DeferredEffects.Clear();

            if (Instance == this)
                Instance = null;
        }

        private void EnsureLights()
        {
            if (m_TargetLights != null && m_TargetLights.Length > 0)
            {
                m_WarnedMissingLights = false;
                return;
            }

            if (!m_FindLightsIfEmpty)
            {
                return;
            }

#if UNITY_2023_1_OR_NEWER
            m_TargetLights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
#else
            m_TargetLights = FindObjectsOfType<Light>();
#endif

            if (m_TargetLights != null && m_TargetLights.Length > 0)
            {
                m_WarnedMissingLights = false;
            }
        }

        private void CaptureDefaults()
        {
            EnsureLights();
            if (m_TargetLights == null)
            {
                m_TargetLights = new Light[0];
            }

            m_DefaultColors = new Color[m_TargetLights.Length];
            m_DefaultIntensities = new float[m_TargetLights.Length];
            for (int i = 0; i < m_TargetLights.Length; i++)
            {
                Light light = m_TargetLights[i];
                if (light == null)
                {
                    continue;
                }

                m_DefaultColors[i] = light.color;
                m_DefaultIntensities[i] = light.intensity;
            }
        }

        private void StartTransition(Color targetColor, float targetIntensity, float duration)
        {
            if (m_TransitionRoutine != null)
            {
                StopCoroutine(m_TransitionRoutine);
            }

            if (duration <= 0f)
            {
                ApplyImmediate(targetColor, targetIntensity);
                return;
            }

            m_TransitionRoutine = StartCoroutine(
                TransitionLighting(targetColor, targetIntensity, duration)
            );
        }

        private void ApplyImmediate(Color targetColor, float targetIntensity)
        {
            for (int i = 0; i < m_TargetLights.Length; i++)
            {
                Light light = m_TargetLights[i];
                if (light == null)
                {
                    continue;
                }

                light.color = targetColor;
                light.intensity = targetIntensity;
            }
        }

        private IEnumerator TransitionLighting(
            Color targetColor,
            float targetIntensity,
            float duration
        )
        {
            int count = m_TargetLights.Length;
            var startColors = new Color[count];
            var startIntensities = new float[count];

            for (int i = 0; i < count; i++)
            {
                Light light = m_TargetLights[i];
                if (light == null)
                {
                    continue;
                }

                startColors[i] = light.color;
                startIntensities[i] = light.intensity;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < count; i++)
                {
                    Light light = m_TargetLights[i];
                    if (light == null)
                    {
                        continue;
                    }

                    light.color = Color.Lerp(startColors[i], targetColor, t);
                    light.intensity = Mathf.Lerp(startIntensities[i], targetIntensity, t);
                }

                yield return null;
            }

            ApplyImmediate(targetColor, targetIntensity);
            m_TransitionRoutine = null;
        }

        private IEnumerator TransitionToDefaults(float duration)
        {
            int count = m_TargetLights.Length;
            var startColors = new Color[count];
            var startIntensities = new float[count];
            for (int i = 0; i < count; i++)
            {
                Light light = m_TargetLights[i];
                if (light == null)
                {
                    continue;
                }

                startColors[i] = light.color;
                startIntensities[i] = light.intensity;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < count; i++)
                {
                    Light light = m_TargetLights[i];
                    if (light == null)
                    {
                        continue;
                    }

                    light.color = Color.Lerp(startColors[i], m_DefaultColors[i], t);
                    light.intensity = Mathf.Lerp(startIntensities[i], m_DefaultIntensities[i], t);
                }

                yield return null;
            }

            for (int i = 0; i < count; i++)
            {
                Light light = m_TargetLights[i];
                if (light == null)
                {
                    continue;
                }

                light.color = m_DefaultColors[i];
                light.intensity = m_DefaultIntensities[i];
            }
            m_TransitionRoutine = null;
        }

        internal static ParticleSystem.MinMaxCurve ScaleCurve(
            ParticleSystem.MinMaxCurve curve,
            float scale
        )
        {
            float clampedScale = Mathf.Max(0f, scale);
            curve.constant *= clampedScale;
            curve.constantMin *= clampedScale;
            curve.constantMax *= clampedScale;
            curve.curveMultiplier *= clampedScale;
            return curve;
        }

        internal static float ResolveMaxStartLifetimeSeconds(
            ParticleSystem.MinMaxCurve lifetimeCurve
        )
        {
            float max = Mathf.Max(lifetimeCurve.constant, lifetimeCurve.constantMax);
            max = Mathf.Max(max, lifetimeCurve.constantMin);
            max = Mathf.Max(max, lifetimeCurve.curveMultiplier);
            return Mathf.Max(0.05f, max);
        }

        internal static float ResolveCurveMax(ParticleSystem.MinMaxCurve curve)
        {
            float max = Mathf.Max(curve.constant, curve.constantMax);
            max = Mathf.Max(max, curve.constantMin);
            max = Mathf.Max(max, curve.curveMultiplier);
            return Mathf.Max(0.01f, max);
        }

        private IEnumerator DestroyParticleSystemAfter(GameObject target, float lifetimeSeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, lifetimeSeconds));
            if (target != null)
            {
                Destroy(target);
            }
        }

        #region Player Special Effects

        /// <summary>
        /// TEST: Apply dissolve effect to local player to make invisible
        /// </summary>
        [ContextMenu("Test Dissolve Effect (5s)")]
        public void TestDissolveEffect()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.Log("[DialogueFX] No NetworkManager found");
                return;
            }

            // Find local player
            var spawnManager = NetworkManager.Singleton.SpawnManager;
            if (spawnManager == null)
            {
                Debug.Log("[DialogueFX] No SpawnManager found");
                return;
            }

            // Try to find player by checking all spawned objects for LocalPlayer
            foreach (var kvp in spawnManager.SpawnedObjects)
            {
                var networkObject = kvp.Value;
                if (networkObject != null && networkObject.IsLocalPlayer)
                {
                    ApplyDissolveEffect(networkObject.NetworkObjectId, 5f);
                    Debug.Log(
                        $"[DialogueFX] Dissolve applied to local player: {networkObject.NetworkObjectId}"
                    );
                    return;
                }
            }

            Debug.Log("[DialogueFX] Local player not found");
        }

        /// <summary>
        /// TEST: Restore visibility to local player
        /// </summary>
        [ContextMenu("Test Respawn Effect")]
        public void TestRespawnEffect()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.Log("[DialogueFX] No NetworkManager found");
                return;
            }

            var spawnManager = NetworkManager.Singleton.SpawnManager;
            if (spawnManager == null)
            {
                Debug.Log("[DialogueFX] No SpawnManager found");
                return;
            }

            foreach (var kvp in spawnManager.SpawnedObjects)
            {
                var networkObject = kvp.Value;
                if (networkObject != null && networkObject.IsLocalPlayer)
                {
                    ApplyRespawnEffect(networkObject.NetworkObjectId);
                    Debug.Log(
                        $"[DialogueFX] Respawn applied to local player: {networkObject.NetworkObjectId}"
                    );
                    return;
                }
            }

            Debug.Log("[DialogueFX] Local player not found");
        }

        /// <summary>
        /// Apply dissolve effect to make player invisible
        /// </summary>
        public void ApplyDissolveEffect(ulong targetNetworkObjectId, float durationSeconds = 5f)
        {
            if (
                TryHandleFeedbackPromptBlocking(
                    "dissolve",
                    "dissolve",
                    () => ApplyDissolveEffect(targetNetworkObjectId, durationSeconds),
                    0,
                    targetNetworkObjectId
                )
            )
            {
                return;
            }

            var targetObj = GetNetworkObject(targetNetworkObjectId);
            if (targetObj == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    $"Dissolve effect: target {targetNetworkObjectId} not found"
                );
                return;
            }

            GameObject target = targetObj.gameObject;

            // Cancel any existing dissolve session for this target and restore baseline first.
            StopActiveDissolve(targetNetworkObjectId, restoreVisible: true);

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                NGLog.Warn("DialogueFX", $"Dissolve effect: no renderers found on {target.name}");
                return;
            }

            RendererFadeState[] fadeStates = BuildFadeStates(renderers);
            m_ActiveDissolveStates[targetNetworkObjectId] = fadeStates;

            float clampedDuration = Mathf.Clamp(durationSeconds, 0.6f, 30f);
            Coroutine routine = StartCoroutine(
                RunDissolveSequence(targetNetworkObjectId, target.name, clampedDuration, fadeStates)
            );
            m_ActiveDissolveRoutines[targetNetworkObjectId] = routine;

            NGLog.Info(
                "DialogueFX",
                $"Dissolve effect started on {target.name} for {clampedDuration:0.00}s"
            );

            EmitEffectApplied(
                new AppliedEffectInfo
                {
                    EffectType = "dissolve",
                    EffectName = "dissolve",
                    TargetNetworkObjectId = targetNetworkObjectId,
                    Position = target.transform.position,
                    Scale = 1f,
                    DurationSeconds = clampedDuration,
                    AttachToTarget = true,
                    FitToTargetMesh = true,
                    AppliedAtRealtime = Time.realtimeSinceStartup,
                    FeedbackDelaySeconds = Mathf.Clamp(m_DissolveFadeOutSeconds * 0.6f, 0.2f, 1.0f),
                }
            );
        }

        /// <summary>
        /// Apply dissolve-style temporary invisibility to semantic floor/terrain objects.
        /// </summary>
        public void ApplyFloorDissolveEffect(float durationSeconds = 8f)
        {
            if (
                TryHandleFeedbackPromptBlocking(
                    "floor_dissolve",
                    "floor_dissolve",
                    () => ApplyFloorDissolveEffect(durationSeconds),
                    0,
                    0
                )
            )
            {
                return;
            }

            List<GameObject> floorTargets = CollectFloorTargets();
            if (floorTargets.Count == 0)
            {
                NGLog.Warn("DialogueFX", "Floor dissolve skipped: no floor targets found.");
                return;
            }

            float clampedDuration = Mathf.Clamp(durationSeconds, 0.6f, 30f);
            int appliedCount = 0;
            Vector3 firstPosition = Vector3.zero;

            for (int i = 0; i < floorTargets.Count; i++)
            {
                GameObject target = floorTargets[i];
                if (target == null)
                {
                    continue;
                }

                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0)
                {
                    continue;
                }

                int targetKey = target.GetInstanceID();
                StopActiveObjectHide(targetKey, restoreVisible: true);

                RendererFadeState[] fadeStates = BuildFadeStates(renderers);
                m_ActiveObjectHideStates[targetKey] = fadeStates;

                Coroutine routine = StartCoroutine(
                    RunFloorDissolveSequence(targetKey, target.name, clampedDuration, fadeStates)
                );
                m_ActiveObjectHideRoutines[targetKey] = routine;

                if (appliedCount == 0)
                {
                    firstPosition = target.transform.position;
                }
                appliedCount++;
            }

            if (appliedCount <= 0)
            {
                NGLog.Warn("DialogueFX", "Floor dissolve skipped: targets had no renderers.");
                return;
            }

            NGLog.Info(
                "DialogueFX",
                $"Floor dissolve effect started on {appliedCount} object(s) for {clampedDuration:0.00}s"
            );

            EmitEffectApplied(
                new AppliedEffectInfo
                {
                    EffectType = "dissolve",
                    EffectName = "floor_dissolve",
                    TargetNetworkObjectId = 0UL,
                    Position = firstPosition,
                    Scale = 1f,
                    DurationSeconds = clampedDuration,
                    AttachToTarget = false,
                    FitToTargetMesh = false,
                    AppliedAtRealtime = Time.realtimeSinceStartup,
                    FeedbackDelaySeconds = 0.2f,
                }
            );
        }

        /// <summary>
        /// Apply respawn effect to restore player
        /// </summary>
        public void ApplyRespawnEffect(ulong targetNetworkObjectId)
        {
            if (
                TryHandleFeedbackPromptBlocking(
                    "respawn",
                    "respawn",
                    () => ApplyRespawnEffect(targetNetworkObjectId),
                    0,
                    targetNetworkObjectId
                )
            )
            {
                return;
            }

            var targetObj = GetNetworkObject(targetNetworkObjectId);
            if (targetObj == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    $"Respawn effect: target {targetNetworkObjectId} not found"
                );
                return;
            }

            GameObject target = targetObj.gameObject;

            StopActiveDissolve(targetNetworkObjectId, restoreVisible: true);

            // Ensure visibility even if no dissolve session is active.
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
            }

            var mainRenderer = target.GetComponent<Renderer>();
            if (mainRenderer != null)
            {
                mainRenderer.enabled = true;
            }

            NGLog.Info("DialogueFX", $"Respawn effect applied to {target.name}");

            EmitEffectApplied(
                new AppliedEffectInfo
                {
                    EffectType = "respawn",
                    EffectName = "respawn",
                    TargetNetworkObjectId = targetNetworkObjectId,
                    Position = target.transform.position,
                    Scale = 1f,
                    DurationSeconds = 0f,
                    AttachToTarget = true,
                    FitToTargetMesh = true,
                    AppliedAtRealtime = Time.realtimeSinceStartup,
                    FeedbackDelaySeconds = 0.08f,
                }
            );
        }

        public void ApplyFloorFreezeSurfaceMaterial(
            string surfaceId,
            string rendererHierarchyPath,
            int materialSlotIndex,
            float durationSeconds,
            ulong sourceNetworkObjectId = 0,
            ulong targetNetworkObjectId = 0
        )
        {
            if (
                TryHandleFeedbackPromptBlocking(
                    "surface_material",
                    "floor_freeze_material",
                    () =>
                        ApplyFloorFreezeSurfaceMaterial(
                            surfaceId,
                            rendererHierarchyPath,
                            materialSlotIndex,
                            durationSeconds,
                            sourceNetworkObjectId,
                            targetNetworkObjectId
                        ),
                    sourceNetworkObjectId,
                    targetNetworkObjectId
                )
            )
            {
                return;
            }

            if (m_FloorFreezeMaterial == null)
            {
                NGLog.Warn(
                    "DialogueFX",
                    "Floor freeze material not assigned on DialogueSceneEffectsController (assign IceBall or IceBall_Floor)."
                );
                return;
            }

            if (
                !TryResolveFloorSurfaceOverrideTarget(
                    surfaceId,
                    rendererHierarchyPath,
                    materialSlotIndex,
                    out EffectSurface surface,
                    out Renderer renderer,
                    out int resolvedMaterialSlot,
                    out string overrideKey,
                    out string reason
                )
            )
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (target unresolved)",
                        ("surfaceId", surfaceId ?? string.Empty),
                        ("rendererPath", rendererHierarchyPath ?? string.Empty),
                        ("reason", reason ?? string.Empty)
                    )
                );
                return;
            }

            if (surface != null && surface.SurfaceType == EffectSurfaceType.Wall)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (resolved wall surface)",
                        ("surfaceId", surface.SurfaceId),
                        ("renderer", renderer != null ? renderer.name : string.Empty)
                    )
                );
                return;
            }

            if (surface != null && !surface.AllowFreeze)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (surface disallows freeze)",
                        ("surfaceId", surface.SurfaceId),
                        ("surfaceType", surface.SurfaceType.ToString())
                    )
                );
                return;
            }

            Material[] workingMaterials;
            try
            {
                workingMaterials = renderer.materials;
            }
            catch (Exception ex)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (renderer materials unavailable)",
                        ("renderer", renderer != null ? renderer.name : string.Empty),
                        ("error", ex.Message ?? string.Empty)
                    )
                );
                Debug.LogException(ex);
                return;
            }

            if (workingMaterials == null || workingMaterials.Length == 0)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (renderer has no materials)",
                        ("renderer", renderer != null ? renderer.name : string.Empty)
                    )
                );
                return;
            }

            resolvedMaterialSlot = Mathf.Clamp(
                resolvedMaterialSlot,
                0,
                workingMaterials.Length - 1
            );
            StopActiveSurfaceMaterialOverride(overrideKey, restoreOriginalMaterial: true);

            Material[] originalMaterials = (Material[])workingMaterials.Clone();
            workingMaterials[resolvedMaterialSlot] = m_FloorFreezeMaterial;

            try
            {
                renderer.materials = workingMaterials;
            }
            catch (Exception ex)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Floor freeze material skipped (apply failed)",
                        ("renderer", renderer != null ? renderer.name : string.Empty),
                        ("slot", resolvedMaterialSlot),
                        ("error", ex.Message ?? string.Empty)
                    )
                );
                Debug.LogException(ex);
                return;
            }

            float clampedDuration = Mathf.Clamp(
                durationSeconds > 0f ? durationSeconds : m_FloorFreezeDefaultDurationSeconds,
                0.25f,
                60f
            );
            m_ActiveSurfaceMaterialStates[overrideKey] = new SurfaceMaterialOverrideState
            {
                Key = overrideKey,
                SurfaceId = surface != null ? surface.SurfaceId : string.Empty,
                RendererPath = BuildHierarchyPath(renderer != null ? renderer.transform : null),
                Renderer = renderer,
                OriginalMaterials = originalMaterials,
                MaterialSlotIndex = resolvedMaterialSlot,
                AppliedAtRealtime = Time.realtimeSinceStartup,
            };
            m_ActiveSurfaceMaterialRoutines[overrideKey] = StartCoroutine(
                RestoreSurfaceMaterialOverrideAfter(overrideKey, clampedDuration)
            );

            Bounds bounds = renderer.bounds;
            NGLog.Info(
                "DialogueFX",
                NGLog.Format(
                    "Floor freeze material applied",
                    ("surfaceId", surface != null ? surface.SurfaceId : string.Empty),
                    ("surfaceType", surface != null ? surface.SurfaceType.ToString() : "unknown"),
                    ("renderer", renderer.name),
                    ("slot", resolvedMaterialSlot),
                    ("material", m_FloorFreezeMaterial.name),
                    ("duration", clampedDuration.ToString("F2"))
                )
            );

            EmitEffectApplied(
                new AppliedEffectInfo
                {
                    EffectType = "surface_material",
                    EffectName = "floor_freeze_material",
                    SourceNetworkObjectId = sourceNetworkObjectId,
                    TargetNetworkObjectId = targetNetworkObjectId,
                    Position = bounds.center,
                    Scale = 1f,
                    DurationSeconds = clampedDuration,
                    AttachToTarget = false,
                    FitToTargetMesh = true,
                    AppliedAtRealtime = Time.realtimeSinceStartup,
                    FeedbackDelaySeconds = Mathf.Clamp(clampedDuration * 0.15f, 0.08f, 1.0f),
                }
            );
        }

        private bool TryResolveFloorSurfaceOverrideTarget(
            string surfaceId,
            string rendererHierarchyPath,
            int requestedMaterialSlot,
            out EffectSurface surface,
            out Renderer renderer,
            out int resolvedMaterialSlot,
            out string overrideKey,
            out string reason
        )
        {
            surface = null;
            renderer = null;
            resolvedMaterialSlot = Mathf.Max(0, requestedMaterialSlot);
            overrideKey = string.Empty;
            reason = string.Empty;

            if (!string.IsNullOrWhiteSpace(surfaceId))
            {
                surface = FindEffectSurfaceById(surfaceId);
                if (surface != null)
                {
                    renderer = surface.GetPrimaryRenderer();
                    if (renderer == null)
                    {
                        reason = "Matched surface has no renderer.";
                        return false;
                    }

                    resolvedMaterialSlot = surface.ResolveMaterialSlot(
                        renderer,
                        requestedMaterialSlot
                    );
                    overrideKey = "surface:" + surface.SurfaceId;
                    return true;
                }
            }

            string normalizedRendererPath = string.IsNullOrWhiteSpace(rendererHierarchyPath)
                ? string.Empty
                : rendererHierarchyPath.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedRendererPath))
            {
                renderer = FindRendererByHierarchyPath(normalizedRendererPath);
                if (renderer != null)
                {
                    surface = renderer.GetComponentInParent<EffectSurface>();
                    if (surface != null)
                    {
                        resolvedMaterialSlot = surface.ResolveMaterialSlot(
                            renderer,
                            requestedMaterialSlot
                        );
                        overrideKey = "surface:" + surface.SurfaceId;
                    }
                    else
                    {
                        Material[] shared = renderer.sharedMaterials;
                        int maxSlot = shared != null ? Mathf.Max(0, shared.Length - 1) : 0;
                        resolvedMaterialSlot = Mathf.Clamp(requestedMaterialSlot, 0, maxSlot);
                        overrideKey = "renderer:" + BuildHierarchyPath(renderer.transform);
                    }

                    return true;
                }
            }

            reason = string.IsNullOrWhiteSpace(surfaceId)
                ? "No surface id or renderer path supplied."
                : "Surface id was not found and renderer path fallback failed.";
            return false;
        }

        private static EffectSurface FindEffectSurfaceById(string surfaceId)
        {
            if (string.IsNullOrWhiteSpace(surfaceId))
            {
                return null;
            }

            string normalized = surfaceId.Trim();
#if UNITY_2023_1_OR_NEWER
            EffectSurface[] surfaces = FindObjectsByType<EffectSurface>(
                FindObjectsInactive.Include
            );
#else
            EffectSurface[] surfaces = FindObjectsOfType<EffectSurface>(true);
#endif
            if (surfaces == null || surfaces.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < surfaces.Length; i++)
            {
                EffectSurface candidate = surfaces[i];
                if (candidate == null)
                {
                    continue;
                }

                if (
                    string.Equals(
                        candidate.SurfaceId,
                        normalized,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Renderer FindRendererByHierarchyPath(string hierarchyPath)
        {
            if (string.IsNullOrWhiteSpace(hierarchyPath))
            {
                return null;
            }

            GameObject target = GameObject.Find(hierarchyPath);
            if (target != null)
            {
                Renderer directRenderer = target.GetComponent<Renderer>();
                if (directRenderer != null)
                {
                    return directRenderer;
                }

                return target.GetComponentInChildren<Renderer>(includeInactive: true);
            }

#if UNITY_2023_1_OR_NEWER
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include);
#else
            Renderer[] renderers = FindObjectsOfType<Renderer>(true);
#endif
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer candidate = renderers[i];
                if (candidate == null)
                {
                    continue;
                }

                if (
                    string.Equals(
                        BuildHierarchyPath(candidate.transform),
                        hierarchyPath,
                        StringComparison.Ordinal
                    )
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        private IEnumerator RestoreSurfaceMaterialOverrideAfter(string key, float durationSeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, durationSeconds));

            m_ActiveSurfaceMaterialRoutines.Remove(key);

            if (
                !m_ActiveSurfaceMaterialStates.TryGetValue(
                    key,
                    out SurfaceMaterialOverrideState state
                )
            )
            {
                yield break;
            }

            TryRestoreSurfaceMaterialOverride(state);
            m_ActiveSurfaceMaterialStates.Remove(key);
        }

        private void StopActiveSurfaceMaterialOverride(string key, bool restoreOriginalMaterial)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (m_ActiveSurfaceMaterialRoutines.TryGetValue(key, out Coroutine routine))
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }

                m_ActiveSurfaceMaterialRoutines.Remove(key);
            }

            if (
                !m_ActiveSurfaceMaterialStates.TryGetValue(
                    key,
                    out SurfaceMaterialOverrideState state
                )
            )
            {
                return;
            }

            if (restoreOriginalMaterial)
            {
                TryRestoreSurfaceMaterialOverride(state);
            }

            m_ActiveSurfaceMaterialStates.Remove(key);
        }

        private void TryRestoreSurfaceMaterialOverride(SurfaceMaterialOverrideState state)
        {
            if (state == null || state.Renderer == null || state.OriginalMaterials == null)
            {
                return;
            }

            try
            {
                state.Renderer.materials = state.OriginalMaterials;
                if (m_LogDebug)
                {
                    NGLog.Debug(
                        "DialogueFX",
                        NGLog.Format(
                            "Floor freeze material restored",
                            ("surfaceId", state.SurfaceId ?? string.Empty),
                            (
                                "renderer",
                                state.Renderer != null ? state.Renderer.name : string.Empty
                            ),
                            ("slot", state.MaterialSlotIndex)
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                NGLog.Warn(
                    "DialogueFX",
                    NGLog.Format(
                        "Failed to restore floor freeze material",
                        ("surfaceId", state.SurfaceId ?? string.Empty),
                        ("rendererPath", state.RendererPath ?? string.Empty),
                        ("error", ex.Message ?? string.Empty)
                    )
                );
                Debug.LogException(ex);
            }
        }

        private static string BuildHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static void EmitEffectApplied(AppliedEffectInfo info)
        {
            Action<AppliedEffectInfo> handler = OnEffectApplied;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(info);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueFX] Effect observer callback failed: {ex.Message}");
            }
        }

        private IEnumerator RunDissolveSequence(
            ulong targetNetworkObjectId,
            string targetName,
            float durationSeconds,
            RendererFadeState[] fadeStates
        )
        {
            ResolveDissolveTimings(durationSeconds, out float fadeOut, out float hold, out float fadeIn);

            // Fade out
            if (fadeOut > 0f)
            {
                float elapsed = 0f;
                while (elapsed < fadeOut)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeOut);
                    float curved = m_DissolveCurve != null ? m_DissolveCurve.Evaluate(t) : t;
                    ApplyFadeAlpha(fadeStates, 1f - curved);
                    yield return null;
                }
            }

            SetRenderersEnabled(fadeStates, false);
            yield return new WaitForSeconds(hold);

            // Fade in
            SetRenderersEnabled(fadeStates, true);
            ApplyFadeAlpha(fadeStates, 0f);
            if (fadeIn > 0f)
            {
                float elapsed = 0f;
                while (elapsed < fadeIn)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeIn);
                    float curved = m_DissolveCurve != null ? m_DissolveCurve.Evaluate(t) : t;
                    ApplyFadeAlpha(fadeStates, curved);
                    yield return null;
                }
            }

            RestoreFadeStates(fadeStates, forceVisible: true);
            m_ActiveDissolveRoutines.Remove(targetNetworkObjectId);
            m_ActiveDissolveStates.Remove(targetNetworkObjectId);

            NGLog.Info(
                "DialogueFX",
                $"Dissolve effect ended - visibility restored for {targetName}"
            );
        }

        private void ResolveDissolveTimings(
            float durationSeconds,
            out float fadeOut,
            out float hold,
            out float fadeIn
        )
        {
            float duration = Mathf.Max(0.6f, durationSeconds);
            float desiredFadeOut = Mathf.Max(m_DissolveFadeOutSeconds, kMinDissolveFadeOutSeconds);
            float desiredFadeIn = Mathf.Max(m_DissolveFadeInSeconds, kMinDissolveFadeInSeconds);
            float desiredHold = kMinDissolveHoldSeconds;

            float required = desiredFadeOut + desiredFadeIn + desiredHold;
            if (required <= duration)
            {
                fadeOut = desiredFadeOut;
                fadeIn = desiredFadeIn;
                hold = duration - fadeOut - fadeIn;
                return;
            }

            float remainingForFades = Mathf.Max(0.1f, duration - desiredHold);
            float fadeScale = remainingForFades / Mathf.Max(0.1f, desiredFadeOut + desiredFadeIn);
            fadeScale = Mathf.Clamp(fadeScale, 0.05f, 1f);

            fadeOut = Mathf.Max(0.05f, desiredFadeOut * fadeScale);
            fadeIn = Mathf.Max(0.05f, desiredFadeIn * fadeScale);
            hold = Mathf.Max(0.05f, duration - fadeOut - fadeIn);
        }

        private void StopActiveDissolve(ulong targetNetworkObjectId, bool restoreVisible)
        {
            if (m_ActiveDissolveRoutines.TryGetValue(targetNetworkObjectId, out Coroutine routine))
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
                m_ActiveDissolveRoutines.Remove(targetNetworkObjectId);
            }

            if (
                m_ActiveDissolveStates.TryGetValue(
                    targetNetworkObjectId,
                    out RendererFadeState[] states
                )
            )
            {
                if (restoreVisible)
                {
                    RestoreFadeStates(states, forceVisible: true);
                }
                m_ActiveDissolveStates.Remove(targetNetworkObjectId);
            }
        }

        private IEnumerator RunFloorDissolveSequence(
            int targetKey,
            string targetName,
            float durationSeconds,
            RendererFadeState[] fadeStates
        )
        {
            ResolveDissolveTimings(durationSeconds, out float fadeOut, out float hold, out float fadeIn);

            if (fadeOut > 0f)
            {
                float elapsed = 0f;
                while (elapsed < fadeOut)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeOut);
                    float curved = m_DissolveCurve != null ? m_DissolveCurve.Evaluate(t) : t;
                    ApplyFadeAlpha(fadeStates, 1f - curved);
                    yield return null;
                }
            }

            SetRenderersEnabled(fadeStates, false);
            if (hold > 0f)
            {
                yield return new WaitForSecondsRealtime(hold);
            }

            SetRenderersEnabled(fadeStates, true);
            ApplyFadeAlpha(fadeStates, 0f);
            if (fadeIn > 0f)
            {
                float elapsed = 0f;
                while (elapsed < fadeIn)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeIn);
                    float curved = m_DissolveCurve != null ? m_DissolveCurve.Evaluate(t) : t;
                    ApplyFadeAlpha(fadeStates, curved);
                    yield return null;
                }
            }

            RestoreFadeStates(fadeStates, forceVisible: true);
            m_ActiveObjectHideRoutines.Remove(targetKey);
            m_ActiveObjectHideStates.Remove(targetKey);

            NGLog.Info(
                "DialogueFX",
                $"Floor dissolve effect ended - visibility restored for {targetName}"
            );
        }

        private void StopActiveObjectHide(int targetKey, bool restoreVisible)
        {
            if (m_ActiveObjectHideRoutines.TryGetValue(targetKey, out Coroutine routine))
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
                m_ActiveObjectHideRoutines.Remove(targetKey);
            }

            if (m_ActiveObjectHideStates.TryGetValue(targetKey, out RendererFadeState[] states))
            {
                if (restoreVisible)
                {
                    RestoreFadeStates(states, forceVisible: true);
                }
                m_ActiveObjectHideStates.Remove(targetKey);
            }
        }

        private static List<GameObject> CollectFloorTargets()
        {
            var targets = new List<GameObject>();
            var seen = new HashSet<int>();

#if UNITY_2023_1_OR_NEWER
            DialogueSemanticTag[] semanticTags = UnityEngine.Object.FindObjectsByType<DialogueSemanticTag>(
                findObjectsInactive: FindObjectsInactive.Exclude
            );
#else
            DialogueSemanticTag[] semanticTags = UnityEngine.Object.FindObjectsOfType<DialogueSemanticTag>();
#endif
            for (int i = 0; i < semanticTags.Length; i++)
            {
                DialogueSemanticTag tag = semanticTags[i];
                if (tag == null || tag.gameObject == null)
                {
                    continue;
                }

                if (tag.Role != DialogueSemanticRole.Floor && tag.Role != DialogueSemanticRole.Terrain)
                {
                    continue;
                }

                TryAddFloorTarget(tag.gameObject, seen, targets);
            }

            if (targets.Count > 0)
            {
                return targets;
            }

#if UNITY_2023_1_OR_NEWER
            Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                findObjectsInactive: FindObjectsInactive.Exclude
            );
#else
            Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>();
#endif
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform transform = transforms[i];
                if (transform == null || transform.gameObject == null)
                {
                    continue;
                }

                string objectName = transform.gameObject.name ?? string.Empty;
                if (!IsLikelyFloorName(objectName))
                {
                    continue;
                }

                TryAddFloorTarget(transform.gameObject, seen, targets);
            }

            return targets;
        }

        private static bool IsLikelyFloorName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            string lower = objectName.ToLowerInvariant();
            for (int i = 0; i < s_FloorNameHints.Length; i++)
            {
                if (lower.Contains(s_FloorNameHints[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryAddFloorTarget(
            GameObject candidate,
            HashSet<int> seen,
            List<GameObject> targets
        )
        {
            if (candidate == null)
            {
                return;
            }

            int key = candidate.GetInstanceID();
            if (!seen.Add(key))
            {
                return;
            }

            Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            targets.Add(candidate);
        }

        private RendererFadeState[] BuildFadeStates(Renderer[] renderers)
        {
            var states = new RendererFadeState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }
                try
                {
                    Material[] materials = renderer.materials;
                    var materialStates = new MaterialFadeState[materials.Length];
                    for (int j = 0; j < materials.Length; j++)
                    {
                        Material mat = materials[j];
                        if (mat == null)
                        {
                            continue;
                        }

                        MaterialFadeState matState = CaptureMaterialFadeState(mat);
                        materialStates[j] = matState;
                    }

                    states[i] = new RendererFadeState
                    {
                        Renderer = renderer,
                        WasEnabled = renderer.enabled,
                        Materials = materialStates,
                    };
                }
                catch (System.Exception ex)
                {
                    string rendererName = renderer != null ? renderer.name : "<null>";
                    string rendererType = renderer != null ? renderer.GetType().Name : "<null>";
                    NGLog.Warn(
                        "DialogueFX",
                        NGLog.Format(
                            "Dissolve skipped renderer due to setup error",
                            ("renderer", rendererName),
                            ("rendererType", rendererType),
                            ("error", ex.Message ?? string.Empty)
                        )
                    );
                    Debug.LogException(ex);
                }
            }

            return states;
        }

        private static MaterialFadeState CaptureMaterialFadeState(Material material)
        {
            var state = new MaterialFadeState
            {
                Material = material,
                OriginalRenderQueue = material.renderQueue,
                KeywordAlphaTest = material.IsKeywordEnabled("_ALPHATEST_ON"),
                KeywordAlphaBlend = material.IsKeywordEnabled("_ALPHABLEND_ON"),
                KeywordAlphaPremultiply = material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"),
            };

            if (material.HasProperty("_BaseColor"))
            {
                state.ColorPropertyName = "_BaseColor";
                state.OriginalColor = material.GetColor("_BaseColor");
            }
            else if (material.HasProperty("_Color"))
            {
                state.ColorPropertyName = "_Color";
                state.OriginalColor = material.GetColor("_Color");
            }

            if (material.HasProperty("_Surface"))
            {
                state.HasSurface = true;
                state.Surface = material.GetFloat("_Surface");
            }
            if (material.HasProperty("_Mode"))
            {
                state.HasMode = true;
                state.Mode = material.GetFloat("_Mode");
            }
            if (material.HasProperty("_SrcBlend"))
            {
                state.HasSrcBlend = true;
                state.SrcBlend = material.GetFloat("_SrcBlend");
            }
            if (material.HasProperty("_DstBlend"))
            {
                state.HasDstBlend = true;
                state.DstBlend = material.GetFloat("_DstBlend");
            }
            if (material.HasProperty("_ZWrite"))
            {
                state.HasZWrite = true;
                state.ZWrite = material.GetFloat("_ZWrite");
            }

            PrepareMaterialForFade(state);
            return state;
        }

        private static void PrepareMaterialForFade(MaterialFadeState state)
        {
            Material material = state.Material;
            if (material == null || string.IsNullOrEmpty(state.ColorPropertyName))
            {
                return;
            }

            if (state.HasSurface)
            {
                material.SetFloat("_Surface", 1f); // URP Transparent
            }

            if (state.HasMode)
            {
                material.SetFloat("_Mode", 2f); // Standard Fade
            }

            if (state.HasSrcBlend)
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (state.HasDstBlend)
            {
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }

            if (state.HasZWrite)
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static void ApplyFadeAlpha(RendererFadeState[] states, float alpha01)
        {
            float clamped = Mathf.Clamp01(alpha01);
            if (states == null)
            {
                return;
            }

            for (int i = 0; i < states.Length; i++)
            {
                RendererFadeState rendererState = states[i];
                if (rendererState?.Materials == null)
                {
                    continue;
                }

                for (int j = 0; j < rendererState.Materials.Length; j++)
                {
                    MaterialFadeState matState = rendererState.Materials[j];
                    if (
                        matState?.Material == null
                        || string.IsNullOrEmpty(matState.ColorPropertyName)
                    )
                    {
                        continue;
                    }

                    Color color = matState.OriginalColor;
                    color.a = Mathf.Clamp01(matState.OriginalColor.a * clamped);
                    matState.Material.SetColor(matState.ColorPropertyName, color);
                }
            }
        }

        private static void SetRenderersEnabled(RendererFadeState[] states, bool enabled)
        {
            if (states == null)
            {
                return;
            }

            for (int i = 0; i < states.Length; i++)
            {
                RendererFadeState state = states[i];
                if (state?.Renderer == null)
                {
                    continue;
                }

                state.Renderer.enabled = enabled;
            }
        }

        private static void RestoreFadeStates(RendererFadeState[] states, bool forceVisible)
        {
            if (states == null)
            {
                return;
            }

            for (int i = 0; i < states.Length; i++)
            {
                RendererFadeState rendererState = states[i];
                if (rendererState == null)
                {
                    continue;
                }

                if (rendererState.Renderer != null)
                {
                    rendererState.Renderer.enabled = forceVisible || rendererState.WasEnabled;
                }

                if (rendererState.Materials == null)
                {
                    continue;
                }

                for (int j = 0; j < rendererState.Materials.Length; j++)
                {
                    MaterialFadeState matState = rendererState.Materials[j];
                    if (matState?.Material == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(matState.ColorPropertyName))
                    {
                        matState.Material.SetColor(
                            matState.ColorPropertyName,
                            matState.OriginalColor
                        );
                    }

                    if (matState.HasSurface)
                    {
                        matState.Material.SetFloat("_Surface", matState.Surface);
                    }
                    if (matState.HasMode)
                    {
                        matState.Material.SetFloat("_Mode", matState.Mode);
                    }
                    if (matState.HasSrcBlend)
                    {
                        matState.Material.SetFloat("_SrcBlend", matState.SrcBlend);
                    }
                    if (matState.HasDstBlend)
                    {
                        matState.Material.SetFloat("_DstBlend", matState.DstBlend);
                    }
                    if (matState.HasZWrite)
                    {
                        matState.Material.SetFloat("_ZWrite", matState.ZWrite);
                    }

                    if (matState.KeywordAlphaTest)
                    {
                        matState.Material.EnableKeyword("_ALPHATEST_ON");
                    }
                    else
                    {
                        matState.Material.DisableKeyword("_ALPHATEST_ON");
                    }

                    if (matState.KeywordAlphaBlend)
                    {
                        matState.Material.EnableKeyword("_ALPHABLEND_ON");
                    }
                    else
                    {
                        matState.Material.DisableKeyword("_ALPHABLEND_ON");
                    }

                    if (matState.KeywordAlphaPremultiply)
                    {
                        matState.Material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    }
                    else
                    {
                        matState.Material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    }

                    matState.Material.renderQueue = matState.OriginalRenderQueue;
                }
            }
        }

        private NetworkObject GetNetworkObject(ulong networkObjectId)
        {
            if (NetworkManager.Singleton == null)
                return null;

            var networkManager = NetworkManager.Singleton;

            // Use SpawnedObjects dictionary (works in all Netcode versions)
            if (networkManager.SpawnManager != null)
            {
                var spawnedObjects = networkManager.SpawnManager.SpawnedObjects;
                if (spawnedObjects.TryGetValue(networkObjectId, out var obj))
                {
                    return obj;
                }
            }
            return null;
        }

        #endregion
    }
}
