# ML-Agents Project - Claude Code Context

## Project Overview

**Purpose**: Unity ML-Agents v4.0.x with LLM dialogue system for NPCs in the DevProject.

This is a research/development project combining:
- Unity ML-Agents toolkit for training intelligent game agents
- Local LLM integration (Ollama/LM Studio) for NPC dialogue
- Custom dialogue pipeline with effect system

**Main Branch**: `develop`

---

## Key Directories

| Directory | Purpose |
|-----------|---------|
| `ml-agents/` | Python training package (PPO, SAC, MA-POCA) |
| `ml-agents-envs/` | Python environment interfaces |
| `com.unity.ml-agents/` | Unity C# package (Runtime, Editor) |
| `DevProject/` | Unity development project |
| `config/` | Training configs (ppo, sac, poca, imitation) |
| `protobuf-definitions/` | Protocol buffer definitions |

---

## Tech Stack

- **Unity**: 2022.3+ (DevProject)
- **Python**: PyTorch-based ML (ml-agents 1.1.0)
- **C#**: ML-Agents Unity package
- **LLM**: Ollama / LM Studio (local server at http://localhost:1234)

### Key Unity Packages
- `com.unity.ai.inference` v2.5.0 — ONNX/LLM inference
- `com.unity.ai.navigation` — NavMesh for NPC movement
- `com.coplaydev.unity-mcp` — Unity MCP server

---

## Commands

### Python Development
```bash
# Install
pip install -e ./ml-agents-envs
pip install -e ./ml-agents

# Train
mlagents-learn config/ppo/MyConfig.yaml --run-id=MyRun

# Test
pytest ml-agents/tests/
pytest ml-agents-envs/tests/
```

### Code Quality
```bash
# Format (black)
black ml-agents/ ml-agents-envs/

# Lint (flake8)
flake8 ml-agents/

# Type check
mypy ml-agents/
mypy ml-agents-envs/

# Pre-commit
pre-commit install
pre-commit run --all-files
```

### Unity Development
- Open `DevProject/` in Unity 2022.3+
- Use `Unity.InferenceEngine` for ONNX inference
- Use `Unity.MLAgents` namespace for ML-Agents API

### Custom Scripts
```bash
python run_training.py
python run_llm_bridge.py
```

---

## Code Style

### Python
- **Black**: 88 char line length
- **flake8**: 120 char max, ignores W503, E203
- **mypy**: `--ignore-missing-imports --disallow-incomplete-defs --no-strict-optional`

### C# (Unity)
- Classes/Methods: `PascalCase`
- Private fields: `m_` prefix (e.g., `m_Agent`)
- Constants: `k_` prefix
- Namespaces: `Unity.MLAgents`, `Unity.MLAgents.Sensors`, `Unity.MLAgents.Policies`

### Generated Files Excluded from Formatting
- `*_pb2.py`, `*_pb2.pyi`, `*_pb2_grpc.py`

---

## Key Files (ML-Agents Integration)

| File | Purpose |
|------|---------|
| `com.unity.ml-agents/Runtime/Agent.cs` | Base agent class |
| `com.unity.ml-agents/Runtime/Inference/ModelRunner.cs` | Inference engine runner |
| `com.unity.ml-agents/Runtime/Policies/SentisPolicy.cs` | Local ONNX inference |
| `com.unity.ml-agents/Runtime/SideChannels/RawBytesChannel.cs` | Python<->Unity comms |
| `com.unity.ml-agents/Runtime/Sensors/BufferSensor.cs` | Variable-length observations |

---

## Skills Available

These project-specific skills are registered and available:

| Skill | Purpose |
|-------|---------|
| `dev-learn` | Update project knowledge base |
| `dev-overnight` | Long-running background dev job |
| `dev-scan` | Code quality scanner for Unity C# |
| `dialogue-automation` | NPC dialogue pipeline automation |
| `effect-pipeline` | VFX/effect management |
| `fix-animation` | Unity Animator diagnostics |
| `fix-vfx` | Unity VFX/particle diagnostics |
| `mcp-source` | Switch Unity MCP package source |
| `scan-asmdefs` | Scan Unity assembly definitions |
| `unity-mcp-orchestrator` | Unity MCP orchestration |
| `python-llm-bridge` | Scaffold Python-to-Unity LLM bridge |
| `unity-component-inspect` | Inspect Unity prefab/scene components |

---

## MCP Servers

- **Unity MCP**: Available via `com.coplaydev.unity-mcp`
- Use for: scene manipulation, component management, game object CRUD

---

## Testing Markers

```python
@pytest.mark.slow  # Slow tests (likely from training)
```

Run with: `pytest -m "not slow"`

---

## Notes

- This is a Windows development environment (Git Bash available)
- Main focus: LLM-powered NPC dialogue system
- Unity推理 Engine API: `using Unity.InferenceEngine;`
- Always update memories after significant code changes via `dev-learn` skill
