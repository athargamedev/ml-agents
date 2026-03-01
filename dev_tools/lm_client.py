"""
dev_tools/lm_client.py — LM Studio client for all dev assistant tools.

Architecture
────────────
Two endpoints, one client:

  • PRIMARY  →  Anthropic-compatible  /v1/messages
    - Same protocol as Claude Code / Anthropic SDK
    - KV cache: system prompt is reused across calls (confirmed via
      cache_read_input_tokens). Large file context embedded once, free after.
    - Use for: all conversational jobs, multi-turn analysis, NPC profiles

  • STRUCTURED → OpenAI-compatible   /v1/chat/completions
    - json_schema response_format forces valid structured output
    - Use for: jobs that must return a typed dict (reward analysis, effects
      catalog, semantic tag patches, watchdog thresholds)

Model routing (per job):
  CODE_MODEL  — qwen2.5-coder-7b-instruct@q4_k_m  (code analysis jobs)
  TEXT_MODEL  — llama-3.2-3b-instruct@q8_0         (text/profile jobs, already loaded)
  FAST_MODEL  — llama-3.2-3b-instruct@q4_k_s       (quick single-fact lookups)

lms CLI integration:
  Use subprocess calls to `lms load <model>` before a job batch and
  `lms unload <model>` after if VRAM is tight.

Claude Code integration:
  To run a Claude Code session backed by LM Studio:
    ANTHROPIC_BASE_URL=http://100.80.22.49:7002
    ANTHROPIC_AUTH_TOKEN=<api_key>
    claude --model qwen2.5-coder-7b-instruct@q4_k_m
"""

from __future__ import annotations

import json
import subprocess
import time
from typing import Any

# ── Config ────────────────────────────────────────────────────────────────────
LMS_HOST    = "127.0.0.1"   # localhost for dev_tools (Unity uses 100.80.22.49 via Tailscale)
LMS_PORT    = 7002
LMS_API_KEY = "sk-lm-Li2oVsHm:wNxCcCTjZM4PFuNC0RnH"

# Model aliases — callers use these constants, not raw strings
CODE_MODEL = "qwen2.5-coder-7b-instruct@q4_k_m"   # best for C# code analysis
TEXT_MODEL = "llama-3.2-3b-instruct@q8_0"          # already loaded, good for prose
FAST_MODEL = "llama-3.2-3b-instruct@q4_k_s"        # already loaded, fastest

DEFAULT_MAX_TOKENS  = 900    # safe upper bound — 600 causes JSON truncation
DEFAULT_TEMPERATURE = 0.15   # deterministic analysis output
# ─────────────────────────────────────────────────────────────────────────────


