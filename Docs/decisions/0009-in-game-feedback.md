# 0009: In-game feedback, filed as GitHub issues

_2026-10-23_

## Context

Playtest notes like "her sleeve looks wrong at the gala" are hard to act on later: which character, which outfit, which
fabric, which build, and what was the renderer actually drawing? The game knows all of that when the player notices.

## Decision

**F8**, or the small **Feedback (F8)** button in the bottom-left corner, works in every scene (`Assets/_Game/Feedback`).
It freezes the moment and opens a form for a title, a kind (Bug, Looks wrong, Idea, Other) and a description, with the
game paused and its UI disabled underneath.

- **The moment** (`FeedbackContext`, read before the form opens so nothing of the form is in it):
  - the screenshot;
  - the build: version, and the commit, branch and whether it had uncommitted changes. A player build carries these
    (`FeedbackBuildInfo` writes them in for the length of the build); the editor asks git.
  - the machine, the screen and quality settings, and the frame rate and memory;
  - every camera and light;
  - **every character**: who they are, seed, style, outfit, whether on screen and where, distance and level of detail,
    their DNA, and each part's renderers and materials with **every shader property value** they're drawn with
    (fabric map, gloss, sheen, print, placement, colours…);
  - each screen's own state through `IFeedbackSource`: the town's time, weather, place, occasion, events, the player,
    who's selected and who's present in which outfit, the notice and the hint; the creator's look and view;
  - the text showing on the UI, and the last 200 log messages with the stacks of errors.
- **Attachments:** the town as a save (`town-save.json`, load it to stand where the ticket was made), or in the creator
  the look (`look.json`).
- **Saved** as `feedback-tickets/NNNN-title/` at the root of the checkout (gitignored): `ticket.md` (the issue's text),
  `context.json`, `screenshot.png` and the attachments. Outside a checkout they go in the game's persistent data
  folder, and `-feedback-dir` puts them elsewhere.
- **Filed** in the background with the GitHub CLI, as whoever is signed in to `gh`:
  - the files are uploaded to the `feedback-attachments` pre-release (made on first use), so the screenshot shows in
    the issue and the game state is a link away;
  - the issue is "[Kind] Title", labelled `feedback`;
  - `github.json` in the ticket's folder records the issue;
  - a ticket that couldn't be filed (no `gh`, not signed in, offline) goes with the next one, or with
    **Game > Feedback > File Waiting Tickets on GitHub**.

The game holds no token: filing needs `gh` installed and signed in (`gh auth login`) on the machine it runs on;
elsewhere tickets just stay local. `-feedback-no-file` keeps them local, and `-feedback-no-button` hides the button.

The repository is public, so **feedback issues are public**. A ticket carries no file paths or machine name, but does
name the OS, CPU and GPU, and the description and screenshot go up as they are.

## Consequences

- The form is IMGUI: it needs no assets and draws over any scene. The project has no screen-capture module, so the
  screenshot is read back from the finished frame.
- `ModularCharacter` remembers the DNA and outfit it was built from (`CharacterBuilder` sets them), so anything can say
  what a character on screen is.
- The player smoke test checks a real ticket: `Game3.exe -batchmode -smoke -smoke-feedback -feedback-no-file`. In a
  window it also needs a screenshot, and `-smoke-feedback-form <png>` photographs the form
  (`Docs/review/feedback-form.png`).
