"""
Builds the diner's street, start to end, out of the downloads:

  python build_street.py <folder> [<folder> ...] [--work <work folder>] [--turn <car id>=<degrees> ...]
      [--blend-only] [--bake] [--kit] [--blender <blender.exe>]

The folders are searched for the zips, as downloaded from their pages: the Quaternius Downtown City MegaKit (the road,
CC0) and WaajaaH's Diner Exterior Asset Reworked (CC BY 4.0) are needed; the Sketchfab models in MODELS (CC BY 4.0, a
login to download) are taken when they are there, and the street is dressed with those (compose_street.py says where
each goes). The base game's: python build_street.py "D:/JUEGOS/3D Models/scenes" "D:/JUEGOS/3D Models/diner"
"D:/JUEGOS/3D Models/station" (the station's folder for its six parked cars).

  1. unpacks the zips into the work folder (default: a "diner_street" folder in the system's temp), and downloads the
     Poly Haven plants (CC0) the lawns are dressed with
  2. prepare_pictures.py draws the signs and the houses' paint; ../StationScene/bake_cards.py takes the pictures of the
     trees, bushes and flowers on the lawns and of the far palms, as crossed cards (kept in the work folder's cards once
     taken; --bake takes them again)
  3. compose_street.py lays the street out in Blender, saved as street.blend in the work folder (open it to look)
  4. ../Props/prepare_prop.py makes the OBJ and the AC pictures of it (2048 at most for the road and the diner, the
     Sketchfab models' smaller by MODELS' cap, the maps as JPEGs, the materials named as in Blender, for the glow)
  5. DinerSceneBuilder writes Street Corsa/Content/streetcorsa/streets/mainstreet/street.kn5 and ground.bin, the
     station's skies round it: Main Street, the Cruise screen's street with the diner on it, and the three other diners'
     looks in it (objects named LOOK_<DINER>_..., each a node of its own the game shows for that diner alone)

Each Sketchfab model used is printed with its credit line (CREDITS.txt and street.json's credit name them all). A car
whose nose comes out the wrong way is turned with --turn <id>=180. --blend-only stops at street.blend (steps 1 to 3), to
look at the layout.

--kit builds the street kit instead (step 39, see docs/screens/diner.md): the stage (the road, the lot, the diner, the
wires, the looks) into the work folder's stage folder (copy its street.kn5 and ground.bin into the street's folder), every
piece compose_street.py put on it into Street Corsa/Content/streetcorsa/streetkit (made anew: a folder a piece, its KN5
and piece.json), and today's street as a layout into the work folder's layout.json.

Needs Blender (--blender <blender.exe>, the BLENDER variable, else the newest in Program Files), Python with numpy and
Pillow, and the .NET SDK.
"""
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "GarageScene"))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "StationScene"))
from scene_tools import blender, without_blender_option  # noqa: E402
from fetch_props import API, fetch, fetch_json  # noqa: E402
from build_station import fetch_alpha  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Street Corsa", "Content", "streetcorsa", "streets", "mainstreet", "street.kn5")
SKIES = os.path.join(REPO, "tools", "StationScene", "skies")

ZIPS = {
    "kit": "Downtown City MegaKit*.zip",
    "diner": "diner-exterior-asset-reworked*.zip",
}

