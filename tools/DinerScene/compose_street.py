"""
Lays out the diner's street in Blender and saves it as a .blend for prepare_prop.py (then DinerSceneBuilder) to make the
game's KN5 of (build_street.py runs it all):

  blender -b --python compose_street.py -- <spec.json>

The spec (build_street.py writes it): kit (the Quaternius Downtown City MegaKit's glTF folder, CC0), diner (WaajaaH's
diner: fbx, its .fbx, and textures, its pictures' folder), signs (prepare_pictures.py's folder), cards (the cards'
pictures and cards.json, bake_cards.py's: the lawns' trees, bushes and flowers, the far palms), sketchfab (the unzipped
Sketchfab models by id, each its .gltf; see build_street.py's MODELS: which are there decides what stands in the
street), out (the .blend to save).

Los Angeles, late 1960s: a corner on Main Street. The diner stands back on the north-east corner of Main Street and a
cross street, its long glass side to Main Street and its door to the cross street, its lot wrapped round both: the
racers' cars in a row along its front, just behind the sidewalk, and a few along its side. The player parks at the
curb across Main Street, facing east, and looks over at it and the racers' cars out of the side window, as in Street
Rod. Low stucco and brick shops line both streets downtown, ranch houses and two-storey ones beyond, each on its lawn
with a tree, bushes along its front and flowers; palms along the sidewalks (the model near the player, its card
further off, a scene's whole palms being a quarter of its triangles), cobra-head lights, telephone poles and
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
import numpy as np
from mathutils import Matrix, Vector

spec = json.load(open(sys.argv[sys.argv.index("--") + 1], encoding="utf-8"))
KIT, SIGNS, OUT = spec["kit"], spec["signs"], spec["out"]
CARDS_FOLDER = spec.get("cards")
DINER_FBX, DINER_TEXTURES = spec["diner"]["fbx"], spec["diner"]["textures"]
MODELS = spec.get("sketchfab", {})
# The kit's build (build_street.py --kit, step 39): the street splits into a stage (what the paths and the ground depend
# on, the diner, the looks) saved as OUT, and the pieces put on it, each once at the origin as an object PIECE_<id> saved
# in KIT_OUT, where each went written to the layout (street_built.json's "layout", the game's space)
KIT_OUT = spec.get("kit_out")
SPLIT = KIT_OUT is not None

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
LOT_NORTH = 44.0
# The diner's overhangs: west (its door's side) and south (its front), the front bays' depth back from the sidewalk so
# the racers' cars stand near the player's, across the street
DINER_WEST, DINER_FRONT = -15.0, 15.0
FRONT_BAYS = [-13.5 + 3.0 * k for k in range(9)]  # the racers' bays along the front, their middles (x), 5 m deep
FRONT_BAY_Y = DINER_FRONT - 3.0
SIDE_BAYS = [18.5 + 3.0 * k for k in range(4)]    # and along the side (y), facing the diner's west wall
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


front_door_x = 0.0


def piece(key, name, keep=None, drop=None, scale=1.0, height=None, length=None, turn=0.0, budget=None, foot=False,
          planar=False, path=None, front=None):
    """
    A Sketchfab model (or the meshes of it kept) as one mesh in the library, to copy from, or None when it was not
    downloaded: its materials its own (named <key>_...), scaled (by scale, or to a height or a length along its longest
    side), turned about the vertical (degrees), its flat faces merged (planar), brought down to a triangle budget,
    stood on its base (its lowest point at z 0) with the middle of its footprint at the origin, or the middle of its
    foot (foot: a lamp's pole, a sign's post, whatever reaches out over the street from it). front names meshes (a
    house's front door) whose side of the model is turned to face -y, the street; their middle across is left in
    front_door_x
    """
    global front_door_x
    path = path or MODELS.get(key)
    if not path:
        if key not in skipped:
            skipped.append(key)
        return None
    meshes = import_meshes(name, path, keep, drop)
    own_materials(meshes, key)
    cut_out_alpha(meshes)
    if front:
        everything = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
        marked = [o.matrix_world @ v.co for o in meshes if any(re.search(f, o.name) for f in front) for v in o.data.vertices]
        middle = sum(everything, Vector()) / len(everything)
        door = sum(marked, Vector()) / len(marked) - middle
        lo, hi = Vector([min(p[i] for p in everything) for i in range(3)]), Vector([max(p[i] for p in everything) for i in range(3)])
        # The side the door is nearest, by how far out it is against the model's half size that way
        if abs(door.x) / (hi.x - lo.x) > abs(door.y) / (hi.y - lo.y):
            turn += -90.0 if door.x > 0 else 90.0
        else:
            turn += 0.0 if door.y < 0 else 180.0
        front_door_x = (Matrix.Rotation(math.radians(turn), 4, 'Z') @ door).x * scale
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
    piece_sources[name] = [key]
    print(f"PIECE {name}: {triangles(o)} triangles, {tuple(round(v, 2) for v in (hi - lo))} m")
    return o


def tinted(p, name, colour, patterns):
    """
    A copy of a library piece, its materials whose names match the patterns painted the colour (linear RGB): their
    picture's lightness times the colour, or the colour when they have none
    """
    o = p.copy()
    o.data = p.data.copy()
    o.name = name
    for k, material in enumerate(o.data.materials):
        if material is None or not any(re.search(pattern, material.name) for pattern in patterns):
            continue
        painted = material.copy()
        painted.name = f"{material.name}_{name.rpartition('_')[2]}"
        bsdf = next((n for n in painted.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if painted.use_nodes else None
        if bsdf is None:
            continue
        node = bsdf.inputs['Base Color'].links[0].from_node if bsdf.inputs['Base Color'].links else None
        if node is not None and node.type == 'TEX_IMAGE' and node.image is not None:
            image = node.image.copy()
            image.name = painted.name
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            pixels = pixels.reshape(-1, 4)
            light = pixels[:, :3] @ np.array((0.3, 0.59, 0.11), dtype=np.float32)
            pixels[:, :3] = np.clip(light[:, None] * np.array(colour, dtype=np.float32)[None, :] / max(light.mean(), 1e-3) * 0.55, 0, 1)
            image.pixels.foreach_set(pixels.ravel())
            image.pack()
            node.image = image
        else:
            bsdf.inputs['Base Color'].default_value = (*colour, 1.0)
        o.data.materials[k] = painted
    shelve(o)
    piece_sources[name] = piece_sources.get(p.name, [])
    return o


# The kit's build: the pieces put on the stage (by name, the library's object), what each is made of (the models'
# keys, for its credit), and where each went
layout = []
kit_used = {}
piece_sources = {}
baking = 0  # while a house is being baked into a piece of its own, what is put goes into it


def put(p, x, y, z=CURB, heading=0.0, scale=1.0, name=None):
    """
    A copy of a library piece at Blender's (x, y), z up, turned by heading (the game's degrees: 0 faces south). In the
    kit's build a piece out on the street (not a look's, not a house's garden) is written to the layout instead
    """
    if p is None:
        return None
    if SPLIT and not baking and not (name or "").startswith("LOOK_"):
        kit_used[p.name] = p
        layout.append({"piece": p.name, "x": round(x, 3), "y": round(z, 3), "z": round(-y, 3),
                       "heading": round(heading, 2), "scale": round(scale, 4)})
        return None
    if baking:
        baked_from.append(p.name)
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
house_shutters = [flat(f"house_shutter_{n}", colour, rough=0.6) for n, colour in
                  enumerate(((0.03, 0.09, 0.05), (0.02, 0.03, 0.08), (0.12, 0.03, 0.02), (0.8, 0.8, 0.76)))]

# ---- The cards (bake_cards.py's pictures on crossed quads): the lawns' trees, bushes and flowers, the far palms ----
cards = {}
if CARDS_FOLDER and os.path.isfile(os.path.join(CARDS_FOLDER, "cards.json")):
    cards = json.load(open(os.path.join(CARDS_FOLDER, "cards.json"), encoding="utf-8"))
card_pieces = {}


def card_material(name, picture):
    """A material cut out by its picture's alpha"""
    material = flat(name, (1.0, 1.0, 1.0), rough=0.9)
    tree = material.node_tree
    node = tree.nodes.new('ShaderNodeTexImage')
    node.image = bpy.data.images.load(os.path.join(CARDS_FOLDER, picture))
    bsdf = tree.nodes["Principled BSDF"]
    tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
    tree.links.new(node.outputs['Alpha'], bsdf.inputs['Alpha'])
    material.surface_render_method = 'DITHERED'
    return material


