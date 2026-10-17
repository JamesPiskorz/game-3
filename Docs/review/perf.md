# Town benchmark

_2026-10-18 02:22 · AMD Ryzen 7 5700X 8-Core Processor  · AMD Radeon RX 7900 XTX · Unity 6000.6.6f1 (editor, batch mode)_

Editor timings are indicative only: the editor is slower than a player build, and this is not min-spec hardware.

Town of **40** generated residents (world seed 2026), stand-in content.

| Measure | Value |
|---|---|
| Generate the town (DNA, with lookalike rejection) | 12.7 ms total |
| Build a character (median / max) | 1.41 / 4.30 ms |
| Re-apply after a slider change (median) | 0.07 ms |
| Re-apply for another outfit context (median) | 0.45 ms |
| Spring bones, whole town, per 60 Hz frame | 0.163 ms (10 chains) |
| Render the town, 1920×1080, CPU + GPU | 8.37 ms |
| … the empty scene (camera, floorless) | 0.93 ms |
| … with every blendshape at zero (an identity bake's ceiling) | 8.22 ms |
| … without the outline pass | 5.23 ms |
| … without sun shadows | 7.32 ms |
| Same town from 22 m, full detail | 7.75 ms |
| Same town from 22 m, with CharacterLod | 4.03 ms |
| Renderers / skinned vertices / blendshapes | 243 / 726,839 / 507 |
| Transforms under characters | 1,476 |

