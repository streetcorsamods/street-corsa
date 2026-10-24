"""
Turns the downloaded models into the garage's props: each one through tools/Props/prepare_prop.py (Blender) and
PropBuilder into Content/streetrod/props/<id>/<id>.kn5, in metres. Where each stands is the garage's to say (its
prop.json, written by hand; see docs/modding.md, props).

  PROPS      Poly Haven's (CC0), as fetch_props.py downloaded them: stood on their base as they come
  SKETCHFAB  CC BY 4.0 models the owner downloaded from Sketchfab (glTF zips, a login needed): each readied by
             compose_props.py (what is no part of it taken out, scaled, posed, its moving parts split off into nodes:
             the engine stand's head)

Every prop goes through compose_props.py, which brings it down to its triangle budget (BUDGETS).

  python build_props.py <Poly Haven folder> <Sketchfab zips folder> <work folder> [id ...] [--blender <blender.exe>]

With no ids, every prop. The work folder keeps what the scripts left, for a look; PropBuilder is built into it first.
Needs what prepare_prop.py needs, and the .NET SDK.
"""
import json
import os
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Shared"))
from scene_tools import blender, without_blender_option  # noqa: E402

PREPARE = os.path.join(REPO, "tools", "Props", "prepare_prop.py")
COMPOSE = os.path.join(HERE, "compose_props.py")
BUILDER = os.path.join(REPO, "tools", "Props", "PropBuilder")
PROPS_FOLDER = os.path.join(REPO, "Street Corsa", "Content", "streetrod", "props")

# The game's id, the Poly Haven model, the largest picture (pixels): small things on a shelf need no more than 256
PROPS = [
    ("tool_chest", "metal_tool_chest", 1024),
    ("toolbox", "metal_toolbox", 512),
    ("tool_cart", "tool_cart", 512),
    ("metal_rack", "worn_metal_rack", 1024),
    ("desk", "metal_office_desk", 1024),
    ("wooden_table", "WoodenTable_03", 1024),
    ("stool", "metal_stool_01", 512),
    ("wooden_stool", "wooden_stool_01", 512),
    ("wooden_shelves", "painted_wooden_shelves", 512),
    ("vise", "bench_vice_01", 512),
    ("compressor", "old_military_compressor", 1024),
    ("welder", "portable_welding_cart", 1024),
    ("drill_press", "old_drill_press", 1024),
    ("tire_pump", "tire_pump", 512),
    ("extinguisher", "korean_fire_extinguisher_01", 512),
    ("ladder", "ladder_sectioned_01", 512),
    ("hand_truck", "hand_truck", 512),
    ("jerrycan_red", "metal_jerrycan", 512),
    ("jerrycan_green", "metal_jerrycan_green", 512),
    ("oil_drum", "Barrel_01", 1024),
    ("oil_can", "small_oil_can_01", 256),
    ("oil_tin", "oil_tin", 256),
    ("old_tyre", "old_tyre", 1024),
    ("steel_rim", "rusted_wheel_rim_01", 512),
    ("propane_tank", "propane_tank", 512),
    ("ratchet", "ratchet_wrench", 256),
    ("adjustable_wrench", "adjustable_wrench", 256),
    ("pipe_wrench", "pipe_wrench", 256),
    ("pliers", "pliers", 256),
    ("screwdriver", "screwdriver", 256),
    ("hammer", "cross_pein_hammer", 256),
    ("crowbar", "crowbar_01", 256),
    ("hacksaw", "rusted_hacksaw", 256),
    ("broom", "wooden_broom", 256),
    ("dustpan", "dustpan", 256),
    ("bucket", "wooden_bucket_01", 512),
    ("crate", "wooden_crate_01", 512),
    ("cardboard_box", "cardboard_box_01", 512),
    ("spray_cans", "spray_paint_bottles", 256),
]

# The game's id, the zip, the largest picture, and how compose_props.py readies it (Blender's space, z up; the game's +z
# is Blender's -y). All CC BY 4.0: the authors are named in each prop.json's credit.
SKETCHFAB = [
    # The engine stand: the head (plate, standoffs, bolts, arms, spindle) turns with the engine; scaled so the spindle is
    # at the game's crank height (0.78 m), the face the block bolts to at the origin, the legs forward under the engine
    ("engine_stand", "engine_stand_-_mechanic_workshop_tool.zip", 512, {
        "move": [-0.04, 0, 0], "turn": -90, "scale": 0.78 / 0.85,
        "nodes": {"engine_stand_head": {"objects": ["^plate", "^standoff", "^boltcap", r"^arm\.", "^spindle"], "origin": [0, 0, 0]}}}),
    ("engine_hoist", "engine_hoist_-_engine_crane.zip", 1024, {"scale": 28}),
    ("jack_stand", "car_jack_stand.zip", 512, {"delete": ["^Plates"], "scale": 0.22}),
    ("creeper", "mechanic_creeper.zip", 512, {}),
    ("mechanic_cart", "mechanic_tool_cart.zip", 512, {}),
    ("oil_drainer", "waste_oil_drainer.zip", 512, {}),
    # Two batteries side by side on a backdrop sphere: the rusted one alone
    ("battery", "car_battery_cleanrusted_fbx_low_poly_free.zip", 512, {"delete": ["^Sphere", r"^Cube\.001"], "scale": 0.000155}),
    ("old_v8", "old_v8.zip", 1024, {"scale": 0.18}),
    ("wall_clock", "classic_garage_clock.zip", 512, {}),
]


