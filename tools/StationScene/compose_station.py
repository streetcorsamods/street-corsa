"""
Lays the gas station out in Blender and saves it as a .blend for prepare_prop.py (then StationSceneBuilder) to make the
game's KN5 of (build_station.py runs it all):

  blender -b --python compose_station.py -- <spec.json>

The spec (build_station.py writes it): sketchfab (the unzipped Sketchfab models by id, each its scene.gltf),
polyhaven (the Poly Haven models by id, each its .gltf), textures (the folder with the ground pictures, Poly Haven
surfaces as scene_tools.poly_haven_surface names them), cards (the folder bake_cards.py filled: cards.json and the
pictures of the trees, bushes and grass), optional (the Sketchfab models that were there to unpack, by id: parked cars
and trees close by; see OPTIONAL below), turns (an optional model's turn about the vertical, degrees, by id, for one
whose nose comes out the wrong way), out (the .blend to save).

The land beyond the forecourt and the road rolls (the game's ground file follows it, so a car never drives there), with
a rise behind the station and at the far edge; grass clumps, bushes and trees stand on it as pictures on crossed
cards (bake_cards.py's), the grass on the flat land (the road's verges the whole way, the lot's edges, the driveways:
a 12 m cell of clumps to a node; the clumps on the rolling land beyond stand still) and every tree under a node named
SWAY_<n>, which the game leans in the wind (GarageRenderer.Wind); poles carry wires along the road and a service drop to
the office.

Blender's space: x along the road, y away from it (the road at -y), z up; metres; the forecourt's surface at z = 0.
The game's (AC's) space is x the same, y up, z = -y (the road towards +z): station.json's positions are the game's, and
G() below turns one into Blender's.

The layout follows the one the game had (the Bannochbrae cut), so the car's ways in and out stay: the road runs
along x at z 26.5..34 (the car drives on the right: heading +x, the lane further from the station, at 32.1); a concrete
forecourt in the grass, a driveway off the road
at each end of it (in at the west, out at the east); two pump islands run along x at z 2 and z 10 under the canopy, a
pump at each end; the car stops between them at (0.6, 4.8) facing +x, its driver's door to the island at z 2 and the
attendant on it; the office stands behind, facing the road, the air meter and the soda machine at its corner, the pole
sign out by the road at the way out; a fence behind the forecourt, bushes along it, a lamp at each driveway and poles
along the far side of the road.
"""
import json
import math
import os
import random
import re
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from foliage_alpha import attach_alpha  # noqa: E402

spec = json.load(open(sys.argv[sys.argv.index("--") + 1], encoding="utf-8"))
SKETCHFAB, POLYHAVEN, TEXTURES, OUT = spec["sketchfab"], spec["polyhaven"], spec["textures"], spec["out"]
CARDS_FOLDER = spec.get("cards")
OPTIONAL_MODELS = spec.get("optional", {})
TURNS = spec.get("turns", {})

# The same scene every build: the scatter is drawn from a seeded source
rng = random.Random(1963)

# ---- The layout, in the game's metres ----
ROAD_NEAR, ROAD_FAR = 26.5, 34.0          # the road's edges (z)
LOT_WEST, LOT_EAST = -17.0, 42.0           # the forecourt along the road (x)
LOT_BACK, LOT_FRONT = -11.0, 19.0          # the forecourt's back and front edges (z)
DRIVE_IN = ((-27.0, -9.0), (-17.0, -5.0))  # the way in: x at the road, x at the forecourt
DRIVE_OUT = ((36.0, 62.0), (30.0, 42.0))   # the way out: x at the road, x at the forecourt
GROUND = 170.0                             # the grass reaches this far every way
ISLANDS = (2.0, 10.0)                      # the pump islands' middles (z), along x from -4 to 4
ISLAND_HALF, ISLAND_WIDTH, ISLAND_HEIGHT = 4.0, 1.4, 0.15
PUMP_X = (-1.6, 1.6)
COLUMN_X = (-3.7, 3.7)
CANOPY = (-7.0, 7.0, -0.75, 12.75)         # x0, x1, z0, z1
CANOPY_UNDER, CANOPY_TOP = 4.3, 4.85
OFFICE_Z = -6.4                            # the office model's origin (its front apron then ends at z -2)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
library = bpy.data.collections.new("library")
station = bpy.data.collections.new("station")
scene.collection.children.link(station)


def G(x, z):
    """A point of the game's forecourt (x, z) in Blender's (x, y)"""
    return x, -z


def same_name(name):
    base, dot, tail = name.rpartition(".")
    return base if dot and tail.isdigit() else name


def dedupe():
    """Each model brings its own copy of a material or picture it shares with another: one of each name is kept"""
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


def unparent(objects):
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


