"""
Draws the pictures the paint booth is built from, into one folder, for PaintBoothSceneBuilder. All drawn here, no
downloads: a 1960s body shop's spray booth, white steel panels flecked with years of overspray, a concrete floor with a
steel grate down its middle, the exhaust filters filling the back wall, light boxes overhead; and the painter's white
coveralls, his mask and gloves, his gun, and the mist it throws.

  python prepare_booth.py <out folder>

Needs numpy and Pillow.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
QUALITY = 88
RNG = np.random.default_rng(1962)

# Overspray: the colours the booth has seen, faint on everything
SPRAY = [(178, 34, 34), (30, 80, 160), (230, 190, 40), (40, 120, 60), (20, 20, 20), (240, 240, 240), (200, 90, 30), (110, 40, 120)]


def noise(size, scale, octaves=4):
    """Smooth value noise, 0..1, tiling"""
    out = np.zeros((size, size), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cells = max(2, int(scale * 2 ** o))
        grid = RNG.random((cells, cells)).astype(np.float32)
        layer = np.array(Image.fromarray((grid * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC), np.float32) / 255
        out += layer * amp
        total += amp
        amp *= 0.5
    return out / total


def grain(size, strength):
    return (RNG.random((size, size)).astype(np.float32) - 0.5) * strength


def save(out, name, rgb):
    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).save(os.path.join(out, name), quality=QUALITY)


def flecks(rgb, count, alpha, radius=(1, 3)):
    """Specks of overspray in the booth's colours"""
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    for _ in range(count):
        x, y = RNG.integers(0, SIZE, 2)
        r = RNG.integers(radius[0], radius[1] + 1)
        c = SPRAY[RNG.integers(0, len(SPRAY))]
        draw.ellipse((x - r, y - r, x + r, y + r), fill=(*c, int(alpha * RNG.uniform(0.4, 1.0))))
    img = Image.alpha_composite(img.convert("RGBA"), layer.filter(ImageFilter.GaussianBlur(0.6)))
    return np.array(img.convert("RGB"), np.float32)


def clouds(rgb, count, alpha):
    """Soft clouds of overspray, where a gun was turned on the wall"""
    out = rgb.copy()
    for _ in range(count):
        c = np.array(SPRAY[RNG.integers(0, len(SPRAY))], np.float32)
        x, y = RNG.integers(0, SIZE, 2)
        r = RNG.uniform(60, 220)
        yy, xx = np.mgrid[0:SIZE, 0:SIZE]
        # Tiling distance
        dx = np.minimum(np.abs(xx - x), SIZE - np.abs(xx - x))
        dy = np.minimum(np.abs(yy - y), SIZE - np.abs(yy - y))
        w = np.exp(-(dx ** 2 + dy ** 2) / (2 * r * r)) * alpha * RNG.uniform(0.3, 1.0)
        out = out * (1 - w[..., None]) + c * w[..., None]
    return out


