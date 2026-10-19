# Look progress: the generated female character against her references

Each screenshot is `ProgressShot` (Assets/_Game/Characters/Editor/Review/ProgressShot.cs). The top row shows the generated source image, then the game's render of her in the T-pose and in the relaxed pose. The bottom row shows the image's face, the game's face from the front and three-quarter, a style-board face, the back of the head and the upper body.

To go back to the state in a screenshot, check out its commit. For example, `git checkout b55fd07 -- Tools Assets/_Game/Characters` restores just the tools and assets.

| Screenshot | Commit | What changed |
|---|---|---|
| 20261019-2207.png | 8f86726 | Baseline: first progress shot, old eyes |
| 20261019-2211.png | 8f86726 | Anime eyes with lash lines, catch-lights and pupils; no body sliders |
| 20261019-2215.png | b55fd07 | Rounder eyes; hair edges relaxed; narrower hair sheen |
| 20261019-2223.png | b55fd07 | Shorts and sleeve hems smoothed (3 cm coverage blur) |
| 20261019-2229.png | b2d7b0f | Edge trims: the collar, cuffs and waistband are even bands along the garment edges |
| 20261019-2233.png | 6f151f9 | Bigger eyes; red sole bands on shoes |
| 20261019-2249.png | 6f151f9 | Garment colours sampled from the source image (first pass: tank and trousers wrong) |
| 20261019-2257.png | 6f151f9 | Image colours with a gain check; mean rather than lightest tones |
| 20261019-2258.png | 6f151f9 | Toon shader: ambient mostly on the shaded side |
| 20261019-2301.png | 6f151f9 | Face normals blended to an ellipsoid: clean anime face shading |
| 20261019-2302.png | 1f2e34f | Hair sheen: a narrow ring with a zigzag lower edge, like anime hair |
| 20261019-2318.png | aad1d73 | Tops drape like cloth (convex-hull drape from the bust, no abs or cleavage); midriff gap |
| 20261019-2320.png | aad1d73 | Tops reach 3 cm over the waistband (hides it) |
| 20261019-2326.png | aad1d73 | 1.5 cm overlap: the waistband shows under the shirt as in the image |
| 20261019-2328.png | 1b2d64f | Bob drawn in below the eyes (no bell flare) |
| 20261019-2329.png | 1b2d64f | Blunt fringe straight across just under the brows |
| 20261019-2331.png | 05a8d91 | Softer rose lips; cool skin undertone in the shot |
| 20261019-2334.png | 46dd7a2 | Chunkier shoes (4.5 cm slack); the shot's person has the image's longer legs and narrower shoulders |
| 20261019-2338.png | 7c83508 | Shot adds outfits 02 and 03 beside their images |
| 20261019-2341.png | 7c83508 | Closing scaled to edge_blur (tank straps still missing: the scan has none at the front) |
| 20261019-2344.png | 7c83508 | Baggy joggers (3.5 cm ease) |
| 20261019-2352.png | c4555ca | Belt experiments reverted (the belt stays a known gap) |
| 20261019-2354.png | c4555ca | Soft cheek blush (30%) |
| 20261019-2356.png | c4555ca | Blush at 45%, wider |
| 20261019-2359.png | e61e69a | Hoodie: the hood's edges aren't trims, so mint shows only round the neck opening |
| 20261020-0002.png | 7d7bbb1 | Sole flare (small) |
| 20261020-0005.png | 7d7bbb1 | Sneaker soles: flat, out to the footprint's hull |
| 20261020-0006.png | 7d7bbb1 | Chunkier sneakers (1.6 cm ease, 1 cm flare): the red sole shows |
| 20261020-0009.png | 6f75529 | Hair normals rounded towards a sphere: one clean shade curve across the bob |
| 20261020-0011.png | 5b489b0 | Rim light tinted by albedo |
| 20261020-0014.png | 5b489b0 | Garments' copied custom normals cleared |
| 20261020-0020.png | 5b489b0 | Bug fix: Region4 UVs zeroed; the grey wash over half of every garment is gone |
| 20261020-0022.png | 3972d9b | Tank top gets shoulder straps |
| 20261020-0031.png | e2b44fd | Tank: straps over the shoulder, scoop neckline, bare shoulder caps; bridging eases in from edges |
| 20261020-0036.png | 1bd2540 | Brown belt band on the trousers; all outfits rebuilt with edge-eased bridging |
| 20261020-0039.png | 4c509d7 | Garment surface faired after drape: far fewer outline squiggles on the shirt |
| 20261020-0041.png | 4c509d7 | Fairing at 24 passes |
| 20261020-0050.png | 0e45dce | Trim bands cut exactly at their width: clean even cuffs and collar |
| 20261020-0113.png | 6ba5fde | Target-look image (cel-shaded anime render of her, generated) added to the shot; face mostly lit; loose hoodie, gathered joggers |
| 20261020-0119.png | 6ba5fde | Garment colours 1.4x chroma (towards the target's saturation) |
| 20261020-0119.png (overwritten at 01:19 by the next run) | 8a15178 | Bob squeezed 25%; paler skin (tone 0.04) |
| 20261020-012046.png | 8a15178 | Bob drawn in towards the scalp (thin 0.6): hugs the head like the image's; squeeze back to 15% |
| 20261020-012651.png | 4ddc4ea | Relaxed pose: feet a little apart (3° per leg), garments refitted to it |
| 20261020-012959.png | ae6c84c | T-shirt sleeves stand off the arm (1.8 cm sleeve ease) |
| 20261020-013221.png | ae6c84c | Sneaker toe box flattened |
| 20261020-014345.png | ae6c84c | Whites are white (collar, cuffs, sneakers); prompt colours for regions the image can't show (red soles, brown boots) |
| 20261020-014839.png | ed9715d | Fringe stands off the forehead (13 mm) |
| 20261020-014951.png | ed9715d | 20 mm: brows hidden but the fringe crumples (rejected) |
| 20261020-015319.png | ed9715d | Fringe cut lower, resting on the lash line |
| 20261020-015619.png | ed9715d | Brows not drawn over: the fringe hides them, as in the image (bald heads still show them) |
| 20261020-015933.png | fc4209d | Stance 5° per leg: a gap between the knees |
| 20261020-020024.png | fc4209d | Bob cut 2.5 cm under the chin |
| 20261020-020119.png | fc4209d | Bob cut 8 mm under the chin: ends at the jaw like the image's |
| 20261020-020730.png | d94c2f0 | Shorts flared at the hem (rejected: the legs met in a skirt) |
| 20261020-020929.png | d94c2f0 | Shorts flared away from the inner thigh only: A-line legs with an inseam, like the image |
| 20261020-021151.png | 43fd05d | Low-top sneakers capped at 8.5 cm (too low: slippers) |
| 20261020-021602.png | 43fd05d | Sneakers up to 11.5 cm, pushed off the toes; slimmer (1.2 cm ease) |
| 20261020-021602.png | ca13ba6 | Bug: a hole in the ankles (the hidden Foot region reached above the low sneakers) |
| 20261020-022142.png | ca13ba6 | Fixed: a part hides only regions it covers all but a few of (99.5%) |
| 20261020-022249.png | 33c98a5 | Hair normals lean towards the viewer: most of the bob lit, shade on its far side |
| 20261020-022550.png | fa60ba7 | Rounder jaw: the pointed V chin widened into a soft U |
| 20261020-023129.png | 45aa949 | All parts rebuilt on the rounder-jaw body |
| 20261020-023325.png | 45aa949 | T-shirt falls straighter from the bust (drape slope 0.06): boxy like the target |
| 20261020-023829.png | 428e215 | Unstrapped tops drape up to the shoulders; the drape relaxes 40 passes: no cleavage, no fold under the bust |
| 20261020-024024.png | 428e215 | 2.5 cm overlap (a skin sliver still showed) |
| 20261020-024538.png | 428e215 | Bug fix: the relax no longer draws the hem up; 2.5 cm now hides the waistband |
| 20261020-024736.png | 428e215 | 6 mm overlap: the waistband shows under the shirt, no skin |
| 20261020-025113.png | c487c63 | Outfits 02 and 03 rebuilt with the same fit |
| 20261020-025308.png | (between commits) | Trim bands at most 2 cm wide (the T-shirt's collar climbs the neck in folds) |
| 20261020-025529.png | (between commits) | T-shirt off the neck entirely: no collar at all (rejected) |
| 20261020-025957.png | (between commits) | A collar band in the cuffs' white round the neck opening; the opening stands 4 cm off the throat |
| 20261020-030929.png | (between commits) | The drape fades out below the neck: the collar no longer stands off the throat |
| 20261020-031136.png | (between commits) | Up the neck 3.5 cm (still folds at the sides) |
| 20261020-031700.png | fab914b | Crew neck: a level ring 2.5 cm up the neck, hugging it, with a 1.8 cm white collar |
| 20261020-032021.png | fab914b | All outfits rebuilt with the neck fade |
| 20261020-032932.png | 78e2636 | Sneakers: wrapped in their cross-sections' hull, a level red sole band, a low-top collar (up the tongue and heel, down under the ankle bones) |
| 20261020-033047.png | 78e2636 | Bolder outlines: 2 px on skin and cloth, 1.8 on hair (at 1080p) |
| 20261020-034239.png | 78e2636 | Shorts: a level hem 13 cm under the hip joints, turned-up cuffs, the crotch spanned |
| 20261020-034442.png | 78e2636 | Shorts flare 1.8 cm |
| 20261020-034713.png | 78e2636 | Shorts hem 3 cm higher, as in the image |
| 20261020-034852.png | 78e2636 | Shoe close-ups added to the shot (one shoe's sole hidden under its toe box) |
| 20261020-040030.png | 78e2636 | Each shoe stands on its own flat floor; the sole runs under the toe box: red all round |
| 20261020-040352.png | 78e2636 | All outfits rebuilt |
| 20261020-040735.png | (between commits) | Bob ends curl under (12%) |
| 20261020-040808.png | d21dab5 | Curl 20%: a rounded bob, its ends turned in like the image's |
| 20261020-041912.png | d21dab5 | Tank: the right strap no longer splits (its shoulder is mostly the arm's); black edging along the neckline and straps |
| 20261020-042313.png | d3628e0 | Relaxed pose: toes turned out 10°, arms 4° closer to the body, as the target stands; garments refitted |
| 20261020-042617.png | d3628e0 | Sneaker laces: four grey bars across each instep |
| 20261020-042939.png | d3628e0 | All outfits refitted to the new pose |
| 20261020-043623.png | 878d465 | Hoodie drawstrings (two white stripes down the front); lace bars cut cleanly along their straight edges |
| 20261020-043902.png | a08b490 | High-tops get six dark lace bars, as in the hoodie image |
| 20261020-044126.png | a08b490 | Eyes 9% bigger (the whites fill their sockets): nearer the image's |
| 20261020-044255.png | a319ed3 | Hair pieces faired (Taubin, edges held): the black fleck on the bob is gone |
| 20261020-044540.png | 2f0d8b7 | Jaw widened 35% at the chin (was 22%): a rounder lower face like the image's |
| 20261020-044850.png | (between commits) | Shorts: a fly line in the waistband's brown |
| 20261020-045109.png | (between commits) | Front pocket lines (too near the centre: darts) |
| 20261020-045314.png | 018fe22 | Pocket lines out at the hips, as the target draws them |
| 20261020-051600.png | (between commits) | New last row: her from three-quarter beside a three-quarter target (camera on the wrong side); thinner closed lips; joggers end above the ankles; hood faired; sneaker toe caps |
| 20261020-051635.png | 8a04b3a | Three-quarter camera on her right, as the target |
| 20261020-052153.png | 8a04b3a | Thumbs tucked in along the hands: no more claws with the arms down |
| 20261020-052419.png | (between commits) | Stance 7° per leg (a clearer gap between the shorts' legs); sleeves 2.6 cm off the arm |
| 20261020-052755.png | 76542e8 | All outfits refitted to the wider stance |
| 20261020-053108.png | (between commits) | Crew collar shaded as a ring (normals out from the neck): fewer white and lavender patches |
| 20261020-053312.png | 13334c3 | Collar normals tipped further up (little change) |
| 20261020-053430.png | f87df46 | Hair sheen ring lower (45-49° from the crown): a band across the fringe, not the top edge |
| 20261020-053617.png | (rejected) | Skin undertone 0.25 (warmer, not pinker: 0 is the cool end) |
| 20261020-054335.png | 919ee47 | Tank straps: clean edges (ramps wider than the triangles), 2.2 cm wide as posed (they spread to 5 cm with the arms down), painted black like the image's |
| 20261020-054556.png | c4279e6 | Crew neck, hems and shoe collars cut from ramps wider than the triangles: cleaner edges |
| 20261020-055359.png | (between commits) | Fringe pushed clear of the brows straight in from the front (beside the brows it kept only 4 mm) |
| 20261020-055512.png | 472c2a0 | Shot DNA: round eyes, thin brows (the seed drew droopy eyes and thick brows that peeked out) |
| 20261020-055659.png | ac576af | Cloth shades cool grey-blue (was lavender): white collar and sneakers grey in shadow, as the targets' |
| 20261020-055750.png | ac576af | Same as 0556 (compile check) |
| 20261020-060029.png | fdfa6b1 | Hoodie: a 2.5 cm mint band round its neck opening (a little more lining shows at the front) |
| 20261020-060348.png | (between commits) | Tank tucked in, its hem 15.2 cm above the hips: the belt shows (skin sliver above it) |
| 20261020-060541.png | (between commits) | Tucked to 13.8 cm (a few skin flecks) |
| 20261020-060734.png | 2bacea1 | Tucked to 13.2 cm: the whole belt shows, no skin |
| 20261020-060954.png | 8c339d9 | A dark buckle on the trousers' belt |
| 20261020-061347.png | (between commits) | Boots: a black sole band and level tops (bare skin showed above the left boot) |
| 20261020-061554.png | 1a6faf4 | Jeans end in a level hem at 30 cm, tucked into boot tops at 37 cm: no skin gap |
| 20261020-062632.png | (next) | Each shoe's sole levelled to the pair's floor (one boot stood 1-2 cm high on a spike); all outfits rebuilt |
