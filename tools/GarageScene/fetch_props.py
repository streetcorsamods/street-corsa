"""
Downloads the Poly Haven models the garage is dressed with (CC0, https://polyhaven.com/license): each one's glTF at 1k
with its .bin and pictures, into <out folder>/<id>/, ready for tools/Props/prepare_prop.py. A model already there whose
files match Poly Haven's checksums is kept (a re-run downloads only what is missing or changed).

  python fetch_props.py <out folder> [id ...]

With no ids, every model in MODELS. Needs nothing but Python.
"""
import hashlib
import json
import os
import sys
import urllib.request

API = "https://api.polyhaven.com/files/{id}"
RESOLUTION = "1k"

# Poly Haven ids, by what they are in the garage
MODELS = [
    # Storage and furniture
    "metal_tool_chest", "metal_toolbox", "tool_cart", "worn_metal_rack", "metal_office_desk",
    "WoodenTable_03", "metal_stool_01", "wooden_stool_01", "painted_wooden_shelves",
    # Workshop
    "bench_vice_01", "old_military_compressor", "portable_welding_cart", "old_drill_press", "tire_pump",
    "korean_fire_extinguisher_01", "ladder_sectioned_01", "hand_truck",
    # Cans, drums, tyres, wheels
    "metal_jerrycan", "metal_jerrycan_green", "Barrel_01", "small_oil_can_01", "oil_tin", "old_tyre",
    "rusted_wheel_rim_01", "propane_tank",
    # Tools
    "ratchet_wrench", "adjustable_wrench", "pipe_wrench", "pliers", "screwdriver",
    "cross_pein_hammer", "crowbar_01", "rusted_hacksaw",
    # Clutter
    "wooden_broom", "dustpan", "wooden_bucket_01", "wooden_crate_01", "cardboard_box_01",
    "spray_paint_bottles",
]


def fetch_json(url):
    request = urllib.request.Request(url, headers={"User-Agent": "StreetCorsa-asset-fetch"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def md5_of(path):
    digest = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download(url, path, md5):
    """The file at url into path, unless one with that checksum is there already"""
    if os.path.exists(path) and md5_of(path) == md5:
        return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": "StreetCorsa-asset-fetch"})
    with urllib.request.urlopen(request, timeout=120) as response, open(path + ".part", "wb") as f:
        f.write(response.read())
    if md5_of(path + ".part") != md5:
        os.remove(path + ".part")
        raise IOError(f"{url}: checksum does not match")
    os.replace(path + ".part", path)
    return True


def fetch(out, model_id, files=None):
    """The model's glTF and its pictures into out/<id>, unless there already; files is the API's list of its files, fetched when not given"""
    files = files or fetch_json(API.format(id=model_id))
    gltf = files.get("gltf", {}).get(RESOLUTION, {}).get("gltf")
    if not gltf:
        raise IOError(f"{model_id}: no glTF at {RESOLUTION}")
    folder = os.path.join(out, model_id)
    got = download(gltf["url"], os.path.join(folder, os.path.basename(gltf["url"])), gltf["md5"])
    for relpath, item in gltf.get("include", {}).items():
        got |= download(item["url"], os.path.join(folder, *relpath.split("/")), item["md5"])
    return got


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    out = sys.argv[1]
    ids = sys.argv[2:] or MODELS
    failed = []
    for model_id in ids:
        try:
            print(f"{model_id}: {'downloaded' if fetch(out, model_id) else 'up to date'}", flush=True)
        except (IOError, OSError, ValueError) as ex:
            print(f"{model_id}: FAILED {ex}", flush=True)
            failed.append(model_id)
    if failed:
        print(f"{len(failed)} failed: {', '.join(failed)}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
