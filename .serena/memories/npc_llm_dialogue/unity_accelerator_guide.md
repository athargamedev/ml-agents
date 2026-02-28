# Unity Accelerator - Complete Management Guide

## Installation Location

Your Installation: `D:\UnityAcelerator`

The executable is at: `D:\UnityAcelerator\unity-accelerator.exe`

Default: `C:\Program Files\Unity\accelerator`

The executable is at: `C:\Program Files\Unity\accelerator\unity-accelerator.exe`

---

## Quick Restart Commands

### Windows (PowerShell)
```powershell
# Stop
net stop "Unity Accelerator"

# Start
net start "Unity Accelerator"

# Or using the CLI directly
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" service --stop
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" service --start
```

### Windows (CMD)
```cmd
net stop "Unity Accelerator"
net start "Unity Accelerator"
```

---

## Common CLI Commands

### Check Status
```powershell
# Basic status
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe"

# With full output
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" version

# Service status
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" service --status
```

### Start/Stop Service
```powershell
# Stop the service
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" service --stop

# Start the service
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" service --start

# Restart (stop + start)
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" service --stop
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" service --start
```

### View Dashboard
```powershell
# Get dashboard URL
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" dashboard url
```

Default: `http://localhost:80/dashboard/`

### Cache Management
```powershell
# Show cache info
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" cache info

# Show cache size
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" cache size

# Clear cache (WARNING: removes all cached assets)
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" cache delete

# Clear by age (e.g., older than 7 days)
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" cache delete --older-than 7d
```

### Help
```powershell
# General help
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" --help

# All commands help
& "C:\Program Files\Unity\accelerator\unity-accelerator.exe" --all-help
```

---

## Configuration File

Location: `C:\Program Files\Unity\accelerator\unity-accelerator.cfg`

### Key Settings
```ini
[cache]
# Maximum cache size in GB (default: 50)
maxSizeGB = 50

# Cache location
path = C:\ProgramData\Unity\accelerator\cache

[server]
# Port for the accelerator
port = 10080

[dashboard]
# Dashboard port
httpPort = 80
httpsPort = 443

# Dashboard credentials
user = admin
password = YOUR_PASSWORD
```

---

## Check if Running

```powershell
# Method 1: Check service status
Get-Service "Unity Accelerator"

# Method 2: Check if process is running
Get-Process -Name "unity-accelerator" -ErrorAction SilentlyContinue

# Method 3: Try to access dashboard
Invoke-WebRequest -Uri "http://localhost:80" -TimeoutSec 5 -ErrorAction SilentlyContinue
```

---

## Auto-Start on Windows Boot

```powershell
# Enable auto-start
Set-Service -Name "Unity Accelerator" -StartupType Automatic

# Disable auto-start
Set-Service -Name "Unity Accelerator" -StartupType Manual
```

---

## Usage with Unity Editor

### Configure in Unity
1. Open Unity → Edit → Preferences → Package Manager
2. Or: Project Settings → Package Manager
3. Add Accelerator: `http://localhost:10080`

### Environment Variable (Optional)
```powershell
# Set cache server globally
$env:UNITY_ACCELERATOR_CACHE_SERVER = "http://localhost:10080"
```

---

## Troubleshooting

```powershell
# View logs
Get-Content "C:\ProgramData\Unity\accelerator\logs\unity-accelerator.log" -Tail 50

# Or in installation directory
Get-Content "C:\Program Files\Unity\accelerator\unity-accelerator.log" -Tail 50
```

### Common Issues
| Issue | Solution |
|-------|----------|
| Service won't start | Check logs in `C:\Program Files\Unity\accelerator\` |
| Port conflict | Change port in `unity-accelerator.cfg` |
| Cache full | Increase `maxSizeGB` or clear cache |

---

## Script: Quick Restart Script

```powershell
# restart-unity-accelerator.ps1
param(
    [switch]$ClearCache,
    [switch]$Status
)

$exe = "C:\Program Files\Unity\accelerator\unity-accelerator.exe"

if ($Status) {
    & $exe version
    exit
}

Write-Host "Stopping Unity Accelerator..." -ForegroundColor Yellow
& $exe service --stop
Start-Sleep -Seconds 2

if ($ClearCache) {
    Write-Host "Clearing cache..." -ForegroundColor Yellow
    & $exe cache delete --force
}

Write-Host "Starting Unity Accelerator..." -ForegroundColor Yellow
& $exe service --start

Write-Host "Done!" -ForegroundColor Green
& $exe version
```

---

## References

- [Unity Accelerator Docs](https://docs.unity3d.com/Manual/UnityAccelerator.html)
- [Command Line Reference](https://docs.unity3d.com/Manual/accelerator-command-line.html)
- [Stop/Restart Guide](https://docs.unity3d.com/Manual/accelerator-stop-restart.html)
