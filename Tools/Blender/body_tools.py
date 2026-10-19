"""Blender side of the generated base body (decision 0008).

Turns a Hunyuan3D scan of a T-posed, bald base character into the canonical body, scripted so it runs headless:

* clean-up: symmetrise, voxel remesh, smooth away the generator's voxel steps, decimate to the PLAN section 4 budget
  with the head kept denser (body about 20k triangles plus head 10k),
* the skeleton fitted to the scan (it replaces the stand-in skeleton as the canonical rest pose),
* automatic skin weights, body regions from the weights, the body shapes,
* the game's face features built against the real head surface.

Coordinates: Blender is Z-up with the front at -Y, the character's left at +X (as in character_tools); the FBX export
turns that into Unity's Y-up, +Z front, left at -X.
"""

import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix

import prop_tools as pt


# ---------------------------------------------------------------------------------------------------- clean-up

def vertices(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def apply_modifier(obj, modifier):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def symmetrise(obj):
    """Mirrors the character's right half (-X in Blender) onto the left, about the bounding box's centre plane."""
    co = vertices(obj)
    centre = (co[:, 0].min() + co[:, 0].max()) / 2
    obj.data.transform(Matrix.Translation((-centre, 0.0, 0.0)))
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.symmetrize(bm, input=bm.verts[:] + bm.edges[:] + bm.faces[:], direction="X", dist=1e-5)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def remesh(obj, voxel_size, smoothing=4):
    """
    Voxel remesh into one closed, even surface, then a volume-preserving Laplacian smooth. The scan's own surface
    comes from a marching-cubes grid a few millimetres wide, and its stair steps show through toon shading.
    """
    modifier = obj.modifiers.new("Remesh", "REMESH")
    modifier.mode = "VOXEL"
    modifier.voxel_size = voxel_size
    modifier.adaptivity = 0.0
    apply_modifier(obj, modifier)
    if smoothing:
        modifier = obj.modifiers.new("Smooth", "LAPLACIANSMOOTH")
        modifier.iterations = smoothing
        modifier.lambda_factor = 0.5
        modifier.use_volume_preserve = True
        modifier.use_normalized = True
        apply_modifier(obj, modifier)


def head_mask(obj, neck_height):
    """1 on the head (above the neck), falling to 0 over 4 cm below it: where decimation should leave more detail."""
    z = vertices(obj)[:, 2]
    return np.clip((z - (neck_height - 0.04)) / 0.04, 0.0, 1.0)


def set_group(obj, name, weights):
    """A vertex group holding ``weights`` (one per vertex), written in a few batches rather than vertex by vertex."""
    group = obj.vertex_groups.new(name=name)
    binned = np.round(np.clip(weights, 0.0, 1.0) * 20) / 20
    for value in np.unique(binned[binned > 0]):
        group.add(np.flatnonzero(binned == value).tolist(), float(value), "REPLACE")
    return group


def smooth_seam(obj, width=0.004, iterations=6):
    """Relaxes the vertices within ``width`` of the mirror plane, where symmetrising leaves a faint crease."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    seam = [v for v in bm.verts if abs(v.co.x) < width]
    for _ in range(iterations):
        bmesh.ops.smooth_vert(bm, verts=seam, factor=0.5, use_axis_x=False, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def relax(obj, weights, iterations, taubin=False):
    """
    Masked umbrella smoothing: each pass moves every vertex towards the mean of its neighbours by its weight. Plain
    passes shrink protrusions into the surrounding surface; Taubin passes (alternating +0.5 and -0.53) remove only
    small details and keep the overall form.
    """
    mesh = obj.data
    edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", edges)
    a, b = edges[0::2], edges[1::2]
    count = np.bincount(np.concatenate([a, b]), minlength=len(mesh.vertices)).astype(float)
    co = vertices(obj)
    w = np.asarray(weights, dtype=float)[:, None]
    steps = (0.5, -0.53) if taubin else (0.5,)
    for _ in range(iterations):
        for step in steps:
            total = np.zeros_like(co)
            np.add.at(total, a, co[b])
            np.add.at(total, b, co[a])
            mean = total / np.maximum(count, 1.0)[:, None]
            co += step * w * (mean - co)
    mesh.vertices.foreach_set("co", co.ravel())
    mesh.update()


def mannequin(obj, joints, measures):
    """
    A mannequin finish, like the stand-in's: the chest's small details (nipples) filtered away, keeping its form, and
    the groin smoothed flat into the surrounding surface. Clothing always covers both; the body ships with the game.
    """
    co = vertices(obj)
    normals = np.empty(len(co) * 3)
    obj.data.vertices.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    front = np.clip(-normals[:, 1] * 2.0, 0.0, 1.0)  # Blender's front is -Y
    chest_low, chest_high = joints["Spine"][2] + 0.03, joints["Neck"][2] - 0.02

    def band(value, low, high, fade):
        return np.clip((value - low) / fade, 0.0, 1.0) * np.clip((high - value) / fade, 0.0, 1.0)

    chest = band(co[:, 2], chest_low, chest_high, 0.03) * band(np.abs(co[:, 0]), -1.0, 0.2, 0.03) * front
    relax(obj, chest, 40, taubin=True)

    crotch = np.array([0.0, float(co[np.abs(co[:, 2] - measures["crotch"]) < 0.01][:, 1].min()), measures["crotch"] + 0.03])
    distance = np.linalg.norm((co - crotch) / np.array([0.08, 0.1, 0.1]), axis=1)
    groin = np.clip((1.3 - distance) / 0.5, 0.0, 1.0)
    relax(obj, groin, 240)
    relax(obj, groin, 30, taubin=True)


def head_triangles_of(obj, neck_height):
    polygons = obj.data.polygons
    centre = np.empty(len(polygons) * 3)
    polygons.foreach_get("center", centre)
    corners = np.empty(len(polygons), dtype=np.int64)
    polygons.foreach_get("loop_total", corners)
    return int((corners - 2)[centre.reshape(-1, 3)[:, 2] > neck_height].sum())


def decimate(obj, body_triangles, head_triangles, neck_height):
    """
    Collapse decimation to the budget, keeping the head about as dense as ``head_triangles`` asks.

    Decimate's vertex group says what may collapse (weight 1) and what may not (0), close to a hard mask, while its
    ratio counts the whole mesh. So the head goes first with the body locked, then the body with the head locked, each
    ratio set so the unlocked part lands on its own target.
    """
    # The head's pass runs twice: the mask's edge keeps some triangles locked, so one pass stops short of its target.
    for name, target in (("head", head_triangles), ("head", head_triangles), ("body", body_triangles)):
        mask = head_mask(obj, neck_height) > 0.5
        set_group(obj, name, (mask if name == "head" else ~mask).astype(float))
        total = pt.triangle_count(obj)
        head = head_triangles_of(obj, neck_height)
        locked = total - head if name == "head" else head
        modifier = obj.modifiers.new("Decimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.vertex_group = name
        modifier.ratio = min(1.0, (locked + target) / total)
        modifier.use_collapse_triangulate = True
        apply_modifier(obj, modifier)
        if name in obj.vertex_groups:  # applying the modifier invalidates earlier handles to the group
            obj.vertex_groups.remove(obj.vertex_groups[name])


# ---------------------------------------------------------------------------------------------------- skeleton

# The stand-ins' proportions along each chain (StandInSkeleton.cs), used where the scan has no landmark to find.
ELBOW_ALONG_ARM = 0.53     # shoulder -> wrist
KNEE_ALONG_LEG = 0.5       # ankle -> hip joint
SPINE_ALONG_TORSO = 0.245  # hips -> neck
CHEST_ALONG_TORSO = 0.59


def _slice(co, axis, value, half_width):
    return co[np.abs(co[:, axis] - value) < half_width]


def fit_body_joints(obj, crotch=None):
    """
    Joint positions for the body bones of a T-posed scan, in Blender coordinates, from cross-sections:
    the armpit is where the slice under the arm empties, the wrist the thinnest slice before the hand, the crotch
    where the gap between the legs closes, the ankle the thinnest slice above the foot, the neck the narrowest slice
    above the shoulders. Elbows, knees and the spine follow the stand-ins' proportions between those.

    ``crotch`` (a height) skips finding it: a dressed scan's shorts or skirt hide it.
    """
    co = vertices(obj)
    top = co[:, 2].max()
    joints = {}

    def measure_arm(c):
        """Shoulder, elbow and wrist of the arm on +X of ``c``, and the arm's radius at the armpit."""
        arm = c[c[:, 0] > 0.3]
        arm_z = float(np.median(arm[:, 2]))
        band = c[np.abs(c[:, 2] - arm_z) < 0.12]
        armpit = None
        for x in np.arange(0.05, 0.45, 0.004):
            part = _slice(band, 0, x, 0.003)
            if len(part) and part[:, 2].min() > arm_z - 0.07:
                armpit = float(x)
                break
        at_armpit = _slice(band, 0, armpit + 0.02, 0.004)
        radius = float(at_armpit[:, 2].max() - at_armpit[:, 2].min()) / 2
        shoulder = np.array([armpit - 0.6 * radius, at_armpit[:, 1].mean(), (at_armpit[:, 2].max() + at_armpit[:, 2].min()) / 2])
        tip = float(c[:, 0].max())
        sizes = []
        for x in np.arange(tip - 0.32, tip - 0.10, 0.004):
            part = _slice(c[c[:, 0] > armpit], 0, x, 0.003)
            if len(part):
                sizes.append((float(np.ptp(part[:, 1]) + np.ptp(part[:, 2])), float(x), part))
        _, wrist_x, wrist_part = min(sizes, key=lambda s: s[0])
        wrist = np.array([wrist_x, wrist_part[:, 1].mean(), (wrist_part[:, 2].max() + wrist_part[:, 2].min()) / 2])
        return shoulder, shoulder + ELBOW_ALONG_ARM * (wrist - shoulder), wrist, radius

    def measure_leg(c, crotch):
        """Hip joint, knee and ankle of the leg on +X of ``c``."""
        thigh = c[(np.abs(c[:, 2] - (crotch - 0.08)) < 0.004) & (c[:, 0] > 0)]
        hip_joint = np.array([thigh[:, 0].mean(), thigh[:, 1].mean(), crotch + 0.06])
        leg = c[(c[:, 0] > 0) & (c[:, 2] < crotch - 0.05)]
        sizes = []
        for z in np.arange(0.05, 0.25, 0.003):
            part = _slice(leg, 2, z, 0.002)
            part = part[part[:, 1] > np.median(part[:, 1]) - 0.06] if len(part) else part  # leave the foot's front out
            if len(part):
                sizes.append((float(np.ptp(part[:, 0]) + np.ptp(part[:, 1])), float(z), part))
        _, ankle_z, ankle_part = min(sizes, key=lambda s: s[0])
        ankle = np.array([ankle_part[:, 0].mean(), ankle_part[:, 1].mean(), ankle_z])
        knee_z = ankle_z + KNEE_ALONG_LEG * (hip_joint[2] - ankle_z)
        knee_part = _slice(leg, 2, knee_z, 0.004)
        return hip_joint, np.array([knee_part[:, 0].mean(), knee_part[:, 1].mean(), knee_z]), ankle

    # Legs: the crotch is where a gap first opens on the centre plane, coming down from the pelvis. Where the thighs
    # touch, that gap only opens low on the legs; then the hip joints go to the pelvis's widest point instead (the top
    # of the thigh bones) with the crotch 6 cm below them, as on the stand-in.
    def centre_solid(z):  # tolerant enough for a raw scan's few-millimetre vertex spacing
        part = _slice(co, 2, z, 0.003)
        return len(part) and (np.abs(part[:, 0]) < 0.015).any()

    if crotch is None:
        for z in np.arange(0.65 * top, 0.3 * top, -0.003):
            if not centre_solid(z) and not any(centre_solid(z - d) for d in (0.004, 0.008, 0.012)):  # a gap that lasts
                crotch = float(z) + 0.003
                break
        if crotch is None or crotch < 0.42 * top:
            widths = []
            for z in np.arange(0.44 * top, 0.6 * top, 0.003):
                part = _slice(co[np.abs(co[:, 0]) < 0.3], 2, z, 0.002)
                if len(part):
                    widths.append((float(np.ptp(part[:, 0])), float(z)))
            crotch = max(widths)[1] - 0.06

    # Each side measured on its own (mirrored onto +X and back): an outfit scan isn't symmetrised, and mirroring one
    # side's joints left the other foot where the scan had it.
    limbs = {}
    for side, sign in ((".L", 1.0), (".R", -1.0)):
        flip = np.array([sign, 1.0, 1.0])
        mirrored = co * flip
        shoulder, elbow, wrist, arm_radius = measure_arm(mirrored)
        hip_joint, knee, ankle = measure_leg(mirrored, crotch)
        limbs[side] = dict(UpperArm=shoulder * flip, LowerArm=elbow * flip, Hand=wrist * flip,
                           UpperLeg=hip_joint * flip, LowerLeg=knee * flip, Foot=ankle * flip)
    shoulder_z = (limbs[".L"]["UpperArm"][2] + limbs[".R"]["UpperArm"][2]) / 2
    hip_z = (limbs[".L"]["UpperLeg"][2] + limbs[".R"]["UpperLeg"][2]) / 2

    # Torso: the neck is the narrowest slice between the shoulders and the crown.
    centre = co[np.abs(co[:, 0]) < 0.2]
    widths = []
    for z in np.arange(shoulder_z + 0.02, top - 0.12, 0.003):
        part = _slice(centre, 2, z, 0.002)
        if len(part):
            widths.append((float(np.ptp(part[:, 0])), float(z)))
    _, neck_narrowest = min(widths)

    def torso_point(z):
        part = _slice(centre, 2, z, 0.004)
        return np.array([0.0, part[:, 1].mean(), z])

    hips = torso_point(hip_z + 0.05)
    neck = torso_point(shoulder_z + 0.035)
    head = torso_point(neck_narrowest + 0.025)
    joints.update({
        "Hips": hips,
        "Spine": torso_point(hips[2] + SPINE_ALONG_TORSO * (neck[2] - hips[2])),
        "Chest": torso_point(hips[2] + CHEST_ALONG_TORSO * (neck[2] - hips[2])),
        "Neck": neck,
        "Head": head,
    })
    for side, found in limbs.items():
        for bone, position in found.items():
            joints[bone + side] = position
    return joints, {"crown": float(top), "neck_narrowest": neck_narrowest, "arm_radius": arm_radius, "crotch": crotch}


def _bvh(obj):
    from mathutils.bvhtree import BVHTree
    co = vertices(obj)
    return BVHTree.FromPolygons([tuple(v) for v in co], [tuple(p.vertices) for p in obj.data.polygons])


def face_depth_map(obj, head_centre, step=0.0015, half_width=0.09, half_height=0.11):
    """
    Front-to-back depth of the face on a grid (x across, z up): where a ray from the front first hits the head.
    Returns (xs, zs, depth) with depth = the hit's y (larger is further back), NaN where a ray misses.
    """
    from mathutils import Vector
    tree = _bvh(obj)
    xs = np.arange(-half_width, half_width + 1e-9, step)
    zs = np.arange(head_centre[2] - half_height, head_centre[2] + half_height + 1e-9, step)
    depth = np.full((len(zs), len(xs)), np.nan)
    for i, z in enumerate(zs):
        for j, x in enumerate(xs):
            hit, _, _, _ = tree.ray_cast(Vector((x, head_centre[1] - 1.0, z)), Vector((0.0, 1.0, 0.0)), 2.0)
            if hit is not None:
                depth[i, j] = hit.y
    return xs, zs, depth


def _blur(field, sigma_cells):
    """Gaussian blur that ignores NaNs (normalised convolution), separable, numpy only."""
    radius = int(3 * sigma_cells)
    kernel = np.exp(-0.5 * (np.arange(-radius, radius + 1) / sigma_cells) ** 2)
    valid = np.isfinite(field).astype(float)
    values = np.nan_to_num(field)
    for axis in (0, 1):
        values = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="same"), axis, values)
        valid = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="same"), axis, valid)
    return values / np.maximum(valid, 1e-9)


