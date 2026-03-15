---
name: qdrant-search
description: Semantic and keyword search over the indexed Unity codebase in Qdrant. Use when the user asks to find code, search classes/methods/types, or mentions /search or /code.
---

# Qdrant Code Search

## Overview
Perform semantic and keyword searches against the indexed Unity codebase.

## Environment
- Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
- Use the venv Python: D:\VScode_presets\UnityAITemplate\unity-code-indexer\.venv\Scripts\python.exe
- Qdrant should be running on localhost:6333
- Semantic search requires LM Studio with an embedding model

## Tasks
- Run quick searches with qdrant_query.py: See references/qdrant-search.md
- Run semantic searches via Python: See references/qdrant-search.md
- Apply filters for kind, namespace, component type, and path: See references/qdrant-search.md

## References
- references/qdrant-search.md
