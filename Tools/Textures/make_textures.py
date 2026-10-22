"""Writes the clothing texture library into the Unity project (decision 0008):

- fabrics: Assets/_Game/Characters/Content/Fabrics/fabric.<id>.png, a seamless grey tile of each fabric's surface
  (fabrics.py), and Fabrics/library.json (id, name, mean brightness, metres per tile, style tags, whether it takes
  a print and which (a knit only takes knitted prints), how much of its structure the shader adds as light on dark
  cloth, the colours it usually comes in and how often, how common it is): the generated catalog's fabrics. Unity makes a material per
  fabric (FabricTextures.cs) and every generated garment wears its fabric's (its spec's "fabric" in outfits.json).
- prints: Assets/_Game/Characters/Content/Patterns/pattern.<id>_01.png (red = pattern colour, green = ink, blue = the
  second colour) for every print in prints.py, and Patterns/library.json, the catalog entries (name, tags, tiles per
  metre, weight, how the second colour follows the first) that the generated content's catalog takes its patterns from.

python Tools/Textures/make_textures.py [fabrics|prints|all]
"""
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import fabrics  # noqa: E402
import prints  # noqa: E402

CONTENT = os.path.join(HERE, "..", "..", "Assets", "_Game", "Characters", "Content")
FABRIC_SIZE = 512
PRINT_SIZE = 256


def write_fabrics():
    folder = os.path.join(CONTENT, "Fabrics")
    os.makedirs(folder, exist_ok=True)
    entries = []
    for i, (fid, fn) in enumerate(fabrics.FABRICS.items()):
        tile = np.clip(fn(FABRIC_SIZE, 1000 + i), 0, 1)
        grey = (tile * 255).round().astype(np.uint8)
        Image.fromarray(np.stack([grey] * 3, axis=-1), "RGB").save(os.path.join(folder, f"fabric.{fid}.png"))
        entries.append({"id": fid, "name": _title(fn), "mean": round(float(tile.mean()), 4), "tileMetres": fabrics.TILE_METRES,
                        "tags": fabrics.TAGS.get(fid, ["casual"]), "printable": fid not in fabrics.NO_PRINT,
                        "printTags": fabrics.PRINT_TAGS.get(fid, []), "lift": fabrics.LIFT.get(fid, fabrics.DEFAULT_LIFT),
                        "colourChance": fabrics.NATURAL.get(fid, (0.0, []))[0], "colours": fabrics.NATURAL.get(fid, (0.0, []))[1],
                        "weight": fabrics.WEIGHT.get(fid, 1.0)})
    with open(os.path.join(folder, "library.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"fabrics": entries}, f, indent=1)
    print(f"[textures] {len(entries)} fabrics in {os.path.normpath(folder)}")


def write_prints():
    folder = os.path.join(CONTENT, "Patterns")
    os.makedirs(folder, exist_ok=True)
    entries = []
    for i, (pid, (fn, name, tags, scales, weight)) in enumerate(prints.PRINTS.items()):
        layers = fn(PRINT_SIZE, 2000 + i)
        colour, ink = layers[0], layers[1]
        accent = layers[2] if len(layers) > 2 else np.zeros_like(colour)
        rgb = np.stack([np.clip(colour, 0, 1), np.clip(ink, 0, 1), np.clip(accent, 0, 1)], axis=-1)
        Image.fromarray((rgb * 255).round().astype(np.uint8), "RGB").save(os.path.join(folder, f"pattern.{pid}_01.png"))
        tags = tags + ["knitted"] if pid in prints.KNITTED else tags
        entries.append({"id": f"pattern.{pid}_01", "name": name, "tags": tags, "minScale": scales[0], "maxScale": scales[1],
                        "weight": weight, "accent": prints.ACCENT.get(pid)})
    with open(os.path.join(folder, "library.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"patterns": entries}, f, indent=1)
    print(f"[textures] {len(entries)} prints in {os.path.normpath(folder)}")


def _title(fn):
    """A fabric's display name: its docstring's first words, up to the colon or full stop."""
    doc = (fn.__doc__ or fn.__name__).strip().split("\n")[0]
    for stop in (":", "."):
        doc = doc.split(stop)[0]
    return doc.split(" (")[0].strip()


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    if which in ("fabrics", "all"):
        write_fabrics()
    if which in ("prints", "all"):
        write_prints()
