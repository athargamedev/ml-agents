# LM Studio Bridge — Python Runner

## Configuration
- LM Studio: `127.0.0.1:7002`, API key format: `sk-lm-...`
- Python: `C:\Users\andre_wjgj23f\miniconda3\envs\mlagents\python.exe`
- `openai` and `anthropic` packages must both be installed

## Model Routing Strategy (confirmed 2026-03-01)

| Constant    | Model                            | Use for                          | Stop constant |
|-------------|----------------------------------|----------------------------------|---------------|
| `DOCS_MODEL`| `qwen3-8b`                       | Package docs, factual knowledge  | `QWEN_STOP`   |
| `CODE_MODEL`| `qwen2.5-coder-7b@q4_k_m`        | Code review / analysis           | `QWEN_STOP`   |
| `TEXT_MODEL`| `llama-3.2-3b-instruct@q8_0`     | `ask_schema()`, fast tasks       | `LLAMA_STOP`  |
| `FAST_MODEL`| `llama-3.2-3b-instruct@q4_k_s`   | Quick single-fact lookups        | `LLAMA_STOP`  |

- `ask_schema()` (json_schema grammar mode) → **always TEXT_MODEL** — qwen2.5-coder-7b fails (garbled output confirmed 2026-02-28)
- LM Studio routes by `model=` param correctly **only if the model is loaded**. Use `ensure_models_loaded()` before any batch.

## Key Stop Token Constants
```python
LLAMA_STOP = ["<|eot_id|>", "<|end_of_text|>", "<|start_header_id|>"]
QWEN_STOP  = ["<|im_end|>", "endoftext-token"]   # works for Qwen2.5 AND Qwen3
# Note: actual QWEN_STOP second element is the endoftext special token — see lm_client.py
```

## Qwen3 `/no_think` Trick
Prepend `/no_think\n` to the **user message** (not system) to suppress Qwen3's chain-of-thought block. Without it, the model outputs visible reasoning that wastes tokens and adds latency. Essential for docs generation and any task where direct answers are wanted.

## Repetition Collapse ("oneoneoneone" pattern)
Cause: wrong chat template format for the model + missing stop tokens + low-bit quantization. The model enters a probability feedback loop on one token.
Fix: match stop tokens to the model family (LLAMA_STOP vs QWEN_STOP) and use the correct chat template.

## lm_client.py — Core API
- `ask(system, user, model, profile, stop_sequences)` — Anthropic `/v1/messages`, KV-cached system prompt, ~5s warm
- `ask_schema(system, user, schema, model, profile)` — OpenAI `/v1/chat/completions` + `json_schema` grammar, ~50s
- `ask_stateful(system, history, user, profile)` — multi-turn, appends to `history` list in-place
- `ask_openai_prose(system, user, model, stop_sequences)` — OpenAI endpoint, plain text (no json_schema)
- `ensure_models_loaded([MODEL_LIST], context_length=4096)` — loads unloaded models via `lms load` before a batch
- `load_model` / `unload_model` — wrap `lms load` / `lms unload` CLI
- `list_loaded_models()` — wraps `lms ps`

## Sampling Profiles (pass `profile=` to any ask* method)
- `"analysis"` — temp=0.1, top_k=20, repeat_penalty=1.2, min_p=0.05 (code review, docs)
- `"schema"`   — temp=0.05, top_k=20, repeat_penalty=1.15 (json_schema calls)
- `"creative"` — temp=0.75, top_k=60 (NPC profile generation)
- `"fast"`     — temp=0.15 only (quick lookups)

## docs_scanner.py — scan-docs pipeline
- Command: `PYTHONIOENCODING=utf-8 python dev_tools/run_dev_tools.py scan-docs`
- Single package: `python dev_tools/run_dev_tools.py scan-docs --package com.unity.netcode.gameobjects`
- Uses `DOCS_MODEL` (qwen3-8b) for core packages, `TEXT_MODEL` for tools batch
- Parallel: `ThreadPoolExecutor(max_workers=2)` — workers submit immediately when LM Studio frees
- `_load_referenced_packages()` filters to only assembly-referenced packages (avoids scanning unused ones)
- System prompt: `dev_tools/prompts/package_docs.txt` — anti-hallucination instruction included
- Output: `dev_tools/reports/package_docs_YYYY-MM-DD.md` + `latest_package_docs.md`
- `PYTHONIOENCODING=utf-8` required — `lms load` outputs unicode that crashes Windows cp1252 console

## Critical Gotchas
1. **Port 5004 collisions**: Orphaned Python processes hold port 5004. Check with `netstat -ano | grep ":5004 "`, kill with `Stop-Process -Id <PID> -Force`
2. **conda not in bash PATH**: Always use full Python path, not `conda run`
3. **mlagents not editable**: `sys.path.insert(0, "ml-agents")` + `sys.path.insert(0, "ml-agents-envs")` in runner
4. **Behavior Type = Default freezes Unity**: Use Heuristic Only when not training
5. **LM Studio auth**: Newer LM Studio requires real API key, not placeholder "lm-studio"
6. **Windows console encoding**: Always prefix with `PYTHONIOENCODING=utf-8` when `lms load` is called — it emits unicode symbols that crash cp1252
