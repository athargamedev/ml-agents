---
name: assembly-query
description: Query Unity assembly definitions and dependencies in the indexed codebase. Use when the user asks about assemblies, asmdef files, package vs first-party assemblies, dependency graphs, circular deps, or mentions /assembly or /deps.
---

# Assembly Query

## Overview
Query the indexed Unity assembly definitions and their dependencies in Qdrant to understand project structure and dependency graphs.

## Environment
- Set-Location D:\VScode_presets\UnityAITemplate\unity-code-indexer
- Use the venv Python: D:\VScode_presets\UnityAITemplate\unity-code-indexer\.venv\Scripts\python.exe
- Qdrant should be running on localhost:6333
- The unity_assemblies collection exists only if the indexer ran with --analyze-usage

## Tasks
- List assemblies (first-party vs packages): See references/assembly-queries.md
- Get assembly info and references: See references/assembly-queries.md
- Find dependents of an assembly: See references/assembly-queries.md
- List symbols within an assembly: See references/assembly-queries.md
- Analyze dependency hotspots: See references/assembly-queries.md
- Filter symbols by assembly and kind: See references/assembly-queries.md

## References
- references/assembly-queries.md
