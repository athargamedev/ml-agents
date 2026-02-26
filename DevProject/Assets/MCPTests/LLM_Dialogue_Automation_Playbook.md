# LLM Dialogue Automation Playbook (Unity)

This document defines concrete interfaces and editor tooling to automate core systems in the project with a focus on the LLM dialogue pipeline and effects instantiation. It is aligned with the server-authoritative design and MCP-safe automation patterns validated in CI.

## 1) Goals

- Provide deterministic, server-authoritative dialogue + FX behavior
- Enable safe, repeatable automation for editing dialogue rules and effect mappings
- Expose clear interfaces for runtime dispatch and editor workflows
- Reduce prompt token usage while preserving control and safety

## 2) Core Runtime Interfaces

### 2.1 Effect Registry

```csharp
namespace Network_Game
{
    public interface IEffectRegistry
    {
        bool TryGetEffect(string effectKey, out EffectDefinition effect);
        IReadOnlyList<string> GetAllKeys();
    }

    public sealed class EffectDefinition
    {
        public string Key;
        public string AddressablePath;
        public string TargetScope; // e.g. "Self", "Target", "Area"
        public float CooldownSeconds;
        public bool RequiresNetworkObject;
        public string[] RequiredComponents; // e.g. "Transform", "Animator"
    }
}
```

### 2.2 Effect Dispatch

```csharp
namespace Network_Game
{
    public interface IEffectDispatcher
    {
        bool TryDispatch(EffectDispatchRequest request, out string error);
    }

    public sealed class EffectDispatchRequest
    {
        public string EffectKey;
        public ulong SourceNetworkId;
        public ulong TargetNetworkId;
        public Vector3 TargetPosition;
        public string[] Tags;
        public double ServerTime;
    }
}
```

### 2.3 Dialogue Output Contract

```csharp
namespace Network_Game
{
    public interface IDialogueIntentParser
    {
        bool TryParse(string llmText, out DialogueIntent intent);
    }

    public sealed class DialogueIntent
    {
        public string Utterance;
        public string[] EffectKeys;
        public string[] ActionTags;
        public string[] MemoryHints;
    }
}
```

### 2.4 Server-Authoritative Dialogue Service

```csharp
namespace Network_Game
{
    public interface INetworkDialogueService
    {
        void SubmitPlayerPrompt(ulong playerId, string prompt);
        void SubmitNpcResponse(ulong npcId, string llmResponse);
    }
}
```

## 3) Runtime Flow (Server-Authoritative)

1. Player input arrives at server
2. LLM response parsed into `DialogueIntent`
3. For each effect key:
   - validate in `IEffectRegistry`
   - resolve target (network IDs + transforms)
   - dispatch via `IEffectDispatcher`
4. Server emits ClientRpc to replicate effects

## 4) Editor Automation Interfaces

### 4.1 Prompt Profile Asset

```csharp
namespace Network_Game
{
    public interface IPromptProfile
    {
        string SystemPrompt { get; }
        string StylePrompt { get; }
        string SafetyPrompt { get; }
        string[] AllowedEffects { get; }
    }
}
```

### 4.2 Effect Mapping Asset

```csharp
namespace Network_Game
{
    public interface IEffectMappingAsset
    {
        IReadOnlyList<EffectDefinition> Effects { get; }
        bool Contains(string key);
    }
}
```

### 4.3 Dialogue Rule Set Asset

```csharp
namespace Network_Game
{
    public interface IDialogueRuleSet
    {
        string[] BannedTokens { get; }
        string[] RequiredTags { get; }
        string[] ResponseSchemas { get; }
    }
}
```

## 5) Automation Playbook: Safe Update Sequence

Use this exact order for scripted edits and tool-driven automation:

1. Load current assets and rules
2. Apply structured edits (insert/replace/delete)
3. Validate script: `validate_script(level:"standard")`
4. If validation fails, revert and mark effect key as blocked
5. On success, emit a versioned snapshot for audit

## 6) Prompt Efficiency Strategy

Reduce prompt size and increase reliability by:

- Fixed schema responses (effect keys + tags only)
- Short system prompt with explicit allowed effects
- Strict, minimal effect vocabulary (no synonyms unless mapped)
- Stable template per NPC role to avoid drift

Example LLM output schema:

```
<dialogue>
text: "..."
effects: ["ignite_torch", "sparks_small"]
</dialogue>
```

## 7) Skills and Editor Tools for LLM Communication

### 7.1 Recommended Skills (Human/Team)

- Prompt engineering with tight schemas
- Network authority patterns (Netcode for GameObjects)
- Addressables management
- UI Toolkit (for editor tools)
- Automated testing (EditMode)

### 7.2 Recommended Unity Editor Tools

- Custom Prompt Profile editor (UI Toolkit)
- Effect Registry inspector (readonly + validation)
- Dialogue Rule Set editor (schema + banned tokens)
- LLM Response Preview window (simulated intents)
- Effect Trigger Simulator (test effects without runtime)
- MCP automation scripts (batch edits + validation)

## 8) LLM Communication Workflow (Editor)

1. Select NPC + Prompt Profile
2. Preview expected response schema and allowed effects
3. Run a dry-run parse against a sample LLM response
4. Validate effect keys against the registry
5. Apply changes using scripted edits + validate

## 9) Audit & Reliability

Store every effect dispatch in a lightweight audit log:

- effect key, source/target, time, status
- error reason for invalid effect keys
- count per minute for cooldown enforcement

## 10) Next Implementation Steps

1. Implement `IEffectRegistry` + `IEffectDispatcher`
2. Add `PromptProfile` + `EffectMapping` ScriptableObjects
3. Build a small editor window for prompt schema and effects testing
4. Add a PlayMode test to simulate LLM response → effect dispatch
