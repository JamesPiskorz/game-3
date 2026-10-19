# Look progress: the generated female character against her references

Each screenshot is `ProgressShot` (Assets/_Game/Characters/Editor/Review/ProgressShot.cs). The top row shows the generated source image, then the game's render of her in the T-pose and in the relaxed pose. The bottom row shows the image's face, the game's face from the front and three-quarter, a style-board face, the back of the head and the upper body.

To go back to the state in a screenshot, check out its commit. For example, `git checkout b55fd07 -- Tools Assets/_Game/Characters` restores just the tools and assets.

| Screenshot | Commit | What changed |
|---|---|---|
| 20261019-2207.png | 8f86726 | Baseline: first progress shot, old eyes |
| 20261019-2211.png | 8f86726 | Anime eyes with lash lines, catch-lights and pupils; no body sliders |
| 20261019-2215.png | b55fd07 | Rounder eyes; hair edges relaxed; narrower hair sheen |
| 20261019-2223.png | b55fd07 | Shorts and sleeve hems smoothed (3 cm coverage blur) |
| 20261019-2229.png | (next) | Edge trims: the collar, cuffs and waistband are even bands along the garment edges |
