"""
Lays out the diner's street in Blender and saves it as a .blend for prepare_prop.py (then DinerSceneBuilder) to make the
game's KN5 of (build_street.py runs it all):

  blender -b --python compose_street.py -- <spec.json>

The spec (build_street.py writes it): kit (the Quaternius Downtown City MegaKit's glTF folder, CC0), diner (WaajaaH's
diner: fbx, its .fbx, and textures, its pictures' folder), signs (prepare_pictures.py's folder), sketchfab (the
unzipped Sketchfab models by id, each its .gltf; see build_street.py's MODELS: which are there decides what stands in
the street), out (the .blend to save).

Los Angeles, late 1960s: a corner on Main Street. The diner stands back on the north-east corner of Main Street and a
cross street, its long glass side to Main Street and its door to the cross street, its lot wrapped round both: the
racers' cars in a row along its front and a few along its side. The player parks at the curb across Main Street,
facing east, and looks over at it out of the side window, as in Street Rod. Low stucco and brick shops line both
streets downtown, houses on their lawns beyond; palms along the sidewalks, cobra-head lights, telephone poles and
their wires, a signal on each corner, hydrants, meters, news racks, a mailbox, a bus bench, the cars of the
neighbourhood at the curbs, a Googie pylon at the lot's corner, a motel's star sign and a billboard across the way.

Blender's space: x along Main Street (east), y north (the diner's side), z up; metres; the road's surface at z = 0.
The game's (AC's) space is x the same, y up, z = -y (the diner towards -z): street.json's and diner.json's positions
are the game's. A heading below is the game's (0 facing its +z, Blender's -y; 90 facing east).

Every Sketchfab model is optional: one not downloaded is left out (its place stays empty), and the build says so.
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
KIT, SIGNS, OUT = spec["kit"], spec["signs"], spec["out"]
DINER_FBX, DINER_TEXTURES = spec["diner"]["fbx"], spec["diner"]["textures"]
MODELS = spec.get("sketchfab", {})

rng = random.Random(1968)

# The kit's road sits 0.15 under its sidewalks; everything goes up by as much, so the road is at 0
LIFT = 0.15
CURB = 0.15  # the sidewalks' and the lot's top

# The diner's model is a tenth-scale file: its mannequin (1.8 m) stands 0.133 high
DINER_SCALE = 13.5

# ---- The layout (Blender's x, y) ----
STREET_WEST, STREET_EAST = -165.0, 165.0       # Main Street's ends
CROSS_X = -36.0                                # the cross street's middle
CROSS_SOUTH, CROSS_NORTH = -150.0, 150.0       # its ends
SIDEWALK = 9.0                                 # the back of each sidewalk, from the middle of a road
JUNCTION = 12.33                               # the kit's crossing reaches this far from its middle every way
LOT_WEST, LOT_EAST = CROSS_X + SIDEWALK, 20.0  # the diner's lot, from the cross street's sidewalk to its neighbour
LOT_NORTH = 50.0
DINER_WEST, DINER_FRONT = -15.0, 21.0          # the diner's overhangs: west (its door's side) and south (its front)
FRONT_BAYS = [-13.5 + 3.0 * k for k in range(9)]  # the racers' bays along the front, their middles (x), 5 m deep
FRONT_BAY_Y = DINER_FRONT - 3.0
SIDE_BAYS = [24.5 + 3.0 * k for k in range(4)]    # and along the side (y), facing the diner's west wall
SIDE_BAY_X = DINER_WEST - 3.0
PLAYER = (-2.0, -4.5)                          # where the player's car stands at the curb, facing east
CRUISE = (35.0, 4.5)                           # where the Cruise screen parks it (street.json), facing west
LANE = 4.5                                     # the parking lanes' middle, either side of a road's
DOWNTOWN_WEST, DOWNTOWN_EAST = -112.0, 112.0   # the shops; houses beyond

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
library = bpy.data.collections.new("library")
street = bpy.data.collections.new("street")
scene.collection.children.link(street)
skipped = []


def same_name(name):
    """A material's or picture's name without the .001 Blender adds to the second of a name"""
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
    """Each object where its parents put it, so they can go"""
    for o in objects:
        world = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = world


def join(meshes, name):
    """The meshes as one object, its transforms in its vertices, still in the scene"""
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
    """An object out of the scene into the library, to copy from"""
    for c in list(piece.users_collection):
        c.objects.unlink(piece)
    library.objects.link(piece)


def triangles(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def bounds(o):
    """The corners of the box round an object's vertices (its cached bound_box lags behind a change to its mesh)"""
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


def decimate(o, ratio=None, planar=False):
    m = o.modifiers.new("decimate", 'DECIMATE')
    if planar:
        m.decimate_type = 'DISSOLVE'
        m.angle_limit = math.radians(1.0)
        m.use_dissolve_boundaries = False
        m.delimit = {'MATERIAL', 'UV'}
    else:
        m.ratio = ratio
        m.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def names_of(o):
    """An object's name and its parents', nearest first"""
    found = []
    while o is not None:
        found.append(o.name)
        o = o.parent
    return found


# ---- Materials and simple shapes of our own ----

def flat(name, colour, rough=0.6, metal=0.0):
    """A material of one colour (linear RGB)"""
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    return material


def pictured(name, path):
    """A material showing a picture, as a sign's board"""
    material = flat(name, (1.0, 1.0, 1.0), rough=0.55)
    tree = material.node_tree
    node = tree.nodes.new('ShaderNodeTexImage')
    node.image = bpy.data.images.load(path)
    tree.links.new(node.outputs['Color'], tree.nodes["Principled BSDF"].inputs['Base Color'])
    return material


def mesh_object(name, vertices, faces, uvs, materials, slots, x=0.0, y=0.0, z=0.0, turn=0.0):
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
    o.matrix_world = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(turn), 4, 'Z')
    street.objects.link(o)
    return o


def cuboid(size):
    """A box's corners round its middle and its six faces, outwards: front (-y), back (+y), then the sides"""
    w, d, h = (s / 2 for s in size)
    v = [(-w, -d, -h), (w, -d, -h), (w, -d, h), (-w, -d, h), (-w, d, -h), (w, d, -h), (w, d, h), (-w, d, h)]
    faces = [(0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0)]
    return v, faces


