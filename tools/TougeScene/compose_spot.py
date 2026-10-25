"""
Lays out Touge Nights' meeting spot in Blender and saves it as a .blend for prepare_prop.py (then DinerSceneBuilder) to
make the game's KN5 of (build_spot.py runs it all):

  blender -b --python compose_spot.py -- <spec.json>

The spec (build_spot.py writes it): sketchfab (the unzipped Sketchfab models by id, each its .gltf; see build_spot.py's
MODELS: which are there decides what stands in the car park), textures (Poly Haven's maps by <asset>_<map>), turns
(degrees a model is turned round by, the cars made facing back), out (the .blend to save), renders (a folder: a few
views of the layout drawn into it, to look at; left out, none).

Gunma, 2002: the car park at the top of a mountain pass, where the racers meet at night. The pass runs east and west
along the ridge; the car park lies north of it on the ridge's edge, a guardrail along its far side, and past the rail
the ground falls away to the valley and the town's lights. The racers back into the bays along the rail, noses out,
facing the player's car, which stops in the middle of the lot facing east and looks over at them out of the side
window. A row of vending machines under a lean-to and a shuttered kiosk on the east side, a rest house, a bench and a
little shrine (a torii, a hokora, stone lanterns) on the west, lamps round the lot, pines all round and the forest up
the hill across the road, signs along the road.

Blender's space: x along the road (east), y north (the valley's side), z up; metres; the road at z = 0. The game's
(AC's) space is x the same, y up, z = -y: street.json's and diner.json's positions are the game's. A heading below is
the game's (0 facing its +z, Blender's -y, the road; 90 facing east).

The ground's materials are named lot_...: the builder never casts their shadow, and the car rides them (ground.bin).
"""
import json
import math
import os
import random
import re
import sys

import bpy
from mathutils import Matrix, Vector

spec = json.load(open(sys.argv[sys.argv.index("--") + 1], encoding="utf-8"))
OUT = spec["out"]
MODELS = spec.get("sketchfab", {})
TEXTURES = spec.get("textures", {})
TURNS = spec.get("turns", {})
RENDERS = spec.get("renders")

rng = random.Random(2002)

# ---- The layout (Blender's x, y) ----
ROAD_HALF = 3.5                 # the pass: a lane each way (Japan drives on the left: eastbound is the north lane)
LOT_WEST, LOT_EAST = -30.0, 30.0
LOT_SOUTH, LOT_NORTH = 5.0, 34.0
RAIL_Y = 34.6                   # the guardrail along the lot's north edge
BAY_Y = 29.0                    # the middle of the racers' bays (5 m deep), backed in towards the rail
BAYS = [-22.5 + 3.0 * k for k in range(16)]
PLAYER = (-6.0, 12.0)           # the player's car, facing east, the bays out of its left window

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
library = bpy.data.collections.new("library")
spot = bpy.data.collections.new("street")
scene.collection.children.link(spot)
skipped = []


def same_name(name):
    base, dot, tail = name.rpartition(".")
    return base if dot and tail.isdigit() else name


def dedupe():
    """Each import brings its own copy of a material or picture another has: one of each name is kept"""
    for collection in (bpy.data.images, bpy.data.materials):
        kept = {}
        for item in list(collection):
            key = same_name(item.name)
            if key in kept and kept[key] is not item:
                item.user_remap(kept[key])
                collection.remove(item)
            else:
                kept[key] = item


def unparent(objects):
    for o in objects:
        world = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = world


