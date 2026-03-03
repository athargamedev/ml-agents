@echo off
:: ─────────────────────────────────────────────────────────────────────────────
:: NpcDialogue Training Launcher
::
:: Usage:
::   Double-click this file  — starts a new training run
::   Pass a run-id argument  — train_npc_dialogue.bat myrun_v2
::
:: Before running:
::   1. Open Unity Editor → DevProject
::   2. Do NOT press Play yet
::   3. Run this script — it delegates to run_training.py, which cleans up
::      stale port 5004 holders before starting the trainer
::   4. Then press Play in Unity
::
:: Results land in:  results\<run_id>\
:: TensorBoard:      run tensorboard.bat in a separate terminal
:: ─────────────────────────────────────────────────────────────────────────────
setlocal

set PYTHON=C:\Users\andre_wjgj23f\miniconda3\envs\mlagents\python.exe
set REPO=D:\GithubRepos\ml-agents
set CONFIG=%REPO%\config\ppo\NpcDialogue.yaml
set TRAINER=%REPO%\run_training.py

:: Auto-generate a timestamped run ID if none provided.
:: Use PowerShell for a locale-independent timestamp with second precision.
if "%~1"=="" (
    for /f %%i in ('powershell -NoProfile -Command "(Get-Date).ToString('yyyyMMdd_HHmmss')"') do (
        set RUN_ID=npc_dialogue_%%i
    )
) else (
    set RUN_ID=%~1
)

echo.
echo  ╔══════════════════════════════════════════════════════╗
echo  ║        NpcDialogue ML-Agents Training                ║
echo  ╚══════════════════════════════════════════════════════╝
echo.
echo  Run ID   : %RUN_ID%
echo  Config   : %CONFIG%
echo  Wrapper  : %TRAINER%
echo  Results  : %REPO%\results\%RUN_ID%
echo.

:: Confirm GPU is available before starting
%PYTHON% -c "import torch; gpu=torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'NOT FOUND'; vram=round(torch.cuda.get_device_properties(0).total_memory/1024**3,1) if torch.cuda.is_available() else 0; print(f'  Device   : {gpu} ({vram} GB VRAM)') if torch.cuda.is_available() else print('  Device   : CPU only — CUDA not available!')"
echo.
echo  ► Now press Play in Unity Editor to connect...
echo.

cd /d "%REPO%"

if not exist "%TRAINER%" (
    echo  ERROR: Training wrapper not found: %TRAINER%
    pause
    exit /b 1
)

%PYTHON% "%TRAINER%" --run-id "%RUN_ID%"

set EXIT_CODE=%ERRORLEVEL%

echo.
if not "%EXIT_CODE%"=="0" (
    echo  Training failed with exit code %EXIT_CODE%.
    echo  The training wrapper did not start a new run.
    echo  If the run ID already exists, use a different name or run run_training.py --resume.
    pause
    exit /b %EXIT_CODE%
)

echo  Training complete. Results in: results\%RUN_ID%
echo  Run tensorboard.bat to visualise.
pause
