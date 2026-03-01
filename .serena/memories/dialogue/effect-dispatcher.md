# EffectDispatcher.cs

New class created to extract effect dispatch logic from NetworkDialogueService for single responsibility principle.

## Location
`Assets/Network_Game/Dialogue/EffectDispatcher.cs`

## Purpose
Handles effect dispatch from dialogue responses to the scene. Separated from NetworkDialogueService (~5200 lines) to improve maintainability.

## Key Components

### DispatchContext struct
Simplified context for effect dispatch:
```csharp
public struct DispatchContext
{
    public string Prompt;
    public string ResponseText;
    public ulong SpeakerNetworkId;
    public ulong ListenerNetworkId;
    public NpcDialogueActor Actor;
    public NpcDialogueProfile Profile;
    public EffectModifier Modifier;
}
```

### EffectModifier struct
Per-player effect modifiers:
```csharp
public struct EffectModifier
{
    public float DamageScaleReceived;
    public float EffectSizeScale;
    public float EffectDurationScale;
    public Color? PreferredColor;
    public string PreferredElement;
    public bool IsShielded;
}
```

### Supported Effects
- Catalog intents (prefab powers from [EFFECT:] tags)
- Player special effects (dissolve, respawn)
- Dynamic parameter modifiers (NLP-extracted color, scale, duration)

### Main Method
```csharp
public void Dispatch(DispatchContext context)
```

## Usage
```csharp
var dispatcher = new EffectDispatcher(sceneEffects, catalog);
var context = new EffectDispatcher.DispatchContext
{
    Prompt = prompt,
    ResponseText = response,
    Actor = actor,
    Profile = profile,
    Modifier = EffectDispatcher.EffectModifier.Neutral
};
dispatcher.Dispatch(context);
```

## Dependencies
- DialogueSceneEffectsController
- EffectCatalog
- ParticleParameterExtractor
- Effects.EffectParser
