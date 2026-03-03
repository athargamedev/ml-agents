"""
dev_tools/verify.py — Confidence check for the entire dev-tools + training pipeline.

Runs fast static checks (no LM Studio) and optional live model checks.
Exits non-zero if any check fails, so it can be used in CI or pre-run gates.

Usage:
    python dev_tools/run_dev_tools.py verify            # static only (fast, no LM Studio)
    python dev_tools/run_dev_tools.py verify --live     # + model behaviour tests (~3 min)
"""

import json
import re
import sys
from pathlib import Path
from typing import Callable

REPO_ROOT = Path(__file__).parent.parent.resolve()
DEV_TOOLS  = REPO_ROOT / "dev_tools"
SCENE_FILE = REPO_ROOT / "DevProject" / "Assets" / "Network_Game" / "Scene" / "Behavior_Scene.unity"
BRIDGE_FILE = REPO_ROOT / "run_llm_bridge.py"

_PASS = "  PASS"
_FAIL = "  FAIL"
_SKIP = "  SKIP"

# ── helpers ───────────────────────────────────────────────────────────────────

def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""


class Results:
    def __init__(self) -> None:
        self._results: list[tuple[str, bool, str]] = []

    def check(self, name: str, passed: bool, detail: str = "") -> bool:
        self._results.append((name, passed, detail))
        status = _PASS if passed else _FAIL
        suffix = f" — {detail}" if detail else ""
        print(f"{status}  {name}{suffix}", flush=True)
        return passed

    def summary(self) -> bool:
        total  = len(self._results)
        failed = [n for n, ok, _ in self._results if not ok]
        print()
        if failed:
            print(f"[verify] {len(failed)}/{total} checks FAILED:")
            for n in failed:
                print(f"  ✗ {n}")
            return False
        print(f"[verify] All {total} checks passed.")
        return True


# ── static checks (no LM Studio) ─────────────────────────────────────────────

def check_model_routing(r: Results) -> None:
    """Verify each tool imports and uses the expected model constant."""
    print("\n── Model routing ──────────────────────────────────────────")

    # lm_client.py must define all four constants
    lm = _read(DEV_TOOLS / "lm_client.py")
    for const in ("CODE_MODEL", "TEXT_MODEL", "FAST_MODEL", "DOCS_MODEL"):
        r.check(f"lm_client defines {const}", f'{const} = "' in lm)

    # code_scanner must import DOCS_MODEL and NOT ask_schema with TEXT_MODEL
    cs = _read(DEV_TOOLS / "code_scanner.py")
    r.check("code_scanner imports DOCS_MODEL",        "DOCS_MODEL" in cs)
    # Check only actual code (import/model= lines), not comments
    cs_code_lines = [l for l in cs.splitlines() if not l.lstrip().startswith("#")]
    cs_code = "\n".join(cs_code_lines)
    r.check("code_scanner does not use TEXT_MODEL in code",
            "TEXT_MODEL" not in cs_code)
    r.check("code_scanner does not use CODE_MODEL",   "CODE_MODEL" not in cs)
    r.check("code_scanner uses ask_schema",           "ask_schema" in cs)

    # docs_scanner uses DOCS_MODEL for core and TEXT_MODEL for tools batch
    ds = _read(DEV_TOOLS / "docs_scanner.py")
    r.check("docs_scanner imports DOCS_MODEL",        "DOCS_MODEL" in ds)
    r.check("docs_scanner imports TEXT_MODEL",        "TEXT_MODEL" in ds)

    # knowledge_updater must use DOCS_MODEL (llama/TEXT_MODEL hallucinates and corrupts KB)
    ku = _read(DEV_TOOLS / "knowledge_updater.py")
    ku_code_lines = [l for l in ku.splitlines() if not l.lstrip().startswith("#")]
    ku_code = "\n".join(ku_code_lines)
    r.check("knowledge_updater imports DOCS_MODEL",       "DOCS_MODEL" in ku)
    r.check("knowledge_updater does not use TEXT_MODEL",  "TEXT_MODEL" not in ku_code,
            "TEXT_MODEL hallucinates in KB synthesis — must use DOCS_MODEL")
    r.check("knowledge_updater uses ask_schema",          "ask_schema" in ku)
    # Slug must use replace(':','-') not replace(':','') — otherwise writes to wrong directory
    r.check("knowledge_updater memory slug uses replace(':','-')",
            "replace(\":\", \"-\")" in ku,
            "replace(':','') produces D-GithubRepos slug (wrong); need D--GithubRepos")

    # run_llm_bridge.py must have an explicit model (not empty string)
    bridge = _read(BRIDGE_FILE)
    empty_model = re.search(r'LMS_MODEL\s*=\s*""', bridge)
    r.check("run_llm_bridge has explicit LMS_MODEL",  empty_model is None,
            "(empty string = routes to whatever LM Studio has active)" if empty_model else "")
    model_match = re.search(r'LMS_MODEL\s*=\s*"([^"]+)"', bridge)
    if model_match:
        r.check("run_llm_bridge LMS_MODEL is qwen3-8b", model_match.group(1) == "qwen3-8b",
                f"got '{model_match.group(1)}'")