# The most triangles a prop may have (compose_props.py brings a heavier one down): it is seen across a room, never up
# close. The big pieces keep the most; anything not named here, a small thing on the floor, 3000
BUDGETS = {
    10000: ["compressor", "engine_hoist", "welder"],
    6000: ["tool_chest", "tool_cart", "metal_rack", "desk", "wooden_table", "ladder", "hand_truck", "mechanic_cart", "creeper",
           "drill_press", "engine_stand", "old_v8"],
    1500: ["ratchet", "adjustable_wrench", "pipe_wrench", "pliers", "screwdriver", "hammer", "crowbar", "hacksaw", "oil_tin",
           "oil_can", "spray_cans", "dustpan"],
}
DEFAULT_BUDGET = 3000


def budget_of(prop_id):
    return next((budget for budget, ids in BUDGETS.items() if prop_id in ids), DEFAULT_BUDGET)


def gltf_of(folder):
    for name in os.listdir(folder):
        if name.endswith(".gltf"):
            return os.path.join(folder, name)
    raise IOError(f"no .gltf in {folder}")


def build(exe, prop_id, source, size, work, nodes=None, keep_origin=False, rests=None):
    """One model through prepare_prop.py and PropBuilder; nodes: by name, the .blend of each moving part; rests: by name,
    where a node is put with the model at rest"""
    prepared = os.path.join(work, "prepared", prop_id)
    subprocess.run([sys.executable, PREPARE, source, prop_id, prepared, str(size), "--jpg"], check=True)
    command = [exe, prepared, prop_id, os.path.join(PROPS_FOLDER, prop_id), "--scale", "1"]
    for node, blend in (nodes or {}).items():
        folder = os.path.join(work, "prepared", f"{prop_id}__{node}")
        subprocess.run([sys.executable, PREPARE, blend, node, folder, str(size), "--jpg"], check=True)
        rest = (rests or {}).get(node)
        command += ["--node", f"{node}={folder}" + (f"@{rest[0]},{rest[1]},{rest[2]}" if rest else "")]
    if keep_origin:
        command.append("--keep-origin")
    subprocess.run(command, check=True)


def compose(prop_id, gltf, spec, work):
    """The model readied by compose_props.py: its .blend, and one for each of its moving parts"""
    folder = os.path.join(work, "composed", prop_id)
    os.makedirs(folder, exist_ok=True)
    spec_path = os.path.join(folder, "spec.json")
    with open(spec_path, "w", encoding="utf-8") as fh:
        json.dump(dict(spec, source=gltf), fh, indent=1)

    def run(group, out):
        subprocess.run([blender(), "-b", "--python", COMPOSE, "--", spec_path, group, out], check=True, stdout=subprocess.DEVNULL)
        return out

    model = run("", os.path.join(folder, prop_id + ".blend"))
    nodes = {node: run(node, os.path.join(folder, node + ".blend")) for node in spec.get("nodes", {})}
    return model, nodes


def main():
    args = without_blender_option(sys.argv[1:])
    if len(args) < 3:
        print(__doc__)
        return 1
    poly_haven, sketchfab, work = args[0], args[1], args[2]
    wanted = set(args[3:])

    tool = os.path.join(work, "propbuilder")
    subprocess.run(["dotnet", "build", BUILDER, "-c", "Release", "-o", tool, "-v", "q", "-nologo"], check=True)
    exe = os.path.join(tool, "PropBuilder.exe")

    failed = []
    for prop_id, source, size in PROPS:
        if wanted and prop_id not in wanted:
            continue
        try:
            model, _ = compose(prop_id, gltf_of(os.path.join(poly_haven, source)), {"budget": budget_of(prop_id)}, work)
            build(exe, prop_id, model, size, work)
        except (subprocess.CalledProcessError, IOError, OSError) as ex:
            print(f"{prop_id}: FAILED {ex}", flush=True)
            failed.append(prop_id)

    for prop_id, archive, size, spec in SKETCHFAB:
        if wanted and prop_id not in wanted:
            continue
        try:
            unzipped = os.path.join(work, "sketchfab", os.path.splitext(archive)[0])
            with zipfile.ZipFile(os.path.join(sketchfab, archive)) as z:
                z.extractall(unzipped)
            model, nodes = compose(prop_id, gltf_of(unzipped), dict(spec, budget=budget_of(prop_id)), work)
            # A model with moving parts keeps the origin they were placed by; the rest are stood on their base
            rests = {name: node.get("rest") for name, node in spec.get("nodes", {}).items()}
            build(exe, prop_id, model, size, work, nodes=nodes, keep_origin=bool(nodes), rests=rests)
        except (subprocess.CalledProcessError, IOError, OSError, zipfile.BadZipFile) as ex:
            print(f"{prop_id}: FAILED {ex}", flush=True)
            failed.append(prop_id)

    if failed:
        print(f"{len(failed)} failed: {', '.join(failed)}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
