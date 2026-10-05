"""
Lays out the diner's street in Blender, out of the Quaternius Downtown City MegaKit (CC0) and WaajaaH's diner (CC-BY),
and saves it as a .blend for prepare_prop.py (then DinerSceneBuilder) to make the game's KN5 of (build_street.py runs it all):

  blender -b --python compose_street.py -- <kit glTF folder> <diner .fbx> <diner textures folder> <signs folder> <out.blend>

The signs folder is prepare_pictures.py's.

Blender's space: x along Main Street, y north (the diner's side), z up; metres; the road's surface at z = 0. The game's
(AC's) space is x the same, y up, z = -y (the diner towards -z): street.json's and diner.json's positions are in the game's.

Main Street is four lanes (a parking lane and a lane each way), 12 m kerb to kerb, a 3 m sidewalk each side, a row of
brick buildings behind each sidewalk downtown, round the diner, and houses on their lawns beyond (our own: clapboard,
shingles, a porch or a picture window, a drive and a hedge, prepare_pictures.py's pictures). The diner stands back from
the north sidewalk behind its own lot, 36 m wide and 30 m deep: a row of bays along the diner's front (the racers' cars), a row near the street facing it (the player's), an
aisle behind those to back out into. Its sign is on its roof, a pole sign at the lot's way in. The lot's neighbours'
side walls flank it, a back row of buildings stands behind.
The street ends at the west in a row of houses across it (a T), and the east is where the car comes from.
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
KIT, DINER_FBX, DINER_TEXTURES, SIGNS, OUT = args

# The kit's road sits 0.15 under its sidewalks; everything goes up by as much, so the road is at 0
LIFT = 0.15

# The diner's model is a tenth-scale file: its mannequin (1.8 m) stands 0.133 high
DINER_SCALE = 13.5

STREET_WEST, STREET_EAST = -120.0, 144.0
SIDEWALK = 9.0  # the back of each sidewalk, from the middle of the road
LOT_WEST, LOT_EAST = -18.0, 18.0
LOT_BACK = SIDEWALK + 30.0  # the diner's front
FRONT_ROW = SIDEWALK + 6.0  # the near end of the player's row of bays (an aisle before it)
BUILDINGS = {  # width along the street, as the kit's models are (front at y = 0 facing -y, the rest towards +y)
    "Building_Small_1": (-7.23, 5.23),
    "Building_Medium_2_001": (-7.53, 7.53),
    "Building_Large_2": (-9.32, 11.32),
}
# A building further than this from the diner's front keeps this much of its triangles
FAR = 45.0
FAR_RATIO = 0.45

ROW = ["Building_Small_1", "Building_Large_2", "Building_Medium_2_001", "Building_Medium_2_001", "Building_Small_1",
       "Building_Large_2", "Building_Medium_2_001", "Building_Small_1", "Building_Large_2"]

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
library = bpy.data.collections.new("library")
street = bpy.data.collections.new("street")
scene.collection.children.link(street)


def same_name(name):
    """A material's or picture's name without the .001 Blender adds to the second of a name"""
    base, dot, tail = name.rpartition(".")
    return base if dot and tail.isdigit() else name


def dedupe():
    """Each piece brings its own copy of the kit's materials and pictures: one of each name is kept"""
    images = {}
    for image in list(bpy.data.images):
        key = same_name(image.name)
        if key in images and images[key] is not image:
            image.user_remap(images[key])
            bpy.data.images.remove(image)
        else:
            images[key] = image
    materials = {}
    for material in list(bpy.data.materials):
        key = same_name(material.name)
        if key in materials and materials[key] is not material:
            material.user_remap(materials[key])
            bpy.data.materials.remove(material)
        else:
            materials[key] = material


def load_piece(name, far):
    """
    A kit model as one mesh with its transforms applied, kept out of the scene to copy from. A building's flat faces are
    merged (half its triangles, nothing seen changes); one far from the diner loses more, its window frames simpler.
    """
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(KIT, name + ".gltf"))
    meshes = [o for o in set(bpy.data.objects) - before if o.type == 'MESH']
    unparent(meshes)
    for o in set(bpy.data.objects) - before:
        if o.type != 'MESH':
            bpy.data.objects.remove(o)
    if name.startswith("Building_"):
        for o in meshes:
            decimate(o, 'DISSOLVE')
            if far:
                decimate(o, 'COLLAPSE')
    piece = join(meshes, name + ("_far" if far else ""))
    dedupe()
    return piece


