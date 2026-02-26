# Remote Mode Architecture - Why Dialogue_LLM_Server Is Deactivated

**Status:** ✅ This is **intentional and correct**  
**Date:** February 16, 2026

---

## Quick Answer

The `Dialogue_LLM_Server` GameObject is deactivated because your system is configured to use **Remote OpenAI Mode**:

- **LLMAgent.remote = true** (configured on NetworkDialogueService)
- **LM Studio runs separately** at `127.0.0.1:7002`
- **OpenAIChatClient** makes HTTP requests to LM Studio for inference
- **Local LLM server is not needed** because responses come over the network

The system still works perfectly because it's using the **remote endpoint** instead.

---

## Architecture: Remote vs Local Mode

### Mode 1: Local Mode (Disabled)
```
Player sends prompt
  ↓
NetworkDialogueService
  ↓
LLMAgent (local) ← Uses Dialogue_LLM_Server (must be active)
  ↓
llama.cpp inference (in-process)
  ↓
Response returned
```

### Mode 2: Remote Mode (Current / Active)
```
Player sends prompt
  ↓
NetworkDialogueService
  ↓
OpenAIChatClient (HTTP client)
  ↓
HTTP POST to LM Studio @ 127.0.0.1:7002
  ↓
LM Studio processes inference (external process)
  ↓
HTTP response with text
  ↓
Response returned
```

---

## How Remote Mode Works

### Startup Flow (What Happens at Scene Load)

1. **NetworkDialogueService.Awake()**
   - Finds LLMAgent component
   - Checks `m_LlmAgent.remote` flag

2. **ValidateLlmConfiguration()**
   - Detected: `m_LlmAgent.remote = true` ✓
   - Calls: `DisableUnusedLocalLlmServers()`

3. **DisableUnusedLocalLlmServers()**
   ```csharp
   // Find all LLM components in scene
   var llmServers = FindObjectsByType<LLMUnity.LLM>();
   
   // Deactivate each one
   for (int i = 0; i < llmServers.Length; i++)
   {
       llmServers[i].gameObject.SetActive(false);  // ← Dialogue_LLM_Server disabled here
       Log: "Deactivated local LLM server (remote mode active)"
   }
   ```

4. **Warmup Phase**
   - Checks: `if (UseOpenAIRemote)`
   - Creates: OpenAIChatClient
   - Verifies: Connection to LM Studio at 127.0.0.1:7002

5. **Ready for Dialogue**
   - Player sends prompt
   - System routes to OpenAIChatClient
   - OpenAIChatClient makes HTTP request
   - Response comes back from LM Studio

### Request Flow (What Happens During Dialogue)

```
RequestDialogue() called
  ↓
ProcessQueue()
  ↓
StartChatRequest()
  ↓
Property check: UseOpenAIRemote && m_OpenAIChatClient != null
  ↓
YES: m_OpenAIChatClient.ChatAsync(...)  ← HTTP to LM Studio
NO: m_LlmAgent.Chat()  ← Local inference
  ↓
Response returned
```

---

## Console Output Evidence

When you start Play Mode, you should see:

```log
[NG:Dialogue] LLMAgent is configured as remote; warmup/chat uses remote provider.
[NG:Dialogue] Deactivated local LLM server (remote mode active) | llm="Dialogue_LLM_Server (Llama)"
[NG:Dialogue] OpenAI chat client initialized | host=127.0.0.1 | port=7002 | model=llama-3.2-3b-instruct
```

This confirms:
1. Remote mode is active ✓
2. Local server was deactivated ✓
3. OpenAI (LM Studio) client ready ✓

---

## Why Use Remote Mode?

| Aspect | Remote Mode | Local Mode |
| --- | --- | --- |
| Startup time | ~100ms (network check) | 5-30s (model load) |
| Memory | ~50MB (client only) | ~2.3GB (full model) |
| Flexibility | Easy to change models | Model baked into build |
| Development | Can switch servers easily | Locked to one model |
| Performance | Depends on network | Depends on hardware |

