"""Rewrites the catalog fields of existing generated parts' sidecars from Tools/Blender/outfits.json and hairs.json,
without rebuilding the parts (decision 0008): name, tags, lean, contexts and weight, as make_generated_outfit.py and
make_generated_hair.py write them. Run it after retuning those fields, then write the generated content in Unity
(Game > Characters > Content > Write Generated Content).

python Tools/Blender/update_sidecars.py [<generated parts folder>]
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
FOLDER = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "Assets", "_Game", "Characters", "Content", "Imported", "Generated")


def specs():
    """Each part id's catalog fields, from both spec files."""
    fields = {}
    with open(os.path.join(HERE, "outfits.json"), encoding="utf-8") as f:
        for key, outfit in json.load(f).items():
            if key.startswith("_"):
                continue
            for spec in outfit["garments"]:
                entry = {"name": spec.get("name"), "tags": spec.get("tags", ["casual"]), "lean": spec.get("lean", 0.0),
                         "contexts": spec.get("contexts", []), "weight": spec.get("weight", 1.0)}
                for spec_key, field in (("blocks_slots", "blocksSlots"), ("excludes_tags", "excludesTags"), ("requires_tags", "requiresTags")):
                    if spec.get(spec_key):
                        entry[field] = spec[spec_key]
                fields[spec["id"]] = entry
    with open(os.path.join(HERE, "hairs.json"), encoding="utf-8") as f:
        for key, style in json.load(f).items():
            if key.startswith("_"):
                continue
            for which in ("front", "back"):
                spec = style[which]
                fields[spec["id"]] = {"name": spec.get("name"), "tags": spec["tags"], "lean": spec.get("lean", 0.0),
                                      "weight": spec.get("weight", 1.0)}
    return fields


def main():
    updated = 0
    for part, entry in specs().items():
        path = os.path.join(FOLDER, part + ".character.json")
        if not os.path.exists(path):
            print(f"[sidecars] {part}: not built yet")
            continue
        with open(path, encoding="utf-8") as f:
            sidecar = json.load(f)
        sidecar.update(entry)
        with open(path, "w", encoding="utf-8") as f:  # as character_tools.write_sidecar writes them
            json.dump(sidecar, f, indent=2)
        updated += 1
    print(f"[sidecars] updated {updated} sidecars")


main()
