# 0005: Blender → Unity character pipeline (Spike E)

**Status:** Accepted (spike result) · 2026-10-17 · Blender 5.2.1 LTS, Unity 6000.6.6f1

## Question

Can an artist's Blender part reach the game with no hand set-up, and land correctly skinned, recolourable, region-hiding and following body shapes? This spike tests the riskiest seams: axis and scale conversion, bone orientation, vertex-colour colour space, shape keys, and weight transfer.

## What we built

**Blender side, headless:**

- `Tools/Blender/character_tools.py`:
  - **Shared rig:** the stand-in skeleton converted to Blender axes.
  - **Mesh builder:** writes weights, region IDs and shape keys.
  - **Weight and shape-key transfer:** copies from the body onto a part, using the nearest surface point and a barycentric blend, with an optional bone mask.
  - **Validator:** checks for unknown bones, unnormalised weights, more than 4 influences, missing body shape keys, triangle budget, the colour attribute, and vertices following bones more than 0.35 m away.
  - **FBX export:** with Unity settings.
- `Tools/Blender/make_pipeline_test_assets.py` builds a body, a vest and a scarf, transfers, validates (exiting non-zero on problems) and exports FBX plus `<name>.character.json` sidecars:

```bash
blender -b --factory-startup --python Tools/Blender/make_pipeline_test_assets.py -- Assets/_Game/Characters/Content/Imported/PipelineTest
```

**Unity side:**

- `CharacterModelImporter`, an `AssetPostprocessor` for `Content/Imported/**.fbx`, sets the import settings:
  - generic rig, no animation, no imported materials,
  - blendshapes kept with authored normals,
  - no imported tangents, readable meshes.
- It then bakes outline normals into tangents, assigns toon material templates, and turns the sidecar into a `ModularCharacter` (body) or a `CharacterPart` (slot, covered regions, blocked slots, spring chains). The sidecar is a declared import dependency.
- `RigCheck.Compare` verifies a part's rig matches the body's rest pose.
- Tests: 3 end-to-end. Plus the review sheet: **Game → Characters → Review → Render Pipeline Test Sheet**.

## Results

![Imported pipeline test](img/0005-pipeline.png)

Right to left in the front view: the body alone; with vest and scarf; with the body shape at full; and posed (arms down).

- **Rig contract holds:** the parts' armatures match the body's rest pose exactly after FBX round-trip (`RigCheck`), even though Blender bones keep Blender orientations (e.g. `UpperArm.L` imports at (270°, 90°, 0)). Rebinding needs identical rigs, not identity rotations.
- **Region IDs survive** in a linear float colour attribute exported with `colors_type=LINEAR` (sRGB export would bend them).
- **Shape keys survive:** transferred body shapes on parts carry real deltas (~3 cm mean).
- **Region hiding, rebinding, material instancing and shape mirroring** all work on imported parts unchanged.

## Lessons (each was a real bug in this spike)

1. **BMesh iteration order is not creation order.** Operators like `create_uvsphere` free temporary vertices, and later vertices reuse those slots. Per-vertex lists indexed by creation order put head weights on the arms. Per-vertex data is now recorded on the vertex itself (an int layer). The validator's far-bone check catches this class of bug.
2. **Weight transfer needs a bone mask for loose or sleeveless parts.** Nearest-surface transfer gave a scarf's outer edge, and a vest's shoulder, upper-arm weights, so they swung with the arms. Each part names the bones it may follow.
3. **Poses must be rig-agnostic.** `Poses.Relaxed` now bends bones in the character's frame relative to rest, not by setting local Euler angles, which assumed world-aligned bones.
4. **`SkinnedMeshRenderer.BakeMesh` ignores blendshape weights in edit-mode tests.** Shape transfer is checked on the shape data instead.
5. **`bake_space_transform` doesn't bake armatures.** The imported `Armature` node carries a -90° X rotation, which is harmless because body and parts match. Code must never assume the armature root is identity (`RigCheck` compares in each root's space).

## Next

- **Real art:** a base body sculpted by art against `character_tools.SKELETON`, with the same export path.
- **Transfer settings in the sidecar:** move the bone mask (and later a per-part push-out offset) into the sidecar, so artists don't edit Python.
- **Body shapes:** the full body shape set (decision 0001 / PLAN §3.2), with the validator blocking any part missing one.
- **Import-time rig check:** run `RigCheck` against the body when a part imports, and fail the import on a mismatch.