def card(name):
    """
    A card as a library piece, or None when it was not taken: its front picture on a quad across x and its side's on
    one across y, each its span metres square standing on its bottom edge at the origin, with a back face to each, its
    normals all up (lit as the leaves are, from above, whichever side is seen)
    """
    if name in card_pieces or name not in cards:
        return card_pieces.get(name)
    entry = cards[name]
    span = entry["span"]
    materials = [card_material(f"card_{name}_{view}", entry[view]) for view in ("front", "side") if view in entry]
    vertices, faces, uvs, slots = [], [], [], []

    def quad(points, slot):
        base = len(vertices)
        vertices.extend(points)
        faces.append((base, base + 1, base + 2, base + 3))
        uvs.append([(0, 0), (1, 0), (1, 1), (0, 1)])
        faces.append((base + 3, base + 2, base + 1, base))
        uvs.append([(0, 1), (1, 1), (1, 0), (0, 0)])
        slots.extend([slot, slot])

    # The front picture's left is Blender's -x; the side's (the camera a quarter turn on, at +x) is -y
    quad([(-span / 2, 0, 0), (span / 2, 0, 0), (span / 2, 0, span), (-span / 2, 0, span)], 0)
    if len(materials) > 1:
        quad([(0, -span / 2, 0), (0, span / 2, 0), (0, span / 2, span), (0, -span / 2, span)], 1)
    o = mesh_object(f"card_{name}", vertices, faces, uvs, materials, slots)
    o.data.normals_split_custom_set_from_vertices([(0.0, 0.0, 1.0)] * len(o.data.vertices))
    shelve(o)
    card_pieces[name] = o
    piece_sources[o.name] = [f"card:{name}"]
    return o


def card_height(name):
    return cards[name]["height"] if name in cards else 1.0


