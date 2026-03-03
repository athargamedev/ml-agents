"""
dev_tools/package_context.py — Injects relevant package API docs into scan prompts.

For each C# file being scanned, resolves which Unity packages it uses via two
complementary signals, then returns condensed Key APIs + Gotchas sections from
the generated package docs.

This context is prepended to the qwen3-8b scan prompt so the model reviews code
against current Unity 6 API docs rather than relying on potentially-outdated
training data.

Two resolution signals (combined, deduplicated):
  1. `using` directives in the file itself  → file-level, precise
  2. .asmdef references for the file's assembly → catches implicit package usage

Typical injected size: 300–800 chars (1–3 packages).
Hard cap: MAX_CONTEXT_CHARS to stay within LM Studio context budget.
"""

import json
import re
from pathlib import Path
from typing import Optional

REPO_ROOT   = Path(__file__).parent.parent.resolve()
REPORTS_DIR = Path(__file__).parent / "reports"
ASM_MAP     = Path(__file__).parent / "schemas" / "assembly_map.json"

MAX_CONTEXT_CHARS = 2000  # ~500 tokens — room for 2-3 package summaries

# ── Namespace prefix → package ID (for using-directive fast path) ─────────────
# Only covers packages for which we generate docs.  Add entries here when
# docs_scanner.py is extended to cover more packages.
_NS_TO_PKG: dict[str, str] = {
    "Unity.Netcode":              "com.unity.netcode.gameobjects",
    "Unity.Networking.Transport": "com.unity.transport",
    "Cinemachine":                "com.unity.cinemachine",
    "Unity.Multiplayer.Tools":    "com.unity.multiplayer.tools",
}


# ── Doc file resolution ───────────────────────────────────────────────────────

def _latest_package_docs() -> Optional[Path]:
    """Return the most recent dated package_docs_YYYY-MM-DD.md file, or None."""
    candidates = sorted(REPORTS_DIR.glob("package_docs_????-??-??.md"), reverse=True)
    return candidates[0] if candidates else None


# ── Doc parsing ───────────────────────────────────────────────────────────────

def _parse_docs(path: Path) -> dict[str, str]:
    """
    Parse a package_docs_*.md file into {package_id: condensed_section}.
    Sections are split on ### `com.xxx` headers; each section is condensed
    to Key APIs + Gotchas only (Purpose and Related Packages are dropped).
    """
    text = path.read_text(encoding="utf-8", errors="replace")
    sections: dict[str, str] = {}

    # Match headers like: ### `com.unity.netcode.gameobjects` v2.9.2  _73.8s_
    header_re = re.compile(r'^#{2,3}\s+`(com\.[^`\s]+)', re.MULTILINE)
    matches = list(header_re.finditer(text))

    for i, m in enumerate(matches):
        pkg_id = m.group(1)
        start  = m.start()
        end    = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        raw    = text[start:end]
        condensed = _condense_section(pkg_id, raw)
        if condensed:
            sections[pkg_id] = condensed

    return sections


def _condense_section(pkg_id: str, raw: str) -> str:
    """
    Keep only Key APIs and Gotchas blocks from a package section.
    Drops Purpose, Related Packages, timing annotations, and [trimmed] markers.
    """
    lines   = raw.splitlines()
    result  = [f"[{pkg_id}]"]
    capture = False

    for line in lines:
        stripped = line.strip()
        # Start capturing on Key APIs or Gotchas headers
        if re.match(r"##\s+(Key APIs|Gotchas)", stripped):
            capture = True
            result.append(stripped)
        # Stop capturing on any other ## header or section divider
        elif re.match(r"##\s+", stripped) or stripped == "---":
            capture = False
        elif capture:
            # Drop trimmed markers and empty lines within suppressed blocks
            if stripped and not stripped.startswith("_[trimmed"):
                result.append(stripped)

    # Return None-equivalent if we captured nothing useful
    return "\n".join(result) if len(result) > 1 else ""


# ── Assembly map helpers ──────────────────────────────────────────────────────

