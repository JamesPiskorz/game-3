# Judge report: scripted character vs stand-in

_Track V judging (graphics plan V3) for the scripted-art spike, [decision 0010](../decisions/0010-scripted-character-art.md). 2026-10-18._

**Method.** A subagent with fresh context got only the two renders below, the prompt and rubric that follow, and the question of whether the new character beats the stand-in. It was told to list defects (tile, location, fix) before its verdict. It saw no code, no plan and none of the building session. Its report is pasted unedited under [Report](#report).

**Renders** (Game → Characters → Review → **Render Scripted Character Sheet**, current toon shader, Day and Sunset lighting rigs, 4× MSAA). The sheet now renders the second pass ([scripted-character-judge-2.md](scripted-character-judge-2.md)); the first-pass renders this report judged are kept as:

- [scripted-character-pass1.png](scripted-character-pass1.png): stand-in (A) and scripted character (B) in the same colours, pose and light.
- [scripted-poses-pass1.png](scripted-poses-pass1.png): both in three test poses (relaxed, reach, sit), front and side.

## Prompt and rubric given to the judge

The judge was given copies of the two PNGs in a scratch folder; the paths are shortened here.

> You are a strict art reviewer for a stylised anime dating sim made in Unity. You have no other context: judge only what the images show.
>
> Images (read them with the Read tool; look closely at each row):
> 1. scripted-character.png (1600 x 4900). Rows, top to bottom:
>    (1) full body, front, daylight: A on the LEFT, B on the RIGHT;
>    (2) full body, back, daylight: mirrored, so B is on the LEFT and A on the RIGHT;
>    (3) face close-ups, four tiles left to right: A front, B front, A three-quarter, B three-quarter;
>    (4) four tiles left to right: A upper body front, B upper body front, A from behind, B from behind;
>    (5) full body three-quarter, daylight: A on the LEFT, B on the RIGHT;
>    (6) the same three-quarter view at sunset: A on the LEFT, B on the RIGHT.
> 2. scripted-poses.png (2000 x 2100). Three rows of test poses (relaxed, reach, sit); each row has four tiles left to right: A front, B front, A side, B side.
>
> A is the current placeholder character (procedural "stand-in" parts). B is a new character whose body, head, hair, jacket and shorts were generated entirely by scripts in Blender (no hand sculpting or painting). B borrows A's sneakers. Both use the same toon shader, colours, pose and lighting. The target look is a Genshin Impact-like stylised anime character (cel shading, painted-looking detail, clean outlines, expressive faces, layered clumpy hair, tailored costumes).
>
> Rubric (answer each for A and for B, yes / partly / no, naming the tile you looked at):
> 1. Silhouette: do body and clothes read as smooth, continuous forms (no faceting, tube joints, visible seams between body parts)?
> 2. Proportions and anatomy: adult, believable proportions; hands with fingers; feet; neck and shoulders.
> 3. Face: does it read as an appealing anime face (eyes, brows, mouth) at full-body distance and in close-up?
> 4. Hair: does it read as hair (clumps, pointed tips, volume, layering, highlight) rather than a shell or helmet?
> 5. Clothing: does the garment read as tailored clothing (collar, cuffs, hem, seams, folds, trims, buttons)?
> 6. Shading: clean toon shading, no blotches, noise, stripes or z-fighting?
> 7. Outlines: even and continuous, no broken or doubled lines?
> 8. Clipping: anything poking through anything else (hair through clothes, clothes through clothes, body through clothes or shoes)?
> 9. Deformation (poses image): do shoulders, elbows, hips and knees keep their volume without collapsing, tearing or candy-wrapping?
> 10. Overall closeness to the target look.
>
> Report format, in this order:
> - **Defects in A**, then **Defects in B**: a numbered list; each defect names the tile (image, row and position), the location on the character, and a concrete suggested fix. List everything you see, worst first.
> - **Rubric**: a table, one row per question, columns A and B.
> - **Verdict**: does B beat A? Answer "B", "A" or "tie", with a confidence (low / medium / high) and the three reasons that decided it.
> - **Distance from the target**: in a few sentences, how far B is from the target look, what is closest and what is furthest away. Be honest; don't soften.
>
> Reply with the report only, as Markdown.

## Report

### Defects in A

1. **Head is a box** (image 1, row 3, tiles 1 and 3; row 4, tile 1). The head is a rounded cube with a flat front, no jaw, cheek or chin, and it sits almost straight on the torso with very little neck. *Fix:* replace it with an anime head base: an egg-shaped cranium, a tapered V jaw, a small chin, a slightly forward face plane and a longer, thinner neck.
2. **Hair is a helmet** (image 1, row 2, right; row 4, tile 3; row 3, tiles 1 and 3). It is one smooth slab with a dark-to-mauve gradient, a flat cap of bangs and flat side panels. There are no clumps, tips or layers. *Fix:* rebuild it as clumped ribbons with pointed tips, layered bangs and side locks, and use a stepped toon ramp with a highlight band instead of the gradient.
3. **Hands are stubs with no fingers** (image 2, row 1, tile 1; row 2, tiles 1 and 3; row 3, tile 1). *Fix:* model hands with a palm and five fingers, and give them a relaxed curl in the rest pose.
4. **Limbs are tubes with hard joints** (image 2, row 2, tile 3). The outstretched arm is a straight cylinder ending in a stub. The bent knee is two cylinders meeting at a sharp corner. The shoulder has a hard tube seam. *Fix:* make one continuous skinned body mesh with edge loops at the joints, smooth weights and volume-preserving skinning.
5. **Clipping: the forearm comes out of the chest** (image 2, row 2, tile 3). The bent arm's upper arm is buried inside the torso, so the forearm seems to sprout from the sweater front. In the sit pose the hand sinks into the shorts (image 2, row 3, tile 3). *Fix:* correct the arm length and shoulder pivot, and adjust the pose or add collision so limbs stay outside the torso and thighs.
6. **Shorts read as a skirt box** (image 1, row 1, left; image 2, row 1, tile 1). There is a wide block at the hips, and thinner leg tubes come out under it, so the hip silhouette steps in. In the sit pose the seat becomes a flat wedge (image 2, row 3, tile 3). *Fix:* model two leg openings with a real crotch that follow the thighs, and weight them to the thighs.
7. **Torso is a barrel** (image 1, row 4, tile 1). There is no chest, waist or shoulder definition, and the hips are wide and boxy. *Fix:* give the torso adult female landmarks: a shaped chest, a waist taper and rounded deltoids.
8. **The collar is two flat cream cards** stuck on the sweater (image 1, row 4, tile 1; image 2, row 1, tile 1). There are no cuffs, seams or hem detail. *Fix:* model the neckline or lapels with thickness, and add cuffs, a hem band and seam lines.
9. **A dark ellipse at the neck and shoulder line** (image 1, row 3, tiles 1 and 3). It looks like the open top or inside of the torso mesh. *Fix:* close the neck hole or cover it with collar geometry.
10. **Face features are weak** (image 1, row 3, tile 1; image 2, row 1, tile 3). The eyes are small round dots, the brows are dashes and the mouth is a line. In profile the eye floats at the edge of the head, the mouth is a dark blot and there is no nose bridge. *Fix:* use an anime eye texture (large iris, lashes, catchlights) and stronger brows, and give the profile a real nose and mouth.
11. **Shading is not toon** (image 1, row 2, right; row 4, tile 3): the hair uses a smooth gradient. There is also a hard-edged dark band down one side of the shorts (image 2, row 1, tile 1; row 2, tile 1). *Fix:* use a stepped ramp on the hair, and soften or tune the shadow on the shorts (bias, or blending the shadow into the toon ramp).
12. **Strong cool shift in back views** (image 1, row 2, right; row 4, tile 3). The beige shorts turn mauve and the shoes lavender. *Fix:* limit the hue shift in the shadow colour or add ambient or rim light from behind.
13. **Shoes are white blobs from the front and flat bricks from the side** (image 2, row 1, tile 3; row 2, tile 3). In sunset light they merge into the skin colour (image 1, row 6, left). *Fix:* model a shaped sneaker with toe spring, a sole edge and laces.
14. **Outlines are thin, with almost no interior lines** (all tiles). *Fix:* use a thicker inverted-hull outline with width control from vertex colours.

### Defects in B

1. **The hair is stringy and noisy, not clumpy** (image 1, row 3, tiles 2 and 4; row 4, tile 4; row 5, right). It is dozens of thin strands, so the silhouette is ragged and the shading has dark and light stripes. There are no large clumps with clean pointed tips and no anime highlight band. *Fix:* merge the strands into about 20–40 wide tapered clumps with sharp tips. Transfer normals from a smooth hair proxy so the toon ramp makes large clean shapes. Add a painted highlight ring.
2. **Hair at the shoulders sticks out and clips into the jacket** (image 1, row 3, tiles 2 and 4). Dark jagged shards splay sideways off both shoulders and seem to pass into the shoulder seams. *Fix:* collide or shrinkwrap the hair against a shoulder proxy with an outward offset, and comb the tips down along the arm and back.
3. **Strands cross the face in three-quarter view** (image 1, row 3, tile 4). Dark strands run over the cheek and near the eye and partly hide the face. *Fix:* keep the bangs and side locks in front of the face surface with a clear part, and remove or shorten strands rooted near the temples.
4. **The hair has a flat crown and a boxy outline** (image 2, row 1, tile 4; row 3, tile 4). From the side the top of the head is flat and the back hair falls as a vertical rectangle. From the front there are squared bumps at the temples (image 1, row 1, right; image 2, row 1, tile 2). *Fix:* guide the roots over a rounded skull dome, pull the mass in toward the nape and flare it below the shoulders.
5. **Knees collapse** (image 2, row 2, tile 4; row 3, tile 4). The bent knee folds into a thin sharp corner and the thigh thins where it leaves the shorts. *Fix:* add knee edge loops, a corrective shape key or helper bone for knee flexion, and volume-preserving skinning.
6. **The shorts seat and hem go lumpy when sitting** (image 2, row 3, tile 4). The back of the shorts has a lumpy outline at the seat and hem. *Fix:* smooth the weights between pelvis and thigh on the shorts and add a corrective shape for hip flexion.
7. **The face disappears at sunset** (image 1, row 6, right). The eyes, brows and mouth almost vanish into a flat orange face. *Fix:* keep a minimum contrast or partly unlit look for the eye, brow and mouth textures, and use a face-shadow map (SDF) instead of normal-based shading on the face.
8. **The face is weak at full-body distance** (image 1, row 1, right; row 5, right). The eyes read, but the brows and mouth are nearly invisible. *Fix:* thicken the brow, lash and mouth lines, enlarge the eyes slightly, and bias the face texture's mipmaps toward sharper levels.
9. **Hands are stiff forks** (image 2, row 1, tile 2; row 3, tile 2; image 1, row 1, right). The fingers splay straight down, thin and evenly spaced, with little palm volume. *Fix:* give the rest pose a relaxed curl, thicken the fingers and add a palm pad.
10. **The jacket has no folds and looks painted on** (image 1, row 4, tile 2; image 2, row 2, tile 2). It is fully smooth, with no creases at the elbow, armpit or waist and no ease in the sleeves. There are no trims or ornaments beyond the cream bands. *Fix:* add painted or normal-baked folds at the joints, a little ease in the sleeves, and a trim or accent detail.
11. **The cool shadow shift changes the fabrics** (image 1, row 2, left; row 4, tile 4). From behind, the cream hem band reads as lavender and the shorts and shoes as mauve, as if they were different materials. *Fix:* the same as A, item 12.
12. **Outlines are thin and uneven** (all tiles). They break along the hair strands, nearly vanish at full-body distance, and a line down the inner shin reads like a seam (image 2, row 1, tile 2). *Fix:* use a uniform inverted-hull width with less width on the hair tips, and stop the outline from drawing on the inner leg.
13. **The borrowed shoes are bricks** (image 2, row 1, tile 4; row 2, tile 4). The lifted foot is a flat slab, and the ankle meets the shoe abruptly. *Fix:* make a shaped sneaker, as for A.
14. **Legs are featureless tubes** (image 1, row 1, right; image 2, row 1, tile 2). They are long and thin with almost no calf curve or knee landmark. *Fix:* add a calf curve, a kneecap shape and ankle definition.
15. **The shorts cuff flares on the raised thigh** (image 2, row 2, tile 2). The cuff tilts away from the thigh and shows a dark gap inside. *Fix:* weight the cuff ring firmly to the thigh bone.
16. **A dark diagonal streak at the collar and upper placket** in the reach pose (image 2, row 2, tile 2). It looks like a hair strand passing into the jacket front or a gap in the collar. *Fix:* check hair collision against the chest, and close the collar opening.

### Rubric

For item 8, "yes" means no clipping.

| # | Question | A | B |
|---|---|---|---|
| 1 | Silhouette | **No**: tubes, box head, barrel torso, stepped shorts (image 2, row 1, tile 1) | **Partly**: body and jacket are smooth, but the hair is ragged and boxy and the shoes are bricks (image 2, row 1, tiles 2 and 4) |
| 2 | Proportions and anatomy | **No**: stub hands, box head, almost no neck (image 1, row 4, tile 1) | **Yes**: adult proportions, fingers, neck and shoulders read, but the hands are stiff and the legs featureless (image 2, row 1, tile 2) |
| 3 | Face | **No**: dot eyes, dash brows, flat box face (image 1, row 3, tile 1) | **Partly**: appealing in the front close-up (image 1, row 3, tile 2); hidden by strands in three-quarter view (tile 4); faint at full-body distance; lost at sunset (row 6) |
| 4 | Hair | **No**: shell or helmet (image 1, row 4, tile 3) | **Partly**: reads as long layered hair, but stringy and noisy, with no clumps, tips or highlight and a flat crown (image 1, row 3, tile 4; image 2, row 1, tile 4) |
| 5 | Clothing | **No**: flat card collar, no cuffs, skirt-like shorts (image 1, row 4, tile 1) | **Yes**: stand collar, button placket, seams, cream hem and cuffs, rolled shorts cuffs; no folds (image 1, row 4, tile 2) |
| 6 | Shading | **Partly**: gradient hair, hard band on the shorts, dark neck ellipse (image 2, row 1, tile 1; image 1, row 3, tile 1) | **Partly**: clean on cloth and skin, stripy on the hair, face lost at sunset (image 1, row 3, tile 2; row 6) |
| 7 | Outlines | **Partly**: continuous but very thin, no interior lines (image 2, row 1, tile 3) | **Partly**: thin and broken on the hair, a stray line on the shin (image 2, row 1, tile 2) |
| 8 | Clipping | **Partly**: forearm comes out of the chest, hand sinks into the shorts (image 2, row 2, tile 3; row 3, tile 3) | **Partly**: hair into the jacket shoulders, strands across the face, a possible strand into the chest (image 1, row 3, tiles 2 and 4; image 2, row 2, tile 2) |
| 9 | Deformation | **No**: tube joints, pinched knee, the seat turns into a wedge (image 2, row 2, tile 3; row 3, tile 3) | **Partly**: shoulders and elbows hold, knees fold sharp, the seat goes lumpy (image 2, row 2, tile 4; row 3, tile 4) |
| 10 | Overall closeness to target | **No** | **Partly**: clearly anime-coded, far from Genshin quality |

### Verdict

**B**, with **high** confidence. Three reasons decided it:

1. **Face:** B's front close-up reads as an anime face with large catchlit eyes, brows and a small mouth. A's is a placeholder box with dots.
2. **Hair and silhouette:** B has long layered hair with real volume and length, and a smooth continuous body. A has a helmet slab on a body of tubes and boxes with stub hands.
3. **Costume and deformation:** B's jacket has a collar, placket, buttons, seams, a hem and cuffs, and its shoulders and elbows hold in the reach pose. A has flat card lapels, a skirt-box pair of shorts and joints that collapse or clip.

### Distance from the target

B is a clear step up, but still far from the Genshin look. Today it reads like a basic VRoid or MMD-tier model, not a polished gacha character.

- **Closest:** the jacket's construction (stand collar, placket, contrast bands) and the front-facing eyes.
- **Furthest:** the hair, then the rendering.
  - Genshin hair is a few large clumps shaped by hand, with crisp tips and a painted highlight ring. B's hair is a noisy curtain of thin strands with a boxy crown, and it clips at the shoulders and across the face.
  - The rendering has no painted texture detail, no face-shadow map, no rim light, and thin, uneven outlines. The face goes blank at sunset.
- **Also missing:** fabric folds, trims, ornaments, asymmetry and layered costume pieces, which Genshin costumes depend on. The hands are stiff, the knees fold sharp in poses, the legs are plain tubes, and the borrowed shoes are bricks.

B gets the outline of an anime character right. Most of what makes a Genshin model look rich (the hair, the costume detail, the face shading) is still missing.

## Notes on the report

Where the judge's reading and the build differ, for whoever acts on it:

- **Hair highlight (B4, B1):** the hair does carry a highlight band (mask region 3 and lightmap G), but in this colour set it is too close to the base to read, and the shader has no specular to drive from it yet (G1.5). The clumps are 29 at the back and 15 at the front; they read as strands because they're narrow and lie in two overlapping layers.
- **Folds (B10):** the jacket has elbow and waist ripples of 3 mm; at this distance and with flat toon shading they don't show. Folds need to be painted shadow shapes (lightmap bias) or deeper geometry to read.
- **Knees (B5):** the body has three loops at each knee; the collapse is linear-blend skinning on a 95° bend with no corrective shape, as the judge says.
