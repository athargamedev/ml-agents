# ML-Agents Training Knowledge Base for LLM Dialogue NPC Systems

## Overview

This knowledge base synthesizes Unity ML-Agents training documentation to provide actionable guidance for improving LLM-driven NPC dialogue systems through reinforcement and imitation learning.

---

## 1. Core Training Concepts

### 1.1 The RL Loop (Applicable to Dialogue)

```
Observation → Agent → Action → Environment → Reward → (repeat)
```

**For NPC Dialogue:**
- **Observation**: Proximity to player, conversation history, NPC emotional state, context (combat, exploration)
- **Action**: Start dialogue, continue, end conversation, change tone
- **Reward**: Player engagement, response quality, conversation completion, latency penalty

### 1.2 Key Training Methods

| Method | Use Case for Dialogue NPCs | Configuration |
|--------|---------------------------|---------------|
| **PPO (Proximal Policy Optimization)** | Default, stable on-policy learning | `trainer_type: ppo` |
| **SAC (Soft Actor-Critic)** | Off-policy, sample-efficient, handles continuous actions | `trainer_type: sac` |
| **GAIL (Generative Adversarial Imitation Learning)** | Learn from expert demonstrations (recorded player dialogues) | `reward_signals.gail` |
| **Behavioral Cloning (BC)** | Pre-training on demonstration data | `behavioral_cloning` section |
| **Curriculum Learning** | Progressively harder dialogue scenarios | `environment_parameters.curriculum` |
| **Self-Play** | Multi-NPC conversations, competitive scenarios | `self_play` section |

---

## 2. Reward Signal Design for Dialogue

### 2.1 Extrinsic Rewards (Environment-Based)

The primary reward signal from the dialogue system:

```yaml
reward_signals:
  extrinsic:
    strength: 1.0      # Primary weight
    gamma: 0.99       # Discount factor for future rewards
```

**Dialogue-Specific Rewards:**
- **Response Quality**: +R when LLM feedback score > threshold
- **Latency**: -R for slow response times (penalizes waiting)
- **Engagement**: +R for player-initiated follow-up
- **Completion**: +R for successful conversation resolution

### 2.2 Intrinsic Rewards (Encouraging Exploration)

Use when dialogue data is sparse or rewards are infrequent:

**Curiosity Module:**
```yaml
reward_signals:
  curiosity:
    strength: 0.02    # Scale carefully - can overwhelm extrinsic
    gamma: 0.99
    encoding_size: 256
    learning_rate: 3e-4
```
- Encourages NPC to try different dialogue approaches
- Useful when there's no "correct" answer

**RND (Random Network Distillation):**
```yaml
reward_signals:
  rnd:
    strength: 0.01
    gamma: 0.99
```
- Alternative to curiosity for sparse-reward environments

### 2.3 GAIL (Imitation Learning)

Train from recorded expert dialogues:

```yaml
reward_signals:
  gail:
    strength: 0.01           # Keep low if demonstrations are suboptimal
    gamma: 0.99
    demo_path: "path/to/demonstrations.demo"
    encoding_size: 128
    learning_rate: 3e-4
    use_actions: false       # true = match actions, false = match states
    use_vail: false         # variational bottleneck for stability
```

**Key Insight**: GAIL with `use_actions: false` is more stable for dialogue - NPC learns to visit "engaging conversation states" without needing to match exact actions.

---

## 3. Imitation Learning for Dialogue NPCs

### 3.1 When to Use Imitation Learning

1. **Cold Start**: No reward signal yet defined
2. **Accelerate RL**: Pre-train with demonstrations, then fine-tune with RL
3. **Style Transfer**: Learn specific dialogue patterns from human examples

### 3.2 Behavioral Cloning (BC)

```yaml
behavioral_cloning:
  demo_path: "Project/Assets/DialogueDemos/ExpertDialogue.demo"
  strength: 0.5           # Relative to main learning rate
  steps: 150000          # BC active for first N steps (0 = always)
  batch_size: 512
  num_epoch: 3
```

