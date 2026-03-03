"""
LLM handler factories for LlmDialogueChannel.

Each factory returns a callable with signature:
    handler(request: dict) -> dict

where request keys are: npcId, playerInput, conversationHistory, npcPersonality, messageType
and response keys are:   npcId, responseText, emotion, confidence

Pick one handler and pass it to LlmDialogueChannel:

    from mlagents.trainers.llm_bridge_server import make_lmstudio_handler
    from mlagents.trainers.llm_dialogue_channel import LlmDialogueChannel

    channel = LlmDialogueChannel(handler=make_lmstudio_handler())
"""

import json
from typing import Callable, List


# ---------------------------------------------------------------------------
# Mock handler — no LLM required, good for testing the wiring
# ---------------------------------------------------------------------------

def make_mock_handler() -> Callable[[dict], dict]:
    """
    Returns a handler that echoes the player input back without calling any LLM.
    Use this to verify the SideChannel plumbing works before adding a real model.
    """
    def handler(request: dict) -> dict:
        return {
            "npcId": request.get("npcId", "npc"),
            "responseText": f"[Mock] You said: \"{request.get('playerInput', '')}\"",
            "emotion": "neutral",
            "confidence": 1.0,
        }
    return handler


# ---------------------------------------------------------------------------
# LM Studio handler — OpenAI-compatible API at localhost:7002
# This matches your project's existing LM Studio setup.
# ---------------------------------------------------------------------------

def make_lmstudio_handler(
    model: str = "",
    host: str = "127.0.0.1",
    port: int = 7002,
    timeout: int = 60,
    api_key: str = "",
) -> Callable[[dict], dict]:
    """
    Returns a handler that calls LM Studio's OpenAI-compatible API.

    Matches the same host/port your NetworkDialogueService already uses.
    Leave model="" to use whatever model is currently loaded in LM Studio.

    Prerequisites:
        pip install openai
        LM Studio running with qwen3-8b (or any loaded model — leave model="" to auto-detect)
        Server started in LM Studio on port 7002

    Args:
        model:   Model identifier shown in LM Studio. Leave empty to auto-select.
        host:    LM Studio host (default: 127.0.0.1)
        port:    LM Studio server port (default: 7002)
        timeout: Request timeout in seconds
    """
    import os
    from openai import OpenAI

    base_url = f"http://{host}:{port}/v1"
    # Use provided key, or fall back to the legacy "lm-studio" placeholder
    # (older LM Studio versions accept any non-empty string).
    resolved_key = api_key if api_key else "lm-studio"
    client = OpenAI(
        api_key=resolved_key,
        base_url=base_url,
    )
    effective_model = model if model else "local-model"

    def handler(request: dict) -> dict:
        messages = _build_chat_messages(request)
        completion = client.chat.completions.create(
            model=effective_model,
            messages=messages,
            response_format={
                "type": "json_schema",
                "json_schema": {
                    "name": "dialogue_response",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "responseText": {"type": "string"},
                            "emotion": {"type": "string"},
                            "confidence": {"type": "number"},
                        },
                        "required": ["responseText", "emotion", "confidence"],
                        "additionalProperties": False,
                    },
                },
            },
            timeout=timeout,
        )
        raw = completion.choices[0].message.content or "{}"
        content = _safe_parse(raw)
        return _build_response(request, content, raw)

    return handler


# ---------------------------------------------------------------------------
# Ollama handler — local LLM via http://localhost:11434
# ---------------------------------------------------------------------------

def make_ollama_handler(
    model: str = "llama3",
    base_url: str = "http://localhost:11434",
    timeout: int = 30,
) -> Callable[[dict], dict]:
    """
    Returns a handler that calls a locally running Ollama model.

    Prerequisites:
        ollama pull llama3   # download the model once
        ollama serve         # keep running in a terminal

    Args:
        model:    Ollama model tag, e.g. "llama3", "mistral", "phi3"
        base_url: Ollama server URL (default: http://localhost:11434)
        timeout:  Request timeout in seconds
    """
    import requests

    def handler(request: dict) -> dict:
        messages = _build_chat_messages(request)
        payload = {
            "model": model,
            "messages": messages,
            "format": "json",
            "stream": False,
        }
        resp = requests.post(f"{base_url}/api/chat", json=payload, timeout=timeout)
        resp.raise_for_status()
        raw = resp.json().get("message", {}).get("content", "{}")
        content = _safe_parse(raw)
        return _build_response(request, content, raw)

    return handler


