"""
What the asset scripts share (tools/DinerScene, tools/Props, tools/ShopScene, tools/WarehouseScene): finding Blender, and
Poly Haven's pictures.

A script in tools/<folder>/ imports it with:

    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
    from scene_tools import ...

Blender is the one named by --blender <blender.exe> on the script's command line, else by the BLENDER environment
variable, else the newest installed under Program Files (by its version: 4.10 is newer than 4.9).
"""
import glob
import os
import re
import sys
import urllib.request

POLY_HAVEN = "https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/{id}/{id}_diff_1k.jpg"


def without_blender_option(argv):
    """
    The command line less --blender <path>, for the script's own reading of it; the path goes into BLENDER, so blender()
    finds it here and in any script this one runs
    """
    args = list(argv)
    if "--blender" in args:
        at = args.index("--blender")
        if at + 1 >= len(args):
            sys.exit("--blender takes the path of blender.exe")
        os.environ["BLENDER"] = args[at + 1]
        del args[at:at + 2]
    return args


def blender():
    """Blender's executable (see the top of this file)"""
    named = os.environ.get("BLENDER")
    if named:
        if not os.path.isfile(named):
            sys.exit(f"No Blender at {named} (BLENDER or --blender)")
        return named
    programs = os.environ.get("ProgramFiles", r"C:\Program Files")
    found = glob.glob(os.path.join(programs, "Blender Foundation", "Blender *", "blender.exe"))
    if not found:
        sys.exit("Blender not found: install it, or name its blender.exe with --blender or the BLENDER environment variable")
    return max(found, key=_version_of)


def _version_of(path):
    """A Blender install's version from its folder ("Blender 4.10" gives (4, 10)); () when it has none"""
    match = re.search(r"Blender (\d+(?:\.\d+)*)", os.path.basename(os.path.dirname(path)))
    return tuple(int(n) for n in match.group(1).split(".")) if match else ()


def poly_haven(texture_id):
    """A Poly Haven texture's 1k diffuse picture (CC0, https://polyhaven.com/license), as the JPEG's bytes"""
    with urllib.request.urlopen(POLY_HAVEN.format(id=texture_id), timeout=60) as response:
        return response.read()


def poly_haven_surface(out, surface, texture_id, size, quality):
    """
    A Poly Haven texture as a surface's picture, <surface>_<id>.jpg in the out folder, size x size; one already there is
    kept (a re-run downloads nothing). So only a whole picture is ever there: what came down must decode as one (not an
    error page or a cut-off download), and it is written beside its name and then moved onto it
    """
    import cv2
    import numpy as np

    path = os.path.join(out, f"{surface}_{texture_id}.jpg")
    if os.path.exists(path):
        return
    image = cv2.imdecode(np.frombuffer(poly_haven(texture_id), np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        sys.exit(f"Poly Haven's {texture_id} did not come down as a picture: run again, or check the id")
    ok, jpeg = cv2.imencode(".jpg", cv2.resize(image, (size, size), interpolation=cv2.INTER_AREA), [cv2.IMWRITE_JPEG_QUALITY, quality])
    if not ok:
        sys.exit(f"Could not make {path} a JPEG")
    temp = path + ".tmp"
    with open(temp, "wb") as f:
        f.write(jpeg.tobytes())
    os.replace(temp, path)
