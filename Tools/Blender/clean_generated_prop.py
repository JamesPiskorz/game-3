"""Cleans a generated mesh (Hunyuan3D 2.1 GLB) into a game prop: placed, decimated, flat palette colours, FBX + sidecar.

blender -b --factory-startup --python Tools/Blender/clean_generated_prop.py -- <input.glb> <output.fbx>
        --height <metres> --palette <location or RRGGBB,...> [--triangles 3000] [--regions 4]

Writes <output>.fbx and <output>.prop.json (source, generator, licence, colours) for Assets/_Game/Town/Content/Props.
Exits non-zero if validation fails, so it can gate content builds. See Docs/hunyuan3d.md for the whole workflow.
"""

import argparse
import os
import sys

import bpy
import numpy as np

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import prop_tools as pt  # noqa: E402


def arguments():
    parser = argparse.ArgumentParser(prog="clean_generated_prop.py", description=__doc__.splitlines()[0])
    parser.add_argument("input", help="generated mesh (.glb); its base colour texture, if any, supplies the colours")
    parser.add_argument("output", help="FBX to write, e.g. Assets/_Game/Town/Content/Props/cafe/armchair.fbx")
    parser.add_argument("--height", type=float, required=True, help="real height of the prop in metres")
    parser.add_argument("--palette", required=True, help="location in palettes.json (cafe, park, ...) or RRGGBB,RRGGBB,...")
    parser.add_argument("--triangles", type=int, default=3000, help="triangle budget (default 3000)")
    parser.add_argument("--regions", type=int, default=4, help="most colour regions to find in the texture (default 4)")
    parser.add_argument("--colour", help="palette colour (RRGGBB) for an untextured mesh; default the palette's first")
    parser.add_argument("--yaw", type=float, default=0.0, help="degrees to turn about up if the front isn't the front")
    parser.add_argument("--sharp-angle", type=float, default=40.0, help="edges sharper than this stay hard (degrees)")
    parser.add_argument("--min-island", type=float, default=0.002, help="drop floating pieces under this area fraction")
    parser.add_argument("--min-region", type=float, default=0.003, help="merge colour specks under this area fraction")
    parser.add_argument("--preview", help="also render front, three-quarter and side views to this PNG")
    parser.add_argument("--generator", default="Hunyuan3D 2.1 (tencent/Hunyuan3D-2.1)")
    parser.add_argument("--licence", default="Tencent Hunyuan 3D 2.1 Community License")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(argv)


def main():
    args = arguments()
    palette = pt.load_palette(args.palette)
    pt.reset_scene()
    obj = pt.import_glb(args.input)
    print(f"[prop] {args.input}: {pt.triangle_count(obj)} triangles")

    pt.place(obj, args.height, args.yaw)
    dropped = pt.clean(obj, weld_distance=args.height * 1e-6, min_island_fraction=args.min_island)
    print(f"[prop] placed at {args.height} m, dropped {dropped} floating pieces")

    image = pt.base_colour_image(obj)
    if image is not None:
        colours, areas, flipped = pt.textured_colours(obj, image)
        labels = pt.smooth_labels(obj, pt.palette_labels(colours, areas, palette, args.regions))
        print(f"[prop] colours from '{image.name}' ({image.size[0]}x{image.size[1]})"
              + (", read with V flipped (the texture was stored upside down)" if flipped else ""))
    else:
        colour = (args.colour or palette[0]).lstrip("#").upper()
        if colour not in palette:
            sys.exit(f"[prop] --colour {colour} is not in the palette {palette}")
        labels = np.full(len(obj.data.polygons), palette.index(colour))
        print(f"[prop] untextured: one colour, {colour}")

    source = obj.data.copy()
    pt.decimate(obj, args.triangles)
    pt.clean(obj, weld_distance=args.height * 1e-6, min_island_fraction=args.min_island)
    labels = pt.merge_specks(obj, pt.transfer_labels(obj, source, labels), args.min_region)
    pt.place(obj, args.height)  # decimation moves the extremes a little
    bpy.data.meshes.remove(source)
    used = pt.assign_palette_materials(obj, labels, palette)
    pt.shade(obj, args.sharp_angle)
    triangles = pt.triangle_count(obj)
    print(f"[prop] {triangles} triangles, colours {', '.join(used)}")

    problems = pt.validate_prop(obj, palette, args.height, args.triangles)
    for problem in problems:
        print("[prop] PROBLEM:", problem)
    if args.preview:
        pt.render_preview(obj, os.path.abspath(args.preview))
        print("[prop] preview", args.preview)
    if problems:
        sys.exit(1)

    name = os.path.splitext(os.path.basename(args.output))[0]
    obj.name = obj.data.name = name
    pt.export_fbx(args.output, [obj])
    pt.write_sidecar(os.path.splitext(args.output)[0] + ".prop.json", {
        "source": os.path.abspath(args.input),
        "generator": args.generator,
        "licence": args.licence,
        "palette": args.palette,
        "colours": used,
        "height": args.height,
        "triangles": triangles,
    })
    print("[prop] exported", args.output)


main()
