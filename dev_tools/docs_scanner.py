"""
dev_tools/docs_scanner.py — Package documentation assistant.

Reads manifest.json, cross-references with assembly_map.json to identify
which packages are actively referenced in project assemblies, then generates
concise project-specific documentation for each core package via LM Studio.

Model routing:
  TEXT_MODEL (llama-3.2-3b@q8_0) via ask() — Anthropic endpoint, KV-cached,
  ~5s per call. Use for all docs generation.
  NOTE: CODE_MODEL (qwen2.5-coder-7b) is NOT used — LM Studio routes all
  OpenAI and Anthropic calls to the single active server model, making
  per-call model routing unreliable. Always target TEXT_MODEL explicitly.

Two tiers:
  CORE  — full per-package markdown: purpose, key APIs, project usage, gotchas
  TOOLS — batched one-liner overview (single LLM call)

Output files:
  dev_tools/reports/package_docs_YYYY-MM-DD.md  — full report
  dev_tools/reports/latest_package_docs.md       — condensed (~200 lines) for Claude memory

Usage (via run_dev_tools.py):
    python dev_tools/run_dev_tools.py scan-docs
    python dev_tools/run_dev_tools.py scan-docs --package com.unity.netcode.gameobjects
"""

from __future__ import annotations

import dataclasses
import os
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime
from pathlib import Path
from threading import Lock
from typing import Optional
import json

from dev_tools.lm_client import LmClient, TEXT_MODEL, LLAMA_STOP, DOCS_MODEL, QWEN_STOP

# Qwen3: prepend to the USER message (not system) to skip chain-of-thought block.
# Without this, qwen3 emits a <think>…</think> preamble that wastes tokens.
_NO_THINK = "/no_think\n"
_PRINT_LOCK = Lock()  # serialise progress lines from parallel workers

# ── Paths ──────────────────────────────────────────────────────────────────────
REPO_ROOT     = Path(__file__).parent.parent.resolve()
MANIFEST_PATH = REPO_ROOT / "DevProject" / "Packages" / "manifest.json"
ASSEMBLY_MAP  = Path(__file__).parent / "schemas" / "assembly_map.json"
PROMPT_FILE   = Path(__file__).parent / "prompts" / "package_docs.txt"
REPORTS_DIR   = Path(__file__).parent / "reports"


def _default_memory_dir() -> str:
    repo_path = str(REPO_ROOT)
    if len(repo_path) >= 2 and repo_path[1] == ":":
        repo_path = repo_path[0].upper() + repo_path[1:]
    slug = repo_path.replace(":", "-").replace("\\", "-").replace("/", "-")
    return str(Path.home() / ".claude" / "projects" / slug / "memory")


MEMORY_DIR    = Path(os.environ.get("CLAUDE_MEMORY_DIR", _default_memory_dir()))
# ──────────────────────────────────────────────────────────────────────────────

# ── Package tiers ──────────────────────────────────────────────────────────────
# CORE: significant C# surface area — one full LLM call each
CORE_PACKAGES: dict[str, str] = {
    "com.unity.netcode.gameobjects":        "Multiplayer backbone — NetworkBehaviour, NetworkObject, NetworkVariable, ClientRpc, ServerRpc",
    "com.unity.ml-agents":                  "RL training — Agent, SideChannel, CollectObservations, AddReward, EndEpisode",
    "com.unity.inputsystem":                "Input — PlayerInput, InputAction, Keyboard.current, StarterAssetsInputs cursor management",
    "com.unity.ai.navigation":              "NavMesh runtime — NavMeshAgent, NavMeshSurface, async baking",
    "com.unity.visualeffectgraph":          "VFX Graph — VisualEffect, SetFloat/SetVector, OutputEvent, EffectDispatcher integration",
    "com.unity.render-pipelines.universal": "URP rendering — UniversalRenderPipelineAsset, camera stack, shader compatibility",
    "com.unity.cinemachine":                "Camera system — CinemachineCamera, CinemachineBrain, priority blending, CinemachineAutoFocus",
    "com.unity.transport":                  "Low-level networking — used internally by NGO; also used for custom SideChannel transport",
    "com.unity.addressables":               "Asset loading — AsyncOperationHandle, LoadAssetAsync, release patterns",
    "com.unity.ai.inference":               "ONNX model inference — Barracuda successor, runs .onnx Walker/Agent models in Unity 6",
    "com.unity.multiplayer.tools":          "Network diagnostics — Runtime Net Stats Monitor, NetworkSimulator for lag testing",
    "com.unity.physics":                    "DOTS physics — PhysicsBody, PhysicsShape, ICollisionEventsJob",
    "com.unity.entities.graphics":          "DOTS hybrid rendering — GPU instancing for static meshes, RenderMeshArray",
    "com.unity.modules.uielements":         "UI Toolkit — UIDocument, VisualElement, USS, UxmlElement, ModernHudController stack",
}