# The Sketchfab models (all CC BY 4.0), by the id compose_street.py places them by: the zip's name, the title, the maker,
# the page, and the size the model's pictures are held to (prepare_prop.py's --cap): the street is seen from across the
# road, and GitHub takes no file over 100 MB
MODELS = {
    "midcity": ("midcity_buildings_set*.zip", "Midcity Buildings Set - Low Poly Model", "Daniel Zhabotinsky",
                "https://sketchfab.com/3d-models/midcity-buildings-set-low-poly-model-d5cbc471d20c4c9da31c3df656648916", 1024),
    "rentable_stores": ("rentable_stores*.zip", "Rentable Stores", "Wardster",
                        "https://sketchfab.com/3d-models/rentable-stores-b62e317e129e456f884ce6be4904aab3", 1024),
    "harbor_breakfast": ("harbor_breakfast_building*.zip", "Harbor Breakfast Building", "Raphael Escamilla",
                         "https://sketchfab.com/3d-models/harbor-breakfast-building-5a7968c435934fcea47b5c5484ee4e13", 512),
    "brick_shop": ("low-poly_old_american_brick_building*.zip", "Low-poly Old American brick building with shop", "Orzechu",
                   "https://sketchfab.com/3d-models/low-poly-old-american-brick-building-with-shop-3170993b67ee4e58b27d69d5877f71bd", 1024),
    "diner_sign": ("diner_sign*.zip", "Diner Sign", "A9908244",
                   "https://sketchfab.com/3d-models/diner-sign-a628a61b62304aedac765d9eb5fa61d3", 1024),
    "star_sign": ("classic_american_signboard*.zip", "Classic American signboard", "exiS7-Gs",
                  "https://sketchfab.com/3d-models/classic-american-signboard-c1d6ed77e69442f7ace544a7f0182e1e", 1024),
    "billboard": ("billboard*.zip", "Billboard (.psd layered texture maps included)", "TampaJoey",
                  "https://sketchfab.com/3d-models/billboard-psd-layered-texture-maps-included-20d1a51fbfa94373900e29de74a0f9ee", 1024),
    "palm_tall": ("realistic_palm_tree_4_free*.zip", "Realistic Palm Tree 4 Free", "Next Spring",
                  "https://sketchfab.com/3d-models/realistic-palm-tree-4-free-917c18d6b2d04d33950dea6e20bc074f", 1024),
    "palm_fan": ("realistic_palm_tree_free*.zip", "Realistic Palm Tree Free", "Next Spring",
                 "https://sketchfab.com/3d-models/realistic-palm-tree-free-39052ea764c945858449e699318efa53", 1024),
    "banyan": ("chinese_banyan_ficus_microcarpa*.zip", "Chinese Banyan (Ficus Microcarpa)", "Valery.Li",
               "https://sketchfab.com/3d-models/chinese-banyan-ficus-microcarpa-2a0dbcdf8f5d48f5ad79987c7a8170ce", 1024),
    "fleetwood": ("house_fleetwood_design_50*.zip", "House: \"Fleetwood\" (Design 50)", "AleixoAlonso",
                  "https://sketchfab.com/3d-models/house-fleetwood-design-50-bd4f4d5b7fca476cb7d260a605192bc2", 512),
    "cobra_head": ("caltrans_streetlight_2*.zip", "Caltrans streetlight 2", "Streetlights & ETC",
                   "https://sketchfab.com/3d-models/caltrans-streetlight-2-ff9c5c8c75a34e22a41794c304418050", 512),
    "pole_lamp": ("utility_pole_street_light_1*.zip", "Utility pole street light 1", "Streetlights & ETC",
                  "https://sketchfab.com/3d-models/utility-pole-street-light-1-1833d380f9e446c0958645f0e9f61132", 512),
    "telephone_pole": ("telephone_poles*.zip", "Telephone Poles", "Caboose3d",
                       "https://sketchfab.com/3d-models/telephone-poles-a76b68b838424a89b7db8a38f4c3d546", 512),
    "signal": ("1960s_horizontal_econolite_signal_set*.zip", "1960's Horizontal Econolite Signal set", "Signalrenders",
               "https://sketchfab.com/3d-models/1960s-horizontal-econolite-signal-set-afa4df9910334f17a3b27a6e0ccebeb0", 512),
    "hydrant": ("fire_hydrant*.zip", "Fire Hydrant", "Raphael Escamilla",
                "https://sketchfab.com/3d-models/fire-hydrant-0777a03dfb694e7dad44d9cd4f24aa0b", 256),
    "trash_can": ("green_city_trash_can*.zip", "Green City Trash Can", "Raphael Escamilla",
                  "https://sketchfab.com/3d-models/green-city-trash-can-c5d23e21daef4fc2a62704149f5f9af6", 256),
    "mailbox": ("us_mailbox*.zip", "US Mailbox", "Yaroslav Dubovikov",
                "https://sketchfab.com/3d-models/us-mailbox-3376b03ebf1d45ea99fbb6ff350489da", 256),
    "news_rack": ("newspaper_dispenser*.zip", "Newspaper Dispenser", "Caboose3d",
                  "https://sketchfab.com/3d-models/newspaper-dispenser-a523551bcaf84aa8bdf63150a629b6a2", 256),
    "bus_bench": ("city_bus_stop_bench*.zip", "City Bus Stop Bench (Low-Poly Prop)", "Derfmode",
                  "https://sketchfab.com/3d-models/city-bus-stop-bench-low-poly-prop-67c94d85ee1844fa81d31a4988c57914", 512),
    # The cars at the curbs
    "chrysler_saratoga": ("low_poly_car_-_chrysler_saratoga_1960*.zip", "Low Poly Car - Chrysler Saratoga 1960", "roh3d",
                          "https://sketchfab.com/3d-models/low-poly-car-chrysler-saratoga-1960-3fa89e6bf78b49aab111af4195efb6fe", 512),
    "cadillac_75": ("low_poly_car_-_cadillac_75*.zip", "Low Poly Car - Cadillac 75 Sedan 1953", "roh3d",
                    "https://sketchfab.com/3d-models/low-poly-car-cadillac-75-sedan-1953-2935f8faa5f64ce4a3bd65af0f925c21", 512),
    "palomino": ("palomino_special_67*.zip", "Palomino Special '67 - Low Poly model", "Daniel Zhabotinsky",
                 "https://sketchfab.com/3d-models/palomino-special-67-low-poly-model-8419d645064e4b1699d970375e1c3cc7", 512),
    "phoenix": ("phoenix_455_71*.zip", "Phoenix 455 '71 - Low Poly model", "Daniel Zhabotinsky",
                "https://sketchfab.com/3d-models/phoenix-455-71-low-poly-model-955edc733c6d44fabc0ad7c246a15896", 512),
    "chapman": ("chapman_73*.zip", "Chapman '73 - Low Poly model", "Daniel Zhabotinsky",
                "https://sketchfab.com/3d-models/chapman-73-low-poly-model-f30b644b59b54e809355009dce463d47", 512),
    "sialia": ("sialia_sw_68*.zip", "Sialia SW '68 - Low Poly model", "Daniel Zhabotinsky",
               "https://sketchfab.com/3d-models/sialia-sw-68-low-poly-model-eea2291a90f04a4b860c9e74be14f2d6", 512),
    "duntov": ("duntov_57*.zip", "Duntov '57 - Low Poly model", "Daniel Zhabotinsky",
               "https://sketchfab.com/3d-models/duntov-57-low-poly-model-a6840c4f4b4b45ca805d0f76e809f042", 512),
    "bokaroo": ("bokaroo_67*.zip", "Bokaroo '67 - Low Poly model", "Daniel Zhabotinsky",
                "https://sketchfab.com/3d-models/bokaroo-67-low-poly-model-22015d1863d6455aa31cfd738b972c50", 512),
    "chevelle": ("1968_chevrolet_chevelle_ss*.zip", "1968 Chevrolet Chevelle SS", "robinmikart",
                 "https://sketchfab.com/3d-models/1968-chevrolet-chevelle-ss-7558ed69102a4d59be0ab1db25fc5dd3", 512),
    "sedan_1953": ("free_1953_american_sedan*.zip", "FREE 1953 American Sedan (Based on Oldsmobile)", "robinmikart",
                   "https://sketchfab.com/3d-models/free-1953-american-sedan-based-on-oldsmobile-18cc2b1da0164127baf1c8f5491841ae", 512),
    "truck_1970": ("1970_truck*.zip", "1970 Truck", "Raphael Escamilla",
                   "https://sketchfab.com/3d-models/1970-truck-e0953a2cfb7349dab78fa363fe6545e8", 512),
    # The gas station's cars (its folder), parked here too
    "buick_1955": ("*buick*.zip", "1955 Buick Special Downloadable", "robinmikart",
                   "https://sketchfab.com/3d-models/1955-buick-special-downloadable-1a6d42ef69294498937aab039ececb00", 512),
    "packard_1955": ("*packard*.zip", "1955 American Sedan (Packard based) FREE", "robinmikart",
                     "https://sketchfab.com/3d-models/1955-american-sedan-packard-based-free-28d583f8d4bd4ce39dac2869fb77a197", 512),
    "taxi_1950s": ("*taxi*.zip", "FREE 1950s American Taxi (based on Checker)", "robinmikart",
                   "https://sketchfab.com/3d-models/free-1950s-american-taxi-based-on-checker-d1b10defaed94a1094419e84ca9b0bf1", 512),
    "studebaker_1951": ("*studebaker*.zip", "Studebaker - Car Model", "Allie2k",
                        "https://sketchfab.com/3d-models/studebaker-car-model-c7578ec0f82043e6b392fcdf9b00c743", 512),
    "chevrolet_c10_1963": ("*chevrolet_c10*.zip", "Low Poly Car - Chevrolet C10 Pickup 1963", "roh3d",
                           "https://sketchfab.com/3d-models/low-poly-car-chevrolet-c10-pickup-1963-679354c151984747bb74310ec5af8995", 512),
    "rancher_50": ("*rancher*50*.zip", "Rancher '50 Custom - Low Poly model", "DanielZhabotinsky",
                   "https://sketchfab.com/3d-models/rancher-50-custom-low-poly-model-7f75854aba96417f8279bd9f1b24e6b2", 512),
    # The diners' looks (step 37, phase 4): what the Hi-Way Grill adds to the one street (the station's folder has the trucks)
    "wrecker": ("*wrecker*.zip", "Wrecker (I.D.:105-645-73)", "-X-ScornGames",
                "https://sketchfab.com/3d-models/wrecker-id105-645-73-2b594f46dd0e42faab75420dd7015dad", 512),
    "heavy_tow_truck": ("heavy_duty_tow_truck*.zip", "Heavy Duty Tow Truck 3d Model", "3DMODEL",
                        "https://sketchfab.com/3d-models/heavy-duty-tow-truck-3d-model-246fc01e05d6465eadeac4ea0d292e4b", 512),
    "old_tire_inflator": ("old_tire_inflator*.zip", "Old Tire Inflator", "gorzi",
                          "https://sketchfab.com/3d-models/old-tire-inflator-3ab1bef1829b44a0857f0287a670c5f0", 256),
    "vintage_soda_machien": ("vintage_soda_machien*.zip", "Vintage Soda Machien", "Han1993",
                             "https://sketchfab.com/3d-models/vintage-soda-machien-05b7b54f12a74e659a8561614e63aee2", 256),
    "city_props": ("city_props_collection_volume_2*.zip", "City Props Collection (volume 2)", "TampaJoey",
                   "https://sketchfab.com/3d-models/city-props-collection-volume-2-2f52ea53c58e455f9b69d11f58763c96", 512),
}

