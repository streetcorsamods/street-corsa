"""
Readies any prop for PropBuilder, out of a model with PBR materials (a Sketchfab .glb, or a .blend): Blender exports
its meshes as one OBJ with a material to each triangle, and each material becomes three pictures for AC's
ksPerPixelMultiMap plus a line in materials.json:

  diffuse     its base colour picture (1024 at most), or a swatch of its flat colour; a PNG with its alpha when the
              alpha cuts its shape out (a road marking: alphaTest)
  normal      its normal map, green turned over when it is OpenGL's (AC's is DirectX's); none when it has none
  maps        red the specular and green the gloss (from how smooth it is), blue the reflection (the metal, and a
              little of any smooth surface): from its roughness and metallic pictures (the channel a split takes, as
              a glTF's packed occlusion-roughness-metal picture), or its flat values
  emissive    a material that glows in the model (a lit dial) glows in AC too
  glass       a see-through material (alpha at most GLASS_ALPHA: a dial's cover, or blended with no picture to cut it
              by: a window) is left out, so what is behind it shows

A picture reached through a multiply by the vertex colours (as Blender brings a glTF's in) is the picture.

Materials that come out the same are one. The pictures are taken out of the model as they are (a .glb carries them).

  python prepare_prop.py <model.glb|.blend> <name> <out folder> [largest picture, pixels: 1024] [--jpg] [--keep-names]

Needs Blender (found in Program Files), numpy and Pillow.
"""
import glob
import io
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image

SIZE = 1024

# The normal maps and the maps: PNGs, or JPEGs for a big scene (--jpg), a tenth of the size
MAPS, SAVE = ".png", {}

# Each material named <name>_<n>, or as the model names it (--keep-names): a scene's file names its materials to make
# them glow at night
KEEP_NAMES = False


def own_name(material, taken):
    """The model's name for a material, letters, digits and _ only, and not one taken already"""
    clean = "".join(c if c.isalnum() or c == "_" else "_" for c in material) or "material"
    name, n = clean, 1
    while name in taken:
        n += 1
        name = f"{clean}_{n}"
    return name
SWATCH = 8
GLASS_ALPHA = 0.5

# Run inside Blender: the meshes out as an OBJ, and what each material is made of
EXPORT = r"""
import bpy, json, os, sys
args = sys.argv[sys.argv.index("--") + 1:]
source, out, name = args
if source.lower().endswith((".glb", ".gltf")):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=source)

def picture(s):
    # The picture behind a socket: through a normal map, a channel split, or a multiply by the vertex colours (a glTF's)
    if s is None or not s.links:
        return None
    n = s.links[0].from_node
    if n.type == 'TEX_IMAGE':
        return n if n.image else None
    if n.type == 'NORMAL_MAP':
        return picture(n.inputs.get('Color'))
    if n.type in ('SEPARATE_COLOR', 'SEPRGB'):
        return picture(n.inputs[0])
    if n.type in ('MIX', 'MIX_RGB', 'MATH'):
        for i in n.inputs:
            found = picture(i) if i.links else None
            if found:
                return found
    return None

def linked(node, socket):
    return picture(node.inputs.get(socket))

def channel(node, socket):
    # Which of its picture's channels feeds a socket through a channel split (0 red, 1 green, 2 blue); None: its brightness
    s = node.inputs.get(socket)
    if s is None or not s.links:
        return None
    link = s.links[0]
    if link.from_node.type in ('SEPARATE_COLOR', 'SEPRGB'):
        return {'Red': 0, 'R': 0, 'Green': 1, 'G': 1, 'Blue': 2, 'B': 2}.get(link.from_socket.name)
    return None

def save(image):
    ext = ".png" if (image.file_format or "PNG").upper() == "PNG" else ".jpg"
    path = os.path.join(out, "src_" + bpy.path.clean_name(image.name) + ext)
    if not os.path.exists(path):
        if image.packed_file:
            with open(path, "wb") as fh:
                fh.write(image.packed_file.data)
        else:
            image.save(filepath=path)
    return os.path.basename(path)

materials = {}
for mat in bpy.data.materials:
    if not mat.use_nodes or mat.users == 0:
        continue
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        continue
    entry = {"name": mat.name}
    base = linked(bsdf, 'Base Color')
    entry["base"] = save(base.image) if base else None
    entry["color"] = list(bsdf.inputs['Base Color'].default_value[:3])
    normal = linked(bsdf, 'Normal')
    entry["normal"] = save(normal.image) if normal else None
    entry["normal_gl"] = bool(normal) and "gl" in normal.image.name.lower() and "dx" not in normal.image.name.lower()
    rough = linked(bsdf, 'Roughness')
    entry["rough_map"] = save(rough.image) if rough else None
    entry["rough_channel"] = channel(bsdf, 'Roughness')
    entry["rough"] = bsdf.inputs['Roughness'].default_value
    metal = linked(bsdf, 'Metallic')
    entry["metal_map"] = save(metal.image) if metal else None
    entry["metal_channel"] = channel(bsdf, 'Metallic')
    entry["metal"] = bsdf.inputs['Metallic'].default_value
    # A picture's own alpha cuts its shape out (a road marking); a blended material with no picture to cut by is glass
    entry["alpha_test"] = bool(base) and linked(bsdf, 'Alpha') is not None
    blended = getattr(mat, "blend_method", "OPAQUE") == 'BLEND' or getattr(mat, "surface_render_method", "") == 'BLENDED'
    entry["alpha"] = 0.0 if blended and not entry["alpha_test"] else bsdf.inputs['Alpha'].default_value
    strength = bsdf.inputs.get('Emission Strength')
    entry["emissive"] = bool(linked(bsdf, 'Emission Color')) or (strength is not None and strength.default_value > 0 and
                        max(bsdf.inputs['Emission Color'].default_value[:3]) > 0)
    materials[mat.name] = entry

for o in bpy.data.objects:
    o.select_set(o.type == 'MESH')
bpy.ops.wm.obj_export(filepath=os.path.join(out, name + ".obj"), export_selected_objects=True, export_triangulated_mesh=True,
                      export_materials=True, export_uv=True, export_normals=True, apply_modifiers=True,
                      forward_axis='NEGATIVE_Z', up_axis='Y')
with open(os.path.join(out, "blender_materials.json"), "w", encoding="utf-8") as fh:
    json.dump(list(materials.values()), fh, indent=1)
"""