**Limitation**: BC cannot generalize beyond demonstrated states - use with GAIL or RL.

### 3.3 Recording Demonstrations

In Unity Editor:
1. Set Agent behavior to **Heuristic** (manual control)
2. Play and manually control NPC dialogue
3. Recording saves observation-action-reward tuples
4. Use for BC and/or GAIL training

---

## 4. Network Architecture for Dialogue

### 4.1 Default Configuration

```yaml
network_settings:
  hidden_units: 128       # Dialogue complexity
  num_layers: 2          # Start simple, increase if needed
  normalize: false       # Usually unnecessary for dialogue
  vis_encode_type: simple
```

### 4.2 With Memory (LSTM)

For conversations requiring context:

```yaml
network_settings:
  hidden_units: 128
  num_layers: 2
  memory:
    sequence_length: 64   # Conversation history length
    memory_size: 256     # Hidden state size
```

**Note**: LSTM does not work well with continuous actions - use discrete actions for dialogue.

### 4.3 Increasing Capacity

For complex multi-modal dialogue (text + emotion + context):

```yaml
network_settings:
  hidden_units: 256       # Increase for complex behaviors
  num_layers: 3          # More layers = more capacity
```

---

## 5. Critical Hyperparameters

### 5.1 PPO Hyperparameters

| Parameter | Default | Dialogue Recommendation | Notes |
|-----------|---------|------------------------|-------|
| `batch_size` | 1024 | 256-512 | Smaller for discrete dialogue actions |
| `buffer_size` | 10240 | 4096-8192 | Multiple of batch_size |
| `learning_rate` | 3e-4 | 1e-4 - 3e-4 | Reduce if unstable |
| `beta` (entropy) | 5e-3 | 1e-3 - 1e-2 | Higher = more exploration |
| `epsilon` | 0.2 | 0.1 - 0.3 | Lower = more stable |
| `lambda` (GAE) | 0.95 | 0.9 - 0.95 | Lower = more variance |
| `num_epoch` | 3 | 3 - 10 | Higher = more stable updates |

### 5.2 Training Control

```yaml
max_steps: 5.0e5          # Total training steps
time_horizon: 64          # Steps per episode (64-128 for dialogue)
summary_freq: 10000       # TensorBoard update frequency
keep_checkpoints: 5
checkpoint_interval: 50000
```

---

## 6. Curriculum Learning for Dialogue

### 6.1 Concept

Start with simple dialogues, progressively increase complexity:

```yaml
environment_parameters:
  dialogue_difficulty:
    curriculum:
      - name: SimpleGreetings
        completion_criteria:
          measure: reward
          behavior: NpcDialogue
          threshold: 0.3
          min_lesson_length: 100
        value: 1.0
      
      - name: ComplexNegotiation
        completion_criteria:
          measure: reward
          threshold: 0.6
          min_lesson_length: 200
        value: 3.0
```

### 6.2 Dialogue Curriculum Stages

1. **Stage 1**: Simple Q&A (greetings, basic questions)
2. **Stage 2**: Contextual responses (account for player state)
3. **Stage 3**: Multi-turn negotiation
4. **Stage 4**: Emotional adaptation
5. **Stage 5**: Complex story-driven conversations

---

## 7. Multi-Agent Scenarios

### 7.1 Self-Play (Competitive Dialogue)

For NPCs that need to compete for player attention:

```yaml
self_play:
  window: 10              # Past snapshots to sample
  play_against_latest_model_ratio: 0.5
  save_steps: 50000
  swap_steps: 2000
  team_change: 100000
```

### 7.2 Cooperative (Multi-NPC Dialogue)

For group conversations:

```yaml
trainer_type: poca        # MA-POCA for cooperative
```

---

## 8. Troubleshooting Dialogue Training

### 8.1 Common Issues

