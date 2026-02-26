---
name: unitymcp-custom-tools-dev
description: Create and validate docs-style UnityMCP custom tools for this project using McpForUnityTool attribute, MCPTests discovery checks, and transport-aware registration/listing steps.
---

# UnityMCP Custom Tools Dev

Use this skill when adding or debugging project custom tools (`[McpForUnityTool]`) in `Assets/.../Editor`.

## Required Pattern

- Static class
- `[McpForUnityTool("tool_name")]`
- `HandleCommand(JObject @params)`
- Return `SuccessResponse`, `ErrorResponse`, or `PendingResponse`

## Implementation Checklist

1. Add tool in a project `Editor/` assembly (not package unless intentionally modifying UnityMCP package).
2. Include nested `Parameters` class with `[ToolParameter]` metadata for better listing/help.
3. Compile/refresh Unity.
4. Check console errors.
5. Add/extend `Assets/MCPTests` discovery tests (`ToolDiscoveryService`-based).
6. Reconnect MCP client if tools do not appear.

## Important Transport/Listing Note

- Project-scoped custom tool registration/listing is HTTP-local sensitive in this UnityMCP build.
- If tools are discovered in Unity but missing in the client list, verify:
  - project-scoped tools enabled
  - HTTP local transport
  - client reconnect/reload

## Suggested Tests

- Discovery test: tool exists, `IsBuiltIn == false`
- Behavior test: invalid params return structured error
- Idempotency test: repeated apply returns `no_op`

## Project References

- `docs/docs-unity-mcp/reference/CUSTOM_TOOLS.md`
- `Assets/MCPTests/Tests/EditMode/Services/ToolDiscoveryServiceTests.cs`