def blender():
    found = sorted(glob.glob(r"C:\Program Files\Blender Foundation\Blender *\blender.exe"))
    if not found:
        sys.exit("Blender not found")
    return found[-1]


def srgb(linear):
    """Blender's colour values are linear; a picture's are sRGB"""
    c = np.clip(np.asarray(linear, np.float32), 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def load(out, name, mode, which=None):
    """A picture of the model, no bigger than SIZE; one of its channels (0 red, 1 green, 2 blue) when given"""
    image = Image.open(os.path.join(out, name))
    image = image.convert("RGB").split()[which] if which is not None else image.convert(mode)
    if max(image.size) > SIZE:
        scale = SIZE / max(image.size)
        image = image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.LANCZOS)
    return image


def main(source, name, out):
    out = os.path.abspath(out)
    os.makedirs(out, exist_ok=True)
    script = os.path.join(out, "_export.py")
    with open(script, "w", encoding="utf-8") as fh:
        fh.write(EXPORT)
    command = [blender(), "-b"] + ([source] if source.lower().endswith(".blend") else []) + \
              ["--python", script, "--", os.path.abspath(source), out, name]
    subprocess.run(command, check=True, stdout=subprocess.DEVNULL)
    os.remove(script)

    with open(os.path.join(out, "blender_materials.json"), encoding="utf-8") as fh:
        found = json.load(fh)

    # The same stuff under two names is one material
    kinds, aliases, written = {}, {}, []
    for m in found:
        if m.get("alpha", 1.0) <= GLASS_ALPHA:
            aliases[m["name"]] = None
            continue
        key = (m["base"], None if m["base"] else tuple(round(c, 3) for c in m["color"]), m["normal"], m["normal_gl"],
               m["rough_map"], m.get("rough_channel"), round(m["rough"], 2), m["metal_map"], m.get("metal_channel"),
               round(m["metal"], 2), m["emissive"], m.get("alpha_test", False))
        if key in kinds:
            aliases[m["name"]] = kinds[key]
            continue
        index = len(kinds)
        kinds[key] = own_name(m["name"], kinds.values()) if KEEP_NAMES else f"{name}_{index}"
        aliases[m["name"]] = kinds[key]
        written.append(prepare(out, kinds[key], m))

    with open(os.path.join(out, "materials.json"), "w", encoding="utf-8") as fh:
        json.dump({"materials": written, "aliases": aliases}, fh, indent=1)
    print(f"{len(found)} materials as {len(written)}, in {out}")


def prepare(out, key, m):
    metal = m["metal"]
    cut = m.get("alpha_test", False)
    if m["base"]:
        diffuse = load(out, m["base"], "RGBA" if cut else "RGB")
    else:
        diffuse = Image.new("RGB", (SWATCH, SWATCH), tuple(int(v * 255) for v in srgb(m["color"])))

    # A metal's colour is in its reflection; its own picture darker, or chrome comes out as grey paint
    if metal > 0.5 and not m["emissive"]:
        diffuse = Image.fromarray((np.asarray(diffuse, np.float32) * (1 - 0.55 * metal)).astype(np.uint8))

    # Cut out by its alpha: a PNG keeps it
    diffuse_name = key + ("_diffuse.png" if cut else "_diffuse.jpg")
    if cut:
        diffuse.save(os.path.join(out, diffuse_name))
    else:
        diffuse.save(os.path.join(out, diffuse_name), quality=90)

    normal = None
    if m["normal"]:
        picture = np.asarray(load(out, m["normal"], "RGB")).copy()
        if m["normal_gl"]:
            picture[..., 1] = 255 - picture[..., 1]
        normal = key + "_normal" + MAPS
        Image.fromarray(picture).save(os.path.join(out, normal), **SAVE)

    size = diffuse.size
    rough = (np.asarray(load(out, m["rough_map"], "L", m.get("rough_channel")).resize(size), np.float32) / 255) if m["rough_map"] else np.full(size[::-1], m["rough"], np.float32)
    metals = (np.asarray(load(out, m["metal_map"], "L", m.get("metal_channel")).resize(size), np.float32) / 255) if m["metal_map"] else np.full(size[::-1], metal, np.float32)
    smooth = 1 - rough
    maps = np.stack([smooth ** 1.5, smooth, np.clip(metals * 0.8 + smooth * 0.15, 0, 1)], axis=-1)
    Image.fromarray((maps * 255).astype(np.uint8), "RGB").save(os.path.join(out, key + "_maps" + MAPS), **SAVE)

    return {"name": key, "diffuse": diffuse_name, "normal": normal, "maps": key + "_maps" + MAPS,
            "emissive": 0.8 if m["emissive"] else 0.0, "alphaTest": cut, "from": m["name"]}


if __name__ == "__main__":
    positional = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(positional) not in (3, 4):
        sys.exit(__doc__)
    if len(positional) == 4:
        SIZE = int(positional[3])
    if "--jpg" in sys.argv:
        MAPS, SAVE = ".jpg", {"quality": 92}
    KEEP_NAMES = "--keep-names" in sys.argv
    main(positional[0], positional[1], positional[2])
