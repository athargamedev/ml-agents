---
name: dev-learn
description: Update the project knowledge base from recent scan reports, training logs, and effect feedback. Use when the user wants to update memories, learn from a training run, push scan findings to the knowledge base, sync Serena memories, or after any significant code change or training session.
---

# Dev Learn Skill — Knowledge Base Updater

Synthesises recent project activity (code scans, training logs, effect feedback) into
persistent Claude memories and Serena project memories.

## NON-NEGOTIABLE RULES

1. ALWAYS run `update` command first — it drives LM Studio synthesis.
2. ALWAYS read both `memory/code_quality.md` and `memory/project_patterns.md` after update to verify content.
3. If new patterns discovered conflict with existing Serena memories — update the Serena memory immediately.
4. NEVER skip Serena memory sync when new HIGH-severity code patterns are confirmed.
5. If no scan report exists yet → tell user to run `/dev-scan` first, then come back.

## Decision Tree

| User Intent | Action |
|---|---|
| "Update memories" / "Learn from scan" | Run `update`, read outputs, sync Serena |
| "What did we learn?" / "Show insights" | Read `memory/code_quality.md` + `memory/project_patterns.md` |
| "Sync Serena memories" | Read latest findings, call `write_memory` for changed topics |
| "What's in the KB?" / "Show patterns" | Read Serena memory `npc_llm_dialogue/code_quality_findings` |
| "Learn from training run" | Run `update` (picks up `.codex/tmp/run_training.log` automatically) |
| "Learn from effect feedback" | Run `update` (picks up `output/effect_feedback_tuning.json` automatically) |

## Workflow

### Standard Update (after scan)
```
1. Run:   python dev_tools/run_dev_tools.py update
2. Read:  memory/code_quality.md        (new code quality entries)
3. Read:  memory/project_patterns.md    (new pattern/convention entries)
4. Check: are any new HIGH patterns discovered? → update Serena memory npc_llm_dialogue/code_quality_findings
5. Check: are new API patterns confirmed? → update Serena memory npc_llm_dialogue/csharp_coding_conventions
6. Report to user: "Learned X insights, Y patterns confirmed, Z invalidated"
```

### After Training Run
```
1. Run:   python dev_tools/run_dev_tools.py update
   (will pick up run_training.log automatically)
2. Read:  memory/project_patterns.md   (look for training health insights)
3. If training was healthy → update npc_llm_dialogue/ml_agents_training_knowledge_base
4. If issues found → note in code_quality.md + suggest training config changes
```

### Full Serena Sync (after major session)
```
1. Run update command
2. Read new entries in memory/code_quality.md
3. Read new entries in memory/project_patterns.md
4. For each new finding:
   - code_quality / gotcha → write to npc_llm_dialogue/code_quality_findings
   - architecture / convention → write to npc_llm_dialogue/csharp_coding_conventions
   - ml_agents → write to npc_llm_dialogue/ml_agents_training_knowledge_base
   - effects → write to npc_llm_dialogue/architecture_notes
5. Confirm: "Synced N entries to Serena memories"
```

## Serena Memory Map

| Finding Category | Target Serena Memory |
|---|---|
| `code_quality`, `gotcha` | `npc_llm_dialogue/code_quality_findings` |
| `architecture`, `convention` | `npc_llm_dialogue/csharp_coding_conventions` |
| `training`, `ml_agents` | `npc_llm_dialogue/ml_agents_training_knowledge_base` |
| `effects`, `vfx` | `npc_llm_dialogue/architecture_notes` |
| `api_change`, `schema` | `npc_llm_dialogue/project_api_schemas` |
| `lm_studio`, `bridge` | `npc_llm_dialogue/lm_studio_bridge` |

## Input Sources (read automatically by `update` command)

| File | Content |
|---|---|
| `dev_tools/reports/latest_scan_summary.md` | Most recent code scan findings |
| `.codex/tmp/run_training.log` | Training session health + metrics |
| `output/effect_feedback_tuning.json` | Effect feedback + scaleMultiplier state |
| `memory/code_quality.md` | Existing KB (deduplication baseline) |

## PYTHON PATH
`C:/Users/andre_wjgj23f/miniconda3/envs/mlagents/python.exe dev_tools/run_dev_tools.py update`

## Known Gotchas
- `update` requires at least ONE of: scan report, training log, or effect JSON. If all missing → nothing to learn.
- Deduplication is fuzzy (LLM-based) — occasionally similar entries slip through. Prune `code_quality.md` periodically.
- `patterns_invalidated` entries in output are important — they mean a previously held belief was WRONG. Always propagate invalidations to Serena.
- The LLM synthesis prompt is in `dev_tools/prompts/knowledge_synthesis.txt` — edit it to tune what kinds of insights get extracted.
