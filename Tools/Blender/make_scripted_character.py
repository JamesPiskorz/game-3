"""Builds the scripted-art spike character (decision 0010): body and head, hair, a jacket and shorts, all from
scripts, with procedural textures; validates, checks deformation in test poses, and exports FBX plus sidecars and
textures for Unity's CharacterModelImporter.

blender -b --factory-startup --python Tools/Blender/make_scripted_character.py -- <output folder> [--previews <folder>]

The output is the same, byte for byte, on every run (see make_fbx_deterministic). Exits non-zero if
validation fails, so it can gate content builds. Nothing here touches the stand-in catalogue.
"""

import datetime
import hashlib
import os
import sys
import time
import types

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402

import character_tools as ct  # noqa: E402
import scripted_body as sb  # noqa: E402
import scripted_garment as sg  # noqa: E402
import scripted_hair as sh  # noqa: E402
import scripted_preview as sp  # noqa: E402
import scripted_textures as st  # noqa: E402

DEFAULT_OUTPUT = "Assets/_Game/Characters/Content/Imported/ScriptedSpike"
BODY_ID = "ScriptedBody"
HAIR_BACK_ID = "hair.back.layered_s1"
HAIR_FRONT_ID = "hair.front.sidelocks_s1"


class _FixedDateTime(datetime.datetime):
    @classmethod
    def now(cls, tz=None):
        return cls(2026, 10, 18, 0, 0, 0)


def make_fbx_deterministic():
    """
    Rebuilds must be byte-identical, and the FBX writer varies run to run in two ways: it stamps the creation time
    into the header, and it derives node IDs from Python's hash(), which is salted per process. Pin the first and give
    the second a stable hash.
    """
    from io_scene_fbx import export_fbx_bin, fbx_utils

    def stable_key_to_uuid(uuids, key):
        if isinstance(key, int) and 0 <= key < 2**63:
            uuid = key
        else:
            digest = hashlib.blake2b(repr(key).encode("utf-8"), digest_size=8).digest()
            uuid = int.from_bytes(digest, "little") % 10**9  # short IDs, as the writer prefers
        while uuid in uuids:
            uuid += 1
        return fbx_utils.UUID(uuid)

    export_fbx_bin.datetime = types.SimpleNamespace(datetime=_FixedDateTime)
    fbx_utils._key_to_uuid = stable_key_to_uuid


def export_part(path, rig, mesh):
    """Exports one part with its own rig, which the importer needs under a node named exactly 'Armature'."""
    others = [o for o in bpy.data.objects if o.type == "ARMATURE" and o is not rig]
    names = {o: o.name for o in others}
    for i, o in enumerate(others):
        o.name = f"_rig{i}"
    own = rig.name
    rig.name = "Armature"
    ct.export_fbx(path, [rig, mesh])
    rig.name = own
    for o, name in names.items():
        o.name = name


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    output = args[0] if args and not args[0].startswith("--") else DEFAULT_OUTPUT
    previews = args[args.index("--previews") + 1] if "--previews" in args else None
    started = time.time()

    ct.reset_scene()
    rig = ct.build_armature()
    body = sb.build_body(rig)
    (hair_back, back_rig, back_chains), (hair_front, front_rig, front_chains) = sh.build_hair(body)
    garments = {gid: sg.build_garment(gid, body, rig) for gid in sg.GARMENTS}

    problems = ct.validate_part(body, rig, body)
    problems += ct.validate_part(hair_back, back_rig, body, follows_body_shapes=False)
    problems += ct.validate_part(hair_front, front_rig, body, follows_body_shapes=False)
    for obj in garments.values():
        problems += ct.validate_part(obj, rig, body)
    for problem in problems:
        print("[scripted] PROBLEM:", problem)
    if problems:
        sys.exit(1)

    for obj in [body, hair_back, hair_front] + list(garments.values()):
        obj.data.calc_loop_triangles()
        print(f"[scripted] {obj.name}: {len(obj.data.vertices)} vertices, {len(obj.data.loop_triangles)} triangles")
    for pose in ("relaxed", "reach", "sit"):
        sp.pose(rig, pose)
        smallest, squashed, stretched = sp.deformation_report(body, rig)
        print(f"[scripted] pose {pose}: smallest triangle {smallest:.2f} of rest area, {squashed} under 25%, {stretched} over 250%")
    sp.pose(rig, "rest")

    st.body_textures(body, output, BODY_ID, rig)
    st.hair_textures(hair_back, output, HAIR_BACK_ID)
    st.hair_textures(hair_front, output, HAIR_FRONT_ID)
    for gid, obj in garments.items():
        st.garment_textures(obj, output, gid, sg.GARMENTS[gid], rig, body)

    make_fbx_deterministic()
    export_part(os.path.join(output, BODY_ID + ".fbx"), rig, body)
    ct.write_sidecar(os.path.join(output, BODY_ID + ".character.json"), {"kind": "body"})
    export_part(os.path.join(output, HAIR_BACK_ID + ".fbx"), back_rig, hair_back)
    ct.write_sidecar(os.path.join(output, HAIR_BACK_ID + ".character.json"),
                     {"kind": "part", "slot": "hair.back", "covers": [], "springChains": back_chains, "followsBodyShapes": False})
    export_part(os.path.join(output, HAIR_FRONT_ID + ".fbx"), front_rig, hair_front)
    ct.write_sidecar(os.path.join(output, HAIR_FRONT_ID + ".character.json"),
                     {"kind": "part", "slot": "hair.front", "covers": [], "springChains": front_chains, "followsBodyShapes": False})
    for gid, obj in garments.items():
        export_part(os.path.join(output, gid + ".fbx"), rig, obj)
        ct.write_sidecar(os.path.join(output, gid + ".character.json"), sg.sidecar(gid))

    if previews:
        render_previews(previews, rig, back_rig, front_rig)
    print(f"[scripted] exported to {output} in {time.time() - started:.0f} s")


def render_previews(folder, *rigs):
    """Workbench contact sheets of the test poses (for checking deformation outside Unity)."""
    os.makedirs(folder, exist_ok=True)
    for pose in ("rest", "relaxed", "reach", "sit"):
        for rig in rigs:
            sp.pose(rig, pose)
        paths = []
        for view in ("front", "side", "three_quarter"):
            path = os.path.join(folder, f"_{pose}_{view}.png")
            sp.render(path, view, ortho_scale=1.9, resolution=(720, 720))
            paths.append(path)
        sp.contact_sheet(paths, os.path.join(folder, f"pose-{pose}.png"), len(paths))


main()
