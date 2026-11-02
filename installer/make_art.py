"""The setup's pictures, in the game's own look:
  dialog.bmp  the welcome, uninstall and finish pages (493x312, the picture in the left 164 pixels)
  banner.bmp  the other pages' top (493x58, the picture on the right)
Both carry the game's icon (street_corsa.ico, the badge on its shortcuts and window) and its chrome wordmark
(Assets/Images/Intro/logo.png), over the intro's garage darkened (Assets/Images/Backgrounds/init.png). Windows'
installed apps show the game's icon itself (Package.wxs). Run once after any of them changes; the pictures are
committed, so a build needs no Python."""
import pathlib

from PIL import Image, ImageEnhance, ImageFilter

here = pathlib.Path(__file__).parent
game = here.parent / "Street Corsa"
garage = Image.open(game / "Assets" / "Images" / "Backgrounds" / "init.png").convert("RGB")
wordmark = Image.open(game / "Assets" / "Images" / "Intro" / "logo.png").convert("RGBA")
# The icon file's biggest picture (256 px), the badge on a clear background
badge = Image.open(game / "street_corsa.ico").convert("RGBA")
w, h = garage.size


def sized(picture, width):
    return picture.resize((width, round(picture.size[1] * width / picture.size[0])), Image.LANCZOS)


def with_shadow(picture, offset, blur):
    """The picture over a soft dark shadow of itself"""
    margin = blur * 2 + max(offset, 0)
    out = Image.new("RGBA", (picture.size[0] + margin * 2, picture.size[1] + margin * 2), (0, 0, 0, 0))
    shadow = Image.new("RGBA", picture.size, (0, 0, 0, 0))
    shadow.putalpha(picture.getchannel("A").point(lambda a: int(a * 0.8)))
    out.alpha_composite(shadow, (margin + offset, margin + offset))
    out = out.filter(ImageFilter.GaussianBlur(blur))
    out.alpha_composite(picture, (margin, margin))
    return out


def darkened(picture, amount):
    return ImageEnhance.Brightness(picture).enhance(amount)


def place_centred(canvas, picture, cx, y):
    canvas.alpha_composite(picture, (int(cx - picture.size[0] / 2), int(y)))


# Welcome and finish: the panel WiX leaves on the left, the garage round the car darkened under the badge and the
# wordmark
panel_w, panel_h = 164, 312
crop_w = int(h * panel_w / panel_h)
left = int(w * 0.33) - crop_w // 2
panel = darkened(garage.crop((left, 0, left + crop_w, h)).resize((panel_w, panel_h), Image.LANCZOS), 0.42).convert("RGBA")
shade = Image.linear_gradient("L").resize((panel_w, panel_h)).point(lambda v: int(170 - v * 0.5))
panel.alpha_composite(Image.merge("RGBA", (*Image.new("RGB", (panel_w, panel_h)).split(), shade)))
place_centred(panel, with_shadow(sized(badge, 118), 3, 4), panel_w / 2, 26)
place_centred(panel, with_shadow(sized(wordmark, 148), 2, 3), panel_w / 2, 168)
dialog = Image.new("RGB", (493, 312), "white")
dialog.paste(panel.convert("RGB"), (0, 0))
dialog.save(here / "dialog.bmp")

# Banner: the wordmark and the badge on the right, over the garage darkened and faded into the white the page's title
# and description are written on
band_w, band_h = 230, 58
crop_h = int(w * 0.5 * band_h / band_w)
top = int(h * 0.62) - crop_h // 2
band = darkened(garage.crop((int(w * 0.2), top, int(w * 0.7), top + crop_h)).resize((band_w, band_h), Image.LANCZOS), 0.45).convert("RGBA")
mark = sized(wordmark, 118)
band.alpha_composite(mark, (band_w - 56 - mark.size[0], (band_h - mark.size[1]) // 2))
band.alpha_composite(sized(badge, 46), (band_w - 51, (band_h - 46) // 2))
banner = Image.new("RGB", (493, 58), "white")
fade = Image.linear_gradient("L").rotate(90).resize((band_w, band_h))  # the mask: 255 (the picture) on the right, 0 on the left
fade = fade.point(lambda v: min(255, v * 3))
banner.paste(band.convert("RGB"), (493 - band_w, 0), fade)
banner.save(here / "banner.bmp")

print("dialog.bmp and banner.bmp written")