def box(name, size, middle, material, turn=0.0, tile=None):
    """A box of one material, its middle at a point; its picture every tile metres, or once a face"""
    v, faces = cuboid(size)
    w, d, h = size
    spans = [(w, h), (w, h), (d, h), (d, h), (w, d), (w, d)]
    uvs = [[(0, 0), (su / tile, 0), (su / tile, sv / tile), (0, sv / tile)] if tile else [(0, 0), (1, 0), (1, 1), (0, 1)] for su, sv in spans]
    return mesh_object(name, v, faces, uvs, [material], [0] * 6, *middle, turn)


def board(name, size, middle, picture, edge, turn=0.0):
    """A sign's board: the picture on its front and back, each reading the right way round, its edges of another material"""
    v, faces = cuboid(size)
    whole = [(0, 0), (1, 0), (1, 1), (0, 1)]
    return mesh_object(name, v, faces, [whole] * 6, [picture, edge], [0, 0, 1, 1, 1, 1], *middle, turn)


def flat_quad(name, x0, y0, x1, y1, z, material, tile):
    v = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
    return mesh_object(name, v, [(0, 1, 2, 3)], [[(p[0] / tile, p[1] / tile) for p in v]], [material], [0])


# ---- The kit's pieces ----

kit_pieces = {}


def load_kit(name):
    """A kit model as one mesh with its transforms applied, kept out of the scene to copy from"""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(KIT, name + ".gltf"))
    meshes = [o for o in set(bpy.data.objects) - before if o.type == 'MESH']
    unparent(meshes)
    for o in set(bpy.data.objects) - before:
        if o.type != 'MESH':
            bpy.data.objects.remove(o)
    piece = join(meshes, name)
    shelve(piece)
    dedupe()
    return piece


def kit(name, x, y, z=0.0, turn=0.0):
    """A copy of a kit model, turned about the vertical by degrees (0: as the kit has it)"""
    if name not in kit_pieces:
        kit_pieces[name] = load_kit(name)
    o = kit_pieces[name].copy()
    o.matrix_world = Matrix.Translation((x, y, z + LIFT)) @ Matrix.Rotation(math.radians(turn), 4, 'Z')
    street.objects.link(o)
    return o


# ---- The Sketchfab models ----

def import_meshes(name, path, keep=None, drop=None):
    """
    A model's meshes imported into the scene, unparented, the rest of the import gone: those matching keep, less those
    matching drop, regular expressions on a mesh's name or its parents'. Nothing kept raises, nothing of it left behind
    """
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


def own_materials(meshes, prefix):
    """
    The meshes' materials and pictures named after the model (build_street.py holds a model's pictures smaller by its
    prefix): two models naming a material "Body" are otherwise taken for one by dedupe, and the second wears the first's
    """
    for o in meshes:
        for slot in o.material_slots:
            material = slot.material
            if material is None:
                continue
            if not material.name.startswith(prefix + "_"):
                material.name = f"{prefix}_{same_name(material.name)}"
            if material.use_nodes:
                for node in material.node_tree.nodes:
                    if node.type == 'TEX_IMAGE' and node.image and not node.image.name.startswith(prefix + "_"):
                        node.image.name = f"{prefix}_{same_name(node.image.name)}"


def cut_out_alpha(meshes):
    """
    Leaves and the like, cut out by their picture's alpha, never blended: prepare_prop.py cuts a material whose alpha
    is its picture's. A blended material with no picture to cut it by keeps its blending: that is how prepare_prop.py
    tells glass (a car's windows, see-through) from a pane to draw
    """
    for o in meshes:
        for slot in o.material_slots:
            material = slot.material
            if material is None or not material.use_nodes:
                continue
            bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if bsdf is not None and bsdf.inputs['Alpha'].links:
                material.surface_render_method = 'DITHERED'


def piece(key, name, keep=None, drop=None, scale=1.0, height=None, length=None, turn=0.0, budget=None, foot=False,
          planar=False, path=None):
    """
    A Sketchfab model (or the meshes of it kept) as one mesh in the library, to copy from, or None when it was not
    downloaded: its materials its own (named <key>_...), scaled (by scale, or to a height or a length along its longest
    side), turned about the vertical (degrees), its flat faces merged (planar), brought down to a triangle budget,
    stood on its base (its lowest point at z 0) with the middle of its footprint at the origin, or the middle of its
    foot (foot: a lamp's pole, a sign's post, whatever reaches out over the street from it)
    """
    path = path or MODELS.get(key)
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
    o.data.transform(Matrix.Rotation(math.radians(turn), 4, 'Z') @ Matrix.Scale(scale, 4))
    lo, hi = bounds(o)
    if height:
        o.data.transform(Matrix.Scale(height / (hi.z - lo.z), 4))
    elif length:
        o.data.transform(Matrix.Scale(length / max(hi.x - lo.x, hi.y - lo.y), 4))
    if planar:
        decimate(o, planar=True)
    # Blender's decimation can stop well short of the ratio asked for, so it goes round again while over the budget
    for _ in range(3):
        if not budget or triangles(o) <= budget * 1.15:
            break
        decimate(o, budget / triangles(o))
    lo, hi = bounds(o)
    if foot:
        base = [v.co for v in o.data.vertices if v.co.z < lo.z + 0.3]
        middle = (sum(v.x for v in base) / len(base), sum(v.y for v in base) / len(base))
    else:
        middle = ((lo.x + hi.x) / 2, (lo.y + hi.y) / 2)
    o.data.transform(Matrix.Translation((-middle[0], -middle[1], -lo.z)))
    shelve(o)
    dedupe()
    lo, hi = bounds(o)
    print(f"PIECE {name}: {triangles(o)} triangles, {tuple(round(v, 2) for v in (hi - lo))} m")
    return o


def put(p, x, y, z=CURB, heading=0.0, scale=1.0, name=None):
    """A copy of a library piece at Blender's (x, y), z up, turned by heading (the game's degrees: 0 faces south)"""
    if p is None:
        return None
    o = p.copy()
    o.matrix_world = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(heading), 4, 'Z') @ Matrix.Scale(scale, 4)
    o.name = name or f"{p.name}_at_{len(street.objects)}"
    street.objects.link(o)
    return o


