"""
Run inside Blender: each wheel download's objects as cut_wheels.py turns them (axle along X, face towards -X, centred
on the whole model's box), with each object's reach off the axle (5th percentile and most) and its span along it, to
say which objects are the tyre.

  blender -b --python survey_wheels.py -- <sources json> <wheels json>
"""
import bpy, json, math, sys
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
sources, wheels = json.loads(args[0]), json.loads(args[1])
exec(open(__file__.replace("survey_wheels.py", "cut_wheels_lib.py")).read()) if False else None


def turn(axis, face):
    m = {"x": Matrix.Identity(4), "y": Matrix.Rotation(math.radians(-90), 4, 'Z'), "z": Matrix.Rotation(math.radians(90), 4, 'Y')}[axis]
    return Matrix.Rotation(math.pi, 4, 'Z') @ m if face > 0 else m


for name, w in wheels.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=sources[w["source"]])
    for o in bpy.data.objects:
        o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    objs = [o for o in bpy.data.objects if o.type == 'MESH']
    m = turn(w["axis"], w["face"])
    pts = {o.name: [m @ v.co for v in o.data.vertices] for o in objs}
    allp = [p for ps in pts.values() for p in ps]
    c = Vector([(min(p[i] for p in allp) + max(p[i] for p in allp)) / 2 for i in range(3)])
    print("=====", name, w["source"])
    for o in objs:
        ps = [p - c for p in pts[o.name]]
        r = sorted(math.hypot(p.y, p.z) for p in ps)
        mats = ",".join(mm.name for mm in o.data.materials if mm)
        print(f"  {o.name[:30]:30s} {len(o.data.polygons):6d}f r5={r[len(r)//20]:.3f} rmin={r[0]:.3f} rmax={r[-1]:.3f} x={min(p.x for p in ps):+.3f}..{max(p.x for p in ps):+.3f} [{mats[:40]}]")
