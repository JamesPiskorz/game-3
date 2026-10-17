# Project status

_Updated 2026-10-18 (overnight build). See [PLAN.md](../PLAN.md) for the full plan and [decisions/](decisions/) for why things are the way they are._

## What exists

| Area | State | Where |
|---|---|---|
| Character DNA | v1: versioned, deterministic JSON, share codes, migration scaffold | `Characters/Core/Dna` |
| Generator | Archetypes, presets + noise, colour harmony, slot/tag rules, skin-contrast rule, population mode, reroll with locks | `Characters/Core/Generation`, `Content/Catalog.json` |
| Runtime assembly | Bone rebinding, part-only bone grafting, blendshape mirroring, region hiding, slot blocking | `Characters/Runtime/Modular` (decision 0001) |
| Builder | DNA → character, idempotent, sliders/styles on bones, colours, patterns | `Characters/Runtime/Building` |
| Toon shading | `Game/Characters/Toon`: ramp shading, hand-picked skin shades, outlines, recolour masks, patterns | `Characters/Shaders` (decision 0002) |
| Spring bones | Verlet hair chains with colliders, synced with equipped parts | `Characters/Runtime/Springs` (decision 0003) |
| Character creator | UI Toolkit screen over `CreatorSession`: tabs, sliders, swatches, locks, undo/redo, contexts, share codes, "Into town" | `Creator/`, scene `Creator/Creator.unity` |
| Blender pipeline | Headless Blender tools: shared rig, weight and shape-key transfer, a validator, FBX export with sidecars; a Unity import post-processor turns them into parts | `Tools/Blender`, `Characters/Editor/Import` (decision 0005) |
| Sim core | Residents derived from the character seed (name, pronouns, personality, interests, job, hangouts, taste), weekly schedules, relationships, interactions, first impressions from outfits, small saves | `Sim/Core`, `Sim/Content/SimCatalog.json` (decision 0006) |
| Town prototype | `TownSession` loop and a playable scene: blockout sets per location, time-of-day light, residents built from DNA, a HUD to travel, talk, give gifts and ask people out, save/load | `Town/`, scene `Town/Town.unity` |
| Stand-in content | Procedural body with simple face, 19 parts, toon materials, patterns. **Placeholder art until Phase 1.** | `Characters/Editor/StandIns`, `Content/StandIn` |
| Performance | Town benchmark; distance-based detail (`CharacterLod`) | decision 0004, `Docs/review/perf.md` |
| Tests | 144 Unity EditMode tests (102 characters, 42 sim); the pure C# sets also run on .NET 8 | `Characters/Tests`, `Sim/Tests` |

## Try it

- **Play the slice:** open `Assets/_Game/Creator/Creator.unity` and press Play.
  1. Make someone, or press Randomize.
  2. Press **Into town →**. The town scene opens with your character.
  3. Pick a place, pick someone under **Here**, then use **Say hi** and **Talk about…**. The card lists what you've learned they love.
  4. Give gifts that match, and **Ask out…** once you're friends.
  5. **Wait an hour** and **Sleep** move time. Places close, and people come and go with their schedules.
  - Opening `Town/Town.unity` directly works too. It uses the look you last took into town, or a generated one.
- **Review sheets:** the **Game → Characters → Review** menu has the generated lineup, the 96-character quality sheet, the skin ramp, the spring filmstrip and the town benchmark. **Game → Town → Build Town Scene** rebuilds the town scene.
- **Regenerate stand-ins:** **Game → Characters → Content → Regenerate All Stand-in Content**.
- **Tests:** Window → General → Test Runner → EditMode, or headless:
  `unity test . --editor-version 6000.6.6f1 --mode EditMode`

## Verified vs not verified

- **Verified headless:**
  - all tests,
  - every review render (images in the repo),
  - the creator in Play mode via `CreatorCapture` (`Docs/review/creator-*.png`),
  - the town in Play mode via `TownCapture`: a scripted visit that presses the HUD's buttons (meet, talk, gift, date, the bar in the evening, the park at night), in `Docs/review/town-*.png`,
  - the new-game flow via `HandOffCheck`: Randomize in the creator, press "Into town", and the town opens with the identical character (same share code). It restores any saved look afterwards.
- **Not verified:**
  - **Interactive use in the Editor GUI:** mouse dragging, typing, the feel of the sliders and buttons.
  - Anything on min-spec hardware or in a player build.
  - Real art. Everything visual is procedural stand-ins and blockout sets.

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
- **`EditorBuildSettings.scenes` isn't written to disk** when a batch run ends with `EditorApplication.Exit`. Call `AssetDatabase.SaveAssets()` after changing it.
- **Simulate before tuning.** The first relationship numbers made friends in 3 days and gave random chat as good results as attention. A 60-day simulated player found that in seconds; `ListeningPaysOff` now guards it.

## Suggested next steps

1. **Play it by hand:** an hour in the editor to tune pacing and find UI friction that headless captures can't.
2. **Dialogue depth:** more lines per reaction, lines by stage, and callbacks to shared memories. Repetition is the slice's biggest weakness.
3. **First real art:** base body and head per the style bible, then swap it in for the stand-ins.
4. **The town as a space:** walkable locations with NavMesh, and residents arriving and leaving instead of popping in.
5. **Performance:** merge each character into fewer renderers (decision 0004), before the town shows more than six people at once.
