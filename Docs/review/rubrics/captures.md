# Rubric: play-mode captures

For `town-*.png`, `creator-*.png` and `menu*.png`, as `TownCapture`, `CreatorCapture` and `FlowCheck` render them. They use the player's anti-aliasing and post-processing (graphics plan G0.1).

- **C1** [look] Are edges and outlines free of stair-stepping?
- **C2** [look] Do the characters and the set look like one game?
- **C3** [look] Is there a sky, horizon or atmosphere behind the set rather than a flat colour? *(from G0.5)*
- **C4** [look] At night, do characters stand out from the background? *(rim from G1.7)*
- **C5** [look] Are hair and faces free of blocky dark patches?
- **C6** [look] Where a character stands in a set's shadow, is the whole character shaded, not part of it?
- **C7** [look] Does each time of day and weather read at a glance (day, sunset, night, rain, interior)?
- **C8** [look] Does only what should glow (lamps, emissive windows, bright highlights) glow?
- **C9** [metric] Does bloom fall only on pixels brighter than sunlit white? *(V6, not built)*
- **C10** [look] Is the HUD text legible, with panels clear of faces?
- **C11** [metric] Is `TownBenchmark` render time within budget (G0: within +1.5 ms of 10.5 ms)? *(`TownBenchmark`)*
- **C12** [look] Is the image graded consistently for its preset, with no clipped whites or crushed blacks? *(per-preset grading from G0.4)*
