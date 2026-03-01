"""
dev_tools/code_scanner.py — Background C# code quality scanner.

Walks all C# files in the DevProject, sends each to LM Studio for review
across four domains (multiplayer safety, ML-Agents, NPC dialogue, Unity best practices),
and writes structured markdown + summary reports.

Usage (via run_dev_tools.py):
    python dev_tools/run_dev_tools.py scan
    python dev_tools/run_dev_tools.py scan --file path/to/MyScript.cs
    python dev_tools/run_dev_tools.py scan --dry-run
"""

import dataclasses
import json
import os
import time
from datetime import datetime
from pathlib import Path
from typing import Callable, List, Optional

from dev_tools.lm_client import LmClient as LmStudioClient, TEXT_MODEL

# ── Paths ──────────────────────────────────────────────────────────────────────
REPO_ROOT = Path(__file__).parent.parent.resolve()
SCAN_DIRS = [
    REPO_ROOT / "DevProject" / "Assets" / "ML-Agents" / "Scripts",
    REPO_ROOT / "DevProject" / "Assets" / "Network_Game" / "Dialogue",
]
PROMPT_FILE   = Path(__file__).parent / "prompts" / "unity_code_review.txt"
PATTERNS_FILE = Path(__file__).parent / "schemas" / "unity_patterns.json"
REPORTS_DIR   = Path(__file__).parent / "reports"

MAX_FILE_CHARS = 6000   # truncate to keep within LM Studio context window

# JSON schema enforced via LmClient.ask_schema (OpenAI /v1/chat/completions)
_SCAN_SCHEMA = {
    "type": "object",
    "properties": {
        "file":     {"type": "string"},
        "severity": {"type": "string", "enum": ["none", "low", "medium", "high"]},
        "issues": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "category":    {"type": "string",
                                    "enum": ["multiplayer_safety", "ml_agents",
                                             "npc_dialogue", "unity_best_practices"]},
                    "severity":    {"type": "string", "enum": ["low", "medium", "high"]},
                    "line_hint":   {"type": "string"},
                    "description": {"type": "string"},
                    "fix":         {"type": "string"},
                },
                "required": ["category", "severity", "line_hint", "description", "fix"],
                "additionalProperties": False,
            },
        },
        "suggestions": {"type": "array", "items": {"type": "string"}},
        "summary":     {"type": "string"},
    },
    "required": ["file", "severity", "issues", "suggestions", "summary"],
    "additionalProperties": False,
}
# ──────────────────────────────────────────────────────────────────────────────


@dataclasses.dataclass
class IssueResult:
    category: str
    severity: str
    line_hint: str
    description: str
    fix: str


@dataclasses.dataclass
class FileScanResult:
    file_path: Path
    severity: str          # none | low | medium | high
    issues: List[IssueResult]
    suggestions: List[str]
    summary: str
    error: Optional[str] = None
    duration_s: float = 0.0


@dataclasses.dataclass
class ScanReport:
    timestamp: datetime
    results: List[FileScanResult]
    total_files: int
    total_issues: int
    high_count: int
    medium_count: int
    low_count: int
    duration_s: float


def _load_system_prompt() -> str:
    if PROMPT_FILE.exists():
        return PROMPT_FILE.read_text(encoding="utf-8")
    # Fallback if file is missing
    return (
        "You are a Unity C# code reviewer. Identify bugs, anti-patterns, and improvements. "
        "Return JSON: {\"severity\": \"none|low|medium|high\", \"issues\": [], \"suggestions\": [], \"summary\": \"\"}"
    )


def _load_few_shot_patterns() -> str:
    """Load a condensed few-shot examples string from the patterns catalog."""
    if not PATTERNS_FILE.exists():
        return ""
    try:
        catalog = json.loads(PATTERNS_FILE.read_text(encoding="utf-8"))
        examples = []
        for category, patterns in catalog.items():
            if category.startswith("_"):
                continue
            for p in patterns[:2]:  # max 2 per category to stay within context
                examples.append(
                    f"- [{p['id']}] {p['pattern']} ({p['severity']}): {p['notes']}"
                )
        return "\n".join(examples)
    except Exception:
        return ""


def _build_user_message(file_path: Path, content: str, few_shot: str) -> str:
    rel_path = file_path.relative_to(REPO_ROOT) if file_path.is_relative_to(REPO_ROOT) else file_path
    truncated = content[:MAX_FILE_CHARS]
    note = f"\n[...truncated at {MAX_FILE_CHARS} chars]" if len(content) > MAX_FILE_CHARS else ""

    parts = [f"File: {rel_path}\n"]
    if few_shot:
        parts.append(f"Reference patterns (use as context, not exhaustive list):\n{few_shot}\n")
    parts.append(f"```csharp\n{truncated}{note}\n```")
    return "\n".join(parts)