def decimate(o, kind):
    m = o.modifiers.new("decimate", 'DECIMATE')
    m.decimate_type = kind
    if kind == 'DISSOLVE':
        m.angle_limit = math.radians(1.0)
        m.use_dissolve_boundaries = False
        m.delimit = {'MATERIAL', 'UV'}
    else:
        m.ratio = FAR_RATIO
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def unparent(objects):
    """Each object where its parents put it, so they can go"""
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
    for c in list(piece.users_collection):
        c.objects.unlink(piece)
    library.objects.link(piece)
    return piece


pieces = {}


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


def mesh_object(name, vertices, faces, uvs, materials, slots, x, y, z, turn):
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


def box(name, size, middle, material, turn=0.0):
    """A box of one material, its middle at a point"""
    v, faces = cuboid(size)
    whole = [(0, 0), (1, 0), (1, 1), (0, 1)]
    return mesh_object(name, v, faces, [whole] * 6, [material], [0] * 6, *middle, turn)


def board(name, size, middle, picture, edge, turn=0.0):
    """A sign's board: the picture on its front and back, each reading the right way round, its edges of another material"""
    v, faces = cuboid(size)
    whole = [(0, 0), (1, 0), (1, 1), (0, 1)]
    return mesh_object(name, v, faces, [whole] * 6, [picture, edge], [0, 0, 1, 1, 1, 1], *middle, turn)


def put(name, x, y, z=0.0, turn=0.0):
    """A copy of a kit model, turned about the vertical by degrees (0: as the kit has it, facing -y)"""
    far = math.hypot(x, y - LOT_BACK) > FAR
    if (name, far) not in pieces:
        pieces[name, far] = load_piece(name, far)
    o = pieces[name, far].copy()
    o.matrix_world = Matrix.Translation((x, y, z + LIFT)) @ Matrix.Rotation(math.radians(turn), 4, 'Z')
    street.objects.link(o)
    return o


def row(begin, end, front, facing_south, start=0):
    """
    Buildings side by side from begin to end along the street (east or west), their fronts on a line, as many as fit;
    the first against begin, so a row out from the lot stands against it
    """
    x, i, way = begin, start, 1 if end > begin else -1
    while True:
        name = ROW[i % len(ROW)]
        left, right = BUILDINGS[name]
        width = right - left
        if abs(end - x) < width:
            break
        west = x if way > 0 else x - width
        if facing_south:
            put(name, west - left, front)
        else:
            put(name, west + right, front, turn=180)
        x += way * width
        i += 1
    return x


# ---- The houses: downtown thins out into a house a lot, along both sides and across the street's end ----

DOWNTOWN_WEST, DOWNTOWN_EAST = -62.0, 76.0
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
    rng = random.Random(1960 + n)
    h = House()
    siding = house_siding[n % len(house_siding)]
    left = rng.random() < 0.5
    cx, width, front, door = (two_storey if n % 3 == 1 else ranch)(h, rng, siding, left)

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


def house_row(begin, end, front, facing_south, first):
    """Houses side by side from begin to end along the street, a lot each; the number of the next house"""
    way = 1 if end > begin else -1
    n = first
    x = begin + way * HOUSE_LOT / 2
    while (end - x) * way >= HOUSE_LOT / 2:
        if facing_south:
            house(n, x, front, 0.0)
        else:
            house(n, x, -front, 180.0)
        x += way * HOUSE_LOT
        n += 1
    return n


# Main Street, a 6 m piece at a time, then the T at its west end
x = STREET_WEST + 3
while x < STREET_EAST:
    put("Street_4Lane", x, 0)
    x += 6

# The west end: the street meets a cross street, a row of buildings across it facing east
for cy in range(-33, 34, 6):
    for cx in (STREET_WEST - 9, STREET_WEST - 3):
        if not (-9 <= cy <= 9 and cx == STREET_WEST - 3):
            put("Street_Asphalt_6x6", cx, cy)
# Across the street's end, houses facing east
for i, hy in enumerate(range(-40, 41, 16)):
    house(100 + i, STREET_WEST - 12, float(hy), 90.0)

# The rows: downtown's brick round the diner (the north row broken by its lot), houses on out to the ends
east = row(LOT_EAST, DOWNTOWN_EAST, SIDEWALK, True)
n = house_row(east, STREET_EAST + 8, SIDEWALK, True, 0)
west = row(LOT_WEST, DOWNTOWN_WEST, SIDEWALK, True, start=5)
n = house_row(west, STREET_WEST, SIDEWALK, True, n)
south = row(DOWNTOWN_WEST, DOWNTOWN_EAST, -SIDEWALK, False, start=4)
n = house_row(south, STREET_EAST + 8, SIDEWALK, False, n)
n = house_row(DOWNTOWN_WEST, STREET_WEST, SIDEWALK, False, n)

