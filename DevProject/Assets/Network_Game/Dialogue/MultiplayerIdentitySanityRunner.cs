using System.Collections;
using System.Collections.Generic;
using Network_Game.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Server-side sanity check for per-client identity/context propagation.
    /// Use this before multi-client dialogue tests to confirm identity bindings are ready.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerIdentitySanityRunner : NetworkBehaviour
    {
        [SerializeField]
        private bool m_RunOnServerSpawn;

        [SerializeField]
        [Min(1)]
        private int m_ExpectedPlayers = 2;

        [SerializeField]
        [Min(2f)]
        private float m_TimeoutSeconds = 45f;

        [SerializeField]
        [Min(0.2f)]
        private float m_PollIntervalSeconds = 1f;

        [SerializeField]
        private bool m_RequirePromptContext = true;

        private Coroutine m_CheckRoutine;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer || !m_RunOnServerSpawn)
            {
                return;
            }

            RunNow();
        }

        [ContextMenu("Dialogue/Run Multiplayer Identity Sanity Check")]
        public void RunNow()
        {
            if (!Application.isPlaying)
            {
                NGLog.Warn("Dialogue", "Identity sanity check requires Play Mode.");
                return;
            }

            if (!IsServer)
            {
                NGLog.Warn("Dialogue", "Identity sanity check must run on server/host.");
                return;
            }

            if (m_CheckRoutine != null)
            {
                StopCoroutine(m_CheckRoutine);
            }

            m_CheckRoutine = StartCoroutine(RunCheckRoutine());
        }

        private IEnumerator RunCheckRoutine()
        {
            NetworkDialogueService service = NetworkDialogueService.Instance;
            if (service == null)
            {
                NGLog.Warn(
                    "Dialogue",
                    "Identity sanity check failed: NetworkDialogueService missing."
                );
                m_CheckRoutine = null;
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + Mathf.Max(2f, m_TimeoutSeconds);
            while (Time.realtimeSinceStartup < deadline)
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening || manager.ConnectedClients == null)
                {
                    yield return new WaitForSeconds(Mathf.Max(0.2f, m_PollIntervalSeconds));
                    continue;
                }

                int connectedCount = manager.ConnectedClients.Count;
                if (connectedCount < Mathf.Max(1, m_ExpectedPlayers))
                {
                    yield return new WaitForSeconds(Mathf.Max(0.2f, m_PollIntervalSeconds));
                    continue;
                }

                bool allReady = true;
                var failures = new List<string>();
                foreach (var pair in manager.ConnectedClients)
                {
                    ulong clientId = pair.Key;
                    NetworkClient client = pair.Value;
                    ulong expectedPlayerNetworkId =
                        client != null && client.PlayerObject != null
                            ? client.PlayerObject.NetworkObjectId
                            : 0;

                    if (!service.TryGetPlayerIdentityByClientId(clientId, out var snapshot))
                    {
                        allReady = false;
                        failures.Add($"client={clientId}: missing identity");
                        continue;
                    }

                    if (
                        expectedPlayerNetworkId != 0
                        && snapshot.PlayerNetworkId != expectedPlayerNetworkId
                    )
                    {
                        allReady = false;
                        failures.Add(
                            $"client={clientId}: player net mismatch expected={expectedPlayerNetworkId} actual={snapshot.PlayerNetworkId}"
                        );
                    }

                    if (string.IsNullOrWhiteSpace(snapshot.NameId))
                    {
                        allReady = false;
                        failures.Add($"client={clientId}: empty name_id");
                    }

                    if (m_RequirePromptContext && IsPlaceholderJson(snapshot.CustomizationJson))
                    {
                        allReady = false;
                        failures.Add($"client={clientId}: placeholder prompt context");
                    }
                }

                if (allReady)
                {
                    NGLog.Info(
                        "Dialogue",
                        NGLog.Format(
                            "Identity sanity check passed",
                            ("connectedPlayers", connectedCount),
                            ("expectedPlayers", m_ExpectedPlayers)
                        )
                    );
                    NGLog.Info("Dialogue", service.BuildPlayerIdentityReport());
                    m_CheckRoutine = null;
                    yield break;
                }

                NGLog.Debug(
                    "Dialogue",
                    $"Identity sanity check pending: {string.Join(" | ", failures)}"
                );
                yield return new WaitForSeconds(Mathf.Max(0.2f, m_PollIntervalSeconds));
            }

            NGLog.Warn(
                "Dialogue",
                NGLog.Format(
                    "Identity sanity check failed (timeout)",
                    ("expectedPlayers", m_ExpectedPlayers),
                    ("timeoutSeconds", m_TimeoutSeconds)
                )
            );
            NGLog.Info("Dialogue", service.BuildPlayerIdentityReport());
            m_CheckRoutine = null;
        }

        private static bool IsPlaceholderJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            string trimmed = json.Trim();
            return trimmed == "{}" || trimmed == "{ }" || trimmed == "null";
        }
    }
}
