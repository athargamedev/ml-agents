using UnityEngine;

namespace Network_Game.Dialogue.MCP
{
    /// <summary>
    /// Marker component added to particle system GameObjects spawned by the dialogue system.
    /// Used by DialogueMCPBridge.GetActiveVfxState() to identify dialogue-owned effects.
    /// </summary>
    public class DialogueSpawnedMarker : MonoBehaviour
    {
        [HideInInspector]
        public string SourceNpcProfileId;

        [HideInInspector]
        public string EffectTag;

        [HideInInspector]
        public ulong SourceNetworkObjectId;

        [HideInInspector]
        public ulong TargetNetworkObjectId;

        [HideInInspector]
        public float ConfiguredScale = 1f;

        [HideInInspector]
        public float ConfiguredDurationSeconds = 0f;

        [HideInInspector]
        public bool AttachToTarget;

        [HideInInspector]
        public bool FitToTargetMesh;

        [HideInInspector]
        public uint EffectSeed;
    }
}
