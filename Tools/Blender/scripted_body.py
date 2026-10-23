"""Scripted base body and anime head (art spike, decision 0010).

The body is a quad cage built ring by ring, so edge loops sit where the rig bends: three loops at each shoulder,
wrist, hip and ankle, five at each knee and elbow (weighted so they keep their volume, see HINGES), and loops at each
finger joint. One welded mesh from neck to toes: the arms grow out of
2x2 holes in the torso's sides, the legs out of the pelvis floor (sharing the crotch line), the fingers out of the
palm, and the head (a cube-sphere cage) is bridged onto the neck. The cage is subdivided once onto its limit surface.

The head is a parametric construction, not a sculpt: a few signed-distance primitives (cranium, cheeks, jaw cones,
chin, nose) blended smoothly, with the head's vertices projected onto that surface along rays from inside the skull.
Its normals come from an ellipsoid proxy, so the face shades as one smooth form instead of following the jaw and
nose (the toon-normal trick in PLAN.md section 3).

Weights come from the rig (each ring is weighted to the bones it sits between, then smoothed over the final mesh);
the three body shape keys are evaluated per final vertex from its part, normal and distance to its limb's axis.

Everything here is in Unity terms (Y up, +Z forward, character's left at -X); see character_tools.
"""

import math

import numpy as np

import character_tools as ct
import scripted_shapes as ss

PARTS = ["torso", "arm_l", "arm_r", "leg_l", "leg_r", "neck", "head"]
TAGS = ["p_" + p for p in PARTS] + ["flesh", "project", "hand", "seam"]
# Cage lines where clothing cut from the body's cage has its seams (sides, princess lines, centre back, armholes,
# shoulders, under the sleeves, inside and outside the legs). Meaningless on the body itself.
SEAM_COLUMNS = {1, 4, 8, 12, 15}  # never two side by side: a face between two seam lines would read as all seam

# ---------------------------------------------------------------------------------------------------- parameters
# Torso rings, bottom to top: height, half-width, front depth, back depth, superellipse exponent, depth centre.
TORSO_ROWS = [
    (0.855, 0.158, 0.066, 0.086, 2.4, -0.006),
    (0.925, 0.170, 0.074, 0.094, 2.4, -0.008),
    (1.000, 0.150, 0.072, 0.082, 2.3, -0.006),
    (1.080, 0.108, 0.066, 0.068, 2.2, -0.004),
    (1.150, 0.113, 0.069, 0.072, 2.2, -0.002),
    (1.220, 0.124, 0.078, 0.078, 2.3, 0.002),
    (1.290, 0.130, 0.084, 0.080, 2.4, 0.004),
    (1.350, 0.137, 0.080, 0.078, 2.5, 0.002),
    (1.405, 0.148, 0.068, 0.072, 2.6, -0.004),
    (1.450, 0.156, 0.050, 0.060, 2.6, -0.010),
]
ARMPIT_ROW = 7          # the arm holes span rows 7-9 ...
ARM_COLUMNS = (3, 4, 5)  # ... and these columns on the left (mirrored on the right)

# Gaussian bumps on the torso: (x, y, sigma x, sigma y, depth change, front (+1) or back (-1)).
TORSO_BUMPS = [
    (0.080, 1.280, 0.038, 0.040, 0.030, +1),   # bust (the body.chest shape adds more)
    (0.075, 0.915, 0.050, 0.050, 0.020, -1),   # buttocks
    (0.075, 1.360, 0.035, 0.035, 0.006, -1),   # shoulder blades
    (0.000, 1.020, 0.050, 0.060, 0.006, +1),   # lower belly
]

