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
3. **Hair** comes from generations of the outfit-01 character with that hairstyle, in a colour far from skin and clothes (`Tools/Blender/hairs.json`, `make_generated_hair.py`). The steps are:
   - take the scan's hair-coloured surface, aligned to the canonical body and fitted to its head by the bare face; over the head it's a cap whose edge is the hairline, so the bald head sits inside it like a wig;
   - cover any bare scalp the scan invented (Hunyuan painted the nape under a ponytail as skin), drop hair lying on the chest, and shorten a fringe to the canonical brows so the game's eyes and expressions show;
   - cut it into `hair.front` (in front of the ears, below the crown) and `hair.back`, which meet without a gap;
   - give it the hair mask (base to tip down its length, the highlight ring round the crown), head weights handing over down the spine for long hair, the body's shapes from the head and trunk, and spring chains along hair hanging free behind (a ponytail).

   `GeneratedHairSheet` reviews them (`Docs/review/generated-hair.png`).
4. **Clothing** comes from generations of a character wearing the outfit (same prompt and seed as the base, so the same body), aligned to the canonical body. The steps are:
   - split by colour and body region into the slots (`Tools/Blender/outfits.json` lists each outfit's garments, the regions they may sit on, and the prompt's colours);
   - **fitted garments** (tops, bottoms, legwear, one-pieces, shoes) are **built from the canonical body's own surface** wherever the scan shows the garment, lifted to the layer offset (style bible §3) or to the scan's own looseness, with hollows bridged the way cloth spans them. They inherit the body's weights and every body shape exactly. Pushing the scan's own garment geometry onto the body came out boxy and lumpy, since it kept the outfit model's proportions. Their shape is made in the game's everyday arms-down pose (`Poses.Relaxed`) and unposed to the bind pose, with the slack round each limb evened out and hollows bridged only between parts that move together: shaped in the T-pose, the slack a loose sleeve hangs with under a raised arm bunched into shoulder pads once the arms came down;
   - **loose layers** (coats, and later skirts) keep the scan's geometry, conformed to the body, and can be **settled with Blender cloth simulation** (`--settle`); on a T-posed body it buckles fitted fabric, so it isn't used for those;
   - transfer weights and body shapes to the loose layers;
   - a hood (down, round the neck) stands too far off the body to fit, so it comes from the scan's own geometry and is joined to the fitted garment;
   - build the recolour mask from the generated texture's colour blocks, counting shades of one colour as one block (shading split a burgundy top into blotches), cut along smooth region edges; garment edges are straightened along their own line rather than blurred, so thin straps survive;
   - draft the catalog entry.

   The physics runs at authoring time. In game, motion stays on spring bones: runtime cloth for 40 residents doesn't fit decision 0004's budget. `GeneratedOutfitSheet` reviews the parts on the generated body (`Docs/review/generated-outfit.png`).
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
  - Generated texture quality limits the recolour masks. Hunyuan invents the views it can't see: it painted a bald head's crown dark grey and a skin-coloured seam down each side of a pair of shorts. Skin is read from the face and hands only, and garment coverage is closed over gaps a few centimetres wide.
  - The Tencent licence doesn't cover the EU, UK or South Korea; the user has accepted that for now.
- **Graphics plan:** G2's "one hand-modelled character" becomes "one generated character at the bar"; G3's parts catalogue is generated. G1 (shader parity) is unchanged and applies to generated parts as it does to stand-ins.