# Poly Haven's plants (CC0), downloaded by the build, and the cards taken of them and of two Sketchfab models: by the name
# compose_street.py knows a card by, the model (a Poly Haven id, or a Sketchfab one of MODELS), the views (1: the front
# only; 2: the side too), the picture's size and the meshes kept (regular expressions; None keeps all)
POLY_HAVEN = ["jacaranda_tree", "island_tree_02", "tree_small_02", "searsia_lucida", "flower_gazania", "periwinkle_plant"]
CARDS = [
    ("jacaranda", "jacaranda_tree", 2, 1024, None),
    ("island", "island_tree_02", 2, 1024, None),
    ("small_tree", "tree_small_02", 2, 1024, None),
    ("banyan", "banyan", 2, 1024, None),
    ("palm", "palm_tall", 2, 1024, None),
    ("bush", "searsia_lucida", 2, 512, [r"searsia_lucida_a_LOD0$"]),
    ("gazania", "flower_gazania", 2, 512, [r"flower_gazania_h_LOD0$"]),
    ("periwinkle", "periwinkle_plant", 2, 512, [r"periwinkle_plant_01_LOD0$"]),
]


# The kit's pieces (--kit): the tags a piece is picked by (the game's street recipes ask for a tag), first match wins,
# and what more its piece.json says: a lamp post's light (where its lamp is lit at night, the piece's own space: x
# across, y up, z ahead of the post, under the end of its arm over the road)
KIT = os.path.join(REPO, "Street Corsa", "Content", "streetcorsa", "streetkit")
TAGS = [
    (r"midcity_(8|14)$", ["shop", "corner_shop"], {}),
    (r"midcity_\d+$|harbor_breakfast$|brick_shop$|rentable_stores$", ["shop"], {}),
    (r"house_", ["house"], {}),
    (r"palm_(tall|fan)$", ["palm"], {}),
    (r"card_palm$", ["palm_card"], {}),
    (r"card_", ["plant_card"], {}),
    (r"cobra_head$", ["lamp"], {"lamp": [0.0, 9.6, 2.9]}),
    (r"pole_lamp$", ["lamp"], {"lamp": [0.0, 10.2, 1.25]}),
    (r"telephone_pole", ["pole"], {}),
    (r"signal$", ["signal"], {}),
    (r"hydrant$", ["hydrant"], {}),
    (r"parking_meter$", ["meter"], {}),
    (r"trash_can$|mailbox$|news_rack_[ab]$|bus_bench$", ["furniture"], {}),
    (r"diner_sign$|star_sign$", ["sign", "googie"], {}),
    (r"truck_1970$", ["curb_car", "truck"], {}),
]
CAR_IDS = ("chrysler_saratoga", "cadillac_75", "palomino", "phoenix", "chapman", "sialia", "duntov", "bokaroo", "chevelle",
           "sedan_1953", "buick_1955", "packard_1955", "taxi_1950s", "studebaker_1951", "chevrolet_c10_1963", "rancher_50")