# Arm stations out from the shoulder: |x|, centre y, centre z, vertical radius, depth radius, twist (degrees).
ARM = [
    (0.195, 1.409, -0.002, 0.051, 0.053, 0.0),   # deltoid
    (0.255, 1.402, -0.004, 0.046, 0.047, 0.0),
    (0.330, 1.400, -0.004, 0.041, 0.043, 0.0),
    (0.400, 1.400, -0.004, 0.037, 0.039, 0.0),   # elbow loops (weighted by HINGES)
    (0.425, 1.400, -0.005, 0.035, 0.037, 0.0),
    (0.450, 1.400, -0.006, 0.035, 0.037, 1.0),
    (0.475, 1.400, -0.004, 0.036, 0.039, 4.0),
    (0.505, 1.400, -0.003, 0.036, 0.038, 7.0),
    (0.545, 1.400, -0.002, 0.034, 0.036, 10.0),
    (0.620, 1.400, 0.000, 0.027, 0.031, 16.0),
    (0.685, 1.400, 0.002, 0.019, 0.027, 22.5),   # wrist: a quarter-step twist lines it up with the palm
]
ARM_WEIGHTS = [  # (UpperArm, LowerArm, Hand) per station; the rest is Chest. Elbow loops are overridden by HINGES.
    (0.6, 0, 0), (0.92, 0, 0), (1, 0, 0), (0.8, 0.2, 0), (0.6, 0.4, 0), (0.4, 0.6, 0), (0.22, 0.78, 0), (0.1, 0.9, 0),
    (0, 1, 0), (0, 0.92, 0.08), (0, 0.5, 0.5),
]
# Palm stations past the wrist: |x|, centre y, centre z, width (thumb to little finger), thickness.
PALM = [(0.712, 1.399, 0.004, 0.060, 0.027), (0.748, 1.398, 0.002, 0.070, 0.026), (0.786, 1.397, 0.000, 0.072, 0.022)]
# Fingers: direction (x along the arm, y, z toward the thumb), length, half width, half height.
FINGERS = {
    "index": ((1.0, -0.02, 0.12), 0.072, 0.0085, 0.0076),
    "middle": ((1.0, -0.02, 0.02), 0.080, 0.0090, 0.0080),
    "ring": ((1.0, -0.02, -0.08), 0.074, 0.0085, 0.0075),
    "little": ((1.0, -0.03, -0.26), 0.060, 0.0072, 0.0066),
    "thumb": ((0.55, -0.32, 0.78), 0.064, 0.0105, 0.0092),
}
FINGER_STATIONS = [(0.15, 1.0), (0.45, 0.94), (0.74, 0.88), (0.92, 0.74), (1.0, 0.36)]

# Leg stations down from the hip: y, |x| centre, z centre, front, back, outer and inner radius.
LEG = [
    (0.780, 0.084, -0.002, 0.069, 0.073, 0.073, 0.065),
    (0.680, 0.088, 0.002, 0.063, 0.063, 0.063, 0.057),
    (0.590, 0.090, 0.006, 0.054, 0.053, 0.053, 0.049),
    (0.545, 0.090, 0.008, 0.048, 0.047, 0.047, 0.045),   # knee loops (weighted by HINGES)
    (0.515, 0.090, 0.009, 0.046, 0.044, 0.045, 0.043),
    (0.490, 0.090, 0.010, 0.046, 0.043, 0.044, 0.043),
    (0.465, 0.090, 0.008, 0.044, 0.045, 0.044, 0.043),
    (0.430, 0.090, 0.004, 0.041, 0.050, 0.045, 0.043),
    (0.370, 0.090, -0.004, 0.037, 0.058, 0.047, 0.043),  # calf
    (0.250, 0.090, -0.002, 0.033, 0.040, 0.036, 0.034),
    (0.160, 0.090, 0.000, 0.027, 0.028, 0.028, 0.026),
    (0.105, 0.090, 0.000, 0.028, 0.029, 0.030, 0.029),   # ankle
]
LEG_WEIGHTS = [  # (UpperLeg, LowerLeg, Foot); the rest is Hips. Knee loops are overridden by HINGES.
    (0.85, 0, 0), (1, 0, 0), (1, 0, 0), (0.85, 0.15, 0), (0.6, 0.4, 0), (0.4, 0.6, 0), (0.15, 0.85, 0), (0, 1, 0),
    (0, 1, 0), (0, 1, 0), (0, 0.9, 0.1), (0, 0.5, 0.5),
]
# Knees and elbows keep their volume without helper bones or corrective shapes (the rig and importer stay as they are).
# Linear blend skinning pulls a vertex shared half and half between two bones toward the joint by cos(bend / 2), so a
# blend centred on the joint folds the outside of a 90° bend to 0.71 of its radius, however wide the blend is. On the
# outside of the bend the blend starts at the joint and runs a few loops toward the child bone, so the half-and-half
# vertices sit away from the joint and the outside turns a round corner (about 0.83 of its radius). On the inside it
# stays tight round the joint. A kneecap and an elbow point add a little volume where the outside still thins
# (together about 0.9). ``along`` runs from parent to child (y down the leg, |x| out along the arm); ``outside`` is
# from the joint to full child weight, ``inside`` likewise; the bump is (height, centre, spread).
HINGES = {
    "knee": dict(zone=(0.56, 0.40), outside=(0.505, 0.432), inside=(0.532, 0.466), bump=(0.013, 0.492, 0.022)),
    "elbow": dict(zone=(0.39, 0.52), outside=(0.437, 0.505), inside=(0.410, 0.472), bump=(0.006, 0.452, 0.020)),
}
# Foot rings (the leg tube bends forward): centre, tilt of the ring's "top" from straight forward (0) to straight up (90),
# radii toward top, sole, outer side, inner side.
FOOT = [
    ((0.090, 0.047, -0.004), 45.0, 0.032, 0.044, 0.031, 0.030),   # heel and instep
    ((0.090, 0.036, 0.055), 90.0, 0.029, 0.031, 0.036, 0.033),    # mid foot
    ((0.088, 0.025, 0.118), 90.0, 0.020, 0.020, 0.040, 0.037),    # ball
    ((0.086, 0.021, 0.152), 90.0, 0.016, 0.017, 0.033, 0.029),    # toes
]
TOE_TIP = (0.085, 0.020, 0.172)

