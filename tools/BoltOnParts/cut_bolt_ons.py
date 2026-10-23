"""
Run inside Blender (prepare_bolt_ons.py does): the bolt-ons that are not wheels, each cut out of its download into a
.blend in the part's own space. Blender's space is Z up; prepare_prop exports with Y up and -Z forward, so the part's X
is Blender's X, its Y Blender's Z and its Z Blender's -Y.

  muffler     its inlet's end at the origin (where the exhaust pipe's exhaust:tip slot is), the muffler running back
              from it along the part's +Z (Blender's -Y), its middle on the axis
  bottle      standing up, the middle of its foot at the origin
  battery     standing, the middle of its foot at the origin, its length across (X)

  blender -b --python cut_bolt_ons.py -- <sources json: download -> scene.gltf> <out folder> <pictures json>

The pictures: {"battery_<colour>": the battery's base colour picture recoloured (prepare_bolt_ons.py does it)}.
"""
import bpy, bmesh, json, math, sys
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
sources, out, pictures = json.loads(args[0]), args[1], json.loads(args[2])


def load(source, only=None):
    """The download's meshes, out of the import's empties with where they put them, joined into one object; only: the
    names of the objects to keep"""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=sources[source])
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in bpy.data.objects:
        o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in [x for x in bpy.data.objects if x.type != 'MESH' or (only and x.name not in only)]:
        bpy.data.objects.remove(o)
    meshes = list(bpy.data.objects)
    for o in meshes:
        if o.data.users > 1:
            o.data = o.data.copy()
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.matrix_world = Matrix.Identity(4)
    return o


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


def paint(o, colour, metallic, roughness):
    """A model of no pictures (a CAD one) given the colour of what it is made of"""
    for mat in o.data.materials:
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if mat and mat.use_nodes else None
        if bsdf is None:
            continue
        bsdf.inputs['Base Color'].default_value = (*colour, 1.0)
        bsdf.inputs['Metallic'].default_value = metallic
        bsdf.inputs['Roughness'].default_value = roughness


def picture(o, path):
    """The base colour picture of every material of the object swapped for another"""
    image = bpy.data.images.load(path)
    for mat in o.data.materials:
        for node in (mat.node_tree.nodes if mat and mat.use_nodes else []):
            if node.type == 'TEX_IMAGE' and node.image and "basecolor" in node.image.name.lower().replace("_", ""):
                node.image = image


def save(name):
    bpy.ops.wm.save_as_mainfile(filepath=out + "/" + name + ".blend")


def rz(deg): return Matrix.Rotation(math.radians(deg), 4, 'Z')
def at(v): return Matrix.Translation(Vector(v))
def scale(s): return Matrix.Scale(s, 4)


def muffler(o, length_axis, inlet, size):
    """
    A muffler into its space: length_axis ("x" or "y") the way it runs in the download, inlet the end its inlet is at
    ("min", "max", "narrow": the end whose pipes span less across, or "wide": the other), size the scale to metres
    """
    lo, hi = bounds(o)
    i = "xy".index(length_axis)
    across = 1 - i
    if inlet in ("narrow", "wide"):
        def span(at_end):
            vs = [v.co[across] for v in o.data.vertices if abs(v.co[i] - at_end) < (hi[i] - lo[i]) * 0.02]
            return max(vs) - min(vs)
        inlet = "min" if (span(lo[i]) <= span(hi[i])) == (inlet == "narrow") else "max"
    end = lo[i] if inlet == "min" else hi[i]
    # the inlet's middle: the middle of what is at that end (an offset inlet is off the case's middle)
    near = [v.co for v in o.data.vertices if abs(v.co[i] - end) < (hi[i] - lo[i]) * 0.02]
    mid = Vector([(min(p[k] for p in near) + max(p[k] for p in near)) / 2 for k in range(3)])
    mid[i] = end
    # the length onto Blender's -Y, the inlet at the origin
    turn = {("x", "min"): rz(-90), ("x", "max"): rz(90), ("y", "min"): rz(180), ("y", "max"): Matrix.Identity(4)}[(length_axis, inlet)]
    o.data.transform(turn @ scale(size) @ at(-mid))


def stand(o, size, length_across=None, squash=1.0):
    """Standing on its foot, the middle of the foot at the origin, scaled by size; squash shortens it along Blender's Y
    (a truck battery made a car's) before length_across turns its length across ("y": the download's length along Y)"""
    lo, hi = bounds(o)
    o.data.transform(scale(size) @ at((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))
    if squash != 1.0:
        o.data.transform(Matrix.Diagonal((1.0, squash, 1.0, 1.0)))
    if length_across == "y":
        o.data.transform(rz(90))


report = {}

# The mufflers. Kryox Shade's exhaust system: Object_4 is one of its two oval mufflers, its length along X with a stub
# each end; 0.936 across, made a 9.5-inch case
o = load("exhaust_system", only=["Object_4"])
decimate(o, 6000)
lo, hi = bounds(o)
muffler(o, "x", "min", 0.24 / (hi.y - lo.y))
save("oval_turbo_muffler")

# VR DESIGNER's chambered muffler: millimetres, a case 250 across with a single inlet one end and two outlets the other,
# along Y; no pictures, so painted aluminised steel
o = load("muffler")
decimate(o, 8000)
paint(o, (0.55, 0.55, 0.56), 0.7, 0.45)
muffler(o, "y", "narrow", 0.001)
save("chambered_muffler")

# Silver10211's round can with its tailpipe tip, along X, the closed end (the inlet's, the wide one) at the other: 1.92
# across, made 5 inches
o = load("exhuast_muffler")
decimate(o, 8000)
lo, hi = bounds(o)
muffler(o, "x", "wide", 0.127 / (hi.z - lo.z))
save("round_muffler")

# The nitrous bottle: ion.fbx's, standing up (its valve one object, the bottle the other), 1.61 across: a 10 lb bottle,
# 7 inches across. The twin set: two of it side by side, a centimetre apart
o = load("car_nitro_bottle")
decimate(o, 12000)
lo, hi = bounds(o)
across = 0.178
stand(o, across / (hi.x - lo.x))
save("nitrous_bottle_10_lb")
other = o.copy(); other.data = o.data.copy(); bpy.context.collection.objects.link(other)
o.data.transform(at((-(across / 2 + 0.005), 0, 0)))
other.data.transform(at((across / 2 + 0.005, 0, 0)))
save("twin_nitrous_bottles_2x10_lb")

# The battery: Helsingr's, a truck battery (60 wide, 155 long along Y, 64 high): made 6.9 inches wide and 11.8 long
# (a group 27 of the day), the caps squashed with it, its case recoloured for each of the stock batteries
for colour in ("black", "blue", "silver"):
    o = load("battery")
    lo, hi = bounds(o)
    size = 0.175 / (hi.x - lo.x)
    stand(o, size, length_across="y", squash=0.30 / ((hi.y - lo.y) * size))
    picture(o, pictures["battery_" + colour])
    save("12_volt_battery_" + colour)

for name in ("oval_turbo_muffler", "chambered_muffler", "round_muffler", "nitrous_bottle_10_lb", "12_volt_battery_black"):
    bpy.ops.wm.open_mainfile(filepath=out + "/" + name + ".blend")
    vs = [v.co for o in bpy.data.objects if o.type == 'MESH' for v in o.data.vertices]
    report[name] = [[round(min(v[k] for v in vs), 3) for k in range(3)], [round(max(v[k] for v in vs), 3) for k in range(3)]]

with open(out + "/bolt_ons_report.json", "w") as f:
    json.dump(report, f, indent=1)
