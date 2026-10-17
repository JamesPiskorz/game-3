# 0004: Generator, builder and town-scale performance

**Status:** Accepted · 2026-10-17

## Decisions

1. **The content catalog is one JSON file** (`Content/Catalog.json`), loaded by pure C# (`ContentCatalog`, strict: unknown fields fail). It holds IDs, tags, weights, slider distributions, presets, palettes, archetypes and contexts. Engine bindings (prefabs, bones, textures, colliders) live separately in `CharacterContent.asset`, keyed by the same IDs.
   - **Why:** the generator and every one of its rules are tested on .NET 8 and on Unity's Mono from the same file, without the editor.
   - **Revisit:** designers will want an editor UI over the JSON eventually.
2. **Taste comes from archetypes:**
   - Seven style archetypes weight every tagged pick. Faces are designed presets plus bounded noise.
   - Colour harmony uses one accent plus neutrals, placed by a scheme.
   - Slot and tag rules (blocks / requires / excludes) keep outfits wearable.
   - Population mode rejects lookalikes.
   - Families: some residents are generated as siblings of an earlier one (`FamilyResemblance`). A sibling is their own generated person; then skin tone is constrained near the relative's (through the request, so clothes are coloured against the real tone), natural hair and eye colour carry over, the face usually takes the same preset and leans towards the relative's features, and each body slider borrows half of the relative's difference from their presentation's average. Everything family-related draws from its own stream. Siblings need only half the usual distance from each other, and the full distance from everyone else. `Docs/review/family-sheet.png` is the review artifact.
3. **Determinism is per stream:**
   - Every slider and category draws from its own seed stream (PCG32 seeded from SplitMix64 and FNV-1a).
   - Normal noise uses Irwin–Hall rather than Box–Muller, so the arithmetic is the same on every runtime.
   - The same tests pass on CoreCLR and Mono.
4. **Coherence rules are found by review, then enforced by tests.** The 96-character quality sheet showed that clothing near the skin colour reads as bare skin. Torso and leg clothing now keeps a minimum distance from the skin. The rule is solved jointly with top/bottom contrast and covered by a 3,000-character sweep.
5. **The builder is idempotent:**
   - It captures a rest pose once and resets bound bones before applying sliders and styles.
   - It reconciles equipped parts with the DNA's outfit for a context.
   - The creator re-applies after every edit, at about 0.07 ms per edit.
6. **The creator talks only to `CreatorSession`** (pure C#: undo/redo, locks, rules). The UI Toolkit view is built from the catalog, so new sliders, parts and presets need no UI work.
7. **Far characters drop detail** (`CharacterLod`): first the outline pass, then spring bones, then shadows, with hysteresis.

## Measurements

40 residents, editor, Ryzen 7 5700X / RX 7900 XTX. Indicative, not min-spec. Details in [perf.md](../review/perf.md).

| | |
|---|---|
| Generate a character | ~0.13 ms |
| Build a character | ~1.2 ms median |
| Re-apply after an edit | ~0.07 ms (outfit context switch ~0.3 ms) |
| Springs for the whole town | ~0.24 ms per frame |
| Render the town at 1080p | ~8.2 ms: outline pass ~45% of character cost, shadows ~20%, blendshapes ~3% |
| Far view with `CharacterLod` | 7.3 → 3.75 ms |

## Consequences / next

- **Draw calls are the cost, not vertices or blendshapes.** About 230 renderers × 4 passes for 40 characters. The next lever is merging each character into a few renderers, which needs per-character colour data (texture arrays or palette textures) in place of per-part material colours. That is Phase 4.
- **The identity blendshape bake is low priority** on desktop GPUs. Re-measure on min-spec.
- **Re-measure in a player build** on min-spec hardware once a target is chosen (PLAN §10, question 1).