# A shade on the lawn under each tree on a card (the renderer's shadow pass cannot cut a card's shadow by its alpha)
def shade_picture(path, size=128):
    image = bpy.data.images.new("shade", size, size, alpha=True)
    pixels = []
    for j in range(size):
        for i in range(size):
            r = math.hypot((i + 0.5) / size * 2 - 1, (j + 0.5) / size * 2 - 1)
            pixels.extend((0.0, 0.0, 0.0, max(0.0, 1.0 - r) ** 1.4 * 0.55))
    image.pixels = pixels
    image.filepath_raw = path
    image.file_format = 'PNG'
    image.save()
    return image


shade_material = flat("lawn_shade", (0.0, 0.0, 0.0), rough=1.0)
_shade_node = shade_material.node_tree.nodes.new('ShaderNodeTexImage')
_shade_node.image = shade_picture(os.path.join(SIGNS, "shade.png"))
shade_material.node_tree.links.new(_shade_node.outputs['Alpha'], shade_material.node_tree.nodes["Principled BSDF"].inputs['Alpha'])
shade_material.node_tree.links.new(_shade_node.outputs['Color'], shade_material.node_tree.nodes["Principled BSDF"].inputs['Base Color'])
shade_material.surface_render_method = 'BLENDED'
shade_material["blend"] = True
# Where the sun's shadow falls on the ground, Blender's x, y (street.json's sun is [0.35, 0.62, 0.7], the game's x, y, z)
SHADE_WAY = (-0.45, 0.89)


def shade(x, y, z, radius):
    """A shade of the radius on the ground about Blender's (x, y), at z, shifted a little the sun's shadow's way"""
    cx, cy = x + SHADE_WAY[0] * radius * 0.3, y + SHADE_WAY[1] * radius * 0.3
    v = [(cx - radius, cy - radius, z), (cx + radius, cy - radius, z), (cx + radius, cy + radius, z), (cx - radius, cy + radius, z)]
    return mesh_object("lawn_shade", v, [(0, 1, 2, 3)], [[(0, 0), (1, 0), (1, 1), (0, 1)]], [shade_material], [0])


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

    def window(self, x, y, z, w, h, facing, shutters=None):
        """
        A window on a wall facing -y (front), +y (back), -x or +x: dark glass in a white frame, a little proud; on the
        front, shutters of a material either side when given
        """
        thick = 0.06
        if facing in ("front", "back"):
            out = -1 if facing == "front" else 1
            self.box(house_glass, (x, y + out * 0.03, z), (w, thick, h))
            if shutters is not None:
                for side in (-1, 1):
                    self.box(shutters, (x + side * (w / 2 + 0.3), y + out * 0.05, z), (0.42, 0.05, h + 0.1))
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
    width, depth, wall = rng.uniform(10.5, 12.5), rng.uniform(8.0, 9.5), 2.8
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
    shutters = house_shutters[rng.randrange(len(house_shutters))]
    h.window(big, front, FLOOR + 1.45, 2.6, 1.4, "front", shutters)
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
    shutters = house_shutters[rng.randrange(len(house_shutters))]
    for wx in (cx - 1.8, cx + 1.8):
        h.window(wx, front, FLOOR + 4.2, 1.1, 1.2, "front", shutters)
    for side, sx in (("left", cx - width / 2), ("right", cx + width / 2)):
        for wy in (cy - 2.0, cy + 2.0):
            h.window(sx, wy, FLOOR + 1.5, 1.0, 1.2, side)
            h.window(sx, wy, FLOOR + 4.2, 1.0, 1.1, side)
    h.box(house_chimney, (cx + (width / 2 - 0.6) * (1 if left else -1), cy + 2.5, FLOOR + wall + 1.8), (0.7, 0.7, 3.6))
    return cx, width, front, door


def fleetwood(h, rng, n, left, at):
    """
    The Fleetwood (a real 1950s ranch plan, AleixoAlonso's model) on the lot, its siding one of the variants' colours,
    long side to the street; where it stands as the procedural houses say (its middle across, its width, its front,
    its door). at turns a point on the lot (x across it, y back from the sidewalk) into Blender's x, y
    """
    variant = fleetwood_variants[n % len(fleetwood_variants)]
    lo, hi = bounds(variant)
    width, depth = hi.x - lo.x, hi.y - lo.y
    setback = rng.uniform(7.5, 9.0)
    cx = -1.2 if left else 1.2
    wx, wy, turn = at(cx, setback + depth / 2)
    put(variant, wx, wy, 0.16, heading=turn)
    return cx, width, setback, cx + FLEETWOOD_DOOR