def fit_face_joints(obj, joints, measures):
    """
    Face bone positions from the head's shape, and a better Head bone. A depth map of the face minus a blurred copy
    gives each point's protrusion or recess, ignoring grazing hits at the silhouette. The nose is the strongest
    protrusion on the centre line, the eye sockets the strongest recesses just above it on each side, the mouth the
    groove below it. Brows, blush and the smile arcs are placed from those in the stand-ins' proportions, and the
    Head bone goes to the chin, under the face, as on the stand-in. Returns joints in Blender coordinates and the eye
    sockets' half sizes (x, z) for sizing the eyes.
    """
    from collections import deque
    crown = measures["crown"]
    head_vertices = vertices(obj)
    head_vertices = head_vertices[(head_vertices[:, 2] > crown - 0.25) & (np.abs(head_vertices[:, 0]) < 0.12)]
    head_centre = np.array([0.0, float(np.median(head_vertices[:, 1])), crown - 0.14])
    xs, zs, depth = face_depth_map(obj, head_centre, half_height=0.16)
    step = xs[1] - xs[0]
    slope_z, slope_x = np.gradient(depth, step)
    valid = np.isfinite(depth) & (np.abs(slope_x) < 1.2) & (np.abs(slope_z) < 1.2)
    masked = np.where(valid, depth, np.nan)
    relief = np.where(valid, depth - _blur(masked, 0.012 / step), np.nan)  # fine: the nose and mouth
    hollows = np.where(valid, depth - _blur(masked, 0.03 / step), np.nan)  # coarse: whole anime eye sockets
    X, Z = np.meshgrid(xs, zs)

    # The head's height first: the chin is where the face's centre profile steps back to the neck, coming down.
    # Everything else is searched in bands of that height (anime proportions), so a pointed chin can't pass for the
    # nose and the hollows behind the ears can't pass for eye sockets.
    column = np.nanmean(np.where(np.abs(X) < 0.004, depth, np.nan), axis=1)
    chin_z = measures["neck_narrowest"] + 0.02
    for a in range(len(zs) - 1, 2, -1):
        if zs[a] < crown - 0.12 and np.isfinite(column[a]) and np.isfinite(column[a - 3]) and column[a - 3] - column[a] > 0.012:
            chin_z = float(zs[a])
            break
    height = crown - chin_z

    def band(top, bottom):  # fractions of the head height, measured down from the crown
        return (Z < crown - top * height) & (Z > crown - bottom * height)

    centre = valid & (np.abs(X) < 0.004) & band(0.58, 0.82)
    k = np.unravel_index(np.argmin(np.where(centre, relief, np.inf)), relief.shape)
    nose = np.array([0.0, depth[k], zs[k[0]]])

    across = valid & band(0.4, 0.66)
    face_half_width = float(np.abs(X[across]).max()) if across.any() else 0.08
    window = valid & (X > 0.012) & (X < 0.72 * face_half_width) & (Z > nose[2] + 0.005) & band(0.36, 1.0)
    i, j = np.unravel_index(np.argmax(np.where(window, hollows, -np.inf)), hollows.shape)
    socket = window & (hollows > 0.35 * hollows[i, j])
    seen = np.zeros_like(socket)
    queue, cells = deque([(i, j)]), []
    seen[i, j] = True
    while queue:
        a, b = queue.popleft()
        cells.append((a, b))
        for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            na, nb = a + da, b + db
            if 0 <= na < socket.shape[0] and 0 <= nb < socket.shape[1] and socket[na, nb] and not seen[na, nb]:
                seen[na, nb] = True
                queue.append((na, nb))
    cells = np.array(cells)
    eye_x = float(xs[cells[:, 1]].mean())
    eye_z = float(zs[cells[:, 0]].mean())
    socket_half = (float(np.ptp(xs[cells[:, 1]])) / 2 + step / 2, float(np.ptp(zs[cells[:, 0]])) / 2 + step / 2)

    groove = valid & (np.abs(X) < 0.004) & (Z < nose[2] - 0.006) & band(0.74, 0.94)
    m = np.unravel_index(np.argmax(np.where(groove, relief, -np.inf)), relief.shape)
    mouth_z = float(zs[m[0]]) if groove.any() and relief[m] > 0.0004 else nose[2] - 0.45 * (nose[2] - chin_z)

    def surface(x, z):
        a = int(np.clip(np.round((z - zs[0]) / step), 0, len(zs) - 1))
        b = int(np.clip(np.round((x - xs[0]) / step), 0, len(xs) - 1))
        return float(depth[a, b]) if np.isfinite(depth[a, b]) else float(np.nanmin(depth))

    brow_z = eye_z + socket_half[1] + 0.01
    blush_z = eye_z - socket_half[1] - 0.008  # just under the eye: generated eyes are set wide, unlike the stand-in's
    face_joints = {
        "Head": np.array([0.0, head_centre[1], chin_z]),
        "Nose": nose,
        "Mouth": np.array([0.0, surface(0.0, mouth_z), mouth_z]),
    }
    face_joints["Mouth.L"] = face_joints["Mouth"] + np.array([0.008, 0.0, 0.0])
    face_joints["Mouth.R"] = face_joints["Mouth"] + np.array([-0.008, 0.0, 0.0])
    eye_y = surface(eye_x, eye_z) + 0.003  # the pivot for blinks and looks, just behind the painted-on eye
    for side, sign in ((".L", 1.0), (".R", -1.0)):
        face_joints["Eye" + side] = np.array([sign * eye_x, eye_y, eye_z])
        face_joints["EyeSmile" + side] = np.array([sign * eye_x, eye_y, eye_z - 0.003])
        face_joints["Brow" + side] = np.array([sign * eye_x * 1.05, surface(eye_x * 1.05, brow_z), brow_z])
        face_joints["Blush" + side] = np.array([sign * eye_x * 1.1, surface(eye_x * 1.1, blush_z), blush_z])
    return face_joints, {"socket_half": socket_half, "head_centre": head_centre, "depth_map": (xs, zs, depth, hollows)}


