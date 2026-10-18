# 0002: Character toon shading and the skin ramp (Spike A)

**Status:** Accepted (spike result; tune by eye against real art) · 2026-10-17

## Question

Can one URP shader give us the anime look across the whole skin-tone range and every lighting setup in the game, while also doing everything the generator needs? That means recolouring, patterns, outlines and hiding skin under clothes.

## What we built

- **`Game/Characters/Toon`**, a hand-written HLSL shader for URP 17.6 (`Assets/_Game/Characters/Shaders/`). It has five passes:

  | Pass | What it does |
  |---|---|
  | ForwardLit | Lighting, recolouring, rim |
  | Outline | Inverted-hull outlines |
  | ShadowCaster | Shadows |
  | DepthOnly | Depth |
  | DepthNormals | Lets SSAO see characters |

  It is SRP Batcher compatible: one shared `UnityPerMaterial` buffer.

- **Shading:**
  - Two-tone lighting from a narrow smoothstep on half-Lambert. Cast shadows snap to the shade colour.
  - The unmasked base region shades to a hand-picked `_BaseShade`, such as a skin palette stop. Recoloured regions shade to their colour × `_ShadeTint`.
  - Ambient fills from the shade colour. Point and spot lights add their toon-stepped lit colour (Forward+ cluster loop). Rim light only on the lit side.
- **Recolouring:** four regions from an RGBA mask texture, or from vertex colour GBA for procedural stand-ins (`_MASK_VERTEX_COLOUR`). On top of that, a tiling pattern applied to chosen regions (UVs in metres).
- **Outlines:** a constant pixel width (scaled to 1080p), coloured as shade albedo × outline colour. Smoothed normals are stored in the **tangent channel**. Skinning moves tangents but not UVs, so outlines on posed characters stay correct. Character shading uses no normal maps, so the channel is free. `OutlineNormals.Bake` writes them.
- **Body-region hiding** in every pass. Hidden skin casts no shadow and draws no outline.
- **`SkinTonePalette`:** eight hand-picked stops (base, shade, rim) from light to dark, plus an undertone hue shift. The default lives at `Content/Palettes/SkinTones.asset`.
- **Review tools:**
  - `Game/Characters/Review/Render Skin Ramp Sheet` renders every tone under the day, sunset and night rigs, plus face close-ups.
  - `Snapshot` and `LightingRig` are reusable for future contact sheets.

## Results

![Skin ramp under day, sunset and night](img/0002-skin-ramp.png)
![Faces, day](img/0002-skin-faces.png)

- **The dark end holds up.** Rich browns, with warm, saturated shade bands rather than grey mud. Hand-picking shade colours per stop is what does it, confirming PLAN §3.4.
- The two-tone face shading reads as anime even on the stand-in sphere head.
- **Rendering under the sun direction:**
  - Day: front-lit.
  - Sunset: low and warm, with long toon shadows.
  - Night: a cool moon plus warm lamps.
- **Night:** additive lamps blew out white cloth at intensity 2.2. Rigs now use 0.8. Night scenes need a light-intensity budget for characters.

## Decisions

1. **Write character shaders as HLSL, not Shader Graph.** Shader Graph can't do the multi-pass outline or the custom passes cleanly. One include (`CharacterToonInput.hlsl`) keeps the passes consistent.
2. **Smoothed outline normals live in tangents.** Real FBX imports must bake them too: an `AssetPostprocessor` calling `OutlineNormals.Bake` (Phase 1).
3. **Shade colours are content, not maths,** for skin (palette stops). Cloth and hair use a shade tint per material, which art can override per item later.
4. **Every character review runs under all three rigs** (`LightingRig.All`).
5. **Characters don't receive shadows per pixel** (added 2026-10-18; graphics plan R2-R3, step G1.3). The target look has no self-shadowing, and a character is in the environment's shadow as a whole or not at all.
   - The toon pass reads the shadow map once per character, at `_ShadowAnchor`: a point by the head that `CharacterShadowAnchor` sets each frame, moved 0.6 m toward the light by the shader so the character can't cover its own sample.
   - Point and spot lights add no shadows on characters.
   - Characters still cast shadows on the world.
   - The default anchor (w < 0) means unshadowed, which suits the portrait studio and the review sheets.
   - This also removed the blocky patches on stand-in hair, where stacked shells 3 mm apart shadowed each other at shadow-map resolution.
   - `ShadowCheck` proves it: the old per-pixel shadows fail it (30% and 52% of the character wrong), and the new ones pass (0%).

## Caveats → follow-ups

- **Face shadows are untested.** The stand-in head is a sphere, so the custom-normals vs SDF face-shadow question waits for a real head mesh (Phase 1).
- **Eye whites take the toon shade,** so they go lavender on the shade side. Anime eyes are usually unlit: give face features an unlit region or a separate eye material.
- **The lightest stop nears white** under the 1.1-intensity day sun. Retune stops 1-2, or the sun, once real art exists.
- **No hair specular ("angel ring") yet,** and no blush or makeup layers (PLAN §3.3).
- **Headless render gotchas,** handled in `Snapshot`: async shader compilation renders placeholders, and the first render after the pipeline starts is garbage, so it renders a throwaway frame first.
