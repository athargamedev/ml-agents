param(
    [switch]$WaitForIndex,
    [int]$WaitSeconds = 600,
    [int]$PollSeconds = 10
)

$ErrorActionPreference = "Stop"

$sourcebotDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envPath = Join-Path $sourcebotDir ".env"
$configPath = Join-Path $sourcebotDir "config.json"
$containerName = "sourcebot-ml-agents"

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host $Title
    Write-Host ("-" * $Title.Length)
}

function Get-DotEnvValue {
    param([string]$Key)
    if (-not (Test-Path $envPath)) {
        return $null
    }

    $line = Get-Content $envPath | Where-Object { $_ -like "$Key=*" } | Select-Object -First 1
    if (-not $line) {
        return $null
    }

    return $line.Substring($Key.Length + 1)
}

function Get-ContainerShOutput {
    param([string]$Command)

    $result = & docker exec $containerName sh -lc $Command
    if ($LASTEXITCODE -ne 0) {
        throw "docker exec failed for command: $Command"
    }

    return ($result | Out-String).Trim()
}

function Get-IndexFileCount {
    $value = Get-ContainerShOutput "find /data/.sourcebot/index -type f 2>/dev/null | wc -l"
    return [int]$value
}

function Get-ActiveIndexerCount {
    $value = Get-ContainerShOutput "ps -ef | grep '[z]oekt-git-index' | wc -l"
    return [int]$value
}

function Get-RepoBranch {
    return Get-ContainerShOutput "git -C /repos/ml-agents rev-parse --abbrev-ref HEAD"
}

Write-Section "Container"
$containerInfo = & docker ps --filter "name=^/${containerName}$" --format "{{.Image}}|{{.Status}}"
if (-not $containerInfo) {
    throw "Sourcebot container '$containerName' is not running."
}

$parts = $containerInfo.Split("|", 2)
Write-Host ("Image:   {0}" -f $parts[0])
Write-Host ("Status:  {0}" -f $parts[1])
Write-Host ("Branch:  {0}" -f (Get-RepoBranch))

Write-Section "HTTP"
$rootResponse = Invoke-WebRequest -Uri "http://127.0.0.1:8090" -UseBasicParsing -TimeoutSec 20
$sessionResponse = Invoke-WebRequest -Uri "http://127.0.0.1:8090/api/auth/session" -UseBasicParsing -TimeoutSec 20
Write-Host ("Root:    HTTP {0}" -f $rootResponse.StatusCode)
Write-Host ("Session: {0}" -f $sessionResponse.Content)

Write-Section "Logs"
$recentLogs = (& cmd /c "docker logs --tail 200 $containerName 2>&1" | Out-String)
$hasAnonymousAccess = $recentLogs -match "Anonymous access enabled"
$hasJwtError = $recentLogs -match "JWTSessionError"
Write-Host ("Anonymous access log: {0}" -f ($(if ($hasAnonymousAccess) { "present" } else { "missing" })))
Write-Host ("JWT session errors:   {0}" -f ($(if ($hasJwtError) { "present" } else { "none found" })))

Write-Section "LM Studio"
$config = Get-Content $configPath | ConvertFrom-Json
$modelName = $config.models[0].model
$token = Get-DotEnvValue "LM_STUDIO_TOKEN"
if (-not $token) {
    throw "LM_STUDIO_TOKEN is missing from sourcebot/.env"
}

$modelsResponse = Invoke-RestMethod -Uri "http://127.0.0.1:7002/v1/models" -Headers @{ Authorization = "Bearer $token" } -Method Get -TimeoutSec 20
$availableModels = @($modelsResponse.data | ForEach-Object { $_.id })
$modelPresent = $availableModels -contains $modelName
Write-Host ("Configured model: {0}" -f $modelName)
Write-Host ("Model available:  {0}" -f ($(if ($modelPresent) { "yes" } else { "no" })))

$probeBody = @{
    model = $modelName
    messages = @(
        @{
            role = "user"
            content = "Reply with READY only"
        }
    )
    max_tokens = 8
} | ConvertTo-Json -Depth 5

$probeResponse = Invoke-RestMethod -Uri "http://127.0.0.1:7002/v1/chat/completions" -Headers @{ Authorization = "Bearer $token" } -Method Post -ContentType "application/json" -Body $probeBody -TimeoutSec 60
$probeText = $probeResponse.choices[0].message.content
Write-Host ("Probe reply:      {0}" -f $probeText)

Write-Section "Index"
if ($WaitForIndex) {
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    do {
        $indexFileCount = Get-IndexFileCount
        $activeIndexerCount = Get-ActiveIndexerCount
        Write-Host ("Index files: {0} | Active indexers: {1}" -f $indexFileCount, $activeIndexerCount)

        if ($indexFileCount -gt 0) {
            break
        }

        if ((Get-Date) -ge $deadline) {
            break
        }

        Start-Sleep -Seconds $PollSeconds
    } while ($true)
} else {
    $indexFileCount = Get-IndexFileCount
    $activeIndexerCount = Get-ActiveIndexerCount
}

$indexSize = Get-ContainerShOutput "du -sh /data/.sourcebot/index 2>/dev/null | cut -f1"
Write-Host ("Index files:      {0}" -f $indexFileCount)
Write-Host ("Active indexers:  {0}" -f $activeIndexerCount)
Write-Host ("Index size:       {0}" -f $indexSize)

if ($indexFileCount -eq 0 -and $activeIndexerCount -gt 0) {
    Write-Host "Status:           initial index still running"
} elseif ($indexFileCount -gt 0) {
    Write-Host "Status:           search shards detected"
} else {
    Write-Host "Status:           index is empty"
}
