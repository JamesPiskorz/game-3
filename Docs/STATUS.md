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
| Character creator | UI Toolkit screen over `CreatorSession`: tabs, sliders, swatches, locks, undo/redo, contexts, share codes, your name, how the look reads ("cute and casual"), expression preview, "Into town" | `Creator/`, scene `Creator/Creator.unity` |
| Blender pipeline | Headless Blender tools: shared rig, weight and shape-key transfer, a validator, FBX export with sidecars; a Unity import post-processor turns them into parts | `Tools/Blender`, `Characters/Editor/Import` (decision 0005) |
| Sim core | Residents derived from the character seed (name, pronouns, personality, interests, job, hangouts, taste), weekly schedules, relationships, interactions, first impressions from outfits, small saves | `Sim/Core`, `Sim/Content/SimCatalog.json` (decision 0006) |
| Dialogue | 194 tone-flavoured lines over 47 reactions. Lines can be gated by stage, and no line repeats back to back. Residents bring up recent shared moments, pass on town news and good words, get jealous, and notice an outfit they love by name | `Sim/Core/Dialogue.cs`, `SimCatalog.json` |
| Town prototype | `TownSession` loop and a playable scene: blockout sets per location, time-of-day light, residents built from DNA. The HUD lets you travel, talk, give gifts, ask people out, work shifts, shop, change clothes, and save/load | `Town/`, scene `Town/Town.unity` (decision 0007: one stage per place, for now) |
| Boutique | Daily stock taken from generated outfits; buying, a wardrobe, outfit rules; money from shifts | `Sim/Core/Sandbox/Boutique.cs` |
| Dates | Three-round scenes: pick topics, a compliment or something the place offers to do together (dancing, feeding the ducks, the arcade...). The outcome combines your choices with how well the place suits them; the date that makes someone a partner ends with them asking to be together | `Sim/Core/Relationships/DateScene.cs` |
| Contacts | Everyone you've met, with whereabouts hints that sharpen as you get to know them; click to go to them | `Sim/Core/Sandbox/Contacts.cs` |
| Social web | Friendships and couples among residents, derived from the town seed. Taken residents turn you down unless smitten enough to leave their partner. Friends put in a good word | `Sim/Core/Social/SocialWeb.cs` |
| Day loop | Priced gifts, an end-of-day summary, autosave on sleep, gentle decay for neglected friends | `Sim/Core/Sandbox/TownSession.cs`, `Relationships/Decay.cs` |
| Player stats | Charm, fitness, smarts and style, raised by activities at each place (time and sometimes coins, with tapering gains). Each style admires certain stats, which feeds first impressions; charm improves compliments, and expertise boosts talk about loved topics | `TownSession.DoActivity`, `Interactions.Admiration` |
| Barks | A few residents at each place say something out loud each hour (the weather, their work, a night out, their partner, a love of theirs), shown as speech bubbles over their heads | `Sim/Core/Sandbox/Barks.cs` |
| Hints | One gentle tip at a time for new players: head out, pick someone, say hi, learn their likes, ask them out | `TownSession.Hint` |
| Weather | Sunny, cloudy or rainy each day, from the seed. Rain empties the park as people shelter indoors, greys the sky and falls on outdoor scenes | `Sim/Core/Weather.cs`, `SimWorld.WhereIs` |
| Invitations | Close friends and partners message the player to meet later today, where their plans already take them; turning up starts a date, standing them up costs | `Sim/Core/Sandbox/Invitations.cs` |
| Expressions | Seven facial expressions plus blinking on the face bones, plus idle breathing, sway, gaze and a procedural walk cycle (people walk in and out of town scenes as time passes). Both layer over the builder's pose; residents react and nod at each interaction | `Characters/Runtime/Expressions`, `Docs/review/expressions.png` |
| Stand-in content | Procedural body with simple face, 33 parts (12 hair, 21 clothing and accessories, including neckwear), 9 face presets, 7 eye and 5 brow styles, 8 patterns, toon materials. **Placeholder art until Phase 1.** | `Characters/Editor/StandIns`, `Content/StandIn` |
| Performance | Town benchmark; distance-based detail (`CharacterLod`) | decision 0004, `Docs/review/perf.md` |
| Tests | 216 Unity EditMode tests (106 characters, 110 sim); the pure C# sets also run on .NET 8 | `Characters/Tests`, `Sim/Tests` |

