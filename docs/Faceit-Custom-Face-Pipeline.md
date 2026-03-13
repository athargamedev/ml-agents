# Faceit Custom Face Pipeline

This note captures what we validated in Blender against the currently opened FBX characters and how that should connect to player-photo customization.

## What We Verified

- `Faceit` is installed and active in Blender as `bl_ext.user_default.faceit`.
- The `Ch33` character can be registered in Faceit with:
  - `Ch33_Body`
  - `Ch33_Eyelashes`
- Faceit correctly infers `Armature.002` as the body armature for that character.
- A usable `faceit_main` region can be auto-derived from the mesh by:
  - reading head/neck/jaw/eye bone weights
  - filtering to the frontal head region
  - keeping only the largest connected component
- Faceit landmark initialization works when Blender is given a live `VIEW_3D` context override.
- Faceit warnings disappear once:
  - `faceit_main` is a single connected island
  - the body armature is placed in `REST` pose before landmarking/binding

## Important Product Constraint

Faceit helps with:

- face registration
- landmark placement
- facial rig creation
- binding
- shape-key baking
- ARKit/A2F/FBX retargeting

Faceit does **not** generate a personalized likeness from a player photo by itself.

That means the full solution should use two layers:

1. `Faceit` for the facial rig and animation standardization on the base FBX.
2. A separate photo-processing step for per-player identity.

## Recommended Automation Split

### 1. One-time character prep in Blender

Run [Tools/blender/faceit_auto_prep.py](../Tools/blender/faceit_auto_prep.py) inside Blender for each imported base character.

What it automates:

- registers the body mesh and face-adjacent meshes with Faceit
- infers the existing armature
- builds a clean `faceit_main` group
- switches the armature to `REST` pose unless told not to
- initializes Faceit landmarks when Blender has a `VIEW_3D` area

Validated example:

```bash
blender your_scene.blend --python Tools/blender/faceit_auto_prep.py -- \
  --face-object Ch33_Body \
  --extra-object Ch33_Eyelashes \
  --armature Armature.002
```

After that, the remaining Faceit steps are:

1. align landmarks to the face
2. project landmarks
3. bind the facial rig
4. generate shape keys
5. export the prepared FBX

### 2. Per-player photo identity generation

Use the player photo to generate the face appearance layer, not the whole rig.

Recommended pipeline:

1. Detect face landmarks in the player photo.
2. Normalize the portrait into a neutral frontal crop.
3. Segment skin, brows, eyes, nose, and lips.
4. Warp the portrait into the character's face UV region.
5. Blend that patch onto the base atlas with feathering and color correction.
6. Assign the baked material/texture to the player character in Unity.

This aligns with the existing Unity bake workflow in:

- [FaceMapperEditorTool.cs](../DevProject/Assets/Network_Game/ThirdPersonController/Editor/FaceMapperEditorTool.cs)
- [PlayerFaceMapper.cs](../DevProject/Assets/Network_Game/ThirdPersonController/Scripts/PlayerFaceMapper.cs)

## Best End-to-End Direction

For your current project, the strongest approach is:

- use Faceit once per base character to produce a reusable facial rig and shape-key-ready FBX
- keep photo-driven customization as a texture-generation pipeline
- optionally add a second pass later that estimates a few personalized facial proportions from the photo and drives corrective shape keys or blendshape presets

That gives you:

- a stable animation system from Faceit
- fast runtime character personalization from player photos
- no need to solve full 3D identity reconstruction before shipping