# Neck rings above the shoulders' cap: y, z centre, x radius, z radius.
NECK_BASE = (1.486, -0.018, 0.054, 0.050)
NECK = [(1.515, -0.014, 0.045, 0.045), (1.540, -0.010, 0.042, 0.044)]

# The head: a parametric anime head (adult). All offsets are from HEAD["centre"]; see head_sdf.
HEAD = {
    "centre": (0.0, 1.645, 0.0),
    "cranium": ((0.0, 0.025, -0.010), (0.092, 0.096, 0.100)),
    "cheeks": ((0.0, -0.018, 0.020), (0.076, 0.072, 0.077)),
    "jaw": ((0.060, -0.040, -0.020), 0.020, (0.007, -0.100, 0.057), 0.012),  # from the jaw's angle to the chin
    "chin": ((0.0, -0.101, 0.058), 0.014),
    "nose": ((0.0, -0.010, 0.093), 0.0028, (0.0, -0.037, 0.0985), 0.0045),
    "face_plane": 0.099,       # the front of the face is flattened at this depth (anime faces are flat-fronted)
    "blend": (0.035, 0.045, 0.010, 0.020),  # smoothing: face parts, face into cranium, nose, face plane
    "ray_origin": (0.0, -0.015, -0.005),
    "proxy": ((0.0, -0.004, -0.014), (0.088, 0.112, 0.104)),  # ellipsoid the head's normals are taken from
}
HEAD_CAGE = 8  # cube-sphere cage resolution per face (16 after subdivision)

BODY_SHAPE_NAMES = ["body.weight", "body.chest", "body.hips"]


# ---------------------------------------------------------------------------------------------------- helpers

def superellipse(t, exponent):
    return math.copysign(abs(t) ** (2.0 / exponent), t)


def gauss(d, sigma):
    return math.exp(-0.5 * (d / sigma) ** 2)


def torso_point(row, j):
    y, a, front, back, n, zc = TORSO_ROWS[row]
    theta = math.radians(j * 22.5)
    s, c = math.sin(theta), math.cos(theta)
    x = -a * superellipse(s, n)
    z = zc + (front if c >= 0 else back) * superellipse(c, n)
    # The top ring dips at the collarbones and rises at the back of the neck.
    if row == len(TORSO_ROWS) - 1:
        y += -0.018 * max(c, 0.0) ** 2 + 0.012 * max(-c, 0.0) ** 2
    for bx, by, sx, sy, depth, side in TORSO_BUMPS:
        if side * c <= 0:
            continue
        for mirror in ((bx, -bx) if bx else (0.0,)):
            z += side * depth * gauss(x - mirror, sx) * gauss(y - by, sy) * abs(c) ** 0.5
    return np.array((x, y, z))


def blend_weights(*pairs):
    weights = {}
    for bone, w in pairs:
        if w > 1e-6:
            weights[bone] = weights.get(bone, 0.0) + w
    return weights


def torso_weights(y):
    def ramp(lo, hi):
        return min(1.0, max(0.0, (y - lo) / (hi - lo)))
    spine = ramp(0.98, 1.10) * (1 - ramp(1.16, 1.27))
    chest = ramp(1.16, 1.27)
    hips = 1 - ramp(0.98, 1.10)
    return blend_weights(("Hips", hips), ("Spine", spine), ("Chest", chest))


def in_hinge(name, along):
    a, b = HINGES[name]["zone"]
    return min(a, b) < along < max(a, b)


def hinge_weight(name, along, outside):
    """The child bone's weight at a hinge loop vertex; ``outside`` is 1 on the outside of the bend, -1 inside."""
    h = HINGES[name]
    on_outside = float(ss.smoothstep(-0.4, 0.6, outside))
    w_out = float(ss.smoothstep(h["outside"][0], h["outside"][1], along))
    w_in = float(ss.smoothstep(h["inside"][0], h["inside"][1], along))
    return w_in + (w_out - w_in) * on_outside


def hinge_bump(name, along, outside):
    """How far the kneecap or elbow point lifts a vertex on the outside of the bend."""
    height, centre, spread = HINGES[name]["bump"]
    return height * gauss(along - centre, spread) * max(outside, 0.0) ** 2


