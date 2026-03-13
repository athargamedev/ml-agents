# NpcDialogueProfile Enhancements

New methods added to improve keyword lookup performance and reduce prompt bloat.

## Location
`Assets/Network_Game/Dialogue/NpcDialogueProfile.cs`

## New Methods

### GetKeywordIndex()
Returns a HashSet for O(1) keyword lookup instead of O(n) array scan:
```csharp
public HashSet<string> GetKeywordIndex()
```
- Builds index from bored keywords + all power keywords
- Case-insensitive comparison
- Call once and cache the result

### HasKeyword()
Fast O(1) containment check using pre-built index:
```csharp
public bool HasKeyword(string keyword, HashSet<string> cachedIndex)
```
- Returns false if index is null or keyword is empty
- Case-insensitive lookup

### BuildCompressedEffectGuide()
Builds compressed effect guide for LLM system prompt to prevent bloat:
```csharp
public string BuildCompressedEffectGuide(string listenerName, int maxPowers = 5)
```
- Limits output to maxPowers (default 5)
- Only includes enabled powers
- Format:
  ```
  [Effects] Append one hidden tag at the END of your response when a visual should appear.
  Format: [EFFECT: EffectName | Target: Player/Self/SceneName | Duration: sec | Scale: x]
  Omit the tag entirely if nothing visual is happening.
  
  - **Fireball**: Particle effect
    → [EFFECT: FireBall | Target: Player]
  ```

## Usage Example
```csharp
// O(1) keyword lookup
var index = profile.GetKeywordIndex();
bool hasFire = profile.HasKeyword("fireball", index);

// Compressed prompt (prevents LLM token overflow)
string guide = profile.BuildCompressedEffectGuide("Player", 5);
```

## Performance Impact
- Keyword lookup: O(n) → O(1)
- Prompt size: ~2000 chars → ~500 chars (with maxPowers=5)
