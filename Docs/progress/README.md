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
