"""The intro's light-up pictures, made from the lit garage (backgrounds/init.png):
  intro/lights.png    the garage with every tube on and the car's headlights off
  intro/light_1..3    which part of it each row of tubes lights, far row first (white, the share in the alpha, small)
The intro lays them over black: a faint glimpse of lights.png, each row's share flickering on, all of lights.png, then
init.png itself (the headlights). Run once after init.png changes; the pictures are committed.
The tube boxes and headlight spots below are this picture's, found by hand (a brightness threshold finds the tubes).
Usage: python tools/IntroArt/make_intro_art.py "Street Corsa/Assets/Images/Backgrounds/init.png" "Street Corsa/Assets/Images/Intro"
"""
import sys, pathlib
import numpy as np
from PIL import Image

src, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
lit = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)
H, W = lit.shape[:2]
sx, sy = W / 1920, H / 1080  # the spots below were found on a 1920x1080 picture

# The tubes (x, y, w, h), in rows by depth, far first
ROWS = [
    [(1030, 292, 198, 39), (539, 316, 209, 32), (816, 350, 166, 26)],
    [(1355, 204, 215, 61), (700, 238, 260, 50), (179, 273, 263, 39)],
    [(411, 0, 447, 97), (983, 112, 305, 84), (261, 170, 331, 63)],
]
# The headlights' lenses (centre x, y, radius) and where they light the floor
LAMPS = [(1068, 669, 21), (1105, 668, 21), (1309, 661, 20), (1333, 660, 17)]
FLOOR = [(1100, 1012, 40), (1302, 965, 40)]

yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)


def blob(cx, cy, sigx, sigy):
    return np.exp(-(((xx - cx * sx) / (sigx * sx)) ** 2 + ((yy - cy * sy) / (sigy * sy)) ** 2) / 2)


# Headlights off: the lenses dulled, their glow on the grille, bumper and floor taken back
glow = np.zeros((H, W), np.float32)
for cx, cy, r in LAMPS:
    glow = np.maximum(glow, 0.72 * blob(cx, cy, r * 0.75, r * 0.75))
    glow = np.maximum(glow, 0.30 * blob(cx, cy + 10, r * 3, r * 2.2))
for cx, cy, r in FLOOR:
    glow = np.maximum(glow, 0.45 * blob(cx, cy, r * 1.4, r))
off = lit * (1 - glow[..., None])
# A dead lens is grey glass, not black: lift what was taken back a little toward a cool grey
off += glow[..., None] * np.array([38, 40, 44], np.float32) * 0.5
Image.fromarray(np.clip(off, 0, 255).astype(np.uint8)).save(out / "lights.png", optimize=True)

# Each row's share of the light: tubes near a spot light it most; light falls down from the ceiling, so the pools sit
# below the tubes and spread wide
weights = []
for row in ROWS:
    wgt = np.zeros((H, W), np.float32)
    for x, y, w, h in row:
        cx, cy = x + w / 2, y + h / 2
        wgt += blob(cx, cy + 200, w * 0.8 + 140, 330)
    weights.append(wgt)
lum = lit.mean(axis=2)


def tube_shape(row):
    """The row's tubes and their halo, from what glows in their boxes, soft edged"""
    from PIL import ImageFilter
    shape = np.zeros((H, W), np.float32)
    for x, y, w, h in row:
        pad = 30
        y0, y1 = int(max(0, y - pad) * sy), int((y + h + pad) * sy)
        x0, x1 = int(max(0, x - pad) * sx), int((x + w + pad) * sx)
        shape[y0:y1, x0:x1] = np.clip((lum[y0:y1, x0:x1] - 70) / 90, 0, 1)
    soft = Image.fromarray((shape * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(6 * sx))
    return np.asarray(soft).astype(np.float32) / 255


total = sum(weights) + 1e-6
for i, (row, wgt) in enumerate(zip(ROWS, weights), 1):
    share = np.clip(wgt / total * 1.5 - 0.1, 0, 1)
    share = np.maximum(share, tube_shape(row))  # the row's own tubes light at once, whole
    for other in ROWS:  # and the other rows' tubes stay dark in its share, the ceiling round them lit
        if other is not row:
            share = np.minimum(share, 1 - tube_shape(other))
    alpha = Image.fromarray((share * 255).astype(np.uint8)).resize((W // 4, H // 4), Image.LANCZOS)
    mask = Image.merge("RGBA", (*Image.new("RGB", alpha.size, "white").split(), alpha))
    mask.save(out / f"light_{i}.png", optimize=True)
print("written to", out)
