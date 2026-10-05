"""
Builds the diner's street, start to end, out of the three downloads (the Quaternius Downtown City MegaKit, WaajaaH's
Diner Exterior Asset Reworked, both from their pages as .zip):

  python build_street.py <folder with the zips> [<work folder>] [--blender <blender.exe>]

  1. unpacks the zips into the work folder (default: a "diner_street" folder in the system's temp)
  2. prepare_pictures.py draws the signs
  3. compose_street.py lays the street out in Blender, saved as street.blend in the work folder (open it to look)
  4. ../Props/prepare_prop.py makes the OBJ and the AC pictures of it (2048 at most, the maps as JPEGs, the materials
     named as in Blender, for diner.json's glow)
  5. DinerSceneBuilder writes Street Corsa/Content/streetcorsa/streets/mainstreet/street.kn5 and ground.bin, the
     station's skies round it: Main Street, the Cruise screen's street with the diner on it

Needs Blender (--blender <blender.exe>, the BLENDER variable, else the newest in Program Files), Python with numpy and
Pillow, and the .NET SDK.
"""
import glob
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
from scene_tools import blender, without_blender_option  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Street Corsa", "Content", "streetcorsa", "streets", "mainstreet", "street.kn5")
SKIES = os.path.join(REPO, "tools", "StationScene", "skies")

ZIPS = {
    "kit": "Downtown City MegaKit*.zip",
    "diner": "diner-exterior-asset-reworked*.zip",
}


def unpack(downloads, work):
    folders = {}
    for key, pattern in ZIPS.items():
        found = glob.glob(os.path.join(downloads, pattern))
        if not found:
            sys.exit(f"No {pattern} in {downloads}")
        folder = os.path.join(work, key)
        if not os.path.isdir(folder):
            with zipfile.ZipFile(found[0]) as z:
                z.extractall(folder)
        folders[key] = folder
    # Walked, not globbed: the kit's folder is named with brackets ("[Standard]"), which a glob reads as a pattern
    kit = [root for root, _, _ in os.walk(folders["kit"]) if root.replace("\\", "/").endswith("Exports/glTF (Godot)")]
    source = os.path.join(folders["diner"], "source")
    fbx = [os.path.join(source, f) for f in os.listdir(source) if f.lower().endswith(".fbx")] if os.path.isdir(source) else []
    if not kit or not fbx:
        sys.exit("The zips are not as expected: a kit with Exports/glTF (Godot), a diner with source/*.fbx")
    return kit[0], fbx[0], os.path.join(folders["diner"], "textures")


def run(*command):
    print(">", " ".join(f'"{c}"' if " " in c else c for c in command))
    subprocess.run(command, check=True)


def main(downloads, work):
    os.makedirs(work, exist_ok=True)
    kit, fbx, textures = unpack(downloads, work)
    signs = os.path.join(work, "signs")
    blend = os.path.join(work, "street.blend")
    prepared = os.path.join(work, "prepared")

    run(sys.executable, os.path.join(HERE, "prepare_pictures.py"), signs)
    run(blender(), "-b", "--python", os.path.join(HERE, "compose_street.py"), "--", kit, fbx, textures, signs, blend)
    # prepare_prop.py keeps a picture it took out before: a fresh folder takes the new ones
    shutil.rmtree(prepared, ignore_errors=True)
    run(sys.executable, os.path.join(REPO, "tools", "Props", "prepare_prop.py"), blend, "street", prepared, "2048", "--jpg",
        "--keep-names")
    sky = ",".join(f"{light}={os.path.join(SKIES, f'sky_{light}.jpg')}" for light in ("day", "dusk", "night"))
    run("dotnet", "run", "--project", os.path.join(HERE, "DinerSceneBuilder"), "--", prepared, "street", OUT, "--sky", sky)


if __name__ == "__main__":
    args = without_blender_option(sys.argv)
    if len(args) not in (2, 3):
        sys.exit(__doc__)
    main(args[1], args[2] if len(args) == 3 else os.path.join(tempfile.gettempdir(), "diner_street"))
