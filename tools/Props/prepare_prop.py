"""
Readies any prop for PropBuilder, out of a model with PBR materials (a Sketchfab .glb, or a .blend): Blender exports
its meshes as one OBJ with a material to each triangle, and each material becomes three pictures for AC's
ksPerPixelMultiMap plus a line in materials.json:

  diffuse     its base colour picture (1024 at most), or a swatch of its flat colour
  normal      its normal map, green turned over when it is OpenGL's (AC's is DirectX's); none when it has none
  maps        red the specular and green the gloss (from how smooth it is), blue the reflection (the metal, and a
              little of any smooth surface): from its roughness and metallic pictures, or its flat values
  emissive    a material that glows in the model (a lit dial) glows in AC too
  glass       a see-through material (alpha at most GLASS_ALPHA: a dial's cover) is left out, so what is behind it shows

Materials that come out the same are one. The pictures are taken out of the model as they are (a .glb carries them).

  python prepare_prop.py <model.glb|.blend> <name> <out folder>

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

def linked(node, socket):
    s = node.inputs.get(socket)
    if s is None or not s.links:
        return None
    n = s.links[0].from_node
    if n.type == 'NORMAL_MAP':
        return linked(n, 'Color')
    if n.type == 'SEPARATE_COLOR' or n.type == 'SEPRGB':
        return linked(n, 'Color')
    return n if n.type == 'TEX_IMAGE' and n.image else None

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
    entry["rough"] = bsdf.inputs['Roughness'].default_value
    metal = linked(bsdf, 'Metallic')
    entry["metal_map"] = save(metal.image) if metal else None
    entry["metal"] = bsdf.inputs['Metallic'].default_value
    entry["alpha"] = bsdf.inputs['Alpha'].default_value
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


def load(out, name, mode):
    image = Image.open(os.path.join(out, name)).convert(mode)
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
               m["rough_map"], round(m["rough"], 2), m["metal_map"], round(m["metal"], 2), m["emissive"])
        if key in kinds:
            aliases[m["name"]] = kinds[key]
            continue
        index = len(kinds)
        kinds[key] = f"{name}_{index}"
        aliases[m["name"]] = kinds[key]
        written.append(prepare(out, kinds[key], m))

    with open(os.path.join(out, "materials.json"), "w", encoding="utf-8") as fh:
        json.dump({"materials": written, "aliases": aliases}, fh, indent=1)
    print(f"{len(found)} materials as {len(written)}, in {out}")


def prepare(out, key, m):
    metal = m["metal"]
    if m["base"]:
        diffuse = load(out, m["base"], "RGB")
    else:
        diffuse = Image.new("RGB", (SWATCH, SWATCH), tuple(int(v * 255) for v in srgb(m["color"])))

    # A metal's colour is in its reflection; its own picture darker, or chrome comes out as grey paint
    if metal > 0.5 and not m["emissive"]:
        diffuse = Image.fromarray((np.asarray(diffuse, np.float32) * (1 - 0.55 * metal)).astype(np.uint8))
    diffuse.save(os.path.join(out, key + "_diffuse.jpg"), quality=90)

    normal = None
    if m["normal"]:
        picture = np.asarray(load(out, m["normal"], "RGB")).copy()
        if m["normal_gl"]:
            picture[..., 1] = 255 - picture[..., 1]
        normal = key + "_normal.png"
        Image.fromarray(picture).save(os.path.join(out, normal))

    size = diffuse.size
    rough = (np.asarray(load(out, m["rough_map"], "L").resize(size), np.float32) / 255) if m["rough_map"] else np.full(size[::-1], m["rough"], np.float32)
    metals = (np.asarray(load(out, m["metal_map"], "L").resize(size), np.float32) / 255) if m["metal_map"] else np.full(size[::-1], metal, np.float32)
    smooth = 1 - rough
    maps = np.stack([smooth ** 1.5, smooth, np.clip(metals * 0.8 + smooth * 0.15, 0, 1)], axis=-1)
    Image.fromarray((maps * 255).astype(np.uint8), "RGB").save(os.path.join(out, key + "_maps.png"))

    return {"name": key, "diffuse": key + "_diffuse.jpg", "normal": normal, "maps": key + "_maps.png",
            "emissive": 0.8 if m["emissive"] else 0.0, "from": m["name"]}


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2], sys.argv[3])