def side_name(sign):
    return ".L" if sign < 0 else ".R"


def frame(direction, up_hint):
    d = np.asarray(direction, dtype=float)
    d /= np.linalg.norm(d)
    up = np.asarray(up_hint, dtype=float) - d * np.dot(up_hint, d)
    up /= np.linalg.norm(up)
    return d, up


# ---------------------------------------------------------------------------------------------------- the cage

def build_cage():
    cage = ss.Cage(TAGS)
    torso = {}
    rows = len(TORSO_ROWS)
    hole_columns = {j for j in ARM_COLUMNS} | {(16 - j) % 16 for j in ARM_COLUMNS}
    centre_columns = {ARM_COLUMNS[1], (16 - ARM_COLUMNS[1]) % 16}
    for row in range(rows):
        for j in range(16):
            if row == ARMPIT_ROW + 1 and j in centre_columns:
                continue  # inside an arm hole
            weights = torso_weights(TORSO_ROWS[row][0])
            if row >= ARMPIT_ROW and j in hole_columns:
                sign = -1 if j < 8 else 1
                weights = blend_weights(("Chest", 0.72), ("UpperArm" + side_name(sign), 0.28))
            seam = j in SEAM_COLUMNS or (row >= ARMPIT_ROW and j in hole_columns)
            torso[row, j] = cage.add(torso_point(row, j), weights, p_torso=1, flesh=1, seam=seam)
    for row in range(rows - 1):
        for j in range(16):
            n = (j + 1) % 16
            in_hole = ARMPIT_ROW <= row <= ARMPIT_ROW + 1 and (
                j in (ARM_COLUMNS[0], ARM_COLUMNS[1]) or j in ((16 - ARM_COLUMNS[2]) % 16, (16 - ARM_COLUMNS[1]) % 16))
            if in_hole:
                continue
            cage.face(torso[row, j], torso[row, n], torso[row + 1, n], torso[row + 1, j])

    def boundary(row, u, v):
        """Cap grid (u: 0 = left .. 4 = right, v: 0 = back .. 4 = front) boundary vertex on a torso ring."""
        if v == 4:
            return torso[row, (2 - u) % 16]
        if u == 0:
            return torso[row, 6 - v]
        if v == 0:
            return torso[row, 6 + u]
        return torso[row, 10 + v]

    # Shoulders' cap: the top ring closes around an 8-vertex neck base.
    neck_ring = []
    y0, zc, rx, rz = NECK_BASE
    for k in range(8):
        phi = math.radians(k * 45)
        y = y0 - 0.006 * math.cos(phi)
        neck_ring.append(cage.add((-rx * math.sin(phi), y, zc + rz * math.cos(phi)),
                                  {"Chest": 0.5, "Neck": 0.5}, p_neck=1, flesh=0.3, seam=k in (2, 6)))
    neck_grid = {(2, 3): 0, (1, 3): 1, (1, 2): 2, (1, 1): 3, (2, 1): 4, (3, 1): 5, (3, 2): 6, (3, 3): 7}
    top = rows - 1

    def top_vertex(u, v):
        if (u, v) in neck_grid:
            return neck_ring[neck_grid[u, v]]
        return boundary(top, u, v)

    for u in range(4):
        for v in range(4):
            if 1 <= u <= 2 and 1 <= v <= 2:
                continue
            cage.face(top_vertex(u, v), top_vertex(u + 1, v), top_vertex(u + 1, v + 1), top_vertex(u, v + 1))

    # Pelvis floor: front and back strips; the legs grow from the two 2x2 holes, sharing the crotch line.
    floor = {
        (1, 1): (-0.082, 0.815, -0.072), (2, 1): (0.0, 0.806, -0.050), (2, 2): (0.0, 0.790, -0.006),
        (2, 3): (0.0, 0.803, 0.036), (1, 3): (-0.076, 0.818, 0.062),
    }
    bottom = {}
    for (u, v), p in list(floor.items()):
        weights = {"Hips": 1.0} if u == 2 else {"Hips": 0.62, "UpperLeg.L": 0.38}
        bottom[u, v] = cage.add(p, weights, p_torso=1, flesh=1)
        if u == 1:
            bottom[3, v] = cage.add((-p[0], p[1], p[2]), {"Hips": 0.62, "UpperLeg.R": 0.38}, p_torso=1, flesh=1)

    def floor_vertex(u, v):
        if (u, v) in bottom:
            return bottom[u, v]
        return boundary(0, u, v)

    for u in range(4):
        for v in (0, 3):
            cage.face(floor_vertex(u, v), floor_vertex(u + 1, v), floor_vertex(u + 1, v + 1), floor_vertex(u, v + 1))
    for sign in (-1, 1):
        outer = 0 if sign < 0 else 4
        inner = 1 if sign < 0 else 3
        hole = [floor_vertex(inner, 3), floor_vertex(outer, 3), floor_vertex(outer, 2), floor_vertex(outer, 1),
                floor_vertex(inner, 1), floor_vertex(2, 1), floor_vertex(2, 2), floor_vertex(2, 3)]
        for corner in (outer,):
            for v in (1, 2, 3):
                w = cage.weights[floor_vertex(corner, v)]
                cage.weights[floor_vertex(corner, v)] = blend_weights(("Hips", 0.62), ("UpperLeg" + side_name(sign), 0.38))
        build_leg(cage, hole, sign)

    for sign in (-1, 1):
        cols = ARM_COLUMNS if sign < 0 else tuple((16 - j) % 16 for j in ARM_COLUMNS)
        r = ARMPIT_ROW
        hole = [torso[r + 2, cols[1]], torso[r + 2, cols[0]], torso[r + 1, cols[0]], torso[r, cols[0]],
                torso[r, cols[1]], torso[r, cols[2]], torso[r + 1, cols[2]], torso[r + 2, cols[2]]]
        build_arm(cage, hole, sign)

    build_neck_and_head(cage, neck_ring)
    return cage


