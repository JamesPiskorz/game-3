# Town benchmark

_2026-10-21 19:44 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 28.9 ms total |
| Build a character (median / max) | 1.91 / 4.95 ms |
| Re-apply after a slider change (median) | 0.12 ms |
| Re-apply for another outfit context (median) | 0.89 ms |
| Spring bones, whole town, per 60 Hz frame | 0.232 ms (11 chains) |
| Render the town, 1920×1080, CPU + GPU | 11.09 ms |
| … the same view with CharacterLod, as in play | 10.26 ms |
| … the empty scene (camera, floorless) | 1.21 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 11.00 ms |
| … without the outline pass | 6.59 ms |
| … without sun shadows | 9.46 ms |
| Same town from 22 m, full detail | 9.78 ms |
| Same town from 22 m, with CharacterLod | 5.58 ms |
| Renderers / skinned vertices / blendshapes | 290 / 839,495 / 546 |
| Triangles (characters, one pass) | 1,355,318 |
| Transforms under characters | 1,817 |

