# Town benchmark

_2026-10-18 01:30 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 10.2 ms total |
| Build a character (median / max) | 1.95 / 4.39 ms |
| Re-apply after a slider change (median) | 0.06 ms |
| Re-apply for another outfit context (median) | 0.44 ms |
| Spring bones, whole town, per 60 Hz frame | 0.159 ms (10 chains) |
| Render the town, 1920×1080, CPU + GPU | 7.92 ms |
| … the empty scene (camera, floorless) | 0.92 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 7.75 ms |
| … without the outline pass | 4.47 ms |
| … without sun shadows | 6.79 ms |
| Same town from 22 m, full detail | 7.13 ms |
| Same town from 22 m, with CharacterLod | 3.87 ms |
| Renderers / skinned vertices / blendshapes | 225 / 627,805 / 453 |
| Transforms under characters | 1,440 |

