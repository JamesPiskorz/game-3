# Project status

_Updated 2026-10-18 (overnight build). See [PLAN.md](../PLAN.md) for the full plan and [decisions/](decisions/) for why things are the way they are._

## New overnight (2026-10-18)

- **Generation:** freckles, beauty marks, blush and lipstick by style; three textured hairstyles (afro, puffs, locs); square and cat-eye glasses; a turtleneck, a sweater, a cardigan, wide trousers, loafers and high-tops; eye and brow styles pushed apart, fuller builds that read, catch-lights; freckles run in families. New review sheets: face styles and body range.
- **Creator:** face marks and lipstick in the Face tab; **Variations** shows six candidates at once.
- **Town:** portraits in contacts, messages, the card and your stats line; confessions (residents ask you out); clothes as gifts that residents wear; a makeup counter at the salon; residents' makeup changes over the weeks.
- **Menu:** the cast behind the menu keeps changing, with a caption of what the generator picked.
- **Fixes:** swept bangs and the hairline no longer show a notch; the AutoPlayer actually shops; parts unequipped mid-frame no longer render.

## What exists

| Area | State | Where |
|---|---|---|
| Character DNA | v1: versioned, deterministic JSON, share codes, migration scaffold, and repair for looks made with other content (removed parts, new sliders and contexts) | `Characters/Core/Dna` |
| Generator | Archetypes, presets + noise, colour harmony, slot/tag rules, skin-contrast rule, population mode with families (siblings who look related), outer layers (a jacket or coat by choice, and a coat on rainy days), legwear where legs show, freckles, beauty marks, blush and lipstick by style and presentation, textured hairstyles (an afro, puffs, locs) for about one in eight, workout clothes for the gym, smart work clothes, reroll with locks | `Characters/Core/Generation`, `Content/Catalog.json` |
| Runtime assembly | Bone rebinding, part-only bone grafting, blendshape mirroring, region hiding, slot blocking | `Characters/Runtime/Modular` (decision 0001) |
| Builder | DNA → character, idempotent, sliders/styles on bones, colours, patterns | `Characters/Runtime/Building` |
| Toon shading | `Game/Characters/Toon`: ramp shading, hand-picked skin shades, outlines, recolour masks, patterns | `Characters/Shaders` (decision 0002) |
| Spring bones | Verlet hair chains with colliders, synced with equipped parts | `Characters/Runtime/Springs` (decision 0003) |
| Character creator | UI Toolkit screen over `CreatorSession`: tabs, sliders, swatches, a style choice for Randomize, locks, undo/redo, contexts, share codes, your name, how the look reads ("cute and casual"), expression preview, a Sibling button (a new person from the same family), freckles, beauty mark, blush and lipstick (with its colour), Variations (six full-length candidates Randomize could give, respecting locks and style; click one to take it), "Into town" | `Creator/`, scene `Creator/Creator.unity` |
| Blender pipeline | Headless Blender tools: shared rig, weight and shape-key transfer, a validator, FBX export with sidecars; a Unity import post-processor turns them into parts | `Tools/Blender`, `Characters/Editor/Import` (decision 0005) |
| Sim core | Residents derived from the character seed (name, pronouns, personality, interests, job, hangouts, taste), weekly schedules, relationships, interactions, first impressions from outfits, small saves | `Sim/Core`, `Sim/Content/SimCatalog.json` (decision 0006) |
| Dialogue | 452 tone-flavoured lines over 117 reactions. Lines can be gated by stage, and no line repeats back to back. Residents bring up recent shared moments, pass on town news and good words, get jealous, and notice an outfit they love by name | `Sim/Core/Dialogue.cs`, `SimCatalog.json` |
| Town prototype | `TownSession` loop and a playable scene: blockout sets per location, time-of-day light, residents built from DNA, some of them sitting at café and library tables or on the park bench. The HUD lets you travel, talk, give gifts, ask people out, work shifts, shop, change clothes, and save/load | `Town/`, scene `Town/Town.unity` (decision 0007: one stage per place, for now) |
| Boutique | Daily stock taken from generated outfits; buying, a wardrobe, outfit rules; money from shifts | `Sim/Core/Sandbox/Boutique.cs` |
| Dates | Three-round scenes: pick topics, a compliment or something the place offers to do together (dancing, feeding the ducks, the arcade...). The outcome combines your choices with how well the place suits them; the date that makes someone a partner ends with them asking to be together. Dating several people has a cost: the others hear about it, and a partner who finds out twice leaves | `Sim/Core/Relationships/DateScene.cs` |
| Families | About a quarter of residents have a brother or sister in town. They look related (skin tone, natural hair and eye colour, face and build carry over, and freckles usually do; dyed hair doesn't), share a family name, are a few years apart, and are never paired as friends or couples. Siblings pass on news and good words, a stranger whose sibling likes you says so, and dating two siblings is noticed. The card says whose brother or sister they are | `Characters/Core/Generation/FamilyResemblance.cs`, `TownGenerator`, `Docs/review/family-sheet.png` |
| Salon | Next to the boutique: any cut, bangs (or none) and colour in the catalog, for coins and an hour; lipstick in any shade and blush for 15 coins (free to take off). Hats the new hair can't take come off. Hair now counts in first impressions, and people who knew the old look react by their own taste: they love it, like it, or find it "different" (makeup goes by how much their style wears it) | `Sim/Core/Sandbox/Salon.cs`, `TownSession.Haircut/Bangs/Dye` |
| Makeovers | Each week a few residents get a haircut, dye their hair or buy new everyday clothes, made by the generator in their own style; people whose style wears lipstick sometimes put it on, change the shade or take it off. Someone you knew before mentions it, the card says something's different, and a compliment while it's new counts for a lot more. It's all derived from the seed and the week, so saves replay it | `Sim/Core/Residents/Makeovers.cs`, `Docs/review/makeover-sheet.png` |
| Main menu | New game, Continue, Quit, in front of a cast of five generated characters. Every few seconds one makes way for a new face, popping in with a caption of what the generator picked (style, hair, clothes); **New faces** regenerates the whole cast | `Town/MainMenuScreen.cs`, `Docs/review/menu-new-face.png` |
| Contacts | Everyone you've met, each with a small portrait rendered from their DNA, and whereabouts hints that sharpen as you get to know them; click to go to them. The resident card, messages and your own stats line have portraits too | `Sim/Core/Sandbox/Contacts.cs`, `Town/Portraits.cs` |
| Social web | Friendships and couples among residents, derived from the town seed. Taken residents turn you down unless smitten enough to leave their partner. Friends put in a good word | `Sim/Core/Social/SocialWeb.cs` |
| Confessions | The town asks too: a close friend with a strong spark (attraction 75+, affection 60+) confesses at the first hello of a day. Yes means dating (someone seeing another resident confesses only if they'd leave them, and does); no cools the spark. Nobody confesses to a player with a partner, and someone left without an answer tries again in two weeks | `Interactions.Confess.cs` |
| Clothes as gifts | Give a resident something from your wardrobe. If it's their style (their two best-liked tags on it) they love it and wear it in their everyday outfit from then on, through makeovers and saves; if not, they say so | `Interactions.GiveClothes`, `Sim/Core/Residents/ClothesGifts.cs` |
| Day loop | Priced gifts, an end-of-day summary, autosave on sleep, gentle decay for neglected friends | `Sim/Core/Sandbox/TownSession.cs`, `Relationships/Decay.cs` |
| Player stats | Charm, fitness, smarts and style, raised by activities at each place (time and sometimes coins, with tapering gains). Each style admires certain stats, which feeds first impressions; charm improves compliments, and expertise boosts talk about loved topics | `TownSession.DoActivity`, `Interactions.Admiration` |
| Chat | A resident starts a conversation (something they love, their day, a worry once they know you, a daydream) and the player answers warmly, playfully or with a question. A reply that lands draws them out for a second exchange, in words that answer how you replied, and two in a row leave them feeling heard. What lands depends on personality: warmth comforts a worrier, teasing suits the outgoing and stings the anxious. Once one way works twice, the card says what they respond to and marks that reply | `Sim/Core/Relationships/Chat.cs`, `Interactions.StartChat` |
| Barks | A few residents at each place say something out loud each hour (the weather, their work, a night out, their partner, a love of theirs), shown as speech bubbles over their heads | `Sim/Core/Sandbox/Barks.cs` |
| Hints | One gentle tip at a time for new players: head out, pick someone, say hi, learn their likes, try a chat, notice a new look, ask them out, and the salon on a first visit | `TownSession.Hint` |
| Weather | Sunny, cloudy or rainy each day, from the seed. Rain empties the park as people shelter indoors, puts everyone in their rainy-day outfit with a coat, greys the sky and falls on outdoor scenes | `Sim/Core/Weather.cs`, `SimWorld.WhereIs` |
| Invitations | Close friends and partners message the player to meet later today, where their plans already take them; turning up starts a date, standing them up costs | `Sim/Core/Sandbox/Invitations.cs` |
| Expressions | Seven facial expressions plus blinking on the face bones (a mouth whose corners curve into smiles and frowns, anime blush hatching for shy and delighted faces, and closed "^ ^" eyes when delighted), plus idle breathing, sway, gaze and a procedural walk cycle (people walk in and out of town scenes as time passes). Both layer over the builder's pose; residents react and nod at each interaction | `Characters/Runtime/Expressions`, `Docs/review/expressions.png` |
| Stand-in content | Procedural body with simple face (catch-lights in the eyes), 61 parts (18 hair, including a braid on its own spring chain and three textured styles; 4 face marks; 39 clothing and accessories, including neckwear, outer layers, legwear, earrings, a crossbody bag, hair accessories and three kinds of glasses), 9 face presets, 7 eye and 5 brow styles, 10 patterns, toon materials. **Placeholder art until Phase 1.** | `Characters/Editor/StandIns`, `Content/StandIn` |
| Generator statistics | 10,000 characters measured as a population: style shares against catalog weights, presentation and skin-tone spread, fantasy hair and eyes, how often each part is worn in each context, patterns, rule violations (0) and time per character (about 0.3 ms). A test fails if any part goes unworn | `Generation/GeneratorStats.cs`, `Docs/review/generator-stats.md` |
| Performance | Town benchmark; distance-based detail (`CharacterLod`) | decision 0004, `Docs/review/perf.md` |
| Tests | 306 Unity EditMode tests (142 characters, 164 sim); the pure C# sets also run on .NET 8 | `Characters/Tests`, `Sim/Tests` |

## Try it

- **Play the slice:** open `Assets/_Game/Town/MainMenu.unity` (the first scene in the build) and press Play.
  1. **New game** opens the creator. Make someone, press Randomize, or press **Variations** to pick from six at a time.
  2. Press **Into town →**. The town scene opens with your character.
  3. Pick a place, pick someone under **Here**, then use **Say hi**, **Talk about…** and **Ask about…** (what they're into, their plans today, their friends). **Chat** lets them bring something up; pick a warm, playful or curious reply. The card lists what you've learned.
  4. Give gifts that match, and **Ask out…** once you're friends. A date is three rounds of choices: talk about what they love.
  5. **Wait an hour** and **Sleep** move time. Places close, and people come and go with their schedules.
  6. **Work a shift** to earn coins. Spend them at the **Boutique** in Arcade Lane, or on a new cut, colour, lipstick or blush at the **Salon** next door, and change clothes in the **Wardrobe** at home. You dress for the occasion automatically, as residents do: a coat in the rain, workout clothes at the gym, dressed up for a night out. Once you know someone a little, their card says which looks they go for.
  7. **Contacts** lists everyone you've met, with a portrait of each. Once you know someone well, it says where they are right now; click a contact to go to them.
  8. Residents have their own friends and partners; the card says who once you know them. Someone who's taken will turn you down, unless they're smitten enough to leave their partner for you.
  9. **Sleep** sums up the day and autosaves. Friends you neglect for more than three days start to cool off.
  10. **Menu** saves and returns to the main menu; **Continue** picks up where you left off.
  11. **Things to do** at each place raise your stats; the card says what a resident admires.
  12. Once someone's close, they'll send **Messages** inviting you out later that day. Someone who's fallen for you may say so: answer yes or let them down gently. Click one to go there. In the evenings, people dress up for the bar, and for Friday and Saturday nights out.
  - Opening `Town/Town.unity` directly works too. It uses the look you last took into town, or a generated one.
- **Review sheets:** the **Game → Characters → Review** menu has the generated lineup, the 96-character quality sheet, the skin ramp, the spring filmstrip, the town benchmark, the part sheet (every part on one model, front and back), the expression sheet, the face style sheet (every eye and brow style on the same faces), the body range sheet (every body slider at its extremes), the family sheet (six families of three), the archetype sheet (eight characters per style, one row each) and the generator statistics. **Game → Town → Render Makeover Sheet** shows residents before and after their makeovers. **Game → Town → Build Town Scene** rebuilds the town scene.
- **Regenerate stand-ins:** **Game → Characters → Content → Regenerate All Stand-in Content**.
- **Tests:** Window → General → Test Runner → EditMode, or headless:
  `unity test . --editor-version 6000.6.6f1 --mode EditMode`

## Verified vs not verified

- **Verified headless:**
  - all tests,
  - every review render (images in the repo),
  - the creator in Play mode via `CreatorCapture` (`Docs/review/creator-*.png`),
  - the town in Play mode via `TownCapture`: a scripted visit that presses the HUD's buttons (meet, talk, chat, gift, date, shop, the salon, the bar in the evening, the park at night and in the rain, the gym), in `Docs/review/town-*.png`,
  - a month of play in three towns by an automated attentive player (`AutoPlayer`), written up as diaries in `Docs/review/stories.md` (**Game → Town → Write Story Report**). Each town tells a different story, and a test checks that,
  - a fuzz test: a random player takes 1,000 actions of every kind (including chats and the salon) in each of three towns, with world invariants checked after every step; a one-off 30 more towns also passed,
  - the whole loop via `FlowCheck`, pressing the real buttons: main menu, New game, Randomize, Into town (identical character), Menu (autosaves), Continue (back at the saved time). It backs up and restores the player's saved look and save file; it also screenshots the menu (`Docs/review/menu.png`), and again once a new face has stepped in (`menu-new-face.png`).
  - the generator as a population: 10,000 characters in `Docs/review/generator-stats.md` (0 rule violations),
  - **a Windows player build**, rebuilt and smoke-tested again after the latest features (`unity build . --target StandaloneWindows64 --output-path Builds/Windows/Game3.exe`). It builds, and `Game3.exe -batchmode -smoke -logFile smoke.log` plays a short day from the main menu into town (meet, talk, work, shop, sleep) and exits 0 with no errors (`PlayerSmokeTest`). Automated runs never write the player's save.
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
2. **First real art:** base body and head per the style bible, then swap it in for the stand-ins. The stand-in mouth curves with corner bones; a real head with blendshapes needs its own expression driver.
3. **Richer conversations:** a chat is at most two exchanges over four kinds of opener. More openers, and a third beat that remembers the first, would make talking feel less like a menu.
4. **The town as a space:** walkable locations with NavMesh. People already walk in and out of each scene, but the town itself is a set of stages.
5. **Sound:** there's none yet. Music per place and time of day, plus UI sounds.
6. **Performance:** render cost now follows renderer count (about 290 for 40 residents with tonight's face marks, about 10 ms in the editor; see `Docs/review/perf.md`). Merging each character's parts into fewer renderers, with merged materials, is the next lever (decision 0004).
