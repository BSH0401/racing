"""Builds the alpha-cut foliage card textures for the countryside trees.

Broadleaf: a dense clump of maple leaves scattered from the ambientCG LeafSet010 atlas (4 leaves),
each scaled, rotated and tinted, inner leaves darker so the clump reads as a volume.
Pine: a drooping branch drawn procedurally - a twig with side shoots and thousands of needles.

Usage: python Tools/foliage_cards.py <AmbientCG dir>   (writes <dir>/Generated/Foliage*.png)
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024


def load_leaves(src):
    color = Image.open(os.path.join(src, "LeafSet010", "LeafSet010_Color.jpg")).convert("RGB")
    alpha = Image.open(os.path.join(src, "LeafSet010", "LeafSet010_Opacity.jpg")).convert("L")
    w, h = color.size
    leaves = []
    for qy in range(2):
        for qx in range(2):
            box = (qx * w // 2, qy * h // 2, (qx + 1) * w // 2, (qy + 1) * h // 2)
            leaf = color.crop(box).convert("RGBA")
            leaf.putalpha(alpha.crop(box))
            leaves.append(leaf.crop(leaf.getbbox()))
    return leaves


def tint(img, rgb):
    r, g, b, a = img.split()
    r = r.point(lambda v: int(v * rgb[0]))
    g = g.point(lambda v: int(v * rgb[1]))
    b = b.point(lambda v: int(v * rgb[2]))
    return Image.merge("RGBA", (r, g, b, a))


def broadleaf(leaves, rng):
    card = Image.new("RGBA", (SIZE, SIZE), (40, 60, 25, 0))
    draw = ImageDraw.Draw(card)
    # A few twigs from the bottom centre so gaps show wood, not nothing.
    for _ in range(7):
        x0, y0 = SIZE * 0.5, SIZE * 0.98
        ang = math.radians(rng.uniform(-150, -30))
        length = rng.uniform(0.35, 0.6) * SIZE
        x1, y1 = x0 + math.cos(ang) * length, y0 + math.sin(ang) * length
        draw.line((x0, y0, x1, y1), fill=(70, 52, 36, 255), width=rng.randint(5, 9))
    count = 420
    for k in range(count):
        depth = k / count  # back to front: later leaves are on the outside and brighter
        # Points in a lumpy disc, denser in the middle.
        while True:
            u, v = rng.uniform(-1, 1), rng.uniform(-1, 1)
            r = math.hypot(u, v)
            lump = 0.82 + 0.18 * math.sin(math.atan2(v, u) * 5 + 1.3)
            if r < lump and rng.random() < 1.15 - r * 0.5:
                break
        cx, cy = SIZE * (0.5 + u * 0.43), SIZE * (0.48 + v * 0.43)
        leaf = leaves[rng.randrange(4)]
        s = rng.uniform(70, 125) / max(leaf.size)
        img = leaf.resize((max(2, int(leaf.size[0] * s)), max(2, int(leaf.size[1] * s))), Image.LANCZOS)
        img = img.rotate(rng.uniform(0, 360), resample=Image.BICUBIC, expand=True)
        light = 0.42 + 0.5 * depth + rng.uniform(-0.08, 0.08)
        hue = rng.uniform(-0.06, 0.06)
        img = tint(img, (0.5 * light * (1 + hue * 3), 0.7 * light, 0.32 * light * (1 - hue)))
        card.alpha_composite(img, (int(cx - img.size[0] / 2), int(cy - img.size[1] / 2)))
    return card


def pine(rng):
    card = Image.new("RGBA", (SIZE, SIZE), (30, 45, 25, 0))
    draw = ImageDraw.Draw(card)

    def needles(x0, y0, x1, y1, length, n, width):
        for i in range(n):
            t = i / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            base = math.atan2(y1 - y0, x1 - x0)
            l = length * (1.0 - 0.55 * t) * rng.uniform(0.7, 1.1)
            for side in (-1, 1):
                a = base + side * math.radians(rng.uniform(30, 80))
                g = rng.uniform(0.45, 1.05) * (1.0 + 0.35 * t)
                col = (min(255, int(30 * g)), min(255, int(66 * g)), min(255, int(36 * g)), 255)
                draw.line((x, y, x + math.cos(a) * l, y + math.sin(a) * l), fill=col, width=width)

    # Main branch: left edge (trunk side) to the right, sagging slightly.
    pts = [(SIZE * 0.02, SIZE * 0.42)]
    for i in range(1, 9):
        t = i / 8
        pts.append((SIZE * (0.02 + 0.95 * t), SIZE * (0.42 + 0.12 * t * t)))
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        draw.line((x0, y0, x1, y1), fill=(66, 48, 34, 255), width=9)
    # Side shoots, alternating, shrinking towards the tip.
    for i in range(1, 8):
        t = i / 8
        x, y = pts[i]
        x += rng.uniform(-20, 20)
        for side in (-1, 1):
            for _ in range(2):
                a = math.radians(rng.uniform(30, 65)) * side
                l = SIZE * 0.4 * (1 - 0.6 * t) * rng.uniform(0.7, 1.1)
                ex, ey = x + math.cos(a) * l, y + math.sin(a) * l
                draw.line((x, y, ex, ey), fill=(66, 48, 34, 255), width=4)
                needles(x, y, ex, ey, 44, 110, 3)
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        needles(x0, y0, x1, y1, 56, 90, 3)
    return card.filter(ImageFilter.SMOOTH)


def main():
    src = sys.argv[1]
    out = os.path.join(src, "Generated")
    os.makedirs(out, exist_ok=True)
    rng = random.Random(7)
    broadleaf(load_leaves(src), rng).save(os.path.join(out, "FoliageBroadleaf.png"))
    pine(rng).save(os.path.join(out, "FoliagePine.png"))
    print("written to", out)


if __name__ == "__main__":
    main()