# TOOLS: editor/test tools — batched into a single call
TOOL_PACKAGES: dict[str, str] = {
    "com.unity.probuilder":                   "Level geometry prototyping",
    "com.unity.recorder":                     "Video/image capture for training session recording",
    "com.unity.terrain-tools":                "Terrain sculpting and painting",
    "com.unity.performance.profile-analyzer": "Frame time comparison between builds",
    "com.unity.multiplayer.playmode":         "Multi-instance editor testing for NGO multiplayer",
    "com.unity.test-framework":               "Unity Test Runner — NpcDialogueAgentPlayModeTests",
    "com.unity.testtools.codecoverage":       "Test coverage reporting",
    "com.unity.nuget.newtonsoft-json":        "JSON.NET — available as fallback to JsonUtility",
    "com.unity.formats.fbx":                  "FBX import/export for Mixamo animations",
}
# ──────────────────────────────────────────────────────────────────────────────

_PROJECT_CONTEXT = """\
Project: Unity 6 (6000.4.x) multiplayer RPG — URP 17.4, Windows
Systems: NetworkDialogueService, NpcDialogueAgent (ML-Agents, 7 observations),
         EffectDispatcher (VFX), ModernHudController (UI Toolkit), StarterAssetsInputs
Packages in use: NGO 2.9.2, ML-Agents local, InputSystem 1.18, Cinemachine 3.1.6,
                 VFX Graph 17.4, AI.Inference 2.5, AI.Navigation 2.0.11"""


@dataclasses.dataclass
class PackageDoc:
    pkg_id:     str
    version:    str
    content:    str        # raw markdown from LLM
    duration_s: float = 0.0
    error:      Optional[str] = None


@dataclasses.dataclass
class ToolDoc:
    pkg_id:  str
    version: str
    content: str           # raw one-liner markdown


def _load_manifest() -> dict[str, str]:
    if not MANIFEST_PATH.exists():
        print(f"[DocScan] WARNING: manifest.json not found at {MANIFEST_PATH}")
        return {}
    data = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    return data.get("dependencies", {})


def _load_system_prompt() -> str:
    if PROMPT_FILE.exists():
        return PROMPT_FILE.read_text(encoding="utf-8")
    return (
        "You are a Unity package documentation specialist. "
        "Write concise, project-specific documentation for the given Unity package. "
        "Cover: purpose, key APIs used in this project, project-specific usage, gotchas."
    )



def _build_package_user_msg(pkg_id: str, version: str, hint: str) -> str:
    return (
        f"Document this package for the project described in the system prompt.\n\n"
        f"Package: {pkg_id}\n"
        f"Version: {version}\n"
        f"Usage hint: {hint}\n"
    )


def _build_tools_user_msg(tool_pkgs: dict[str, tuple[str, str]]) -> str:
    lines = [
        "Write a brief one-liner summary and one Unity 6 gotcha for each tool package listed below.",
        "Format each entry as a markdown bullet: `package-id` vX.Y — <summary>. Gotcha: <gotcha>",
        "",
    ]
    for pid, (ver, hint) in tool_pkgs.items():
        lines.append(f"- {pid} v{ver}: {hint}")
    return "\n".join(lines)