| Symptom | Likely Cause | Fix |
|---------|--------------|-----|
| Reward stays near 0 | No clear reward signal | Implement shaped rewards |
| Entropy drops too fast | Beta too low | Increase `beta` |
| Training unstable | Learning rate too high | Reduce `learning_rate` |
| Policy not exploring | Beta too low / epsilon too low | Increase exploration |
| Overfitting to demos | GAIL strength too high | Reduce `gail.strength` |

### 8.2 Debugging Tools

- **TensorBoard**: Monitor entropy, loss, reward curves
- **Inference Mode**: Test trained model manually
- **Checkpoint Loading**: `--resume` to continue training

---

## 9. Integration with LLM Bridge

### 9.1 Architecture Pattern

```
Unity (Dialogue Environment)
    │
    ├── Observations: [proximity, turn_count, player_state, ...]
    ├── Actions: [engage, respond, end_conversation, change_tone]
    └── Rewards: [quality_score, latency_penalty, engagement_bonus]
        │
        ▼
Python (mlagents-train)
    │
    ├── Learns policy: When should NPC engage?
    └── Outputs: trained dialogue_timing_policy.onnx
        │
        ▼
Unity (Inference)
    └── Model runs locally → NPC decides when to trigger LLM
```

### 9.2 Key Insight

**ML-Agents trains the timing/engagement policy, NOT the LLM itself.** The LLM generates dialogue content; ML-Agents learns when to trigger the LLM.

---

## 10. Recommended Configuration for Dialogue NPCs

### Starting Point (PPO + Curiosity)

```yaml
behaviors:
  NpcDialogue:
    trainer_type: ppo
    
    hyperparameters:
      batch_size: 256
      buffer_size: 4096
      learning_rate: 3e-4
      beta: 5e-3
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
      
    network_settings:
      hidden_units: 128
      num_layers: 2
      memory:
        sequence_length: 64
        memory_size: 256
        
    max_steps: 5.0e5
    time_horizon: 128
    summary_freq: 5000
    
    reward_signals:
      extrinsic:
        strength: 1.0
        gamma: 0.99
      curiosity:
        strength: 0.01
        gamma: 0.99
        encoding_size: 128
```

### Advanced (with BC + GAIL pre-training)

```yaml
behaviors:
  NpcDialogue:
    # ... PPO config above ...
    
    behavioral_cloning:
      demo_path: "DialogueDemos/expert.demo"
      strength: 0.3
      steps: 100000
      
    reward_signals:
      extrinsic:
        strength: 1.0
        gamma: 0.99
      gail:
        strength: 0.01
        gamma: 0.99
        demo_path: "DialogueDemos/expert.demo"
        use_actions: false
```

---

## 11. Key Takeaways

1. **Start Simple**: Pure RL with extrinsic rewards first
2. **Add Curiosity**: When rewards are sparse (rare player engagement)
3. **Use Imitation**: When you have demonstration data (recorded player sessions)
4. **Scale Network**: Increase `hidden_units` and `num_layers` if underfitting
5. **Monitor Entropy**: Should decrease slowly; too fast = no exploration
6. **Curriculum**: Progressively harder dialogue scenarios
7. **Memory**: Add LSTM for multi-turn conversation context

---

## 11. Example YAML Configurations for NpcDialogue

### 11.1 Starting Point (Current Best Practice)

```yaml
# config/ppo/NpcDialogue.yaml
behaviors:
  NpcDialogue:
    trainer_type: ppo
    
    hyperparameters:
      batch_size: 256
      buffer_size: 1024
      learning_rate: 0.0002
      learning_rate_schedule: linear
      beta: 0.05
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
      
    network_settings:
      normalize: true
      hidden_units: 256
      num_layers: 3
      memory:
        sequence_length: 64
        memory_size: 256
        
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
      curiosity:
        gamma: 0.99
        strength: 0.005
        network_settings:
          hidden_units: 128
        learning_rate: 0.0002
        
    max_steps: 5000000
    time_horizon: 128
    summary_freq: 1000
    keep_checkpoints: 5
    checkpoint_interval: 50000
```

