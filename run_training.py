"""
run_training.py — Unified NpcDialogue training launcher.

Usage:
    python run_training.py [--fresh] [--run-id NAME] [--no-watchdog] [--unity-check]

What this does (and why):
  - Port 5004 cleanup     → kills orphan Python procs that block mlagents-learn on startup
  - Optional Unity readiness gate
                         → warns early if Unity HTTP MCP bridge is reachable and Play mode is not ready
  - Auto-resume           → detects last run_id in results/ and passes --resume automatically
                            (never lose progress again from an unexpected crash)
  - Watchdog              → relaunches mlagents-learn if it exits with a non-zero code
                            passes --resume on every restart, always continuing from checkpoint

Run ALONGSIDE run_llm_bridge.py (separate terminal):
    Terminal 1:  python run_llm_bridge.py        ← LLM dialogue sidechannel
    Terminal 2:  python run_training.py          ← PPO trainer (this file)
    Unity:       press Play with BehaviorType=Default
"""

import argparse
import os
import subprocess
import sys
import time
from datetime import datetime

# ── Paths ─────────────────────────────────────────────────────────────────────
REPO_ROOT   = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(REPO_ROOT, "config", "ppo", "NpcDialogue.yaml")
RESULTS_DIR = os.path.join(REPO_ROOT, "results")
LOG_DIR     = os.path.join(REPO_ROOT, ".codex", "tmp")

# ── Config ────────────────────────────────────────────────────────────────────
TRAINER_PORT             = 5004
UNITY_HEALTH_URL         = "http://localhost:8009/mcp"   # Unity MCP HTTP bridge endpoint (optional)
UNITY_HEALTH_TIMEOUT_S   = 120    # max seconds to wait for Unity to be ready
UNITY_HEALTH_POLL_S      = 3
MAX_WATCHDOG_RESTARTS    = 10     # stop after this many unexpected crashes
WATCHDOG_RESTART_DELAY_S = 5


# ── Logging ───────────────────────────────────────────────────────────────────

def _ts() -> str:
    return datetime.now().strftime("%H:%M:%S")


