"""
The running gear's stand-in models (roadmap step 31 phase 4): parts of the packs under parts/running_gear drawn with
CC BY 4.0 models from Sketchfab until our own (RunningGearParts, the C# builder next to this) take their place. Each is
cut out of its download, decimated, scaled to metres and turned to the part's own space, with its origin where the part
mounts, then goes through tools/Props/prepare_prop.py and PropBuilder (--keep-origin) into its pack's folder, with a
CREDITS.txt there naming what it was made from.

  "Low Poly Brakes" by themodel_finder        the drum (four sizes)
  "Vented Disk Brake Rotor" by pao1011        the period discs' rotor (and the stock discs, parts/models.json)
  "SINGLE PISTON BREAK CALIPER" by VR DESIGNER   the period discs' caliper
  "Brake Shoe" by Addere Creations            the drum's shoes, two to a drum
  "Leaf Spring" by 3dsl                       every leaf spring
  "Differential Gear box" by Anoopbg          every differential carrier (the case only: the gears are ours)
  "COIL SPRING" by VR DESIGNER                the stock coil springs (the base game's parts/models.json)

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
    "coil_spring": ("COIL SPRING", "VR DESIGNER", "https://sketchfab.com/vr.designer_09",
                    "https://sketchfab.com/3d-models/coil-spring-3bf2215ce5f34e83a966c780393956fc"),
    "vented_disk_brake__rotor": ("Vented Disk Brake Rotor", "pao1011", "https://sketchfab.com/pao1011",
                                 "https://sketchfab.com/3d-models/vented-disk-brake-rotor-b4393b994037442db7da86a4fba8fb85"),
    "single_piston_break_caliper": ("SINGLE PISTON BREAK CALIPER", "VR DESIGNER", "https://sketchfab.com/vr.designer_09",
                                    "https://sketchfab.com/3d-models/single-piston-break-caliper-a68ccf22cca946d08d03d9e345b5d8e6"),
}

# Drum diameters, inches: the drum's model at each size (the drums' diam_mm picks one)
DRUMS = {"drum_9": 9.0, "drum_9_5": 9.5, "drum_10": 10.0, "drum_11": 11.0}

# Each model: its download (or the downloads it is put together from), the packs it goes into, its largest picture
MODELS = {name: ("low_poly_brakes", ["brakes_drum"], 256) for name in DRUMS}
MODELS.update({
    "disc": (("vented_disk_brake__rotor", "single_piston_break_caliper"), ["brakes_disc"], 256),
    "shoes": ("brake_shoe", ["brakes_drum"], 512),
    "leaf": ("leaf_spring", ["springs_leaf"], 512),
    "diff_case": ("differential_gear_box", ["axles_gm", "axles_ford", "axles_mopar", "axles_dana", "axle_speed"], 256),
    # Not a pack: the folder the base game's parts/models.json names for the stock springs
    "coil_spring": ("coil_spring", ["coil_springs"], 256),
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

def load(name, only=None, fresh=True):
    # fresh=False adds the download to what is in the scene already; only: the names of the objects to keep
    if fresh:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=sources[name])
    new = [o for o in bpy.data.objects if o not in before]
    names = [o.name for o in new]
    meshes = [o for o in new if o.type == 'MESH']
    for o in bpy.data.objects:
        o.select_set(o in meshes)
    bpy.context.view_layer.objects.active = meshes[0]
    # Out of the import's empties (Sketchfab's root turns the model upright) with where they put it
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in [m for m in meshes if only and m.name not in only]:
        meshes.remove(o)
        bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    # What is not a mesh (the import's empties) goes
    for other in [bpy.data.objects[n] for n in names if n in bpy.data.objects and n != o.name]:
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

def paint(o, colour, metallic, roughness):
    # A model of no pictures (a CAD one) given the colour of what it is made of
    for mat in o.data.materials:
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if mat and mat.use_nodes else None
        if bsdf is None:
            continue
        bsdf.inputs['Base Color'].default_value = (*colour, 1.0)
        bsdf.inputs['Metallic'].default_value = metallic
        bsdf.inputs['Roughness'].default_value = roughness

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

def machined(o, radius, size=512, seed=7):
    # A turned steel face (a rotor's): fine rings of the lathe's cut round the axle (X), a picture of them made here and
    # packed into the file, its rows the distance out from the axle, so each ring is one row
    import numpy as np
    rng = np.random.RandomState(seed)
    rows = 0.5 + 0.012 * np.convolve(rng.standard_normal(size + 8), np.ones(9) / 3, 'valid')[:size] + 0.02 * rng.standard_normal(size)
    grey = np.clip(rows[:, None] + 0.008 * rng.standard_normal((size, size)), 0, 1)
    pixels = np.ones((size, size, 4), dtype=np.float32)
    pixels[..., 0], pixels[..., 1], pixels[..., 2] = grey, grey * 1.005, grey * 1.02
    image = bpy.data.images.new(o.name + "_machined", size, size)
    image.pixels.foreach_set(pixels.ravel())
    image.pack()
    uv = o.data.uv_layers[0] if o.data.uv_layers else o.data.uv_layers.new()
    for loop in o.data.loops:
        c = o.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (math.atan2(c.z, c.y) / math.tau * 6, min(math.hypot(c.y, c.z) / radius, 0.999))
    for mat in o.data.materials:
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        texture = nodes.new('ShaderNodeTexImage')
        texture.image = image
        links.new(texture.outputs['Color'], next(n for n in nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'])

def pads(o, test, colour, roughness):
    # The pieces of a mesh a test of their bounds picks (a caliper's pads) given a material of their own
    bm = bmesh.new(); bm.from_mesh(o.data)
    mat = bpy.data.materials.new("brake_pad"); mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*colour, 1.0)
    bsdf.inputs['Metallic'].default_value = 0.0
    bsdf.inputs['Roughness'].default_value = roughness
    o.data.materials.append(mat)
    index, seen = len(o.data.materials) - 1, set()
    for face in bm.faces:
        if face.index in seen:
            continue
        piece, todo = [], [face]
        seen.add(face.index)
        while todo:
            f = todo.pop()
            piece.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index); todo.append(g)
        vs = [v.co for f in piece for v in f.verts]
        lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        if test(len(piece), lo, hi):
            for f in piece:
                f.material_index = index
    bm.to_mesh(o.data); bm.free()

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

# The discs: an undrilled vented rotor (its vanes' quarter, Object_3, is inside it and goes), 11.77 across on an axis
# along Y with its hat towards -Y, made an 11-inch rotor; and a single-piston caliper in millimetres, its piston along Y,
# its fingers (the outboard side) towards -Y, the rotor's ring running between its pads at y -50 with its outer edge
# under the bridge at z 188. The caliper stands upright behind the rotor (the part's +Z); the right side's disc is the
# left's rolled over about the axle (CarPartsLayout), so it is behind there too. The origin on the hat's face, the outside
# towards +X
rotor = load("vented_disk_brake__rotor", only=["Object_2"])
decimate(rotor, 8000)
paint(rotor, (0.32, 0.32, 0.33), 0.8, 0.45)
lo, hi = bounds(rotor)
radius = (hi.x - lo.x) / 2
ring = [v.co.y for v in rotor.data.vertices if math.hypot(v.co.x, v.co.z) > radius * 0.9]
ring_y, hat = (min(ring) + max(ring)) / 2, lo.y
rotor_m = 11.0 * 0.0254 / 2
s = rotor_m / radius
rotor.data.transform(scale(s) @ at((-(lo.x + hi.x) / 2, -ring_y, -(lo.z + hi.z) / 2)))
caliper = load("single_piston_break_caliper", fresh=False)
paint(caliper, (0.22, 0.22, 0.23), 0.6, 0.7)
# Its pads (each lining on its plate, one piece) either side of the rotor's ring: y -87 to -69 and -33 to -15, the
# lining's whole arc across; the bolts in the same y are small
pads(caliper, lambda faces, lo, hi: faces > 1000 and hi.x - lo.x > 200
     and any(a - 1 < lo.y and hi.y < b + 1 for a, b in ((-87, -69), (-33, -15))), (0.035, 0.03, 0.028), 0.9)
decimate(caliper, 8000)
caliper.data.transform(scale(0.001) @ at((0, 50, -(188 - rotor_m * 1000))))
for o in (rotor, caliper):
    o.data.transform(rz(90) @ at((0, -(hat - ring_y) * s, 0)))
# From over the top to behind: Blender's +Z (up) to -Y (the part's +Z, the back)
caliper.data.transform(rx(90))
# The rotor's faces turned steel, rings round its axle (X now)
machined(rotor, rotor_m)
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

# The coil spring for the stock springs, in their model's space: their mount slot is 0.4 m under the origin, so
# the coil's foot goes 0.34 m under it (at the hub's height: the seat is 0.06 m under the hub) and it stands up from
# there. The stock coil was 96 mm across and 277 mm tall, small for the cars of the day (front coils about 5.5
# inches across, 12 to 13 inches tall installed): this one 135 mm across, its own build making it 320 mm tall
o = load("coil_spring")
decimate(o, 6000)
lo, hi = bounds(o)
across = max(hi.x - lo.x, hi.y - lo.y)
o.data.transform(at((0, 0, -0.34)) @ scale(0.135 / across) @ at((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))
save("coil_spring")
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
            used = source if isinstance(source, tuple) else (source,)
            prepared = os.path.join(work, "prepared_" + name)
            subprocess.run([sys.executable, PREPARE, os.path.join(blends, name + ".blend"), name, prepared, str(size), "--jpg"],
                           check=True)
            for pack in packs:
                target = os.path.join(running_gear, pack)
                os.makedirs(target, exist_ok=True)
                subprocess.run(["dotnet", PROP_BUILDER, prepared, name, target, "--scale", "1", "--keep-origin"], check=True)
                credits.setdefault(pack, []).extend((name + ".kn5", u) for u in used)

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
