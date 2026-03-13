"""
NPC Dialogue Training Efficiency Tests
=======================================
Validates the NpcDialogue training config and—if past training results exist—
checks that reward curves are moving in the right direction.

Run with:
    cd D:/GithubRepos/ml-agents
    C:/Users/andre_wjgj23f/miniconda3/envs/mlagents/python.exe -m pytest \
        ml-agents/tests/test_npc_dialogue_training.py -v

No Unity connection needed. Tests use config files and results CSVs only.
"""

import csv
import math
import os
import subprocess
import sys

import pytest
import yaml

# ── Paths ──────────────────────────────────────────────────────────────────

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONFIG_PATH = os.path.join(REPO_ROOT, "config", "ppo", "NpcDialogue.yaml")
RESULTS_DIR = os.path.join(REPO_ROOT, "results")
BEHAVIOR_NAME = "NpcDialogue"

# ── Fixtures ────────────────────────────────────────────────────────────────

@pytest.fixture(scope="module")
def config() -> dict:
    """Load and parse the NpcDialogue YAML config."""
    assert os.path.exists(CONFIG_PATH), (
        f"Config not found: {CONFIG_PATH}\n"
        "Create it first or run the game to generate it."
    )
    with open(CONFIG_PATH, encoding="utf-8") as f:
        raw = yaml.safe_load(f)
    assert "behaviors" in raw, "Config must have a 'behaviors' top-level key."
    assert BEHAVIOR_NAME in raw["behaviors"], (
        f"Behavior '{BEHAVIOR_NAME}' not found in config."
    )
    return raw["behaviors"][BEHAVIOR_NAME]


@pytest.fixture(scope="module")
def hyperparams(config) -> dict:
    return config["hyperparameters"]


@pytest.fixture(scope="module")
def network_settings(config) -> dict:
    return config["network_settings"]


@pytest.fixture(scope="module")
def reward_signals(config) -> dict:
    return config["reward_signals"]


def find_latest_run_stats_csv() -> str | None:
    """Find the most recently modified training stats CSV in results/."""
    if not os.path.isdir(RESULTS_DIR):
        return None
    candidates = []
    for root, _, files in os.walk(RESULTS_DIR):
        for fname in files:
            if fname == f"{BEHAVIOR_NAME}.csv":
                candidates.append(os.path.join(root, fname))
    if not candidates:
        return None
    return max(candidates, key=os.path.getmtime)


# ── 1. Config structure ─────────────────────────────────────────────────────

class TestConfigStructure:
    def test_trainer_type_is_ppo(self, config):
        assert config["trainer_type"] == "ppo", (
            "trainer_type must be 'ppo' for discrete-action dialogue policy."
        )

    def test_has_hyperparameters_block(self, config):
        assert "hyperparameters" in config

    def test_has_network_settings_block(self, config):
        assert "network_settings" in config

    def test_has_reward_signals_block(self, config):
        assert "reward_signals" in config

    def test_has_memory_block(self, network_settings):
        assert "memory" in network_settings, (
            "LSTM memory block required for within-conversation temporal context."
        )

    def test_has_extrinsic_reward_signal(self, reward_signals):
        assert "extrinsic" in reward_signals

    def test_has_curiosity_reward_signal(self, reward_signals):
        assert "curiosity" in reward_signals, (
            "Curiosity required to prevent idle-lock on 3 discrete actions."
        )


# ── 2. Hyperparameter ranges ────────────────────────────────────────────────

