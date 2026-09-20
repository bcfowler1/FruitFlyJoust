# Fly Tack Blender Files

`Fly Saddle.blend` and `Fly Armor.blend` are independent, editable meshes exported from the currently saved Fruit Fly Joust tack fit.

- Each `.blend` contains exactly one mesh object at the world origin.
- Horse geometry and armatures have been removed.
- Rotation and non-uniform fitting scale are applied to the mesh.
- Unity fit offset, rotation, size ratio, source file, and included source meshes are stored as custom properties on the object.
- Matching `.obj` and `.mtl` files are included for interchange.

Edit the mesh in Blender and keep its object name and origin. Export to FBX with **Apply Transform** enabled when it is ready to return to Unity.

Run `Tools/export_fly_tack_blender.py` through Blender to regenerate both files from `Research/fly-tack-fit.json`.
