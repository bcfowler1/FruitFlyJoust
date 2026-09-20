import bpy
import os
import sys


def arguments():
    values = sys.argv[sys.argv.index("--") + 1:]
    if len(values) != 1:
        raise RuntimeError("Expected one output FBX path")
    return os.path.abspath(values[0])


output_path = arguments()
meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and not obj.hide_get()]
if not meshes:
    raise RuntimeError("The Blender file contains no visible mesh to export")

bpy.context.view_layer.objects.active = meshes[0]
if meshes[0].mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")
for obj in bpy.context.view_layer.objects:
    obj.select_set(obj in meshes)
os.makedirs(os.path.dirname(output_path), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=output_path,
    use_selection=True,
    use_active_collection=False,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_anim=False,
    add_leaf_bones=False,
    path_mode="AUTO",
)
print("FLY_TACK_FBX_EXPORTED: " + output_path)
