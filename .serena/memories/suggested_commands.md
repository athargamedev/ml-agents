# Suggested Commands

## Python ML-Agents Training
```bash
# Install Python packages
pip install -e ./ml-agents-envs
pip install -e ./ml-agents

# Run training
mlagents-learn config/ppo/MyConfig.yaml --run-id=MyRun

# Resume training
mlagents-learn config/ppo/MyConfig.yaml --run-id=MyRun --resume

# Run tests
pytest ml-agents/tests/

# Format/lint Python
black ml-agents/
flake8 ml-agents/
```

## Unity DevProject
- Open in Unity 2022.3+ by pointing Unity Hub to `DevProject/` folder
- Unity Inference Engine API: `using Unity.InferenceEngine;`
- ML-Agents API: `using Unity.MLAgents;`, `using Unity.MLAgents.Sensors;`, etc.

## Git
- Main branch: `develop`
- Platform: Windows (bash shell available via Git Bash or WSL)
