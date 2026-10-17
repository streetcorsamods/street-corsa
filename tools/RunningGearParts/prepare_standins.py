"""
The running gear's stand-in models (roadmap step 31 phase 4): parts of the packs under parts/running_gear drawn with
CC BY 4.0 models from Sketchfab until our own (RunningGearParts, the C# builder next to this) take their place. Each is
cut out of its download, decimated, scaled to metres and turned to the part's own space, with its origin where the part
mounts, then goes through tools/Props/prepare_prop.py and PropBuilder (--keep-origin) into its pack's folder, with a
CREDITS.txt there naming what it was made from.

  "Low Poly Brakes" by themodel_finder        the drum (four sizes) and the period discs
  "Brake Shoe" by Addere Creations            the drum's shoes, two to a drum
  "Leaf Spring" by 3dsl                       every leaf spring
  "Differential Gear box" by Anoopbg          every differential carrier (the case only: the gears are ours)

A part's own space: metres, Y up, its front towards -Z. A wheel-side part (a drum, its shoes) has its +X towards the
outside of the car and the hub's axis along X; a leaf spring runs along Z with its top at the axle seat; a carrier's
axis is the axle's, X across the car.

  python prepare_standins.py <folder of the downloads (the Sketchfab zips)> <parts folder> [--blender <blender.exe>]

Needs Blender, numpy and Pillow (prepare_prop.py), and PropBuilder built (dotnet build tools/Props/PropBuilder).
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "Shared"))
from scene_tools import blender, without_blender_option  # noqa: E402

PREPARE = os.path.join(HERE, "..", "Props", "prepare_prop.py")
PROP_BUILDER = os.path.join(HERE, "..", "Props", "PropBuilder", "bin", "Debug", "net10.0", "PropBuilder.dll")

# The downloads, and what each is credited as
SOURCES = {
    "low_poly_brakes": ("Low Poly Brakes", "themodel_finder", "https://sketchfab.com/themodel_finder",
                        "https://sketchfab.com/3d-models/low-poly-brakes-08c70a12956f4775bd67de898be343aa"),
    "brake_shoe": ("Brake Shoe", "Addere Creations", "https://sketchfab.com/adderecreations",
                   "https://sketchfab.com/3d-models/brake-shoe-c2b2e708f2fd4bd297c353776abb39a9"),
    "leaf_spring": ("Leaf Spring", "3dsl", "https://sketchfab.com/3dsl",
                    "https://sketchfab.com/3d-models/leaf-spring-02cea2df054242f099c3c70721ca5b81"),
    "differential_gear_box": ("Differential Gear box", "Anoopbg", "https://sketchfab.com/Anoopbg",
                              "https://sketchfab.com/3d-models/differential-gear-box-1e3b1c25370b467887d982c1011f3b1e"),
}

# Drum diameters, inches: the drum's model at each size (the drums' diam_mm picks one)
DRUMS = {"drum_9": 9.0, "drum_9_5": 9.5, "drum_10": 10.0, "drum_11": 11.0}

# Each model: its download, the packs it goes into, its largest picture
MODELS = {name: ("low_poly_brakes", ["brakes_drum"], 256) for name in DRUMS}
MODELS.update({
    "disc": ("low_poly_brakes", ["brakes_disc"], 256),
    "shoes": ("brake_shoe", ["brakes_drum"], 512),
    "leaf": ("leaf_spring", ["springs_leaf"], 512),
    "diff_case": ("differential_gear_box", ["axles_gm", "axles_ford", "axles_mopar", "axles_dana", "axle_speed"], 256),
})

# Run inside Blender: each model out of its download into a .blend of its own, in the part's space. Blender's space is
# Z up; prepare_prop exports with Y up and -Z forward, so the part's X is Blender's X, its Y Blender's Z and its Z
# Blender's -Y.
CUT = r"""
import bpy, bmesh, json, math, sys
from mathutils import Matrix, Vector
args = sys.argv[sys.argv.index("--") + 1:]
sources, out = json.loads(args[0]), args[1]
drums = json.loads(args[2])