def _parse_result(file_path: Path, raw: dict) -> FileScanResult:
    issues = []
    for item in raw.get("issues", []):
        if not isinstance(item, dict):
            continue
        issues.append(IssueResult(
            category=item.get("category", "unknown"),
            severity=item.get("severity", "low"),
            line_hint=item.get("line_hint", ""),
            description=item.get("description", ""),
            fix=item.get("fix", ""),
        ))

    return FileScanResult(
        file_path=file_path,
        severity=raw.get("severity", "none"),
        issues=issues,
        suggestions=raw.get("suggestions", []),
        summary=raw.get("summary", ""),
    )


def _collect_cs_files(paths: Optional[List[Path]] = None) -> List[Path]:
    targets = paths or SCAN_DIRS
    files = []
    for base in targets:
        if not base.exists():
            continue
        for root, _, fnames in os.walk(str(base)):
            for fname in sorted(fnames):
                if fname.endswith(".cs"):
                    files.append(Path(root) / fname)
    return sorted(set(files), key=lambda p: p.name)


class CodeScanner:
    def __init__(self, client: Optional[LmStudioClient] = None):
        self._client   = client or LmStudioClient()
        self._sys_prompt = _load_system_prompt()
        self._few_shot   = _load_few_shot_patterns()

    def scan_file(self, file_path: Path) -> FileScanResult:
        """Scan a single C# file and return structured results."""
        t0 = time.monotonic()
        try:
            content = file_path.read_text(encoding="utf-8", errors="replace")
        except Exception as ex:
            return FileScanResult(
                file_path=file_path, severity="none", issues=[], suggestions=[],
                summary="", error=f"Could not read file: {ex}",
            )

        user_msg = _build_user_message(file_path, content, self._few_shot)
        raw = self._client.ask_schema(
            system=self._sys_prompt,
            user=user_msg,
            schema=_SCAN_SCHEMA,
            schema_name="code_review",
            model=TEXT_MODEL,
            max_tokens=900,
        )

        duration = time.monotonic() - t0

        if not raw or "error" in raw:
            # JSON parse failed — still record partial info
            result = FileScanResult(
                file_path=file_path, severity="low", issues=[], suggestions=[],
                summary=raw.get("error", "LLM returned non-JSON response"),
                error="Schema request failed",
                duration_s=duration,
            )
        else:
            result = _parse_result(file_path, raw)
            result.duration_s = duration

        return result

    def scan_all(
        self,
        file_paths: Optional[List[Path]] = None,
        progress_callback: Optional[Callable[[int, int, str], None]] = None,
    ) -> ScanReport:
        """Scan all discovered C# files. Calls progress_callback(current, total, filename)."""
        files = file_paths or _collect_cs_files()
        results = []
        t0 = time.monotonic()

        for i, fpath in enumerate(files, 1):
            if progress_callback:
                progress_callback(i, len(files), fpath.name)
            result = self.scan_file(fpath)
            results.append(result)

        total_issues = sum(len(r.issues) for r in results)
        high_count   = sum(1 for r in results for issue in r.issues if issue.severity == "high")
        med_count    = sum(1 for r in results for issue in r.issues if issue.severity == "medium")
        low_count    = sum(1 for r in results for issue in r.issues if issue.severity == "low")

        return ScanReport(
            timestamp=datetime.now(),
            results=results,
            total_files=len(files),
            total_issues=total_issues,
            high_count=high_count,
            medium_count=med_count,
            low_count=low_count,
            duration_s=time.monotonic() - t0,
        )

    def write_report(self, report: ScanReport, output_dir: Optional[Path] = None) -> Path:
        """Write a full markdown report and the latest_scan_summary.md file."""
        out_dir = output_dir or REPORTS_DIR
        out_dir.mkdir(parents=True, exist_ok=True)

        date_str = report.timestamp.strftime("%Y-%m-%d")
        report_path = out_dir / f"code_scan_{date_str}.md"

        lines = [
            f"# Code Scan Report — {report.timestamp.strftime('%Y-%m-%d %H:%M')}",
            "",
            f"**Files scanned**: {report.total_files}  ",
            f"**Total issues**: {report.total_issues}  ",
            f"**High**: {report.high_count} | **Medium**: {report.medium_count} | **Low**: {report.low_count}  ",
            f"**Duration**: {report.duration_s:.0f}s",
            "",
        ]

        # High severity first
        for severity_filter in ("high", "medium", "low"):
            section_results = [
                r for r in report.results
                if any(i.severity == severity_filter for i in r.issues)
            ]
            if not section_results:
                continue

            severity_label = severity_filter.upper()
            lines.append(f"## {severity_label} Severity Issues")
            lines.append("")

            for r in section_results:
                rel = r.file_path.relative_to(REPO_ROOT) if r.file_path.is_relative_to(REPO_ROOT) else r.file_path
                lines.append(f"### `{rel}`")
                if r.summary:
                    lines.append(f"_{r.summary}_")
                lines.append("")
                for issue in r.issues:
                    if issue.severity != severity_filter:
                        continue
                    lines.append(f"- **[{issue.category}]** {issue.description}")
                    if issue.line_hint:
                        lines.append(f"  - Location: `{issue.line_hint}`")
                    if issue.fix:
                        lines.append(f"  - Fix: {issue.fix}")
                lines.append("")

        # Clean files
        clean = [r for r in report.results if r.severity == "none" and not r.issues]
        if clean:
            lines.append("## Clean Files (no issues detected)")
            lines.append("")
            for r in clean:
                rel = r.file_path.relative_to(REPO_ROOT) if r.file_path.is_relative_to(REPO_ROOT) else r.file_path
                lines.append(f"- `{rel}`")
            lines.append("")

        report_path.write_text("\n".join(lines), encoding="utf-8")

        # Write condensed summary for Claude memory consumption
        summary_path = out_dir / "latest_scan_summary.md"
        summary_lines = [
            f"# Latest Code Scan Summary ({date_str})",
            "",
            f"Scanned {report.total_files} files | "
            f"High: {report.high_count} | Medium: {report.medium_count} | Low: {report.low_count}",
            "",
            "## Top Issues",
            "",
        ]
        # Collect all high+medium issues, max 20
        top_issues = [
            (r.file_path.name, issue)
            for r in report.results
            for issue in r.issues
            if issue.severity in ("high", "medium")
        ][:20]
        for fname, issue in top_issues:
            summary_lines.append(
                f"- **{issue.severity.upper()}** `{fname}` — [{issue.category}] {issue.description}"
            )
        if not top_issues:
            summary_lines.append("No high or medium severity issues found.")

        summary_lines += [
            "",
            f"Full report: `dev_tools/reports/code_scan_{date_str}.md`",
        ]
        summary_path.write_text("\n".join(summary_lines), encoding="utf-8")

        return report_path


