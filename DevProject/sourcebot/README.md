# Sourcebot for Unity ML-Agents

Self-hosted code search with LM Studio integration (port 7002).

## Quick Start

### Prerequisites

1. **Docker Desktop** running
2. **LM Studio** running at `http://127.0.0.1:7002`

### Start Sourcebot

```powershell
cd sourcebot
.\start-sourcebot.bat
```

Or with Docker Compose:
```bash
cd sourcebot && docker-compose up -d
```

### Access

Open **http://localhost:8090**

## Search Examples

```
repo:ml-agents DevProject/NetworkManager
repo:ml-agents DevProject/ logger.Debug
```

## Ask Sourcebot

Uses your LM Studio at port 7002. Click the Ask tab to question your codebase.

## Files

```
sourcebot/
├── config.json
├── docker-compose.yml
├── start-sourcebot.bat
└── README.md
```