def plants(rng, cx, width, front, door, drive, half, at, ground):
    """
    A house's garden: bushes along its front either side of the door, flowers in front of them, and a tree on the
    lawn away from the drive, a shade under it; at turns a point on the lot into Blender's x, y and the lot's heading
    """
    bush, flowers = card("bush"), [f for f in (card("gazania"), card("periwinkle")) if f is not None]
    x = cx - width / 2 + 0.7
    while x < cx + width / 2 - 0.5:
        if abs(x - door) > 1.1:
            if bush is not None and rng.random() < 0.8:
                wx, wy, _ = at(x, front - 0.8)
                put(bush, wx, wy, ground, heading=rng.uniform(0, 360), scale=rng.uniform(0.45, 0.65))
            for dx in (-0.5, 0.4):
                if flowers and rng.random() < 0.85:
                    wx, wy, _ = at(x + dx, front - 1.7 + rng.uniform(-0.2, 0.2))
                    put(flowers[rng.randrange(len(flowers))], wx, wy, ground, heading=rng.uniform(0, 360), scale=rng.uniform(1.6, 2.3))
        x += rng.uniform(1.5, 2.1)
    # The tree, of the kinds LA planted in the 50s: a jacaranda, a ficus, or a smaller one
    kinds = [(name, size) for name, size in (("jacaranda", (7.0, 9.0)), ("banyan", (7.5, 9.5)), ("island", (6.0, 7.5)),
                                              ("small_tree", (5.0, 6.5))) if name in cards]
    if not kinds:
        return
    name, (low, high) = kinds[rng.randrange(len(kinds))]
    side = -1 if drive > 0 else 1
    tx, ty = side * (half - rng.uniform(3.5, 4.5)), rng.uniform(2.8, max(3.0, front - 3.5))
    wx, wy, _ = at(tx, ty)
    height = rng.uniform(low, high)
    put(card(name), wx, wy, ground, heading=rng.uniform(0, 360), scale=height / card_height(name))
    shade(wx, wy, ground + 0.015, height * 0.38)


def house(n, x, y, turn, plain=False):
    """
    House n on its lot: the lot's front middle at (x, y) on the back of the sidewalk, facing the street (turn); a third
    of them two-storey, the rest ranch houses, every other one of those the Fleetwood (unless plain: the far rows
    closing the streets' ends, ours only). In the kit's build the house is one of HOUSE_VARIANTS baked as pieces, its
    garden and all, put in the lot's middle
    """
    if SPLIT and not baking:
        p = house_piece(n % HOUSE_VARIANTS, plain)
        middle = Matrix.Rotation(math.radians(turn), 4, 'Z') @ Vector((0.0, LOT_DEPTH / 2, 0.0))
        put(p, x + middle.x, y + middle.y, CURB, heading=turn)
        return
    rng_house = random.Random(1960 + n)
    h = House()
    siding = house_siding[n % len(house_siding)]
    left = rng_house.random() < 0.5
    rotation = Matrix.Rotation(math.radians(turn), 4, 'Z')

    def at(lx, ly):
        p = rotation @ Vector((lx, ly, 0.0))
        return x + p.x, y + p.y, turn

    if n % 3 == 1:
        cx, width, front, door = two_storey(h, rng_house, siding, left)
    elif n % 3 == 0 and not plain and fleetwood_variants:
        cx, width, front, door = fleetwood(h, rng_house, n, left, at)
    else:
        cx, width, front, door = ranch(h, rng_house, siding, left)

    # The lawn over the lot, the drive and the walk to the door on it, a hedge along the sidewalk
    half = HOUSE_LOT / 2
    h.flat(house_lawn, -half, 0.0, half, LOT_DEPTH, 0.16, 3.0)
    drive = (half - 2.0) * (1 if left else -1)
    h.flat(house_concrete, drive - 1.4, 0.0, drive + 1.4, front + 6.0, 0.175, 2.0)
    h.flat(house_concrete, door - 0.6, 0.0, door + 0.6, front - 0.1, 0.175, 2.0)
    if rng_house.random() < 0.5:
        for a, b in ((-half + 0.3, min(door, drive) - 1.6), (min(door, drive) + 1.6, max(door, drive) - 1.6), (max(door, drive) + 1.6, half - 0.3)):
            if b - a > 1.0:
                h.box(house_hedge, ((a + b) / 2, 0.6, 0.16 + 0.35), (b - a, 0.6, 0.7), tile=1.5)
    h.build(f"house_{n}", x, y, turn)
    plants(rng_house, cx, width, front, door, drive, half, at, 0.16)


house_count = 0

# The kit's houses: house n is variant n % 12 (its kind n % 3 and its siding n % 4 kept), a plain one a ranch house
HOUSE_VARIANTS = 12
house_pieces = {}
baked_from = []


def house_piece(v, plain):
    """
    House variant v baked into a library piece of its own (house_<kind>_<v>): the house, its lot, its drive and hedge,
    its garden's plants and the shade under its tree as one mesh, the lot's middle at the origin, stood on its base
    """
    global baking
    kind = ("two_storey" if v % 3 == 1 else "fleetwood" if v % 3 == 0 and not plain and fleetwood_variants else "ranch")
    name = f"house_{kind}_{v}"
    if name in house_pieces:
        return house_pieces[name]
    before = set(street.objects)
    baked_from.clear()
    baking += 1
    try:
        house(v, 0.0, -LOT_DEPTH / 2, 0.0, plain=plain)
    finally:
        baking -= 1
    made = [o for o in street.objects if o not in before]
    # The copies of library pieces (the garden's cards, the Fleetwood) share their meshes: each gets its own to join
    for o in made:
        if o.data.users > 1:
            o.data = o.data.copy()
    o = join(made, name)
    lo, _ = bounds(o)
    o.data.transform(Matrix.Translation((0.0, 0.0, -lo.z)))
    shelve(o)
    piece_sources[name] = ["own"] + sorted({s for p in baked_from for s in piece_sources.get(p, [])})
    house_pieces[name] = o
    print(f"PIECE {name}: {triangles(o)} triangles")
    return o


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

