# 0008: Characters generated with Hunyuan3D, split into the game's parts

**Status:** Accepted (in progress) · 2026-10-18 · supersedes the "not for characters" verdict in the graphics plan's cross-check (rows 6, 8-12 and 15)

## Context

The procedural stand-ins prove the systems (modular assembly, DNA, recolouring, springs, toon shading) but don't look good enough, and the graphics plan's route to good-looking characters (G2: model one character by hand, then a parts catalogue) needs artists the project doesn't have. Hunyuan3D 2.1 now runs locally ([hunyuan3d.md](../hunyuan3d.md)). From one T-pose image it makes a convincing stylised character in about 7 minutes: a clean body, separated fingers on a thumb-and-mitten hand, modelled clothing edges, strand-cut hair, an anime head with eye sockets, and a texture in the right colours.

What it makes is one fused solid with a painted face: no rig, no separate hair or clothes, no recolour regions. The game needs modular parts on one shared rig (decision 0001), recolourable (0002), with body shapes and layering (style bible §3).

## Decision

**Generate whole characters, then decompose them into the game's parts**, conformed to one canonical generated body. The modular system (DNA, the population generator, outfits per context, recolouring, the salon) stays as it is.

1. **The canonical body is generated.** Bald base images (feminine and masculine, T-pose, tight minimal clothing, barefoot) go through Hunyuan3D. Blender scripts then:
   - symmetrise, remesh and smooth it to the PLAN §4 budget (body about 20k triangles plus head 10k);
   - fit the skeleton to it, which **replaces the stand-in skeleton** as the canonical rest pose;
   - weight it automatically and derive the body regions from the weights;
   - author `body.weight`, `body.chest` and `body.hips` as the stand-ins do.

   The masculine scan is wrapped onto the feminine body's topology to make a **`body.femininity` shape**, so one body and one set of parts covers everyone, and `BodyDna.Femininity` finally drives the mesh.
2. **Faces stay the game's own.** The generated head keeps its sculpted shape (eye sockets, nose, chin). The game's face features (eye whites, irises, catch-lights, brows, mouth pieces, blush and smile strokes) are built in Blender against the real head surface and skinned to the face bones, so expressions, blinking, styles and recolouring keep working. Hunyuan3D's painted face is only used to find where the features go.
3. **Hair** comes from generations of a character with that hairstyle, aligned to the canonical body. What lies outside the head becomes `hair.back` and `hair.front`, on the Head bone, with spring chains fitted along long hair.
4. **Clothing** comes from generations of a character wearing the outfit, aligned to the canonical body. The steps are:
   - split by colour and body region into the slots;
   - push each piece out to its layer offset (style bible §3);
   - **settle it on the body with Blender cloth simulation**, so nothing clips and skirts hang;
   - transfer weights and every body shape from the body;
   - build the recolour mask from the generated texture's colour blocks;
   - draft the catalog entry.

   The physics runs at authoring time. In game, motion stays on spring bones: runtime cloth for 40 residents doesn't fit decision 0004's budget.
5. **Everything arrives through decision 0005's path:** FBX plus a sidecar, through `CharacterModelImporter`, checked by the Blender validator, reviewed on the part sheet, the 96-character quality sheet and the lineups.

The images are text-to-image, not edits of one base, so no two generations share a body exactly. Aligning and conforming to the canonical body absorbs that. If it doesn't, Hunyuan3D-Omni can condition a generation on the canonical body's point cloud.

## Consequences

- **Good:** the look comes from generated sculpts instead of capsules, while every system built on the stand-ins keeps working. New hair and outfits cost an image and a script run, not modelling time.
- **Cost:**
  - Replacing the canonical skeleton moves every joint. The 70 procedural stand-ins are built in absolute coordinates, so they misfit until they are retargeted or replaced by generated parts.
  - The PipelineTest FBXs, `character_tools.SKELETON`, spring colliders and the hard-coded head centre and radius all follow the new skeleton.
- **Risks:**
  - Generated hands are mittens with a thumb.
  - Separate generations may not align well enough to conform.
  - Generated texture quality limits the recolour masks.
  - The Tencent licence doesn't cover the EU, UK or South Korea; the user has accepted that for now.
- **Graphics plan:** G2's "one hand-modelled character" becomes "one generated character at the bar"; G3's parts catalogue is generated. G1 (shader parity) is unchanged and applies to generated parts as it does to stand-ins.
