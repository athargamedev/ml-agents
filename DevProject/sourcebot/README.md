# Sourcebot for Unity ML-Agents

Local-first Sourcebot setup for the Unity `ml-agents` workspace with LM Studio on port `7002`.

This setup now builds Sourcebot from your local clone at `D:\GithubRepos\sourcebot`, so Unity-specific Ask and UI customizations are fully under your control.

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

### MCP Endpoint

Sourcebot also exposes an MCP server at `http://localhost:8090/api/mcp`.

- Codex uses the global `~/.codex/config.toml` file; add `[mcp_servers.sourcebot]` with `url = "http://127.0.0.1:8090/api/mcp"`
- The workspace `.mcp.json` and `.claude/mcp.json` files can expose the same endpoint to other MCP-aware tools
- The local health check validates the MCP initialize handshake

## What This Setup Indexes

- The local git checkout at `D:\GithubRepos\ml-agents`
- Your active Unity project under `DevProject/`
- The default `develop` branch plus the additional `main` branch for branch-aware search
- Unity metadata and content files such as `*.asmdef`, `*.uxml`, `*.uss`, `*.json`, `*.yaml`, `*.unity`, `*.prefab`, and shader files

The first full index can take a while because Sourcebot is indexing the whole `ml-agents` repository, not only `DevProject/`. Search and Ask become reliable once `.\check-sourcebot.ps1 -WaitForIndex` reports that search shards were detected.

## Feature Coverage

Available in this local setup:

- Search
- Ask via LM Studio using the `openai-compatible` model provider
- MCP access from editor tools through `http://localhost:8090/api/mcp`
- Branch search across `develop` and `main`

Not enabled by the current setup:

- Enterprise-only UI features such as code navigation, search contexts, analytics, and permission syncing require `SOURCEBOT_EE_LICENSE_KEY` in `sourcebot/.env` plus a container restart
- The AI code review agent needs a code-host integration flow such as GitHub App/webhook setup; the current connection is a local `file:///repos/ml-agents` repository, which is correct for local indexing but not for PR review automation

## LM Studio Note

For `openai-compatible` models, Sourcebot should point at the API root such as `http://127.0.0.1:7002/v1`, not the full `/v1/chat/completions` path. Using the full completions endpoint causes Sourcebot's AI SDK client to build an invalid nested path and Ask/chat-title generation can fail with `Invalid JSON response`.

This setup now routes Sourcebot through a small sidecar compatibility proxy before LM Studio. That proxy rewrites `tool_choice` objects into the simpler format LM Studio accepts, because Sourcebot emits OpenAI-style forced-tool requests that LM Studio otherwise rejects with `400 Bad Request`.

The current checked-in Ask model is `qwen2.5-coder-7b-instruct@q8_0`. In this workspace, `qwen3-8b` timed out on a basic tool-calling probe, while the older `qwen2.5-coder-7b-instruct@q4_k_m` quant produced unreliable Ask behavior such as empty `step-start` chats, `0 steps`, and one-word answers.

## Troubleshooting

- If `http://127.0.0.1:8090/~` hangs with no response, Docker Desktop is usually wedged rather than Sourcebot being misconfigured.
- If `.\check-sourcebot.ps1` warns that `com.docker.service` is stopped, restart Docker Desktop from an elevated shell or start that Windows service as Administrator, then rerun `docker compose up -d`.
- If Ask still shows `0 steps`, stalls at `Thinking...`, or gives nonsense answers after the restart, confirm the live container has reloaded `config.json` and is using `qwen2.5-coder-7b-instruct@q8_0` rather than the old `@q4_k_m` model or `qwen3-8b`.

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