def _build_asm_to_pkg(asm_map: dict) -> dict[str, str]:
    """
    Build {assembly_name: package_id} for all package-assembly entries.
    Extracts the package ID from the asmdef path, e.g.:
      DevProject/Packages/com.unity.netcode.gameobjects/Runtime/... → com.unity.netcode.gameobjects
    """
    result: dict[str, str] = {}
    for name, info in asm_map.get("packages", {}).items():
        parts = info.get("path", "").replace("\\", "/").split("/")
        try:
            idx = parts.index("Packages")
            result[name] = parts[idx + 1]
        except (ValueError, IndexError):
            pass
    return result


# ── Main provider ─────────────────────────────────────────────────────────────

class PackageContextProvider:
    """
    Resolves and returns relevant package API context for a C# file.
    Safe to create once per CodeScanner and reuse across all file scans.
    All data is loaded lazily on first call and then cached.
    """

    def __init__(self) -> None:
        self._docs: dict[str, str]         = {}  # pkg_id → condensed section
        self._asm_to_pkg: dict[str, str]   = {}  # assembly_name → pkg_id
        self._project_asms: dict[str, dict] = {} # assembly_name → asmdef info
        self._asmdef_dirs: dict[str, str]  = {}  # absolute asmdef dir → assembly_name
        self._ready = False

    def _load(self) -> None:
        if self._ready:
            return

        # Load assembly map
        if ASM_MAP.exists():
            try:
                data = json.loads(ASM_MAP.read_text(encoding="utf-8"))
                self._asm_to_pkg   = _build_asm_to_pkg(data)
                self._project_asms = data.get("project", {})
                for name, info in self._project_asms.items():
                    asmdef_path = (REPO_ROOT / info["path"]).resolve()
                    self._asmdef_dirs[str(asmdef_path.parent)] = name
            except Exception:
                pass

        # Load + parse package docs
        docs_path = _latest_package_docs()
        if docs_path:
            try:
                self._docs = _parse_docs(docs_path)
            except Exception:
                pass

        self._ready = True

    def _assembly_for_file(self, file_path: Path) -> Optional[str]:
        """Walk up from the file to find the nearest asmdef directory."""
        current = file_path.resolve().parent
        while True:
            name = self._asmdef_dirs.get(str(current))
            if name:
                return name
            parent = current.parent
            if parent == current:
                return None
            current = parent

    def _resolve_pkg_ids(self, file_path: Path, content: str) -> list[str]:
        """
        Collect relevant package IDs for this file via two signals.
        Returns a deduplicated, sorted list (sorted for deterministic prompt order).
        """
        pkg_ids: set[str] = set()

        # Signal 1: `using` directives — file-level, precise
        for line in content.splitlines()[:40]:   # usings are always near the top
            m = re.match(r"^\s*using\s+([\w.]+)\s*;", line)
            if not m:
                continue
            ns = m.group(1)
            for prefix, pkg in _NS_TO_PKG.items():
                if ns == prefix or ns.startswith(prefix + "."):
                    pkg_ids.add(pkg)
                    break

        # Signal 2: asmdef references — assembly-level, catches implicit usage
        asm_name = self._assembly_for_file(file_path)
        if asm_name and asm_name in self._project_asms:
            for ref in self._project_asms[asm_name].get("references", []):
                pkg = self._asm_to_pkg.get(ref)
                if pkg and pkg in self._docs:
                    pkg_ids.add(pkg)

        return sorted(pkg_ids)

    def get_context(self, file_path: Path, content: str) -> str:
        """
        Return a condensed package API context block for this file.
        Returns empty string if no relevant docs are available — scan proceeds
        normally without context in that case.
        """
        self._load()
        if not self._docs:
            return ""

        pkg_ids = self._resolve_pkg_ids(file_path, content)
        if not pkg_ids:
            return ""

        sections: list[str] = []
        total = 0
        for pkg_id in pkg_ids:
            section = self._docs.get(pkg_id, "")
            if not section:
                continue
            remaining = MAX_CONTEXT_CHARS - total
            if remaining <= 100:
                break
            if len(section) > remaining:
                section = section[:remaining] + "\n[...truncated]"
            sections.append(section)
            total += len(section)

        if not sections:
            return ""

        return (
            "PACKAGE API REFERENCE (for review context — do not flag these APIs as unknown):\n"
            + "\n\n".join(sections)
        )