### 11.2 With Imitation Learning (BC + GAIL)

```yaml
# config/imitation/NpcDialogue.yaml
behaviors:
  NpcDialogue:
    trainer_type: ppo
    
    hyperparameters:
      batch_size: 256
      buffer_size: 2048
      learning_rate: 0.0003
      beta: 0.01
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
      
    network_settings:
      normalize: false
      hidden_units: 256
      num_layers: 3
      memory:
        sequence_length: 64
        memory_size: 256
        
    # Behavioral Cloning - pre-train on demonstrations
    behavioral_cloning:
      demo_path: DevProject/Assets/ML-Agents/DialogueDemos/ExpertDialogue.demo
      strength: 0.3
      steps: 100000
      batch_size: 256
      num_epoch: 3
      
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
      gail:
        strength: 0.01
        gamma: 0.99
        demo_path: DevProject/Assets/ML-Agents/DialogueDemos/ExpertDialogue.demo
        encoding_size: 128
        learning_rate: 0.0003
        use_actions: false
        use_vail: false
        
    max_steps: 5000000
    time_horizon: 128
    summary_freq: 5000
```

### 11.3 With Curriculum Learning

```yaml
# config/ppo/NpcDialogue_curriculum.yaml
behaviors:
  NpcDialogue:
    trainer_type: ppo
    # ... same hyperparameters as starting point ...
    
environment_parameters:
  # Curriculum stage 1: Simple greetings
  dialogue_complexity:
    curriculum:
      - name: SimpleGreetings
        completion_criteria:
          measure: reward
          behavior: NpcDialogue
          signal_smoothing: true
          min_lesson_length: 50
          threshold: 0.2
        value: 1.0
        
      - name: ContextualResponses
        completion_criteria:
          measure: reward
          behavior: NpcDialogue
          signal_smoothing: true
          min_lesson_length: 100
          threshold: 0.4
        value: 2.0
        
      - name: ComplexNegotiation
        completion_criteria:
          measure: reward
          behavior: NpcDialogue
          signal_smoothing: true
          min_lesson_length: 150
          threshold: 0.6
        value: 3.0
```

---

## 12. TensorBoard Metrics for Dialogue NPCs

### 12.1 Standard Metrics Recorded by ML-Agents

| Metric | Path | What to Watch For |
|--------|------|-------------------|
| Cumulative Reward | `Environment/Cumulative Reward` | Should increase over time |
| Episode Length | `Environment/Episode Length` | Dialogue length trends |
| Entropy | `Policy/Entropy` | Should decrease slowly, not collapse |
| Learning Rate | `Policy/Learning Rate` | Decays if using linear schedule |
| Extrinsic Reward | `Policy/Extrinsic Reward` | Main dialogue reward signal |
| Value Estimate | `Policy/Value Estimate` | Should increase with reward |
| Policy Loss | `Losses/Policy Loss` | Should decrease, spikes = instability |
| Value Loss | `Losses/Value Loss` | Should stabilize |

### 12.2 Custom Metrics from NpcDialogueAgent

Your agent records these custom metrics under `NpcDialogue/` namespace:

| Metric | Description | Target Trend |
|--------|-------------|--------------|
| `NpcDialogue/Dialogue/Turns` | Turns per episode | Varies by scenario |
| `NpcDialogue/Latency/QueueMs` | LLM queue wait time | Lower is better |
| `NpcDialogue/Latency/ModelMs` | LLM inference time | Depends on model |
| `NpcDialogue/Latency/TotalMs` | Total response latency | Lower is better |
| `NpcDialogue/Retries/Count` | Retry attempts per episode | Should decrease |
| `NpcDialogue/Feedback/ScoreRaw` | Raw feedback score (0-1) | Higher is better |
| `NpcDialogue/Feedback/HasEffect` | Effect triggered flag | Higher = better engagement |
| `NpcDialogue/Feedback/TagValid` | Valid effect tag | Should be 1.0 ideally |
| `NpcDialogue/Reward/*` | Individual reward components | Context-dependent |

