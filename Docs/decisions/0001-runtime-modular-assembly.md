# 0001: Runtime modular assembly (Spike B)

**Status:** Accepted (spike result) · 2026-10-17 · Unity 6000.6.6f1, URP 17.6

## Question

Can we build a character at runtime from a body plus separately authored parts, with no per-combination authoring? That means:

- rebinding each part onto the body's skeleton,
- driving the part with the body's shapes,
- hiding the skin it covers.

## What we built

| Piece | Where |
|---|---|
| `SkeletonBinder`: rebinds skinned meshes by bone name and grafts bones that only the part has | `Assets/_Game/Characters/Runtime/Modular/` |
| `ModularCharacter`: equip/unequip by slot, body-shape mirroring, region hiding | same |
| `CharacterPart` (slot, covered regions, follows-body-shapes flag), `BodyRegions` (17 regions) | same |
| `Game/Characters/StandIn` shader: discards body regions set in `_HiddenRegions` | `Assets/_Game/Characters/Shaders/` |
| Procedural stand-ins: a mannequin with 3 body shapes, a T-shirt, shorts, and hair with a ponytail on two extra bones. Each part prefab carries its own skeleton copy, as an FBX import would | `Assets/_Game/Spikes/SpikeB_Assembly/` |
| Demo scene (Play mode: sliders drive the shapes, number keys toggle parts), lineup renderer | same |
| 11 EditMode tests | `Assets/_Game/Characters/Tests/Modular/` |

## Results

All four mechanisms work. The front lineup reads **right to left**: body only · outfit · all shapes at 1 · posed with weight 0.5 · head bone scaled 1.25 · control.

![Front lineup](img/0001-spike-b-front.png)
![Back lineup](img/0001-spike-b-back.png)

| Mechanism | Result |
|---|---|
| **Bone rebinding by name** | Works. Part bind poses stay valid on the character's bones because every rest skeleton is identical. The part's own skeleton copy is discarded after rebinding. |
| **Part-only bones** (ponytail) | Grafted under the nearest shared ancestor (`Head`). They follow head rotation and head scale and are removed on unequip. This is the path hair and skirt spring bones will use. |
| **Blendshape mirroring** | Same-named shapes drive the body and every part. A part equipped later picks up the current values. A part that lacks a body shape logs a warning. |
| **Region hiding** | Covered regions are discarded through a bitmask on a per-character material instance. Without it (the control), skin pokes straight through. |
| **Bones-first proportions** | Scaling the head bone carried the hair along with zero extra work. This confirms the plan's "bones first, blendshapes only where needed" rule. |

| All shapes at 1: no poke-through | Posed: sleeves follow the arms | Control: shapes on body only, no hiding |
|---|---|---|
| ![](img/0001-spike-b-closeup-2.png) | ![](img/0001-spike-b-closeup-3.png) | ![](img/0001-spike-b-closeup-5.png) |

## Decisions

1. **Parts ship as prefabs with their own skeleton copy.** The Builder rebinds by bone name and drops the copy. Body and parts must share bone names and rest pose exactly, and the Blender validator must enforce this.
2. **Bones that only a part has are grafted.** Their names must be unique across all content: prefix them with the part (`Hair.Ponytail.1`). The binder throws on a duplicate name.
3. **Body shapes are mirrored by blendshape name.** A part missing a body shape is a guaranteed clipping bug. The Blender validator blocks it at export, and the runtime warning is only a backstop. Parts that sit where body shapes never reach (hair, hats) opt out with `FollowsBodyShapes = false`.
4. **Skin is hidden by region.** Each body vertex carries a region ID in vertex-colour red. The body material instance (not a MaterialPropertyBlock, which breaks the SRP Batcher) holds an int mask. That allows up to 32 regions.
5. The whole runtime layer came to about 300 lines of C#, comments included, plus a 70-line shader. That is evidence for the custom route, but Spike C (UMA) is still due before we close that question.

## Caveats → Phase 1

- **Regions need to be finer.** The shorts had to be knee-length because the thigh is a single region; shorter shorts would leave bare, hidden skin. The production body needs half-segment regions placed along real hemlines (PLAN §3.2).
- **The stand-in cloth is ideal.** It was computed from the body surface, so mirroring is exact. Real clothing depends on the quality of the Blender shape-key transfer, and needs testing with real assets in the clip-test scene.
- **Equipping is expensive per call.** Each part instantiates a full skeleton copy and then destroys it, and shapes are looked up by name on every `SetShape`. Fine for one character; at town scale, strip the skeleton copies at import time, cache blendshape indices, and bake identity shapes (Phase 4).
- **Bounds:** the stand-ins use `updateWhenOffscreen`. Real parts need authored bounds.
- **Edit mode:** the material instance is `DontSave`, so don't save a scene after equipping outside Play mode.
- **The Play-mode demo** (sliders, number keys) was built but not exercised headlessly. Try it in the Editor.
- **Unity warning:** the `Game.Characters.Editor` assembly is still empty, which triggers a Unity warning until Phase 1 tooling lands there.
- **Blender 5.2 is installed.** The Phase 1 pipeline can use it from day one.

## Reproduce

In Unity: **Game → Spikes → Spike B → Generate, Build Scene and Render Lineup**. Headless:

```bash
unity run . --editor-version 6000.6.6f1 -- -executeMethod Game.Spikes.SpikeB.SpikeBLineup.GenerateAndRender
```