def build_arm(cage, root, sign):
    side = side_name(sign)
    upper, lower, hand = "UpperArm" + side, "LowerArm" + side, "Hand" + side
    part = {"p_arm_l" if sign < 0 else "p_arm_r": 1}
    previous = root
    for station, (bone_up, bone_low, bone_hand) in zip(ARM, ARM_WEIGHTS):
        x, cy, cz, ry, rz, twist = station
        ring = []
        for k in range(8):
            phi = math.radians(k * 45 + twist)
            outside = -math.sin(phi)  # elbows bend forward: the back of the arm is the outside
            w_up, w_low = bone_up, bone_low
            if in_hinge("elbow", x):
                w_low = hinge_weight("elbow", x, outside)
                w_up = 1.0 - w_low
            ry_k = ry * (1.04 + (0.03 if station is ARM[0] and math.cos(phi) > 0.3 else 0.0))
            z = cz + rz * 1.04 * math.sin(phi) - hinge_bump("elbow", x, outside)
            ring.append(cage.add((sign * x, cy + ry_k * math.cos(phi), z),
                                 blend_weights((upper, w_up), (lower, w_low), (hand, bone_hand),
                                               ("Chest", 1 - w_up - w_low - bone_hand)), flesh=1, seam=k == 4, **part))
        cage.bridge(previous, ring)
        previous = ring
    build_hand(cage, previous, sign, part)


def build_hand(cage, wrist, sign, part):
    hand = "Hand" + side_name(sign)
    tags = dict(part, hand=1)
    rings = []
    for x, cy, cz, w, h in PALM:
        top = [np.array((sign * x, cy + h / 2, cz + w / 2 - i * w / 3)) for i in range(4)]
        bot = [np.array((sign * x, cy - h / 2, cz + w / 2 - i * w / 3)) for i in range(4)]
        for corners in (top, bot):  # round the palm's edges a little
            for i in (0, 3):
                corners[i][1] -= np.sign(corners[i][1] - cy) * h * 0.08
        T = [cage.add(p, {hand: 1}, **tags) for p in top]
        B = [cage.add(p, {hand: 1}, **tags) for p in bot]
        rings.append((T, B))

    def ordered(T, B):  # the twisted wrist ring's order
        return [T[1], T[0], B[0], B[1], B[2], B[3], T[3], T[2]]

    cage.bridge(wrist, ordered(*rings[0]))
    (T1, B1), (T2, B2), (T3, B3) = rings
    a, b = ordered(T1, B1), ordered(T2, B2)
    thumb_root = (T1[0], T2[0], B2[0], B1[0])
    for k in range(8):
        quad = (a[k], a[(k + 1) % 8], b[(k + 1) % 8], b[k])
        if set(quad) == set(thumb_root):
            continue
        cage.face(*quad)
    a, b = ordered(T2, B2), ordered(T3, B3)
    little_root = (T2[3], T3[3], B3[3], B2[3])
    for k in range(8):
        quad = (a[k], a[(k + 1) % 8], b[(k + 1) % 8], b[k])
        if set(quad) == set(little_root):
            continue
        cage.face(*quad)

    for i, name in enumerate(("index", "middle", "ring")):
        root = [T3[i], T3[i + 1], B3[i + 1], B3[i]]
        build_finger(cage, root, name, sign, hand, tags, side_hint=(0, 0, 1))
    build_finger(cage, list(little_root), "little", sign, hand, tags, side_hint=(0, 0, -1))
    build_finger(cage, list(thumb_root), "thumb", sign, hand, tags, side_hint=(-sign, 0, 0))