def run_scan_cli(
    file_arg: Optional[str] = None,
    dry_run: bool = False,
) -> Optional[ScanReport]:
    """Entry point called by run_dev_tools.py."""
    if dry_run:
        files = _collect_cs_files()
        print(f"[Scan] Dry run — would scan {len(files)} files:")
        for f in files:
            rel = f.relative_to(REPO_ROOT) if f.is_relative_to(REPO_ROOT) else f
            print(f"  {rel}")
        return None

    client = LmStudioClient()
    if not client.is_available():
        print("[Scan] ERROR: LM Studio is not reachable at "
              f"http://{client.base_url}. Start LM Studio and load a model first.")
        return None

    scanner = CodeScanner(client)

    if file_arg:
        target = Path(file_arg).resolve()
        if not target.exists():
            # Try relative to repo root
            target = REPO_ROOT / file_arg
        if not target.exists():
            print(f"[Scan] File not found: {file_arg}")
            return None
        print(f"[Scan] Single file: {target.name}")
        result = scanner.scan_file(target)
        report = ScanReport(
            timestamp=datetime.now(),
            results=[result],
            total_files=1,
            total_issues=len(result.issues),
            high_count=sum(1 for i in result.issues if i.severity == "high"),
            medium_count=sum(1 for i in result.issues if i.severity == "medium"),
            low_count=sum(1 for i in result.issues if i.severity == "low"),
            duration_s=result.duration_s,
        )
    else:
        def progress(current, total, name):
            print(f"[Scan] [{current:>2}/{total}] {name}")

        print("[Scan] Starting full project scan...")
        report = scanner.scan_all(progress_callback=progress)

    report_path = scanner.write_report(report)
    print(f"\n[Scan] Done. {report.total_files} files, {report.total_issues} issues "
          f"(High={report.high_count} Med={report.medium_count} Low={report.low_count})")
    print(f"[Scan] Report: {report_path}")
    print(f"[Scan] Summary: {REPORTS_DIR / 'latest_scan_summary.md'}")
    return report
