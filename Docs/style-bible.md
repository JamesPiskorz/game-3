# Character style bible

_Draft v0 · 2026-10-18._

Everything here is either **enforced** (code checks it: the Blender validator, the importer, the generator's tests) or **convention** (what the stand-ins do and what the systems assume). Items marked **[Art to decide]** are calls for the art team. This draft deliberately stops short of taste: references, mood boards and per-archetype looks are theirs.

Related: [PLAN.md](../PLAN.md) §3 and §4; decisions [0001](decisions/0001-runtime-modular-assembly.md) (assembly), [0002](decisions/0002-character-toon-shading.md) (shading), [0003](decisions/0003-spring-bones.md) (springs), [0005](decisions/0005-blender-unity-pipeline.md) (pipeline).

---

## 1. The look

3D stylized anime. Two-tone toon shading with hand-picked shade colours, constant-width ink outlines, and flat colour regions that the generator recolours. Characters read from a distance by **silhouette and colour blocks**, not texture detail.

**Everyone is an adult and must read as one.** This is a hard constraint (PLAN §1). Proportion sliders are clamped so no combination looks childlike, and art must hold the same line at every slider extreme.

**[Art to decide]:**
- Reference sheet and mood board.
- Line weight feel, how much hair and clothing silhouette exaggeration, and eye size relative to the face.

## 2. Units, axes and the base body (enforced)

| | |
|---|---|
| Units | Metres. 1 Blender unit = 1 m = 1 Unity unit. |
| Axes | Y up. The character faces **+Z**, and the character's left is **−X**. |
| Rest pose | T-pose, arms along ±X. Rest bone rotations are **identity** in Unity (the exporter bakes space transforms). |
| Height | The stand-in crown sits at about 1.75 m with a head radius of 0.11 m, so about 7.9 heads tall. Rule for art: at least 7 heads at every slider extreme (not yet checked by code). |

### Skeleton (bone names are an interface; parts must use exactly these)

| Group | Bones |
|---|---|
| Body | `Hips` (root), `Spine`, `Chest`, `Neck`, `Head` |
| Face | `Eye.L`, `Eye.R`, `Brow.L`, `Brow.R`, `Nose`, `Mouth` |
| Arms | `UpperArm.L/R`, `LowerArm.L/R`, `Hand.L/R` |
| Legs | `UpperLeg.L/R`, `LowerLeg.L/R`, `Foot.L/R` |

- **Part-only bones** (hair tails, dangling accessories) hang off an existing bone and are named `Hair.<Name>.<n>`, for example `Hair.Ponytail.1..3`. They are grafted at equip time and simulated as spring chains (decision 0003).
- Head colliders use radius 0.108 m. Tails must clear it at rest.

### Body shapes (enforced for anything that follows the body)

`body.weight`, `body.chest` and `body.hips` are blendshapes on the body, each 0..1.

- Every part that touches the torso or legs **must carry the same three shape keys**. The Blender transfer tool copies them from the body.
- The validator rejects a body-following part that's missing any of them.
- Hair and accessories set `followsBodyShapes: false`.

Other proportions are bone sliders the art doesn't need to carry: height, shoulders, legs and head size.

**Range:** `Docs/review/body-range.png` shows every body slider at its minimum, average and maximum on a feminine and a masculine character. The stand-in weight shape adds 40% to the radius of everything fleshy (and a little at the belly): at 22% the heaviest builds didn't read at town distance. The generator's weight spread is 0.28, so about one character in ten has a clearly fuller build. [Art to decide] the real range.

### Body regions (enforced)

The body's vertex colour **R channel = region ID / 255**. Parts declare which regions they **cover**, and the shader hides covered skin, so there's no poke-through and no hidden skin is shaded.

`Head, Neck, UpperTorso, LowerTorso, Pelvis, UpperArmL/R, ForearmL/R, HandL/R, ThighL/R, CalfL/R, FootL/R`

## 3. Parts

### Slots

| Slot | Notes |
|---|---|
| `top`, `bottom` | Required unless a one-piece is worn. |
| `onepiece` | Blocks `top` and `bottom`. |
| `shoes` | Required. |
| `outer` | Jackets and coats, over any top. Tops tagged `hooded` or `tailored` don't go under one. |
| `legwear` | Tights and socks, right on the skin. Requires a `bare_legs` piece (skirts, dresses, shorts), so it's never hidden under trousers. |
| `hat` | Requires hair tagged `hat_ok`. |
| `glasses`, `earrings` | Belong to the person: the same pair in every outfit. |
| `neck`, `bag` | Scarves and necklaces; a crossbody bag. Per outfit. |
| `headpiece` | Ribbons, headbands and hair clips, sitting just outside the hair. Not with a hat (hats carry a `hat` tag). Not named `hair.…`: that prefix means a hair piece. |
| `face.freckles`, `face.mole`, `face.blush` | Marks on the skin, worn whatever the outfit (stored in the face DNA's makeup list, not in outfits): freckles across the cheeks, a beauty mark by the eye or the lips, a blush oval on each cheek. They sit just proud of the head along its normal, have no outline, and take their colour from the builder: freckles and beauty marks are a deeper shade of the skin, blush is its own colour blended 70% over the skin. |
| `hair.back`, `hair.front` | Hair is split into crown plus length, and bangs. Every piece is offered at the salon by its `name`. |

### Layering (enforced by review, measured in metres off the body surface)

Each layer sits further out than everything that can be worn under it:

| Layer | Stand-in offset |
|---|---|
| Legwear | 0.003 to 0.004 |
| Skirt and dress waistbands | 0.006 |
| Tops, trousers, shorts, shoes | 0.012 to 0.022 (the hoodie is the bulkiest top) |
| Boots | 0.016 |
| Outer layers | 0.034 to 0.036 |
| Things that lie on top of everything (a scarf's hanging end, a bag strap) | in front of an outer layer's surface |

- An open-fronted jacket leaves its front out of the mesh, so the top shows through. Real jackets should do the same, not rely on the top poking through.
- A coat's skirt must be wider than every skirt and dress down to its own hem. Longer skirts may show below it.
- **Lesson from the stand-ins:** the scarf's hanging end poked through coats until it was moved out to lie over them.

### Budgets (enforced by the Blender validator)

- **12,000 triangles per part at most.** Aim far lower: the town shows many characters (decision 0004).
- **At most 4 bone influences per vertex**, normalised.
- **No vertex follows a bone more than 0.35 m away.** That catches stray weights, like a scarf following a hand.
- Every part has the `Col` colour attribute (the recolour mask), even if it's all one region.

### Recolour regions (enforced format, conventional meaning)

Up to four recolour regions per material, plus an un-recoloured base. They come from an RGBA mask texture, or, for procedural stand-ins, vertex colour G/B/A plus UV1.x.

| Region | Clothing convention | Body convention |
|---|---|---|
| Base | (unused on clothes) | Skin |
| 1 | Main fabric | Iris |
| 2 | Trim and accents: cuffs, hems, collars, soles, lapels, pockets | Brows |
| 3 | Secondary panel: skirt band, lining | Eye whites |
| 4 | Rare detail | Lips |

- A part's catalog entry says how many regions it uses (`"regions": 2`), and the generator colours exactly that many.
- **Lesson from the stand-ins:** detail narrower than the mesh's vertex spacing smears when painted into vertex colour. Lapels, narrow stripes and piping should be **geometry or a mask texture**, not vertex paint.

### Patterns

- Patterns tile over **region 1 only**, in the shader, with **UVs laid out in metres** (1 UV unit = 1 m of fabric). That way the same pattern scale reads the same on every garment.
- Put UV seams where a pattern mismatch won't be seen: side seams and inseams.
- Patterns are greyscale tiles in `Content/Patterns`; the catalog sets their tiling range per metre.

### Fit (convention)

- Test every garment at the **shape extremes** (weight, chest and hips at 1) and at slider extremes.
- Details that sit on a garment must move out with it. **Lesson from the stand-ins:** blazer lapels without the chest shape sank into the jacket on fuller figures.
- Review with the **part sheet** (every part on one model, front and back) and the **96-character quality sheet**.

### Naming and catalog entry (enforced)

IDs are `category.slot.name_variant`, for example `cloth.top.hoodie_01`, `hair.back.wavy_01` or `acc.hat.cap_01`. Each part needs a catalog entry in `Content/Catalog.json`:

| Field | Meaning |
|---|---|
| `name` | Shown in the creator and the boutique. |
| `slot`, `blocksSlots` | See above. |
| `tags` | Archetype styles it suits (`casual`, `sporty`, `cute`, `gothic`, `elegant`, `street`, `bookish`), contexts (`date`), and rule tags (`hat_ok`). |
| `lean` | −1 (masculine-coded) to +1 (feminine-coded). It **weights** picks and never forbids them. |
| `regions`, `patternable` | Recolouring. |
| `contexts` | Outfit contexts it may appear in (empty = any). |
| `tags` with rules | `hat_ok` (hair a hat can sit on), `bare_legs` (legs show, so legwear can go with it), `hooded` and `tailored` (too bulky or structured to go under a coat). |
| `requiresTags`, `excludesTags` | Compatibility rules, for example a hat needs `hat_ok` hair. |
| `weight` | Relative pick frequency. |

A test fails if any part, style or pattern never shows up in 3,000 generated characters, or if a garment appears in fewer than 0.5% of outfits. `Docs/review/generator-stats.md` shows how often every part is worn, per context, over 10,000 characters.

### Outfit contexts

Every character has an outfit per context, all generated in their style. The catalog defines them:

| Context | Rule | Worn |
|---|---|---|
| `casual` | Everyday. | Most of the time. |
| `date` | Weights `date` and `elegant` pieces. | On dates, and evenings out somewhere romantic or social. |
| `rain` | `"outer": true`: a coat or jacket is picked first, and the outfit is built to go under it. | On rainy days. |
| `sport` | `"onlyTags": ["sporty"]`. | At the gym. |
| `work` | `"onlyTags": ["elegant", "bookish"]`. | At their job, during working hours. |

A part limited to some contexts (`contexts`) stays out of the others, in the generator and in the player's wardrobe. The catalog check refuses a context whose rules leave a required slot with nothing to wear.

### Per-archetype chances

Each archetype sets how often optional pieces appear: `hatChance`, `glassesChance`, `neckChance`, `outerChance`, `legwearChance`, `earringChance`, `bagChance` and `patternChance`. `faceChances` sets freckles, beauty marks, blush and lipstick per style (blush and lipstick are scaled by presentation, from 20% at the masculine end to 100% at the feminine end), and `lipPalette` picks the lipstick colours (gothic uses `makeup.lips.dark`). They draw from separate random streams, so adding a slot or changing a chance doesn't reshuffle anyone's other choices.

## 4. Faces

- **Catch-lights:** each iris has a small highlight, upper left as you look at them, the same side on both eyes, in the eye whites' colour. It rides the eye bone, so it closes with the eye. Anime eyes read as alive with one; real eyes should keep it.
- **Features:** eyes, brows, nose and mouth are separate features. On the stand-in they're rigid shapes on the face bones; the face sliders (eye scale, height, spacing and tilt; brow height and tilt; mouth width and height; nose size) and the eye and brow **styles** move and scale those bones.
- **Generation:** generated faces are designed **presets** plus small noise.

**Styles must read at a glance.** `Docs/review/face-styles.png` shows every eye style, then every brow style, on the same three faces. The stand-in styles were pushed apart until each column is recognisable (narrow eyes 64% height, tall 120%, cat and droopy tilted 16° and −14°, thick brows 185%, thin 50%); real eye and brow shapes should be at least as distinct.

**[Art to decide]:** the face design itself. Plan on:
- 6–10 presets,
- 5–8 eye shapes,
- 4–6 brow shapes.

**Expressions:** the game uses seven expressions plus a blink, layered on top of the face sliders: `neutral`, `happy`, `delighted`, `shy`, `sad`, `annoyed`, `surprised` (see `Docs/review/expressions.png`).

- **Proposal:** real heads ship these as blendshapes named `expr.happy`, `expr.delighted`, `expr.shy`, `expr.sad`, `expr.annoyed`, `expr.surprised`, `expr.blink.L` and `expr.blink.R`, each modelled at full strength.
- A curved mouth matters most. The stand-in fakes one with three mouth pieces whose corners turn (`Mouth.L`, `Mouth.R`), and that alone made happy and sad read at town distance. A real head should curve properly.

Recolourable face colours are iris, brows, eye whites and lips (body regions 1–4). Lipstick recolours the lips; freckles, beauty marks and blush are small parts in `face.` slots (see Slots). On real heads these could become texture overlays instead; the DNA stores them as overlay IDs with a tint either way.

- **Blush** on the stand-in is anime hatching, three strokes per cheek in the brow colour, pushed out through the skin by expressions (`Blush.L`, `Blush.R`). Blush as makeup is a separate pink oval (`face.makeup.blush_01`), and the hatching shows on top of it. [Art to decide] whether real heads keep both.

## 5. Hair

- **Pieces:** a back piece (crown and length) plus a front piece (bangs). Any back can pair with any front, so bangs must not assume a particular back, unless the back blocks `hair.front` (an afro has no bangs).
- **Textured styles:** back pieces tagged `textured` (the afro, puffs, locs) are drawn on their own seed stream at the archetype's `texturedHairChance` (default 12%), so adding one doesn't reshuffle anyone else's hair. Hair with real volume is tagged `big_hair`; hats and hair accessories stay off it.
- **Clean edges:** an edge cut across a sphere's rings comes out as a staircase. Cut level (along the rings) and rotate the piece instead (`StandInMesh.Rotate`), as the swept bangs and the afro's face opening do.
- **Hats:** a back piece that a hat can sit over is tagged `hat_ok`. Volume above the crown (buns, curls) means it isn't.
- **Tails:** ponytails, twin tails and side tails swing on spring chains. Model them straight down in rest pose.
- **Colours:** a base colour, a gradient to a tip colour (region 2 near the ends), and a highlight. The generator mostly picks natural palettes, with fantasy colours by archetype.

## 6. Colour

- **Skin:** eight hand-picked stops from light to dark, each with its own shade and rim colour, plus an undertone shift (decision 0002). Shade colours are chosen by hand, never just darkened: that's what keeps dark skin rich under toon shading.
- **Clothing vs skin:** torso and leg clothing must stay at least **0.2 HSV distance from the wearer's skin**, or it reads as bare skin. This is enforced in generation and covered by a sweep.
- **Outfits:** each archetype has a palette. The generator uses one accent plus neutrals, with the top and bottom contrasting.

**[Art to decide]:** the archetype palettes. The current ones are placeholders tuned for readability.

## 7. Shading and outlines (technical rules)

- **No normal maps on characters.** The tangent channel is reserved for **smoothed outline normals**, baked at import.
- **Outline widths** are per material at 1080p: cloth 1.4, hair 1.2, accessories 0.8. Outlines can't be thinner than a pixel, so very thin geometry (glasses frames) gets thin outlines.
- **Two-sided materials** are cloth and hair (backfaces visible inside sleeves and under skirts). Skin and accessories cull backfaces.

## 8. Pipeline checklist for a new part

1. **Model** in Blender on the shared rig (`Tools/Blender/character_tools.py`), in metres, facing +Z.
2. **Weights and shape keys:** transfer from the body with the tool, using a bone mask where needed (a scarf follows Neck, Chest and Head only).
3. **Recolour mask:** paint the `Col` attribute or author a mask texture. Lay out UVs in metres if the part is patternable.
4. **Validate** (the script exits non-zero on problems).
5. **Export** the FBX plus a `<id>.character.json` sidecar (`kind`, `slot`, `covers`, `blocks`, `springChains`, `followsBodyShapes`, and `small` for tiny details that should cast no shadow and lose their outline beyond a close-up) into `Content/Imported/`. The importer turns it into a part prefab.
6. **Catalog:** add the entry (§3).
7. **Review** in Unity:
   - **Game → Characters → Review → Render Part Sheet**
   - **Render Quality Sheet (96)**
   - **Render Generated Lineup**
8. **Test:** run the EditMode tests. Coverage, rules and the 10k-character sweep must pass.

## 9. Archetypes (current tags; [Art to decide] the looks)

`Docs/review/archetype-sheet.png` shows eight characters generated in each style, one row per archetype: the starting point for deciding each look.

| Archetype | Intent, as the systems use it |
|---|---|
| Casual | Everyday basics. Wide appeal; the default for neutral items. |
| Sporty | Athletic pieces: joggers, tanks, caps, sneakers. Outgoing personalities. |
| Cute | Soft shapes and pastels: sundresses, blouses, hearts and dots. |
| Gothic | Dark palettes, long skirts, boots, arched brows. Introverted, into music and art. |
| Elegant | Tailored pieces: blazers, long skirts, pinstripes. |
| Street | Hoodies, caps, bold contrast. Into music, fashion and games. |
| Bookish | Cardigan-adjacent: glasses, plaid, calm faces. Into books. |

### What the systems ask of characters

- **Families:** siblings share skin tone, natural hair and eye colour, and lean towards each other's face sliders, often from the same face preset. Presets need to blend well with one another (see `Docs/review/family-sheet.png`).
- **Makeovers and the salon:** residents change haircut, colour or everyday clothes over the weeks, and the player can restyle their hair. Any hair piece must work with any colour, and a hat that a new cut can't take is taken off.
- **First impressions** count hair and clothes by their style tags. A piece's tags are a gameplay statement, not only a filter.

## 10. Open questions for art

- The target triangle budget per character, and whether to aim lower than the validator's per-part ceiling.
- Whether faces should be fully texture-driven (decals for eyes and mouth) or feature-mesh-driven like the stand-in. This decides how expressions are authored.
- Layering is built (one outer layer over the top, legwear under everything). Should real coats cover more, such as hoods over jackets or layered skirts?
- How far the fantasy hair and eye colours go, and per archetype.