def width_of(p):
    lo, hi = bounds(p)
    return hi.x - lo.x


def depth_of(p):
    lo, hi = bounds(p)
    return hi.y - lo.y


# ---- The houses (our own): a house a lot beyond the shops, along both streets and across their ends ----

HOUSE_LOT = 16.0  # frontage
LOT_DEPTH = 28.0  # from the back of the sidewalk
FLOOR = 0.15 + 0.35  # the sidewalk's top, and a foundation

house_siding = [pictured(f"house_siding_{n}", os.path.join(SIGNS, f"siding_{n}.jpg")) for n in range(4)]
house_roof = pictured("house_roof", os.path.join(SIGNS, "roof.jpg"))
house_lawn = pictured("house_lawn", os.path.join(SIGNS, "lawn.jpg"))
house_hedge = pictured("house_hedge", os.path.join(SIGNS, "hedge.jpg"))
house_trim = flat("house_trim", (0.82, 0.82, 0.78), rough=0.5)
house_glass = flat("house_glass", (0.025, 0.03, 0.035), rough=0.08, metal=0.4)
house_door = flat("house_door", (0.22, 0.06, 0.04), rough=0.5)
house_concrete = flat("house_concrete", (0.42, 0.41, 0.39), rough=0.85)
house_chimney = flat("house_chimney", (0.24, 0.09, 0.06), rough=0.9)


class House:
    """A house's faces in its own space (x across its lot, y back from the sidewalk, its front facing -y, z up), by material"""

    def __init__(self):
        self.parts = {}

    def face(self, material, points, uvs):
        verts, faces, corners = self.parts.setdefault(material, ([], [], []))
        faces.append(tuple(range(len(verts), len(verts) + len(points))))
        verts.extend(points)
        corners.append(uvs)

    def box(self, material, middle, size, tile=None):
        """A box; its picture tiles every <tile> metres, or fills each face once"""
        (cx, cy, cz), (w, d, h) = middle, (s / 2 for s in size)
        v, faces = cuboid(size)
        spans = [(2 * w, 2 * h), (2 * w, 2 * h), (2 * d, 2 * h), (2 * d, 2 * h), (2 * w, 2 * d), (2 * w, 2 * d)]
        for face, (su, sv) in zip(faces, spans):
            u, vv = (su / tile, sv / tile) if tile else (1, 1)
            self.face(material, [(cx + v[i][0], cy + v[i][1], cz + v[i][2]) for i in face], [(0, 0), (u, 0), (u, vv), (0, vv)])

    def flat(self, material, x0, y0, x1, y1, z, tile):
        self.face(material, [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)],
                  [(0, 0), ((x1 - x0) / tile, 0), ((x1 - x0) / tile, (y1 - y0) / tile), (0, (y1 - y0) / tile)])

    def gable(self, wall, roof, middle, length, width, eave, rise, overhang, ridge_across):
        """
        A pitched roof over walls <length> along its ridge and <width> across, the ridge along x (or along y when
        ridge_across: a front gable), its ends filled with the walls' siding
        """
        cx, cy = middle
        a, b, o = length / 2, width / 2, overhang
        top = eave + rise

        def at(along, across, z):
            return (cx + across, cy + along, z) if ridge_across else (cx + along, cy + across, z)

        slope = math.hypot(b + o, rise * (b + o) / b)
        for side in (-1, 1):
            low_a, low_b = at(-a - o, side * (b + o), eave - rise * o / b), at(a + o, side * (b + o), eave - rise * o / b)
            high_a, high_b = at(-a - o, 0, top), at(a + o, 0, top)
            points = [low_a, low_b, high_b, high_a] if side < 0 else [low_b, low_a, high_a, high_b]
            if ridge_across:
                points = points[::-1]
            uvs = [(0, 0), ((length + 2 * o) / 3, 0), ((length + 2 * o) / 3, slope / 3), (0, slope / 3)]
            self.face(roof, points, uvs)
            self.face(house_trim, points[::-1], uvs)
        for end in (-1, 1):
            points = [at(end * a, -b, eave), at(end * a, b, eave), at(end * a, 0, top)]
            if (end > 0) != ridge_across:
                points = points[::-1]
            self.face(wall, points, [(0, 0), (2 * b / 2, 0), (b / 2, rise / 2)])

    def window(self, x, y, z, w, h, facing):
        """A window on a wall facing -y (front), +y (back), -x or +x: dark glass in a white frame, a little proud"""
        thick = 0.06
        if facing in ("front", "back"):
            out = -1 if facing == "front" else 1
            self.box(house_glass, (x, y + out * 0.03, z), (w, thick, h))
            for dx, dz, sw, sh in ((0, h / 2, w + 0.16, 0.08), (0, -h / 2, w + 0.2, 0.1), (-w / 2, 0, 0.08, h), (w / 2, 0, 0.08, h), (0, 0, 0.05, h)):
                self.box(house_trim, (x + dx, y + out * 0.06, z + dz), (sw, thick, sh))
        else:
            out = -1 if facing == "left" else 1
            self.box(house_glass, (x + out * 0.03, y, z), (thick, w, h))
            for dy, dz, sw, sh in ((0, h / 2, w + 0.16, 0.08), (0, -h / 2, w + 0.2, 0.1), (-w / 2, 0, 0.08, h), (w / 2, 0, 0.08, h), (0, 0, 0.05, h)):
                self.box(house_trim, (x + out * 0.06, y + dy, z + dz), (thick, sw, sh))

    def build(self, name, x, y, turn):
        for material, (verts, faces, uvs) in self.parts.items():
            mesh_object(f"{name}_{material.name}", verts, faces, uvs, [material], [0] * len(faces), x, y, 0.0, turn)


