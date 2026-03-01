"""
dev_tools/knowledge_updater.py — Auto-updates Claude memory files with insights
extracted from code scan reports, training logs, and effect feedback data.

Memory files written:
  <CLAUDE_MEMORY_DIR or repo-scoped default>\\code_quality.md
  <CLAUDE_MEMORY_DIR or repo-scoped default>\\project_patterns.md

Usage (via run_dev_tools.py):
    python dev_tools/run_dev_tools.py update
"""

import json
import os
from datetime import datetime
from pathlib import Path
from typing import Optional

from dev_tools.lm_client import LmClient as LmStudioClient, TEXT_MODEL

# ── Paths ──────────────────────────────────────────────────────────────────────
REPO_ROOT   = Path(__file__).parent.parent.resolve()
PROMPT_FILE = Path(__file__).parent / "prompts" / "knowledge_synthesis.txt"


def _default_memory_dir() -> str:
    repo_path = str(REPO_ROOT)
    if len(repo_path) >= 2 and repo_path[1] == ":":
        repo_path = repo_path[0].upper() + repo_path[1:]
    slug = repo_path.replace(":", "").replace("\\", "-").replace("/", "-")
    return str(Path.home() / ".claude" / "projects" / slug / "memory")


MEMORY_DIR  = Path(os.environ.get("CLAUDE_MEMORY_DIR", _default_memory_dir()))

# Inputs
SCAN_SUMMARY  = Path(__file__).parent / "reports" / "latest_scan_summary.md"
TRAINING_LOG  = REPO_ROOT / ".codex" / "tmp" / "run_training.log"
EFFECT_JSON_CANDIDATES = [
    REPO_ROOT / "DevProject" / "output" / "effect_feedback_tuning.json",
    REPO_ROOT / "output" / "effect_feedback_tuning.json",
]

# Output memory files
CODE_QUALITY_MD     = MEMORY_DIR / "code_quality.md"
PROJECT_PATTERNS_MD = MEMORY_DIR / "project_patterns.md"
# ──────────────────────────────────────────────────────────────────────────────

MAX_LOG_CHARS   = 3000   # last N chars of training log
MAX_EFFECT_CHARS = 2000

_SYNTHESIS_SCHEMA = {
    "type": "object",
    "properties": {
        "new_insights": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "category": {"type": "string"},
                    "title": {"type": "string"},
                    "insight": {"type": "string"},
                    "evidence": {"type": "string"},
                },
                "required": ["category", "title", "insight", "evidence"],
                "additionalProperties": False,
            },
        },
        "patterns_confirmed": {
            "type": "array",
            "items": {"type": "string"},
        },
        "patterns_invalidated": {
            "type": "array",
            "items": {"type": "string"},
        },
    },
    "required": ["new_insights", "patterns_confirmed", "patterns_invalidated"],
    "additionalProperties": False,
}


def _read_tail(path: Path, max_chars: int) -> str:
    """Read the last max_chars characters of a file."""
    if not path.exists():
        return ""
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
        return text[-max_chars:] if len(text) > max_chars else text
    except Exception:
        return ""


def _read_json_summary(path: Path, max_chars: int) -> str:
    """Read and prettify a JSON file, truncated to max_chars."""
    if not path.exists():
        return ""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        text = json.dumps(data, indent=2)
        return text[:max_chars]
    except Exception as ex:
        return f"[Error reading {path.name}: {ex}]"


def _resolve_first_existing(paths: list[Path]) -> Optional[Path]:
    for path in paths:
        if path.exists():
            return path
    return None


def _load_existing_memory(path: Path) -> str:
    if not path.exists():
        return ""
    try:
        return path.read_text(encoding="utf-8")
    except Exception:
        return ""


def _load_synthesis_prompt() -> str:
    if PROMPT_FILE.exists():
        return PROMPT_FILE.read_text(encoding="utf-8")
    return (
        "Extract 3-8 new, actionable insights from the provided data and return JSON: "
        "{\"new_insights\": [{\"category\": str, \"title\": str, \"insight\": str, \"evidence\": str}], "
        "\"patterns_confirmed\": [], \"patterns_invalidated\": []}"
    )


def _format_insights_as_markdown(insights: list, date_str: str) -> str:
    """Format a list of insight dicts into markdown lines."""
    lines = []
    for item in insights:
        if not isinstance(item, dict):
            continue
        category = item.get("category", "general").upper()
        title    = item.get("title", "Untitled insight")
        insight  = item.get("insight", "")
        evidence = item.get("evidence", "")
        lines.append(f"### [{category}] {title} _{date_str}_")
        lines.append(insight)
        if evidence:
            lines.append(f"_Evidence: {evidence}_")
        lines.append("")
    return "\n".join(lines)


