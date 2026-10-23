"""
Run inside Blender (prepare_bolt_ons.py does): each wheel download cut into its rim and its tyre, each a .blend in the
wheel's own space. The game's rim and tyre models have the axle along X and the face towards -X (Parts/Cars/WheelFit):
in Blender's space (Z up) the axle is Blender's X, the face towards -X. The origin is the wheel's middle; the rim is
scaled so its lip (the farthest of it off the axle) is 7.5 inches, a 15-inch wheel's (the game scales a shown wheel to
the car's lip or tyre anyway), the tyre with it.

  blender -b --python cut_wheels.py -- <sources json: download -> scene.gltf> <out folder> <wheels json>

A wheel: {"source", "axis": "x"|"y"|"z" (the axle in the imported model), "face": +1|-1 (the outboard side along it),
"tyre": names of the tyre's objects, or a rule ("rubber": the objects of a rubber/tyre/tire/pneu material, and what sits
outside the rim, the sidewall's lettering and whitewall rings), "keep": the objects to keep (default all), "rim_faces",
"tyre_faces": what each is decimated to}.
"""
import bpy, bmesh, json, math, sys
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
sources, out, wheels = json.loads(args[0]), args[1], json.loads(args[2])
PREVIEW = "--preview" in args
LIP = 7.5 * 0.0254
RUBBER = ("rubber", "tyre", "tire", "pneu", "tread")


def load(source):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=sources[source])
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in bpy.data.objects:
        o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for other in [x for x in bpy.data.objects if x.type != 'MESH']:
        bpy.data.objects.remove(other)
    # a mesh shared by several objects (instanced lug nuts) made single so each keeps its place
    for o in bpy.data.objects:
        if o.data.users > 1:
            o.data = o.data.copy()
    return list(bpy.data.objects)


def turn(axis, face):
    """the matrix taking the model's axle onto X, its outboard face towards -X"""
    to_x = {"x": Matrix.Identity(4), "y": Matrix.Rotation(math.radians(-90), 4, 'Z'), "z": Matrix.Rotation(math.radians(90), 4, 'Y')}[axis]
    m = to_x
    if face > 0:                   # the face is towards +X after the turn: about Z, half a turn
        m = Matrix.Rotation(math.pi, 4, 'Z') @ m
    return m


def radial(v): return math.hypot(v.y, v.z)


def is_rubber(o): return any(any(w in (m.name if m else "").lower() for w in RUBBER) for m in o.data.materials)


def join(objs, name):
    if not objs:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    return o


def decimate(o, faces):
    if o is None or len(o.data.polygons) <= faces:
        return
    m = o.modifiers.new("decimate", 'DECIMATE')
    m.ratio = faces / len(o.data.polygons)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)


def two_sided(o):
    """faces seen from both sides: AC draws one side of a face, and these models' thin shells (a wheel's barrel, a
    cover) were made to be seen from both"""
    bm = bmesh.new(); bm.from_mesh(o.data)
    back = bmesh.ops.duplicate(bm, geom=list(bm.faces))["geom"]
    bmesh.ops.reverse_faces(bm, faces=[f for f in back if isinstance(f, bmesh.types.BMFace)])
    bm.to_mesh(o.data); bm.free()




report = {}
for name, w in wheels.items():
    objs = load(w["source"])
    if "keep" in w:
        for o in [o for o in objs if o.name not in w["keep"]]:
            bpy.data.objects.remove(o)
        objs = [o for o in objs if o.name in w["keep"]]
    m = turn(w["axis"], w["face"])
    for o in objs:
        o.data.transform(m)
    # the tyre's objects: named, or the rule's
    if w.get("tyre") == "rubber":
        tyre = [o for o in objs if is_rubber(o)]
    else:
        tyre = [o for o in objs if o.name in (w.get("tyre") or [])]
    rim = [o for o in objs if o not in tyre]
    # the middle: the middle of the tyre's box (of the rim's when there is no tyre); the axle through it along X
    pts = [v.co for o in (tyre or rim) for v in o.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = (lo + hi) / 2
    for o in objs:
        o.data.transform(Matrix.Translation(-centre))
    # the rule's tyre also takes what lies outside the rim (lettering, a whitewall ring): every vertex past the lip
    # (past the tyre's bead: the rubber's inner radius, its 5th percentile so a stray vertex does not set it)
    if w.get("tyre") == "rubber" and tyre:
        rr = sorted(radial(v.co) for o in tyre for v in o.data.vertices)
        bead = rr[len(rr) // 20]
        outside = [o for o in rim if min(radial(v.co) for v in o.data.vertices) > bead * w.get("outside", 0.92)]
        tyre += outside
        rim = [o for o in rim if o not in outside]
    if rim:
        lip = max(radial(v.co) for o in rim for v in o.data.vertices)
    else:                          # a tyre alone (a slick): its bead goes on a 15-inch lip
        rr = sorted(radial(v.co) for o in tyre for v in o.data.vertices)
        lip = rr[len(rr) // 50]
    scale = LIP / lip
    for o in objs:
        o.data.transform(Matrix.Scale(scale, 4))
    rim_o = join(rim, name + "_rim")
    tyre_o = join(tyre, name + "_tyre")
    decimate(rim_o, w.get("rim_faces", 14000))
    decimate(tyre_o, w.get("tyre_faces", 10000))
    for o in (rim_o, tyre_o):
        if o is not None and w.get("two_sided"):
            two_sided(o)
    report[name] = {"lip": LIP, "scale": scale}
    if tyre_o is not None:
        tv = [v.co for v in tyre_o.data.vertices]
        report[name].update({"tyre_radius": max(radial(v) for v in tv), "tyre_inner": min(radial(v) for v in tv),
                             "tyre_width": max(v.x for v in tv) - min(v.x for v in tv)})
    if rim_o is not None:
        rv = [v.co for v in rim_o.data.vertices]
        report[name].update({"rim_min_x": min(v.x for v in rv), "rim_max_x": max(v.x for v in rv)})
    # each into its own .blend: the other out of the scene while it is saved (an object no scene holds is not saved)
    scene = bpy.context.scene.collection
    for o in [x for x in bpy.data.objects if x not in (rim_o, tyre_o)]:
        bpy.data.objects.remove(o)
    if PREVIEW:                    # the whole wheel as cut, for a look (a glTF: Y up, so the face looks towards -X still)
        bpy.ops.export_scene.gltf(filepath=out + "/" + name + "_preview.glb", export_format='GLB')
    for keep, other, part in ((rim_o, tyre_o, "_rim"), (tyre_o, rim_o, "_tyre")):
        if keep is None:
            continue
        if other is not None:
            for c in list(other.users_collection):
                c.objects.unlink(other)
        if not keep.users_collection:
            scene.objects.link(keep)
        bpy.ops.wm.save_as_mainfile(filepath=out + "/" + name + part + ".blend")

with open(out + "/wheels_report.json", "w") as f:
    json.dump(report, f, indent=1)
