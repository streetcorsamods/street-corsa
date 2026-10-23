"""
The base game's bolt-on parts made from CC BY / CC0 models (2026-10-22: the SLRR-converted ones went): the aftermarket
wheels (rims/street and tyres/street), the mufflers (exhaust/mufflers), the nitrous bottles (engines/nitrous) and the stock
batteries' models (engines/stock). Each download is cut in Blender (cut_wheels.py, wheels.json: the rim and the tyre
apart, in the wheel's own space; cut_bolt_ons.py the rest), goes through tools/Props/prepare_prop.py and PropBuilder
(--keep-origin) into its folder, with a CREDITS.txt there naming what it was made from, and the parts' files are written
from the tables below (a wheel is one model at several widths: the game scales a shown wheel to the car's). The
batteries' files are the base game's own (engines' builds name them): only their model and credit are written in.

  python prepare_bolt_ons.py <folder of the downloads (the Sketchfab zips)> <parts folder> [--blender <blender.exe>]

Needs Blender, numpy and Pillow (prepare_prop.py), and PropBuilder built (dotnet build tools/Props/PropBuilder).
"""
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
sys.path.insert(0, os.path.join(HERE, "..", "Shared"))
from scene_tools import blender, without_blender_option  # noqa: E402

PREPARE = os.path.join(HERE, "..", "Props", "prepare_prop.py")
PROP_BUILDER = os.path.join(HERE, "..", "Props", "PropBuilder", "bin", "Debug", "net10.0", "PropBuilder.dll")
TYRE_FIT = "tyre:15in"
BY = ("CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/")
BY_SA = ("CC BY-SA 4.0", "https://creativecommons.org/licenses/by-sa/4.0/")

# The downloads: title, author, author's page, the model's page, licence
SOURCES = {
    "1969_ford_mustang_wheel": ("1969 Ford Mustang Wheel", "Kryox Shade", "https://sketchfab.com/kryoxshade", "https://sketchfab.com/3d-models/29f6cbebedf244faac77ed9667c194a8", BY),
    "camaro_copo_tyre": ("Camaro Copo Tyre", "Kryox Shade", "https://sketchfab.com/kryoxshade", "https://sketchfab.com/3d-models/2cc48cde98cc475dbc57f017adfef008", BY),
    "classic_wheel": ("Classic Wheel", "Kryox Shade", "https://sketchfab.com/kryoxshade", "https://sketchfab.com/3d-models/64c242c610dd4e8088f5f69394409c9d", BY),
    "chevy_impala_tyre": ("Chevy Impala Tyre", "Kryox Shade", "https://sketchfab.com/kryoxshade", "https://sketchfab.com/3d-models/6d787d68e791431ca457eea8a2fdec43", BY),
    "corvette_c2_wheel": ("Corvette C2 Wheel", "Kryox Shade", "https://sketchfab.com/kryoxshade", "https://sketchfab.com/3d-models/41f327f550c34f8194c2f5668c9b2e18", BY),
    "dunlop_r7_classic_racing_tire": ("Dunlop R7 Classic Racing Tire", "Kryox Shade", "https://sketchfab.com/kryoxshade", "https://sketchfab.com/3d-models/567aaf0b1aff4eeda4829963a3580e4b", BY),
    "70s_wheel_velha_escola_brasil_style": ("70s Wheel Velha Escola Brasil Style", "ijiklvn", "https://sketchfab.com/kelvin-carvalho", "https://sketchfab.com/3d-models/34e8e8c0be8e4c5d9a4164bb70fdf48d", BY),
    "classic_wheel_r15": ("Classic Wheel R15", "PaperDiCE", "https://sketchfab.com/PaperDiCE", "https://sketchfab.com/3d-models/c853b0d6c8bc42549bf61692b3ec0cd7", BY),
    "gm_rally_wheels": ("GM Rally Wheels", "cesa210", "https://sketchfab.com/cesa210", "https://sketchfab.com/3d-models/0110b22ce3624dedb3d1eed401507de0", BY_SA),
    "gm_rally_wheels_type_b": ("GM Rally Wheels (Type B)", "cesa210", "https://sketchfab.com/cesa210", "https://sketchfab.com/3d-models/13cf0eab9f4f4eee996869cdc34d8a94", BY_SA),
    "wire_rim": ("Wire rim", "rotating3d.com", "https://sketchfab.com/3dpeter", "https://sketchfab.com/3d-models/bda093957f8e47a7b27270de6c3e0d14", BY),
    "hoosier_drag_tire": ("hoosier drag tire", "dermaks", "https://sketchfab.com/dermaks", "https://sketchfab.com/3d-models/3778f71211314bba92bef0b1564a8ec5", BY),
    "exhaust_system": ("Exhaust System", "Kryox Shade", "https://sketchfab.com/kryoxshade", "https://sketchfab.com/3d-models/exhaust-system-81b892a3c9804d10a25dae64d81641a3", BY),
    "muffler": ("MUFFLER", "VR DESIGNER", "https://sketchfab.com/vr.designer_09", "https://sketchfab.com/3d-models/muffler-23ca47d8866b4d98ad50919d6ec48302", BY),
    "exhuast_muffler": ("Exhuast muffler", "Silver10211", "https://sketchfab.com/Silver10211", "https://sketchfab.com/3d-models/exhuast-muffler-3163e0c9ef224901a33568113f62582d", BY),
    "car_nitro_bottle": ("Car Nitro Bottle", "ion.fbx", "https://sketchfab.com/ionfbx", "https://sketchfab.com/3d-models/car-nitro-bottle-653db48eeaf543f3abec580e5f59fdbc", BY),
    "battery": ("Battery", "Helsingr", "https://sketchfab.com/helsingr", "https://sketchfab.com/3d-models/battery-07ad5f34a2c349a0bd1dc1bd46cb8424", BY),
}

