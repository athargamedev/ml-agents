---
name: indexer-manage
description: Manage the Unity code indexer pipeline. Use when the user asks about indexer status, running, stopping, resetting, incremental updates, or indexer logs, or mentions /indexer.
---

# Indexer Management

## Overview
Start, stop, and monitor the Unity code indexer, including reset and incremental runs.

## Environment
- Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
- Use the venv Python: D:\VScode_presets\UnityAITemplate\unity-code-indexer\.venv\Scripts\python.exe
- Qdrant should be running on localhost:6333
- LM Studio should be running on localhost:7002 for summaries

## Tasks
- Check indexer status and Qdrant collection counts: See references/indexer-commands.md
- Start indexing (full, fast, reset): See references/indexer-commands.md
- Stop the indexer process: See references/indexer-commands.md
- View logs and progress details: See references/indexer-commands.md
- Run incremental updates: See references/indexer-commands.md

## Notes
- Index state is stored in output\index_state.db
- Safe to stop any time; use --incremental to resume

## References
- references/indexer-commands.md
