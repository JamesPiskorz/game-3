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
8. **Dialogue is templated, but it remembers.**
   - Lines carry a tone and can carry a stage range.
   - Each relationship keeps the last line used per reaction, so lines never repeat back to back.
   - The latest great date, loved gift and loved topic are kept for three days. The first greeting on a new day brings one up, once.
   - Meeting someone in an outfit they love gets the piece named ("I love your sneakers"). A compliment while you're dressed to their taste is returned the same way.
9. **The boutique sells from the character generator.**
   - Each day's stock comes from outfits generated in random archetypes' styles, so every piece arrives in colours chosen to work together.
   - Buying puts the piece on. The wardrobe keeps outfits wearable (a dress replaces top and bottom; a top pulls a bottom back from the wardrobe) and applies the generator's tag rules.
   - Money comes from hourly shifts, which cost the time that could go to socialising.
   - A test checks the point of all this: shopping for someone's style raises their first impression.
10. **Dates are short scenes** (`DateScene`).
    - Each of three rounds offers three topics not yet raised (usually one they love) and a compliment.
    - The outcome is the rounds' average plus how well the place suits them: great, fine or awkward.
    - `AskOnDate` still plays a whole date in one call, choosing as an attentive player would, for tests and simulations.
    - A test checks that attentive choices give clearly more great dates than careless ones.
11. **Whereabouts are knowledge.** The contact book reveals someone's favourite haunt at familiarity 15 and where they are right now at 30, so schedules become something to learn rather than a guessing game.
12. **Residents have lives of their own** (`SocialWeb`).
    - Friendships and couples are derived from the town seed: shared loves, shared haunts, temperament, and mutual taste in each other's style.
    - About a quarter of the town starts in a couple. Taken residents turn the player down, unless affection is at least 85 and attraction at least 75. Then they leave their partner, and the ex loses 20 affection for the player.
    - Breakups are the one part of the web that's saved.
    - A friend who's fond of the player puts in a good word, once.
    - This is the start of stories that differ by seed. Gossip, rivals and NPC dates come next.
13. **The day has a shape.** Gifts cost coins and shifts earn them. Sleeping sums up the day and autosaves. Friends left alone for more than three days cool by 1.5 affection a day; someone dating the player says they miss them.
14. **Expressions and idle motion are character-layer features** (`FaceExpression`, `IdleMotion`). They are symmetric offsets on the stand-in's feature bones, layered over the builder's base and rebased after every Apply. The game maps reactions to faces by tone.
15. **The playable loop is `TownSession`** (pure C#). It covers travel, waiting, sleeping, selection, closing time and people leaving, and dates. Unity's `TownView` (UI Toolkit) and `TownStage` (3D) only draw it. This is the same pattern as `CreatorSession`.

## Pacing (simulated, 24 residents, 60 days)

The player visits one resident every evening with three talks and a compliment, plus a gift every other day.

| | First friend | First date | Partner (never within 60 days) |
|---|---|---|---|
| Attentive (loved topics, fitting gifts and places) | ~5 days | ~5 days | ~12 days (3 of 24) |
| Random topics and gifts | ~6 days | ~10 days | ~17 days (13 of 24) |

Partner may still be too quick for a sandbox. Retune it after real play sessions.

## Consequences / next

- **The town is a stage, not a space.** Each location is a blockout set showing up to six present residents. There's no walking or NavMesh yet (Phase 5 plans both). Residents teleport between places on the hour.
- **Dialogue still runs on single lines.** With about 5 lines per reaction and no back-to-back repeats it holds up for a session. Multi-line exchanges and conversation choices are the next step.
- **Expressions are limited by the stand-in face:** an ellipsoid mouth can open but can't curve into a smile. Real heads should drive blendshapes instead.
- **Not built yet:**
  - NPCs dating and gossiping among themselves over time (the web is static apart from breakups).
  - Weather, and outfits changing by occasion beyond casual and date.
  - Jealousy when dating several people.
- **Tests:** 78 sim tests (the town, profiles, schedules, interactions, pacing, dialogue, dates, the boutique, contacts, the social web, the day loop, saves and the session) pass on .NET 8 and in Unity. The prototype is verified headless by `TownCapture`, which presses HUD buttons through a scripted visit and saves `Docs/review/town-*.png`.