def tags_of(piece_id):
    if piece_id in CAR_IDS:
        return ["curb_car"], {}
    for pattern, tags, more in TAGS:
        if re.match(pattern, piece_id):
            return tags, more
    return ["misc"], {}


def credit_of(sources):
    """A piece's credit line: the models it is made of (the Sketchfab ones CC BY 4.0, Poly Haven's CC0), or ours"""
    lines = []
    for source in sources:
        if source.startswith("card:"):
            model = next((m for name, m, *_ in CARDS if name == source[5:]), None)
            source = model if model in MODELS else None
            if model in POLY_HAVEN:
                lines.append(f"{model} by Poly Haven (CC0)")
        if source in MODELS:
            _, title, maker, url, _ = MODELS[source]
            lines.append(f'"{title}" by {maker} ({url}), CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/), '
                         f"scaled, decimated and converted to KN5")
    return "; ".join(dict.fromkeys(lines)) or "Street Corsa (our own)"


def write_kit(built, kit, written):
    """piece.json beside each piece's KN5: its footprint and height (metres, as made), its tags, its credit"""
    for piece_id in written:
        entry = built["pieces"][piece_id]
        width, height, depth = entry["size"]
        tags, more = tags_of(piece_id)
        piece = {"footprint": [width, depth], "height": height, "tags": tags, "weight": 1.0, **more,
                 "credit": credit_of(entry["sources"])}
        with open(os.path.join(kit, piece_id, "piece.json"), "w", encoding="utf-8", newline="\n") as fh:
            json.dump(piece, fh, indent=2)
            fh.write("\n")