# The rims: id stem, display name, the cut wheel (wheels.json) whose rim it is drawn with, widths (inches), mass (kg)
RIMS = [
    ("styled_steel_wheel", "Styled steel wheel", "styled_steel", [6, 7], 8.6),
    ("steel_wheel_poverty_cap", "Steel wheel with dog-dish cap", "steel_poverty_cap", [6, 7, 8], 8.8),
    ("steel_wheel_chrome_hubcap", "Steel wheel with chrome hubcap", "steel_hubcap", [6], 8.6),
    ("steel_wheel_full_cover", "Steel wheel with full wheel cover", "wheel_cover", [6], 9.2),
    ("steel_wheel_wire_cover", "Steel wheel with wire wheel cover", "wire_cover", [6], 9.4),
    ("slot_mag_wheel", "Slotted mag wheel", "slot_mag", [7, 8, 10, 12], 7.6),
    ("eight_spoke_mag_wheel", "Eight-spoke mag wheel", "eight_spoke_mag", [7, 8], 7.2),
    ("rally_wheel", "Rally wheel", "rally", [7, 8], 8.4),
    ("rally_wheel_chrome_trim", "Rally wheel with chrome trim ring", "rally_b", [7, 8], 8.6),
    ("wire_wheel", "Wire wheel", "wire_wheel", [6], 9.8),
    ("racing_wheel", "Slotted racing wheel", "racing", [7], 7.0),
]

# The tyres: id stem, display name, the cut wheel whose tyre it is drawn with, grip, rolling resistance, widths
TYRES = [
    ("bias_ply_blackwall", "Bias-ply blackwall tire", "steel_hubcap", 1.333, 0.0029, [6, 7]),
    ("bias_ply_whitewall", "Bias-ply wide whitewall tire", "wheel_cover", 1.333, 0.0029, [6, 7]),
    ("wide_tread_raised_letter", "Wide Tread raised-letter tire", "steel_poverty_cap", 1.40, 0.0029, [6, 7, 8]),
    ("belted_gt_raised_letter", "Belted GT raised-letter tire", "styled_steel", 1.45, 0.0028, [6, 7, 8]),
    ("radial", "Radial tire", "slot_mag", 1.50, 0.0026, [7, 8, 10]),
    ("low_profile_radial", "Low-profile radial tire", "eight_spoke_mag", 1.55, 0.0026, [7, 8]),
    ("vintage_racing", "Vintage racing tire", "racing", 1.62, 0.0030, [7]),
    ("drag_slick", "Drag slick", "slick", 1.75, 0.0035, [10, 12]),
]

