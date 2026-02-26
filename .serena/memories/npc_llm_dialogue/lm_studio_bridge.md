# LM Studio Bridge — Python Runner

## Configuration
- LM Studio: `127.0.0.1:7002`, model `llama-3.2-3b-instruct`
- API key stored in `run_llm_bridge.py` as `LMS_API_KEY` (format: `sk-lm-...`)
- Python: `C:\Users\andre_wjgj23f\miniconda3\envs\mlagents\python.exe`
- `openai` package must be installed: `python.exe -m pip install openai`

## run_llm_bridge.py (repo root)
Standalone runner with retry logic, structured logging, file logging to `.codex/tmp/`.
- `USE_MOCK = False` — set True to test without LM Studio (echoes input)
- `UNITY_TIMEOUT_SECONDS = 300` — generous timeout for heavy Editor scenes
- `RESET_MAX_RETRIES = 3` — reconnects on UnityCommunicatorStoppedException
- Must start BEFORE pressing Play in Unity

## llm_bridge_server.py handler factories
`make_lmstudio_handler(model, host, port, timeout, api_key)`:
- Does NOT use `response_format={"type": "json_object"}` — LM Studio logs
  "Unexpected endpoint" for this parameter. System prompt requests JSON instead.
- `_build_system_prompt`: passes Unity's full prompt (>120 chars) straight through;
  short personality strings use generic template fallback.
- `_build_response(request, content, raw)`: falls back to raw text if JSON parse
  fails — NPC says something intelligible rather than "..."

## Critical gotchas
1. **Port 5004 collisions**: Orphaned Python processes hold port 5004. Check with
   `netstat -ano | grep ":5004 "`, kill with `Stop-Process -Id <PID> -Force`
2. **conda not in bash PATH**: Always use full Python path, not `conda run`
3. **mlagents not editable**: `sys.path.insert(0, "ml-agents")` + `sys.path.insert(0, "ml-agents-envs")` in runner
4. **Behavior Type = Default freezes Unity**: Academy waits 30-60s if no Python running.
   Use Heuristic Only when not training.
5. **UnityCommunicatorStoppedException during reset**: Usually BehaviorParameters
   Space Size mismatch (must be exactly 5) or Unity exiting Play mode.
6. **LM Studio auth**: Newer LM Studio requires real API key, not placeholder "lm-studio"