# ---------------------------------------------------------------------------------------------------- rig

# Parent order as in StandInSkeleton.cs: parents before children.
PARENTS = [
    ("Hips", None), ("Spine", "Hips"), ("Chest", "Spine"), ("Neck", "Chest"), ("Head", "Neck"),
    ("Eye.L", "Head"), ("Eye.R", "Head"), ("Brow.L", "Head"), ("Brow.R", "Head"), ("Nose", "Head"),
    ("Mouth", "Head"), ("Mouth.L", "Mouth"), ("Mouth.R", "Mouth"), ("Blush.L", "Head"), ("Blush.R", "Head"),
    ("EyeSmile.L", "Head"), ("EyeSmile.R", "Head"),
    ("UpperArm.L", "Chest"), ("LowerArm.L", "UpperArm.L"), ("Hand.L", "LowerArm.L"),
    ("UpperArm.R", "Chest"), ("LowerArm.R", "UpperArm.R"), ("Hand.R", "LowerArm.R"),
    ("UpperLeg.L", "Hips"), ("LowerLeg.L", "UpperLeg.L"), ("Foot.L", "LowerLeg.L"),
    ("UpperLeg.R", "Hips"), ("LowerLeg.R", "UpperLeg.R"), ("Foot.R", "LowerLeg.R"),
]
FACE_BONES = {name for name, parent in PARENTS if parent in ("Head", "Mouth")}


