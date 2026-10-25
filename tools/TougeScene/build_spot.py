"""
Builds Touge Nights' meeting spot, the car park at the top of the pass where the racers meet at night (the setting's
diner), start to end, out of the downloads:

  python build_spot.py <folder> [<folder> ...] [--work <work folder>] [--turn <id>=<degrees> ...] [--blend-only]
      [--render] [--blender <blender.exe>]

The folders are searched for the Sketchfab zips in MODELS (all CC BY 4.0, a login to download: the owner keeps them in
D:/JUEGOS/3D Models/touge); each is optional, and one not there leaves its place empty (the build says so). Touge Nights':
python build_spot.py "D:/JUEGOS/3D Models/touge" "D:/JUEGOS/3D Models/polyhaven"

  1. unpacks the zips into the work folder (default: a "touge_spot" folder in the system's temp), and downloads the
     Poly Haven textures (CC0) the ground, the road and the rest house are made of
  2. prepare_skies.py next to this file makes the skies (Poly Haven, CC0): the night one a moonlit hilltop with a town's
     lights in the valley, seen past the guardrail
  3. compose_spot.py lays the car park out in Blender, saved as spot.blend in the work folder (--render also draws a few
     views of it into the work folder's renders, to look at the layout)
  4. ../Props/prepare_prop.py makes the OBJ and the AC pictures of it
  5. ../DinerScene/DinerSceneBuilder writes Street Corsa/Content/touge_nights/streets/summit/street.kn5 and ground.bin,
     the skies round it

Each Sketchfab model used is printed with its credit line (CREDITS.txt and street.json's credit name them all).

Needs Blender (--blender <blender.exe>, the BLENDER variable, else the newest in Program Files), Python with numpy and
Pillow, and the .NET SDK.
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "GarageScene"))
from scene_tools import blender, without_blender_option  # noqa: E402
from fetch_props import download, fetch_json  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Street Corsa", "Content", "touge_nights", "streets", "summit", "street.kn5")
SKIES = os.path.join(HERE, "skies")

# The Sketchfab models (all CC BY 4.0), by the id compose_spot.py places them by: the zip's name, the title, the maker,
# the page, and the size the model's pictures are held to (prepare_prop.py's --cap)
MODELS = {
    "vending_jpn": ("jpn_vending_machine*.zip", "JPN vending machine", "adenotoxin",
                    "https://sketchfab.com/3d-models/jpn-vending-machine-e9a1050ada83417592fc3233cb6a4c0a", 1024),
    "vending_pair": ("japanese_inspired_vending_machines*.zip", "Japanese Inspired Vending Machines", "spookyghostboo",
                     "https://sketchfab.com/3d-models/japanese-inspired-vending-machines-2b3638c2c2a64633a3e11d473152c909", 1024),
    "traffic": ("japanese_traffic_assets*.zip", "Japanese Traffic Assets", "erikkinc",
                "https://sketchfab.com/3d-models/japanese-traffic-assets-1a4833770ace4df6aecfdabb76e36d60", 1024),
    "pines": ("pine_trees_pack*.zip", "Pine trees pack (lowpoly, game ready, LODs)", "lolipop_1707",
              "https://sketchfab.com/3d-models/pine-trees-pack-lowpoly-game-ready-lods-e1e9c07b8e2e445c943fec660beefba2", 1024),
    "lamps": ("various_low-poly_street_lights*.zip", "Various Low-Poly Street Lights", "kmb_jr",
              "https://sketchfab.com/3d-models/various-low-poly-street-lights-1173b0c4d9b0400bbeaafbee0e94ca59", 512),
    "toro": ("japanese_toro*.zip", "Japanese Toro", "andereberna",
             "https://sketchfab.com/3d-models/japanese-toro-afe883adeab045348b7dc9a038975feb", 512),
    "payphone": ("old_green_payphone*.zip", "Old Green Payphone", "naprimerjenya",
                 "https://sketchfab.com/3d-models/old-green-payphone-ad06f3ec91a24943ad4a38dd70413cda", 256),
    "kiosk": ("japanese_store*.zip", "Japanese Store", "Nick.Stark",
              "https://sketchfab.com/3d-models/japanese-store-78396a70304b412d9bc8e3955891f6cd", 1024),
    "torii": ("japanese_torii_gate*.zip", "Japanese Torii Gate", "sahirvirmani",
              "https://sketchfab.com/3d-models/japanese-torii-gate-2027a248de1b4b70985ff97e708fb50d", 512),
    "hokora": ("hokora*.zip", "Hokora", "Derik_Nicolas",
               "https://sketchfab.com/3d-models/hokora-3cb1e6261fad4f48a15cf3629a2f76e1", 512),
    "road_signs": ("japanese_traffic_signs*.zip", "Japanese Traffic Signs", "Hiushiko",
                   "https://sketchfab.com/3d-models/japanese-traffic-signs-60fe7ba6f092458a8cfb8ddae14c67ad", 256),
    "trash_can": ("japanese_trash_can*.zip", "Japanese Trash Can", "ditovirnantio",
                  "https://sketchfab.com/3d-models/japanese-trash-can-477e8292037043508fcfcaed32805ca8", 256),
    "bench": ("japan_train_station_bench*.zip", "Japan Train Station Bench", "ditovirnantio",
              "https://sketchfab.com/3d-models/japan-train-station-bench-e23d79c1014a431bb913d15fc395b4f0", 512),
    "cone": ("japanese_traffic_cone*.zip", "Japanese Traffic Cone", "Hiushiko",
             "https://sketchfab.com/3d-models/japanese-traffic-cone-07cf4bafef3a4a7196398eea991b560d", 256),
    "house": ("japanese_residential_home_03*.zip", "Japanese Residential Home 03", "reckzilla",
              "https://sketchfab.com/3d-models/japanese-residential-home-03-1c53f4f37fc44c32a8874464025aea48", 512),
    # The parked cars: Daniel Zhabotinsky's made-up makes, no real badges
    "kiri_86": ("kiri_86*.zip", "Kiri '86 - Low Poly model", "Daniel Zhabotinsky",
                "https://sketchfab.com/3d-models/kiri-86-low-poly-model-5c17c582ef9549798f694b660feea42a", 512),
    "lct_3000": ("lct_3000*.zip", "LCT 3000 '95 - Low Poly model", "Daniel Zhabotinsky",
                 "https://sketchfab.com/3d-models/lct-3000-95-low-poly-model-663a0953c038434a918cb85725c88ffa", 512),
    "jdm_sport_99": ("jdm_sport_99*.zip", "JDM Sport '99 - Low Poly model", "Daniel Zhabotinsky",
                     "https://sketchfab.com/3d-models/jdm-sport-99-low-poly-model-6dd4ae19c454414d9eed2bc524515d78", 512),
    "tiara_gt_83": ("tiara_gt_83*.zip", "Tiara GT '83 - Low Poly model", "Daniel Zhabotinsky",
                    "https://sketchfab.com/3d-models/tiara-gt-83-low-poly-model-8918b07a427443cd8d1b334d9ad213a5", 512),
    "kiri_94": ("kiri_94*.zip", "Kiri '94 - Low Poly model", "Daniel Zhabotinsky",
                "https://sketchfab.com/3d-models/kiri-94-low-poly-model-b92072c8cd9d4ee6b3c0be45de48223c", 512),
    "tozzo_98": ("tozzo_98*.zip", "Tozzo '98 - Low Poly model", "Daniel Zhabotinsky",
                 "https://sketchfab.com/3d-models/tozzo-98-low-poly-model-d43bb748da364af2b3f0a9348365f0fb", 512),
    "nfr_90": ("nfr_90*.zip", "NFR '90 - Low Poly model", "Daniel Zhabotinsky",
               "https://sketchfab.com/3d-models/nfr-90-low-poly-model-776078e51fbe4ec9addbe45ba13e1aad", 512),
    "lolita_91": ("lolita_91*.zip", "Lolita '91 - Low Poly model", "Daniel Zhabotinsky",
                  "https://sketchfab.com/3d-models/lolita-91-low-poly-model-128691a403ec4dd5af28c2e7c9037a3a", 512),
}

# Poly Haven's textures (CC0): the ground's, the road's and the lot's, the rest house's; each map at 2k
TEXTURES = {
    "asphalt_02": ["Diffuse"],
    "worn_asphalt": ["Diffuse"],
    "gravel_road": ["Diffuse"],
    "forest_ground_04": ["Diffuse"],
    "concrete_wall_001": ["Diffuse"],
    "grey_roof_tiles": ["Diffuse"],
    "corrugated_iron": ["Diffuse"],
    "concrete_moss": ["Diffuse"],
}

# Daniel Zhabotinsky's low poly cars are made facing back: turned round (--turn <id>=<degrees> adds to or overrides these)
TURNS = {"kiri_86": 180.0, "lct_3000": 180.0, "jdm_sport_99": 180.0, "tiara_gt_83": 180.0, "kiri_94": 180.0,
         "tozzo_98": 180.0, "nfr_90": 180.0, "lolita_91": 180.0}


def fetch_textures(out):
    """Each texture's maps (2k JPGs) into out/<id>_<map>.jpg, unless there already"""
    found = {}
    for asset, maps in TEXTURES.items():
        files = fetch_json(f"https://api.polyhaven.com/files/{asset}")
        for name in maps:
            item = files.get(name, {}).get("2k", {}).get("jpg")
            if not item:
                print(f"{asset}: no 2k {name}", flush=True)
                continue
            path = os.path.join(out, f"{asset}_{name.lower()}.jpg")
            download(item["url"], path, item["md5"])
            found[f"{asset}_{name.lower()}"] = path
    return found


