import bpy
import json
import math
import os
import sys
from mathutils import Vector


def arguments():
    values = sys.argv[sys.argv.index("--") + 1:]
    if len(values) != 3:
        raise RuntimeError("Expected: source.fbx fit.json output-directory")
    return [os.path.abspath(value) for value in values]


def reset_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.armatures, bpy.data.materials):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def selected_names(fit, kind):
    if kind == "saddle":
        choices = (("saddle", "Saddle"), ("saddleLow", "Saddle Low"),
                   ("reins", "Reins"), ("reinsHead", "Reins Head"))
    else:
        choices = (("armour", "Armour"),)
    return [name for key, name in choices if fit.get(key, False)]


def bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return low, high


def make_editable_object(source_fbx, fit, kind, display_name):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=source_fbx, use_anim=False)
    wanted = selected_names(fit, kind)
    objects = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH' and obj.name in wanted]
    if not objects:
        raise RuntimeError(f"No enabled {kind} meshes found. Expected one of: {wanted}")

    # Bake the imported rest pose into ordinary meshes so each file is independent
    # of the original horse armature and immediately editable in Blender.
    baked = []
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for source in objects:
        evaluated = source.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(evaluated, depsgraph=depsgraph)
        target = bpy.data.objects.new(source.name, mesh)
        target.matrix_world = source.matrix_world.copy()
        bpy.context.collection.objects.link(target)
        baked.append(target)

    bpy.ops.object.select_all(action='DESELECT')
    for obj in baked:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = baked[0]
    if len(baked) > 1:
        bpy.ops.object.join()
    result = bpy.context.view_layer.objects.active
    result.name = display_name
    result.data.name = display_name + " Mesh"

    # Remove the source horse, its armature, and every unused accessory. The
    # resulting Blender file must contain only the independent editable piece.
    for obj in list(bpy.context.scene.objects):
        if obj != result:
            bpy.data.objects.remove(obj, do_unlink=True)

    # Unity's Y-up rotation maps to Blender's Z-up frame after FBX import.
    rotation = fit[kind + "Rotation"]
    result.rotation_euler = (math.radians(rotation["x"]), math.radians(rotation["z"]), -math.radians(rotation["y"]))
    bpy.context.view_layer.objects.active = result
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Preserve the fitted proportions. Unity X/Y/Z corresponds to Blender X/Z/Y.
    desired = fit[kind + "Size"]
    target = Vector((desired["x"], desired["z"], desired["y"]))
    result.dimensions = target
    bpy.context.view_layer.update()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    low, high = bounds([result])
    result.location -= (low + high) * 0.5
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    result["fruit_fly_joust_source"] = os.path.basename(source_fbx)
    result["unity_fit_offset"] = json.dumps(fit[kind + "Offset"])
    result["unity_fit_rotation_degrees"] = json.dumps(fit[kind + "Rotation"])
    result["unity_thorax_size_ratio"] = json.dumps(fit[kind + "Size"])
    result["included_source_meshes"] = ", ".join(wanted)
    return result


def save_piece(source_fbx, fit, output_dir, kind, display_name):
    result = make_editable_object(source_fbx, fit, kind, display_name)
    os.makedirs(output_dir, exist_ok=True)
    blend_path = os.path.join(output_dir, display_name + ".blend")
    obj_path = os.path.join(output_dir, display_name + ".obj")
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    backup_path = blend_path + "1"
    if os.path.exists(backup_path):
        os.remove(backup_path)
    bpy.ops.object.select_all(action='DESELECT')
    result.select_set(True)
    bpy.context.view_layer.objects.active = result
    bpy.ops.wm.obj_export(filepath=obj_path, export_selected_objects=True, export_materials=True,
                          export_uv=True, export_normals=True, apply_modifiers=True)
    print(f"EXPORTED {display_name}: {blend_path}")


source_fbx, fit_path, output_dir = arguments()
with open(fit_path, "r", encoding="utf-8") as stream:
    fit = json.load(stream)
save_piece(source_fbx, fit, output_dir, "saddle", "Fly Saddle")
save_piece(source_fbx, fit, output_dir, "armour", "Fly Armor")
