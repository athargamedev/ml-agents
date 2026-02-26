"""
LlmDialogueChannel — Python-side SideChannel for NPC dialogue.

Receives dialogue requests from Unity NPCs, dispatches them to a pluggable
LLM handler in a background thread, and queues responses to be sent back.

Usage:
    from mlagents.trainers.llm_dialogue_channel import LlmDialogueChannel
    from mlagents.trainers.llm_bridge_server import make_ollama_handler

    channel = LlmDialogueChannel(handler=make_ollama_handler("llama3"))
    env = UnityEnvironment(side_channels=[channel])

    for step in range(1000):
        env.step()
        channel.flush_responses()   # must call this every step
"""

import json
import queue
import threading
import time
import uuid
from typing import Callable

from mlagents_envs.side_channel import SideChannel, IncomingMessage, OutgoingMessage

DIALOGUE_CHANNEL_ID = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
PING_MESSAGE_TYPE = "ping"
PING_RESPONSE_TEXT = "__pong__"
HEARTBEAT_NPC_ID = "__bridge__"
HEARTBEAT_RESPONSE_TEXT = "__bridge_ready__"
HEARTBEAT_INTERVAL_SECONDS = 0.5


class LlmDialogueChannel(SideChannel):
    """
    SideChannel that bridges Unity NPC dialogue requests to any LLM backend.

    The handler is called in a background thread so the simulation step never
    blocks. Responses are queued and flushed back to Unity on the next step
    via flush_responses().
    """

    def __init__(self, handler: Callable[[dict], dict]):
        """
        Args:
            handler: A callable that takes a request dict and returns a response
                     dict. Called in a background thread per request.
                     See llm_bridge_server.py for ready-made handlers.
        """
        super().__init__(DIALOGUE_CHANNEL_ID)
        self._handler = handler
        self._response_queue: queue.Queue = queue.Queue()
        self._last_heartbeat_sent = 0.0

    def on_message_received(self, msg: IncomingMessage) -> None:
        """Deserialize the request and dispatch it to the LLM handler."""
        raw = msg.get_raw_bytes()
        request = json.loads(raw.decode("utf-8"))
        if request.get("messageType") == PING_MESSAGE_TYPE:
            # Reply immediately in the current exchange so Unity warmup probes
            # (which can run during env.reset()) don't deadlock waiting for a
            # later env.step()/flush_responses() cycle.
            out = OutgoingMessage()
            out.set_raw_bytes(
                json.dumps(
                    {
                        "npcId": request.get("npcId", "ping"),
                        "responseText": PING_RESPONSE_TEXT,
                        "emotion": "neutral",
                        "confidence": 1.0,
                    }
                ).encode("utf-8")
            )
            self.queue_message_to_send(out)
            return

        threading.Thread(
            target=self._handle_and_queue,
            args=(request,),
            daemon=True,
        ).start()

    def _handle_and_queue(self, request: dict) -> None:
        """Run the LLM handler and push the response into the queue."""
        try:
            response = self._handler(request)
        except Exception as e:
            # Return a safe error response so Unity is never left hanging
            response = {
                "npcId": request.get("npcId", "unknown"),
                "responseText": "[LLM error — check Python console]",
                "emotion": "neutral",
                "confidence": 0.0,
            }
            print(f"[LlmDialogueChannel] Handler error for '{request.get('npcId')}': {e}")
        self._response_queue.put(response)

    def flush_responses(self) -> None:
        """
        Push all queued LLM responses back to Unity.

        Call this once per step, AFTER env.step(), before the next step:

            env.step()
            channel.flush_responses()
        """
        self._queue_heartbeat_if_due()
        while not self._response_queue.empty():
            response = self._response_queue.get_nowait()
            out = OutgoingMessage()
            out.set_raw_bytes(json.dumps(response).encode("utf-8"))
            self.queue_message_to_send(out)

    def _queue_heartbeat_if_due(self) -> None:
        now = time.monotonic()
        if now - self._last_heartbeat_sent < HEARTBEAT_INTERVAL_SECONDS:
            return

        self._last_heartbeat_sent = now
        self._response_queue.put(
            {
                "npcId": HEARTBEAT_NPC_ID,
                "responseText": HEARTBEAT_RESPONSE_TEXT,
                "emotion": "neutral",
                "confidence": 1.0,
            }
        )
