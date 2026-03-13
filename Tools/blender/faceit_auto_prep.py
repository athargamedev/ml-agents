"""Bootstrap Faceit setup for imported humanoid FBX characters in Blender.

This script is intended to be run inside Blender with the Faceit addon enabled.
It was validated against the currently opened Ch33 character setup in this repo.

Example:
    blender your_scene.blend --python Tools/blender/faceit_auto_prep.py -- \\
        --face-object Ch33_Body --extra-object Ch33_Eyelashes --armature Armature.002

Notes:
- Faceit landmark generation requires a live Blender UI with a VIEW_3D area.
- This script prepares Faceit registration and a clean `faceit_main` group.
- Faceit helps with facial rigging, binding, and retargeting. It does not turn
  player photos into likeness textures by itself.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from typing import Iterable, Sequence

import bmesh
import bpy
from mathutils import Vector

FACEIT_ADDON_KEY = "bl_ext.user_default.faceit"
FACE_TOKENS = ("head", "neck", "jaw", "eye", "face")
FACE_OBJECT_NAME_HINTS = ("head", "face")
BODY_NAME_HINTS = ("body",)
EXTRA_NAME_HINTS = ("eyelash", "lash", "eyebrow", "brow", "beard", "mustache", "facial")


@dataclass
class PrepResult:
    face_object: str
    extra_objects: list[str]
    armature: str | None
    head_bone: str | None
    faceit_main_vertices: int
    faceit_main_component_sizes: list[int]
    landmarks_initialized: bool
    warnings: list[dict[str, str]]


def log(message: str) -> None:
    print(f"[faceit_auto_prep] {message}")


def fail(message: str) -> None:
    raise RuntimeError(message)


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--face-object", help="Primary face mesh, preferably the dedicated head mesh.")
    parser.add_argument(
        "--extra-object",
        action="append",
        default=[],
        help="Additional face-related meshes to register, such as eyelashes.",
    )
    parser.add_argument("--armature", help="Existing armature object to associate with Faceit.")
    parser.add_argument(
        "--append",
        action="store_true",
        help="Keep existing Faceit registrations instead of clearing them first.",
    )
    parser.add_argument(
        "--skip-landmarks",
        action="store_true",
        help="Skip Faceit landmark initialization.",
    )
    parser.add_argument(
        "--keep-pose-position",
        action="store_true",
        help="Do not switch the body armature to REST pose during setup.",
    )
    return parser.parse_args(list(argv))


def get_faceit_addon_key() -> str:
    addons = bpy.context.preferences.addons
    for key in addons.keys():
        if "faceit" in key.lower():
            return key
    fail("Faceit addon is not enabled in this Blender session.")
    return ""


def infer_face_object(explicit_name: str | None) -> bpy.types.Object:
    if explicit_name:
        obj = bpy.data.objects.get(explicit_name)
        if not obj or obj.type != "MESH":
            fail(f"Face object '{explicit_name}' was not found or is not a mesh.")
        return obj

    head_candidates = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH"
        and any(hint in obj.name.lower() for hint in FACE_OBJECT_NAME_HINTS)
    ]
    if head_candidates:
        head_candidates.sort(key=lambda obj: obj.name)
        return head_candidates[0]

    candidates = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH" and any(hint in obj.name.lower() for hint in BODY_NAME_HINTS)
    ]
    if not candidates:
        fail("Could not infer a face object. Pass --face-object explicitly.")
    candidates.sort(key=lambda obj: obj.name)
    return candidates[0]


def infer_armature(face_object: bpy.types.Object, explicit_name: str | None) -> bpy.types.Object | None:
    if explicit_name:
        armature = bpy.data.objects.get(explicit_name)
        if not armature or armature.type != "ARMATURE":
            fail(f"Armature '{explicit_name}' was not found or is not an armature.")
        return armature

    for modifier in face_object.modifiers:
        if modifier.type == "ARMATURE" and modifier.object:
            return modifier.object
    return None


def infer_extra_objects(
    face_object: bpy.types.Object,
    armature: bpy.types.Object | None,
    explicit_names: Sequence[str],
) -> list[bpy.types.Object]:
    if explicit_names:
        result = []
        for name in explicit_names:
            obj = bpy.data.objects.get(name)
            if not obj or obj.type != "MESH":
                fail(f"Extra object '{name}' was not found or is not a mesh.")
            result.append(obj)
        return result

    result = []
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.name == face_object.name:
            continue
        if not any(hint in obj.name.lower() for hint in EXTRA_NAME_HINTS):
            continue
        if armature is not None:
            armature_targets = {
                modifier.object.name
                for modifier in obj.modifiers
                if modifier.type == "ARMATURE" and modifier.object
            }
            if armature.name not in armature_targets:
                continue
        result.append(obj)
    result.sort(key=lambda obj: obj.name)
    return result


def find_view3d_override() -> dict[str, bpy.types.bpy_struct] | None:
    window = bpy.context.window
    if not window or not window.screen:
        return None

    for area in window.screen.areas:
        if area.type != "VIEW_3D":
            continue
        for region in area.regions:
            if region.type == "WINDOW":
                return {"window": window, "area": area, "region": region}
    return None


def set_active_object(obj: bpy.types.Object) -> None:
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def clear_faceit_state() -> None:
    scene = bpy.context.scene
    if scene.faceit_face_objects:
        scene.faceit_face_index = 0
        scene.faceit_face_objects.clear()
    scene.faceit_body_armature = None
    scene.faceit_armature = None
    scene.faceit_shapes_generated = False


def register_faceit_objects(objects: Iterable[bpy.types.Object]) -> None:
    for obj in objects:
        set_active_object(obj)
        if not bpy.ops.faceit.add_facial_part.poll():
            fail(f"Faceit could not register '{obj.name}' in the current context.")
        bpy.ops.faceit.add_facial_part(facial_part="main")


def compute_face_candidate_vertices(obj: bpy.types.Object) -> set[int]:
    scores: dict[int, float] = {}
    for vertex in obj.data.vertices:
        score = 0.0
        for group in vertex.groups:
            name = obj.vertex_groups[group.group].name.lower()
            if any(token in name for token in FACE_TOKENS):
                score += group.weight
        if score > 0.0:
            scores[vertex.index] = score

    if not scores:
        fail(f"No face-related bone weights were found on '{obj.name}'.")

    coords = [obj.data.vertices[index].co for index in scores]
    min_x = min(co.x for co in coords)
    max_x = max(co.x for co in coords)
    min_y = min(co.y for co in coords)
    max_y = max(co.y for co in coords)
    min_z = min(co.z for co in coords)
    max_z = max(co.z for co in coords)
    center_x = (min_x + max_x) * 0.5
    span_x = max(max_x - min_x, 1e-6)
    span_y = max(max_y - min_y, 1e-6)
    span_z = max(max_z - min_z, 1e-6)

    candidate = set()
    for index, score in scores.items():
        co = obj.data.vertices[index].co
        if score < 0.5:
            continue
        if abs(co.x - center_x) > span_x * 0.7:
            continue
        if co.y < min_y + span_y * 0.05:
            continue
        if co.z < min_z + span_z * 0.05:
            continue
        candidate.add(index)

    if not candidate:
        fail(f"Could not derive a Faceit face region on '{obj.name}'.")
    return candidate


def connected_components(obj: bpy.types.Object, vertex_indices: set[int]) -> list[list[int]]:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()

    remaining = set(vertex_indices)
    components: list[list[int]] = []

    while remaining:
        seed = remaining.pop()
        stack = [seed]
        component = [seed]

        while stack:
            index = stack.pop()
            vert = bm.verts[index]
            for edge in vert.link_edges:
                other_index = edge.other_vert(vert).index
                if other_index in remaining:
                    remaining.remove(other_index)
                    stack.append(other_index)
                    component.append(other_index)

        components.append(component)

    bm.free()
    components.sort(key=len, reverse=True)
    return components


def rebuild_faceit_main_group(obj: bpy.types.Object) -> tuple[int, list[int]]:
    candidate = compute_face_candidate_vertices(obj)
    components = connected_components(obj, candidate)
    if not components:
        fail(f"Could not isolate a connected Faceit main region on '{obj.name}'.")

    old_group = obj.vertex_groups.get("faceit_main")
    if old_group:
        obj.vertex_groups.remove(old_group)

    faceit_main = obj.vertex_groups.new(name="faceit_main")
    faceit_main.add(components[0], 1.0, "REPLACE")
    return len(components[0]), [len(component) for component in components]


def set_body_armature(scene: bpy.types.Scene, armature: bpy.types.Object | None, keep_pose_position: bool) -> None:
    if armature is None:
        return
    scene.faceit_body_armature = armature
    if not keep_pose_position:
        armature.data.pose_position = "REST"


def initialize_landmarks() -> bool:
    override = find_view3d_override()
    if not override:
        log("Skipping landmark initialization because no VIEW_3D area is available.")
        return False

    if not bpy.ops.faceit.facial_landmarks.poll():
        log("Skipping landmark initialization because Faceit is not ready for landmarks yet.")
        return False

    with bpy.context.temp_override(**override):
        bpy.ops.faceit.facial_landmarks("EXEC_DEFAULT")
    return bpy.data.objects.get("facial_landmarks") is not None


def collect_warnings() -> list[dict[str, str]]:
    override = find_view3d_override()
    if override and hasattr(bpy.ops.faceit, "face_object_warning_check"):
        with bpy.context.temp_override(**override):
            bpy.ops.faceit.face_object_warning_check(
                item_name="ALL",
                set_show_warnings=False,
                check_main=True,
            )

    result = []
    for item in bpy.context.scene.faceit_face_objects:
        result.append({"name": item.name, "warnings": item.warnings})
    return result


def main(argv: Sequence[str]) -> int:
    get_faceit_addon_key()
    args = parse_args(argv)
    scene = bpy.context.scene

    face_object = infer_face_object(args.face_object)
    armature = infer_armature(face_object, args.armature)
    extra_objects = infer_extra_objects(face_object, armature, args.extra_object)

    if not args.append:
        clear_faceit_state()

    register_faceit_objects([face_object, *extra_objects])
    set_body_armature(scene, armature, keep_pose_position=args.keep_pose_position)
    faceit_main_vertices, component_sizes = rebuild_faceit_main_group(face_object)
    landmarks_initialized = False if args.skip_landmarks else initialize_landmarks()
    warnings = collect_warnings()

    result = PrepResult(
        face_object=face_object.name,
        extra_objects=[obj.name for obj in extra_objects],
        armature=armature.name if armature else None,
        head_bone=scene.faceit_body_armature_head_bone or None,
        faceit_main_vertices=faceit_main_vertices,
        faceit_main_component_sizes=component_sizes,
        landmarks_initialized=landmarks_initialized,
        warnings=warnings,
    )
    log(json.dumps(result.__dict__, indent=2))
    return 0


if __name__ == "__main__":
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = []
    sys.exit(main(argv))