def blender_to_unity(p):
    return (-float(p[0]), float(p[2]), -float(p[1]))


# The game's everyday pose: Poses.Relaxed (Assets/_Game/Characters/Runtime/Building/Poses.cs), which the town, the
# creator, the menu and portraits all stand characters in. Each bone's bend in Unity Euler degrees, in the character's
# frame, on top of its parent's. Keep the two in step.
RELAXED = {"UpperArm.L": (0.0, 8.0, 72.0), "UpperArm.R": (0.0, -8.0, -72.0), "LowerArm.L": (0.0, 12.0, 0.0), "LowerArm.R": (0.0, -12.0, 0.0)}
UNITY_TO_BLENDER = np.array([[-1.0, 0.0, 0.0], [0.0, 0.0, -1.0], [0.0, 1.0, 0.0]])  # blender_to_unity's inverse


def unity_euler(x, y, z):
    """Quaternion.Euler(x, y, z) as a matrix (Unity coordinates): z degrees about Z, then x about X, then y about Y."""
    def turn(axis, degrees):
        c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))
        i, j = [(1, 2), (2, 0), (0, 1)][axis]
        m = np.eye(3)
        m[i, i], m[i, j], m[j, i], m[j, j] = c, -s, s, c
        return m
    return turn(1, y) @ turn(0, x) @ turn(2, z)


def pose_skinning(joints, pose=RELAXED):
    """
    Each body bone's skinning transform for ``pose``, as (rotation, translation) in Blender coordinates: a vertex
    on that bone goes to rotation @ v + translation. Bends compose down the hierarchy as Poses.Bend composes them.
    """
    bend, rotation, head = {}, {}, {}
    for name, parent in PARENTS:
        if name in FACE_BONES or name not in joints:
            continue
        bend[name] = (bend[parent] if parent else np.eye(3)) @ unity_euler(*pose.get(name, (0.0, 0.0, 0.0)))
        rotation[name] = UNITY_TO_BLENDER @ bend[name] @ UNITY_TO_BLENDER.T
        at = np.asarray(joints[name], dtype=float)
        head[name] = at if parent is None else head[parent] + rotation[parent] @ (at - np.asarray(joints[parent], dtype=float))
    return {name: (rotation[name], head[name] - rotation[name] @ np.asarray(joints[name], dtype=float)) for name in rotation}


def _unposed_share(weights, names, skinning):
    """Each vertex's weight on no posed bone (unweighted, or on face bones): that share stays where it is."""
    posed = [column for column, name in enumerate(names) if name in skinning]
    return np.clip(1.0 - weights[:, posed].sum(axis=1), 0.0, 1.0)


def skin_positions(co, weights, names, skinning):
    """Linear blend skinning of rest positions ``co`` (weights: vertices x names) into the pose."""
    posed = co * _unposed_share(weights, names, skinning)[:, None]
    for column, name in enumerate(names):
        if name in skinning and weights[:, column].any():
            r, t = skinning[name]
            posed += weights[:, column:column + 1] * (co @ r.T + t)
    return posed


def unskin_positions(posed, weights, names, skinning):
    """The rest positions that skin_positions takes to ``posed``: each vertex's blended transform, inverted."""
    blend = np.repeat(np.eye(3)[None], len(posed), axis=0) * _unposed_share(weights, names, skinning)[:, None, None]
    shift = np.zeros_like(posed)
    for column, name in enumerate(names):
        if name in skinning and weights[:, column].any():
            r, t = skinning[name]
            blend += weights[:, column, None, None] * r
            shift += weights[:, column:column + 1] * t
    return np.linalg.solve(blend, (posed - shift)[:, :, None])[:, :, 0]


def skeleton_from_joints(joints):
    """The (name, parent, Unity position) list character_tools.build_armature takes, rounded to 0.1 mm."""
    return [(name, parent, tuple(round(c, 4) for c in blender_to_unity(joints[name]))) for name, parent in PARENTS]


def skin(obj, rig):
    """
    Bone-heat automatic weights from the body bones only (the face bones carry the face features, not the skin),
    then at most 4 influences per vertex, normalised, as the validator requires.
    """
    import character_tools as ct
    for bone in rig.data.bones:
        bone.use_deform = bone.name not in FACE_BONES
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for bone in rig.data.bones:
        bone.use_deform = True
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=ct.MAX_INFLUENCES)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)


def weight_matrix(obj):
    """(vertices x groups) skin weights and the group names."""
    names = [g.name for g in obj.vertex_groups]
    weights = np.zeros((len(obj.data.vertices), len(names)))
    for v in obj.data.vertices:
        for g in v.groups:
            weights[v.index, g.group] = g.weight
    return weights, names


def bone_region(bone):
    """The body region a bone's skin belongs to (BodyRegions.cs), as character_tools.REGIONS names it."""
    side = bone[-1] if bone.endswith((".L", ".R")) else ""
    stem = bone[:-2] if side else bone
    return {
        "Hips": "Pelvis", "Spine": "LowerTorso", "Chest": "UpperTorso", "Neck": "Neck", "Head": "Head",
        "UpperArm": "UpperArm" + side, "LowerArm": "Forearm" + side, "Hand": "Hand" + side,
        "UpperLeg": "Thigh" + side, "LowerLeg": "Calf" + side, "Foot": "Foot" + side,
    }.get(stem, "Head")


def paint_regions(obj):
    """
    The `Col` attribute (character_tools.COLOUR_ATTRIBUTE): R = the region of the vertex's strongest bone / 255,
    G, B, A = recolour regions 1-3, zero on skin. Returns the per-vertex region names.
    """
    import character_tools as ct
    weights, names = weight_matrix(obj)
    strongest = [bone_region(names[i]) for i in np.argmax(weights, axis=1)]
    mesh = obj.data
    if ct.COLOUR_ATTRIBUTE in mesh.color_attributes:
        mesh.color_attributes.remove(mesh.color_attributes[ct.COLOUR_ATTRIBUTE])
    colours = mesh.color_attributes.new(ct.COLOUR_ATTRIBUTE, "FLOAT_COLOR", "POINT")
    values = np.zeros((len(mesh.vertices), 4))
    values[:, 0] = [ct.REGIONS[r] / 255.0 for r in strongest]
    colours.data.foreach_set("color", values.ravel())
    return strongest


# Body shape responses, after StandInMesh.ShapeDeltas.
FLESHY = {"Pelvis", "LowerTorso", "UpperTorso", "UpperArmL", "UpperArmR", "ForearmL", "ForearmR", "ThighL", "ThighR", "CalfL", "CalfR"}


def _region_segment(region, joints):
    """The axis a region's shape response is measured along: (start, end) in Blender coordinates, or None."""
    j = {k: np.asarray(v, dtype=float) for k, v in joints.items()}
    side = "." + region[-1] if region[-1] in "LR" and region not in ("Pelvis",) else ""
    stem = region[:-1] if side else region
    crotch = np.array([0.0, j["Hips"][1], j["UpperLeg.L"][2] - 0.06])
    segments = {
        "Pelvis": (crotch, j["Spine"]),
        "LowerTorso": (j["Spine"], j["Chest"]),
        "UpperTorso": (j["Chest"], j["Neck"] + np.array([0.0, 0.0, 0.01])),
        "UpperArm": ("UpperArm", "LowerArm"), "Forearm": ("LowerArm", "Hand"),
        "Thigh": ("UpperLeg", "LowerLeg"), "Calf": ("LowerLeg", "Foot"),
    }
    segment = segments.get(stem)
    if segment is None:
        return None
    if isinstance(segment[0], str):
        return j[segment[0] + side], j[segment[1] + side]
    return segment


