"""Split a skinned character mesh into body + head objects for Unity export.

Faceit is useful for preparing and validating the facial region, but the actual
\"separate head\" result that Unity needs is a Blender mesh operation. This
script isolates the dominant head polygon island from a skinned mesh, creates a
new head object that stays bound to the same armature, and can export the
updated character back to FBX.
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

import bmesh
import bpy

BODY_HINTS = ("body",)
HEAD_GROUP_TOKENS = ("head",)
HEAD_MATERIAL_TOKENS = ("head", "face")
BODY_MATERIAL_TOKENS = ("body",)


def log(message: str) -> None:
    print(f"[faceit_head_object_split] {message}")


def fail(message: str) -> None:
    raise RuntimeError(message)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mesh-object", help="Body mesh to split, such as model_T or Ch33_Body.")
    parser.add_argument("--armature", help="Armature driving the mesh.")
    parser.add_argument("--head-object-name", help="Name for the new head object.")
    parser.add_argument("--head-group", help="Explicit head vertex group name.")
    parser.add_argument("--threshold", type=float, default=0.5)
    parser.add_argument("--output-fbx", help="Optional export path.")
    parser.add_argument("--backup-fbx", help="Optional backup path written before export.")
    parser.add_argument("--dry-run", action="store_true")
    return parser.parse_args(argv)


def infer_mesh_object(explicit_name: str | None) -> bpy.types.Object:
    if explicit_name:
        obj = bpy.data.objects.get(explicit_name)
        if not obj or obj.type != "MESH":
            fail(f"Mesh object '{explicit_name}' was not found or is not a mesh.")
        return obj

    for obj in bpy.data.objects:
        if obj.type == "MESH" and any(token in obj.name.lower() for token in BODY_HINTS):
            return obj
    fail("Could not infer a skinned body mesh. Pass --mesh-object explicitly.")
    return None


def infer_armature(mesh_object: bpy.types.Object, explicit_name: str | None) -> bpy.types.Object | None:
    if explicit_name:
        armature = bpy.data.objects.get(explicit_name)
        if not armature or armature.type != "ARMATURE":
            fail(f"Armature '{explicit_name}' was not found or is not an armature.")
        return armature

    for modifier in mesh_object.modifiers:
        if modifier.type == "ARMATURE" and modifier.object:
            return modifier.object
    return None


def find_head_group(mesh_object: bpy.types.Object, explicit_name: str | None) -> bpy.types.VertexGroup:
    if explicit_name:
        group = mesh_object.vertex_groups.get(explicit_name)
        if group is None:
            fail(f"Vertex group '{explicit_name}' was not found on '{mesh_object.name}'.")
        return group

    candidates = [
        group
        for group in mesh_object.vertex_groups
        if any(token in group.name.lower() for token in HEAD_GROUP_TOKENS)
    ]
    candidates = [group for group in candidates if "faceit" not in group.name.lower()]
    if not candidates:
        fail(f"Could not infer a head vertex group on '{mesh_object.name}'.")
    candidates.sort(key=lambda group: group.name)
    return candidates[0]


def get_weight_map(mesh_object: bpy.types.Object, group_index: int) -> dict[int, float]:
    weights: dict[int, float] = {}
    for vertex in mesh_object.data.vertices:
        for membership in vertex.groups:
            if membership.group == group_index:
                weights[vertex.index] = membership.weight
                break
    return weights


def build_candidate_faces(mesh_object: bpy.types.Object, weight_map: dict[int, float], threshold: float) -> list[int]:
    candidate_vertices = {index for index, weight in weight_map.items() if weight >= threshold}
    if not candidate_vertices:
        fail("No vertices matched the requested head-weight threshold.")

    return [
        polygon.index
        for polygon in mesh_object.data.polygons
        if all(vertex_index in candidate_vertices for vertex_index in polygon.vertices)
    ]


def extract_largest_face_component(mesh_object: bpy.types.Object, candidate_faces: list[int]) -> list[int]:
    if not candidate_faces:
        fail("No polygons qualified for the head split.")

    bm = bmesh.new()
    try:
        bm.from_mesh(mesh_object.data)
        bm.faces.ensure_lookup_table()

        remaining = set(candidate_faces)
        largest: list[int] = []

        while remaining:
            seed = remaining.pop()
            stack = [seed]
            component = [seed]

            while stack:
                face_index = stack.pop()
                for edge in bm.faces[face_index].edges:
                    for linked_face in edge.link_faces:
                        linked_index = linked_face.index
                        if linked_index in remaining:
                            remaining.remove(linked_index)
                            stack.append(linked_index)
                            component.append(linked_index)

            if len(component) > len(largest):
                largest = component

        if not largest:
            fail("Failed to find a connected head polygon island.")
        return largest
    finally:
        bm.free()


def ensure_object_in_view_layer(obj: bpy.types.Object) -> None:
    if obj.name in {item.name for item in bpy.context.view_layer.objects}:
        return

    scene = bpy.context.scene
    if not obj.users_collection:
        scene.collection.objects.link(obj)
    bpy.context.view_layer.update()


def separate_head_object(mesh_object: bpy.types.Object, head_faces: list[int], head_object_name: str) -> dict[str, str | int]:
    ensure_object_in_view_layer(mesh_object)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    mesh_object.select_set(True)
    bpy.context.view_layer.objects.active = mesh_object

    for polygon in mesh_object.data.polygons:
        polygon.select = polygon.index in head_faces

    existing_mesh_names = {obj.name for obj in bpy.data.objects if obj.type == "MESH"}

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")

    new_objects = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH" and obj.name not in existing_mesh_names
    ]
    if len(new_objects) != 1:
        fail(f"Expected 1 new head object after separation, found {len(new_objects)}.")

    head_object = new_objects[0]
    head_object.name = head_object_name
    if head_object.data:
        head_object.data.name = head_object_name

    return {
        "body_object": mesh_object.name,
        "head_object": head_object.name,
        "head_polygon_count": len(head_object.data.polygons),
        "body_polygon_count": len(mesh_object.data.polygons),
    }


def count_polygons_by_material(obj: bpy.types.Object) -> dict[int, int]:
    counts: dict[int, int] = {}
    for polygon in obj.data.polygons:
        counts[polygon.material_index] = counts.get(polygon.material_index, 0) + 1
    return counts


def choose_material_slot(
    obj: bpy.types.Object,
    preferred_tokens: tuple[str, ...],
) -> int:
    counts = count_polygons_by_material(obj)
    if not counts:
        return 0

    for index, slot in enumerate(obj.material_slots):
        material = slot.material
        if material and any(token in material.name.lower() for token in preferred_tokens):
            return index

    return max(counts.items(), key=lambda item: item[1])[0]


def collapse_object_to_single_material(
    obj: bpy.types.Object,
    preferred_tokens: tuple[str, ...],
) -> str | None:
    if obj is None or obj.type != "MESH":
        return None

    slot_index = choose_material_slot(obj, preferred_tokens)
    material = (
        obj.material_slots[slot_index].material
        if slot_index < len(obj.material_slots)
        else None
    )

    for polygon in obj.data.polygons:
        polygon.material_index = 0

    obj.data.materials.clear()
    if material is not None:
        obj.data.materials.append(material)

    return material.name if material else None


def normalize_split_materials(
    body_object: bpy.types.Object,
    head_object: bpy.types.Object,
) -> dict[str, str | None]:
    return {
        "body_material": collapse_object_to_single_material(
            body_object,
            BODY_MATERIAL_TOKENS,
        ),
        "head_material": collapse_object_to_single_material(
            head_object,
            HEAD_MATERIAL_TOKENS,
        ),
    }


def select_export_objects(mesh_object: bpy.types.Object, armature: bpy.types.Object | None) -> list[str]:
    selected = []
    bpy.ops.object.select_all(action="DESELECT")

    if armature is not None:
        ensure_object_in_view_layer(armature)
        armature.select_set(True)
        bpy.context.view_layer.objects.active = armature
        selected.append(armature.name)

    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        if armature is not None:
            linked_armatures = {
                modifier.object.name
                for modifier in obj.modifiers
                if modifier.type == "ARMATURE" and modifier.object
            }
            if armature.name not in linked_armatures:
                continue
        elif obj.name != mesh_object.name:
            continue

        ensure_object_in_view_layer(obj)
        obj.select_set(True)
        selected.append(obj.name)

    if not selected:
        fail("Nothing was selected for FBX export.")
    return selected


def export_fbx(mesh_object: bpy.types.Object, armature: bpy.types.Object | None, output_path: str, backup_path: str | None) -> list[str]:
    output_file = Path(output_path)
    output_file.parent.mkdir(parents=True, exist_ok=True)

    if backup_path and output_file.exists():
        backup_file = Path(backup_path)
        if not backup_file.exists():
            backup_file.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(output_file, backup_file)
            log(f"Backed up FBX to {backup_file}")
        else:
            log(f"Backup already exists, leaving it untouched: {backup_file}")

    selected = select_export_objects(mesh_object, armature)
    bpy.ops.export_scene.fbx(
        filepath=str(output_file),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        use_active_collection=False,
        add_leaf_bones=False,
        bake_anim=False,
        use_armature_deform_only=True,
        use_mesh_modifiers=False,
        mesh_smooth_type="FACE",
        path_mode="AUTO",
        axis_forward="-Z",
        axis_up="Y",
    )
    return selected


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    mesh_object = infer_mesh_object(args.mesh_object)
    armature = infer_armature(mesh_object, args.armature)
    head_group = find_head_group(mesh_object, args.head_group)
    head_object_name = args.head_object_name or f"{mesh_object.name}_Head"

    weight_map = get_weight_map(mesh_object, head_group.index)
    candidate_faces = build_candidate_faces(mesh_object, weight_map, args.threshold)
    head_faces = extract_largest_face_component(mesh_object, candidate_faces)

    summary: dict[str, object] = {
        "mesh_object": mesh_object.name,
        "armature": armature.name if armature else None,
        "head_group": head_group.name,
        "candidate_face_count": len(candidate_faces),
        "largest_component_face_count": len(head_faces),
        "head_object_name": head_object_name,
    }

    if not args.dry_run:
        split_result = separate_head_object(mesh_object, head_faces, head_object_name)
        summary.update(split_result)
        head_object = bpy.data.objects.get(split_result["head_object"])
        summary.update(normalize_split_materials(mesh_object, head_object))
        if args.output_fbx:
            summary["exported_objects"] = export_fbx(
                mesh_object,
                armature,
                args.output_fbx,
                args.backup_fbx,
            )
            summary["output_fbx"] = args.output_fbx
            summary["backup_fbx"] = args.backup_fbx

    log(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    else:
        argv = []
    raise SystemExit(main(list(argv)))
