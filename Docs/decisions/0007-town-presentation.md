# 0007: Town presentation: one stage per place, for now

**Status:** Accepted (prototype) · 2026-10-18

## Context

PLAN.md Phase 5 imagines a walkable town with NavMesh navigation. Tonight's goal was a playable loop: create a character, meet generated residents, date, with stories that differ by seed. That had to be built and verified headless, without anyone at the keyboard. A walkable town brings three things that can't be judged in screenshots: movement, camera control and collision.

## Decision

The town is **a set of stages**, one per location, rather than a continuous space.

- **What a place is:** travelling picks a place. The stage shows a blockout set for it (`TownSets`), up to six people who are there, and the player.
- **Light:** follows the clock and weather outdoors, with warm interior light indoors (`TownStage.Light`).
- **Framing:**
  - Wide shot of the group, or a two-shot when someone is selected.
  - Always shifted left so the subject clears the HUD panel, which covers the right quarter.
- **People move:**
  - Newcomers walk in from the nearest side, and leavers walk off. The player steps up beside whoever they pick.
  - A procedural walk cycle and idle motion (`IdleMotion`) stand in for animation.
  - Arriving somewhere new shows the scene as it is, without walking.
  - Places with seats (the café, the library, the park bench) seat a few people behind their tables, facing the room: those on their own, not being talked to and not at work there. Whoever the player turns to stands up. Sitting is another `IdleMotion` blend (hips down, legs folded, hands to the lap). Tables sit on the left, since the HUD covers the right.
- **The view is a projection of the session:** `TownSession` (pure C#) owns the rules, and `TownView` and `TownStage` only draw it. Captures press the same buttons a player would.

## Consequences

- **Good:**
  - Every system (schedules, weather, relationships, dates, invitations) is visible and testable now.
  - Captures are deterministic: fixed frame rate, characters snapped into place, blinking off.
  - A walkable town can come later on the same session API: it only has to render `Present()` differently.
- **Missing:**
  - No sense of geography: places are buttons, not streets.
  - No chance encounters on the way, and no spatial puzzles (finding someone).
  - Travel always takes 20 minutes.
- **Next:** a walkable town is still the Phase 5 target.
  - Start with one outdoor hub (the park and the café front) with NavMesh, and keep the stage view for interiors.
  - Keep `TownSession` as the authority, so tests and captures don't change.

## Lighting (added 2026-10-18, graphics plan G0.4-G0.10)

The light that followed the clock and weather became data:

- **Presets.** `LightingPreset` assets (day, sunset, night, interior, studio) in a `LightingLibrary` the character content points to. `SceneLighting` applies a look to a scene: sun, ambient as a gradient (sky, horizon, ground, built into the ambient probe), a gradient sky with soft clouds (`Game/Environment/Sky`), linear fog, and a grading volume on the Default layer that only main cameras see.
- **Blending and weather.** Changes blend over half an hour around 7:00, 17:00 and 20:00; weather greys the result (`LightingLook.Overcast`).
- **The refactor was checked against the hard-coded light first.** With presets holding the old values, the captures matched the committed ones: 29 of 33 changed images differed in under 0.4% of pixels, lineups were identical, and the rest was rain streaks and a sub-visible grading shift. Only then did the look change.
- **Sets are toon-shaded** (`Game/Environment/Toon`): the characters' two-tone banding, a cooler, slightly desaturated shade, per-pixel shadows from characters and props, emission for lit windows and bulbs, fog, and an inverted-hull outline. The outline runs along outline normals baked into a copy of each primitive mesh, so it stays closed at cube corners. `RenderingChecks` fails if a game scene still uses URP Lit.
- **Shadows** cover 25 m in 2 cascades at 4096 (stages are small): about four times the texel density of the old 50 m in 4 cascades at 2048. Characters ignore them (decision 0002 item 5), so this is for the ground and props. It costs about 1.9 ms in the benchmark (0.6 ms before), and the total stays within budget.
- **Weather and night.**
  - Rain is soft and see-through, and darkens the ground.
  - At night the park has a lamp post. One street lamp is the key light and casts shadows.
  - Windows and bulbs marked `Lit()` in `TownSets` glow by the preset (bloom catches the night ones).
- **Removed.** SSAO and the unused opaque texture. SSAO is removed from the renderer, not just switched off: a build strips an inactive SSAO feature's shaders, and the player then throws creating it (the smoke test caught this).

## Portraits

Contacts and the resident card show a portrait rendered from the resident's DNA (`Town/Portraits.cs`): one hidden character in a studio 500 m below the town, re-dressed per portrait, rendered once into a 160 px texture by a camera that's otherwise off. The sun and ambient light are swapped for neutral daylight for that one render, so portraits don't go dark at night. Portraits are cached per resident and redrawn when their DNA object changes (makeovers and gifted clothes replace it). Real art would likely do the same with a proper portrait light rig and expression.

The same studio (`PortraitStudio`, in the character runtime) seats two for **date photos**: after a great date the sim keeps both looks as DNA, so the photo shows how they looked that night even after later makeovers, and the HUD renders it on demand. The creator's **Variations** use it too, full length or face close up. Two lessons from it: an unequipped part is destroyed only at the end of the frame, so `Unequip` now hides it at once (several shots in one frame showed leftovers); and the first render after startup can come out with placeholder shaders in the editor, so the studio throws one away.