**Remote mode is ideal for:**
- Development (quick iteration)
- Testing different models
- Offloading inference to dedicated GPU machine
- Reduced memory footprint

**Local mode is ideal for:**
- Shipping product (no external dependency)
- Offline gameplay
- Consistent performance

---

## Configuration: How Remote Mode Is Set

### On NetworkDialogueService (Inspector)

1. Find component: **NetworkDialogueService**
2. In Inspector, find: **LLM Agent** (serialized field)
3. Expand that LLMAgent reference
4. Look for checkbox: **Remote** ✓ (should be checked)

If Remote is unchecked:
- Local mode would activate
- Dialogue_LLM_Server would stay active
- System would use in-process llama.cpp

### LLMAgent also has:
- **Host:** 127.0.0.1
- **Port:** 7002
- **API Key:** (leave empty for local LM Studio)

---

## Current Setup (Your System)

```
✓ Remote mode enabled
✓ LM Studio running at 127.0.0.1:7002
✓ Dialogue_LLM_Server intentionally deactivated (not needed)
✓ All dialogue responses working via OpenAIChatClient HTTP
✓ Model: llama-3.2-3b-instruct (Q4_K_S)
```

---

## Verification Checklist

To confirm remote mode is working correctly:

- [ ] See log: "Deactivated local LLM server (remote mode active)" ✓
- [ ] See log: "OpenAI chat client initialized" ✓
- [ ] Dialogue responds within 90 seconds ✓
- [ ] Can see network request to 127.0.0.1:7002 (if monitoring)
- [ ] Dialogue_LLM_Server is inactive in Hierarchy ✓

If ANY of these are wrong → Remote mode may not be configured correctly

---

## Troubleshooting

### If dialogue is not responding:

1. **Check LM Studio:**
   ```
   Is LM Studio running? Yes/No
   Check: http://127.0.0.1:7002/v1/models (should return model list)
   ```

2. **Check Remote Flag:**
   - Open NetworkDialogueService in Inspector
   - Verify LLMAgent.Remote is **checked**

3. **Check Port:**
   - LLMAgent.Port should be **7002** (or whatever LM Studio is on)
   - LLMAgent.Host should be **127.0.0.1** (or your LM Studio machine IP)

4. **Check Console:**
   - Should see: "OpenAI chat client initialized"
   - If missing: Remote mode not enabled

### If you want to switch to LOCAL mode:

1. Uncheck **Remote** checkbox on LLMAgent
2. Verify **Dialogue_LLM_Server** activates in Hierarchy
3. Restart scene
4. Console should show: "LLM warmup complete" (local mode)

---

## Code References

### Where Deactivation Happens
**File:** [NetworkDialogueService.cs](NetworkDialogueService.cs#L420)  
**Method:** `DisableUnusedLocalLlmServers()`  
**Trigger:** `ValidateLlmConfiguration()` at startup

### Where Remote Requests Are Made
**File:** [NetworkDialogueService.cs](NetworkDialogueService.cs#L1412)  
**Method:** `StartChatRequest()`  
**Check:** `bool useOpenAI = UseOpenAIRemote && m_OpenAIChatClient != null;`

### OpenAI Client Implementation
**File:** [OpenAIChatClient.cs](OpenAIChatClient.cs)  
**Method:** `ChatAsync()`  
**Endpoint:** `POST http://{Host}:{Port}/v1/chat/completions`

---

## Summary

✅ **Dialogue_LLM_Server is deactivated BECAUSE:**
- Remote mode is configured
- LM Studio is running externally
- OpenAIChatClient handles all inference via HTTP
- No local LLM processing is happening

✅ **System is answering BECAUSE:**
- OpenAIChatClient makes HTTP requests to LM Studio
- LM Studio runs inference on the model
- Response is returned over HTTP
- Everything works without local LLMAgent

**This is the intended architecture. No problems to fix.** 

If you want to switch back to local mode, simply uncheck the Remote checkbox on LLMAgent and restart the scene.
