"""
dev_tools/dashboard.py — Local web dashboard for ML-Agents dev tools.

    pip install fastapi uvicorn
    python dev_tools/run_dev_tools.py dashboard

Then open http://localhost:8765

Features:
  • LM Studio model status (loaded / unloaded, context length)
  • Process management: overnight scan watcher + LLM bridge
  • Live log streaming via SSE
  • Latest scan report with per-severity/category breakdown
  • On-demand verify checks
"""

import asyncio
import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Any, AsyncGenerator, Optional

REPO_ROOT   = Path(__file__).parent.parent.resolve()
LOG_SCAN    = REPO_ROOT / ".codex" / "tmp" / "overnight.log"
LOG_BRIDGE  = REPO_ROOT / ".codex" / "tmp" / "run_llm_bridge.runtime.log"
REPORTS_DIR = REPO_ROOT / "dev_tools" / "reports"
LMS_API     = "http://127.0.0.1:7002"
LMS_KEY     = "sk-lm-Li2oVsHm:wNxCcCTjZM4PFuNC0RnH"
PYTHON      = sys.executable

# ── managed process handles ────────────────────────────────────────────────────
_procs: dict[str, Optional[subprocess.Popen]] = {"watcher": None, "bridge": None}


# ── helpers ────────────────────────────────────────────────────────────────────

def _tail(path: Path, n: int = 120) -> list[str]:
    if not path.exists():
        return []
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        return lines[-n:]
    except Exception:
        return []


def _lms_models() -> dict:
    try:
        import urllib.request
        req = urllib.request.Request(
            f"{LMS_API}/api/v0/models",
            headers={"Authorization": f"Bearer {LMS_KEY}"},
        )
        with urllib.request.urlopen(req, timeout=4) as r:
            data = json.loads(r.read())
        models = [
            {
                "id":      m["id"],
                "state":   m.get("state", "unknown"),
                "arch":    m.get("arch", ""),
                "quant":   m.get("quantization", ""),
                "ctx":     m.get("loaded_context_length"),
                "type":    m.get("type", "llm"),
            }
            for m in data.get("data", [])
        ]
        return {"reachable": True, "models": models}
    except Exception as ex:
        return {"reachable": False, "models": [], "error": str(ex)}


def _parse_scan_progress(log_path: Path) -> dict:
    """Extract current scan run stats from the log file."""
    if not log_path.exists():
        return {}
    try:
        text = log_path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return {}

    runs   = re.findall(r"--- Watch run #(\d+) ---", text)
    run_no = int(runs[-1]) if runs else 0

    # current file progress
    files  = re.findall(r"\[Scan\] \[\s*(\d+)/(\d+)\]", text)
    cur, total = (int(files[-1][0]), int(files[-1][1])) if files else (0, 0)

    # last completed summary
    done   = re.findall(r"Done\. (\d+) files, (\d+) issues \(High=(\d+) Med=(\d+) Low=(\d+)\)", text)
    last   = {"files": int(done[-1][0]), "issues": int(done[-1][1]),
              "high": int(done[-1][2]), "med": int(done[-1][3]), "low": int(done[-1][4])} if done else {}

    # last phase timestamp
    phases = re.findall(r"\[(\d{2}:\d{2}:\d{2})\] === Phase", text)
    last_ts = phases[-1] if phases else ""

    mtime = log_path.stat().st_mtime
    age_s = int(time.time() - mtime)
    running = age_s < 300  # consider active if updated within 5 min

    return {
        "run":      run_no,
        "current":  cur,
        "total":    total,
        "last":     last,
        "last_ts":  last_ts,
        "age_s":    age_s,
        "running":  running,
    }


def _parse_report() -> dict:
    """Parse the latest scan markdown report into structured data."""
    reports = sorted(REPORTS_DIR.glob("code_scan_*.md"), reverse=True)
    if not reports:
        return {}
    path = reports[0]
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return {}

    issues = []
    current_file = ""
    for line in text.splitlines():
        m = re.match(r"### `(.+?)`", line)
        if m:
            current_file = m.group(1).replace("\\", "/").split("Assets/")[-1]
        m = re.match(r"- \*\*\[(.+?)\]\*\* (.+)", line)
        if m:
            issues.append({"file": current_file, "category": m.group(1), "desc": m.group(2)})
        m = re.match(r"  - Location: `(.+?)`", line)
        if m and issues:
            issues[-1]["location"] = m.group(1)

    # severity from header
    hdr = re.search(r"\*\*High\*\*: (\d+) \| \*\*Medium\*\*: (\d+) \| \*\*Low\*\*: (\d+)", text)
    high, med, low = (int(hdr.group(1)), int(hdr.group(2)), int(hdr.group(3))) if hdr else (0, 0, 0)
    files_m = re.search(r"\*\*Files scanned\*\*: (\d+)", text)
    dur_m   = re.search(r"\*\*Duration\*\*: (\d+)s", text)
    ts_m    = re.search(r"# Code Scan Report — (.+)", text)

    # errors
    errors = re.findall(r"- `(.+?)` — (.+)", text[text.find("Scan Errors"):] if "Scan Errors" in text else "")

    return {
        "timestamp": ts_m.group(1) if ts_m else "",
        "files":     int(files_m.group(1)) if files_m else 0,
        "high":      high, "med": med, "low": low,
        "duration":  int(dur_m.group(1)) if dur_m else 0,
        "issues":    issues,
        "errors":    [{"file": e[0], "msg": e[1][:100]} for e in errors],
        "report":    path.name,
    }


