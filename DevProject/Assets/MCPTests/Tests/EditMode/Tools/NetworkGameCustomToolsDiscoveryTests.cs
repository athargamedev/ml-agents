using System.Linq;
using MCPForUnity.Editor.Services;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Tools
{
    public class NetworkGameCustomToolsDiscoveryTests
    {
        private static readonly string[] ExpectedCustomToolNames =
        {
            "ng_effects_test_mode",
            "ng_disable_effect_feedback_prompt",
            "ng_enable_effect_feedback_prompt",
            "ng_toggle_effect_feedback_prompt",
            "ng_get_effect_feedback_prompt_status",
            "ng_run_all_effects_direct",
            "ng_stop_all_effects_direct",
            "ng_spawn_direct_npc_power",
            "ng_probe_effect_targets",
        };

        [Test]
        public void NetworkGame_CustomTools_AreDiscovered_AsCustomTools()
        {
            var discovery = new ToolDiscoveryService();
            discovery.InvalidateCache();
            var tools = discovery.DiscoverAllTools();

            foreach (string toolName in ExpectedCustomToolNames)
            {
                var metadata = tools.FirstOrDefault(t => t.Name == toolName);
                Assert.IsNotNull(metadata, $"Expected tool '{toolName}' to be discovered.");
                Assert.IsFalse(metadata.IsBuiltIn, $"Tool '{toolName}' should be classified as custom.");
                Assert.AreEqual("Network_Game.Editor", metadata.AssemblyName, $"Tool '{toolName}' should come from the project editor assembly.");
            }
        }
    }
}
