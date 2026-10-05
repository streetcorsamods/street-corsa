"""
Readies the garage's radio for PropBuilder, out of its Sketchfab download:

  "1950 Bush DAC10 Vintage Radio" by Fishboe (https://sketchfab.com/ministephen), CC BY 4.0
  https://sketchfab.com/3d-models/1c293fc092734e0190b0ffc6072b5014

  mesh        the radio object of source/bushy2.blend, through Blender, as an OBJ (Y up, triangles, UVs, normals); the
              table and the rest of the author's scene stay behind
  diffuse     the base colour, 1024 square, JPEG
  normal      the normal map (DirectX, as AC's), 1024 square
  maps        AC's ksPerPixelMultiMap maps out of the PBR ones: red the specular, green the gloss (both from how smooth
              it is), blue the reflection (the metal, and a little of any smooth surface)

  python prepare_radio.py <the unzipped download> <out folder> [--blender <blender.exe>]

Needs Blender (--blender <blender.exe>, the BLENDER variable, else the newest in Program Files), numpy and Pillow.
"""
import os
import subprocess
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
from scene_tools import blender, without_blender_option  # noqa: E402

SIZE = 1024

EXPORT = r"""
import bpy, sys
out = sys.argv[sys.argv.index("--") + 1]
radio = bpy.data.objects["radio"]
for o in bpy.data.objects:
    o.select_set(False)
radio.select_set(True)
bpy.context.view_layer.objects.active = radio
bpy.ops.wm.obj_export(filepath=out, export_selected_objects=True, export_triangulated_mesh=True, export_materials=False,
                      export_uv=True, export_normals=True, apply_modifiers=True, forward_axis='NEGATIVE_Z', up_axis='Y')
"""


def texture(src, name, mode):
    image = Image.open(os.path.join(src, "textures", name)).convert(mode)
    return image.resize((SIZE, SIZE), Image.LANCZOS)


def main(src, out):
    os.makedirs(out, exist_ok=True)

    script = os.path.join(out, "export_radio.py")
    with open(script, "w", encoding="utf-8") as fh:
        fh.write(EXPORT)
    subprocess.run([blender(), "-b", os.path.join(src, "source", "bushy2.blend"), "--python", script, "--",
                    os.path.join(out, "radio.obj")], check=True, stdout=subprocess.DEVNULL)
    os.remove(script)

    texture(src, "Radio_Base_Color.png", "RGB").save(os.path.join(out, "radio_diffuse.jpg"), quality=90)
    texture(src, "Radio_Normal.png", "RGB").save(os.path.join(out, "radio_normal.png"))

    smooth = 1.0 - np.asarray(texture(src, "Radio_Roughness.png", "L"), np.float32) / 255
    metal = np.asarray(texture(src, "Radio_Metallic.png", "L"), np.float32) / 255
    maps = np.stack([
        smooth ** 1.5,                              # specular: the worn, rough patches do not shine
        smooth,                                     # gloss: a tight highlight on the polished Bakelite
        np.clip(metal * 0.8 + smooth * 0.15, 0, 1)  # reflection: a hint on the glossy case, more on any metal
    ], axis=-1)
    Image.fromarray((maps * 255).astype(np.uint8), "RGB").save(os.path.join(out, "radio_maps.png"))
    print("ready in", out)


if __name__ == "__main__":
    args = without_blender_option(sys.argv)
    if len(args) != 3:
        sys.exit(__doc__)
    main(args[1], args[2])