### 12.3 Interpreting Training Curves

**Healthy Training:**
- Reward curves upward with some noise
- Entropy decreases gradually (not suddenly to 0)
- Value loss stabilizes
- Policy loss decreases

**Problem Signs:**
- **Reward flat at 0**: No reward signal reaching agent
- **Entropy collapsed**: Policy stopped exploring → increase `beta`
- **High variance**: Increase `buffer_size`, decrease `learning_rate`
- **Loss spikes**: Reduce `epsilon`, reduce `learning_rate`

---

## 13. Automating TensorBoard Analysis

### 13.1 Exporting TensorBoard Data

```bash
# Method 1: TensorBoard UI download
# In TensorBoard: Settings → Enable data download links
# Click "Download" under each chart

# Method 2: Direct event file access (Python)
```

### 13.2 Python Script: Auto-Analyze Training Runs

```python
# analyze_training.py
"""
Automated training analysis for NPC Dialogue agents.
Reads TensorBoard event files and generates insights.
"""

import os
import json
from pathlib import Path
from typing import Dict, List, Optional

import numpy as np
from tensorboard.backend.event_processing import event_accumulator


class TrainingAnalyzer:
    """Analyze ML-Agents training runs from TensorBoard event files."""
    
    def __init__(self, results_dir: str = "results"):
        self.results_dir = Path(results_dir)
        
    def find_latest_run(self, pattern: str = "NpcDialogue") -> Optional[Path]:
        """Find most recent training run directory."""
        run_dirs = []
        for run_dir in self.results_dir.iterdir():
            if run_dir.is_dir() and pattern in run_dir.name:
                # Check for event files
                event_files = list(run_dir.rglob("*.tfevents.*"))
                if event_files:
                    # Sort by modification time
                    run_dirs.append((run_dir, event_files[0].stat().st_mtime))
        
        if not run_dirs:
            return None
        
        run_dirs.sort(key=lambda x: x[1], reverse=True)
        return run_dirs[0][0]
    
    def load_events(self, run_path: Path) -> event_accumulator.EventAccumulator:
        """Load TensorBoard events from a run directory."""
        ea = event_accumulator.EventAccumulator(
            str(run_path),
            size_guidance={
                event_accumulator.SCALARS: 0,
                event_accumulator.HISTOGRAMS: 0,
            }
        )
        ea.Reload()
        return ea
    
    def get_metrics(self, ea: event_accumulator.EventAccumulator) -> Dict[str, List]:
        """Extract all scalar metrics from events."""
        metrics = {}
        for tag in ea.Tags()["scalars"]:
            events = ea.Scalars(tag)
            values = [e.value for e in events]
            steps = [e.step for e in events]
            metrics[tag] = {"steps": steps, "values": values}
        return metrics
    
    def analyze_training_health(self, metrics: Dict) -> Dict:
        """Analyze training health indicators."""
        analysis = {"issues": [], "warnings": [], "status": "unknown"}
        
        # Check reward trend
        if "Environment/Cumulative Reward" in metrics:
            reward_data = metrics["Environment/Cumulative Reward"]["values"]
            if len(reward_data) > 10:
                early = np.mean(reward_data[:len(reward_data)//3])
                late = np.mean(reward_data[-len(reward_data)//3:])
                if late > early * 1.2:
                    analysis["status"] = "healthy"
                elif late < early * 0.8:
                    analysis["issues"].append("Reward declining - possible regression")
        
        # Check entropy
        if "Policy/Entropy" in metrics:
            entropy_data = metrics["Policy/Entropy"]["values"]
            if len(entropy_data) > 10:
                final_entropy = entropy_data[-1]
                if final_entropy < 0.01:
                    analysis["issues"].append("Entropy collapsed - policy stopped exploring")
                elif final_entropy < 0.1:
                    analysis["warnings"].append("Entropy very low - consider increasing beta")
        
        # Check loss stability
        if "Losses/Policy Loss" in metrics:
            loss_data = metrics["Losses/Policy Loss"]["values"]
            if len(loss_data) > 10:
                recent_spikes = [i for i in range(1, len(loss_data)) 
                               if abs(loss_data[i] - loss_data[i-1]) > np.std(loss_data) * 3]
                if len(recent_spikes) > 3:
                    analysis["warnings"].append("Policy loss unstable - consider reducing learning rate")
        
        return analysis
    
    def generate_report(self, run_path: Path) -> str:
        """Generate a text report for a training run."""
        ea = self.load_events(run_path)
        metrics = self.get_metrics(ea)
        analysis = self.analyze_training_health(metrics)
        
        report = []
        report.append(f"# Training Analysis: {run_path.name}")
        report.append("")
        
        # Summary
        report.append("## Summary")
        report.append(f"Status: {analysis['status'].upper()}")
        report.append("")
        
        # Key Metrics
        report.append("## Key Metrics (Latest)")
        for key in ["Environment/Cumulative Reward", "Policy/Entropy", 
                   "Environment/Episode Length"]:
            if key in metrics:
                vals = metrics[key]["values"]
                report.append(f"- {key}: {vals[-1]:.4f} (step {metrics[key]['steps'][-1]})")
        report.append("")
        
        # Issues
        if analysis["issues"]:
            report.append("## Issues Detected")
            for issue in analysis["issues"]:
                report.append(f"- ❌ {issue}")
            report.append("")
        
        # Warnings
        if analysis["warnings"]:
            report.append("## Warnings")
            for warning in analysis["warnings"]:
                report.append(f"- ⚠️ {warning}")
            report.append("")
        
        return "\n".join(report)


if __name__ == "__main__":
    analyzer = TrainingAnalyzer("results")
    latest_run = analyzer.find_latest_run()
    
    if latest_run:
        print(f"Analyzing: {latest_run}")
        report = analyzer.generate_report(latest_run)
        print(report)
        
        # Save report
        report_path = latest_run / "analysis_report.md"
        report_path.write_text(report)
        print(f"\nReport saved to: {report_path}")
    else:
        print("No training runs found")
```

