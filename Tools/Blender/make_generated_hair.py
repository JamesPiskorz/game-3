"""Makes hair parts from a Hunyuan3D scan of a character wearing a hairstyle (decision 0008, step 3).

blender -b --factory-startup --python Tools/Blender/make_generated_hair.py -- <hair key> <scan_textured.glb>
        <canonical body .fbx> <output folder> [--preview <png>]

<hair key> names an entry in Tools/Blender/hairs.json (prompt, hair colour, the two pieces' ids and tags, spring
chains). It writes a front piece (fringe and the locks in front of the ears) and a back piece as <id>.fbx and
<id>.character.json for CharacterModelImporter: on the canonical skeleton plus any chain bones, weighted to the head
(fading to the chest below the shoulders), following every body shape, with the hair recolour mask (base to tip down
the length, highlight ring round the crown). Exits non-zero if validation fails.
"""

import argparse
import math
import os
import sys

import bmesh
import bpy
import numpy as np

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import body_tools as bt  # noqa: E402
import character_tools as ct  # noqa: E402
import garment_tools as gt  # noqa: E402
import hair_tools as ht  # noqa: E402
import prop_tools as pt  # noqa: E402

BUDGET = 10000  # triangles for the whole style, before the split


def arguments():
    parser = argparse.ArgumentParser(prog="make_generated_hair.py", description=__doc__.splitlines()[0])
    parser.add_argument("hair", help="key in Tools/Blender/hairs.json, e.g. hair-bob")
    parser.add_argument("scan", help="the hairstyle scan's textured mesh (.glb)")
    parser.add_argument("body", help="the canonical body FBX from make_generated_body.py (with its .skeleton.json)")
    parser.add_argument("output", help="folder, e.g. Assets/_Game/Characters/Content/Imported/Generated")
    parser.add_argument("--preview", help="also render the hair on the body to this PNG (front, back, side, masculine)")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(argv)


def main():
    args = arguments()
    spec = ht.load_hair(args.hair)
    ct.reset_scene()
    body, skeleton, joints = gt.load_body(args.body)
    regions_of_body = gt.body_regions(body)
    frame = ht.head_frame(body, regions_of_body)
    neck_z, shoulder_z = float(joints["Neck"][2]), float(joints["UpperArm.L"][2])

    scan = gt.load_scan(args.scan, 1.70)
    factor = ht.match_arm_height(scan, body)
    labels, mean_colour, areas = gt.colour_clusters(scan, clusters=10)
    clusters = ht.hair_clusters(mean_colour, spec["colour"])
    if not clusters:
        sys.exit(f"[hair] no colour cluster near the hair colour {spec['colour']}")
    mask = ht.hair_mask(scan, labels, areas, clusters)
    skin_cluster = int(np.bincount(labels[gt.skin_faces(scan)], weights=areas[gt.skin_faces(scan)], minlength=len(mean_colour)).argmax())
    print(f"[hair] {args.hair}: scan scaled by {factor:.3f} to the body's arm height; hair is clusters {sorted(clusters)} "
          f"({areas[mask == 0].sum() / areas.sum():.0%} of the surface), skin cluster {skin_cluster}")

    scan_joints, _ = bt.fit_body_joints(scan, crotch=float(joints["UpperLeg.L"][2]) - 0.06)
    bt.align_to_skeleton(scan, scan_joints, joints)
    offset = ht.fit_head(scan, body, regions_of_body, skin_cluster, labels, neck_z, shoulder_z)
    print(f"[hair] head fitted to the canonical head: moved {np.linalg.norm(offset) * 100:.1f} cm {np.round(offset * 100, 1)}")

    hair = gt.extract(scan, mask, 0, args.hair)
    holes, loose = ht.tidy(hair)
    ht.smooth(hair)
    pt.decimate(hair, BUDGET)
    gt._relax_edges(hair, iterations=30)  # the fringe's and the ends' cut follows the texture's ragged colour edge
    lifted = ht.clear_eyes(hair, frame, joints)
    if spec.get("blunt"):
        ht.blunt_cut(hair, frame, joints)
    if spec.get("below_chin") is not None:
        ht.cut_length(hair, joints, spec["below_chin"])
    ht.tame(hair, frame, joints, squeeze=spec.get("squeeze", 0.0), curl=spec.get("curl", 0.0))
    if spec.get("thin"):
        ht.thin(hair, body, neck_z, keep=spec["thin"])
    pushed = ht.push_off_body(hair, body, neck_z, joints)
    chest = ht.off_the_chest(hair, body, regions_of_body, shoulder_z)
    scalp = ht.cover_scalp(hair, body, regions_of_body, frame, joints)
    print(f"[hair] filled {holes} holes, dropped {loose} loose pieces and {chest} faces on the chest, "
          f"fringe raised {lifted * 100:.1f} cm off the eyes, {scalp} faces of bare scalp covered")
    scan.hide_render = True
    co = bt.vertices(hair)
    tip_from, tip_to = float(frame["centre"][2]) - 0.02, float(co[:, 2].min())
    print(f"[hair] {pt.triangle_count(hair)} triangles, {pushed} vertex moves off the body; hangs to {tip_to:.2f} m")

    front, back = ht.split_front_back(hair, frame, spec["front"]["id"], spec["back"]["id"])
    bpy.data.objects.remove(hair)
    problems, made = [], []
    for piece, which in ((front, "front"), (back, "back")):
        if piece is None:
            problems.append(f"{spec[which]['id']}: no hair on that side")
            continue
        ht.tidy(piece, largest_hole=0.0)  # slivers the split left
        fair_piece(piece)
        ring = ht.hair_mask_colours(piece, frame, tip_from, tip_to)
        ht.round_normals(piece, frame)  # after the ring's cuts, which would interpolate them
        planar_uvs(piece)
        ht.transfer_shapes(piece, body, regions_of_body)
        gt._push_out(piece, body, ht.HEAD_OFFSET)  # in every shape: the masculine chest is broader than the hair beside it
        ht.follow_head(piece, joints, shoulder_z)
        chain_bones = []
        if which == "back":
            for chain in spec.get("chains", []):
                chain_bones += ht.fit_chain(piece, frame, chain["name"], chain["bones"])
        gt.limit_influences(piece)
        for old in [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]:
            bpy.data.objects.remove(old)
        rig = ct.build_armature("Armature", skeleton + [(n, p, tuple(round(c, 4) for c in bt.blender_to_unity(pos))) for n, p, pos in chain_bones])
        ct.bind_to_rig(piece, rig)
        found = ct.validate_part(piece, rig, body, max_triangles=12000)
        problems += found
        chains = sorted({n.rsplit(".", 1)[0] + ".1" for n, _, _ in chain_bones})
        print(f"[hair] {piece.name}: {pt.triangle_count(piece)} triangles, highlight ring on {ring} faces"
              + (f", spring chains {chains}" if chains else "") + (f", {len(found)} problems" if found else ""))
        if not found:
            ct.export_fbx(os.path.join(args.output, piece.name + ".fbx"), [rig, piece])
            ct.write_sidecar(os.path.join(args.output, piece.name + ".character.json"), {
                "kind": "part", "slot": f"hair.{which}", "followsBodyShapes": True, "springChains": chains,
                "tags": spec[which]["tags"], "style": args.hair,
                "source": os.path.abspath(args.scan), "generator": "Hunyuan3D 2.1 (tencent/Hunyuan3D-2.1)",
                "licence": "Tencent Hunyuan 3D 2.1 Community License",
            })
            made.append(piece)

    for problem in problems:
        print("[hair] PROBLEM:", problem)
    if args.preview:
        preview(body, made, os.path.abspath(args.preview))
        print("[hair] preview", args.preview)
    if problems:
        sys.exit(1)
    print(f"[hair] exported {len(made)} pieces to {args.output}")