def log(msg: str) -> None:
    line = f"[{_ts()}] [Trainer] {msg}"
    print(line, flush=True)
    try:
        os.makedirs(LOG_DIR, exist_ok=True)
        log_file = os.path.join(LOG_DIR, "run_training.log")
        with open(log_file, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


# ── Port cleanup ──────────────────────────────────────────────────────────────

def kill_port_holders(port: int) -> None:
    """Kill any Windows processes currently listening on the given TCP port.

    Root cause this solves: after an abrupt trainer exit, the Python process
    sometimes keeps the port open for 30-60 seconds (TIME_WAIT). A subsequent
    mlagents-learn launch immediately fails with UnityWorkerInUseException.
    Cleaning the port before launch eliminates that failure mode entirely.
    """
    try:
        result = subprocess.run(
            ["netstat", "-ano"],
            capture_output=True, text=True, timeout=10,
        )
        pids_to_kill: set[int] = set()
        for line in result.stdout.splitlines():
            # Look for lines like: TCP  0.0.0.0:5004  ...  LISTENING  1234
            if f":{port} " in line and ("LISTENING" in line or "LISTEN" in line):
                parts = line.split()
                if parts:
                    try:
                        pids_to_kill.add(int(parts[-1]))
                    except ValueError:
                        pass

        for pid in pids_to_kill:
            if pid <= 4:          # never kill System, smss, csrss, etc.
                continue
            try:
                subprocess.run(
                    ["taskkill", "/F", "/PID", str(pid)],
                    capture_output=True, timeout=5,
                )
                log(f"Killed PID {pid} (was holding port {port})")
            except Exception as ex:
                log(f"Could not kill PID {pid}: {ex}")

        if not pids_to_kill:
            log(f"Port {port} is free.")

    except Exception as ex:
        log(f"Port cleanup skipped ({type(ex).__name__}: {ex})")


# ── Unity readiness gate ──────────────────────────────────────────────────────

def wait_for_unity(timeout_s: int = UNITY_HEALTH_TIMEOUT_S) -> bool:
    """Poll the Unity MCP health endpoint until it responds or the timeout expires.

    Why: mlagents-learn's own connection timeout is a long silent wait with
    unhelpful output. By checking the HTTP endpoint first we can give the user
    a clear "please press Play" message immediately, instead of the trainer
    silently hanging for minutes.

    Returns True when Unity is ready, False if timeout expired (caller continues anyway).
    """
    import urllib.request
    import urllib.error

    deadline = time.monotonic() + timeout_s
    log(f"Checking Unity readiness at {UNITY_HEALTH_URL} (timeout={timeout_s}s)…")
    log("  → Make sure the DevProject is open and you have pressed Play in Unity.")

    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(UNITY_HEALTH_URL, timeout=3) as resp:
                if resp.status < 400:
                    log(f"Unity bridge responded (HTTP {resp.status}). Proceeding.")
                    return True
        except urllib.error.HTTPError as ex:
            if ex.code < 500:
                # Any non-server-error means something is listening — good enough.
                log(f"Unity bridge responded (HTTP {ex.code}). Proceeding.")
                return True
        except Exception:
            pass
        time.sleep(UNITY_HEALTH_POLL_S)

    log(
        "WARNING: Unity did not respond within timeout.\n"
        "  → Continuing anyway — mlagents-learn will retry its own connection.\n"
        "  → If training fails immediately, check that Unity is in Play mode."
    )
    return False


# ── Run-id detection ──────────────────────────────────────────────────────────

def detect_last_run_id() -> str | None:
    """Scan results/ and return the run_id whose checkpoint file was most recently written.

    This means we always resume the run that was most recently active, even if
    you have multiple run_ids under results/. Returns None if results/ is empty
    or doesn't exist.
    """
    if not os.path.isdir(RESULTS_DIR):
        return None

    best_run_id: str | None = None
    best_mtime: float = -1.0

    for entry in os.scandir(RESULTS_DIR):
        if not entry.is_dir():
            continue
        # Walk the run_id directory looking for .pt checkpoint files.
        for dirpath, _dirs, filenames in os.walk(entry.path):
            for fname in filenames:
                if fname.endswith(".pt"):
                    fpath = os.path.join(dirpath, fname)
                    mtime = os.path.getmtime(fpath)
                    if mtime > best_mtime:
                        best_mtime = mtime
                        best_run_id = entry.name

    return best_run_id


def make_fresh_run_id() -> str:
    return f"NpcDialogue_{datetime.now().strftime('%d%m%y%a_%H%M%S')}"


# ── Trainer invocation ────────────────────────────────────────────────────────

def build_trainer_command(run_id: str, resume: bool) -> list[str]:
    """Build the mlagents-learn command list.

    Uses sys.executable (-m mlagents.trainers.learn) so the correct conda
    Python binary is always used regardless of PATH configuration.
    """
    cmd = [
        sys.executable, "-m", "mlagents.trainers.learn",
        CONFIG_PATH,
        "--run-id", run_id,
        "--results-dir", RESULTS_DIR,
        "--torch-device", "cuda",   # use GPU; falls back to CPU if CUDA unavailable
        "--time-scale", "1",        # real-time; required when LLM calls have real latency
    ]
    if resume:
        cmd.append("--resume")
    return cmd


def run_trainer_once(run_id: str, resume: bool) -> int:
    """Launch mlagents-learn and block until it exits. Returns the exit code."""
    cmd = build_trainer_command(run_id, resume)
    log(f"Launch: {' '.join(cmd)}")

    # Add local ml-agents source to PYTHONPATH so any repo-local trainer
    # customisations take priority over the installed conda package.
    env = os.environ.copy()
    local_paths = os.pathsep.join([
        os.path.join(REPO_ROOT, "ml-agents"),
        os.path.join(REPO_ROOT, "ml-agents-envs"),
    ])
    env["PYTHONPATH"] = local_paths + os.pathsep + env.get("PYTHONPATH", "")

    proc = subprocess.run(cmd, env=env)
    return proc.returncode


# ── Main ──────────────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(
        description="NpcDialogue unified training launcher (wraps mlagents-learn)"
    )
    parser.add_argument(
        "--fresh", action="store_true",
        help="Start a brand-new run instead of resuming (ignores any existing checkpoint).",
    )
    parser.add_argument(
        "--run-id", default=None, metavar="NAME",
        help="Explicit run_id to use. Implies --fresh unless --resume is also passed.",
    )
    parser.add_argument(
        "--resume", action="store_true",
        help="Force resume even when --run-id is specified (useful for explicit resume).",
    )
    parser.add_argument(
        "--no-watchdog", action="store_true",
        help="Run mlagents-learn exactly once (no restart on crash).",
    )
    parser.set_defaults(skip_unity_check=True)
    unity_check_group = parser.add_mutually_exclusive_group()
    unity_check_group.add_argument(
        "--unity-check",
        dest="skip_unity_check",
        action="store_false",
        help="Enable the Unity readiness HTTP poll against the MCP HTTP bridge (only use when localhost:8009/mcp is available).",
    )
    unity_check_group.add_argument(
        "--skip-unity-check",
        dest="skip_unity_check",
        action="store_true",
        help=argparse.SUPPRESS,
    )
    args = parser.parse_args()

    # 1 ── Port cleanup
    log(f"Cleaning up port {TRAINER_PORT}…")
    kill_port_holders(TRAINER_PORT)

    # 2 ── Unity readiness gate
    if args.skip_unity_check:
        log(
            "Skipping Unity readiness HTTP poll (disabled by default; pass --unity-check to enable it)."
        )
    else:
        wait_for_unity()

    # 3 ── Decide run_id + resume flag
    if args.run_id:
        run_id = args.run_id
        resume = args.resume        # explicit resume only if flag given alongside --run-id
        if not resume:
            log(f"Explicit run_id '{run_id}' (fresh start unless --resume passed).")
        else:
            log(f"Explicit run_id '{run_id}' with --resume.")
    elif args.fresh:
        run_id = make_fresh_run_id()
        resume = False
        log(f"--fresh: starting new run '{run_id}'.")
    else:
        last = detect_last_run_id()
        if last:
            run_id = last
            resume = True
            log(f"Auto-detected last run: '{run_id}' — resuming from checkpoint.")
        else:
            run_id = make_fresh_run_id()
            resume = False
            log(f"No previous checkpoints found. Starting fresh: '{run_id}'.")

    # 4 ── Trainer loop (with optional watchdog)
    restarts = 0
    while True:
        start_time = time.monotonic()
        exit_code = run_trainer_once(run_id, resume)
        elapsed = time.monotonic() - start_time
        log(f"mlagents-learn exited with code {exit_code} after {elapsed:.0f}s.")

        if args.no_watchdog:
            break

        if exit_code == 0:
            log("Training finished cleanly.")
            break

        if restarts >= MAX_WATCHDOG_RESTARTS:
            log(f"Reached restart limit ({MAX_WATCHDOG_RESTARTS}). Exiting.")
            break

        restarts += 1
        log(
            f"Unexpected exit (code={exit_code}). "
            f"Restart {restarts}/{MAX_WATCHDOG_RESTARTS} in {WATCHDOG_RESTART_DELAY_S}s…"
        )
        # Always clean the port before retrying — the crashed process may still hold it.
        kill_port_holders(TRAINER_PORT)
        time.sleep(WATCHDOG_RESTART_DELAY_S)
        resume = True   # every restart continues from the last checkpoint


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        log("Stopped by user (Ctrl+C).")
    except Exception as ex:
        import traceback
        for line in traceback.format_exc().rstrip().splitlines():
            log(line)
        raise
