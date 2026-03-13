"""Create a dedicated head material slot on a skinned FBX character mesh.

This keeps the mesh and rig intact while splitting the renderable head faces
into their own material slot, which is what Unity needs for head-only material
swaps. The default workflow is:

1. Open the imported character scene in Blender.
2. Run this script to assign the dominant head island to a new material slot.
3. Optionally export the same armature + meshes back to the Unity FBX path.

Example:
    blender scene.blend --python Tools/blender/faceit_head_material_split.py -- \
        --mesh-object Ch33_Body \
        --armature Armature.002 \
        --output-fbx D:/GithubRepos/ml-agents/DevProject/Assets/Network_Game/ThirdPersonController/Character/Models/Ch33_nonPBR.fbx \
        --backup-fbx D:/GithubRepos/ml-agents/DevProject/Assets/Network_Game/ThirdPersonController/Character/Models/Ch33_nonPBR.pre_head_split_backup.fbx
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
HEAD_MATERIAL_NAME = "Ch33_head"


def log(message: str) -> None:
    print(f"[faceit_head_material_split] {message}")


def fail(message: str) -> None:
    raise RuntimeError(message)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mesh-object", help="Primary skinned body mesh, such as Ch33_Body.")
    parser.add_argument("--armature", help="Armature object to export with the mesh.")
    parser.add_argument("--output-fbx", help="Optional FBX path to overwrite/export.")
    parser.add_argument("--backup-fbx", help="Optional backup path written before export.")
    parser.add_argument(
        "--head-material",
        default=HEAD_MATERIAL_NAME,
        help="Name of the dedicated head material slot to create.",
    )
    parser.add_argument(
        "--head-group",
        help="Explicit head vertex group. Defaults to the first group containing 'head'.",
    )
    parser.add_argument(
        "--threshold",
        type=float,
        default=0.5,
        help="Minimum vertex weight required to count as part of the head region.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Analyze and print the target region without modifying the mesh.",
    )
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
    fail("Could not infer the body mesh. Pass --mesh-object explicitly.")
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
    candidates = [group for group in candidates if "faceit_main" not in group.name.lower()]
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


def build_candidate_faces(
    mesh_object: bpy.types.Object,
    weight_map: dict[int, float],
    threshold: float,
) -> list[int]:
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
        fail("No polygons qualified for the head material split.")

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


def ensure_head_material_slot(mesh_object: bpy.types.Object, material_name: str) -> tuple[int, int]:
    base_slot_index = 0
    if len(mesh_object.material_slots) == 0:
        fail(f"'{mesh_object.name}' has no base material slot to duplicate.")

    head_slot_index = None
    for index, slot in enumerate(mesh_object.material_slots):
        material = slot.material
        if material and material.name == material_name:
            head_slot_index = index
            break

    if head_slot_index is not None:
        return base_slot_index, head_slot_index

    base_material = mesh_object.material_slots[base_slot_index].material
    if base_material is None:
        fail(f"'{mesh_object.name}' base material slot is empty.")

    material = bpy.data.materials.get(material_name)
    if material is None:
        material = base_material.copy()
        material.name = material_name

    mesh_object.data.materials.append(material)
    return base_slot_index, len(mesh_object.material_slots) - 1


def assign_head_material(mesh_object: bpy.types.Object, head_faces: list[int], material_name: str) -> dict[str, int]:
    base_slot_index, head_slot_index = ensure_head_material_slot(mesh_object, material_name)
    head_face_set = set(head_faces)

    reassigned = 0
    for polygon in mesh_object.data.polygons:
        if polygon.material_index == head_slot_index:
            polygon.material_index = base_slot_index
        if polygon.index in head_face_set:
            polygon.material_index = head_slot_index
            reassigned += 1

    mesh_object.data.update()
    return {
        "base_slot_index": base_slot_index,
        "head_slot_index": head_slot_index,
        "head_face_count": reassigned,
        "material_slot_count": len(mesh_object.material_slots),
    }


def select_export_objects(mesh_object: bpy.types.Object, armature: bpy.types.Object | None) -> list[str]:
    selected = []
    bpy.ops.object.select_all(action="DESELECT")

    if armature is not None:
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

    weight_map = get_weight_map(mesh_object, head_group.index)
    candidate_faces = build_candidate_faces(mesh_object, weight_map, args.threshold)
    head_faces = extract_largest_face_component(mesh_object, candidate_faces)

    summary = {
        "mesh_object": mesh_object.name,
        "armature": armature.name if armature else None,
        "head_group": head_group.name,
        "candidate_face_count": len(candidate_faces),
        "largest_component_face_count": len(head_faces),
        "head_material": args.head_material,
    }

    if not args.dry_run:
        summary.update(assign_head_material(mesh_object, head_faces, args.head_material))
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
