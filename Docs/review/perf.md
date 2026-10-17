# Town benchmark

_2026-10-18 05:56 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 31.6 ms total |
| Build a character (median / max) | 1.66 / 4.64 ms |
| Re-apply after a slider change (median) | 0.09 ms |
| Re-apply for another outfit context (median) | 0.77 ms |
| Spring bones, whole town, per 60 Hz frame | 0.192 ms (11 chains) |
| Render the town, 1920×1080, CPU + GPU | 10.02 ms |
| … the same view with CharacterLod, as in play | 9.59 ms |
| … the empty scene (camera, floorless) | 0.94 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 9.72 ms |
| … without the outline pass | 6.07 ms |
| … without sun shadows | 8.05 ms |
| Same town from 22 m, full detail | 8.56 ms |
| Same town from 22 m, with CharacterLod | 4.97 ms |
| Renderers / skinned vertices / blendshapes | 289 / 831,155 / 546 |
| Transforms under characters | 1,814 |

