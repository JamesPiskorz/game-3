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
# Metres a fitted garment may stand off the body (6 cm otherwise): shoes as chunky as the scan's, up to 4.5 cm (at
# 2.5 cm they hugged the foot like socks), bottoms sit close to the
# hips and thighs (with the scan's full slack, loose shorts flared into a bell over a wide-hipped body), and legwear
# hugs the skin (tights filled in under a skirt took the skirt's slack and came out through it). A garment's
# "loosest" in outfits.json overrides its slot's (straight-leg trousers hang looser than shorts).
LOOSEST = {"shoes": 0.045, "bottom": 0.01, "legwear": 0.004}
SCAN_HEIGHT = 1.70
OVERLAP = 0.006  # metres a top reaches down over its outfit's bottom: just over the top of a waistband


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


def catalog_fields(spec):
    """The catalog fields a part's sidecar carries from its spec (outfits.json or hairs.json): display name, style tags,
    lean (-1 masculine .. +1 feminine), the outfit contexts it may appear in, a weight and the slots it blocks.
    update_sidecars.py rewrites them in existing sidecars without rebuilding the parts."""
    fields = {"name": spec.get("name"), "tags": spec.get("tags", ["casual"]), "lean": spec.get("lean", 0.0),
              "contexts": spec.get("contexts", []), "weight": spec.get("weight", 1.0)}
    if spec.get("blocks_slots"):
        fields["blocksSlots"] = spec["blocks_slots"]
    if spec.get("excludes_tags"):  # no other worn part may carry these (a coat over a hood)
        fields["excludesTags"] = spec["excludes_tags"]
    if spec.get("requires_tags"):
        fields["requiresTags"] = spec["requires_tags"]
    return fields


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
    # Recolour region 4 (x) and the drawn-over flag (y) stay empty. A new UV layer copies the active one, and the
    # planar UVs there made region 4 a white wash over half of every garment and lifted them towards the camera.
    region4 = mesh.uv_layers.new(name="Region4")
    region4.data.foreach_set("uv", np.zeros(len(mesh.loops) * 2))