class DocScanner:
    def __init__(self, client: Optional[LmClient] = None):
        self._client     = client or LmClient()
        self._sys_prompt = _load_system_prompt()

    def scan_package(self, pkg_id: str, version: str, hint: str) -> PackageDoc:
        t0   = time.monotonic()
        user = _NO_THINK + _build_package_user_msg(pkg_id, version, hint)

        content = self._client.ask(
            system=self._sys_prompt,
            user=user,
            model=DOCS_MODEL,
            max_tokens=700,
            profile="analysis",
            stop_sequences=QWEN_STOP,
        )
        duration = time.monotonic() - t0

        if not content:
            return PackageDoc(
                pkg_id=pkg_id, version=version, content="",
                duration_s=duration, error="Empty response from LLM",
            )
        return PackageDoc(pkg_id=pkg_id, version=version, content=content, duration_s=duration)

    def scan_tools_batch(self, tool_pkgs: dict[str, tuple[str, str]]) -> list[ToolDoc]:
        if not tool_pkgs:
            return []
        user    = _build_tools_user_msg(tool_pkgs)
        content = self._client.ask(
            system=self._sys_prompt,
            user=user,
            model=TEXT_MODEL,
            max_tokens=800,
            profile="analysis",
            stop_sequences=LLAMA_STOP,
        )
        # Each line is a tool doc — just store the whole block as one ToolDoc
        return [
            ToolDoc(pkg_id=pid, version=ver, content=content)
            for pid, (ver, _) in list(tool_pkgs.items())[:1]  # store once, keyed to first pkg
        ] if content else []

    def scan_all(
        self,
        target_pkg: Optional[str] = None,
        manifest:   Optional[dict[str, str]] = None,
    ) -> tuple[list[PackageDoc], str]:
        """
        Returns (core_docs, tools_content).
        tools_content is raw markdown from the batch call.
        """
        manifest = manifest or _load_manifest()

        if target_pkg:
            version = manifest.get(target_pkg, "unknown")
            hint    = CORE_PACKAGES.get(target_pkg) or TOOL_PACKAGES.get(target_pkg, "")
            print(f"[DocScan] Scanning: {target_pkg} v{version}")
            doc = self.scan_package(target_pkg, version, hint)
            return [doc], ""

        core_packages = [
            (pkg_id, hint)
            for pkg_id, hint in CORE_PACKAGES.items()
            if pkg_id in manifest
        ]
        tool_packages = {
            pkg_id: hint
            for pkg_id, hint in TOOL_PACKAGES.items()
            if pkg_id in manifest
        }
        if not core_packages:
            core_packages = list(CORE_PACKAGES.items())
        if not tool_packages:
            tool_packages = dict(TOOL_PACKAGES)

        total = len(core_packages)
        core_docs: list[Optional[PackageDoc]] = [None] * total

        def _scan_one(idx: int, pkg_id: str, hint: str) -> None:
            version = manifest.get(pkg_id, "unknown")
            with _PRINT_LOCK:
                print(f"[DocScan] [{idx+1:>2}/{total}] {pkg_id} v{version} ...", flush=True)
            doc = self.scan_package(pkg_id, version, hint)
            core_docs[idx] = doc
            status = f"{doc.duration_s:.1f}s" if not doc.error else f"ERROR: {doc.error[:40]}"
            with _PRINT_LOCK:
                print(f"[DocScan]  ✓ {pkg_id} — {status}", flush=True)

        with ThreadPoolExecutor(max_workers=2) as pool:
            futs = [
                pool.submit(_scan_one, i, pkg_id, hint)
                for i, (pkg_id, hint) in enumerate(core_packages)
            ]
            for f in as_completed(futs):
                f.result()  # re-raise any worker exception

        print(f"\n[DocScan] Tool packages batch ({len(tool_packages)} packages)  "
              f"[{TEXT_MODEL}]...")
        tool_pkgs = {
            pid: (manifest.get(pid, "unknown"), hint)
            for pid, hint in tool_packages.items()
        }
        t0 = time.monotonic()
        tool_user     = _build_tools_user_msg(tool_pkgs)
        tools_content = self._client.ask(
            system=self._sys_prompt,
            user=tool_user,
            model=TEXT_MODEL,
            max_tokens=800,
            profile="analysis",
            stop_sequences=LLAMA_STOP,
        )
        print(f"[DocScan] Tools batch done ({time.monotonic()-t0:.1f}s)")

        return [d for d in core_docs if d is not None], tools_content

    def write_report(
        self,
        core_docs:     list[PackageDoc],
        tools_content: str,
    ) -> tuple[Path, Path]:
        REPORTS_DIR.mkdir(parents=True, exist_ok=True)
        date_str     = datetime.now().strftime("%Y-%m-%d")
        report_path  = REPORTS_DIR / f"package_docs_{date_str}.md"
        summary_path = REPORTS_DIR / "latest_package_docs.md"

        # ── Full report ────────────────────────────────────────────────────────
        lines = [
            f"# Package Docs — {datetime.now().strftime('%Y-%m-%d %H:%M')}",
            "",
            f"Documented {sum(1 for d in core_docs if not d.error)} core packages"
            f" + tool packages batch.",
            f"Models: core={DOCS_MODEL} / tools={TEXT_MODEL}",
            "",
            "## Core Packages",
            "",
        ]
        for doc in core_docs:
            lines.append(f"---")
            lines.append(f"### `{doc.pkg_id}` v{doc.version}  _{doc.duration_s:.1f}s_")
            if doc.error:
                lines.append(f"_Error: {doc.error}_")
            else:
                lines.append(doc.content)
            lines.append("")

        if tools_content:
            lines += ["---", "## Tool Packages (batch)", "", tools_content, ""]

        report_path.write_text("\n".join(lines), encoding="utf-8")

        # ── Condensed memory summary ───────────────────────────────────────────
        # First ~3 sections per package (up to 600 chars), then a tools footer
        summary_lines = [
            f"# Package Docs Summary ({date_str})",
            f"Generated by docs_scanner using {DOCS_MODEL} (core) / {TEXT_MODEL} (tools).",
            "",
        ]
        for doc in core_docs:
            if doc.error or not doc.content:
                continue
            summary_lines.append(f"## `{doc.pkg_id}` v{doc.version}")
            # Trim to first 500 chars so summary stays ≤ 200 lines total
            trimmed = doc.content[:500]
            if len(doc.content) > 500:
                trimmed += "\n_[trimmed — see full report]_"
            summary_lines.append(trimmed)
            summary_lines.append("")

        if tools_content:
            summary_lines += ["## Tool Packages", "", tools_content[:800], ""]

        summary_lines.append(f"Full report: `dev_tools/reports/package_docs_{date_str}.md`")
        summary_path.write_text("\n".join(summary_lines), encoding="utf-8")

        _update_memory_index(summary_path)
        return report_path, summary_path