def fair_piece(part, iterations=12):
    """
    Taubin smoothing of a hair piece, its open edges held: a small dent in the scan's surface turned away from the
    camera, and the toon outline showed through it as a black fleck.
    """
    mesh = part.data
    pairs = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", pairs)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    held = np.array([v.is_boundary for v in bm.verts])
    bm.free()
    co = gt._fair(bt.vertices(part), (pairs[0::2], pairs[1::2]), held, iterations=iterations)
    mesh.vertices.foreach_set("co", co.ravel())
    mesh.update()


def planar_uvs(part):
    """UV0 in metres from the front (the toon shader's patterns; hair takes none yet), and an empty region-4 layer."""
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


def preview(body, parts, path):
    """Workbench renders of the hair on the canonical body, head and shoulders: front, back, side, and the masculine shape."""
    scene = bpy.context.scene
    scene.world = scene.world or bpy.data.worlds.new("Preview")
    scene.world.color = (0.18, 0.18, 0.2)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light, shading.color_type, shading.show_object_outline = "STUDIO", "OBJECT", True
    body.color = (0.8, 0.62, 0.55, 1.0)
    for part, colour in zip(parts, ((0.25, 0.7, 0.45), (0.2, 0.45, 0.85))):
        part.color = (*colour, 1.0)
    camera = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 0.9
    scene.render.resolution_x, scene.render.resolution_y = 600, 700
    base = path[:-4] if path.lower().endswith(".png") else path
    views = (("front", (0, -6, 1.42), (90, 0, 0)), ("back", (0, 6, 1.42), (90, 0, 180)), ("side", (6, 0, 1.42), (90, 0, 90)),
             ("three", (4.2, -4.2, 1.5), (88, 0, 45)))
    for name, location, rotation in views:
        camera.location, camera.rotation_euler = location, tuple(math.radians(a) for a in rotation)
        scene.render.filepath = f"{base}_{name}.png"
        bpy.ops.render.render(write_still=True)
    for obj in [body, *parts]:
        if obj.data.shape_keys and "body.masculine" in obj.data.shape_keys.key_blocks:
            obj.data.shape_keys.key_blocks["body.masculine"].value = 1.0
    camera.location, camera.rotation_euler = views[0][1], tuple(math.radians(a) for a in views[0][2])
    scene.render.filepath = f"{base}_masculine.png"
    bpy.ops.render.render(write_still=True)


main()
