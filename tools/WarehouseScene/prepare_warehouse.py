"""
Gathers the pictures the player's warehouse is built from, into one folder, for WarehouseSceneBuilder:

  surfaces    Poly Haven CC0 textures (floors, brick, cladding, doors, steel), downloaded at 1k and re-encoded
  sign        the enamel plate on the back wall ("WAREHOUSE") and a plate for each digit, so the number of the
              warehouse can be put up at run time by pointing the two digit plates at other pictures
  paint       the yellow of the bay lines, worn
  skylight    the frosted panels in the roof
  solids      flat colours: the lamp shades, the bulbs, the cables

Every surface texture goes into the one KN5; a warehouse's look (warehouse.json, "looks") says which of them each
surface wears, and the game switches between them without loading the scene again.

  python prepare_warehouse.py <out folder>

Needs numpy and opencv-python. Downloads about 15 MB.
"""
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
from scene_tools import poly_haven_surface  # noqa: E402

# Poly Haven ids, by the surface they are used for. All CC0 (https://polyhaven.com/license).
SURFACES = {
    "floor": ["concrete_floor_worn_001", "hangar_concrete_floor", "garage_floor", "smooth_concrete_floor"],
    "wall": ["red_brick_03", "factory_brick", "whitewashed_brick", "concrete_block_wall", "yellow_brick"],
    "upper": ["corrugated_iron_02", "rusty_corrugated_iron", "box_profile_metal_sheet", "weathered_plank_siding"],
    "door": ["painted_metal_shutter", "rusty_metal_shutter", "wooden_garage_door"],
    "steel": ["blue_metal_plate", "rusty_painted_metal", "green_metal_rust", "metal_plate"],
}

SIZE = 1024
QUALITY = 85

# The sign: cream letters on green enamel
PLATE = (38, 70, 30)      # BGR
LETTERS = (200, 226, 236)


def save(path, image):
    cv2.imwrite(path, image, [cv2.IMWRITE_JPEG_QUALITY, QUALITY])


def enamel(width, height, seed):
    """A painted steel plate: the green, a little uneven, with a cream border"""
    rng = np.random.default_rng(seed)
    plate = np.zeros((height, width, 3), np.float32)
    plate[:] = PLATE
    noise = cv2.GaussianBlur(rng.normal(0, 1, (height, width)).astype(np.float32), (0, 0), 6)
    plate += noise[..., None] * 5
    border = max(6, height // 28)
    cv2.rectangle(plate, (border, border), (width - border - 1, height - border - 1), LETTERS, border // 2)
    return plate


def letters(plate, text, scale, thickness):
    height, width = plate.shape[:2]
    font = cv2.FONT_HERSHEY_DUPLEX
    (w, h), _ = cv2.getTextSize(text, font, scale, thickness)
    cv2.putText(plate, text, ((width - w) // 2, (height + h) // 2), font, scale, LETTERS, thickness, cv2.LINE_AA)


def weather(plate, seed):
    """Chips and grime, so the plate has hung there a while"""
    rng = np.random.default_rng(seed)
    height, width = plate.shape[:2]
    grime = cv2.GaussianBlur(rng.random((height, width)).astype(np.float32), (0, 0), 30)
    plate *= (0.82 + 0.18 * grime)[..., None]
    for _ in range(width * height // 20000):
        x, y = int(rng.integers(0, width)), int(rng.integers(0, height))
        cv2.circle(plate, (x, y), int(rng.integers(1, 4)), (60, 60, 64), -1)
    return np.clip(plate, 0, 255).astype(np.uint8)


def sign(out):
    plate = enamel(1024, 256, 1)
    letters(plate, "WAREHOUSE", 4.2, 9)
    save(os.path.join(out, "sign_warehouse.jpg"), weather(plate, 2))

    for digit in range(10):
        plate = enamel(256, 256, 10 + digit)
        letters(plate, str(digit), 6.0, 14)
        save(os.path.join(out, f"sign_digit_{digit}.jpg"), weather(plate, 20 + digit))


def paint(out):
    """Traffic yellow, scuffed by tyres"""
    rng = np.random.default_rng(3)
    size = 256
    image = np.zeros((size, size, 3), np.float32)
    image[:] = (40, 190, 230)
    wear = cv2.GaussianBlur(rng.random((size, size)).astype(np.float32), (0, 0), 4)
    image *= (0.7 + 0.3 * wear)[..., None]
    scuffs = cv2.GaussianBlur(rng.random((size, size)).astype(np.float32), (0, 0), 2) > 0.62
    image[scuffs] = image[scuffs] * 0.55 + np.array([70, 75, 80]) * 0.45
    save(os.path.join(out, "paint_yellow.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def skylight(out):
    """Wired glass in a frame: bright in the middle of each pane, the glazing bars dark"""
    size = 512
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    glow = 1.0 - 0.25 * ((x - 0.5) ** 2 + (y - 0.5) ** 2) * 4
    image = np.zeros((size, size, 3), np.float32)
    image[:] = (250, 244, 232)
    image *= glow[..., None]
    wire = ((np.floor(x * 24) + np.floor(y * 24)) % 2 == 0) & (((x * 24) % 1 < 0.04) | ((y * 24) % 1 < 0.04))
    image[wire] *= 0.8
    bars = ((x * 4) % 1 < 0.03) | ((y * 2) % 1 < 0.03) | (x < 0.02) | (x > 0.98) | (y < 0.02) | (y > 0.98)
    image[bars] = (40, 42, 45)
    save(os.path.join(out, "skylight.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def solids(out):
    for name, bgr in {"shade": (50, 72, 44), "bulb": (200, 235, 255), "cable": (18, 18, 20)}.items():
        image = np.zeros((16, 16, 3), np.uint8)
        image[:] = bgr
        save(os.path.join(out, f"{name}.jpg"), image)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for surface, ids in SURFACES.items():
        for texture_id in ids:
            poly_haven_surface(out, surface, texture_id, SIZE, QUALITY)
    sign(out)
    paint(out)
    skylight(out)
    solids(out)
    print(f"{len(os.listdir(out))} pictures in {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
