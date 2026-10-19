"""
Builds the gas station and its attendant, start to end, out of the downloads:

  python build_station.py <folder with the Sketchfab zips> [<work folder>] [--blender <blender.exe>] [--bake]
      [--turn <optional id>=<degrees> ...]

  1. unpacks the Sketchfab zips (CC BY 4.0, downloaded by hand from their pages: a login is needed) into the work
     folder, and downloads the Poly Haven models (CC0) the station is dressed with and the ground's surface pictures
  2. bake_cards.py takes the pictures of the trees, bushes and grass the land is dressed with (Poly Haven scans of a
     million triangles each, as crossed cards); kept in the work folder's cards once taken (--bake takes them again)
  3. compose_station.py lays the station out in Blender, saved as station.blend in the work folder (open it to look)
  4. ../Props/prepare_prop.py makes the OBJ and the AC pictures of it (1536 at most, the maps as JPEGs, the materials
     named as in Blender, for station.json's glow)
  5. StationSceneBuilder writes Street Corsa/Content/streetcorsa/stations/crossroads/station.kn5 and ground.bin, with the
     skies (tools/StationScene/skies) round it
  6. the attendant: his sweater painted as a work shirt, prepare_prop.py for his pictures, ../Props/prepare_actor.py
     for his skin, bones and talk, ActorBuilder for Street Corsa/Content/streetcorsa/props/attendant (attendant.kn5,
     talk.ksanim)

The zips (ZIPS), by what they are: Wardster's "Vintage Gas Station" (the office), Rescue3D's "Vintage Gas Pump",
Mark Peters' "Gasstation Sign - 2048px²", gorzi's "Old Tire Inflator", Han1993's "Vintage Soda Machien", LasquetiSpice's
"Speaking Man with Talking Gestures" (the attendant); see docs/screens/gas-station.md for their pages.

OPTIONAL names more Sketchfab zips the station takes when they are in the folder (CC BY 4.0 too; compose_station.py
says where each goes): parked cars and trucks of the 1950s and 60s, and packs of real trees for the spots nearest the
lot (cards of a Poly Haven tree stand there otherwise). A car whose nose comes out the wrong way is turned with
--turn <id>=180. Each one used is printed with the credit line to add to CREDITS.txt and station.json's credit.

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
from fetch_props import API, download, fetch, fetch_json  # noqa: E402

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

# More Sketchfab zips, taken when there (all CC BY 4.0): the zip's name, the title, the maker, the page and the size
# its pictures are held to, by the id compose_station.py places them by (VEHICLES, the tree packs). The station's own
# pictures are 1024 at most (GitHub takes no file over 100 MB, and the station is near it); a vehicle's are smaller
# still (prepare_prop.py's --cap on the materials compose_station.py names after it): it stands 10 m and more from the
# pump, the two nearest a little bigger. None for a pack of plants, whose leaves' pictures stay as the rest
OPTIONAL = {
    "chevrolet_c10_1963": ("*chevrolet_c10*.zip", "Low Poly Car - Chevrolet C10 Pickup 1963", "roh3d",
                           "https://sketchfab.com/3d-models/low-poly-car-chevrolet-c10-pickup-1963-679354c151984747bb74310ec5af8995", 768),
    "rancher_50": ("*rancher*50*.zip", "Rancher '50 Custom - Low Poly model", "DanielZhabotinsky",
                   "https://sketchfab.com/3d-models/rancher-50-custom-low-poly-model-7f75854aba96417f8279bd9f1b24e6b2", 768),
    "buick_1955": ("*buick*.zip", "1955 Buick Special Downloadable", "robinmikart",
                   "https://sketchfab.com/3d-models/1955-buick-special-downloadable-1a6d42ef69294498937aab039ececb00", 512),
    "packard_1955": ("*packard*.zip", "1955 American Sedan (Packard based) FREE", "robinmikart",
                     "https://sketchfab.com/3d-models/1955-american-sedan-packard-based-free-28d583f8d4bd4ce39dac2869fb77a197", 512),
    "studebaker_1951": ("*studebaker*.zip", "Studebaker - Car Model", "Allie2k",
                        "https://sketchfab.com/3d-models/studebaker-car-model-c7578ec0f82043e6b392fcdf9b00c743", 512),
    "taxi_1950s": ("*taxi*.zip", "FREE 1950s American Taxi (based on Checker)", "robinmikart",
                   "https://sketchfab.com/3d-models/free-1950s-american-taxi-based-on-checker-d1b10defaed94a1094419e84ca9b0bf1", 512),
    "wrecker": ("*wrecker*.zip", "Wrecker (I.D.:105-645-73)", "-X-ScornGames",
                "https://sketchfab.com/3d-models/wrecker-id105-645-73-2b594f46dd0e42faab75420dd7015dad", 512),
    "heavy_tow_truck": ("heavy_duty_tow_truck*.zip", "Heavy Duty Tow Truck 3d Model", "3DMODEL",
                        "https://sketchfab.com/3d-models/heavy-duty-tow-truck-3d-model-246fc01e05d6465eadeac4ea0d292e4b", 512),
    "lilac_pack": ("*lilac*.zip", "Lilac bush pack (12 vars, LODs, game ready)", "lolipop_1707",
                   "https://sketchfab.com/3d-models/lilac-bush-pack-12-vars-lods-game-ready-10312697ec994fc99355cb94f1963a2e", None),
    "maple_pack": ("*maple*.zip", "Maple trees pack (lowpoly, game ready, LODs)", "lolipop_1707",
                   "https://sketchfab.com/3d-models/maple-trees-pack-lowpoly-game-ready-lods-b5d2833c258f4054a01ee2b4ef85adf0", None),
    "oak_pack": ("*oak_trees*.zip", "Oak trees pack (17var, LODs, seasons, gameready)", "lolipop_1707",
                 "https://sketchfab.com/3d-models/oak-trees-pack-17var-lods-seasons-gameready-a5e4e64f9f1d4089bdcc6170a9333393", None),
}

# Poly Haven's models (CC0), as compose_station.py places them
POLY_HAVEN = [
    "painted_wooden_bench", "metal_trash_can", "standing_chalkboard_01", "steel_frame_shelves_01", "oil_tin", "old_tyre",
    "rusted_wheel_rim_02", "Barrel_01", "metal_jerrycan", "wooden_crate_01", "hand_truck", "covered_car", "utility_box_01",
    "wooden_picnic_table", "street_lamp_01", "modular_chainlink_fence", "shrub_02", "modular_electricity_poles",
    # The land round it: weeds and small bushes, a boulder, a fallen trunk, stumps; and what the cards are pictures of
    "shrub_03", "wild_rooibos_bush", "boulder_01", "dead_tree_trunk_02", "tree_stump_01",
    "island_tree_02", "searsia_lucida", "grass_medium_02",
]

# The cards (bake_cards.py): a tree seen from the front and the side, two bushes and a shrub the same, three clumps of
# grass from the front; by the name compose_station.py knows them by, which Poly Haven model and which of its meshes
CARDS = [
    ("tree", "island_tree_02", 2, 1024, None),
    ("bush_a", "searsia_lucida", 2, 768, [r"searsia_lucida_a_LOD0$"]),
    ("bush_b", "searsia_lucida", 2, 768, [r"searsia_lucida_b_LOD0$"]),
    ("shrub", "shrub_02", 2, 768, [r"shrub_02_a$"]),
    ("grass_c", "grass_medium_02", 1, 512, [r"grass_medium_02_c$"]),
    ("grass_d", "grass_medium_02", 1, 512, [r"grass_medium_02_d$"]),
    ("grass_e", "grass_medium_02", 1, 512, [r"grass_medium_02_e$"]),
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


def unpack(downloads, work, zips, wanted=True):
    """The zips unpacked into the work folder, each its scene.gltf by id; one not there stops the build when wanted, else is left out"""
    folders = {}
    for key, pattern in zips.items():
        found = glob.glob(os.path.join(downloads, pattern))
        if not found:
            if wanted:
                sys.exit(f"No {pattern} in {downloads}")
            continue
        folder = os.path.join(work, "sketchfab", key)
        if not os.path.isfile(os.path.join(folder, "scene.gltf")):
            with zipfile.ZipFile(found[0]) as z:
                z.extractall(folder)
        folders[key] = os.path.join(folder, "scene.gltf")
    return folders


def bake_cards(work, haven, again):
    """The cards' pictures, taken when not there yet (or again); the folder they are in"""
    cards = os.path.join(work, "cards")
    if again or not os.path.isfile(os.path.join(cards, "cards.json")):
        spec = os.path.join(work, "cards_spec.json")
        with open(spec, "w", encoding="utf-8") as fh:
            json.dump({"out": cards, "cards": [
                {"name": name, "path": gltf_of(os.path.join(haven, model)), "views": views, "size": size, "keep": keep}
                for name, model, views, size, keep in CARDS]}, fh, indent=1)
        run(blender(), "-b", "--python", os.path.join(HERE, "bake_cards.py"), "--", spec, quiet=True)
    return cards


