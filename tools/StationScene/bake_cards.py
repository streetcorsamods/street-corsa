"""
Takes pictures of models for the cards the station's backdrop is made of (a tree seen from the front and from the side,
a bush, a clump of grass): a scanned tree of a million triangles becomes two crossed pictures of a few thousand pixels,
cut out by their alpha, which is how a racing track draws the trees it never drives up to.

  blender -b --python bake_cards.py -- <spec.json>

The spec: out (the folder the pictures and cards.json go in) and cards, a list of {name, path (a .gltf), views (1 or 2:
the front, and the side a quarter turn on), size (pixels, square), keep (regular expressions on the meshes to keep,
optional)}. Each view is drawn straight on (an orthographic camera), evenly lit, over nothing (a transparent film), as
<name>_front.png and <name>_side.png; cards.json then says, by name, how many metres the square picture spans
(the model stands on the picture's bottom edge, in its middle) and which pictures there are, for compose_station.py.
"""
import json
import math
import os
import re
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from foliage_alpha import attach_alpha  # noqa: E402

spec = json.load(open(sys.argv[sys.argv.index("--") + 1], encoding="utf-8"))
OUT = spec["out"]
os.makedirs(OUT, exist_ok=True)


def bounds(objects):
    pts = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


def bake(card):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=card["path"])
    added = set(bpy.data.objects) - before
    keep = card.get("keep")
    meshes = [o for o in added if o.type == 'MESH' and (not keep or any(re.search(p, o.name) for p in keep))]
    for o in added:
        if o not in meshes and o.type == 'MESH':
            bpy.data.objects.remove(o)
    if not meshes:
        raise RuntimeError(f"{card['name']}: nothing kept of {card['path']}")

    # Leaves cut out by their alpha (the model's alpha map put into its picture), seen from both sides
    attach_alpha(meshes, card["path"])
    for material in bpy.data.materials:
        if material.use_nodes:
            material.surface_render_method = 'DITHERED'
            material.use_backface_culling = False

    lo, hi = bounds(meshes)
    span = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z) * 1.04
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z + span / 2))

    # Lit evenly from everywhere, and a little more from above, so the card reads the same from either side and the
    # game's own sun does the rest
    world = bpy.data.worlds.new("world")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    background.inputs["Strength"].default_value = 0.65
    scene.world = world
    sun = bpy.data.lights.new("sun", 'SUN')
    sun.energy = 1.1
    sun.angle = math.radians(20)
    sun_object = bpy.data.objects.new("sun", sun)
    sun_object.rotation_euler = (math.radians(35), 0.0, 0.0)
    scene.collection.objects.link(sun_object)

    camera = bpy.data.cameras.new("camera")
    camera.type = 'ORTHO'
    camera.ortho_scale = span
    camera.clip_end = span * 20
    camera_object = bpy.data.objects.new("camera", camera)
    scene.collection.objects.link(camera_object)
    scene.camera = camera_object

    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.eevee.taa_render_samples = 48
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = card.get("size", 1024)
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.view_settings.view_transform = 'Standard'

    pictures = {}
    for view, angle in (("front", 0.0), ("side", math.pi / 2))[: card.get("views", 2)]:
        distance = span * 4
        camera_object.location = centre + Vector((distance * math.sin(angle), -distance * math.cos(angle), 0.0))
        camera_object.rotation_euler = (math.pi / 2, 0.0, angle)
        scene.render.filepath = os.path.join(OUT, f"{card['name']}_{view}.png")
        bpy.ops.render.render(write_still=True)
        pictures[view] = os.path.basename(scene.render.filepath)
        print(f"CARD {card['name']} {view}: {scene.render.filepath}", flush=True)

    return {"span": round(span, 3), "height": round(hi.z - lo.z, 3), "width": round(max(hi.x - lo.x, hi.y - lo.y), 3), **pictures}


cards = {}
for entry in spec["cards"]:
    cards[entry["name"]] = bake(entry)
with open(os.path.join(OUT, "cards.json"), "w", encoding="utf-8") as fh:
    json.dump(cards, fh, indent=1)
print(f"CARDS {len(cards)} in {OUT}")