def shape_deltas(region, t, normal_unity, radius):
    """StandInMesh.ShapeDeltas in Unity axes: the (body.weight, body.chest, body.hips) offsets of one point."""
    n = np.asarray(normal_unity)
    unit = lambda x: min(max(x, 0.0), 1.0)
    weight, chest, hips = np.zeros(3), np.zeros(3), np.zeros(3)
    if region in FLESHY:
        weight = n * radius * 0.4
    if region in ("UpperTorso", "Pelvis"):
        front = max(0.0, n[2])
        weight = weight + n * (0.022 * front * front * math.sin(math.pi * unit(t)))
    if region == "UpperTorso":
        front = max(0.0, n[2])
        chest = n * (0.055 * front * front * math.sin(math.pi * unit((t - 0.05) / 0.85)))
    if region == "Pelvis":
        hips = np.array([n[0], 0.0, 0.0]) * (0.05 * math.sin(math.pi * unit(t)))
    if region in ("ThighL", "ThighR"):
        hips = np.array([n[0], 0.0, 0.0]) * (0.04 * unit(1.0 - t / 0.45))
    return weight, chest, hips


def add_body_shapes(obj, joints):
    """
    `body.weight`, `body.chest` and `body.hips` as shape keys. Each vertex blends the responses of the regions of the
    bones it's weighted to, by those weights, so region boundaries move smoothly: the stand-ins' separate tubes could
    afford hard steps, one continuous body can't.
    """
    weights, names = weight_matrix(obj)
    co = vertices(obj)
    normals = np.empty(len(co) * 3)
    obj.data.vertices.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    deltas = np.zeros((3, len(co), 3))  # Unity axes
    for g, name in enumerate(names):
        region = bone_region(name)
        segment = _region_segment(region, joints)
        affected = np.flatnonzero(weights[:, g] > 0)
        if segment is None or not len(affected):
            continue
        a, b = segment
        axis = b - a
        t = np.clip((co[affected] - a) @ axis / max(np.dot(axis, axis), 1e-12), -1.0, 2.0)
        radii = np.linalg.norm(co[affected] - (a + np.outer(np.clip(t, 0.0, 1.0), axis)), axis=1)
        # A scan's radius isn't a tube's: breasts and buttocks sit far off the bone axis, and a response proportional
        # to that distance inflates them. Cap it at a little over the region's typical radius.
        radii = np.minimum(radii, 1.15 * np.median(radii))
        for i, ti, radius in zip(affected, t, radii):
            for k, d in enumerate(shape_deltas(region, float(ti), blender_to_unity(normals[i]), float(radius))):
                deltas[k, i] += weights[i, g] * np.asarray(d)
    if obj.data.shape_keys is None:
        obj.shape_key_add(name="Basis", from_mix=False)
    for k, shape in enumerate(("body.weight", "body.chest", "body.hips")):
        key = obj.shape_key_add(name=shape, from_mix=False)
        key.value = 0.0  # new keys aren't guaranteed to start at zero
        unity = deltas[k]
        blender = np.stack([-unity[:, 0], -unity[:, 2], unity[:, 1]], axis=1)  # character_tools.unity_to_blender
        key.data.foreach_set("co", (co + blender).ravel())


# ---------------------------------------------------------------------------------------------------- face

def u2b(offset):
    """A Unity-axes offset in Blender axes (character_tools.unity_to_blender without the Vector)."""
    x, y, z = offset
    return np.array([-x, -z, y])


# Recolour masks (G, B, A = regions 1-3; region 4 rides the second UV map), as StandInBody's mask functions.
IRIS, DARK, WHITE, LIPS = (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)


def weight_nose(obj, joints, radius=0.012):
    """
    Hands the skin around the sculpted nose tip to the Nose bone (falling off over ``radius``), so the nose style
    sliders, which move that bone, move the generated nose. The stand-in's nose was a separate ellipsoid on it.
    """
    tip = np.asarray(joints["Nose"])
    co = vertices(obj)
    share = np.clip(1.0 - np.linalg.norm(co - tip, axis=1) / radius, 0.0, 1.0) ** 2
    nose = obj.vertex_groups.get("Nose") or obj.vertex_groups.new(name="Nose")
    for i in np.flatnonzero(share > 0.01):
        v = obj.data.vertices[i]
        for g in v.groups:
            obj.vertex_groups[g.group].add([int(i)], g.weight * (1.0 - share[i]), "REPLACE")
        nose.add([int(i)], float(share[i]), "REPLACE")
    import character_tools as ct
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=ct.MAX_INFLUENCES)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)


