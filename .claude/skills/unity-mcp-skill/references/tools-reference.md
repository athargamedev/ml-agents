# Unity-MCP Tools Reference

Complete tool schemas for Unity MCP. Keep this as a parameter/type reference.

> Template warning: examples are templates and may vary by Unity version, package setup, or project conventions.

## Infrastructure Tools

### `batch_execute`
Run multiple tool calls in one request.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `commands` | `list[object]` | yes | Max 25 by default; each item: `{ "tool": str, "params": object }` |
| `parallel` | `bool` | no | Advisory hint; Unity may still execute sequentially |
| `fail_fast` | `bool` | no | Stop on first command failure |
| `max_parallelism` | `int` | no | Max parallel workers |

```python
batch_execute(commands=[
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "CubeA", "primitive_type": "Cube"}},
    {"tool": "manage_gameobject", "params": {"action": "create", "name": "CubeB", "primitive_type": "Cube"}}
], fail_fast=True)
```

### `set_active_instance`
Route subsequent tool calls to a specific Unity editor instance.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `instance` | `str` | yes | `Name@hash` or hash prefix |

### `refresh_unity`
Refresh assets and optionally request compilation.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `mode` | `str` | no | `if_dirty` or `force` |
| `scope` | `str` | no | `assets`, `scripts`, or `all` |
| `compile` | `str` | no | `none` or `request` |
| `wait_for_ready` | `bool` | no | Wait until editor is ready |

```python
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
```

## Scene Tools

### `manage_scene`
Scene operations and hierarchy/screenshot queries.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `get_hierarchy`, `screenshot`, `get_active`, `get_build_settings`, `create`, `load`, `save` |
| `page_size` | `int` | no | For `get_hierarchy`; default 50, max 500 |
| `cursor` | `int` | no | Pagination cursor |
| `parent` | `str|int` | no | Parent filter |
| `include_transform` | `bool` | no | Include transform data |
| `name` | `str` | no | For `create` |
| `path` | `str` | no | For `create`/`load` |

```python
manage_scene(action="get_hierarchy", page_size=50, cursor=0)
manage_scene(action="screenshot")
```

### `find_gameobjects`
Search for GameObjects and return IDs.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `search_term` | `str` | yes | Search value |
| `search_method` | `str` | no | `by_name`, `by_tag`, `by_layer`, `by_component`, `by_path`, `by_id` |
| `include_inactive` | `bool|str` | no | Include inactive objects |
| `page_size` | `int` | no | Default 50, max 500 |
| `cursor` | `int` | no | Pagination cursor |

```python
find_gameobjects(search_term="Player", search_method="by_name", page_size=50)
```

## GameObject Tools

### `manage_gameobject`
Create, modify, delete, duplicate, or move GameObjects.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `create`, `modify`, `delete`, `duplicate`, `move_relative` |
| `name` | `str` | no | Name for `create` |
| `primitive_type` | `str` | no | `Cube`, `Sphere`, `Capsule`, `Cylinder`, `Plane`, `Quad` |
| `target` | `str|int` | no | Name/path/instance ID |
| `search_method` | `str` | no | Target lookup strategy |
| `position` | `list[float]|str` | no | World position |
| `rotation` | `list[float]|str` | no | Euler rotation |
| `scale` | `list[float]|str` | no | Scale |
| `set_active` | `bool` | no | Active state |
| `layer` | `str|int` | no | Layer name/index |
| `components_to_add` | `list[str]` | no | Components to add |
| `components_to_remove` | `list[str]` | no | Components to remove |
| `component_properties` | `dict` | no | Nested component property payload |
| `save_as_prefab` | `bool` | no | Save created object as prefab |
| `prefab_path` | `str` | no | Prefab destination |
| `new_name` | `str` | no | For `duplicate` |
| `offset` | `list[float]|str` | no | Duplicate position offset |
| `reference_object` | `str|int` | no | For `move_relative` |
| `direction` | `str` | no | `left`, `right`, `up`, `down`, `forward`, `back` |
| `distance` | `float` | no | Move distance |
| `world_space` | `bool` | no | Relative move in world space |
| `parent` | `str|int` | no | Parent object for creation |

```python
manage_gameobject(action="create", name="MyCube", primitive_type="Cube", position=[0,1,0])
manage_gameobject(action="modify", target=12345, scale=[2,2,2])
```

