@echo off
REM Sourcebot Startup Script for Windows
REM 
REM Prerequisites:
REM   1. Docker Desktop is running
REM   2. LM Studio is running with server started (port 7002)

echo ==========================================
echo   Sourcebot for Unity ML-Agents
echo ==========================================
echo.

REM Check if Docker is running
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Docker is not running. Please start Docker Desktop.
    pause
    exit /b 1
)

REM Get script directory
set SCRIPT_DIR=%~dp0
set CONFIG_PATH=%SCRIPT_DIR%config.json
set PROJECT_PATH=D:\GithubRepos\ml-agents

echo Project path: %PROJECT_PATH%
echo Config path: %CONFIG_PATH%
echo Port: 8090
echo LM Studio: http://127.0.0.1:7002
echo.

REM Stop existing container
echo Stopping existing Sourcebot container...
docker stop sourcebot-ml-agents 2>nul
docker rm sourcebot-ml-agents 2>nul

echo Starting Sourcebot container...
echo.

REM Run Sourcebot - mounting parent ml-agents repo which contains DevProject
docker run -d ^
    --name sourcebot-ml-agents ^
    -p 8090:3000 ^
    -v "%PROJECT_PATH%:/data/repos/ml-agents:ro" ^
    -v "%CONFIG_PATH%:/data/config.json:ro" ^
    -e CONFIG_PATH=/data/config.json ^
    -e FORCE_ENABLE_ANONYMOUS_ACCESS=true ^
    --add-host=host.docker.internal:host-gateway ^
    ghcr.io/sourcebot-dev/sourcebot:latest

if %errorlevel% equ 0 (
    echo ==========================================
    echo   Sourcebot started successfully!
    echo ==========================================
    echo.
    echo Web UI: http://localhost:8090
    echo.
    echo Useful commands:
    echo   docker logs -f sourcebot-ml-agents   - View logs
    echo   docker stop sourcebot-ml-agents    - Stop
    echo   docker restart sourcebot-ml-agents  - Restart
    echo.
    echo Note: Full ml-agents repo is indexed.
    echo   Use repo:ml-agents filter in searches.
    echo.
) else (
    echo ERROR: Failed to start Sourcebot container
    pause
    exit /b 1
)

pause
