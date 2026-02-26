---
name: unity-mcp-orchestrator
description: Orchestrate Unity Editor via MCP (Model Context Protocol) tools and resources. Use when working with Unity projects through MCP for Unity - creating/modifying GameObjects, editing scripts, managing scenes, running tests, or any Unity Editor automation. Provides best practices, tool schemas, and workflow patterns for effective Unity-MCP integration.
---

# Unity-MCP Operator Guide

This skill is optimized for direct execution of Unity MCP tasks. Use resource-first inspection, then tool execution.

## NON-NEGOTIABLE (failures happen if you skip these)

1. After any `create_script`, `script_apply_edits`, or `apply_text_edits`, you MUST run:
   - `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)`
2. Immediately after refresh, you MUST run `read_console(types=["error"], count=10, include_stacktrace=True)`.
3. NEVER guess resource URIs. Use the URI table below or call `ListMcpResourcesTool(server="UnityMCP")`.
4. For 2+ operations, ALWAYS use `batch_execute` (typically 10-100x faster).
5. Before complex operations (multi-step scene/script/batch/test), ALWAYS check `mcpforunity://editor/state`.

## Fast Execution Order

1. Read `mcpforunity://editor/state`.
2. If not ready, wait/retry using `recommended_retry_after_ms`.
3. Read resource context (object/prefab/project/tests).
4. Execute tools (prefer one `batch_execute` for grouped operations).
5. Verify with `read_console`, resources, and `manage_scene(action="screenshot")` when visual output matters.

## Complete URI Table (do not guess)

| URI | Purpose |
| --- | --- |
| `mcpforunity://editor/state` | Editor readiness, compile/domain reload status |
| `mcpforunity://editor/selection` | Current Unity selection |
| `mcpforunity://editor/active-tool` | Active transform/editor tool |
| `mcpforunity://editor/windows` | Open editor windows |
| `mcpforunity://editor/prefab-stage` | Active prefab stage context |
| `mcpforunity://scene/gameobject-api` | GameObject resource API guide |
| `mcpforunity://scene/gameobject/{instance_id}` | GameObject snapshot |
| `mcpforunity://scene/gameobject/{instance_id}/components` | Component list/properties (paginated) |
| `mcpforunity://scene/gameobject/{instance_id}/component/{component_name}` | Single component properties |
| `mcpforunity://prefab-api` | Prefab resource API guide |
| `mcpforunity://prefab/{encoded_path}` | Prefab metadata |
| `mcpforunity://prefab/{encoded_path}/hierarchy` | Prefab hierarchy |
| `mcpforunity://project/info` | Project metadata |
| `mcpforunity://project/tags` | Tag list |
| `mcpforunity://project/layers` | Layer map |
| `mcpforunity://menu-items` | Executable Unity menu item paths |
| `mcpforunity://custom-tools` | Project custom tool schemas |
| `mcpforunity://instances` | Connected Unity instances |
| `mcpforunity://tests` | All tests |
| `mcpforunity://tests/{mode}` | Tests filtered by `EditMode`/`PlayMode` |

## Decision Tree (top requests)

| User wants... | Use this first | Then do this |
| --- | --- | --- |
| Create object | `manage_gameobject(action="create")` | Add components via `manage_components(action="add")` |
| Move/rotate/scale object | `find_gameobjects` or object ID | `manage_gameobject(action="modify", ...)` |
| Add/remove component | `find_gameobjects` -> ID | `manage_components(action="add"/"remove")` |
| Set component properties | `mcpforunity://scene/gameobject/{id}/component/{name}` | `manage_components(action="set_property")` |
| Find objects by name/tag/layer/component | `find_gameobjects(...)` | Read details via `mcpforunity://scene/gameobject/{id}` |
| Create new script and attach | `create_script` | Script lifecycle recipe (below), then `manage_gameobject(...components_to_add...)` |
| Edit existing script safely | `get_sha` + `script_apply_edits` (or `apply_text_edits`) | Refresh + console check |
| Validate a script | `validate_script` | If valid, still refresh + check console before attach/use |
| Build or inspect scene hierarchy | `manage_scene(action="get_hierarchy")` | Paginate with `cursor`/`next_cursor` |
| Create/load/save scene | `manage_scene(action="create"/"load"/"save")` | Verify with screenshot + console |
| Work with prefabs | `manage_prefabs(...)` or prefab resources | For path resources, URL-encode path |
| Search/create/move/delete assets | `manage_asset(...)` | Use small page sizes on `search` |
| Create/apply material/texture | `manage_material` / `manage_texture` | Verify visually with screenshot |
| Run tests | `run_tests(mode=...)` | Poll with `get_test_job` until complete |
| Use multiple Unity editors | `mcpforunity://instances` | `set_active_instance(instance="Name@hash")` |

