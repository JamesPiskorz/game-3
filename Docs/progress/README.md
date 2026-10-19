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
| 20261020-015933.png | (next) | Stance 5° per leg: a gap between the knees |
| 20261020-020024.png | (next) | Bob cut 2.5 cm under the chin |
| 20261020-020119.png | (next) | Bob cut 8 mm under the chin: ends at the jaw like the image's |
