# Qdrant Search Reference

## Contents
- Quick Start (CLI Helper)
- Semantic Search (Python)
- Filters and Fields
- Payload Fields

## Quick Start (CLI Helper)
```powershell
Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
.\.venv\Scripts\python.exe qdrant_query.py search "network synchronization"
.\.venv\Scripts\python.exe qdrant_query.py search --kind class "player controller"
.\.venv\Scripts\python.exe qdrant_query.py search --component-type Action "navigate"
.\.venv\Scripts\python.exe qdrant_query.py keyword "NetworkBehaviour"
```

## Semantic Search (Python)
```python
import os
import sys
from dotenv import load_dotenv
import requests
from qdrant_client import QdrantClient
from qdrant_client.models import Filter, FieldCondition, MatchValue, MatchText

load_dotenv()

QDRANT_HOST = os.getenv("QDRANT_HOST", "localhost")
QDRANT_PORT = int(os.getenv("QDRANT_PORT", "6333"))
LMSTUDIO_URL = os.getenv("LMSTUDIO_BASE_URL", "http://127.0.0.1:7002")
EMBED_MODEL = os.getenv("LMSTUDIO_EMBED_MODEL", "text-embedding-nomic-embed-text-v1.5")

def get_embedding(text: str) -> list:
    resp = requests.post(
        f"{LMSTUDIO_URL}/v1/embeddings",
        json={"model": EMBED_MODEL, "input": text},
    )
    return resp.json()["data"][0]["embedding"]

def search_code(query: str, kind: str = None, namespace: str = None, limit: int = 10):
    client = QdrantClient(QDRANT_HOST, port=QDRANT_PORT)

    conditions = []
    if kind:
        conditions.append(FieldCondition(key="kind", match=MatchValue(value=kind)))
    if namespace:
        conditions.append(FieldCondition(key="namespace", match=MatchText(text=namespace)))

    query_filter = Filter(must=conditions) if conditions else None

    embedding = get_embedding(query)
    results = client.search(
        collection_name="unity_codebase",
        query_vector=embedding,
        query_filter=query_filter,
        limit=limit,
        with_payload=True,
    )
    return results

if __name__ == "__main__":
    query = sys.argv[1] if len(sys.argv) > 1 else "network synchronization"
    results = search_code(query, limit=10)

    for r in results:
        p = r.payload
        print(f"\n[{r.score:.3f}] {p.get('kind','?')}: {p.get('namespace','')}.{p.get('name','')}")
        print(f"  File: {p.get('file_path','')}")
        if p.get("summary"):
            print(f"  Summary: {p.get('summary','')[:200]}")
```

## Filters and Fields
- kind: class, struct, interface, enum, record, method, property, field, event, delegate, namespace
- unity_component_type: MonoBehaviour, ScriptableObject, NetworkBehaviour, Action, Condition, Composite, Modifier
- namespace: MatchText filter for partial namespace matches
- file_path: MatchText filter for path patterns

## Payload Fields
- name, kind, namespace, file_path
- summary, body, signature
- unity_component_type, base_types, interfaces_implemented
- is_package, is_editor_only, is_test_code
- importance_score, design_patterns
- node_description_name, node_description_story
- authority_pattern, rpc_bridge_class
