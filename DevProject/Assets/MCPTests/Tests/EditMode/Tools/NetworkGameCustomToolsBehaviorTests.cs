using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Network_Game.Editor.CustomTools;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class NetworkGameCustomToolsBehaviorTests
    {
        [Test]
        public void EffectsTestMode_Status_ReturnsStructuredPayload_InEditMode()
        {
            var result = ToJObject(EffectsTestModeTool.HandleCommand(new JObject
            {
                ["action"] = "status"
            }));

            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"], "Status should return a data payload.");
            Assert.IsNotNull(result["data"]?["playMode"], "Status payload should include playMode.");
        }

        [Test]
        public void EffectsTestMode_Start_ReturnsError_WhenNotPlaying()
        {
            var result = ToJObject(EffectsTestModeTool.HandleCommand(new JObject
            {
                ["action"] = "start"
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            string error = result.Value<string>("error");
            Assert.IsFalse(string.IsNullOrWhiteSpace(error), "Error response should include error.");
            StringAssert.Contains("Play Mode", error);
        }

        [Test]
        public void EffectsTestMode_InvalidAction_ReturnsError()
        {
            var result = ToJObject(EffectsTestModeTool.HandleCommand(new JObject
            {
                ["action"] = "definitely_not_real"
            }));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Unsupported action", result.Value<string>("error"));
        }

        [Test]
        public void ProbeEffectTargets_ReturnsErrorWithStructuredPayload_WhenNotPlaying()
        {
            var result = ToJObject(ProbeEffectTargetsTool.HandleCommand(new JObject()));

            Assert.IsFalse(result.Value<bool>("success"), result.ToString());
            StringAssert.Contains("Play Mode", result.Value<string>("error"));
            Assert.IsNotNull(result["data"], "Error payload should include probe data.");
            Assert.AreEqual(false, result["data"]?["is_playing"]?.Value<bool>());
            Assert.IsNotNull(result["data"]?["options"], "Probe data should include options.");
        }
    }
}
