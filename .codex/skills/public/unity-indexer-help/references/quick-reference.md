# Unity Indexer Quick Reference

## Indexer Management
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
.\.venv\Scripts\python.exe enhanced_indexer.py --reset --include-packages --analyze-usage
.\.venv\Scripts\python.exe enhanced_indexer.py --fast --include-packages
.\.venv\Scripts\python.exe enhanced_indexer.py --incremental --include-packages
Get-Content -Path indexer_run.log -Tail 100
```

## Semantic Code Search
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
.\.venv\Scripts\python.exe qdrant_query.py search "network synchronization"
.\.venv\Scripts\python.exe qdrant_query.py search --kind class "player controller"
.\.venv\Scripts\python.exe qdrant_query.py search --component-type Action "navigate"
.\.venv\Scripts\python.exe qdrant_query.py search --assembly Unity.Behavior "action"
.\.venv\Scripts\python.exe qdrant_query.py keyword "NetworkBehaviour"
```

## Behavior Graph Search
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
.\.venv\Scripts\python.exe qdrant_query.py behaviors --type Action
.\.venv\Scripts\python.exe qdrant_query.py behaviors --type Condition
.\.venv\Scripts\python.exe qdrant_query.py behaviors --type Composite
.\.venv\Scripts\python.exe qdrant_query.py behaviors --type Modifier
.\.venv\Scripts\python.exe qdrant_query.py behaviors --network
.\.venv\Scripts\python.exe qdrant_query.py behaviors --category Navigation
```

## Assembly Queries
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
.\.venv\Scripts\python.exe qdrant_query.py assembly list
.\.venv\Scripts\python.exe qdrant_query.py assembly info Unity.Behavior
.\.venv\Scripts\python.exe qdrant_query.py assembly deps Assembly-CSharp
.\.venv\Scripts\python.exe qdrant_query.py assembly dependents Unity.Netcode.Runtime
```

## Requirements
- Qdrant: localhost:6333
- LM Studio: localhost:7002 (for embeddings)
- Python: qdrant-client, requests, python-dotenv
