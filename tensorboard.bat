@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: TensorBoard Launcher
:: Opens http://localhost:6006 and watches the results/ directory.
:: Run this in a separate terminal while training is in progress
:: (or after training to review past runs).
:: ─────────────────────────────────────────────────────────────────────────────
setlocal

set PYTHON=C:\Users\andre_wjgj23f\miniconda3\envs\mlagents\python.exe
set RESULTS=D:\GithubRepos\ml-agents\results

echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║               TensorBoard Viewer                     ║
echo  ╚══════════════════════════════════════════════════════╝
echo.
echo  Watching: %RESULTS%
echo  Open:     http://localhost:6006
echo.
echo  Graphs to watch:
echo    Environment/Cumulative Reward   — overall learning curve
echo    NpcDialogue/Feedback/HasEffect  — effects fired per window
echo    NpcDialogue/Latency/TotalMs     — LM Studio response speed
echo    NpcDialogue/Reward/*            — reward component breakdown
echo.

:: Open browser automatically
start "" "http://localhost:6006"

%PYTHON% -m tensorboard.main --logdir="%RESULTS%" --port=6006

pause