def _append_to_memory(path: Path, new_content: str, header: str) -> None:
    """Append new_content to a memory file, creating it with a header if new."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        path.write_text(f"# {header}\n\n{new_content}", encoding="utf-8")
    else:
        existing = path.read_text(encoding="utf-8")
        path.write_text(existing.rstrip() + "\n\n" + new_content, encoding="utf-8")


def _update_memory_md_index() -> None:
    """Ensure MEMORY.md references the new memory files if they exist."""
    memory_md = MEMORY_DIR / "MEMORY.md"
    if not memory_md.exists():
        return

    content = memory_md.read_text(encoding="utf-8")
    additions = []

    if "code_quality.md" not in content and CODE_QUALITY_MD.exists():
        additions.append("- `code_quality.md` — LLM-extracted code quality findings from scans")
    if "project_patterns.md" not in content and PROJECT_PATTERNS_MD.exists():
        additions.append("- `project_patterns.md` — Confirmed project-specific patterns and gotchas")

    if not additions:
        return

    insert_after = "See detailed notes in topic files below:"
    if insert_after in content:
        insert_pos = content.index(insert_after) + len(insert_after) + 1
        new_content = (
            content[:insert_pos]
            + "\n".join(additions) + "\n"
            + content[insert_pos:]
        )
        memory_md.write_text(new_content, encoding="utf-8")
        print(f"[Update] Updated MEMORY.md index with {len(additions)} new entries.")


def run_update_cli(client: Optional[LmStudioClient] = None) -> bool:
    """Entry point called by run_dev_tools.py. Returns True on success."""
    if client is None:
        client = LmStudioClient()

    if not client.is_available():
        print("[Update] ERROR: LM Studio is not reachable. Start LM Studio and load a model first.")
        return False

    if not client.ensure_models_loaded([TEXT_MODEL], context_length=4096):
        print(f"[Update] ERROR: Could not load required model: {TEXT_MODEL}")
        return False

    # ── Gather inputs ──────────────────────────────────────────────────────────
    effect_path    = _resolve_first_existing(EFFECT_JSON_CANDIDATES)
    scan_summary   = _read_tail(SCAN_SUMMARY, 4000)
    training_log   = _read_tail(TRAINING_LOG, MAX_LOG_CHARS)
    effect_summary = _read_json_summary(effect_path, MAX_EFFECT_CHARS) if effect_path else ""
    existing_code  = _load_existing_memory(CODE_QUALITY_MD)
    existing_patt  = _load_existing_memory(PROJECT_PATTERNS_MD)

    if not scan_summary and not training_log and not effect_summary:
        print("[Update] No input data found (no scan report, training log, or effect JSON).")
        print("[Update] Run 'python dev_tools/run_dev_tools.py scan' first.")
        return False

    sys_prompt = _load_synthesis_prompt()

    user_msg_parts = []
    if scan_summary:
        user_msg_parts.append(f"=== LATEST CODE SCAN SUMMARY ===\n{scan_summary}")
    if training_log:
        user_msg_parts.append(f"=== RECENT TRAINING LOG (last {MAX_LOG_CHARS} chars) ===\n{training_log}")
    if effect_summary:
        user_msg_parts.append(f"=== EFFECT FEEDBACK DATA ===\n{effect_summary}")

    existing_combined = (existing_code + "\n" + existing_patt).strip()
    if existing_combined:
        user_msg_parts.append(f"=== EXISTING KNOWLEDGE BASE (do not duplicate) ===\n{existing_combined[:3000]}")

    user_msg = "\n\n".join(user_msg_parts)

    print("[Update] Synthesising insights with LM Studio...")
    result = client.ask_schema(
        system=sys_prompt,
        user=user_msg,
        schema=_SYNTHESIS_SCHEMA,
        schema_name="knowledge_synthesis",
        model=TEXT_MODEL,
        max_tokens=800,
    )

    if not result or "error" in result:
        print(f"[Update] LLM request failed: {result.get('error', 'unknown error')}")
        return False

    date_str       = datetime.now().strftime("%Y-%m-%d")
    new_insights   = result.get("new_insights", [])
    confirmed      = result.get("patterns_confirmed", [])
    invalidated    = result.get("patterns_invalidated", [])

    if not new_insights and not confirmed and not invalidated:
        print("[Update] No new insights extracted.")
        return True

    # ── Split insights into code_quality vs project_patterns ──────────────────
    code_quality_insights  = [i for i in new_insights if i.get("category") in ("code_quality", "gotcha")]
    pattern_insights       = [i for i in new_insights if i.get("category") not in ("code_quality", "gotcha")]

    if code_quality_insights:
        md = _format_insights_as_markdown(code_quality_insights, date_str)
        _append_to_memory(CODE_QUALITY_MD, md, "Code Quality Findings")
        print(f"[Update] Wrote {len(code_quality_insights)} insight(s) to code_quality.md")

    if pattern_insights or confirmed or invalidated:
        md_parts = []
        if pattern_insights:
            md_parts.append(_format_insights_as_markdown(pattern_insights, date_str))
        if confirmed:
            md_parts.append(f"### Patterns Confirmed _{date_str}_")
            md_parts.extend(f"- {p}" for p in confirmed)
            md_parts.append("")
        if invalidated:
            md_parts.append(f"### Patterns Invalidated _{date_str}_")
            md_parts.extend(f"- {p}" for p in invalidated)
            md_parts.append("")
        md = "\n".join(md_parts)
        _append_to_memory(PROJECT_PATTERNS_MD, md, "Project Patterns & Conventions")
        print(f"[Update] Wrote {len(pattern_insights)} pattern insight(s) + "
              f"{len(confirmed)} confirmed + {len(invalidated)} invalidated to project_patterns.md")

    # ── Update MEMORY.md index ─────────────────────────────────────────────────
    _update_memory_md_index()

    total = len(new_insights) + len(confirmed) + len(invalidated)
    print(f"[Update] Done. {total} total knowledge base entries added/updated.")
    return True
