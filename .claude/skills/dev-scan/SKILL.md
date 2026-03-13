---
name: dev-scan
description: Run the local LLM code quality scanner on Unity C# files. Use when the user asks to review code, check for issues, audit a file, or wants a code quality report. Covers 4 domains: multiplayer safety, ML-Agents patterns, NPC dialogue pipeline, Unity best practices.
---

# Dev Scan Skill — Local LLM Code Reviewer

Runs `dev_tools/run_dev_tools.py scan` via LM Studio (port 7002) and interprets results.

## NON-NEGOTIABLE RULES

1. ALWAYS check LM Studio is available before scanning — if not, tell the user clearly.
2. NEVER start a full 44-file scan without warning the user it takes ~10 minutes.
3. ALWAYS read `dev_tools/reports/latest_scan_summary.md` after the scan completes.
4. NEVER just dump the raw report — summarise findings by severity, offer to fix HIGH issues.
5. After fixing any issues found, run `schema` to keep the API schema current.

## Decision Tree

| User Intent | Command | Notes |
|---|---|---|
| "Scan this file" / "Review X.cs" | `scan --file <path>` | ~15–40s, safe to run now |
| "Quick review" / no file specified | `scan --file <last edited .cs>` | use .dev_tools_queue if available |
| "Scan everything" / "Full audit" | `scan` (full) | WARN user: ~10 min |
| "What files would be scanned?" | `scan --dry-run` | instant, no LM Studio needed |
| "Refresh schema" / "Update API map" | `schema` | instant, no LM Studio needed |

## Workflow

### Single File Scan (default — fast)
```
1. Check: python dev_tools/run_dev_tools.py scan --dry-run  (confirm file is in scope)
2. Run:   python dev_tools/run_dev_tools.py scan --file <path>
3. Read:  dev_tools/reports/latest_scan_summary.md
4. Show:  HIGH issues first, with fix suggestions
5. Ask:   "Want me to fix the HIGH issues now?"
```

### Full Project Scan
```
1. Warn user: "Full scan takes ~10 minutes for 44 files. Running in background."
2. Run:   python dev_tools/run_dev_tools.py scan  (can run in background with run_in_background=true)
3. When done, read: dev_tools/reports/latest_scan_summary.md
4. Summarise by severity: HIGH → MEDIUM → LOW
5. Suggest: run /dev-learn to push findings to memory
```

### After Scan — Interpreting Results

Severity meaning:
- **HIGH**: Fix before committing — safety or correctness risk
- **MEDIUM**: Should fix — performance or training quality impact
- **LOW**: Polish / convention — fix when convenient

Domain codes in report:
- `[multiplayer_safety]` — Netcode ownership, NetworkVariable, RPCs
- `[ml_agents]` — Reward design, observation normalisation, episode management
- `[npc_dialogue]` — Null guards on NetworkDialogueService, effect tag handling
- `[unity_best_practices]` — GetComponent caching, GC alloc, field visibility

## PYTHON PATH
Always use full path:
`C:/Users/andre_wjgj23f/miniconda3/envs/mlagents/python.exe dev_tools/run_dev_tools.py scan`

## Known Gotchas
- If 0 issues are returned and no error shown → JSON likely truncated (token limit). Re-run the same file.
- LM Studio model is auto-detected. If wrong model loads, results may be lower quality — check `models.list()`.
- `dev_tools/reports/latest_scan_summary.md` is overwritten each run. Read it immediately.
- The `.codex/tmp/.dev_tools_queue` file contains recently edited .cs files queued for priority scan.

## Key Files
| File | Purpose |
|---|---|
| `dev_tools/run_dev_tools.py` | CLI entry point |
| `dev_tools/code_scanner.py` | Scanner logic |
| `dev_tools/prompts/unity_code_review.txt` | LLM review prompt (edit to tune quality) |
| `dev_tools/schemas/unity_patterns.json` | Pattern catalog — few-shot examples |
| `dev_tools/reports/latest_scan_summary.md` | Always read this after scan |
| `dev_tools/reports/code_scan_YYYY-MM-DD.md` | Full detailed report |