def ranch(h, rng, siding, left):
    """A one-storey ranch house, long side to the street, a picture window, the drive beside it"""
    width, depth, wall = rng.uniform(12.0, 14.0), rng.uniform(8.0, 9.5), 2.8
    setback = rng.uniform(7.0, 9.0)
    cx = (-1.6 if left else 1.6)
    cy = setback + depth / 2
    h.box(house_concrete, (cx, cy, 0.15 + 0.175), (width + 0.2, depth + 0.2, 0.35))
    h.box(siding, (cx, cy, FLOOR + wall / 2), (width, depth, wall), tile=2.0)
    h.gable(siding, house_roof, (cx, cy), width, depth, FLOOR + wall, rng.uniform(1.4, 1.9), 0.55, ridge_across=False)
    front = setback
    door = cx + (width * 0.18 if left else -width * 0.18)
    h.box(house_door, (door, front - 0.04, FLOOR + 1.05), (0.95, 0.08, 2.1))
    h.box(house_trim, (door, front - 0.06, FLOOR + 2.15), (1.15, 0.08, 0.1))
    big = cx + (-width * 0.22 if left else width * 0.22)
    h.window(big, front, FLOOR + 1.45, 2.6, 1.4, "front")
    for wx in (door + (1.6 if left else -1.6), door + (3.0 if left else -3.0)):
        if abs(wx - cx) < width / 2 - 0.8:
            h.window(wx, front, FLOOR + 1.6, 1.0, 1.1, "front")
    for side, sx in (("left", cx - width / 2), ("right", cx + width / 2)):
        h.window(sx, cy, FLOOR + 1.6, 1.1, 1.1, side)
    h.window(cx, front + depth, FLOOR + 1.6, 1.4, 1.1, "back")
    chimney = cx + (width / 2 - 1.0) * (1 if left else -1)
    h.box(house_chimney, (chimney, cy + 1.0, FLOOR + wall + 1.6), (0.7, 0.7, 3.2))
    return cx, width, front, door


def two_storey(h, rng, siding, left):
    """A two-storey house, its gable to the street, a porch across its front"""
    width, depth, wall = rng.uniform(8.0, 9.0), rng.uniform(9.0, 10.5), 5.6
    setback = rng.uniform(8.0, 9.5)
    cx = (-1.8 if left else 1.8)
    cy = setback + depth / 2
    h.box(house_concrete, (cx, cy, 0.15 + 0.175), (width + 0.2, depth + 0.2, 0.35))
    h.box(siding, (cx, cy, FLOOR + wall / 2), (width, depth, wall), tile=2.0)
    h.gable(siding, house_roof, (cx, cy), depth, width, FLOOR + wall, rng.uniform(2.6, 3.2), 0.45, ridge_across=True)
    front = setback
    # The porch: a deck, a roof on three posts
    h.box(house_trim, (cx, front - 1.25, FLOOR - 0.05), (width, 2.5, 0.12))
    h.box(house_roof, (cx, front - 1.3, FLOOR + 2.75), (width + 0.3, 2.8, 0.14), tile=3.0)
    for px in (cx - width / 2 + 0.2, cx, cx + width / 2 - 0.2):
        h.box(house_trim, (px, front - 2.4, FLOOR + 1.35), (0.16, 0.16, 2.7))
    door = cx + (1.6 if left else -1.6)
    h.box(house_door, (door, front - 0.04, FLOOR + 1.05), (0.95, 0.08, 2.1))
    h.window(cx + (-1.6 if left else 1.6), front, FLOOR + 1.5, 1.5, 1.3, "front")
    for wx in (cx - 1.8, cx + 1.8):
        h.window(wx, front, FLOOR + 4.2, 1.1, 1.2, "front")
    for side, sx in (("left", cx - width / 2), ("right", cx + width / 2)):
        for wy in (cy - 2.0, cy + 2.0):
            h.window(sx, wy, FLOOR + 1.5, 1.0, 1.2, side)
            h.window(sx, wy, FLOOR + 4.2, 1.0, 1.1, side)
    h.box(house_chimney, (cx + (width / 2 - 0.6) * (1 if left else -1), cy + 2.5, FLOOR + wall + 1.8), (0.7, 0.7, 3.6))
    return cx, width, front, door


def house(n, x, y, turn):
    """House n on its lot: the lot's front middle at (x, y) on the back of the sidewalk, facing the street (turn)"""
    rng_house = random.Random(1960 + n)
    h = House()
    siding = house_siding[n % len(house_siding)]
    left = rng_house.random() < 0.5
    cx, width, front, door = (two_storey if n % 3 == 1 else ranch)(h, rng_house, siding, left)

    # The lawn over the lot, the drive and the walk to the door on it, a hedge along the sidewalk
    half = HOUSE_LOT / 2
    h.flat(house_lawn, -half, 0.0, half, LOT_DEPTH, 0.16, 3.0)
    drive = (half - 2.0) * (1 if left else -1)
    h.flat(house_concrete, drive - 1.4, 0.0, drive + 1.4, front + 6.0, 0.175, 2.0)
    h.flat(house_concrete, door - 0.6, 0.0, door + 0.6, front - 0.1, 0.175, 2.0)
    for a, b in ((-half + 0.3, min(door, drive) - 1.6), (min(door, drive) + 1.6, max(door, drive) - 1.6), (max(door, drive) + 1.6, half - 0.3)):
        if b - a > 1.0:
            h.box(house_hedge, ((a + b) / 2, 0.6, 0.16 + 0.4), (b - a, 0.6, 0.8), tile=1.5)
    h.build(f"house_{n}", x, y, turn)


house_count = 0


def house_row(begin, end, line, facing):
    """
    Houses side by side from begin to end (along x for a row facing south or north, along y for one facing east or
    west), a lot each, their lots' fronts on the line
    """
    global house_count
    way = 1 if end > begin else -1
    at = begin + way * HOUSE_LOT / 2
    turn = {"south": 0.0, "north": 180.0, "west": -90.0, "east": 90.0}[facing]
    while (end - at) * way >= HOUSE_LOT / 2:
        x, y = (at, line) if facing in ("south", "north") else (line, at)
        house(house_count, x, y, turn)
        house_count += 1
        at += way * HOUSE_LOT


# ---- The streets ----

# Main Street, a 6 m piece at a time, either side of the crossing; the cross street the same, turned
x = CROSS_X + JUNCTION + 3
while x < STREET_EAST:
    kit("Street_4Lane", x, 0)
    x += 6
