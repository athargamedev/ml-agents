#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// Represents a message in a dialogue inference conversation.
    /// </summary>
    public readonly struct DialogueInferenceMessage
    {
        /// <summary>
        /// Gets the role of the message sender (e.g., "user", "assistant", "system").
        /// </summary>
        public readonly string Role;
        
        /// <summary>
        /// Gets the content of the message.
        /// </summary>
        public readonly string Content;

        /// <summary>
        /// Initializes a new instance of the <see cref="DialogueInferenceMessage"/> struct.
        /// </summary>
        /// <param name="role">The role of the message sender. Defaults to "user" if null or whitespace.</param>
        /// <param name="content">The content of the message.</param>
        public DialogueInferenceMessage(string role, string content)
        {
            Role = string.IsNullOrWhiteSpace(role) ? "user" : role.Trim();
            Content = content ?? string.Empty;
        }
        
        /// <summary>
        /// Validates the message to ensure it meets basic requirements.
        /// </summary>
        /// <returns>True if the message is valid, false otherwise.</returns>
        public bool IsValid()
        {
            // A valid message should have non-empty content
            return !string.IsNullOrEmpty(Content);
        }
    }

    /// <summary>
    /// Configuration settings for the dialogue inference runtime.
    /// </summary>
    public sealed class DialogueInferenceRuntimeConfig
    {
        /// <summary>
        /// Gets or sets the host address for the inference backend. Default is "127.0.0.1".
        /// </summary>
        public string Host { get; set; } = "127.0.0.1";
        
        /// <summary>
        /// Gets or sets the port number for the inference backend. Default is 7002.
        /// </summary>
        public int Port { get; set; } = 7002;
        
        /// <summary>
        /// Gets or sets the API key for authentication with the inference backend.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the model identifier to use for inference.
        /// </summary>
        public string Model { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the temperature parameter for response creativity. Default is 0.2f.
        /// </summary>
        public float Temperature { get; set; } = 0.2f;
        
        /// <summary>
        /// Gets or sets the maximum number of tokens to generate. -1 means no limit. Default is -1.
        /// </summary>
        public int MaxTokens { get; set; } = -1;
        
        /// <summary>
        /// Gets or sets the nucleus sampling parameter. Default is 0.9f.
        /// </summary>
        public float TopP { get; set; } = 0.9f;
        
        /// <summary>
        /// Gets or sets the frequency penalty. Default is 0f.
        /// </summary>
        public float FrequencyPenalty { get; set; } = 0f;
        
        /// <summary>
        /// Gets or sets the presence penalty. Default is 0f.
        /// </summary>
        public float PresencePenalty { get; set; } = 0f;
        
        /// <summary>
        /// Gets or sets the random seed for reproducible results. Default is 0.
        /// </summary>
        public int Seed { get; set; } = 0;
        
        /// <summary>
        /// Gets or sets the top-k sampling parameter. Default is 40.
        /// </summary>
        public int TopK { get; set; } = 40;
        
        /// <summary>
        /// Gets or sets the repeat penalty. Default is 1.1f.
        /// </summary>
        public float RepeatPenalty { get; set; } = 1.1f;
        
        /// <summary>
        /// Gets or sets the minimum probability threshold. Default is 0.05f.
        /// </summary>
        public float MinP { get; set; } = 0.05f;
        
        /// <summary>
        /// Gets or sets the typical probability parameter. Default is 1f.
        /// </summary>
        public float TypicalP { get; set; } = 1f;
        
        /// <summary>
        /// Gets or sets the number of tokens to consider for repetition penalty. Default is 64.
        /// </summary>
        public int RepeatLastN { get; set; } = 64;
        
        /// <summary>
        /// Gets or sets the mirostat algorithm setting. 0 disables it. Default is 0.
        /// </summary>
        public int Mirostat { get; set; } = 0;
        
        /// <summary>
        /// Gets or sets the mirostat tau parameter. Default is 5f.
        /// </summary>
        public float MirostatTau { get; set; } = 5f;
        
        /// <summary>
        /// Gets or sets the mirostat eta parameter. Default is 0.1f.
        /// </summary>
        public float MirostatEta { get; set; } = 0.1f;
        
        /// <summary>
        /// Gets or sets the number of probabilities to return. Default is 0.
        /// </summary>
        public int NProbs { get; set; } = 0;
        
        /// <summary>
        /// Gets or sets whether to ignore end-of-sequence tokens. Default is false.
        /// </summary>
        public bool IgnoreEos { get; set; } = false;
        
        /// <summary>
        /// Gets or sets whether to cache the prompt for efficiency. Default is true.
        /// </summary>
        public bool CachePrompt { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the grammar to constrain the generated output.
        /// </summary>
        public string? Grammar { get; set; } = null;
        
        /// <summary>
        /// Gets or sets the sequences that will cause the model to stop generating.
        /// </summary>
        public string[]? StopSequences { get; set; } = null;
        
        /// <summary>
        /// Validates the configuration values to ensure they are within acceptable ranges.
        /// </summary>
        /// <returns>True if all values are valid, false otherwise.</returns>
        public bool Validate()
        {
            // Validate port number
            if (Port <= 0 || Port > 65535)
            {
                Debug.LogError($"Invalid port number: {Port}. Port must be between 1 and 65535.");
                return false;
            }
            
            // Validate temperature (typically between 0 and 2)
            if (Temperature < 0.0f || Temperature > 2.0f)
            {
                Debug.LogWarning($"Unusual temperature value: {Temperature}. Recommended range is 0.0 to 2.0.");
            }
            
            // Validate MaxTokens (-1 means no limit, otherwise positive)
            if (MaxTokens < -1)
            {
                Debug.LogError($"Invalid MaxTokens value: {MaxTokens}. Value must be -1 (no limit) or positive.");
                return false;
            }
            
            // Validate TopP (between 0 and 1)
            if (TopP < 0.0f || TopP > 1.0f)
            {
                Debug.LogError($"Invalid TopP value: {TopP}. Value must be between 0.0 and 1.0.");
                return false;
            }
            
            // Validate other penalty values
            if (FrequencyPenalty < -2.0f || FrequencyPenalty > 2.0f)
            {
                Debug.LogWarning($"Unusual FrequencyPenalty value: {FrequencyPenalty}. Recommended range is -2.0 to 2.0.");
            }
            
            if (PresencePenalty < -2.0f || PresencePenalty > 2.0f)
            {
                Debug.LogWarning($"Unusual PresencePenalty value: {PresencePenalty}. Recommended range is -2.0 to 2.0.");
            }
            
            // Validate TopK (positive or -1 for disabled)
            if (TopK < -1 || (TopK > 0 && TopK > 1000))
            {
                Debug.LogWarning($"Unusual TopK value: {TopK}. Recommended range is 1 to 1000, or -1 to disable.");
            }
            
            // Validate RepeatPenalty (typically > 0)
            if (RepeatPenalty <= 0.0f)
            {
                Debug.LogError($"Invalid RepeatPenalty value: {RepeatPenalty}. Value must be greater than 0.0.");
                return false;
            }
            
            // Validate MinP (between 0 and 1)
            if (MinP < 0.0f || MinP > 1.0f)
            {
                Debug.LogError($"Invalid MinP value: {MinP}. Value must be between 0.0 and 1.0.");
                return false;
            }
            
            // Validate TypicalP (typically between 0 and 1)
            if (TypicalP < 0.0f || TypicalP > 1.0f)
            {
                Debug.LogError($"Invalid TypicalP value: {TypicalP}. Value must be between 0.0 and 1.0.");
                return false;
            }
            
            // Validate Mirostat settings
            if (Mirostat < 0 || Mirostat > 2)
            {
                Debug.LogError($"Invalid Mirostat value: {Mirostat}. Value must be 0, 1, or 2.");
                return false;
            }
            
            if (Mirostat > 0 && MirostatTau <= 0.0f)
            {
                Debug.LogError($"Invalid MirostatTau value: {MirostatTau}. When Mirostat is enabled, Tau must be positive.");
                return false;
            }
            
            if (Mirostat > 0 && MirostatEta <= 0.0f)
            {
                Debug.LogError($"Invalid MirostatEta value: {MirostatEta}. When Mirostat is enabled, Eta must be positive.");
                return false;
            }
            
            return true;
        }
    }

    /// <summary>
    /// Options for configuring dialogue inference requests.
    /// </summary>
    public sealed class DialogueInferenceRequestOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of tokens to override for this request. Default is -1 (no override).
        /// </summary>
        public int MaxTokensOverride { get; set; } = -1;
        
        /// <summary>
        /// Gets or sets whether to prefer JSON response format.
        /// </summary>
        public bool PreferJsonResponse { get; set; }
        
        /// <summary>
        /// Gets or sets the instruction for structured response format.
        /// </summary>
        public string? StructuredResponseInstruction { get; set; }
    }

    public interface IDialogueInferenceClient
    {
        string BackendName { get; }
        bool ManagesHistoryInternally { get; }
        void ApplyConfig(DialogueInferenceRuntimeConfig config);
        Task<bool> CheckConnectionAsync(CancellationToken ct = default);
        Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<DialogueInferenceMessage> history,
            string userPrompt,
            bool addToHistory = true,
            CancellationToken ct = default
        );
    }
}