# ---- The diners' looks (step 37, phase 4): the Hi-Way Grill, Surfside and the Starlite are this same corner dressed
# three more ways. Every object named LOOK_<DINER>_... goes into the KN5 under a node of its own (PreparedScene's
# LookPrefix) that the game shows for that diner alone (diners.json's look, street.json's looks): the roof sign with the
# diner's name, its billboard, and what stands in its lot. The rest of the street is shared. ----
LOOKS = ("mainstreet", "hiway", "surfside", "starlite")


def look_name(look, what):
    return f"LOOK_{look.upper()}_{what}_{len(street.objects)}"


# The roof board along the roof's front, on legs: the diner's name for each look, the legs shared
steel = flat("sign_steel", (0.08, 0.08, 0.09), rough=0.35, metal=0.8)
roof_top = bounds(diner_mesh)[1].z
roof_x = (DINER_WEST + DINER_EAST) / 2 + 1.5
for look in LOOKS:
    picture = "sign_roof" if look == "mainstreet" else f"sign_roof_{look}"
    board(look_name(look, "sign_roof"), (10.0, 0.3, 2.5), (roof_x, DINER_FRONT + 5.0, roof_top + 0.6 + 1.25),
          pictured(picture, os.path.join(SIGNS, picture + ".jpg")), steel)
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

# The Fleetwood: its inside (doors, frames, the floor showing through the windows) left out, its glass ours (dark, as
# our houses' windows: the model's is see-through, onto nothing), its front door's side to the street, its siding in
# four colours of the time
fleetwood_variants = []
FLEETWOOD_DOOR = 0.0
fleetwood_piece = piece("fleetwood", "fleetwood", drop=[r"door-Interior", r"interiorFrame", r"doorFront.*Metal_Aluminum"],
                        front=[r"doorFront_I\.001"], planar=True, budget=3000)
if fleetwood_piece is not None:
    for k, material in enumerate(fleetwood_piece.data.materials):
        if material is not None and "Glass" in material.name:
            fleetwood_piece.data.materials[k] = house_glass
    FLEETWOOD_DOOR = front_door_x
    for k, colour in enumerate(((0.62, 0.78, 0.66), (0.85, 0.72, 0.55), (0.62, 0.72, 0.86), (0.9, 0.86, 0.72))):
        fleetwood_variants.append(tinted(fleetwood_piece, f"fleetwood_v{k}", colour, [r"Siding", r"plywood"]))

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
    house(200 + k, STREET_WEST - 12, float(hy), 90.0, plain=True)
    house(220 + k, STREET_EAST + 12, float(hy), -90.0, plain=True)
for k, hx in enumerate(range(-64, -7, 16)):
    house(240 + k, float(hx), CROSS_NORTH + 12, 180.0, plain=True)
    house(260 + k, float(hx), CROSS_SOUTH - 12, 0.0, plain=True)

# ---- What stands on the sidewalks ----
# The palms whole (cut down, their fronds and trunks were torn open), the fan palm by half
palm_tall = piece("palm_tall", "palm_tall", height=17.0)
palm_fan = piece("palm_fan", "palm_fan", height=8.5, budget=14000)
PALM_NEAR = 45.0  # metres from the player's car: a palm nearer is the model, one further its card
# The lamp posts' arms turned to face the game's +z (they come along -x): a post's heading is where its arm reaches out over
# the road, and its light hangs at the arm's end (build_street.py's LAMPS)
cobra = piece("cobra_head", "cobra_head", height=10.5, budget=1500, foot=True, turn=90.0)
pole_lamp = piece("pole_lamp", "pole_lamp", height=11.0, budget=1500, foot=True, turn=90.0)
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


def with_ad(p, name, picture):
    """A copy of the billboard with our ad of the time on its face (its own is today's): the material named after the look"""
    o = p.copy()
    o.data = p.data.copy()
    o.name = name
    ad = pictured(name, os.path.join(SIGNS, picture))
    for k, material in enumerate(o.data.materials):
        if material is not None and "material" in material.name.lower() and "Main" not in material.name:
            o.data.materials[k] = ad
    shelve(o)
    return o


billboards = {}
if billboard is not None:
    for look in LOOKS:
        billboards[look] = with_ad(billboard, "billboard_ad" if look == "mainstreet" else f"billboard_ad_{look}",
                                   "billboard.jpg" if look == "mainstreet" else f"billboard_{look}.jpg")


def is_clear(x, y, gaps):
    return not any(a <= x <= b for a, b in gaps)


# Driveways and corners the sidewalk things keep out of, along Main Street (x) and the cross street (y)
MAIN_GAPS = [(CROSS_X - JUNCTION - 2, CROSS_X + JUNCTION + 2), (LOT_WEST, LOT_EAST)]
CROSS_GAPS = [(-JUNCTION - 2, JUNCTION + 2), (SIDEWALK, LOT_NORTH)]



def palm(x, y, z=CURB, scale=1.0):
    """A tall palm, 17 m by scale: the model within PALM_NEAR of the player's car, its card further off"""
    if math.hypot(x - PLAYER[0], y - PLAYER[1]) < PALM_NEAR or card("palm") is None:
        put(palm_tall, x, y, z, heading=rng.uniform(0, 360), scale=scale)
    else:
        put(card("palm"), x, y, z, heading=rng.uniform(0, 360), scale=17.0 * scale / card_height("palm"))


