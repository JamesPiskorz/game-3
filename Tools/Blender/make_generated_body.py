"""Builds the canonical body from a Hunyuan3D scan of a T-posed, bald base character (decision 0008).

blender -b --factory-startup --python Tools/Blender/make_generated_body.py -- <scan_raw.glb> <output folder>
        [--height 1.70] [--name GeneratedBody] [--preview <png>]

Writes <name>.fbx and <name>.character.json ({"kind": "body"}) for CharacterModelImporter, plus <name>.skeleton.json
(the fitted rest pose, Unity axes). Exits non-zero if validation fails, so it can gate content builds.
"""

import argparse
import json
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import body_tools as bt  # noqa: E402
import character_tools as ct  # noqa: E402
import prop_tools as pt  # noqa: E402

BODY_TRIANGLES = 20000  # PLAN.md section 4: body about 20k, head 10k
HEAD_TRIANGLES = 10000
BUDGET = 40000          # both, plus the face features


def arguments():
    parser = argparse.ArgumentParser(prog="make_generated_body.py", description=__doc__.splitlines()[0])
    parser.add_argument("scan", help="the scan's raw mesh (.glb), T-posed, facing the camera that made it")
    parser.add_argument("output", help="folder, e.g. Assets/_Game/Characters/Content/Imported/Generated")
    parser.add_argument("--height", type=float, default=1.70, help="crown height in metres (default 1.70)")
    parser.add_argument("--name", default="GeneratedBody")
    parser.add_argument("--voxel", type=float, default=0.003, help="remesh voxel size in metres (default 3 mm)")
    parser.add_argument("--masculine", help="a scan of the masculine base: adds the body.masculine shape")
    parser.add_argument("--keep-anatomy", action="store_true", help="skip the mannequin finish on chest and groin")
    parser.add_argument("--preview", help="also render the body, its regions and a test pose to this PNG")
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return parser.parse_args(argv)


def validate_body(body, rig, budget):
    """Problems that would break the body in Unity. Empty list = OK to export."""
    problems = ct.validate_part(body, rig, body, max_triangles=budget)
    for shape in ("body.weight", "body.chest", "body.hips"):
        if body.data.shape_keys is None or shape not in body.data.shape_keys.key_blocks:
            problems.append(f"{body.name}: no '{shape}' shape key")
    missing = {name for name, _ in bt.PARENTS} - {bone.name for bone in rig.data.bones}
    if missing:
        problems.append(f"{body.name}: the rig lacks {sorted(missing)}")
    return problems


def main():
    args = arguments()
    pt.reset_scene()
    body = bt.prepare_scan(args.scan, args.height, args.voxel)
    print(f"[body] {args.scan}: placed at {args.height} m, remeshed to {pt.triangle_count(body)} triangles")
    joints, measures = bt.fit_body_joints(body)
    face, face_info = bt.fit_face_joints(body, joints, measures)
    joints.update(face)
    print(f"[body] joints fitted: crotch {measures['crotch']:.3f} m, eye sockets {face_info['socket_half'][0] * 2000:.0f} x {face_info['socket_half'][1] * 2000:.0f} mm")
    if not args.keep_anatomy:
        bt.mannequin(body, joints, measures)

    other = None
    if args.masculine:
        other = bt.prepare_scan(args.masculine, args.height, args.voxel)
        other_joints, other_measures = bt.fit_body_joints(other)
        if not args.keep_anatomy:
            bt.mannequin(other, other_joints, other_measures)
        moved = max(float(((joints[n] - other_joints[n]) ** 2).sum() ** 0.5) for n in other_joints)
        bt.align_to_skeleton(other, other_joints, joints)
        print(f"[body] masculine scan aligned to the skeleton (joints moved up to {moved * 100:.1f} cm)")

    bt.decimate(body, BODY_TRIANGLES, HEAD_TRIANGLES, joints["Head"][2] - 0.02)
    body.name = body.data.name = args.name
    skeleton = bt.skeleton_from_joints(joints)
    rig = ct.build_armature("Armature", skeleton)
    bt.skin(body, rig)
    bt.weight_nose(body, joints)
    bt.paint_regions(body)
    rounded = bt.round_jaw(body, joints)
    print(f"[body] jaw rounded (up to {rounded * 1000:.1f} mm)")
    features = bt.build_face(body, joints, face_info["socket_half"])
    bt.paint_blush(body, joints)
    bt.add_body_shapes(body, joints)
    if other is not None:
        largest = bt.add_presentation_shape(body, other, joints)
        bpy.data.meshes.remove(other.data)
        print(f"[body] body.masculine shape: moves the skin up to {largest * 100:.1f} cm")
    tucked = bt.tuck_thumbs(body, joints)
    print(f"[body] thumbs tucked in ({tucked} vertices)")
    shaded = bt.face_normals(body, joints)
    print(f"[body] {pt.triangle_count(body)} triangles ({features} face-feature vertices), {len(body.vertex_groups)} bones weighted, "
          f"face normals on {shaded} vertices")

    problems = validate_body(body, rig, BUDGET)
    for problem in problems:
        print("[body] PROBLEM:", problem)
    if args.preview:
        bt.render_preview(body, rig, os.path.abspath(args.preview))
        print("[body] preview", args.preview)
    if problems:
        sys.exit(1)

    path = os.path.join(args.output, args.name + ".fbx")
    ct.export_fbx(path, [rig, body])
    ct.write_sidecar(os.path.join(args.output, args.name + ".character.json"), {"kind": "body"})
    with open(os.path.join(args.output, args.name + ".skeleton.json"), "w", encoding="utf-8") as f:
        json.dump({"source": os.path.abspath(args.scan), "height": args.height,
                   "bones": [{"name": n, "parent": p, "position": list(pos)} for n, p, pos in skeleton]}, f, indent=2)
    print("[body] exported", path)


main()
