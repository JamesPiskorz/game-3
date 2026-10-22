"""Contact sheets of the texture library, for reviewing it outside the game.

python Tools/Textures/swatches.py fabrics <out.png> [size]
python Tools/Textures/swatches.py prints <out.png> [size]
"""
import sys
import os

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fabrics  # noqa: E402
import prints  # noqa: E402
import texture_kit as tk  # noqa: E402

TINTS = [(0.22, 0.32, 0.62), (0.75, 0.18, 0.22), (0.92, 0.88, 0.80), (0.20, 0.20, 0.22), (0.86, 0.66, 0.28)]


def fabric_sheet(out, size=256, cell=200):
    names = list(fabrics.FABRICS)
    cols = 8
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 18)), "white")
    draw = ImageDraw.Draw(sheet)
    bad = []
    for i, name in enumerate(names):
        tile = fabrics.FABRICS[name](size, 7)
        if not tk.is_tileable(tile):
            bad.append(name)
        tint = np.array(TINTS[i % len(TINTS)])
        # Two repeats of the tile side by side and stacked, to show the seams if any, at the tile's real scale.
        big = np.tile(tile, (2, 2))
        rgb = (big[:, :, None] * tint[None, None, :] * 255).clip(0, 255).astype(np.uint8)
        im = Image.fromarray(rgb).resize((cell, cell), Image.LANCZOS)
        x, y = (i % cols) * cell, (i // cols) * (cell + 18)
        sheet.paste(im, (x, y + 18))
        draw.text((x + 4, y + 3), f"{name}  mean {tile.mean():.2f}", fill="black")
    sheet.save(out)
    print("sheet", out, "not tileable:", bad)


def print_sheet(out, size=256, cell=180):
    names = list(prints.PRINTS)
    cols = 9
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 18)), "white")
    draw = ImageDraw.Draw(sheet)
    bad, failed = [], []
    for i, name in enumerate(names):
        fn = prints.PRINTS[name][0]
        try:
            colour, ink = fn(size, 7)
        except Exception as e:  # report and carry on
            failed.append(f"{name}: {e}")
            continue
        colour, ink = np.clip(colour, 0, 1), np.clip(ink, 0, 1)
        if not tk.is_tileable(colour, 0.6):
            bad.append(name)
        base = np.array(TINTS[i % len(TINTS)])
        pattern = np.array(TINTS[(i + 2) % len(TINTS)])
        c2, k2 = np.tile(colour, (2, 2)), np.tile(ink, (2, 2))
        rgb = (base[None, None] * (1 - c2[..., None]) + pattern[None, None] * c2[..., None]) * (1 - 0.7 * k2[..., None])
        im = Image.fromarray((rgb * 255).clip(0, 255).astype(np.uint8)).resize((cell, cell), Image.LANCZOS)
        x, y = (i % cols) * cell, (i // cols) * (cell + 18)
        sheet.paste(im, (x, y + 18))
        draw.text((x + 4, y + 3), name, fill="black")
    sheet.save(out)
    print("sheet", out, "maybe seams:", bad, "failed:", failed)


if __name__ == "__main__":
    which, out = sys.argv[1], sys.argv[2]
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 256
    if which == "fabrics":
        fabric_sheet(out, size)
    else:
        print_sheet(out, size)
