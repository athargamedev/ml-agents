"""
dev_tools/lm_client.py — LM Studio client for all dev assistant tools.

Architecture
────────────
Two endpoints, one client:

  • PRIMARY  →  Anthropic-compatible  /v1/messages
    - Same protocol as Claude Code / Anthropic SDK
    - KV cache: system prompt is reused across calls (confirmed via
      cache_read_input_tokens). Large file context embedded once, free after.
    - stop_sequences accepted (important for llama chat tokens)
    - Use for: all conversational jobs, multi-turn analysis, NPC profiles

  • STRUCTURED → OpenAI-compatible   /v1/chat/completions
    - json_schema response_format forces valid structured output
    - extra_body passes LM Studio-specific params: top_k, repeat_penalty, min_p
    - Use for: jobs that must return a typed dict (reward analysis, effects
      catalog, semantic tag patches, watchdog thresholds)

Model routing (confirmed by benchmark):
  CODE_MODEL  — qwen2.5-coder-7b-instruct@q4_k_m   ask() only (Anthropic)
  TEXT_MODEL  — llama-3.2-3b-instruct@q8_0          ask() + ask_schema() (reliable, ~5s)
  FAST_MODEL  — llama-3.2-3b-instruct@q4_k_s        quick single-fact lookups

  ⚠ qwen2.5-coder-7b FAILS json_schema mode (136s + garbled, 2026-02-28 test).
  ⚠ qwen3-8b too slow for batch schema (46s/call, 2026-02-28 test).
  → Always use TEXT_MODEL for ask_schema().

Sampling profiles (pass profile="name" to any ask*() method):
  analysis  — low temp, high repeat_penalty, top_k=20 — deterministic code review
  creative  — higher temp, min_p gating — NPC profile generation
  schema    — minimal temp, strict repeat_penalty — json_schema grammar sampling
  fast      — defaults, no extra params — quick lookups

lms CLI integration:
  load_model(model, gpu="max", context_length=4096) calls
    lms load <model> --gpu max --context-length 4096
  before a job batch. Use unload_model() to free VRAM after.
"""

from __future__ import annotations

import json
import os
import subprocess
import time
from typing import Any


