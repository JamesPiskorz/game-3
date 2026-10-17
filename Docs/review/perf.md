# Town benchmark

_2026-10-18 07:01 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 22.9 ms total |
| Build a character (median / max) | 1.72 / 4.82 ms |
| Re-apply after a slider change (median) | 0.09 ms |
| Re-apply for another outfit context (median) | 0.75 ms |
| Spring bones, whole town, per 60 Hz frame | 0.191 ms (11 chains) |
| Render the town, 1920×1080, CPU + GPU | 10.17 ms |
| … the same view with CharacterLod, as in play | 9.44 ms |
| … the empty scene (camera, floorless) | 0.95 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 9.98 ms |
| … without the outline pass | 5.98 ms |
| … without sun shadows | 8.09 ms |
| Same town from 22 m, full detail | 8.79 ms |
| Same town from 22 m, with CharacterLod | 4.98 ms |
| Renderers / skinned vertices / blendshapes | 290 / 833,835 / 546 |
| Transforms under characters | 1,816 |

