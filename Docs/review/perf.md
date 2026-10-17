# Town benchmark

_2026-10-18 03:34 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 27.7 ms total |
| Build a character (median / max) | 1.68 / 4.52 ms |
| Re-apply after a slider change (median) | 0.13 ms |
| Re-apply for another outfit context (median) | 0.67 ms |
| Spring bones, whole town, per 60 Hz frame | 0.276 ms (15 chains) |
| Render the town, 1920×1080, CPU + GPU | 9.40 ms |
| … the empty scene (camera, floorless) | 0.92 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 9.20 ms |
| … without the outline pass | 5.61 ms |
| … without sun shadows | 8.24 ms |
| Same town from 22 m, full detail | 8.30 ms |
| Same town from 22 m, with CharacterLod | 4.63 ms |
| Renderers / skinned vertices / blendshapes | 271 / 770,626 / 549 |
| Transforms under characters | 1,792 |

