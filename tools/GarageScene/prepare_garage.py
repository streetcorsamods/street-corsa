"""
Gathers the pictures the player's garage is built from, into one folder, for GarageSceneBuilder:

  surfaces    Poly Haven CC0 textures (the floor, the block walls and their painted dado, the roof boards and the
              timbers, the side door, the driveway), downloaded at 1k
  door        the sectional garage door, white-painted panels, drawn here
  windows     wired glass in a steel frame, lit from outside (the scene's glow sets how bright at each hour)
  posters     three old advertising signs for makes of our own (no real brand), and a calendar
  solids      flat colours: the fluorescent fittings, their tubes, the window frames, the door's tracks, the dark
              outside

  python prepare_garage.py <out folder>

Needs numpy, opencv-python and Pillow. Downloads about 10 MB.
"""
import os
import sys

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Shared"))
from scene_tools import poly_haven_surface  # noqa: E402

Image.MAX_IMAGE_PIXELS = None

# Poly Haven ids, by the surface they are used for. All CC0 (https://polyhaven.com/license).
SURFACES = {
    "floor": "garage_floor",
    "wall": "concrete_block_wall_03",
    "dado": "painted_concrete",
    "roof": "weathered_brown_planks",
    "timber": "weathered_planks",
    "sidedoor": "rough_pine_door",
    "drive": "concrete_pavement",
}

# Surfaces painted over (paint_over): BGR, and how much of the picture's shading shows through
PAINT = {
    "wall": ((178, 202, 214), 0.55),
    "dado": ((72, 104, 58), 0.3),
}

SIZE = 1024
QUALITY = 85
FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")


def save(path, image):
    cv2.imwrite(path, image, [cv2.IMWRITE_JPEG_QUALITY, QUALITY])


def save_rgb(out, name, image):
    pixels = np.asarray(image.convert("RGB"), dtype=np.uint8)
    save(os.path.join(out, name), cv2.cvtColor(pixels, cv2.COLOR_RGB2BGR))


def font(size, names=("impact.ttf", "arialbd.ttf")):
    for name in names:
        path = os.path.join(FONTS, name)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def grime(image, seed, amount=0.18, blur=30):
    """Dirt in soft patches and a fine grain, so nothing looks new"""
    rng = np.random.default_rng(seed)
    pixels = np.asarray(image, dtype=np.float32)
    height, width = pixels.shape[:2]
    patches = cv2.GaussianBlur(rng.random((height, width)).astype(np.float32), (0, 0), blur)
    patches = (patches - patches.min()) / max(1e-6, patches.max() - patches.min())
    pixels *= (1 - amount + amount * patches)[..., None]
    pixels += rng.normal(0, 5, (height, width, 1))
    return Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8))


def paint_over(out, name, bgr, depth):
    """
    A surface painted: its picture's shading kept (the block's joints, the chips), its colour the paint's. depth is how
    much of the shading shows through, 0 none
    """
    path = os.path.join(out, name)
    image = cv2.imread(path).astype(np.float32)
    shade = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    shade /= max(1.0, float(shade.mean()))
    painted = np.array(bgr, np.float32) * (1 - depth + depth * shade)[..., None]
    save(path, np.clip(painted, 0, 255).astype(np.uint8))


def door(out):
    """
    A sectional door seen from inside: four sections high, each of four raised panels, white paint gone grey, a
    rubber strip along the bottom. The picture is the whole door, 2:1 wide to its height (2.8 m by 2.3 m: stretched a
    little, nobody counts the panels)
    """
    width, height = 2048, 1024
    image = Image.new("RGB", (width, height), (196, 194, 184))
    draw = ImageDraw.Draw(image)
    sections, panels = 4, 4
    section_h = height / sections
    for s in range(sections):
        top = s * section_h
        draw.line([(0, top), (width, top)], fill=(120, 118, 110), width=6)
        for p in range(panels):
            left = p * width / panels
            box = [left + 40, top + 34, left + width / panels - 40, top + section_h - 34]
            draw.rectangle(box, outline=(150, 148, 140), width=8)
            draw.line([(box[0] + 8, box[3] - 4), (box[2] - 4, box[3] - 4)], fill=(222, 220, 212), width=4)
    draw.rectangle([0, height - 24, width, height], fill=(30, 30, 30))
    save_rgb(out, "door_sections.jpg", grime(image, 5))