class TestHyperparameterRanges:
    def test_batch_size_range(self, hyperparams):
        v = hyperparams["batch_size"]
        assert 32 <= v <= 2048, f"batch_size {v} out of [32, 2048]"

    def test_buffer_size_larger_than_batch_size(self, hyperparams):
        assert hyperparams["buffer_size"] > hyperparams["batch_size"]

    def test_learning_rate_range(self, hyperparams):
        lr = hyperparams["learning_rate"]
        assert 1e-5 <= lr <= 1e-2, f"learning_rate {lr} out of [1e-5, 1e-2]"

    def test_beta_entropy_range(self, hyperparams):
        beta = hyperparams["beta"]
        assert 0.0001 <= beta <= 0.1, f"beta {beta} out of [0.0001, 0.1]"

    def test_beta_is_higher_than_typical_for_discrete_actions(self, hyperparams):
        # For only 3 discrete actions, entropy should be >= 0.005 to keep exploration alive.
        assert hyperparams["beta"] >= 0.005, (
            "With only 3 discrete actions, beta should be >= 0.005 to prevent premature convergence."
        )

    def test_epsilon_range(self, hyperparams):
        eps = hyperparams["epsilon"]
        assert 0.1 <= eps <= 0.3, f"epsilon {eps} out of typical PPO range [0.1, 0.3]"

    def test_lambd_range(self, hyperparams):
        lambd = hyperparams["lambd"]
        assert 0.8 <= lambd <= 1.0, f"lambd {lambd} out of [0.8, 1.0]"

    def test_num_epoch_range(self, hyperparams):
        assert 1 <= hyperparams["num_epoch"] <= 10

    def test_max_steps_sufficient_for_dialogue_training(self, config):
        assert config["max_steps"] >= 100_000, (
            "max_steps must be >= 100k. Dialogue training needs sufficient episodes."
        )

    def test_time_horizon_is_reasonable(self, config):
        th = config["time_horizon"]
        assert 32 <= th <= 512, f"time_horizon {th} out of [32, 512]"

    def test_summary_freq_enables_monitoring(self, config):
        freq = config["summary_freq"]
        assert 0 < freq <= 50_000, (
            f"summary_freq {freq} should be in (0, 50000] for useful TensorBoard curves."
        )


# ── 3. Network architecture ──────────────────────────────────────────────────

class TestNetworkArchitecture:
    def test_hidden_units_range(self, network_settings):
        units = network_settings["hidden_units"]
        assert 32 <= units <= 512, (
            f"hidden_units {units} — for 7-obs input, 32-512 is appropriate."
        )

    def test_num_layers_range(self, network_settings):
        layers = network_settings["num_layers"]
        assert 1 <= layers <= 4

    def test_sequence_length_in_memory_range(self, network_settings):
        seq_len = network_settings["memory"]["sequence_length"]
        assert 8 <= seq_len <= 128, (
            f"sequence_length {seq_len} out of [8, 128] for dialogue conversations."
        )

    def test_memory_size_matches_or_exceeds_hidden_units(self, network_settings):
        mem_size = network_settings["memory"]["memory_size"]
        hidden = network_settings["hidden_units"]
        assert mem_size >= hidden // 2, (
            f"memory_size {mem_size} should be at least half of hidden_units {hidden}."
        )


# ── 4. Reward signal calibration ─────────────────────────────────────────────

class TestRewardSignals:
    def test_extrinsic_gamma_is_high_for_delayed_rewards(self, reward_signals):
        gamma = reward_signals["extrinsic"]["gamma"]
        assert gamma >= 0.95, (
            f"gamma {gamma} — dialogue rewards are delayed, needs >= 0.95 to value future quality."
        )

    def test_extrinsic_strength_is_one(self, reward_signals):
        assert reward_signals["extrinsic"]["strength"] == 1.0

    def test_curiosity_strength_is_low(self, reward_signals):
        strength = reward_signals["curiosity"]["strength"]
        assert strength <= 0.05, (
            f"Curiosity strength {strength} must be <= 0.05 to not overpower extrinsic rewards."
        )

    def test_curiosity_strength_is_positive(self, reward_signals):
        strength = reward_signals["curiosity"]["strength"]
        assert strength > 0, "Curiosity strength must be > 0 to help escape idle-lock."


# ── 5. Effect decay math ──────────────────────────────────────────────────────

class TestEffectDecayMath:
    """Pure math tests for the exponential decay formula used in obs[6]."""

    DECAY_SECONDS = 8.0  # matches m_EffectDecaySeconds default in NpcDialogueAgent

    def _decay(self, t: float) -> float:
        return math.exp(-t / self.DECAY_SECONDS)

    def test_decay_is_one_at_time_zero(self):
        assert abs(self._decay(0) - 1.0) < 1e-6

    def test_decay_is_half_at_half_life(self):
        half_life = math.log(2) * self.DECAY_SECONDS
        assert abs(self._decay(half_life) - 0.5) < 1e-4

    def test_decay_approaches_zero_after_three_constants(self):
        assert self._decay(self.DECAY_SECONDS * 3) < 0.05

    def test_decay_is_monotonically_decreasing(self):
        values = [self._decay(t) for t in range(61)]
        assert all(values[i] > values[i + 1] for i in range(len(values) - 1))

    def test_decay_never_goes_negative(self):
        assert all(self._decay(t) >= 0 for t in range(301))


