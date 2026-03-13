# LM Studio Bridge — Current Runtime Notes

## Production runtime
- Gameplay dialogue uses LM Studio through the OpenAI-compatible HTTP path in `OpenAIChatClient`.
- Primary local endpoint remains `127.0.0.1:7002`.
- Primary gameplay model family is Qwen, with `qwen3-8b` as the current default model target.
- The removed `LLMUnity` local llama.cpp path is no longer part of the runtime architecture.

## Structured output compatibility
- LM Studio in this setup expects `response_format.type` to be `json_schema` or `text`.
- The project was patched to use `json_schema` for structured effect-probe requests.
- Do not use `json_object` in this environment; that caused HTTP 400 errors.

## Effect-probe tuning
- The effect-validation path is intentionally constrained:
  - low token cap (`96`)
  - schema-constrained output
  - prompt tightened to final answer only
- This reduced wasted Qwen reasoning and made effect automation more deterministic.

## Chat client expectations
- `OpenAIChatClient` is the main gameplay transport.
- It applies `DialogueBackendConfig` values directly to OpenAI-compatible request JSON.
- It supports the structured `json_schema` path for constrained responses.
- It is the correct place to keep LM Studio / Qwen-specific transport quirks.

## Training / launcher integration
- `train_npc_dialogue.bat` now delegates to `run_training.py` instead of calling `mlagents.learn` directly.
- `run_training.py` is now the safe launcher path:
  - cleans up stale training port usage
  - owns the canonical training startup flow
  - skips the Unity HTTP MCP readiness poll by default
- The old `http://localhost:8009/mcp` readiness check is opt-in via `--unity-check`.

## Important collision note
- `run_llm_bridge.py` can still contend for the Unity ML-Agents editor connection if it is already holding a `UnityEnvironment` session.
- If training reports worker/socket-in-use errors, check for an already-running bridge or stale trainer process first.

## Practical rule
- Keep gameplay dialogue on the remote LM Studio path.
- Treat the Python bridge and side-channel tools as training/test infrastructure, not the default gameplay transport.