# Palms along both sides of Main Street and the cross street downtown, near the kerb
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
for x, y in palm_spots:
    palm(x, y, scale=rng.uniform(0.85, 1.15))
# Palms beside and behind the diner, seen either side of it from the curb, and a fan palm in the lot's corner bed
for x, y in ((-21.0, 40.0), (13.0, 38.0), (19.0, 48.0), (-6.0, 54.0)):
    palm(x, y, scale=rng.uniform(0.95, 1.2))
bed = flat("lot_bed", (0.16, 0.11, 0.07), rough=0.95)
box("lot_bed", (5.0, 4.0, 0.3), (LOT_WEST + 3.2, SIDEWALK + 2.6, CURB + 0.15), bed)
put(palm_fan, LOT_WEST + 2.2, SIDEWALK + 4.0, CURB + 0.3, heading=40.0)
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
# Hydrants at the kerb (the sidewalk's front is 6 m from a road's middle), as LA has them
for x, y in ((LOT_WEST + 0.8, 6.5), (PLAYER[0] + 9.0, -6.5), (62.0, 6.5), (CROSS_X - 6.5, -SIDEWALK - 4.0), (-88.0, -6.5)):
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

# Across the way: a motel's Googie star sign on the north-west corner, the billboard on the vacant south-west lot (its
# ad each diner's own)
put(star_sign, CROSS_X - SIDEWALK - 3.0, SIDEWALK + 1.8, heading=90.0)
for look, p in billboards.items():
    put(p, CROSS_X - SIDEWALK - 9.0, -SIDEWALK - 8.0, heading=-135.0, name=look_name(look, "billboard"))

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
CAR_BUDGET = 8000      # within CAR_NEAR metres of the player's car
CAR_FAR_BUDGET = 5000  # the rest, seen across a block at the nearest
CAR_NEAR = 45.0


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
car_pieces = {}
for key, ((cx, cy), heading, length, drop) in CARS.items():
    if key not in MODELS:
        skipped.append(key)
        continue
    try:
        near = math.hypot(cx - PLAYER[0], cy - PLAYER[1]) < CAR_NEAR
        # The kit holds each car once, for wherever it is parked
        car = piece(key, key, drop=drop or None, budget=CAR_BUDGET if near or SPLIT else CAR_FAR_BUDGET)
        fit_car(car, length)
        put(car, cx, cy, 0.0, heading=heading, name=f"CAR_{key}")
        cars_used.append(key)
        car_pieces[key] = car
    except RuntimeError as ex:
        print(f"CAR {key} left out: {ex}")


# ---- What each diner's look adds to the lot (see LOOKS above). The lot behind the diner (y 30 to 44, between the palms
# at (-21, 40) and (13, 38)) is free of the racers' bays: that is where a look's cars and trucks stand. ----

def cutout(name, path):
    """A material cut out by its picture's alpha (a fence, a string of flags), seen from both sides"""
    material = flat(name, (1.0, 1.0, 1.0), rough=0.8)
    tree = material.node_tree
    node = tree.nodes.new('ShaderNodeTexImage')
    node.image = bpy.data.images.load(path)
    bsdf = tree.nodes["Principled BSDF"]
    tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
    tree.links.new(node.outputs['Alpha'], bsdf.inputs['Alpha'])
    material.surface_render_method = 'DITHERED'
    return material


def standing_quad(name, a, b, z0, z1, material, tile):
    """A quad standing on the ground from a to b (Blender's x, y), z0 to z1 up, a face each side, its picture every tile metres"""
    (x0, y0), (x1, y1) = a, b
    length = math.hypot(x1 - x0, y1 - y0)
    v = [(x0, y0, z0), (x1, y1, z0), (x1, y1, z1), (x0, y0, z1)]
    uv = [(0, 0), (length / tile, 0), (length / tile, (z1 - z0) / tile), (0, (z1 - z0) / tile)]
    return mesh_object(name, v, [(0, 1, 2, 3), (3, 2, 1, 0)], [uv, list(reversed(uv))], [material], [0, 0])


def cylinder(name, radius, height, middle, material, sides=12):
    """A drum: a prism of so many sides standing on its base, its middle at a point"""
    x, y, z = middle
    ring = [(x + radius * math.cos(2 * math.pi * k / sides), y + radius * math.sin(2 * math.pi * k / sides)) for k in range(sides)]
    v = [(px, py, z) for px, py in ring] + [(px, py, z + height) for px, py in ring]
    faces = [tuple(range(sides - 1, -1, -1)), tuple(range(sides, 2 * sides))]
    uvs = [[(0.5 + 0.5 * math.cos(2 * math.pi * k / sides), 0.5 + 0.5 * math.sin(2 * math.pi * k / sides)) for k in range(sides - 1, -1, -1)],
           [(0.5 + 0.5 * math.cos(2 * math.pi * k / sides), 0.5 + 0.5 * math.sin(2 * math.pi * k / sides)) for k in range(sides)]]
    for k in range(sides):
        n = (k + 1) % sides
        faces.append((k, n, sides + n, sides + k))
        uvs.append([(k / sides, 0), ((k + 1) / sides, 0), ((k + 1) / sides, 1), (k / sides, 1)])
    return mesh_object(name, v, faces, uvs, [material], [0] * len(faces))


