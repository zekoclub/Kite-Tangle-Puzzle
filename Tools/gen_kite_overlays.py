#!/usr/bin/env python3
"""Draws the extra kite sprites that sit on (or replace) the colored kites, in the kites' own 256x358 frame
(viewBox -19..19 x -23..33, string point at y=22), so the importer's kite pivot lines them up:

  kite_hidden.png       gray "?" kite for hidden-color kites (and spool_hidden.png, a gray charkhi)
  kite_ice.png          frost layer for iced kites (first tap cracks it)
  kite_skin_<name>.png  white/ink patterns drawn over any color: chand, patti, gudda, sunehri

Run from the project root: python3 Tools/gen_kite_overlays.py
"""
import math
from PIL import Image, ImageDraw, ImageFont, ImageChops

W, H = 256, 358
SS = 4                                   # supersampling
X0, Y0, UNIT = -19, -23, W / 38          # viewBox origin and px per unit
ART = "Assets/Resources/Art/"
FONT = "Assets/Resources/Fonts/Baloo2-ExtraBold.ttf"

TOP, LEFT, RIGHT, BOTTOM = (0, -20), (-16, -2), (16, -2), (0, 22)


def p(x, y):
    return ((x - X0) * UNIT * SS, (y - Y0) * UNIT * SS)


def canvas():
    return Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))


def diamond_mask(grow=0.0):
    m = Image.new("L", (W * SS, H * SS), 0)
    d = ImageDraw.Draw(m)
    pts = [(0, -20 - grow), (-16 - grow, -2), (0, 22 + grow), (16 + grow, -2)]
    d.polygon([p(*q) for q in pts], fill=255)
    return m


def finish(img, mask=None):
    if mask is not None:
        a = ImageChops.multiply(img.getchannel("A"), mask)
        img.putalpha(a)
    return img.resize((W, H), Image.LANCZOS)


def hidden_kite():
    img = canvas()
    d = ImageDraw.Draw(img)
    w = int(3 * UNIT * SS)
    d.polygon([p(*TOP), p(*LEFT), p(*BOTTOM)], fill="#BDB5AA")
    d.line([p(*TOP), p(*LEFT), p(*BOTTOM)], fill="#BDB5AA", width=w, joint="curve")
    d.polygon([p(*TOP), p(*RIGHT), p(*BOTTOM)], fill="#948B80")
    d.line([p(*TOP), p(*RIGHT), p(*BOTTOM)], fill="#948B80", width=w, joint="curve")
    d.polygon([p(-6, 30), p(0, 21), p(6, 30)], fill="#6E665C")
    font = ImageFont.truetype(FONT, int(26 * UNIT * SS))
    d.text(p(0, 4), "?", font=font, fill="#FBF1DE", anchor="mm", stroke_width=int(1.6 * UNIT * SS), stroke_fill="#2A1A12")
    finish(img).save(ART + "kites/kite_hidden.png")


def hidden_spool():
    src = Image.open(ART + "spools/spool_red.png").convert("RGBA")
    gray = src.convert("LA").convert("RGBA")
    gray.putalpha(src.getchannel("A"))
    gray.save(ART + "spools/spool_hidden.png")


def ice():
    img = canvas()
    d = ImageDraw.Draw(img)
    d.polygon([p(0, -22.5), p(-18.5, -2), p(0, 24.5), p(18.5, -2)], fill=(200, 236, 255, 150))
    w = max(1, int(1.1 * UNIT * SS))
    for a, b in [((0, -20), (-3, -8)), ((-3, -8), (-11, -3)), ((-3, -8), (4, 2)), ((4, 2), (12, -1)),
                 ((4, 2), (1, 12)), ((1, 12), (-4, 16)), ((-11, -3), (-9, 6))]:
        d.line([p(*a), p(*b)], fill=(255, 255, 255, 230), width=w)
    # frosty rim
    rim = int(1.6 * UNIT * SS)
    d.line([p(0, -22.5), p(-18.5, -2), p(0, 24.5), p(18.5, -2), p(0, -22.5)], fill=(240, 250, 255, 220), width=rim, joint="curve")
    finish(img).save(ART + "kites/kite_ice.png")


def skin(name, draw):
    img = canvas()
    draw(ImageDraw.Draw(img))
    finish(img, diamond_mask(1.0)).save(ART + f"kites/kite_skin_{name}.png")


def chand(d):
    # Crescent moon and a star, cream with an ink edge.
    cx, cy, r = 0, 1, 7.5
    d.ellipse([p(cx - r, cy - r), p(cx + r, cy + r)], fill=(251, 241, 222, 255))
    d.ellipse([p(cx - r + 3.6, cy - r - 1), p(cx + r + 3.6, cy + r - 1)], fill=(0, 0, 0, 0))
    star = []
    for i in range(10):
        ang = -math.pi / 2 + i * math.pi / 5
        rr = 3.4 if i % 2 == 0 else 1.4
        star.append(p(6 + rr * math.cos(ang), -1 + rr * math.sin(ang)))
    d.polygon(star, fill=(251, 241, 222, 255))


def patti(d):
    # Two bands across the kite.
    for y0, y1, col in [(-9, -5.5, (251, 241, 222, 235)), (5, 8.5, (42, 26, 18, 120))]:
        d.polygon([p(-20, y0), p(20, y0), p(20, y1), p(-20, y1)], fill=col)


def gudda(d):
    # The classic round "eye" in the middle of a Basant patang.
    d.ellipse([p(-7, -9), p(7, 5)], fill=(42, 26, 18, 150))
    d.ellipse([p(-5, -7), p(5, 3)], fill=(251, 241, 222, 245))
    d.ellipse([p(-2.2, -4.2), p(2.2, 0.2)], fill=(42, 26, 18, 200))


def sunehri(d):
    # A gold border along the edges.
    w = int(2.6 * UNIT * SS)
    d.line([p(0, -19.6), p(-15.6, -2), p(0, 21.6), p(15.6, -2), p(0, -19.6)], fill=(255, 205, 64, 255), width=w, joint="curve")
    d.line([p(0, -19.6), p(-15.6, -2), p(0, 21.6), p(15.6, -2), p(0, -19.6)], fill=(255, 240, 170, 255), width=max(1, w // 3), joint="curve")


if __name__ == "__main__":
    hidden_kite()
    hidden_spool()
    ice()
    skin("chand", chand)
    skin("patti", patti)
    skin("gudda", gudda)
    skin("sunehri", sunehri)
    print("ok")
