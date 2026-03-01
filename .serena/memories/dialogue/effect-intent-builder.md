# EffectIntentBuilder.cs

New class for unified effect intent extraction pipeline.

## Location
`Assets/Network_Game/Dialogue/EffectIntentBuilder.cs`

## Purpose
Combines structured tag parsing ([EFFECT:] tags) with heuristic parameter extraction in a single pipeline.

## Extraction Priority
1. [EFFECT:] tags from LLM (highest - structured)
2. Heuristic modifiers from plain text (applied to all intents)
3. Profile constraints (clamping)
4. Catalog validation (whitelist)

## Key Methods

### Build()
```csharp
public Effects.EffectIntent[] Build(string responseText, NpcDialogueProfile profile, string prompt = null)
```
Returns validated effect intents with modifiers applied.

### BuildWithCache()
Cached version with 5-second TTL for repeated calls:
```csharp
public Effects.EffectIntent[] BuildWithCache(string responseText, NpcDialogueProfile profile, string prompt = null)
```

### ClearCache()
```csharp
public void ClearCache()
```

## Dependencies
- EffectCatalog
- ParticleParameterExtractor
- Effects.EffectParser

## Usage
```csharp
var builder = new EffectIntentBuilder(catalog, enableDynamicParams: true);
var intents = builder.Build(responseText, profile, prompt);
```

## Notes
- Uses `Effects.EffectIntent` (full namespace to avoid conflicts)
- EffectIntent struct is in Network_Game.Dialogue.Effects namespace
- Cache duration is 5 seconds