# ---------------------------------------------------------------------------
# OpenAI handler — GPT-4o-mini or any chat-completions model
# ---------------------------------------------------------------------------

def make_openai_handler(
    model: str = "gpt-4o-mini",
    api_key: str = None,
) -> Callable[[dict], dict]:
    """
    Returns a handler that calls the OpenAI Chat Completions API.

    Prerequisites:
        pip install openai
        export OPENAI_API_KEY=sk-...

    Args:
        model:   OpenAI model name, e.g. "gpt-4o-mini", "gpt-4o"
        api_key: API key (falls back to OPENAI_API_KEY env var)
    """
    import os
    from openai import OpenAI

    client = OpenAI(api_key=api_key or os.environ.get("OPENAI_API_KEY"))

    def handler(request: dict) -> dict:
        messages = _build_chat_messages(request)
        completion = client.chat.completions.create(
            model=model,
            messages=messages,
            response_format={"type": "json_object"},
        )
        raw = completion.choices[0].message.content or "{}"
        content = _safe_parse(raw)
        return _build_response(request, content, raw)

    return handler


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _build_system_prompt(request: dict) -> str:
    personality = request.get("npcPersonality", "")

    # When Unity sends the full, pre-built system prompt via npcPersonality
    # (SideChannelDialogueClient always does this), use it directly so the
    # rich NPC persona / lore / effect guide reaches the LLM unchanged.
    # Only fall back to the generic template for lightweight integrations
    # that supply a short personality string instead.
    if len(personality) > 120:
        # Append the JSON-format reminder so the output stays parseable.
        return (
            personality + "\n\n"
            "Respond ONLY with valid JSON in this exact format: "
            '{"responseText": "...", "emotion": "neutral|happy|angry|sad|fearful|surprised", "confidence": 0.0}'
        )

    npc_id = request.get("npcId", "an NPC")
    return (
        f"You are {npc_id}, a character in a video game. "
        f"Personality: {personality or 'a generic NPC'}. "
        "Reply in 1-2 sentences, staying fully in character. "
        'Respond ONLY with valid JSON in this exact format: '
        '{"responseText": "...", "emotion": "neutral|happy|angry|sad|fearful|surprised", "confidence": 0.0}'
    )


def _build_chat_messages(request: dict) -> List[dict]:
    messages: List[dict] = [
        {"role": "system", "content": _build_system_prompt(request)}
    ]
    messages.extend(_parse_history_messages(request.get("conversationHistory")))
    messages.append({"role": "user", "content": request.get("playerInput", "")})
    return messages


def _parse_history_messages(raw_history) -> List[dict]:
    if not raw_history:
        return []

    if isinstance(raw_history, str):
        try:
            parsed = json.loads(raw_history)
        except json.JSONDecodeError:
            return []
    elif isinstance(raw_history, list):
        parsed = raw_history
    else:
        return []

    if not isinstance(parsed, list):
        return []

    messages: List[dict] = []
    for item in parsed:
        if not isinstance(item, dict):
            continue

        role = str(item.get("role", "user")).strip().lower()
        if role == "model":
            role = "assistant"
        if role not in ("user", "assistant", "system"):
            role = "user"

        # Unity already sends the authoritative system prompt in npcPersonality.
        if role == "system":
            continue

        content = item.get("content", "")
        if not isinstance(content, str):
            content = str(content)
        if not content:
            continue

        messages.append({"role": role, "content": content})

    return messages


def _safe_parse(raw: str) -> dict:
    """Parse JSON from LLM output, returning an empty dict on failure."""
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return {}


def _build_response(request: dict, content: dict, raw: str = "") -> dict:
    # If JSON parsing succeeded, use structured fields.
    # If it failed (content is {}), fall back to the raw text so the NPC
    # still says something intelligible rather than "...".
    response_text = content.get("responseText") or raw.strip() or "..."
    return {
        "npcId": request.get("npcId", "npc"),
        "responseText": response_text,
        "emotion": content.get("emotion", "neutral"),
        "confidence": float(content.get("confidence", 0.8)),
    }
