using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UndreamAI.LlamaLib
{
    public class ChatMessage
    {
        public string role { get; set; }
        public string content { get; set; }

        public ChatMessage(string role, string content)
        {
            this.role = role ?? string.Empty;
            this.content = content ?? string.Empty;
        }
    }

    public class LlamaLib : IDisposable
    {
        public delegate void CharArrayCallback(string charArray);

        public static string baseLibraryPath = string.Empty;
        public static List<string> libraryExclusion = new List<string>();
        public string architecture { get; } = "stub";

        public LlamaLib(bool gpu = false) {}

        public static void Debug(int debugLevel) {}

        public static string GetPlatform()
        {
            return "stub";
        }

        public static void LoggingCallback(CharArrayCallback callback) {}

        public static void LoggingStop() {}

        public void Dispose() {}
    }

    public abstract class LLM : IDisposable
    {
        protected readonly object _disposeLock = new object();
        private JObject completionParameters = new JObject();
        private string grammar = string.Empty;

        public bool disposed;
        public LlamaLib llamaLib;
        public IntPtr llm;

        protected LLM() {}

        protected LLM(LlamaLib llamaLibInstance)
        {
            llamaLib = llamaLibInstance;
        }

        public virtual string Completion(
            string prompt,
            LlamaLib.CharArrayCallback callback = null,
            int idSlot = -1
        )
        {
            return string.Empty;
        }

        public virtual Task<string> CompletionAsync(
            string prompt,
            LlamaLib.CharArrayCallback callback = null,
            int idSlot = -1
        )
        {
            return Task.FromResult(string.Empty);
        }

        public virtual string Detokenize(List<int> tokens)
        {
            return string.Empty;
        }

        public virtual List<float> Embeddings(string content)
        {
            return new List<float>();
        }

        public virtual JObject GetCompletionParameters()
        {
            return completionParameters;
        }

        public virtual string GetGrammar()
        {
            return grammar;
        }

        public virtual void SetCompletionParameters(JObject parameters = null)
        {
            completionParameters = parameters != null
                ? (JObject)parameters.DeepClone()
                : new JObject();
        }

        public virtual void SetGrammar(string value)
        {
            grammar = value ?? string.Empty;
        }

        public virtual List<int> Tokenize(string content)
        {
            return new List<int>();
        }

        public virtual void Dispose()
        {
            disposed = true;
        }
    }

    public abstract class LLMLocal : LLM
    {
        protected LLMLocal() {}

        protected LLMLocal(LlamaLib llamaLibInstance)
            : base(llamaLibInstance) {}

        public virtual void Cancel(int idSlot) {}

        public virtual string LoadSlot(int idSlot, string filepath)
        {
            return string.Empty;
        }

        public virtual string SaveSlot(int idSlot, string filepath)
        {
            return string.Empty;
        }
    }

    public abstract class LLMProvider : LLMLocal
    {
        protected LLMProvider() {}

        protected LLMProvider(LlamaLib llamaLibInstance)
            : base(llamaLibInstance) {}

        public virtual int EmbeddingSize()
        {
            return 0;
        }

        public virtual void EnableReasoning(bool enableReasoning) {}

        public virtual void JoinServer() {}

        public virtual void JoinService() {}

        public virtual List<LoraIdScalePath> LoraList()
        {
            return new List<LoraIdScalePath>();
        }

        public virtual bool LoraWeight(List<LoraIdScale> loras)
        {
            return true;
        }

        public virtual bool LoraWeight(params LoraIdScale[] loras)
        {
            return true;
        }

        public virtual void SetSSL(string sslCert, string sslKey) {}

        public virtual bool Start()
        {
            return true;
        }

        public virtual bool Started()
        {
            return true;
        }

        public virtual void StartServer(
            string host = "0.0.0.0",
            int port = -1,
            string apiKey = ""
        ) {}

        public virtual void Stop() {}

        public virtual void StopServer() {}
    }

    public class LLMService : LLMProvider
    {
        public string Command { get; set; } = string.Empty;

        public LLMService(LlamaLib llamaLibInstance, IntPtr llmInstance)
            : base(llamaLibInstance)
        {
            llm = llmInstance;
        }

        public LLMService(
            string modelPath,
            int numSlots = 1,
            int numThreads = -1,
            int numGpuLayers = 0,
            bool flashAttention = false,
            int contextSize = 4096,
            int batchSize = 2048,
            bool embeddingOnly = false,
            string[] loraPaths = null
        ) {}

        public static IntPtr CreateLLM(
            LlamaLib llamaLib,
            string modelPath,
            int numSlots,
            int numThreads,
            int numGpuLayers,
            bool flashAttention,
            int contextSize,
            int batchSize,
            bool embeddingOnly,
            string[] loraPaths
        )
        {
            return IntPtr.Zero;
        }

        public static LLMService FromCommand(string paramsString)
        {
            return new LLMService(new LlamaLib(), IntPtr.Zero);
        }
    }

    public class LLMClient : LLMLocal
    {
        public LLMClient(LLMProvider provider)
            : base(provider != null ? provider.llamaLib : null) {}

        public LLMClient(string url, int port, string apiKey = "", int numRetries = 5) {}

        public virtual bool IsServerAlive()
        {
            return true;
        }

        public virtual void SetSSL(string sslCert) {}
    }

    public class LLMAgent : LLMLocal
    {
        private readonly List<ChatMessage> history = new List<ChatMessage>();

        public JArray History { get; set; } = new JArray();
        public int SlotId { get; set; }
        public string SystemPrompt { get; set; }

        public LLMAgent(LLMLocal llm, string systemPrompt = "")
            : base(llm != null ? llm.llamaLib : null)
        {
            SystemPrompt = systemPrompt ?? string.Empty;
        }

        public virtual void AddAssistantMessage(string content)
        {
            history.Add(new ChatMessage("assistant", content));
        }

        public virtual void AddUserMessage(string content)
        {
            history.Add(new ChatMessage("user", content));
        }

        public virtual void Cancel() {}

        public virtual string Chat(
            string userPrompt,
            bool addToHistory = true,
            LlamaLib.CharArrayCallback callback = null,
            bool returnResponseJson = false,
            bool debugPrompt = false
        )
        {
            if (addToHistory)
            {
                AddUserMessage(userPrompt);
            }
            return string.Empty;
        }

        public virtual Task<string> ChatAsync(
            string userPrompt,
            bool addToHistory = true,
            LlamaLib.CharArrayCallback callback = null,
            bool returnResponseJson = false,
            bool debugPrompt = false
        )
        {
            return Task.FromResult(Chat(userPrompt, addToHistory, callback, returnResponseJson, debugPrompt));
        }

        public virtual void ClearHistory()
        {
            history.Clear();
        }

        public virtual List<ChatMessage> GetHistory()
        {
            return new List<ChatMessage>(history);
        }

        public virtual int GetHistorySize()
        {
            return history.Count;
        }

        public virtual void LoadHistory(string filepath) {}

        public virtual void RemoveLastMessage()
        {
            if (history.Count > 0)
            {
                history.RemoveAt(history.Count - 1);
            }
        }

        public virtual void SaveHistory(string filepath) {}

        public virtual void SetHistory(List<ChatMessage> messages)
        {
            history.Clear();
            if (messages != null)
            {
                history.AddRange(messages);
            }
        }
    }

    public struct LoraIdScale
    {
        public int Id { get; set; }
        public float Scale { get; set; }

        public LoraIdScale(int id, float scale)
        {
            Id = id;
            Scale = scale;
        }
    }

    public struct LoraIdScalePath
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public float Scale { get; set; }

        public LoraIdScalePath(int id, float scale, string path)
        {
            Id = id;
            Scale = scale;
            Path = path ?? string.Empty;
        }
    }
}
