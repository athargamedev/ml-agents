using System;
using Network_Game.Dialogue.Effects;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Network_Game.Dialogue.Editor
{
    /// <summary>
    /// Editor utility to run one end-to-end dialogue FX request in Play Mode.
    /// Useful for validating LLM tag emission + runtime effect dispatch quickly.
    /// </summary>
    public static class DialogueFxSmokeTestMenu
    {
        private const string MenuPath = "Network Game/MCP/Run FX Smoke Test";
        private static bool s_Subscribed;
        private static bool s_RequestInFlight;
        private static int s_ActiveClientRequestId;
        private static int s_NextClientRequestId = 99100;

        [MenuItem(MenuPath)]
        public static void RunFxSmokeTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DialogueFXSmokeTest] Enter Play Mode first.");
                return;
            }

            NetworkDialogueService service = NetworkDialogueService.Instance;
            if (service == null)
            {
                Debug.LogWarning("[DialogueFXSmokeTest] NetworkDialogueService.Instance is null.");
                return;
            }

            if (s_RequestInFlight)
            {
                Debug.LogWarning(
                    $"[DialogueFXSmokeTest] Request already in flight (clientRequestId={s_ActiveClientRequestId})."
                );
                return;
            }

            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            {
                Debug.LogWarning("[DialogueFXSmokeTest] Local Netcode client is not ready.");
                return;
            }

            NetworkObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayer == null)
            {
                Debug.LogWarning("[DialogueFXSmokeTest] Local player object is null.");
                return;
            }

#if UNITY_2023_1_OR_NEWER
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsByType<NpcDialogueActor>(
                FindObjectsInactive.Exclude
            );
#else
            NpcDialogueActor[] actors = UnityEngine.Object.FindObjectsOfType<NpcDialogueActor>();
#endif

            if (actors == null || actors.Length == 0)
            {
                Debug.LogWarning("[DialogueFXSmokeTest] No NpcDialogueActor found in scene.");
                return;
            }

            NpcDialogueActor actor = actors[0];
            if (actor == null || actor.NetworkObject == null)
            {
                Debug.LogWarning("[DialogueFXSmokeTest] Selected NPC actor has no NetworkObject.");
                return;
            }

            ulong speakerId = actor.NetworkObjectId;
            ulong listenerId = localPlayer.NetworkObjectId;
            ulong clientId = localPlayer.OwnerClientId;
            string conversationKey = service.ResolveConversationKey(
                speakerId,
                listenerId,
                clientId,
                null
            );

            if (!s_Subscribed)
            {
                NetworkDialogueService.OnDialogueResponse += HandleDialogueResponse;
                s_Subscribed = true;
            }

            const string forcedTag =
                "[EFFECT: Lightning Storm | Target: Player | Intensity: 2.0 | Radius: 4 | Speed: 28 | Color: #66CCFF]";
            string prompt =
                "Reply in-character with one short dramatic sentence, and append this exact hidden tag at the end: "
                + forcedTag;

            int clientRequestId = ++s_NextClientRequestId;
            s_ActiveClientRequestId = clientRequestId;
            s_RequestInFlight = true;

            service.RequestDialogue(
                new NetworkDialogueService.DialogueRequest
                {
                    Prompt = prompt,
                    ConversationKey = conversationKey,
                    SpeakerNetworkId = speakerId,
                    ListenerNetworkId = listenerId,
                    RequestingClientId = clientId,
                    Broadcast = false,
                    BroadcastDuration = 0f,
                    NotifyClient = true,
                    ClientRequestId = clientRequestId,
                    IsUserInitiated = true,
                    BlockRepeatedPrompt = false,
                    MinRepeatDelaySeconds = 0f,
                    RequireUserReply = false,
                }
            );

            Debug.Log(
                $"[DialogueFXSmokeTest] Sent request: speaker={speakerId}, listener={listenerId}, key={conversationKey}"
            );
            Debug.Log($"[DialogueFXSmokeTest] Forced tag target: {forcedTag}");
        }

        private static void HandleDialogueResponse(NetworkDialogueService.DialogueResponse response)
        {
            if (response.Request.ClientRequestId != s_ActiveClientRequestId)
            {
                return;
            }

            s_RequestInFlight = false;

            string text = response.ResponseText ?? string.Empty;
            bool hasEffectTag =
                text.IndexOf("[EFFECT:", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("[FX:", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("EFFECT:", StringComparison.OrdinalIgnoreCase) >= 0;

            Debug.Log(
                $"[DialogueFXSmokeTest] Response status={response.Status}, hasEffectTag={hasEffectTag}, error='{response.Error ?? ""}'"
            );
            Debug.Log($"[DialogueFXSmokeTest] Response text: {text}");

            EffectCatalog catalog = EffectCatalog.Load();
            var intents = EffectParser.ExtractIntents(text, catalog, stripTags: false);
            Debug.Log($"[DialogueFXSmokeTest] Parsed intents count={intents.Count}");
            for (int i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                Debug.Log(
                    $"[DialogueFXSmokeTest] Intent[{i}] tag='{intent.rawTagName}', target='{intent.target}', intensity={intent.intensity:F2}, scale={intent.scale:F2}, duration={intent.duration:F2}, radius={intent.radius:F2}, speed={intent.speed:F2}"
                );
            }
        }
    }
}
