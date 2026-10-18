"""The wizard's pictures, in the game's own look: dialog.bmp (welcome and finish pages, 493x312, the picture in the left 164
pixels) and banner.bmp (the other pages' top, 493x58, the picture on the right). Both carry the game's icon
(street_corsa.ico) and its title lettered as the intro letters it (Impact, italic, the chrome of IntroTitleBrush), over
the main screen background darkened. Run once after any of them changes; the bitmaps are committed, so a build needs no
Python. Windows only (the Impact font)."""
import pathlib

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

here = pathlib.Path(__file__).parent
game = here.parent / "Street Corsa"
source = Image.open(game / "Assets" / "Images" / "Backgrounds" / "main.png").convert("RGB")
icon = Image.open(game / "street_corsa.ico").convert("RGBA")
w, h = source.size
FONT = "C:/Windows/Fonts/impact.ttf"

# The intro title's chrome (Styles/Colors.xaml IntroTitle*): white at the top, a hard step to dark at the middle, light at
# the bottom
CHROME = [(0.0, (0xFF, 0xFF, 0xFF)), (0.45, (0xC8, 0xCC, 0xD2)), (0.5, (0x6E, 0x74, 0x7C)), (1.0, (0xE6, 0xE8, 0xEB))]


def chrome_at(t):
    for (a, ca), (b, cb) in zip(CHROME, CHROME[1:]):
        if t <= b:
            k = 0 if b == a else (t - a) / (b - a)
            return tuple(round(x + (y - x) * k) for x, y in zip(ca, cb))
    return CHROME[-1][1]


def lettering(text, height):
    """The text in the intro's lettering, cut out (RGBA), its capitals about this many pixels high"""
    font = ImageFont.truetype(FONT, int(height * 1.32))
    left, top, right, bottom = font.getbbox(text)
    tw, th = right - left, bottom - top
    lean = int(th * 0.2)  # the intro's italic: Impact has none, WPF leans it about this much
    pad = 4
    mask = Image.new("L", (tw + lean + pad * 2, th + pad * 2), 0)
    ImageDraw.Draw(mask).text((pad - left + lean, pad - top), text, font=font, fill=255)
    mask = mask.transform(mask.size, Image.AFFINE, (1, lean / mask.size[1], -lean / 2, 0, 1, 0), Image.BICUBIC)
    gradient = Image.new("RGB", mask.size)
    draw = ImageDraw.Draw(gradient)
    for y in range(mask.size[1]):
        draw.line([(0, y), (mask.size[0], y)], fill=chrome_at(min(1.0, max(0.0, (y - pad) / max(1, th)))))
    out = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    out.paste(gradient, (0, 0), mask)
    return out


def with_shadow(picture, offset, blur):
    """The picture over a soft dark shadow of itself, as the intro sets its title"""
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


# Welcome and finish: the panel WiX leaves on the left, the background's cars darkened under the icon and the title
panel_w, panel_h = 164, 312
crop_w = int(h * panel_w / panel_h)
left = int(w * 0.39) - crop_w // 2
panel = darkened(source.crop((left, 0, left + crop_w, h)).resize((panel_w, panel_h), Image.LANCZOS), 0.45).convert("RGBA")
shade = Image.linear_gradient("L").resize((panel_w, panel_h)).point(lambda v: int(150 - v * 0.45))
panel.alpha_composite(Image.merge("RGBA", (*Image.new("RGB", (panel_w, panel_h)).split(), shade)))
place_centred(panel, with_shadow(icon.resize((104, 104), Image.LANCZOS), 3, 4), panel_w / 2, 22)
street = with_shadow(lettering("STREET", 30), 3, 3)
corsa = with_shadow(lettering("CORSA", 30), 3, 3)
place_centred(panel, street, panel_w / 2, 140)
place_centred(panel, corsa, panel_w / 2, 140 + street.size[1] - 14)
dialog = Image.new("RGB", (493, 312), "white")
dialog.paste(panel.convert("RGB"), (0, 0))
dialog.save(here / "dialog.bmp")

# Banner: the title and the icon on the right, over the background's cars darkened and faded into the white the page's
# title and description are written on
band_w, band_h = 210, 58
crop_h = int(w * 0.5 * band_h / band_w)
top = int(h * 0.62) - crop_h // 2
band = darkened(source.crop((int(w * 0.25), top, int(w * 0.75), top + crop_h)).resize((band_w, band_h), Image.LANCZOS), 0.5).convert("RGBA")
title = lettering("STREET CORSA", 17)
band.alpha_composite(title, (band_w - 57 - title.size[0], (band_h - title.size[1]) // 2))
band.alpha_composite(icon.resize((44, 44), Image.LANCZOS), (band_w - 50, (band_h - 44) // 2))
banner = Image.new("RGB", (493, 58), "white")
fade = Image.linear_gradient("L").rotate(90).resize((band_w, band_h))  # the mask: 255 (the picture) on the right, 0 on the left
fade = fade.point(lambda v: min(255, v * 3))
banner.paste(band.convert("RGB"), (493 - band_w, 0), fade)
banner.save(here / "banner.bmp")
print("dialog.bmp and banner.bmp written")