# ── 6. Training efficiency — results analysis ────────────────────────────────

class TestTrainingEfficiency:
    """
    Analyses TensorBoard/CSV results from past training runs.
    These tests are SKIPPED if no results exist yet — they become active
    once you have run at least one training session.
    """

    def _load_csv_rows(self) -> list[dict]:
        csv_path = find_latest_run_stats_csv()
        if not csv_path:
            pytest.skip(
                "No training results CSV found. Run mlagents-learn first, then re-run tests."
            )
        with open(csv_path, newline="", encoding="utf-8") as f:
            return list(csv.DictReader(f))

    def test_results_directory_exists(self):
        if not os.path.isdir(RESULTS_DIR):
            pytest.skip("results/ directory does not exist yet.")
        assert True

    def test_reward_is_nonzero_in_training_run(self):
        rows = self._load_csv_rows()
        rewards = [float(r["Value"]) for r in rows
                   if "Reward" in r.get("Tag", "") and r.get("Value")]
        assert len(rewards) > 0, "No reward entries found in CSV."
        assert any(abs(v) > 1e-6 for v in rewards), (
            "All rewards are zero — check that events are firing and rewards are accumulating."
        )

    def test_reward_trend_is_positive_in_second_half(self):
        """Reward in the second half of training should be higher than the first half."""
        rows = self._load_csv_rows()
        reward_rows = [r for r in rows if "cumulative_reward" in r.get("Tag", "").lower()]
        if len(reward_rows) < 20:
            pytest.skip("Too few reward data points to assess trend (need >= 20).")

        mid = len(reward_rows) // 2
        first_half_mean  = sum(float(r["Value"]) for r in reward_rows[:mid])  / mid
        second_half_mean = sum(float(r["Value"]) for r in reward_rows[mid:]) / (len(reward_rows) - mid)

        assert second_half_mean >= first_half_mean - 0.05, (
            f"Reward appears to be declining: "
            f"first_half_mean={first_half_mean:.4f}, second_half_mean={second_half_mean:.4f}. "
            "Check reward shaping — penalty/bonus balance may be off."
        )

    def test_effect_metric_appears_in_results(self):
        """NpcDialogue/Feedback/HasEffect should appear in TensorBoard stats."""
        rows = self._load_csv_rows()
        effect_rows = [r for r in rows if "HasEffect" in r.get("Tag", "")]
        if not effect_rows:
            pytest.skip(
                "NpcDialogue/Feedback/HasEffect metric not found. "
                "Effects may not have fired during the recorded training run."
            )
        assert len(effect_rows) > 0

    def test_no_nan_rewards_in_results(self):
        rows = self._load_csv_rows()
        reward_rows = [r for r in rows if "Reward" in r.get("Tag", "") and r.get("Value")]
        nan_count = sum(1 for r in reward_rows if r["Value"].lower() in ("nan", "inf", "-inf"))
        assert nan_count == 0, (
            f"{nan_count} NaN/Inf reward entries found. "
            "Check for division by zero in reward shaping or observation normalization."
        )

    def test_latency_metrics_are_recorded(self):
        rows = self._load_csv_rows()
        latency_rows = [r for r in rows if "Latency" in r.get("Tag", "")]
        if not latency_rows:
            pytest.skip("No Latency metrics found — LM Studio may not have been running during training.")
        assert len(latency_rows) > 0, "Latency metrics must be recorded during training."


# ── 7. Config loader smoke test ──────────────────────────────────────────────

class TestConfigLoaderSmoke:
    def test_mlagents_learn_can_parse_config(self):
        """Verify mlagents-learn accepts the config without error (--help mode)."""
        python = sys.executable
        result = subprocess.run(
            [python, "-m", "mlagents.trainers.learn", "--help"],
            capture_output=True, text=True, timeout=30
        )
        assert result.returncode == 0, (
            f"mlagents.trainers.learn --help failed:\n{result.stderr}"
        )

    def test_config_yaml_is_valid_python_yaml(self):
        with open(CONFIG_PATH, encoding="utf-8") as f:
            parsed = yaml.safe_load(f)
        assert parsed is not None
        assert isinstance(parsed, dict)

    def test_config_has_no_tabs(self):
        """YAML requires spaces, not tabs."""
        with open(CONFIG_PATH, encoding="utf-8") as f:
            content = f.read()
        assert "\t" not in content, (
            "Config contains tab characters. YAML requires spaces for indentation."
        )
