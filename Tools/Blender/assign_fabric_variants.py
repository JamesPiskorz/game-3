"""Gives every garment in Tools/Blender/outfits.json the other fabrics it can be made in ("fabrics"), by its own fabric
and its slot and tags (decision 0008: the generator makes about half of items in one of them, by style and occasion).
Run it after adding a fabric to Tools/Textures/fabrics.py: add the fabric to ALTERNATIVES (and to the filters below if it
doesn't suit some garments), then run Tools/Blender/update_sidecars.py to carry the lists into the parts' sidecars.

python Tools/Blender/assign_fabric_variants.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PATH = os.path.join(HERE, "outfits.json")
sys.path.insert(0, os.path.join(HERE, "..", "Textures"))
import fabrics  # noqa: E402

import json

def load():
    with open(PATH, encoding="utf-8") as f:
        return json.load(f)

def dumps(data):
    lines = ["{"]
    keys = list(data)
    for i, key in enumerate(keys):
        value = data[key]
        end = "," if i < len(keys) - 1 else ""
        if not isinstance(value, dict) or "garments" not in value:
            lines.append(f"  {json.dumps(key)}: {json.dumps(value, ensure_ascii=False)}{end}")
            continue
        lines.append(f"  {json.dumps(key)}: {{")
        inner = list(value)
        for j, k in enumerate(inner):
            e = "," if j < len(inner) - 1 else ""
            if k == "garments":
                lines.append(f"    \"garments\": [")
                for n, g in enumerate(value[k]):
                    lines.append("      " + json.dumps(g, ensure_ascii=False) + ("," if n < len(value[k]) - 1 else ""))
                lines.append("    ]" + e)
            else:
                lines.append(f"    {json.dumps(k)}: {json.dumps(value[k], ensure_ascii=False)}{e}")
        lines.append("  }" + end)
    lines.append("}")
    return "\n".join(lines) + "\n"

def save(data):
    with open(PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(dumps(data))


# Garments whose own fabric is better one of the new ones.
OWN = {
    "Pleated skirt": "pleated", "Tennis skirt": "pleated", "Track jacket": "nylon", "Track pants": "nylon",
    "Running shorts": "nylon",
}

# By the garment's own fabric: what else the same cut is made in.
ALTERNATIVES = {
    "jersey": ["marl", "slub_jersey", "rib_knit", "waffle", "pique", "fleece", "velvet", "pointelle"],
    "rib_knit": ["jersey", "marl", "wide_rib", "knit", "waffle", "crochet", "pointelle", "mohair", "lurex_knit"],
    "wide_rib": ["knit", "cable_knit", "chunky_knit", "rib_knit", "boucle", "mohair", "chenille", "seed_stitch", "lurex_knit"],
    "knit": ["cable_knit", "chunky_knit", "wide_rib", "boucle", "rib_knit", "crochet", "pointelle", "mohair", "seed_stitch", "chenille", "lurex_knit"],
    "fleece": ["marl", "jersey", "terry", "velvet", "waffle", "faux_fur", "sherpa", "chenille", "tricot"],
    "twill": ["denim", "black_denim", "corduroy", "needlecord", "linen", "canvas", "flannel", "tweed", "leather", "stonewash", "ottoman", "pinstripe_wool", "houndstooth_wool"],
    "denim": ["stonewash", "acid_wash", "black_denim", "ripped_denim", "twill", "corduroy", "canvas", "leather", "chambray", "patchwork"],
    "poplin": ["oxford", "chambray", "linen", "satin", "seersucker", "flannel", "lace", "dobby", "crinkle", "shantung", "satin_stripe", "sateen", "dotted_swiss"],
    "oxford": ["poplin", "chambray", "linen", "flannel", "seersucker", "dobby", "satin_stripe"],
    "linen": ["poplin", "gauze", "chambray", "seersucker", "canvas", "crochet", "crinkle", "smocked"],
    "satin": ["crepe", "velvet", "chiffon", "jacquard", "lace", "metallic", "shantung", "moire", "taffeta", "satin_stripe", "brocade", "sateen", "plisse", "crushed_velvet"],
    "crepe": ["satin", "velvet", "jersey", "lace", "guipure", "scuba", "plisse", "foil", "crushed_velvet", "sequins", "jacquard", "metallic", "taffeta", "beaded", "glitter", "ottoman", "vinyl"],
    "chiffon": ["satin", "crepe", "tulle", "lace", "gauze", "organza", "crinkle", "pleated", "dotted_swiss", "plisse"],
    "lycra": ["rib_lycra", "shimmer_lycra", "athletic_knit", "foil", "metallic", "crochet", "seersucker", "velvet", "mesh", "vinyl", "glitter", "smocked", "scuba"],
    "lace": ["satin", "mesh", "jersey", "velvet", "pointelle", "guipure"],
    "leather": ["suede", "patent", "croc", "canvas", "velvet", "snake", "vinyl", "perforated", "straw"],
    "patent": ["leather", "suede", "croc", "metallic", "velvet", "vinyl", "glitter", "snake"],
    "canvas": ["leather", "suede", "denim", "corduroy", "mesh", "straw", "perforated", "waxed"],
    "mesh": ["canvas", "leather", "ripstop", "knit", "perforated", "nylon"],
    "suede": ["leather", "velvet", "corduroy", "croc", "snake", "sherpa"],
    "rubber": ["patent", "vinyl"],
    "ripstop": ["quilted", "canvas", "satin", "fleece", "denim", "nylon", "waxed", "vinyl"],
    "nylon": ["tricot", "ripstop", "lycra", "athletic_knit", "satin", "velvet", "fleece", "mesh", "hex_mesh"],
    "quilted": ["diamond_quilt", "ripstop", "faux_fur", "corduroy", "nylon", "sherpa", "gloss_puffer"],
    "tweed": ["herringbone", "corduroy", "jumbo_cord", "velvet", "boucle", "twill", "denim", "leather", "ottoman", "felt", "pinstripe_wool", "houndstooth_wool"],
    "herringbone": ["tweed", "flannel", "twill", "corduroy", "velvet", "linen", "ottoman", "felt", "pinstripe_wool", "houndstooth_wool"],
    "velvet": ["satin", "jacquard", "crepe", "lace", "moire", "taffeta", "brocade", "crushed_velvet"],
    "sequins": ["metallic", "velvet", "satin", "lace", "glitter", "beaded", "foil", "crushed_velvet"],
    "tulle": ["satin", "chiffon", "lace", "organza", "taffeta", "glitter"],
    "eyelet": ["linen", "gauze", "poplin", "seersucker", "crochet", "smocked", "crinkle"],
    "gauze": ["linen", "crochet", "eyelet", "chiffon", "crinkle", "smocked"],
    "chambray": ["denim", "linen", "poplin", "oxford", "stonewash", "dobby", "patchwork"],
    "pique": ["jersey", "knit", "rib_knit", "waffle", "marl", "hex_mesh"],
    "flannel": ["jersey", "satin", "waffle", "fleece", "marl"],
    "plain": ["linen", "poplin", "eyelet", "seersucker", "jersey", "smocked", "crinkle", "pointelle", "patchwork", "dotted_swiss", "sateen"],
    "jacquard": ["satin", "velvet", "crepe", "lace", "moire", "shantung", "vinyl", "brocade"],
    "corduroy": ["denim", "tweed", "suede", "twill", "leather", "velvet", "patchwork", "needlecord", "jumbo_cord"],
    "metallic": ["leather", "patent", "satin", "glitter", "foil"],
    "pleated": ["twill", "chiffon", "satin", "crepe", "metallic"],
}
# Per slot, fabrics that don't suit it whatever the garment is made of.
NOT_FOR = {
    "shoes": {"jersey", "fleece", "satin", "chiffon", "lace", "faux_fur", "knit", "marl", "slub_jersey", "mohair",
              "pointelle", "seed_stitch", "chenille", "shantung", "moire", "taffeta", "organza", "pleated", "smocked",
              "crinkle", "dobby", "waxed", "stonewash", "acid_wash", "ottoman", "satin_stripe", "lurex_knit", "felt", "pinstripe_wool", "houndstooth_wool", "patchwork",
              "needlecord", "jumbo_cord", "guipure", "scuba", "gloss_puffer", "dotted_swiss", "sateen",
              "black_denim", "ripped_denim", "athletic_knit", "plisse", "crushed_velvet", "foil",
              "rib_lycra", "shimmer_lycra", "hex_mesh", "tricot"},
    "legwear": set(),
    "outer": {"chiffon", "lace", "lycra", "tulle", "organza", "pleated", "smocked", "crinkle", "pointelle", "guipure", "scuba", "dotted_swiss"},
    "top": {"pleated", "straw", "perforated"},
    "bottom": {"smocked", "straw", "perforated", "pointelle"},
    "onepiece": {"straw", "perforated"},
}
SWIM = {"metallic", "crochet", "seersucker", "velvet", "mesh", "satin", "glitter", "smocked", "crinkle", "scuba", "foil",
        "rib_lycra", "shimmer_lycra"}
SLEEP = {"satin", "jersey", "waffle", "flannel", "lace", "velvet", "mesh", "chiffon", "fleece", "terry", "poplin", "linen",
         "pointelle", "marl", "slub_jersey", "sherpa", "chenille", "crinkle", "smocked", "shantung", "seersucker", "satin_stripe", "sateen", "dotted_swiss"}

o = load()
counts, moved = [], []
for k, v in o.items():
    if k.startswith("_"):
        continue
    for g in v["garments"]:
        if g["name"] in OWN and g.get("fabric") != OWN[g["name"]]:
            moved.append(f'{g["name"]}: {g.get("fabric")} -> {OWN[g["name"]]}')
            g["fabric"] = OWN[g["name"]]
        own = g.get("fabric")
        if g["slot"] == "legwear":
            # Tights and stockings come in fishnet or lace too, socks in a plain knit or fishnet.
            options = {"chiffon": ["fishnet", "lace", "lycra"], "rib_knit": ["knit", "jersey", "fishnet"]}.get(own, [])
            g["fabrics"] = options
            counts.append(len(options))
            continue
        if own is None:
            g.pop("fabrics", None)
            continue
        options = [f for f in ALTERNATIVES.get(own, []) if f not in NOT_FOR.get(g["slot"], set()) and f != own]
        # Swimwear and lingerie stay swim- and sleep-like: no tweed bikinis.
        if "swim" in g.get("tags", []):
            options = [f for f in options if f in SWIM]
        if "sleep" in g.get("tags", []):
            options = [f for f in options if f in SLEEP]
        # Sporty leggings and shorts aren't vinyl; gowns and party dresses are where the sparkle goes.
        if "sporty" in g.get("tags", []) and g["slot"] in ("bottom", "top"):
            options = [f for f in options if f not in {"vinyl", "glitter", "beaded", "foil"}]
        # Rips are for jeans and denim skirts and shorts, not a denim jacket's or a dress's.
        if g["slot"] != "bottom":
            options = [f for f in options if f != "ripped_denim"]
        # Straw for sandals, espadrille-like canvas shoes, loafers and strappy heels, not boots, trainers or a jacket.
        name = g["name"].lower()
        if g["slot"] != "shoes" or any(w in name for w in ("boot", "derby", "chunky", "pumps", "high-top", "tennis", "running", "trail", "sneakers")):
            options = [f for f in options if f != "straw"]
        # Crochet is for swimwear, tops and dresses, not activewear.
        if "sporty" in g.get("tags", []) and "swim" not in g.get("tags", []):
            options = [f for f in options if f != "crochet"]
        # Mesh for activewear and lingerie, not everyday trousers; faux fur for tops and coats, not trousers.
        if g["slot"] == "bottom" and not ({"sporty", "sleep", "swim"} & set(g.get("tags", []))):
            options = [f for f in options if f not in ("mesh", "hex_mesh")]
        if g["slot"] == "bottom":
            options = [f for f in options if f != "faux_fur"]
        g["fabrics"] = options
        counts.append(len(options))
save(o)
print("\n".join(moved))
print(len(counts), "garments with alternatives; mean", round(sum(counts) / len(counts), 2))
used = {f for v in o.values() if isinstance(v, dict) and "garments" in v for g in v["garments"] for f in [g.get("fabric")] + g.get("fabrics", [])}
print("fabrics never worn:", sorted(set(fabrics.FABRICS) - used))
