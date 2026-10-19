"""
Readies a rigged, animated model (a Sketchfab glTF of a person) for ActorBuilder, inside Blender: its skin (every mesh
bound to the rig, with each vertex's bones and weights), its bones (the rig at rest, parent by parent) and each of its
animations baked frame by frame, all in the game's space (y up, the figure facing +z; Blender's -y), in metres.
Run with the model's pictures readied by prepare_prop.py (--keep-names), which ActorBuilder takes with this file:

  blender -b --python prepare_actor.py -- <model.glb|.gltf> <out actor.json> [--fps 24] [--scale 1] [--max-bones 64]

AC's skinned shader takes 64 bones at most: a rig with more loses its least-weighted leaf bones (the finger tips, the
eyes), each one's skin handed to its parent, until it fits.

actor.json:
  bones       in order, parents first: name, parent (its index, -1 for a root), rest (the bone's transform in its
              parent's space, 16 floats, row by row as a KN5 holds them), bind (the inverse of where the bone is as
              the skin was bound, in the model's space)
  meshes      material (the model's material name, as prepare_prop.py's materials.json knows it), vertices (16 floats
              each: position, normal, u, v, four weights, four bone indices), indices (triangles)
  animations  by name: fps, frames, and for each bone that moves, a keyframe a frame: rotation (x, y, z, w), position,
              scale, in its parent's space

A mesh with no bones (a bone's custom shape, an icosphere Blender draws a rig with) is left out.
"""
import json
import sys

import bpy
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
source, out = args[0], args[1]
fps = float(args[args.index("--fps") + 1]) if "--fps" in args else 24.0
scale = float(args[args.index("--scale") + 1]) if "--scale" in args else 1.0
max_bones = int(args[args.index("--max-bones") + 1]) if "--max-bones" in args else 64

# Blender's (x, y, z), z up and -y forward, into the game's (x, z, -y): y up, +z forward
TO_GAME = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
FROM_GAME = TO_GAME.inverted()
SCALE = Matrix.Scale(scale, 4)


def game(matrix):
    """A Blender world matrix as the game's"""
    return SCALE @ TO_GAME @ matrix @ FROM_GAME


def rows(matrix):
    """A matrix as a KN5 holds it: row by row, the translation in the fourth row"""
    return [float(matrix[j][i]) for i in range(4) for j in range(4)]


def keyframe(matrix):
    position, rotation, size = matrix.decompose()
    return [rotation.x, rotation.y, rotation.z, rotation.w, position.x, position.y, position.z, size.x, size.y, size.z]


bpy.ops.wm.read_factory_settings(use_empty=True)
# The frame rate before the import: the importer keys the glTF's times into frames at the scene's rate as it reads
scene = bpy.context.scene
scene.render.fps = int(round(fps))
bpy.ops.import_scene.gltf(filepath=source)

rig = next((o for o in scene.objects if o.type == "ARMATURE"), None)
if rig is None:
    sys.exit("No armature in the model")

# The bones, parents before children, each one's rest transform in its parent's space, and the inverse of where it
# is bound (the skin is exported at rest, so the bind is the rest)
rig.data.pose_position = "REST"
bpy.context.view_layer.update()
bones = []
by_name = {}


def add(bone):
    by_name[bone.name] = len(bones)
    bones.append(bone)
    for child in bone.children:
        add(child)


for root in (b for b in rig.data.bones if b.parent is None):
    add(root)

rest_world = {b.name: game(rig.matrix_world @ b.matrix_local) for b in bones}

# The skin: every mesh deformed by the rig, its corners (a vertex at a seam is one corner a side), each with its
# bones and weights
shapes = {pb.custom_shape for pb in rig.pose.bones if pb.custom_shape}
skinned = []
for o in scene.objects:
    if o.type != "MESH" or o in shapes:
        continue
    deformed = any(m.type == "ARMATURE" and m.object == rig for m in o.modifiers) or o.parent == rig
    if deformed and o.vertex_groups:
        skinned.append(o)

# More bones than the shader takes: the leaf bone with the least skin on it goes, its skin to its parent, until it fits
carried = {}
for o in skinned:
    groups = {g.index: by_name.get(g.name) for g in o.vertex_groups}
    for v in o.data.vertices:
        for g in v.groups:
            if groups.get(g.group) is not None:
                carried[groups[g.group]] = carried.get(groups[g.group], 0.0) + g.weight
kept = list(range(len(bones)))
stands_for = {i: i for i in kept}
while len(kept) > max_bones:
    leaves = [i for i in kept if bones[i].parent is not None and not any(bones[j].parent == bones[i] for j in kept)]
    if not leaves:
        break
    gone = min(leaves, key=lambda i: carried.get(i, 0.0))
    parent = by_name[bones[gone].parent.name]
    carried[parent] = carried.get(parent, 0.0) + carried.get(gone, 0.0)
    for i, target in stands_for.items():
        if target == gone:
            stands_for[i] = parent
    kept.remove(gone)
    print(f"BONE {bones[gone].name} left out ({carried.get(gone, 0.0):.1f} of skin), its parent {bones[parent].name} takes its place")
