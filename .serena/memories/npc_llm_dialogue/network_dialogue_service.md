# NetworkDialogueService — Key Knowledge

## Overview
- ~8569-line NetworkBehaviour singleton in Network_Game.Dialogue namespace
- `NetworkDialogueService.Instance` — static singleton accessor
- Manages NPC dialogue queue, LLM inference routing, history, effects, retries

## Dialogue Call Chain (Player → LLM)
```
Player input → DialogueClientUI.SendPrompt() [~line 392]
    → ResolveParticipants() [~2497] — selects NPC by raycast/proximity
    → RequestDialogue() [~1387]
    → TryEnqueueRequest() [~981]
    → ProcessQueue() [~1557]
    → ExecuteRequestWorkerAsync() [~1659]
    → ResolveInferenceClient(useOpenAI) [~2852]  ← ML-Agents override hook
    → inferenceClient.ChatAsync(systemPrompt, history, userPrompt)
    → NpcDialogueActor.ShowSpeechText() [~494]  ← display in UI + speech bubble
```

## Static Events (used for ML-Agents reward shaping)
```csharp
// NetworkDialogueService.cs
public static event Action<DialogueResponse> OnDialogueResponse;
public static event Action<DialogueResponseTelemetry> OnDialogueResponseTelemetry;

// DialogueFeedbackCollector.cs line 55
public static event Action<FeedbackScoreSummary> OnFeedbackScored;
```

## Key Types
### DialogueStatus enum
Pending, InProgress, Completed, Failed, Cancelled

### DialogueResponse struct
- RequestId (int), Status (DialogueStatus), ResponseText, Error
- Request (DialogueRequest) — has IsUserInitiated, SpeakerNetworkId, Prompt, etc.

### DialogueResponseTelemetry struct
- RequestId, Status, Error, Request
- RetryCount (int)
- QueueLatencyMs, ModelLatencyMs, TotalLatencyMs (float)

### FeedbackScoreSummary struct (DialogueFeedbackCollector)
- RequestId, Score (int), HasEffect (bool), TagValid (bool), TagName, IsUserInitiated

## Full Public API (confirmed by schema extraction 2026-02-28)
```csharp
int  EnqueueRequest(DialogueRequest request)
bool TryEnqueueRequest(DialogueRequest, out int requestId, out string rejectionReason)
bool TryConsumeResponse(int requestId, out DialogueResponse response)
bool TryConsumeResponseByClientRequestId(int clientReqId, out DialogueResponse, ulong clientId)
bool TryGetTerminalResponseByClientRequestId(int clientReqId, out DialogueResponse, ulong clientId)
bool IsClientRequestInFlight(int clientReqId, ulong clientId)
bool TryGetPlayerIdentityByClientId(ulong clientId, out PlayerIdentitySnapshot snapshot)
bool TryGetPlayerIdentityByNetworkId(ulong playerNetworkId, out PlayerIdentitySnapshot snapshot)
bool SetPlayerPromptContext(ulong playerNetworkId, string nameId, string customizationJson)
bool ClearPlayerPromptContext(ulong playerNetworkId)
bool SetPlayerPromptContextForClient(ulong clientId, string nameId, string customizationJson)
bool ClearPlayerPromptContextForClient(ulong clientId)
bool RequestSetPlayerPromptContextFromClient(string nameId, string customizationJson)
bool RequestClearPlayerPromptContextFromClient()
string[] GetConversationKeys()
List<ChatMessage> GetHistoryPublic(string conversationKey)
void ClearHistory(string conversationKey)
void ClearPendingRequests()
DialogueStats GetStats()
void LogPlayerIdentityReport()
```

## IDialogueInferenceClient Interface
```csharp
string BackendName { get; }
bool ManagesHistoryInternally { get; }
Task<bool> CheckConnectionAsync(CancellationToken ct);
Task<string> ChatAsync(string systemPrompt,
    IReadOnlyList<DialogueInferenceMessage> history,
    string userPrompt, bool addToHistory = true, CancellationToken ct = default);
void ApplyConfig(DialogueInferenceRuntimeConfig config);
```

## ML-Agents Hook (SetMLAgentsSideChannelClient)
```csharp
// Added to NetworkDialogueService.cs (~line 2847)
public void SetMLAgentsSideChannelClient(IDialogueInferenceClient client)
{
    m_OverrideClient = client;  // null restores normal backend
}
```
In ObserveOnly mode, this is called with null — normal LM Studio path is used.
In SideChannelOverride mode, this is called with SideChannelDialogueClient.

## Null-Safety Rule (IMPORTANT — confirmed by code scan)
Always null-check Instance before calling methods:
```csharp
NetworkDialogueService.Instance?.DoThing();
// or:
if (NetworkDialogueService.Instance != null) NetworkDialogueService.Instance.DoThing();
```
WriteDiscreteActionMask in NpcDialogueAgent correctly checks:
```csharp
bool serviceAvailable = NetworkDialogueService.Instance != null && (...);
```
Other call sites may not — audit before adding new ones.

## Assembly Info
- Network_Game.asmdef: autoReferenced=true
- Unity.ML-Agents.asmdef: autoReferenced=true
- New scripts go in Assembly-CSharp (no explicit asmdef needed)
- `using Unity.MLAgents.Policies;` required for BehaviorParameters (not just Unity.MLAgents)
- `using Network_Game.Dialogue;` for types in this namespace