### 13.3 Automated Hyperparameter Recommendations

```python
# hyperopt_recommender.py
"""
Generate hyperparameter recommendations based on training metrics.
"""

import numpy as np
from typing import Dict, List, Tuple


def analyze_and_recommend(metrics: Dict) -> Dict[str, any]:
    """Analyze metrics and return hyperparameter recommendations."""
    recommendations = {"actions": [], "confidence": "low"}
    
    # Get key data
    rewards = metrics.get("Environment/Cumulative Reward", {}).get("values", [])
    entropy = metrics.get("Policy/Entropy", {}).get("values", [])
    policy_loss = metrics.get("Losses/Policy Loss", {}).get("values", [])
    value_loss = metrics.get("Losses/Value Loss", {}).get("values", [])
    
    if len(rewards) < 10:
        recommendations["actions"].append("Wait for more training data")
        return recommendations
    
    # Analyze reward trend
    reward_trend = np.polyfit(range(len(rewards)), rewards, 1)[0]
    
    # Analyze entropy
    if entropy:
        final_entropy = entropy[-1]
        if final_entropy < 0.01:
            recommendations["actions"].append(
                "INCREASE beta from current to 0.1 (entropy collapsed)"
            )
            recommendations["confidence"] = "high"
        elif final_entropy < 0.2:
            recommendations["actions"].append(
                "Slightly INCREASE beta to 0.02-0.05 (low exploration)"
            )
    
    # Analyze losses
    if policy_loss and len(policy_loss) > 5:
        loss_spikes = sum(1 for i in range(1, len(policy_loss))
                         if abs(policy_loss[i] - policy_loss[i-1]) > 
                                np.std(policy_loss) * 2)
        if loss_spikes > 3:
            recommendations["actions"].append(
                "DECREASE learning_rate by 50% (unstable policy loss)"
            )
            recommendations["actions"].append(
                "DECREASE epsilon to 0.1 (reduce update magnitude)"
            )
    
    # Reward not improving
    if reward_trend < 0.001 and rewards[-1] < 0.1:
        recommendations["actions"].append(
            "Check reward signal - no improvement detected"
        )
        recommendations["actions"].append(
            "Consider: 1) verify rewards are reaching agent, "
            "2) increase curiosity strength, 3) use BC/GAIL"
        )
    
    # Positive trends
    if reward_trend > 0.01:
        recommendations["actions"].append(
            "Training healthy - continue current config"
        )
        recommendations["confidence"] = "high"
    
    return recommendations
```