class FeatureBuilder:
    """
    Collects the face features, each rigid on one bone with its recolour mask, then merges them into the body.

    Two kinds: conformal discs, projected onto the scanned face and lifted just proud of it, like features painted on
    an anime face (eyes, irises, catch-lights, brows, lips); and ellipsoid strokes sunk under the skin until
    FaceExpression pushes their bones out (blush, smiling eyes).
    """

    def __init__(self, tree):
        self.tree = tree  # BVH of the head before any features
        self.parts = []   # (vertices Nx3, faces, bone, mask, drawn over the skin)

    def surface(self, x, z):
        """Where a ray from the front first hits the face at (x, z): its y, or None."""
        from mathutils import Vector
        hit, _, _, _ = self.tree.ray_cast(Vector((x, -2.0, z)), Vector((0.0, 1.0, 0.0)), 4.0)
        return None if hit is None else hit.y

    def disc(self, centre_x, centre_z, radii, lift, bone, mask, rings=5, segments=20, thickness=0.002):
        """
        An elliptical lens on the face: its front follows the scanned surface ``lift`` metres proud of it, its back
        ``thickness`` behind the surface, so it reads as painted on and stays closed for the outline pass.
        """
        rx, rz = radii
        points = [(0.0, 0.0)] + [(r * np.cos(a), r * np.sin(a)) for r in np.linspace(1.0 / rings, 1.0, rings)
                                 for a in np.linspace(0.0, 2 * np.pi, segments, endpoint=False)]
        centre_y = self.surface(centre_x, centre_z)
        front, back = [], []
        for u, w in points:
            x, z = centre_x + u * rx, centre_z + w * rz
            y = self.surface(x, z)
            y = centre_y if y is None else y
            front.append((x, y - lift, z))
            back.append((x, y + thickness, z))
        n = len(points)

        def ring(k, a):  # index of the a-th point on ring k (1-based)
            return 1 + (k - 1) * segments + a % segments

        faces = []
        for offset, flip in ((0, False), (n, True)):  # the front, then the back facing away
            for a in range(segments):
                tri = (offset, offset + ring(1, a), offset + ring(1, a + 1))
                faces.append(tri[::-1] if flip else tri)
            for k in range(1, rings):
                for a in range(segments):
                    quad = (offset + ring(k, a), offset + ring(k + 1, a), offset + ring(k + 1, a + 1), offset + ring(k, a + 1))
                    faces.append(quad[::-1] if flip else quad)
        for a in range(segments):  # the rim joins them
            faces.append((ring(rings, a + 1), n + ring(rings, a + 1), n + ring(rings, a), ring(rings, a)))
        self.parts.append((np.array(front + back), faces, bone, mask, True))

    def ellipsoid(self, centre, radii, bone, mask, axis=None, segments=12, rows=8):
        rotation = np.eye(3)
        if axis is not None:  # stretched along a direction: a stroke between two points
            a = np.asarray(axis, dtype=float) / np.linalg.norm(axis)
            reference = np.array([0.0, 0.0, 1.0]) if abs(a[2]) < 0.9 else np.array([0.0, 1.0, 0.0])
            b = np.cross(reference, a)
            b /= np.linalg.norm(b)
            rotation = np.stack([a, b, np.cross(a, b)], axis=1)
        unit = [(0.0, 0.0, -1.0)]
        for r in range(1, rows):
            phi = np.pi * r / rows - np.pi / 2
            unit += [(np.cos(phi) * np.cos(t), np.cos(phi) * np.sin(t), np.sin(phi))
                     for t in np.linspace(0.0, 2 * np.pi, segments, endpoint=False)]
        unit.append((0.0, 0.0, 1.0))
        verts = (np.array(unit) * np.asarray(radii)) @ rotation.T + np.asarray(centre, dtype=float)
        last = len(unit) - 1
        faces = []
        for t in range(segments):
            faces.append((0, 1 + (t + 1) % segments, 1 + t))
            faces.append((last, last - segments + t, last - segments + (t + 1) % segments))
        for r in range(rows - 2):
            for t in range(segments):
                a, b = 1 + r * segments + t, 1 + r * segments + (t + 1) % segments
                faces.append((a, b, b + segments, a + segments))
        self.parts.append((verts, faces, bone, mask, False))

    def stroke(self, start, end, radius, bone, mask):
        start, end = np.asarray(start), np.asarray(end)
        self.ellipsoid((start + end) / 2, (np.linalg.norm(end - start) / 2 + radius, radius, radius), bone, mask, end - start)

    def merge_into(self, obj):
        """Appends the features to ``obj``: geometry, bone weights, `Col` masks, and the second UV map (x: recolour
        region 4; y: 1 on the painted-on discs, which the toon shader draws over the skin)."""
        import character_tools as ct
        mesh = obj.data
        base_vertices = len(mesh.vertices)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        tag = bm.verts.layers.int.new("feature")  # mesh order after to_mesh isn't creation order: tag instead
        for part, (verts, faces, _, _, _) in enumerate(self.parts):
            made = []
            for co in verts:
                v = bm.verts.new(tuple(co))
                v[tag] = part + 1
                made.append(v)
            for face in faces:
                bm.faces.new([made[i] for i in face])
        bm.to_mesh(mesh)
        bm.free()
        feature = np.empty(len(mesh.vertices), dtype=np.int64)
        mesh.attributes["feature"].data.foreach_get("value", feature)
        mesh.attributes.remove(mesh.attributes["feature"])

        colours = mesh.color_attributes[ct.COLOUR_ATTRIBUTE]
        values = np.empty(len(mesh.vertices) * 4)
        colours.data.foreach_get("color", values)
        values = values.reshape(-1, 4)
        while len(mesh.uv_layers) < 2:
            mesh.uv_layers.new(name="UVMap" if not mesh.uv_layers else "Region4")
        loop_vertex = np.empty(len(mesh.loops), dtype=np.int64)
        mesh.loops.foreach_get("vertex_index", loop_vertex)
        region4 = np.zeros(len(mesh.vertices))
        over = np.zeros(len(mesh.vertices))
        for part, (_, _, bone, mask, drawn_over) in enumerate(self.parts):
            indices = np.flatnonzero(feature == part + 1)
            group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
            group.add(indices.tolist(), 1.0, "REPLACE")
            values[indices] = (0.0, mask[0], mask[1], mask[2])  # R = the Head region (0)
            region4[indices] = mask[3]
            over[indices] = 1.0 if drawn_over else 0.0
        colours.data.foreach_set("color", values.ravel())
        uv = np.zeros((len(mesh.loops), 2))
        uv[:, 0] = region4[loop_vertex]
        uv[:, 1] = over[loop_vertex]  # CharacterToon draws these 10 mm nearer the camera (ToonObjectToHClip)
        mesh.uv_layers[1].data.foreach_set("uv", uv.ravel())
        mesh.update()
        return len(mesh.vertices) - base_vertices


LIFT = 0.002  # how far the painted-on face features stand off the skin


def build_face(obj, joints, socket_half):
    """
    The game's face features (StandInBody.Build), built against the generated head. Eyes, irises, catch-lights,
    brows and lips are conformal discs sized to the scan's eye sockets, layered 2.0, 2.6 and 3.2 mm proud of the
    surface: the skin's outline shell (a constant 1.4 px) hides anything flatter, as the stand-in's blush comment notes. The blush and smiling-eye strokes are sunk under the real surface by the stand-in's depths along the
    face's forward axis, so FaceExpression's pushes (BlushDepth 9.5 mm, EyeSmileDepth 11.5 mm) bring them out as
    before. The scan's own nose stays (see weight_nose).
    """
    j = {k: np.asarray(v, dtype=float) for k, v in joints.items()}
    features = FeatureBuilder(_bvh(obj))
    white = (socket_half[0] * 0.8, socket_half[1] * 0.8)  # across, up: anime eyes fill their sockets
    scale = white[0] / 0.019  # relative to the stand-in's eye white

    for side in (".L", ".R"):
        eye = j["Eye" + side]
        features.disc(eye[0], eye[2], white, LIFT, "Eye" + side, WHITE)
        features.disc(eye[0], eye[2] - 0.002 * scale, (white[0] * 0.6, white[1] * 0.85), LIFT + 0.0006, "Eye" + side, IRIS)
        # The catch-light: upper left as you look at them (the same side on both eyes), in the eye whites' colour.
        features.disc(eye[0] - 0.0045 * scale, eye[2] + 0.0065 * scale, (0.0036 * scale, 0.0042 * scale), LIFT + 0.0012,
                      "Eye" + side, WHITE, rings=2, segments=12)
        brow = j["Brow" + side]
        features.disc(brow[0], brow[2], (0.019 * scale, 0.0045 * scale), LIFT, "Brow" + side, DARK, rings=2)

        # Blush: three short strokes on each cheek, 6.2 mm under the skin along the face's forward axis, every one
        # leaning the same way on both cheeks (/// ///) as the stand-in's do.
        blush = j["Blush" + side]
        for i in (-1, 0, 1):
            cx = blush[0] + i * 0.0085 * scale

            def under(px, pz):
                y = features.surface(px, pz)
                return np.array([px, (blush[1] if y is None else y) + 0.0062, pz])

            features.stroke(under(cx - 0.0028, blush[2] - 0.006), under(cx + 0.0028, blush[2] + 0.006), 0.0017, "Blush" + side, DARK)

        # Smiling closed eyes: an arch of five strokes over each eye, 6.5 mm under the eye's surface.
        smile = j["EyeSmile" + side]
        arc = []
        for a in np.linspace(math.pi * 0.08, math.pi * 0.92, 6):
            px, pz = smile[0] + math.cos(a) * 0.017 * scale, smile[2] - 0.004 + math.sin(a) * 0.012 * scale
            y = features.surface(px, pz)
            arc.append(np.array([px, (smile[1] if y is None else y) - LIFT + 0.0065, pz]))
        for a, b in zip(arc[:-1], arc[1:]):
            features.stroke(a, b, 0.0021, "EyeSmile" + side, DARK)

    mouth = j["Mouth"]
    features.disc(mouth[0], mouth[2], (0.0095 * scale, 0.003), LIFT, "Mouth", LIPS, rings=2)
    for side, sign in ((".L", 1.0), (".R", -1.0)):
        features.disc(mouth[0] + sign * 0.0135 * scale, mouth[2], (0.0072 * scale, 0.0027), LIFT - 0.0002, "Mouth" + side, LIPS, rings=2)
    return features.merge_into(obj)


# ---------------------------------------------------------------------------------------------------- preview

MASK_PREVIEW = ((0.93, 0.80, 0.74), (0.25, 0.45, 0.85), (0.30, 0.18, 0.12), (0.97, 0.97, 0.99), (0.85, 0.35, 0.40))


