using System;
using System.Collections.Generic;
using Network_Game.Dialogue;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Network_Game.Combat
{
    /// <summary>
    /// Lightweight runtime overlay for multiplayer combat debugging:
    /// player health state, recent effect dispatches, and recent damage ticks.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(550)]
    public sealed class CombatRuntimeOverlay : MonoBehaviour
    {
        private sealed class LogEntry
        {
            public string Text;
            public float ExpiresAt;
        }

        [SerializeField]
        private bool m_ShowOverlay = true;

        [SerializeField]
        private Key m_ToggleKey = Key.F9;

        [SerializeField]
        [Min(2)]
        private int m_MaxLogEntries = 4;

        [SerializeField]
        [Min(1f)]
        private float m_LogLifetimeSeconds = 14f;

        [SerializeField]
        private Vector2 m_PanelPosition = new Vector2(12f, 54f);

        [SerializeField]
        private float m_PanelWidth = 360f;

        private readonly List<CombatHealth> m_PlayerHealthTargets = new List<CombatHealth>(16);
        private readonly List<LogEntry> m_RecentEffects = new List<LogEntry>(16);
        private readonly List<LogEntry> m_RecentDamage = new List<LogEntry>(16);
        private bool m_PlayerListDirty = true;
        private GUIStyle m_HeaderLabelStyle;
        private GUIStyle m_LogLabelStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
#if UNITY_2023_1_OR_NEWER
            CombatRuntimeOverlay existing = FindAnyObjectByType<CombatRuntimeOverlay>(
                FindObjectsInactive.Include
            );
#else
            CombatRuntimeOverlay existing = FindAnyObjectByType<CombatRuntimeOverlay>(
                FindObjectsInactive.Include
            );
#endif
            if (existing != null)
            {
                return;
            }

            GameObject overlayRoot = new GameObject("CombatRuntimeOverlay");
            DontDestroyOnLoad(overlayRoot);
            overlayRoot.AddComponent<CombatRuntimeOverlay>();
        }

        private void OnEnable()
        {
            CombatHealthRegistry.OnRegistered += HandleHealthRegistered;
            CombatHealthRegistry.OnUnregistered += HandleHealthUnregistered;
            CombatHealth.OnDamageApplied += HandleDamageApplied;
            CombatHealth.OnHealthChanged += HandleHealthChanged;
            DialogueSceneEffectsController.OnEffectApplied += HandleEffectApplied;
            RebuildPlayerHealthTargets();
        }

        private void OnDisable()
        {
            CombatHealthRegistry.OnRegistered -= HandleHealthRegistered;
            CombatHealthRegistry.OnUnregistered -= HandleHealthUnregistered;
            CombatHealth.OnDamageApplied -= HandleDamageApplied;
            CombatHealth.OnHealthChanged -= HandleHealthChanged;
            DialogueSceneEffectsController.OnEffectApplied -= HandleEffectApplied;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[m_ToggleKey].wasPressedThisFrame)
            {
                m_ShowOverlay = !m_ShowOverlay;
            }

            if (!m_ShowOverlay)
            {
                return;
            }

            if (m_PlayerListDirty)
            {
                RebuildPlayerHealthTargets();
            }

            float now = Time.unscaledTime;
            PruneExpiredLogs(m_RecentEffects, now);
            PruneExpiredLogs(m_RecentDamage, now);
        }

        private void OnGUI()
        {
            if (!m_ShowOverlay)
            {
                return;
            }

            EnsureStyles();

            float x = Mathf.Max(0f, m_PanelPosition.x);
            float y = Mathf.Max(0f, m_PanelPosition.y);
            float width = Mathf.Clamp(m_PanelWidth, 320f, 760f);

            float line = 18f;
            int healthCount = m_PlayerHealthTargets.Count;
            int effectCount = Mathf.Min(m_MaxLogEntries, m_RecentEffects.Count);
            int damageCount = Mathf.Min(m_MaxLogEntries, m_RecentDamage.Count);
            float panelHeight = 94f + (healthCount * 22f) + ((effectCount + damageCount) * line);

            GUI.Box(new Rect(x, y, width, panelHeight), $"Combat Runtime  ({m_ToggleKey})");

            float rowY = y + 28f;
            NetworkManager networkManager = NetworkManager.Singleton;
            string netState =
                networkManager == null
                ? "offline"
                : networkManager.IsServer
                ? "server"
                : networkManager.IsClient ? "client" : "unknown";
            GUI.Label(
                new Rect(x + 10f, rowY, width - 20f, line),
                $"Net: {netState} | Players tracked: {healthCount}",
                m_HeaderLabelStyle
            );
            rowY += line;

            if (healthCount == 0)
            {
                GUI.Label(
                    new Rect(x + 10f, rowY, width - 20f, line),
                    "No player CombatHealth found (requires spawned NetworkObjects).",
                    m_LogLabelStyle
                );
                rowY += line;
            }
            else
            {
                for (int i = 0; i < healthCount; i++)
                {
                    CombatHealth health = m_PlayerHealthTargets[i];
                    if (health == null)
                    {
                        continue;
                    }

                    DrawHealthRow(new Rect(x + 10f, rowY, width - 20f, 20f), health);
                    rowY += 24f;
                }
            }

            GUI.Label(new Rect(x + 10f, rowY, width - 20f, line), "Recent effects:", m_HeaderLabelStyle);
            rowY += line;
            rowY = DrawLogList(x + 18f, rowY, width - 28f, m_RecentEffects, effectCount, line);

            GUI.Label(new Rect(x + 10f, rowY, width - 20f, line), "Recent damage:", m_HeaderLabelStyle);
            rowY += line;
            DrawLogList(x + 18f, rowY, width - 28f, m_RecentDamage, damageCount, line);
        }

        private void DrawHealthRow(Rect rect, CombatHealth health)
        {
            NetworkObject networkObject = health.CachedNetworkObject;
            string playerLabel = health.gameObject.name;
            if (networkObject != null)
            {
                playerLabel =
                    $"{playerLabel} [obj:{networkObject.NetworkObjectId} owner:{networkObject.OwnerClientId}]";
            }

            float maxHealth = Mathf.Max(1f, health.MaxHealth);
            float currentHealth = Mathf.Clamp(health.CurrentHealth, 0f, maxHealth);
            float fillRatio = currentHealth / maxHealth;

            GUI.Label(rect, $"{playerLabel}  {currentHealth:0}/{maxHealth:0}", m_LogLabelStyle);

            Rect barRect = new Rect(rect.x + rect.width * 0.45f, rect.y + 3f, rect.width * 0.52f, 14f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f), new Color(0.1f, 0.75f, 0.2f), fillRatio);
            GUI.DrawTexture(
                new Rect(barRect.x + 1f, barRect.y + 1f, (barRect.width - 2f) * fillRatio, barRect.height - 2f),
                Texture2D.whiteTexture
            );
            GUI.color = previousColor;
        }

        private float DrawLogList(
            float x,
            float y,
            float width,
            List<LogEntry> source,
            int count,
            float lineHeight
        )
        {
            int start = Mathf.Max(0, source.Count - count);
            for (int i = start; i < source.Count; i++)
            {
                GUI.Label(new Rect(x, y, width, lineHeight), source[i].Text, m_LogLabelStyle);
                y += lineHeight;
            }

            if (count == 0)
            {
                GUI.Label(new Rect(x, y, width, lineHeight), "(none yet)", m_LogLabelStyle);
                y += lineHeight;
            }

            return y;
        }

        private void RebuildPlayerHealthTargets()
        {
            m_PlayerListDirty = false;
            CombatHealthRegistry.CopyTo(m_PlayerHealthTargets);
            if (m_PlayerHealthTargets.Count == 0)
            {
                return;
            }

            for (int i = m_PlayerHealthTargets.Count - 1; i >= 0; i--)
            {
                CombatHealth health = m_PlayerHealthTargets[i];
                if (health == null || !health.isActiveAndEnabled || !IsPlayerHealth(health))
                {
                    m_PlayerHealthTargets.RemoveAt(i);
                }
            }

            m_PlayerHealthTargets.Sort(CompareByNetworkObjectId);
        }

        private static bool IsPlayerHealth(CombatHealth health)
        {
            if (health == null)
            {
                return false;
            }

            NetworkObject networkObject = health.CachedNetworkObject;
            if (networkObject != null && networkObject.IsPlayerObject)
            {
                return true;
            }

            return health.CompareTag("Player");
        }

        private static int CompareByNetworkObjectId(CombatHealth a, CombatHealth b)
        {
            if (a == b)
            {
                return 0;
            }
            if (a == null)
            {
                return 1;
            }
            if (b == null)
            {
                return -1;
            }

            NetworkObject networkA = a.CachedNetworkObject;
            NetworkObject networkB = b.CachedNetworkObject;

            ulong idA = networkA != null ? networkA.NetworkObjectId : ulong.MaxValue;
            ulong idB = networkB != null ? networkB.NetworkObjectId : ulong.MaxValue;
            return idA.CompareTo(idB);
        }

        private void HandleHealthRegistered(CombatHealth _)
        {
            m_PlayerListDirty = true;
        }

        private void HandleHealthUnregistered(CombatHealth _)
        {
            m_PlayerListDirty = true;
        }

        private void HandleHealthChanged(CombatHealth.HealthChangedEvent _)
        {
            // NetworkObject IDs and player-object flags can change after spawn; defer rebuild.
            m_PlayerListDirty = true;
        }

        private void HandleEffectApplied(DialogueSceneEffectsController.AppliedEffectInfo info)
        {
            string effectName = string.IsNullOrWhiteSpace(info.EffectName) ? info.EffectType : info.EffectName;
            string entry =
                $"{DateTime.Now:HH:mm:ss} {effectName} -> target:{info.TargetNetworkObjectId} src:{info.SourceNetworkObjectId} scale:{info.Scale:0.00}";
            AppendLogEntry(m_RecentEffects, entry, Time.unscaledTime + m_LogLifetimeSeconds);
        }

        private void HandleDamageApplied(CombatHealth.DamageEvent damageEvent)
        {
            string targetName = damageEvent.Target != null ? damageEvent.Target.gameObject.name : "(missing)";
            string entry =
                $"{DateTime.Now:HH:mm:ss} {damageEvent.DamageType} {damageEvent.DamageAmount:0.0} on {targetName} [{damageEvent.CurrentHealth:0.0}]";
            AppendLogEntry(m_RecentDamage, entry, Time.unscaledTime + m_LogLifetimeSeconds);
        }

        private void AppendLogEntry(List<LogEntry> target, string text, float expiresAt)
        {
            if (target == null)
            {
                return;
            }

            target.Add(
                new LogEntry
                {
                    Text = text,
                    ExpiresAt = expiresAt,
                }
            );

            int overflow = target.Count - Mathf.Max(2, m_MaxLogEntries * 2);
            if (overflow <= 0)
            {
                return;
            }

            target.RemoveRange(0, overflow);
        }

        private static void PruneExpiredLogs(List<LogEntry> source, float now)
        {
            if (source == null || source.Count == 0)
            {
                return;
            }

            for (int i = source.Count - 1; i >= 0; i--)
            {
                LogEntry entry = source[i];
                if (entry != null && now < entry.ExpiresAt)
                {
                    continue;
                }

                source.RemoveAt(i);
            }
        }

        private void EnsureStyles()
        {
            if (m_HeaderLabelStyle == null)
            {
                m_HeaderLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    richText = false,
                };
                m_HeaderLabelStyle.normal.textColor = new Color(0.88f, 0.95f, 1f);
            }

            if (m_LogLabelStyle == null)
            {
                m_LogLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    richText = false,
                };
                m_LogLabelStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
            }
        }
    }
}
