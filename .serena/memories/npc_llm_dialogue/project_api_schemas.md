# Project API Schemas (auto-extracted 2026-02-28)

Source: `dev_tools/schemas/project_api_schema.json`
Regenerate: `python dev_tools/run_dev_tools.py schema`

---

## NpcDialogueAgent : Agent
File: `DevProject/Assets/ML-Agents/Scripts/NpcDialogueAgent.cs`
Namespace: (none — Assembly-CSharp)

### Observation Layout (Space Size = 7, FIXED)
| Index | Description | Range |
|-------|-------------|-------|
| [0] | Proximity to nearest NPC (0=far, 1=right next to it) | 0–1 |
| [1] | Player health normalised | 0–1 |
| [2] | Player in combat | 0 or 1 |
| [3] | Turn count / 10 (soft-normalised) | 0–∞ (capped) |
| [4] | Episode time / 60 (soft-normalised, max 1) | 0–1 |
| [5] | Conversation phase / 3 | 0=idle, 0.33=initiated, 0.67=responded, 1=resolved |
| [6] | Effect state — Mathf.Exp(-t / decaySeconds) | 0–1 |

### Action Layout
- 1 discrete branch, size 3
- 0 = idle | 1 = engage/continue | 2 = end conversation

### Enums
- `ConversationPhase`: Idle=0, Initiated=1, Responded=2, Resolved=3
- `DialogueRoutingMode`: ObserveOnly=0, SideChannelOverride=1

### Key Public Methods
- `Initialize()` — registers sidechannel, hooks, DecisionRequester
- `OnEpisodeBegin()` — resets all state including phase, turnCount, effect time
- `CollectObservations(VectorSensor)` — writes 7 floats
- `OnActionReceived(ActionBuffers)` — handles idle/engage/end, fires rewards
- `WriteDiscreteActionMask(IDiscreteActionMask)` — masks engage when far/maxTurns
- `Heuristic(in ActionBuffers)` — always returns action 1 for manual testing
- `OnDialogueTurnComplete(bool playerReplied)` — increments turn count + adds reward

### Serialized Inspector Fields
- `m_PlayerTransform : Transform`
- `m_GameState : GameStateProvider`
- (reward scales, threshold ms, and 8 other fields defined inline — see source)

---

## NetworkDialogueService : NetworkBehaviour
File: `DevProject/Assets/Network_Game/Dialogue/NetworkDialogueService.cs`
Namespace: `Network_Game.Dialogue`

### Static Events (subscribe for ML-Agents reward shaping)
```csharp
public static event Action<DialogueResponse> OnDialogueResponse;
public static event Action<DialogueResponseTelemetry> OnDialogueResponseTelemetry;
```

### Key Public API
```csharp
int  EnqueueRequest(DialogueRequest request)
bool TryEnqueueRequest(DialogueRequest, out int requestId, out string rejectionReason)
bool TryConsumeResponse(int requestId, out DialogueResponse response)
bool TryConsumeResponseByClientRequestId(int clientRequestId, out DialogueResponse response, ulong requestingClientId)
bool IsClientRequestInFlight(int clientRequestId, ulong requestingClientId)
bool TryGetPlayerIdentityByClientId(ulong clientId, out PlayerIdentitySnapshot snapshot)
bool SetPlayerPromptContext(ulong playerNetworkId, string nameId, string customizationJson)
DialogueStats GetStats()
```

### ML-Agents Override Hook
```csharp
public void SetMLAgentsSideChannelClient(IDialogueInferenceClient client)
```
Pass null to restore normal LM Studio path. Set SideChannelDialogueClient for bridge mode.

### Enums
- `DialogueStatus`: Pending, InProgress, Completed, Failed, Cancelled
- `PlayerSpecialEffectMode`
- `EffectSpatialType`

### Serialized Fields
- `m_LlmAgent : LLMAgent`
- `m_SceneEffectsController : DialogueSceneEffectsController`

---

## EffectDefinition : ScriptableObject
File: `DevProject/Assets/Network_Game/Dialogue/Effects/EffectDefinition.cs`
Namespace: `Network_Game.Dialogue.Effects`

### Key Fields
| Field | Type | Purpose |
|-------|------|---------|
| `effectTag` | string | LLM tag that triggers this effect |
| `effectPrefab` | GameObject | VFX prefab to spawn |
| `defaultScale/Duration/Color` | float/float/Color | defaults |
| `allow*` | bool | allow LLM to override scale/duration/color |
| `placementMode` | EffectPlacementMode | how to place in scene |
| `targetType` | EffectTargetType | who the effect targets |
| `preferFitTargetMesh` | bool | auto-fit scale to target bounds |
| `attachBone` | string | bone to attach to |
| `min/maxScale`, `min/maxRadius` | float | clamping ranges |
| `enableGameplayDamage` | bool | whether effect deals damage |
| `enableHoming` | bool | projectile tracking |
| `projectileSpeed`, `homingTurnRateDegrees` | float | projectile config |
| `damageAmount`, `damageRadius` | float | damage config |
| `alternativeTags` | string[] | aliases for the effect |

### Enums
- `EffectPlacementMode`
- `EffectTargetType`

---

## EffectCatalog : ScriptableObject
File: `DevProject/Assets/Network_Game/Dialogue/Effects/EffectCatalog.cs`
Namespace: `Network_Game.Dialogue.Effects`

### API
```csharp
bool TryGet(string tag, out EffectDefinition effect)   // ALWAYS check return value first
void RegisterRuntimeEffect(EffectDefinition def)
string GetPromptCatalog()   // returns LLM-ready list of all effect tags
static EffectCatalog Load()
void RebuildLookup()
```

### Fields
- `allEffects : List<EffectDefinition>` — all registered effects
- `fallbackEffectPrefab : GameObject` — used when tag not found + allowUnknownTags=true
- `logUnknownTags : bool`
- `allowUnknownTags : bool`

---

## NpcDialogueProfile : ScriptableObject
File: `DevProject/Assets/Network_Game/Dialogue/NpcDialogueProfile.cs`
Namespace: `Network_Game.Dialogue`

### API
```csharp
EffectDefinition ToEffectDefinition()
string[] GetKeywords()
static NpcDialogueProfile[] GetAllProfiles()
static NpcDialogueProfile GetProfile(string profileId)
HashSet<string> GetKeywordIndex()
bool HasKeyword(string keyword, HashSet<string> cachedIndex)
string BuildCompressedEffectGuide(string listenerName, int maxPowers = 5)
```