def render_preview(body, rig, path):
    """
    Workbench renders in stand-in colours (skin, iris, brows, eye whites, lips from the recolour masks): the whole body
    from the front, the face close up, and a test pose with arms down, a knee bent and the head turned. Writes
    <path>_front.png, <path>_face.png and <path>_pose.png.
    """
    import mathutils
    import character_tools as ct
    base = path[:-4] if path.lower().endswith(".png") else path
    mesh = body.data
    col = np.empty(len(mesh.vertices) * 4)
    mesh.color_attributes[ct.COLOUR_ATTRIBUTE].data.foreach_get("color", col)
    col = col.reshape(-1, 4)
    loop_vertex = np.empty(len(mesh.loops), dtype=np.int64)
    mesh.loops.foreach_get("vertex_index", loop_vertex)
    uv = np.empty(len(mesh.loops) * 2)
    mesh.uv_layers[1].data.foreach_get("uv", uv)
    region4 = np.zeros(len(mesh.vertices))
    region4[loop_vertex] = uv.reshape(-1, 2)[:, 0]
    colour = np.tile(np.array(MASK_PREVIEW[0]), (len(col), 1))
    for weight, rgb in zip((col[:, 1], col[:, 2], col[:, 3], region4), MASK_PREVIEW[1:]):
        colour = colour * (1 - weight[:, None]) + np.array(rgb) * weight[:, None]
    view = mesh.color_attributes.new("Preview", "FLOAT_COLOR", "POINT")
    view.data.foreach_set("color", np.c_[colour, np.ones(len(colour))].ravel())
    mesh.color_attributes.active_color = view

    scene = bpy.context.scene
    scene.world = scene.world or bpy.data.worlds.new("Preview")
    scene.world.color = (0.18, 0.18, 0.2)
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light, shading.color_type, shading.show_object_outline = "STUDIO", "VERTEX", True
    camera = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = "ORTHO"
    top = vertices(body)[:, 2].max()
    eye_z = rig.data.bones["Eye.L"].head_local.z

    def shoot(name, location, rotation, scale, size):
        camera.location, camera.data.ortho_scale = location, scale
        camera.rotation_euler = tuple(math.radians(a) for a in rotation)
        scene.render.resolution_x, scene.render.resolution_y = size
        scene.render.filepath = f"{base}_{name}.png"
        bpy.ops.render.render(write_still=True)

    shoot("front", (0, -6, top / 2), (90, 0, 0), top * 1.12, (900, 1000))
    shoot("face", (0, -3, eye_z - 0.02), (90, 0, 0), 0.24, (700, 700))
    keys = body.data.shape_keys.key_blocks if body.data.shape_keys else {}
    if "body.masculine" in keys:
        keys["body.masculine"].value = 1.0
        shoot("masculine", (0, -6, top / 2), (90, 0, 0), top * 1.12, (900, 1000))
        shoot("masculine_face", (0, -3, eye_z - 0.02), (90, 0, 0), 0.24, (700, 700))
        keys["body.masculine"].value = 0.0

    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")

    def turn(bone, axis, degrees):
        rest = rig.data.bones[bone].matrix_local.to_quaternion()
        rig.pose.bones[bone].rotation_mode = "QUATERNION"
        rig.pose.bones[bone].rotation_quaternion = rest.inverted() @ mathutils.Quaternion(axis, math.radians(degrees)) @ rest

    turn("UpperArm.L", (0, 1, 0), 70)
    turn("UpperArm.R", (0, 1, 0), -70)
    turn("LowerArm.L", (0, 0, 1), 30)
    turn("UpperLeg.R", (1, 0, 0), -35)
    turn("LowerLeg.R", (1, 0, 0), 60)
    turn("Head", (0, 0, 1), 20)
    bpy.ops.object.mode_set(mode="OBJECT")
    shoot("pose", (4.6, -4.6, top / 2), (90, 0, 45), top * 1.12, (700, 1000))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for bone in rig.pose.bones:
        bone.rotation_quaternion = (1, 0, 0, 0)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.data.objects.remove(camera)
    mesh.color_attributes.remove(mesh.color_attributes["Preview"])


# ---------------------------------------------------------------------------------------------------- presentation

def prepare_scan(path, height, voxel):
    """A scan imported, placed at ``height``, symmetrised, legs parted, remeshed and smoothed: every base's clean-up."""
    obj = pt.import_glb(path)
    pt.place(obj, height)
    symmetrise(obj)
    _, measures = fit_body_joints(obj)
    part_legs(obj, measures["crotch"] - 0.03)
    remesh(obj, voxel)
    smooth_seam(obj)
    return obj


def part_legs(obj, below, gap=0.006):
    """
    Cuts a slab ``gap`` wide out of the centre plane below ``below`` (just under the crotch). Where the thighs touch,
    the scan joins them, and a voxel remesh would weld them into one surface that tears when a leg moves.
    """
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.0, (below - 1.0) / 2))
    slab = bpy.context.active_object
    slab.scale = (gap, 4.0, below + 1.0)
    modifier = obj.modifiers.new("PartLegs", "BOOLEAN")
    modifier.operation = "DIFFERENCE"
    modifier.object = slab
    modifier.solver = "EXACT"
    apply_modifier(obj, modifier)
    bpy.data.objects.remove(slab)


def fill_missing_weights(obj):
    """
    Vertices bone heat left unweighted take the weights of the nearest weighted vertex. Bone heat skips pieces of a
    scan no bone passes through (a shoe Hunyuan3D made as a separate piece), and unweighted vertices don't move.
    """
    from mathutils.kdtree import KDTree
    weights, names = weight_matrix(obj)
    weighted = np.flatnonzero(weights.sum(axis=1) > 0.0)
    missing = np.flatnonzero(weights.sum(axis=1) == 0.0)
    if not len(missing) or not len(weighted):
        return 0
    co = vertices(obj)
    tree = KDTree(len(weighted))
    for k, i in enumerate(weighted):
        tree.insert(co[i], k)
    tree.balance()
    for i in missing:
        _, k, _ = tree.find(co[i])
        source = weighted[k]
        for g in np.flatnonzero(weights[source] > 0.0):
            obj.vertex_groups[names[g]].add([int(i)], float(weights[source, g]), "REPLACE")
    return len(missing)


# Bones that swing to point the way the target's do: each limb bone towards its child joint, hands along the forearm.
# Feet and the torso only move, so feet stay flat and the spine upright.
AIM = {"UpperArm": "LowerArm", "LowerArm": "Hand", "Hand": None, "UpperLeg": "LowerLeg", "LowerLeg": "Foot"}


def align_to_skeleton(obj, own, target):
    """
    Moves a scan so its joints land on ``target``'s: skin it to a rig at its own fitted joints, then give each bone
    the rigid motion taking its joint to the target joint and turning its limb to the target limb's direction, and
    blend those by the skin weights. Two bases then differ in their flesh, not in where their joints are, so one
    canonical skeleton serves both. Translating alone shears a limb instead of swinging it: a scan standing with one
    foot 20 cm wider got a bent shin.
    """
    import character_tools as ct
    from mathutils import Vector
    body_bones = [(name, parent) for name, parent in PARENTS if name not in FACE_BONES]
    rig = ct.build_armature("AlignRig", [(n, p, tuple(blender_to_unity(own[n]))) for n, p in body_bones])
    skin(obj, rig)
    fill_missing_weights(obj)
    weights, names = weight_matrix(obj)
    for modifier in [m for m in obj.modifiers if m.type == "ARMATURE"]:
        obj.modifiers.remove(modifier)
    obj.parent = None
    obj.vertex_groups.clear()
    bpy.data.objects.remove(rig)

    def rotation(name):
        side = name[-2:] if name.endswith((".L", ".R")) else ""
        stem = name[:-2] if side else name
        if stem not in AIM:
            return np.eye(3)
        if AIM[stem] is None:
            return rotation("LowerArm" + side)
        child = AIM[stem] + side
        a = Vector(tuple(np.asarray(own[child]) - np.asarray(own[name])))
        b = Vector(tuple(np.asarray(target[child]) - np.asarray(target[name])))
        return np.array(a.rotation_difference(b).to_matrix())

    co = vertices(obj)
    moved = np.zeros_like(co)
    for column, name in enumerate(names):
        w = weights[:, column]
        if name in own and w.any():
            moved += w[:, None] * ((co - own[name]) @ rotation(name).T + np.asarray(target[name]))
    total = weights.sum(axis=1)
    moved = np.where(total[:, None] > 0, moved / np.maximum(total, 1e-9)[:, None], co)
    obj.data.vertices.foreach_set("co", moved.ravel())
    obj.data.update()


