"""
dev_tools/static_checker.py — Targeted static analysis for ML-Agents + NGO.
No LLM. Runs in seconds. Reports only patterns it can prove exist in the code.

Rules:
  ML001  AddReward() argument not wrapped in Mathf.Clamp  → PPO gradient instability
  ML002  sensor.AddObservation() of likely raw value in CollectObservations  → slow convergence
  NET01  NetworkVariable .Value = write without IsServer/IsOwner in same method  → multiplayer exploit
"""

import re
import sys
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent.resolve()

SCAN_DIRS = [
    REPO_ROOT / "DevProject" / "Assets" / "ML-Agents" / "Scripts",
    REPO_ROOT / "DevProject" / "Assets" / "Network_Game" / "Dialogue",
    REPO_ROOT / "DevProject" / "Assets" / "Network_Game" / "UI",
    REPO_ROOT / "DevProject" / "Assets" / "Network_Game" / "Auth",
    REPO_ROOT / "DevProject" / "Assets" / "Network_Game" / "Combat",
]


@dataclass
class Finding:
    file: Path
    line_no: int
    rule: str
    severity: str
    snippet: str
    fix: str

    def __str__(self) -> str:
        rel = self.file.relative_to(REPO_ROOT)
        return (
            f"  [{self.severity}] {self.rule}  {rel}:{self.line_no}\n"
            f"    code: {self.snippet}\n"
            f"    fix:  {self.fix}"
        )


# ── helpers ────────────────────────────────────────────────────────────────────

def _strip_line_comments(source: str) -> str:
    """Remove // comments, preserving line numbers."""
    return re.sub(r"//[^\n]*", "", source)


def _extract_paren_arg(source: str, open_paren_pos: int) -> str:
    """Return the text between the opening paren at open_paren_pos and its matching close."""
    depth = 1
    i = open_paren_pos + 1
    while i < len(source) and depth > 0:
        c = source[i]
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        i += 1
    return source[open_paren_pos + 1 : i - 1]


def _line_of(source: str, pos: int) -> int:
    """1-indexed line number for a character position in source."""
    return source[:pos].count("\n") + 1


def _enclosing_method(lines: list[str], target_line_0: int) -> list[str]:
    """
    Return the lines of the method that contains target_line_0 (0-indexed).
    Uses brace counting — accurate for typical C# with no string-literal braces.
    """
    # Walk backward to find where brace depth returns to 0 (method start)
    depth = 0
    start = target_line_0
    for i in range(target_line_0, -1, -1):
        depth += lines[i].count("}") - lines[i].count("{")
        if depth > 0:
            start = i + 1
            break
    else:
        start = 0

    # Walk forward to find closing brace
    depth = 0
    end = len(lines) - 1
    for i in range(start, len(lines)):
        depth += lines[i].count("{") - lines[i].count("}")
        if depth <= 0 and i > start:
            end = i
            break

    return lines[start : end + 1]


# ── rule implementations ───────────────────────────────────────────────────────

def check_add_reward(source: str, filepath: Path) -> list[Finding]:
    """ML001 — AddReward() without Mathf.Clamp."""
    findings: list[Finding] = []
    clean = _strip_line_comments(source)

    for m in re.finditer(r"\bAddReward\s*\(", clean):
        arg = _extract_paren_arg(clean, m.end() - 1)
        # Skip if already clamped or is a literal zero/constant
        if "Clamp" in arg:
            continue
        if re.fullmatch(r"\s*[-\d.ef]+\s*", arg):
            # literal constant — acceptable
            continue
        line_no = _line_of(clean, m.start())
        snippet = f"AddReward({arg.strip()[:60]})"
        findings.append(Finding(
            file=filepath,
            line_no=line_no,
            rule="ML001",
            severity="HIGH",
            snippet=snippet,
            fix="Wrap in Mathf.Clamp(..., -0.5f, 0.5f) to prevent PPO gradient spikes",
        ))
    return findings


# Indicators that a value has already been normalised
_NORM_RE = re.compile(
    r"Normalized|Clamp|InverseLerp"          # explicit normalisation methods
    r"|/\s*[\d.f]"                            # divided by a numeric literal
    r"|/\s*m_[A-Za-z]|/\s*[A-Za-z]+Max"      # divided by a field (e.g. / m_MaxDist)
    r"|\?\s*[01]f?\s*:"                       # ternary → 0 or 1
    r"|==|!=|<\s|>\s"                         # boolean expression
    r"|\bfalse\b|\btrue\b",
    re.IGNORECASE,
)


