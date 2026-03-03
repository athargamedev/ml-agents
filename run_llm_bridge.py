"""
LM Studio <-> Unity NPC Dialogue Bridge

Run this BEFORE pressing Play in Unity:
    python run_llm_bridge.py

Requirements:
    - LM Studio running with server enabled on port 7002
    - Unity DevProject open with DialogueBridge scene
    - BehaviorParameters set to Behavior Type = Default (no model)
    - openai package: pip install openai
"""

import sys
import os
import time
import traceback
from datetime import datetime

# Add local ml-agents source so our new modules are found
# regardless of whether mlagents is installed as editable or not.
REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(REPO_ROOT, "ml-agents"))
sys.path.insert(0, os.path.join(REPO_ROOT, "ml-agents-envs"))

from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.exception import (
    UnityCommunicatorStoppedException,
    UnityTimeOutException,
    UnityWorkerInUseException,
)
from mlagents.trainers.llm_dialogue_channel import LlmDialogueChannel
from mlagents.trainers.llm_bridge_server import make_lmstudio_handler, make_mock_handler

# ── Config ────────────────────────────────────────────────────────────────────
ENABLE_DIALOGUE_SIDECHANNEL = False  # False = observer-only ML-Agents run (no custom LLM sidechannel traffic)
USE_MOCK   = False          # True = echo responses (no LM Studio needed)
LMS_HOST   = "127.0.0.1"
LMS_PORT   = 7002
LMS_MODEL  = "qwen3-8b"     # NPC dialogue model — best 8B for roleplay/instruction following
LMS_API_KEY = "sk-lm-Li2oVsHm:wNxCcCTjZM4PFuNC0RnH"  # LM Studio API key
MAX_STEPS  = 100_000        # run until stopped with Ctrl+C
UNITY_TIMEOUT_SECONDS = 300 # Covers slow Editor startup/reset on heavy scenes.
RESET_MAX_RETRIES = 3       # Reconnect + retry reset on communicator timeout.
RESET_RETRY_DELAY_SECONDS = 3
LOG_FILE = os.path.join(REPO_ROOT, ".codex", "tmp", "run_llm_bridge.runtime.log")
OBSERVER_LOG_EVERY_STEPS = 50
# ─────────────────────────────────────────────────────────────────────────────

def _ts() -> str:
    return datetime.now().strftime("%H:%M:%S")


def log(message: str) -> None:
    line = f"[{_ts()}] [Bridge] {message}"
    print(line, flush=True)
    try:
        os.makedirs(os.path.dirname(LOG_FILE), exist_ok=True)
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


def create_env(channel) -> UnityEnvironment:
    started = time.monotonic()
    log("Waiting for Unity to connect — press Play in the Editor now...")
    side_channels = [channel] if channel is not None else []
    env = UnityEnvironment(
        file_name=None,       # None = connect to already-running Editor instance
        side_channels=side_channels,
        timeout_wait=UNITY_TIMEOUT_SECONDS,
    )
    log(f"Connected in {time.monotonic() - started:0.1f}s.")
    return env


def _fmt_reward_stats(rewards) -> str:
    if rewards is None:
        return "none"
    try:
        values = [float(r) for r in rewards]
    except Exception:
        return "unreadable"
    if not values:
        return "none"
    total = sum(values)
    min_v = min(values)
    max_v = max(values)
    return f"n={len(values)}, sum={total:0.3f}, min={min_v:0.3f}, max={max_v:0.3f}"


def log_behavior_specs(env: UnityEnvironment) -> None:
    if not env.behavior_specs:
        log("No behavior specs found after reset.")
        return
    for behavior_name, spec in env.behavior_specs.items():
        obs_shapes = []
        try:
            obs_shapes = [tuple(o.shape) for o in spec.observation_specs]
        except Exception:
            obs_shapes = ["<unreadable>"]
        action_desc = "unknown"
        try:
            action_spec = spec.action_spec
            if action_spec.is_discrete():
                action_desc = f"discrete{list(action_spec.discrete_branches)}"
            elif action_spec.is_continuous():
                action_desc = f"continuous[{action_spec.continuous_size}]"
            else:
                action_desc = "empty"
        except Exception:
            pass
        log(
            f"Behavior '{behavior_name}': obs={obs_shapes}, actions={action_desc}"
        )


