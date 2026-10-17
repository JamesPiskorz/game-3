# 0003: Spring bones for hair and cloth (Spike D)

**Status:** Accepted (spike result) · 2026-10-17

## Question

How do hair tails (and later skirts) get secondary motion? The options were to adopt UniVRM's SpringBone, buy Magica Cloth 2, or write our own.

## Decision

**Write our own small verlet spring-bone solver** (`Assets/_Game/Characters/Runtime/Springs/`, about 200 lines), using the same model as VRM's SpringBone:

- Each bone's tail is a point mass. It is pulled towards its rest direction (stiffness), damped (drag), pushed by gravity, kept at the bone's length, and pushed out of sphere colliders.
- It runs at a fixed 90 Hz with at most 6 steps per frame, so motion doesn't depend on frame rate and a hitch can't spiral.
- Parts declare their chains (`CharacterPart.SpringChains`); content declares the body's colliders (`CharacterContent.SpringColliders`). `SpringBoneRig.Sync`, called by the builder, adds chains when a part goes on and removes them when it comes off. Chain bones are the part-only bones the builder already grafts (0001).

We chose our own because the solver is tiny, stays inside our assembly rules, and avoids a third-party dependency for a core feature. Magica Cloth 2 is still the fallback if skirts need real cloth (see below).

## Results

![Spring filmstrip](../review/springs.png)

A ponytail and twin tails whip round as the character turns 110°, flare out, and settle back within about half a second (frames left to right, top to bottom, at 30 fps). There are 7 tests:

- rest stability,
- lag-then-settle,
- gravity sag,
- collider push-out,
- teleport snapping,
- huge time steps staying finite,
- chains following equip and unequip.

## Lessons

- **Hair roots sit at the scalp.** A head collider sized to the hair, rather than the skull, pushes every tail radially outward, so the tails stood straight up. Colliders model the body. Joints rooted inside a collider are left alone.
- **Gravity per step, not per step squared.** Our first version scaled gravity by dt², which made it negligible against stiffness. Like VRM, both now apply per step.
- **Teleports:** tails are stored in world space, so moving a character makes them flail. A jump of more than 0.5 m in the anchor now snaps the chain to rest.
- **Headless renders:** edit-mode renders back to back don't re-skin meshes. Review tools set `SkinnedMeshRenderer.forceMatrixRecalculationPerRender`.

## Follow-ups

- **Skirts:** hang the stand-in skirt on bone panels with leg colliders (capsules), then decide between spring panels and Magica Cloth 2.
- **Tuning:** stiffness and drag per part (long hair is heavier than bangs). Move them from component defaults into content.
- **Performance:** one component per chain is fine for the creator. A town of 30+ characters wants one batched solver per character, possibly Burst.