def join(meshes, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    piece = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    piece.name = name
    return piece


def shelve(piece):
    for c in list(piece.users_collection):
        c.objects.unlink(piece)
    library.objects.link(piece)


def triangles(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def bounds(o):
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


def decimate(o, ratio):
    m = o.modifiers.new("decimate", 'DECIMATE')
    m.ratio = ratio
    m.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def names_of(o):
    found = []
    while o is not None:
        found.append(o.name)
        o = o.parent
    return found


# ---- Materials and shapes of our own ----

def flat(name, colour, rough=0.6, metal=0.0):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    return material


def textured(name, key, rough=0.85, tint=None):
    """A material showing a Poly Haven texture (a flat colour when it was not downloaded)"""
    path = TEXTURES.get(key)
    material = flat(name, tint or (0.4, 0.4, 0.4), rough=rough)
    if path and os.path.isfile(path):
        tree = material.node_tree
        node = tree.nodes.new('ShaderNodeTexImage')
        node.image = bpy.data.images.load(path)
        tree.links.new(node.outputs['Color'], tree.nodes["Principled BSDF"].inputs['Base Color'])
    return material


def mesh_object(name, vertices, faces, uvs, materials, slots):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    layer = mesh.uv_layers.new()
    for face, corners in zip(mesh.polygons, uvs):
        for loop, uv in zip(face.loop_indices, corners):
            layer.data[loop].uv = uv
    for material in materials:
        mesh.materials.append(material)
    for face, slot in zip(mesh.polygons, slots):
        face.material_index = slot
    o = bpy.data.objects.new(name, mesh)
    spot.objects.link(o)
    return o


def box(name, size, middle, material, tile=None, turn=0.0):
    """A box of one material, its middle at a point, turned about the vertical; its picture every tile metres, or once a face"""
    w, d, h = (s / 2 for s in size)
    v = [(-w, -d, -h), (w, -d, -h), (w, -d, h), (-w, -d, h), (-w, d, -h), (w, d, -h), (w, d, h), (-w, d, h)]
    faces = [(0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0)]
    spans = [(2 * w, 2 * h), (2 * w, 2 * h), (2 * d, 2 * h), (2 * d, 2 * h), (2 * w, 2 * d), (2 * w, 2 * d)]
    uvs = [[(0, 0), (su / tile, 0), (su / tile, sv / tile), (0, sv / tile)] if tile else [(0, 0), (1, 0), (1, 1), (0, 1)]
           for su, sv in spans]
    rot = Matrix.Rotation(math.radians(turn), 4, 'Z')
    v = [tuple(Vector(middle) + rot @ Vector(p)) for p in v]
    return mesh_object(name, v, faces, uvs, [material], [0] * 6)


def quad(name, corners, material, tile):
    """A flat quad (four corners, Blender's x, y, z, counter-clockwise from above), its picture every tile metres"""
    return mesh_object(name, corners, [(0, 1, 2, 3)], [[(p[0] / tile, p[1] / tile) for p in corners]], [material], [0])


# ---- The Sketchfab models ----

def import_meshes(name, path, keep=None, drop=None):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = set(bpy.data.objects) - before

    def matches(o, patterns):
        return any(re.search(p, n) for p in patterns for n in names_of(o))

    meshes = [o for o in added if o.type == 'MESH' and not o.hide_render
              and (keep is None or matches(o, keep)) and not (drop and matches(o, drop))]
    if not meshes:
        for o in added:
            bpy.data.objects.remove(o)
        raise RuntimeError(f"{name}: nothing kept of {path}")
    unparent(meshes)
    for o in added:
        if o not in meshes:
            bpy.data.objects.remove(o)
    return meshes


def own_name(name):
    """A model's own name with its .NNN kept apart ("vending_machine.001" -> "vending_machine_001"): a model may have two
    materials so named, two different ones, which dedupe() would otherwise take for copies of one"""
    base, dot, tail = name.rpartition(".")
    return f"{base}_{tail}" if dot and tail.isdigit() else name


def own_materials(meshes, prefix):
    """
    The meshes' materials and pictures named after the model: two models' "Material" are otherwise taken for one. A
    model imported again gets Blender's .NNN on these names, which dedupe() merges back into the first import's
    """
    for o in meshes:
        for slot in o.material_slots:
            material = slot.material
            if material is None:
                continue
            if not material.name.startswith(prefix + "_"):
                material.name = f"{prefix}_{own_name(material.name)}"
            if material.use_nodes:
                for node in material.node_tree.nodes:
                    if node.type == 'TEX_IMAGE' and node.image and not node.image.name.startswith(prefix + "_"):
                        node.image.name = f"{prefix}_{own_name(node.image.name)}"


def cut_out_alpha(meshes):
    """Leaves and the like cut out by their picture's alpha, never blended (prepare_prop.py cuts a material whose alpha is its picture's)"""
    for o in meshes:
        for slot in o.material_slots:
            material = slot.material
            if material is None or not material.use_nodes:
                continue
            bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if bsdf is not None and bsdf.inputs['Alpha'].links:
                material.surface_render_method = 'DITHERED'


piece_sources = {}


def piece(key, name, keep=None, drop=None, height=None, length=None, turn=0.0, budget=None, grounded=True):
    """
    A Sketchfab model (or the meshes of it kept) as one mesh in the library, to copy from, or None when it was not
    downloaded: its materials its own (named <key>_...), turned about the vertical (degrees: its front to Blender's -y),
    scaled to a height or a length along its longest side, brought down to a triangle budget, the middle of its footprint
    at the origin and its lowest point at z 0 (or, not grounded, its own z 0 kept: a guardrail's posts reach into the
    ground)
    """
    path = MODELS.get(key)
    if not path:
        if key not in skipped:
            skipped.append(key)
        return None
    meshes = import_meshes(name, path, keep, drop)
    own_materials(meshes, key)
    cut_out_alpha(meshes)
    o = join(meshes, name)
    if o.data.shape_keys:
        o.shape_key_clear()
    o.data.transform(Matrix.Rotation(math.radians(turn + TURNS.get(key, 0.0)), 4, 'Z'))
    lo, hi = bounds(o)
    if height:
        o.data.transform(Matrix.Scale(height / (hi.z - lo.z), 4))
    elif length:
        o.data.transform(Matrix.Scale(length / max(hi.x - lo.x, hi.y - lo.y), 4))
    for _ in range(3):
        if not budget or triangles(o) <= budget * 1.15:
            break
        decimate(o, budget / triangles(o))
    lo, hi = bounds(o)
    o.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z if grounded else 0.0)))
    shelve(o)
    dedupe()
    lo, hi = bounds(o)
    piece_sources[name] = [key]
    print(f"PIECE {name}: {triangles(o)} triangles, {tuple(round(v, 2) for v in (hi - lo))} m")
    return o


