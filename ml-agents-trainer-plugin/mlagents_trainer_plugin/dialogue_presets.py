from typing import Any, Dict


def make_npc_dialogue_ppo_config(behavior_name: str = "NpcDialogue") -> Dict[str, Any]:
    """
    Create a PPO config for the NpcDialogue behavior that matches the tuned
    settings in config/ppo/NpcDialogue.yaml.

    Use this when you want to drive training programmatically (instead of via
    a static YAML file) while keeping the same hyperparameters as the example.
    """
    return {
        "behaviors": {
            behavior_name: {
                "trainer_type": "ppo",
                "hyperparameters": {
                    "batch_size": 256,
                    "buffer_size": 1024,
                    "learning_rate": 0.0002,
                    "learning_rate_schedule": "linear",
                    "beta": 0.05,
                    "epsilon": 0.2,
                    "lambd": 0.95,
                    "num_epoch": 3,
                },
                "network_settings": {
                    "normalize": True,
                    "hidden_units": 256,
                    "num_layers": 3,
                    "vis_encode_type": "simple",
                    "memory": {
                        "sequence_length": 64,
                        "memory_size": 256,
                    },
                },
                "reward_signals": {
                    "extrinsic": {
                        "gamma": 0.99,
                        "strength": 1.0,
                    },
                    "curiosity": {
                        "gamma": 0.99,
                        "strength": 0.005,
                        "network_settings": {
                            "hidden_units": 128,
                        },
                        "learning_rate": 0.0002,
                    },
                },
                "keep_checkpoints": 5,
                "max_steps": 5_000_000,
                "time_horizon": 128,
                "summary_freq": 1000,
            }
        }
    }

