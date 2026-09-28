"""
The gas station's skies: one equirectangular panorama per light of the day, for StationSceneBuilder's --sky.

Downloads Poly Haven "pure sky" panoramas (CC0, tonemapped JPGs), brings them to 4096 x 2048, darkens and tints the night
one, and fills what is below the horizon with a hazy ground colour: the station's terrain is cut 250 m out, and past it
the dome shows.

    python prepare_skies.py <out folder>

Needs Pillow and numpy. Writes sky_day.jpg, sky_dusk.jpg, sky_night.jpg.
"""
import io
import sys
import urllib.request

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

URL = "https://dl.polyhaven.org/file/ph-assets/HDRIs/extra/Tonemapped%20JPG/{}.jpg"

# light: (Poly Haven asset, colour below the horizon)
SKIES = {
    "day": ("kloofendal_48d_partly_cloudy_puresky", (96, 104, 92)),
    "dusk": ("qwantani_dusk_2_puresky", (58, 52, 60)),
    "night": ("qwantani_night_puresky", (7, 8, 11)),
}


def night(a):
    """The night panorama is exposed for its stars: much darker, and blue"""
    return (a ** 1.8) * np.array([0.22, 0.26, 0.38])


def main(out):
    for light, (asset, ground) in SKIES.items():
        print(f"{light}: {asset}")
        data = urllib.request.urlopen(URL.format(asset)).read()
        image = Image.open(io.BytesIO(data)).convert("RGB").resize((4096, 2048), Image.LANCZOS)
        a = np.asarray(image).astype(np.float32) / 255
        if light == "night":
            a = night(a)
        a *= 255

        # The horizon's own colour fading into the ground's over a dozen rows, the ground below
        h = a.shape[0]
        top = h // 2 - 6
        for row in range(top, h):
            t = min(1.0, max(0.0, (row - top) / 12))
            a[row] = a[row] * (1 - t) + np.array(ground, np.float32) * t

        Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(f"{out}/sky_{light}.jpg", quality=90)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1])