x = CROSS_X - JUNCTION - 3
while x > STREET_WEST:
    kit("Street_4Lane", x, 0)
    x -= 6
kit("Street_4WayIntersection", CROSS_X, 0)
for way in (1, -1):
    y = way * (JUNCTION + 3)
    while CROSS_SOUTH < y < CROSS_NORTH:
        kit("Street_4Lane", CROSS_X, y, turn=90)
        y += way * 6

# Under it all, the ground of the lots between the buildings, a little under the road, so no gap shows the void
dirt = flat("ground_dirt", (0.07, 0.065, 0.06), rough=0.9)
box("ground", (STREET_EAST - STREET_WEST + 120.0, CROSS_NORTH - CROSS_SOUTH + 120.0, 0.02),
    ((STREET_EAST + STREET_WEST) / 2, (CROSS_NORTH + CROSS_SOUTH) / 2, -0.03), dirt)

# The lot: asphalt a little over the sidewalk's height, wrapped round the diner from the corner to its east neighbour;
# its bays' lines along the diner's front and its side
ly = SIDEWALK + 3
while ly - 3 < LOT_NORTH:
    lx = LOT_WEST + 3
    while lx - 3 < LOT_EAST:
        kit("Street_Asphalt_6x6", lx, ly, z=CURB + 0.02)
        lx += 6
    ly += 6
paint = flat("lot_paint", (0.85, 0.85, 0.82), rough=0.7)
for bx in [b - 1.5 for b in FRONT_BAYS] + [FRONT_BAYS[-1] + 1.5]:
    box("bay_line", (0.12, 5.0, 0.01), (bx, FRONT_BAY_Y, CURB + 0.03), paint)
for by in [b - 1.5 for b in SIDE_BAYS] + [SIDE_BAYS[-1] + 1.5]:
    box("bay_line", (5.0, 0.12, 0.01), (SIDE_BAY_X, by, CURB + 0.03), paint)

# ---- The diner, turned a quarter so its long glass side faces Main Street and its door the cross street ----
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=DINER_FBX)
diner = [o for o in set(bpy.data.objects) - before
         if o.type == 'MESH' and "Mannequin" not in o.name and not o.name.startswith("Ground")]
unparent(diner)
for o in set(bpy.data.objects) - before:
    if o not in diner:
        bpy.data.objects.remove(o)
for m in bpy.data.materials:
    if m.name in ("Building", "Glass1", "Other") and m.use_nodes:
        part = m.name
        tree = m.node_tree
        bsdf = next(n for n in tree.nodes if n.type == 'BSDF_PRINCIPLED')
        for socket, suffix, colour in (("Base Color", "BaseCol" if part == "Building" else "BaseColor", True),
                                       ("Roughness", "Roughne" if part == "Building" else "Roughness", False),
                                       ("Metallic", "Metalli" if part == "Building" else "Metallic", False)):
            path = os.path.join(DINER_TEXTURES, f"Building_Exterior_Export5_{part}_{suffix}.png")
            if not os.path.exists(path):
                continue
            node = tree.nodes.new('ShaderNodeTexImage')
            node.image = bpy.data.images.load(path)
            if not colour:
                node.image.colorspace_settings.name = 'Non-Color'
            tree.links.new(node.outputs['Color'], bsdf.inputs[socket])
        # Its normal map is Unreal's, DirectX's as AC's
        path = os.path.join(DINER_TEXTURES, f"Building_Exterior_Export5_{part}_Normal.png")
        if os.path.exists(path):
            node = tree.nodes.new('ShaderNodeTexImage')
            node.image = bpy.data.images.load(path)
            node.image.colorspace_settings.name = 'Non-Color'
            bump = tree.nodes.new('ShaderNodeNormalMap')
            tree.links.new(node.outputs['Color'], bump.inputs['Color'])
            tree.links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
diner_mesh = join(diner, "Diner")
diner_mesh.data.transform(Matrix.Rotation(math.radians(-90.0), 4, 'Z') @ Matrix.Scale(DINER_SCALE, 4))
lo, hi = bounds(diner_mesh)
diner_mesh.data.transform(Matrix.Translation((DINER_WEST - lo.x, DINER_FRONT - lo.y, CURB + 0.02 - lo.z)))
DINER_EAST, DINER_BACK = bounds(diner_mesh)[1].x, bounds(diner_mesh)[1].y
print(f"DINER x {DINER_WEST}..{DINER_EAST:.1f}, y {DINER_FRONT}..{DINER_BACK:.1f}")

# The kit's normal maps are glTF's, OpenGL's: prepare_prop.py turns a picture named so over for AC
for image in bpy.data.images:
    if image.name.startswith("T_") and image.name.endswith("_Normal"):
        image.name += "_gl"

# DINER along the roof's front, on legs
steel = flat("sign_steel", (0.08, 0.08, 0.09), rough=0.35, metal=0.8)
roof_board = pictured("sign_roof", os.path.join(SIGNS, "sign_roof.jpg"))
roof_top = bounds(diner_mesh)[1].z
roof_x = (DINER_WEST + DINER_EAST) / 2 + 1.5
board("sign_roof", (10.0, 0.3, 2.5), (roof_x, DINER_FRONT + 5.0, roof_top + 0.6 + 1.25), roof_board, steel)
for lx in (-3.5, 3.5):
    box("sign_leg", (0.14, 0.14, 0.7), (roof_x + lx, DINER_FRONT + 5.1, roof_top + 0.3), steel)

# ---- The shops: low stucco and brick downtown, along both sides of both streets ----
mid_city = {}
for n in (1, 2, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15):
    p = piece("midcity", f"midcity_{n}", keep=[rf"^MidCity_Commercial_{n}$"], budget=None)
    if p is not None:
        mid_city[n] = p
# The corner buildings (8, a cut corner, and 14, a rounded one) stand on corners; the rest along the rows
ROW = [1, 2, 13, 6, 4, 10, 7, 5, 12, 15, 11]
harbor = piece("harbor_breakfast", "harbor_breakfast", scale=0.01,
               drop=[r"^ground_r8", r"^flooradd", r"^HallFloor", r"^firehy", r"^Mailbox", r"^mailbox", r"^Parkingmeter",
                     r"^trashcan", r"^streetlamp", r"^doorground", r"^storesign"], planar=True, budget=20000)