def _update_memory_index(summary_path: Path) -> None:
    memory_md = MEMORY_DIR / "MEMORY.md"
    if not memory_md.exists() or not summary_path.exists():
        return
    content = memory_md.read_text(encoding="utf-8")
    if "package_docs" in content:
        return
    entry        = "- `package_docs.md` — LLM-generated docs for all project packages with gotchas"
    insert_after = "See detailed notes in topic files below:"
    if insert_after in content:
        pos     = content.index(insert_after) + len(insert_after) + 1
        content = content[:pos] + entry + "\n" + content[pos:]
        memory_md.write_text(content, encoding="utf-8")
        print("[DocScan] Updated MEMORY.md index.")


def run_docs_cli(package_arg: Optional[str] = None) -> bool:
    client = LmClient()
    if not client.is_available():
        print("[DocScan] ERROR: LM Studio not reachable. Start LM Studio and load a model first.")
        return False

    if not client.ensure_models_loaded([DOCS_MODEL, TEXT_MODEL], context_length=4096):
        print(f"[DocScan] ERROR: Could not load required models.")
        return False

    manifest = _load_manifest()
    if not manifest:
        print("[DocScan] ERROR: Could not load manifest.json.")
        return False

    print(f"[DocScan] Manifest: {len(manifest)} packages  "
          f"|  Core: {DOCS_MODEL}  |  Tools: {TEXT_MODEL}")
    scanner = DocScanner(client)
    t0      = time.monotonic()

    core_docs, tools_content = scanner.scan_all(
        target_pkg=package_arg,
        manifest=manifest,
    )

    report_path, summary_path = scanner.write_report(core_docs, tools_content)
    total     = time.monotonic() - t0
    ok_count  = sum(1 for d in core_docs if not d.error)
    err_count = sum(1 for d in core_docs if d.error)

    print(f"\n[DocScan] Done in {total:.0f}s — {ok_count} OK, {err_count} errors")
    print(f"[DocScan] Report:  {report_path}")
    print(f"[DocScan] Summary: {summary_path}")
    return err_count == 0
