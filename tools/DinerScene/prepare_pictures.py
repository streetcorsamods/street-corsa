"""
The pictures compose_street.py dresses Main Street in that the kit has not, into one folder:

  sign_roof.jpg    the board along the diner's roof: DINER in red on cream, a row of bulbs round it (4:1); and the other
                   diners' boards on the same street (step 37, phase 4): sign_roof_hiway.jpg (HI-WAY GRILL), sign_roof_surfside.jpg
                   (SURFSIDE), sign_roof_starlite.jpg (STARLITE)
  billboard.jpg    the billboard's ad across the cross street: a made-up motor oil of the time, a can and a sunset (14:5);
                   billboard_hiway.jpg (tyres), billboard_surfside.jpg (surf wax) and billboard_starlite.jpg (the lounge) for the other diners
  chainlink.png    a chain-link fence, see-through (the Hi-Way Grill's lot), 2 m square
  pennants.png     a string of pennants, see-through (Surfside's lot), 4 m by 1
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


# The roof boards: the file, the words, the board's colour, the letters', their shadow, the frame
ROOF_SIGNS = {
    "sign_roof": ("DINER", CREAM, RED, (120, 20, 24), TEAL),
    "sign_roof_hiway": ("HI-WAY GRILL", (62, 40, 26), CREAM, (28, 16, 8), (150, 28, 30)),
    "sign_roof_surfside": ("SURFSIDE", (250, 250, 244), TEAL, (18, 66, 66), (255, 140, 60)),
    "sign_roof_starlite": ("STARLITE", (20, 26, 78), (255, 120, 205), (120, 30, 95), (255, 214, 90)),
}


def roof(out):
    for name, (text, board, colour, shadow, frame) in ROOF_SIGNS.items():
        width, height = 2048, 512
        image = Image.new("RGB", (width, height), board)
        draw = ImageDraw.Draw(image)
        draw.rectangle([0, 0, width - 1, height - 1], outline=frame, width=46)
        bulbs(draw, width, height, 23, 64)
        letters(draw, text, 120, 70, width - 240, height - 140, colour, shadow)
        weathered(image, 1).save(os.path.join(out, name + ".jpg"), quality=90)


def billboard_hiway(out):
    """A tyre maker's: a big tyre on dark grey, a yellow band"""
    width, height = 2048, 732
    image = Image.new("RGB", (width, height), (48, 46, 44))
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, 560, width, height], fill=(240, 190, 40))
    cx, cy = 1620, 330
    for r, colour in ((300, (20, 20, 20)), (250, (60, 60, 60)), (175, (20, 20, 20)), (120, (170, 170, 175)), (60, (90, 90, 95))):
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=colour)
    for k in range(0, 360, 12):
        a = k * 3.14159 / 180
        draw.line([(cx + 255 * np.cos(a), cy + 255 * np.sin(a)), (cx + 300 * np.cos(a), cy + 300 * np.sin(a))], fill=(70, 70, 70), width=14)
    letters(draw, "ROADKING TIRES", 70, 60, 1200, 240, CREAM, (10, 10, 10))
    letters(draw, "Built for the long haul", 70, 320, 1100, 150, (240, 190, 40), (10, 10, 10))
    letters(draw, "Truck and car - fitted while you eat", 70, 580, 1500, 120, (40, 30, 10), (240, 210, 120))
    weathered(image, 4).save(os.path.join(out, "billboard_hiway.jpg"), quality=90)


def billboard_surfside(out):
    """A surf wax: a pale sky, the sun, waves, teal letters"""
    width, height = 2048, 732
    image = Image.new("RGB", (width, height), CREAM)
    draw = ImageDraw.Draw(image)
    for y in range(height):
        t = y / height
        draw.line([(0, y), (width, y)], fill=(int(150 + 90 * t), int(205 + 40 * t), int(245 - 10 * t)))
    draw.ellipse([1560, 90, 1880, 410], fill=(255, 230, 120))
    draw.rectangle([0, 470, width, height], fill=(40, 140, 150))
    for k in range(0, width, 160):
        draw.ellipse([k - 60, 430, k + 100, 520], fill=(220, 240, 245))
    letters(draw, "PACIFIC SURF WAX", 70, 50, 1380, 250, TEAL, (255, 255, 255))
    letters(draw, "Cooler. Faster. Stickier.", 70, 300, 1100, 140, (255, 120, 60), (255, 255, 255))
    letters(draw, "Sold at the Surfside counter", 70, 560, 1400, 120, CREAM, (20, 80, 90))
    weathered(image, 5).save(os.path.join(out, "billboard_surfside.jpg"), quality=90)


def billboard_starlite(out):
    """The lounge's: stars on navy, pink letters"""
    width, height = 2048, 732
    image = Image.new("RGB", (width, height), (20, 26, 78))
    draw = ImageDraw.Draw(image)
    rng = np.random.default_rng(7)
    for _ in range(220):
        x, y, r = rng.integers(0, width), rng.integers(0, height), rng.integers(2, 7)
        draw.ellipse([x - r, y - r, x + r, y + r], fill=(255, 245, 200))
    draw.ellipse([1620, 110, 1900, 390], fill=(255, 230, 140))
    draw.ellipse([1560, 80, 1830, 350], fill=(20, 26, 78))
    letters(draw, "THE STARLITE LOUNGE", 70, 60, 1450, 250, (255, 120, 205), (120, 30, 95))
    letters(draw, "Dancing nightly", 70, 320, 900, 150, (255, 214, 90), (120, 30, 95))
    letters(draw, "Where the big iron parks", 70, 540, 1300, 130, CREAM, (10, 10, 40))
    weathered(image, 6).save(os.path.join(out, "billboard_starlite.jpg"), quality=90)


def chainlink(out):
    """A chain-link fence, 2 m square: a lattice of diamonds, the rest see-through"""
    size = 1024
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    step = 64
    for k in range(-size, 2 * size, step):
        draw.line([(k, 0), (k + size, size)], fill=(150, 150, 155, 255), width=5)
        draw.line([(k, size), (k + size, 0)], fill=(150, 150, 155, 255), width=5)
    image.save(os.path.join(out, "chainlink.png"))


def pennants(out):
    """A string of pennants, 4 m by 1: the rope along the top, a flag hanging every half metre"""
    width, height = 2048, 512
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.line([(0, 24), (width, 24)], fill=(90, 70, 50, 255), width=8)
    colours = [(230, 50, 50), (250, 220, 60), (40, 150, 200), (250, 250, 240), (60, 170, 90), (250, 120, 40)]
    for k, x in enumerate(range(0, width, 256)):
        draw.polygon([(x + 20, 26), (x + 236, 26), (x + 128, 420)], fill=(*colours[k % len(colours)], 255))
    image.save(os.path.join(out, "pennants.png"))


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
    billboard_hiway(out)
    billboard_surfside(out)
    billboard_starlite(out)
    chainlink(out)
    pennants(out)
    siding(out)
    poly_haven_pictures(out)
    print(f"pictures in {out}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