brick = piece("brick_shop", "brick_shop", budget=None)
stores = piece("rentable_stores", "rentable_stores", drop=[r"mossDecal"])


def shop_row(begin, end, front, facing_south, first=(), start=0):
    """
    Shops side by side from begin towards end along Main Street, their fronts on a line (facing south: the north side),
    as many as fit; the first ones given (pieces), then the row's in turn. Where it stopped
    """
    row = [p for p in first if p is not None] + [mid_city[ROW[(start + k) % len(ROW)]] for k in range(60) if ROW[(start + k) % len(ROW)] in mid_city]
    x, way = begin, 1 if end > begin else -1
    for p in row:
        w = width_of(p)
        if abs(end - x) < w:
            break
        middle = x + way * w / 2
        d = depth_of(p)
        if facing_south:
            put(p, middle, front + d / 2, heading=0.0)
        else:
            put(p, middle, front - d / 2, heading=180.0)
        x += way * (w + 0.4)
    return x


# North side: east of the lot the breakfast house, the brick block and shops; west of the cross street shops
east_north = shop_row(LOT_EAST + 0.5, DOWNTOWN_EAST, SIDEWALK, True, first=(harbor, brick))
west_north = shop_row(CROSS_X - SIDEWALK - 30.0, DOWNTOWN_WEST, SIDEWALK, True, start=3)
# South side: across from the diner the stores the player parks at, then shops either way; a vacant lot with the
# billboard on the corner across the cross street
east_south = shop_row(CROSS_X + SIDEWALK + 0.5, DOWNTOWN_EAST, -SIDEWALK, False, first=(stores, stores), start=5)
west_south = shop_row(CROSS_X - SIDEWALK - 18.0, DOWNTOWN_WEST, -SIDEWALK, False, start=7)
# The corners across the way: the rounded corner block on the north-west one, the cut corner on the south-east
if 14 in mid_city:
    p = mid_city[14]
    put(p, CROSS_X - SIDEWALK - width_of(p) / 2 - 0.5, SIDEWALK + depth_of(p) / 2, heading=0.0)
# Behind the diner, the backs of a row along the next street, seen over the lot
for k, n in enumerate((5, 1, 2)):
    if n in mid_city:
        p = mid_city[n]
        put(p, LOT_WEST + 8.0 + k * 18.0, LOT_NORTH + 4.0 + depth_of(p) / 2, heading=0.0)

# Houses beyond downtown on Main Street, along the cross street, and across both streets' ends
house_row(max(east_north, DOWNTOWN_EAST), STREET_EAST + 8, SIDEWALK, "south")
house_row(min(west_north, DOWNTOWN_WEST), STREET_WEST - 8, SIDEWALK, "south")
house_row(max(east_south, DOWNTOWN_EAST), STREET_EAST + 8, -SIDEWALK, "north")
house_row(min(west_south, DOWNTOWN_WEST), STREET_WEST - 8, -SIDEWALK, "north")
house_row(LOT_NORTH + 30.0, CROSS_NORTH + 8, CROSS_X + SIDEWALK, "west")
house_row(SIDEWALK + 32.0, CROSS_NORTH + 8, CROSS_X - SIDEWALK, "east")
house_row(-SIDEWALK - 22.0, CROSS_SOUTH - 8, CROSS_X + SIDEWALK, "west")
house_row(-SIDEWALK - 30.0, CROSS_SOUTH - 8, CROSS_X - SIDEWALK, "east")
for k, hy in enumerate(range(-40, 41, 16)):
    house(200 + k, STREET_WEST - 12, float(hy), 90.0)
    house(220 + k, STREET_EAST + 12, float(hy), -90.0)
for k, hx in enumerate(range(-64, -7, 16)):
    house(240 + k, float(hx), CROSS_NORTH + 12, 180.0)
    house(260 + k, float(hx), CROSS_SOUTH - 12, 0.0)

# ---- What stands on the sidewalks ----
palm_tall = piece("palm_tall", "palm_tall", height=17.0, budget=2500)
palm_fan = piece("palm_fan", "palm_fan", height=8.5, budget=4000)
washingtonia = piece("washingtonia", "washingtonia", height=19.0, budget=6000)
cobra = piece("cobra_head", "cobra_head", height=10.5, budget=1500, foot=True)
pole_lamp = piece("pole_lamp", "pole_lamp", height=11.0, budget=1500, foot=True)
pole = piece("telephone_pole", "telephone_pole", keep=[r"^Small_Pole1$"], height=11.5, budget=1200)
pole_b = piece("telephone_pole", "telephone_pole_b", keep=[r"^Small_Pole2$"], height=11.5, budget=1000)
signal = piece("signal", "signal", keep=[r"^4 sect signal horizontal$"], budget=2000, foot=True)
hydrant = piece("hydrant", "hydrant", height=0.75, budget=600)
trash_can = piece("trash_can", "trash_can", height=0.95, budget=600)
mailbox = piece("mailbox", "mailbox", height=1.3, budget=1800)
news_a = piece("news_rack", "news_rack_a", keep=[r"^group1$"], height=1.15, budget=1300)
news_b = piece("news_rack", "news_rack_b", keep=[r"^group2$"], height=1.15, budget=1300)
bench = piece("bus_bench", "bus_bench", drop=[r"(?i)sidewalk_base", r"(?i)drain"], budget=3000)
meter = piece("harbor_breakfast", "parking_meter", keep=[r"^Parkingmeter"], scale=0.01, height=1.45, budget=250)
diner_sign = piece("diner_sign", "diner_sign", height=8.5, budget=9000, foot=True)
star_sign = piece("star_sign", "star_sign", height=12.0, budget=7000, foot=True)
billboard = piece("billboard", "billboard", height=9.0, budget=4000)
# Its ad is today's: ours of the time goes on its face (prepare_pictures.py's billboard.jpg)
if billboard is not None:
    ad = pictured("billboard_ad", os.path.join(SIGNS, "billboard.jpg"))
    for slot in billboard.material_slots:
        if slot.material is not None and "material" in slot.material.name.lower() and "Main" not in slot.material.name:
            slot.material = ad


