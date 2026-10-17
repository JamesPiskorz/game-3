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
