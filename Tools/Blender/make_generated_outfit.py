"""Makes game parts from a Hunyuan3D scan of a dressed character (decision 0008).

blender -b --factory-startup --python Tools/Blender/make_generated_outfit.py -- <outfit key> <scan_textured.glb>
        <canonical body .fbx> <output folder> [--preview <png>]

<outfit key> names an entry in Tools/Blender/outfits.json (its garments, slots, regions and colours). For each garment
it writes <id>.fbx and <id>.character.json for CharacterModelImporter: a part on the canonical skeleton with a recolour
mask from the generated texture's colour blocks (and those colours in the sidecar). Fitted garments, shoes included,
are the canonical body's own surface where the scan shows the garment, lifted to the slot's layer offset or the scan's
looseness, carrying the body's weights and shapes. Loose layers are the scan's geometry conformed to the body and
smoothed (optionally draped by cloth simulation), skinned and given every body shape by transfer. The preview renders
them in the game's everyday pose: front, back, side and the masculine shape. Exits non-zero if validation fails.
"""

import argparse
import os
import sys

import bpy
import numpy as np

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import body_tools as bt  # noqa: E402
import character_tools as ct  # noqa: E402
import garment_tools as gt  # noqa: E402
import prop_tools as pt  # noqa: E402

BUDGET = {"top": 6000, "bottom": 5000, "shoes": 3000, "outer": 8000, "onepiece": 8000, "legwear": 3000}
# Metres a fitted garment may stand off the body (6 cm otherwise): shoes hug the foot, and bottoms sit close to the
# hips and thighs (with the scan's full slack, loose shorts flared into a bell over a wide-hipped body). A garment's
# "loosest" in outfits.json overrides its slot's (straight-leg trousers hang looser than shorts).
LOOSEST = {"shoes": 0.025, "bottom": 0.01}
SCAN_HEIGHT = 1.70
OVERLAP = 0.015  # metres a top reaches down over its outfit's bottom (it hid the waistband at 3 cm)


def arguments():
    parser = argparse.ArgumentParser(prog="make_generated_outfit.py", description=__doc__.splitlines()[0])
    parser.add_argument("outfit", help="key in Tools/Blender/outfits.json, e.g. outfit-01")
    parser.add_argument("scan", help="the outfit scan's textured mesh (.glb)")
    parser.add_argument("body", help="the canonical body FBX from make_generated_body.py (with its .skeleton.json)")
    parser.add_argument("output", help="folder, e.g. Assets/_Game/Characters/Content/Imported/Generated")
    parser.add_argument("--preview", help="also render the parts on the body to this PNG")
    parser.add_argument("--settle", action="store_true",
                        help="also drape the loose layers by cloth simulation: only for garments whose scan "
                             "doesn't hang right, since on a T-posed body it buckles fabric into folds")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(argv)


def srgb_hex(linear):
    c = np.clip(np.asarray(linear, dtype=float), 0.0, 1.0)
    c = np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)
    return "#" + "".join(f"{int(round(v * 255)):02x}" for v in c)


def planar_uvs(part):
    """UV0 in metres, projected from the front (patterns tile in metres; a proper unwrap can come later)."""
    mesh = part.data
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    loop_vertex = np.empty(len(mesh.loops), dtype=np.int64)
    mesh.loops.foreach_get("vertex_index", loop_vertex)
    co = bt.vertices(part)
    mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", co[loop_vertex][:, [0, 2]].ravel())
    mesh.uv_layers.new(name="Region4")  # recolour region 4 stays empty


