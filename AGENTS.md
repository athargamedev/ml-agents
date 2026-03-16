# ML-Agents Toolkit — Agent Guide

> This file is intended for AI coding agents working on the Unity ML-Agents Toolkit project.  
> Last updated: 2026-03-15

---

## Project Overview

The **Unity Machine Learning Agents Toolkit (ML-Agents)** is an open-source project that enables games and simulations to serve as environments for training intelligent agents. This repository combines:

- **Unity ML-Agents package** (v4.0.2) — C# SDK for integrating ML into Unity
- **Python training framework** (v1.2.0.dev0) — PyTorch-based implementations of RL algorithms (PPO, SAC, MA-POCA)
- **LLM dialogue system** — Custom NPC dialogue pipeline using local LLMs (Ollama/LM Studio)

### Key Features
- Reinforcement Learning: PPO, SAC, MA-POCA, self-play
- Imitation Learning: BC, GAIL
- Multi-agent cooperative and competitive scenarios
- Curriculum Learning and environment randomization
- ONNX inference via Unity Inference Engine
- Python API for environment control (gym, PettingZoo wrappers)

---

## Technology Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Unity | Unity Editor | 6000.0+ (package.json), 2022.3+ (DevProject) |
| Python | CPython | 3.10.1 - 3.10.12 |
| ML Framework | PyTorch | 2.1.1 - 2.8.0 |
| RL Algorithms | PPO, SAC, MA-POCA | Built-in |
| Inference | ONNX Runtime | Via `com.unity.ai.inference` v2.5.0 |
| Communication | gRPC | 1.11.0 - 1.53.2 |
| LLM Backend | LM Studio / Ollama | Local HTTP API |

### Key Unity Package Dependencies
```json
{
  "com.unity.ai.inference": "2.5.0",
  "com.unity.modules.imageconversion": "1.0.0",
  "com.unity.modules.jsonserialize": "1.0.0",
  "com.unity.modules.physics": "1.0.0"
}
```

---

## Project Structure

```
ml-agents/
├── com.unity.ml-agents/          # Unity C# package (Runtime + Editor)
│   ├── Runtime/                  # Core ML-Agents SDK
│   │   ├── Agent.cs              # Base agent class
│   │   ├── Academy.cs            # Training orchestrator
│   │   ├── Sensors/              # Observation sensors
│   │   ├── Actuators/            # Action interfaces
│   │   ├── Policies/             # Inference policies (SentisPolicy)
│   │   ├── SideChannels/         # Python<->Unity communication
│   │   └── Inference/            # ONNX model runner
│   ├── Editor/                   # Unity Editor tools
│   └── Documentation~/           # Package documentation
│
├── com.unity.ml-agents.tests/    # Unity playmode/editmode tests
│
├── ml-agents/                    # Python training package
│   ├── mlagents/
│   │   ├── trainers/             # RL algorithm implementations
│   │   │   ├── ppo/              # PPO trainer
│   │   │   ├── sac/              # SAC trainer
│   │   │   ├── poca/             # MA-POCA trainer
│   │   │   ├── llm_dialogue_channel.py  # Custom LLM sidechannel
│   │   │   └── llm_bridge_server.py     # LLM handler factories
│   │   ├── plugins/              # Plugin system for trainers/stats
│   │   └── utils/                # HuggingFace hub utilities
│   └── tests/                    # Python unit tests
│
├── ml-agents-envs/               # Python environment interface
│   ├── mlagents_envs/
│   │   ├── environment.py        # UnityEnvironment class
│   │   ├── side_channel/         # SideChannel implementations
│   │   ├── envs/                 # Gym/PettingZoo wrappers
│   │   └── registry/             # Environment registry
│   └── tests/                    # Environment tests
│
├── ml-agents-trainer-plugin/     # Template for custom trainer plugins
│
├── DevProject/                   # Unity development project
│   ├── Assets/
│   │   ├── ML-Agents/            # ML-Agents examples
│   │   ├── Network_Game/         # NPC dialogue system (custom)
│   │   └── Scripts/              # C# gameplay scripts
│   └── Packages/manifest.json    # Unity package manifest
│
├── config/                       # Training configuration files
│   ├── ppo/                      # PPO configs (NpcDialogue.yaml, etc.)
│   ├── sac/                      # SAC configs
│   ├── poca/                     # MA-POCA configs
│   └── imitation/                # BC/GAIL configs
│
├── protobuf-definitions/         # gRPC proto files
│   └── proto/                    # Communicator proto definitions
│
├── utils/                        # Development utilities
│   ├── validate_versions.py      # Version validation
│   ├── validate_inits.py         # __init__.py checker
│   └── run_markdown_link_check.py
│
└── dev_tools/                    # Additional development tools
```

---

## Build and Installation

### Python Packages (Development)

```bash
# Install in editable mode
pip install -e ./ml-agents-envs
pip install -e ./ml-agents

# Verify installation
mlagents-learn --help
```

### Unity Package

The Unity package is managed via Package Manager:
- Open `DevProject/` in Unity 2022.3+
- Package is referenced as a local package via `manifest.json`

---

## Testing