def put(p, x, y, z=0.0, heading=0.0, scale=1.0, name=None):
    """A copy of a library piece at Blender's (x, y), z up, turned by heading (the game's degrees: 0 faces the road, south)"""
    if p is None:
        return None
    o = p.copy()
    o.matrix_world = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(heading), 4, 'Z') @ Matrix.Scale(scale, 4)
    o.name = name or f"{p.name}_at_{len(spot.objects)}"
    spot.objects.link(o)
    return o


def fit_car(o, length):
    """A car scaled to its length and turned to face Blender's -y, its paint no more than a little metallic"""
    for slot in o.material_slots:
        material = slot.material
        if material is None or not material.use_nodes:
            continue
        bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is not None and not bsdf.inputs['Metallic'].links and bsdf.inputs['Metallic'].default_value > 0.35:
            colour = bsdf.inputs['Base Color'].default_value
            if bsdf.inputs['Base Color'].links or max(colour[:3]) < 0.7:
                bsdf.inputs['Metallic'].default_value = 0.35
    lo, hi = bounds(o)
    o.data.transform(Matrix.Scale(length / max(hi.x - lo.x, hi.y - lo.y), 4))
    lo, hi = bounds(o)
    o.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))


# ---- The ground: the ridge, the hill rising behind the road, the slope falling to the valley past the rail ----

def ground_height(x, y):
    if y < -ROAD_HALF - 3.0:
        # Up the hill across the road, steepening, levelling off high up
        d = -y - ROAD_HALF - 3.0
        return min(55.0, 0.18 * d + 0.006 * d * d)
    if y > RAIL_Y + 1.0:
        # Down to the valley past the rail: a steep bank, then the long slope
        d = y - RAIL_Y - 1.0
        return -min(160.0, 0.9 * d + 0.002 * d * d)
    return 0.0


