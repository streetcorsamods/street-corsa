"""
Pictures of part models put together as the garage would place them, to check their places and sizes without the game:
Blender's Workbench engine renders OBJs (RunningGearParts obj <model.kn5> <out.obj> dumps a KN5's triangles) placed by a
spec, from cameras the spec gives. Every placement of step 31's running gear (the axle with its drums, leaves, shocks and
bars, the coil spring against a tyre) was checked this way before the owner looked in game.

The spec, in the game's space (metres, Y up, the car's front towards -Z, +X its left):

  { "size": [800, 500],
    "items": [ { "obj": "C:/tmp/gm_12bolt.obj", "at": [0, 0, 0], "yaw": 0, "scale": [1, 1, 1], "colour": 0, "alpha": 1 }, ... ],
    "views": [ { "eye": [1.9, 0.9, 2.2], "target": [0, -0.05, 0], "lens": 40 }, ... ] }

"yaw" turns an item about the vertical (180 for a right-side wheel part), "colour" picks one of eight flat colours,
"alpha" under 1 draws it see-through (a stand-in tyre). Where the garage puts things: CarPartsLayout (a rear hub about
0.76 m out; a brake 0.06 m inboard of the hub, a drum 0.08 m out of that; a spring seat 0.24 m in and 0.06 m under it).

  python render_preview.py <spec.json> <out prefix> [--blender <blender.exe>]      writes <out prefix>_<view>.png
"""
import os
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
from scene_tools import blender, without_blender_option  # noqa: E402

RENDER = r"""
import bpy, json, math, sys
from mathutils import Vector, Matrix
args = sys.argv[sys.argv.index("--") + 1:]
spec = json.load(open(args[0])); out = args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
palette = [(0.55, 0.55, 0.6), (0.8, 0.25, 0.2), (0.3, 0.55, 0.85), (0.35, 0.65, 0.35), (0.85, 0.7, 0.3), (0.6, 0.4, 0.7), (0.9, 0.9, 0.9), (0.18, 0.18, 0.2)]
for item in spec['items']:
    bpy.ops.wm.obj_import(filepath=item['obj'], forward_axis='NEGATIVE_Z', up_axis='Y')
    for o in bpy.context.selected_objects:
        # The game's (x, y, z) is Blender's (x, -z, y): a yaw about the game's Y is about Blender's Z
        a = item.get('at', [0, 0, 0]); sc = item.get('scale', [1, 1, 1])
        o.matrix_world = Matrix.Translation(Vector((a[0], -a[2], a[1]))) @ Matrix.Rotation(math.radians(item.get('yaw', 0)), 4, 'Z') \
            @ Matrix.Diagonal((sc[0], sc[2], sc[1], 1)) @ o.matrix_world
        mat = bpy.data.materials.new("m"); c = palette[item.get('colour', 0) % len(palette)]
        mat.diffuse_color = (*c, item.get('alpha', 1))
        mat.blend_method = 'BLEND' if item.get('alpha', 1) < 1 else 'OPAQUE'
        o.data.materials.clear(); o.data.materials.append(mat)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.light = 'STUDIO'
scene.display.shading.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = spec.get('size', [800, 520])
scene.world = bpy.data.worlds.new("w"); scene.world.color = (1, 1, 1)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
for n, view in enumerate(spec['views']):
    eye, target = view['eye'], view['target']
    e = Vector((eye[0], -eye[2], eye[1])); t = Vector((target[0], -target[2], target[1]))
    cam.location = e
    cam.rotation_euler = (t - e).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = view.get('lens', 50)
    scene.render.filepath = f"{out}_{n}.png"
    bpy.ops.render.render(write_still=True)
"""


def main(spec, out):
    with tempfile.TemporaryDirectory(prefix="sc_preview_") as work:
        script = os.path.join(work, "_render.py")
        with open(script, "w", encoding="utf-8") as fh:
            fh.write(RENDER)
        subprocess.run([blender(), "-b", "--python", script, "--", os.path.abspath(spec), os.path.abspath(out)], check=True, stdout=subprocess.DEVNULL)


if __name__ == "__main__":
    args = without_blender_option(sys.argv)
    if len(args) != 3:
        sys.exit(__doc__)
    main(args[1], args[2])
