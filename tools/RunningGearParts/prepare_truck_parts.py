"""
A private mod for this PC only (roadmap step 31 phase 4): the leaf springs, drums, Dana housings and a shock drawn with
pieces of "Pickup truck chassis" by austinchasejez23 (https://sketchfab.com/3d-models/pickup-truck-chassis-00dcf51b90364321919d2988dba8ace8),
CC BY-NC 4.0. The licence keeps it out of the game until its author agrees: the mod's folder is git-ignored
(Street Corsa/Content/truckparts), and the base game keeps its own models for every clone of the repo.

The parts stay the base game's; the mod's parts/models.json gives them its models (docs/modding.md, "models.json"):
every leaf spring, every drum by its size, the Dana 44 and 60 housings, the Gabriel shock. Each piece is cut out of the
chassis by its place, scaled so the axle's flanges land where the game's housings end (0.655 m out), and turned to the
part's own space (tools/RunningGearParts/prepare_standins.py says which).

  python prepare_truck_parts.py <pickup_truck_chassis.zip> <Content folder> [--blender <blender.exe>]

Needs Blender, numpy and Pillow (prepare_prop.py), and PropBuilder built (dotnet build tools/Props/PropBuilder).
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "Shared"))
from scene_tools import blender, without_blender_option  # noqa: E402

PREPARE = os.path.join(HERE, "..", "Props", "prepare_prop.py")
PROP_BUILDER = os.path.join(HERE, "..", "Props", "PropBuilder", "bin", "Debug", "net10.0", "PropBuilder.dll")
MOD = "truckparts"
DRUMS = {"drum_9": 9.0, "drum_9_5": 9.5, "drum_10": 10.0, "drum_11": 11.0}

# Run inside Blender. The chassis' space: X along the frame (the front at -X), Y across (its left at +Y), Z up, its
# axle's line at x 17.5, z 134.8, the tube flanges 54.3 out. Each piece is picked by its box (whole meshes only), or
# for the housing mesh by mesh (centre and size), so the brake lines, the U-bolts and the truck's spring perches stay
# behind (the game's leaves hang under the axle).
CUT = r"""
import bpy, json, math, sys
from mathutils import Matrix, Vector
args = sys.argv[sys.argv.index("--") + 1:]
source, out, drums = args[0], args[1], json.loads(args[2])
K = 0.655 / 54.3

def load(pick, drop=None, grow=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=source)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in bpy.data.objects:
        o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    keep = []
    for o in meshes:
        vs = [v.co for v in o.data.vertices]
        lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        if pick(lo, hi):
            keep.append(o)
    for o in [x for x in bpy.data.objects if x not in keep]:
        bpy.data.objects.remove(o)
    if grow is not None:
        # Pieces made bigger about a point before they are one mesh (the housing's middle)
        which, factor, about = grow
        for o in keep:
            vs = [v.co for v in o.data.vertices]
            if which(Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
                     Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))):
                o.data.transform(Matrix.Translation(Vector(about)) @ Matrix.Scale(factor, 4) @ Matrix.Translation(-Vector(about)))
    for o in keep:
        o.select_set(True)
    bpy.context.view_layer.objects.active = keep[0]
    if len(keep) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.matrix_world = Matrix.Identity(4)
    print("PICKED", len(keep))
    if drop is not None:
        # Loose pieces of a picked mesh that are not wanted (the housing's vent hose, rising over it)
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
        pieces = [x for x in bpy.data.objects if x.type == 'MESH']
        for x in pieces:
            vs = [v.co for v in x.data.vertices]
            if drop(Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
                    Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))):
                bpy.data.objects.remove(x)
        rest = [x for x in bpy.data.objects if x.type == 'MESH']
        for x in rest:
            x.select_set(True)
        bpy.context.view_layer.objects.active = rest[0]
        if len(rest) > 1:
            bpy.ops.object.join()
        o = bpy.context.view_layer.objects.active
        print("KEPT", len(rest), "of", len(pieces))
    return o

def inside(box_lo, box_hi):
    return lambda lo, hi: all(box_lo[i] <= lo[i] and hi[i] <= box_hi[i] for i in range(3))

def one_of(pieces, tolerance=0.6):
    def pick(lo, hi):
        c, s = (lo + hi) / 2, hi - lo
        return any(all(abs(c[i] - pc[i]) <= tolerance and abs(s[i] - ps[i]) <= tolerance for i in range(3)) for pc, ps in pieces)
    return pick

# The part's space from the chassis': its front -X to the part's front, the truck's left (+Y) to the part's +X
TURN = Matrix.Rotation(math.radians(-90), 4, 'Z')

def finish(o, origin, scale, name):
    o.data.transform(Matrix.Scale(scale, 4) @ TURN @ Matrix.Translation(-Vector(origin)))
    bpy.ops.wm.save_as_mainfile(filepath=out + "/" + name + ".blend")

# The leaf spring of the truck's left side with its hangers, shackle, clip and centre bolt; the origin on top of the
# pack at the axle seat
o = load(inside((-40, 38, 140), (72, 52, 170)))
finish(o, (17.4, 45.1, 147.5), K, "leaf")

# The left rear drum with its studs, the origin on its outer face, scaled by its diameter (24.2 across)
for name, inches in drums.items():
    o = load(inside((3, 50, 120), (32, 68, 150)))
    finish(o, (17.3, 62.7, 134.8), inches * 0.0254 / 24.2, name)

