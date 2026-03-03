using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace NpcDialogue.Tests.Editor
{
    /// <summary>
    /// EditMode tests for the NpcDialogue training configuration.
    ///
    /// Validates config/ppo/NpcDialogue.yaml parameters are within sensible ranges
    /// and that NpcDialogueAgent's observation/action spec matches the config.
    ///
    /// Run via: Unity Test Runner → EditMode → NpcDialogue.Tests.Editor
    /// </summary>
    [TestFixture]
    public class NpcDialogueConfigEditModeTests
    {
        private const string k_ConfigRelativePath = "config/ppo/NpcDialogue.yaml";
        private const string k_BehaviorKey = "behaviors";
        private const string k_BehaviorName = "NpcDialogue";
        private const int k_ExpectedObsSize = 7;
        private const int k_ExpectedActionBranchSize = 3;

        private string m_ConfigPath;
        private string m_ConfigText;

        [SetUp]
        public void SetUp()
        {
            // Resolve from the repo root (two levels above the Assets/ folder).
            string assetsPath = Application.dataPath;                    // .../DevProject/Assets
            string devProjectPath = Path.GetDirectoryName(assetsPath);  // .../DevProject
            string repoRoot = Path.GetDirectoryName(devProjectPath);    // .../ml-agents
            m_ConfigPath = Path.Combine(repoRoot, k_ConfigRelativePath).Replace('\\', '/');

            Assert.IsTrue(File.Exists(m_ConfigPath),
                $"Config not found at: {m_ConfigPath}\n" +
                "Run the game once or create config/ppo/NpcDialogue.yaml.");

            m_ConfigText = File.ReadAllText(m_ConfigPath);
        }

        // ── 1. File structure ────────────────────────────────────────────────

        [Test]
        public void Config_FileExists_AndIsNotEmpty()
        {
            Assert.IsNotEmpty(m_ConfigText, "NpcDialogue.yaml must not be empty.");
        }

        [Test]
        public void Config_ContainsBehaviorName_NpcDialogue()
        {
            Assert.IsTrue(m_ConfigText.Contains(k_BehaviorName),
                $"Config must declare behavior '{k_BehaviorName}' matching the BehaviorParameters name.");
        }

        [Test]
        public void Config_TrainerType_IsPPO()
        {
            Assert.IsTrue(m_ConfigText.Contains("trainer_type: ppo"),
                "trainer_type must be 'ppo' for discrete-action dialogue training.");
        }

        // ── 2. Hyperparameter sanity ─────────────────────────────────────────

        [Test]
        public void Config_BatchSize_IsInValidRange()
        {
            int batchSize = ParseYamlInt(m_ConfigText, "batch_size");
            Assert.GreaterOrEqual(batchSize, 32,   "batch_size must be >= 32");
            Assert.LessOrEqual(batchSize,    2048, "batch_size must be <= 2048 for single-agent dialogue");
        }

        [Test]
        public void Config_BufferSize_IsLargerThan_BatchSize()
        {
            int batchSize  = ParseYamlInt(m_ConfigText, "batch_size");
            int bufferSize = ParseYamlInt(m_ConfigText, "buffer_size");
            Assert.Greater(bufferSize, batchSize,
                "buffer_size must be greater than batch_size.");
        }

        [Test]
        public void Config_LearningRate_IsInValidRange()
        {
            float lr = ParseYamlFloat(m_ConfigText, "learning_rate");
            Assert.GreaterOrEqual(lr, 1e-5f, "learning_rate must be >= 1e-5");
            Assert.LessOrEqual(lr,   1e-2f,  "learning_rate must be <= 0.01");
        }

        [Test]
        public void Config_Beta_IsInValidRange()
        {
            float beta = ParseYamlFloat(m_ConfigText, "beta");
            Assert.GreaterOrEqual(beta, 0.0001f, "beta (entropy) must be >= 0.0001");
            Assert.LessOrEqual(beta,    0.1f,    "beta (entropy) must be <= 0.1");
        }

        [Test]
        public void Config_Gamma_IsInValidRange()
        {
            float gamma = ParseYamlFloat(m_ConfigText, "gamma");
            Assert.GreaterOrEqual(gamma, 0.9f,  "gamma must be >= 0.9 for long conversations");
            Assert.LessOrEqual(gamma,    1.0f,  "gamma must be <= 1.0");
        }

        [Test]
        public void Config_MaxSteps_IsPositive()
        {
            int maxSteps = ParseYamlInt(m_ConfigText, "max_steps");
            Assert.Greater(maxSteps, 0, "max_steps must be positive.");
            Assert.GreaterOrEqual(maxSteps, 100_000,
                "max_steps must be at least 100k for meaningful dialogue training.");
        }

        // ── 3. Network settings ──────────────────────────────────────────────

        [Test]
        public void Config_HasMemoryBlock_WithSequenceLength()
        {
            Assert.IsTrue(m_ConfigText.Contains("memory:"),
                "Config must include a memory (LSTM) block — required for within-conversation context.");
            Assert.IsTrue(m_ConfigText.Contains("sequence_length:"),
                "Memory block must specify sequence_length.");
            Assert.IsTrue(m_ConfigText.Contains("memory_size:"),
                "Memory block must specify memory_size.");
        }

        [Test]
        public void Config_SequenceLength_IsInValidRange()
        {
            int seqLen = ParseYamlInt(m_ConfigText, "sequence_length");
            Assert.GreaterOrEqual(seqLen, 8,   "sequence_length must be >= 8");
            Assert.LessOrEqual(seqLen,    256, "sequence_length must be <= 256");
        }

        [Test]
        public void Config_HiddenUnits_IsInValidRange()
        {
            int hiddenUnits = ParseYamlInt(m_ConfigText, "hidden_units");
            Assert.GreaterOrEqual(hiddenUnits, 32,   "hidden_units must be >= 32");
            Assert.LessOrEqual(hiddenUnits,    1024, "hidden_units must be <= 1024 for 7-obs input");
        }

        // ── 4. Reward signals ────────────────────────────────────────────────

        [Test]
        public void Config_HasExtrinsicRewardSignal()
        {
            Assert.IsTrue(m_ConfigText.Contains("extrinsic:"),
                "Config must have an extrinsic reward signal.");
        }

        [Test]
        public void Config_HasCuriosityRewardSignal()
        {
            Assert.IsTrue(m_ConfigText.Contains("curiosity:"),
                "Config must have a curiosity signal to prevent idle-lock on 3 discrete actions.");
        }

        [Test]
        public void Config_CuriosityStrength_IsLowEnoughNotToOverride_ExtrinsicRewards()
        {
            // Find curiosity section and parse its strength
            int curiosityIdx = m_ConfigText.IndexOf("curiosity:", StringComparison.Ordinal);
            Assert.Greater(curiosityIdx, 0, "curiosity section not found");

            // Extract the 200 chars after 'curiosity:' to find strength within that block
            string curiosityBlock = m_ConfigText.Substring(curiosityIdx,
                Mathf.Min(200, m_ConfigText.Length - curiosityIdx));
            float strength = ParseYamlFloat(curiosityBlock, "strength");

            Assert.LessOrEqual(strength, 0.05f,
                "Curiosity strength must be <= 0.05 to not overpower extrinsic dialogue rewards.");
        }

        // ── 5. Agent class consistency ────────────────────────────────────────

        [Test]
        public void Agent_ConversationPhaseEnum_HasFourValues()
        {
            var agentType = Type.GetType("NpcDialogueAgent, Assembly-CSharp");
            Assert.IsNotNull(agentType, "NpcDialogueAgent not found in Assembly-CSharp.");

            var phaseType = agentType.GetNestedType("ConversationPhase", BindingFlags.NonPublic);
            Assert.IsNotNull(phaseType, "ConversationPhase enum not found on NpcDialogueAgent.");

            string[] names = Enum.GetNames(phaseType);
            Assert.AreEqual(4, names.Length,
                "ConversationPhase must have exactly 4 values: Idle, Initiated, Responded, Resolved.");
        }

        [Test]
        public void Agent_PhaseEnum_NormalizedValuesSpanZeroToOne()
        {
            var agentType = Type.GetType("NpcDialogueAgent, Assembly-CSharp");
            var phaseType = agentType?.GetNestedType("ConversationPhase", BindingFlags.NonPublic);
            Assert.IsNotNull(phaseType);

            int[] values = (int[])Enum.GetValues(phaseType);
            float min = values[0] / 3f;
            float max = values[values.Length - 1] / 3f;

            Assert.AreEqual(0f, min, 0.001f, "Idle phase must normalize to 0.0");
            Assert.AreEqual(1f, max, 0.001f, "Resolved phase must normalize to 1.0");
        }

        [Test]
        public void Agent_HasExpectedPrivateObservationFields()
        {
            var agentType = Type.GetType("NpcDialogueAgent, Assembly-CSharp");
            Assert.IsNotNull(agentType);

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Assert.IsNotNull(agentType.GetField("m_ConversationPhase", flags),
                "m_ConversationPhase field missing from NpcDialogueAgent.");
            Assert.IsNotNull(agentType.GetField("m_LastEffectFireTime", flags),
                "m_LastEffectFireTime field missing from NpcDialogueAgent.");
            Assert.IsNotNull(agentType.GetField("m_EffectDecaySeconds", flags),
                "m_EffectDecaySeconds field missing from NpcDialogueAgent.");
        }

        [Test]
        public void Config_SummaryFreq_AllowsReasonableMonitoring()
        {
            int summaryFreq = ParseYamlInt(m_ConfigText, "summary_freq");
            Assert.Greater(summaryFreq, 0, "summary_freq must be positive.");
            Assert.LessOrEqual(summaryFreq, 50_000,
                "summary_freq <= 50k ensures TensorBoard updates are not too infrequent.");
        }

        // ── YAML helpers (minimal, no external YAML library needed) ──────────

        // Finds "key: value" in a yaml string and parses as int.
        private static int ParseYamlInt(string yaml, string key)
        {
            string pattern = key + ":";
            int idx = yaml.IndexOf(pattern, StringComparison.Ordinal);
            Assert.Greater(idx, -1, $"Key '{key}' not found in YAML.");
            int valueStart = idx + pattern.Length;
            int lineEnd = yaml.IndexOf('\n', valueStart);
            string raw = yaml.Substring(valueStart, (lineEnd > 0 ? lineEnd : yaml.Length) - valueStart)
                .Trim().Split('#')[0].Trim();
            Assert.IsTrue(int.TryParse(raw, out int result),
                $"Could not parse '{key}' value '{raw}' as int.");
            return result;
        }

        private static float ParseYamlFloat(string yaml, string key)
        {
            string pattern = key + ":";
            int idx = yaml.IndexOf(pattern, StringComparison.Ordinal);
            Assert.Greater(idx, -1, $"Key '{key}' not found in YAML.");
            int valueStart = idx + pattern.Length;
            int lineEnd = yaml.IndexOf('\n', valueStart);
            string raw = yaml.Substring(valueStart, (lineEnd > 0 ? lineEnd : yaml.Length) - valueStart)
                .Trim().Split('#')[0].Trim();
            Assert.IsTrue(float.TryParse(raw,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float result),
                $"Could not parse '{key}' value '{raw}' as float.");
            return result;
        }
    }
}
