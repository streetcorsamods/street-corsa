"""
Turns the Poly Haven models fetch_props.py downloaded into the garage's props: each one through tools/Props/
prepare_prop.py (Blender) and PropBuilder into Content/streetcorsa/props/<id>/<id>.kn5, in metres, stood on its base.
Where each stands is the garage's to say (its prop.json, written by hand; see docs/modding.md, props).

  python build_props.py <downloaded folder> <work folder> [id ...] [--blender <blender.exe>]

With no ids, every prop in PROPS. The work folder keeps what prepare_prop.py left, for a look; PropBuilder is built
into it first. Needs what prepare_prop.py needs, and the .NET SDK.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Shared"))
from scene_tools import without_blender_option  # noqa: E402

PREPARE = os.path.join(REPO, "tools", "Props", "prepare_prop.py")
BUILDER = os.path.join(REPO, "tools", "Props", "PropBuilder")
PROPS_FOLDER = os.path.join(REPO, "Street Corsa", "Content", "streetcorsa", "props")

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


def gltf_of(folder):
    for name in os.listdir(folder):
        if name.endswith(".gltf"):
            return os.path.join(folder, name)
    raise IOError(f"no .gltf in {folder}")


def main():
    args = without_blender_option(sys.argv[1:])
    if len(args) < 2:
        print(__doc__)
        return 1
    downloaded, work = args[0], args[1]
    wanted = set(args[2:])

    tool = os.path.join(work, "propbuilder")
    subprocess.run(["dotnet", "build", BUILDER, "-c", "Release", "-o", tool, "-v", "q", "-nologo"], check=True)
    exe = os.path.join(tool, "PropBuilder.exe")

    failed = []
    for prop_id, source, size in PROPS:
        if wanted and prop_id not in wanted:
            continue
        prepared = os.path.join(work, "prepared", prop_id)
        try:
            subprocess.run([sys.executable, PREPARE, gltf_of(os.path.join(downloaded, source)), prop_id, prepared, str(size), "--jpg"],
                           check=True)
            subprocess.run([exe, prepared, prop_id, os.path.join(PROPS_FOLDER, prop_id), "--scale", "1"], check=True)
        except (subprocess.CalledProcessError, IOError, OSError) as ex:
            print(f"{prop_id}: FAILED {ex}", flush=True)
            failed.append(prop_id)
    if failed:
        print(f"{len(failed)} failed: {', '.join(failed)}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
