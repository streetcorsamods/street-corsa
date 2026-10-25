"""
Touge Nights' meeting spot's skies: one equirectangular panorama per light of the day, for the scene builder's --sky.

Poly Haven panoramas (CC0, their tonemapped JPGs) at 4096 x 2048, all three of one hilltop, Clarens: by day (and,
darkened and warmed, at dusk) clarens_midday, its hills and a valley; by night clarens_night_01, the moon over the hills
and a town's lights in the valley. Each is kept whole: past the car park's guardrail the ground falls away and the dome
shows the valley below the horizon. Each is turned so its valley lies north of the car park, past the rail (the dome's
u = 0.5 is the game's +z, the car park's south), and lifted a little, so the valley shows over the dome's foot, which
stops 4.6 degrees under the horizon.

    python prepare_skies.py <out folder>

Needs Pillow and numpy. Writes sky_day.jpg, sky_dusk.jpg, sky_night.jpg.
"""
import io
import os
import sys
import urllib.request

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

URL = "https://dl.polyhaven.org/file/ph-assets/HDRIs/extra/Tonemapped%20JPG/{}.jpg"


def night(a):
    """The night panorama, exposed for its moonlit hill: darker and bluer, the town's lights kept bright"""
    lights = np.clip((a.max(axis=2, keepdims=True) - 0.55) / 0.45, 0, 1)
    dark = (a ** 1.7) * np.array([0.26, 0.3, 0.42])
    return dark * (1 - lights) + a * np.array([1.0, 0.85, 0.6]) * lights


def dusk(a):
    """The day's panorama at dusk: darker, the light warm low down and blue-grey up high"""
    rows = np.linspace(0, 1, a.shape[0])[:, None, None]
    warm = np.array([0.95, 0.55, 0.38]) * (1 - rows) + np.array([0.45, 0.45, 0.6]) * rows
    return (a ** 1.25) * warm * 0.75


# light: (Poly Haven asset, how it is coloured, the share of a turn it goes round by, rows of 2048 it goes up by)
SKIES = {
    "day": ("clarens_midday", None, 0.15, 30),
    "dusk": ("clarens_midday", dusk, 0.15, 30),
    "night": ("clarens_night_01", night, 0.5, 70),
}


def main(out):
    os.makedirs(out, exist_ok=True)
    pictures = {}
    for light, (asset, colour, turn, lift) in SKIES.items():
        print(f"{light}: {asset}")
        if asset not in pictures:
            request = urllib.request.Request(URL.format(asset), headers={"User-Agent": "StreetCorsa-asset-fetch"})
            data = urllib.request.urlopen(request, timeout=300).read()
            pictures[asset] = Image.open(io.BytesIO(data)).convert("RGB").resize((4096, 2048), Image.LANCZOS)
        a = np.asarray(pictures[asset]).astype(np.float32) / 255
        if colour is not None:
            a = colour(a)
        a = np.roll(a, int(a.shape[1] * turn), axis=1)
        a = np.concatenate([a[lift:], np.repeat(a[-1:], lift, axis=0)])
        Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8)).save(os.path.join(out, f"sky_{light}.jpg"), quality=90)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1])