def fetch_alpha(haven, model_id, files):
    """
    A Poly Haven model's alpha maps (its leaves' cut-outs), which its glTF leaves out (JPEG pictures): each 1k PNG the
    API's list of its files names with a name ending in alpha, into the model's textures as <id>_<name>_1k.png
    (foliage_alpha.py reads them); how many were downloaded
    """
    got = 0
    for key, formats in files.items():
        if not key.lower().endswith("alpha") or not isinstance(formats, dict):
            continue
        png = formats.get("1k", {}).get("png")
        if not png:
            continue
        name = f"{model_id}_{key.lower()}_1k.png"
        got += download(png["url"], os.path.join(haven, model_id, "textures", name), png["md5"])
    return got


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


def main(downloads, work, bake_again=False, turns=None):
    os.makedirs(work, exist_ok=True)
    sketchfab = unpack(downloads, work, ZIPS)
    optional = unpack(downloads, work, {key: entry[0] for key, entry in OPTIONAL.items()}, wanted=False)
    print(f"optional models: {', '.join(optional) if optional else 'none in ' + downloads}", flush=True)

    haven = os.path.join(work, "polyhaven")
    for model_id in POLY_HAVEN:
        # The API's list of the model's files once, for the model and its alpha maps both
        files = fetch_json(API.format(id=model_id))
        got = fetch(haven, model_id, files)
        alphas = fetch_alpha(haven, model_id, files)
        print(f"{model_id}: {'downloaded' if got else 'up to date'}{f', {alphas} alpha map(s)' if alphas else ''}", flush=True)
    textures = os.path.join(work, "textures")
    os.makedirs(textures, exist_ok=True)
    for surface, texture_id in SURFACES.items():
        poly_haven_surface(textures, surface, texture_id, 1024, 85)

    cards = bake_cards(work, haven, bake_again)

    station_builder = build_tool(os.path.join(HERE, "StationSceneBuilder"), work, "StationSceneBuilder")
    actor_builder = build_tool(os.path.join(REPO, "tools", "Props", "ActorBuilder"), work, "ActorBuilder")

    # The station
    blend = os.path.join(work, "station.blend")
    spec = os.path.join(work, "spec.json")
    with open(spec, "w", encoding="utf-8") as fh:
        json.dump({"sketchfab": sketchfab, "polyhaven": {m: gltf_of(os.path.join(haven, m)) for m in POLY_HAVEN},
                   "textures": textures, "cards": cards, "optional": optional, "turns": turns or {}, "out": blend}, fh, indent=1)
    run(blender(), "-b", "--python", os.path.join(HERE, "compose_station.py"), "--", spec, quiet=True)
    # The optional models compose_station.py kept (one unpacked may be left out: a pack with no plant of a size)
    with open(os.path.join(work, "optional_used.json"), encoding="utf-8") as fh:
        used = json.load(fh)
    left_out = [key for key in optional if key not in used]
    if used:
        print("Optional models in the scene: add each to CREDITS.txt and to station.json's credit:")
        for key in used:
            _, title, maker, url, _ = OPTIONAL[key]
            print(f'  - "{title}" by {maker}, CC BY 4.0: {url}')
    if left_out:
        print(f"Optional models left out of the scene (compose_station.py says why above): {', '.join(left_out)}")
    prepared = os.path.join(work, "prepared")
    shutil.rmtree(prepared, ignore_errors=True)
    caps = [arg for key in used if OPTIONAL[key][4] for arg in ("--cap", f"{key}_={OPTIONAL[key][4]}")]
    run(sys.executable, PREPARE, blend, "station", prepared, "1024", "--jpg", "--keep-names", *caps)
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
    bake_again = "--bake" in args
    turns = {}
    while "--turn" in args:
        at = args.index("--turn")
        if at + 1 >= len(args) or "=" not in args[at + 1]:
            sys.exit("--turn takes <optional id>=<degrees>")
        key, degrees = args[at + 1].split("=", 1)
        turns[key] = float(degrees)
        del args[at:at + 2]
    args = [a for a in args if a != "--bake"]
    if len(args) not in (2, 3):
        sys.exit(__doc__)
    main(args[1], args[2] if len(args) == 3 else os.path.join(tempfile.gettempdir(), "gas_station"), bake_again, turns)