### Python Tests

```bash
# Run all tests
pytest ml-agents/tests/
pytest ml-agents-envs/tests/

# Run with coverage (minimum 60%)
pytest --cov=mlagents --cov=mlagents_envs --cov-report html

# Run tests in parallel (using pytest-xdist)
pytest -n auto ml-agents/tests/

# Exclude slow tests (training tests)
pytest -m "not slow"
```

### Test Markers

```python
@pytest.mark.slow  # Slow tests (training-related)
```

### Unity Tests

- Open Test Runner in Unity: `Window > General > Test Runner`
- Run PlayMode tests for runtime verification
- Run EditMode tests for editor tools

---

## Code Style Guidelines

### Python

| Tool | Configuration |
|------|---------------|
| **Black** | 88 char line length |
| **flake8** | 120 char max, ignores W503, E203, I200 |
| **mypy** | `--ignore-missing-imports --disallow-incomplete-defs --no-strict-optional` |

**Banned Modules** (enforced via flake8-tidy-imports):
- `tensorflow` → use `mlagents.tf_utils` instead
- `logging` → use `mlagents_envs.logging_util` instead
- `torch` → use `mlagents.torch_utils` instead

**Generated Files Excluded from Formatting:**
- `*_pb2.py`, `*_pb2.pyi`, `*_pb2_grpc.py` (protobuf generated)

### C# (Unity)

| Convention | Pattern | Example |
|------------|---------|---------|
| Classes/Methods | PascalCase | `Agent`, `RequestDecision()` |
| Private fields | `m_` prefix | `m_Agent`, `m_BehaviorParameters` |
| Constants | `k_` prefix | `k_MaxSteps` |
| Namespaces | Unity.MLAgents.* | `Unity.MLAgents.Sensors` |

**Formatting:** `dotnet format whitespace` (run via pre-commit)

---

## Pre-commit Hooks

```bash
# Install pre-commit
pip install pre-commit>=2.8.0
pip install identify>=2.1.3

# Install git hooks
pre-commit install

# Run all checks
pre-commit run --all-files

# Run specific hook
pre-commit run black
pre-commit run mypy-ml-agents
```

### Hooks Configured
- `black` — Python code formatting
- `mypy` — Type checking (ml-agents, ml-agents-envs)
- `flake8` — Linting with plugins (comprehensions, tidy-imports, bugbear)
- `pyupgrade` — Python 3.6+ syntax upgrades
- `dotnet-format` — C# whitespace formatting
- `validate-versions` — Version consistency check
- `validate-init-py` — __init__.py validation

---

## Custom Scripts (NPC Dialogue)

### Training Launcher
```bash
# Terminal 1: Start LLM bridge
python run_llm_bridge.py

# Terminal 2: Start training
python run_training.py [--fresh] [--run-id NAME] [--no-watchdog]
```

**Key Configuration:**
- Trainer port: 5004
- Unity MCP endpoint: http://localhost:8009/mcp
- LM Studio endpoint: http://127.0.0.1:7002/v1
- Default model: `qwen3-8b`

### Batch Scripts (Windows)
- `train_npc_dialogue.bat` — Launch NPC training
- `tensorboard.bat` — Launch TensorBoard on results/
- `run_npc_tests.bat` — Run NPC dialogue tests

---

## Key Files for AI Agents

### Core ML-Agents
| File | Purpose |
|------|---------|
| `com.unity.ml-agents/Runtime/Agent.cs` | Base agent class - extend this for custom agents |
| `com.unity.ml-agents/Runtime/Academy.cs` | Training orchestrator |
| `com.unity.ml-agents/Runtime/Policies/SentisPolicy.cs` | Local ONNX inference |
| `com.unity.ml-agents/Runtime/SideChannels/RawBytesChannel.cs` | Binary communication |
| `com.unity.ml-agents/Runtime/Sensors/BufferSensor.cs` | Variable-length observations |

### LLM Dialogue System
| File | Purpose |
|------|---------|
| `ml-agents/mlagents/trainers/llm_dialogue_channel.py` | SideChannel for NPC dialogue |
| `ml-agents/mlagents/trainers/llm_bridge_server.py` | LLM handler factories (LM Studio, Ollama, Mock) |
| `run_llm_bridge.py` | Bridge runner script |
| `run_training.py` | Training launcher with watchdog |
| `config/ppo/NpcDialogue.yaml` | Training config for dialogue agents |

### Environment Interface
| File | Purpose |
|------|---------|
| `ml-agents-envs/mlagents_envs/environment.py` | UnityEnvironment class |
| `ml-agents-envs/mlagents_envs/side_channel/` | SideChannel implementations |
| `ml-agents-envs/mlagents_envs/envs/gym_wrapper.py` | OpenAI Gym wrapper |

---

## Training Configuration

Example PPO config (`config/ppo/PushBlock.yaml`):

```yaml
behaviors:
  PushBlock:
    trainer_type: ppo
    hyperparameters:
      batch_size: 128
      buffer_size: 2048
      learning_rate: 0.0003
      beta: 0.01
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
      learning_rate_schedule: linear
    network_settings:
      normalize: false
      hidden_units: 256
      num_layers: 2
      vis_encode_type: simple
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    max_steps: 2000000
    time_horizon: 64
```