class LmClient:
    """
    Dual-mode LM Studio client.

    Usage:
        client = LmClient()
        if not client.is_available():
            return

        # Conversational (Anthropic endpoint, KV cache)
        reply = client.ask(
            system="You are a Unity code reviewer.",
            user="Review NpcDialogueAgent reward params...",
            model=CODE_MODEL,
        )

        # Structured output (OpenAI endpoint, json_schema)
        result = client.ask_schema(
            system="You analyze Unity C# issues.",
            user="MaxStep=5 on Agent but 1000 on NpcDialogueAgent.",
            schema={
                "type": "object",
                "properties": {
                    "issue": {"type": "string"},
                    "severity": {"type": "string", "enum": ["low", "medium", "high"]},
                    "fix": {"type": "string"},
                },
                "required": ["issue", "severity", "fix"],
                "additionalProperties": False,
            },
            schema_name="issue_report",
            model=CODE_MODEL,
        )

        # Multi-turn (Anthropic endpoint, KV cache on system prompt)
        history = []
        reply1 = client.ask_stateful(
            system="You are reviewing Unity NPC profiles.",
            history=history,
            user="Here is the StormOracle profile: ...",
            model=TEXT_MODEL,
        )
        reply2 = client.ask_stateful(
            system="You are reviewing Unity NPC profiles.",
            history=history,
            user="Now generate a 2-sentence Description for the DialogueSemanticTag.",
            model=TEXT_MODEL,
        )
    """

    def __init__(
        self,
        host: str = LMS_HOST,
        port: int = LMS_PORT,
        api_key: str = LMS_API_KEY,
    ) -> None:
        self._base    = f"http://{host}:{port}"
        self._api_key = api_key
        self._anthropic: Any = None
        self._openai: Any    = None
        self._init_clients()

    # ── Init ──────────────────────────────────────────────────────────────────

    def _init_clients(self) -> None:
        try:
            import anthropic
            self._anthropic = anthropic.Anthropic(
                base_url=self._base,
                api_key=self._api_key,
            )
        except ImportError:
            print("[LmClient] WARNING: 'anthropic' not installed. "
                  "Run: pip install anthropic")

        try:
            import openai
            self._openai = openai.OpenAI(
                base_url=f"{self._base}/v1",
                api_key=self._api_key,
            )
        except ImportError:
            print("[LmClient] WARNING: 'openai' not installed. "
                  "Run: pip install openai")

    # ── Availability ──────────────────────────────────────────────────────────

    def is_available(self, timeout: float = 5.0) -> bool:
        """Cheap liveness check — does not consume tokens."""
        if self._anthropic is None and self._openai is None:
            return False
        try:
            import urllib.request
            req = urllib.request.Request(
                f"{self._base}/v1/models",
                headers={"Authorization": f"Bearer {self._api_key}"},
            )
            with urllib.request.urlopen(req, timeout=timeout):
                return True
        except Exception:
            return False

    # ── Primary: Anthropic endpoint (KV cache, clean system prompt) ───────────

    def ask(
        self,
        system: str,
        user: str,
        model: str = TEXT_MODEL,
        max_tokens: int = DEFAULT_MAX_TOKENS,
        temperature: float = DEFAULT_TEMPERATURE,
    ) -> str:
        """
        Single-turn request via Anthropic /v1/messages.

        The system prompt is KV-cached by LM Studio — repeated calls with the
        same system string (e.g. large code file) skip re-tokenising it.
        """
        if self._anthropic is None:
            return ""
        try:
            msg = self._anthropic.messages.create(
                model=model,
                system=system,
                messages=[{"role": "user", "content": user}],
                max_tokens=max_tokens,
                temperature=temperature,
            )
            return msg.content[0].text if msg.content else ""
        except Exception as ex:
            print(f"[LmClient] ask() error: {type(ex).__name__}: {ex}")
            return ""

    def ask_stateful(
        self,
        system: str,
        history: list[dict],
        user: str,
        model: str = TEXT_MODEL,
        max_tokens: int = DEFAULT_MAX_TOKENS,
        temperature: float = DEFAULT_TEMPERATURE,
    ) -> str:
        """
        Multi-turn request. Appends user message to history in-place and
        records the assistant reply, so the same list can be passed next call.

        The system prompt is the KV-cache anchor — keep it identical across
        turns to maximise cache hits.

        Example:
            history = []
            r1 = client.ask_stateful(system, history, "Read this profile: ...")
            r2 = client.ask_stateful(system, history, "Now write a description.")
        """
        if self._anthropic is None:
            return ""
        history.append({"role": "user", "content": user})
        try:
            msg = self._anthropic.messages.create(
                model=model,
                system=system,
                messages=history,
                max_tokens=max_tokens,
                temperature=temperature,
            )
            reply = msg.content[0].text if msg.content else ""
            history.append({"role": "assistant", "content": reply})
            return reply
        except Exception as ex:
            print(f"[LmClient] ask_stateful() error: {type(ex).__name__}: {ex}")
            history.pop()  # rollback the user message on failure
            return ""

    # ── Structured: OpenAI endpoint (json_schema enforcement) ─────────────────

    def ask_schema(
        self,
        system: str,
        user: str,
        schema: dict,
        schema_name: str = "result",
        model: str = TEXT_MODEL,   # llama-3.2-3b@q8_0 — reliable under grammar sampling
        max_tokens: int = DEFAULT_MAX_TOKENS,
        temperature: float = DEFAULT_TEMPERATURE,
        frequency_penalty: float = 0.3,  # breaks repetition loops in grammar-sampled output
    ) -> dict:
        """
        Request with enforced JSON schema via OpenAI /v1/chat/completions.

        LM Studio uses response_format.type = "json_schema" (not "json_object").
        Returns the parsed dict, or {"error": reason} on failure.

        The schema must have "additionalProperties": false at every level to
        satisfy LM Studio's strict mode.
        """
        if self._openai is None:
            return {"error": "openai SDK not available"}
        try:
            resp = self._openai.chat.completions.create(
                model=model,
                messages=[
                    {"role": "system", "content": system},
                    {"role": "user",   "content": user},
                ],
                max_tokens=max_tokens,
                temperature=temperature,
                frequency_penalty=frequency_penalty,
                response_format={
                    "type": "json_schema",
                    "json_schema": {
                        "name":   schema_name,
                        "strict": True,
                        "schema": schema,
                    },
                },
            )
            raw = resp.choices[0].message.content or ""
            return json.loads(raw)
        except json.JSONDecodeError as ex:
            return {"error": f"JSON parse failed: {ex}", "raw": raw}
        except Exception as ex:
            print(f"[LmClient] ask_schema() error: {type(ex).__name__}: {ex}")
            return {"error": str(ex)}

    # ── Retry wrapper ──────────────────────────────────────────────────────────

    def ask_with_retry(
        self,
        system: str,
        user: str,
        model: str = TEXT_MODEL,
        max_tokens: int = DEFAULT_MAX_TOKENS,
        retries: int = 2,
        retry_delay: float = 3.0,
    ) -> str:
        """ask() with simple retry for transient LM Studio hiccups."""
        for attempt in range(retries + 1):
            result = self.ask(system, user, model=model, max_tokens=max_tokens)
            if result:
                return result
            if attempt < retries:
                print(f"[LmClient] Empty response on attempt {attempt+1}, "
                      f"retrying in {retry_delay}s...")
                time.sleep(retry_delay)
        return ""

    # ── CLI model management ───────────────────────────────────────────────────

    @staticmethod
    def load_model(model: str, ttl: int = 0) -> bool:
        """
        Load a model via `lms load`. Blocks until the model is ready.
        ttl=0 means keep loaded until explicitly unloaded.
        Returns True on success.
        """
        cmd = ["lms", "load", model]
        if ttl > 0:
            cmd += ["--ttl", str(ttl)]
        try:
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
            if result.returncode == 0:
                print(f"[LmClient] Loaded model: {model}")
                return True
            print(f"[LmClient] lms load failed: {result.stderr.strip()}")
            return False
        except Exception as ex:
            print(f"[LmClient] lms load error: {ex}")
            return False

    @staticmethod
    def unload_model(model: str) -> bool:
        """Unload a model via `lms unload` to free VRAM between job batches."""
        try:
            result = subprocess.run(
                ["lms", "unload", model],
                capture_output=True, text=True, timeout=30,
            )
            return result.returncode == 0
        except Exception as ex:
            print(f"[LmClient] lms unload error: {ex}")
            return False

    @staticmethod
    def list_loaded_models() -> list[str]:
        """Return names of currently loaded models via `lms ps`."""
        try:
            result = subprocess.run(
                ["lms", "ps"],
                capture_output=True, text=True, timeout=10,
            )
            lines = result.stdout.splitlines()
            # Lines with a loaded model contain '@' in the model identifier
            return [
                line.split()[0]
                for line in lines
                if "@" in line or ("/" in line and line.strip())
            ]
        except Exception:
            return []