def _run_verify() -> list[dict]:
    """Run static verify checks, return list of {name, passed, detail}."""
    sys.path.insert(0, str(REPO_ROOT))
    from dev_tools.verify import Results, check_model_routing, check_unity_scene, check_error_surfacing
    r = Results()
    check_model_routing(r)
    check_unity_scene(r)
    check_error_surfacing(r)
    return [{"name": n, "passed": ok, "detail": d} for n, ok, d in r._results]


def _start_proc(key: str, cmd: list[str], log: Path) -> str:
    global _procs
    p = _procs.get(key)
    if p and p.poll() is None:
        return f"{key} already running (pid {p.pid})"
    log.parent.mkdir(parents=True, exist_ok=True)
    fh = open(log, "a", encoding="utf-8")
    fh.write(f"\n[{datetime.now():%H:%M:%S}] --- Dashboard start ---\n")
    fh.flush()
    env = os.environ.copy()
    env["PYTHONUNBUFFERED"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    # Insert -u (unbuffered) after the python executable so log writes are immediate
    unbuffered_cmd = [cmd[0], "-u"] + cmd[1:]
    _procs[key] = subprocess.Popen(unbuffered_cmd, stdout=fh, stderr=fh, cwd=str(REPO_ROOT), env=env)
    return f"{key} started (pid {_procs[key].pid})"


def _stop_proc(key: str) -> str:
    p = _procs.get(key)
    if not p or p.poll() is not None:
        return f"{key} not running"
    p.terminate()
    try:
        p.wait(timeout=5)
    except subprocess.TimeoutExpired:
        p.kill()
    _procs[key] = None
    return f"{key} stopped"


# ── FastAPI app ────────────────────────────────────────────────────────────────

def build_app():
    from fastapi import FastAPI
    from fastapi.middleware.cors import CORSMiddleware
    from fastapi.responses import HTMLResponse, JSONResponse, StreamingResponse

    app = FastAPI(title="ML-Agents Dev Console")
    app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

    @app.get("/", response_class=HTMLResponse)
    async def root():
        return _HTML

    @app.get("/api/status")
    async def status():
        lms   = _lms_models()
        scan  = _parse_scan_progress(LOG_SCAN)
        bridge_running = bool(_procs.get("bridge") and _procs["bridge"].poll() is None)
        watcher_pid    = _procs["watcher"].pid if _procs.get("watcher") and _procs["watcher"].poll() is None else None
        return {
            "lms":     lms,
            "scan":    scan,
            "bridge":  {"running": bridge_running},
            "watcher": {"managed_pid": watcher_pid},
            "ts":      datetime.now().strftime("%H:%M:%S"),
        }

    @app.get("/api/logs")
    async def logs(src: str = "scan", n: int = 150):
        path = LOG_SCAN if src == "scan" else LOG_BRIDGE
        return {"lines": _tail(path, n), "path": str(path)}

    @app.get("/api/logs/stream")
    async def logs_stream(src: str = "scan"):
        path = LOG_SCAN if src == "scan" else LOG_BRIDGE

        async def generator() -> AsyncGenerator[str, None]:
            pos = path.stat().st_size if path.exists() else 0
            while True:
                await asyncio.sleep(1.5)
                if not path.exists():
                    continue
                size = path.stat().st_size
                if size <= pos:
                    yield "data: \n\n"
                    continue
                with open(path, "r", encoding="utf-8", errors="replace") as fh:
                    fh.seek(pos)
                    new = fh.read()
                pos = size
                for line in new.splitlines():
                    line = line.replace("\\", "\\\\").replace('"', '\\"')
                    yield f'data: {{"line":"{line}"}}\n\n'

        return StreamingResponse(generator(), media_type="text/event-stream",
                                 headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})

    @app.get("/api/report")
    async def report():
        return _parse_report()

    @app.post("/api/verify")
    async def verify():
        try:
            results = await asyncio.get_event_loop().run_in_executor(None, _run_verify)
            return {"results": results, "passed": sum(1 for r in results if r["passed"]), "total": len(results)}
        except Exception as ex:
            return JSONResponse({"error": str(ex)}, status_code=500)

    @app.post("/api/watcher/start")
    async def watcher_start():
        cmd = [PYTHON, str(REPO_ROOT / "dev_tools" / "run_dev_tools.py"), "all", "--watch", "120"]
        return {"message": _start_proc("watcher", cmd, LOG_SCAN)}

    @app.post("/api/watcher/stop")
    async def watcher_stop():
        return {"message": _stop_proc("watcher")}

    @app.post("/api/watcher/restart")
    async def watcher_restart():
        _stop_proc("watcher")
        await asyncio.sleep(1)
        cmd = [PYTHON, str(REPO_ROOT / "dev_tools" / "run_dev_tools.py"), "all", "--watch", "120"]
        return {"message": _start_proc("watcher", cmd, LOG_SCAN)}

    @app.post("/api/bridge/start")
    async def bridge_start():
        cmd = [PYTHON, str(REPO_ROOT / "run_llm_bridge.py")]
        return {"message": _start_proc("bridge", cmd, LOG_BRIDGE)}

    @app.post("/api/bridge/stop")
    async def bridge_stop():
        return {"message": _stop_proc("bridge")}

    @app.post("/api/scan/file")
    async def scan_file(body: dict):
        filepath = body.get("file", "")
        if not filepath:
            return JSONResponse({"error": "file required"}, status_code=400)

        def _do_scan():
            sys.path.insert(0, str(REPO_ROOT))
            from dev_tools.lm_client import LmClient
            from dev_tools.code_scanner import CodeScanner
            c = LmClient()
            s = CodeScanner(c)
            r = s.scan_file(Path(filepath).resolve())
            return {
                "file":     filepath,
                "severity": r.severity,
                "issues":   [{"category": i.category, "severity": i.severity,
                               "description": i.description, "fix": i.fix,
                               "line_hint": i.line_hint, "line_quote": i.line_quote} for i in r.issues],
                "summary":  r.summary,
                "error":    r.error,
                "duration": round(r.duration_s),
            }

        result = await asyncio.get_event_loop().run_in_executor(None, _do_scan)
        return result

    return app


