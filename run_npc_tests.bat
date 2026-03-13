@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: NpcDialogue Python Test Runner
:: Runs all 40 pytest tests without needing Unity open.
:: Training-efficiency tests auto-skip if no results CSV exists yet.
:: ─────────────────────────────────────────────────────────────────────────────
setlocal

set PYTHON=C:\Users\andre_wjgj23f\miniconda3\envs\mlagents\python.exe
set REPO=D:\GithubRepos\ml-agents
set TESTS=%REPO%\ml-agents\tests\test_npc_dialogue_training.py

echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║          NpcDialogue Test Suite                      ║
echo  ╚══════════════════════════════════════════════════════╝
echo.

cd /d "%REPO%"

%PYTHON% -m pytest "%TESTS%" -v --noconftest --tb=short

echo.
pause