def load(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=sources[name])
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in bpy.data.objects:
        o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = meshes[0]
    # Out of the import's empties (Sketchfab's root turns the model upright) with where they put it
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    # What is not a mesh (the import's empties) goes
    for other in [x for x in bpy.data.objects if x != o]:
        bpy.data.objects.remove(other)
    o.matrix_world = Matrix.Identity(4)
    # An unlit material (a scan's: KHR_materials_unlit) becomes a plain one of its picture, which prepare_prop reads
    for mat in o.data.materials:
        if mat is None or not mat.use_nodes or any(n.type == 'BSDF_PRINCIPLED' for n in mat.node_tree.nodes):
            continue
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        image = next((n for n in nodes if n.type == 'TEX_IMAGE' and n.image), None)
        output = next((n for n in nodes if n.type == 'OUTPUT_MATERIAL'), None) or nodes.new('ShaderNodeOutputMaterial')
        bsdf = nodes.new('ShaderNodeBsdfPrincipled')
        bsdf.inputs['Roughness'].default_value = 0.75
        if image:
            links.new(image.outputs['Color'], bsdf.inputs['Base Color'])
        links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    return o

def keep(o, test):
    # Only the vertices a test keeps, with their faces
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not test(v.co)], context='VERTS')
    bm.to_mesh(o.data); bm.free()

def bounds(o):
    vs = [v.co for v in o.data.vertices]
    return (Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
            Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))))

def decimate(o, faces):
    if len(o.data.polygons) <= faces:
        return
    m = o.modifiers.new("decimate", 'DECIMATE')
    m.ratio = faces / len(o.data.polygons)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)

def save(name):
    bpy.ops.wm.save_as_mainfile(filepath=out + "/" + name + ".blend")

def rz(deg): return Matrix.Rotation(math.radians(deg), 4, 'Z')
def rx(deg): return Matrix.Rotation(math.radians(deg), 4, 'X')
def ry(deg): return Matrix.Rotation(math.radians(deg), 4, 'Y')
def at(v): return Matrix.Translation(Vector(v))
def scale(s): return Matrix.Scale(s, 4)

# The brakes: three assemblies side by side along X, each on an axis along Y, its studs (the wheel's side) towards -Y.
# The drum is the third: centre (5.684, 0.780) across, its drum's outer face at y -0.229 and 1.0 across. The origin goes
# on that face (the brake slot is a little inboard of the hub), the outside towards +X.
for name, inches in drums.items():
    o = load("low_poly_brakes")
    keep(o, lambda c: 5.0 < c.x < 6.4)
    o.data.transform(scale(inches * 0.0254) @ rz(90) @ at((-5.684, 0.229, -0.780)))
    save(name)

# The discs: the second assembly, rotor 1.0 across at x 4.079, its hat's face at y -0.146; an 11-inch rotor
o = load("low_poly_brakes")
keep(o, lambda c: 3.5 < c.x < 4.85)
o.data.transform(scale(11.1 * 0.0254) @ rz(90) @ at((-4.079, 0.146, -0.781)))
save("disc")

# The shoe: a scanned arc, its chord along X, its width along Y, bowed up. Its circle from its chord and height, scaled
# so the lining's outside meets a 10-inch drum, the arc's centre on the hub's axis; two of them, front and back of the
# drum (the wheel cylinder goes between them at the top), in the drum's depth
o = load("brake_shoe")
decimate(o, 1600)
lo, hi = bounds(o)
half, height = (hi.x - lo.x) / 2, hi.z - lo.z
radius = (half * half + height * height) / (2 * height)
centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, hi.z - radius))
o.data.transform(at((-0.045, 0, 0)) @ scale(0.125 / radius) @ rz(90) @ at(-centre))
other = o.copy(); other.data = o.data.copy(); bpy.context.collection.objects.link(other)
o.data.transform(rx(90)); other.data.transform(rx(-90))
save("shoes")

