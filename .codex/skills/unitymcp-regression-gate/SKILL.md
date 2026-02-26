---
name: unitymcp-regression-gate
description: Map UnityMCP/package/custom-tool changes to the right MCPTests subsets and produce a merge gate summary (pass/fail, risk, follow-up tests) for this project.
---

# UnityMCP Regression Gate

Use this skill when a change touches UnityMCP package code, project custom tools, transport setup, or automation interfaces.

## Goal

Run the smallest useful test set that still catches regressions, then summarize risk clearly.

## Change Classification

- **Custom tool only (`Assets/.../Editor/CustomTools`)**
  - Discovery tests
  - Behavior tests for new tool responses
- **Tool dispatcher / parser (`Manage*`, `Execute*`, parameter parsing)**
  - Tool-specific EditMode tests
  - Characterization tests if behavior intentionally changed
- **Transport / server / listing**
  - Services tests + characterization
  - Manual reconnect/listing verification notes
- **Gameplay runtime only (dialogue/effects/combat)**
  - Project runtime verification + targeted integration tests
  - MCPTests only if automation interfaces changed

## Minimum Output

- Files changed
- Test subsets run
- Pass/fail/blocked
- What was *not* tested
- Merge risk

## References

- `Assets/MCPTests/Tests/EditMode`
- `Assets/MCPTests/Tests/PlayMode`
- `Assets/MCPTests/README_LLM_Automation.md`