ground_mat = textured("lot_ground", "forest_ground_04_diffuse", tint=(0.12, 0.13, 0.09))
xs = [-260.0 + 8.0 * k for k in range(66)]
ys = sorted(set([-160.0 + 6.0 * k for k in range(28)] + [-ROAD_HALF - 3.0, -ROAD_HALF - 1.0, RAIL_Y + 1.0, RAIL_Y + 3.0]
                + [RAIL_Y + 1.0 + 6.0 * k for k in range(1, 46)]))
vertices = [(x, y, ground_height(x, y) - 0.03) for y in ys for x in xs]
faces, uvs = [], []
n = len(xs)
for j in range(len(ys) - 1):
    for i in range(n - 1):
        a = j * n + i
        corners = (a, a + 1, a + n + 1, a + n)
        faces.append(corners)
        uvs.append([(vertices[c][0] / 6.0, vertices[c][1] / 6.0) for c in corners])
mesh_object("ground", vertices, faces, uvs, [ground_mat], [0] * len(faces))

# The pass's road along the ridge, its gravel shoulders, its lines (white at the edges, yellow down the middle: no passing)
road = textured("lot_road", "worn_asphalt_diffuse", tint=(0.2, 0.2, 0.2))
gravel = textured("lot_gravel", "gravel_road_diffuse", tint=(0.3, 0.28, 0.25))
white = flat("lot_paint", (0.8, 0.8, 0.78), rough=0.7)
yellow = flat("lot_paint_yellow", (0.85, 0.62, 0.1), rough=0.7)
quad("road", [(-260, -ROAD_HALF, 0.0), (260, -ROAD_HALF, 0.0), (260, ROAD_HALF, 0.0), (-260, ROAD_HALF, 0.0)], road, 8.0)
quad("shoulder", [(-260, -ROAD_HALF - 1.6, -0.01), (260, -ROAD_HALF - 1.6, -0.01), (260, -ROAD_HALF, -0.01), (-260, -ROAD_HALF, -0.01)], gravel, 4.0)
for name, a, b in (("shoulder", (-260, LOT_WEST - 1.0), (LOT_WEST - 1.0, LOT_EAST + 1.0)), ("shoulder", (LOT_EAST + 1.0, 260), None)):
    x0, x1 = a if b is None else (a[0], a[1])
    quad(name, [(x0, ROAD_HALF, -0.01), (x1, ROAD_HALF, -0.01), (x1, ROAD_HALF + 1.6, -0.01), (x0, ROAD_HALF + 1.6, -0.01)], gravel, 4.0)
for side in (-1, 1):
    box("lot_line", (520.0, 0.15, 0.01), (0.0, side * (ROAD_HALF - 0.25), 0.005), white)
box("lot_line", (520.0, 0.15, 0.01), (0.0, 0.0, 0.005), yellow)

# The car park: asphalt from the road's edge to the rail, its bays' lines along the rail, the lane's arrows left out
lot = textured("lot_asphalt", "asphalt_02_diffuse", tint=(0.22, 0.22, 0.22))
quad("lot", [(LOT_WEST, ROAD_HALF, 0.01), (LOT_EAST, ROAD_HALF, 0.01), (LOT_EAST, LOT_NORTH + 1.0, 0.01), (LOT_WEST, LOT_NORTH + 1.0, 0.01)], lot, 6.0)
for bx in [b - 1.5 for b in BAYS] + [BAYS[-1] + 1.5]:
    box("lot_line", (0.12, 5.0, 0.01), (bx, BAY_Y, 0.02), white)
box("lot_line", (BAYS[-1] - BAYS[0] + 3.0, 0.12, 0.01), ((BAYS[0] + BAYS[-1]) / 2, BAY_Y - 2.5, 0.02), white)

# The rail's concrete curb under it, and the bank's edge
moss = textured("lot_curb", "concrete_moss_diffuse", tint=(0.45, 0.45, 0.42))
box("lot_curb", (LOT_EAST - LOT_WEST + 20.0, 0.5, 0.2), (0.0, RAIL_Y, 0.1), moss, tile=2.0)