def build_finger(cage, root, name, sign, bone, tags, side_hint):
    (dx, dy, dz), length, half_w, half_h = FINGERS[name]
    root_centre = np.mean([cage.position(i) for i in root], axis=0)
    direction, up = frame((sign * dx, dy, dz), (0, 1, 0))
    side = np.asarray(side_hint, dtype=float)
    side = side - direction * side.dot(direction) - up * side.dot(up)
    side /= np.linalg.norm(side)
    previous = root
    for along, scale in FINGER_STATIONS:
        centre = root_centre + direction * (length * along) - up * (0.004 * along * along)
        u, s = up * half_h * scale, side * half_w * scale
        ring = [cage.add(p, {bone: 1}, **tags) for p in (centre + u + s, centre + u - s, centre - u - s, centre - u + s)]
        cage.bridge(previous, ring)
        previous = ring
    cage.face(*previous)


def build_leg(cage, root, sign):
    side = side_name(sign)
    upper, lower, foot = "UpperLeg" + side, "LowerLeg" + side, "Foot" + side
    part = {"p_leg_l" if sign < 0 else "p_leg_r": 1}
    previous = root
    for station, (bu, bl, bf) in zip(LEG, LEG_WEIGHTS):
        y, cx, cz, rf, rb, ro, ri = station
        ring = []
        for k in range(8):
            phi = math.radians(k * 45)
            c, s = math.cos(phi), math.sin(phi)  # knees bend backward: the front (c > 0) is the outside
            w_up, w_low = bu, bl
            if in_hinge("knee", y):
                w_low = hinge_weight("knee", y, c)
                w_up = 1.0 - w_low
            z = cz + (rf if c >= 0 else rb) * c * 1.04 + hinge_bump("knee", y, c)
            x = sign * (cx + (ro if s >= 0 else ri) * s * 1.04)
            ring.append(cage.add((x, y, z), blend_weights((upper, w_up), (lower, w_low), (foot, bf), ("Hips", 1 - w_up - w_low - bf)),
                                 flesh=1 if bf == 0 else 0.4, seam=k in (2, 6), **part))
        cage.bridge(previous, ring)
        previous = ring
    for centre, tilt, r_top, r_sole, r_out, r_in in FOOT:
        t = math.radians(tilt)
        top = np.array((0.0, math.sin(t), math.cos(t)))
        outer = np.array((float(sign), 0.0, 0.0))
        c0 = np.array((sign * centre[0], centre[1], centre[2]))
        ring = []
        for k in range(8):
            phi = math.radians(k * 45)
            c, s = math.cos(phi), math.sin(phi)
            p = c0 + top * ((r_top if c >= 0 else r_sole) * c) + outer * ((r_out if s >= 0 else r_in) * s)
            p[1] = max(p[1], 0.004)  # a flat sole on the floor
            ring.append(cage.add(p, {foot: 1}, flesh=0, **part))
        cage.bridge(previous, ring)
        previous = ring
    tip = cage.add((sign * TOE_TIP[0], TOE_TIP[1], TOE_TIP[2]), {foot: 1}, flesh=0, **part)
    for k in range(0, 8, 2):
        cage.face(previous[k], previous[k + 1], previous[(k + 2) % 8], tip)


def build_neck_and_head(cage, neck_base):
    previous = neck_base
    for i, (y, zc, rx, rz) in enumerate(NECK):
        weights = {"Neck": 1.0} if i == 0 else {"Neck": 0.7, "Head": 0.3}
        ring = []
        for k in range(8):
            phi = math.radians(k * 45)
            ring.append(cage.add((-rx * math.sin(phi), y - 0.004 * math.cos(phi), zc + rz * math.cos(phi)), weights, p_neck=1, flesh=0.3))
        cage.bridge(previous, ring)
        previous = ring
    build_head(cage, previous)


def cube_sphere_direction(p):
    """Even cube-to-sphere mapping of a point on the cube [-1, 1]^3."""
    x, y, z = p
    return np.array((x * math.sqrt(max(0.0, 1 - y * y / 2 - z * z / 2 + y * y * z * z / 3)),
                     y * math.sqrt(max(0.0, 1 - z * z / 2 - x * x / 2 + z * z * x * x / 3)),
                     z * math.sqrt(max(0.0, 1 - x * x / 2 - y * y / 2 + x * x * y * y / 3))))