# The axle housing: its middle, tubes, flanges, cover, the pinion's yoke and the shocks' lower mounts
middle = [((9.9, 8.3, 140.5), (25.5, 57.0, 29.5)), ((24.5, 0.0, 135.1), (7.6, 22.4, 18.7)), ((-4.7, 0.0, 135.0), (7.7, 6.7, 7.0))]
pieces = list(middle)
for side in (1, -1):
    pieces += [((17.5, side * 28.4, 134.8), (8.0, 51.1, 8.0)), ((17.5, side * 54.3, 134.7), (8.9, 1.7, 8.9)),
               ((17.6, side * 36.3, 135.8), (7.1, 6.0, 5.0))]
# The middle a little bigger, as the game's own housings' (Parts.CentreScale): the owner found the diffs small
# (the vent hose, grown with it, still rises over the grown middle's top)
o = load(one_of(pieces), drop=lambda lo, hi: hi.z > 153, grow=(one_of(middle), 1.05, (17.5, 0.0, 134.8)))
finish(o, (17.5, 0.0, 134.8), K, "housing")

# The left shock with its boot and mounts; the origin at the game's spring seat (0.24 m in from the hub, 0.06 m under it)
o = load(inside((-5, 20, 125), (15, 34, 162)))
finish(o, (17.3, 62.7 - 0.24 / K, 134.8 - 0.06 / K), K, "shock")
"""


def models(parts, names):
    # Which of the base game's running gear parts each model is for, by its folder
    running = os.path.join(parts, "running_gear")
    drum_of = {229: "drum_9", 241: "drum_9_5", 254: "drum_10", 279: "drum_11"}
    found = {}
    for pack in ("springs_leaf", "brakes_drum", "axles_dana", "shocks_period"):
        folder = os.path.join(running, pack)
        for name in sorted(n for n in os.listdir(folder) if n.endswith(".json")):
            with open(os.path.join(folder, name), encoding="utf-8-sig") as fh:
                part = json.load(fh)
                cls, pid = part.get("class"), part["id"]
                if cls == "leaf_spring":
                    found[pid] = "truck/leaf.kn5"
                elif cls == "drum_brake":
                    found[pid] = f"truck/{drum_of[int(part['specs']['diam_mm'])]}.kn5"
                elif cls == "rear_axle":
                    found[pid] = "truck/housing.kn5"
                elif pid.endswith("/gabriel_hd"):
                    found[pid] = "truck/shock.kn5"
    missing = {m for m in found.values() if os.path.basename(m)[:-4] not in names}
    if missing:
        sys.exit(f"Not built: {missing}")
    return dict(sorted(found.items()))


def main(chassis, content):
    base_parts = os.path.join(content, "streetcorsa", "parts")
    if not os.path.isdir(base_parts):
        sys.exit(f"No base game parts under {content}")
    if not os.path.isfile(PROP_BUILDER):
        sys.exit(f"Build PropBuilder first: {PROP_BUILDER}")

    mod = os.path.join(content, MOD)
    target = os.path.join(mod, "parts", "truck")
    os.makedirs(target, exist_ok=True)
    work = tempfile.mkdtemp(prefix="sc_truckparts_")
    try:
        src = os.path.join(work, "src")
        os.makedirs(src)
        with zipfile.ZipFile(chassis) as z:
            z.extractall(src)
        blends = os.path.join(work, "blends")
        os.makedirs(blends)
        script = os.path.join(work, "_cut.py")
        with open(script, "w", encoding="utf-8") as fh:
            fh.write(CUT)
        subprocess.run([blender(), "-b", "--python", script, "--", os.path.join(src, "scene.gltf"), blends, json.dumps(DRUMS)],
                       check=True, stdout=subprocess.DEVNULL)

        names = [n for n in ["leaf", "housing", "shock", *DRUMS] if os.path.isfile(os.path.join(blends, n + ".blend"))]
        for name in names:
            prepared = os.path.join(work, "prepared_" + name)
            subprocess.run([sys.executable, PREPARE, os.path.join(blends, name + ".blend"), name, prepared, "256", "--jpg"], check=True)
            subprocess.run(["dotnet", PROP_BUILDER, prepared, name, target, "--scale", "1", "--keep-origin"], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)

    with open(os.path.join(mod, "parts", "models.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump({"_comment": "Written by tools/RunningGearParts/prepare_truck_parts.py: the base game's parts drawn with this mod's models",
                   "models": models(base_parts, names)}, fh, indent=1)
    credit = ('Made from "Pickup truck chassis" by austinchasejez23 (https://sketchfab.com/austinchasejez23), '
              "CC BY-NC 4.0 (https://creativecommons.org/licenses/by-nc/4.0/): "
              "https://sketchfab.com/3d-models/pickup-truck-chassis-00dcf51b90364321919d2988dba8ace8")
    with open(os.path.join(mod, "mod.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump({"id": MOD, "name": "Truck chassis running gear (local)", "version": "1.0", "author": "pecamardelli",
                   "description": "A private mod for this PC only, never published until the author agrees: the leaf springs, drums, "
                                  "Dana housings and the Gabriel shock drawn with pieces of a truck chassis. " + credit,
                   "order": 20}, fh, indent=1)
    with open(os.path.join(mod, "CREDITS.txt"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(credit + "\n\nChanges: pieces cut out of the chassis, scaled and turned to the parts' own space, converted to KN5 "
                          "(tools/RunningGearParts/prepare_truck_parts.py). Not for sharing: no commercial use.\n")
    print(f"{mod}: {len(names)} models")


if __name__ == "__main__":
    args = without_blender_option(sys.argv)
    if len(args) != 3:
        sys.exit(__doc__)
    main(args[1], args[2])