## Parameter Type Conventions

Use these shapes unless active tool schema says otherwise.

| Type | Accepted forms | Example |
| --- | --- | --- |
| `Vector2/3/4` | list numbers or JSON string | `[1,2,3]`, `"[1,2,3]"` |
| `Quaternion/Euler` | numeric list | `[0,90,0]` |
| `Color` | 0-255 RGBA or 0-1 RGBA | `[255,0,0,255]`, `[1,0,0,1]` |
| `bool` | boolean or lowercase string | `true`, `"true"` |
| `instance ID` | integer preferred | `12345` |
| `target` | ID, name, or path (ID safest) | `target=12345` |
| `file path` | assets-relative | `Assets/Scripts/Player.cs` |
| `uri` | `mcpforunity://path/...` or `file:///...` | `mcpforunity://path/Assets/Scripts/A.cs` |

Notes:
- For `manage_components(action="set_property")`, payload shape varies by component/property; inspect component resource first.
- For `manage_prefabs` resources, use URL-encoded path (`Assets/Prefabs/A.prefab` -> `Assets%2FPrefabs%2FA.prefab`).

## Script Lifecycle (required recipe)

1. Create or edit script (`create_script`, `script_apply_edits`, `apply_text_edits`).
2. Refresh compilation:
   - `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)`
3. Read compile errors:
   - `read_console(types=["error"], count=10, include_stacktrace=True)`
4. If errors exist: fix script and repeat steps 2-3 until clean.
5. Attach script component (`manage_gameobject(...components_to_add=["ClassName"])` or `manage_components(action="add")`).
6. Set script fields (`manage_components(action="set_property")`).
7. Verify runtime/editor behavior (`manage_scene(action="screenshot")`, optional play mode, tests).

## High-Value Patterns

- Batch pattern:
  - Use one `batch_execute(commands=[...], fail_fast=True)` for dependent chains.
  - Use `parallel=True` only as a hint for independent commands.
  - Keep each batch within current limit (default 25, configurable up to 100).
- Read-before-write pattern:
  - `find_gameobjects` -> resource read -> mutation tool.
- Pagination pattern:
  - Follow `next_cursor` until null.
- Verification pattern:
  - After major changes: `read_console(types=["error","warning"], count=10, format="detailed")`.

## Error Recovery Table

| Symptom | Likely cause | Recovery |
| --- | --- | --- |
| Tools return busy/not ready | Compiling or domain reload | Read `editor/state`; wait `recommended_retry_after_ms`; retry |
| Script component cannot be added | Compile errors | Run script lifecycle steps 2-4 |
| `stale_file` on text edit | SHA changed | Re-run `get_sha`, retry `apply_text_edits` with new precondition |
| Wrong objects changed | Name collision target | Re-run using `find_gameobjects`; target by instance ID |
| Prefab resource not found | Unencoded path | URL-encode path in `mcpforunity://prefab/{encoded_path}` |
| Commands affect wrong Unity project | Wrong active instance | Read `mcpforunity://instances`, then `set_active_instance` |
| UI interactions not working | Missing EventSystem/input module | Add EventSystem + correct input module |

## Tool Buckets

- Scene and hierarchy: `manage_scene`, `find_gameobjects`
- GameObjects/components: `manage_gameobject`, `manage_components`
- Scripts/text edits: `create_script`, `script_apply_edits`, `apply_text_edits`, `validate_script`, `get_sha`, `delete_script`, `find_in_file`, `refresh_unity`
- Assets/prefabs: `manage_asset`, `manage_prefabs`
- Materials/visuals: `manage_material`, `manage_texture`, `manage_shader`, `manage_vfx`, `manage_animation`
- Editor control: `manage_editor`, `execute_menu_item`, `read_console`
- Testing: `run_tests`, `get_test_job`
- Infrastructure/custom: `batch_execute`, `set_active_instance`, `execute_custom_tool`

## Deep Dives

- @references/tools-reference.md
- @references/resources-reference.md
- @references/workflows.md