def main():
    args = arguments()
    outfit = gt.load_outfit(args.outfit)
    ct.reset_scene()
    body, skeleton, joints = gt.load_body(args.body)
    regions_of_body = gt.body_regions(body)

    scan = gt.load_scan(args.scan, SCAN_HEIGHT)
    labels, mean_colour, areas = gt.colour_clusters(scan)
    lit_colour = gt.lit_colours(scan, labels, areas)
    def region_colour(k, spec):
        """A region's colour: the image's, or where the image couldn't be read for it (a thin sole, a trim), the
        prompt's colour for the garment nearest the texture's (Hunyuan painted the red soles magenta)."""
        if gt.TRUSTED is None or k in gt.TRUSTED or not spec.get("colours"):
            return srgb_hex(lit_colour[k])
        hints = [pt.linear_to_oklab(pt.srgb_to_linear(pt.hex_to_srgb(h.lstrip("#")))) for h in spec["colours"]]

        def by_hue(h):  # hue and chroma first: the texture's darks are darker than the prompt's (brown boots read black)
            d = lab[k] - h
            return float((0.15 * d[0]) ** 2 + d[1] ** 2 + d[2] ** 2)

        nearest = min(hints, key=by_hue)
        return srgb_hex(pt.oklab_to_linear(nearest)) if by_hue(nearest) < 0.1 ** 2 else srgb_hex(lit_colour[k])

    image = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", outfit["image"])
    if os.path.exists(image):  # the source image's own colours, which the texture darkens
        lit_colour = gt.image_colours(scan, labels, os.path.abspath(image), lit_colour)
    gt.tag_clusters(scan, labels)
    # Outfit images come from the base's prompt and seed, so the dressed body has the canonical body's proportions;
    # its own crotch is hidden by the garments (loose shorts read as hips 10 cm low, stretching them up the torso), and
    # an oversized hoodie's sleeves hide its armpits.
    scan_joints, _ = bt.fit_body_joints(scan, crotch=float(joints["UpperLeg.L"][2]) - 0.06, shoulder=joints["UpperArm.L"])
    if outfit.get("legs_hidden"):
        # A floor-length gown hides the legs, so the joints found for them are guesses (feet 20 cm out), and their bones
        # dragged the hem in. The legs take the canonical joints, moved with the hips: the skirt moves with the hips.
        shift = scan_joints["Hips"] - joints["Hips"]
        for n in [n for n in scan_joints if n.startswith(("UpperLeg", "LowerLeg", "Foot", "Toes"))]:
            scan_joints[n] = joints[n] + shift
    moves = {n: float(np.linalg.norm(joints[n] - scan_joints[n])) for n in scan_joints}
    moved = max(moves.values())
    print("[outfit] joint moves (cm): " + ", ".join(f"{n} {d * 100:.0f}" for n, d in sorted(moves.items(), key=lambda m: -m[1])[:6]))
    bt.align_to_skeleton(scan, scan_joints, joints)
    print(f"[outfit] {args.outfit}: scan aligned to the canonical skeleton (joints moved up to {moved * 100:.1f} cm)")

    # An outfit generated on the masculine base is measured against the body's masculine shape.
    keys = body.data.shape_keys.key_blocks if body.data.shape_keys else {}
    masculine_body = gt._key_positions(keys["body.masculine"]) if outfit.get("body") == "masculine" and "body.masculine" in keys else None
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
        fitted = slot in gt.FITTED or spec.get("fitted", False)  # a short jacket can be built like a top, a layer out
        skirt_spec = spec.get("skirt")
        if skirt_spec is not None and slot == "bottom":
            # A skirt on its own: one tube from the waist to the scan's hem (gt.build_skirt).
            part = gt.build_skirt(body, scan, garment, index, joints, spec["id"], hem=skirt_spec.get("hem"),
                                  offset=skirt_spec.get("offset", 0.012), legs=skirt_spec.get("legs", 0.5),
                                  pleats=skirt_spec.get("pleats", 0), pleat_depth=skirt_spec.get("pleat_depth", 0.008),
                                  flare=skirt_spec.get("flare", False), smooth=skirt_spec.get("smooth", 0),
                                  straight=skirt_spec.get("straight", False), widest=skirt_spec.get("widest"))
            if part is None:
                problems.append(f"{spec['id']}: the scan shows no skirt")
                continue
            part.name = part.data.name = spec["id"]
        elif fitted:
            # Built from the body: its surface, weights and shapes, lifted by the offset or the scan's looseness.
            # Shoes enclose the whole foot, and tights ("fill") the regions they list, whatever the scan shows: under a
            # skirt the scan shows the skirt.
            filled = (["FootL", "FootR"] if slot == "shoes" else []) + spec.get("fill", [])
            always = np.isin(regions_of_body, filled) if filled else None
            part, _ = gt.project_garment(body, scan, garment, labels, index, offset, spec["id"], joints, always,
                                         max_loose=spec.get("loosest", LOOSEST.get(slot, 0.06)),
                                         edge_blur=spec.get("edge_blur"), drape=spec.get("drape", slot in ("top", "outer")),
                                         ease=spec.get("ease", 0.0), straps=spec.get("straps", 0.0),
                                         scoop=spec.get("scoop", 0.0), gather=spec.get("gather", 0.0),
                                         sleeve_ease=spec.get("sleeve_ease", 0.0), flare=tuple(spec.get("flare", (0.0, 0.0))),
                                         top=spec.get("top"), neck=spec.get("neck"), hem=spec.get("hem"),
                                         # "length": a level hem at that height above the hip joint, not tucked in (a jacket)
                                         tucked=spec.get("tucked", spec.get("length")), tuck_in=spec.get("tucked") is not None,
                                         extend_down=spec.get("overlap", OVERLAP) if slot == "top" and any(g["slot"] == "bottom" for g in outfit["garments"]) else 0.0,
                                         # one waist line for every outfit, so any top meets any bottom (gt.WAIST_ABOVE_HIPS)
                                         # (not for swimwear, lingerie or crop tops: "waist": false)
                                         waist=None if spec.get("waist", True) is False else
                                               "bottom" if slot == "bottom" else "top" if slot == "top" and spec.get("tucked") is None else None,
                                         measure_from=masculine_body if outfit.get("body") == "masculine" else None,
                                         ring=spec.get("ring"), halter=spec.get("halter", False),
                                         one_shoulder=spec.get("one_shoulder"))

            if slot == "shoes":
                gt.flare_sole(part)
                gt._push_out(part, body, offset)  # the toe box mustn't dip into the toes
                gt.level_soles(part)
                gt.round_shoe(part)  # over the lumps the toes pushed up
                if spec.get("top") is not None and np.max(spec["top"]) > 0.2:  # a boot's shaft: a smooth tube
                    print(f"[outfit] {spec['id']}: shaft wrapped (up to {gt.round_shaft(part) * 1000:.0f} mm)")
            if skirt_spec is not None:  # a dress: the fitted bodice, and a skirt from the waist down (gt.build_skirt)
                skirt = gt.build_skirt(body, scan, garment, index, joints, spec["id"], hem=skirt_spec.get("hem"),
                                       offset=skirt_spec.get("offset", 0.012), legs=skirt_spec.get("legs", 0.5),
                                  pleats=skirt_spec.get("pleats", 0), pleat_depth=skirt_spec.get("pleat_depth", 0.008),
                                  flare=skirt_spec.get("flare", False), smooth=skirt_spec.get("smooth", 0),
                                  straight=skirt_spec.get("straight", False), widest=skirt_spec.get("widest"))
                if skirt is None:
                    problems.append(f"{spec['id']}: the scan shows no skirt")
                else:
                    gt.join_into(part, skirt)
            hood = gt.extract_hood(scan, garment, index, body, joints, areas, spec["id"]) if spec.get("hood") else None
            if hood is not None:
                if spec.get("hood_rise"):  # standing up round the neck, as the image's
                    gt.stand_hood(hood, joints, rise=spec["hood_rise"])
                    gt._push_out(hood, body, gt.LAYER_OFFSET["outer"])
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
        if part.data.has_custom_normals:  # a fitted garment copied the body's (face-shading) normals: its own instead
            bpy.ops.object.select_all(action="DESELECT")
            part.select_set(True)
            bpy.context.view_layer.objects.active = part
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
        cluster = gt.read_vertex_clusters(part)
        palette = gt.palette_clusters(outfit, lab, index, skin)
        own = {k: float(areas[(garment == index) & (labels == k)].sum()) for k in set(cluster.tolist()) if k in palette}
        # One colour in the prompt, one region (unless "solid": false): the rest is shading and stray patches. Shoes
        # keep their soles' region.
        # A belt is drawn (spec "band"), not taken from the scan: its colour in "colours" is no block of its own
        # (taken as one, the slim trousers' shading came out in belt-coloured patches down the legs).
        solid = spec.get("solid", len(spec.get("colours", [])) - (1 if spec.get("band") else 0) <= 1 and slot != "shoes")
        recolour = len(region_clusters := gt.recolour_mask(part, cluster, own, lab, sole=slot == "shoes", most=1 if solid else 3))
        band_colour = None
        if spec.get("band") and recolour < 3:  # a belt or waistband the scan has no colour block for
            gt.add_band(part, spec["band"]["width"], recolour)
            band_colour = spec["band"]["colour"]
            recolour += 1
            if spec["band"].get("buckle") and recolour < 3:  # a buckle on the belt, in its own colour
                print(f"[outfit] {spec['id']}: buckle on {gt.draw_buckle(part, recolour, below=0.0)} faces")
                band_colour = [band_colour, spec["band"]["buckle"]]
                recolour += 1
        if spec.get("sole") and recolour < 3:  # a thin sole the scan has no colour block for
            print(f"[outfit] {spec['id']}: sole on {gt.paint_sole(part, recolour, spec['sole'].get('height', 0.016))} faces")
            band_colour = spec["sole"]["colour"]
            recolour += 1
        if spec.get("laces") and recolour < 3:  # a sneaker's laces, drawn as a few bars across the instep, and its toe cap
            print(f"[outfit] {spec['id']}: laces on {gt.lace_lines(part, recolour, lines=spec['laces'].get('lines', 4))} faces, "
                  f"toe caps on {gt.draw_toe_caps(part, recolour)}")
            band_colour = spec["laces"]["colour"]
            recolour += 1
        if spec.get("cords") and recolour < 3:  # a hoodie's drawstrings, drawn down its front
            print(f"[outfit] {spec['id']}: cords on {gt.draw_cords(part, recolour)} faces")
            band_colour = spec["cords"]["colour"]
            recolour += 1
        if spec.get("fly") and recolour > 1:  # a fly and front pockets in the waistband's colour, as the target draws them
            print(f"[outfit] {spec['id']}: fly on {gt.draw_fly(part, 1)} faces, pockets on {gt.draw_pockets(part, 1)}")
        if spec.get("cuff") and recolour < 3:  # turned-up cuffs, a shade off the cloth, as the target draws them
            gt.add_band(part, spec["cuff"]["width"], recolour, edge="bottom")
            band_colour = spec["cuff"]["colour"]
            recolour += 1
        if spec.get("collar") and (trim := gt.trim_region(part)) is not None:
            # An even band round the top edge in the edge trims' colour: a crew neck's ribbed collar like its cuffs
            # (the scan's collar climbed the neck; the shirt now stops at its base), a tank top's black edging.
            print(f"[outfit] {spec['id']}: collar in region {trim}, {gt.add_band(part, spec['collar'], trim)} faces")
            if spec.get("straps"):  # straps in the edging's colour, as the image's
                print(f"[outfit] {spec['id']}: straps painted on {gt.paint_straps(part, trim, joints)} faces")
            if spec.get("neck"):  # a crew neck's collar is a ring round the neck: shaded as one
                print(f"[outfit] {spec['id']}: collar normals on {gt.collar_normals(part, trim, joints)} vertices")
        gt.limit_influences(part)  # vertices made by the cuts blend two vertices' weights
        if fitted or skirt_spec is not None:
            print(f"[outfit] {spec['id']}: {gt.keep_shapes_clear(part, body)} vertices kept clear of the body's shapes")
        planar_uvs(part)
        if not fitted and skirt_spec is None:
            ct.transfer_from_body(part, body, bones=gt.FOLLOWS[slot])
            gt.limit_influences(part)
        for old in [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]:
            bpy.data.objects.remove(old)
        rig = ct.build_armature("Armature", skeleton)
        ct.bind_to_rig(part, rig)
        # A gown claims the feet only for its hem's scan faces (they lie nearest the feet): it never hides them.
        hides = [r for r in spec["regions"] if slot in ("shoes", "legwear") or r not in ("FootL", "FootR")]
        covers = gt.covered_regions(part, body, regions_of_body, hides,
                                    skirt_axis=joints["Hips"] if skirt_spec is not None else None)
        # Fitted garments keep the body's density (they can't be decimated with shape keys); budgets are set aside
        # for now (decision 0008), so they get room for a hoodie that covers most of the body.
        # A long skirt hangs far from the bones it follows (a gown's hem is a metre below the hips).
        found = ct.validate_part(part, rig, body, max_triangles=20000 if fitted or skirt_spec is not None else 12000,
                                 max_bone_distance=1.3 if skirt_spec is not None else ct.MAX_BONE_DISTANCE)
        problems += found
        print(f"[outfit] {spec['id']}: {pt.triangle_count(part)} triangles, {recolour} recolour regions, "
              + (f"settled with {pinned:.0%} pinned, " if pinned is not None else "") + f"covers {covers or 'nothing'}" + (f", {len(found)} problems" if found else ""))
        if not found:
            path = os.path.join(args.output, spec["id"] + ".fbx")
            ct.export_fbx(path, [rig, part])
            ct.write_sidecar(os.path.join(args.output, spec["id"] + ".character.json"), {
                "kind": "part", "slot": slot, "covers": covers, "followsBodyShapes": True, "regions": recolour, "outfit": args.outfit,
                # The generated texture's colour for each recolour region where lit (sRGB): how the image dressed it.
                # "main" overrides the main region's colour where the image's lighting shifted its hue (khaki read red).
                "colours": ([spec["main"]] + [region_colour(k, spec) for k in region_clusters[1:]] if spec.get("main") else
                            [region_colour(k, spec) for k in region_clusters])
                           + (band_colour if isinstance(band_colour, list) else [band_colour] if band_colour else []),
                # How the game's generator treats it (the catalog entry the content baker makes from this).
                **catalog_fields(spec),
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
