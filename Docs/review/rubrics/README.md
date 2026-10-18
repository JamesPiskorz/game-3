# Review rubrics

These are the questions a review answers about each kind of render (graphics plan, track V, step V2). They turn "does it look better?" into checkable yes/no questions.

| Rubric | For |
|---|---|
| [diagnostics.md](diagnostics.md) | One part's diagnostic sheet: `Docs/review/diagnostics/<part>.png` (**Game → Characters → Review → Render Diagnostic Sheets**) |
| [faces.md](faces.md) | `faces-*.png`, `expressions.png`, `face-styles.png`, `creator-face*.png` |
| [lineups.md](lineups.md) | `lineup-*.png`, `qa-lineup-casual.png`, `archetype-sheet.png`, `family-sheet.png` |
| [captures.md](captures.md) | Play-mode captures: `town-*.png`, `creator-*.png`, `menu*.png` |

## How to answer

- **[look]** questions are answered from the image by the reviewer (the judge agent, or a person).
  - Answer each one yes, no, or n/a. A question marked *(from G1.x)* is n/a until that step lands.
  - For every "no", write the **defect, its location (sheet and tile, counted from the top left), and a fix**.
- **[metric]** questions are answered by a script, never by eye: models are poor at proportions, counts, contrast and light behaviour (see the cross-check, item 32).
  - Each one names the script. Until the script exists, the question stays **open**. Don't guess it.
- **For an A/B comparison**, list the defects in both images first, then give a verdict: A, B or tie. The verdict comes last, so it's built on the defects rather than the other way round.
- **Art has the final say** wherever the judge and the metrics disagree.

## Coverage

Each element of the target look (R1-R12 in the [graphics plan](../../graphics-plan.md#the-reference-look-element-by-element)) and each baseline problem (B1-B12, [Baseline before G0](../../graphics-plan.md#baseline-before-g0)) has at least one question.

| Item | What | Questions |
|---|---|---|
| R1 | Painted shadows | D13, D17 |
| R2 | No self-shadowing | D10, D11 |
| R3 | No partial shadow | C6, D11 |
| R4 | Warm bands at the shadow edge | D13, D18, F8 |
| R5 | Face light map | F4, F5 |
| R6 | Metals | D15 |
| R7 | Specular | D15 |
| R8 | Hair shine | D16 |
| R9 | Matcap patterns | D19 |
| R10 | Outlines | D14, L8 |
| R11 | Rim light | C4, D20 |
| R12 | Glow | C8, C9, F3 |
| B1 | Jagged edges | C1, L8 |
| B2 | No post-processing | C8, C9, C12 |
| B3 | Characters and sets don't match | C2 |
| B4 | No sky or atmosphere | C3 |
| B5 | Flat lighting, worst at night | C4, C7 |
| B6 | Blocky patches on hair | C5, D10, D11 |
| B7 | Face details at distance | F6 |
| B8 | Brows on darker skin | F1, F2 |
| B9 | Eye whites in shade | D12, F3 |
| B10 | Crude bodies | D1, D2, D5, L5 |
| B11 | UI | C10 |
| B12 | Draw-call cost | C11 |

## Scripts that answer metric questions today

| Script | Answers | Run with |
|---|---|---|
| `ShadowCheck` | D11 (R2-R3): a board over the body leaves the character as in the open; a board over the head shadows all of it | `-executeMethod Game.Characters.Review.ShadowCheck.Run`; exits 0 on pass |
| `TownBenchmark` | C11 | `-executeMethod Game.Characters.Review.TownBenchmark.Run`; writes `perf.md` |
| `GeneratorReport` | L4 (0 rule violations) | `-executeMethod Game.Characters.Review.GeneratorReport.Write`; writes `generator-stats.md` |
| The Blender validator | D6 | `Tools/Blender/character_tools.py`, `validate_part` |

The other metrics (palette distance, separation, silhouette IoU, contrast, line and rim widths, face-shadow monotonicity) are V6, not built yet.
