# Graphics plan

_Draft 2026-10-18, revised the same day for the target look. Extends [PLAN.md](../PLAN.md) §3.7 (shading), §4 (art pipeline) and Phases 1, 4 and 6. Art calls are marked **[Art to decide]**._

## Summary

**Target:** characters that look like Genshin Impact's. [Ben Ayers' breakdown of its character shader](https://bjayers.artstation.com/blog/9oOD/blender-npr-recreating-the-genshin-impact-shader) is the reference. The characters are our own original designs, and our generator makes them from modular parts.

The look comes from a dozen tricks, most of them faked for art direction rather than computed (see [The reference look](#the-reference-look-element-by-element)). Three things have to be true to generate it:

1. **The shader does every trick (G1).** This is engineering work, and it can be proved on the stand-ins with procedurally made textures before any art exists.
2. **Parts are authored so the generator can recolour them (G3).** The reference paints each character's textures and shadow ramps by hand, and a generator can't. So painted detail is kept separate from colour, and each character's ramps are baked from artist-made templates and its DNA colours.
3. **There are enough parts at that quality.** This is the real cost. One original hero character comes first (G2): it proves the look and measures how long a part takes, and the catalogue plan is sized from that number.

**Order of work:**
- **G0:** frame the picture.
- **G1:** reach shader parity on the stand-ins.
- **G2:** one original character at the bar (overlaps G1).
- **G3:** conventions, tools and the parts catalogue.
- **G4:** the generator tuned to the style.
- **G5-G7:** environments, presentation, performance.
- **Track V** (judging and checks) runs throughout.

**Keep our own shader and extend it.** It already does what no off-the-shelf toon shader does: DNA recolour regions, patterns, body-region hiding, SRP Batcher material instances and the LOD pass toggles. Its painted base texture already multiplies the region colours, which is the hook item 2 needs.

## Progress

**2026-10-18: G0.1-G0.3, G1.3, V1 and V2 are done. G1.1 has its shader half.**

- **G0.1, honest captures.**
  - `GameCamera` sets up every camera (town, creator, menu, portrait studio, review sheets): post on, MSAA from the pipeline asset.
  - Captures render into targets with the pipeline's sample count. Before, they used 4× while the game had none, because a camera that renders into a texture takes its MSAA from the texture.
  - Not done: the pixel diff against a player-build screenshot (the batch-mode player doesn't screenshot). The shared setup is the guarantee instead.
- **G0.2:** MSAA 4× on PC (`RenderingSetup.Apply`). URP records it in the PC quality level too.
- **G0.3, post-processing.**
  - One profile, `Settings/GamePost.asset`, on the pipeline assets rather than a Volume per scene. It reaches every camera, including the portrait studio and review sheets.
  - No tonemapping, which keeps the authored colours; Neutral vs a LUT is still **[Art to decide]**.
  - Bloom above sunlit white (threshold 1.3).
  - The vignette moves to G0.4's per-scene volumes: on the shared profile, every portrait and review tile got its own dark corners.
  - Not yet judged A/B, because V3 isn't built.
- **G1.3, no self-shadowing.**
  - One environment-shadow sample per character (`CharacterShadowAnchor`, decision 0002 item 5).
  - `ShadowCheck` passes at 0% wrong, against 30% and 52% for the old per-pixel shadows.
  - The blocky hair patches (baseline 6) are gone from the town captures.
- **G1.1, lightmap input (shader half).**
  - `_LightMap` on the toon shader (layout in `CharacterToonInput.hlsl`): R, the shadow bias, moves the toon step's threshold by up to ±0.5, for the main light and lamps. G, B and A wait for G1.5, G1.6 and G1.2. `ToonMaterial.SetLightMap` sets it.
  - Unset, it's Unity's `linearGrey`, which reads just under 0.5 (treating 128/255 as neutral darkened 1.8% of the stand-in part sheet's pixels along the shadow edge); the Blender scripts write 0.5 as 128. Anything within one 8-bit step of 0.5 counts as neutral, so the stand-in part sheet renders pixel-identical: 0 of 9.5 M pixels differ.
  - Proved on the scripted character (decision 0009), whose parts carry G1.1 lightmaps: `Docs/review/scripted-lightmap.png` (row 1 without lightmaps, row 2 with, row 3 marks what moved, row 4 another colourway). The hair layers fall into shade sooner at every sun angle. At first the body and jacket lightmaps were nearly flat, because they were baked in the rest pose with the arms out. They're now baked in the relaxed pose, with the body occluding the garments and painted fold strokes added (decision 0009). So the shadow edge also moves at the armpits, the folds, and the collar under the jaw, where the high collar leaves the jaw shadow its only room.
  - `ToonShaderTests` checks the shader stays SRP Batcher compatible, using Unity's internal check (the one the shader inspector shows), against a built-in shader as a control.
  - Not done: procedural lightmaps for the stand-ins (the generator half of the row).
- **V1:** `DiagnosticSheet` renders all 70 parts in 17 s, and two runs give identical images (0 pixels differ). The sheets are regenerated on demand and kept out of git.
- **V2:** rubrics in [Docs/review/rubrics](review/rubrics/README.md). Every R element and baseline problem has a question.
- **Benchmark:**

  | Measure | Before | Now | Change |
  |---|---|---|---|
  | Render | 10.46 ms | 10.22 ms | −0.24 ms |
  | With CharacterLod | 9.66 ms | 9.88 ms | +0.22 ms |
  | Shadow cost | 2.2 ms | 0.6 ms | −1.6 ms |

  Within G0's +1.5 ms budget. Most of the anti-aliasing and post cost is paid for by the cheaper shadows.

## The reference look, element by element

As the article observes the game behaving; this isn't from HoYoverse documentation.

| # | Element | What the article shows | How we build it | Step | Today |
|---|---|---|---|---|---|
| R1 | Cel shading with painted shadows | Shadows under arms and necks, on seams and folds, and the anime shading in hair are painted into the textures and work like ambient occlusion | A per-part lightmap texture whose shadow-bias channel moves where the shadow edge falls | G1.1 | Two-tone, no textures |
| R2 | No self-shadowing | Shading comes from the light direction only; a hat brim or an arm doesn't shadow the character | Characters stop receiving the shadow map | G1.3 | They receive it (the likely cause of the hair patches) |
| R3 | No partial shadow | In a building's shadow the whole character goes dark, even standing on the edge | Sample the shadow map once per character, at a point beside the head offset toward the light, and apply the result to the whole character | G1.3 | Per pixel |
| R4 | Fake subsurface scattering | A faint warm gradient on the lit side of the shadow edge and a reddish one inside it; different by day and night; shadow ramps per character | A ramp atlas per character with day and night rows, baked from ramp templates and the DNA colours | G1.2, G4.1 | One hand-picked shade colour |
| R5 | Face light map | Face shadows come from a light map that shifts and mirrors as the light moves around the head; no subsurface bands on the face | An SDF face map per head base, sampled with the light direction in head space and mirrored by side | G1.4 | None |
| R6 | Metals | A matcap plus a real-time specular, with adjustable roughness | A matcap sampled with the view-space normal, roughness by mip level, masked by the lightmap's metal channel, tinted by the region colour | G1.6 | None |
| R7 | Specular | A stepped specular on some non-metals too | A stepped specular masked by the lightmap's specular channel | G1.5 | None |
| R8 | Hair shine | The painted shine marks also mask a live specular | The hair lightmap's shine mask; a highlight that moves with the view inside it | G1.5 | A painted band (vertex region 3) |
| R9 | Matcap patterns | Some fabric patterns shade as the camera turns | Optional: our pattern tiles modulate the matcap | G1.6 | Flat patterns |
| R10 | Outlines | Thin, dark inverted hull; some materials tint their line | Keep the inverted hull; line colour from the darkened region colour; taper and distance scaling | G1.8 | Constant pixel width, one tint |
| R11 | Rim light | Even thickness on both sides; ignores the mesh shape; reads like a second outline | A screen-space depth rim: compare scene depth at a small screen offset along the normal; colour from the lighting preset | G1.7 | Fresnel rim, lit side only |
| R12 | Glow | Bright highlights bloom | Bloom with a high threshold, plus an emission mask | G0.3, G1.9 | None |

## What generating this look changes

The reference gives every character bespoke textures and a ramp of their own. Our generator combines parts and recolours them, so:

1. **Colour is kept apart from detail.**
   - Each part's painted *detail* texture is mostly value: light and dark, hair strands, folds, seams, shine marks. It multiplies the DNA colour of the four recolour regions; `_BaseMap` × region colour already works.
   - Fixed-colour details (embroidery, metal trims, prints) sit in a region the generator never recolours.
2. **Ramps come from templates.**
   - Art paints one ramp template per material type (skin, hair, cloth, leather, metal, eyes), with day and night rows. A template is the band positions plus hue, saturation and value shifts.
   - The builder bakes each character's ramp atlas from the templates and the DNA colours, as the skin palette does today. The hand-picked skin shades stay as the ends of the skin template.
3. **Each part gets a lightmap texture:** a shadow bias (R1), a specular mask (R7, R8), a metal mask (R6) and a material type that selects the ramp row (R4).
4. **Face maps belong to a head base, not a character.** Face sliders stay within the range where a map holds, which G1.4 measures.
5. **Palettes and archetypes are tuned to the style.** Saturated accents over harmonious neutrals. Colour harmony is extended to cover shadow hues, and the skin-contrast rule to cover the ramps, so a shaded garment never reads as bare skin.
6. **Detail density.** The look leans on trims, layers and accessories. The slots already cover accessories. **[Art to decide]** whether costumes stay modern-town as now or go ornate. The plan works for both, but ornate means more parts per character.

## Guardrails

- **The style and the techniques are fair game; HoYoverse's assets and characters aren't.**
  - Don't import datamined or official models or textures. The article's demo uses an official model, which its author credits to miHoYo.
  - Don't copy specific character designs.
  - Game screenshots are fine as internal reference on the mood board and in the judge's reference frames. They never ship.
- **The article's own EEVEE shader** is sold as a fan work for original personal projects. Check its licence before using it even as a preview tool, or build our own Blender preview (G1.11).

## Baseline before G0

| # | Problem | Seen in | Cause |
|---|---|---|---|
| 1 | Jagged edges and outlines in play | Any live frame | MSAA off (`Settings/PC_RPAsset.asset:28`), and every camera has AA set to None. Review renders use 4× MSAA render textures (`Review/Snapshot.cs:30`, `TownCapture.cs:99`), so **the review PNGs look better than the game**. |
| 2 | No tonemapping, bloom or colour grading | Everything | No Volume in any game scene, and no URP camera data, so post is off. |
| 3 | Characters and sets look like two different games | [town-greet](review/town-greet.png), [town-bar](review/town-bar.png) | Sets are primitives with URP/Lit, smoothness 0.5 and specular highlights (`Town/TownSets.cs`, `Materials/TownProp.mat`). There are no outlines, no toon bands and no textures. |
| 4 | No sky, horizon or atmosphere | [town-park-night](review/town-park-night.png) | The camera clears to a solid colour (`TownStage.cs:517`), and fog is off in every scene. |
| 5 | Flat, low-contrast lighting, worst at night | town-park-night | Presets are hard-coded twice (`LightingRig.cs`, `TownStage.cs:464-543`) and switch with hard cuts at 7, 17 and 20 h. Ambient is flat. The lamps cast no shadows. |
| 6 | Blocky dark patches on hair at town distance | town-greet, town-bar | **Hypothesis:** the hair shells shadow each other. The bangs sit 3 mm outside the crown, the shadow map is 2048 px over 4 cascades and 50 m, and the shader snaps shadow attenuation hard (`CharacterToon.shader:120`). R2 removes the cause. |
| 7 | Face details don't scale with distance | town-greet (the blush hatching reads as dirt) | Faces are 3D strokes with no distance falloff (`StandInBody.cs:61-76`). |
| 8 | Brows and lines vanish on darker skin | [expressions](review/expressions.png), rows 3-4 | There's a contrast rule for clothing against skin, but none for facial features. |
| 9 | Eye whites go lavender in shade | Decision 0002 | Eyes use the same toon shading as skin. |
| 10 | Crude bodies: no hands, unwelded tubes, faceted clothes, blob faces | Every sheet | Procedural stand-ins, as expected until real art. |
| 11 | Default font and plain flat panels | HUD | The UI theme imports only the default theme. |
| 12 | Draw-call heavy | [perf.md](review/perf.md) | 290 renderers for 40 residents; the outline pass is ~42% of render time (10.5 ms → 6.0 ms without it). |

## Look and principles

- **Characters:** the reference look (R1-R12). Original designs. Adult proportions, as the generator already enforces.
- **Environments:** **[Art to decide]**. Suggested: the reference game's painterly stylised world (soft gradients, painted textures, atmospheric depth), slightly desaturated so characters stay the most saturated, highest-contrast thing in frame.
- **Light:** colour-scripted presets for day, sunset, night, rain and interiors. They feed the ramps' day and night rows (R4).
- **Mood board before G2 starts:** 10-20 frames. Reference-game frames for each R element (internal only), plus our own direction for costumes, environments and UI.

**Principles**

1. **Characters are the hero.** Environment shading, fog and grading are tuned so people stand out.
2. **Light is art-directed,** not physical.
3. **Review renders show the game as it plays:** the same AA, post and lighting as the player build.
4. **Every step is measured:** a judge A/B verdict, metrics and `TownBenchmark` (track V), with the tests kept green.

## Build or buy

| Option | Fit | Verdict |
|---|---|---|
| Our `Game/Characters/Toon` (HLSL, 5 passes) | Has the DNA recolour, patterns, region hiding, LOD toggles and SRP Batcher instances | **Keep and extend** to R1-R12 |
| [URPSimpleGenshinShaders](https://github.com/NoiRC256/URPSimpleGenshinShaders) | URP reference for face light maps, ramps and hair | Study for G1 |
| [Unity Toon Shader](https://docs.unity3d.com/Packages/com.unity.toonshader@0.15/manual/index.html) (0.13+ needs Unity 6) | Full-featured, but would need forking for our regions and patterns | Study its ramp, rim and highlight code |
| lilToon / MToon | Avatar-oriented; same forking problem | Study |
| NiloToonURP | Strong anime results; source only on request | Not viable |
| [Ben Ayers' EEVEE shader](https://www.artstation.com/marketplace/p/9nrrR) | Blender-only; useful to artists as a look preview | Licence check first; otherwise build our own preview (G1.11) |
| [Linework](https://marketplace.unity.com/packages/vfx/shaders/linework-294140) / [Toon Shaders Pro](https://danielilett.itch.io/toon-shaders-pro-for-unity-urp) (Render Graph, Unity 6) | Edge-detection outlines for **environments** | Decide in G0.6: buy one, or write a small depth-normals edge pass |

## Cross-check: 3D generation and judging technologies

This checks a candidate list of 32 technologies against this project. The deciding constraints are:

- Characters are modular parts on one shared rig, with transferred body shape keys, recolour region masks and toon-friendly normals (decisions 0001, 0005).
- Everything visual must meet R1-R12 and the style bible.

| # | Technology | Verdict for this project | Where |
|---|---|---|---|
| 1 | Blender Python (bpy) | **In use.** `Tools/Blender/character_tools.py` (rig, weight and shape transfer, validator, FBX export); Blender 5.2 is installed. Extend it. | G1.11, G3.2, G3.3, V1 |
| 2 | Three.js | Skip. The game is Unity; a web viewer adds nothing the review sheets don't. | — |
| 3 | OpenSCAD / CadQuery | Skip. Their output has no UVs, vertex colours or outline normals. | — |
| 4 | Unity / Godot scripting | **In use** (Unity C#). Godot doesn't apply. | All |
| 5 | Blender MCP | Optional, for live sessions with an artist. Anything that must repeat ends up as a bpy script. | G2, G3 |
| 6 | Blender MCP forks | Asset search and generation: environment props only. Auto-rigging: **no**, because parts use the shared rig. Text-to-animation: outside this plan. | G5.1 |
| 7 | Unity MCP | Not set up (`unity pipeline list`: no Pipeline package). Optional, for live lighting tuning with art watching. | G0.4 |
| 8-12 | Hunyuan3D, TRELLIS, Tripo, Meshy, Rodin | **Not for characters.** They can't produce rigged, region-masked parts with painted lightmaps (R1-R8). **Yes for environment props** after cleanup. Local models mostly target NVIDIA CUDA, and the dev machine has an AMD card; hosted ones need their commercial terms checked. | G5.1 |
| 13 | Poly Haven | Yes, as CC0 base meshes for props. Its HDRIs and PBR textures don't suit art-directed toon light. | G5.2 |
| 14 | Sketchfab | Low priority. Per-model licences need a licence log. | G5.2 |
| 15 | Concept image first, then image-to-3D | Props: yes. Characters: concept and turnaround first, then modelled by hand. | G2.1, G5.1 |
| 16 | LLM does layout, materials and code; a generator does organic shapes | Matches the split here. | G5.3 |
| 17 | Constrained space (primitives, fixed camera, style guide) | **In use** (the stand-ins and review sheets). It's also how G1 proves the shader before art exists. | G1 |
| 18 | Fixed orthographic camera, scripted renders | Partly. Add orthographic front, side and top. | V1 |
| 19 | Rubric of checkable questions | **Add,** with one question per R element. | V2 |
| 20 | Diagnostic render set | **Add** three-quarter, silhouette, wireframe and normals views. | V1 |
| 21 | Reference side by side | **Add.** Reference frames in the target style (internal only). | V5 |
| 22 | Pairwise A/B, not scores | **Add.** | V3 |
| 23 | Separate judge with fresh context | **Add.** | V3 |
| 24 | Tiled crops | **Add** 2× crops of faces, hair and the shadow edge. | V1 |
| 25 | Actionable output | **Add** as the judge's report format: defect, location, fix. | V3 |
| 26 | Calibration set | **Add,** seeded from before/after pairs in the git history of `Docs/review`. | V4 |
| 27 | Polycount, non-manifold, flipped normals | The triangle budget is checked now; add the rest for real art. | G3.3 |
| 28 | Scale and bounding box | Add a bounding-box envelope per slot. | G3.3 |
| 29 | Palette adherence by histogram | **Add,** for "characters are the hero" and for palettes. | V6, G4.2 |
| 30 | Silhouette overlap with reference mask | Add: part vs concept turnaround, and a regression check. | G3.4, V6 |
| 31 | Texture resolution, UV stretching | Add, together with the lightmap channel checks. | G3.3 |
| 32 | Known LLM weak spots | Proportions, counts, contrast, clipping, consistency and light behaviour go to metrics. The judge only gets questions a look can answer. | V2, V6 |

## Track V: judging and checks (alongside G0, ~1 week, then continuous)

| Step | Action | Tool | Inputs | Outputs | Acceptance check |
|---|---|---|---|---|---|
| V1 | Extend the review sheets into a diagnostic set: orthographic front, side and top, a three-quarter beauty shot, a flat silhouette, wireframe, mesh normals and outline normals, a light turntable, plus 2× crops of faces, hair and the shadow edge | Unity C# review sheets, run with `-executeMethod` | Catalog parts, lighting presets | `Docs/review/diagnostics/<part>/*.png` | Every catalog part renders all views. A re-render differs by under 0.5% of pixels. |
| V2 | Write a rubric per sheet type: yes/no questions, each tagged *look* (the judge answers) or *metric* (a script answers) | Markdown in `Docs/review/rubrics/` | R1-R12, the baseline problems, the style bible | One rubric per sheet | Every R element and baseline problem has at least one question. Each question is answerable from one image or one number. |
| V3 | A judge with fresh context compares A and B renders pairwise and lists defects before its verdict | A Claude subagent given only the images, the rubric and the reference | A/B renders, rubric, reference frame | A report per change: defect, location, fix, then A, B or tie | At least 80% agreement with art's labels on the calibration set (threshold confirmed in V4). Every defect names a tile. |
| V4 | Build a calibration set: 20-30 renders labelled good or bad with a reason | git history, art team | Before/after pairs in history, new renders, reference frames | `Docs/review/calibration/` plus `labels.csv` | At least 20 items, both classes, a reason per label. Agreement is recorded, and the rubric is revised where the judge disagrees. |
| V5 | Put the reference frame next to the render on the lighting sheet and turntables | Unity C# sheet | Mood-board frames (internal) | Paired sheets | Every preset and every R element has a reference frame. |
| V6 | Add metric scripts that run next to the sheets | Python (PIL, numpy) on renders and mask renders | Renders, a character-mask render, palettes | `metrics.json` per sheet | Each metric flags a known-bad item and passes a known-good one. See the metric list below. |

**V6 metrics:**
- palette distance,
- character-vs-background saturation and luminance separation,
- silhouette IoU against the previous run,
- brow-vs-skin contrast,
- hair patches (diff of shadow-on vs shadow-off renders),
- terminator band hue (R4),
- rim width in pixels at three distances (R11),
- line width in pixels (R10),
- face-shadow monotonicity over a light turntable (R5).

## Phases

Estimates assume 1-2 programmers and 1-2 artists, as in PLAN.md §7.

### G0: Frame the picture (engineering, ~2 weeks)

| Step | Action | Tool | Inputs | Outputs | Acceptance check |
|---|---|---|---|---|---|
| G0.1 | Make review renders use the player's AA and post settings | C# (`Snapshot`, `TownCapture`, `CreatorCapture`, `PortraitStudio`) | Pipeline asset | Honest captures; a note in the `Docs/review` README | A capture matches a player-build screenshot of the same shot (pixel diff under 2%) |
| G0.2 | Turn on MSAA 4× and set camera AA (none, or SMAA if alpha-cut edges show). No TAA: it ghosts hair and lines, and there's no motion-vector pass. | `PC_RPAsset`, camera setup code | — | Settings | No stair-stepping on outlines in the 2× crops. Benchmark recorded. |
| G0.3 | Add a global post Volume per scene: tonemapping **[Art to decide]**, high-threshold bloom (R12), colour adjustments and split toning per preset, a light vignette. Enable post on all cameras, and make portrait targets HDR or tonemapped. | URP Volumes, C# scene setup | Presets | Volume profiles | Judge prefers it A/B on the lighting sheet. Only emissive and specular pixels bloom (metric). |
| G0.4 | Turn the lighting presets into data (`LightingPreset`: sun, gradient ambient, sky, fog, rim colour, post, day/night ramp weight), shared by town, creator and portraits. Blend presets over half an hour. Add a lighting section to the style bible. | C#, ScriptableObjects | Today's literals | Preset assets; duplicates removed | Same look as before at each preset (pixel diff), then art tunes. No hard cuts across an hour boundary. |
| G0.5 | Add a gradient sky, distance fog, and emissive window and lamp glow at night | HLSL sky shader, URP fog | Presets | Sky material | Judge prefers it A/B. Characters-vs-background separation (V6) holds or improves. |
| G0.6 | Add a toon environment shader on the blockouts (banding, no specular, soft shade colour) and environment outlines (bought, or a depth-normals edge pass) | HLSL; Render Graph renderer feature | Blockout sets | Environment shader | Judge prefers it A/B. No URP/Lit left in game scenes (scripted check). |
| G0.7 | Set environment shadow distance to 20-25 m with 2 cascades; G1.3 fixes the character side | `PC_RPAsset` | — | Settings | Shadow texel density at least 4× today's. Benchmark recorded. |
| G0.8 | Rain and night: transparent soft rain, a darker wet ground, one shadowed key lamp per outdoor stage | C#, materials | Presets | Weather visuals | Judge prefers it A/B on the rain and night shots |
| G0.9 | Clean up: drop SSAO once sets are toon-shaded; turn off the unused opaque texture; fix PLAN.md §3.7 ("Shader Graph" vs HLSL) | Pipeline assets, docs | — | — | Benchmark is no worse. |
| G0.10 | Add a lighting sheet: four characters (light to dark skin) × Day, Sunset, Night, Rain and Interior, with post | Unity C# sheet | Presets | `Docs/review/lighting.png` | It exists and is deterministic. It becomes the main A/B target for G1. |

**G0 is done when:**
- Every step is accepted.
- `TownBenchmark` stays within +1.5 ms of today's 10.5 ms (editor, 40 residents), or the overage is justified.
- All tests pass.

### G1: Shader parity on stand-ins (engineering, ~5-6 weeks)

Every feature is proved on today's stand-ins before art exists. Their lightmaps and face map are generated procedurally, the same way the stand-in meshes are.

| Step | Action | Tool | Inputs | Outputs | Acceptance check |
|---|---|---|---|---|---|
| G1.1 | Add a lightmap texture input (shadow bias, specular mask, metal mask, material type), and generate procedural lightmaps for the stand-ins (darker bias at joints, under the hair and collar; a shine mask on the hair band) | HLSL; C# in the stand-in generator | Stand-in parts | `_LightMap` support; stand-in lightmaps | Painting the bias moves the shadow edge as expected on the turntable. Recolouring still works. SRP Batcher stays on. Tests pass. |
| G1.2 | Add ramp templates per material type (day and night rows), and bake a per-character ramp atlas from the templates and the DNA colours at build time (R4) | C# (builder, `ToonMaterial`), HLSL | Templates, DNA colours | A ramp atlas per character | The lighting sheet shows a warm band on the lit side of the edge and a reddish band inside it, for every skin stop and region colour (terminator-hue metric). Deterministic for a given DNA. The day/night row follows the preset. |
| G1.3 | No self-shadowing, and one environment-shadow sample per character (R2, R3) | HLSL; C# per-character anchor | Shadow map | — | The hair-patch metric reads zero. A test scene shows a character fully in or fully out of a building's shadow, never split. |
| G1.4 | Add SDF face shadows (R5), with a procedural SDF for the stand-in head; no ramp bands on the face. Measure which face-slider ranges a map survives. | HLSL; a C# or Python SDF generator | Head bone axes, light direction | The face-map feature; a stand-in face map | Over a 360° light turntable, the face shadow moves monotonically and mirrors correctly (metric). Slider limits recorded. |
| G1.5 | Add stepped specular, plus a hair shine that moves with the view inside the painted mask (R7, R8) | HLSL | Lightmap masks | — | On the view turntable, the highlight stays inside its mask and moves (judge plus a mask-overlap metric). |
| G1.6 | Add a metal matcap with roughness, tinted by the region colour; optionally modulate it by pattern tiles (R6, R9) | HLSL; matcap textures made in Blender | Metal mask | Matcap textures | Judge says metal accessories read as metal on the diagnostic set. Roughness visibly widens the reflection. |
| G1.7 | Add a screen-space depth rim, even width on both sides, coloured by the preset (R11) | HLSL with the camera depth texture (already enabled) | Depth texture, preset | — | Rim width is constant in pixels at three distances (metric). Visible on both sides. Judge prefers it A/B at night. |
| G1.8 | Lines: colour from the darkened region colour, width tapered per vertex, scaled with distance and dropped below about 1 px (replacing the hard 14 m cut), plus a Z-offset (R10) | HLSL; the vertex-colour convention | — | — | Line-width metric within spec. Outline cost falls at a distance (benchmark). |
| G1.9 | Eyes, emission and catch-lights: eye whites never shade below a floor; catch-lights unlit; an emission mask for bloom (R12) | HLSL | — | — | The eye-white luminance floor holds in every preset (metric). |
| G1.10 | Fade face detail with distance (hatching, marks), and add a brow-vs-skin contrast rule to the generator and `DnaValidator` | HLSL; C# with a sweep test | — | — | Contrast sweep passes. Hatching is gone in the town captures. |
| G1.11 | Build a Blender preview material that matches the Unity shader, generated by script, so artists see the look while painting | bpy node-group generator | The shader's features | `Tools/Blender/preview_material.py` | One test asset in Blender and Unity side by side: the judge finds no rubric difference. |
| G1.12 | Add a feature-toggle sheet: each R element off and on, side by side, next to its reference frame | Unity C# sheet; a decision record | Reference frames | `Docs/review/features.png`; decision 0008 (the shading model, superseding 0002's two-tone) | Every R element has a row. Art confirms each one reads like the reference. |

### G2: One original character at the bar (art-led, ~6-8 weeks; overlaps G1)

This is the vertical slice (PLAN.md Phase 1). One original character, built as modular parts, proves the look end to end and measures the art cost.

| Step | Action | Tool | Inputs | Outputs | Acceptance check |
|---|---|---|---|---|---|
| G2.1 | Concept and turnaround for an original character in the reference style | Hand painting (image models for thumbnails only, licence permitting) | Mood board | Front, side and back orthographic turnarounds | Art signs off. Judge rates it the same family as the reference frames, with no specific HoYoverse design copied. |
| G2.2 | Model the body, head, hair and outfit as separate parts on our rig: deforming topology, **hands**, face bones plus `expr.*` blendshapes | Blender, by hand; `character_tools.py` export | Turnaround, rig | `.blend` files, FBX with sidecars | Validator clean, including G3.3 checks. The clip test passes at slider extremes. |
| G2.3 | Paint textures to the convention (G3.1): value detail, recolour masks, lightmap channels; bake the face SDF | Blender, Krita or Substance; G3.2 helpers; G1.11 preview | Parts | Texture sets; face map | Validator texture checks pass. With the generator's colours swapped in, the judge says it still looks painted (A/B against original colours). |
| G2.4 | Author the ramp templates (skin, hair, cloth, leather, metal, eyes; day and night) | Painted PNG or ScriptableObject | Reference frames | Templates | Lighting sheet: judge rates it the same family as the reference on R4 for every skin stop. |
| G2.5 | Review in engine against the reference | Track V | Feature sheet, turntables, town captures | Judge reports | On at least 10 of the 12 R elements, the judge prefers ours or calls a tie against the reference frame. Art signs off. |
| G2.6 | Log the hours per part type (head base, hair, top, bottom, shoes, accessory, face textures) | Time log | — | `Docs/art-cost.md` | Every part of the slice is logged. G3.5 sizes the catalogue from these numbers. |

### G3: Conventions, tools and the parts catalogue (engineering ~2-3 weeks; art ongoing)

| Step | Action | Tool | Inputs | Outputs | Acceptance check |
|---|---|---|---|---|---|
| G3.1 | Write the texture and material convention into the style bible: lightmap channels and value ranges, tintable vs fixed regions, texel density, material types, vertex-colour use (line width, shadow bias). Also rewrite §1 (today it describes a two-tone look) to the R1-R12 look. | Markdown | G2 learnings | Style bible §1 and §7 updates | The G2 character complies, and the validator enforces it. |
| G3.2 | Add bake helpers: AO and curvature into a starting shadow bias and specular mask, the SDF face-map baker (from painted shadow thresholds), normal transfer from a smooth proxy | bpy | Parts, painted thresholds | `Tools/Blender` tools | Painting time per part falls against the G2.6 baseline (logged). |
| G3.3 | Add validator checks: non-manifold, flipped normals, a bounding-box envelope per slot, UV stretch, texel density, texture channels present and in range, valid material types | bpy (`bmesh`) | Part `.blend` | A problems list | One seeded bad asset per check is caught; the G2 assets pass. |
| G3.4 | Check each part's silhouette against its concept mask | bpy orthographic render plus Python IoU | Part, turnaround | IoU per view | At or above art's threshold (start at 0.9). |
| G3.5 | Plan the launch catalogue: which parts, ordered by how many distinct looks each unlocks (from `GeneratorStats`), costed with G2.6 | Spreadsheet, generator stats | Today's 70 stand-in parts, G2.6 costs | A catalogue plan and schedule | Art and production sign off. The schedule fits the team, or the scope is cut. |
| G3.6 | Produce parts and replace stand-ins slot by slot | Blender, the pipeline | Catalogue plan | Parts | Validator clean. Judge prefers the new part on the QA lineup. All tests pass. |

### G4: The generator tuned to the style (engineering, ~2-3 weeks)

| Step | Action | Tool | Inputs | Outputs | Acceptance check |
|---|---|---|---|---|---|
| G4.1 | Wire the ramp templates into generation per material type and archetype (gothic might get cooler night rows) | C# generator, builder | Templates, archetypes | — | Deterministic. Tests pass. Lighting sheet judged. |
| G4.2 | Tune palettes and colour harmony for the style, including shadow hues; extend the skin-contrast rule to the ramps | C# generator, `GeneratorStats` | Palettes | Updated catalog | 10,000-character stats with 0 rule violations; palette-histogram metric within bounds. |
| G4.3 | Add detail-density rules: trims and accessories per archetype, within the performance budget | C# generator | Catalogue | — | Stats show the target accessory rate; benchmark holds. |
| G4.4 | Quality gate: the 96-character QA sheet judged against the reference frames and the calibration set | Track V | QA sheet | Judge report | Art signs off. Judge pass rate at or above the threshold set in V4. Lookalike rejection still holds. |

### G5: Environments (art ~4-6 weeks, engineering ~1-2 weeks)

Look: **[Art to decide]**, suggested above. Start with the three most visited places (Moonbean Café, Willow Park, Arcade Lane), then the rest.

| Step | Action | Tool | Inputs | Outputs | Acceptance check |
|---|---|---|---|---|---|
| G5.1 | Generate organic props: concept image, then image-to-3D, then a bpy cleanup (decimate to budget, weld, flat colours from the location palette, outline normals, pivot and scale) | Image model or art; one generator from Hunyuan3D, TRELLIS, Tripo, Meshy or Rodin (open question); bpy | Concept image, palette | Prop FBX and prefab | Validator passes. Palette distance under the threshold. Judge prefers it over the blockout prop. Licence logged. |
| G5.2 | Source base meshes from libraries through the same cleanup | Poly Haven (CC0); Sketchfab only with a licence-log entry | Search terms | Props | Every asset has a source and licence in `Docs/asset-licences.md`. |
| G5.3 | Build sets by code from kit pieces and a layout file; an LLM may propose the layout, and metrics check it | C# set builders (`TownSets`) | Kits, layout file | Sets | No prop covers more than 10% of a character in the wide shot or the two-shot (mask render). Judge prefers the new set at every preset. |
| G5.4 | Dress the sets: signage, backdrop and skyline cards, emissive windows and cookie lamps at night, wind sway on plants, wet weather | Art; C# hooks | Kits, presets | Finished stages | Characters-vs-background separation (V6) holds in every preset. |
| G5.5 | Decide baked lightmaps plus probes vs fully realtime, by benchmark | `TownBenchmark` | Stages | A decision record | Within the frame budget on min-spec. |

### G6: Presentation (~2-3 weeks)

- **Camera language** with Cinemachine (installed, unused): the wide shot and two-shot, a close-up on the speaker in dialogue, and a shallow depth of field in close-ups and on dates. The face light map and rim are made for close-ups.
- **Portrait and photo rig:** key, fill and rim instead of neutral daylight; post on; higher-resolution photos; booth strip frames.
- **Creator:** lighting presets (Studio, Day, Sunset, Night) per PLAN §3.9, a light turntable to show the face map, and a designed backdrop.
- **UI art direction:** a licensed font pair, a panel style, transitions, icons and portrait frames.

### G7: Performance and quality tiers (continuous; PLAN.md Phase 4)

- **New costs:** the lightmap, detail and mask textures per part; a ramp atlas per character; the depth-rim samples; matcaps. Budget texture memory per character, and use texture arrays or atlases.
- **Merge each character into a few renderers,** with per-character colours in a palette texture. This cuts the 290 renderers and the outline pass, which is the biggest cost (decision 0004).
- **Quality levels** that vary MSAA, shadows, line distance, rim, post and the environment edge pass.
- **Min-spec:** set the platform and min-spec (PLAN §10.1), then measure in a player build.

## Verification

- **Each change** lands with before/after renders, a judge report (V3: defects first, then the A/B verdict), its metrics (V6) and a `TownBenchmark` run. Art has the final say wherever the judge and the metrics disagree.
- **"Visibly better" is never the bar.** Done criteria are A/B verdicts, rubric answers and metric thresholds.
- **The R elements are the spec.** Each has a rubric question, a reference frame, and a metric where one is possible.

## Risks

| Risk | Mitigation |
|---|---|
| **Art cost.** Reference-quality parts take far longer than stand-ins, and the generator's variety depends on how many there are. | Measure it in G2.6; rank parts by the looks they unlock (G3.5); bake helpers (G3.2); cut scope early rather than lower the bar. |
| Recoloured parts look flat next to hand-painted references | Paint value detail, keep fixed-colour regions, use ramp templates; A/B with swapped colours in G2.3. |
| Looking too close to the reference (IP) | Original designs only; no imported assets; references stay internal; legal review before marketing (open question). |
| Face maps break when face sliders reshape the face | Measure slider limits in G1.4; one map per head base; bound the sliders. |
| Frame time grows (MSAA, post, extra textures and passes) | Measure each step; distance-scaled lines; renderer merging and texture arrays (G7); quality tiers. |
| The judge drifts from art's taste | Calibration set with measured agreement (V4); defects before the verdict; metrics for what models judge badly; art has the final say. |
| Generated props look off-style, or carry licence problems | Cleanup to palette and toon normals; A/B against the blockout; licence log; generators for props only. |
| TAA ghosting on hair and lines | MSAA, no TAA. |

## First steps

1. **G0.1-G0.3** (honest captures, MSAA, post), with **V1-V2** (diagnostic set, rubrics with one question per R element).
2. **G1.3** (no self-shadowing, one shadow sample per character). It's central to the look and fixes the hair patches, so it doesn't wait for the rest of G1.
3. **G0.4-G0.10**, then **G1.1-G1.2** (lightmaps, ramps), which unlock most of the look.
4. **In parallel (art):** the mood board with reference frames per R element, and the G2.1 concept for the original hero character.

## Sources

- [Ben Ayers, *Blender NPR: Recreating the Genshin Impact Shader*](https://bjayers.artstation.com/blog/9oOD/blender-npr-recreating-the-genshin-impact-shader): the reference for R1-R12.
- [URPSimpleGenshinShaders](https://github.com/NoiRC256/URPSimpleGenshinShaders): face light maps and hair in URP.
- [HoYoverse at Unity Dojo (Japanese slides)](https://docswell.com/s/UnityJapan/KWRPQ5-210617-unity-dojo20211mihoyozhenzhongyi).
- [GuiltyGearXrd's Art Style, GDC 2015](https://gdcvault.com/play/1022031/GuiltyGearXrd-s-Art-Style-The) and [Motomura's notes (PDF)](https://www.ggxrd.com/Motomura_Junya_GuiltyGearXrd.pdf): edited normals and vertex colours for clean cel shading.
- [Unity Toon Shader manual](https://docs.unity3d.com/Packages/com.unity.toonshader@0.15/manual/index.html) and [changelog](https://docs.unity3d.com/Packages/com.unity.toonshader@0.13/changelog/CHANGELOG.html).
- [Introduction to URP's Render Graph in Unity 6](https://80.lv/articles/introduction-to-urp-s-render-graph-in-unity-6/).
- Outline options: [unity-urp-outline](https://github.com/unitycoder/unity-urp-outline), [Linework](https://marketplace.unity.com/packages/vfx/shaders/linework-294140), [Toon Shaders Pro](https://danielilett.itch.io/toon-shaders-pro-for-unity-urp).
- Generators and libraries: [Hunyuan3D-2 (licence)](https://huggingface.co/tencent/Hunyuan3D-2/blob/main/LICENSE), [TRELLIS](https://github.com/microsoft/TRELLIS), [Tripo](https://www.tripo3d.ai/), [Meshy](https://www.meshy.ai/), [Rodin](https://hyper3d.ai/), [Poly Haven](https://polyhaven.com/), [Sketchfab](https://sketchfab.com/).

## Open questions

1. **Costume direction.** Stay modern-town (cafés, gym, bar) as now, or go ornate like the reference's fantasy costumes? The rendering transfers either way, but ornate means more parts per character and a higher art cost.
2. **Art capacity and launch catalogue size.** The plan sizes the catalogue from G2.6, but how many artists, for how long?
3. **How close to the reference.** How close to the reference game do we want to look, and when does legal review the designs and marketing?
4. **The article's EEVEE shader.** Buy it for artists' previews (its licence must allow commercial work), or build our own (G1.11)?
5. **Generator hardware and licences.** For props only:
   - Local generators mainly target NVIDIA, and the dev machine is AMD on Windows.
   - Hunyuan3D-2's licence is source-available and excludes the EU, UK and South Korea.
   - Hosted services' terms vary by plan.
6. **Calibration and judging.** Who labels the V4 set (about 1-2 hours of art time)? What agreement threshold do we use, which judge model, and when is a human review required anyway?
7. **Unity MCP and Blender MCP.** Optional. Install them, and which Blender MCP server?
8. **Tonemapping and the other [Art to decide] items.**
9. **Platform and min-spec** (PLAN §10.1). The G5.5 and G7 budgets depend on them.
