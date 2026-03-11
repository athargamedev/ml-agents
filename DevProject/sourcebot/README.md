# Sourcebot for Unity ML-Agents

Local-first Sourcebot setup for the Unity `ml-agents` workspace with LM Studio on port `7002`.

Latest Sourcebot release checked on 2026-03-11: `v4.15.3`
Pinned image for this setup: `v4.15.2` because the `v4.15.3` image pulled here contains broken zero-byte `zoekt` binaries, which disables search and Ask.

## Quick Start

### Prerequisites

1. **Docker Desktop** running
2. **LM Studio** running at `http://127.0.0.1:7002`
3. `sourcebot/.env` contains a valid `LM_STUDIO_TOKEN`

### Start Sourcebot

```powershell
cd sourcebot
.\start-sourcebot.bat
```

Or directly with Docker Compose:

```bash
cd sourcebot
docker compose up -d
```

Check health and wait for the initial index:

```powershell
cd sourcebot
.\check-sourcebot.ps1 -WaitForIndex
```

### Access

Open **http://localhost:8090**

## What This Setup Indexes

- The local git checkout at `D:\GithubRepos\ml-agents`
- Your active Unity project under `DevProject/`
- Unity metadata and content files such as `*.asmdef`, `*.uxml`, `*.uss`, `*.json`, `*.yaml`, `*.unity`, `*.prefab`, and shader files

The first full index can take a while because Sourcebot is indexing the whole `ml-agents` repository, not only `DevProject/`. Search and Ask become reliable once `.\check-sourcebot.ps1 -WaitForIndex` reports that search shards were detected.

## Unity Search Examples

```text
repo:github.com/athargamedev/ml-agents DevProject/Assets/Network_Game/Diagnostics/NGLog.cs
repo:github.com/athargamedev/ml-agents DevProject/Assets/Network_Game NetworkManager
repo:github.com/athargamedev/ml-agents DevProject/Packages asmdef
repo:github.com/athargamedev/ml-agents DevProject/ProjectSettings Input
```

## Ask Workflows

Try prompts like:

```text
Explain how NGLog is used across DevProject/Assets/Network_Game.
List the asmdef boundaries that affect Network_Game code in DevProject.
Show me where multiplayer diagnostics and logging are wired together in DevProject.
Find the scene, prefab, and script files that define the Network_Game flow.
Trace the shader and UI assets used by the login flow in DevProject.
```

## Files

```text
sourcebot/
├── .env.example
├── check-sourcebot.ps1
├── config.json
├── docker-compose.yml
├── start-sourcebot.bat
└── README.md
```