def decimate(o, ratio):
    m = o.modifiers.new("decimate", 'DECIMATE')
    m.ratio = ratio
    m.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def triangles(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def bounds(o):
    """The corners of the box round an object's vertices (its cached bound_box lags behind a change to its mesh)"""
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


pieces = {}


def names_of(o):
    """An object's name and its parents', nearest first"""
    found = []
    while o is not None:
        found.append(o.name)
        o = o.parent
    return found


def import_meshes(name, path, keep=None, drop=None):
    """
    A model's meshes imported into the scene, unparented, the rest of the import (its rigs, their shapes, the empties)
    gone: those matching keep, less those matching drop, regular expressions on a mesh's name or its parents'. Nothing
    kept raises, with nothing of the import left behind for the export
    """
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = set(bpy.data.objects) - before

    def matches(o, patterns):
        return any(re.search(p, n) for p in patterns for n in names_of(o))

    rigs = [o for o in added if o.type == 'ARMATURE']
    shapes = {pb.custom_shape for rig in rigs for pb in rig.pose.bones if pb.custom_shape}
    meshes = [o for o in added if o.type == 'MESH' and o not in shapes and not o.hide_render
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


def load_piece(name, path, keep=None, drop=None, **how):
    """
    A model as one mesh in the library, to copy from: its meshes (those matching keep, less those matching drop,
    regular expressions on a mesh's name or its parents'), made a piece as make_piece says
    """
    return make_piece(name, import_meshes(name, path, keep, drop), path, **how)


def make_piece(name, meshes, path, scale=1.0, turn=0.0, budget=None, stand=True, centre=True, own_materials=False):
    """
    Imported meshes (import_meshes) as one mesh in the library, to copy from: scaled, turned about the vertical
    (degrees), brought down to a triangle budget, stood on its base (its lowest point at z 0) with the middle of its
    footprint at the origin. own_materials keeps its materials and their pictures apart from every other model's by
    naming them after it: two models naming a material "Body", or a picture "Body_baseColor.png", are otherwise taken
    for one (dedupe), and the second wears the first's
    """
    if own_materials:
        for o in meshes:
            for slot in o.material_slots:
                material = slot.material
                if material is None:
                    continue
                if not material.name.startswith(name + "_"):
                    material.name = f"{name}_{same_name(material.name)}"
                if material.use_nodes:
                    for node in material.node_tree.nodes:
                        if node.type == 'TEX_IMAGE' and node.image and not node.image.name.startswith(name + "_"):
                            node.image.name = f"{name}_{same_name(node.image.name)}"
    # Leaves cut out by their alpha (a Poly Haven model's alpha map put into its picture), never blended:
    # prepare_prop.py cuts a material whose alpha is its picture's. A blended material with no picture to cut by keeps
    # its blending: that is how prepare_prop.py tells glass (a car's windows, see-through) from a pane to draw
    attach_alpha(meshes, path)
    for o in meshes:
        for slot in o.material_slots:
            material = slot.material
            if material is None or not material.use_nodes:
                continue
            bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
            if bsdf is not None and bsdf.inputs['Alpha'].links:
                material.surface_render_method = 'DITHERED'
    piece = join(meshes, name)
    if piece.data.shape_keys:
        piece.shape_key_clear()
    piece.data.transform(Matrix.Rotation(math.radians(turn), 4, 'Z') @ Matrix.Scale(scale, 4))
    # Blender's decimation can stop well short of the ratio asked for (a boulder's 66k came down to 27k, not 4k),
    # so it goes round again while the piece is over its budget
    for _ in range(3):
        if not budget or triangles(piece) <= budget * 1.15:
            break
        decimate(piece, budget / triangles(piece))
    lo, hi = bounds(piece)
    shift = Vector((-(lo.x + hi.x) / 2 if centre else 0, -(lo.y + hi.y) / 2 if centre else 0, -lo.z if stand else 0))
    piece.data.transform(Matrix.Translation(shift))
    shelve(piece)
    dedupe()
    print(f"PIECE {name}: {triangles(piece)} triangles, {tuple(round(v, 2) for v in (hi - lo))} m")
    return piece


def put(piece, x, z, y=0.0, heading=0.0, tilt=0.0, scale=1.0, name=None):
    """A copy of a library piece at the game's (x, z), y up off the ground, turned by heading (degrees about the vertical, the game's way) and tipped forward by tilt"""
    o = piece.copy()
    bx, by = G(x, z)
    o.matrix_world = (Matrix.Translation((bx, by, y)) @ Matrix.Rotation(math.radians(heading), 4, 'Z')
                      @ Matrix.Rotation(math.radians(tilt), 4, 'X') @ Matrix.Scale(scale, 4))
    o.name = name or f"{piece.name}_at_{len(station.objects)}"
    station.objects.link(o)
    return o


def flat(name, colour, rough=0.6, metal=0.0, emission=None):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 1.0
    return material


def pictured(name, surface, texture, rough=0.8):
    """A material of a Poly Haven surface picture (scene_tools.poly_haven_surface's file)"""
    material = flat(name, (1.0, 1.0, 1.0), rough=rough)
    tree = material.node_tree
    node = tree.nodes.new('ShaderNodeTexImage')
    node.image = bpy.data.images.load(os.path.join(TEXTURES, f"{surface}_{texture}.jpg"))
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
    station.objects.link(o)
    return o


def ground(name, x0, x1, z0, z1, height, material, tile):
    """A flat surface over the game's x0..x1, z0..z1 at height, its picture every tile metres"""
    (bx0, by0), (bx1, by1) = G(x0, z1), G(x1, z0)
    v = [(bx0, by0, height), (bx1, by0, height), (bx1, by1, height), (bx0, by1, height)]
    uv = [(p[0] / tile, p[1] / tile) for p in v]
    return mesh_object(name, v, [(0, 1, 2, 3)], [uv], [material], [0])


def box(name, x0, x1, z0, z1, y0, y1, material, tile=None, materials=None):
    """A box over the game's x0..x1, z0..z1 from y0 up to y1; its picture every tile metres, or once a face; materials: one a face (front -z... as cuboid's order) when given"""
    (bx0, by0), (bx1, by1) = G(x0, z1), G(x1, z0)
    v = [(bx0, by0, y0), (bx1, by0, y0), (bx1, by0, y1), (bx0, by0, y1), (bx0, by1, y0), (bx1, by1, y0), (bx1, by1, y1), (bx0, by1, y1)]
    faces = [(0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0)]
    w, d, h = bx1 - bx0, by1 - by0, y1 - y0
    spans = [(w, h), (w, h), (d, h), (d, h), (w, d), (w, d)]
    uvs = [[(0, 0), (su / tile, 0), (su / tile, sv / tile), (0, sv / tile)] if tile else [(0, 0), (1, 0), (1, 1), (0, 1)] for su, sv in spans]
    mats = materials or [material]
    slots = list(range(len(mats))) if materials else [0] * 6
    return mesh_object(name, v, faces, uvs, mats, slots)


def cylinder(name, x, z, y0, y1, radius, material, sides=20):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=y1 - y0, location=(*G(x, z), (y0 + y1) / 2))
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(material)
    for c in list(o.users_collection):
        c.objects.unlink(o)
    station.objects.link(o)
    return o


# ---- The ground: grass all round, the forecourt, the road with its lines, the kerbs ----
grass = pictured("st_grass", "grass", "aerial_grass_rock", rough=0.95)
forecourt = pictured("st_forecourt", "forecourt", "concrete_pavement", rough=0.85)
asphalt = pictured("st_road", "road", "asphalt_02", rough=0.9)
concrete = pictured("st_concrete", "island", "painted_concrete", rough=0.7)
yellow = flat("st_line_yellow", (0.75, 0.55, 0.05), rough=0.8)
white = flat("st_line_white", (0.85, 0.85, 0.8), rough=0.8)

# ---- The land: flat where the car goes, rolling beyond, rising at the back and at the far edge ----
# Where the ground stays flat (the game's x0, x1, z0, z1): the forecourt with its fence and a margin, the road the whole
# way with its shoulders, the driveways; the hills begin over BLEND metres beyond these
FLAT = ((LOT_WEST - 7.0, LOT_EAST + 7.0, LOT_BACK - 9.0, LOT_FRONT + 2.0),
        (-GROUND, GROUND, ROAD_NEAR - 5.0, ROAD_FAR + 5.0),
        (DRIVE_IN[0][0] - 4.0, DRIVE_IN[1][1] + 4.0, LOT_FRONT - 1.0, ROAD_NEAR + 1.0),
        (DRIVE_OUT[1][0] - 4.0, DRIVE_OUT[0][1] + 4.0, LOT_FRONT - 1.0, ROAD_NEAR + 1.0))
BLEND = 24.0
LAND_STEP = 4.0                            # the land's mesh, a vertex every so many metres
LAND_BELOW = -0.02                         # the grass under the forecourt's and the road's faces


def outside(x, z):
    """How far a point is from the nearest flat part of the land (FLAT); 0 inside one"""
    best = None
    for x0, x1, z0, z1 in FLAT:
        d = math.hypot(max(x0 - x, 0.0, x - x1), max(z0 - z, 0.0, z - z1))
        best = d if best is None else min(best, d)
    return best


def smoothstep(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def lattice(ix, iz, seed):
    """A number in 0..1 that is always the same for a lattice point"""
    n = (ix * 73856093) ^ (iz * 19349663) ^ (seed * 83492791)
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def value_noise(x, z, size, seed):
    """Smooth noise in -1..1, a hump about every size metres"""
    fx, fz = x / size, z / size
    ix, iz = math.floor(fx), math.floor(fz)
    tx, tz = smoothstep(fx - ix), smoothstep(fz - iz)
    a, b = lattice(ix, iz, seed), lattice(ix + 1, iz, seed)
    c, d = lattice(ix, iz + 1, seed), lattice(ix + 1, iz + 1, seed)
    return 2.0 * ((a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz) - 1.0


def land(x, z):
    """The land's height at the game's (x, z): 0 where the car goes, rolling beyond, a rise at the back, beyond the road and at the edge"""
    d = outside(x, z)
    if d <= 0.0:
        return 0.0
    rolling = 1.7 * value_noise(x, z, 31.0, 1) + 0.6 * value_noise(x, z, 11.0, 2) + 0.6
    back = 7.0 * max(0.0, (-z - 60.0) / 110.0) ** 1.6
    far = 5.0 * max(0.0, (z - 90.0) / 80.0) ** 1.6
    rim = 6.0 * max(0.0, (max(abs(x), abs(z)) - 130.0) / 40.0) ** 2
    return smoothstep(d / BLEND) * (rolling + back + far + rim)


def paved(x, z):
    """True on the forecourt, the road with its shoulders or a driveway: nothing grows there"""
    if LOT_WEST - 0.5 <= x <= LOT_EAST + 0.5 and LOT_BACK - 0.5 <= z <= LOT_FRONT + 0.5:
        return True
    if ROAD_NEAR - 1.8 <= z <= ROAD_FAR + 1.8:
        return True
    for (rx0, rx1), (lx0, lx1) in (DRIVE_IN, DRIVE_OUT):
        if LOT_FRONT - 0.5 <= z <= ROAD_NEAR:
            t = (z - LOT_FRONT) / (ROAD_NEAR - LOT_FRONT)
            if lx0 + (rx0 - lx0) * t - 0.8 <= x <= lx1 + (rx1 - lx1) * t + 0.8:
                return True
    return False


LAND_CELLS = int(round(2 * GROUND / LAND_STEP))
LAND_HEIGHTS = [[land(-GROUND + i * LAND_STEP, -GROUND + j * LAND_STEP) + LAND_BELOW for i in range(LAND_CELLS + 1)] for j in range(LAND_CELLS + 1)]


def land_triangles(cells):
    """
    The land's faces over the given cells (i, j: along x and z), as the mesh has them: each cell two triangles, cut
    from its (x0, z0) corner to its (x1, z1) one, so mesh_land() and the shades laid on the land agree with it
    """
    vertices, faces, uvs = [], [], []
    for i, j in cells:
        x0, z0 = -GROUND + i * LAND_STEP, -GROUND + j * LAND_STEP
        x1, z1 = x0 + LAND_STEP, z0 + LAND_STEP
        corners = [(x0, z0, LAND_HEIGHTS[j][i]), (x0, z1, LAND_HEIGHTS[j + 1][i]), (x1, z1, LAND_HEIGHTS[j + 1][i + 1]), (x1, z0, LAND_HEIGHTS[j][i + 1])]
        base = len(vertices)
        vertices.extend((*G(x, z), h) for x, z, h in corners)
        # Anticlockwise seen from above, so the faces look up
        faces.append((base, base + 1, base + 2))
        faces.append((base, base + 2, base + 3))
        uvs.append([(corners[k][0], corners[k][1]) for k in (0, 1, 2)])
        uvs.append([(corners[k][0], corners[k][1]) for k in (0, 2, 3)])
    return vertices, faces, uvs


def mesh_land(x, z):
    """The land mesh's height at the game's (x, z): the triangle under it, as land_triangles() cuts the cell"""
    fx, fz = (x + GROUND) / LAND_STEP, (z + GROUND) / LAND_STEP
    i, j = min(max(int(math.floor(fx)), 0), LAND_CELLS - 1), min(max(int(math.floor(fz)), 0), LAND_CELLS - 1)
    tx, tz = min(max(fx - i, 0.0), 1.0), min(max(fz - j, 0.0), 1.0)
    h00, h01, h11, h10 = LAND_HEIGHTS[j][i], LAND_HEIGHTS[j + 1][i], LAND_HEIGHTS[j + 1][i + 1], LAND_HEIGHTS[j][i + 1]
    if tz >= tx:
        return h00 + (h01 - h00) * tz + (h11 - h01) * tx
    return h00 + (h10 - h00) * tx + (h11 - h10) * tz


def land_mesh(name, material, tile):
    """The land over the game's square, a vertex every LAND_STEP metres at land()'s height, shaded smooth, its picture every tile metres"""
    vertices, faces, uvs = land_triangles([(i, j) for j in range(LAND_CELLS) for i in range(LAND_CELLS)])
    uvs = [[(u / tile, -v / tile) for u, v in corners] for corners in uvs]
    o = mesh_object(name, vertices, faces, uvs, [material], [0] * len(faces))
    o.data.validate()
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.001)
    bpy.ops.object.mode_set(mode='OBJECT')
    for p in o.data.polygons:
        p.use_smooth = True
    return o


land_mesh("LAND", grass, 7.0)
ground("FORECOURT", LOT_WEST, LOT_EAST, LOT_BACK, LOT_FRONT, 0.0, forecourt, 3.0)
ground("ROAD", -GROUND, GROUND, ROAD_NEAR, ROAD_FAR, 0.0, asphalt, 6.0)
# A gravel shoulder where the road meets the grass: the forecourt's concrete, tiled small
ground("SHOULDER_FAR", -GROUND, GROUND, ROAD_FAR, ROAD_FAR + 1.5, -0.005, forecourt, 3.0)
ground("SHOULDER_NEAR", -GROUND, GROUND, ROAD_NEAR - 1.5, ROAD_NEAR, -0.005, forecourt, 3.0)


def driveway(name, at_road, at_lot):
    """An asphalt way between the road's edge and the forecourt's front, wider at the road"""
    (rx0, rx1), (lx0, lx1) = at_road, at_lot
    # Round anticlockwise seen from above, so the face looks up
    v = [(*G(rx0, ROAD_NEAR), 0.0), (*G(rx1, ROAD_NEAR), 0.0), (*G(lx1, LOT_FRONT), 0.0), (*G(lx0, LOT_FRONT), 0.0)]
    uv = [(p[0] / 6.0, p[1] / 6.0) for p in v]
    return mesh_object(name, v, [(0, 1, 2, 3)], [uv], [asphalt], [0])


driveway("DRIVE_IN", *DRIVE_IN)
driveway("DRIVE_OUT", *DRIVE_OUT)

# The road's lines: a broken yellow line down its middle, a white one along each edge away from the forecourt
mid = (ROAD_NEAR + ROAD_FAR) / 2
x = -GROUND
while x < GROUND:
    box(f"LINE_MID_{int(x)}", x, x + 3.0, mid - 0.06, mid + 0.06, 0.0, 0.004, yellow)
    x += 9.0
box("LINE_FAR", -GROUND, GROUND, ROAD_FAR - 0.25, ROAD_FAR - 0.13, 0.0, 0.004, white)
box("LINE_NEAR_WEST", -GROUND, DRIVE_IN[0][0], ROAD_NEAR + 0.13, ROAD_NEAR + 0.25, 0.0, 0.004, white)
box("LINE_NEAR_MID", DRIVE_IN[0][1], DRIVE_OUT[0][0], ROAD_NEAR + 0.13, ROAD_NEAR + 0.25, 0.0, 0.004, white)
box("LINE_NEAR_EAST", DRIVE_OUT[0][1], GROUND, ROAD_NEAR + 0.13, ROAD_NEAR + 0.25, 0.0, 0.004, white)

# ---- The pump islands, the canopy on its columns, the fittings under it ----
for i, z in enumerate(ISLANDS):
    box(f"ISLAND_{i}", -ISLAND_HALF, ISLAND_HALF, z - ISLAND_WIDTH / 2, z + ISLAND_WIDTH / 2, -0.02, ISLAND_HEIGHT, concrete, tile=1.0)

cream = flat("st_canopy", (0.86, 0.82, 0.68), rough=0.55)
red = flat("st_canopy_stripe", (0.55, 0.08, 0.06), rough=0.5)
underside = flat("st_canopy_under", (0.78, 0.78, 0.74), rough=0.7)
steel = flat("st_steel", (0.72, 0.72, 0.68), rough=0.4, metal=0.3)
x0, x1, z0, z1 = CANOPY
box("CANOPY", x0, x1, z0, z1, CANOPY_UNDER, CANOPY_TOP, cream, materials=[cream, cream, cream, cream, cream, underside])
box("CANOPY_STRIPE", x0 - 0.03, x1 + 0.03, z0 - 0.03, z1 + 0.03, CANOPY_UNDER + 0.2, CANOPY_UNDER + 0.34, red)
for z in ISLANDS:
    for cx in COLUMN_X:
        cylinder(f"COLUMN_{cx}_{z}", cx, z, ISLAND_HEIGHT, CANOPY_UNDER, 0.17, steel)
tube = flat("st_tube", (0.95, 0.95, 0.92), rough=0.3, emission=(1.0, 1.0, 0.95))
fitting = flat("st_fitting", (0.9, 0.9, 0.88), rough=0.5, metal=0.2)
for fx in (-3.6, 0.0, 3.6):
    for fz in (4.0, 8.0):
        box(f"FITTING_{fx}_{fz}", fx - 0.65, fx + 0.65, fz - 0.16, fz + 0.16, CANOPY_UNDER - 0.1, CANOPY_UNDER, fitting)
        box(f"TUBE_{fx}_{fz}", fx - 0.6, fx + 0.6, fz - 0.12, fz + 0.12, CANOPY_UNDER - 0.13, CANOPY_UNDER - 0.1, tube)

# ---- The office (Wardster's), its decals of neglect left off, where its front apron ends at z -2 ----
office = load_piece("office", SKETCHFAB["vintage_gas_station"], drop=[r"Decal"], stand=False, centre=False)
put(office, 0.0, OFFICE_Z, name="OFFICE")

# ---- The pumps (Rescue3D's), the air meter (gorzi's), the soda machine (Han1993's), the pole sign (Mark Peters') ----
pump = load_piece("pump", SKETCHFAB["vintage_gas_pump"], turn=spec.get("pump_turn", 0.0))
for i, z in enumerate(ISLANDS):
    for px in PUMP_X:
        put(pump, px, z, ISLAND_HEIGHT, heading=0.0 if i == 0 else 180.0)

inflator = load_piece("air_meter", SKETCHFAB["old_tire_inflator"], keep=[r"^Object_4$", r"^Object_6$"], scale=0.28, budget=4000,
                      turn=spec.get("air_meter_turn", 0.0))
put(inflator, 8.4, -1.2, heading=0.0)

soda = load_piece("soda_machine", SKETCHFAB["vintage_soda_machien"], scale=0.01, turn=spec.get("soda_turn", 0.0))
put(soda, 4.3, -3.4, 0.05, heading=0.0)

sign = load_piece("pole_sign", SKETCHFAB["gasstation_sign"], turn=90.0)
put(sign, 33.0, 22.5)

# ---- Poly Haven's props (CC0): the dressing round the office and the islands ----
def haven(name, pid, **kw):
    return load_piece(name, POLYHAVEN[pid], **kw)


bench = haven("bench", "painted_wooden_bench")
put(bench, -4.6, -3.2, 0.05, heading=0.0)
trash = haven("trash_can", "metal_trash_can", keep=[r"^metal_trash_can$", r"handle_left$", r"handle_right$", r"^metal_trash_can_lid$"], drop=[r"rust"])
put(trash, 2.2, -3.3, 0.05)
board = haven("chalkboard", "standing_chalkboard_01")
put(board, -6.2, 0.9, heading=20.0)
rack = haven("oil_rack", "steel_frame_shelves_01", scale=0.1)
put(rack, 2.75, 10.0, ISLAND_HEIGHT, heading=180.0)
rack_top = bounds(rack)[1].z * 1.0
tin = haven("oil_tin", "oil_tin")
for k, (tx, tz) in enumerate(((2.55, 9.85), (2.75, 10.1), (2.95, 9.9))):
    put(tin, tx, tz, ISLAND_HEIGHT + rack_top, heading=30.0 * k)
tyre = haven("tyre", "old_tyre")
for k in range(4):
    put(tyre, 9.1, -8.2, 0.16 * k, heading=25.0 * k, tilt=90.0)
rim = haven("rim", "rusted_wheel_rim_02", budget=4000)
put(rim, 9.6, -9.1, 0.0, heading=100.0, tilt=80.0)
barrel = haven("barrel", "Barrel_01")
put(barrel, 9.2, -10.6)
put(barrel, 9.9, -10.9, heading=50.0)
jerrycan = haven("jerrycan", "metal_jerrycan", budget=5000)
put(jerrycan, 8.6, -9.6, heading=-20.0)
crate = haven("crate", "wooden_crate_01")
put(crate, 9.3, -12.3, heading=10.0)
put(crate, 9.3, -12.3, 0.34, heading=-8.0)
truck = haven("hand_truck", "hand_truck", budget=6000)
put(truck, -3.2, -14.6, heading=170.0)
covered = haven("covered_car", "covered_car")
put(covered, -13.5, -11.0, heading=15.0)
utility = haven("utility_box", "utility_box_01")
put(utility, -7.6, -6.5, heading=90.0)
picnic = haven("picnic_table", "wooden_picnic_table", budget=5000)
put(picnic, -11.0, -1.5, heading=80.0)
lamp = haven("street_lamp", "street_lamp_01", budget=8000)
put(lamp, DRIVE_IN[0][0] - 1.5, ROAD_NEAR - 2.5)
put(lamp, DRIVE_OUT[0][1] + 1.5, ROAD_NEAR - 2.5)

# The fence along the back of the lot: panels between posts
panel = haven("fence_panel", "modular_chainlink_fence", keep=[r"^modular_chainlink_fence_double$"], stand=False, budget=700)
post = haven("fence_post", "modular_chainlink_fence", keep=[r"^modular_chainlink_fence_post$"], stand=False, budget=240)
panel_width = bounds(panel)[1].x - bounds(panel)[0].x
x = LOT_WEST - 3.0
while x + panel_width <= LOT_EAST + 3.1:
    put(post, x, LOT_BACK - 1.0)
    put(panel, x + panel_width / 2, LOT_BACK - 1.0, heading=0.0)
    x += panel_width + 0.05
put(post, x, LOT_BACK - 1.0)

# Bushes along the fence (the nearest ones real, Poly Haven's shrubs) and small bushes and weeds in the grass about the lot
bush = haven("bush", "shrub_02", keep=[r"shrub_02_b$"], budget=2200)
bush_big = haven("bush_big", "shrub_02", keep=[r"shrub_02_c$"], budget=3200)
for k, bx in enumerate(range(int(LOT_WEST), int(LOT_EAST) + 3, 8)):
    put(bush if k % 2 else bush_big, bx + (k % 3), LOT_BACK - 2.6, heading=47.0 * k)


def on_land(x, z):
    """The land's height under a plant, a little under it so its base is in the ground, not on it"""
    return mesh_land(x, z) - 0.04


# A shade on the land under each tree (the renderer cannot cut a card's shadow by its alpha, so a tree on a card casts
# none): a soft dark disc, blended, laid over the land's own triangles so the land never cuts through it, a little
# towards where the sun's shadow falls
SHADE_PICTURE = os.path.join(os.path.dirname(OUT), "shade.png")


def shade_picture(path, size=128):
    image = bpy.data.images.new("shade", size, size, alpha=True)
    pixels = []
    for j in range(size):
        for i in range(size):
            r = math.hypot((i + 0.5) / size * 2 - 1, (j + 0.5) / size * 2 - 1)
            pixels.extend((0.0, 0.0, 0.0, max(0.0, 1.0 - r) ** 1.4 * 0.6))
    image.pixels = pixels
    image.filepath_raw = path
    image.file_format = 'PNG'
    image.save()
    return image


shade_material = flat("st_shade", (0.0, 0.0, 0.0), rough=1.0)
_shade_tree = shade_material.node_tree
_shade_node = _shade_tree.nodes.new('ShaderNodeTexImage')
_shade_node.image = shade_picture(SHADE_PICTURE)
_shade_tree.links.new(_shade_node.outputs['Color'], _shade_tree.nodes["Principled BSDF"].inputs['Base Color'])
_shade_tree.links.new(_shade_node.outputs['Alpha'], _shade_tree.nodes["Principled BSDF"].inputs['Alpha'])
shade_material.surface_render_method = 'BLENDED'
shade_material["blend"] = True
SHADE_WAY = (0.575, -0.818)      # where the sun's shadow falls, on the ground (the station's sun is [-0.45, 0.62, 0.64])
shade_count = 0


def shade(x, z, radius):
    """A shade of the radius on the land about (x, z), shifted a little the sun's shadow's way"""
    global shade_count
    cx, cz = x + SHADE_WAY[0] * radius * 0.3, z + SHADE_WAY[1] * radius * 0.3
    i0, i1 = int(math.floor((cx - radius + GROUND) / LAND_STEP)), int(math.floor((cx + radius + GROUND) / LAND_STEP))
    j0, j1 = int(math.floor((cz - radius + GROUND) / LAND_STEP)), int(math.floor((cz + radius + GROUND) / LAND_STEP))
    cells = [(i, j) for j in range(max(j0, 0), min(j1, LAND_CELLS - 1) + 1) for i in range(max(i0, 0), min(i1, LAND_CELLS - 1) + 1)]
    if not cells:
        return None
    vertices, faces, uvs = land_triangles(cells)
    vertices = [(vx, vy, vz + 0.06) for vx, vy, vz in vertices]
    uvs = [[((u - cx) / (2 * radius) + 0.5, (v - cz) / (2 * radius) + 0.5) for u, v in corners] for corners in uvs]
    shade_count += 1
    return mesh_object(f"SHADE_{shade_count}", vertices, faces, uvs, [shade_material], [0] * len(faces))


def scatter(pieces, count, x0, x1, z0, z1, clear=0.0, scale=(1.0, 1.0), near=None, tries=40):
    """
    count copies of the pieces (one each, in turn) at random over the game's x0..x1, z0..z1, off anything paved and
    at least clear metres out of the flat ground round the lot and the road; within near metres of the forecourt's
    middle when given. Each turned any way and scaled between scale's two
    """
    placed = []
    for k in range(count):
        for _ in range(tries):
            x, z = rng.uniform(x0, x1), rng.uniform(z0, z1)
            if paved(x, z) or outside(x, z) < clear:
                continue
            if near is not None and math.hypot(x - 12.0, z - 4.0) > near:
                continue
            placed.append(put(pieces[k % len(pieces)], x, z, on_land(x, z), heading=rng.uniform(0.0, 360.0), scale=rng.uniform(*scale)))
            break
    return placed


try:
    weeds = [haven(f"weed_{v}", "shrub_03", keep=[rf"shrub_03_{v}$"], budget=700) for v in "abcd"]
    scatter(weeds, 70, LOT_WEST - 40, LOT_EAST + 40, LOT_BACK - 35, ROAD_FAR + 12, scale=(1.2, 2.2), near=60)
    rooibos = [haven(f"rooibos_{v}", "wild_rooibos_bush", keep=[rf"wild_rooibos_bush_{v}$"], budget=1500) for v in "ab"]
    scatter(rooibos, 18, LOT_WEST - 40, LOT_EAST + 40, LOT_BACK - 30, ROAD_FAR + 12, scale=(1.3, 2.0), near=55)
    # Blender's decimation leaves the boulder at 26k triangles however often it goes round (a scan of 66k), so two
    boulder = haven("boulder", "boulder_01", budget=4000)
    for bx, bz, bh in ((-62.0, -48.0, 20.0), (130.0, 90.0, 200.0)):
        put(boulder, bx, bz, mesh_land(bx, bz) - 0.25, heading=bh, scale=rng.uniform(1.2, 1.8))
    log = haven("log", "dead_tree_trunk_02", budget=5000)
    put(log, -40.0, -30.0, on_land(-40.0, -30.0), heading=25.0)
    put(log, 112.0, 62.0, on_land(112.0, 62.0), heading=-60.0, scale=1.2)
    stump = haven("stump", "tree_stump_01", budget=2500)
    put(stump, -44.0, -34.0, on_land(-44.0, -34.0), heading=120.0)
    put(stump, 60.0, -46.0, on_land(60.0, -46.0), heading=10.0)
except RuntimeError as ex:
    print(f"SCATTER left out: {ex}")

# ---- The cards: trees, bushes and grass as pictures on crossed quads (bake_cards.py), seen from both sides, lit as the
# ground is (their normals straight up), so a side of a tree is never in its own shade ----
cards = {}
if CARDS_FOLDER and os.path.isfile(os.path.join(CARDS_FOLDER, "cards.json")):
    cards = json.load(open(os.path.join(CARDS_FOLDER, "cards.json"), encoding="utf-8"))


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


card_materials = {}


def card_piece(name, card_name):
    """
    A card as a library piece: its front picture on a quad across x and, when it has a side picture, that on a quad
    across z, each span metres square standing on its bottom edge at the origin, with a back face to each so it shows
    from behind
    """
    card = cards[card_name]
    s = card["span"]
    materials = []
    for view in ("front", "side"):
        if view not in card:
            continue
        key = f"{card_name}_{view}"
        if key not in card_materials:
            card_materials[key] = card_material(f"st_card_{key}", card[view])
        materials.append(card_materials[key])
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
    quad([(-s / 2, 0, 0), (s / 2, 0, 0), (s / 2, 0, s), (-s / 2, 0, s)], 0)
    if len(materials) > 1:
        quad([(0, -s / 2, 0), (0, s / 2, 0), (0, s / 2, s), (0, -s / 2, s)], 1)
    o = mesh_object(name, vertices, faces, uvs, materials, slots)
    o.data.normals_split_custom_set_from_vertices([(0.0, 0.0, 1.0)] * len(o.data.vertices))
    shelve(o)
    return o


def group(objects, name):
    """Several copies as one object of the name (for the game to lean as one), the first's mesh its own first"""
    objects[0].data = objects[0].data.copy()
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = name
    return joined


sway_count = 0


def sways(objects):
    """The objects as one node the game leans in the wind"""
    global sway_count
    sway_count += 1
    return group(objects, f"SWAY_{sway_count}")


# The trees nearest the lot: real ones from a tree pack when one was downloaded (OPTIONAL, below), cards otherwise
NEAR_TREES = [(-48.0, -22.0), (-56.0, 6.0), (72.0, -20.0), (78.0, 6.0), (-30.0, -42.0), (22.0, -44.0), (52.0, -40.0), (-70.0, -30.0), (96.0, -34.0)]
used_optional = []


def tree_pack(key, path, count):
    """
    Up to count plants of a pack (lolipop_1707's lilac, maple, oak and pine packs name each one's meshes <name>_LOD<n>),
    imported once: each plant's meshes at its first level of detail there, as a piece, those between 3.5 and 13 m tall
    """
    meshes = import_meshes(key, path)

    def plant_of(o):
        # The name with the level of detail in it, the mesh's or a parent's; a pack without levels names each plant
        labelled = next((n for n in names_of(o) if "LOD" in n), None)
        return re.sub(r"_LOD\d.*$", "", labelled) if labelled else same_name(o.name)

    def level_of(o):
        found = next((re.search(r"_LOD(\d)", n) for n in names_of(o) if "LOD" in n), None)
        return int(found.group(1)) if found else 0

    plants = {}
    for o in meshes:
        plants.setdefault(plant_of(o), []).append(o)
    # The full ones first: a "bush" of the lilac pack is a 4 m shrub, its "single tree" a sapling
    names = sorted(plants, key=lambda n: (0 if "bush" in n.lower() and "small" not in n.lower() else 1 if "tree" in n.lower() else 2, n))
    pieces = []
    for tree_name in names:
        group = plants[tree_name]
        rest = group
        if len(pieces) < count:
            first = min(level_of(o) for o in group)
            chosen = [o for o in group if level_of(o) == first]
            rest = [o for o in group if o not in chosen]
            piece = make_piece(f"{key}_{tree_name}", chosen, path, budget=30000)
            lo, hi = bounds(piece)
            if 3.5 <= hi.z - lo.z <= 13.0:
                pieces.append(piece)
            else:
                bpy.data.objects.remove(piece)
        for o in rest:
            bpy.data.objects.remove(o)
    return pieces


real_trees = []
for key in ("lilac_pack", "maple_pack", "oak_pack"):
    path = OPTIONAL_MODELS.get(key)
    if not path:
        continue
    try:
        found = tree_pack(key, path, 3)
        print(f"OPTIONAL {key}: {len(found)} trees")
        real_trees.extend(found)
        if found:
            used_optional.append(key)
    except RuntimeError as ex:
        print(f"OPTIONAL {key} left out: {ex}")

if cards:
    tree_card = card_piece("tree_card", "tree")
    bush_cards = [card_piece(f"bush_card_{n}", n) for n in ("bush_a", "bush_b", "shrub") if n in cards]
    grass_cards = [card_piece(f"grass_card_{n}", n) for n in ("grass_c", "grass_d", "grass_e") if n in cards]

    def tree(x, z, scale):
        """A tree card at the game's (x, z), its foot a little into the ground, a shade on the land under it; none on or by anything paved"""
        if paved(x, z) or outside(x, z) < 4.0:
            return None
        shade(x, z, cards["tree"]["span"] * scale * 0.42)
        return sways([put(tree_card, x, z, mesh_land(x, z) - 0.12 * scale, heading=rng.uniform(0.0, 360.0), scale=scale)])

    # A line of trees along the rise behind the station, two rows deep
    for row, (z_from, z_to) in enumerate(((-74.0, -86.0), (-92.0, -112.0))):
        x = -GROUND + rng.uniform(0.0, 8.0)
        while x < GROUND:
            tree(x, rng.uniform(z_from, z_to), rng.uniform(2.1, 3.0))
            x += rng.uniform(9.0, 15.0)
    # Clumps of trees beyond the road, and along the sides
    for cx, cz, n in ((-140.0, 95.0, 6), (-95.0, 118.0, 7), (-40.0, 88.0, 5), (12.0, 112.0, 7), (58.0, 82.0, 5), (108.0, 100.0, 7), (150.0, 128.0, 6), (-8.0, 150.0, 6),
                      (-135.0, -20.0, 5), (-125.0, 60.0, 4), (125.0, 62.0, 5), (145.0, -40.0, 5), (-150.0, 130.0, 5), (85.0, 140.0, 5)):
        for _ in range(n):
            tree(cx + rng.uniform(-14.0, 14.0), cz + rng.uniform(-12.0, 12.0), rng.uniform(1.9, 2.9))
    for tx, tz in NEAR_TREES:
        tree(tx, tz, rng.uniform(2.0, 2.6))
    # Bushes in the grass, nearer the lot the smaller ones; cards every way beyond
    for piece in bush_cards:
        for o in scatter([piece], 14, LOT_WEST - 60, LOT_EAST + 60, LOT_BACK - 60, ROAD_FAR + 50, clear=3.0, scale=(0.9, 1.4)):
            sways([o])
        for o in scatter([piece], 22, -GROUND + 10, GROUND - 10, -GROUND + 10, GROUND - 10, clear=20.0, scale=(1.0, 1.6)):
            sways([o])

    # Grass: clumps along the road's verges and the lot's edges, then scattered thinner and thinner out to the edge; the
    # nearer ones in groups of a cell each, which the game leans in the wind
    cell = 12.0
    near_tufts = {}

    def tuft(x, z, scale):
        """A clump of grass; the pictures are of small clumps, so they stand half as big again"""
        o = put(grass_cards[rng.randrange(len(grass_cards))], x, z, mesh_land(x, z) - 0.03, heading=rng.uniform(0.0, 360.0), scale=scale * 1.5)
        if outside(x, z) <= 0.0:
            near_tufts.setdefault((math.floor(x / cell), math.floor(z / cell)), []).append(o)

    x = -GROUND + 2.0
    while x < GROUND - 2.0:
        in_drive = any(rx0 - 1.0 <= x <= rx1 + 1.0 for (rx0, rx1), _ in (DRIVE_IN, DRIVE_OUT))
        if not in_drive:
            tuft(x + rng.uniform(-0.6, 0.6), ROAD_NEAR - 1.9 - rng.uniform(0.0, 2.4), rng.uniform(1.4, 2.3))
        tuft(x + rng.uniform(-0.6, 0.6), ROAD_FAR + 1.9 + rng.uniform(0.0, 2.4), rng.uniform(1.4, 2.3))
        x += 1.7
    x = LOT_WEST - 6.0
    while x < LOT_EAST + 6.0:
        tuft(x + rng.uniform(-0.5, 0.5), LOT_BACK - 1.6 - rng.uniform(0.0, 4.5), rng.uniform(1.3, 2.1))
        x += 1.5
    z = LOT_BACK - 2.0
    while z < LOT_FRONT:
        tuft(LOT_WEST - 1.2 - rng.uniform(0.0, 3.5), z + rng.uniform(-0.5, 0.5), rng.uniform(1.3, 2.1))
        tuft(LOT_EAST + 1.2 + rng.uniform(0.0, 3.5), z + rng.uniform(-0.5, 0.5), rng.uniform(1.3, 2.1))
        z += 1.5
    for count, reach, far in ((900, 0.0, 70.0), (700, 70.0, 125.0), (260, 125.0, GROUND - 3.0)):
        for _ in range(count):
            for _ in range(20):
                x, z = rng.uniform(-far, far), rng.uniform(-far, far)
                d = math.hypot(x - 12.0, z - 4.0)
                if paved(x, z) or d < reach or d > far:
                    continue
                tuft(x, z, rng.uniform(1.3, 2.2))
                break
    for objects in near_tufts.values():
        sways(objects)
    print(f"CARDS {sway_count} swaying nodes, {sum(len(v) for v in near_tufts.values())} near tufts")
else:
    print("CARDS none: no cards.json, the land stays bare")

# ---- Poles along the far side of the road, their wires, and a service drop to the office ----
wire_material = flat("st_wire", (0.06, 0.06, 0.06), rough=0.7)


def wire(name, a, b, sag, width=0.035, segments=10):
    """A wire from a to b (the game's x, z and height), hanging sag metres in its middle: two crossed strips, each with a back"""
    (ax, az, ay), (bx, bz, by) = a, b
    points = []
    for k in range(segments + 1):
        t = k / segments
        points.append(Vector((*G(ax + (bx - ax) * t, az + (bz - az) * t), ay + (by - ay) * t - sag * 4.0 * t * (1.0 - t))))
    along = (points[-1] - points[0]).normalized()
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


poles = POLYHAVEN.get("modular_electricity_poles")
if poles:
    try:
        pole = haven("pole", "modular_electricity_poles", keep=[r"^pole_large$"], stand=False, centre=True, budget=2500)
        arm = haven("pole_arm", "modular_electricity_poles", keep=[r"^connection_large_01$"], stand=False, centre=True, budget=900)
        small_arm = haven("pole_arm_small", "modular_electricity_poles", keep=[r"^connection_small_01$"], stand=False, centre=True, budget=800)
        # The crossarm lies across the line (the line runs along x), so its wires hang a little apart in z
        lo, hi = bounds(arm)
        arm_turn = 90.0 if (hi.x - lo.x) > (hi.y - lo.y) else 0.0
        arm_half = max(hi.x - lo.x, hi.y - lo.y) / 2
        ARM_UP = 6.4
        wire_up = ARM_UP + hi.z + 0.22
        line_z = ROAD_FAR + 2.5
        xs = [float(px) for px in range(-163, 170, 40)]
        for px in xs:
            put(pole, px, line_z)
            put(arm, px, line_z, ARM_UP, heading=arm_turn)
        for k in range(len(xs) - 1):
            for n, dz in enumerate((-arm_half + 0.25, 0.0, arm_half - 0.25)):
                wire(f"WIRE_{k}_{n}", (xs[k], line_z + dz, wire_up), (xs[k + 1], line_z + dz, wire_up), sag=0.9)
        # The service drop: from the line's pole past the way out, over the lot's corner to a pole behind it, and down to the office
        drop_pole = (LOT_EAST + 4.0, LOT_BACK - 5.0)
        put(pole, *drop_pole, heading=90.0)
        put(small_arm, *drop_pole, ARM_UP - 0.4, heading=90.0 + arm_turn)
        from_pole = min(xs, key=lambda px: abs(px - 37.0))
        for n, dz in enumerate((-0.3, 0.3)):
            wire(f"DROP_{n}", (from_pole, line_z + dz, wire_up), (drop_pole[0] + dz, drop_pole[1], ARM_UP - 0.2), sag=1.6)
            wire(f"DROP_OFFICE_{n}", (drop_pole[0] + dz, drop_pole[1], ARM_UP - 0.2), (7.5 + dz, OFFICE_Z - 3.0, 4.2), sag=0.7)
    except RuntimeError as ex:
        print(f"POLES left out: {ex}")

# ---- Optional models (Sketchfab, downloaded by hand when wanted; build_station.py's OPTIONAL): parked cars and trucks,
# real trees close by in place of the cards' spots ----
VEHICLES = {
    # id: where it stands (the game's x, z), which way it faces (0 along +z, towards the road), how long it is (metres),
    # and the meshes of the model to leave out (a label, a display plinth), regular expressions on their names
    "chevrolet_c10_1963": ((16.5, -5.5), 10.0, 5.2, []),                                        # the mechanic's pickup beside the office
    "rancher_50": ((0.6, 13.6), 90.0, 5.0, []),                                                  # a customer at the far island
    "buick_1955": ((20.0, 15.5), 80.0, 5.3, [r"^Text"]),                                         # parked along the forecourt's front, clear of the way out
    "studebaker_1951": ((-4.0, 16.5), 100.0, 5.0, [r"_Floor_", r"^pPipe\d", r"^pCylinder22", r"pTorus"]),  # the same, clear of the way in
    "packard_1955": ((-98.0, 37.2), 270.0, 5.4, []),                                             # pulled onto the far shoulder
    "taxi_1950s": ((39.0, 0.0), 170.0, 5.3, []),                                                 # at the forecourt's east end, clear of the way out
    "wrecker": ((33.0, -7.0), 100.0, 6.5, []),                                                   # the station's tow truck behind the office, along the fence
    "heavy_tow_truck": ((41.5, -7.0), 100.0, 7.5, []),                                           # the big tow truck to the right of it, at the lot's corner
}

# A car's triangles at most (the station is seen from the pump, the cars from 10 m and more; the shadow pass draws
# every car again each frame the player's car moves, so each one costs twice): the trucks a little more
VEHICLE_BUDGET = 25000
TRUCK_BUDGET = 30000


def fit_vehicle(piece, length, turn):
    """
    A car scaled to its length (a model may come in centimetres or inches) and turned to face the game's +z, then turn
    more; its paint no more than a little metallic (a Sketchfab export's paint is often all metal, which the game's
    pictures turn dark; chrome, near white, stays). Its pictures are held smaller by prepare_prop.py's --cap
    (build_station.py): scaling them here is lost, Blender packs them from their files
    """
    for slot in piece.material_slots:
        material = slot.material
        if material is None or not material.use_nodes:
            continue
        bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is not None and not bsdf.inputs['Metallic'].links and bsdf.inputs['Metallic'].default_value > 0.35:
            colour = bsdf.inputs['Base Color'].default_value
            if bsdf.inputs['Base Color'].links or max(colour[:3]) < 0.7:
                bsdf.inputs['Metallic'].default_value = 0.35
    lo, hi = bounds(piece)
    longest = max(hi.x - lo.x, hi.y - lo.y)
    piece.data.transform(Matrix.Scale(length / longest, 4))
    lo, hi = bounds(piece)
    if (hi.x - lo.x) > (hi.y - lo.y):
        piece.data.transform(Matrix.Rotation(math.radians(90.0), 4, 'Z'))
    piece.data.transform(Matrix.Rotation(math.radians(turn), 4, 'Z'))
    lo, hi = bounds(piece)
    piece.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))


