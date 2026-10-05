"""
Gathers the pictures the part shop's inside is built from, into one folder, for ShopSceneBuilder:

  surfaces    Poly Haven CC0 textures (floors, walls, wood, steel, ceilings), downloaded at 1k and re-encoded
  signs       the board over the shelves, one for each kind of shop ("SPEED EQUIPMENT", "AUTO PARTS", ...)
  pegboard    the brown hardboard with its holes, over the tyre rack
  checker     the speed shop's black and white floor tiles
  solids      flat colours: the tubes' light, the window's glow, the till, the rubber of the skirting

Every surface texture goes into the one KN5; a shop's look (shop.json, "looks") says which of them each surface
wears, and the game switches between them without loading the scene again.

  python prepare_shop.py <out folder>

Needs numpy, opencv-python and Pillow. Downloads about 20 MB.
"""
import os
import sys

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
from scene_tools import poly_haven_surface  # noqa: E402

# Poly Haven ids, by the surface they are used for. All CC0 (https://polyhaven.com/license).
SURFACES = {
    "floor": ["old_linoleum_flooring_01", "linoleum_brown", "garage_floor", "concrete_floor_worn_001"],
    "wall": ["painted_brick", "plastered_wall_04", "concrete_block_wall", "raw_plank_wall"],
    "wood": ["dark_wood", "kitchen_wood", "plywood", "old_planks_02"],
    "steel": ["blue_metal_plate", "metal_plate", "painted_metal_shutter", "green_metal_rust"],
    "ceiling": ["grey_plaster", "beige_wall_001", "concrete_wall_004", "roof_planks"],
}

SIZE = 1024
QUALITY = 85

FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")

# The signs: what each kind of shop calls itself over its shelves, in the colours of its board (RGB)
SIGNS = {
    "speed": ("SPEED EQUIPMENT", (196, 32, 28), (250, 236, 200)),
    "specialist": ("AUTO PARTS", (24, 58, 112), (246, 238, 214)),
    "wheels": ("TIRES  WHEELS  BRAKES", (232, 182, 30), (30, 26, 24)),
    "junkyard": ("USED PARTS", (60, 72, 46), (232, 220, 190)),
}


def save(path, image):
    cv2.imwrite(path, image, [cv2.IMWRITE_JPEG_QUALITY, QUALITY])


def font(size):
    for name in ("impact.ttf", "arialbd.ttf"):
        path = os.path.join(FONTS, name)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def sign(out, kind, text, board, letters):
    """A painted board, 4:1, the shop's word across it in big letters with a thin border, worn a little"""
    width, height = 2048, 512
    rng = np.random.default_rng(len(text))
    image = Image.new("RGB", (width, height), board)
    draw = ImageDraw.Draw(image)
    draw.rectangle([18, 18, width - 19, height - 19], outline=letters, width=10)
    size = 300
    while size > 60:
        face = font(size)
        box = draw.textbbox((0, 0), text, font=face)
        if box[2] - box[0] <= width - 160:
            break
        size -= 10
    box = draw.textbbox((0, 0), text, font=face)
    x = (width - (box[2] - box[0])) / 2 - box[0]
    y = (height - (box[3] - box[1])) / 2 - box[1]
    draw.text((x + 6, y + 6), text, font=face, fill=tuple(int(c * 0.45) for c in board))
    draw.text((x, y), text, font=face, fill=letters)

    pixels = np.asarray(image, dtype=np.float32)
    grain = rng.normal(0, 7, (height, width, 1)).astype(np.float32)
    pixels = np.clip(pixels + grain, 0, 255).astype(np.uint8)
    save(os.path.join(out, f"sign_{kind}.jpg"), cv2.cvtColor(pixels, cv2.COLOR_RGB2BGR))


def pegboard(out):
    """Brown hardboard with a grid of holes an inch apart, as the board is 1 m across and the picture tiles"""
    rng = np.random.default_rng(7)
    image = np.zeros((SIZE, SIZE, 3), np.float32)
    image[:] = (62, 92, 128)  # BGR: a warm brown
    image += rng.normal(0, 6, (SIZE, SIZE, 1))
    step = SIZE // 40
    for row in range(40):
        for col in range(40):
            cv2.circle(image, (col * step + step // 2, row * step + step // 2), 4, (22, 30, 40), -1, cv2.LINE_AA)
    save(os.path.join(out, "pegboard.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def checker(out):
    """Black and white floor tiles, about a foot square (the picture is 2 m across), scuffed and yellowed"""
    rng = np.random.default_rng(11)
    tiles = 14
    step = SIZE / tiles
    image = np.zeros((SIZE, SIZE, 3), np.float32)
    for row in range(tiles):
        for col in range(tiles):
            light = (row + col) % 2 == 0
            base = np.array((200, 214, 222) if light else (34, 34, 36), np.float32) + rng.normal(0, 6)
            y0, y1 = int(row * step), int((row + 1) * step)
            x0, x1 = int(col * step), int((col + 1) * step)
            image[y0:y1, x0:x1] = base
    image += rng.normal(0, 5, (SIZE, SIZE, 1))
    # Grout lines, and scuffs where feet go
    for k in range(tiles + 1):
        p = int(k * step)
        cv2.line(image, (p, 0), (p, SIZE), (90, 96, 100), 2)
        cv2.line(image, (0, p), (SIZE, p), (90, 96, 100), 2)
    scuffs = cv2.GaussianBlur(rng.normal(0, 1, (SIZE, SIZE)).astype(np.float32), (0, 0), 25)
    image *= (1 - 0.12 * np.clip(scuffs * 4, 0, 1))[..., None]
    save(os.path.join(out, "floor_checker.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def solid(out, name, bgr, noise=0):
    rng = np.random.default_rng(len(name))
    image = np.zeros((64, 64, 3), np.float32)
    image[:] = bgr
    if noise:
        image += rng.normal(0, noise, (64, 64, 1))
    save(os.path.join(out, f"{name}.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(1)
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    for surface, ids in SURFACES.items():
        for texture_id in ids:
            print(f"{surface}: {texture_id}")
            poly_haven_surface(out, surface, texture_id, SIZE, QUALITY)

    for kind, (text, board, letters) in SIGNS.items():
        sign(out, kind, text, board, letters)

    pegboard(out)
    checker(out)
    solid(out, "tube", (250, 252, 255))
    solid(out, "window", (235, 225, 205))
    solid(out, "till", (40, 44, 46), noise=4)
    solid(out, "rubber", (24, 24, 24), noise=3)
    solid(out, "chrome", (200, 200, 205), noise=6)
    print(f"done: {out}")


if __name__ == "__main__":
    main()
