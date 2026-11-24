"""
Run in Blender to generate a single animated FBX with a 0 -> 100 Drink Shape Key:
  blender --background --python build_coffee_fbx.py
"""
import os
import bpy

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.dirname(SCRIPT_DIR)
MUG_OBJ = os.path.join(ROOT_DIR, "Meshes", "FlutedGlassMug.obj")
COFFEE_OBJ = os.path.join(ROOT_DIR, "Meshes", "CoffeeLiquid.obj")
OUT_FBX = os.path.join(ROOT_DIR, "FlutedCoffeeMug_Animated.fbx")

bpy.ops.wm.read_factory_settings(use_empty=True)

# Import Mug
bpy.ops.wm.obj_import(filepath=MUG_OBJ)
mug_objs = list(bpy.context.selected_objects)

# Import Coffee Liquid
bpy.ops.wm.obj_import(filepath=COFFEE_OBJ)
coffee_objs = [o for o in bpy.context.selected_objects if o not in mug_objs]

# Position coffee inside mug floor (y=0.010m -> Z in Blender)
for obj in coffee_objs:
    obj.location.z = 0.010
    # Add Shape Key for 0 -> 100 drink animation
    obj.shape_key_add(name="Basis")
    sk = obj.shape_key_add(name="Drink_0_to_100")
    for v in sk.data:
        v.co.z = 0.0005  # Collapse liquid height to bottom

    # Keyframe animation from frame 1 (0%) to frame 100 (100% drunk)
    sk.value = 0.0
    sk.keyframe_insert(data_path="value", frame=1)
    sk.value = 1.0
    sk.keyframe_insert(data_path="value", frame=100)

bpy.context.scene.frame_start = 1
bpy.context.scene.frame_end = 100

# Export Unity-compatible FBX
bpy.ops.export_scene.fbx(
    filepath=OUT_FBX,
    use_selection=False,
    apply_scale_options='FBX_SCALE_UNITS',
    bake_anim=True,
    bake_anim_use_all_actions=False,
    add_leaf_bones=False,
)
print("Exported:", OUT_FBX)