# ---- What stands in the car park ----
# The guardrail (our own): a white W-beam on posts every two metres, its ends bent down into the curb
rail_paint = flat("rail_paint", (0.78, 0.79, 0.78), rough=0.45, metal=0.3)
RAIL_WEST, RAIL_EAST = LOT_WEST - 10.0, LOT_EAST + 8.0
box("rail_beam", (RAIL_EAST - RAIL_WEST, 0.06, 0.32), ((RAIL_WEST + RAIL_EAST) / 2, RAIL_Y - 0.12, 0.2 + 0.58), rail_paint)
box("rail_beam", (RAIL_EAST - RAIL_WEST, 0.04, 0.08), ((RAIL_WEST + RAIL_EAST) / 2, RAIL_Y - 0.17, 0.2 + 0.58), rail_paint)
px = RAIL_WEST
while px <= RAIL_EAST + 0.01:
    box("rail_post", (0.12, 0.12, 0.85), (px, RAIL_Y, 0.2 + 0.425), rail_paint)
    px += 2.0

# The wheel stops (our own): a concrete block across the back of each bay, a yellow one here and there
stop_grey = flat("wheel_stop", (0.42, 0.42, 0.4), rough=0.85)
stop_yellow = flat("wheel_stop_yellow", (0.75, 0.6, 0.12), rough=0.7)
for k, bx in enumerate(BAYS):
    box("wheel_stop", (1.7, 0.2, 0.12), (bx, BAY_Y + 1.7, 0.07), stop_yellow if k % 5 == 2 else stop_grey)

# The lamps round the lot: the sodium arm (its light at the arm's end found from its "light" material)
lamp = piece("lamps", "lamp", keep=[r"^polySurface60_"], height=8.0, budget=1500)
LAMPS = []


def lamp_light(o):
    """Where an object's lamp is: the middle of its faces of a material named ...light (Blender's space)"""
    points = []
    for poly in o.data.polygons:
        material = o.data.materials[poly.material_index] if o.data.materials else None
        if material is not None and material.name.lower().endswith("light"):
            points += [o.matrix_world @ o.data.vertices[i].co for i in poly.vertices]
    return sum(points, Vector()) / len(points) if points else None


for lx, ly, heading in ((-24.0, LOT_NORTH - 0.6, 0.0), (0.0, LOT_NORTH - 0.6, 0.0), (24.0, LOT_NORTH - 0.6, 0.0),
                        (-24.0, ROAD_HALF + 1.4, 180.0), (24.0, ROAD_HALF + 1.4, 180.0), (-70.0, -ROAD_HALF - 1.0, 0.0),
                        (70.0, -ROAD_HALF - 1.0, 0.0)):
    o = put(lamp, lx, ly, 0.0, heading=heading)
    if o is not None:
        bpy.context.view_layer.update()
        at = lamp_light(o)
        if at is not None:
            LAMPS.append([round(at.x, 2), round(at.z - 0.3, 2), round(-at.y, 2)])

# The vending machines in a row on the east side under a lean-to, facing the lot (west), the kiosk beyond them
VEND_X = LOT_EAST + 1.2
jpn = piece("vending_jpn", "vending_jpn", height=1.83, budget=6000, turn=0.0)
pair_a = piece("vending_pair", "vending_pair_a", keep=[r"^Vending_machine_1"], height=1.83, budget=4000, turn=-90.0)
pair_b = piece("vending_pair", "vending_pair_b", keep=[r"^Vending_machine_2"], height=1.83, budget=4000, turn=-90.0)
traffic_vending = piece("traffic", "vending_blue", keep=[r"^vending_machine_"], height=1.83, budget=3000)
vy = 13.0
for p in (jpn, pair_a, traffic_vending, pair_b):
    if p is not None:
        put(p, VEND_X, vy, 0.0, heading=-90.0)
        vy += 1.15