def log_observer_snapshot(env: UnityEnvironment, step: int, force: bool = False) -> None:
    for behavior_name in env.behavior_specs.keys():
        try:
            decision_steps, terminal_steps = env.get_steps(behavior_name)
        except Exception as ex:
            if force:
                log(f"Observer read failed for '{behavior_name}': {type(ex).__name__}: {ex}")
            continue

        d_count = len(decision_steps)
        t_count = len(terminal_steps)
        d_rewards = getattr(decision_steps, "reward", None)
        t_rewards = getattr(terminal_steps, "reward", None)

        eventful = False
        try:
            if d_rewards is not None:
                eventful = eventful or any(abs(float(r)) > 1e-6 for r in d_rewards)
            if t_rewards is not None:
                eventful = eventful or any(abs(float(r)) > 1e-6 for r in t_rewards)
        except Exception:
            pass
        eventful = eventful or t_count > 0

        if not force and not eventful and (step % OBSERVER_LOG_EVERY_STEPS != 0):
            continue

        log(
            "Observer "
            f"[{behavior_name}] step={step:,} "
            f"decisions={d_count} terminals={t_count} "
            f"dRewards=({_fmt_reward_stats(d_rewards)}) "
            f"tRewards=({_fmt_reward_stats(t_rewards)})"
        )


def main():
    channel = None
    if ENABLE_DIALOGUE_SIDECHANNEL:
        handler = (
            make_mock_handler()
            if USE_MOCK
            else make_lmstudio_handler(
                model=LMS_MODEL,
                host=LMS_HOST,
                port=LMS_PORT,
                api_key=LMS_API_KEY,
            )
        )
        backend = "mock" if USE_MOCK else f"LM Studio @ {LMS_HOST}:{LMS_PORT}"
        log(f"Handler: {backend}")
        log("Custom dialogue sidechannel enabled (for SideChannelOverride testing).")
        channel = LlmDialogueChannel(handler=handler)
    else:
        log("Observer-only mode: custom dialogue sidechannel disabled.")
        log("No LM Studio traffic will be sent by this Python process.")
    env = None

    for attempt in range(1, RESET_MAX_RETRIES + 1):
        try:
            env = create_env(channel)
            log(
                f"Reset attempt {attempt}/{RESET_MAX_RETRIES} "
                f"(Unity timeout={UNITY_TIMEOUT_SECONDS}s)"
            )
            reset_started = time.monotonic()
            env.reset()
            log(f"Reset succeeded in {time.monotonic() - reset_started:0.1f}s.")
            log_behavior_specs(env)
            log_observer_snapshot(env, step=0, force=True)
            break
        except UnityWorkerInUseException:
            log("ERROR: Another process is already connected to Unity on port 5004.")
            log("Close other mlagents-learn sessions or restart Unity.")
            return
        except (UnityTimeOutException, UnityCommunicatorStoppedException) as ex:
            log(f"Reset/connect failed on attempt {attempt}: {type(ex).__name__}: {ex}")
            if env is not None:
                try:
                    env.close()
                except Exception:
                    pass
                env = None
            if attempt >= RESET_MAX_RETRIES:
                log("ERROR: Exhausted reset retries. Exiting.")
                return
            log(f"Retrying in {RESET_RETRY_DELAY_SECONDS}s...")
            time.sleep(RESET_RETRY_DELAY_SECONDS)

    if env is None:
        log("ERROR: Unity environment unavailable after retry loop.")
        return

    log("Running step loop — press Ctrl+C to stop.")

    try:
        for step in range(MAX_STEPS):
            env.step()
            if channel is not None:
                channel.flush_responses()
            log_observer_snapshot(env, step)

            if step % 100 == 0:
                log(f"Step {step:,} — bridge alive")

    except KeyboardInterrupt:
        log("Stopped by user.")
    finally:
        env.close()
        log("Unity environment closed.")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        tb = traceback.format_exc()
        for line in tb.rstrip().splitlines():
            log(line)
        raise
