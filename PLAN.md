# Sandbox Dating Sim — Development Plan

_Draft v1 · 2026-10-17 · Engine: Unity 6 (URP) · Look: 3D stylized/anime · Art: in-house, small team_

> **Progress:** Phase 0 is done apart from the style bible and Spike C (UMA). Phase 2's generator, creator and quality gate are done on procedural stand-in content. See [Docs/STATUS.md](Docs/STATUS.md) and the decision records in [Docs/decisions](Docs/decisions/).

---

## 1. What we're building

A 3D anime-style dating sim set in a small free-roam town. There is no fixed cast and no fixed script. The town is filled with procedurally generated characters, and each one has their own look, personality, wardrobe, schedule and tastes. The player, made in the same character creator, meets them, builds relationships and dates whoever they click with. Stories come out of the simulation instead of being written in advance.

**The character generator is the heart of the game.** If generated characters aren't consistently appealing, varied and glitch-free, nothing else matters. This plan builds it first: work on the game layer doesn't start until the generator passes its quality bar (§3.8).

### Pillars

1. **Every character looks like someone you'd want to meet.** Generated characters must look intentionally designed, with coherent outfits, harmonious colours and appealing faces, rather than like random sliders.
2. **Endless, distinct variety.** No two of a town's 50 residents should look alike, and a new world seed should feel like a new cast.
3. **Appearance is gameplay.** Clothes, style and looks feed into NPC preferences and reactions, so the generator matters for gameplay as well as looks.
4. **Systems over scripts.** Behaviour comes from simulation plus templated content, so the sandbox grows with the number of characters.

### Hard constraints

- **Unity 6 + URP.** The project is pinned to `6000.6.6f1` (the 6.6 "Supported" stream). The whole team uses this exact version, and we upgrade only between milestones.
- **3D stylized/anime** look: toon shading and outlines.
- **A small in-house art team.** Every system has to get a lot of variety out of a small number of hand-made parts.
- **All characters are adults and must read as adults.** The generator and creator enforce adult proportions and adult ages, and slider ranges are clamped so that no combination looks childlike. This is an ethical line and also a storefront requirement. Anime-style dating games get checked for exactly this.

---

## 2. Key decisions

| Area | Decision | Why |
|---|---|---|
| Render pipeline | **URP**, character shaders in **HLSL** (decision 0002) | Well-trodden path for toon/NPR; Shader Graph can't do the multi-pass outline cleanly |
| Character system | **Custom**, built on our own DNA model. UMA gets a timeboxed evaluation spike | The anime look needs custom shading, hair handling and a custom face rig. UMA is built around realistic texture-overlay compositing |
| Base body | **One shared topology, UV layout and skeleton** for everyone. Masculine↔feminine is a shape axis | Each clothing item is authored once and fits every body. Fallback: two bases that share topology |
| Body shaping | **Bones first, blendshapes only where bones can't do it** | Clothing follows bones automatically. Every body blendshape must be transferred onto every clothing item, so we keep that list short |
| Face shaping | **Hybrid:** face bones for proportions, blendshapes for shape types and expressions | Expressions stay correct when features are moved or scaled |
| Variety | **Grayscale/mask-authored textures recoloured in the shader** | One hairstyle comes in any colour. One shirt gives N colourways × M patterns |
| Source of truth | **`CharacterDna`**: versioned and serialized. Seeds are only used to generate | Content updates never change existing saved characters |
| Content data | **`Catalog.json`** (pure data) + `CharacterContent.asset` (engine bindings) (decision 0004) | The generator and its rules are tested without the editor |
| UI | **UI Toolkit** | Mature runtime UI in Unity 6; well suited to the slider-heavy creator |
| Version control | **Git + Git LFS** | Binary-heavy art; UnityYAMLMerge for scenes and prefabs |

---

## 3. Character system

### Architecture