iron = textured("leanto_roof", "corrugated_iron_diffuse", rough=0.5, tint=(0.5, 0.52, 0.55))
steel = flat("leanto_post", (0.35, 0.36, 0.38), rough=0.5, metal=0.6)
box("leanto_roof", (2.4, vy - 13.0 + 1.6, 0.06), (VEND_X + 0.2, (13.0 + vy) / 2 - 0.6, 2.7), iron, tile=2.0)
for py in (12.0, vy - 0.4):
    box("leanto_post", (0.08, 0.08, 2.7), (VEND_X - 0.9, py, 1.35), steel)
bin_ = piece("traffic", "recycling_bin", keep=[r"^street_bin_"], height=0.95, budget=800)
put(bin_, VEND_X - 0.2, vy + 0.4, 0.0, heading=-90.0)

kiosk = piece("kiosk", "kiosk", height=3.9, budget=6200, turn=0.0)
put(kiosk, LOT_EAST + 4.5, 22.5, 0.0, heading=-90.0)

# The payphone on a post of our own by the vending machines
phone = piece("payphone", "payphone", height=0.4, budget=1200)
box("phone_post", (0.12, 0.12, 1.15), (VEND_X - 0.4, 11.0, 0.575), steel)
put(phone, VEND_X - 0.4, 11.0, 1.15, heading=-90.0)

# The rest house on the west side (our own): white concrete, a tiled roof, a dark door either end
wall = textured("rest_wall", "concrete_wall_001_diffuse", tint=(0.75, 0.74, 0.7))
roof = textured("rest_roof", "grey_roof_tiles_diffuse", tint=(0.3, 0.3, 0.32))
door = flat("rest_door", (0.06, 0.07, 0.08), rough=0.4)
REST = (LOT_WEST - 6.0, 15.0)
box("rest_wall", (6.0, 9.0, 3.0), (REST[0], REST[1], 1.5), wall, tile=2.5)
box("rest_roof", (7.2, 10.2, 0.25), (REST[0], REST[1], 3.12), roof, tile=2.0)
for dy in (-2.5, 2.5):
    box("rest_door", (0.06, 1.0, 2.1), (REST[0] + 3.02, REST[1] + dy, 1.05), door)
box("rest_window", (0.06, 2.0, 0.5), (REST[0] + 3.02, REST[1], 2.3), flat("rest_glass", (0.55, 0.6, 0.55), rough=0.2))
bench = piece("bench", "bench", height=2.6, budget=2200, turn=0.0)
put(bench, LOT_WEST - 1.6, 26.0, 0.0, heading=90.0)
trash = piece("trash_can", "trash_can", height=1.1, budget=650)
put(trash, LOT_WEST - 1.4, 21.0, 0.0, heading=90.0)

# The shrine corner, north-west by the rail: a torii, the hokora behind it, stone lanterns either side
torii = piece("torii", "torii", height=4.2, budget=1300, turn=90.0)
hokora = piece("hokora", "hokora", keep=[r"^Hokora 01"], height=1.7, budget=400)
toro = piece("toro", "toro", height=2.0, budget=1800)
SHRINE = (LOT_WEST - 6.0, LOT_NORTH - 4.0)
put(torii, SHRINE[0], SHRINE[1] - 3.0, 0.0, heading=90.0)
put(hokora, SHRINE[0] - 2.5, SHRINE[1] - 3.0, 0.0, heading=90.0)
put(toro, SHRINE[0] + 2.0, SHRINE[1] - 5.6, 0.0, heading=90.0)
put(toro, SHRINE[0] + 2.0, SHRINE[1] - 0.4, 0.0, heading=90.0)
box("shrine_step", (5.5, 6.0, 0.15), (SHRINE[0], SHRINE[1] - 3.0, 0.075), moss, tile=2.0)

# Cones by the kiosk; road signs along the pass either side of the car park
cone = piece("cone", "cone", height=0.7, budget=250)
for cx, cy in ((LOT_EAST + 2.2, 28.0), (LOT_EAST + 2.6, 29.0), (LOT_EAST + 1.8, 30.2)):
    put(cone, cx, cy, 0.0, heading=rng.uniform(0, 360))