def check_observations(source: str, filepath: Path) -> list[Finding]:
    """ML002 — sensor.AddObservation() of likely raw value inside CollectObservations."""
    findings: list[Finding] = []
    clean = _strip_line_comments(source)

    # Find CollectObservations method body
    co_match = re.search(r"\bCollectObservations\s*\(", clean)
    if not co_match:
        return findings

    # Extract the method body by brace counting
    brace_start = clean.find("{", co_match.end())
    if brace_start == -1:
        return findings
    depth = 0
    i = brace_start
    while i < len(clean):
        if clean[i] == "{":
            depth += 1
        elif clean[i] == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    method_body = clean[brace_start : i + 1]
    method_start_line = _line_of(clean, brace_start)

    for m in re.finditer(r"\bAddObservation\s*\(", method_body):
        arg = _extract_paren_arg(method_body, m.end() - 1).strip()
        if _NORM_RE.search(arg):
            continue  # already normalised
        # Only flag field/property accesses that look unbounded
        if re.match(r"^[a-zA-Z_][a-zA-Z0-9_.]*$", arg):
            local_line = method_body[: m.start()].count("\n")
            line_no = method_start_line + local_line
            findings.append(Finding(
                file=filepath,
                line_no=line_no,
                rule="ML002",
                severity="MEDIUM",
                snippet=f"sensor.AddObservation({arg})",
                fix=f"Normalise to [0,1]: sensor.AddObservation({arg} / maxExpectedValue)",
            ))
    return findings


def check_network_variable_writes(source: str, filepath: Path) -> list[Finding]:
    """NET01 — NetworkVariable .Value = written without IsServer/IsOwner in the same method."""
    findings: list[Finding] = []
    clean = _strip_line_comments(source)
    lines = clean.splitlines()

    # Collect declared NetworkVariable field names in this file
    nv_names: list[str] = re.findall(
        r"NetworkVariable\s*<[^>]+>\s+(\w+)\s*[=;]", clean
    )
    if not nv_names:
        return findings

    nv_pattern = re.compile(r"\b(" + "|".join(re.escape(n) for n in nv_names) + r")\.Value\s*=(?!=)")

    for m in nv_pattern.finditer(clean):
        line_no = _line_of(clean, m.start())
        method_lines = _enclosing_method(lines, line_no - 1)
        method_src = "\n".join(method_lines)

        if "IsServer" in method_src or "IsOwner" in method_src:
            continue  # guarded

        snippet = lines[line_no - 1].strip()[:70]
        findings.append(Finding(
            file=filepath,
            line_no=line_no,
            rule="NET01",
            severity="HIGH",
            snippet=snippet,
            fix="Guard with: if (!IsServer) return;  (or IsOwner if client-authoritative)",
        ))
    return findings


# ── runner ────────────────────────────────────────────────────────────────────

def _collect_files() -> list[Path]:
    files: list[Path] = []
    for base in SCAN_DIRS:
        if not base.exists():
            continue
        for p in base.rglob("*.cs"):
            parts = set(p.relative_to(base).parts)
            if parts & {"Tests", "Test"}:
                continue
            if p.name.endswith("Tests.cs"):
                continue
            files.append(p)
    return sorted(files)


def run_static_checks(paths: list[Path] | None = None) -> list[Finding]:
    all_findings: list[Finding] = []
    files = paths or _collect_files()

    for fp in files:
        try:
            source = fp.read_text(encoding="utf-8", errors="replace")
        except Exception:
            continue
        all_findings += check_add_reward(source, fp)
        all_findings += check_observations(source, fp)
        all_findings += check_network_variable_writes(source, fp)

    return all_findings


def run_static_cli() -> None:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    findings = run_static_checks()

    by_rule: dict[str, list[Finding]] = {}
    for f in findings:
        by_rule.setdefault(f.rule, []).append(f)

    total = len(findings)
    print(f"\n[StaticCheck] {total} finding(s) across {len(set(f.file for f in findings))} file(s)\n")

    labels = {
        "ML001": "ML001 -- AddReward not clamped (PPO gradient instability)",
        "ML002": "ML002 -- Unnormalised observation (slow convergence)",
        "NET01": "NET01 -- NetworkVariable write without server guard (exploit risk)",
    }
    for rule, label in labels.items():
        group = by_rule.get(rule, [])
        if not group:
            continue
        print(f"-- {label} ({len(group)})")
        for f in group:
            print(f)
        print()

    if not findings:
        print("  All clean.")


if __name__ == "__main__":
    run_static_cli()
