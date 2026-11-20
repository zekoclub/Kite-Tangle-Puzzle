#!/usr/bin/env python3
"""Draws the Lock & Key badges and the tutorial tap arrow (centered sprites, 4x supersampled):

  ui/badge_lock.png   white padlock on a transparent background (tinted per key pair in the game)
  ui/badge_key.png    white key
  ui/badge_open.png   white open padlock (shown for a moment when its key kite is freed)
  ui/tap_arrow.png    saffron arrow with an ink outline, pointing down, for "tap this kite"

Run from the project root: python3 Tools/gen_badges.py
"""
from PIL import Image, ImageDraw

SS = 4
ART = "Assets/Resources/Art/ui/"
INK = "#2A1A12"


def canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def save(img, w, h, name):
    img.resize((w, h), Image.LANCZOS).save(ART + name)


def s(*v):
    return [x * SS for x in v]


def padlock(open_=False):
    w = h = 128
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    # Shackle: a thick arc above the body (lifted and shifted when open).
    lift = 12 if open_ else 0
    shift = 22 if open_ else 0
    d.arc(s(34 + shift, 16 - lift, 94 + shift, 80 - lift), 180, 360, fill="white", width=14 * SS)
    d.line(s(41 + shift, 48 - lift, 41 + shift, 62), fill="white", width=14 * SS)
    if not open_:
        d.line(s(87, 48, 87, 62), fill="white", width=14 * SS)
    # Body with a keyhole cut out.
    d.rounded_rectangle(s(20, 56, 108, 120), radius=14 * SS, fill="white")
    d.ellipse(s(56, 74, 72, 90), fill=(0, 0, 0, 0))
    d.polygon([tuple(s(59, 84)), tuple(s(69, 84)), tuple(s(72, 106)), tuple(s(56, 106))], fill=(0, 0, 0, 0))
    save(img, w, h, "badge_open.png" if open_ else "badge_lock.png")


def key():
    w = h = 128
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    # Ring (bow) on the left, shaft to the right, two teeth down.
    d.ellipse(s(8, 34, 64, 90), fill="white")
    d.ellipse(s(24, 50, 48, 74), fill=(0, 0, 0, 0))
    d.rounded_rectangle(s(56, 54, 122, 70), radius=6 * SS, fill="white")
    d.rectangle(s(96, 62, 108, 92), fill="white")
    d.rectangle(s(78, 62, 88, 84), fill="white")
    save(img, w, h, "badge_key.png")


def tap_arrow():
    w, h = 128, 160
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    pts = [(40, 8), (88, 8), (88, 80), (118, 80), (64, 150), (10, 80), (40, 80)]
    d.polygon([tuple(s(*q)) for q in pts], fill="#F2A23A")
    d.line([tuple(s(*q)) for q in pts + [pts[0]]], fill=INK, width=8 * SS, joint="curve")
    d.line(s(52, 20, 52, 76), fill="#FFD27A", width=6 * SS)  # shine
    save(img, w, h, "tap_arrow.png")


if __name__ == "__main__":
    padlock()
    padlock(open_=True)
    key()
    tap_arrow()
    print("wrote badge_lock, badge_open, badge_key, tap_arrow")
