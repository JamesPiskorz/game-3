# Town benchmark

_2026-10-21 19:43 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), generated content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 18.7 ms total |
| Build a character (median / max) | 1.34 / 4.14 ms |
| Re-apply after a slider change (median) | 0.10 ms |
| Re-apply for another outfit context (median) | 0.57 ms |
| Spring bones, whole town, per 60 Hz frame | 0.254 ms (16 chains) |
| Render the town, 1920×1080, CPU + GPU | 8.07 ms |
| … the same view with CharacterLod, as in play | 8.17 ms |
| … the empty scene (camera, floorless) | 1.23 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 7.99 ms |
| … without the outline pass | 4.52 ms |
| … without sun shadows | 6.60 ms |
| Same town from 22 m, full detail | 6.97 ms |
| Same town from 22 m, with CharacterLod | 3.63 ms |
| Renderers / skinned vertices / blendshapes | 227 / 2,794,219 / 908 |
| Triangles (characters, one pass) | 2,506,073 |
| Transforms under characters | 1,702 |