sign_round = piece("road_signs", "sign_round", keep=[r"^Cylinder\.001_", r"^Cylinder_Material"], height=3.4, budget=200)
sign_plate = piece("road_signs", "sign_plate", keep=[r"^Plane\.001_", r"^Cylinder\.002_"], height=3.4, budget=200)
put(sign_round, -48.0, -ROAD_HALF - 1.0, 0.0, heading=90.0)
put(sign_plate, 52.0, ROAD_HALF + 1.2, 0.0, heading=-90.0)
put(sign_round, 90.0, ROAD_HALF + 1.2, 0.0, heading=-90.0)

# A house down the road across the way, in the trees
house = piece("house", "house", height=7.0, budget=2400)
put(house, -78.0, -22.0, ground_height(-78.0, -22.0) - 0.5, heading=0.0)

# ---- The parked cars: a few that are not the racers', by the kiosk and the rest house ----
CARS = {
    "kiri_86": ((LOT_WEST + 3.0, 9.0), 90.0, 4.3),
    "lct_3000": ((LOT_EAST - 3.5, 9.5), 0.0, 6.0),
    "jdm_sport_99": ((LOT_WEST + 3.0, 12.5), 90.0, 4.6),
    "tiara_gt_83": ((LOT_EAST - 6.0, 16.0), -90.0, 4.2),
    "kiri_94": ((LOT_WEST + 3.0, 22.0), 90.0, 4.5),
    "tozzo_98": ((LOT_EAST - 6.0, 20.0), -90.0, 4.5),
    "nfr_90": ((-60.0, ROAD_HALF + 2.6), 90.0, 4.4),
    "lolita_91": ((LOT_WEST + 3.0, 25.5), 90.0, 3.9),
}
cars_used = []
for key, ((cx, cy), heading, length) in CARS.items():
    if key not in MODELS:
        skipped.append(key)
        continue
    try:
        car = piece(key, key, budget=9000)
        fit_car(car, length)
        put(car, cx, cy, 0.0, heading=heading, name=f"CAR_{key}")
        cars_used.append(key)
    except RuntimeError as ex:
        print(f"CAR {key} left out: {ex}")

# ---- The pines: whole trees (their LOD2) round the lot, their billboards up the hill and down the slope ----
TREE_KINDS = ["big_1", "big_2", "big_3", "large_1", "large_2", "large_3", "medium_1", "medium_2", "medium_3"]
trees = {}
cards = {}
for kind in TREE_KINDS:
    trees[kind] = piece("pines", f"pine_{kind}", keep=[rf"^Pine_{kind}_LOD2_"], budget=None)
    cards[kind] = piece("pines", f"pine_card_{kind}", keep=[rf"^Pine_{kind}_Billboard_LOD3"], budget=None)
# The large pines' whole trees are twice the others' triangles: only their billboards stand
whole_kinds = [k for k in TREE_KINDS if trees[k] is not None and not k.startswith("large")]
card_kinds = [k for k in TREE_KINDS if cards[k] is not None]
placed_trees = 0


def clear_of(x, y):
    """Not on the road, its shoulders, the car park, the buildings or the shrine"""
    if -ROAD_HALF - 2.5 < y < ROAD_HALF + 2.5:
        return False
    if LOT_WEST - 12.0 < x < LOT_EAST + 9.0 and ROAD_HALF < y < RAIL_Y + 1.5:
        return False
    return True


if whole_kinds:
    # Close round the lot: west and east of it, along the road across the way
    for _ in range(90):
        side = rng.random()
        if side < 0.3:
            x, y = rng.uniform(LOT_WEST - 34.0, LOT_WEST - 12.0), rng.uniform(ROAD_HALF + 4.0, RAIL_Y + 4.0)
        elif side < 0.6:
            x, y = rng.uniform(LOT_EAST + 9.0, LOT_EAST + 30.0), rng.uniform(ROAD_HALF + 4.0, RAIL_Y + 4.0)
        else:
            x, y = rng.uniform(-80.0, 80.0), rng.uniform(-ROAD_HALF - 26.0, -ROAD_HALF - 3.5)
        if clear_of(x, y):
            put(trees[rng.choice(whole_kinds)], x, y, ground_height(x, y) - 0.1, heading=rng.uniform(0, 360),
                scale=rng.uniform(0.85, 1.25))
            placed_trees += 1
