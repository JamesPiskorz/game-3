# 0006: Sandbox sim core and the town prototype

**Status:** Accepted · 2026-10-18

## Decisions

1. **The sim is its own pure C# assembly** (`Game.Sim`: no UnityEngine, references `Game.Characters`), with its content in one strict JSON file (`Sim/Content/SimCatalog.json`). The file holds locations and opening hours, jobs, topics, gifts, per-archetype personality and interests, name pools and dialogue lines.
   - **Why:** this is the same split as the character generator (decision 0004). Every rule is tested on .NET 8 and on Mono, and designers edit data, not code.
2. **A resident is derived from their character seed:**
   - **Name:** presentation tilts the name pool and pronouns without deciding them. Feminine-presenting residents get a feminine-pool name about 85% of the time; neutral names and they/them come up for anyone.
   - **Traits and taste:** Big Five traits sit around the dress archetype's means. Each resident loves three topics and dislikes one or two. Their taste in other people's clothes is their own style plus a soft spot for one other archetype.
   - **Life:** a job (capped per town, so the café always has staff), hangouts, and a weekly hourly schedule.
   - **Why:** the look and the person stay consistent, because a gothic dresser leans introverted and into music and books. Everything sits on its own seed stream, so adding profile fields never changes anyone's appearance.
3. **Saves store only what the player changed:** the world seed and population, the clock, the player (name, DNA share code, outfit, location) and the relationships. The town itself is regenerated from the seed.
   - **Consequence:** changing the catalogs or the generator changes existing towns. That's fine before release. After release, content changes need versioned catalogs or a saved population.
4. **Relationships use four meters** (familiarity, affection, attraction, trust, each 0–100).
   - Stranger, acquaintance, friend and close friend follow from the meters.
   - Dating and partner need the resident to say yes.
   - Each relationship remembers what the player has learned about the resident's tastes, which topics came up today, and refusals.
5. **Interactions are seeded per action.** The world counts actions, and each verb's randomness comes from the world seed, the resident and that count. A loaded save therefore replays identically, and this is tested.
6. **Appearance feeds the sim:**
   - The first impression comes from the player's current outfit against the resident's style taste, and it sets starting attraction.
   - Compliments land better when you're dressed to their taste.
   - A date puts both characters in their date outfits.
7. **Pacing is a tested design goal,** not just tuning. `ListeningPaysOff` checks that an attentive player gets a first date clearly sooner than one chatting at random, and within days rather than one visit or a season. Talks the same day have diminishing returns (1, 2/3, 1/2...), so visiting daily beats camping.
8. **The playable loop is `TownSession`** (pure C#). It covers travel, waiting, sleeping, selection, closing time and people leaving, and dates. Unity's `TownView` (UI Toolkit) and `TownStage` (3D) only draw it. This is the same pattern as `CreatorSession`.

## Pacing (simulated, 24 residents, 60 days)

The player visits one resident every evening with three talks and a compliment, plus a gift every other day.

| | First friend | First date | Partner (never within 60 days) |
|---|---|---|---|
| Attentive (loved topics, fitting gifts and places) | ~5 days | ~5 days | ~12 days (3 of 24) |
| Random topics and gifts | ~6 days | ~10 days | ~17 days (13 of 24) |

Partner may still be too quick for a sandbox. Retune it after real play sessions.

## Consequences / next

- **The town is a stage, not a space.** Each location is a blockout set showing up to six present residents. There's no walking or NavMesh yet (Phase 5 plans both). Residents teleport between places on the hour.
- **Dialogue is one line per reaction,** chosen by tone. It works, but it repeats quickly. Next steps are more lines per key, lines conditioned on stage, and memory callbacks ("How was the café yesterday?").
- **Not built yet:**
  - NPC-to-NPC relationships, gossip and rivals.
  - Shops that sell from the character catalog.
  - Weather, and outfits changing by occasion beyond casual and date.
  - Relationship decay.
- **Tests:** 42 sim tests (the town, profiles, schedules, interactions, pacing, saves and the session) pass on .NET 8 and in Unity. The prototype is verified headless by `TownCapture`, which presses HUD buttons through a scripted visit and saves `Docs/review/town-*.png`.
