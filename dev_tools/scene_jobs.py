"""
dev_tools/scene_jobs.py — Behavior_Scene LLM analysis jobs.

Eight targeted jobs derived from a live scene inspection of Behavior_Scene.unity.
Each job reads the relevant source data, calls LM Studio, and writes a structured
report to dev_tools/reports/jobs/.

Run all jobs:
    python dev_tools/run_dev_tools.py jobs

Run a single job:
    python dev_tools/run_dev_tools.py jobs --job 01
    python dev_tools/run_dev_tools.py jobs --job 03   # stop-token fix first

Job index:
    01  NPC SemanticTag + profile consistency audit
    02  ML-Agents reward balance & MaxStep conflict
    03  LLM stop sequences + system prompt quality  ← run this first
    04  Effects catalog vs feedback data audit
    05  Waypoint semantic enrichment
    06  Adaptive tuner learning-rate review
    07  DebugWatchdog threshold calibration
    08  Performance_Culling re-enable assessment
"""

from __future__ import annotations

import json
import sys
import time

# Windows terminals default to cp1252; force UTF-8 so model output doesn't crash prints.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from dev_tools.lm_client import LmClient, CODE_MODEL, TEXT_MODEL, FAST_MODEL

# ── Paths ─────────────────────────────────────────────────────────────────────
REPO_ROOT    = Path(__file__).parent.parent.resolve()
DEVPROJECT   = REPO_ROOT / "DevProject"
PROFILES_DIR = DEVPROJECT / "Assets" / "Network_Game" / "Dialogue" / "Profiles"
OUTPUT_DIR   = DEVPROJECT / "output"
SCRIPTS_DIR  = DEVPROJECT / "Assets" / "ML-Agents" / "Scripts"
JOBS_DIR     = Path(__file__).parent / "reports" / "jobs"
# ─────────────────────────────────────────────────────────────────────────────

_SEPARATOR = "-" * 60


def _ts() -> str:
    return datetime.now().strftime("%H:%M:%S")


def _log(job_id: str, msg: str) -> None:
    print(f"[{_ts()}][JOB-{job_id}] {msg}", flush=True)


def _write_report(job_id: str, title: str, result: dict) -> Path:
    JOBS_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.now().strftime("%Y-%m-%d")
    path = JOBS_DIR / f"job_{job_id}_{date_str}.json"
    payload = {
        "job_id": job_id,
        "title": title,
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "result": result,
    }
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
    return path


def _read_asset(name: str, max_chars: int = 3000) -> str:
    p = PROFILES_DIR / name
    if not p.exists():
        return f"[file not found: {name}]"
    return p.read_text(encoding="utf-8", errors="replace")[:max_chars]


def _read_output(name: str, max_chars: int = 4000) -> str:
    p = OUTPUT_DIR / name
    if not p.exists():
        return f"[file not found: {name}]"
    return p.read_text(encoding="utf-8", errors="replace")[:max_chars]


def _read_feedback_summary(max_entries: int = 30) -> str:
    """Summarise feedback_log.jsonl — last N entries, stripped to essential fields."""
    p = OUTPUT_DIR / "feedback_log.jsonl"
    if not p.exists():
        return "[feedback_log.jsonl not found]"
    lines = p.read_text(encoding="utf-8", errors="replace").strip().splitlines()
    recent = lines[-max_entries:]
    summary = []
    for line in recent:
        try:
            e = json.loads(line)
            summary.append({
                "ts": e.get("ts", "")[:10],
                "npc": e.get("npc_id", ""),
                "effect": e.get("effect_name", ""),
                "type": e.get("effect_type", ""),
                "outcome": e.get("outcome", ""),
                "score": e.get("score", 0),
            })
        except json.JSONDecodeError:
            continue
    return json.dumps(summary, indent=2)


# ═════════════════════════════════════════════════════════════════════════════
# JOB-01 — NPC SemanticTag + Profile Consistency Audit
# ═════════════════════════════════════════════════════════════════════════════

