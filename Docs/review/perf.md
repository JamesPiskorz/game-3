# Town benchmark

_2026-10-17 23:24 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 14.5 ms total |
| Build a character (median / max) | 1.25 / 4.31 ms |
| Re-apply after a slider change (median) | 0.07 ms |
| Re-apply for another outfit context (median) | 0.28 ms |
| Spring bones, whole town, per 60 Hz frame | 0.240 ms (14 chains) |
| Render the town, 1920×1080, CPU + GPU | 8.19 ms |
| … the empty scene (camera, floorless) | 0.95 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 7.91 ms |
| … without the outline pass | 4.62 ms |
| … without sun shadows | 6.69 ms |
| Same town from 22 m, full detail | 7.31 ms |
| Same town from 22 m, with CharacterLod | 3.75 ms |
| Renderers / skinned vertices / blendshapes | 229 / 627,636 / 465 |
| Transforms under characters | 1,460 |