def wall(out):
    """Steel panels 1.2 m wide (a picture is 2.4 m): seams, rivets, overspray, grime low down"""
    base = np.array([226, 224, 216], np.float32)
    n = noise(SIZE, 6)[..., None]
    rgb = base * (0.94 + 0.08 * n) + grain(SIZE, 6)[..., None]
    rgb = clouds(rgb, 7, 0.10)
    rgb = flecks(rgb, 700, 60, (1, 2))
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    for x in (0, SIZE // 2):
        d.rectangle((x - 3, 0, x + 3, SIZE), fill=(150, 150, 146))
        d.line((x + 4, 0, x + 4, SIZE), fill=(245, 245, 240), width=2)
        for y in range(24, SIZE, 96):
            for dx in (-12, 12):
                d.ellipse((x + dx - 4, y - 4, x + dx + 4, y + 4), fill=(170, 170, 165))
                d.ellipse((x + dx - 2, y - 3, x + dx + 2, y + 1), fill=(240, 240, 236))
    for y in (0, SIZE // 2):
        d.rectangle((0, y - 2, SIZE, y + 2), fill=(165, 165, 160))
    save(out, "wall.jpg", np.array(img, np.float32))


def ceiling(out):
    base = np.array([214, 214, 210], np.float32)
    rgb = base * (0.95 + 0.06 * noise(SIZE, 5)[..., None]) + grain(SIZE, 5)[..., None]
    rgb = flecks(rgb, 300, 50, (1, 2))
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    for v in (0, SIZE // 2):
        d.rectangle((v - 2, 0, v + 2, SIZE), fill=(160, 160, 156))
        d.rectangle((0, v - 2, SIZE, v + 2), fill=(160, 160, 156))
    save(out, "ceiling.jpg", np.array(img, np.float32))


def floor(out):
    """Sealed concrete, grey, years of overspray settled on it"""
    base = np.array([150, 150, 146], np.float32)
    n = noise(SIZE, 5, 6)[..., None]
    rgb = base * (0.82 + 0.3 * n) + grain(SIZE, 14)[..., None]
    rgb = clouds(rgb, 10, 0.16)
    rgb = flecks(rgb, 1400, 70, (1, 3))
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    # Saw cuts every 2 m (a picture is 4 m)
    for v in (0, SIZE // 2):
        d.line((v, 0, v, SIZE), fill=(95, 95, 92), width=3)
        d.line((0, v, SIZE, v), fill=(95, 95, 92), width=3)
    save(out, "floor.jpg", np.array(img, np.float32))


def grate(out):
    """The steel grate over the floor's exhaust trench: bars across, a dark pit under"""
    rgb = np.zeros((SIZE, SIZE, 3), np.float32) + 22
    img = Image.fromarray(rgb.astype(np.uint8))
    d = ImageDraw.Draw(img)
    step = SIZE // 24
    for i in range(24):
        y = i * step
        d.rectangle((0, y, SIZE, y + step // 3), fill=(118, 118, 112))
        d.line((0, y, SIZE, y), fill=(160, 160, 152), width=2)
    for x in range(0, SIZE, SIZE // 6):
        d.rectangle((x, 0, x + 10, SIZE), fill=(96, 96, 90))
    arr = np.array(img, np.float32)
    arr = flecks(arr, 1500, 110)
    save(out, "grate.jpg", arr)


def filters(out):
    """The back wall's filter pads, in a steel frame: fibrous, stained with what they caught"""
    n = noise(SIZE, 40, 3)
    base = np.array([228, 222, 200], np.float32)
    rgb = base * (0.85 + 0.2 * n[..., None]) + grain(SIZE, 18)[..., None]
    rgb = clouds(rgb, 14, 0.28)
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    cells = 4
    step = SIZE // cells
    for i in range(cells + 1):
        v = i * step
        d.rectangle((v - 8, 0, v + 8, SIZE), fill=(120, 122, 118))
        d.rectangle((0, v - 8, SIZE, v + 8), fill=(120, 122, 118))
    save(out, "filter.jpg", np.array(img, np.float32))


def light(out):
    """A light box's diffuser: white, its tubes showing a little through it, a steel rim"""
    rgb = np.zeros((SIZE, SIZE, 3), np.float32) + 236
    xx = np.arange(SIZE, dtype=np.float32)
    for c in (SIZE * 0.3, SIZE * 0.7):
        rgb += (np.exp(-((xx - c) ** 2) / (2 * 40 ** 2)) * 19)[None, :, None]
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, SIZE - 1, SIZE - 1), outline=(150, 150, 146), width=18)
    save(out, "light.jpg", np.array(img, np.float32))


def steel(out):
    """Galvanised steel: the kick plates, the door frame"""
    base = np.array([150, 154, 156], np.float32)
    streaks = np.array(Image.fromarray((RNG.random((SIZE, 8)) * 255).astype(np.uint8)).resize((SIZE, SIZE), Image.BILINEAR), np.float32) / 255
    rgb = base * (0.85 + 0.25 * noise(SIZE, 8)[..., None]) + (streaks[..., None] - 0.5) * 18 + grain(SIZE, 8)[..., None]
    rgb = flecks(rgb, 1200, 100)
    save(out, "steel.jpg", rgb)


def shop(out):
    """The body shop outside the booth: dim, painted block, half seen through the door"""
    base = np.array([96, 92, 84], np.float32)
    rgb = base * (0.8 + 0.3 * noise(SIZE, 6)[..., None]) + grain(SIZE, 10)[..., None]
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    for y in range(0, SIZE, SIZE // 12):
        d.line((0, y, SIZE, y), fill=(70, 68, 62), width=4)
        off = (y // (SIZE // 12)) % 2 * (SIZE // 8)
        for x in range(off, SIZE + SIZE // 4, SIZE // 4):
            d.line((x, y, x, y + SIZE // 12), fill=(70, 68, 62), width=4)
    save(out, "shop.jpg", np.array(img, np.float32))


def solid(out, name, colour, wobble=6):
    rgb = np.zeros((256, 256, 3), np.float32) + np.array(colour, np.float32) + grain(256, wobble)[..., None]
    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).save(os.path.join(out, name), quality=QUALITY)


def cloth(out):
    """The painter's coveralls: white cotton twill, overspray down the front"""
    size = 512
    yy, xx = np.mgrid[0:size, 0:size]
    twill = ((xx + yy) % 6 < 3).astype(np.float32) * 10
    rgb = np.zeros((size, size, 3), np.float32) + 228 + twill[..., None] + (RNG.random((size, size, 1)) - 0.5) * 10
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).convert("RGBA")
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for _ in range(160):
        x, y = RNG.integers(0, size, 2)
        r = RNG.integers(1, 4)
        c = SPRAY[RNG.integers(0, len(SPRAY))]
        d.ellipse((x - r, y - r, x + r, y + r), fill=(*c, int(RNG.uniform(40, 110))))
    Image.alpha_composite(img, layer).convert("RGB").save(os.path.join(out, "cloth.jpg"), quality=QUALITY)


def mist(out):
    """The spray's cone: white, soft at its edges and its end, a little turbulent (alpha in a PNG)"""
    size = 256
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    # u along the cone (0 at the gun), v round it: alpha falls off along it and towards the edges of the picture
    along = xx / size
    edge = np.sin(np.pi * yy / size)
    turb = noise(size, 8, 3)
    alpha = (1 - along) ** 1.4 * (0.35 + 0.65 * edge) * (0.55 + 0.45 * turb) * 0.75
    rgba = np.zeros((size, size, 4), np.float32)
    rgba[..., :3] = 245
    rgba[..., 3] = np.clip(alpha, 0, 1) * 255
    Image.fromarray(rgba.astype(np.uint8), "RGBA").save(os.path.join(out, "mist.png"))


def main(out):
    os.makedirs(out, exist_ok=True)
    wall(out)
    ceiling(out)
    floor(out)
    grate(out)
    filters(out)
    light(out)
    steel(out)
    shop(out)
    cloth(out)
    mist(out)
    solid(out, "dark.jpg", (34, 34, 36))
    solid(out, "rubber.jpg", (22, 22, 22))
    solid(out, "metal.jpg", (176, 178, 180), 10)
    solid(out, "lens.jpg", (60, 70, 66))
    solid(out, "lamp.jpg", (255, 250, 236), 2)
    print(out)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