def is_clear(x, y, gaps):
    return not any(a <= x <= b for a, b in gaps)


# Driveways and corners the sidewalk things keep out of, along Main Street (x) and the cross street (y)
MAIN_GAPS = [(CROSS_X - JUNCTION - 2, CROSS_X + JUNCTION + 2), (LOT_WEST, LOT_EAST)]
CROSS_GAPS = [(-JUNCTION - 2, JUNCTION + 2), (SIDEWALK, LOT_NORTH)]

# Palms along both sides of Main Street and the cross street downtown, near the kerb; a few of them the big Washingtonia
palm_spots = []
for side in (-1, 1):
    for px in range(-108, 109, 24):
        x = px + (5 if side > 0 else -6)
        if is_clear(x, 0, MAIN_GAPS) and abs(x - PLAYER[0]) > 4:
            palm_spots.append((x, side * 7.2))
    for py in range(-100, 101, 30):
        y = py + (7 if side > 0 else -3)
        if is_clear(0, y, CROSS_GAPS):
            palm_spots.append((CROSS_X + side * 7.2, y))
for k, (x, y) in enumerate(palm_spots):
    tall = washingtonia if washingtonia is not None and k % 7 == 3 and abs(x) < 90 and abs(y) < 60 else palm_tall
    put(tall, x, y, heading=rng.uniform(0, 360), scale=rng.uniform(0.85, 1.15))
# Palms beside and behind the diner, seen either side of it from the curb, and fan palms in the lot's corner bed and beside the diner's door
for x, y in ((-21.0, 46.0), (13.0, 44.0), (19.0, 54.0), (-6.0, 60.0)):
    put(palm_tall if washingtonia is None else washingtonia, x, y, heading=rng.uniform(0, 360), scale=rng.uniform(0.95, 1.2))
bed = flat("lot_bed", (0.16, 0.11, 0.07), rough=0.95)
box("lot_bed", (5.0, 4.0, 0.3), (LOT_WEST + 3.2, SIDEWALK + 2.6, CURB + 0.15), bed)
put(palm_fan, LOT_WEST + 2.2, SIDEWALK + 4.0, CURB + 0.3, heading=40.0)
put(palm_fan, DINER_WEST - 1.2, DINER_BACK - 2.0, heading=200.0, scale=0.8)
put(diner_sign, LOT_WEST + 4.4, SIDEWALK + 1.6, CURB + 0.3, heading=45.0)

# Cobra-head lights along Main Street, the arm over the road (alternating sides); the cross street's on its poles
LAMPS = []
for k, lx in enumerate(range(-150, 151, 30)):
    side = 1 if k % 2 else -1
    x = lx + (8 if side > 0 else 0)
    if not is_clear(x, 0, MAIN_GAPS) or abs(x - PLAYER[0]) < 5:
        x += 9
    put(cobra, x, side * 7.6, heading=0.0 if side > 0 else 180.0)
    LAMPS.append((x, side * 4.8))
for k, ly in enumerate((-60, -28, 28, 70)):
    side = 1 if k % 2 else -1
    put(pole_lamp, CROSS_X + side * 7.6, ly, heading=-90.0 if side > 0 else 90.0)
    LAMPS.append((CROSS_X + side * 4.8, ly))
# A signal on each corner of the crossing, facing the traffic coming up to it
for sx, sy, heading in ((1, 1, 0.0), (-1, 1, 90.0), (-1, -1, 180.0), (1, -1, -90.0)):
    put(signal, CROSS_X + sx * 7.6, sy * 7.6, heading=heading)

# Telephone poles behind the south sidewalk and along the cross street's west side, wires between them
wire_material = flat("wire", (0.05, 0.05, 0.05), rough=0.7)


def wire(name, a, b, sag, width=0.03, segments=10):
    """A wire from a to b (Blender's points), hanging sag metres in its middle: two crossed strips, each with a back"""
    a, b = Vector(a), Vector(b)
    points = [a + (b - a) * (k / segments) - Vector((0, 0, sag * 4.0 * (k / segments) * (1 - k / segments))) for k in range(segments + 1)]
    along = (b - a).normalized()
    across = along.cross(Vector((0.0, 0.0, 1.0))).normalized() * (width / 2)
    up = Vector((0.0, 0.0, width / 2))
    vertices, faces, uvs, slots = [], [], [], []
    for k in range(segments):
        p, q = points[k], points[k + 1]
        for offset in (across, up):
            base = len(vertices)
            vertices.extend([tuple(p - offset), tuple(q - offset), tuple(q + offset), tuple(p + offset)])
            faces.append((base, base + 1, base + 2, base + 3))
            faces.append((base + 3, base + 2, base + 1, base))
            uvs.extend([[(0, 0), (1, 0), (1, 1), (0, 1)], [(0, 1), (1, 1), (1, 0), (0, 0)]])
            slots.extend([0, 0])
    return mesh_object(name, vertices, faces, uvs, [wire_material], slots)


def pole_line(points, along_x):
    """Poles at the points (Blender's x, y), their crossarms across the line, three wires from each to the next"""
    if pole is None:
        return
    lo, hi = bounds(pole)
    top = hi.z - 0.7
    arm = max(hi.x - lo.x, hi.y - lo.y) / 2 - 0.3
    for k, (x, y) in enumerate(points):
        put(pole if k % 3 else (pole_b or pole), x, y, heading=0.0 if along_x else 90.0)
    for k in range(len(points) - 1):
        (ax, ay), (bx, by) = points[k], points[k + 1]
        for n, d in enumerate((-arm, 0.0, arm)):
            off = (0.0, d) if along_x else (d, 0.0)
            wire(f"WIRE_{'x' if along_x else 'y'}_{k}_{n}", (ax + off[0], ay + off[1], CURB + top), (bx + off[0], by + off[1], CURB + top), sag=0.8)


