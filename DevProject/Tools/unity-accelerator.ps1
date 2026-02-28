# Unity Accelerator Manager Script
# Save as: DevProject/Tools/unity-accelerator.ps1

param(
    [ValidateSet("start", "stop", "restart", "status", "info", "clear-cache", "dashboard")]
    [string]$Action = "status",

    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Configuration - Auto-detected
$AcceleratorExe = "D:\UnityAcelerator\unity-accelerator.exe"
$AcceleratorPath = "D:\UnityAcelerator"
$ServiceName = "UnityAccelerator"
$DashboardPort = 80

# Colors
function Write-Green { param([string]$m) Write-Host $m -ForegroundColor Green }
function Write-Yellow { param([string]$m) Write-Host $m -ForegroundColor Yellow }
function Write-Red { param([string]$m) Write-Host $m -ForegroundColor Red }
function Write-Cyan { param([string]$m) Write-Host $m -ForegroundColor Cyan }

# Check if accelerator exists
if (-not (Test-Path $AcceleratorExe)) {
    Write-Red "Unity Accelerator not found at: $AcceleratorExe"
    Write-Host "Is it installed? Download from: https://unity.com/accelerator"
    exit 1
}

# Helper: Get service status
function Get-ServiceStatus {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        return @{
            Name = $svc.Name
            Status = $svc.Status
            StartType = $svc.StartType
        }
    }
    return $null
}

# Action: Status
if ($Action -eq "status" -or $Action -eq "info") {
    Write-Cyan "=========================================="
    Write-Cyan "       Unity Accelerator Status"
    Write-Cyan "=========================================="
    
    $svc = Get-ServiceStatus
    if ($svc) {
        Write-Host "Service Name: " -NoNewline
        Write-Host $svc.Name
        Write-Host "Status: " -NoNewline
        if ($svc.Status -eq "Running") {
            Write-Green $svc.Status
        } else {
            Write-Red $svc.Status
        }
        Write-Host "Start Type: $($svc.StartType)"
    } else {
        Write-Red "Service '$ServiceName' not found!"
    }
    
    Write-Host ""
    Write-Host "Version:"
    & "$AcceleratorPath\unity-accelerator.exe" --version 2>&1 | Select-Object -First 3
    
    Write-Host ""
    Write-Host "Cache location: $AcceleratorPath"
    exit 0
}

# Action: Start
if ($Action -eq "start") {
    Write-Yellow "Starting Unity Accelerator..."
    Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    $svc = Get-ServiceStatus
    if ($svc.Status -eq "Running") {
        Write-Green "Started successfully!"
    } else {
        Write-Red "Failed to start. Try running as Administrator."
    }
    exit 0
}

# Action: Stop
if ($Action -eq "stop") {
    Write-Yellow "Stopping Unity Accelerator..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    $svc = Get-ServiceStatus
    if ($svc.Status -eq "Stopped") {
        Write-Green "Stopped successfully!"
    } else {
        Write-Red "Failed to stop. Try running as Administrator."
    }
    exit 0
}

# Action: Restart
if ($Action -eq "restart") {
    Write-Yellow "Restarting Unity Accelerator..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    $svc = Get-ServiceStatus
    if ($svc.Status -eq "Running") {
        Write-Green "Restarted successfully!"
    } else {
        Write-Red "Failed to restart. Try running as Administrator."
    }
    exit 0
}

# Action: Clear Cache
if ($Action -eq "clear-cache") {
    Write-Yellow "Cache clear requested..."
    
    if (-not $Force) {
        Write-Host "This will delete ALL cached assets."
        $confirm = Read-Host "Are you sure? (y/N)"
        if ($confirm -ne "y" -and $confirm -ne "Y") {
            Write-Host "Cancelled."
            exit 0
        }
    }
    
    Write-Yellow "Clearing cache..."
    & "$AcceleratorPath\unity-accelerator.exe" cache delete --force
    Write-Green "Cache cleared!"
    exit 0
}

# Action: Dashboard
if ($Action -eq "dashboard") {
    $url = "http://localhost:$DashboardPort/dashboard/"
    Write-Host "Opening dashboard: $url"
    Start-Process $url
    exit 0
}