def bake_cards(work, models, again):
    """
    The cards' pictures, taken when not there yet (or again; or when a model they are of has come since): the folder
    they are in. A card of a Sketchfab model not downloaded is left out
    """
    haven = os.path.join(work, "polyhaven")
    for model_id in POLY_HAVEN:
        # The API's list of the model's files once, for the model and its alpha maps both
        files = fetch_json(API.format(id=model_id))
        got = fetch(haven, model_id, files)
        alphas = fetch_alpha(haven, model_id, files)
        print(f"{model_id}: {'downloaded' if got else 'up to date'}{f', {alphas} alpha map(s)' if alphas else ''}", flush=True)
    wanted = [(name, models.get(model) or gltf_of(os.path.join(haven, model)), views, size, keep)
              for name, model, views, size, keep in CARDS if model in models or model in POLY_HAVEN]
    cards = os.path.join(work, "cards")
    listed = os.path.join(cards, "cards.json")
    have = json.load(open(listed, encoding="utf-8")) if os.path.isfile(listed) else {}
    if again or any(name not in have for name, *_ in wanted):
        spec = os.path.join(work, "cards_spec.json")
        with open(spec, "w", encoding="utf-8") as fh:
            json.dump({"out": cards, "cards": [{"name": name, "path": path, "views": views, "size": size, "keep": keep}
                                               for name, path, views, size, keep in wanted]}, fh, indent=1)
        run(blender(), "-b", "--python", os.path.join(REPO, "tools", "StationScene", "bake_cards.py"), "--", spec, quiet=True)
    return cards


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


