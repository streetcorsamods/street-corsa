"""
The pictures compose_street.py dresses Main Street in that the kit has not, into one folder:

  sign_roof.jpg    the board along the diner's roof: DINER in red on cream, a row of bulbs round it (4:1)
  billboard.jpg    the billboard's ad across the cross street: a made-up motor oil of the time, a can and a sunset (14:5)
  siding_<n>.jpg   the houses' clapboard, painted: white, cream, pale green, pale blue (a picture is 2 m square)
  roof.jpg         the houses' roofs: Poly Haven's even slates (CC0), darkened to asphalt shingles' grey
  lawn.jpg         the houses' lawns: Poly Haven's leafy grass (CC0), turned a mown lawn's green
  hedge.jpg        their hedges: the lawn, darker and denser

  python prepare_pictures.py <out folder>

Needs numpy and Pillow; the letters are Windows' Impact (Arial Black failing that). Downloads about 2 MB.
"""
import io
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
from scene_tools import poly_haven  # noqa: E402

FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")

CREAM = (244, 233, 205)
RED = (196, 30, 36)
TEAL = (38, 128, 128)
BULB = (255, 236, 160)


def font(size):
    for name in ("impact.ttf", "ariblk.ttf", "arialbd.ttf"):
        path = os.path.join(FONTS, name)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def fitted(draw, text, width, height):
    """The biggest letters the text fits in at, and where it goes to stand in the middle of the box"""
    size = height
    while size > 20:
        face = font(size)
        box = draw.textbbox((0, 0), text, font=face)
        if box[2] - box[0] <= width and box[3] - box[1] <= height:
            return face, box
        size -= 6
    face = font(size)
    return face, draw.textbbox((0, 0), text, font=face)


def letters(draw, text, left, top, width, height, colour, shadow):
    face, box = fitted(draw, text, width, height)
    x = left + (width - (box[2] - box[0])) / 2 - box[0]
    y = top + (height - (box[3] - box[1])) / 2 - box[1]
    draw.text((x + 8, y + 8), text, font=face, fill=shadow)
    draw.text((x, y), text, font=face, fill=colour)


def bulbs(draw, width, height, inset, step):
    """A row of light bulbs round the board's edge"""
    for x in range(inset, width - inset + 1, step):
        for y in (inset, height - inset):
            draw.ellipse([x - 9, y - 9, x + 9, y + 9], fill=BULB, outline=(150, 120, 60), width=2)
    for y in range(inset + step, height - inset, step):
        for x in (inset, width - inset):
            draw.ellipse([x - 9, y - 9, x + 9, y + 9], fill=BULB, outline=(150, 120, 60), width=2)


def weathered(image, seed):
    """A painted board's grain and a little dirt, so it is not a print"""
    rng = np.random.default_rng(seed)
    pixels = np.asarray(image, dtype=np.float32)
    pixels += rng.normal(0, 6, pixels.shape[:2] + (1,))
    return Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))


def roof(out):
    width, height = 2048, 512
    image = Image.new("RGB", (width, height), CREAM)
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, 0, width - 1, height - 1], outline=TEAL, width=46)
    bulbs(draw, width, height, 23, 64)
    letters(draw, "DINER", 120, 70, width - 240, height - 140, RED, (120, 20, 24))
    weathered(image, 1).save(os.path.join(out, "sign_roof.jpg"), quality=90)


def billboard(out):
    """A 1960s motor oil ad, our own make: a sunset over the sea behind the headline, the can at the right"""
    width, height = 2048, 732
    image = Image.new("RGB", (width, height), CREAM)
    draw = ImageDraw.Draw(image)
    for y in range(height):
        t = y / height
        draw.line([(0, y), (width, y)], fill=(int(250 - 40 * t), int(170 - 90 * t), int(70 + 40 * t)))
    draw.ellipse([1180, 330, 1520, 670], fill=(255, 214, 120))
    draw.rectangle([0, 520, width, height], fill=(40, 70, 110))
    for y in range(540, height, 26):
        draw.line([(0, y), (width, y)], fill=(70, 105, 150), width=4)
    letters(draw, "GOLDEN STATE", 70, 50, 1250, 230, CREAM, (90, 30, 20))
    letters(draw, "MOTOR OIL", 70, 290, 1000, 170, (255, 236, 160), (90, 30, 20))
    letters(draw, "Keeps your V8 singing!", 70, 560, 1100, 110, CREAM, (20, 30, 60))
    # The can
    draw.rectangle([1640, 120, 1940, 640], fill=(196, 30, 36), outline=(60, 10, 10), width=10)
    draw.rectangle([1640, 260, 1940, 470], fill=(255, 214, 90))
    letters(draw, "GS", 1660, 280, 260, 170, RED, (120, 20, 24))
    draw.rectangle([1630, 100, 1950, 140], fill=(150, 150, 150))
    weathered(image, 3).save(os.path.join(out, "billboard.jpg"), quality=90)


# The houses' paints (RGB)
PAINTS = [(236, 234, 226), (238, 226, 188), (196, 218, 190), (190, 210, 226)]


def siding(out):
    """Lap siding: boards 20 cm high, each a shade darker under the lap of the one above, a little grain"""
    size = 1024
    rng = np.random.default_rng(5)
    board = size // 10
    shade = np.ones((size, 1), np.float32)
    for row in range(size):
        within = (row % board) / board
        shade[row] = 0.78 + 0.22 * min(1.0, within * 6) - 0.05 * within
    grain = 1 + rng.normal(0, 0.025, (size, size)).astype(np.float32)
    for n, paint in enumerate(PAINTS):
        pixels = np.asarray(paint, np.float32)[None, None, :] * (shade * grain)[..., None]
        Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8)).save(os.path.join(out, f"siding_{n}.jpg"), quality=90)


def download(texture_id):
    """A Poly Haven picture (CC0, https://polyhaven.com/license)"""
    return Image.open(io.BytesIO(poly_haven(texture_id))).convert("RGB")


def poly_haven_pictures(out):
    if not os.path.exists(os.path.join(out, "roof.jpg")):
        roof_picture = np.asarray(download("roof_slates_03"), np.float32)
        grey = roof_picture.mean(axis=2, keepdims=True)
        Image.fromarray(np.clip(grey * 0.55 + roof_picture * 0.1, 0, 255).astype(np.uint8)).save(os.path.join(out, "roof.jpg"), quality=90)
    if not os.path.exists(os.path.join(out, "lawn.jpg")):
        # Its leaves turned to a mown lawn's green: its light and shade kept, the colour pushed towards grass
        leaves = np.asarray(download("leafy_grass"), np.float32)
        light = leaves.mean(axis=2, keepdims=True) / 128
        lawn = np.clip(light * np.asarray([70, 118, 46], np.float32) * 0.8 + leaves * 0.2, 0, 255)
        Image.fromarray(lawn.astype(np.uint8)).save(os.path.join(out, "lawn.jpg"), quality=90)
        hedge = lawn * np.asarray([0.6, 0.72, 0.6], np.float32)
        Image.fromarray(np.clip(hedge, 0, 255).astype(np.uint8)).save(os.path.join(out, "hedge.jpg"), quality=90)


def main(out):
    os.makedirs(out, exist_ok=True)
    roof(out)
    billboard(out)
    siding(out)
    poly_haven_pictures(out)
    print(f"pictures in {out}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