def main():
    args = arguments()
    outfit = gt.load_outfit(args.outfit)
    ct.reset_scene()
    body, skeleton, joints = gt.load_body(args.body)
    regions_of_body = gt.body_regions(body)

    scan = gt.load_scan(args.scan, SCAN_HEIGHT)
    labels, mean_colour, areas = gt.colour_clusters(scan)
    lit_colour = gt.lit_colours(scan, labels, areas)
    image = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", outfit["image"])
    if os.path.exists(image):  # the source image's own colours, which the texture darkens
        lit_colour = gt.image_colours(scan, labels, os.path.abspath(image), lit_colour)
    gt.tag_clusters(scan, labels)
    # Outfit images come from the base's prompt and seed, so the dressed body has the canonical body's proportions;
    # its own crotch is hidden by the garments (loose shorts read as hips 10 cm low, stretching them up the torso), and
    # an oversized hoodie's sleeves hide its armpits.
    scan_joints, _ = bt.fit_body_joints(scan, crotch=float(joints["UpperLeg.L"][2]) - 0.06, shoulder=joints["UpperArm.L"])
    moves = {n: float(np.linalg.norm(joints[n] - scan_joints[n])) for n in scan_joints}
    moved = max(moves.values())
    print("[outfit] joint moves (cm): " + ", ".join(f"{n} {d * 100:.0f}" for n, d in sorted(moves.items(), key=lambda m: -m[1])[:6]))
    bt.align_to_skeleton(scan, scan_joints, joints)
    print(f"[outfit] {args.outfit}: scan aligned to the canonical skeleton (joints moved up to {moved * 100:.1f} cm)")

    regions = gt.face_regions(scan, body, regions_of_body)
    garment, _, skin = gt.assign_garments(outfit, labels, mean_colour, areas, regions, gt.skin_faces(scan))
    lab = pt.linear_to_oklab(mean_colour)
    garment = gt.fill_holes(scan, garment, areas)
    garment = gt.drop_islands(scan, garment, areas)

    problems, made = [], []
    for index, spec in enumerate(outfit["garments"]):
        slot, offset = spec["slot"], gt.LAYER_OFFSET[spec["slot"]]
        if not (garment == index).any():
            problems.append(f"{spec['id']}: no scan faces were assigned to it")
            continue
        pinned = None
        if slot in gt.FITTED:
            # Built from the body: its surface, weights and shapes, lifted by the offset or the scan's looseness.
            always = np.isin(regions_of_body, ["FootL", "FootR"]) if slot == "shoes" else None
            part, _ = gt.project_garment(body, scan, garment, labels, index, offset, spec["id"], joints, always,
                                         max_loose=spec.get("loosest", LOOSEST.get(slot, 0.06)),
                                         edge_blur=spec.get("edge_blur"), drape=slot in ("top", "onepiece"),
                                         extend_down=OVERLAP if slot == "top" and any(g["slot"] == "bottom" for g in outfit["garments"]) else 0.0)
            hood = gt.extract_hood(scan, garment, index, body, joints, areas, spec["id"]) if spec.get("hood") else None
            if hood is not None:
                gt.join_into(part, hood)
            # No decimation: it keeps the body's density, which is what makes it deform exactly like the skin under
            # it (and Blender can't decimate a mesh with shape keys). The validator still caps it at 12k triangles.
        else:
            # Loose layers keep the scan's own shape, conformed to the body.
            part = gt.extract(scan, garment, index, spec["id"])
            gt.vertex_clusters(part)
            gt.conform(part, body, offset)
            gt.smooth_outside(part, body, offset)
            pinned = gt.settle(part, body, offset) if args.settle else None
            pt.decimate(part, BUDGET[slot])
        pt.shade(part, 55.0)
        cluster = gt.read_vertex_clusters(part)
        palette = gt.palette_clusters(outfit, lab, index, skin)
        own = {k: float(areas[(garment == index) & (labels == k)].sum()) for k in set(cluster.tolist()) if k in palette}
        recolour = len(region_clusters := gt.recolour_mask(part, cluster, own, lab, sole=slot == "shoes"))
        gt.limit_influences(part)  # vertices made by the cuts blend two vertices' weights
        planar_uvs(part)
        if slot not in gt.FITTED:
            ct.transfer_from_body(part, body, bones=gt.FOLLOWS[slot])
            gt.limit_influences(part)
        for old in [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]:
            bpy.data.objects.remove(old)
        rig = ct.build_armature("Armature", skeleton)
        ct.bind_to_rig(part, rig)
        covers = gt.covered_regions(part, body, regions_of_body, spec["regions"])
        # Fitted garments keep the body's density (they can't be decimated with shape keys); budgets are set aside
        # for now (decision 0008), so they get room for a hoodie that covers most of the body.
        found = ct.validate_part(part, rig, body, max_triangles=20000 if slot in gt.FITTED else 12000)
        problems += found
        print(f"[outfit] {spec['id']}: {pt.triangle_count(part)} triangles, {recolour} recolour regions, "
              + (f"settled with {pinned:.0%} pinned, " if pinned is not None else "") + f"covers {covers or 'nothing'}" + (f", {len(found)} problems" if found else ""))
        if not found:
            path = os.path.join(args.output, spec["id"] + ".fbx")
            ct.export_fbx(path, [rig, part])
            ct.write_sidecar(os.path.join(args.output, spec["id"] + ".character.json"), {
                "kind": "part", "slot": slot, "covers": covers, "followsBodyShapes": True, "regions": recolour, "outfit": args.outfit,
                # The generated texture's colour for each recolour region where lit (sRGB): how the image dressed it.
                "colours": [srgb_hex(lit_colour[k]) for k in region_clusters],
                "source": os.path.abspath(args.scan), "generator": "Hunyuan3D 2.1 (tencent/Hunyuan3D-2.1)",
                "licence": "Tencent Hunyuan 3D 2.1 Community License",
            })
            made.append(part)
        part.hide_render = False

    for problem in problems:
        print("[outfit] PROBLEM:", problem)
    if args.preview:
        scan.hide_render = True
        bt_preview(body, made, joints, os.path.abspath(args.preview))
        print("[outfit] preview", args.preview)
    if problems:
        sys.exit(1)
    print(f"[outfit] exported {len(made)} parts to {args.output}")