# ── embedded HTML ──────────────────────────────────────────────────────────────

_HTML = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>ML-Agents Dev Console</title>
<style>
  :root {
    --bg:       #0d1117;
    --surface:  #161b22;
    --border:   #30363d;
    --text:     #e6edf3;
    --muted:    #8b949e;
    --green:    #3fb950;
    --red:      #f85149;
    --orange:   #d29922;
    --blue:     #58a6ff;
    --purple:   #bc8cff;
    --yellow:   #e3b341;
    --radius:   8px;
    --mono:     'JetBrains Mono', 'Cascadia Code', 'Consolas', monospace;
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { background: var(--bg); color: var(--text); font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; font-size: 14px; min-height: 100vh; }

  /* ── layout ── */
  .header { display: flex; align-items: center; justify-content: space-between; padding: 12px 24px; background: var(--surface); border-bottom: 1px solid var(--border); position: sticky; top: 0; z-index: 100; }
  .header-left { display: flex; align-items: center; gap: 12px; }
  .logo { font-size: 16px; font-weight: 700; color: var(--text); letter-spacing: -0.3px; }
  .logo span { color: var(--blue); }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--red); transition: background .3s; }
  .dot.live { background: var(--green); box-shadow: 0 0 6px var(--green); }
  .dot.warn { background: var(--orange); }
  .ts { color: var(--muted); font-family: var(--mono); font-size: 12px; }

  .layout { display: grid; grid-template-columns: 320px 1fr; grid-template-rows: auto 1fr; gap: 16px; padding: 16px; height: calc(100vh - 53px); }
  .sidebar { grid-row: 1 / 3; display: flex; flex-direction: column; gap: 16px; overflow-y: auto; }
  .main-top { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
  .main-bottom { overflow: hidden; display: flex; flex-direction: column; }

  /* ── card ── */
  .card { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); overflow: hidden; }
  .card-header { display: flex; align-items: center; justify-content: space-between; padding: 10px 14px; border-bottom: 1px solid var(--border); background: rgba(255,255,255,.02); }
  .card-title { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: .5px; color: var(--muted); }
  .card-body { padding: 14px; }
  .card-actions { display: flex; gap: 6px; }

  /* ── buttons ── */
  .btn { padding: 5px 10px; border-radius: 6px; border: 1px solid var(--border); background: rgba(255,255,255,.06); color: var(--text); font-size: 12px; cursor: pointer; transition: all .15s; }
  .btn:hover { background: rgba(255,255,255,.12); }
  .btn.primary { background: rgba(88,166,255,.15); border-color: var(--blue); color: var(--blue); }
  .btn.primary:hover { background: rgba(88,166,255,.25); }
  .btn.danger  { background: rgba(248,81,73,.1); border-color: var(--red); color: var(--red); }
  .btn.danger:hover { background: rgba(248,81,73,.2); }
  .btn.success { background: rgba(63,185,80,.1); border-color: var(--green); color: var(--green); }
  .btn.success:hover { background: rgba(63,185,80,.2); }
  .btn:disabled { opacity: .4; cursor: not-allowed; }

  /* ── badges ── */
  .badge { display: inline-block; padding: 2px 7px; border-radius: 20px; font-size: 11px; font-weight: 600; font-family: var(--mono); }
  .badge.high   { background: rgba(248,81,73,.15);   color: var(--red);    border: 1px solid rgba(248,81,73,.3); }
  .badge.medium { background: rgba(210,153,34,.15);  color: var(--orange); border: 1px solid rgba(210,153,34,.3); }
  .badge.low    { background: rgba(88,166,255,.15);  color: var(--blue);   border: 1px solid rgba(88,166,255,.3); }
  .badge.none   { background: rgba(63,185,80,.15);   color: var(--green);  border: 1px solid rgba(63,185,80,.3); }
  .badge.loaded { background: rgba(63,185,80,.1);    color: var(--green);  border: 1px solid rgba(63,185,80,.2); }
  .badge.unloaded { background: rgba(139,148,158,.1); color: var(--muted); border: 1px solid rgba(139,148,158,.2); }
  .badge.pass   { background: rgba(63,185,80,.1);    color: var(--green);  border: 1px solid rgba(63,185,80,.2); }
  .badge.fail   { background: rgba(248,81,73,.1);    color: var(--red);    border: 1px solid rgba(248,81,73,.2); }

  /* ── model list ── */
  .model-row { display: flex; align-items: center; justify-content: space-between; padding: 7px 0; border-bottom: 1px solid rgba(255,255,255,.04); gap: 8px; }
  .model-row:last-child { border-bottom: none; }
  .model-id { font-family: var(--mono); font-size: 12px; color: var(--text); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .model-meta { font-family: var(--mono); font-size: 11px; color: var(--muted); white-space: nowrap; }

  /* ── progress bar ── */
  .progress-wrap { background: rgba(255,255,255,.05); border-radius: 4px; height: 6px; overflow: hidden; margin: 8px 0; }
  .progress-bar  { height: 100%; background: var(--blue); border-radius: 4px; transition: width .5s ease; }

  /* ── stat grid ── */
  .stat-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
  .stat { text-align: center; }
  .stat-val { font-size: 24px; font-weight: 700; font-family: var(--mono); }
  .stat-lbl { font-size: 11px; color: var(--muted); margin-top: 2px; }
  .stat-val.red    { color: var(--red); }
  .stat-val.orange { color: var(--orange); }
  .stat-val.blue   { color: var(--blue); }

  /* ── log pane ── */
  .log-wrap { flex: 1; overflow: hidden; display: flex; flex-direction: column; }
  .log-scroll { flex: 1; overflow-y: auto; background: #0a0e14; border: 1px solid var(--border); border-radius: var(--radius); padding: 10px 12px; font-family: var(--mono); font-size: 12px; line-height: 1.7; }
  .log-scroll::-webkit-scrollbar { width: 4px; }
  .log-scroll::-webkit-scrollbar-track { background: transparent; }
  .log-scroll::-webkit-scrollbar-thumb { background: var(--border); border-radius: 2px; }
  .log-line { white-space: pre-wrap; word-break: break-all; }
  .log-line.scan   { color: var(--blue); }
  .log-line.warn   { color: var(--orange); }
  .log-line.error  { color: var(--red); }
  .log-line.done   { color: var(--green); font-weight: 600; }
  .log-line.phase  { color: var(--purple); font-weight: 600; }
  .log-line.ts     { color: var(--muted); }

  /* ── tabs ── */
  .tabs { display: flex; gap: 0; border-bottom: 1px solid var(--border); margin-bottom: 12px; }
  .tab { padding: 7px 14px; font-size: 12px; font-weight: 500; cursor: pointer; border-bottom: 2px solid transparent; color: var(--muted); transition: all .15s; }
  .tab.active { color: var(--blue); border-bottom-color: var(--blue); }
  .tab:hover:not(.active) { color: var(--text); }

  /* ── issues table ── */
  .issues-wrap { overflow-y: auto; max-height: 100%; }
  .issue-row { display: grid; grid-template-columns: 80px 140px 1fr; gap: 8px; align-items: start; padding: 8px 0; border-bottom: 1px solid rgba(255,255,255,.04); }
  .issue-row:last-child { border-bottom: none; }
  .issue-cat  { font-size: 11px; color: var(--muted); font-family: var(--mono); }
  .issue-file { font-size: 11px; font-family: var(--mono); color: var(--blue); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .issue-desc { font-size: 12px; line-height: 1.5; }
  .issue-loc  { font-size: 11px; color: var(--muted); font-family: var(--mono); margin-top: 2px; }

  /* ── verify ── */
  .verify-row { display: flex; align-items: center; gap: 8px; padding: 5px 0; border-bottom: 1px solid rgba(255,255,255,.04); }
  .verify-row:last-child { border-bottom: none; }
  .verify-icon { font-size: 13px; flex-shrink: 0; }
  .verify-name { flex: 1; font-size: 12px; }
  .verify-detail { font-size: 11px; color: var(--muted); font-family: var(--mono); }

  /* ── process card ── */
  .proc-row { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
  .proc-name { font-weight: 600; font-size: 13px; }
  .proc-status { font-size: 12px; font-family: var(--mono); }
  .proc-status.running { color: var(--green); }
  .proc-status.stopped { color: var(--muted); }
  .proc-status.external { color: var(--orange); }

  /* ── scan-file input ── */
  .scan-input-row { display: flex; gap: 6px; margin-top: 10px; }
  .scan-input { flex: 1; background: rgba(255,255,255,.05); border: 1px solid var(--border); border-radius: 6px; padding: 5px 8px; color: var(--text); font-family: var(--mono); font-size: 12px; }
  .scan-input:focus { outline: none; border-color: var(--blue); }
  .scan-result { margin-top: 10px; }

  /* ── empty state ── */
  .empty { color: var(--muted); font-size: 12px; text-align: center; padding: 24px 0; }

  /* ── filter bar ── */
  .filter-bar { display: flex; gap: 6px; margin-bottom: 10px; flex-wrap: wrap; align-items: center; }
  .filter-bar label { font-size: 11px; color: var(--muted); }
  select.filter-select { background: var(--surface); border: 1px solid var(--border); border-radius: 5px; color: var(--text); font-size: 12px; padding: 3px 6px; cursor: pointer; }
</style>
</head>
<body>

<header class="header">
  <div class="header-left">
    <div class="dot" id="lms-dot"></div>
    <span class="logo">ML-Agents <span>Dev Console</span></span>
  </div>
  <span class="ts" id="clock">--:--:--</span>
</header>

<div class="layout">

  <!-- ── SIDEBAR ── -->
  <aside class="sidebar">

    <!-- Models -->
    <div class="card">
      <div class="card-header">
        <span class="card-title">LM Studio Models</span>
        <span class="badge" id="lms-status-badge">—</span>
      </div>
      <div class="card-body" id="models-body">
        <div class="empty">Loading…</div>
      </div>
    </div>

    <!-- Watcher process -->
    <div class="card">
      <div class="card-header">
        <span class="card-title">Overnight Watcher</span>
        <div class="card-actions">
          <button class="btn success" onclick="procAction('watcher','start')">▶ Start</button>
          <button class="btn" onclick="procAction('watcher','restart')">↺ Restart</button>
          <button class="btn danger" onclick="procAction('watcher','stop')">■ Stop</button>
        </div>
      </div>
      <div class="card-body">
        <div class="proc-row">
          <span class="proc-name">all --watch 120</span>
          <span class="proc-status stopped" id="watcher-status">—</span>
        </div>
        <div id="watcher-detail" style="font-size:12px;color:var(--muted)"></div>
        <div class="progress-wrap" id="watcher-progress-wrap" style="display:none">
          <div class="progress-bar" id="watcher-progress-bar" style="width:0%"></div>
        </div>
        <div id="proc-msg" style="font-size:11px;color:var(--blue);margin-top:6px;font-family:var(--mono)"></div>
      </div>
    </div>

    <!-- Bridge process -->
    <div class="card">
      <div class="card-header">
        <span class="card-title">LLM Bridge</span>
        <div class="card-actions">
          <button class="btn success" onclick="procAction('bridge','start')">▶ Start</button>
          <button class="btn danger" onclick="procAction('bridge','stop')">■ Stop</button>
        </div>
      </div>
      <div class="card-body">
        <div class="proc-row">
          <span class="proc-name">run_llm_bridge.py</span>
          <span class="proc-status stopped" id="bridge-status">—</span>
        </div>
        <div style="font-size:11px;color:var(--muted);margin-top:4px">Model: <span style="color:var(--blue);font-family:var(--mono)">qwen3-8b</span></div>
      </div>
    </div>

    <!-- Verify -->
    <div class="card">
      <div class="card-header">
        <span class="card-title">Verify</span>
        <button class="btn primary" onclick="runVerify()" id="verify-btn">Run checks</button>
      </div>
      <div class="card-body" id="verify-body">
        <div class="empty">Press Run checks to validate model routing + scene values.</div>
      </div>
    </div>

  </aside>

  <!-- ── MAIN TOP ── -->
  <div class="main-top">

    <!-- Scan stats -->
    <div class="card">
      <div class="card-header">
        <span class="card-title">Latest Scan</span>
        <span class="ts" id="report-ts">—</span>
      </div>
      <div class="card-body">
        <div class="stat-grid">
          <div class="stat"><div class="stat-val red"    id="s-high">—</div><div class="stat-lbl">High</div></div>
          <div class="stat"><div class="stat-val orange" id="s-med">—</div><div class="stat-lbl">Medium</div></div>
          <div class="stat"><div class="stat-val blue"   id="s-low">—</div><div class="stat-lbl">Low</div></div>
        </div>
        <div style="margin-top:10px;font-size:12px;color:var(--muted)" id="scan-meta"></div>
      </div>
    </div>

    <!-- Scan-file on demand -->
    <div class="card">
      <div class="card-header">
        <span class="card-title">Scan a File</span>
      </div>
      <div class="card-body">
        <div style="font-size:12px;color:var(--muted);margin-bottom:6px">Relative path from repo root:</div>
        <div class="scan-input-row">
          <input class="scan-input" id="scan-file-input" placeholder="DevProject/Assets/…/MyScript.cs" />
          <button class="btn primary" onclick="scanFile()" id="scan-file-btn">Scan</button>
        </div>
        <div class="scan-result" id="scan-file-result"></div>
      </div>
    </div>

  </div>

  <!-- ── MAIN BOTTOM ── -->
  <div class="main-bottom card">
    <div class="card-header" style="flex-shrink:0">
      <div class="tabs" style="border:none;margin:0">
        <div class="tab active" onclick="switchTab('log')"   id="tab-log">Live Log</div>
        <div class="tab"        onclick="switchTab('issues')" id="tab-issues">Issues <span id="issues-count"></span></div>
      </div>
      <div class="card-actions">
        <select class="filter-select" id="log-src" onchange="switchLogSrc()">
          <option value="scan">Overnight log</option>
          <option value="bridge">Bridge log</option>
        </select>
        <button class="btn" onclick="clearLog()">Clear view</button>
        <button class="btn" id="autoscroll-btn" onclick="toggleAutoscroll()">⬇ Auto-scroll ON</button>
      </div>
    </div>

    <!-- Log panel -->
    <div id="panel-log" class="log-wrap" style="padding:10px;flex:1;overflow:hidden;display:flex;flex-direction:column">
      <div class="log-scroll" id="log-scroll"></div>
    </div>

    <!-- Issues panel -->
    <div id="panel-issues" style="display:none;padding:14px;flex:1;overflow:hidden;display:flex;flex-direction:column">
      <div class="filter-bar">
        <label>Severity:</label>
        <select class="filter-select" id="filter-sev" onchange="renderIssues()">
          <option value="all">All</option>
          <option value="high">High</option>
          <option value="medium">Medium</option>
          <option value="low">Low</option>
        </select>
        <label>Category:</label>
        <select class="filter-select" id="filter-cat" onchange="renderIssues()">
          <option value="all">All</option>
          <option value="multiplayer_safety">Multiplayer</option>
          <option value="ml_agents">ML-Agents</option>
          <option value="npc_dialogue">NPC Dialogue</option>
          <option value="unity_best_practices">Unity Best Practices</option>
        </select>
        <span id="filter-count" style="font-size:11px;color:var(--muted);margin-left:4px"></span>
      </div>
      <div class="issues-wrap" id="issues-list" style="flex:1"></div>
    </div>

  </div>

</div>

<script>
const API = '';
let _autoScroll = true;
let _logSrc = 'scan';
let _sse = null;
let _issues = [];
let _logLines = [];
const MAX_LOG = 500;

// ── utilities ──────────────────────────────────────────────────────────────────
const $ = id => document.getElementById(id);

function classifyLine(line) {
  if (/\[Scan\] Done\./.test(line))  return 'done';
  if (/=== Phase/.test(line))        return 'phase';
  if (/^\[Scan\]/.test(line))        return 'scan';
  if (/^\[19:|^\[20:|^\[0\d:/.test(line)) return 'ts';
  if (/warn|WARN|warning/i.test(line)) return 'warn';
  if (/error|ERROR|exception/i.test(line)) return 'error';
  return '';
}

function fmtLine(line) {
  const cls = classifyLine(line);
  const escaped = line.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
  return `<div class="log-line ${cls}">${escaped}</div>`;
}

// ── log ────────────────────────────────────────────────────────────────────────
async function loadLog() {
  const src = $('log-src').value;
  const res = await fetch(`${API}/api/logs?src=${src}&n=150`);
  const d   = await res.json();
  _logLines = d.lines || [];
  const el  = $('log-scroll');
  el.innerHTML = _logLines.map(fmtLine).join('');
  if (_autoScroll) el.scrollTop = el.scrollHeight;
}

function appendLogLine(line) {
  _logLines.push(line);
  if (_logLines.length > MAX_LOG) _logLines.shift();
  const el  = $('log-scroll');
  el.insertAdjacentHTML('beforeend', fmtLine(line));
  if (el.children.length > MAX_LOG) el.removeChild(el.firstChild);
  if (_autoScroll) el.scrollTop = el.scrollHeight;
}

function clearLog() { $('log-scroll').innerHTML = ''; _logLines = []; }

function toggleAutoscroll() {
  _autoScroll = !_autoScroll;
  $('autoscroll-btn').textContent = `⬇ Auto-scroll ${_autoScroll ? 'ON' : 'OFF'}`;
}

function startSSE() {
  if (_sse) _sse.close();
  _sse = new EventSource(`${API}/api/logs/stream?src=${_logSrc}`);
  _sse.onmessage = e => {
    if (!e.data || e.data === '') return;
    try { const o = JSON.parse(e.data); if (o.line) appendLogLine(o.line); }
    catch {}
  };
}

function switchLogSrc() {
  _logSrc = $('log-src').value;
  clearLog();
  loadLog();
  startSSE();
}

// ── tabs ───────────────────────────────────────────────────────────────────────
function switchTab(name) {
  ['log','issues'].forEach(t => {
    $('tab-'+t).classList.toggle('active', t === name);
    $('panel-'+t).style.display = t === name ? 'flex' : 'none';
  });
  if (name === 'issues') renderIssues();
}

// ── status polling ─────────────────────────────────────────────────────────────
async function pollStatus() {
  try {
    const res = await fetch(`${API}/api/status`);
    const d   = await res.json();
    updateModels(d.lms);
    updateWatcher(d.scan, d.watcher);
    updateBridge(d.bridge);
    $('clock').textContent = d.ts;
    const dot = $('lms-dot');
    dot.className = 'dot ' + (d.lms.reachable ? 'live' : '');
  } catch {}
}

function updateModels(lms) {
  const badge = $('lms-status-badge');
  if (!lms.reachable) {
    badge.textContent = 'offline'; badge.className = 'badge fail';
    $('models-body').innerHTML = '<div class="empty" style="color:var(--red)">LM Studio unreachable on port 7002</div>';
    return;
  }
  const loaded = lms.models.filter(m => m.state === 'loaded');
  badge.textContent = `${loaded.length} loaded`; badge.className = 'badge loaded';
  $('models-body').innerHTML = lms.models
    .filter(m => m.type === 'llm')
    .map(m => `
      <div class="model-row">
        <span class="model-id" title="${m.id}">${m.id}</span>
        <span class="model-meta">${m.ctx ? Math.round(m.ctx/1024)+'k ctx' : ''}</span>
        <span class="badge ${m.state === 'loaded' ? 'loaded' : 'unloaded'}">${m.state}</span>
      </div>`).join('');
}

function updateWatcher(scan, watcher) {
  if (!scan || !Object.keys(scan).length) return;
  const pid = watcher && watcher.managed_pid;
  const running = scan.running;
  const el = $('watcher-status');
  if (pid) { el.textContent = `running (pid ${pid})`; el.className = 'proc-status running'; }
  else if (running) { el.textContent = 'external'; el.className = 'proc-status external'; }
  else { el.textContent = `idle (${Math.round(scan.age_s/60)}m ago)`; el.className = 'proc-status stopped'; }

  const detail = $('watcher-detail');
  const pw = $('watcher-progress-wrap');
  if (scan.current && scan.total) {
    detail.textContent = `Run #${scan.run} · file ${scan.current}/${scan.total}`;
    const pct = Math.round(scan.current / scan.total * 100);
    $('watcher-progress-bar').style.width = pct + '%';
    pw.style.display = 'block';
  } else if (scan.last && scan.last.files) {
    detail.textContent = `Last: ${scan.last.files} files · ${scan.last.issues} issues`;
    pw.style.display = 'none';
  }
}

function updateBridge(bridge) {
  const el = $('bridge-status');
  if (bridge.running) { el.textContent = 'running'; el.className = 'proc-status running'; }
  else { el.textContent = 'stopped'; el.className = 'proc-status stopped'; }
}

// ── report ────────────────────────────────────────────────────────────────────
async function loadReport() {
  const res = await fetch(`${API}/api/report`);
  const d   = await res.json();
  if (!d || !d.files) return;
  $('s-high').textContent = d.high;
  $('s-med').textContent  = d.med;
  $('s-low').textContent  = d.low;
  $('report-ts').textContent = d.timestamp || '';
  $('scan-meta').textContent = `${d.files} files · ${Math.round(d.duration/60)}m · ${d.report}`;
  $('issues-count').textContent = `(${d.high + d.med + d.low})`;
  _issues = d.issues || [];
  renderIssues();
}

function renderIssues() {
  const sev = $('filter-sev').value;
  const cat = $('filter-cat').value;

  // We don't have per-issue severity in the parsed data — group by category heuristic
  const filtered = _issues.filter(i => {
    const catOk = cat === 'all' || i.category === cat;
    return catOk;
  });

  $('filter-count').textContent = `${filtered.length} of ${_issues.length}`;
  $('issues-list').innerHTML = filtered.length === 0
    ? '<div class="empty">No issues match the current filter.</div>'
    : filtered.map(i => `
      <div class="issue-row">
        <span class="badge ${i.category === 'multiplayer_safety' ? 'high' : i.category === 'ml_agents' ? 'medium' : 'low'}">${i.category.replace('_',' ')}</span>
        <span class="issue-file" title="${i.file}">${i.file.split('/').slice(-2).join('/')}</span>
        <div>
          <div class="issue-desc">${i.desc.replace(/</g,'&lt;')}</div>
          ${i.location ? `<div class="issue-loc">${i.location}</div>` : ''}
        </div>
      </div>`).join('');
}

// ── process actions ────────────────────────────────────────────────────────────
async function procAction(proc, action) {
  const res = await fetch(`${API}/api/${proc}/${action}`, {method:'POST'});
  const d   = await res.json();
  $('proc-msg').textContent = d.message || '';
  setTimeout(() => $('proc-msg').textContent = '', 4000);
  pollStatus();
}

// ── verify ────────────────────────────────────────────────────────────────────
async function runVerify() {
  const btn = $('verify-btn');
  btn.disabled = true; btn.textContent = 'Running…';
  $('verify-body').innerHTML = '<div class="empty">Running checks…</div>';
  try {
    const res = await fetch(`${API}/api/verify`, {method:'POST'});
    const d   = await res.json();
    if (d.error) { $('verify-body').innerHTML = `<div style="color:var(--red);font-size:12px">${d.error}</div>`; return; }
    $('verify-body').innerHTML = d.results.map(r => `
      <div class="verify-row">
        <span class="verify-icon">${r.passed ? '✓' : '✗'}</span>
        <span class="verify-name">${r.name}</span>
        ${r.detail ? `<span class="verify-detail">${r.detail}</span>` : ''}
        <span class="badge ${r.passed ? 'pass' : 'fail'}">${r.passed ? 'PASS' : 'FAIL'}</span>
      </div>`).join('') +
      `<div style="margin-top:10px;font-size:12px;font-weight:600;color:${d.passed===d.total?'var(--green)':'var(--red)'}">
        ${d.passed}/${d.total} passed
      </div>`;
  } finally { btn.disabled = false; btn.textContent = 'Run checks'; }
}

// ── scan file ────────────────────────────────────────────────────────────────
async function scanFile() {
  const file = $('scan-file-input').value.trim();
  if (!file) return;
  const btn = $('scan-file-btn');
  btn.disabled = true; btn.textContent = '…';
  $('scan-file-result').innerHTML = '<div style="color:var(--muted);font-size:12px">Scanning… (may take 60–120s)</div>';
  try {
    const res = await fetch(`${API}/api/scan/file`, {
      method: 'POST', headers: {'Content-Type':'application/json'},
      body: JSON.stringify({file})
    });
    const d = await res.json();
    if (d.error && !d.issues) {
      $('scan-file-result').innerHTML = `<div style="color:var(--red);font-size:12px">Error: ${d.error}</div>`;
    } else {
      const badge = `<span class="badge ${d.severity || 'none'}">${d.severity || 'none'}</span>`;
      const issues = (d.issues||[]).map(i =>
        `<div style="margin-top:6px;font-size:12px"><b style="color:var(--blue)">[${i.category}]</b> ${i.description.replace(/</g,'&lt;')}</div>`
      ).join('');
      $('scan-file-result').innerHTML = `${badge} ${d.summary || ''} ${issues}
        <div style="font-size:11px;color:var(--muted);margin-top:6px">${d.duration}s</div>`;
    }
  } finally { btn.disabled = false; btn.textContent = 'Scan'; }
}

// ── init ──────────────────────────────────────────────────────────────────────
(async () => {
  await loadLog();
  startSSE();
  await pollStatus();
  await loadReport();
  setInterval(pollStatus, 5000);
  setInterval(loadReport, 30000);
})();
</script>
</body>
</html>"""


# ── CLI entry point ────────────────────────────────────────────────────────────

def run_dashboard_cli(host: str = "127.0.0.1", port: int = 8765) -> None:
    try:
        import uvicorn
    except ImportError:
        print("[Dashboard] uvicorn not found. Install with:  pip install fastapi uvicorn")
        sys.exit(1)

    os.environ.setdefault("PYTHONIOENCODING", "utf-8")
    # Reconfigure stdout/stderr to utf-8 for the current process so that
    # verify.py's box-drawing characters (──) don't crash on Windows cp1252
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    print(f"[Dashboard] Starting at http://{host}:{port}")
    app = build_app()
    uvicorn.run(app, host=host, port=port, log_level="warning")