def find(folders, pattern):
    for folder in folders:
        found = sorted(glob.glob(os.path.join(glob.escape(folder), pattern)))
        if found:
            return found[0]
    return None


def unpack_zip(path, folder):
    if not os.path.isdir(folder):
        with zipfile.ZipFile(path) as z:
            z.extractall(folder)
    return folder


def gltf_of(folder):
    for root, _, files in os.walk(folder):
        for name in files:
            if name.endswith(".gltf"):
                return os.path.join(root, name)
    raise IOError(f"no .gltf in {folder}")


def run(*command, quiet=False):
    print(">", " ".join(f'"{c}"' if " " in c else c for c in command), flush=True)
    subprocess.run(command, check=True, stdout=subprocess.DEVNULL if quiet else None)


def main(folders, work, turns, blend_only=False, render=False):
    os.makedirs(work, exist_ok=True)
    models = {}
    for key, (pattern, *_rest) in MODELS.items():
        path = find(folders, pattern)
        if path:
            models[key] = gltf_of(unpack_zip(path, os.path.join(work, "sketchfab", key)))
    missing = [key for key in MODELS if key not in models]
    print(f"Sketchfab models: {len(models)} found{', not found: ' + ', '.join(missing) if missing else ''}", flush=True)

    textures = fetch_textures(os.path.join(work, "textures"))
    if not all(os.path.isfile(os.path.join(SKIES, f"sky_{light}.jpg")) for light in ("day", "dusk", "night")):
        run(sys.executable, os.path.join(HERE, "prepare_skies.py"), SKIES)

    blend = os.path.join(work, "spot.blend")
    spec = os.path.join(work, "spec.json")
    with open(spec, "w", encoding="utf-8") as fh:
        json.dump({"sketchfab": models, "textures": textures, "turns": turns, "out": blend,
                   "sky_night": os.path.join(SKIES, "sky_night.jpg"),
                   **({"renders": os.path.join(work, "renders")} if render else {})}, fh, indent=1)
    run(blender(), "-b", "--python-exit-code", "1", "--python", os.path.join(HERE, "compose_spot.py"), "--", spec, quiet=True)
    with open(os.path.join(work, "spot_built.json"), encoding="utf-8") as fh:
        built = json.load(fh)
    used = [key for key in models if key not in built["skipped"]]
    print("Sketchfab models in the car park (CC BY 4.0; CREDITS.txt and street.json's credit name them):")
    for key in used:
        _, title, maker, url, _ = MODELS[key]
        print(f'  - "{title}" by {maker}: {url}')
    if blend_only:
        return

    prepared = os.path.join(work, "prepared")
    shutil.rmtree(prepared, ignore_errors=True)
    caps = [arg for key in used for arg in ("--cap", f"{key}_={MODELS[key][4]}")]
    run(sys.executable, os.path.join(REPO, "tools", "Props", "prepare_prop.py"), blend, "street", prepared, "2048", "--jpg",
        "--keep-names", *caps)
    sky = ",".join(f"{light}={os.path.join(SKIES, f'sky_{light}.jpg')}" for light in ("day", "dusk", "night"))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    run("dotnet", "run", "--project", os.path.join(REPO, "tools", "DinerScene", "DinerSceneBuilder"), "--", prepared, "street",
        OUT, "--sky", sky, "--ground-half", "200")
    print(f"Lamps (street.json's night lamps, the game's x, y, z): {built['lamps']}")


if __name__ == "__main__":
    args = without_blender_option(sys.argv)[1:]
    work = os.path.join(tempfile.gettempdir(), "touge_spot")
    turns = dict(TURNS)
    folders = []
    blend_only = render = False
    while args:
        arg = args.pop(0)
        if arg == "--work" and args:
            work = args.pop(0)
        elif arg == "--turn" and args and "=" in args[0]:
            key, degrees = args.pop(0).split("=", 1)
            turns[key] = float(degrees)
        elif arg == "--blend-only":
            blend_only = True
        elif arg == "--render":
            render = True
        elif arg.startswith("--"):
            sys.exit(__doc__)
        else:
            folders.append(arg)
    if not folders:
        sys.exit(__doc__)
    main(folders, work, turns, blend_only, render)
