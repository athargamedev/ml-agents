using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Network_Game.Dialogue
{
    public readonly struct DialogueInferenceMessage
    {
        public readonly string Role;
        public readonly string Content;

        public DialogueInferenceMessage(string role, string content)
        {
            Role = string.IsNullOrWhiteSpace(role) ? "user" : role.Trim();
            Content = content ?? string.Empty;
        }
    }

    public sealed class DialogueInferenceRuntimeConfig
    {
        public string Host = "127.0.0.1";
        public int Port = 7002;
        public string ApiKey = string.Empty;
        public string Model = string.Empty;
        public float Temperature = 0.2f;
        public int MaxTokens = -1;
        public float TopP = 0.9f;
        public float FrequencyPenalty = 0f;
        public float PresencePenalty = 0f;
        public int Seed = 0;
        public int TopK = 40;
        public float RepeatPenalty = 1.1f;
        public float MinP = 0.05f;
        public float TypicalP = 1f;
        public int RepeatLastN = 64;
        public int Mirostat = 0;
        public float MirostatTau = 5f;
        public float MirostatEta = 0.1f;
        public int NProbs = 0;
        public bool IgnoreEos;
        public bool CachePrompt = true;
        public string Grammar = null;
        public string[] StopSequences = null;
    }

    public sealed class DialogueInferenceRequestOptions
    {
        public int MaxTokensOverride = -1;
        public bool PreferJsonResponse;
        public string StructuredResponseInstruction;
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
