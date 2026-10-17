"""
Gathers the pictures the machine shop's inside is built from, into one folder, for MachineShopSceneBuilder:

  surfaces    Poly Haven CC0 textures (the floor, the walls, the ceiling, the bench tops, the racks), at 1k
  sign        the board over the crank grinder: "MACHINE SHOP"
  pegboard    the brown hardboard with its holes, over the engine stands
  machine     the machines' grey-green enamel, chipped and oily
  solids      flat colours: the tubes' light, the window's glow, the hot tank's black steel, ground steel, the grinding
              wheel's grit, the work tickets on the counter, rubber

  python prepare_machine_shop.py <out folder>

Needs numpy, opencv-python and Pillow. Downloads about 5 MB.
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
    "floor": ["concrete_floor_worn_001"],
    "wall": ["concrete_block_wall"],
    "wood": ["plywood"],
    "steel": ["metal_plate"],
    "ceiling": ["concrete_wall_004"],
}

SIZE = 1024
QUALITY = 85

FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")


def save(path, image):
    cv2.imwrite(path, image, [cv2.IMWRITE_JPEG_QUALITY, QUALITY])


def font(size):
    for name in ("impact.ttf", "arialbd.ttf"):
        path = os.path.join(FONTS, name)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def sign(out, text, board, letters):
    """A painted board, 4:1, the shop's word across it in big letters with a thin border, worn a little"""
    width, height = 2048, 512
    rng = np.random.default_rng(len(text))
    image = Image.new("RGB", (width, height), board)
    draw = ImageDraw.Draw(image)
    draw.rectangle([18, 18, width - 19, height - 19], outline=letters, width=10)
    size = 300
    face = font(size)
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
    save(os.path.join(out, "sign_machine.jpg"), cv2.cvtColor(pixels, cv2.COLOR_RGB2BGR))


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


def machine(out):
    """The machines' enamel: a grey green, mottled, chipped to bare metal here and there, darker where the oil runs"""
    rng = np.random.default_rng(21)
    image = np.zeros((SIZE, SIZE, 3), np.float32)
    image[:] = (112, 124, 104)  # BGR: a machine-tool grey green
    mottle = cv2.GaussianBlur(rng.normal(0, 1, (SIZE, SIZE)).astype(np.float32), (0, 0), 30)
    image += (mottle * 60)[..., None]
    image += rng.normal(0, 4, (SIZE, SIZE, 1))
    for _ in range(140):
        x, y = rng.integers(0, SIZE, 2)
        radius = int(rng.integers(2, 9))
        cv2.circle(image, (int(x), int(y)), radius, (150, 150, 152), -1, cv2.LINE_AA)
    oil = cv2.GaussianBlur(np.clip(rng.normal(0, 1, (SIZE, SIZE)), 0, None).astype(np.float32), (0, 0), 18)
    image *= (1 - 0.25 * np.clip(oil * 3, 0, 1))[..., None]
    save(os.path.join(out, "machine.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def tickets(out):
    """Work tickets on the counter, a pad of them, a line or two written on the top one"""
    rng = np.random.default_rng(5)
    image = np.full((256, 256, 3), (214, 232, 238), np.float32)
    image += rng.normal(0, 4, (256, 256, 1))
    for row in range(6):
        y = 50 + row * 30
        cv2.line(image, (24, y), (232, y), (180, 190, 200), 1)
        cv2.line(image, (30, y - 6), (int(80 + rng.random() * 140), y - 6), (110, 70, 40), 2)
    save(os.path.join(out, "tickets.jpg"), np.clip(image, 0, 255).astype(np.uint8))


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

    sign(out, "MACHINE SHOP", (40, 52, 64), (236, 196, 60))
    pegboard(out)
    machine(out)
    tickets(out)
    solid(out, "tube", (250, 252, 255))
    solid(out, "window", (235, 225, 205))
    solid(out, "tank", (34, 36, 38), noise=6)
    solid(out, "chrome", (200, 200, 205), noise=6)
    solid(out, "grit", (118, 120, 122), noise=18)
    solid(out, "rubber", (24, 24, 24), noise=3)
    print(f"done: {out}")


if __name__ == "__main__":
    main()