# Under it all, the ground of the lots between the buildings, a little under the road, so no gap shows the void
dirt = flat("ground_dirt", (0.07, 0.065, 0.06), rough=0.9)
box("ground", (STREET_EAST - STREET_WEST + 160.0, 260.0, 0.02), ((STREET_EAST + STREET_WEST) / 2, 20.0, -0.03), dirt)

# Behind the lot: a back row past the diner, seen over and beside it
row(-46, 46, LOT_BACK + 26, True, start=2)

# The lot: asphalt at the sidewalk's height from the sidewalk to the diner and beyond it at the sides
for lx in range(int(LOT_WEST) + 3, int(LOT_EAST), 6):
    for ly in range(int(SIDEWALK) + 3, int(LOT_BACK + 26), 6):
        put("Street_Asphalt_6x6", lx, ly, z=0.15 + 0.005)

# The bays: white lines 3 m apart, 5 m deep, along the diner's front and in the row near the street
paint = flat("lot_paint", (0.85, 0.85, 0.82), rough=0.7)
for bx in range(-15, 16, 3):
    for near in (LOT_BACK - 2.5, FRONT_ROW + 2.5):
        box("bay_line", (0.12, 5.0, 0.01), (bx, near, 0.15 + 0.01), paint)

# The signs: DINER along the roof's front, on legs; EAT on a pole at the lot's way in, side on to the street
steel = flat("sign_steel", (0.08, 0.08, 0.09), rough=0.35, metal=0.8)
roof_board = pictured("sign_roof", os.path.join(SIGNS, "sign_roof.jpg"))
pylon_board = pictured("sign_pylon", os.path.join(SIGNS, "sign_pylon.jpg"))

# The diner, its own ground under it left out (the lot is)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=DINER_FBX)
diner = []
for o in set(bpy.data.objects) - before:
    if o.type != 'MESH' or "Mannequin" in o.name or o.name.startswith("Ground"):
        continue
    diner.append(o)
unparent(diner)
for o in set(bpy.data.objects) - before:
    if o not in diner:
        bpy.data.objects.remove(o)
for m in bpy.data.materials:
    if m.name in ("Building", "Glass1", "Other") and m.use_nodes:
        part = {"Building": "Building", "Glass1": "Glass1", "Other": "Other"}[m.name]
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
library.objects.unlink(diner_mesh)
street.objects.link(diner_mesh)
lowest = min((diner_mesh.matrix_world @ v.co).z for v in diner_mesh.data.vertices)
front = min((diner_mesh.matrix_world @ v.co).y for v in diner_mesh.data.vertices)
diner_mesh.matrix_world = Matrix.Translation((0, LOT_BACK - front * DINER_SCALE, 0.15 + LIFT * 0 - lowest * DINER_SCALE)) @ \
    Matrix.Scale(DINER_SCALE, 4)

# The kit's normal maps are glTF's, OpenGL's: prepare_prop.py turns a picture named so over for AC
for image in bpy.data.images:
    if image.name.startswith("T_") and image.name.endswith("_Normal"):
        image.name += "_gl"

# The kit's models were only there to copy: the copies keep their meshes
for o in list(library.objects):
    bpy.data.objects.remove(o)
bpy.data.collections.remove(library)

# The roof sign stands on the diner's roof at its front, the pole sign at the lot's east corner
top = max((diner_mesh.matrix_world @ v.co).z for v in diner_mesh.data.vertices)
board("sign_roof", (10.0, 0.3, 2.5), (0.0, LOT_BACK + 2.5, top + 0.6 + 1.25), roof_board, steel)
for lx in (-3.5, 3.5):
    box("sign_leg", (0.14, 0.14, 0.7), (lx, LOT_BACK + 2.6, top + 0.3), steel)
box("sign_pole", (0.3, 0.3, 5.2), (14.0, SIDEWALK + 1.2, 0.15 + 2.6), steel)
board("sign_pylon", (3.0, 0.35, 2.4), (14.0, SIDEWALK + 1.2, 0.15 + 5.2 + 1.2), pylon_board, steel, turn=90)

bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("pieces", len(street.objects))
