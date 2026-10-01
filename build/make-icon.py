#!/usr/bin/env python3
"""
Generates the application icon (src/Orbix/Assets/orbix.ico and orbix.png).

    python -m venv .venv && .venv/bin/pip install pillow && .venv/bin/python build/make-icon.py

The icon is an orb with six satellites - the "radial menu" in one picture. Small sizes use a simplified
variant (bigger orb, no satellites) so that they stay readable.
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageOps

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Orbix", "Assets")
SS = 4  # super-sampling factor


def orb_layer(diameter, inner, mid, outer, light=(0.36, 0.30)):
    """Sphere-like radial gradient, cut to a circle (anti-aliased by the caller's super-sampling)."""
    big = Image.radial_gradient("L").resize((diameter * 2, diameter * 2), Image.BICUBIC)
    # move the gradient centre to the upper left to get a highlight
    cx = int(diameter * light[0] * 1.0)
    cy = int(diameter * light[1] * 1.0)
    # crop so that the gradient centre (diameter, diameter) lands on (cx, cy) of the output
    left = diameter - cx
    top = diameter - cy
    mask = big.crop((left, top, left + diameter, top + diameter))
    mask = mask.point(lambda v: min(255, int(v * 0.78)))  # do not reach the darkest colour on the far edge
    coloured = ImageOps.colorize(mask, black=inner, mid=mid, white=outer).convert("RGBA")
    circle = Image.new("L", (diameter, diameter), 0)
    ImageDraw.Draw(circle).ellipse((0, 0, diameter - 1, diameter - 1), fill=255)
    coloured.putalpha(circle)
    return coloured


def render(size, detailed):
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    accent = (108, 99, 255)
    light = (196, 190, 255)
    deep = (64, 52, 214)

    if detailed:
        orb_d = int(S * 0.42)
        orbit_r = S * 0.365
        sat_d = int(S * 0.145)
    else:
        orb_d = int(S * 0.70)
        orbit_r = 0
        sat_d = 0

    cx = cy = S / 2

    # soft glow under everything (the colour is constant, only the alpha is blurred - no dark fringes)
    gmask = Image.new("L", (S, S), 0)
    gr = orb_d * 0.62
    ImageDraw.Draw(gmask).ellipse((cx - gr, cy - gr, cx + gr, cy + gr), fill=120)
    gmask = gmask.filter(ImageFilter.GaussianBlur(S * 0.05))
    glow = Image.new("RGBA", (S, S), accent + (0,))
    glow.putalpha(gmask)
    img = Image.alpha_composite(img, glow)

    if detailed:
        # thin guide ring
        ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        rd = ImageDraw.Draw(ring)
        w = max(2, int(S * 0.014))
        rd.ellipse((cx - orbit_r, cy - orbit_r, cx + orbit_r, cy + orbit_r), outline=accent + (110,), width=w)
        img = Image.alpha_composite(img, ring)

        # satellites
        for k in range(6):
            a = math.radians(-90 + 60 * k)
            sx = cx + orbit_r * math.cos(a) - sat_d / 2
            sy = cy + orbit_r * math.sin(a) - sat_d / 2
            sat = orb_layer(sat_d, light, accent, deep, light=(0.38, 0.30))
            img.alpha_composite(sat, (int(sx), int(sy)))

    # the central orb
    orb = orb_layer(orb_d, light, accent, deep)
    img.alpha_composite(orb, (int(cx - orb_d / 2), int(cy - orb_d / 2)))

    # specular highlight
    smask = Image.new("L", (S, S), 0)
    hx, hy = cx - orb_d * 0.16, cy - orb_d * 0.22
    hw, hh = orb_d * 0.30, orb_d * 0.17
    ImageDraw.Draw(smask).ellipse((hx - hw / 2, hy - hh / 2, hx + hw / 2, hy + hh / 2), fill=130)
    smask = smask.filter(ImageFilter.GaussianBlur(S * 0.008))
    spec = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    spec.putalpha(smask)
    img = Image.alpha_composite(img, spec)

    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    frames = {}
    for s in sizes:
        frames[s] = render(s, detailed=s >= 48)

    png = os.path.join(OUT_DIR, "orbix.png")
    frames[256].save(png)
    ico = os.path.join(OUT_DIR, "orbix.ico")
    frames[256].save(ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=[frames[s] for s in sizes if s != 256])
    print("written", png, ico, os.path.getsize(ico), "bytes")


if __name__ == "__main__":
    sys.exit(main())