# A tyre's size by its rim's width: section (mm) and rolling radius (m) of a 15-inch tyre of the day
SIZES = {6: (195, 0.300), 7: (215, 0.308), 8: (235, 0.315), 10: (275, 0.330), 12: (325, 0.340)}


# The mufflers (cut_bolt_ons.py): id stem and model, display name, download, mass (kg). They dress a tailpipe: each fits
# exhaust:tip, which a mod's exhaust pipe takes (no part of the base game does yet)
MUFFLERS = [
    ("oval_turbo_muffler", "Oval turbo muffler", "exhaust_system", 7.0),
    ("chambered_muffler", "Chambered muffler, dual outlet", "muffler", 8.5),
    ("round_muffler", "Round straight-through muffler with tip", "exhuast_muffler", 5.0),
]

# The nitrous bottles: id stem and model, display name, mass full (kg: a 10 lb bottle weighs 24.6 lb with its gas)
BOTTLES = [
    ("nitrous_bottle_10_lb", "Nitrous bottle, 10 lb", 11.2),
    ("twin_nitrous_bottles_2x10_lb", "Twin nitrous bottles, 2x10 lb", 22.4),
]

# The stock batteries' case colours (the download's is cream), and where its labels are on its picture (fractions of
# it: left, top, right, bottom), which keep theirs
BATTERIES = {"black": (34, 34, 36), "blue": (34, 58, 122), "silver": (172, 174, 178)}
BATTERY_LABELS = [(0.597, 0.053, 0.706, 0.360), (0.070, 0.502, 0.348, 0.601)]


def recolour(source, target, colour):
    """The battery's picture with its case (the cream: warm, not dark, off the labels) in another colour, its grime kept"""
    rgb = np.asarray(Image.open(source).convert("RGB")).astype(np.float32)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    light = 0.299 * r + 0.587 * g + 0.114 * b
    case = (r - b > 12) & (light > 70)
    h, w = light.shape
    for left, top, right, bottom in BATTERY_LABELS:
        case[int(top * h):int(bottom * h), int(left * w):int(right * w)] = False
    tinted = np.asarray(colour, np.float32)[None, None, :] * (light / np.median(light[case]))[..., None]
    out = np.where(case[..., None], np.clip(tinted, 0, 255), rgb)
    Image.fromarray(out.astype(np.uint8)).save(target)


def load_capacity(width_mm):
    """N, as the old tyres had it by their section (185 mm 7061, 205 8788, 280 15267, 325 19537), straight between"""
    pts = [(185, 7060.8), (205, 8788.4), (280, 15266.5), (325, 19536.9)]
    segment = next(((a, b) for a, b in zip(pts, pts[1:]) if width_mm <= b[0]), (pts[-2], pts[-1]))
    (a, la), (b, lb) = segment
    return round(la + (lb - la) * (width_mm - a) / (b - a), 1)


def credit_line(source):
    title, author, _, url, (lic, _) = SOURCES[source]
    return f'"{title}" by {author} ({url.replace("https://", "")}), {lic}'


WHEEL_CHANGES = ("Changes: cut out of the download (the rim and the tyre apart), decimated, turned to the wheel's own space and "
                 "scaled to a 15-inch wheel, converted to KN5 (tools/BoltOnParts/prepare_bolt_ons.py), textures scaled down. "
                 "A model under CC BY-SA 4.0 stays under it, changed or not.\n")
CHANGES = ("Changes: cut out of the download, decimated, scaled to metres and turned to the part's own space, converted to "
           "KN5 (tools/BoltOnParts/prepare_bolt_ons.py), textures scaled down.\n")
BATTERY_CHANGES = ("Changes: shortened from a truck battery's length to a car's, its case recoloured, scaled to metres and turned "
                   "to the part's own space, converted to KN5 (tools/BoltOnParts/prepare_bolt_ons.py), textures scaled down.\n")


