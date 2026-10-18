"""The wizard's pictures from the game's main screen background: dialog.bmp (welcome and finish pages, 493x312, the
picture in the left 164 pixels) and banner.bmp (the other pages' top, 493x58, the picture fading in on the right).
Run once after the background changes; the bitmaps are committed, so a build needs no Python."""
import pathlib

from PIL import Image

here = pathlib.Path(__file__).parent
source = Image.open(here.parent / "Street Corsa" / "Assets" / "Images" / "Backgrounds" / "main.png").convert("RGB")
w, h = source.size

# Welcome and finish: one car, the full height, in the panel WiX leaves on the left
panel_w, panel_h = 164, 312
crop_w = int(h * panel_w / panel_h)
left = int(w * 0.39) - crop_w // 2
panel = source.crop((left, 0, left + crop_w, h)).resize((panel_w, panel_h), Image.LANCZOS)
dialog = Image.new("RGB", (493, 312), "white")
dialog.paste(panel, (0, 0))
dialog.save(here / "dialog.bmp")

# Banner: the two cars on the right, faded into the white the title and description are written on
band_w, band_h = 220, 58
crop_h = int(w * 0.5 * band_h / band_w)
top = int(h * 0.62) - crop_h // 2
band = source.crop((int(w * 0.25), top, int(w * 0.75), top + crop_h)).resize((band_w, band_h), Image.LANCZOS)
banner = Image.new("RGB", (493, 58), "white")
fade = Image.linear_gradient("L").rotate(90).resize((band_w, band_h))  # the mask: 255 (the picture) on the right, 0 on the left
fade = fade.point(lambda v: min(255, v * 2))
banner.paste(band, (493 - band_w, 0), fade)
banner.save(here / "banner.bmp")
print("dialog.bmp and banner.bmp written")