def check_unity_scene(r: Results) -> None:
    """Verify key Inspector values are saved in the scene file."""
    print("\n── Unity scene values ─────────────────────────────────────")

    if not SCENE_FILE.exists():
        r.check("Behavior_Scene.unity exists", False, str(SCENE_FILE))
        return

    scene = _read(SCENE_FILE)

    def find_value(key: str) -> str | None:
        m = re.search(rf"{re.escape(key)}:\s*(\S+)", scene)
        return m.group(1) if m else None

    dist = find_value("m_MaxInteractionDistance")
    r.check("m_MaxInteractionDistance = 25",
            dist is not None and float(dist) == 25.0, f"got {dist}")

    scale = find_value("m_FeedbackScoreRewardScale")
    r.check("m_FeedbackScoreRewardScale ≈ 0.1",
            scale is not None and abs(float(scale) - 0.1) < 0.01, f"got {scale}")

    remote_model = find_value("m_RemoteModelName")
    r.check("m_RemoteModelName = qwen3-8b",
            remote_model == "qwen3-8b", f"got {remote_model}")

    max_step = find_value("MaxStep")
    r.check("MaxStep = 1000",
            max_step is not None and int(max_step) == 1000, f"got {max_step}")


def check_error_surfacing(r: Results) -> None:
    """Verify the report writer surfaces parse errors (not silently hides them)."""
    print("\n── Report error surfacing ─────────────────────────────────")
    cs = _read(DEV_TOOLS / "code_scanner.py")
    r.check("report writer has Scan Errors section",
            "Scan Errors" in cs)
    r.check("error field checked before clean files",
            "r.error" in cs)


def check_memory_health(r: Results) -> None:
    """Verify the Claude memory directory exists at the expected path."""
    print("\n── Memory directory ────────────────────────────────────────")
    mem_dir = Path.home() / ".claude" / "projects" / "D--GithubRepos-ml-agents" / "memory"
    r.check("memory dir exists (D--GithubRepos)", mem_dir.exists(), str(mem_dir))
    r.check("MEMORY.md exists", (mem_dir / "MEMORY.md").exists())
    r.check("project_patterns.md exists", (mem_dir / "project_patterns.md").exists())
    r.check("code_quality.md exists", (mem_dir / "code_quality.md").exists())
    # Detect the ghost directory from the old slug bug
    ghost = Path.home() / ".claude" / "projects" / "D-GithubRepos-ml-agents" / "memory"
    r.check("ghost dir D-GithubRepos absent", not ghost.exists(),
            "Ghost directory found — KB writes are going to the wrong place!" if ghost.exists() else "")