### 13.4 Real-Time Monitoring Dashboard

```python
# training_monitor.py
"""
Real-time training monitor - polls TensorBoard events and alerts.
"""

import time
import json
import smtplib
from pathlib import Path
from typing import Optional

from tensorboard.backend.event_processing import event_accumulator


class TrainingMonitor:
    """Monitor training in real-time and alert on issues."""
    
    def __init__(self, run_path: str, alert_email: Optional[str] = None):
        self.run_path = Path(run_path)
        self.alert_email = alert_email
        self.last_entropy = None
        self.last_reward = None
        self.alert_cooldown = 300  # 5 minutes between alerts
        self.last_alert_time = 0
        
    def check_training(self) -> dict:
        """Check current training status."""
        ea = event_accumulator.EventAccumulator(str(self.run_path))
        ea.Reload()
        
        status = {"healthy": True, "alerts": []}
        
        # Check entropy
        entropy_data = ea.Scalars("Policy/Entropy")
        if entropy_data:
            current_entropy = entropy_data[-1].value
            if self.last_entropy is not None:
                if current_entropy < 0.01 and self.last_entropy > 0.01:
                    status["alerts"].append(
                        f"ENTROPY COLLAPSED: {current_entropy:.4f}"
                    )
                    status["healthy"] = False
            self.last_entropy = current_entropy
        
        # Check reward
        reward_data = ea.Scalars("Environment/Cumulative Reward")
        if reward_data:
            current_reward = reward_data[-1].value
            status["current_reward"] = current_reward
            status["current_step"] = reward_data[-1].step
            
            # No improvement over extended period
            if len(reward_data) > 100:
                window = reward_data[-50:]
                if max(r.value for r in window) - min(r.value for r in window) < 0.01:
                    status["alerts"].append(
                        "No reward improvement in last 50 summaries"
                    )
        
        return status
    
    def send_alert(self, message: str):
        """Send alert email."""
        if not self.alert_email:
            return
            
        now = time.time()
        if now - self.last_alert_time < self.alert_cooldown:
            return
        
        # Email sending logic here
        print(f"ALERT: {message}")
        self.last_alert_time = now
    
    def run(self, interval: int = 60):
        """Run continuous monitoring."""
        print(f"Monitoring {self.run_path}...")
        while True:
            status = self.check_training()
            if status["alerts"]:
                for alert in status["alerts"]:
                    print(f"⚠️ {alert}")
                    self.send_alert(alert)
            else:
                step = status.get("current_step", 0)
                reward = status.get("current_reward", 0)
                print(f"✓ Step {step}: reward = {reward:.4f}")
            
            time.sleep(interval)


if __name__ == "__main__":
    import sys
    run_path = sys.argv[1] if len(sys.argv) > 1 else "results/npc_dialogue_latest"
    monitor = TrainingMonitor(run_path)
    monitor.run(interval=60)
```

---

## 14. LLM Token Optimization for NPC Dialogue

### 14.1 Your Current Configuration

