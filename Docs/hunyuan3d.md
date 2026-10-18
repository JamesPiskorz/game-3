# Hunyuan3D 2.1: local image-to-3D

_Set up 2026-10-18 on the dev machine (RX 7900 XTX, Windows 10)._

Tencent's open-weight image-to-3D model, running locally on the AMD card. One image in, a mesh out (and optionally PBR textures).

**What it's for:** environment props, step G5.1 of the [graphics plan](graphics-plan.md). **Not characters**: it can't produce rigged, region-masked parts on the shared rig (graphics plan, rows 8-12 of the cross-check).

---

## 1. Where it lives

Everything is outside the repo, in `D:\Tools\Hunyuan3D` (about 25 GB):

| Path | What |
|---|---|
| `D:\Tools\Hunyuan3D\` | [hunyuan3d-2.1-mac-rocm](https://github.com/VladimirTalyzin/hunyuan3d-2.1-mac-rocm), a community wrapper (MIT) that runs Tencent's code without CUDA. Web UI, launcher, diagnostics, a pure-PyTorch rasterizer. |
| `Hunyuan3D-2.1\` | Tencent's [official repo](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1), unmodified. The wrapper patches it at runtime. |
| `venv\` | Python 3.12 with AMD's ROCm 7.2.1 PyTorch (`torch 2.9.1+rocm7.2.1`, from `repo.radeon.com`). |
| `weights\Hunyuan3D-2.1\` | Shape model (DiT 6.9 GB, VAE 0.6 GB) and texture model (6.4 GB), from [tencent/Hunyuan3D-2.1](https://huggingface.co/tencent/Hunyuan3D-2.1). |
| `Hunyuan3D-2.1\hy3dpaint\ckpt\` | Real-ESRGAN x4 upscaler for textures. |
| `%USERPROFILE%\.cache\huggingface\` | `facebook/dinov2-giant` (4.5 GB), which the texture model conditions on. |
| `outputs\` | Generated meshes. |

## 2. Running it

**Web UI** (upload an image, Generate, optionally decimate and texture, download):

```powershell
powershell -ExecutionPolicy Bypass -File D:\Tools\Hunyuan3D\launch.ps1
```

Then open <http://127.0.0.1:7860>. It only listens on localhost.

**Command line** (image to mesh to texture, with turntable renders and an HTML report):

```powershell
cd D:\Tools\Hunyuan3D
.\venv\Scripts\python.exe tests\e2e_visual.py --image <image.png> --out outputs\<name> --steps 30 --octree 256 --tex-preset safe
```

Add `--skip-texture` for shape only. The meshes land in `outputs\` itself, named by timestamp: `hunyuan3d_<time>_raw.glb` (the untextured shape) and `hunyuan3d_<time>_for_texture_textured.glb` (remeshed and textured, plus OBJ, MTL and JPG maps). `outputs\<name>\report.html` shows every stage with turntable renders.

**What comes out:** at octree 256 the raw shape is about 300k triangles. Texturing remeshes it to 40k triangles with 1024² albedo and metallic-roughness maps. Both are normalised to about 2 units tall, centred on the origin, Y up.

**Check the install** (backend, weights, extensions, and whether the GPU still computes correctly):

```powershell
D:\Tools\Hunyuan3D\venv\Scripts\python.exe D:\Tools\Hunyuan3D\scripts\doctor.py
```

## 3. Settings and timings on this machine

Measured 2026-10-18 on the demo penguin and the wrapper's armchair example:

| Stage | Setting | Time | Notes |
|---|---|---|---|
| Shape | 30 steps, octree 256 | **~30 s** generating (about 1 s per step), **~100 s** per run including loading the 7 GB model | Very clean geometry: the penguin's embossed sign lettering and thin posts survive. Octree 384+ adds detail and triangles; props get decimated anyway. |
| Texture | «Safe»: 6 views at 256 px | **~50 s** texturing, **~80 s** including loading the model | The wrapper's default on AMD. Soft, with seams where views meet and invented colour on faces no view saw. |

The very first texture run took 340 s while MIOpen tuned its kernels. The tuning cache is kept in `D:\Tools\Hunyuan3D\.cache\miopen`, so that's paid once.

Close games and other GPU-heavy apps first: shape generation wants about 10 GB of VRAM and texturing more.

### Higher texture resolution (not enabled)

The wrapper caps texturing at «Safe» on AMD because, without fast attention, the texture model's attention matrix grows with the square of views × resolution (67 GB at 6 views × 512 px). On the laptop chip it was tested on, going over the cap reset the machine.

On the 7900 XTX (gfx1100), PyTorch's experimental AOTriton attention kernels **do work**: checked against the reference kernel they match to fp16 precision, run about 6× faster, and the 6 × 512 px case needs 0.2 GB instead of 67 GB. Using them for the «Low» preset (6 × 512 px) means setting `TORCH_ROCM_AOTRITON_ENABLE_EXPERIMENTAL=1` and the wrapper's cap override `HY3D_ALLOW_RISKY_TEXTURING=1`. Not done yet: it overrides a safety check, so it's a deliberate decision, and it should be tried with the GPU timeout raised (below).

## 4. Inputs that work

One object, centred, the whole silhouette in frame with a margin, plain background, even light, no text. The UI's background removal (rembg) handles simple backgrounds. A visible floor or shadow gets reconstructed as a disc under the object, so remove the ground plane from the image.

For our props the input is a concept image in the location's style (G5.1). The model reconstructs shape well; it does not know our toon look.

## 5. Into the game (G5.1)

The output is a dense, triangulated mesh with realistic PBR textures. `Game/Environment/Toon` takes **one flat colour per material**, so a prop becomes one submesh per palette colour. Two pieces do that with no hand set-up:

**Blender:** `Tools/Blender/clean_generated_prop.py` (library: `prop_tools.py`) cleans a GLB into an FBX plus a `.prop.json` sidecar (source, generator, licence, colours, height, triangles):

```bash
blender -b --factory-startup --python Tools/Blender/clean_generated_prop.py -- D:/Tools/Hunyuan3D/outputs/<name>_textured.glb Assets/_Game/Town/Content/Props/cafe/armchair.fbx --height 0.85 --palette cafe --preview armchair.png
```

| Option | Default | |
|---|---|---|
| `--height` | required | Real height in metres. Hunyuan3D's output is normalised to about 2 units. |
| `--palette` | required | A location in `Tools/Blender/palettes.json` (`cafe`, `park`, …) or `RRGGBB,RRGGBB,…`. The palettes are seeded from the blockout set colours in `TownSets` until art picks real ones. |
| `--triangles` | 3000 | Triangle budget. |
| `--regions` | 4 | Most colours to use. |
| `--colour` | first palette colour | The one colour for an untextured mesh (a `_raw.glb`). |
| `--yaw` | 0 | Degrees to turn if the front isn't the front. Hunyuan3D's front, the side the input image saw, already faces +Z in Unity. |
| `--preview` | none | Renders front, three-quarter and side views in the final colours. |

What it does, in order:

1. Places the prop at its height with the pivot at the centre of its base.
2. Welds UV-seam duplicates, drops floating pieces, and patches the few open edges Hunyuan3D's texture remesh leaves.
3. Reads the albedo per face: the median of 7 samples. It reads the UVs whichever way up gives colours that are smooth across neighbouring faces (see the GLB bug in section 7).
4. Clusters the face colours, gives each cluster the nearest palette colour, and keeps the largest few. Matching counts lightness half as much as hue and chroma, because the albedo has light and shade baked in and the toon shader adds its own.
5. Cleans speckle with a majority filter, decimates to the budget, and carries the colours across to the low-poly mesh. Colour specks under 0.3% of the surface merge into their neighbour.
6. Uses smooth shading with edges over 40° kept hard, then validates: budget, height, pivot, manifold, normals facing out, palette-only materials. It exits non-zero on problems, then exports FBX with the character pipeline's Unity settings.

**Unity:** `PropModelImporter` (`Assets/_Game/Town/Editor`) handles every FBX under `Assets/_Game/Town/Content/Props/`. Each `pal_RRGGBB` material becomes an environment-toon material in that colour, tuned like the blockout props (`TownProp.mat`). It also bakes outline normals and adds no colliders. **Game → Town → Render Prop Sheet** renders every prop with a person for scale (`Docs/review/props.png`). `ImportedPropTests` checks the two test props in `PipelineTest/`: materials, outline normals, pivot, height, budget and licence.

**Limits:**
- The colours are only as good as the generated texture. At the «Safe» preset, thin parts smear: the test armchair's legs are mostly mustard in its texture, so they only came out walnut because the café palette has two browns.
- A palette without a fitting colour snaps to the nearest hue. The same chair with the mall palette got pink legs.
- Decimation to 3000 triangles loses fine relief like sign lettering.
- Touch-ups (repainting a region, merging two colours) are art work in Blender on the exported FBX.

## 6. Licence

The model, weights **and everything generated with them** fall under the [Tencent Hunyuan 3D 2.1 Community License](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE):

- It does **not** apply in the EU, the UK or South Korea.
- Above 100 million monthly active users, a separate licence from Tencent is needed.
- Keep Tencent's notice with anything distributed.

Log each shipped prop's source as G5.1 requires. The cleanup script writes the generator and licence into each prop's `.prop.json`.

## 7. Troubleshooting

- **Screen flashes black mid-run:** Windows reset the GPU because one operation ran over its 2-second watchdog. The process keeps going but the GPU returns **wrong numbers**, so the output is silently corrupt. Run `doctor.py`; if "gpu arithmetic" fails, reboot. To stop it happening, raise the timeout in an **administrator** PowerShell and reboot:

  ```powershell
  New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name TdrDelay -PropertyType DWord -Value 60 -Force
  ```

- **Textures look scrambled in Blender, Unity or any glTF viewer:** the wrapper's textured GLBs have their UVs upside down. `mesh_io.py:226` flips V for glTF, then trimesh's exporter flips it again. Only the wrapper's own preview, which makes the same mistake, shows them right. The textured OBJ (with its MTL and JPGs) is correct. `clean_generated_prop.py` detects which way up the UVs are, so it handles both.
- **Out of memory in shape generation:** lower octree to 192, or close other GPU apps.
- **Never set `MIOPEN_FIND_MODE=FAST`.** It crashes the texture pipeline.
- **Rebuilding from scratch:** create the venv with Python 3.12, install AMD's ROCm 7.2.1 wheels (`rocm_sdk_*` then `torch`, `torchvision`, `torchaudio` from `https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/`), then run `scripts\install_rocm_windows.ps1 -Venv` from `D:\Tools\Hunyuan3D`. It reuses the working torch, clones Tencent's repo, installs the rest, builds the one C++ extension and downloads the weights.
