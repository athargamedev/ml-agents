# 20× MCP Efficiency Plan for Multiplayer Dialogue Pipeline

**Goal:** Collapse 10-15 generic MCP round-trips per workflow into 1-2 domain-specific calls, guided by purpose-built Amp skills.

---

## Current Bottleneck Analysis

| Common Workflow | Calls Today | Target |
|---|---|---|
| "Test one effect on an NPC" | 12-15 (find NPC, read profile, read catalog, read hierarchy, trigger, validate, screenshot, console…) | **1-2** |
| "Add a new EffectDefinition asset" | 8-10 (create SO, set fields, refresh, validate, register in catalog, save) | **1** |
| "Get full pipeline diagnostic" | 6-8 (stats, LLM status, queue, profiles, catalog, console) | **1** |
| "Modify NPC prompt + test" | 10+ (read profile, modify SO field, refresh, validate, trigger dialogue, read response) | **2** |
| "Bulk-validate all effect tags" | N × 3 (per-tag: parse, catalog lookup, log) | **1** |
| "Create NPC profile from scratch" | 15+ (create SO, set 12+ fields, link powers, refresh, validate) | **1-2** |

**Root causes of inefficiency:**
1. **DialogueMCPBridge is read-only** — no write path via MCP for profiles, catalog, or effects.
2. **Only 6 custom MCP tools** — all for prompt toggling and menu execution; nothing domain-compound.
3. **No Amp skill** for dialogue/effects domain — AI agent doesn't know optimal call sequences.
4. **Editor tools are menu-driven** — structured results are lost; MCP gets only "menu executed."
5. **No compound operations** — "create effect + register + test" is 10 steps instead of 1.

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│  AMP SKILLS (context + routing layer)           │
│                                                 │
│  ┌─────────────┐  ┌──────────────────────────┐  │
│  │ dialogue-    │  │ effect-pipeline          │  │
│  │ automation   │  │ (create/test/validate)   │  │
│  └──────┬──────┘  └───────────┬──────────────┘  │
│         │                     │                  │
│         ▼                     ▼                  │
│  ┌──────────────────────────────────────────┐   │
│  │  MCP CUSTOM TOOLS (compound operations)  │   │
│  │  ng_pipeline_status                      │   │
│  │  ng_create_effect_definition             │   │
│  │  ng_modify_npc_profile                   │   │
│  │  ng_test_effect_tag                      │   │
│  │  ng_simulate_llm_response                │   │
│  │  ng_bulk_validate_effects                │   │
│  │  ng_get_full_diagnostics                 │   │
│  │  ng_create_npc_profile                   │   │
│  │  ng_catalog_summary                      │   │
│  │  ng_probe_npc_dialogue                   │   │
│  └──────────────────┬───────────────────────┘   │
│                     │                            │
│                     ▼                            │
│  ┌──────────────────────────────────────────┐   │
│  │  EDITOR SERVICES (C# backing layer)      │   │
│  │  DialoguePipelineInspector               │   │
│  │  EffectDefinitionFactory                 │   │
│  │  ProfileAutomationService                │   │
│  │  EffectValidationService                 │   │
│  └──────────────────────────────────────────┘   │
└─────────────────────────────────────────────────┘
```

---

## Pillar 1: Amp Skills (2 skills)

### Skill 1: `dialogue-automation`

**Purpose:** Teaches the AI agent the dialogue pipeline domain so it picks the right tool on the first call instead of exploring generically.

**Contents:**
- Decision tree: "User wants X → call `ng_Y`"
- ScriptableObject field maps for `NpcDialogueProfile`, `EffectDefinition`, `EffectCatalog`
- Response schema reference (the `[EFFECT: Tag | Param: Value]` format)
- Common workflow recipes (1-2 calls each)
- Anti-patterns (don't use generic `manage_scriptable_object` for profiles — use `ng_modify_npc_profile`)
- Effect tag vocabulary with categories
- Safety rules from the Checklist.md (server-authoritative, validate after edit, etc.)

**Decision Tree:**

| User Intent | Skill Routes To |
|---|---|
| "What's the pipeline state?" | `ng_pipeline_status` |
| "Add a new fireball effect" | `ng_create_effect_definition` |
| "Change NPC personality/prompt" | `ng_modify_npc_profile` |
| "Test the Lightning effect" | `ng_test_effect_tag` |
| "Simulate an LLM response" | `ng_simulate_llm_response` |
| "Are all effects valid?" | `ng_bulk_validate_effects` |
| "Show me the full diagnostic" | `ng_get_full_diagnostics` |
| "Create a new NPC" | `ng_create_npc_profile` |
| "What effects are available?" | `ng_catalog_summary` |
| "Talk to the NPC" | `ng_probe_npc_dialogue` |

### Skill 2: `effect-pipeline`

**Purpose:** Focused skill for effect authoring and testing workflows. Loaded when the user is specifically working on VFX/effect creation and iteration.

**Contents:**
- Effect prefab discovery patterns (ParticlePack paths)
- EffectDefinition field reference (placement modes, target types, damage params)
- EffectParser tag syntax reference
- Preview/sandbox workflow
- Bulk importer integration
- Category → prefab mapping table

---

## Pillar 2: MCP Custom Tools (10 new tools)

All tools live in `Assets/Network_Game/Editor/CustomTools/` using the `[McpForUnityTool]` attribute pattern.

### Tool 1: `ng_pipeline_status`
**Collapses:** 6-8 calls → 1  
**Returns:** Combined snapshot of:
- DialogueMCPBridge.GetStats()
- DialogueMCPBridge.GetLLMStatus()
- DialogueMCPBridge.GetQueueStatus()
- EffectCatalog summary (count, categories, missing prefabs)
- Active NPC actors (count, names, profile IDs)
- Console error count
- Editor play state

```csharp
[McpForUnityTool("ng_pipeline_status",
    Description = "Get complete dialogue pipeline status in one call: LLM state, queue, stats, catalog health, active NPCs, and errors.")]
```

### Tool 2: `ng_create_effect_definition`
**Collapses:** 8-10 calls → 1  
**Params:** `effectTag`, `description`, `prefabName`, `placementMode`, `targetType`, `defaultScale`, `defaultDuration`, `defaultColor`, `alternativeTags[]`, `enableDamage`, `damageAmount`  
**Actions:**
1. Resolve prefab from ParticlePack by name
2. Create EffectDefinition ScriptableObject asset
3. Register in EffectCatalog.allEffects
4. Save assets
5. Return created asset path + validation status

```csharp
[McpForUnityTool("ng_create_effect_definition",
    Description = "Create a new EffectDefinition asset, link its prefab, register it in the EffectCatalog, and save. One call replaces 8-10 generic steps.")]
```

### Tool 3: `ng_modify_npc_profile`
**Collapses:** 6-10 calls → 1  
**Params:** `profileId`, `fields{}` (system_prompt, lore, display_name, bored_keywords[], enable_bored_light, dynamic_effect_params)  
**Actions:**
1. Find profile by ID
2. Apply field patches via SerializedObject
3. Save and validate
4. Return updated profile summary

```csharp
[McpForUnityTool("ng_modify_npc_profile",
    Description = "Patch NPC dialogue profile fields (prompt, lore, powers, keywords) by profileId. One call replaces 6-10 generic SO edits.")]
```

### Tool 4: `ng_test_effect_tag`
**Collapses:** 12-15 calls → 1  
**Params:** `effectTag`, `targetType` (optional: "player", "npc", "ground"), `npcProfileId` (optional)  
**Actions:**
1. Validate tag against catalog
2. Resolve NPC + player targets
3. Dispatch effect via DialogueSceneEffectsController (Play Mode) or EffectSandboxRunner (Edit Mode)
4. Return: tag valid, effect spawned, position, warnings

```csharp
[McpForUnityTool("ng_test_effect_tag",
    Description = "Validate an effect tag against the catalog and spawn it in-scene (PlayMode: networked dispatch, EditMode: sandbox preview). One call replaces 12+ steps.")]
```

### Tool 5: `ng_simulate_llm_response`
**Collapses:** 8-12 calls → 1  
**Params:** `responseText` (raw LLM text with [EFFECT:] tags), `npcProfileId` (optional)  
**Actions:**
1. Parse with EffectParser.ExtractIntents()
2. Validate all tags against catalog
3. Report: parsed intents, valid/invalid tags, stripped text, parameter overrides
4. Optionally dispatch valid effects in sandbox

```csharp
[McpForUnityTool("ng_simulate_llm_response",
    Description = "Parse a simulated LLM response through the full effect pipeline: extract tags, validate against catalog, report intents. Optionally spawn valid effects.")]
```

### Tool 6: `ng_bulk_validate_effects`
**Collapses:** N × 3 calls → 1  
**Params:** (none — validates everything)  
**Actions:**
1. Load EffectCatalog
2. For each definition: check prefab reference, check for missing fields, check tag uniqueness
3. Load all NpcDialogueProfiles
4. For each profile power: check prefab, check keywords, check catalog registration
5. Return: total effects, valid count, issues list

```csharp
[McpForUnityTool("ng_bulk_validate_effects",
    Description = "Validate all EffectDefinitions in the catalog and all NPC profile powers: missing prefabs, duplicate tags, unregistered effects. Full health report in one call.")]
```

### Tool 7: `ng_get_full_diagnostics`
**Collapses:** 6-8 calls → 1  
**Actions:** Aggregates ALL diagnostic data:
- Pipeline stats + LLM status + queue
- Effect catalog health (from bulk_validate)
- NPC profile summaries
- Scene snapshot (nearby objects)
- Console errors/warnings (last 20)
- Conversation history summaries
- Feedback tuning state

```csharp
[McpForUnityTool("ng_get_full_diagnostics",
    Description = "Complete dialogue system diagnostic dump: pipeline stats, LLM health, catalog validation, NPC profiles, scene context, console errors — all in one call.")]
```

### Tool 8: `ng_create_npc_profile`
**Collapses:** 15+ calls → 1  
**Params:** `profileId`, `displayName`, `systemPrompt`, `lore`, `powers[]` (each with effectTag, keywords, prefabName), `boredKeywords[]`, `enableDynamicParams`  
**Actions:**
1. Create NpcDialogueProfile ScriptableObject
2. Set all fields
3. Resolve and link power prefabs
4. Save to Profiles directory
5. Return created asset path + power link status

```csharp
[McpForUnityTool("ng_create_npc_profile",
    Description = "Create a complete NPC dialogue profile with persona, powers, and keywords in one call. Resolves prefab links automatically.")]
```

### Tool 9: `ng_catalog_summary`
**Collapses:** 3-5 calls → 1  
**Returns:** Structured catalog data:
- All effect tags grouped by category/element
- Each with: tag, description, placement mode, has prefab, alternative tags
- Total count, categories breakdown
- LLM-ready prompt catalog string

```csharp
[McpForUnityTool("ng_catalog_summary",
    Description = "Get the full EffectCatalog as structured data: all tags, descriptions, categories, prefab status, and the LLM-ready prompt string.")]
```

### Tool 10: `ng_probe_npc_dialogue`
**Collapses:** 8-12 calls → 1  
**Params:** `npcProfileId` (optional), `playerMessage`, `expectEffectTags` (optional bool)  
**Actions:**
1. Find NPC actor + resolve conversation key
2. Submit player prompt via NetworkDialogueService
3. Wait for response (with timeout)
4. Parse response for effect tags
5. Return: NPC response text, parsed intents, effect dispatch status

```csharp
[McpForUnityTool("ng_probe_npc_dialogue",
    Description = "Send a player message to an NPC and get the full response with parsed effect intents. Play Mode only. One call replaces the full probe workflow.")]
```

---

## Pillar 3: Editor Services (C# backing layer)

These are **not** MCP tools — they're internal C# services that the tools call. They encapsulate reusable logic.

### Service 1: `DialoguePipelineInspector` (Editor-only static class)
- `GetPipelineSnapshot()` → aggregated status dict
- `GetCatalogHealth()` → validation report
- `GetProfileSummaries()` → all profiles compact
- `GetActiveNpcSummary()` → spawned NPCs with network IDs

### Service 2: `EffectDefinitionFactory` (Editor-only static class)
- `CreateFromParams(tag, desc, prefabName, ...)` → creates + saves EffectDefinition SO
- `RegisterInCatalog(definition)` → adds to catalog's allEffects list and saves
- `ResolvePrefabByName(name)` → searches ParticlePack paths
- `ValidateDefinition(definition)` → returns issues list

### Service 3: `ProfileAutomationService` (Editor-only static class)
- `CreateProfile(id, name, prompt, lore, powers)` → creates + saves NpcDialogueProfile SO
- `PatchProfile(profileId, fieldPatches)` → modifies existing profile via SerializedObject
- `LinkPowerPrefabs(profile)` → resolves and links all power prefab references
- `GetProfileSchema()` → returns field names + types for tool parameter generation

### Service 4: `EffectValidationService` (Editor-only static class)
- `ValidateAll()` → full catalog + profile validation
- `ValidateTag(tag)` → single tag against catalog
- `ValidateProfile(profileId)` → single profile power links + keywords
- `GenerateHealthReport()` → markdown-formatted report

---

## Pillar 4: Workflow Recipes (in the Amp Skill)

Each recipe is a **1-2 call sequence** that replaces 10+ generic calls:

### Recipe: "Add New Effect End-to-End"
```
1. ng_create_effect_definition(effectTag="Meteor", prefabName="BigExplosion", ...)
   → Creates SO, links prefab, registers in catalog, returns path
2. ng_test_effect_tag(effectTag="Meteor")
   → Validates + spawns preview
```

### Recipe: "Create and Deploy New NPC"
```
1. ng_create_npc_profile(profileId="npc.blacksmith", displayName="Grim the Blacksmith", 
     systemPrompt="You are Grim...", powers=[{effectTag: "Fireball", keywords: ["fire","forge"]}])
   → Creates profile SO with linked powers
2. ng_pipeline_status()
   → Confirms registration, shows in active NPCs after scene placement
```

### Recipe: "Debug Why an Effect Isn't Firing"
```
1. ng_get_full_diagnostics()
   → Pipeline state, catalog health, console errors, NPC status — identifies the issue immediately
```

### Recipe: "Iterate on NPC Personality"
```
1. ng_modify_npc_profile(profileId="npc.elder", fields={system_prompt: "You are a wise elder..."})
2. ng_probe_npc_dialogue(npcProfileId="npc.elder", playerMessage="Tell me about the dragon")
   → Immediate feedback on prompt change
```

### Recipe: "Validate Before Merge"
```
1. ng_bulk_validate_effects()
   → Full health check: missing prefabs, duplicate tags, orphan definitions
```

---

## Implementation Priority

### Phase 1 — Highest Impact (week 1)
| Item | Type | Impact |
|---|---|---|
| `ng_pipeline_status` | MCP Tool | Most-used query, saves 6-8 calls every session |
| `ng_catalog_summary` | MCP Tool | Required for all effect work |
| `ng_test_effect_tag` | MCP Tool | Core iteration loop for VFX |
| `ng_get_full_diagnostics` | MCP Tool | Single-call debugging |
| `DialoguePipelineInspector` | Editor Service | Backs pipeline_status + diagnostics |
| `dialogue-automation` skill | Amp Skill | Routes all requests optimally from day 1 |

### Phase 2 — Authoring Acceleration (week 2)
| Item | Type | Impact |
|---|---|---|
| `ng_create_effect_definition` | MCP Tool | New effect in 1 call vs 8-10 |
| `ng_create_npc_profile` | MCP Tool | New NPC in 1 call vs 15+ |
| `ng_modify_npc_profile` | MCP Tool | Prompt iteration in 1 call |
| `EffectDefinitionFactory` | Editor Service | Backs create_effect_definition |
| `ProfileAutomationService` | Editor Service | Backs profile tools |
| `effect-pipeline` skill | Amp Skill | VFX authoring guidance |

### Phase 3 — Advanced Workflows (week 3)
| Item | Type | Impact |
|---|---|---|
| `ng_simulate_llm_response` | MCP Tool | Test parsing without LLM |
| `ng_bulk_validate_effects` | MCP Tool | Pre-merge health gate |
| `ng_probe_npc_dialogue` | MCP Tool | End-to-end dialogue testing |
| `EffectValidationService` | Editor Service | Backs validation tools |

---

## Efficiency Multiplier Breakdown

| Improvement | Factor |
|---|---|
| Compound MCP tools (10→1 calls) | **5-10×** per operation |
| Amp skill routing (no exploration/guessing) | **2-3×** per session |
| Structured returns (no re-querying for data) | **1.5-2×** per operation |
| Batch validation (N×3 → 1) | **10-50×** for validation |
| Domain-specific error messages | **2×** for debugging |
| **Combined** | **~20×** |

---

## File Locations

```
Assets/Network_Game/Editor/CustomTools/
├── NetworkGameMcpCustomTools.cs          ← existing (6 tools)
├── DialoguePipelineTools.cs              ← NEW (ng_pipeline_status, ng_get_full_diagnostics)
├── EffectAuthoringTools.cs               ← NEW (ng_create_effect_definition, ng_test_effect_tag, 
│                                                  ng_catalog_summary, ng_simulate_llm_response,
│                                                  ng_bulk_validate_effects)
├── ProfileManagementTools.cs             ← NEW (ng_create_npc_profile, ng_modify_npc_profile,
│                                                  ng_probe_npc_dialogue)
└── Services/
    ├── DialoguePipelineInspector.cs       ← NEW
    ├── EffectDefinitionFactory.cs         ← NEW
    ├── ProfileAutomationService.cs        ← NEW
    └── EffectValidationService.cs         ← NEW

.claude/skills/
├── dialogue-automation/SKILL.md          ← NEW
└── effect-pipeline/SKILL.md              ← NEW
```