if len(kept) > max_bones:
    sys.exit(f"{len(kept)} bones, more than the {max_bones} the shader takes, and none a leaf to leave out")
index_of = {old: new for new, old in enumerate(kept)}

bone_entries = []
rest_key = {}
for i in kept:
    b = bones[i]
    world = rest_world[b.name]
    local = (rest_world[b.parent.name].inverted() @ world) if b.parent else world
    rest_key[b.name] = keyframe(local)
    bone_entries.append({"name": b.name, "parent": index_of[by_name[b.parent.name]] if b.parent else -1,
                         "rest": rows(local), "bind": rows(world.inverted())})


def differs(a, b):
    """Two keyframes that are not the same pose (a rotation and its negative are one)"""
    dot = abs(sum(x * y for x, y in zip(a[:4], b[:4])))
    return 1.0 - dot > 1e-5 or any(abs(x - y) > 1e-5 for x, y in zip(a[4:], b[4:]))

meshes = []
for o in skinned:
    # A point goes into the game's space outright (a transform is conjugated, game())
    mesh = o.data
    world = SCALE @ TO_GAME @ o.matrix_world
    normals = world.to_3x3().inverted().transposed()
    groups = {g.index: by_name.get(g.name) for g in o.vertex_groups}
    uv = mesh.uv_layers.active.data if mesh.uv_layers.active else None

    def weights_of(vertex):
        merged = {}
        for g in vertex.groups:
            bone = groups.get(g.group)
            if bone is not None:
                bone = index_of[stands_for[bone]]
                merged[bone] = merged.get(bone, 0.0) + g.weight
        found = sorted(((w, i) for i, w in merged.items()), reverse=True)[:4]
        total = sum(w for w, _ in found) or 1.0
        weights = [w / total for w, _ in found] + [0.0] * (4 - len(found))
        indices = [float(i) for _, i in found] + [-1.0] * (4 - len(found))
        return weights, indices

    by_material = {}
    corners = {}
    for polygon in mesh.polygons:
        material = mesh.materials[polygon.material_index].name if mesh.materials and mesh.materials[polygon.material_index] else ""
        vertices, indices = by_material.setdefault(material, ([], []))
        loops = list(polygon.loop_indices)
        ids = []
        for loop in loops:
            vertex = mesh.loops[loop].vertex_index
            n = mesh.corner_normals[loop].vector
            t = uv[loop].uv if uv else (0.0, 0.0)
            key = (material, vertex, round(n.x, 4), round(n.y, 4), round(n.z, 4), round(t[0], 5), round(t[1], 5))
            if key not in corners:
                p = world @ mesh.vertices[vertex].co
                nn = (normals @ n).normalized()
                w, i = weights_of(mesh.vertices[vertex])
                corners[key] = len(vertices) // 16
                vertices.extend([p.x, p.y, p.z, nn.x, nn.y, nn.z, t[0], 1.0 - t[1], *w, *i])
            ids.append(corners[key])
        for k in range(1, len(ids) - 1):
            indices.extend([ids[0], ids[k], ids[k + 1]])

    for material, (vertices, indices) in by_material.items():
        meshes.append({"name": o.name, "material": material, "vertices": vertices, "indices": indices})

if not meshes:
    sys.exit("No mesh is bound to the rig")

# Each animation baked a frame at a time: every bone's transform in its parent's space
rig.data.pose_position = "POSE"
if rig.animation_data is None:
    rig.animation_data_create()
animations = {}
for action in bpy.data.actions:
    rig.animation_data.action = action
    start, end = action.frame_range
    frames = int(round(end - start)) + 1
    tracks = {bones[i].name: [] for i in kept}
    for frame in range(frames):
        scene.frame_set(int(start) + frame)
        bpy.context.view_layer.update()
        worlds = {pb.name: game(rig.matrix_world @ pb.matrix) for pb in rig.pose.bones}
        for i in kept:
            b = bones[i]
            world = worlds[b.name]
            local = (worlds[b.parent.name].inverted() @ world) if b.parent else world
            tracks[b.name].append(keyframe(local))
    # A bone the animation leaves at rest is left out: the game keeps its rest pose. One held in another pose the whole
    # clip (an arm out while the hands talk) is kept, or its children would move off the wrong parent
    moving = {name: track for name, track in tracks.items() if any(differs(rest_key[name], frame) for frame in track)}
    animations[action.name] = {"fps": fps, "frames": frames, "bones": moving}
    print(f"ANIMATION {action.name}: {frames} frames at {fps:g} fps, {len(moving)} of {len(kept)} bones move")

with open(out, "w", encoding="utf-8") as fh:
    json.dump({"bones": bone_entries, "meshes": meshes, "animations": animations}, fh)
print(f"ACTOR {len(kept)} bones, {len(meshes)} mesh(es), {sum(len(m['indices']) // 3 for m in meshes)} triangles -> {out}")
