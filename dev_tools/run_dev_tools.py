"""
dev_tools/run_dev_tools.py — Unified local LLM dev assistant launcher.

Commands:
    scan                    Run C# code quality scanner (all project files)
    scan --file path.cs     Scan a single file
    scan --dry-run          List files that would be scanned, no LM Studio needed
    scan-docs               Generate project-specific package docs from manifest + asmdefs
    update                  Synthesise insights from scan reports + logs -> update memory files
    schema                  Extract project API schemas from C# source files
    asmdef                  Scan all .asmdef files, build assembly dependency map
    jobs                    Run targeted Behavior_Scene analysis jobs
    all                     Run: schema + asmdef -> scan -> update (full pipeline)
    all --watch N           Repeat full pipeline every N minutes (default: 120)

Examples:
    C:\\...\\mlagents\\python.exe dev_tools/run_dev_tools.py all
    C:\\...\\mlagents\\python.exe dev_tools/run_dev_tools.py scan --dry-run
    C:\\...\\mlagents\\python.exe dev_tools/run_dev_tools.py all --watch 60

Notes:
    - LM Studio must be running on port 7002 with a model loaded.
    - Run from the repo root directory.
    - Scan takes ~5-10 minutes for the full project (~35 C# files).
    - Use 'all --watch' for an overnight session that keeps the knowledge base fresh.
"""

import argparse
import sys
import time
from datetime import datetime
from pathlib import Path

# ── Make dev_tools importable when run as a script from repo root ──────────────
REPO_ROOT = Path(__file__).parent.parent.resolve()
sys.path.insert(0, str(REPO_ROOT))
# ──────────────────────────────────────────────────────────────────────────────

from dev_tools.lm_client import LmClient as LmStudioClient  # backward-compat alias


def _ts() -> str:
    return datetime.now().strftime("%H:%M:%S")


def log(msg: str) -> None:
    print(f"[{_ts()}] {msg}", flush=True)


def cmd_scan(args) -> bool:
    from dev_tools.code_scanner import run_scan_cli
    result = run_scan_cli(
        file_arg=getattr(args, "file", None),
        dry_run=getattr(args, "dry_run", False),
    )
    return result is not None or getattr(args, "dry_run", False)


def cmd_update(args) -> bool:
    from dev_tools.knowledge_updater import run_update_cli
    return run_update_cli()


def cmd_schema(args) -> bool:
    from dev_tools.schema_extractor import run_schema_cli
    return run_schema_cli()


def cmd_asmdef(args) -> bool:
    from dev_tools.asmdef_scanner import run_asmdef_cli
    return run_asmdef_cli()


def cmd_scan_docs(args) -> bool:
    from dev_tools.docs_scanner import run_docs_cli
    return run_docs_cli(package_arg=getattr(args, "package", None))


def cmd_jobs(args) -> bool:
    from dev_tools.scene_jobs import run_jobs
    job_ids = [args.job] if getattr(args, "job", None) else None
    summary = run_jobs(job_ids=job_ids)
    if not summary:
        return False
    failed = [jid for jid, s in summary.items() if s["status"] != "ok"]
    return len(failed) == 0


def cmd_all(args) -> bool:
    """Run schema + asmdef -> scan -> update."""
    log("=== Phase 1/3: Schema extraction + Assembly map (no LM Studio needed) ===")
    schema_ok = cmd_schema(args)
    asmdef_ok = cmd_asmdef(args)

    log("=== Phase 2/3: Code scan ===")
    scan_ok = cmd_scan(args)
    if not scan_ok:
        log("Scan failed or returned no results. Skipping knowledge update.")
        return False

    log("=== Phase 3/3: Knowledge base update ===")
    update_ok = cmd_update(args)

    if schema_ok and asmdef_ok and update_ok:
        log("=== Full pipeline complete ===")
        return True

    log("=== Full pipeline completed with errors ===")
    return False


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Local LLM Dev Assistant for Unity ML-Agents project",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    subparsers = parser.add_subparsers(dest="command", metavar="command")

    # ── scan ──
    scan_p = subparsers.add_parser("scan", help="Run C# code quality scanner")
    scan_p.add_argument("--file", metavar="PATH", help="Scan a single file instead of all files")
    scan_p.add_argument("--dry-run", action="store_true", help="List files without scanning")

    # ── update ──
    subparsers.add_parser("update", help="Update memory files from scan reports + logs")

    # ── schema ──
    subparsers.add_parser("schema", help="Extract project API schemas from C# files")

    # ── asmdef ──
    subparsers.add_parser("asmdef", help="Scan .asmdef files and build assembly dependency map")

    # ── scan-docs ──
    docs_p = subparsers.add_parser("scan-docs", help="Generate project-specific package documentation")
    docs_p.add_argument(
        "--package", metavar="PKG_ID",
        help="Document a single package by ID (e.g. --package com.unity.netcode.gameobjects)",
    )

    # ── jobs ──
    jobs_p = subparsers.add_parser("jobs", help="Run Behavior_Scene analysis jobs (01-08)")
    jobs_p.add_argument(
        "--job", metavar="ID",
        help="Run a single job by ID (e.g. --job 03). Omit to run all jobs.",
    )

    # ── all ──
    all_p = subparsers.add_parser("all", help="Run full pipeline: schema → scan → update")
    all_p.add_argument(
        "--watch", metavar="MINUTES", type=int, nargs="?", const=120,
        help="Repeat every N minutes (default: 120) for overnight operation",
    )

    args = parser.parse_args()

    if not args.command:
        parser.print_help()
        return

    COMMANDS = {
        "scan":      cmd_scan,
        "scan-docs": cmd_scan_docs,
        "update":    cmd_update,
        "schema":    cmd_schema,
        "asmdef":    cmd_asmdef,
        "jobs":      cmd_jobs,
        "all":       cmd_all,
    }

    fn = COMMANDS[args.command]

    if args.command == "all" and getattr(args, "watch", None):
        interval_min = args.watch
        run_count = 0
        log(f"Watch mode: running full pipeline every {interval_min} minutes. Ctrl+C to stop.")
        while True:
            run_count += 1
            log(f"--- Watch run #{run_count} ---")
            try:
                ok = fn(args)
                if not ok:
                    log("Pipeline run completed with errors.")
            except Exception as ex:
                log(f"Pipeline error: {type(ex).__name__}: {ex}")
            log(f"Sleeping {interval_min}m until next run...")
            time.sleep(interval_min * 60)
    else:
        try:
            ok = fn(args)
        except KeyboardInterrupt:
            log("Stopped by user.")
        except Exception as ex:
            import traceback
            log(f"ERROR: {type(ex).__name__}: {ex}")
            traceback.print_exc()
            sys.exit(1)
        else:
            if not ok:
                sys.exit(1)


if __name__ == "__main__":
    main()