def bt_preview(body, parts, joints, path):
    """
    Workbench renders of the parts on the canonical body in the game's everyday pose (body_tools.RELAXED, which
    they're fitted in): front, back and side, then front in the masculine shape.
    """
    import math
    scene = bpy.context.scene
    scene.world = scene.world or bpy.data.worlds.new("Preview")
    scene.world.color = (0.18, 0.18, 0.2)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light, shading.color_type, shading.show_object_outline = "STUDIO", "OBJECT", True
    camera = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 1.95
    scene.render.resolution_x, scene.render.resolution_y = 800, 900
    base = path[:-4] if path.lower().endswith(".png") else path
    skinning = bt.pose_skinning(joints)
    colours = [(0.8, 0.62, 0.55)] + [(0.3, 0.45, 0.85), (0.35, 0.7, 0.35), (0.9, 0.75, 0.2), (0.75, 0.3, 0.75)][:len(parts)]
    for obj in [body, *parts]:
        obj.hide_render = True

    def posed(obj, colour, masculine):
        keys = obj.data.shape_keys
        rest = gt._key_positions(keys.key_blocks["body.masculine"]) if masculine and keys and "body.masculine" in keys.key_blocks else bt.vertices(obj)
        weights, names = bt.weight_matrix(obj)
        copy = obj.copy()
        copy.data = obj.data.copy()
        copy.parent = None
        copy.modifiers.clear()
        scene.collection.objects.link(copy)
        if copy.data.shape_keys:
            copy.shape_key_clear()
        copy.data.vertices.foreach_set("co", bt.skin_positions(rest, weights, names, skinning).ravel())
        copy.data.update()
        copy.color, copy.hide_render = (*colour, 1.0), False
        return copy

    views = (("front", (0, -6, 0.85), (90, 0, 0)), ("back", (0, 6, 0.85), (90, 0, 180)), ("side", (6, 0, 0.85), (90, 0, 90)))
    for masculine, shots in ((False, views), (True, views[:1])):
        copies = [posed(obj, colour, masculine) for obj, colour in zip([body, *parts], colours)]
        for name, location, rotation in shots:
            camera.location, camera.rotation_euler = location, tuple(math.radians(a) for a in rotation)
            scene.render.filepath = f"{base}_{'masculine' if masculine else name}.png"
            bpy.ops.render.render(write_still=True)
        for copy in copies:
            bpy.data.objects.remove(copy)


main()
