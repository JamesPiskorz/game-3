"""Builds the pipeline test rig, body and parts, transfers skinning and shape keys, validates and exports FBX.

blender -b --factory-startup --python Tools/Blender/make_pipeline_test_assets.py -- <output folder>

Exits non-zero if validation fails, so it can gate content builds.
"""

import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import character_tools as ct  # noqa: E402


def main():
    output = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "Assets/_Game/Characters/Content/Imported/PipelineTest"
    ct.reset_scene()
    rig = ct.build_armature()
    body = ct.build_body(rig)
    vest = ct.build_vest(rig)
    scarf = ct.build_scarf(rig)
    ct.transfer_from_body(vest, body, bones=["Chest", "Spine", "Hips", "Neck"])  # sleeveless: the arms move freely
    ct.transfer_from_body(scarf, body, bones=["Neck", "Chest", "Head"])  # a scarf mustn't follow the arms

    problems = ct.validate_part(body, rig, body) + ct.validate_part(vest, rig, body) + ct.validate_part(scarf, rig, body)
    for problem in problems:
        print("[pipeline] PROBLEM:", problem)
    if problems:
        sys.exit(1)

    ct.export_fbx(os.path.join(output, "PipelineBody.fbx"), [rig, body])
    ct.write_sidecar(os.path.join(output, "PipelineBody.character.json"), {"kind": "body"})
    ct.export_fbx(os.path.join(output, "cloth.top.vest_test.fbx"), [rig, vest])
    ct.write_sidecar(os.path.join(output, "cloth.top.vest_test.character.json"),
                     {"kind": "part", "slot": "top", "covers": ["UpperTorso", "LowerTorso"], "followsBodyShapes": True})
    ct.export_fbx(os.path.join(output, "acc.neck.scarf_test.fbx"), [rig, scarf])
    ct.write_sidecar(os.path.join(output, "acc.neck.scarf_test.character.json"),
                     {"kind": "part", "slot": "neck", "covers": [], "followsBodyShapes": True})
    print("[pipeline] exported to", output)


main()
