using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Network_Game.Diagnostics;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Network_Game.Dialogue
{
    /// <summary>
    /// HTTP client for OpenAI-compatible chat completion APIs (LM Studio, Ollama, vLLM, etc.).
    /// Used as the primary runtime transport for the project's remote dialogue backend.
    ///
    /// Parameter mapping from DialogueBackendConfig fields to OpenAI-compat JSON:
    ///   temperature        → temperature
    ///   numPredict         → max_tokens       (-1 → omit, let server decide)
    ///   topP               → top_p
    ///   frequencyPenalty   → frequency_penalty
    ///   presencePenalty    → presence_penalty
    ///   seed               → seed             (0 → omit)
    ///   topK               → top_k            (LM Studio / llama.cpp extension)
    ///   repeatPenalty      → repeat_penalty   (LM Studio / llama.cpp extension)
    ///   minP               → min_p            (LM Studio / llama.cpp extension)
    ///   StopSequences      → stop             (chat-template guard, e.g. ["</s>","[INST]"])
    /// </summary>
    public class OpenAIChatClient : IDialogueInferenceClient, IDisposable
    {
        private static readonly HttpClient s_Http = new HttpClient();
        private const string StructuredResponseFieldName = "responseText";
        private bool m_ForceAutoModelRouting;
        private string m_LastActiveModelId = string.Empty;

        static OpenAIChatClient()
        {
            // Enforce timeout via external CancellationToken, not HttpClient's own timer.
            s_Http.Timeout = Timeout.InfiniteTimeSpan;
        }

        // ── Connection ──────────────────────────────────────────────────────────
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 7002;
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// Model identifier sent in the request body. Leave empty for LM Studio
        /// single-model setups ("auto" is used as fallback to satisfy the spec).
        /// Set to the exact model name shown in LM Studio when multiple models are loaded.
        /// </summary>
        public string Model { get; set; } = "";

        // ── Standard OpenAI sampling parameters ────────────────────────────────
        public float Temperature { get; set; } = 0.2f;

        /// <summary>Max tokens to generate. -1 means omit the field (server default).</summary>
        public int MaxTokens { get; set; } = -1;

        public float TopP { get; set; } = 0.9f;
        public float FrequencyPenalty { get; set; } = 0f;
        public float PresencePenalty { get; set; } = 0f;

        /// <summary>Seed for reproducible generation. 0 means omit (random).</summary>
        public int Seed { get; set; } = 0;

        // ── LM Studio / llama.cpp extended parameters ──────────────────────────
        /// <summary>
        /// Top-k sampling (LM Studio extension, maps to llama.cpp top_k).
        /// 0 = disabled. Default matches LLMClient.topK = 40.
        /// </summary>
        public int TopK { get; set; } = 40;

        /// <summary>
        /// Repeat penalty (LM Studio extension, maps to llama.cpp repeat_penalty).
        /// 1.0 = no penalty. Default matches LLMClient.repeatPenalty = 1.1.
        /// </summary>
        public float RepeatPenalty { get; set; } = 1.1f;

        /// <summary>
        /// Min-p sampling threshold (LM Studio extension, maps to llama.cpp min_p).
        /// 0 = disabled. Default matches LLMClient.minP = 0.05.
        /// </summary>
        public float MinP { get; set; } = 0.05f;

        /// <summary>Locally typical sampling strength (1.0 = disabled).</summary>
        public float TypicalP { get; set; } = 1f;

        /// <summary>Number of recent tokens used for repetition penalty.</summary>
        public int RepeatLastN { get; set; } = 64;

        /// <summary>Mirostat sampling mode (0 disabled, 1/2 enabled).</summary>
        public int Mirostat { get; set; } = 0;

        /// <summary>Mirostat target entropy.</summary>
        public float MirostatTau { get; set; } = 5f;

        /// <summary>Mirostat learning rate.</summary>
        public float MirostatEta { get; set; } = 0.1f;

        /// <summary>Return top-N token probabilities in response (0 = disabled).</summary>
        public int NProbs { get; set; } = 0;

        /// <summary>Ignore EOS token during generation.</summary>
        public bool IgnoreEos { get; set; } = false;

        /// <summary>Request prompt caching on server side when available.</summary>
        public bool CachePrompt { get; set; } = true;

        /// <summary>Optional grammar constraints when backend supports it.</summary>
        public string Grammar { get; set; } = null;

        /// <summary>
        /// Stop sequences injected into every request.
        /// Use to guard against chat-template bleed (e.g. ["</s>", "[INST]", "User:", "Assistant:"]).
        /// Null or empty = omit the field entirely.
        /// </summary>
        public string[] StopSequences { get; set; } = null;

        private string BaseUrl => $"http://{Host}:{Port}";
        public string BackendName => "openai_compatible_remote";
        public bool ManagesHistoryInternally => false;

        public void ApplyConfig(DialogueInferenceRuntimeConfig config)
        {
            if (config == null)
            {
                return;
            }

            Host = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host.Trim();
            Port = config.Port;
            ApiKey = config.ApiKey ?? string.Empty;
            Model = config.Model ?? string.Empty;
            Temperature = config.Temperature;
            MaxTokens = config.MaxTokens;
            TopP = config.TopP;
            FrequencyPenalty = config.FrequencyPenalty;
            PresencePenalty = config.PresencePenalty;
            Seed = config.Seed;
            TopK = config.TopK;
            RepeatPenalty = config.RepeatPenalty;
            MinP = config.MinP;
            TypicalP = config.TypicalP;
            RepeatLastN = config.RepeatLastN;
            Mirostat = config.Mirostat;
            MirostatTau = config.MirostatTau;
            MirostatEta = config.MirostatEta;
            NProbs = config.NProbs;
            IgnoreEos = config.IgnoreEos;
            CachePrompt = config.CachePrompt;
            Grammar = config.Grammar;
            StopSequences = config.StopSequences;
        }

        // ── Chat completion ─────────────────────────────────────────────────────

        /// <summary>
        /// Sends a chat completion request to the OpenAI-compatible endpoint.
        /// All sampling parameters set on this instance are forwarded, including
        /// LM Studio extensions (top_k, repeat_penalty, min_p).
        /// </summary>
        /// <param name="systemPrompt">System prompt for the assistant persona.</param>
        /// <param name="history">Prior conversation messages (role + content).</param>
        /// <param name="userPrompt">The new user message.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The assistant's reply text, or empty string on failure.</returns>
        public async Task<string> ChatAsync(
            string systemPrompt,
            IReadOnlyList<DialogueInferenceMessage> history,
            string userPrompt,
            bool addToHistory = true,
            CancellationToken ct = default
        )
        {
            return await ChatInternalAsync(
                systemPrompt,
                history,
                userPrompt,
                null,
                addToHistory,
                ct
            ).ConfigureAwait(false);
        }

        public async Task<string> ChatWithOptionsAsync(
            string systemPrompt,
            IReadOnlyList<DialogueInferenceMessage> history,
            string userPrompt,
            DialogueInferenceRequestOptions requestOptions,
            bool addToHistory = true,
            CancellationToken ct = default
        )
        {
            return await ChatInternalAsync(
                systemPrompt,
                history,
                userPrompt,
                requestOptions,
                addToHistory,
                ct
            ).ConfigureAwait(false);
        }

        private async Task<string> ChatInternalAsync(
            string systemPrompt,
            IReadOnlyList<DialogueInferenceMessage> history,
            string userPrompt,
            DialogueInferenceRequestOptions requestOptions,
            bool addToHistory,
            CancellationToken ct
        )
        {
            var stopwatch = Stopwatch.StartNew();
            bool preferJsonResponse = requestOptions != null && requestOptions.PreferJsonResponse;
            int historyCount = history != null ? history.Count : 0;
            int messageCapacity = historyCount + 1;
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messageCapacity++;
            }

            if (preferJsonResponse)
            {
                messageCapacity++;
            }

            var messages = new List<MessageDto>(messageCapacity);
            int effectiveMaxTokens =
                requestOptions != null && requestOptions.MaxTokensOverride > 0
                ? requestOptions.MaxTokensOverride
                : MaxTokens;

            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(new MessageDto { role = "system", content = systemPrompt });

            if (preferJsonResponse)
            {
                messages.Add(
                    new MessageDto
                    {
                        role = "system",
                        content = BuildStructuredResponseInstruction(requestOptions),
                    }
                );
            }

            if (history != null)
            {
                foreach (var msg in history)
                    messages.Add(new MessageDto { role = msg.Role, content = msg.Content });
            }

            messages.Add(new MessageDto { role = "user", content = userPrompt });

            JObject requestBody = BuildRequestBody(messages, requestOptions);
            string url = $"{BaseUrl}/v1/chat/completions";
            string json = requestBody.ToString(Formatting.None);

            LogInfo(
                $"Sending OpenAI request | url={url} | model={requestBody["model"] ?? "auto"}"
                + $" | temp={Temperature} | topK={TopK} | topP={TopP}"
                + $" | repeatPenalty={RepeatPenalty} | minP={MinP}"
                + $" | typicalP={TypicalP} | repeatLastN={RepeatLastN}"
                + $" | mirostat={Mirostat} | maxTokens={effectiveMaxTokens} | seed={Seed}"
                + $" | structured={(preferJsonResponse ? "json" : "off")}"
            );

            HttpResponseMessage response = null;
            string responseBody = string.Empty;
            try
            {
                response = await SendChatRequestAsync(url, json, ct).ConfigureAwait(false);
                responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                InferenceWatchReporter.ReportInference(
                    userPrompt,
                    $"[http_error] {ex.Message}",
                    (float)stopwatch.Elapsed.TotalMilliseconds
                );
                LogWarn($"OpenAI HTTP request failed | error={ex.Message}");
                return string.Empty;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                InferenceWatchReporter.ReportInference(
                    userPrompt,
                    "[transport_timeout]",
                    (float)stopwatch.Elapsed.TotalMilliseconds
                );
                LogWarn(
                    "OpenAI request timed out (internal HttpClient timeout, not caller cancellation)."
                );
                return string.Empty;
            }

            using (response)
            {
                if (
                    !response.IsSuccessStatusCode
                    && ShouldRetryWithAutoModel(response.StatusCode, responseBody, requestBody)
                )
                {
                    requestBody["model"] = "auto";
                    m_ForceAutoModelRouting = true;
                    string retryJson = requestBody.ToString(Formatting.None);
                    LogWarn(
                        $"Configured model routing failed; retrying with auto model | status={(int)response.StatusCode}"
                    );

                    try
                    {
                        using HttpResponseMessage retryResponse = await SendChatRequestAsync(
                            url,
                            retryJson,
                            ct
                        )
                                    .ConfigureAwait(false);
                        responseBody = await retryResponse
                            .Content.ReadAsStringAsync()
                                .ConfigureAwait(false);
                        if (!retryResponse.IsSuccessStatusCode)
                        {
                            LogError(
                                $"OpenAI HTTP status error | status={(int)retryResponse.StatusCode} | body={Truncate(responseBody, 300)}"
                            );
                            return string.Empty;
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        InferenceWatchReporter.ReportInference(
                            userPrompt,
                            $"[retry_http_error] {ex.Message}",
                            (float)stopwatch.Elapsed.TotalMilliseconds
                        );
                        LogWarn($"OpenAI retry request failed | error={ex.Message}");
                        return string.Empty;
                    }
                    catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                    {
                        InferenceWatchReporter.ReportInference(
                            userPrompt,
                            "[retry_transport_timeout]",
                            (float)stopwatch.Elapsed.TotalMilliseconds
                        );
                        LogWarn("OpenAI retry request timed out (internal HttpClient timeout).");
                        return string.Empty;
                    }
                }
                else if (!response.IsSuccessStatusCode)
                {
                    InferenceWatchReporter.ReportInference(
                        userPrompt,
                        $"[http_status_{(int)response.StatusCode}] {Truncate(responseBody, 120)}",
                        (float)stopwatch.Elapsed.TotalMilliseconds
                    );
                    LogError(
                        $"OpenAI HTTP status error | status={(int)response.StatusCode} | body={Truncate(responseBody, 300)}"
                    );
                    return string.Empty;
                }
            }

            try
            {
                var obj = JObject.Parse(responseBody);
                string content = obj ? ["choices"] ? [0] ? ["message"] ? ["content"]?.ToString();

                if (preferJsonResponse)
                {
                    content = TryExtractStructuredResponseText(content);
                }

                if (
                    string.IsNullOrEmpty(content)
                    || content.Contains("User:")
                    || content.Contains("Assistant:")
                )
                {
                    LogInfo(
                        $"OpenAI full response trace | id={obj?["id"]} | fullBody={Truncate(responseBody, 1000)}"
                    );
                }

                if (string.IsNullOrEmpty(content))
                    LogWarn($"OpenAI empty content | body={Truncate(responseBody, 300)}");

                InferenceWatchReporter.ReportInference(
                    userPrompt,
                    content ?? string.Empty,
                    (float)stopwatch.Elapsed.TotalMilliseconds
                );
                return content ?? string.Empty;
            }
            catch (JsonException ex)
            {
                InferenceWatchReporter.ReportInference(
                    userPrompt,
                    $"[json_error] {ex.Message}",
                    (float)stopwatch.Elapsed.TotalMilliseconds
                );
                LogError(
                    $"OpenAI JSON parse error | error={ex.Message} | body={Truncate(responseBody, 300)}"
                );
                return string.Empty;
            }
        }

        // ── Connection probe ────────────────────────────────────────────────────

        /// <summary>
        /// Checks connectivity by hitting GET /v1/models. Used for warmup.
        /// Optionally logs the first loaded model name for diagnostics.
        /// </summary>
        public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
        {
            string url = $"{BaseUrl}/v1/models";
            CancellationTokenSource timeoutCts = null;
            CancellationToken effectiveToken = ct;
            if (!effectiveToken.CanBeCanceled)
            {
                timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                effectiveToken = timeoutCts.Token;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(ApiKey))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

                using HttpResponseMessage response = await s_Http
                        .SendAsync(request, effectiveToken)
                            .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    LogWarn(
                        $"OpenAI connection check non-success | url={url} | status={(int)response.StatusCode} | body={Truncate(body, 300)}"
                    );
                    return false;
                }

                // Log the active model(s) so the Inspector-set model can be validated at warmup.
                try
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var obj = JObject.Parse(body);
                    var data = obj["data"] as JArray;
                    if (data != null && data.Count > 0)
                    {
                        string firstId = data[0] ? ["id"]?.ToString() ?? "(unknown)";
                        m_LastActiveModelId = firstId;
                        string configured = string.IsNullOrWhiteSpace(Model) ? "(auto)" : Model;
                        bool configuredAvailable = IsConfiguredModelAvailable(data);
                        if (!string.IsNullOrWhiteSpace(Model) && !configuredAvailable)
                        {
                            m_ForceAutoModelRouting = true;
                            LogWarn(
                                $"Configured model not found in LM Studio list; forcing auto model routing | configured={Model} | active={firstId}"
                            );
                        }
                        else
                        {
                            m_ForceAutoModelRouting = string.IsNullOrWhiteSpace(Model);
                        }
                        LogInfo(
                            $"LM Studio connected | active_model={firstId} | configured_model={configured} | total_loaded={data.Count}"
                        );
                    }
                }
                catch
                { /* diagnostics only — don't fail warmup */
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarn($"OpenAI connection check failed | url={url} | error={ex.Message}");
                return false;
            }
            finally
            {
                timeoutCts?.Dispose();
            }
        }

        // ── IDisposable ─────────────────────────────────────────────────────────

        public void Dispose()
        {
            // s_Http is static/shared — do not dispose here
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the JSON request body with all sampling parameters.
        /// LM Studio-specific extensions (top_k, repeat_penalty, min_p) are always
        /// included — standard OpenAI will ignore unknown fields gracefully.
        /// </summary>
        private JObject BuildRequestBody(
            List<MessageDto> messages,
            DialogueInferenceRequestOptions requestOptions
        )
        {
            bool preferJsonResponse = requestOptions != null && requestOptions.PreferJsonResponse;
            int effectiveMaxTokens =
                requestOptions != null && requestOptions.MaxTokensOverride > 0
                ? requestOptions.MaxTokensOverride
                : MaxTokens;
            var body = new JObject
            {
                ["messages"] = JArray.FromObject(messages),
                ["temperature"] = Temperature,
                ["top_p"] = TopP,
                ["frequency_penalty"] = FrequencyPenalty,
                ["presence_penalty"] = PresencePenalty,
                ["stream"] = false,

                // LM Studio / llama.cpp extensions
                ["top_k"] = TopK,
                ["repeat_penalty"] = RepeatPenalty,
                ["min_p"] = MinP,
                ["typical_p"] = TypicalP,
                ["repeat_last_n"] = RepeatLastN,
                ["mirostat"] = Mirostat,
                ["mirostat_tau"] = MirostatTau,
                ["mirostat_eta"] = MirostatEta,
                ["ignore_eos"] = IgnoreEos,
                ["n_probs"] = NProbs,
                ["cache_prompt"] = CachePrompt,
            };

            // model — "auto" satisfies spec when empty, real name preferred
            body["model"] = ResolveModelForRequest();

            // max_tokens: -1 means "omit" (let LM Studio use its own default)
            if (effectiveMaxTokens > 0)
                body["max_tokens"] = effectiveMaxTokens;
            // else omit entirely — avoids capping 3B LoRA responses unexpectedly

            // seed: 0 means "random" in llama.cpp, omit to avoid accidentally
            // locking every run to the same sequence when user leaves it at 0
            if (Seed > 0)
                body["seed"] = Seed;

            // stop sequences — only include when explicitly set
            if (StopSequences != null && StopSequences.Length > 0)
                body["stop"] = JArray.FromObject(StopSequences);

            if (!preferJsonResponse && !string.IsNullOrWhiteSpace(Grammar))
                body["grammar"] = Grammar;

            if (preferJsonResponse)
            {
                body["response_format"] = BuildStructuredResponseFormat();
            }

            return body;
        }

        private async Task<HttpResponseMessage> SendChatRequestAsync(
            string url,
            string json,
            CancellationToken ct
        )
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            }

            try
            {
                return await s_Http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                LogError($"OpenAI HTTP error | url={url} | error={ex.Message}");
                throw;
            }
            catch (TaskCanceledException ex) when (ct.IsCancellationRequested)
            {
                LogWarn("OpenAI request cancelled or timed out.");
                throw new OperationCanceledException("OpenAI request cancelled.", ex, ct);
            }
            catch (TaskCanceledException)
            {
                LogWarn("OpenAI request canceled by transport.");
                throw;
            }
        }

        private string ResolveModelForRequest()
        {
            if (m_ForceAutoModelRouting)
            {
                return "auto";
            }

            string configuredModel = string.IsNullOrWhiteSpace(Model) ? string.Empty : Model.Trim();
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                return configuredModel;
            }

            return string.IsNullOrWhiteSpace(m_LastActiveModelId) ? "auto" : m_LastActiveModelId;
        }

        private bool ShouldRetryWithAutoModel(
            System.Net.HttpStatusCode statusCode,
            string responseBody,
            JObject requestBody
        )
        {
            string currentModel = requestBody ? ["model"]?.ToString();
            if (string.Equals(currentModel, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int numeric = (int)statusCode;
            if (numeric != 400 && numeric != 404 && numeric != 422)
            {
                return false;
            }

            string body = responseBody ?? string.Empty;
            return body.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0
                && (
                    body.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || body.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0
                    || body.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                );
        }

        private bool IsConfiguredModelAvailable(JArray models)
        {
            if (string.IsNullOrWhiteSpace(Model) || models == null || models.Count == 0)
            {
                return false;
            }

            string configured = Model.Trim();
            for (int i = 0; i < models.Count; i++)
            {
                string loaded = models[i] ? ["id"]?.ToString();
                if (string.Equals(loaded, configured, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s;
            return s.Substring(0, max) + "...";
        }

        private static JObject BuildStructuredResponseFormat()
        {
            return new JObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JObject
                {
                    ["name"] = "dialogue_response",
                    ["strict"] = true,
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            [StructuredResponseFieldName] = new JObject
                            {
                                ["type"] = "string",
                            },
                        },
                        ["required"] = new JArray(StructuredResponseFieldName),
                        ["additionalProperties"] = false,
                    },
                },
            };
        }

        private static string BuildStructuredResponseInstruction(
            DialogueInferenceRequestOptions requestOptions
        )
        {
            if (
                requestOptions != null
                && !string.IsNullOrWhiteSpace(requestOptions.StructuredResponseInstruction)
            )
            {
                return requestOptions.StructuredResponseInstruction.Trim();
            }

            return
                "For this request, respond with a valid JSON object only. "
                + "Use exactly one key named \"responseText\". "
                + "The responseText value must contain only the final user-facing response, following the exact tag instructions already present in the prompt. "
                + "No analysis. No extra keys.";
        }

        private static string TryExtractStructuredResponseText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content ?? string.Empty;
            }

            try
            {
                var obj = JObject.Parse(content);
                string responseText = obj[StructuredResponseFieldName]?.ToString();
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    return responseText;
                }
            }
            catch (JsonException)
            {
                // Fall back to the raw content to keep the request resilient if the
                // backend ignores response_format or returns plain text.
            }

            return content;
        }

        private static void LogInfo(string msg) => NGLog.Info("OpenAI", msg);

        private static void LogWarn(string msg) => NGLog.Warn("OpenAI", msg);

        private static void LogError(string msg) => NGLog.Error("OpenAI", msg);

        [Serializable]
        private class MessageDto
        {
            public string role;
            public string content;
        }
    }
}
