"""
Readies a downloaded model (a Sketchfab glTF) for tools/Props/prepare_prop.py, inside Blender: what is not part of the
prop taken out, a pose of its animation held, turned, scaled and moved into the game's space, and its moving parts (the
lift's carriages, the engine stand's head) split off into .blend files of their own, each with its node's origin where
the game wants it. build_props.py runs it, one group at a time:

  blender -b --python compose_props.py -- <spec.json> <group> <out.blend>

The spec (build_props.py, SKETCHFAB) says, in Blender's space (z up; the game's +z, the car's nose, is Blender's -y):
  source     the .gltf
  delete     names (regular expressions) of objects that are no part of the prop: a backdrop, a second copy
  action     part of the name of an animation to hold, and frame: its frame
  move       [x, y, z] added first, turn degrees about the vertical next, scale last (about the origin)
  nodes      by node name, the objects (regular expressions, on an object's name or any of its parents') that move, and
             origin: where the node's origin is, after the move, turn and scale; the group "" is everything else
  swing      parts turned about a pivot first: objects (regular expressions), pivot [x, y], degrees about the vertical
  squash     parts made thinner up and down: objects, about (the height that stays), factor
  budget     the most triangles the whole prop may have: a model with more is brought down (Blender's decimate,
             collapse) evenly over all its parts
"""
import json
import math
import re
import sys

import bpy
import mathutils

args = sys.argv[sys.argv.index("--") + 1:]
spec = json.load(open(args[0], encoding="utf-8"))
group, out = args[1], args[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=spec["source"])
scene = bpy.context.scene


def names(o):
    found = []
    while o is not None:
        found.append(o.name)
        o = o.parent
    return found


def matches(o, patterns):
    return any(re.search(p, n) for p in patterns for n in names(o))


# The pose of the animation asked for, everything where that frame has it
if spec.get("action"):
    for rig in (o for o in scene.objects if o.type == "ARMATURE"):
        action = next(a for a in bpy.data.actions if spec["action"] in a.name)
        rig.animation_data.action = action
    scene.frame_set(int(spec.get("frame", 0)))
bpy.context.view_layer.update()

# Blender's glTF import draws a rig's bones with a mesh of its own (an icosphere): no part of the model, nor is
# anything hidden
bone_shapes = {b.custom_shape for rig in scene.objects if rig.type == "ARMATURE" for b in rig.pose.bones if b.custom_shape}
meshes = [o for o in scene.objects if o.type == "MESH" and o not in bone_shapes and not o.hide_render and not o.hide_get()
          and not matches(o, spec.get("delete", []))]

# Held where they are: off their bones and empties, any skinning applied, the world placement in the vertices
held = {}
for o in meshes:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(o.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
    mesh.transform(o.matrix_world)
    held[o.name] = (mesh, names(o))

for o in list(scene.objects):
    bpy.data.objects.remove(o, do_unlink=True)


def chain_matches(chain, patterns):
    return any(re.search(p, n) for p in patterns for n in chain)


# Parts swung about a pivot (the lift's arms, to the angle wanted), then parts made thinner up and down about a height
# (the lift's arms, to lie lower than a car's body): both in the model's own space, before the move, turn and scale
for swing in spec.get("swing", []):
    px, py = swing["pivot"]
    turn = (mathutils.Matrix.Translation((px, py, 0)) @ mathutils.Matrix.Rotation(math.radians(swing["degrees"]), 4, "Z")
            @ mathutils.Matrix.Translation((-px, -py, 0)))
    for mesh, chain in held.values():
        if chain_matches(chain, swing["objects"]):
            mesh.transform(turn)

for squash in spec.get("squash", []):
    about, factor = squash["about"], squash["factor"]
    thin = (mathutils.Matrix.Translation((0, 0, about)) @ mathutils.Matrix.Diagonal((1, 1, factor, 1))
            @ mathutils.Matrix.Translation((0, 0, -about)))
    for mesh, chain in held.values():
        if chain_matches(chain, squash["objects"]):
            mesh.transform(thin)

# Into the game's space
placement = (mathutils.Matrix.Scale(spec.get("scale", 1.0), 4)
             @ mathutils.Matrix.Rotation(math.radians(spec.get("turn", 0.0)), 4, "Z")
             @ mathutils.Matrix.Translation(spec.get("move", [0, 0, 0])))

nodes = spec.get("nodes", {})
wanted = nodes[group]["objects"] if group else None
moving = [p for node in nodes.values() for p in node["objects"]]
origin = mathutils.Vector(nodes[group].get("origin", [0, 0, 0])) if group else mathutils.Vector()

kept = 0
for name, (mesh, chain) in held.items():
    in_group = any(re.search(p, n) for p in wanted for n in chain) if group else not any(re.search(p, n) for p in moving for n in chain)
    if not in_group:
        continue
    mesh.transform(mathutils.Matrix.Translation(-origin) @ placement)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    kept += 1

# No more triangles than the prop's budget: a garage prop is seen across a room, never up close. The whole model's
# count decides how far each of its parts, the moving ones too, is brought down, so they keep their likeness together
budget = spec.get("budget")
if budget:
    total = sum(len(p.vertices) - 2 for m, _ in held.values() for p in m.polygons)
    ratio = min(1.0, budget / max(1, total))
    if ratio < 1.0:
        for obj in list(scene.collection.objects):
            modifier = obj.modifiers.new("budget", "DECIMATE")
            modifier.ratio = ratio
            modifier.use_collapse_triangulate = True
            depsgraph = bpy.context.evaluated_depsgraph_get()
            reduced = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
            obj.modifiers.remove(modifier)
            obj.data = reduced
    print(f"BUDGET {budget}: {total} triangles brought down by {ratio:.2f}")

print(f"COMPOSED {group or '(static)'}: {kept} objects")

# The pictures go inside the .blend, so prepare_prop.py finds them wherever it is opened
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=out)