pole_line([(float(px), -SIDEWALK + 0.6) for px in range(-160, 161, 32)], along_x=True)
pole_line([(CROSS_X - SIDEWALK + 0.6, float(py)) for py in range(-148, 149, 32) if abs(py) > JUNCTION + 4], along_x=False)

# Hydrants, cans, meters, the news racks and the mailbox on the corners, a bus bench
for x, y in ((LOT_WEST + 0.8, SIDEWALK - 1.0), (PLAYER[0] + 9.0, -SIDEWALK + 1.0), (62.0, SIDEWALK - 1.0), (CROSS_X - SIDEWALK + 1.0, -SIDEWALK - 4.0), (-88.0, -SIDEWALK + 1.0)):
    put(hydrant, x, y, heading=rng.uniform(0, 360))
for x, y in ((LOT_WEST + 7.0, SIDEWALK - 0.6), (CROSS_X + SIDEWALK - 0.6, -SIDEWALK - 1.5), (CROSS_X - SIDEWALK + 0.6, SIDEWALK + 1.5), (30.0, -SIDEWALK + 0.6), (LOT_EAST + 4.0, SIDEWALK - 0.6)):
    put(trash_can, x, y, heading=rng.uniform(0, 360))
put(mailbox, CROSS_X + SIDEWALK - 0.9, -SIDEWALK + 0.9, heading=135.0)
put(news_a, LOT_WEST + 5.2, SIDEWALK - 0.7, heading=180.0)
put(news_b, LOT_WEST + 6.0, SIDEWALK - 0.7, heading=180.0)
put(news_a, CROSS_X - SIDEWALK + 0.8, -SIDEWALK + 2.2, heading=90.0)
put(bench, 40.0, -SIDEWALK + 1.2, heading=180.0)
for mx in range(12, 100, 7):
    if abs(mx - 40) > 3:
        put(meter, float(mx), -6.6, heading=0.0)
for mx in range(int(LOT_EAST) + 4, 100, 7):
    if abs(mx - CRUISE[0]) > 4:
        put(meter, float(mx), 6.6, heading=180.0)

# Across the way: a motel's Googie star sign on the north-west corner, the billboard on the vacant south-west lot
put(star_sign, CROSS_X - SIDEWALK - 3.0, SIDEWALK + 1.8, heading=90.0)
put(billboard, CROSS_X - SIDEWALK - 9.0, -SIDEWALK - 8.0, heading=-135.0)

# ---- The neighbourhood's cars at the curbs: Main Street's parking lanes clear of the player's and the Cruise screen's
# spots and of where the player's car pulls in and out, and two along the diner's side on the cross street ----
CARS = {
    # id: where it stands (Blender's x, y), its heading (the game's), its length, the meshes to leave out
    "chrysler_saratoga": ((24.0, -LANE), 90.0, 5.6, []),
    "palomino": ((31.5, -LANE), 90.0, 4.8, []),
    "cadillac_75": ((52.0, -LANE), 90.0, 5.8, []),
    "phoenix": ((-62.0, -LANE), 90.0, 5.0, []),
    "sialia": ((-70.0, -LANE), 90.0, 4.7, []),
    "duntov": ((-104.0, -LANE), 90.0, 4.6, []),
    "chapman": ((-60.0, LANE), -90.0, 5.4, []),
    "bokaroo": ((-74.0, LANE), -90.0, 4.6, []),
    "chevelle": ((66.0, LANE), -90.0, 5.0, []),
    "sedan_1953": ((74.0, LANE), -90.0, 5.3, []),
    "truck_1970": ((96.0, LANE), -90.0, 5.4, []),
    "buick_1955": ((CROSS_X + LANE, 26.0), 180.0, 5.3, [r"^Text"]),
    "packard_1955": ((CROSS_X + LANE, 33.5), 180.0, 5.4, []),
    "taxi_1950s": ((CROSS_X + LANE, -30.0), 180.0, 5.3, []),
    "studebaker_1951": ((CROSS_X - LANE, 36.0), 0.0, 5.0, [r"_Floor_", r"^pPipe\d", r"^pCylinder22", r"pTorus"]),
    "chevrolet_c10_1963": ((CROSS_X - LANE, -26.0), 0.0, 5.2, []),
    "rancher_50": ((CROSS_X - LANE, -48.0), 0.0, 5.0, []),
}
CAR_BUDGET = 8000


def fit_car(o, length):
    """
    A car scaled to its length and turned to face the game's +z (Blender's -y); its paint no more than a little
    metallic (a Sketchfab export's paint is often all metal, which the game's pictures turn dark; chrome stays)
    """
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
    if (hi.x - lo.x) > (hi.y - lo.y):
        o.data.transform(Matrix.Rotation(math.radians(90.0), 4, 'Z'))
    o.data.transform(Matrix.Rotation(math.radians(TURNS.get(o.name, 0.0)), 4, 'Z'))
    lo, hi = bounds(o)
    o.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))


TURNS = spec.get("turns", {})
cars_used = []
for key, ((cx, cy), heading, length, drop) in CARS.items():
    if key not in MODELS:
        skipped.append(key)
        continue
    try:
        car = piece(key, key, drop=drop or None, budget=CAR_BUDGET)
        fit_car(car, length)
        put(car, cx, cy, 0.0, heading=heading, name=f"CAR_{key}")
        cars_used.append(key)
    except RuntimeError as ex:
        print(f"CAR {key} left out: {ex}")

# The library's pieces go: the copies in the scene keep their meshes, and prepare_prop.py exports what is in the scene
for o in list(library.objects):
    bpy.data.objects.remove(o)
bpy.data.collections.remove(library)

total = sum(triangles(o) for o in street.objects if o.type == 'MESH')
print(f"STREET {len(street.objects)} objects, {total} triangles")
print(f"LEFT OUT (not downloaded): {', '.join(sorted(set(skipped))) if skipped else 'nothing'}")
with open(os.path.join(os.path.dirname(OUT), "street_built.json"), "w", encoding="utf-8") as fh:
    json.dump({"lamps": LAMPS, "cars": cars_used, "skipped": sorted(set(skipped)),
               "diner": [DINER_WEST, DINER_EAST, DINER_FRONT, DINER_BACK]}, fh, indent=1)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=OUT)
