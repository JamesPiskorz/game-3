# Rubric: part diagnostic sheet

For `Docs/review/diagnostics/<part>.png`. Tiles are numbered from the top left, six to a row.

| Row | Tiles |
|---|---|
| 1 | 1 orthographic front · 2 side · 3 top · 4 three-quarter beauty · 5 silhouette · 6 close-up |
| 2 | 7 wireframe · 8 mesh normals · 9 outline normals · 10 lightmap (blue: shade sooner; green: shine; gold: metal) · 11-12 light turntable, steps 1-2 |
| 3 | 13-18 light turntable, steps 3-8 (the sun steps round 45° each time) |

## Fit and shape

- **D1** [look] In tiles 1-3, does the part sit on the body with no floating gap and without sinking into it?
- **D2** [look] In any tile, is the part free of skin or an under-layer poking through it?
- **D3** [metric] Does the silhouette (tile 5) match the previous run (IoU ≥ 0.98), unless the change meant to alter the shape? *(V6 silhouette IoU, not built)*
- **D4** [look] Does the silhouette alone (tile 5) say what the part is?

## Geometry

- **D5** [look] In the wireframe (tile 7), is triangle density even, with no dense knots or long slivers, and no more than the shape needs?
- **D6** [metric] Is the triangle count within the budget? *(Blender validator)*
- **D7** [look] In the mesh normals (tile 8), are gradients smooth, with no seams, sudden colour flips or speckle?
- **D8** [look] In the outline normals (tile 9), are they continuous across hard edges and where shells meet? A break means a gap in the outline.

## Shading

- **D9** [look] In the close-up (tile 6), are edges between colour regions clean, with no staircases or notches?
- **D10** [look] In every turntable step (tiles 11-18), is there one clean shadow edge on the part, with no speckle and no blocky patches?
- **D11** [metric] Does `ShadowCheck` pass (no self-shadowing, no partial shadow)?
- **D12** [look] Do the eye whites stay white in every turntable step?
- **D13** [look] Does the shaded side read as a darker version of the same material, not muddy grey and with no hue jump?
- **D14** [look] Are the outlines thin, continuous and dark, and tinted toward the material?
- **D15** [look] Do metal parts read as metal, and shiny materials as shiny? *(from G1.5-G1.6)*
- **D16** [look] Does hair carry a shine that stays inside the hair and moves with the view? *(from G1.5)*
- **D17** [look] Are there painted shadow cues where the style needs them (under collars, arms, hair, in folds)? Tile 10 shows where they're painted. *(from G1.1)*
- **D18** [look] Is there a faint warm band on the lit side of the shadow edge and a reddish one inside it? *(from G1.2)*
- **D19** [look] Do patterned metal or fabric surfaces shade as the view turns? *(from G1.6, optional)*
- **D20** [metric] Is the rim even in width, the same number of pixels at near and far distances? *(from G1.7; V6 rim width, not built)*
