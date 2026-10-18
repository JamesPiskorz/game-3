# Town benchmark

_2026-10-18 15:36 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 24.7 ms total |
| Build a character (median / max) | 1.71 / 5.95 ms |
| Re-apply after a slider change (median) | 0.11 ms |
| Re-apply for another outfit context (median) | 0.84 ms |
| Spring bones, whole town, per 60 Hz frame | 0.211 ms (11 chains) |
| Render the town, 1920×1080, CPU + GPU | 11.28 ms |
| … the same view with CharacterLod, as in play | 10.66 ms |
| … the empty scene (camera, floorless) | 1.38 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 10.88 ms |
| … without the outline pass | 6.99 ms |
| … without sun shadows | 9.40 ms |
| Same town from 22 m, full detail | 9.77 ms |
| Same town from 22 m, with CharacterLod | 5.84 ms |
| Renderers / skinned vertices / blendshapes | 290 / 839,495 / 546 |
| Transforms under characters | 1,817 |