def surfboard(name, colour, x, y, lean=14.0, length=2.4, width=0.56, thick=0.07):
    """A surfboard leaning on the diner's west wall at (x, y), its tail on the ground, lean degrees off the vertical"""
    outline = [(0.0, 0.0), (0.12, -0.3), (0.45, -0.46), (1.1, -0.5), (1.8, -0.44), (2.25, -0.2), (2.4, 0.0),
               (2.25, 0.2), (1.8, 0.44), (1.1, 0.5), (0.45, 0.46), (0.12, 0.3)]
    outline = [(u * length / 2.4, w * width) for u, w in outline]
    a = math.radians(lean)
    along = Vector((math.sin(a), 0.0, math.cos(a)))   # up the board, leaning towards +x (the wall)
    across = Vector((0.0, 1.0, 0.0))
    normal = along.cross(across)
    base = Vector((x, y, CURB))
    v = [tuple(base + along * u + across * w - normal * (thick / 2)) for u, w in outline]
    v += [tuple(base + along * u + across * w + normal * (thick / 2)) for u, w in outline]
    n = len(outline)
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    uvs = [[(u / length, 0.5 + w / width) for u, w in reversed(outline)], [(u / length, 0.5 + w / width) for u, w in outline]]
    for k in range(n):
        m = (k + 1) % n
        faces.append((k, m, n + m, n + k))
        uvs.append([(0, 0), (1, 0), (1, 1), (0, 1)])
    paint = flat(f"{name}_paint", colour, rough=0.35)
    return mesh_object(name, v, faces, uvs, [paint], [0] * len(faces))


# The Hi-Way Grill: a truck stop by the yards. The tow trucks in the back of the lot, drums behind the diner, the air meter
# and the soda machine by its side door, a chain-link fence along the lot's back, barriers at its east edge
wrecker = piece("wrecker", "wrecker", budget=9000)
heavy_tow = piece("heavy_tow_truck", "heavy_tow_truck", budget=9000)
for key, p, (tx, ty), heading, length in (("wrecker", wrecker, (-8.0, 37.0), 90.0, 6.5), ("heavy_tow_truck", heavy_tow, (6.0, 37.5), 90.0, 7.5)):
    if p is not None:
        fit_car(p, length)
        put(p, tx, ty, 0.0, heading=heading, name=look_name("hiway", key))
drum_red = flat("oil_drum_red", (0.5, 0.1, 0.07), rough=0.55, metal=0.3)
drum_blue = flat("oil_drum_blue", (0.08, 0.12, 0.42), rough=0.55, metal=0.3)
for k, (dx, dy) in enumerate(((0.0, 0.0), (0.65, 0.05), (1.3, -0.02), (0.32, 0.62), (0.97, 0.66), (2.4, 0.1))):
    cylinder(look_name("hiway", "drum"), 0.29, 0.88, (DINER_WEST + 2.5 + dx, DINER_BACK + 1.0 + dy, CURB + 0.02), drum_red if k % 2 else drum_blue)
air_meter = piece("old_tire_inflator", "air_meter", keep=[r"^Object_4$", r"^Object_6$"], scale=0.28, budget=3000)
soda = piece("vintage_soda_machien", "soda_machine", scale=0.01, budget=3000)
put(air_meter, DINER_WEST - 1.4, max(DINER_BACK - 1.0, 31.5), CURB + 0.02, heading=90.0, name=look_name("hiway", "air_meter"))
put(soda, DINER_WEST - 0.9, max(DINER_BACK - 3.2, 29.6), CURB + 0.02, heading=90.0, name=look_name("hiway", "soda"))
chain = cutout("chainlink", os.path.join(SIGNS, "chainlink.png"))
standing_quad(look_name("hiway", "fence"), (LOT_WEST, LOT_NORTH - 0.3), (LOT_EAST, LOT_NORTH - 0.3), CURB, CURB + 2.0, chain, tile=2.0)
fx = LOT_WEST
while fx <= LOT_EAST + 0.01:
    box(look_name("hiway", "fence_post"), (0.08, 0.08, 2.1), (fx, LOT_NORTH - 0.3, CURB + 1.05), steel)
    fx += 3.0
barrier = piece("city_props", "barrier", keep=[r"^barrier"], length=2.0, budget=800)
for k in range(3):
    put(barrier, LOT_EAST - 1.3, 30.5 + k * 2.3, CURB, heading=90.0, name=look_name("hiway", "barrier"))

# Surfside: the coffee shop on the strip by the beach. A string of pennants over the lot's front and down its side,
# surfboards against the side wall, the beach cars in the back, more palms behind
pennant = cutout("pennants", os.path.join(SIGNS, "pennants.png"))
for px, py in ((LOT_WEST + 1.2, SIDEWALK + 0.7), (LOT_EAST - 1.2, SIDEWALK + 0.7), (LOT_WEST + 0.8, LOT_NORTH - 1.5)):
    box(look_name("surfside", "pennant_pole"), (0.1, 0.1, 5.2), (px, py, CURB + 2.6), steel)