---

## Entry Points (Console Scripts)

```bash
mlagents-learn              # Main training command
mlagents-run-experiment     # Run pre-configured experiments
mlagents-push-to-hf         # Upload model to HuggingFace
mlagents-load-from-hf       # Download model from HuggingFace
```

---

## Plugin System

ML-Agents supports custom trainers and stats writers via entry points:

```python
# setup.py entry points
ML_AGENTS_TRAINER_TYPE = [
    "default=mlagents.plugins.trainer_type:get_default_trainer_types"
]
ML_AGENTS_STATS_WRITER = [
    "default=mlagents.plugins.stats_writer:get_default_stats_writers"
]
```

---

## MCP Integration

This project uses MCP (Model Context Protocol) for Unity Editor integration:

**Configured Servers** (in `.mcp.json`):
- `unityMCP` — Unity Editor automation (via `com.coplaydev.unity-mcp`)
- `filesystem` — File system access
- `serena-mcp` — Serena code analysis
- `sequentialthinking` — Sequential thinking tool

**Unity MCP** location: `D:\GithubRepos\unity-mcp-beta\Server`

---

## Project-Specific Skills

These custom skills are available in `.claude/skills/`:

| Skill | Purpose |
|-------|---------|
| `dialogue-automation` | NPC dialogue pipeline automation |
| `effect-pipeline` | VFX/effect management for dialogue |
| `fix-animation` | Unity Animator diagnostics |
| `fix-vfx` | Unity VFX/particle diagnostics |
| `scan-asmdefs` | Scan Unity assembly definitions |
| `unity-mcp-orchestrator` | Unity MCP orchestration |
| `python-llm-bridge` | Scaffold Python-to-Unity LLM bridge |
| `dev-learn` | Update project knowledge base |
| `dev-overnight` | Long-running background dev job |
| `mcp-source` | Switch Unity MCP package source |

---

## Security Considerations

### Sensitive Configuration
- `.mcp.json` contains API keys (e.g., Sourcebot token) — do not commit changes
- LM Studio API key defaults to `lm-studio` placeholder
- Environment variables for secrets: `LM_STUDIO_API_KEY`

### Network Security
- Training communication uses local gRPC on configurable ports (default 5004)
- LLM bridge connects to localhost only (127.0.0.1)
- Unity MCP HTTP endpoint at localhost:8009

### Code Safety
- Never commit `*.log` files or `results/` directory
- Pre-commit hooks prevent credential leakage
- `.gitignore` excludes: `results/`, `.venv/`, `__pycache__/`, `*.log`

---

## Common Development Workflows

### Adding a New Sensor
1. Implement `ISensor` interface in `com.unity.ml-agents/Runtime/Sensors/`
2. Add corresponding proto message in `protobuf-definitions/`
3. Regenerate protobuf files using `make_for_win.bat`
4. Add Python sensor wrapper in `ml-agents-envs/`

### Adding a Custom Trainer
1. Create trainer in `ml-agents/mlagents/trainers/<algorithm>/`
2. Register in `mlagents/plugins/trainer_type.py`
3. Add config schema in `ml-agents/mlagents/trainers/settings.py`

### Updating Protobuf Definitions
```bash
cd protobuf-definitions
# On Windows:
make_for_win.bat
# On Linux/Mac:
./make.sh
```

---

## Version Management

Version constants are defined in:
- `ml-agents/mlagents/trainers/__init__.py` → `__version__`, `__release_tag__`
- `ml-agents-envs/mlagents_envs/__init__.py` → `__version__`, `__release_tag__`
- `com.unity.ml-agents/package.json` → Unity package version

**Validation:** Run `utils/validate_versions.py` to ensure consistency.

---

## Documentation

- **Unity Package Docs**: https://docs.unity3d.com/Packages/com.unity.ml-agents@latest
- **Legacy Web Docs**: https://unity-technologies.github.io/ml-agents/ (deprecated)
- **Project Memory**: See `MEMORY.md` for quick reference

---

## Troubleshooting

### Port Already in Use (5004)
```python
# run_training.py automatically kills port holders
# Manual fix: taskkill /F /IM python.exe
```

### Unity Not Connecting
- Verify Unity is in Play mode with `BehaviorType = Default`
- Check that ML-Agents package is properly imported
- Verify gRPC versions match between Python and Unity

### LLM Bridge Not Responding
- Ensure LM Studio server is running on port 7002
- Check `run_llm_bridge.py` logs in `.codex/tmp/run_llm_bridge.runtime.log`
- Verify `ENABLE_DIALOGUE_SIDECHANNEL = True` in config

---

## References

- [ML-Agents Paper](https://arxiv.org/abs/1809.02627)
- [MA-POCA Paper](http://aaai-rlg.mlanctot.info/papers/AAAI22-RLG_paper_32.pdf)
- [Unity Discussions](https://discussions.unity.com/tag/ml-agents)
- [Discord](https://discord.com/channels/489222168727519232/1202574086115557446)
