# Town benchmark

_2026-10-18 03:38 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 23.2 ms total |
| Build a character (median / max) | 1.52 / 4.46 ms |
| Re-apply after a slider change (median) | 0.08 ms |
| Re-apply for another outfit context (median) | 0.65 ms |
| Spring bones, whole town, per 60 Hz frame | 0.281 ms (15 chains) |
| Render the town, 1920×1080, CPU + GPU | 9.26 ms |
| … the same view with CharacterLod, as in play | 8.90 ms |
| … the empty scene (camera, floorless) | 0.92 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 9.07 ms |
| … without the outline pass | 5.62 ms |
| … without sun shadows | 7.50 ms |
| Same town from 22 m, full detail | 8.03 ms |
| Same town from 22 m, with CharacterLod | 4.60 ms |
| Renderers / skinned vertices / blendshapes | 271 / 770,626 / 549 |
| Transforms under characters | 1,792 |

