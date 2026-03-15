# Indexer Commands Reference

## Contents
- Check Status
- Start Indexing (Full)
- Start Indexing (Fast)
- Start Indexing (Reset + Full)
- Stop Indexer
- View Logs
- Check Progress Details
- Incremental Indexing

## Check Status
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -match "enhanced_indexer.py" } |
    Select-Object ProcessId, CommandLine
```

```python
from qdrant_client import QdrantClient

c = QdrantClient("localhost", port=6333)
for col in c.get_collections().collections:
    info = c.get_collection(col.name)
    print(f"{col.name}: {info.points_count} points, status={info.status}")
```

## Start Indexing (Full)
```powershell
$python = "D:\\VScode_presets\\UnityAITemplate\\unity-code-indexer\\.venv\\Scripts\\python.exe"
Start-Process -FilePath $python `
    -ArgumentList "enhanced_indexer.py --include-packages --analyze-usage" `
    -WorkingDirectory "D:\\VScode_presets\\UnityAITemplate\\unity-code-indexer" `
    -RedirectStandardOutput "indexer_run.log" `
    -RedirectStandardError "indexer_run.log" `
    -WindowStyle Hidden
```

## Start Indexing (Fast - No Summaries)
```powershell
$python = "D:\\VScode_presets\\UnityAITemplate\\unity-code-indexer\\.venv\\Scripts\\python.exe"
Start-Process -FilePath $python `
    -ArgumentList "enhanced_indexer.py --fast --include-packages" `
    -WorkingDirectory "D:\\VScode_presets\\UnityAITemplate\\unity-code-indexer" `
    -RedirectStandardOutput "indexer_run.log" `
    -RedirectStandardError "indexer_run.log" `
    -WindowStyle Hidden
```

## Start Indexing (Reset + Full)
```powershell
$python = "D:\\VScode_presets\\UnityAITemplate\\unity-code-indexer\\.venv\\Scripts\\python.exe"
Start-Process -FilePath $python `
    -ArgumentList "enhanced_indexer.py --reset --include-packages --analyze-usage" `
    -WorkingDirectory "D:\\VScode_presets\\UnityAITemplate\\unity-code-indexer" `
    -RedirectStandardOutput "indexer_run.log" `
    -RedirectStandardError "indexer_run.log" `
    -WindowStyle Hidden
```

## Stop Indexer
```powershell
Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -match "enhanced_indexer.py" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

## View Logs
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
Get-Content -Path indexer_run.log -Tail 100
```

## Check Progress Details
```python
from qdrant_client import QdrantClient

c = QdrantClient("localhost", port=6333)
info = c.get_collection("unity_codebase")
print("=== unity_codebase ===")
print(f"Points: {info.points_count}")
print(f"Status: {info.status}")

results, _ = c.scroll(
    "unity_codebase",
    limit=5,
    with_payload=["name", "kind", "namespace", "summary_ok"],
)
for p in results:
    payload = p.payload
    print(
        f"  - {payload.get('kind','?')}: "
        f"{payload.get('namespace','')}.{payload.get('name','')} "
        f"(summary: {payload.get('summary_ok', False)})"
    )
```

## Incremental Indexing
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
.\.venv\Scripts\python.exe enhanced_indexer.py --incremental --include-packages
```