```
                 ┌─────────────────────────────────────────────┐
                 │ CONTENT  (ScriptableObjects + meshes/tex)   │
                 │ parts · palettes · archetypes · rules       │
                 └──────────────┬───────────────────┬──────────┘
                                │ ContentCatalog    │ assets
                                │ (plain C# view)   │
   seed + request ──► ┌─────────▼────────┐          │
                      │    GENERATOR     │          │
                      │ taste, rules,    │          │
                      │ no meshes        │          │
                      └─────────┬────────┘          │
                                ▼                   │
   Character Creator ◄──►  CharacterDna  ◄──► save / load / share code
   (edits DNA only)             │                   │
                                ▼                   ▼
                      ┌─────────────────────────────────────┐
                      │ BUILDER  (DNA → GameObject)         │
                      │ skeleton · parts · shapes · mats    │
                      └─────────┬───────────────────────────┘
                                ▼
                      CharacterInstance
                       ├ body + head SkinnedMeshRenderers
                       ├ hair + clothing (rebound to skeleton)
                       ├ expressions · blink · look-at · spring bones
                       └ optimizer (identity bake, mesh merge, LOD)
```

The four layers stay strictly separate:

- **Content**: ScriptableObject definitions for every part, palette and rule, plus the meshes and textures. Artists and designers can extend the game here without touching code.
- **DNA**: plain C# data that completely describes one character. It holds no Unity object references, only stable string IDs and numbers. It is serializable, diffable and versioned.
- **Generator**: `seed + request → DNA`. Deterministic and **pure C# (no `UnityEngine`)**. It reads a plain `ContentCatalog` (IDs, slots, tags, weights, ranges) that the Unity layer builds from the ScriptableObjects, so it can be unit-tested and batch-run headless over thousands of characters. It knows about taste and nothing about meshes.
- **Builder**: `DNA → GameObject`. It knows about meshes, bones and materials and nothing about taste.

The Character Creator only edits DNA and asks the Builder to re-apply it. It never touches meshes directly.

### 3.1 DNA (data model)

```csharp
// Pure data: no UnityEngine.Object references, only stable IDs and numbers.
public sealed class CharacterDna
{
    public int Version = 1;
    public ulong Seed;                       // provenance only; the DNA is the source of truth
    public BodyDna Body = new();
    public FaceDna Face = new();
    public SkinDna Skin = new();
    public HairDna Hair = new();
    public List<OutfitDna> Outfits = new();  // one per context: casual, date, work, sleep...
}

public sealed class BodyDna
{
    public float Femininity;                              // 0 = masculine end, 1 = feminine end
    public Dictionary<string, float> Sliders = new();     // "height", "shoulders", "chest", "hips"... normalized
}

public sealed class FaceDna
{
    public string PresetId;                               // "face.preset.soft_03"
    public Dictionary<string, float> Sliders = new();     // offsets from the preset, normalized
    public string EyeStyleId, BrowStyleId, LashStyleId;
    public HsvColor IrisLeft, IrisRight;                  // heterochromia = two different values
    public List<OverlayDna> Makeup = new();               // blush, lipstick, freckles, moles...
}

public sealed class SkinDna { public float Tone; public float Undertone; }  // positions on curated ramps, 0..1

public sealed class HairDna
{
    public List<string> PieceIds = new();                 // "hair.back.long_02", "hair.front.blunt_01"
    public HsvColor Base, Tip, Highlight;
    public float Gradient;                                // 0 = solid, 1 = full gradient to Tip
    public HsvColor? BrowOverride;                        // null = derived from hair
}

public sealed class OutfitDna { public string Context; public List<ItemDna> Items = new(); }

public sealed class ItemDna
{
    public string ItemId;                                 // "cloth.top.hoodie_01"
    public HsvColor[] RegionColors;                       // up to 4 recolourable regions
    public string PatternId; public float PatternScale;
}
```

```csharp
public interface ICharacterGenerator
{
    CharacterDna Generate(ulong seed, GenerationRequest request);                     // deterministic
    CharacterDna Reroll(CharacterDna current, CategoryMask categories, ulong seed);   // "randomize with locks"
}

public interface ICharacterBuilder
{
    CharacterInstance Build(CharacterDna dna, Transform parent);   // full assembly
    void Apply(CharacterInstance instance, CharacterDna dna);      // incremental update for the creator
}
```

Rules:

- **IDs are stable strings** (`hair.front.bob_01`) and never list indices. Adding content must never shift existing characters.
- **Sliders are normalized** (0..1 or −1..1). The content definition maps them to real bone scales or blendshape weights, so retuning a range never invalidates a save.
- **Colours are stored literally** (HSV) and not as palette references. Editing a palette must not recolour existing characters.
- **`Version` plus migration functions from day one**, with a test for each migration.
- Serialized with Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`), which handles the dictionaries that `JsonUtility` can't. Share codes are JSON → Deflate → Base64Url. _Stretch goal:_ embed the DNA in a PNG portrait ("character cards"), a well-loved feature in this genre.

### 3.2 Base body & skeleton

- **Body and head are separate SkinnedMeshRenderers on one skeleton.** This keeps the many face blendshapes off the body mesh and lets the face use its own shading.
- **Humanoid-compatible skeleton** (Mecanim Humanoid) so animations retarget. On top of it: face bones (eyes, brows, nose, mouth, jaw, cheeks, ears), physics bones (hair, skirt, chest) and **shape helper bones**.
- **Proportions come from bones:** height, head size, neck length, shoulder width, arm and leg length, hand and foot size. Non-uniform scale goes on leaf helper bones (skinned to, but not parents of the chain), so children don't get skewed. Length changes go through bone position offsets.
- **Body blendshapes are kept to about 10:** femininity axis, weight/softness, muscle, chest, waist, hips, belly, thighs, calves. Each one must be transferred to every clothing item (§3.6), which is why the list is short.
- **Body region map:** about 20 regions (upper/lower torso, upper-arm halves, forearm, hands, upper/lower thigh, knee, calf, feet, …) encoded in vertex colour. These are used to hide skin under clothing. Region boundaries follow common hemlines and sleeve lengths.
- **Budget (LOD0):** body about 20k tris, head about 10k tris, one 2048² texture set each.

### 3.3 Face

The anime face is where most of the visual quality lives, so it gets the most care.

- **Proportions (bones):** eye position, scale, tilt and spacing; brow height; nose position and size; mouth position and width; jaw width; chin length; cheek width; ear size.
- **Shape types (blendshapes):** eye shapes (round, sharp, upturned, downturned, narrow), eyelid types, nose types, lip fullness, jaw and chin types, cheek fullness.
- **Expressions (blendshapes):** blink L/R, smiling eyes, wide eyes, brows (angry, sad, raised), mouth A/I/U/E/O, smile, frown, pout, open/teeth. They are supplemented by **texture swaps for anime "special eyes"** (^ ^, sparkle, swirl, blush lines).
- **Eyes:** a separate eye mesh. Iris, pupil and highlight are shader layers on grayscale textures, so any iris colour, heterochromia and pupil style is possible. An **eye style** is a set of lash and eyeliner textures plus an eyelid blendshape preset, and most of the "anime look" lives in eye styles.
- **Brows & lashes:** separate meshes with texture-swapped styles. Their colour is derived from the hair unless overridden. Following anime convention, brows render over bangs (stencil or render-queue trick).
- **Makeup & marks:** shader-composited layers (blush, lipstick, eyeshadow, freckles, moles, beauty marks) from a texture array with tint and opacity. No runtime texture baking in v1.
- **Face shading:** anime faces need controlled shadows. One option is custom normals transferred from an ellipsoid, with blendshape normals imported as `None` so morphs don't wreck them. The other is an SDF face-shadow map. Both get prototyped in Phase 0.
- **Face presets:** artists author 8–12 face archetypes, each a slider configuration that looks great. The generator samples a preset and then adds bounded noise. Picking every slider independently at random produces uncanny faces.

### 3.4 Skin

- **Skin tone is a position on a curated ramp** (`Tone` 0..1) plus an `Undertone` (cool↔warm). A `SkinTonePalette` asset resolves these into **base, shade, rim and blush** colours.
- **Every stop on the ramp is art-directed under toon lighting.** Shade colours are picked by hand (warmer and more saturated) instead of computed as `base × 0.6`. Computed shades are the most common reason dark skin looks muddy in toon shaders.
- **The full range, from very light to very dark, is a requirement.** It gets checked in the lineup under every lighting setup the game uses (day, sunset, night, interior).
- Optional fantasy tones are a stretch goal behind a setting.

### 3.5 Hair

- **Modular pieces:** **back**, **front/bangs**, **sides** and **extras** (ponytails, buns, braids, ahoge, twintails). The combinations multiply: 12 backs × 12 fronts × optional extras gives far more than a dozen hand-made hairstyles.
- **Full one-piece styles** cover looks that don't decompose into parts, such as afros, locs, long braids and other textured or protective styles. The hair catalogue has to cover curly and coily textures, not only straight anime hair, to match the skin range.
- **Rigging:** skinned to the head bone, with optional hair bones for spring-bone physics.
- **Shading:** value-authored grayscale texture → base, shade and highlight colours. Optional gradient to the tip and an inner colour (two-tone hair is popular in anime), plus an "angel ring" anisotropic highlight.
- **Colours** come from **natural** palettes (black, brown, blonde, red, grey) and **fantasy** palettes (pink, blue, silver, green …), weighted by world setting and archetype.
- **Compatibility tags** such as `hat_ok`, `hood_ok`, `high_collar_ok`, and clashing front/back pairs. A hat either names a "hat hair" variant or hides specific pieces.
- **Rule:** face sliders only change the front of the face and never the cranium, or every hair piece clips. Head *size* is a uniform head-bone scale, which hair follows.
- **Budget:** 3–8k tris per piece, at most 4 pieces per character.

### 3.6 Clothing (the hardest part)

The problems: fit across body shapes, clipping, layering, and enough variety from little art.

1. **Shared skeleton.** Every item is skinned to the base skeleton. At runtime the Builder rebinds `SkinnedMeshRenderer.bones` to the character's skeleton by bone name, and bone-driven body sliders then fit the clothing automatically.
2. **Shape-key transfer.** Every body blendshape is transferred onto every item in Blender by script (Surface Deform / Data Transfer). At runtime, body weights are mirrored onto clothing by name. A missing shape key guarantees clipping, so **the Blender validator refuses to export an item that lacks one.**
3. **Body hiding.** Each item declares which body regions it fully covers, and the body shader clips those regions (a bitmask uniform compared with the vertex-colour region ID). This removes skin poking through clothes without any mesh surgery.
4. **Slots & layering.** Slots: `underwear_top/bottom`, `top`, `bottom`, `onepiece` (occupies top + bottom), `outer`, `legwear`, `shoes`, `hat`, `glasses`, `earrings`, `neck`, `bag`. Each item declares the slots it occupies, its layer order and the tags it excludes (e.g. high collar ✕ `long_hair_front`). Outer layers are modelled over a "max-bulk" inner layer.
5. **Recolour & patterns.** Each item ships a mask texture (R/G/B/A = up to 4 recolourable regions) plus detail and normal textures. The shader applies the DNA colours per region, plus optional patterns from a texture array (stripes, plaid, floral, polka dot, denim, knit). 40 items × colourways × 10 patterns gives thousands of distinct outfits.
6. **Skirts & loose clothing.** Bone-rigged skirts with spring bones and leg colliders to start. Long loose cloth is a known risk (§9).
7. **Wardrobes.** Each character owns several outfits by context (casual, work/school, date, sleep, formal), all generated from their style archetype.

### 3.7 Shading: `CharacterToon`

A custom URP Shader Graph family with these features:

- Two-tone or ramp lighting with art-directed shade colours, rim light, and specular for hair and eyes.
- An **inverted-hull outline** using smoothed normals that are baked into a spare UV channel at import, so outlines don't break on hard edges. Width and colour are set per material.
- Mask recolour, pattern overlay, body-region clipping and makeup layers.
- Works with the main light, additional lights and shadows (night scenes, interiors).
- **Per-character material instances, not MaterialPropertyBlocks.** In URP, MaterialPropertyBlocks opt renderers out of the SRP Batcher.
- References to study: MToon (UniVRM), lilToon, Unity Toon Shader.

### 3.8 The generator

`CharacterGenerator.Generate(seed, request) → CharacterDna`

1. **Seeded sub-streams.** Our own PCG32, with a sub-seed per category: `SplitMix64(seed ^ StableHash("hair"))`. We never use `UnityEngine.Random` (global state) or `string.GetHashCode` (not stable). With sub-streams, adding a new hairstyle doesn't reshuffle every other aspect of every seed.
2. **Identity.** Adult age, position on the body axis, a **style archetype** (sporty, preppy, gothic, cute, punk, elegant, street, bookish, outdoorsy …) and a personality seed. Gender presentation and the profile's gender and pronouns use separate, configurable distributions.
3. **Body & face.** Pick a face preset and add bounded Gaussian noise. Body sliders come from distributions conditioned on the body axis. Everything is clamped to the **safe range**: adult proportions, and only the ranges clothing has been tested at.
4. **Colouring.**
   - Skin tone comes from a distribution that covers the full ramp.
   - Hair colour mixes natural and fantasy palettes by weight.
   - Brows and lashes are derived from the hair colour.
   - Eye colour follows the same pattern.
5. **Hair.** Pieces are weighted by archetype and checked for compatibility.
6. **Wardrobe.** For each outfit context:
   - Pick items by archetype weight.
   - Resolve slot and tag constraints.
   - Colour the outfit with a **harmony scheme**: a base hue, then analogous, complementary or monochrome-plus-accent colours from curated palettes, weighted toward neutrals so outfits look wearable.
7. **Validate.** The constraint checker runs over the result. A failing category gets resampled with bounded retries, then falls back to a known-good default.
8. **Population mode.** When generating a whole town, any candidate whose appearance distance to an existing resident is below a threshold is rejected, so the town has no lookalikes.

`GenerationRequest` constrains the output, for example "feminine-presenting, gothic, 25–30, red hair". The same mechanism powers "reroll hair only" in the creator.

**Quality bar (the gate for starting the sim layer):**

- 100 random characters reviewed in the lineup scene: **≥ 80% "good", 0% "broken"** (clipping, visual bugs, off-model).
- A headless batch of 10,000 characters: **0 constraint violations**, and DNA generation under 5 ms per character.
- The same seed gives **byte-identical DNA** across runs and machines.
- The bar is re-run after every content drop, and good/broken rates are tracked over time.

### 3.9 Character Creator

- **One UI (UI Toolkit) with three users:** players making their own character, designers authoring presets, and developers debugging.
- **Categories:** Body · Face · Eyes · Skin · Hair · Makeup · Outfits. Each has sliders, palette swatches plus a free colour picker, and part pickers with auto-rendered thumbnails.
- **Randomize with locks:** a dice button per category plus lock toggles ("keep the hair, reroll the outfit"). This is the sandbox toy in its purest form.
- **Housekeeping:** undo/redo, presets, save/load and share codes.
- **Preview:** a Cinemachine camera rig (full body, face close-up, turntable), lighting presets (day, night, indoor), and pose and expression preview.

### 3.10 Performance

Target: **30+ unique characters on screen at 60 fps on min-spec.** The min-spec depends on the target platform (§10).

- **Identity bake.** At spawn, the identity blendshapes (body and face shape) are baked into a per-character mesh and removed. Only expression shapes stay live. This is the biggest single GPU saving.
- **Mesh merge.** After building, a character's body, clothing and hair meshes are merged into as few renderers as possible. The head stays separate for expressions.
- **LODs** for body, hair and clothing. Far LODs drop outlines and physics.
- **Addressables** for parts, so only the parts in use are loaded.
- **Time-sliced building**, so spawning residents never hitches.
- **Portrait cache.** Portraits are rendered to texture for the phone and contact UIs, so menus never instantiate full characters.
- Basic profiling starts in Phase 1, not Phase 4.

---

## 4. Art & content pipeline

Blender (current LTS) → FBX → Unity. The pipeline has one goal: **an artist can add a new clothing item or hair piece without a programmer, in under an hour of pipeline work on top of modelling.**

**Blender (`Tools/Blender/`)**

- A reference `.blend` with the base body, skeleton and all shape keys. Every part starts from this file.
- A script that transfers body shape keys and skin weights onto clothing and hair.
- A validator that checks bone names, the full shape-key set, the triangle budget, UV channels, material slots, naming and scale/axis.
- A batch exporter with locked FBX settings plus a sidecar `.json` (slots, tags, covered regions, recolour regions).

**Unity (`Characters/Editor/`)**

- An `AssetPostprocessor` that applies import settings by folder (rig, blendshape normals, compression) and bakes smoothed normals for outlines. It also creates or updates the part's ScriptableObject from the naming convention plus the sidecar json.
- A thumbnail renderer for creator part pickers.
- **A lineup scene and contact-sheet exporter** that renders N generated characters to a PNG grid. This is the main art-review tool.
- **A clip-test scene** that puts every clothing item on the body at slider extremes, in stress poses (sit, arms up, walk, crouch, run).

**Naming:** `{category}.{slot}.{name}_{variant}`, for example `cloth.top.hoodie_01` or `hair.front.bob_02`.

**Budgets (LOD0)**

| Part | Tris | Textures |
|---|---|---|
| Body | ~20k | 2048² |
| Head (incl. eyes, brows, lashes) | ~10k | 2048² |
| Hair piece | 3–8k | 1024² |
| Clothing item | 3–12k | 1024² |
| Accessory | ≤ 2k | 512² |
| **Whole character** | **≤ 60k** | |

Source files live in `ArtSource/` (outside `Assets/`, tracked with LFS), in a layout that mirrors `Content/`.

---

## 5. Sandbox dating sim layer (initial scope)

This layer stays deliberately thin until the generator clears its gate. It will get its own design doc.

- **World:** a small town of 8–10 locations (café, park, campus/office, shops, bar, beach, gym, homes), with a day/night cycle and a weekly calendar.
- **Population:** a world seed generates 30–50 residents, each with appearance, profile, wardrobe, home, job and schedule.
- **Character record:** DNA plus a profile (name, age, pronouns, personality traits, interests, likes and dislikes, **appearance preferences**, romance preferences), a schedule and relationships.
- **Relationships:** per-pair values (affection, attraction, trust, familiarity), stages (stranger → acquaintance → friend → dating → partner) and a memory of shared events.
- **Interactions:**
  - Talk, with topic picking against the other character's interests.
  - Compliment.
  - Give a gift.
  - Invite someone to an activity or a date. Dates are short scenes.
- **Appearance as gameplay:**
  - NPCs have preferences about style, colour and features.
  - The player's look and outfit shift first impressions and date outcomes.
  - Shops sell items from the same catalogue the generator uses.
- **Dialogue:** templated lines with variables, flavoured by personality. LLM-driven dialogue is a possible later experiment but is not core, because of cost, offline play and content control.
- **Stretch goals:** NPCs date each other (rivals, existing couples), gossip, and change outfits for weather and occasion.
- **Save/load:** world state plus every DNA and character record, as JSON.

---

## 6. Tech stack & project setup

**Unity packages**

- Universal RP and Shader Graph
- Input System
- Cinemachine
- UI Toolkit (built in)
- Animation Rigging (look-at, head and eye tracking)
- Addressables
- Newtonsoft JSON
- Test Framework
- AI Navigation (NPC movement, Phase 5)
- Timeline (date scenes, later)

**Third-party candidates (decided in the Phase 0 spikes)**

- Spring bones: the open-source VRM SpringBone from UniVRM, or Magica Cloth 2 (paid).
- UMA 2, for evaluation only.

**Project layout**

```
Game3/
├─ Assets/_Game/
│  ├─ Characters/
│  │  ├─ Core/        # asmdef Game.Characters (noEngineReferences): DNA, RNG, generator, rules
│  │  ├─ Runtime/     # asmdef Game.Characters.Unity: builder, instance, expressions, optimizer
│  │  ├─ Content/     # part/palette/archetype ScriptableObjects + imported meshes & textures
│  │  ├─ Shaders/     # CharacterToon family
│  │  ├─ Editor/      # importers, inspectors, thumbnails, contact sheets
│  │  └─ Tests/       # EditMode (DNA, generator) + PlayMode (builder)
│  ├─ Creator/        # Character Creator UI
│  ├─ Sim/            # world, NPCs, relationships (Phase 5)
│  └─ Scenes/         # Lineup, ClipTest, Creator, Town
├─ ArtSource/         # .blend / .psd / .kra sources (LFS)
├─ Tools/Blender/     # export, validation, shape-key transfer
├─ Docs/              # decision records, style bible, sim design
└─ PLAN.md
```

**Repo:** Unity `.gitignore`. `.gitattributes` sends `*.fbx *.blend *.png *.psd *.kra *.tga *.exr *.wav *.ogg` to LFS and uses UnityYAMLMerge for `*.unity *.prefab *.asset`. Editor settings: Force Text serialization and Visible Meta Files.

**Tests:**

- The generator, DNA and migrations are pure C#, so EditMode tests are fast. They cover determinism, round-trips, migrations and a 10k-character constraint sweep.
- PlayMode tests cover the builder: parts attach, bones rebind and shapes mirror.
- Tests run headless through the Unity CLI.

---

## 7. Roadmap

These estimates assume **1–2 programmers and 1–2 artists**. They are rough and get redone at the end of Phase 1. The total is roughly **6–9 months to a playable demo**.

### Phase 0: Foundations & spikes (~2 weeks)

Goal: remove the technical unknowns before building for real.

- Project setup: Unity project, git + LFS, packages, folder layout, assembly definitions, headless test run.
- **Style bible (art):**
  - 3–5 target character paintings or turnarounds.
  - A proportion sheet (heads tall, eye placement, adult proportions).
  - Shading and outline references.
  - A palette guide.
  - Every later review is measured against this document.
- **Spike A, toon look:** `CharacterToon` v0 plus outlines on a stand-in model. Face shadows with custom normals vs. SDF. The light-to-dark skin ramp under day and night lighting.
- **Spike B, modular assembly:** a stand-in body with 2 clothing items and 1 hair piece. Runtime bone rebinding, blendshape mirroring and region clipping.
- **Spike C, UMA (timeboxed to 2 days):** does it get us our look with less work than going custom?
- **Spike D, physics:** hair and skirt spring bones, then choose a solution.

**Exit:** decision records are in `Docs/decisions/` and the spikes have been demoed. Phase 1 should start with no open "can Unity even do X?" questions.

### Phase 1: One great character (~5–7 weeks)

Goal: the full pipeline, end to end and at production quality, with a small set of parts.

**Art:**

- The base body and head: topology, UVs, skeleton, face bones, region map, ~10 body shape keys, ~15 face shape keys, ~25 expression keys.
- 2 eye styles, 3 brow styles, 4 hair pieces (2 front, 2 back).
- 6 clothing items covering every slot type, plus 3 patterns.

**Code:**

- DNA v1 with serialization and a migration scaffold.
- The Builder: assembly, bone rebinding, shape mirroring, region hiding, materials.
- `CharacterToon` v1.
- An expression controller with blink and look-at.
- A live DNA inspector for Play mode.
- Blender exporter and validator v1, and the Unity importer v1.

**Exit:**

- A JSON file fully describes a character, and loading it rebuilds the same character.
- No visible clipping for any slider extreme × item × stress pose.
- Expressions look right on every face preset.
- An artist adds a new clothing item with no programmer involved.

### Phase 2: The generator (~3–4 weeks)

- Generator features:
  - RNG with sub-streams.
  - Archetype and weight tables.
  - The constraint solver.
  - Colour harmony.
  - Face presets plus noise.
  - Distinctiveness checks.
  - `GenerationRequest`.
- The lineup scene, the contact-sheet exporter and headless batch stats (distributions of skin tones, hair colours, archetypes).
- Character Creator v1, with randomize-with-locks, presets and share codes.
- **Exit:** the §3.8 quality bar passes against the current content. **This is the gate for Phase 5.**

### Phase 3: Content scale-up (~6–10 weeks, alongside Phases 4–5)

- Fully support the masculine end of the body axis, and re-validate every existing item there.
- Reach the MVP content targets (§8).
- Tune hair and skirt physics. Add the special eye expressions and makeup layers.
- Hold a weekly contact-sheet review and track good/broken rates over time.

### Phase 4: Town-scale performance (~2–3 weeks)

- Identity bake, mesh merging, LODs, Addressables, time-sliced building and the portrait cache.
- **Exit:** 30+ characters on screen at the target fps on min-spec, with the memory budget met and no spawn hitches.

### Phase 5: Sandbox sim vertical slice (~8–12 weeks)

- **World:** the town (start with 3–4 locations) and time/calendar.
- **NPCs:** schedules and NavMesh navigation.
- **Social systems:** interactions, relationships, dialogue templates, gifts, dates and shops.
- **Plumbing:** save/load and the player-creation flow.
- **Exit:** a 30-minute session. The player creates a character, meets generated residents and goes on a date, and the stories differ meaningfully by world seed.

### Phase 6: Demo polish

- Onboarding, a UI pass, audio, external playtests and a final performance pass.

---

## 8. MVP content targets

| Category | Target | Multiplied by |
|---|---|---|
| Body | 1 topology, masc↔fem axis, ~10 shape keys, ~10 bone sliders | continuous sliders |
| Face presets | 10 | bounded noise on ~25 sliders |
| Eye styles | 10 | any iris colour × 6 pupil styles (shader) |
| Brows / lashes | 8 / 6 | hair-derived colour |
| Hair | 12 back, 12 front, 10 extras, 6 one-piece styles | colour × gradient × two-tone |
| Clothing | 40 items (10 tops, 8 bottoms, 5 onepieces, 5 outers, 6 shoes, 6 legwear) | 4 recolour regions × 10 patterns |
| Accessories | 15 (glasses, earrings, hats, necklaces, bags) | colour |
| Makeup & marks | 12 overlays | tint × opacity |
| Skin | 1 ramp, ~12 art-directed stops + undertone | continuous |

---

## 9. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Clothing clips on some body/pose combos | **High.** Visible constantly | Bones-first shaping, scripted shape-key transfer with a blocking validator, region hiding, clamped "tested" ranges, the clip-test scene |
| Generated characters look random or unappealing | **High.** Breaks the core promise | Archetypes, presets plus bounded noise, curated palettes and harmony rules, the contact-sheet quality gate |
| Content cost outruns a small team | **High** | Modular hair, recolour masks and patterns, pipeline automation, tracking "time to add an item" |
| Characters read as underage | **High** (ethical + platform) | Adult-proportion bounds in the generator and creator, an art-review checklist item |
| Toon face shading artifacts | Med | Phase 0 spike (custom normals vs. SDF) |
| Expressions break on morphed faces | Med | Hybrid bones/blendshapes, a preset × expression test matrix |
| Dark skin tones look muddy | Med | Hand-picked shade colours per ramp stop, lighting test scenes |
| Performance with many unique skinned characters | Med | Identity bake, merging and LODs; profiling from Phase 1 |
| Long or loose cloth (long skirts, coats) misbehaves | Med | Start with bone-rigged skirts; consider Magica Cloth 2; limit long cloth in the MVP |
| The sim grows before the generator is solid | Med | Hard gate: Phase 5 starts after the Phase 2 exit |
| Unity version churn | Low | Pin the editor version per milestone |

---

## 10. Open questions

1. **Target platforms and min-spec.** PC/Steam only? Steam Deck? Consoles? The answer sets every budget.
2. **Camera and movement.** Third-person free roam, or a town map plus 3D scenes?
3. **Romance options.** Can players choose any gender and presentation, and is every NPC romanceable according to their own preferences? Recommended: yes, since it's a sandbox.
4. **Dialogue.** Hand-written templates, procedural generation, or an LLM experiment? And how much writing can we afford?
5. **Voice.** None, gibberish ("animalese"), or voiced? This sets the lip-sync scope.
6. **Content rating.** Swimwear? How intimate do dates get? This decides which clothing and body content is needed.
7. **Working title.**

---

## 11. First two weeks: next steps

- [x] Create the Unity 6 URP project in this folder and pin the editor version.
- [x] Run `git init` and add the Unity `.gitignore`, LFS `.gitattributes` and UnityYAMLMerge config. Set Force Text and Visible Meta.
- [x] Install the packages from §6.
- [x] Create the folder layout and asmdefs (`Game.Characters` with `noEngineReferences`, `Game.Characters.Unity`, Editor, Tests).
- [x] Implement `CharacterDna` v1 and PCG32, with JSON round-trip and determinism tests.
- [ ] Art: write the style bible, and block out the base-body proportions and skeleton in Blender.
- [x] Run spikes A, B and D and write the decision records (0001–0003). Spike C (UMA) is still open.
- [ ] Answer the open questions in §10, especially platform and camera.