def window(out):
    """Six panes of wired glass in a steel frame, bright from the day outside; the glow is the scene's to set"""
    size = 512
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    image = np.zeros((size, size, 3), np.float32)
    image[:] = (236, 232, 222)
    image *= (1.0 - 0.18 * y)[..., None]
    wire = (((x * 30) % 1 < 0.05) | ((y * 30) % 1 < 0.05))
    image[wire] *= 0.86
    bars = ((x * 3) % 1 < 0.035) | ((y * 2) % 1 < 0.035) | (x < 0.03) | (x > 0.97) | (y < 0.03) | (y > 0.97)
    image[bars] = (34, 38, 40)
    save(os.path.join(out, "window.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def poster(out, name, size, background, draw_it, seed):
    image = Image.new("RGB", size, background)
    draw_it(ImageDraw.Draw(image), size)
    save_rgb(out, name, grime(image, seed, 0.25, 40))


def centred(draw, width, y, text, face, fill):
    box = draw.textbbox((0, 0), text, font=face)
    draw.text(((width - (box[2] - box[0])) / 2 - box[0], y), text, font=face, fill=fill)


def posters(out):
    """Signs of the time for makes of our own, nailed up round the garage"""

    def oil(draw, size):
        w, h = size
        draw.rectangle([16, 16, w - 17, h - 17], outline=(250, 236, 200), width=12)
        centred(draw, w, 70, "THUNDERBOLT", font(120), (250, 236, 200))
        centred(draw, w, 200, "MOTOR OIL", font(96), (250, 200, 40))
        draw.polygon([(w / 2 - 60, 340), (w / 2 + 40, 340), (w / 2 - 10, 470), (w / 2 + 70, 470), (w / 2 - 70, 680),
                      (w / 2 - 20, 520), (w / 2 - 90, 520)], fill=(250, 200, 40))
        centred(draw, w, 720, "KEEPS 'EM RUNNING", font(64, ("arialbd.ttf",)), (250, 236, 200))
        centred(draw, w, 800, "25c a quart", font(56, ("arialbd.ttf",)), (250, 236, 200))

    def plugs(draw, size):
        w, h = size
        draw.rectangle([0, 0, w, h * 0.35], fill=(24, 60, 140))
        centred(draw, w, 30, "BLUE FLAME", font(150), (255, 255, 255))
        centred(draw, w, h * 0.42, "SPARK PLUGS", font(110), (24, 60, 140))
        centred(draw, w, h * 0.70, "Hotter spark - more power!", font(52, ("arialbd.ttf",)), (180, 30, 30))

    def tyres(draw, size):
        w, h = size
        draw.ellipse([w * 0.22, 60, w * 0.78, 60 + w * 0.56], outline=(20, 20, 20), width=70)
        draw.ellipse([w * 0.22 + 52, 112, w * 0.78 - 52, 8 + w * 0.56], outline=(245, 245, 240), width=22)
        centred(draw, w, h * 0.62, "SPEEDWAY", font(130), (20, 20, 20))
        centred(draw, w, h * 0.78, "WHITEWALL TIRES", font(70), (190, 30, 30))

    def calendar(draw, size):
        w, h = size
        draw.rectangle([0, 0, w, h * 0.5], fill=(70, 110, 150))
        draw.polygon([(0, h * 0.5), (w * 0.3, h * 0.22), (w * 0.55, h * 0.4), (w * 0.8, h * 0.18), (w, h * 0.5)], fill=(60, 90, 70))
        centred(draw, w, h * 0.52, "JOE'S AUTO SUPPLY", font(48, ("arialbd.ttf",)), (150, 30, 30))
        grid_top = h * 0.62
        for row in range(5):
            for col in range(7):
                x0, y0 = 30 + col * (w - 60) / 7, grid_top + row * (h * 0.36) / 5
                draw.rectangle([x0, y0, x0 + (w - 60) / 7, y0 + (h * 0.36) / 5], outline=(120, 120, 120), width=2)
                day = row * 7 + col - 2
                if 1 <= day <= 31:
                    draw.text((x0 + 8, y0 + 4), str(day), font=font(26, ("arial.ttf",)), fill=(40, 40, 40))

    poster(out, "poster_oil.jpg", (768, 1024), (150, 26, 24), oil, 21)
    poster(out, "poster_plugs.jpg", (1024, 768), (236, 226, 196), plugs, 22)
    poster(out, "poster_tyres.jpg", (768, 1024), (226, 200, 60), tyres, 23)
    poster(out, "calendar.jpg", (512, 768), (240, 236, 224), calendar, 24)


def pegboard(out):
    """Brown hardboard with a grid of holes an inch apart, as the board is 1 m across and the picture tiles (the part shop's)"""
    rng = np.random.default_rng(7)
    image = np.zeros((SIZE, SIZE, 3), np.float32)
    image[:] = (62, 92, 128)  # BGR: a warm brown
    image += rng.normal(0, 6, (SIZE, SIZE, 1))
    step = SIZE // 40
    for row in range(40):
        for col in range(40):
            cv2.circle(image, (col * step + step // 2, row * step + step // 2), 4, (22, 30, 40), -1, cv2.LINE_AA)
    save(os.path.join(out, "pegboard.jpg"), np.clip(image, 0, 255).astype(np.uint8))


def solids(out):
    for name, bgr in {"fitting": (200, 204, 206), "tube": (250, 250, 246), "frame": (40, 46, 48), "track": (70, 72, 74),
                      "stripe": (40, 140, 200), "black": (0, 0, 0)}.items():
        image = np.zeros((16, 16, 3), np.uint8)
        image[:] = bgr
        save(os.path.join(out, f"{name}.jpg"), image)


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 1

    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for surface, texture_id in SURFACES.items():
        # A painted surface is downloaded again each run, so it is never painted twice
        name = f"{surface}_{texture_id}.jpg"
        if surface in PAINT and os.path.exists(os.path.join(out, name)):
            os.remove(os.path.join(out, name))
        poly_haven_surface(out, surface, texture_id, SIZE, QUALITY)
        if surface in PAINT:
            paint_over(out, name, *PAINT[surface])
    door(out)
    window(out)
    posters(out)
    pegboard(out)
    solids(out)
    print(f"{len(os.listdir(out))} pictures in {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
