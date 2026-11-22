"""Build every character size in its own clean scene and export it as FBX for Unity.
Usage: python3 export.py <out_dir> [species ...]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, cf
import yeti, skeleton, chihuahua, sharc, slime

SPECIES = {"Yeti": yeti, "Skeleton": skeleton, "Chihuahua": chihuahua, "Sharc": sharc, "Slime": slime}

def export(out_dir, names):
    os.makedirs(out_dir, exist_ok=True)
    for sp in names:
        mod = SPECIES[sp]
        for sz in mod.SIZES:
            cf.reset_scene()
            root = mod.build(sz, name=f"{sp}_{sz}")
            path = os.path.join(out_dir, f"{sp}_{sz}.fbx")
            bpy.ops.export_scene.fbx(filepath=path, use_selection=False, object_types={"EMPTY", "MESH"},
                                     apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                     axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                                     use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                                     bake_anim=False, path_mode="AUTO")
            print("exported", path, os.path.getsize(path))

if __name__ == "__main__":
    # works both as plain python with the bpy module and inside Blender: blender -b -P export.py -- <out_dir> [species]
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    out = args[0] if args else "out/fbx"
    export(out, args[1:] or list(SPECIES))