| Parameter | Current Value | Issue |
|-----------|---------------|-------|
| Model | `llama-3.2-3b-instruct` | ✅ Good choice (3B params = fast) |
| `max_tokens` | `-1` (unlimited) | ❌ Causes variable latency |
| Context Size | Model default (128K) | OK, but larger = more VRAM |
| Temperature | Not configured | Should tune for consistency |

### 14.2 Token Calculation for Dialogue NPCs

**Prompt Structure:**
```
System Prompt (~500 chars) → ~125 tokens
Player Context (~300 chars) → ~75 tokens  
NPC Personality (~200 chars) → ~50 tokens
─────────────────────────────────────────
Total Input: ~1000 chars → ~250 tokens
```

**Output Requirements:**
- NPC dialogue responses are SHORT - single sentences to short paragraphs
- Effect commands: `[EFFECT:FireBall:target=player]` format (~50 chars)
- Emotion tags: `[EMOTION:happy]` (~20 chars)

**Recommended Output Tokens:**
| Response Type | Chars | Tokens (≈÷4) | Max Tokens Setting |
|--------------|-------|--------------|---------------------|
| Short reply | 50-100 | 12-25 | 32 |
| With effect | 100-200 | 25-50 | 64 |
| Full response | 200-400 | 50-100 | 128 |

### 14.3 Recommended Configuration

```yaml
# LLMAgent / LLM Settings (in Unity Inspector)
Num Predict: 128          # Max output tokens (was -1/unlimited)
Temperature: 0.3           # Low = consistent, predictable dialogue
Top P: 0.9               # Default, good for dialogue
Top K: 40                # Default
Repeat Penalty: 1.1       # Prevent repetitive NPC lines

# For faster inference:
Context Size: 2048        # Sufficient for NPC dialogue history
Batch Size: 512           # Default, good for prompt processing
```

### 14.4 Why These Numbers?

| Setting | Value | Reasoning |
|---------|-------|----------|
| `max_tokens=128` | 128 tokens ≈ 300-500 chars | Enough for short paragraph + effect command. Prevents runaway generation that causes latency spikes. |
| `temperature=0.3` | Low | NPC personality should be consistent, not random |
| `context=2048` | 2K tokens | 8x the input requirement (250 tokens), leaves headroom |

**Latency Impact:**
- `max_tokens=-1` (unlimited): Variable, can generate 2000+ tokens = 5-10s latency
- `max_tokens=128`: Bounded, ~0.5-1s typical latency
- At 20 tokens/sec for 3B model: 128 tokens = ~6 seconds max

### 14.5 Token-to-Character Ratio Reference

| Model Size | English | Code | Mixed |
|------------|---------|------|-------|
| 3B params | 4 chars/token | 3 chars/token | 3.5 chars/token |
| 7B params | 4 chars/token | 3 chars/token | 3.5 chars/token |
| 70B params | 3.5 chars/token | 2.5 chars/token | 3 chars/token |

**For your `llama-3.2-3b-instruct`: Use ~4 chars/token estimate**

### 14.6 Monitoring Token Usage

Your agent already tracks latency. Here's how to correlate:

```
NpcDialogue/Latency/TotalMs 
  = QueueMs + ModelMs + ResponseParseMs

Expected at max_tokens=128:
- QueueMs: ~50-200ms (depends on concurrent requests)
- ModelMs: ~3000-6000ms (128 tokens @ ~20-40 tokens/sec)
- Total: ~3500-6500ms

If TotalMs >> 6500ms → model is generating beyond 128 tokens
```

### 14.7 Quick Configuration Checklist

- [ ] Set `Num Predict` = **128** in LLMAgent inspector
- [ ] Set `Temperature` = **0.3** (or 0.2-0.4 range)
- [ ] Set `Context Size` = **2048** (if you want to limit VRAM)
- [ ] Monitor `NpcDialogue/Latency/TotalMs` in TensorBoard
- [ ] Target: < 5000ms average latency

---

## References