def check_package_context(r: Results) -> None:
    """Verify the package context provider loads and has meaningful content."""
    print("\n── Package context ─────────────────────────────────────────")
    sys.path.insert(0, str(REPO_ROOT))
    from dev_tools.package_context import PackageContextProvider, _latest_package_docs
    docs_path = _latest_package_docs()
    r.check("package docs file exists", docs_path is not None,
            "Run: python dev_tools/run_dev_tools.py scan-docs")
    if docs_path:
        r.check("package docs non-empty", docs_path.stat().st_size > 500)
        p = PackageContextProvider()
        p._load()
        r.check("package context loads ≥2 packages", len(p._docs) >= 2,
                f"got {len(p._docs)}")
        r.check("NGO docs present", "com.unity.netcode.gameobjects" in p._docs)


# ── live model checks (LM Studio required) ────────────────────────────────────

def check_lm_studio_models(r: Results) -> None:
    """Verify expected models are actually loaded in LM Studio RAM."""
    print("\n── LM Studio loaded models ────────────────────────────────")
    try:
        import urllib.request, json as _json
        req = urllib.request.Request(
            "http://127.0.0.1:7002/api/v0/models",
            headers={"Authorization": "Bearer sk-lm-Li2oVsHm:wNxCcCTjZM4PFuNC0RnH"},
        )
        with urllib.request.urlopen(req, timeout=5) as resp:
            data = _json.loads(resp.read())
    except Exception as ex:
        r.check("LM Studio reachable", False, str(ex))
        return

    r.check("LM Studio reachable", True)

    loaded = {m["id"] for m in data.get("data", []) if m.get("state") == "loaded"}
    for model_id in ("qwen3-8b", "llama-3.2-3b-instruct@q8_0"):
        r.check(f"model loaded: {model_id}", model_id in loaded,
                "(not in RAM — will need to load on first use)" if model_id not in loaded else "")


def check_scan_behaviour(r: Results) -> None:
    """
    Behavioural correctness tests for the code scanner:
      1. Known-clean file  → 0 issues, no parse error   (tests: no hallucination)
      2. Known-dirty file  → ≥1 issue, no parse error   (tests: real findings returned)
    """
    print("\n── Scanner behaviour (LLM calls, ~3 min) ──────────────────")
    sys.path.insert(0, str(REPO_ROOT))
    from dev_tools.lm_client import LmClient
    from dev_tools.code_scanner import CodeScanner

    client = LmClient()
    if not client.is_available():
        r.check("LM Studio available for scanner test", False)
        return
    r.check("LM Studio available for scanner test", True)

    scanner = CodeScanner(client)

    # Test 1: pure data-only marker — no RPCs, no ML-Agents, no patterns
    clean_file = (REPO_ROOT /
        "DevProject/Assets/Network_Game/Dialogue/MCP/DialogueSpawnedMarker.cs")
    result_clean = scanner.scan_file(clean_file)
    r.check("clean file: no parse error",    result_clean.error is None,
            result_clean.error or "")
    r.check("clean file: 0 issues (no hallucination)",
            result_clean.error is None and len(result_clean.issues) == 0,
            f"{len(result_clean.issues)} issue(s) found")

    # Test 2: file with known real issues (documented NPC001 pattern)
    dirty_file = (REPO_ROOT /
        "DevProject/Assets/Network_Game/Dialogue/DialogueEffectFeedbackPrompt.cs")
    result_dirty = scanner.scan_file(dirty_file)
    r.check("dirty file: no parse error",    result_dirty.error is None,
            result_dirty.error or "")
    r.check("dirty file: ≥1 real issue found",
            result_dirty.error is None and len(result_dirty.issues) >= 1,
            f"{len(result_dirty.issues)} issue(s) found")


# ── entry point ───────────────────────────────────────────────────────────────

def run_verify_cli(live: bool = False) -> bool:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")

    r = Results()

    print("[verify] Running static checks...")
    check_model_routing(r)
    check_unity_scene(r)
    check_error_surfacing(r)
    check_memory_health(r)
    check_package_context(r)

    if live:
        print("\n[verify] Running live model checks (requires LM Studio)...")
        check_lm_studio_models(r)
        check_scan_behaviour(r)
    else:
        print("\n[verify] Skipping live checks. Run with --live to test model behaviour.")

    return r.summary()