def build_head(cage, neck_top):
    """A cube-sphere cage (HEAD_CAGE quads per cube face) with a 2x2 hole under the skull for the neck."""
    n = HEAD_CAGE
    h = n // 2
    centre = np.asarray(HEAD["centre"])
    origin = centre + np.asarray(HEAD["ray_origin"])
    hole_z = -1  # the neck sits under the back half of the skull
    hole_ring = [(0, 0), (-1, 0), (-1, -1), (-1, -2), (0, -2), (1, -2), (1, -1), (1, 0)]
    hole_ring = [(x, z + hole_z + 1) for x, z in hole_ring]  # lattice (x, z) on the bottom face

    lattice = {}
    for i in range(-h, h + 1):
        for j in range(-h, h + 1):
            for k in range(-h, h + 1):
                if max(abs(i), abs(j), abs(k)) == h:
                    lattice[i, j, k] = None
    keys = sorted(lattice)
    directions = np.array([cube_sphere_direction(np.array(key) / h) for key in keys])
    directions /= np.linalg.norm(directions, axis=1, keepdims=True)
    surface = ss.ray_surface(head_sdf, origin, directions)

    hole_keys = {(x, -h, z) for x, z in hole_ring}
    centre_key = (0, -h, hole_z)
    for key, point in zip(keys, surface):
        if key == centre_key:
            continue
        if key in hole_keys:
            index = hole_ring.index((key[0], key[2]))
            p = cage.position(neck_top[index]) + np.array((0.0, 0.016, 0.0))
            lattice[key] = cage.add(p, {"Head": 0.7, "Neck": 0.3}, p_head=1, project=0.0)
            continue
        near_hole = key[1] == -h and abs(key[0]) <= 2 and abs(key[2] - hole_z) <= 2
        lattice[key] = cage.add(point, {"Head": 1.0}, p_head=1, project=0.55 if near_hole else 1.0)
    cage.bridge(neck_top, [lattice[x, -h, z] for x, z in hole_ring])

    # Quads on each cube face, skipping the hole.
    hole_faces = {(x, z) for x in (-1, 0) for z in (hole_z - 1, hole_z)}
    for axis in range(3):
        for sign in (-1, 1):
            for a in range(-h, h):
                for b in range(-h, h):
                    def key(da, db):
                        coords = [0, 0, 0]
                        others = [ax for ax in range(3) if ax != axis]
                        coords[axis] = sign * h
                        coords[others[0]] = a + da
                        coords[others[1]] = b + db
                        return tuple(coords)
                    if axis == 1 and sign == -1 and (a, b) in hole_faces:
                        continue
                    cage.face(lattice[key(0, 0)], lattice[key(1, 0)], lattice[key(1, 1)], lattice[key(0, 1)])


# ---------------------------------------------------------------------------------------------------- the head shape

def head_sdf(p):
    c = np.asarray(HEAD["centre"])
    k_face, k_head, k_nose, k_plane = HEAD["blend"]
    cranium = ss.sd_ellipsoid(p, c + HEAD["cranium"][0], HEAD["cranium"][1])
    face = ss.sd_ellipsoid(p, c + HEAD["cheeks"][0], HEAD["cheeks"][1])
    (ja, ra, jb, rb) = HEAD["jaw"]
    for mirror in (-1, 1):
        a = c + np.array((mirror * ja[0], ja[1], ja[2]))
        b = c + np.array((mirror * jb[0], jb[1], jb[2]))
        face = ss.smooth_min(face, ss.sd_round_cone(p, a, b, ra, rb), k_face)
    face = ss.smooth_min(face, ss.sd_sphere(p, c + HEAD["chin"][0], HEAD["chin"][1]), k_face)
    head = ss.smooth_min(cranium, face, k_head)
    head = ss.smooth_max(head, p[:, 2] - (c[2] + HEAD["face_plane"]), k_plane)
    (na, nra, nb, nrb) = HEAD["nose"]
    return ss.smooth_min(head, ss.sd_round_cone(p, c + na, c + nb, nra, nrb), k_nose)


def head_proxy_normals(p):
    centre = np.asarray(HEAD["centre"]) + HEAD["proxy"][0]
    radii = np.asarray(HEAD["proxy"][1])
    g = (p - centre) / (radii * radii)
    return g / np.linalg.norm(g, axis=1, keepdims=True)


# ---------------------------------------------------------------------------------------------------- the body

def build_body(rig, name="Body"):
    """The welded, subdivided, skinned body with head; returns the object."""
    cage = build_cage()
    obj = ss.subdivide(cage.to_object(name))
    ct.bind_to_rig(obj, rig)
    finish_head(obj)
    smooth_weights(obj)
    paint_regions(obj)
    ss.add_shape_keys(obj, body_shapes(obj))
    set_normals(obj)
    return obj


