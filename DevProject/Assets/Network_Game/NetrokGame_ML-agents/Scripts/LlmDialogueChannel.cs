using System;
using System.Collections.Generic;
using System.Text;
using Unity.MLAgents.SideChannels;
using UnityEngine;

namespace Unity.MLAgents.NpcDialogue
{
    /// <summary>
    /// SideChannel that bridges NPC dialogue requests to a Python LLM backend.
    ///
    /// Usage:
    ///   1. Create an instance and register it with SideChannelManager.
    ///   2. Call SendRequest() when a player addresses an NPC.
    ///   3. Subscribe to OnResponseReceived to get the LLM's reply (arrives next step).
    ///
    /// The Python-side channel GUID must match: a1b2c3d4-e5f6-7890-abcd-ef1234567890
    /// </summary>
    public class LlmDialogueChannel : SideChannel
    {
        public static readonly Guid k_ChannelId =
            new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        /// <summary>Fired on the main thread when a response arrives from Python.</summary>
        public event Action<DialogueResponse> OnResponseReceived;

        public LlmDialogueChannel()
        {
            ChannelId = k_ChannelId;
        }

        /// <summary>
        /// Send a dialogue request to the Python LLM backend.
        /// The response will arrive via OnResponseReceived on the next simulation step.
        /// </summary>
        public void SendRequest(DialogueRequest request)
        {
            string json = JsonUtility.ToJson(request);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var msg = new OutgoingMessage())
            {
                msg.SetRawBytes(bytes);
                QueueMessageToSend(msg);
            }
        }

        protected override void OnMessageReceived(IncomingMessage msg)
        {
            try
            {
                string json = Encoding.UTF8.GetString(msg.GetRawBytes());
                var response = JsonUtility.FromJson<DialogueResponse>(json);
                OnResponseReceived?.Invoke(response);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LlmDialogueChannel] Failed to parse response: {ex.Message}");
            }
        }
    }

    [Serializable]
    public class DialogueRequest
    {
        /// <summary>Unique request identifier used for response correlation.</summary>
        public string requestId;

        /// <summary>
        /// Optional request type. Null/empty means normal dialogue.
        /// Reserved values include "ping" for connection probes.
        /// </summary>
        public string messageType;

        /// <summary>Identifier for the NPC being addressed.</summary>
        public string npcId;

        /// <summary>What the player just said.</summary>
        public string playerInput;

        /// <summary>JSON array of prior conversation turns for multi-turn context.</summary>
        public string conversationHistory;

        /// <summary>Short persona description for the NPC (e.g. "gruff blacksmith who distrusts strangers").</summary>
        public string npcPersonality;
    }

    [Serializable]
    public class DialogueResponse
    {
        /// <summary>Unique request identifier echoed back from the Python bridge.</summary>
        public string requestId;

        /// <summary>Which NPC this response is for.</summary>
        public string npcId;

        /// <summary>Bridge-level status code (for example ok, error, busy).</summary>
        public string status;

        /// <summary>The LLM-generated dialogue line.</summary>
        public string responseText;

        /// <summary>Optional bridge or backend error message.</summary>
        public string error;

        /// <summary>Detected or generated emotion tag (e.g. "neutral", "angry", "happy").</summary>
        public string emotion;

        /// <summary>How confident the LLM was that this response is in-character (0-1).</summary>
        public float confidence;

        /// <summary>Unix epoch milliseconds when the request was queued by the bridge.</summary>
        public long queuedAtUnixMs;

        /// <summary>Unix epoch milliseconds when the response completed in the bridge.</summary>
        public long completedAtUnixMs;
    }
}
