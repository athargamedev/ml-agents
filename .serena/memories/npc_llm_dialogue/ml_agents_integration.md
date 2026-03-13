# ML-Agents Integration — Current Implementation

## Design intent
- ML-Agents is integrated as a training and instrumentation layer on top of the main dialogue system.
- Normal gameplay remains on the remote LM Studio backend.
- ML-Agents only replaces the dialogue backend when explicitly put into side-channel override mode.

## NpcDialogueAgent routing modes
- `ObserveOnly`
  - Default/safe mode
  - `NetworkDialogueService` keeps using `OpenAIChatClient`
  - ML-Agents observes dialogue outcomes and shapes rewards without replacing gameplay inference
- `SideChannelOverride`
  - `NpcDialogueAgent` injects `SideChannelDialogueClient`
  - `NetworkDialogueService.SetMLAgentsSideChannelClient(...)` makes the side-channel backend take precedence
  - Used for training experiments or explicit bridge-driven tests

## SideChannelDialogueClient role
- Implements `IDialogueInferenceClient`
- Routes `ChatAsync()` over `LlmDialogueChannel`
- Should be treated as a temporary override backend, not the normal gameplay path

## Training launch knowledge
- `train_npc_dialogue.bat` now routes to `run_training.py`
- `run_training.py` is the canonical launcher and handles:
  - safer startup flow
  - stale port cleanup
  - explicit run-id handling
  - skipping the Unity HTTP MCP poll by default

## Important runtime caveat
- If `run_llm_bridge.py` is already connected to Unity through `UnityEnvironment`, it can block a training launch with worker/socket-in-use errors.
- The trainer and the bridge should not both try to own the same Unity ML-Agents environment connection at the same time.

## What changed in the refactor
- The gameplay stack no longer has a local `LLMUnity` backend to fall back to.
- ML-Agents override now sits on top of a remote-only gameplay baseline.
- This makes behavior clearer:
  - gameplay = LM Studio remote
  - training override = side-channel bridge

## Validation rule
- When debugging training issues, first determine which backend is active:
  - normal runtime: `openai-compatible-remote`
  - training override: `ml-agents-sidechannel`