def bone_segments(body, joints):
    """
    The axis each body bone's flesh wraps around, from joint to joint (Blender coordinates): the pelvis from the
    crotch up, the head from the chin to the crown, hands to their fingertips, feet from the ankle to the toes.
    """
    j = {k: np.asarray(v, dtype=float) for k, v in joints.items()}
    co = vertices(body)
    crown = np.array([0.0, j["Head"][1], co[:, 2].max()])
    segments = {
        "Hips": (np.array([0.0, j["Hips"][1], j["UpperLeg.L"][2] - 0.06]), j["Spine"]),
        "Spine": (j["Spine"], j["Chest"]),
        "Chest": (j["Chest"], j["Neck"]),
        "Neck": (j["Neck"], j["Head"]),
        "Head": (j["Head"], crown),
        "Nose": (j["Head"], crown),
    }
    for side, sign in ((".L", 1.0), (".R", -1.0)):
        on_side = co[co[:, 0] * sign > 0.0]
        tip = on_side[np.argmax(on_side[:, 0] * sign)]
        hand = j["Hand" + side]
        ankle = j["Foot" + side]
        foot = on_side[(on_side[:, 2] < ankle[2]) & (np.abs(on_side[:, 0] - ankle[0]) < 0.06)]
        toe = foot[np.argmin(foot[:, 1])] if len(foot) else ankle + np.array([0.0, -0.15, -ankle[2] * 0.6])
        segments.update({
            "UpperArm" + side: (j["UpperArm" + side], j["LowerArm" + side]),
            "LowerArm" + side: (j["LowerArm" + side], hand),
            "Hand" + side: (hand, np.array([tip[0], hand[1], hand[2]])),
            "UpperLeg" + side: (j["UpperLeg" + side], j["LowerLeg" + side]),
            "LowerLeg" + side: (j["LowerLeg" + side], ankle),
            "Foot" + side: (ankle, np.array([ankle[0], toe[1], ankle[2] * 0.4])),
        })
    return segments


def add_presentation_shape(body, other, joints, name="body.masculine"):
    """
    A shape key that turns ``body`` into ``other`` (another base, already aligned to the same skeleton): radial
    projection from the skeleton, snapped onto the other surface. The head (and the face features on it) only scale
    and shift to fit the other head: the two faces' features don't correspond, and matching them point for point
    crumples the face. Returns the largest movement in metres.
    """
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromPolygons([tuple(v) for v in vertices(other)], [tuple(p.vertices) for p in other.data.polygons])
    mesh = body.data
    co = vertices(body)
    weights, names = weight_matrix(body)
    feature_bones = [i for i, n in enumerate(names) if n in FACE_BONES and n != "Nose"]
    is_feature = weights[:, feature_bones].sum(axis=1) > 0.5 if feature_bones else np.zeros(len(co), bool)
    head_bones = [i for i, n in enumerate(names) if n in ("Head", "Nose")]
    head = np.clip(weights[:, head_bones].sum(axis=1) * 1.5, 0.0, 1.0) if head_bones else np.zeros(len(co))

    edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", edges)
    a, b = edges[0::2], edges[1::2]
    keep = ~is_feature[a] & ~is_feature[b]
    a, b = a[keep], b[keep]
    count = np.maximum(np.bincount(np.concatenate([a, b]), minlength=len(co)), 1).astype(float)

    def smooth(field, iterations, weight=None):
        for _ in range(iterations):
            total = np.zeros_like(field)
            np.add.at(total, a, field[b])
            np.add.at(total, b, field[a])
            mean = total / count[:, None]
            w = 0.5 if weight is None else 0.5 * weight[:, None]
            field = field + w * (mean - field)
        return field

    # Radial projection from the shared skeleton: a ray from the nearest point on each influencing bone, out through
    # the vertex, to the other surface, blended by skin weight. Rays from an axis keep their order around it, so no
    # two vertices can cross: a breast maps onto the chest behind it. (Closest-point and along-the-normal targets
    # both let neighbours pass each other on big differences, and the folded triangles crumpled the whole body.)
    segments = bone_segments(body, joints)
    skin = np.flatnonzero(~is_feature)
    target = co.copy()
    for i in skin:
        point, total = np.zeros(3), 0.0
        for g in np.flatnonzero(weights[i] > 0.0):
            segment = segments.get(names[g])
            if segment is None:
                continue
            s, e = segment
            axis = e - s
            t = np.clip(np.dot(co[i] - s, axis) / max(np.dot(axis, axis), 1e-12), 0.0, 1.0)
            origin = s + axis * t
            out = co[i] - origin
            length = np.linalg.norm(out)
            hit = None
            if length > 1e-6:
                hit, _, _, _ = tree.ray_cast(Vector(tuple(origin)), Vector(tuple(out / length)), 1.0)
            if hit is None:
                hit = tree.find_nearest(Vector(tuple(co[i])))[0]
            point += weights[i, g] * np.array(hit)
            total += weights[i, g]
        if total > 0:
            target[i] = point / total
    delta = smooth(target - co, 3)
    broad = smooth(delta, 60, head)  # the head keeps only the broad change

    # Snap onto the other surface, then even out the spacing (relax, snap again). The blended rays and the smoothing
    # leave vertices a millimetre or two off it, and on the decimated body's long thin triangles that tilts the
    # normals enough to read as crumpled.
    def snap(positions):
        snapped = positions.copy()
        for i in skin:
            snapped[i] = np.array(tree.find_nearest(Vector(tuple(positions[i])))[0])
        return snapped

    current = snap(co + delta)
    for _ in range(2):
        current = snap(co + smooth(current - co, 2))

    # The head: a per-axis scale and offset fitted to the other head (its size, the jaw's width), so the face keeps
    # its own features. The feet: the broad change only, since the snap squashes a longer foot unevenly.
    other_co = vertices(other)
    own_head = co[(head > 0.5) & ~is_feature]
    other_head = other_co[(other_co[:, 2] > joints["Head"][2]) & (np.abs(other_co[:, 0]) < 0.15)]
    own_low, own_high = own_head.min(axis=0), own_head.max(axis=0)
    other_low, other_high = other_head.min(axis=0), other_head.max(axis=0)
    scale = (other_high - other_low) / np.maximum(own_high - own_low, 1e-6)
    own_centre, other_centre = (own_low + own_high) / 2, (other_low + other_high) / 2
    head_delta = (co - own_centre) * scale + other_centre - co
    foot_bones = [i for i, n in enumerate(names) if n.startswith("Foot.")]
    feet = np.clip(weights[:, foot_bones].sum(axis=1) * 1.5, 0.0, 1.0)[:, None] if foot_bones else 0.0
    body_delta = feet * smooth(current - co, 40) + (1.0 - feet) * (current - co)
    keep_head = head[:, None]
    delta = keep_head * head_delta + (1.0 - keep_head) * body_delta
    delta = smooth(delta, 30, np.clip(4.0 * head * (1.0 - head) * 2.0, 0.0, 1.0))  # no ridge where the two meet

    # Face features sit on the head, so they take the head's transform exactly, as the skin under them does. (Moving
    # each rigidly with its nearest skin vertex left the eyes behind the scaled face.)
    delta[is_feature] = head_delta[is_feature]

    if mesh.shape_keys is None:
        body.shape_key_add(name="Basis", from_mix=False)
    key = body.shape_key_add(name=name, from_mix=False)
    key.value = 0.0
    key.data.foreach_set("co", (co + delta).ravel())
    return float(np.linalg.norm(delta, axis=1).max())