for key, ((vx, vz), heading, length, drop) in VEHICLES.items():
    path = OPTIONAL_MODELS.get(key)
    if not path:
        continue
    try:
        car = load_piece(key, path, drop=drop or None, budget=TRUCK_BUDGET if "tow" in key or "wrecker" in key else VEHICLE_BUDGET, own_materials=True)
        fit_vehicle(car, length, TURNS.get(key, 0.0))
        put(car, vx, vz, 0.0 if paved(vx, vz) else on_land(vx, vz) + 0.04, heading=heading, name=f"CAR_{key}")
        used_optional.append(key)
    except RuntimeError as ex:
        print(f"OPTIONAL {key} left out: {ex}")

# The pack's plants beside the trees nearest the lot, and a pair behind the fence at the forecourt's corners
for k, (tx, tz) in enumerate([(x + 4.5, z + 2.0) for x, z in NEAR_TREES] + [(-21.0, -15.5), (44.0, -16.0)]):
    if not real_trees:
        break
    piece = real_trees[k % len(real_trees)]
    lo, hi = bounds(piece)
    shade(tx, tz, max(hi.x - lo.x, hi.y - lo.y) * 0.45)
    sways([put(piece, tx, tz, on_land(tx, tz), heading=rng.uniform(0.0, 360.0), scale=rng.uniform(0.9, 1.15))])
print(f"OPTIONAL used: {', '.join(used_optional) if used_optional else 'none'}")
# Which were kept, for build_station.py's credit lines (an unpacked one may be left out: a pack with no plant of a size)
with open(os.path.join(os.path.dirname(OUT), "optional_used.json"), "w", encoding="utf-8") as fh:
    json.dump(used_optional, fh)

# The library's pieces go: the copies in the scene keep their meshes, and prepare_prop.py exports what is in the scene
for o in list(library.objects):
    bpy.data.objects.remove(o)
bpy.data.collections.remove(library)

total = sum(triangles(o) for o in station.objects if o.type == 'MESH')
print(f"STATION {len(station.objects)} objects, {total} triangles")
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=OUT)
