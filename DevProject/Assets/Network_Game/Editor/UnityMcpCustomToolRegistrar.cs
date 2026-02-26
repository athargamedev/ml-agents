using System;
using System.Linq;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEngine;

namespace NetworkGame.EditorTools
{
    public static class UnityMcpCustomToolRegistrar
    {
        [MenuItem("Network Game/MCP/Admin/Register All Custom Tools")]
        public static void RegisterAllCustomTools()
        {
            try
            {
                var discovery = MCPServiceLocator.ToolDiscovery;
                discovery.InvalidateCache();
                var allTools = discovery.DiscoverAllTools();
                if (allTools == null || allTools.Count == 0)
                {
                    Debug.Log("[UnityMCPTools] No MCP tools discovered.");
                    return;
                }

                var targetTools = allTools
                    .Where(t => t != null && (!t.IsBuiltIn || !t.AutoRegister))
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int changed = 0;
                foreach (var tool in targetTools)
                {
                    if (discovery.IsToolEnabled(tool.Name))
                    {
                        continue;
                    }

                    discovery.SetToolEnabled(tool.Name, true);
                    changed++;
                }

                Debug.Log($"[UnityMCPTools] Enabled extra tools (custom + off-by-default): {changed} changed / {targetTools.Count} total.");
                if (targetTools.Count > 0)
                {
                    Debug.Log("[UnityMCPTools] Extra tools: " + string.Join(", ", targetTools.Select(t => t.Name)));
                }

                ReregisterIfConnected(MCPServiceLocator.TransportManager.GetClient(TransportMode.Http));
                ReregisterIfConnected(MCPServiceLocator.TransportManager.GetClient(TransportMode.Stdio));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityMCPTools] Failed to register custom tools: {ex}");
            }
        }

        [MenuItem("Network Game/MCP/Admin/Register All Custom Tools", true)]
        private static bool ValidateRegisterAllCustomTools()
        {
            return !EditorApplication.isCompiling && !EditorApplication.isUpdating;
        }

        private static void ReregisterIfConnected(IMcpTransportClient client)
        {
            if (client == null || !client.IsConnected)
            {
                return;
            }

            _ = client.ReregisterToolsAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogWarning($"[UnityMCPTools] Reregister failed for {client.TransportName}: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                Debug.Log($"[UnityMCPTools] Reregistered tools on transport: {client.TransportName}");
            });
        }
    }
}
