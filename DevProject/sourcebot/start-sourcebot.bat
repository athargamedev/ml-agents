@echo off
setlocal
cd /d "%~dp0"

REM Sourcebot Startup Script for Windows.
REM Uses docker compose so startup matches the checked-in config.

echo ==========================================
echo   Sourcebot for Unity ML-Agents
echo ==========================================
echo.

REM Check if Docker is running
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Docker is not running. Please start Docker Desktop.
    sc query com.docker.service | findstr /I "STATE" | findstr /I "STOPPED" >nul 2>&1
    if %errorlevel% equ 0 (
        echo NOTE: com.docker.service is stopped. Start Docker Desktop from an elevated shell or start the service as Administrator.
    )
    pause
    exit /b 1
)

if not exist ".env" (
    echo Creating sourcebot\.env with fresh local auth secrets...
    powershell -NoProfile -Command ^
        "$auth=[Convert]::ToBase64String((1..33 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }));" ^
        "$enc=[Convert]::ToBase64String((1..24 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }));" ^
        "@('AUTH_URL=http://localhost:8090','AUTH_SECRET=' + $auth,'SOURCEBOT_ENCRYPTION_KEY=' + $enc,'LM_STUDIO_TOKEN=') | Set-Content -Encoding ascii .env"
)

for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "$line = Get-Content .env | Where-Object { $_ -match '^LM_STUDIO_TOKEN=' } | Select-Object -First 1; if ($line) { $line.Substring(16) }"`) do set "LM_STUDIO_TOKEN_FROM_FILE=%%A"

if "%LM_STUDIO_TOKEN%"=="" if "%LM_STUDIO_TOKEN_FROM_FILE%"=="" (
    echo ERROR: LM_STUDIO_TOKEN is not set.
    echo Add it to sourcebot\.env or set it in your shell before launching.
    exit /b 1
)

if not exist "runtime-v4152" mkdir runtime-v4152

echo Sourcebot source:  D:\GithubRepos\sourcebot
echo Sourcebot image:   sourcebot-local-unity:latest
echo Sourcebot URL:   http://localhost:8090
echo Local repo:      D:\GithubRepos\ml-agents
echo Config path:     %CD%\config.json
echo LM Studio API:   http://127.0.0.1:7002
echo.

echo Building local Sourcebot image from D:\GithubRepos\sourcebot ...
docker compose build sourcebot
if %errorlevel% neq 0 (
    echo ERROR: Failed to build the local Sourcebot image
    exit /b 1
)

echo Starting Sourcebot with docker compose...
echo.

docker compose up -d --remove-orphans
if %errorlevel% equ 0 (
    echo ==========================================
    echo   Sourcebot started successfully!
    echo ==========================================
    echo.
    echo Web UI:          http://localhost:8090
    echo Repo source:     local ml-agents checkout
    echo Ask model:       qwen2.5-coder-7b-instruct@q8_0
    echo Verify status:   .\check-sourcebot.ps1 -WaitForIndex
    echo Logs:            docker compose logs -f
    echo Stop:            docker compose down
    echo Restart:         docker compose restart
) else (
    echo ERROR: Failed to start Sourcebot container
    exit /b 1
)

pause
