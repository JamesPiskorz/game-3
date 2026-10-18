# 0009: Character art built entirely by script (scripted-art spike)

**Status:** Accepted (spike result) · 2026-10-18 · Blender 5.2.1 LTS, Unity 6000.6.6f1

## Question

The graphics plan assumes 1-2 artists make every character part, and names art cost as its biggest risk. Claude can't paint, sculpt or generate images, but it can build geometry and textures by script in Blender. How good can a character built entirely that way get, measured on one original character against today's stand-ins and against the target look (R1-R12)? Should the catalogue be built this way?

## What we built

All headless, all deterministic, all through the existing pipeline (decision 0005):

```bash
blender -b --factory-startup --python Tools/Blender/make_scripted_character.py
```

It writes `Assets/_Game/Characters/Content/Imported/ScriptedSpike/` (FBX, sidecars, textures) in about 105 s. Rebuilds are byte-identical (the FBX writer's time stamp and node IDs are pinned). Nothing is added to the catalogue; the stand-ins are untouched.

| Part | Module | How it's built | Triangles |
|---|---|---|---|
| Body and head | `scripted_body.py` | A quad cage built ring by ring on `character_tools.SKELETON`: three loops at each shoulder, elbow, wrist, hip, knee and ankle; five fingers grown from the palm; one welded mesh. Subdivided once onto its limit surface. Weights per ring from the bones it sits between, smoothed on the final mesh. `body.weight`, `body.chest`, `body.hips` evaluated per vertex. | 9,520 |
| Head (same mesh) | `scripted_body.py` | A cube-sphere cage projected onto a parametric head: signed-distance cranium, cheeks, jaw cones, chin and nose, blended smoothly. Normals from an ellipsoid proxy, so the face shades as one form. | (in body) |
| Hair | `scripted_hair.py` | Guide curves grown from the scalp, combed along it, pushed out of the head and torso by collision, then hanging under gravity. Tapered lens-section clumps swept along them, with count, width, taper, curl, layer offset per group. A scalp cap underneath. Spring chains `Hair.Back.L/C/R`, `Hair.SideLock.L/R`, each the average of the hanging parts of its clumps; clump vertices weighted along their own curve. | 9,132 back, 2,640 front |
| Jacket | `scripted_garment.py` | The body cage's own faces, eased out, with banded collar, cuffs and hem (each with a turned-back facing for thickness), settled (fill hollows, push clear of the body), seam grooves on the cage's seam lines, elbow and waist folds. Then `transfer_from_body` and the validator. | 3,680 |
| Shorts | `scripted_garment.py` | The same tool with a different recipe (not asked for; added so the comparison isn't bare legs). | 1,984 |
| Textures | `scripted_textures.py` | Cycles bakes position, normal, ambient occlusion and curvature, plus construction attributes; numpy writes `_base` (value detail), `_mask` (regions 1-4) and `_light` (G1.1 layout: shadow bias, specular mask, metal mask, material type). The face (eyes, lashes, brows, mouth) is drawn with signed-distance shapes in face space. | — |

The character carries about 13.7k skinned vertices before shoes (a generated stand-in averages about 21k, `Docs/review/perf.md`). Every part passes `validate_part`. Test poses (relaxed, reach, sit) keep every triangle above 16% of its rest area.

**Unity:** the parts import with the unchanged `CharacterModelImporter`. **Game → Characters → Review → Render Scripted Character Sheet** renders the character with the current toon shader next to a stand-in in the same colours, pose and light (`Docs/review/scripted-character.png`, `scripted-poses.png`). The scripted character borrows the stand-in sneakers, with bind poses rebaked for its rig. The current shader reads `_BaseMap` and `_MaskMap`; the `_light` texture waits for G1.1.

## Results

![Stand-in (left) and scripted character (right)](../review/scripted-character.png)

Test poses: [scripted-poses.png](../review/scripted-poses.png). Full judge report: [scripted-character-judge.md](../review/scripted-character-judge.md).

**Track V judge** (fresh context; renders, rubric and question only; defects before verdict): **the scripted character beats the stand-in, with high confidence.** Its reasons: an anime face with large catch-lit eyes against a box with dots; layered long hair and one smooth body against a helmet on tubes with stub hands; and a jacket with collar, placket, buttons, seams, hem and cuffs that holds its shape in the reach pose.

On the target look it was blunt: **"a basic VRoid or MMD-tier model, not a polished gacha character."** Its worst defects in the scripted character:

1. The hair reads as many thin strands with a ragged outline and a flat crown, with no visible highlight band, and it clips at the shoulders and across the face in three-quarter view.
2. Knees fold to a sharp corner when bent, and the shorts' seat goes lumpy when sitting.
3. The face is faint at full-body distance and almost vanishes at sunset.
4. The jacket has no visible folds; the hands are stiff; the legs are featureless; the borrowed shoes are bricks.

Rubric (scripted / stand-in): proportions and clothing **yes / no**; silhouette, face, hair, shading, outlines, clipping, deformation and closeness to the target **partly**, against mostly **no** for the stand-in.

**Checks:** every part passes `validate_part`; the 311 EditMode tests pass; three builds in a row produce byte-identical files.

## Hours

Wall-clock time for one agent session (11:35-13:25), from the timestamps of its previews, renders and logs. It includes reading the plan and code, every build and render, and this write-up; machine time (Blender builds of about 105 s, Unity runs of 1-4 min) is inside these numbers. A person writing the same scripts would take far longer; the point is what the scripts cost to run and change once they exist.

| Step | Time |
|---|---|
| Reading the plan, pipeline and stand-ins; worktree set-up | 20 min |
| Body, head and hands (cage, weights, shapes, poses) | 28 min |
| Hair (guide curves, clumps, cap, chains) | 8 min |
| Jacket and shorts | 5 min |
| Bakes, masks, face drawing, lightmap, export script | 14 min |
| Unity review sheet, import, render-and-fix rounds (hair, shorts, seam and eye fixes) | 21 min |
| Judge, tests, determinism check, write-up | 15 min |
| **Total** | **about 1 h 50 min** |

For scale: the graphics plan budgets 6-8 weeks of art for its first hero character (G2). The two aren't the same deliverable (see the ceiling below), but a second scripted hairstyle or garment is a new recipe (tens of parameters), not a new build: roughly 15-30 minutes of tuning each.

## What worked

- **Topology by construction.** Building the cage ring by ring puts edge loops exactly where the rig bends, and the tags on cage vertices (part, flesh, seams) survive subdivision. Garments cut from the same cage inherit those loops and seam lines for free.
- **Parametric shapes plus projection.** The head is a dozen SDF numbers; projecting the subdivided cage onto it gives a clean mesh with art-directable proportions. Proxy-ellipsoid normals give a clean toon terminator on the face without an SDF face map.
- **Simulation-lite.** Growing hair against a collider and settling cloth against the body (smooth along the normal, push out) produce plausible drape with no physics engine.
- **Textures from construction.** Masks come from what the script already knows (trim bands, linings, buttons, clump tips, face features), so trims and lash lines are crisp, and region colours recolour cleanly through the existing shader.
- **The pipeline held.** Rig contract, shape transfer, validator, importer and region hiding all worked unchanged on five new parts.

## The quality ceiling

What scripts reach: clean, welded, well-deformed meshes; credible tailoring; recolourable masks; an anime face that reads; hair that reads as hair. That is far past the stand-ins and roughly where a competent low-poly base mesh plus a careful technical artist would land.

What they don't reach, and why:

- **Appeal.** Genshin-style characters live on designed silhouettes: hair clumps placed and shaped for flow and rhythm, faces with drawn eye art, hands and costume details posed for charm. Scripts place things by rule; the result is tidy rather than designed. Judging appeal is also Claude's weak spot, so iterating toward it without an artist's eye is slow and unreliable.
- **Painted detail.** The reference paints gradients, strand highlights, cloth folds and light shapes by hand. Procedural strands and AO-derived shadows are plausible but generic.
- **Faces.** The face is a decal set drawn from a few shapes: one expression, no `expr.*` blendshapes, no SDF face map, no per-character eye design. Real heads need all of these (G1.4, G2.2).
- **Hair silhouette.** Rule-grown clumps overlap and bunch where they meet the shoulders; the crown and bangs need designer-placed guide curves to look intentional.
- **The look itself is mostly shader.** R1-R12 (ramps, rim, face map, specular, lightmap use) are G1 shader work. Neither these parts nor hand-made ones will look like the reference until G1 lands.

## Recommendation

**Don't plan the catalogue as script-only art if the bar is the reference look.** Under two hours bought a character the judge calls clearly better than the stand-ins but VRoid-tier against the target, and the gap that remains is the part scripts are worst at: designed appeal in hair, faces and costume detail. More iteration would polish the defects the judge listed, not close that gap.

**Do build the catalogue on this pipeline, with artists directing it instead of modelling from scratch:**

| Who | What |
|---|---|
| **Scripts** | The base body and its shape range; garment shells cut from the body cage, with fit, bands, hems, plackets, buttons and seams as recipes; UVs, weights, shape transfer, masks, lightmap bakes, validation, export and review renders. |
| **Art** | Head bases and face art (eye and mouth designs, `expr.*` shapes, SDF face maps); hair silhouettes, placed as guide curves in Blender that the scripts sweep into clumps, cap, UVs, masks and chains; costume concepts and the painted shadow and fold shapes in the lightmap; the final call on appeal. |

This keeps G2 (one hero character to measure art cost) but changes what's measured: hours per part *with* the script kit, not modelling from scratch. Two steps should come before any catalogue sizing:

1. **G1 first.** Most of R1-R12 is shader work, and the scripted parts already carry the G1.1 lightmap channels and clean UVs to test it on.
2. **Better stand-ins in the meantime.** These parts beat today's stand-ins (no rubric line went the other way), and a new garment or hairstyle is a recipe of tens of parameters taking roughly 15-30 minutes. Replacing stand-ins slot by slot with scripted parts (not done in this spike) would make every review sheet and the G1 work more representative. That's a production call.

## Lessons (each was a real bug in this spike)

1. **Blender 5.2 adds shape keys at value 1.0.** Previews showed every body shape at full until the scripts reset them to 0.
2. **Blender's PNG writer drops the colour of fully transparent pixels.** Masks keep region 4 in alpha, so regions 1-3 vanished wherever there was no region 4. The textures are written with a small PNG encoder instead.
3. **Entering edit mode in vertex-select mode rebuilds the face selection from leftover vertex flags,** so Smart UV Project re-unwrapped the face at body density. Use face-select mode and clear vertex and edge flags first.
4. **Hair grown in the T-pose rest pose collides with the outstretched arms** and splays out along them. Hair collides with the head and torso only.
5. **Seam lines on adjacent cage columns turn the face between them into "all seam".** Seam columns must never be neighbours.
6. **A part that covers only part of a body region must not hide it.** The shorts first hid the whole thigh region and left a gap above the knee.
7. **New worktrees hold Git LFS pointers, not files.** Stand-ins built with null meshes until `git lfs checkout`.
8. **Unity's front camera puts +X on the left of the frame.** Review sheets must place characters accordingly.
9. **Blender's FBX writer isn't deterministic.** It stamps the creation time and derives node IDs from Python's per-process salted `hash()`. The build script pins the stamp and swaps in a stable hash.

## Next

- **The judge's top fixes, in the scripts:** fewer, wider hair clumps with normals from a smooth proxy and a highlight band that reads; corrective shapes at the knees and elbows; thicker face lines with a contrast floor (and the SDF face map, G1.4); painted fold shapes in the lightmap; a scripted shoe recipe.
- **G1.1:** read `_light` in the toon shader, using these parts as the test assets.
- **Art and production:** decide whether to replace stand-ins with scripted parts slot by slot, and run G2 with the script kit to measure art hours per part.
- **Compare with the image-to-3D head spike** when it reports: a generated head wrapped to the shared topology could supply the face these scripts can't.