### `manage_components`
Add/remove components or set component properties.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `add`, `remove`, `set_property` |
| `target` | `str|int` | yes | Prefer instance ID |
| `component_type` | `str` | yes | Component class/type name |
| `search_method` | `str` | no | Lookup strategy when target is not ID |
| `property` | `str` | no | Single property key |
| `value` | `any` | no | Value for single property |
| `properties` | `dict` | no | Multiple property key/value pairs |

```python
manage_components(action="add", target=12345, component_type="Rigidbody")
manage_components(action="set_property", target=12345, component_type="Rigidbody", property="mass", value=5.0)
```

## Script Tools

### `create_script`
Create a new C# script file.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `path` | `str` | yes | Assets-relative C# file path |
| `contents` | `str` | yes | Full script content |
| `script_type` | `str` | no | Optional hint, e.g. `MonoBehaviour` |
| `namespace` | `str` | no | Optional namespace |

```python
create_script(path="Assets/Scripts/MyScript.cs", contents="using UnityEngine;\npublic class MyScript : MonoBehaviour {}")
```

### `script_apply_edits`
Apply structured code edits by operations.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `name` | `str` | yes | Script name without `.cs` |
| `path` | `str` | yes | Folder path |
| `edits` | `list[object]` | yes | Edit ops: `replace_method`, `insert_method`, `delete_method`, `anchor_insert`, `regex_replace`, `prepend`, `append` |

```python
script_apply_edits(name="PlayerController", path="Assets/Scripts", edits=[
    {"op": "replace_method", "methodName": "Update", "replacement": "void Update() { }"}
])
```

### `apply_text_edits`
Apply exact text edits by line/column ranges (1-indexed).

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `uri` | `str` | yes | File URI, usually `mcpforunity://path/...` |
| `edits` | `list[object]` | yes | Items with `startLine`, `startCol`, `endLine`, `endCol`, `newText` |
| `precondition_sha256` | `str` | no | Prevent stale-file writes |
| `strict` | `bool` | no | Enable stricter checks |

```python
apply_text_edits(uri="mcpforunity://path/Assets/Scripts/MyScript.cs", edits=[
    {"startLine": 10, "startCol": 1, "endLine": 10, "endCol": 5, "newText": "void FixedUpdate()"}
], strict=True)
```

### `validate_script`
Validate C# script syntax/semantics.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `uri` | `str` | yes | Script URI |
| `level` | `str` | no | `basic` or `standard` |
| `include_diagnostics` | `bool` | no | Include detailed diagnostics |

```python
validate_script(uri="mcpforunity://path/Assets/Scripts/MyScript.cs", level="standard", include_diagnostics=True)
```

### `get_sha`
Return file SHA without fetching file content.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `uri` | `str` | yes | File URI |

### `delete_script`
Delete a script file.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `uri` | `str` | yes | Script URI |

## Asset Tools

### `manage_asset`
Search/import/create/modify/move/rename/delete assets.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `search`, `get_info`, `create`, `duplicate`, `move`, `rename`, `create_folder`, `delete` |
| `path` | `str` | no | Path or search scope |
| `search_pattern` | `str` | no | Glob or filter expression |
| `filter_type` | `str` | no | Asset type filter |
| `page_size` | `int` | no | Search pagination size |
| `page_number` | `int` | no | Search pagination page (1-based) |
| `generate_preview` | `bool` | no | Include previews |
| `asset_type` | `str` | no | For `create` |
| `properties` | `dict` | no | Create/modify payload |
| `destination` | `str` | no | For `duplicate`/`move`/`rename` |

```python
manage_asset(action="search", path="Assets", search_pattern="*.prefab", page_size=25, page_number=1)
manage_asset(action="create_folder", path="Assets/Materials")
```

### `manage_prefabs`
Headless prefab inspection and modification.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `get_info`, `get_hierarchy`, `create_from_gameobject`, `modify_contents` |
| `prefab_path` | `str` | yes | Assets-relative prefab path |
| `target` | `str|int` | no | Scene object or prefab child target |
| `allow_overwrite` | `bool` | no | For prefab creation |
| `position` | `list[float]|str` | no | For `modify_contents` |
| `components_to_add` | `list[str]` | no | For `modify_contents` |

```python
manage_prefabs(action="get_hierarchy", prefab_path="Assets/Prefabs/Player.prefab")
manage_prefabs(action="create_from_gameobject", target="Player", prefab_path="Assets/Prefabs/Player.prefab")
```

## Material and Shader Tools

