---
name: dev-overnight
description: Launch a long-running local LLM dev assistant background job that scans code, updates the knowledge base, and refreshes schemas on repeat. Use when the user wants to run an overnight session, keep the KB fresh automatically, start a background audit, or let the assistant work unattended for hours.
---

# Dev Overnight Skill — Background Long-Running Job Launcher

Starts the full dev assistant pipeline (`schema → scan → update`) on a timed repeat loop.
Designed to run unattended while the user sleeps or works on something else.

## NON-NEGOTIABLE RULES

1. ALWAYS check LM Studio is reachable before starting — it must stay running for the loop to work.
2. ALWAYS run the first iteration synchronously (not in background) to confirm it works.
3. NEVER launch `--watch` in background without telling the user how to stop it (Ctrl+C in that terminal).
4. After the job finishes or is stopped, ALWAYS run `/dev-learn` to sync findings to Serena.
5. If LM Studio is NOT running, tell the user exactly what to do — don't start the job.

## Decision Tree

| User Intent | Command | Notes |
|---|---|---|
| "Run overnight" / "Let it work all night" | `all --watch 120` | repeats every 2h |
| "Run every hour" / "Frequent updates" | `all --watch 60` | repeats every 1h |
| "Just run once" / "Full pipeline now" | `all` | schema → scan → update, once |
| "Just the scan" / "Code audit only" | `scan` | no schema/update |
| "Check if LM Studio is up" | Python health check | see below |
| "Stop the overnight job" | tell user: Ctrl+C in that terminal | cannot stop from Claude |

## Pre-flight Check

Before launching any job, verify LM Studio is running:
```python
python -c "from dev_tools.lm_client import LmStudioClient; c = LmStudioClient(); print('Ready:', c.is_available())"
```

If `Ready: False`:
- Tell the user: "LM Studio is not running. Open LM Studio, load a model, enable the server on port 7002, then retry."
- Do NOT start the job.

## Recommended Overnight Config

```
Interval: 120 min (2 hours) — scans 44 files × ~15s each = ~11 min per cycle
LM Studio must stay open with a model loaded
Leave a terminal running with the command below
Morning: run /dev-learn to sync all new insights to Serena memories
```

## Launch Command (run in a dedicated terminal)

```
C:/Users/andre_wjgj23f/miniconda3/envs/mlagents/python.exe dev_tools/run_dev_tools.py all --watch 120
```

For background launch from Claude (use Bash with run_in_background=true):
```bash
C:/Users/andre_wjgj23f/miniconda3/envs/mlagents/python.exe dev_tools/run_dev_tools.py all
```
Note: `--watch` loops indefinitely — only run interactively in a terminal, not as a background task.

## What Each Pipeline Cycle Does

```
Cycle N (every 120 min):
  Phase 1 — schema:  Parse 5 key C# files → update project_api_schema.json (~5s)
  Phase 2 — scan:    LM Studio reviews 44 .cs files × ~15s = ~11 min
                     Writes: dev_tools/reports/code_scan_YYYY-MM-DD.md
                     Writes: dev_tools/reports/latest_scan_summary.md
  Phase 3 — update:  LM Studio synthesises scan + training log + effect JSON
                     Appends: memory/code_quality.md
                     Appends: memory/project_patterns.md
                     (~20s)
Total per cycle: ~12 min active work, then sleeps until next interval
```

## Morning Routine (after overnight job)

After stopping the overnight job:
```
1. Run /dev-learn          ← sync all new Claude memories → Serena memories
2. Ask Claude: "What did we learn overnight?"
   → Claude reads memory/code_quality.md + memory/project_patterns.md
   → Summarises new findings by category
3. Review HIGH severity findings → decide what to fix today
```

## Monitoring (while job runs)

The job logs to stdout in real time. Key log patterns:
```
[HH:MM:SS] === Phase 1/3: Schema extraction ===     ← cycle started
[HH:MM:SS] [Scan] [ 1/44] NpcDialogueAgent.cs       ← scanning in progress
[HH:MM:SS] [Update] Wrote 3 insight(s) to code_quality.md  ← KB updated
[HH:MM:SS] Sleeping 120m until next run...          ← cycle complete
```

## Resource Notes

- LM Studio uses ~2-4 GB VRAM with `llama-3.2-3b-instruct@q4_k_s`
- Each scan file takes 15–40s depending on file size
- Full cycle peak VRAM usage: same as NPC dialogue inference
- CPU/memory: Python process is lightweight between LM calls
- Does NOT require Unity to be open or in Play mode

## Known Gotchas
- If LM Studio crashes mid-cycle, the job will log errors and sleep until next interval (does NOT auto-restart LM Studio)
- Scan report is overwritten each cycle — `code_scan_YYYY-MM-DD.md` is cumulative per day
- `memory/code_quality.md` and `memory/project_patterns.md` grow over time — prune monthly
- Windows hibernation/sleep will pause the Python process — disable sleep mode for true overnight runs
