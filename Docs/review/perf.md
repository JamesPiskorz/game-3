# Town benchmark

_2026-10-18 12:03 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 28.0 ms total |
| Build a character (median / max) | 1.69 / 5.47 ms |
| Re-apply after a slider change (median) | 0.10 ms |
| Re-apply for another outfit context (median) | 0.82 ms |
| Spring bones, whole town, per 60 Hz frame | 0.210 ms (11 chains) |
| Render the town, 1920×1080, CPU + GPU | 10.22 ms |
| … the same view with CharacterLod, as in play | 9.88 ms |
| … the empty scene (camera, floorless) | 1.24 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 9.86 ms |
| … without the outline pass | 6.63 ms |
| … without sun shadows | 9.64 ms |
| Same town from 22 m, full detail | 8.54 ms |
| Same town from 22 m, with CharacterLod | 5.44 ms |
| Renderers / skinned vertices / blendshapes | 290 / 839,495 / 546 |
| Transforms under characters | 1,817 |