if card_kinds:
    # The forest beyond: up the hill across the road, either side along the ridge, down the slope below the rail
    for _ in range(1600):
        x = rng.uniform(-250.0, 250.0)
        y = rng.uniform(-150.0, 230.0)
        near = abs(x) < 75.0 and -ROAD_HALF - 22.0 < y < RAIL_Y + 6.0
        # The valley's view kept open in front of the lot: only low on the slope there
        view = abs(x) < 120.0 and RAIL_Y < y < RAIL_Y + 60.0
        if near or view or not clear_of(x, y):
            continue
        put(cards[rng.choice(card_kinds)], x, y, ground_height(x, y) - 0.2, heading=rng.uniform(0, 360),
            scale=rng.uniform(0.9, 1.5))
        placed_trees += 1
print(f"TREES {placed_trees}")

# Out of the library, the pieces' meshes stay with their copies
for o in list(library.objects):
    bpy.data.objects.remove(o)
bpy.data.collections.remove(library)

total = sum(triangles(o) for o in spot.objects if o.type == 'MESH')
print(f"SPOT {len(spot.objects)} objects, {total} triangles")
print(f"LEFT OUT (not downloaded): {', '.join(sorted(set(skipped))) if skipped else 'nothing'}")
with open(os.path.join(os.path.dirname(OUT), "spot_built.json"), "w", encoding="utf-8") as fh:
    json.dump({"lamps": LAMPS, "cars": cars_used, "skipped": sorted(set(skipped)), "triangles": total,
               "player": [PLAYER[0], -PLAYER[1]], "bays": [[bx, -BAY_Y] for bx in BAYS]}, fh, indent=1)


# ---- A few views of it, to look at the layout (the world lit by a night-ish sky and the lamps) ----
def render_views(folder):
    os.makedirs(folder, exist_ok=True)
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    world = bpy.data.worlds.new("world")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    sky = spec.get("sky_night")
    if sky and os.path.isfile(sky):
        env = world.node_tree.nodes.new('ShaderNodeTexEnvironment')
        env.image = bpy.data.images.load(sky)
        world.node_tree.links.new(env.outputs['Color'], bg.inputs['Color'])
    bg.inputs['Strength'].default_value = 1.4
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
    sun.data.energy = 1.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    scene.collection.objects.link(sun)
    for k, (x, z, y) in enumerate((l[0], l[1], -l[2]) for l in LAMPS):
        light = bpy.data.objects.new(f"lamp{k}", bpy.data.lights.new(f"lamp{k}", 'POINT'))
        light.data.energy = 2500
        light.data.color = (1.0, 0.75, 0.45)
        light.location = (x, y, z)
        scene.collection.objects.link(light)
    camera = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
    camera.data.lens = 24
    scene.collection.objects.link(camera)
    scene.camera = camera
    views = {
        # From the player's seat, looking out of the left window at the bays and the valley
        "player": ((PLAYER[0], PLAYER[1] + 0.4, 1.15), (PLAYER[0] + 0.5, BAY_Y, 1.4)),
        # Over the lot from the road's far side
        "overview": ((0.0, -30.0, 18.0), (0.0, 18.0, 0.0)),
        "east": ((-35.0, 8.0, 4.0), (30.0, 18.0, 1.5)),
        "west": ((25.0, 12.0, 4.0), (-36.0, 22.0, 1.5)),
        "valley": ((0.0, 25.0, 2.0), (0.0, 200.0, -20.0)),
    }
    for name, (eye, target) in views.items():
        camera.location = eye
        direction = Vector(target) - Vector(eye)
        camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(folder, f"{name}.png")
        bpy.ops.render.render(write_still=True)


if RENDERS:
    render_views(RENDERS)
    # The views' lights and camera go: the .blend is what the KN5 is made of
    for o in list(scene.collection.objects):
        bpy.data.objects.remove(o)

bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=OUT)