def job_01_npc_audit(client: LmClient) -> dict:
    """
    Reads all 3 NPC .asset files + live inspector data captured during scene scan.
    Uses stateful two-turn: turn 1 builds understanding, turn 2 generates patches.
    """
    _log("01", "Starting NPC profile + SemanticTag audit")

    storm   = _read_asset("StormOracleProfile.asset", 2000)
    forge   = _read_asset("ForgeKeeperProfile.asset", 2000)
    archivist = _read_asset("ArchivistProfile.asset", 2000)

    # Scene inspector data captured via unityMCP
    inspector_data = """
NPC_StormOracle:
  DialogueSemanticTag.DisplayName = "Storm Oracle"
  DialogueSemanticTag.Description = ""          ← EMPTY
  DialogueSemanticTag.SemanticId  = "StormOracle"
  NpcDialogueActor.m_FaceMainCamera = false
  Has: DetailedStaticMeshCollisionAuthoring     ← INCONSISTENT (others don't)

NPC_ForgeKeeper:
  DialogueSemanticTag.DisplayName = ""          ← EMPTY
  DialogueSemanticTag.Description = ""          ← EMPTY
  DialogueSemanticTag.SemanticId  = "ForgeKeeper"
  NpcDialogueActor.m_FaceMainCamera = true
  Missing: DetailedStaticMeshCollisionAuthoring

NPC_Archivist:
  DialogueSemanticTag.DisplayName = "Archivist" (assumed from profile)
  DialogueSemanticTag.Description = ""          ← EMPTY
  DialogueSemanticTag.SemanticId  = "Archivist"
  NpcDialogueActor.m_FaceMainCamera = (unverified, likely inconsistent)
  Missing: DetailedStaticMeshCollisionAuthoring
"""

    system = (
        "You are a Unity NPC data auditor for a multiplayer RPG. "
        "Your job is to find inconsistencies between NPC component data and produce "
        "concrete patch recommendations. Be concise and precise."
    )

    # Turn 1: build context
    history: list[dict] = []
    context_msg = f"""Here are the 3 NPC Unity asset files and their live Inspector state.

=== INSPECTOR DATA ===
{inspector_data}

=== StormOracleProfile.asset ===
{storm}

=== ForgeKeeperProfile.asset ===
{forge}

=== ArchivistProfile.asset ===
{archivist}

Identify all inconsistencies across the 3 NPCs. Look at:
- Empty DisplayName or Description fields
- Inconsistent m_FaceMainCamera values
- Component presence mismatches (DetailedStaticMeshCollisionAuthoring)
- Profile data that could improve scene snapshot quality for the LLM
"""
    _log("01", "Turn 1 — reading context")
    r1 = client.ask_stateful(system, history, context_msg, model=TEXT_MODEL, max_tokens=400)
    _log("01", f"Turn 1 done ({len(r1)} chars)")

    # Turn 2: generate structured patch
    schema = {
        "type": "object",
        "properties": {
            "npcs": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "id":               {"type": "string"},
                        "displayName":      {"type": "string"},
                        "description":      {"type": "string"},
                        "faceMainCamera":   {"type": "boolean"},
                        "addCollisionAuth": {"type": "boolean"},
                        "issues":           {"type": "array", "items": {"type": "string"}},
                    },
                    "required": ["id", "displayName", "description",
                                 "faceMainCamera", "addCollisionAuth", "issues"],
                    "additionalProperties": False,
                },
            },
            "summary": {"type": "string"},
        },
        "required": ["npcs", "summary"],
        "additionalProperties": False,
    }

    _log("01", "Turn 2 — generating patch via json_schema")
    result = client.ask_schema(
        system=system + "\n\nPrevious analysis:\n" + r1,
        user=(
            "Now generate the patch JSON for all 3 NPCs. "
            "Write a 1-2 sentence Description per NPC using their lore. "
            "Set faceMainCamera=true for all (consistency fix). "
            "addCollisionAuth=true only if the NPC should have it based on role."
        ),
        schema=schema,
        schema_name="npc_patch",
        model=TEXT_MODEL,
        max_tokens=700,
    )

    path = _write_report("01", "NPC SemanticTag + Profile Audit", result)
    _log("01", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# JOB-02 — ML-Agents Reward Balance & MaxStep Conflict
# ═════════════════════════════════════════════════════════════════════════════

def job_02_reward_audit(client: LmClient) -> dict:
    _log("02", "Starting reward balance + MaxStep conflict audit")

    reward_params = """
NpcDialogueAgent (Inspector values, DialogueBridge GameObject):
  OutcomeRewardScale         = 1.0
  FeedbackScoreRewardScale   = 0.02
  FastResponseBonus          = 0.03   (threshold: <2000ms)
  AcceptableResponseBonus    = 0.01   (threshold: <6000ms)
  SlowResponsePenalty        = 0.03   (threshold: >15000ms)
  TimeoutPenalty             = 0.08
  RetryPenaltyPerAttempt     = 0.02
  EffectDecaySeconds         = 8.0

Code constants (NpcDialogueAgent.cs):
  FullArcBonus               = 0.02
  MaxSingleRewardComponent   = 0.5   (clip)
  IdleNearNpcCostPerStep     = 0.001
  MaxTurnsBeforeForceEnd     = 20
  ConversationTimeoutSeconds = 30.0

MaxStep conflict:
  Agent base component  MaxStep = 5      ← INSPECTOR VALUE (overrides at training)
  NpcDialogueAgent      MaxStep = 1000   ← INTENDED VALUE (comment says 1000)
  DecisionRequester     DecisionPeriod = 5  (Inspector)
  NpcDialogueAgent      m_DecisionPeriod = 2 (serialized field, but overridden in EnsureDecisionRequester())

Feedback data (last 30 sessions):
  453 total records in feedback_log.jsonl
  Most common outcomes: looks_correct, note_only, not_visible, wrong_target
"""

    schema = {
        "type": "object",
        "properties": {
            "maxstep_conflict": {
                "type": "object",
                "properties": {
                    "verdict":    {"type": "string"},
                    "root_cause": {"type": "string"},
                    "fix":        {"type": "string"},
                },
                "required": ["verdict", "root_cause", "fix"],
                "additionalProperties": False,
            },
            "decision_period_conflict": {
                "type": "object",
                "properties": {
                    "verdict": {"type": "string"},
                    "fix":     {"type": "string"},
                },
                "required": ["verdict", "fix"],
                "additionalProperties": False,
            },
            "reward_balance": {
                "type": "object",
                "properties": {
                    "feedback_scale_verdict":  {"type": "string"},
                    "latency_scale_verdict":   {"type": "string"},
                    "suggested_feedback_scale": {"type": "number"},
                    "suggested_timeout_penalty": {"type": "number"},
                },
                "required": ["feedback_scale_verdict", "latency_scale_verdict",
                             "suggested_feedback_scale", "suggested_timeout_penalty"],
                "additionalProperties": False,
            },
            "priority": {"type": "string", "enum": ["low", "medium", "high", "critical"]},
        },
        "required": ["maxstep_conflict", "decision_period_conflict", "reward_balance", "priority"],
        "additionalProperties": False,
    }

    result = client.ask_schema(
        system=(
            "You are a Unity ML-Agents reward function engineer. "
            "Analyse the given agent configuration for bugs and imbalances. "
            "FeedbackScore (0-6 range normalised to ±1) at scale 0.02 competes against "
            "OutcomeReward at scale 1.0 — assess if this ratio makes feedback too weak to learn from."
        ),
        user=reward_params,
        schema=schema,
        schema_name="reward_audit",
        model=TEXT_MODEL,
        max_tokens=600,
    )

    path = _write_report("02", "ML-Agents Reward Balance & MaxStep Conflict", result)
    _log("02", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# JOB-03 — LLM Stop Sequences + System Prompt Fix  (run FIRST)
# ═════════════════════════════════════════════════════════════════════════════

def job_03_llm_config(client: LmClient) -> dict:
    _log("03", "Starting LLM stop sequence + system prompt audit")

    config = """
Model in use:
  Name:       llama-3.2-3b-instruct@q4_k_s
  Format:     Llama 3 Instruct (uses <|begin_of_text|>, <|eot_id|>, <|end_of_text|>)
  Host:       100.80.22.49:7002 (LM Studio, Tailscale)

Current LLMAgent stop sequences in Unity Inspector:
  ["</s>", "[INST]", "User:", "Assistant:"]
  Note: </s> and [INST] are LLAMA-2 format tokens — NOT used by Llama 3.

Current system prompt (m_DefaultSystemPromptOverride):
  "Identify the player and use its data to customize your answers."
  Length: ~60 chars

LLMAgent settings:
  temperature     = 0.3
  topK            = 40
  topP            = 0.9
  repeatPenalty   = 1.1
  numPredict      = 2048
  cachePrompt     = true

NetworkDialogueService character budgets:
  RemoteSystemPromptCharBudget     = 5000
  RemoteUserPromptCharBudget       = 520
  RemoteHistoryMessageCharBudget   = 320  (per message)
  RemoteMaxHistoryMessages         = 6
  RemoteMaxPlayerCustomizationChars = 220
"""

    schema = {
        "type": "object",
        "properties": {
            "corrected_stop_tokens": {
                "type": "array",
                "items": {"type": "string"},
                "description": "The CORRECT stop tokens for Llama 3 Instruct (not the current wrong ones)",
            },
            "current_stop_tokens_problem": {"type": "string"},
            "system_prompt_improved": {
                "type": "string",
                "description": "An improved 2-3 sentence system prompt (not the original — write a better one)",
            },
            "history_budget_verdict": {"type": "string"},
            "temperature_verdict": {"type": "string"},
        },
        "required": [
            "corrected_stop_tokens", "current_stop_tokens_problem",
            "system_prompt_improved", "history_budget_verdict", "temperature_verdict",
        ],
        "additionalProperties": False,
    }

    result = client.ask_schema(
        system=(
            "You are an expert on Llama 3 tokenization and LLM API configuration. "
            "Llama 3 Instruct end-of-turn token is <|eot_id|>. "
            "The model also uses <|end_of_text|> as a hard stop. "
            "The WRONG current stop tokens are: </s> [INST] User: Assistant: — these are Llama-2 tokens. "
            "In your JSON response: "
            "corrected_stop_tokens must be the RIGHT tokens (e.g. <|eot_id|>), not the current wrong ones. "
            "system_prompt_improved must be a NEW improved prompt, not a copy of the original."
        ),
        user=config,
        schema=schema,
        schema_name="llm_config_audit",
        model=TEXT_MODEL,
        max_tokens=500,
    )

    path = _write_report("03", "LLM Stop Sequences + System Prompt", result)
    _log("03", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# JOB-04 — Effects Catalog vs Feedback Data Audit
# ═════════════════════════════════════════════════════════════════════════════

def job_04_effects_audit(client: LmClient) -> dict:
    _log("04", "Starting effects catalog audit")

    scene_effects = """
Scene hierarchy (Effects/*) — all GameObjects are DISABLED (pool pattern):
  Explosions:    BigExplosion, DustExplosion, SmallExplosion, TinyExplosion, EnergyExplosion
  Fire:          (7 children — names not expanded)
  Magic:         (2 children)
  Impacts:       (8 children)
  Smoke/Steam:   (8 children)
  Misc:          (13 children)
  Water:         (3 children)
  Legacy Particles: (5 children) ← uses old Unity particle system, not VFX Graph
"""

    tuning_json = _read_output("effect_feedback_tuning.json", 4000)
    feedback_summary = _read_feedback_summary(30)

    user_msg = f"""
{scene_effects}

=== effect_feedback_tuning.json (current multipliers + sample counts) ===
{tuning_json[:2000]}

=== Recent feedback_log.jsonl (last 30 records) ===
{feedback_summary}

Audit tasks:
1. Which effects have hit the MaxScaleMultiplier (3.0x) clamp? (scale >= 2.5 is near-clamp)
2. Which effects have low looksCorrectCount/sampleCount ratio (< 0.3)?
3. Which effects have lastOutcome=not_visible or wrong_target repeatedly?
4. Identify the Legacy Particles category risk — should any be migrated to VFX Graph?
5. Flag effects with 0 samples (never tested).
"""

    schema = {
        "type": "object",
        "properties": {
            "near_clamped": {
                "type": "array",
                "items": {"type": "string"},
            },
            "low_accuracy": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "effect":   {"type": "string"},
                        "ratio":    {"type": "number"},
                        "verdict":  {"type": "string"},
                    },
                    "required": ["effect", "ratio", "verdict"],
                    "additionalProperties": False,
                },
            },
            "persistent_problems": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "effect":       {"type": "string"},
                        "last_outcome": {"type": "string"},
                        "action":       {"type": "string"},
                    },
                    "required": ["effect", "last_outcome", "action"],
                    "additionalProperties": False,
                },
            },
            "legacy_particle_verdict": {"type": "string"},
            "untested_count": {"type": "integer"},
            "top_priority_fix": {"type": "string"},
        },
        "required": [
            "near_clamped", "low_accuracy", "persistent_problems",
            "legacy_particle_verdict", "untested_count", "top_priority_fix",
        ],
        "additionalProperties": False,
    }

    result = client.ask_schema(
        system=(
            "You are a Unity VFX and particle effect auditor. "
            "Analyse effect feedback data to find poorly performing effects and configuration issues. "
            "scaleMultiplier near 3.0 means the adaptive tuner has maxed out and is no longer effective. "
            "looksCorrect/sampleCount < 0.3 means the effect is failing more than 70% of the time."
        ),
        user=user_msg,
        schema=schema,
        schema_name="effects_audit",
        model=TEXT_MODEL,
        max_tokens=800,
    )

    path = _write_report("04", "Effects Catalog vs Feedback Audit", result)
    _log("04", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# JOB-05 — Waypoint Semantic Enrichment
# ═════════════════════════════════════════════════════════════════════════════

def job_05_waypoints(client: LmClient) -> dict:
    _log("05", "Starting waypoint semantic enrichment")

    spatial_data = """
Scene layout (world positions):
  SpawnPoint:     [0, 10, 0]  (player spawn, elevated)

  NPCs:
    NPC_StormOracle  pos=[12.64, 0.5, 0.0]   facing=+Z (east side)
    NPC_ForgeKeeper  pos=[0.85, 0.5, -14.56]  facing=+Z (south side)
    NPC_Archivist    pos=[0.47, 0.5, 16.76]   facing=-Z (north side, rotated 180)

  Waypoints (Transform-only, tagged "Waypoints", no semantic data):
    Waypoint1      pos=[-0.3, 1, 81.4]   → far north
    Waypoint1 (1)  pos=[0.12, 1, -71.2]  → far south
    Waypoint1 (2)  pos=[-62.9, 1.62, -0.46] → far west
    Waypoint1 (3)  pos=[56.42, 1.72, -0.18] → far east

  Environment/Structures: 61 static meshes (scene geometry)
  WaterPlane: disabled, at y=-5 (underground water)

Scene scale: waypoints are ~70-80 units from origin. NPCs are 0-16 units from origin.
The scene appears to be a large open arena with NPCs near centre and waypoints marking the outer perimeter.
"""

    schema = {
        "type": "object",
        "properties": {
            "waypoints": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "original_name":  {"type": "string"},
                        "suggested_name": {"type": "string"},
                        "semantic_id":    {"type": "string"},
                        "description":    {"type": "string"},
                        "nearest_npc":    {"type": "string"},
                        "role":           {"type": "string"},
                    },
                    "required": [
                        "original_name", "suggested_name", "semantic_id",
                        "description", "nearest_npc", "role",
                    ],
                    "additionalProperties": False,
                },
            },
            "patrol_suggestion": {"type": "string"},
            "add_semantic_tag": {"type": "boolean"},
        },
        "required": ["waypoints", "patrol_suggestion", "add_semantic_tag"],
        "additionalProperties": False,
    }

    result = client.ask_schema(
        system=(
            "You are a Unity game level designer and spatial analyst. "
            "Given NPC and waypoint positions in 3D space, suggest meaningful names "
            "and semantic metadata. The game is a fantasy RPG with dialogue-driven NPCs. "
            "Waypoints mark the outer perimeter of the play area. "
            "Names should reflect cardinal direction and lore context."
        ),
        user=spatial_data,
        schema=schema,
        schema_name="waypoint_enrichment",
        model=TEXT_MODEL,
        max_tokens=600,
    )

    path = _write_report("05", "Waypoint Semantic Enrichment", result)
    _log("05", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# JOB-06 — Adaptive Tuner Learning Rate Review
# ═════════════════════════════════════════════════════════════════════════════

def job_06_tuner_review(client: LmClient) -> dict:
    _log("06", "Starting adaptive tuner learning rate review")

    tuner_config = """
DialogueEffectFeedbackRuntimeTuner (Dialogue_RuntimeServices GameObject):
  m_EnableAdaptiveTuning      = true
  m_LearningRate              = 0.18         ← under review
  m_MinScaleMultiplier        = 0.35x
  m_MaxScaleMultiplier        = 3.0x
  m_MinDurationMultiplier     = 0.5x
  m_MaxDurationMultiplier     = 2.5x
  m_AttachPreferenceThreshold = 0.35
  m_FitPreferenceThreshold    = 0.35
  m_SaveDebounceSeconds       = 1.0
  m_LogUpdates                = true

Context:
  This is an ONLINE per-session adaptive tuner. It adjusts scale/duration multipliers
  each time a player rates an effect. The adjustment is applied immediately without
  training batch averaging. Sessions are short (~5-30 minutes per player session).
"""

    tuning_state = _read_output("effect_feedback_tuning.json", 3000)

    user_msg = f"""
{tuner_config}

Current tuning state (effect_feedback_tuning.json):
{tuning_state[:2000]}

Analysis tasks:
1. Is LearningRate=0.18 appropriate for online per-feedback adjustment?
   Compare to typical online learning rates (0.01-0.05 for SGD, 0.001-0.01 for Adam).
2. Which effects have scaleMultiplier approaching MaxScaleMultiplier (3.0)?
   These are stuck at the ceiling — the tuner can no longer help them.
3. Which effects show attachScore=0 with many samples? (suggests bad attach point logic)
4. Suggest a safe learning rate for this use case.
5. Should AttachPreferenceThreshold and FitPreferenceThreshold be different values?
"""

    schema = {
        "type": "object",
        "properties": {
            "learning_rate_verdict":  {"type": "string"},
            "suggested_learning_rate": {"type": "number"},
            "clamped_effects": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "effect":        {"type": "string"},
                        "scale":         {"type": "number"},
                        "recommendation":{"type": "string"},
                    },
                    "required": ["effect", "scale", "recommendation"],
                    "additionalProperties": False,
                },
            },
            "zero_attach_effects": {
                "type": "array",
                "items": {"type": "string"},
            },
            "threshold_verdict":      {"type": "string"},
            "suggested_attach_thresh": {"type": "number"},
            "suggested_fit_thresh":    {"type": "number"},
        },
        "required": [
            "learning_rate_verdict", "suggested_learning_rate",
            "clamped_effects", "zero_attach_effects",
            "threshold_verdict", "suggested_attach_thresh", "suggested_fit_thresh",
        ],
        "additionalProperties": False,
    }

    result = client.ask_schema(
        system=(
            "You are an expert in online learning algorithms and adaptive systems. "
            "This tuner runs on player feedback (1-3 ratings per session, not thousands). "
            "High learning rates cause oscillation — a single 'wrong_target' report could "
            "swing a multiplier by 0.18 in one step, which is large when the range is 0.35-3.0."
        ),
        user=user_msg,
        schema=schema,
        schema_name="tuner_review",
        model=TEXT_MODEL,
        max_tokens=700,
    )

    path = _write_report("06", "Adaptive Tuner Learning Rate Review", result)
    _log("06", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# JOB-07 — DebugWatchdog Threshold Calibration
# ═════════════════════════════════════════════════════════════════════════════

def job_07_watchdog(client: LmClient) -> dict:
    _log("07", "Starting DebugWatchdog calibration")

    watchdog_config = """
DebugWatchdog component (on NetworkDialogueService GameObject):
  m_PollInterval         = 1.0s  (checks service stats every second)

NetworkDialogueService relevant settings:
  m_RequestTimeoutSeconds       = 120s
  m_WarmupTimeoutSeconds        = 120s
  m_MaxConcurrentRequests       = 2
  m_MaxPendingRequests          = 50
  m_MinSecondsBetweenRequests   = 0.2s
  m_MaxRetries                  = 2
  m_RetryBackoffSeconds         = 0.5s
  m_LatencySampleWindow         = 128 samples
  m_RejectionReasonWindow       = 128 samples
  m_SummaryLogIntervalSeconds   = 30s
  m_RemoteMinRequestTimeoutSeconds = 240s

LLM model characteristics (from live test):
  Model:     llama-3.2-3b-instruct@q4_k_s (also q8_0 loaded)
  tok/s:     38.6 tokens/second (measured via native API stats)
  TTFT:      0.26s (time to first token)
  Context:   23000 tokens max

Typical response size:
  NPC dialogue: 30-80 tokens → ~1-2 seconds generation at 38 tok/s
  Full timeout at 120s → allows up to ~4600 tokens (way more than dialogue needs)
"""

    schema = {
        "type": "object",
        "properties": {
            "timeout_verdict": {"type": "string"},
            "suggested_request_timeout_s": {"type": "number"},
            "suggested_warmup_timeout_s":  {"type": "number"},
            "suggested_poll_interval_s":   {"type": "number"},
            "ttft_risk": {"type": "string"},
            "concurrent_request_verdict": {"type": "string"},
            "summary_log_verdict":        {"type": "string"},
            "notes":                      {"type": "string"},
        },
        "required": [
            "timeout_verdict", "suggested_request_timeout_s",
            "suggested_warmup_timeout_s", "suggested_poll_interval_s",
            "ttft_risk", "concurrent_request_verdict",
            "summary_log_verdict", "notes",
        ],
        "additionalProperties": False,
    }

    result = client.ask_schema(
        system=(
            "You are a Unity networking and diagnostics expert. "
            "The system serves NPC dialogue via a local LLM at 38 tok/s. "
            "Dialogue responses are 30-80 tokens, so actual generation is 1-2s. "
            "A 120s timeout is 60x the expected response time — far too long for "
            "a responsive game experience. Consider that users will feel hung if "
            "they wait more than 5-10 seconds for an NPC to reply."
        ),
        user=watchdog_config,
        schema=schema,
        schema_name="watchdog_calibration",
        model=TEXT_MODEL,
        max_tokens=500,
    )

    path = _write_report("07", "DebugWatchdog Threshold Calibration", result)
    _log("07", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# JOB-08 — Performance_Culling Re-enable Assessment
# ═════════════════════════════════════════════════════════════════════════════

def job_08_culling(client: LmClient) -> dict:
    _log("08", "Starting Performance_Culling re-enable assessment")

    scene_stats = """
Performance_Culling GameObject:
  Status:    DISABLED (activeSelf=false)
  Component: PerformanceCullingSetup
  Tag:       MainCamera  ← unusual tag for a culling manager

Scene complexity:
  Environment/Structures:   61 static mesh children (scene geometry)
  Environment/ReflectionProbes: 10 reflection probes
  Effects/*:                ~46 particle effect GameObjects (all disabled/pooled)
  NPCs:                     3 humanoid characters (NetworkObject + NetworkAnimator)
  Modern_HUD_Root:          Canvas with 9 components, 2 UIDocument children
  Runtime Network Stats Monitor: active, layer=UI (5)
  WaterPlane:               DISABLED (activeSelf=false)

Rendering stack:
  MainCamera: UniversalAdditionalCameraData (URP), CinemachineBrain
  PlayerCinemachineCamera: CinemachineCamera with CinemachineAutoFocus + ShotQualityEvaluator

Platform:
  Unity 6000.4.0b9 (Unity 6 Beta)
  Windows Editor / Windows build target
"""

    schema = {
        "type": "object",
        "properties": {
            "re_enable_safe": {"type": "boolean"},
            "risk_factors": {
                "type": "array",
                "items": {"type": "string"},
            },
            "likely_disable_reason":     {"type": "string"},
            "wrong_tag_verdict":         {"type": "string"},
            "suggested_conditions":      {"type": "string"},
            "reflection_probe_verdict":  {"type": "string"},
            "priority":                  {"type": "string", "enum": ["low", "medium", "high"]},
        },
        "required": [
            "re_enable_safe", "risk_factors", "likely_disable_reason",
            "wrong_tag_verdict", "suggested_conditions",
            "reflection_probe_verdict", "priority",
        ],
        "additionalProperties": False,
    }

    result = client.ask_schema(
        system=(
            "You are a Unity performance optimization expert specialising in URP rendering. "
            "PerformanceCullingSetup is a custom component, not a Unity built-in. "
            "It likely drives camera culling masks, LOD distances, or object occlusion. "
            "The scene has 61 static structures and 10 reflection probes — both are "
            "significant performance factors that culling management would address."
        ),
        user=scene_stats,
        schema=schema,
        schema_name="culling_assessment",
        model=TEXT_MODEL,
        max_tokens=500,
    )

    path = _write_report("08", "Performance_Culling Re-enable Assessment", result)
    _log("08", f"Done → {path}")
    return result


# ═════════════════════════════════════════════════════════════════════════════
# Runner
# ═════════════════════════════════════════════════════════════════════════════

# Priority order: JOB-03 (stop tokens) first, then diagnostic jobs, then enrichment
ALL_JOBS: dict[str, tuple[str, Any]] = {
    "03": ("LLM Config Fix",               job_03_llm_config),
    "02": ("Reward Balance & MaxStep",      job_02_reward_audit),
    "01": ("NPC Profile Audit",             job_01_npc_audit),
    "06": ("Adaptive Tuner Review",         job_06_tuner_review),
    "04": ("Effects Catalog Audit",         job_04_effects_audit),
    "07": ("Watchdog Calibration",          job_07_watchdog),
    "05": ("Waypoint Enrichment",           job_05_waypoints),
    "08": ("Culling Assessment",            job_08_culling),
}


def run_jobs(job_ids: list[str] | None = None) -> dict[str, Any]:
    """
    Run all jobs (or a subset by ID list) in priority order.
    Returns a summary dict keyed by job ID.
    """
    client = LmClient()
    if not client.is_available():
        print("[scene_jobs] ERROR: LM Studio unreachable at 100.80.22.49:7002")
        return {}

    loaded = client.list_loaded_models()
    print(f"[scene_jobs] Loaded models: {loaded}")

    # Load code model if not already present
    if CODE_MODEL not in loaded:
        print(f"[scene_jobs] Loading {CODE_MODEL} via lms...")
        client.load_model(CODE_MODEL)

    targets = {
        k: v for k, v in ALL_JOBS.items()
        if job_ids is None or k in job_ids
    }

    summary: dict[str, Any] = {}
    t0 = time.monotonic()

    for job_id, (title, fn) in targets.items():
        print(f"\n{_SEPARATOR}")
        print(f"[{_ts()}] JOB-{job_id}: {title}")
        print(_SEPARATOR)
        job_t0 = time.monotonic()
        try:
            result = fn(client)
            elapsed = time.monotonic() - job_t0
            summary[job_id] = {"status": "ok", "elapsed_s": round(elapsed, 1)}
            print(f"[{_ts()}] JOB-{job_id} completed in {elapsed:.1f}s")
        except Exception as ex:
            elapsed = time.monotonic() - job_t0
            summary[job_id] = {"status": "error", "error": str(ex), "elapsed_s": round(elapsed, 1)}
            print(f"[{_ts()}] JOB-{job_id} ERROR: {type(ex).__name__}: {ex}")

    total = time.monotonic() - t0
    print(f"\n{_SEPARATOR}")
    print(f"[{_ts()}] All jobs done in {total:.1f}s")
    for jid, s in summary.items():
        status_str = "OK" if s["status"] == "ok" else f"ERROR: {s.get('error','?')}"
        print(f"  JOB-{jid}: {status_str} ({s['elapsed_s']}s)")

    # Write overall summary
    JOBS_DIR.mkdir(parents=True, exist_ok=True)
    summary_path = JOBS_DIR / "jobs_summary.json"
    summary_path.write_text(
        json.dumps({"run_utc": datetime.now(timezone.utc).isoformat(), "jobs": summary},
                   indent=2),
        encoding="utf-8",
    )
    print(f"[{_ts()}] Summary: {summary_path}")
    return summary