def credits_file(used, changes=WHEEL_CHANGES):
    lines = []
    for model, source in used:
        title, author, author_url, url, (lic, lic_url) = SOURCES[source]
        lines.append(f'{model} is made from "{title}" by {author} ({author_url}),\nlicensed under {lic} ({lic_url}):\n{url}\n')
    lines.append(changes)
    return "\n".join(lines)


def write(path, part):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(part, f, indent=2, ensure_ascii=False)
        f.write("\n")


def slot(sid, name, fits=None, takes=None, rotation=(0.0, 0.0, 0.0)):
    s = {"id": sid, "name": name, "position": [0.0, 0.0, 0.0], "rotation": list(rotation)}
    if fits: s["fits"] = fits
    if takes: s["takes"] = takes
    return s


def main(downloads, parts):
    if not os.path.isfile(PROP_BUILDER):
        sys.exit(f"Build PropBuilder first: {PROP_BUILDER}")
    wheels = {k: v for k, v in json.load(open(os.path.join(HERE, "wheels.json"), encoding="utf-8")).items() if not k.startswith("_")}
    work = tempfile.mkdtemp(prefix="sc_bolt_ons_")
    try:
        sources = {}
        for source in sorted({w["source"] for w in wheels.values()} | {m[2] for m in MUFFLERS} | {"car_nitro_bottle", "battery"}):
            folder = os.path.join(work, "src_" + source)
            os.makedirs(folder)
            with zipfile.ZipFile(os.path.join(downloads, source + ".zip")) as z:
                z.extractall(folder)
            sources[source] = os.path.join(folder, "scene.gltf")
        blends = os.path.join(work, "blends")
        os.makedirs(blends)
        subprocess.run([blender(), "-b", "--python", os.path.join(HERE, "cut_wheels.py"), "--", json.dumps(sources), blends,
                        json.dumps(wheels)], check=True, stdout=subprocess.DEVNULL)
        pictures = {}
        for colour, rgb in BATTERIES.items():
            pictures["battery_" + colour] = os.path.join(work, f"battery_{colour}.png")
            recolour(os.path.join(work, "src_battery", "textures", "Material_baseColor.png"), pictures["battery_" + colour], rgb)
        subprocess.run([blender(), "-b", "--python", os.path.join(HERE, "cut_bolt_ons.py"), "--", json.dumps(sources), blends,
                        json.dumps(pictures)], check=True, stdout=subprocess.DEVNULL)

        def build(blend, name, target, size=512):
            prepared = os.path.join(work, "prepared_" + os.path.basename(target) + "_" + name)
            subprocess.run([sys.executable, PREPARE, blend, name, prepared, str(size), "--jpg"], check=True, stdout=subprocess.DEVNULL)
            os.makedirs(target, exist_ok=True)
            subprocess.run(["dotnet", PROP_BUILDER, prepared, name, target, "--scale", "1", "--keep-origin"], check=True, stdout=subprocess.DEVNULL)

        rims_dir, tyres_dir = os.path.join(parts, "rims", "street"), os.path.join(parts, "tyres", "street")
        for d in (rims_dir, tyres_dir):
            shutil.rmtree(d, ignore_errors=True)
        rim_credits, tyre_credits = [], []
        for stem, display, cut, widths, mass in RIMS:
            build(os.path.join(blends, cut + "_rim.blend"), stem, rims_dir)
            source = wheels[cut]["source"]
            rim_credits.append((stem + ".kn5", source))
            for w in widths:
                write(os.path.join(rims_dir, f"{stem}_15x{w}.json"), {
                    "id": f"rims/street/{stem}_15x{w}", "display_name": f"{display} 15x{w}", "credit": credit_line(source),
                    "class": "rim", "model": stem + ".kn5", "mass": mass + 0.4 * (w - 6),
                    "specs": {"offset": -0.025, "wheel_radius": 0.1905, "rim_width": float(w), "rim_type": 1},
                    "slots": [slot(1, "chassis"), slot(2, "tyre", takes=[TYRE_FIT])]})
        for stem, display, cut, grip, rolling, widths in TYRES:
            build(os.path.join(blends, cut + "_tyre.blend"), stem, tyres_dir)
            source = wheels[cut]["source"]
            tyre_credits.append((stem + ".kn5", source))
            for w in widths:
                section, radius = SIZES[w]
                write(os.path.join(tyres_dir, f"{stem}_15x{w}.json"), {
                    "id": f"tyres/street/{stem}_15x{w}", "display_name": f"{display} 15x{w}", "credit": credit_line(source),
                    "class": "tyre", "model": stem + ".kn5", "mass": 0.127,
                    "specs": {"friction": grip, "radius": radius, "rolling_resistance": rolling, "load_capacity": load_capacity(section),
                              "wheel_radius": 190.5, "tyre_width": float(section), "optimal_inflation": 2.0,
                              "max_rim_width": w + 0.5, "min_rim_width": w - 0.5},
                    "slots": [slot(1, "rim", fits=[TYRE_FIT], rotation=(3.142, 0.0, 0.0))]})
        for d, used in ((rims_dir, rim_credits), (tyres_dir, tyre_credits)):
            with open(os.path.join(d, "CREDITS.txt"), "w", encoding="utf-8", newline="\n") as fh:
                fh.write(credits_file(used))

        mufflers_dir, nitrous_dir = os.path.join(parts, "exhaust", "mufflers"), os.path.join(parts, "engines", "nitrous")
        for d in (mufflers_dir, nitrous_dir):
            shutil.rmtree(d, ignore_errors=True)
        for stem, display, source, mass in MUFFLERS:
            build(os.path.join(blends, stem + ".blend"), stem, mufflers_dir)
            write(os.path.join(mufflers_dir, stem + ".json"), {
                "id": "exhaust/mufflers/" + stem, "display_name": display, "credit": credit_line(source),
                "class": "exhaust_tip", "model": stem + ".kn5", "mass": mass,
                "slots": [slot(1, "mount", fits=["exhaust:tip"])]})
        for stem, display, mass in BOTTLES:
            build(os.path.join(blends, stem + ".blend"), stem, nitrous_dir, 1024)
            write(os.path.join(nitrous_dir, stem + ".json"), {
                "id": "engines/nitrous/" + stem, "display_name": display, "credit": credit_line("car_nitro_bottle"),
                "class": "nitrous_bottle", "model": stem + ".kn5", "mass": mass,
                "slots": [slot(1, "mount")]})
        with open(os.path.join(mufflers_dir, "CREDITS.txt"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(credits_file([(stem + ".kn5", source) for stem, _, source, _ in MUFFLERS], CHANGES))
        with open(os.path.join(nitrous_dir, "CREDITS.txt"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(credits_file([(stem + ".kn5", "car_nitro_bottle") for stem, _, _ in BOTTLES], CHANGES))

        # The stock batteries: the base game's files (the engines' builds name them), given their model and credit
        stock = os.path.join(parts, "engines", "stock")
        for colour in BATTERIES:
            stem = "12_volt_battery_" + colour
            build(os.path.join(blends, stem + ".blend"), stem, stock)
            # written into the file's text after its name, the rest of it as it is
            path = os.path.join(stock, stem + ".json")
            with open(path, encoding="utf-8", newline="") as fh:
                text = fh.read()
            eol = "\r\n" if "\r\n" in text else "\n"
            lines = [line for line in text.split(eol) if not line.lstrip().startswith(('"credit":', '"model":'))]
            at = next(i for i, line in enumerate(lines) if line.lstrip().startswith('"display_name":')) + 1
            lines[at:at] = ['  "credit": ' + json.dumps(credit_line("battery"), ensure_ascii=False) + ",", f'  "model": "{stem}.kn5",']
            with open(path, "w", encoding="utf-8", newline="") as fh:
                fh.write(eol.join(lines))
        with open(os.path.join(stock, "CREDITS.txt"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(credits_file([("12_volt_battery_" + c + ".kn5", "battery") for c in BATTERIES], BATTERY_CHANGES))
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    args = without_blender_option(sys.argv)
    if len(args) != 3:
        sys.exit(__doc__)
    main(args[1], args[2])
