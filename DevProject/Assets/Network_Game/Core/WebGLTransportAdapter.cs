using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Network_Game.Core
{
    /// <summary>
    /// Automatically switches UnityTransport to WebSockets when running in WebGL.
    /// This prevents UDP connection errors in browser-based multiplayer.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(UnityTransport))]
    public class WebGLTransportAdapter : MonoBehaviour
    {
        private void Awake()
        {
            var transport = GetComponent<UnityTransport>();
            if (transport != null)
            {
#if UNITY_WEBGL
                Debug.Log("[WebGLTransportAdapter] WebGL Build detected. Switching Unity Transport to Use WebSockets.");
                transport.UseWebSockets = true;
#else
                Debug.Log("[WebGLTransportAdapter] Native platform detected. Using default UDP transport.");
#endif
            }
        }
    }
}