## Try it

- **Play the slice:** open `Assets/_Game/Town/MainMenu.unity` (the first scene in the build) and press Play.
  1. **New game** opens the creator. Make someone, or press Randomize.
  2. Press **Into town →**. The town scene opens with your character.
  3. Pick a place, pick someone under **Here**, then use **Say hi**, **Talk about…** and **Ask about…** (what they're into, their plans today, their friends). The card lists what you've learned.
  4. Give gifts that match, and **Ask out…** once you're friends. A date is three rounds of choices: talk about what they love.
  5. **Wait an hour** and **Sleep** move time. Places close, and people come and go with their schedules.
  6. **Work a shift** to earn coins. Spend them at the **Boutique** in Arcade Lane, and change clothes in the **Wardrobe** at home. Once you know someone a little, their card says which looks they go for.
  7. **Contacts** lists everyone you've met. Once you know someone well, it says where they are right now; click a contact to go to them.
  8. Residents have their own friends and partners; the card says who once you know them. Someone who's taken will turn you down, unless they're smitten enough to leave their partner for you.
  9. **Sleep** sums up the day and autosaves. Friends you neglect for more than three days start to cool off.
  10. **Menu** saves and returns to the main menu; **Continue** picks up where you left off.
  11. **Things to do** at each place raise your stats; the card says what a resident admires.
  12. Once someone's close, they'll send **Messages** inviting you out later that day. Click one to go there. In the evenings, people dress up for the bar, and for Friday and Saturday nights out.
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
  - a month of play in three towns by an automated attentive player (`AutoPlayer`), written up as diaries in `Docs/review/stories.md` (**Game → Town → Write Story Report**). Each town tells a different story, and a test checks that,
  - a fuzz test: a random player takes 1,000 actions of every kind in each of three towns, with world invariants checked after every step,
  - the whole loop via `FlowCheck`, pressing the real buttons: main menu, New game, Randomize, Into town (identical character), Menu (autosaves), Continue (back at the saved time). It backs up and restores the player's saved look and save file; it also screenshots the menu (`Docs/review/menu.png`).
  - **a Windows player build** (`unity build . --target StandaloneWindows64 --output-path Builds/Windows/Game3.exe`). It builds, and `Game3.exe -batchmode -smoke -logFile smoke.log` plays a short day from the main menu into town (meet, talk, work, shop, sleep) and exits 0 with no errors (`PlayerSmokeTest`). Automated runs never write the player's save.
- **Not verified:**
  - **Interactive use in the Editor GUI or the built player:** mouse dragging, typing, the feel of the sliders and buttons.
  - Anything on min-spec hardware.
  - Real art. Everything visual is procedural stand-ins and blockout sets.

## For artists

[style-bible.md](style-bible.md) (draft v0) gathers everything the code enforces for character art: units and axes, bone names, body shapes, regions, recolour masks, budgets, naming, catalog fields and the review checklist. The taste calls are left open for the art team.

## Gotchas found along the way

- **Unity's bundled NUnit is older than NuGet's.** `Is.AnyOf` doesn't exist there; tests that pass on .NET can fail to compile in Unity.
- **Fuzzing finds what scripted tests don't.** The random-player test caught a time-passing bug on its first run.
- **Fine colour detail can't live in vertex colours.** Bands narrower than the vertex spacing smear, so lapels and narrow stripes are geometry; details on garments must also follow the body shapes, or they sink into the garment on fuller figures.

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

1. **Play it by hand:** an hour in the editor to tune pacing and find UI friction that headless captures can't. `Docs/review/stories.md` suggests a partner in two to three weeks of attentive play.
2. **First real art:** base body and head per the style bible, then swap it in for the stand-ins. The stand-in face can't smile; a real head with blendshapes needs its own expression driver.
3. **Conversations with choices:** multi-line exchanges where the player picks replies, instead of one line per action.
4. **The town as a space:** walkable locations with NavMesh. People already walk in and out of each scene, but the town itself is a set of stages.
5. **Sound:** there's none yet. Music per place and time of day, plus UI sounds.
6. **Performance:** merge each character into fewer renderers (decision 0004) before the town shows many more people at once. The current town renders in about 8 ms in the editor.
