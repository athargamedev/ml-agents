# NetworkDialogueService — Current Knowledge

## Core role
- `NetworkDialogueService` is the single server-authoritative dialogue router in `Network_Game.Dialogue`.
- It owns request queueing, retries, history, effect dispatch handoff, telemetry, and response RPC delivery.
- It is now a **remote-first and remote-only** gameplay path.

## Current backend model
- Default runtime backend: `OpenAIChatClient`
- Optional override backend: `SideChannelDialogueClient` via `SetMLAgentsSideChannelClient(...)`
- There is no remaining local `LLMUnity` fallback path in the live runtime.
- Legacy bridge classes (`LegacyLocalLlmRuntime`, `LlmAgentInferenceClient`) were removed.

## Config source of truth
- `DialogueBackendConfig` on the same GameObject is the project-owned source of:
  - host / port
  - model name
  - API key override
  - sampling params
  - stop sequences
  - grammar
  - system prompt
- `NetworkDialogueService` no longer depends on `LLMAgent` serialized fields.

## Runtime call chain (gameplay)
```csharp
DialogueClientUI.SendPrompt()
  -> RequestDialogue()
  -> TryEnqueueRequest()
  -> ProcessQueue()
  -> ExecuteRequestWorkerAsync()
  -> ResolveInferenceClient()         // override or OpenAIChatClient
  -> inferenceClient.ChatAsync(...)
  -> NpcDialogueActor.ShowSpeechText()
```

## Important current behavior
- `UsesRemoteInference` is effectively always `true` for gameplay.
- `ActiveInferenceBackendName` is normally `openai-compatible-remote`.
- If ML-Agents injects an override, `ActiveInferenceBackendName` becomes the override backend name.
- `RemoteInferenceEndpoint` is derived from `DialogueBackendConfig` (or built-in defaults if missing).

## Effect probe improvements
- Effect-validation probes use a dedicated low-latency request path.
- `EffectProbeMaxResponseTokens` is capped at `96`.
- Effect probes send structured output instructions and use the JSON-schema path in `OpenAIChatClient`.
- This was added to reduce Qwen overthinking, token waste, and malformed effect-test output.

## Warmup / readiness
- Warmup is now remote probe based (`CheckConnectionAsync`) and bounded.
- The service no longer warms or validates any in-process local model runtime.
- Debug log analysis also uses the same remote backend path.

## Events used elsewhere
```csharp
public static event Action<DialogueResponse> OnDialogueResponse;
public static event Action<DialogueResponseTelemetry> OnDialogueResponseTelemetry;
```
- ML-Agents reward shaping still uses these events.

## Operational rule
- Always null-check `NetworkDialogueService.Instance` before use from external callers.
- Do not reintroduce `LLMUnity` or per-player local inference into this service; the intended architecture is dedicated-server LM Studio inference.
