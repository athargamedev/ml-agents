# Unity-MCP Resources Reference

Read-only Unity resources for inspection before mutations.

URI pattern: `mcpforunity://{category}/{resource_path}[?query_params]`

Categories: `editor`, `scene`, `prefab`, `project`, `menu-items`, `custom-tools`, `tests`, `instances`.

## Editor

### `mcpforunity://editor/state`
Returns readiness and runtime state.

```json
{
  "unity_version": "2022.3.10f1",
  "is_compiling": false,
  "is_domain_reload_pending": false,
  "play_mode": {"is_playing": false, "is_paused": false},
  "active_scene": {"path": "Assets/Scenes/Main.unity", "name": "Main"},
  "ready_for_tools": true,
  "blocking_reasons": [],
  "recommended_retry_after_ms": null,
  "staleness": {"age_ms": 150, "is_stale": false}
}
```

### `mcpforunity://editor/selection`
```json
{"activeObject": "Player", "activeGameObject": "Player", "activeInstanceID": 12345, "count": 3, "gameObjects": ["Player", "Enemy", "Wall"], "assetGUIDs": []}
```

### `mcpforunity://editor/active-tool`
```json
{"activeTool": "Move", "isCustom": false, "pivotMode": "Center", "pivotRotation": "Global"}
```

### `mcpforunity://editor/windows`
```json
{"windows": [{"title": "Scene", "typeName": "UnityEditor.SceneView", "isFocused": true, "position": {"x": 0, "y": 0, "width": 800, "height": 600}}]}
```

### `mcpforunity://editor/prefab-stage`
```json
{"isOpen": true, "assetPath": "Assets/Prefabs/Player.prefab", "prefabRootName": "Player", "isDirty": false}
```

## Scene and GameObject

### `mcpforunity://scene/gameobject-api`
GameObject resource API description.

### `mcpforunity://scene/gameobject/{instance_id}`
Parameters:

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `instance_id` | `int` | yes | ID from `find_gameobjects` |

```json
{
  "instanceID": 12345,
  "name": "Player",
  "tag": "Player",
  "layer": 8,
  "layerName": "Player",
  "active": true,
  "activeInHierarchy": true,
  "isStatic": false,
  "transform": {"position": [0, 1, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1]},
  "parent": {"instanceID": 0},
  "children": [{"instanceID": 67890}],
  "componentTypes": ["Transform", "Rigidbody", "PlayerController"],
  "path": "/Player"
}
```

### `mcpforunity://scene/gameobject/{instance_id}/components`
Parameters:

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `instance_id` | `int` | yes | GameObject instance ID |
| `page_size` | `int` | no | Default 25, max 100 |
| `cursor` | `int` | no | Pagination cursor |
| `include_properties` | `bool` | no | Default true |

```json
{
  "gameObjectID": 12345,
  "gameObjectName": "Player",
  "components": [
    {"type": "Transform", "properties": {"position": {"x": 0, "y": 1, "z": 0}}},
    {"type": "Rigidbody", "properties": {"mass": 1.0, "useGravity": true}}
  ],
  "cursor": 0,
  "pageSize": 25,
  "nextCursor": null,
  "hasMore": false
}
```

### `mcpforunity://scene/gameobject/{instance_id}/component/{component_name}`
Parameters:

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `instance_id` | `int` | yes | GameObject instance ID |
| `component_name` | `string` | yes | Example: `Rigidbody` |

```json
{"gameObjectID": 12345, "gameObjectName": "Player", "component": {"type": "Rigidbody", "properties": {"mass": 1.0, "drag": 0, "angularDrag": 0.05, "useGravity": true, "isKinematic": false}}}
```

## Prefabs

### `mcpforunity://prefab-api`
Prefab resource API description.

### `mcpforunity://prefab/{encoded_path}`
Parameters:

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `encoded_path` | `string` | yes | URL-encoded prefab path |

Encoding: `Assets/Prefabs/Player.prefab` -> `Assets%2FPrefabs%2FPlayer.prefab`

```json
{"assetPath": "Assets/Prefabs/Player.prefab", "guid": "abc123...", "prefabType": "Regular", "rootObjectName": "Player", "rootComponentTypes": ["Transform", "PlayerController"], "childCount": 5, "isVariant": false, "parentPrefab": null}
```

### `mcpforunity://prefab/{encoded_path}/hierarchy`
```json
{"prefabPath": "Assets/Prefabs/Player.prefab", "total": 6, "items": [{"name": "Player", "instanceId": 12345, "path": "/Player", "activeSelf": true, "childCount": 2, "componentTypes": ["Transform", "PlayerController"]}, {"name": "Model", "path": "/Player/Model", "isNestedPrefab": true, "nestedPrefabPath": "Assets/Prefabs/PlayerModel.prefab"}]}
```

## Project

### `mcpforunity://project/info`
```json
{"projectRoot": "/Users/dev/MyProject", "projectName": "MyProject", "unityVersion": "2022.3.10f1", "platform": "StandaloneWindows64", "assetsPath": "/Users/dev/MyProject/Assets"}
```

### `mcpforunity://project/tags`
```json
["Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController", "Enemy"]
```

### `mcpforunity://project/layers`
```json
{"0": "Default", "1": "TransparentFX", "2": "Ignore Raycast", "4": "Water", "5": "UI", "8": "Player", "9": "Enemy"}
```

### `mcpforunity://menu-items`
```json
["File/New Scene", "File/Open Scene", "File/Save", "Edit/Undo", "Edit/Redo", "GameObject/Create Empty", "GameObject/3D Object/Cube", "Window/General/Console"]
```

### `mcpforunity://custom-tools`
```json
{"project_id": "MyProject", "tool_count": 3, "tools": [{"name": "capture_screenshot", "description": "Capture screenshots in Unity", "parameters": [{"name": "filename", "type": "string", "required": true}, {"name": "width", "type": "int", "required": false}, {"name": "height", "type": "int", "required": false}]}]}
```

## Instances

### `mcpforunity://instances`
```json
{"transport": "http", "instance_count": 2, "instances": [{"id": "MyProject@abc123", "name": "MyProject", "hash": "abc123", "unity_version": "2022.3.10f1", "connected_at": "2024-01-15T10:30:00Z"}, {"id": "TestProject@def456", "name": "TestProject", "hash": "def456", "unity_version": "2022.3.10f1", "connected_at": "2024-01-15T11:00:00Z"}], "warnings": []}
```

Use with:

```python
set_active_instance(instance="MyProject@abc123")
```

## Tests

### `mcpforunity://tests`
```json
[{"name": "TestSomething", "full_name": "MyTests.TestSomething", "mode": "EditMode"}, {"name": "TestOther", "full_name": "MyTests.TestOther", "mode": "PlayMode"}]
```

### `mcpforunity://tests/{mode}`
Parameters:

| Parameter | Type | Required | Notes |
| --- | --- | --- | --- |
| `mode` | `string` | yes | `EditMode` or `PlayMode` |

Example: `mcpforunity://tests/EditMode`
