"""App icons for Kite Tangle, built from the game's own sprites.

Writes Assets/Store/icon.png (1024, flat), adaptive-background.png and adaptive-foreground.png (432 each,
Android adaptive layers) and Store/play-icon-512.png (Play listing). Run from the project root:
    python3 Tools/gen_icon.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter

ART = "Assets/Resources/Art"
EDGE = (251, 246, 236, 255)
RED, RED_DARK = (224, 83, 61, 255), (184, 57, 42, 255)
YEL, YEL_DARK = (242, 184, 49, 255), (201, 144, 24, 255)


def sky(size):
    im = Image.open(f"{ART}/backgrounds/sky_day.png").convert("RGBA")
    w, h = im.size
    top = 0  # a square from the upper sky, where the gradient is richest
    return im.crop((0, top, w, top + w)).resize((size, size), Image.LANCZOS)


def bezier(p0, p1, p2, n=48):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in (i / (n - 1) for i in range(n))]


def string(draw, pts, base, dark, s):
    draw.line(pts, fill=EDGE, width=int(40 * s), joint="curve")
    draw.line(pts, fill=dark, width=int(24 * s), joint="curve")
    draw.line(pts, fill=base, width=int(14 * s), joint="curve")


def kite(layer, name, anchor, width, angle):
    k = Image.open(f"{ART}/kites/kite_{name}.png").convert("RGBA")
    k = k.resize((int(width), int(width * k.height / k.width)), Image.LANCZOS)
    pivot = (k.width / 2, k.height * (1 - 11 / 56))  # string point, as in ArtImporter
    big = Image.new("RGBA", (k.width * 2, k.height * 2))
    big.paste(k, (int(big.width / 2 - pivot[0]), int(big.height / 2 - pivot[1])), k)
    big = big.rotate(angle, resample=Image.BICUBIC)
    shadow = Image.new("RGBA", big.size, (42, 26, 18, 0))
    shadow.putalpha(big.getchannel("A").point(lambda a: a * 0.25).filter(ImageFilter.GaussianBlur(width * 0.03)))
    pos = (int(anchor[0] - big.width / 2), int(anchor[1] - big.height / 2))
    layer.alpha_composite(shadow, (pos[0] + int(width * 0.06), pos[1] + int(width * 0.1)))
    layer.alpha_composite(big, pos)


def foreground(size, scale):
    """Two kites whose strings cross; the red string lies on top (the game's rule)."""
    s = size / 1024 * scale
    off = size / 2 * (1 - scale)
    P = lambda x, y: (off + x * s, off + y * s)
    layer = Image.new("RGBA", (size, size))
    d = ImageDraw.Draw(layer)
    yel_anchor, red_anchor = P(690, 470), P(370, 560)
    string(d, bezier(P(90, 1300), P(560, 900), yel_anchor), YEL, YEL_DARK, s)   # below
    string(d, bezier(P(960, 1300), P(470, 920), red_anchor), RED, RED_DARK, s)   # on top
    kite(layer, "yellow", yel_anchor, 300 * s, -12)
    kite(layer, "red", red_anchor, 400 * s, 10)
    return layer


def main():
    os.makedirs("Assets/Store", exist_ok=True)
    os.makedirs("Store", exist_ok=True)
    flat = sky(1024)
    flat.alpha_composite(foreground(1024, 1.0))
    flat.save("Assets/Store/icon.png")
    flat.convert("RGB").resize((512, 512), Image.LANCZOS).save("Store/play-icon-512.png")
    # Adaptive: the launcher crops to a circle/squircle, so the kites sit inside the 66/108 safe zone.
    sky(432).save("Assets/Store/adaptive-background.png")
    foreground(432, 0.8).save("Assets/Store/adaptive-foreground.png")
    print("icons written")


if __name__ == "__main__":
    main()
