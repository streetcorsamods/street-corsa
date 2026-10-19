"""
Builds the gas station and its attendant, start to end, out of the downloads:

  python build_station.py <folder with the Sketchfab zips> [<work folder>] [--blender <blender.exe>]

  1. unpacks the Sketchfab zips (CC BY 4.0, downloaded by hand from their pages: a login is needed) into the work
     folder, and downloads the Poly Haven models (CC0) the station is dressed with and the ground's surface pictures
  2. compose_station.py lays the station out in Blender, saved as station.blend in the work folder (open it to look)
  3. ../Props/prepare_prop.py makes the OBJ and the AC pictures of it (1536 at most, the maps as JPEGs, the materials
     named as in Blender, for station.json's glow)
  4. StationSceneBuilder writes Street Corsa/Content/streetcorsa/stations/crossroads/station.kn5 and ground.bin, with the
     skies (tools/StationScene/skies) round it
  5. the attendant: his sweater painted as a work shirt, prepare_prop.py for his pictures, ../Props/prepare_actor.py
     for his skin, bones and talk, ActorBuilder for Street Corsa/Content/streetcorsa/props/attendant (attendant.kn5,
     talk.ksanim)

The zips (ZIPS), by what they are: Wardster's "Vintage Gas Station" (the office), Rescue3D's "Vintage Gas Pump",
Mark Peters' "Gasstation Sign - 2048px²", gorzi's "Old Tire Inflator", Han1993's "Vintage Soda Machien", LasquetiSpice's
"Speaking Man with Talking Gestures" (the attendant); see docs/screens/gas-station.md for their pages.

Needs Blender (--blender <blender.exe>, the BLENDER variable, else the newest in Program Files), Python with numpy,
opencv-python and Pillow, and the .NET SDK. Downloads about 70 MB the first time.
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(HERE, "..", "Shared"))
sys.path.insert(0, os.path.join(HERE, "..", "GarageScene"))
from scene_tools import blender, poly_haven_surface, without_blender_option  # noqa: E402
from fetch_props import fetch  # noqa: E402

CONTENT = os.path.join(REPO, "Street Corsa", "Content", "streetcorsa")
STATION_OUT = os.path.join(CONTENT, "stations", "crossroads", "station.kn5")
ATTENDANT_OUT = os.path.join(CONTENT, "props", "attendant")
SKIES = os.path.join(HERE, "skies")
PREPARE = os.path.join(REPO, "tools", "Props", "prepare_prop.py")
PREPARE_ACTOR = os.path.join(REPO, "tools", "Props", "prepare_actor.py")

# The Sketchfab zips, by the id compose_station.py knows them by
ZIPS = {
    "vintage_gas_station": "vintage_gas_station*.zip",
    "vintage_gas_pump": "vintage_gas_pump.zip",
    "gasstation_sign": "gasstation_sign*.zip",
    "old_tire_inflator": "old_tire_inflator*.zip",
    "vintage_soda_machien": "vintage_soda_machien*.zip",
    "speaking_man_with_talking_gestures": "speaking_man*.zip",
}

# Poly Haven's models (CC0), as compose_station.py places them
POLY_HAVEN = [
    "painted_wooden_bench", "metal_trash_can", "standing_chalkboard_01", "steel_frame_shelves_01", "oil_tin", "old_tyre",
    "rusted_wheel_rim_02", "Barrel_01", "metal_jerrycan", "wooden_crate_01", "hand_truck", "covered_car", "utility_box_01",
    "wooden_picnic_table", "street_lamp_01", "modular_chainlink_fence", "shrub_02", "modular_electricity_poles",
]

# The ground's surfaces: Poly Haven textures (CC0), by what compose_station.py uses them for
SURFACES = {"grass": "aerial_grass_rock", "forecourt": "concrete_pavement", "road": "asphalt_02", "island": "painted_concrete"}

# The attendant's clothes: his grey sweater (the top left quarter of his picture) painted a tan work shirt; the collar
# and the buttons, brighter than this, stay white
SHIRT = (214, 182, 128)
SWEATER_DARKEST, SWEATER_LIGHTEST = 28, 190


def run(*command, quiet=False):
    print(">", " ".join(f'"{c}"' if " " in c else c for c in command), flush=True)
    subprocess.run(command, check=True, stdout=subprocess.DEVNULL if quiet else None)


def unpack(downloads, work):
    folders = {}
    for key, pattern in ZIPS.items():
        found = glob.glob(os.path.join(downloads, pattern))
        if not found:
            sys.exit(f"No {pattern} in {downloads}")
        folder = os.path.join(work, "sketchfab", key)
        if not os.path.isfile(os.path.join(folder, "scene.gltf")):
            with zipfile.ZipFile(found[0]) as z:
                z.extractall(folder)
        folders[key] = os.path.join(folder, "scene.gltf")
    return folders


def gltf_of(folder):
    for name in os.listdir(folder):
        if name.endswith(".gltf"):
            return os.path.join(folder, name)
    raise IOError(f"no .gltf in {folder}")


def build_tool(project, work, name):
    out = os.path.join(work, name.lower())
    run("dotnet", "build", project, "-c", "Release", "-o", out, "-v", "q", "-nologo")
    return os.path.join(out, name + ".exe")


def shirt(source, out):
    """The attendant's picture with his sweater painted as a work shirt (its luminance kept), saved as out"""
    image = Image.open(source).convert("RGB")
    pixels = np.asarray(image, np.float32)
    h, w = pixels.shape[:2]
    quarter = pixels[: h // 2, : w // 2]
    lum = quarter.mean(axis=-1)
    grey = (quarter.max(axis=-1) - quarter.min(axis=-1)) < 24
    sweater = grey & (lum > SWEATER_DARKEST) & (lum < SWEATER_LIGHTEST)
    tint = np.asarray(SHIRT, np.float32) / 160.0
    painted = np.clip(lum[..., None] * tint[None, None, :], 0, 255)
    quarter[sweater] = painted[sweater]
    Image.fromarray(pixels.astype(np.uint8)).save(out)


def main(downloads, work):
    os.makedirs(work, exist_ok=True)
    sketchfab = unpack(downloads, work)

    haven = os.path.join(work, "polyhaven")
    for model_id in POLY_HAVEN:
        print(f"{model_id}: {'downloaded' if fetch(haven, model_id) else 'up to date'}", flush=True)
    textures = os.path.join(work, "textures")
    os.makedirs(textures, exist_ok=True)
    for surface, texture_id in SURFACES.items():
        poly_haven_surface(textures, surface, texture_id, 1024, 85)

    station_builder = build_tool(os.path.join(HERE, "StationSceneBuilder"), work, "StationSceneBuilder")
    actor_builder = build_tool(os.path.join(REPO, "tools", "Props", "ActorBuilder"), work, "ActorBuilder")

    # The station
    blend = os.path.join(work, "station.blend")
    spec = os.path.join(work, "spec.json")
    with open(spec, "w", encoding="utf-8") as fh:
        json.dump({"sketchfab": sketchfab, "polyhaven": {m: gltf_of(os.path.join(haven, m)) for m in POLY_HAVEN},
                   "textures": textures, "out": blend}, fh, indent=1)
    run(blender(), "-b", "--python", os.path.join(HERE, "compose_station.py"), "--", spec, quiet=True)
    prepared = os.path.join(work, "prepared")
    shutil.rmtree(prepared, ignore_errors=True)
    run(sys.executable, PREPARE, blend, "station", prepared, "1536", "--jpg", "--keep-names")
    sky = ",".join(f"{light}={os.path.join(SKIES, f'sky_{light}.jpg')}" for light in ("day", "dusk", "night"))
    run(station_builder, prepared, "station", STATION_OUT, "--sky", sky)

    # The attendant: his pictures (the shirt painted first, in a copy of his folder), then his skin and talk
    man = os.path.join(work, "attendant")
    shutil.rmtree(man, ignore_errors=True)
    shutil.copytree(os.path.dirname(sketchfab["speaking_man_with_talking_gestures"]), man)
    picture = os.path.join(man, "textures", "Wolf3D_Avatar_baseColor.png")
    shirt(picture, picture)
    source = os.path.join(man, "scene.gltf")
    prepared = os.path.join(work, "attendant_prepared")
    shutil.rmtree(prepared, ignore_errors=True)
    run(sys.executable, PREPARE, source, "attendant", prepared, "1024", "--jpg", "--keep-names")
    actor = os.path.join(work, "attendant.json")
    run(blender(), "-b", "--python", PREPARE_ACTOR, "--", source, actor, quiet=True)
    run(actor_builder, prepared, actor, "attendant", ATTENDANT_OUT, "--rename", "Animation=talk")


if __name__ == "__main__":
    args = without_blender_option(sys.argv)
    if len(args) not in (2, 3):
        sys.exit(__doc__)
    main(args[1], args[2] if len(args) == 3 else os.path.join(tempfile.gettempdir(), "gas_station"))
