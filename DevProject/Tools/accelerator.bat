@echo off
REM Unity Accelerator Quick Commands
REM Save in PATH or DevProject/Tools/

if "%1"=="" goto status
if "%1"=="start" goto start
if "%1"=="stop" goto stop
if "%1"=="restart" goto restart
if "%1"=="status" goto status
if "%1"=="info" goto status
if "%1"=="clear" goto clear
if "%1"=="dashboard" goto dashboard

echo Usage: accelerator.bat [start^|stop^|restart^|status^|clear^|dashboard]
echo.
echo Commands:
echo   start     - Start Unity Accelerator service
echo   stop      - Stop Unity Accelerator service
echo   restart   - Restart Unity Accelerator service
echo   status    - Show status and cache info
echo   clear     - Clear the cache
echo   dashboard - Open dashboard in browser
exit /b 1

:start
echo Starting Unity Accelerator...
net start "UnityAccelerator"
goto done

:stop
echo Stopping Unity Accelerator...
net stop "UnityAccelerator"
goto done

:restart
echo Restarting Unity Accelerator...
net stop "UnityAccelerator"
timeout /t 2 /nobreak >nul
net start "UnityAccelerator"
goto done

:status
echo.
echo ==========================================
echo        Unity Accelerator Status
echo ==========================================
powershell -Command "Get-Service -Name 'UnityAccelerator' | Select-Object Status" | findstr /I "Running"
if %errorlevel%==0 (
    echo Status: RUNNING
) else (
    echo Status: NOT RUNNING
)
echo.
echo ==========================================
echo        Unity Accelerator Status
echo ==========================================
net start | findstr "UnityAccelerator"
if errorlevel 1 (
    echo Status: NOT RUNNING
) else (
    echo Status: RUNNING
)
echo.
"D:\UnityAcelerator\unity-accelerator.exe" cache info
goto done

:clear
echo Clearing cache...
"D:\UnityAcelerator\unity-accelerator.exe" cache delete --force
goto done

:dashboard
start http://localhost/dashboard/
goto done

:done
echo Done.
