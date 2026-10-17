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
| Dialogue | 123 tone-flavoured lines over 26 reactions. Lines can be gated by stage, and no line repeats back to back. Residents bring up recent shared moments and notice an outfit they love by name | `Sim/Core/Dialogue.cs`, `SimCatalog.json` |
| Town prototype | `TownSession` loop and a playable scene: blockout sets per location, time-of-day light, residents built from DNA. The HUD lets you travel, talk, give gifts, ask people out, work shifts, shop, change clothes, and save/load | `Town/`, scene `Town/Town.unity` |
| Boutique | Daily stock taken from generated outfits; buying, a wardrobe, outfit rules; money from shifts | `Sim/Core/Sandbox/Boutique.cs` |
| Dates | Three-round scenes: pick topics or compliments; the outcome combines your choices with how well the place suits them | `Sim/Core/Relationships/DateScene.cs` |
| Contacts | Everyone you've met, with whereabouts hints that sharpen as you get to know them; click to go to them | `Sim/Core/Sandbox/Contacts.cs` |
| Social web | Friendships and couples among residents, derived from the town seed. Taken residents turn you down unless smitten enough to leave their partner. Friends put in a good word | `Sim/Core/Social/SocialWeb.cs` |
| Day loop | Priced gifts, an end-of-day summary, autosave on sleep, gentle decay for neglected friends | `Sim/Core/Sandbox/TownSession.cs`, `Relationships/Decay.cs` |
| Expressions | Seven facial expressions plus blinking on the face bones, plus idle breathing, sway and gaze. Both layer over the builder's pose; residents react and nod at each interaction | `Characters/Runtime/Expressions`, `Docs/review/expressions.png` |
| Stand-in content | Procedural body with simple face, 31 parts (12 hair, 19 clothing and accessories), 9 face presets, 7 eye and 5 brow styles, 8 patterns, toon materials. **Placeholder art until Phase 1.** | `Characters/Editor/StandIns`, `Content/StandIn` |
| Performance | Town benchmark; distance-based detail (`CharacterLod`) | decision 0004, `Docs/review/perf.md` |
| Tests | 182 Unity EditMode tests (104 characters, 78 sim); the pure C# sets also run on .NET 8 | `Characters/Tests`, `Sim/Tests` |

## Try it

- **Play the slice:** open `Assets/_Game/Creator/Creator.unity` and press Play.
  1. Make someone, or press Randomize.
  2. Press **Into town →**. The town scene opens with your character.
  3. Pick a place, pick someone under **Here**, then use **Say hi** and **Talk about…**. The card lists what you've learned they love.
  4. Give gifts that match, and **Ask out…** once you're friends. A date is three rounds of choices: talk about what they love.
  5. **Wait an hour** and **Sleep** move time. Places close, and people come and go with their schedules.
  6. **Work a shift** to earn coins. Spend them at the **Boutique** in Arcade Lane, and change clothes in the **Wardrobe** at home. Once you know someone a little, their card says which looks they go for.
  7. **Contacts** lists everyone you've met. Once you know someone well, it says where they are right now; click a contact to go to them.
  8. Residents have their own friends and partners; the card says who once you know them. Someone who's taken will turn you down, unless they're smitten enough to leave their partner for you.
  9. **Sleep** sums up the day and autosaves. Friends you neglect for more than three days start to cool off.
  - Opening `Town/Town.unity` directly works too. It uses the look you last took into town, or a generated one.
- **Review sheets:** the **Game → Characters → Review** menu has the generated lineup, the 96-character quality sheet, the skin ramp, the spring filmstrip, the town benchmark, the part sheet (every part on one model, front and back) and the expression sheet. **Game → Town → Build Town Scene** rebuilds the town scene.
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
2. **First real art:** base body and head per the style bible, then swap it in for the stand-ins. The stand-in face can't smile; a real head with blendshapes needs its own expression driver.
3. **More to do in town:** activities on dates, NPC-to-NPC relationships and gossip, and relationship decay.
4. **The town as a space:** walkable locations with NavMesh, and residents arriving and leaving instead of popping in.
5. **Performance:** merge each character into fewer renderers (decision 0004), before the town shows more than six people at once.