def _env_int(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if raw is None:
        return default
    try:
        return int(raw)
    except ValueError:
        return default

# ── Config ────────────────────────────────────────────────────────────────────
LMS_HOST    = os.environ.get("LM_STUDIO_HOST", "127.0.0.1")
LMS_PORT    = _env_int("LM_STUDIO_PORT", 7002)
LMS_API_KEY = os.environ.get("LM_STUDIO_API_KEY", "lm-studio")

# Model aliases — callers use these constants, not raw strings
CODE_MODEL = "qwen2.5-coder-7b-instruct@q4_k_m"   # ask() only — fails json_schema
TEXT_MODEL = "llama-3.2-3b-instruct@q8_0"          # ask() + ask_schema() — reliable
FAST_MODEL = "llama-3.2-3b-instruct@q4_k_s"        # fastest — quick lookups only

DEFAULT_MAX_TOKENS  = 900    # safe upper bound — 600 causes JSON truncation
DEFAULT_TEMPERATURE = 0.15   # deterministic analysis output

# ── Sampling profiles ─────────────────────────────────────────────────────────
# Each profile is a dict of kwargs forwarded to the inference call.
# extra_body keys (top_k, repeat_penalty, min_p) are LM Studio extensions.
SAMPLING_PROFILES: dict[str, dict] = {
    "analysis": {
        # Strict determinism for code review / reward diagnostics
        "temperature": 0.1,
        "top_k": 20,
        "repeat_penalty": 1.2,
        "min_p": 0.05,
    },
    "schema": {
        # Grammar-sampled structured output — max stability
        "temperature": 0.05,
        "top_k": 20,
        "repeat_penalty": 1.15,
        "min_p": 0.03,
        "frequency_penalty": 0.3,
    },
    "creative": {
        # NPC profile / narrative generation — allow diversity
        "temperature": 0.75,
        "top_k": 60,
        "repeat_penalty": 1.05,
        "min_p": 0.10,
    },
    "fast": {
        # Single-fact lookups — stock defaults
        "temperature": DEFAULT_TEMPERATURE,
    },
}

# Stop sequences for llama-3.x chat models (prevents runaway generation)
LLAMA_STOP = ["<|eot_id|>", "<|end_of_text|>", "<|start_header_id|>"]
# Stop sequences for Qwen2.x / Qwen2.5 instruct models
QWEN_STOP  = ["<|im_end|>", "<|endoftext|>"]
# ─────────────────────────────────────────────────────────────────────────────


class LmClient:
    """
    Dual-mode LM Studio client with per-job sampling profiles.

    Usage:
        client = LmClient()
        if not client.is_available():
            return

        # Conversational with sampling profile (Anthropic endpoint, KV cache)
        reply = client.ask(
            system="You are a Unity code reviewer.",
            user="Review NpcDialogueAgent reward params...",
            model=CODE_MODEL,
            profile="analysis",          # → top_k=20, repeat_penalty=1.2
        )

        # With explicit stop sequences (prevents llama chat overflow)
        reply = client.ask(
            system="...", user="...",
            model=TEXT_MODEL,
            stop_sequences=LLAMA_STOP,
        )

        # Structured output (OpenAI endpoint, json_schema + extra_body)
        result = client.ask_schema(
            system="You analyze Unity C# issues.",
            user="MaxStep=5 on Agent but 1000 on NpcDialogueAgent.",
            schema={...},
            schema_name="issue_report",
            model=TEXT_MODEL,            # always TEXT_MODEL for json_schema
            profile="schema",            # → top_k=20, repeat_penalty=1.15
        )

        # Multi-turn with profile
        history = []
        r1 = client.ask_stateful(system, history, "Here is the profile: ...",
                                  model=TEXT_MODEL, profile="creative")
        r2 = client.ask_stateful(system, history, "Now summarise it.",
                                  model=TEXT_MODEL, profile="creative")
    """

    def __init__(
        self,
        host: str = LMS_HOST,
        port: int = LMS_PORT,
        api_key: str = LMS_API_KEY,
    ) -> None:
        self._host    = host
        self._port    = port
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

    # ── Properties ────────────────────────────────────────────────────────────

    @property
    def base_url(self) -> str:
        return self._base

    @property
    def host(self) -> str:
        return self._host

    @property
    def port(self) -> int:
        return self._port

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

    def ensure_models_loaded(
        self,
        models: list[str],
        ttl: int = 0,
        context_length: int | None = 4096,
        gpu: str = "max",
    ) -> bool:
        """
        Ensure every model in the list is loaded. Calls load_model() for any
        that are not currently loaded. Returns True if all models are available.

        Use before a job batch to avoid mid-scan model-not-found errors:
            client.ensure_models_loaded([TEXT_MODEL], context_length=4096)
        """
        requested = [model for model in dict.fromkeys(models) if model]
        if not requested:
            return True

        loaded = set(self.list_loaded_models())
        all_ok = True
        for model in requested:
            if any(model in loaded_model or loaded_model in model for loaded_model in loaded):
                continue  # already loaded (substring match handles alias differences)
            print(f"[LmClient] Model not loaded: {model} — loading now...")
            ok = self.load_model(
                model,
                ttl=ttl,
                gpu=gpu,
                context_length=context_length,
            )
            if not ok:
                print(f"[LmClient] WARNING: Failed to load {model}")
                all_ok = False
                continue
            loaded.add(model)
        return all_ok

    # ── Profile helper ────────────────────────────────────────────────────────

    @staticmethod
    def _resolve_profile(
        profile: str | None,
        temperature: float,
    ) -> dict:
        """
        Merge a named sampling profile with explicit overrides.
        A named profile supplies defaults, but a non-default temperature
        explicitly passed by the caller overrides the profile.
        Returns a flat dict with all resolved params.
        """
        resolved: dict[str, Any] = {}
        if profile and profile in SAMPLING_PROFILES:
            resolved.update(SAMPLING_PROFILES[profile])
        if "temperature" not in resolved or temperature != DEFAULT_TEMPERATURE or not profile:
            resolved["temperature"] = temperature
        return resolved

    @staticmethod
    def _extract_text_content(message: Any) -> str:
        content = getattr(message, "content", None) or []
        parts: list[str] = []
        for block in content:
            text = getattr(block, "text", None)
            if text:
                parts.append(text)
        return "".join(parts)

    # ── Primary: Anthropic endpoint (KV cache, clean system prompt) ───────────

    def ask(
        self,
        system: str,
        user: str,
        model: str = TEXT_MODEL,
        max_tokens: int = DEFAULT_MAX_TOKENS,
        temperature: float = DEFAULT_TEMPERATURE,
        profile: str | None = None,
        stop_sequences: list[str] | None = None,
    ) -> str:
        """
        Single-turn request via Anthropic /v1/messages.

        The system prompt is KV-cached by LM Studio — repeated calls with the
        same system string (e.g. large code file) skip re-tokenising it.

        profile:         one of SAMPLING_PROFILES keys ("analysis", "creative",
                         "schema", "fast") — overrides temperature if present.
        stop_sequences:  forwarded to LM Studio via Anthropic SDK.
                         Use LLAMA_STOP for llama-3.x models to cap runaway output.
        """
        if self._anthropic is None:
            return ""
        params = self._resolve_profile(profile, temperature)
        extra: dict[str, Any] = {}
        # Map extra_body keys (LM Studio extensions) out of the resolved params
        for key in ("top_k", "repeat_penalty", "min_p"):
            if key in params:
                extra[key] = params.pop(key)
        try:
            kwargs: dict[str, Any] = dict(
                model=model,
                system=system,
                messages=[{"role": "user", "content": user}],
                max_tokens=max_tokens,
                **params,
            )
            if stop_sequences:
                kwargs["stop_sequences"] = stop_sequences
            if extra:
                kwargs["extra_body"] = extra
            msg = self._anthropic.messages.create(**kwargs)
            return self._extract_text_content(msg)
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
        profile: str | None = None,
        stop_sequences: list[str] | None = None,
    ) -> str:
        """
        Multi-turn request. Appends user message to history in-place and
        records the assistant reply, so the same list can be passed next call.

        The system prompt is the KV-cache anchor — keep it identical across
        turns to maximise cache hits.

        Example:
            history = []
            r1 = client.ask_stateful(system, history, "Read this profile: ...",
                                      profile="creative")
            r2 = client.ask_stateful(system, history, "Now write a description.",
                                      profile="creative")
        """
        if self._anthropic is None:
            return ""
        params = self._resolve_profile(profile, temperature)
        extra: dict[str, Any] = {}
        for key in ("top_k", "repeat_penalty", "min_p"):
            if key in params:
                extra[key] = params.pop(key)
        history.append({"role": "user", "content": user})
        try:
            kwargs: dict[str, Any] = dict(
                model=model,
                system=system,
                messages=history,
                max_tokens=max_tokens,
                **params,
            )
            if stop_sequences:
                kwargs["stop_sequences"] = stop_sequences
            if extra:
                kwargs["extra_body"] = extra
            msg = self._anthropic.messages.create(**kwargs)
            reply = self._extract_text_content(msg)
            history.append({"role": "assistant", "content": reply})
            return reply
        except Exception as ex:
            print(f"[LmClient] ask_stateful() error: {type(ex).__name__}: {ex}")
            history.pop()  # rollback the user message on failure
            return ""

    # ── Structured: OpenAI endpoint (json_schema + extra_body) ───────────────

    def ask_schema(
        self,
        system: str,
        user: str,
        schema: dict,
        schema_name: str = "result",
        model: str = TEXT_MODEL,   # always TEXT_MODEL — qwen2.5-coder fails json_schema
        max_tokens: int = DEFAULT_MAX_TOKENS,
        temperature: float = DEFAULT_TEMPERATURE,
        frequency_penalty: float = 0.3,
        profile: str | None = "schema",   # default to schema profile
    ) -> dict:
        """
        Request with enforced JSON schema via OpenAI /v1/chat/completions.

        LM Studio uses response_format.type = "json_schema" (not "json_object").
        Returns the parsed dict, or {"error": reason} on failure.

        The schema must have "additionalProperties": false at every level to
        satisfy LM Studio's strict mode.

        profile:  defaults to "schema" — applies top_k=20, repeat_penalty=1.15,
                  min_p=0.03 via extra_body for maximum grammar-sampling stability.

        ⚠ Use only TEXT_MODEL (llama-3.2-3b@q8_0) — qwen2.5-coder-7b produces
          garbled output in json_schema mode (confirmed 2026-02-28).
        """
        if self._openai is None:
            return {"error": "openai SDK not available"}

        params = self._resolve_profile(profile, temperature)
        # Separate standard OpenAI params from LM Studio extra_body params
        extra: dict[str, Any] = {}
        for key in ("top_k", "repeat_penalty", "min_p"):
            if key in params:
                extra[key] = params.pop(key)
        # frequency_penalty: use profile value if present, else explicit arg
        fp = params.pop("frequency_penalty", frequency_penalty)

        try:
            kwargs: dict[str, Any] = dict(
                model=model,
                messages=[
                    {"role": "system", "content": system},
                    {"role": "user",   "content": user},
                ],
                max_tokens=max_tokens,
                frequency_penalty=fp,
                response_format={
                    "type": "json_schema",
                    "json_schema": {
                        "name":   schema_name,
                        "strict": True,
                        "schema": schema,
                    },
                },
                **params,
            )
            if extra:
                kwargs["extra_body"] = extra
            resp = self._openai.chat.completions.create(**kwargs)
            raw = resp.choices[0].message.content or ""
            return json.loads(raw)
        except json.JSONDecodeError as ex:
            return {"error": f"JSON parse failed: {ex}", "raw": raw}
        except Exception as ex:
            print(f"[LmClient] ask_schema() error: {type(ex).__name__}: {ex}")
            return {"error": str(ex)}

    # ── OpenAI prose (no json_schema) — for CODE_MODEL ───────────────────────

    def ask_openai_prose(
        self,
        system: str,
        user: str,
        model: str = CODE_MODEL,
        max_tokens: int = DEFAULT_MAX_TOKENS,
        temperature: float = DEFAULT_TEMPERATURE,
        profile: str | None = "analysis",
        stop_sequences: list[str] | None = None,
    ) -> str:
        """
        Plain-text request via OpenAI /v1/chat/completions (no json_schema).

        Use for CODE_MODEL (qwen2.5-coder-7b) which fails on the Anthropic
        endpoint and on json_schema mode but works well for prose via OpenAI.
        Also suitable as a fallback when ask() returns empty.

        stop_sequences: pass QWEN_STOP for qwen models to prevent runaway output.
        """
        if self._openai is None:
            return ""
        params = self._resolve_profile(profile, temperature)
        extra: dict[str, Any] = {}
        for key in ("top_k", "repeat_penalty", "min_p"):
            if key in params:
                extra[key] = params.pop(key)
        params.pop("frequency_penalty", None)  # not needed for prose
        try:
            kwargs: dict[str, Any] = dict(
                model=model,
                messages=[
                    {"role": "system", "content": system},
                    {"role": "user",   "content": user},
                ],
                max_tokens=max_tokens,
                **params,
            )
            if stop_sequences:
                kwargs["stop"] = stop_sequences
            if extra:
                kwargs["extra_body"] = extra
            resp = self._openai.chat.completions.create(**kwargs)
            return resp.choices[0].message.content or ""
        except Exception as ex:
            print(f"[LmClient] ask_openai_prose() error: {type(ex).__name__}: {ex}")
            return ""

    # ── Retry wrapper ──────────────────────────────────────────────────────────

    def ask_with_retry(
        self,
        system: str,
        user: str,
        model: str = TEXT_MODEL,
        max_tokens: int = DEFAULT_MAX_TOKENS,
        profile: str | None = "analysis",
        stop_sequences: list[str] | None = None,
        retries: int = 2,
        retry_delay: float = 3.0,
    ) -> str:
        """ask() with simple retry for transient LM Studio hiccups."""
        for attempt in range(retries + 1):
            result = self.ask(
                system, user,
                model=model,
                max_tokens=max_tokens,
                profile=profile,
                stop_sequences=stop_sequences,
            )
            if result:
                return result
            if attempt < retries:
                print(f"[LmClient] Empty response on attempt {attempt+1}, "
                      f"retrying in {retry_delay}s...")
                time.sleep(retry_delay)
        return ""

    # ── CLI model management ───────────────────────────────────────────────────

    @staticmethod
    def load_model(
        model: str,
        ttl: int = 0,
        gpu: str = "max",
        context_length: int | None = None,
    ) -> bool:
        """
        Load a model via `lms load`. Blocks until the model is ready.

        gpu:             "max" offloads all layers to GPU (recommended for speed).
                         Set to "0" to run on CPU only.
        context_length:  reduces KV cache size to save VRAM.
                         None = model default. Use 4096 for scan jobs (saves ~1GB).
        ttl:             seconds before auto-unload (0 = keep until explicit unload).

        Example:
            LmClient.load_model(TEXT_MODEL, gpu="max", context_length=4096)
        """
        cmd = ["lms", "load", model, "--gpu", gpu]
        if context_length is not None:
            cmd += ["--context-length", str(context_length)]
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
            if result.returncode != 0:
                return []

            models: list[str] = []
            for line in result.stdout.splitlines():
                stripped = line.strip()
                if not stripped:
                    continue
                if stripped.lower().startswith("identifier"):
                    continue
                if set(stripped) <= {"-", " "}:
                    continue
                first_token = stripped.split()[0]
                if "@" in first_token or "/" in first_token:
                    models.append(first_token)
            return models
        except Exception:
            return []