def finish_head(obj):
    """Projects the subdivided head onto the parametric head surface (subdivision rounds it off a little)."""
    p = ss.unity_positions(obj)
    weight = ss.point_attribute(obj, "project")
    moving = weight > 1e-3
    origin = np.asarray(HEAD["centre"]) + np.asarray(HEAD["ray_origin"])
    d = p[moving] - origin
    d /= np.linalg.norm(d, axis=1, keepdims=True)
    target = ss.ray_surface(head_sdf, origin, d)
    p[moving] += (target - p[moving]) * weight[moving, None]
    ss.set_unity_positions(obj, p)


def smooth_weights(obj, iterations=3):
    table, names = ss.weight_table(obj)
    edges = ss.vertex_neighbours(obj)
    pinned = ss.point_attribute(obj, "hand") > 0.5  # fingers are rigid: no bleeding between them
    table = ss.laplacian(table, edges, iterations, 0.5, pinned=pinned)
    ss.set_weight_table(obj, table, names)


def part_of(obj):
    tags = np.stack([ss.point_attribute(obj, "p_" + p) for p in PARTS], axis=1)
    return np.array(PARTS)[np.argmax(tags, axis=1)]


def paint_regions(obj):
    """Body region IDs (vertex colour R) from each vertex's part and position; they match the stand-in's boundaries."""
    p = ss.unity_positions(obj)
    parts = part_of(obj)
    regions = []
    for (x, y, z), part in zip(p, parts):
        if part == "head":
            region = "Head"
        elif part == "neck":
            region = "Neck"
        elif part == "torso":
            region = "Pelvis" if y < 1.07 else "LowerTorso" if y < 1.24 else "UpperTorso"
        elif part.startswith("arm"):
            s = "L" if x < 0 else "R"
            region = ("UpperArm" if abs(x) < 0.44 else "Forearm" if abs(x) < 0.69 else "Hand") + s
        else:
            s = "L" if x < 0 else "R"
            region = ("Thigh" if y > 0.5 else "Calf" if y > 0.095 else "Foot") + s
        regions.append(ct.REGIONS[region] / 255.0)
    colours = np.zeros((len(p), 4))
    colours[:, 0] = regions
    ss.set_colour_attribute(obj, colours)


def body_shapes(obj):
    """body.weight, body.chest and body.hips as Unity-space offsets per vertex."""
    p = ss.unity_positions(obj)
    n = ss.unity_normals(obj)
    parts = part_of(obj)
    flesh = np.clip(ss.point_attribute(obj, "flesh"), 0, 1)
    radius = np.zeros(len(p))
    torso = (parts == "torso") | (parts == "neck")
    radius[torso] = np.hypot(p[torso, 0], (p[torso, 2] + 0.005) * 1.3)
    arms = np.char.startswith(parts.astype(str), "arm")
    radius[arms] = np.hypot(p[arms, 1] - 1.40, p[arms, 2])
    legs = np.char.startswith(parts.astype(str), "leg")
    radius[legs] = np.hypot(np.abs(p[legs, 0]) - 0.09, p[legs, 2])
    front = np.clip(n[:, 2], 0, 1)

    weight = n * (radius * 0.4 * flesh)[:, None]
    belly = (torso * front * front * np.exp(-0.5 * ((p[:, 1] - 1.06) / 0.08) ** 2) * (np.abs(p[:, 0]) < 0.14))
    weight += n * (0.022 * belly)[:, None]

    bust = np.zeros(len(p))
    for bx in (-0.082, 0.082):
        bust += np.exp(-0.5 * ((p[:, 0] - bx) / 0.05) ** 2) * np.exp(-0.5 * ((p[:, 1] - 1.285) / 0.05) ** 2)
    chest = n * (0.055 * np.clip(bust, 0, 1) * front * front * torso)[:, None]

    lateral = np.stack([n[:, 0], np.zeros(len(p)), np.zeros(len(p))], axis=1)
    hips_amount = torso * 0.05 * np.exp(-0.5 * ((p[:, 1] - 0.93) / 0.07) ** 2)
    hips_amount += legs * 0.04 * np.clip((p[:, 1] - 0.6) / 0.25, 0, 1)
    hips = lateral * hips_amount[:, None]
    return {"body.weight": weight, "body.chest": chest, "body.hips": hips}


def set_normals(obj):
    """Geometry normals on the body; on the head, normals from the ellipsoid proxy (smooth toon terminator)."""
    n = ss.unity_normals(obj)
    p = ss.unity_positions(obj)
    head = ss.point_attribute(obj, "p_head")
    blend = ss.smoothstep(0.2, 0.9, head)[:, None]
    proxy = head_proxy_normals(p)
    mixed = n * (1 - blend) + proxy * blend
    ss.set_custom_normals(obj, mixed / np.linalg.norm(mixed, axis=1, keepdims=True))
