# Project status

_Updated 2026-10-17 (overnight build). See [PLAN.md](../PLAN.md) for the full plan and [decisions/](decisions/) for why things are the way they are._

## What exists

| Area | State | Where |
|---|---|---|
| Character DNA | v1: versioned, deterministic JSON, share codes, migration scaffold | `Characters/Core/Dna` |
| Generator | Archetypes, presets + noise, colour harmony, slot/tag rules, skin-contrast rule, population mode, reroll with locks | `Characters/Core/Generation`, `Content/Catalog.json` |
| Runtime assembly | Bone rebinding, part-only bone grafting, blendshape mirroring, region hiding, slot blocking | `Characters/Runtime/Modular` (decision 0001) |
| Builder | DNA → character, idempotent, sliders/styles on bones, colours, patterns | `Characters/Runtime/Building` |
| Toon shading | `Game/Characters/Toon`: ramp shading, hand-picked skin shades, outlines, recolour masks, patterns | `Characters/Shaders` (decision 0002) |
| Spring bones | Verlet hair chains with colliders, synced with equipped parts | `Characters/Runtime/Springs` (decision 0003) |
| Character creator | UI Toolkit screen over `CreatorSession`: tabs, sliders, swatches, locks, undo/redo, contexts, share codes | `Creator/`, scene `Creator/Creator.unity` |
| Stand-in content | Procedural body with simple face, 19 parts, toon materials, patterns. **Placeholder art until Phase 1.** | `Characters/Editor/StandIns`, `Content/StandIn` |
| Performance | Town benchmark; distance-based detail (`CharacterLod`) | decision 0004, `Docs/review/perf.md` |
| Tests | 99 Unity EditMode tests; the core set also runs on .NET 8 | `Characters/Tests` |

## Try it

- **Creator:** open `Assets/_Game/Creator/Creator.unity` and press Play.
  - Drag the preview to turn the character.
  - Randomize, use the locks, switch tabs, and try Casual and Date.
  - Copy and Load share codes.
- **Review sheets:** the **Game → Characters → Review** menu has:
  - the generated lineup (32 residents, both contexts),
  - the 96-character quality sheet,
  - the skin ramp under three lights,
  - the spring filmstrip,
  - the town benchmark.

  Output goes to `Docs/review/` and `Docs/decisions/img/`.
- **Regenerate stand-ins:** **Game → Characters → Content → Regenerate All Stand-in Content**.
- **Tests:** Window → General → Test Runner → EditMode, or headless:
  `unity test . --editor-version 6000.6.6f1 --mode EditMode`

## Verified vs not verified

- **Verified headless:**
  - all tests,
  - every review render (images in the repo),
  - the creator in Play mode via `CreatorCapture` (tabs, Randomize and context buttons pressed, screenshots in `Docs/review/creator-*.png`).
- **Not verified:**
  - Interactive use in the Editor GUI: mouse dragging, typing into the share-code field, the feel of the slider drags.
  - Anything on min-spec hardware or in a player build.
  - Real art. Everything visual is procedural stand-ins.

## Gotchas found along the way

- **Unity CLI and licensing need an unsandboxed shell** on this machine, because the sandbox hides the Windows credential store.
- **Headless rendering:**
  - Disable async shader compilation.
  - Throw away the first render.
  - Force skinned-mesh re-skinning between back-to-back renders.
  - Reload assets after `NewScene`, which unloads them.
- **Play mode has domain reload disabled.** Editor hooks must not rely on `[InitializeOnLoad]` running when Play mode starts.
- **Rebuild generated meshes through the Mesh API.** `CopySerialized` over a mesh whose vertex layout changed crashes the editor.
- **Mono and .NET throw different exceptions for corrupt deflate data.** Running the same tests on both caught it.

## Suggested next steps

1. **Blender → Unity part pipeline:** FBX import post-processor, sidecar metadata, a shape-key transfer script, and an end-to-end test with Blender 5.2, which is already installed.
2. **First real art:** base body and head per the style bible, then swap it in for the stand-ins.
3. **Performance:** merge each character into fewer renderers (decision 0004).
4. **Phase 5:** the sandbox sim layer (time, residents, relationships) now that the generator gate is met on stand-ins.
