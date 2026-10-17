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
14. **The town reaches out.** Each morning, close friends and partners may invite the player to meet later that day, somewhere their own plans already take them. Turning up starts a date with no asking; standing them up costs 6 affection and 4 trust. This gives the player plans to keep, not just people to find.
15. **Weather is derived, not saved.** Each day's weather comes from the seed (20% rain, 30% cloud). Rain moves people from outdoor plans to an open indoor favourite or home. That's applied in `SimWorld.WhereIs`, the single source of truth for whereabouts, which presence, contacts and invitations all use.
16. **Evenings out look different.** Residents wear their date outfit for evenings somewhere romantic, and for Friday and Saturday nights anywhere social.
17. **The player has stats, and styles admire them.** Charm, fitness, smarts and style are raised by place activities, with tapering gains, at the cost of time and sometimes coins.
    - Each archetype admires one or two stats, adding up to 0.35 to the first impression. Sporty types admire fitness; bookish ones smarts.
    - Charm improves compliments. Knowing a loved topic well (fitness for sport, smarts for books) makes talking about it count for more.
    - This gives the player a second lever besides clothes: who you are, not just what you wear.
18. **Dates use the place.** Each location offers things to do together that suit some interests, such as dancing at the bar or feeding the ducks. One appears in a round in place of a topic. The date that makes someone a partner ends with them asking to be together, a scripted beat inside a systemic loop.
19. **Robustness and balance are tested by play.** A fuzz test drives the session with a random player and checks world invariants after every step; it found a real bug on its first run. `AutoPlayer` plays a plausible attentive month. The story tests and `Docs/review/stories.md` check that it goes somewhere and that towns differ.
20. **Expressions and idle motion are character-layer features** (`FaceExpression`, `IdleMotion`). They are symmetric offsets on the stand-in's feature bones, layered over the builder's base and rebased after every Apply. The game maps reactions to faces by tone.
21. **The playable loop is `TownSession`** (pure C#). It covers travel, waiting, sleeping, selection, closing time and people leaving, and dates. Unity's `TownView` (UI Toolkit) and `TownStage` (3D) only draw it. This is the same pattern as `CreatorSession`.
22. **Chats are choices scored by personality, not by a script.** The resident opens (a loved topic, their day, a worry, a daydream) and the player picks a warm, playful or curious reply. Each style's appeal is a formula over the Big Five plus the opener, and a reply is judged against the average of the three, so there is always a better and a worse answer but no fixed right one.
    - Worries need familiarity, so strangers don't confide.
    - A reply that lands draws them out: they say more and the player answers again. A miss ends the chat. Two good replies in one chat leave them feeling heard, a small bonus to affection and trust.
    - Chats speed the simulated attentive player's first partner from about 15 days to about 13; the gains were trimmed so listening helps without becoming a shortcut.
    - Two good replies of one style teach the player what that person responds to. It's saved, shown on the card and marked on later replies, the same "learn by playing, then see it written down" pattern as likes.

23. **Families come from the character generator, not from the sim.** The town asks the population generator for a quarter of residents as siblings; the sim then gives them the family's surname and an age a few years apart. Every other family name is unique, so a shared surname always means family. Siblings aren't paired as friends or couples. They count as part of each other's circle for news, good words and jealousy, and a stranger whose sibling is fond of the player greets them as someone they've heard about.
    - Saves regenerate the town from its seed, so changing town generation (as families did) changes who lives in an existing save's town. That's acceptable before release; a released game will need town generation versioned with the save.

24. **Looks change over time, and attention is rewarded.** Each week about one resident in eight has a makeover: a new haircut (same colour), a new colour (same cut) or new everyday clothes, made by rerolling that part of their DNA in their own style.
    - It's a pure function of the town seed and the week, so saves don't store it: the world remembers which week it has applied, and a loaded game catches up.
    - Someone the player knew before the change brings it up when they next meet. The card says "something's different" while it's new. A compliment within the week counts for about three times a normal one, once.
    - This uses the character generator at runtime, so the town looks different from one month to the next.

25. **The player's hair is part of how they come across.** Hair pieces carry the same style tags as clothes, and first impressions now count them. The salon changes cut, bangs and colour.
    - Anyone who knew the old look reacts once by their average taste across the new hair's style tags. It's an average, not the best tag, because nearly everyone likes something casual.
    - Measured over a town, that taste runs from about 0.14 (10th percentile) to 0.42 (90th). "Loves it" is 0.4 or more and "different" is below 0.2, so restyling for one person's taste is a real choice.

26. **Seeing several people has a cost.** When a date ends, everyone else the player is dating hears about it ("the town talks"), and brings it up at their next greeting, hurt. A partner forgives once; the second time, or when trust falls below 25, they leave. Someone only dating leaves the third time. The AutoPlayer stays loyal once it has a partner, but juggles before then, so `Docs/review/stories.md` now has breakups in it.

27. **The player dresses by the same rules as everyone.** They change into their rainy-day, workout or going-out outfit (all generated by the creator) wherever residents would. The wardrobe edits whichever outfit is on, and offers only what suits it: no tank top on a rainy day, nothing but sportswear at the gym. A look made before some contexts existed gets them, generated in its own style. The fuzz test's DNA check caught the wardrobe breaking those rules before this.
    - Residents also dress per person: at their job during working hours they wear their "Work" outfit (smart pieces only; students aside, and trainers in workout clothes), so the café and library look staffed.

## Pacing (simulated, 24 residents, 60 days)

The player visits one resident every evening with three talks and a compliment, plus a gift every other day.

| | First friend | First date | Partner (never within 60 days) |
|---|---|---|---|
| Attentive (loved topics, fitting gifts and places) | ~5 days | ~5 days | ~12 days (3 of 24) |
| Random topics and gifts | ~6 days | ~10 days | ~17 days (13 of 24) |

Partner may still be too quick for a sandbox. Retune it after real play sessions.

## Consequences / next

- **The town is a stage, not a space.** Each location is a blockout set showing up to six present residents. There's no walking or NavMesh yet (Phase 5 plans both). Residents teleport between places on the hour.
- **Dialogue is still mostly single lines.** With about 4 lines per reaction and no back-to-back repeats it holds up for a session. Chats add a choice per exchange, up to two exchanges, and what they say next answers the style of the player's reply.
- **Expressions are limited by the stand-in face:** its mouth is three pieces whose corners turn, which is enough for smiles and frowns but not for shapes like a pout. Real heads should drive blendshapes instead.
- **Not built yet:**
  - Residents don't visibly do things together in a scene beyond standing near friends; the town's couples change weekly, but only off-screen.
  - Nobody changes job or home, and ages don't advance.
- **Tests:** 146 sim tests (the town, families, makeovers, the salon, faithfulness, occasions, profiles, schedules, weather, interactions, chats, pacing, dialogue, dates, the boutique, contacts, the social web, invitations, the day loop, saves, the session, a fuzz test and story tests) pass on .NET 8 and in Unity. The prototype is verified headless by `TownCapture`, which presses HUD buttons through a scripted visit and saves `Docs/review/town-*.png`.