def main(folders, work, turns, blend_only=False, bake_again=False, kit=False):
    os.makedirs(work, exist_ok=True)
    needed = {}
    for key, pattern in ZIPS.items():
        path = find(folders, pattern)
        if not path:
            sys.exit(f"No {pattern} in {', '.join(folders)}")
        needed[key] = unpack_zip(path, os.path.join(work, key))
    # Walked, not globbed: the kit's folder is named with brackets ("[Standard]"), which a glob reads as a pattern
    quaternius = [root for root, _, _ in os.walk(needed["kit"]) if root.replace("\\", "/").endswith("Exports/glTF (Godot)")]
    source = os.path.join(needed["diner"], "source")
    fbx = [os.path.join(source, f) for f in os.listdir(source) if f.lower().endswith(".fbx")] if os.path.isdir(source) else []
    if not quaternius or not fbx:
        sys.exit("The zips are not as expected: a kit with Exports/glTF (Godot), a diner with source/*.fbx")

    models = {}
    for key, (pattern, *_rest) in MODELS.items():
        path = find(folders, pattern)
        if path:
            models[key] = gltf_of(unpack_zip(path, os.path.join(work, "sketchfab", key)))
    missing = [key for key in MODELS if key not in models]
    print(f"Sketchfab models: {len(models)} found{', not found: ' + ', '.join(missing) if missing else ''}", flush=True)

    signs = os.path.join(work, "signs")
    blend = os.path.join(work, "street.blend")
    prepared = os.path.join(work, "prepared")
    run(sys.executable, os.path.join(HERE, "prepare_pictures.py"), signs)
    cards = bake_cards(work, models, bake_again)
    spec = os.path.join(work, "spec.json")
    kit_blend = os.path.join(work, "kit.blend")
    with open(spec, "w", encoding="utf-8") as fh:
        json.dump({"kit": quaternius[0], "diner": {"fbx": fbx[0], "textures": os.path.join(needed["diner"], "textures")},
                   "signs": signs, "cards": cards, "sketchfab": models, "turns": turns, "out": blend,
                   **({"kit_out": kit_blend} if kit else {})}, fh, indent=1)
    # A failing script leaves Blender's exit code at 0 unless told: the build stops there rather than on an old street.blend
    run(blender(), "-b", "--python-exit-code", "1", "--python", os.path.join(HERE, "compose_street.py"), "--", spec, quiet=True)
    with open(os.path.join(work, "street_built.json"), encoding="utf-8") as fh:
        built = json.load(fh)
    used = [key for key in models if key not in built["skipped"]]
    print("Sketchfab models in the street (CC BY 4.0; CREDITS.txt and street.json's credit name them):")
    for key in used:
        _, title, maker, url, _ = MODELS[key]
        print(f'  - "{title}" by {maker}: {url}')
    if blend_only:
        return

    # prepare_prop.py keeps a picture it took out before: a fresh folder takes the new ones
    shutil.rmtree(prepared, ignore_errors=True)
    caps = [arg for key in used for arg in ("--cap", f"{key}_={MODELS[key][4]}")]
    run(sys.executable, os.path.join(REPO, "tools", "Props", "prepare_prop.py"), blend, "street", prepared, "2048", "--jpg",
        "--keep-names", *caps)
    sky = ",".join(f"{light}={os.path.join(SKIES, f'sky_{light}.jpg')}" for light in ("day", "dusk", "night"))
    # The kit's build writes the stage into the work folder (the game's street is still the one model) and the pieces
    # into the game's streetkit, made anew
    out = os.path.join(work, "stage", "street.kn5") if kit else OUT
    os.makedirs(os.path.dirname(out), exist_ok=True)
    run("dotnet", "run", "--project", os.path.join(HERE, "DinerSceneBuilder"), "--", prepared, "street", out, "--sky", sky)
    print(f"Lamps (street.json's night lamps, the game's x, z): {[[round(x, 1), round(-y, 1)] for x, y in built['lamps']]}")
    if not kit:
        return

    prepared_kit = os.path.join(work, "prepared_kit")
    shutil.rmtree(prepared_kit, ignore_errors=True)
    # Every house carries its own siding, lawn and plants' pictures, and every one is seen from across a street at the nearest
    house_caps = ["--cap", "house_=512", "--cap", "card_=512", "--cap", "lawn_=256"]
    run(sys.executable, os.path.join(REPO, "tools", "Props", "prepare_prop.py"), kit_blend, "kit", prepared_kit, "2048", "--jpg",
        "--keep-names", *caps, *house_caps)
    shutil.rmtree(KIT, ignore_errors=True)
    run("dotnet", "run", "--project", os.path.join(HERE, "DinerSceneBuilder"), "--", prepared_kit, "kit", KIT, "--kit")
    written = sorted(d for d in os.listdir(KIT) if os.path.isfile(os.path.join(KIT, d, d + ".kn5")))
    write_kit(built, KIT, written)
    with open(os.path.join(work, "layout.json"), "w", encoding="utf-8") as fh:
        json.dump({"pieces": built["layout"], "lamps": built["lamps"]}, fh, indent=1)
    print(f"Kit: {len(written)} pieces in {KIT}; the layout of today's street ({len(built['layout'])} placements) in {work}")


if __name__ == "__main__":
    args = without_blender_option(sys.argv)[1:]
    work = os.path.join(tempfile.gettempdir(), "diner_street")
    turns = {}
    folders = []
    blend_only = bake_again = kit = False
    while args:
        arg = args.pop(0)
        if arg == "--work" and args:
            work = args.pop(0)
        elif arg == "--turn" and args and "=" in args[0]:
            key, degrees = args.pop(0).split("=", 1)
            turns[key] = float(degrees)
        elif arg == "--blend-only":
            blend_only = True
        elif arg == "--bake":
            bake_again = True
        elif arg == "--kit":
            kit = True
        elif arg.startswith("--"):
            sys.exit(__doc__)
        else:
            folders.append(arg)
    if not folders:
        sys.exit(__doc__)
    main(folders, work, turns, blend_only, bake_again, kit)