standing_quad(look_name("surfside", "pennants"), (LOT_WEST + 1.2, SIDEWALK + 0.7), (LOT_EAST - 1.2, SIDEWALK + 0.7), CURB + 3.9, CURB + 5.1, pennant, tile=4.0)
standing_quad(look_name("surfside", "pennants"), (LOT_WEST + 1.2, SIDEWALK + 0.7), (LOT_WEST + 0.8, LOT_NORTH - 1.5), CURB + 3.9, CURB + 5.1, pennant, tile=4.0)
for k, colour in enumerate(((0.9, 0.3, 0.18), (0.95, 0.82, 0.25), (0.25, 0.55, 0.85))):
    surfboard(look_name("surfside", "surfboard"), colour, DINER_WEST - 0.72, max(DINER_BACK - 3.4, 29.4) + k * 0.7, lean=14.0 + k * 2.0)
for key, (sx, sy), heading in (("duntov", (-7.0, 36.5), 90.0), ("chevelle", (5.0, 36.5), -90.0)):
    if key in car_pieces:
        put(car_pieces[key], sx, sy, 0.0, heading=heading, name=look_name("surfside", key))
for px, py in ((-3.0, 49.0), (9.0, 51.0), (-15.0, 50.0)):
    if card("palm") is not None:
        put(card("palm"), px, py, CURB, heading=rng.uniform(0, 360), scale=17.0 / card_height("palm"), name=look_name("surfside", "palm"))

# The Starlite: the Googie palace. Neon tubes along the roof's edges (lit by the look's glow), a second star sign on the
# lot's east corner, the big iron in the back
neon_pink = flat("neon_strip_pink", (1.0, 0.35, 0.8), rough=0.3)
neon_blue = flat("neon_strip_blue", (0.3, 0.6, 1.0), rough=0.3)
front_span = DINER_EAST - DINER_WEST - 1.0
side_span = DINER_BACK - DINER_FRONT - 1.0
box(look_name("starlite", "neon"), (front_span, 0.08, 0.08), ((DINER_WEST + DINER_EAST) / 2, DINER_FRONT - 0.12, roof_top - 0.25), neon_pink)
box(look_name("starlite", "neon"), (front_span, 0.08, 0.08), ((DINER_WEST + DINER_EAST) / 2, DINER_FRONT - 0.12, roof_top - 0.6), neon_blue)
box(look_name("starlite", "neon"), (0.08, side_span, 0.08), (DINER_WEST - 0.12, (DINER_FRONT + DINER_BACK) / 2, roof_top - 0.25), neon_pink)
box(look_name("starlite", "neon"), (0.08, side_span, 0.08), (DINER_WEST - 0.12, (DINER_FRONT + DINER_BACK) / 2, roof_top - 0.6), neon_blue)
put(star_sign, LOT_EAST - 3.0, SIDEWALK + 2.6, CURB, heading=180.0, name=look_name("starlite", "star_sign"))
for key, (sx, sy), heading in (("phoenix", (-8.0, 36.5), 90.0), ("chapman", (5.0, 36.5), -90.0), ("cadillac_75", (-19.0, 33.5), 0.0)):
    if key in car_pieces:
        put(car_pieces[key], sx, sy, 0.0, heading=heading, name=look_name("starlite", key))

# The kit's pieces, each once at the origin (objects of no collection yet, so the stage's file leaves them out), and what
# the kit's build writes of each: its size in the game's space (width x, height y, depth z), its triangles, its models
kit_objects = []
pieces_built = {}
for name, p in sorted(kit_used.items()):
    o = p.copy()
    o.matrix_world = Matrix.Identity(4)
    o.name = f"PIECE_{name}"
    kit_objects.append(o)
    lo, hi = bounds(p)
    pieces_built[name] = {"size": [round(hi.x - lo.x, 3), round(hi.z - lo.z, 3), round(hi.y - lo.y, 3)],
                          "triangles": triangles(p), "sources": piece_sources.get(name, ["own"])}

# The library's pieces go: the copies in the scene keep their meshes, and prepare_prop.py exports what is in the scene
for o in list(library.objects):
    bpy.data.objects.remove(o)
bpy.data.collections.remove(library)

total = sum(triangles(o) for o in street.objects if o.type == 'MESH')
print(f"STREET {len(street.objects)} objects, {total} triangles")
print(f"LEFT OUT (not downloaded): {', '.join(sorted(set(skipped))) if skipped else 'nothing'}")
with open(os.path.join(os.path.dirname(OUT), "street_built.json"), "w", encoding="utf-8") as fh:
    json.dump({"lamps": LAMPS, "cars": cars_used, "skipped": sorted(set(skipped)),
               "diner": [DINER_WEST, DINER_EAST, DINER_FRONT, DINER_BACK],
               "looks": {look: sum(triangles(o) for o in street.objects if o.type == 'MESH' and o.name.startswith(f"LOOK_{look.upper()}_")) for look in LOOKS},
               "pieces": pieces_built, "layout": layout}, fh, indent=1)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=OUT)

# The kit's file: the stage taken out, the pieces in
if SPLIT:
    # Everything but the pieces goes, the stage's and what was never in the street collection (the diner)
    for o in list(bpy.data.objects):
        if o not in kit_objects:
            bpy.data.objects.remove(o)
    for o in kit_objects:
        street.objects.link(o)
    print(f"KIT {len(kit_objects)} pieces, {sum(triangles(o) for o in kit_objects)} triangles")
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=KIT_OUT)