# The leaf spring: a scan in millimetres, along Y, arched along X, its width along Z. Turned so it arches up and down,
# its middle (the axle seat) below its eyes, the origin on the top of the pack at the seat
o = load("leaf_spring")
decimate(o, 12000)
o.data.transform(ry(-90))
lo, hi = bounds(o)
mid_y, length = (lo.y + hi.y) / 2, hi.y - lo.y
def mean_z(test):
    zs = [v.co.z for v in o.data.vertices if test(abs(v.co.y - mid_y) / length)]
    return sum(zs) / len(zs)
if mean_z(lambda t: t < 0.08) > mean_z(lambda t: t > 0.4):
    o.data.transform(ry(180))
    lo, hi = bounds(o)
seat = max(v.co.z for v in o.data.vertices if abs(v.co.y - mid_y) / length < 0.05)
o.data.transform(scale(0.00105) @ at((-(lo.x + hi.x) / 2, -mid_y, -seat)))
save("leaf")

# The carrier's case: millimetres, its axis along X already, the ring gear's flange at the low end (the car's right),
# 94 across: about 170 mm, a carrier for an 8 to 9-inch ring gear, made as much bigger as the housings' middles
# (RunningGearParts' Parts.CentreScale, 1.1)
o = load("differential_gear_box")
decimate(o, 6000)
lo, hi = bounds(o)
o.data.transform(scale(0.17 * 1.1 / (hi.y - lo.y)) @ at(-(lo + hi) / 2))
save("diff_case")
"""


def credit(name, sources_used):
    lines = []
    for model, source in sources_used:
        title, author, author_url, url = SOURCES[source]
        lines.append(f'{model} is made from "{title}" by {author} ({author_url}),\nlicensed under CC BY 4.0 '
                     f'(https://creativecommons.org/licenses/by/4.0/):\n{url}\n')
    lines.append("Changes: cut out of the download, decimated, scaled to metres and turned to the part's own space, "
                 "converted to KN5 (tools/RunningGearParts/prepare_standins.py), textures scaled down.\n")
    return "\n".join(lines)


def main(downloads, parts):
    running_gear = os.path.join(parts, "running_gear")
    if not os.path.isdir(running_gear):
        sys.exit(f"No running_gear packs under {parts}")
    if not os.path.isfile(PROP_BUILDER):
        sys.exit(f"Build PropBuilder first: {PROP_BUILDER}")

    work = tempfile.mkdtemp(prefix="sc_standins_")
    try:
        # Each download unzipped into a folder of its own
        sources = {}
        for source in SOURCES:
            folder = os.path.join(work, "src_" + source)
            os.makedirs(folder)
            with zipfile.ZipFile(os.path.join(downloads, source + ".zip")) as z:
                z.extractall(folder)
            sources[source] = os.path.join(folder, "scene.gltf")

        blends = os.path.join(work, "blends")
        os.makedirs(blends)
        script = os.path.join(work, "_cut.py")
        with open(script, "w", encoding="utf-8") as fh:
            fh.write(CUT)
        subprocess.run([blender(), "-b", "--python", script, "--", json.dumps(sources), blends, json.dumps(DRUMS)],
                       check=True, stdout=subprocess.DEVNULL)

        credits = {}
        for name, (source, packs, size) in MODELS.items():
            prepared = os.path.join(work, "prepared_" + name)
            subprocess.run([sys.executable, PREPARE, os.path.join(blends, name + ".blend"), name, prepared, str(size), "--jpg"],
                           check=True)
            for pack in packs:
                target = os.path.join(running_gear, pack)
                subprocess.run(["dotnet", PROP_BUILDER, prepared, name, target, "--scale", "1", "--keep-origin"], check=True)
                credits.setdefault(pack, []).append((name + ".kn5", source))

        for pack, used in credits.items():
            with open(os.path.join(running_gear, pack, "CREDITS.txt"), "w", encoding="utf-8", newline="\n") as fh:
                fh.write(credit(pack, used))
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    args = without_blender_option(sys.argv)
    if len(args) != 3:
        sys.exit(__doc__)
    main(args[1], args[2])