### `manage_material`
Create materials and update material/renderer properties.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `create`, `get_material_info`, `set_material_shader_property`, `set_material_color`, `assign_material_to_renderer`, `set_renderer_color` |
| `material_path` | `str` | no | Target material path |
| `shader` | `str` | no | Shader name for create |
| `properties` | `dict` | no | Material properties |
| `property` | `str` | no | Shader/color property key |
| `value` | `any` | no | Shader property value |
| `color` | `list[number]` | no | RGBA |
| `target` | `str|int` | no | Renderer target |
| `slot` | `int` | no | Renderer material slot |
| `mode` | `str` | no | `shared`, `instance`, or `property_block` |

```python
manage_material(action="create", material_path="Assets/Materials/Red.mat", shader="Standard")
manage_material(action="assign_material_to_renderer", target="MyCube", material_path="Assets/Materials/Red.mat", slot=0)
```

### `manage_texture`
Create and procedurally modify textures.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `create`, `apply_pattern`, `apply_gradient` |
| `path` | `str` | yes | Texture asset path |
| `width` | `int` | no | For `create` |
| `height` | `int` | no | For `create` |
| `fill_color` | `list[number]` | no | RGBA |
| `pattern` | `str` | no | `checkerboard`, `stripes`, `dots`, `grid`, `brick` |
| `palette` | `list[list[number]]` | no | Colors |
| `pattern_size` | `int` | no | Pattern scale |
| `gradient_type` | `str` | no | `linear` or `radial` |
| `gradient_angle` | `number` | no | Gradient angle |

```python
manage_texture(action="create", path="Assets/Textures/Checker.png", width=64, height=64, fill_color=[255,255,255,255])
manage_texture(action="apply_pattern", path="Assets/Textures/Checker.png", pattern="checkerboard", pattern_size=8)
```

## Editor Control Tools

### `manage_editor`
Control play mode, active tool, tags, and layers.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | yes | `play`, `pause`, `stop`, `set_active_tool`, `add_tag`, `remove_tag`, `add_layer`, `remove_layer` |
| `tool_name` | `str` | no | For `set_active_tool` |
| `tag_name` | `str` | no | For tag actions |
| `layer_name` | `str` | no | For layer actions |

```python
manage_editor(action="play")
manage_editor(action="set_active_tool", tool_name="Move")
```

### `execute_menu_item`
Execute any Unity menu command by path.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `menu_path` | `str` | yes | Exact Unity menu path |

### `read_console`
Read or clear console messages.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `action` | `str` | no | `get` or `clear` |
| `types` | `list[str]` | no | `error`, `warning`, `log`, or `all` |
| `count` | `int` | no | Max messages when not paging |
| `filter_text` | `str` | no | Text filter |
| `page_size` | `int` | no | Paging size |
| `cursor` | `int` | no | Paging cursor |
| `format` | `str` | no | `plain`, `detailed`, `json` |
| `include_stacktrace` | `bool` | no | Include stack traces |

```python
read_console(action="get", types=["error"], count=10, include_stacktrace=True)
read_console(action="clear")
```

## Testing Tools

### `run_tests`
Start async Unity test execution.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `mode` | `str` | yes | `EditMode` or `PlayMode` |
| `test_names` | `list[str]` | no | Explicit tests |
| `group_names` | `list[str]` | no | Regex-style group filters |
| `category_names` | `list[str]` | no | NUnit category filter |
| `assembly_names` | `list[str]` | no | Assembly filter |
| `include_failed_tests` | `bool` | no | Return failed tests with details |
| `include_details` | `bool` | no | Return full result details |

```python
result = run_tests(mode="EditMode", test_names=["MyTests.TestA"], include_failed_tests=True)
```

### `get_test_job`
Poll a test job.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `job_id` | `str` | yes | Job from `run_tests` |
| `wait_timeout` | `int` | no | Wait up to N seconds |
| `include_failed_tests` | `bool` | no | Include failed test payload |
| `include_details` | `bool` | no | Include complete details |

## Search Tools

### `find_in_file`
Regex search within a file.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `uri` | `str` | yes | File URI |
| `pattern` | `str` | yes | Regex pattern |
| `max_results` | `int` | no | Match cap |
| `ignore_case` | `bool` | no | Case-insensitive search |

```python
find_in_file(uri="mcpforunity://path/Assets/Scripts/MyScript.cs", pattern="public void \\w+", max_results=200)
```

## Custom Tools

### `execute_custom_tool`
Execute a custom tool declared in the Unity project.

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `tool_name` | `str` | yes | Name from `mcpforunity://custom-tools` |
| `parameters` | `dict` | no | Tool-specific payload |

```python
execute_custom_tool(tool_name="my_custom_tool", parameters={"param1": "value", "param2": 42})
```
