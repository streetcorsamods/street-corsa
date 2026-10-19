"""
Lays the gas station out in Blender and saves it as a .blend for prepare_prop.py (then StationSceneBuilder) to make the
game's KN5 of (build_station.py runs it all):

  blender -b --python compose_station.py -- <spec.json>

The spec (build_station.py writes it): sketchfab (the unzipped Sketchfab models by id, each its scene.gltf),
polyhaven (the Poly Haven models by id, each its .gltf), textures (the folder with the ground pictures, Poly Haven
surfaces as scene_tools.poly_haven_surface names them), out (the .blend to save).

Blender's space: x along the road, y away from it (the road at -y), z up; metres; the forecourt's surface at z = 0.
The game's (AC's) space is x the same, y up, z = -y (the road towards +z): station.json's positions are the game's, and
G() below turns one into Blender's.

The layout follows the one the game had (the Bannochbrae cut), so the car's ways in and out stay: the road runs
along x at z 26.5..34 (the lane nearer the station at 28.4); a concrete forecourt in the grass, a driveway off the road
at each end of it (in at the west, out at the east); two pump islands run along x at z 2 and z 10 under the canopy, a
pump at each end; the car stops between them at (0.6, 4.8) facing +x, its driver's door to the island at z 2 and the
attendant on it; the office stands behind, facing the road, the air meter and the soda machine at its corner, the pole
sign out by the road at the way out; a fence behind the forecourt, bushes along it, a lamp at each driveway and poles
along the far side of the road.
"""
import json
import math
import os
import re
import sys

import bpy
from mathutils import Matrix, Vector

spec = json.load(open(sys.argv[sys.argv.index("--") + 1], encoding="utf-8"))
SKETCHFAB, POLYHAVEN, TEXTURES, OUT = spec["sketchfab"], spec["polyhaven"], spec["textures"], spec["out"]

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


def load_piece(name, path, keep=None, drop=None, scale=1.0, turn=0.0, budget=None, stand=True, centre=True):
    """
    A model as one mesh in the library, to copy from: its meshes (those matching keep, less those matching drop,
    regular expressions on a mesh's name or its parents'), scaled, turned about the vertical (degrees), brought down to
    a triangle budget, stood on its base (its lowest point at z 0) with the middle of its footprint at the origin
    """
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = set(bpy.data.objects) - before

    def names(o):
        found = []
        while o is not None:
            found.append(o.name)
            o = o.parent
        return found

    def matches(o, patterns):
        return any(re.search(p, n) for p in patterns for n in names(o))

    rigs = [o for o in added if o.type == 'ARMATURE']
    shapes = {pb.custom_shape for rig in rigs for pb in rig.pose.bones if pb.custom_shape}
    meshes = [o for o in added if o.type == 'MESH' and o not in shapes and not o.hide_render
              and (keep is None or matches(o, keep)) and not (drop and matches(o, drop))]
    if not meshes:
        # Nothing of the import stays in the scene: a piece left out is left out of the export too
        for o in added:
            bpy.data.objects.remove(o)
        raise RuntimeError(f"{name}: nothing kept of {path}")
    unparent(meshes)
    for o in added:
        if o not in meshes:
            bpy.data.objects.remove(o)
    piece = join(meshes, name)
    if piece.data.shape_keys:
        piece.shape_key_clear()
    piece.data.transform(Matrix.Rotation(math.radians(turn), 4, 'Z') @ Matrix.Scale(scale, 4))
    if budget and triangles(piece) > budget:
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

ground("GRASS", -GROUND, GROUND, -GROUND, GROUND, -0.02, grass, 7.0)
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

# Bushes along the fence and across the road
bush = haven("bush", "shrub_02", keep=[r"shrub_02_b$"], budget=2200)
bush_big = haven("bush_big", "shrub_02", keep=[r"shrub_02_c$"], budget=3200)
for k, bx in enumerate(range(int(LOT_WEST), int(LOT_EAST) + 3, 8)):
    put(bush if k % 2 else bush_big, bx + (k % 3), LOT_BACK - 2.6, heading=47.0 * k)
for k, bx in enumerate(range(-150, 160, 26)):
    put(bush_big if k % 3 else bush, bx + (k % 5), ROAD_FAR + 6.0 + (k % 4), heading=31.0 * k)

# Poles along the far side of the road, where the model has a pole to put up
poles = POLYHAVEN.get("modular_electricity_poles")
if poles:
    try:
        pole = haven("pole", "modular_electricity_poles", keep=[r"^pole_large$"], stand=False, centre=True)
        arm = haven("pole_arm", "modular_electricity_poles", keep=[r"^connection_large_01$"], stand=False, centre=True, budget=1500)
        for px in (-90.0, -50.0, -10.0, 30.0, 70.0, 110.0):
            put(pole, px + 7.0, ROAD_FAR + 2.5)
            put(arm, px + 7.0, ROAD_FAR + 2.5, 6.4)
    except RuntimeError as ex:
        print(f"POLES left out: {ex}")

# The library's pieces go: the copies in the scene keep their meshes, and prepare_prop.py exports what is in the scene
for o in list(library.objects):
    bpy.data.objects.remove(o)
bpy.data.collections.remove(library)

total = sum(triangles(o) for o in station.objects if o.type == 'MESH')
print(f"STATION {len(station.objects)} objects, {total} triangles")
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=OUT)
