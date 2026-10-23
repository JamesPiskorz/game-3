# Judge report: scripted character, second pass

_Track V judging for the second pass of the scripted-art spike, [decision 0009](../decisions/0009-scripted-character-art.md#second-pass). 2026-10-18._

**Method.** As in the [first report](scripted-character-judge.md): a subagent with fresh context got only the renders, the prompt and the rubric, and listed defects before its verdicts. It saw no code, no plan and none of the building session. This time it got two sets of renders and three characters, and answered two A/B questions:

- the second pass against the first pass;
- the second pass against the stand-in.

To keep the first question blind, the two scripted versions are called B and C and described as "variants from the same scripts with different settings". The judge wasn't told which came first. Its report is pasted unedited under [Report](#report).

There were two judging rounds, each with a new subagent and the same prompt. Round 1 judged an intermediate build. Its report is kept unedited in the [appendix](#appendix-round-1-report-intermediate-build), and its top defects were fixed before the final renders:

- dark slabs beside the face;
- a flat "sticker" highlight on the scalp cap;
- a uniform sawtooth hem;
- shoes that read as sandals;
- brows under the bangs.

The intermediate renders weren't kept.

**Renders** (Game → Characters → Review → **Render Scripted Character Sheet**, current toon shader, Day and Sunset lighting rigs, 4× MSAA):

| Set | Character sheet | Poses | Scripted character |
|---|---|---|---|
| B | [scripted-character-pass1.png](scripted-character-pass1.png) | [scripted-poses-pass1.png](scripted-poses-pass1.png) | First pass (commit 98a7911), borrowing the stand-in sneakers |
| C | [scripted-character.png](scripted-character.png) | [scripted-poses.png](scripted-poses.png) | Second pass, with its own scripted sneakers |

A, the stand-in, is the same in both sets.

## Prompt and rubric given to the judge

The rubric is the first report's, word for word. The image list, the character descriptions and the report format were extended to cover three characters. The scratch-folder path is shortened here.

> You are a strict art reviewer for a stylised anime dating sim made in Unity. You have no other context: judge only what the images show. Do not read any other files, code or documents; use only the Read tool on the four images named below.
>
> Images (read them with the Read tool; look closely at each row). They are in (scratch folder):
> 1. set-B-character.png and set-C-character.png (each 1600 x 4900, same layout). Rows, top to bottom:
>    (1) full body, front, daylight: the stand-in on the LEFT, the scripted character on the RIGHT;
>    (2) full body, back, daylight: mirrored, so the scripted character is on the LEFT and the stand-in on the RIGHT;
>    (3) face close-ups, four tiles left to right: stand-in front, scripted front, stand-in three-quarter, scripted three-quarter;
>    (4) four tiles left to right: stand-in upper body front, scripted upper body front, stand-in from behind, scripted from behind;
>    (5) full body three-quarter, daylight: stand-in on the LEFT, scripted on the RIGHT;
>    (6) the same three-quarter view at sunset: stand-in on the LEFT, scripted on the RIGHT.
> 2. set-B-poses.png and set-C-poses.png (each 2000 x 2100). Three rows of test poses (relaxed, reach, sit); each row has four tiles left to right: stand-in front, scripted front, stand-in side, scripted side.
>
> There are three characters:
> - A is the current placeholder character (procedural "stand-in" parts). It is the same in both sets.
> - B is the scripted character in the set-B images. Its body, head, hair, jacket and shorts were generated entirely by scripts in Blender (no hand sculpting or painting). B borrows A's sneakers.
> - C is the scripted character in the set-C images: another variant from the same scripts with different settings, also generated entirely by script (no hand sculpting or painting), with its own scripted sneakers.
>
> All three use the same toon shader, colours, pose and lighting. The target look is a Genshin Impact-like stylised anime character (cel shading, painted-looking detail, clean outlines, expressive faces, layered clumpy hair, tailored costumes).
>
> Rubric (answer each for A, B and C, yes / partly / no, naming the tile you looked at):
> 1. Silhouette: do body and clothes read as smooth, continuous forms (no faceting, tube joints, visible seams between body parts)?
> 2. Proportions and anatomy: adult, believable proportions; hands with fingers; feet; neck and shoulders.
> 3. Face: does it read as an appealing anime face (eyes, brows, mouth) at full-body distance and in close-up?
> 4. Hair: does it read as hair (clumps, pointed tips, volume, layering, highlight) rather than a shell or helmet?
> 5. Clothing: does the garment read as tailored clothing (collar, cuffs, hem, seams, folds, trims, buttons)?
> 6. Shading: clean toon shading, no blotches, noise, stripes or z-fighting?
> 7. Outlines: even and continuous, no broken or doubled lines?
> 8. Clipping: anything poking through anything else (hair through clothes, clothes through clothes, body through clothes or shoes)?
> 9. Deformation (poses images): do shoulders, elbows, hips and knees keep their volume without collapsing, tearing or candy-wrapping?
> 10. Overall closeness to the target look.
>
> Report format, in this order:
> - **Defects in C**, then **Defects in B**: a numbered list each; each defect names the tile (image, row and position), the location on the character, and a concrete suggested fix. List everything you see, worst first.
> - **Rubric**: a table, one row per question, columns A, B and C.
> - **Verdict 1, C against B**: does C beat B? Answer "C", "B" or "tie", with a confidence (low / medium / high) and the three reasons that decided it.
> - **Verdict 2, C against A**: does C beat A? Answer "C", "A" or "tie", with a confidence (low / medium / high) and the three reasons that decided it.
> - **Distance from the target**: in a few sentences, how far C is from the target look, what is closest and what is furthest away. Be honest; don't soften.
>
> Reply with the report only, as Markdown.

## Report

Pasted unedited; only its headings are moved down two levels to sit under this one.

### Art review: stand-in (A) vs scripted B vs scripted C

#### Defects in C

1. **The top of the head reads as ears or earmuffs, not hair.** Seen in set-C-character row 3 tile 2, row 1 right, and set-C-poses column 2 in every row. Two rounded lobes sit at the top left and top right of the skull. They make the head look square, like cat ears, buns or headphones. *Fix:* replace them with one crown dome that sits close to the skull, about 8–10% of head height at most. Build the volume from overlapping tapered clumps that spread out from a parting. If buns are intended, make them clearly separate shapes with a tie.
2. **In profile, a side lock lies on the cheek and reads as a sideburn or beard.** Seen in set-C-poses column 4, rows 1–3. A dark lock runs flat down the cheek from under the eye to the jaw, apparently on or inside the face surface. *Fix:* root it further back at the temple, push it off the face (collision or offset from the head), and taper it so the cheek and jaw read in profile.
3. **The hair has no highlight or toon banding, and the back hair is a flat curtain of parallel comb lines.** Seen in set-C-character row 4 tile 4 and row 2 left. *Fix:* add an anime highlight band (angel ring) through a hair ramp or a UV-placed band. Build 2–3 layers of different lengths with gaps between them. Vary clump widths and stagger the pointed tips.
4. **In three-quarter view, a side lock sticks straight out.** Seen in set-C-character row 3 tile 4. A lock at the left temple flares out like a wing, and the side volume forms a round blob behind the face. *Fix:* make the lock droop under gravity and pull the side volume in toward the head.
5. **The hair length looks different from front and back.** Compare set-C-character row 4 tile 2 with tile 4. From the front it reads as a shoulder-length bob with puffy sides; from behind it reaches the waist. *Fix:* bring one or two long locks forward over the shoulders so the length reads from the front.
6. **At sunset the shoes look like sandals.** Seen in set-C-character row 6 right. The white socks and uppers turn the same orange as the skin, and only the dark sole and lace lines remain, so they read as straps on bare feet. *Fix:* give the socks and uppers a cooler or greyer base, or a sock colour, plus a firmer outline where the sock meets the skin.
7. **The hands are thin and spidery.** Seen in set-C-poses row 1 column 2 and row 2 column 2. The fingers splay stiffly like a fork, and the palm has little mass. *Fix:* thicken the fingers and palm, group the middle, ring and little fingers, and add a relaxed default curl.
8. **The outline of the seat is lumpy when sitting.** Seen in set-C-poses row 3 column 4, along the back edge of the shorts. *Fix:* add a corrective shape for bending at the hip, or more edge loops and smoothing around the seat.
9. **The face is generic and blank.** Seen in set-C-character row 3 tiles 2 and 4, and row 1 right. The mouth is a tiny mark and the brows are hidden or too faint at full-body distance. There is no blush and no nose accent. From full-body distance the eyes read as flat cyan spots. *Fix:* draw heavier brows that show above the bangs, a larger shaped mouth line, a mark on the nose tip and blush. Give the upper lashes more shape and the eyes clear highlights.
10. **The jacket is clean but bare.** Seen in set-C-character row 4 tile 2. The sleeves are smooth tubes with no folds at the elbow, armpit or waist. There are no pockets, piping, layered trims or accessories. *Fix:* add fold lines or shadow shapes at the elbows, inner arms and waist, and one or two costume details.
11. **The legs are long, uniform tubes in front view.** Seen in set-C-character row 1 right. Knees and calves have almost no shape, and the gap between the thighs is wide. *Fix:* shape the knee and calf, and narrow the stance slightly.
12. **Minor: the shadow on the cream trim turns a saturated lilac.** Seen in set-C-character row 2 left. From behind, the jacket's hem band reads as a separate pink or lilac band. *Fix:* use a warmer, less saturated shadow colour for the cream material.

#### Defects in B

1. **The hair is a stringy mop.** Seen in set-B-character row 3 tiles 2 and 4. Dozens of thin strands, each with its own outline, make the hair scribbly and noisy. In three-quarter view it is a chaotic mass. *Fix:* merge the strands into 10–15 broad clumps with tapered tips, drop the outlines inside a clump, and add a highlight band.
2. **In profile, the hair hides the face.** Seen in set-B-poses column 4, rows 1–3. The side of the head is all hair, and the nose, mouth and chin barely show. *Fix:* move the side locks off the face, tuck one behind the ear, or shorten them.
3. **In three-quarter close-up, the hair covers the face.** Seen in set-B-character row 3 tile 4. Strands cross the near eye and cheek. *Fix:* same as item 2.
4. **The hair piles onto the shoulders.** Seen in set-B-character row 1 right and row 3 tile 2. Dark masses spread over, and apparently into, the jacket shoulders like a cape, widening the shoulders. *Fix:* make the hair collide with the shoulders and jacket with an offset, and let the locks drape over them.
5. **The borrowed shoes are blobs.** Seen in set-B-poses row 1 columns 2 and 4. They are featureless white lumps from the front and flat box slabs from the side, so they read as slippers. *Fix:* replace them with modelled sneakers, as C has.
6. **The shoes disappear at sunset.** Seen in set-B-character row 6 right. They turn the same orange as the skin and read as bare feet. *Fix:* increase the contrast in value between shoe and skin.
7. **The back hair has flyaways.** Seen in set-B-character row 4 tile 4. Stray strands stick out on the left, the tips are ragged, and there is no highlight. *Fix:* trim the flyaways, taper the tips and add a highlight band.
8. **The hands are spidery.** Seen in set-B-poses row 1 column 2 and row 2 column 2. Same fix as C item 7.
9. **The outline of the seat is lumpy when sitting.** Seen in set-B-poses row 3 column 4. Same fix as C item 8.
10. **The face is generic.** Seen in set-B-poses row 1 column 2 and set-B-character row 3 tile 2. The face is long and narrow with a pointed chin and heavy eyelids, the brows can't be read, and the mouth is tiny. *Fix:* same as C item 9.
11. **The shorts are plain.** Seen in set-B-poses row 1 column 2. They show only a fly line and a waist line, with no folds. *Fix:* add folds pulling toward the crotch and pocket openings, as C partly has.
12. **The rest matches C.** The jacket is bare and the legs are tubes (set-B-character rows 4 and 1). The cream trim also turns lilac in shadow (set-B-character row 2 left).

#### Rubric

| # | Question | A (stand-in) | B | C |
|---|---|---|---|---|
| 1 | Silhouette | **No.** Blocky, faceted head; tube arms and legs with hard joints; boxy sweater (set-B-character row 1 left, row 3 tile 1) | **Yes** for body and clothes, smooth and continuous. The hair outline is ragged (row 1 right) | **Yes** for body and clothes. The top of the head is lumpy (set-C-character row 1 right, row 3 tile 2) |
| 2 | Proportions and anatomy | **Partly.** Adult height, but stick hands with no fingers, blob feet, cylinder neck and wide boxy hips (poses row 1 column 1) | **Partly.** Adult and slim, with fingers. The feet are A's blobs and the hands are spindly (set-B-poses row 1 column 2) | **Yes, with reservations.** Adult and slim, with fingers and real sneakers. The hands are spindly and the legs tubular (set-C-poses row 1 column 2) |
| 3 | Face | **No.** Block head with decal eyes (row 3 tile 1) | **Partly.** Anime eyes read in close-up, but hair hides the face in three-quarter and profile views, and it is blank (row 3 tile 4; poses column 4) | **Partly.** A clear anime face, unobstructed from front and three-quarter, but blank. The profile shows the cheek lock (row 3 tiles 2 and 4; poses column 4) |
| 4 | Hair | **No.** A helmet shell and a flat slab (row 2 right, row 4 tile 3) | **Partly.** Reads as long hair, but stringy and noisy, with no clumps and no highlight (row 3 tiles 2 and 4) | **Partly.** Cleaner and clumpier, with pointed tips at the back. The crown lobes read as ears, and there is no highlight (row 3 tile 2, row 4 tile 4) |
| 5 | Clothing | **No.** A sweater with a V insert and hem bands only (row 4 tile 1) | **Partly.** Collar, placket, buttons, cuffs, hem band and seams, but no folds; plain shorts; blob shoes (row 4 tile 2) | **Partly, leaning yes.** The same jacket, plus shorts with crease folds and a fly, and sneakers with laces, sole and socks (row 4 tile 2; poses row 1 column 2) |
| 6 | Shading | **Partly.** Smooth gradient on the hair slab, not toon; a dark disc at the neckline (row 3 tile 1, row 4 tile 3) | **Partly.** The body is clean, but the hair is noisy with line hatching (row 3 tile 4) | **Partly, mostly clean.** The back hair has comb-line stripes and no highlight; the cream shadows turn lilac (row 4 tile 4, row 2 left) |
| 7 | Outlines | **Partly.** Even, but they show the facet edges (row 3 tile 1) | **Partly.** The body is even; inside the hair the lines are dense and scribbly; the seat outline is wavy (row 3 tile 2; poses row 3 column 4) | **Partly.** Mostly even with fewer hair lines; the seat outline is wavy (poses row 3 column 4) |
| 8 | Clipping (yes = clean) | **Partly.** The shorts don't follow the raised thigh in the reach pose; the hair slab cuts through the shoulders (poses row 2 column 1, row 1 column 3) | **Partly.** Hair masses sink into the jacket shoulders (row 3 tile 2) | **Partly, mostly clean.** The side lock lies on or in the cheek in profile (poses column 4) |
| 9 | Deformation | **No.** Rigid tube joints at elbows and knees (poses row 2 columns 1 and 3) | **Yes, mostly.** Shoulders, elbows, hips and knees keep their volume in the reach and sit poses; the seat is lumpy when sitting | **Yes, mostly.** Same as B (set-C-poses rows 2 and 3) |
| 10 | Closeness to target | **No** | **Partly, low** | **Partly, low to medium** |

#### Verdict 1: C against B

**C wins, medium confidence.**

1. **Hair:** C's hair is cleaner and less noisy, and it leaves the face visible from the front and in three-quarter view. B's is a stringy mop that hides the face in three-quarter view and almost entirely in profile (set-B-character row 3 tile 4 and set-B-poses column 4, against the same tiles in set C).
2. **Shoes:** C's scripted sneakers, with sole, laces, toe cap and socks, read as real shoes. B's borrowed blobs read as slippers (poses row 1 columns 2 and 4).
3. **Shorts:** C's shorts have crease folds and a fly, so they read as more tailored (poses row 1 column 2).

The confidence is not high because C's crown lobes and its cheek lock in profile are new oddities. At full-body distance, B's long flowing hair arguably reads as more glamorous and "heroine".

#### Verdict 2: C against A

**C wins, high confidence.**

1. **Body:** C's body and clothes are smooth and continuous, with fingers and real shoes. A is faceted blocks with tube joints and stick hands (character row 1, poses row 2).
2. **Face and hair:** C has a readable anime face and hair with strands and tips. A has a block head with decal eyes and a helmet-and-slab hairdo (character row 3 tiles 1 and 2).
3. **Costume and posing:** C has a tailored jacket with collar, placket, buttons and cuffs, and deforms without collapsing in the reach and sit poses. A has a boxy sweater, and its shorts fail to follow the legs (poses rows 2 and 3).

#### Distance from the target

C is still far from a Genshin-like character. It reads as a clean but bare 3D anime base model, closer to an un-customised avatar-maker default than to a finished gacha hero.

- **Closest to the target:** the smooth body silhouette and adult proportions, the clean toon shading and even outlines on the body, the basic tailoring of the jacket, the poses deforming without collapse, and the shoes.
- **Furthest from the target:**
  - **Hair:** no highlight band, no deliberate layered clumps, and two strange crown lobes, all on the feature that most defines the style.
  - **Face:** none of the expressive painted detail of the target. No readable brows, mouth shape, blush or rich eyes.
  - **Costume:** nowhere near Genshin's density of trims, ornaments, layered pieces, folds and painted texture. Everything is flat colour with no folds.

Being blunt, it is perhaps a third of the way there. The silhouette and topology are fine; the hair, face and costume detail are what remain.

*Note: the character sheets were viewed downscaled (about 0.4×), so very fine detail such as eye highlights, button count and thin outline breaks in the close-ups could not be judged reliably. The pose sheets were viewed at near full size.*

## Notes on the report

Where the judge's reading and the build differ, for whoever acts on it:

- **Hair highlight (C3, rubric 4).**
  - The final build paints one highlight stroke per clump (mask region 3 and lightmap G): about 1% of the back texture and 2% of the front.
  - At the 0.4× the judge viewed the sheets at, the strokes don't read.
  - In the round-1 build the ring also covered the scalp cap. That made it big enough to see, but the judge read it as a pasted-on sticker.
  - So the highlight is still unsolved. It needs bigger, designed strokes, or the shader's specular reading lightmap G (G1.5).
- **Jacket folds (C10).**
  - Painted fold strokes are in the jacket's detail map: drags from the armpits, bunching inside the elbows, the waist and the hem.
  - They show in the full-size close-ups (character sheet row 4, tile 2), but not at 0.4×, and they are low-contrast on dark navy. Neither judge saw them.
  - The shorts' strokes, on a light colour, were seen.
- **Knees (rubric 9).**
  - Both judges rated B and C the same on deformation. Round 1 said both pinch; round 2 said both hold "mostly".
  - Measured on the outside of the knee in the sit pose, the second pass keeps more volume: the worst vertex goes from 0.71 to 0.79 of its rest distance from the bones, and the 5th percentile from 0.74 to 0.85.
  - The change is real but doesn't show at sheet scale.
- **Crown lobes (C1).**
  - Confirmed. The outer clumps at the sides lie over the top of the skull and fall into toon shade.
  - Setting the normal proxy's centre back fixed the hair beside the face, not these lobes.
- **"Socks" (C6).** Since round 2, the shoe's collar is the upper's white, which the judge reads as socks. The sunset problem is the sneaker's white going the same orange as the skin.
- **Judges vary.** Round 1 rated B's deformation "partly" and the final judge "yes, mostly". Both rounds placed the scripted character about a third of the way to the target.

## Appendix: round 1 report (intermediate build)

Unedited. It judged an intermediate build of C (same B and A). The defects it found in C were fixed before the final renders above, as listed under Method.

Headings moved down two levels, as above.

#### Defects in C

Tile references use the format image / row / tile (left to right). Note: the character sheets render at about 40% size in my viewer, so small details such as brows, lashes and button shapes were judged mostly from the poses images.

1. **Hair side panels look like dark flat slabs.** Seen in set-C-character r3 t2 (scripted front face), r4 t2 and r1 right, and in set-C-poses r1 t2 and r3 t2. On both sides of the face, from temple to shoulder, there are broad, square-ended, near-black panels with almost no clump break-up. They read as headphones, ear-flaps or a hood, not side locks. They look like the darker inside of the back-hair shell seen from the front.
   - **Fix:** put 3–4 separate tapered side-lock clumps in front of them. Shade the inside of the shell the same as the outside, or close or thicken the shell so its back face never shows. Taper the bottoms to points.
2. **The hair highlight is a flat pink rectangle on the crown.** Seen in set-C-character r3 t4, also r1 right and set-C-poses r1 t2. It reads as a sticker or a skull-cap, much like A's flat helmet top.
   - **Fix:** make it a thin curved band that follows the head, broken per clump with zig-zag edges, and less saturated.
3. **The back hair is a uniform sawtooth curtain.** Seen in set-C-character r4 t4 and r2 left. The clumps are all the same width, the pointed tips all end at one length, and light and dark stripes alternate regularly. It is a single layer with no volume at the nape, so it reads as cut-paper fringe.
   - **Fix:** vary clump width and length by about 30%. Use 2–3 layers with a shorter top layer. Make the hem V- or U-shaped, let some clumps overlap or twist, and add a few flyaways at the silhouette.
4. **The sneakers read as strappy sandals at distance.** Seen in set-C-character r6 right (sunset) and r1 right. The dark collar, tongue and lace marks against skin read as straps. They read correctly only in the close poses tiles (set-C-poses r1 t2 and t4).
   - **Fix:** make the upper mostly white, with a lighter collar, fewer and larger lace marks and a clear continuous sole line. Keep the dark accents small.
5. **The hands have splayed, thin, claw-like fingers.** Seen in set-C-poses r1 t2 (both hands), r3 t2 and r1 t4, where fingers fan forward at the hip. Relaxed hands look like forks.
   - **Fix:** give the finger bones a default soft curl, thicken the fingers and palm, and turn the thumb in.
6. **The face is under-drawn.** Seen in set-C-character r3 t2 and t4. Brows are not readable (hidden under or lost in the bangs), the mouth is a tiny dash, there is no blush or nose shadow, and the face shading is flat. At full-body distance (r1 right, r5 right) the face reduces to two teal dots. At sunset (r6 right) the face splits into a hard dark half.
   - **Fix:** draw the brows over the hair, enlarge the eye whites and highlights, add a lash wing, give the mouth a shape, add blush, and use a face shadow map or normal-smoothed face shading.
7. **The face is hidden in profile.** Seen in set-C-poses r1 t4 and r3 t4. The dark side panel covers the cheek and jaw, so the side view barely reads as a face.
   - **Fix:** thinner side locks set further back, or locks that part around the cheek.
8. **The knees pinch in bent poses.** Seen in set-C-poses r3 t4 (sit, side) and r2 t4 (raised knee). The knee narrows to a sharp hinge.
   - **Fix:** add a knee helper bone or corrective shape key, plus more edge loops at the knee.
9. **The legs are straight tubes.** Seen in set-C-character r1 right and set-C-poses r1 t2. There is little calf, knee or ankle shape, and a narrow hard shadow strip down each leg makes them read as cylinders.
   - **Fix:** add calf curve and kneecap shaping in the body generator, and widen or soften the leg shadow threshold.
10. **The jacket lacks secondary detail.** Seen in set-C-character r4 t2 and t4.
    - There are no lapels, pockets or piping, and no folds at the elbows or waist.
    - The back is plain, with no seams or yoke.
    - The hem band is a flat stripe.
    - From behind, the cream hem reads as lilac-grey (r2 left).
    - **Fix:** add back seams or a yoke, elbow and waist fold lines, pocket flaps and trim piping, and flare the hem slightly. Check the trim colour in back light.
11. **The side-lock ends curl outward like rigid hooks.** Seen in set-C-character r3 t4. They look plastic.
    - **Fix:** a softer taper and less curl.

#### Defects in B

1. **The hair is stringy and noisy, not clumpy.** Seen in set-B-character r3 t4 (three-quarter close-up) and r4 t4 (from behind).
   - There are dozens of thin, separate strands with a ragged, spiky silhouette.
   - The light and dark of each strand varies, which reads as noise.
   - The hem is ragged, so the whole thing reads as a wet or messy mop, not anime clumps.
   - **Fix:** use far fewer, wider clumps with tapered pointed tips, and merge the thin strands. Remove the inner outlines between strands and unify the shading ramp.
2. **Hair masses at the shoulders look like dark spiky shoulder pads and appear to cut into the jacket shoulders.** Seen in set-B-character r3 t2 and t4, and set-B-poses r1 t2.
   - **Fix:** add a collision offset against the shoulders, and route each lock clearly in front of or behind the shoulder.
3. **There is no highlight band, and the hair is uniformly dark.** Seen in set-B-character r3 t2 and t4. At sunset (r6 right) the head becomes a near-black mass.
   - **Fix:** add a highlight band (angel ring) and lift the base value.
4. **Hair covers the face in three-quarter view.** Seen in set-B-character r3 t4. Front locks cover the cheek and the far eye, so the face reads small and dark.
   - **Fix:** shorten or move the front locks, and part the bangs.
5. **Long front locks cut across the chest and sleeves.** Seen in set-B-character r4 t2 and set-B-poses r2 t2. Thin strands cross the placket and upper arms, cut the jacket into pieces and hide the costume. They possibly clip into the sleeves when the arm rises.
   - **Fix:** use fewer front locks, or put one side behind the shoulder.
6. **The borrowed stand-in shoes don't match the rest.** Seen in set-B-poses r1 t4 and r3 t4 (a flat white slab with a dark base) and r1 t2 (white blobs). There are no laces, toe cap or collar, so they read as slippers and clash with the more detailed body.
   - **Fix:** use the scripted sneaker.
7. **The hands are claw-like with splayed fingers** (set-B-poses r1 t2). Same fix as C #5.
8. **The face is under-drawn**: brows not readable, tiny mouth, flat shading, and a hard half-face shadow at sunset (set-B-character r3 t2, r6 right). Same fix as C #6.
9. **The knees pinch** in set-B-poses r3 t4 and r2 t4. Same fix as C #8.
10. **The legs are tubes** (set-B-character r1 right). Same fix as C #9.
11. **The jacket lacks folds, pockets, lapels and back detail** (set-B-character r4 t2). Same fix as C #10.

#### Rubric

| # | A (stand-in) | B | C |
|---|---|---|---|
| 1 Silhouette | **No**: tube limbs, boxy torso and shorts, stub hands, tube elbow joint (char r1 left; poses r2 t1) | **Yes** for body and clothes: smooth, no seams (char-B r1 right; poses-B r1 t2). The hair silhouette is ragged. | **Yes** for body and clothes (char-C r1 right; poses-C r1 t2). The hair side slabs are blocky. |
| 2 Proportions and anatomy | **No**: mitten stubs with no fingers, block torso, slab shoes (poses r1 t1, t3) | **Partly**: adult, long-legged, neck and shoulders fine; but claw-like fingers, tube legs, slipper shoes (poses-B r1 t2, t4) | **Partly**: same body with real sneakers; claw-like fingers and tube legs (poses-C r1 t2, t4) |
| 3 Face | **No**: eyes readable, but a flat, crude face with a dash or "o" mouth (char r3 t1, t3) | **Partly**: decent front (char-B r3 t2); covered and dark in three-quarter (r3 t4); tiny at full body | **Partly**: clearer front and three-quarter (char-C r3 t2, t4); brows not readable, tiny mouth, flat, hidden in profile |
| 4 Hair | **No**: helmet shell plus a flat back slab (char r3 t1, r4 t3) | **Partly**: reads as hair, but stringy and noisy, with no clumps or highlight (char-B r3 t4, r4 t4) | **Partly**: clumps, pointed tips, bangs, highlight and some layering; but dark side slabs, a sticker highlight and a uniform sawtooth (char-C r3 t2, t4, r4 t4) |
| 5 Clothing | **No**: V decal on a box, no cuffs; shorts are a box (char r4 t1) | **Partly**: stand collar, placket, buttons, princess seams, cuffs, hem band, shorts fly and turn-ups; no folds, lapels or pockets; plain back (char-B r4 t2) | **Partly**: same garment (char-C r4 t2) |
| 6 Shading | **Yes**: clean and flat (char r4 t1) | **Partly**: the body is clean; the hair is noisy strand by strand (char-B r3 t4) | **Partly**: the body is clean; the hair has regular stripes and a flat pink patch (char-C r3 t4, r4 t4) |
| 7 Outlines | **Yes**: even (char r1 left) | **Partly**: even on the body; the inner hair lines are scribbly (char-B r3 t4) | **Yes**: even on the body and hair clumps (char-C r4 t4; poses-C r1 t4) |
| 8 Clipping (yes = clean) | **No**: thighs pass through the rigid shorts box (poses r3 t3, r2 t1); a flat lid of the jacket shows round the neck (char r3 t1, t3) | **Partly**: hair locks go into the jacket shoulders and sleeves (char-B r3 t2, r4 t2) | **Yes**: nothing obvious at this resolution; the side-slab bottoms just rest on the shoulders (char-C r3 t2) |
| 9 Deformation | **No**: the shorts don't follow the legs (poses r3 t3, r2 t1); tube elbows | **Partly**: shoulders, hips and shorts hold up; the knees pinch (poses-B r3 t4, r2 t4) | **Partly**: same as B (poses-C r3 t4, r2 t4) |
| 10 Overall closeness | **No** | **Partly** (far) | **Partly** (far, a bit closer than B) |

#### Verdict 1, C against B

**C**, medium confidence. The body, jacket, shorts and face are effectively the same, so C wins only on hair and shoes. Three things decided it:

1. **Hair language.** C's hair reads as anime hair: clumps, pointed tips, bangs, a highlight and curled side locks (char-C r3 t4, r4 t4). B's reads as a stringy, noisy mop (char-B r3 t4, r4 t4). C's flaws are flat slabs and a sticker highlight, which are easier to fix than B's noise.
2. **Sneakers.** C's own sneakers have a toe cap, laces, collar and sole line (poses-C r1 t2, t4). B wears the stand-in's white slippers (poses-B r1 t4).
3. **Face visibility.** C's face is framed and clear in front and three-quarter views. B's hair covers and darkens the face (char-B r3 t4, r6 right).

The new side-slab defect is very visible at every distance. That is the main reason this is not "high".

#### Verdict 2, C against A

**C**, high confidence. Three things decided it:

1. **Anatomy.** C has a smooth adult body with hands and fingers, a neck and real shoes. A has tube limbs, mitten stubs and a block torso (poses r1 t1 vs t2).
2. **Costume.** C's jacket and shorts read as tailored: collar, placket, buttons, seams, cuffs, fly and turn-ups. A has a V decal on a box (char r4 t1 vs t2).
3. **Hair and posing.** C's hair has clumps and tips where A's is a helmet and slab, and C's clothes follow the poses where A's shorts stay a rigid box the legs pass through (poses r3 t3 vs t4).

#### Distance from the target

C is still far from a Genshin-like character, roughly a third of the way. The closest parts are the overall silhouette, the adult proportions, the costume layout (stand collar, placket, cuffs, turn-up shorts) and the basic toon set-up with clean outlines. At full-body distance in daylight it passes as a generic low-budget anime 3D character.

The furthest parts are:
- **Hair:** Genshin hair is sculpted, layered and clumpy, with painted gradients and a crisp curved highlight. C has dark slabs, a pasted rectangle and a mechanical sawtooth.
- **Face:** there are no readable brows, no lash shape, blush or mouth expression, and no face shadow control, so it is blank rather than appealing.
- **Costume:** there are no trims, ornaments, layering, folds or asymmetry, and the back is plain.
- **Hands:** they are stiff claws.
- **Shading:** a soft single ramp, with no painted secondary tones or rim light.

Nothing here yet shows the "painted-looking detail" that defines the target.
