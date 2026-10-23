"""Scripted garments as shells cut from the body's own cage (art spike, decision 0010).

A garment takes the faces of the body cage it covers (so its edge loops and seam lines are the body's), eases them out
along the cage normals, adds bands at its open edges (hem, cuffs, collar, waistband) each with a turned-back facing
so the edge shows a thickness, and is subdivided once. Then it settles: a few rounds of smoothing along the normal
(which fills hollows the cloth would span, like under the bust) and pushing back out of the body to a clearance.
Seams are shallow grooves along the cage's seam lines; folds are small ripples where cloth bunches (elbows, waist,
the backs of the knees), and the fold shapes that actually read are painted into the detail map (painted_folds, drawn
by scripted_textures). Ease, band sizes, seam depth and folds are parameters. Shoes are the same tool on the foot:
the cage below the ankle, a padded collar, then a flat flared sole with toe spring (shape_sole).

Skin weights and body shape keys come from character_tools.transfer_from_body, and the part is validated like any
artist's part. Unity terms throughout (Y up, +Z forward, character's left at -X).
"""

import math

import bmesh
import bpy
import numpy as np

import character_tools as ct
import scripted_body as sb
import scripted_hair as sh
import scripted_shapes as ss

# Each garment: which cage vertices it covers, its ease off the body, its open edges, seams and folds.
GARMENTS = {
    "cloth.top.jacket_s1": dict(
        slot="top",
        covers=["UpperTorso", "LowerTorso", "UpperArmL", "UpperArmR", "ForearmL", "ForearmR"],
        hem_y=0.99,             # the body rings at or above this height are covered...
        sleeve_to=0.69,         # ...and the arms out to here (the wrist)
        ease=0.018, sleeve_ease=0.013, clearance=0.014,
        settle=8,               # rounds of smoothing and pushing out
        bands=dict(
            hem=dict(length=0.045, flare=0.010, depth=0.022),    # band below the cut edge, its flare and facing depth
            cuff=dict(length=0.035, flare=0.006, depth=0.022),
            collar=dict(length=0.042, flare=-0.007, depth=0.030),  # a stand collar leans in
        ),
        thickness=0.004,
        seam_depth=0.0012,
        folds=[  # where, along which axis, how many ripples, amplitude, and which side of the limb
            dict(kind="elbow", centre=0.44, span=0.07, count=3, amplitude=0.0035),
            dict(kind="waist", centre=1.10, span=0.08, count=3, amplitude=0.0028),
        ],
        wide_occlusion=0.08,    # reach of the lightmap's wider occlusion bake: arm against torso, jaw over collar
        fold_shadows=[  # painted fold shapes in the lightmap (scripted_textures.fold_shadows), both sides
            # Pull folds from the armpit: down the sleeve to the front and back of the elbow, and onto the chest.
            dict(frame="sleeve", start=(180, 0.205), end=(105, 0.36), bend=0.010, width=0.036, peak=0.2, depth=0.26),
            dict(frame="sleeve", start=(185, 0.205), end=(250, 0.34), bend=-0.008, width=0.032, peak=0.2, depth=0.24),
            dict(frame="sleeve", start=(165, 0.215), end=(140, 0.29), width=0.022, peak=0.3, depth=0.20),
            dict(frame="torso", start=(72, 1.335), end=(40, 1.25), bend=0.008, width=0.032, peak=0.15, depth=0.24),
            dict(frame="torso", start=(108, 1.335), end=(140, 1.24), bend=-0.008, width=0.032, peak=0.15, depth=0.22),
            dict(frame="torso", start=(90, 1.34), end=(86, 1.19), width=0.036, peak=0.1, depth=0.26),
            # Bunching inside the elbow, and above the cuff.
            dict(frame="sleeve", start=(70, 0.418), end=(165, 0.432), bend=0.006, width=0.018, peak=0.4, depth=0.28),
            dict(frame="sleeve", start=(85, 0.447), end=(180, 0.452), bend=0.007, width=0.020, peak=0.35, depth=0.30),
            dict(frame="sleeve", start=(100, 0.474), end=(160, 0.490), bend=0.005, width=0.016, peak=0.4, depth=0.24),
            dict(frame="sleeve", start=(120, 0.500), end=(95, 0.600), width=0.018, peak=0.2, depth=0.18),
            dict(frame="sleeve", start=(110, 0.625), end=(170, 0.640), bend=0.004, width=0.016, peak=0.4, depth=0.20),
            # Under the bust, and the waist pinching in at the sides.
            dict(frame="torso", start=(15, 1.205), end=(62, 1.225), bend=-0.010, width=0.026, peak=0.5, depth=0.15),
            dict(frame="torso", start=(68, 1.135), end=(100, 1.115), bend=-0.006, width=0.020, peak=0.6, depth=0.26),
            dict(frame="torso", start=(74, 1.095), end=(108, 1.080), bend=-0.005, width=0.021, peak=0.6, depth=0.26),
            dict(frame="torso", start=(80, 1.060), end=(104, 1.050), width=0.016, peak=0.5, depth=0.20),
        ],
        placket=0.013,          # half width of the trim strip down the front
        buttons=[1.40, 1.30, 1.20, 1.10],
        # Fold and shadow shapes painted into the detail map (scripted_textures.painted_folds): strokes as (from, to,
        # half width) in metres; torso strokes in (|x|, y) on the front or back, sleeve strokes in (out along the arm,
        # round it from the top toward the front). Geometry folds this shallow don't show under flat toon shading.
        painted_folds=dict(
            depth=0.38, fall=0.10, fall_from=(1.0, 1.36), arm_radius=0.045, asymmetry=0.006,
            front=[
                ((0.145, 1.345), (0.085, 1.245), 0.0050),   # drag from the armpit toward the waist
                ((0.140, 1.300), (0.100, 1.215), 0.0034),
                ((0.128, 1.135), (0.088, 1.120), 0.0030),   # bunching at the sides of the waist
                ((0.130, 1.100), (0.096, 1.092), 0.0026),
                ((0.060, 1.000), (0.052, 1.050), 0.0030),   # hem folds
                ((0.112, 1.000), (0.118, 1.040), 0.0026),
            ],
            back=[
                ((0.135, 1.370), (0.055, 1.270), 0.0045),   # drag between the shoulder blades
                ((0.120, 1.150), (0.040, 1.125), 0.0032),   # the waist at the back
                ((0.090, 1.000), (0.080, 1.050), 0.0028),
            ],
            sleeve=[
                ((0.428, 0.030), (0.448, 0.108), 0.0042),   # bunching inside the elbow
                ((0.458, 0.040), (0.472, 0.100), 0.0034),
                ((0.405, 0.050), (0.418, 0.092), 0.0028),
                ((0.505, 0.075), (0.575, 0.020), 0.0026),   # drag down the forearm
                ((0.215, 0.125), (0.300, 0.150), 0.0036),   # under the arm toward the armpit
                ((0.628, 0.030), (0.640, 0.075), 0.0025),   # bunching above the cuffs
                ((0.640, -0.060), (0.655, -0.020), 0.0022),
            ],
        ),
    ),
    "cloth.bottom.shorts_s1": dict(
        slot="bottom",
        covers=["Pelvis"],     # the legs end above the knee, so the thigh regions stay drawn
        top_y=1.09,             # body rings at or below this height (and above the legs' cut) are covered
        leg_to=0.60,
        ease=0.009, sleeve_ease=0.016, clearance=0.006,
        settle=6,
        bands=dict(
            waist=dict(length=0.03, flare=0.0, depth=0.025),
            leg=dict(length=0.03, flare=0.008, depth=0.02),
        ),
        thickness=0.004,
        seam_depth=0.0012,
        folds=[dict(kind="crotch", centre=0.80, span=0.06, count=2, amplitude=0.0025)],
        wide_occlusion=0.05,
        fold_shadows=[  # short pull folds out from the crotch toward the hips, front and back
            dict(frame="torso", start=(10, 0.858), end=(40, 0.885), bend=0.004, width=0.014, peak=0.25, depth=0.15),
            dict(frame="torso", start=(170, 0.858), end=(142, 0.882), bend=-0.004, width=0.014, peak=0.25, depth=0.14),
        ],
        placket=0.0,
        buttons=[],
        painted_folds=dict(
            depth=0.30, fall=0.0, fall_from=(0.6, 1.0), arm_radius=0.045, asymmetry=0.005,
            front=[
                ((0.020, 0.805), (0.072, 0.850), 0.0030),   # whiskers from the crotch
                ((0.025, 0.785), (0.085, 0.812), 0.0024),
                ((0.130, 1.000), (0.120, 0.930), 0.0026),   # over the hip
            ],
            back=[
                ((0.030, 0.800), (0.105, 0.785), 0.0034),   # under the seat
                ((0.040, 0.770), (0.100, 0.740), 0.0026),
            ],
            sleeve=[],
        ),
    ),
    "cloth.shoes.sneakers_s1": dict(
        slot="shoes",
        covers=["FootL", "FootR"],
        ankle_y=0.12,           # leg rings at or below this height (the ankle and the foot) are covered
        ease=0.012, sleeve_ease=0.012, clearance=0.008,
        settle=4,
        bands=dict(collar=dict(length=0.016, flare=0.003, depth=0.014)),  # a padded collar round the ankle
        thickness=0.005,
        seam_depth=0.0008,
        folds=[],
        wide_occlusion=None,    # no wider bake: nothing hangs over a shoe
        fold_shadows=[],
        placket=0.0,
        buttons=[],
        # A flat, slightly flared sole on the floor with the toe sprung up; a toe cap; laces across the instep; a
        # stripe down the outer side. Heights and positions in metres, Unity terms, for the left foot (mirrored).
        sole=dict(height=0.026, flare=0.007, toe_spring=0.010, toe_from=0.11, toe_to=0.19),
        toe_cap=0.135,          # forward of this the upper is the accent colour's toe cap
        laces=dict(rows=[0.040, 0.058, 0.076, 0.094], half_width=0.015, thickness=0.0026, centre=0.088),
        stripe=dict(start=(0.000, 0.030), end=(0.085, 0.070), half_width=0.007),  # (z, y) on the outer side
    ),
}


# ---------------------------------------------------------------------------------------------------- shell

def covered(garment, p, tags):
    """Whether a body cage vertex is under the garment."""
    x, y, z = p
    if garment["slot"] == "shoes":
        return (tags["p_leg_l"] > 0.5 or tags["p_leg_r"] > 0.5) and y <= garment["ankle_y"]
    if garment["slot"] == "top":
        if tags["p_torso"] > 0.5:
            return y >= garment["hem_y"]
        if tags["p_arm_l"] > 0.5 or tags["p_arm_r"] > 0.5:
            return tags["hand"] < 0.5 and abs(x) <= garment["sleeve_to"]
        return tags["p_neck"] > 0.5 and y < 1.5
    if tags["p_torso"] > 0.5:
        return y <= garment["top_y"]
    if tags["p_leg_l"] > 0.5 or tags["p_leg_r"] > 0.5:
        return y >= garment["leg_to"]
    return False


def build_shell(garment_id):
    """The garment cage: the covered faces of the body cage, eased out, with banded edges; returns a Cage."""
    garment = GARMENTS[garment_id]
    body_cage = sb.build_cage()
    points = np.array(body_cage.points)
    tags = [dict(zip(body_cage.tag_names, t)) for t in body_cage.tags]
    normals = cage_normals(body_cage)
    keep = [covered(garment, points[i], tags[i]) for i in range(len(points))]
    faces = [f for f in body_cage.faces if all(keep[i] for i in f)]
    used = sorted({i for f in faces for i in f})
    remap = {old: new for new, old in enumerate(used)}

    cage = ss.Cage(["seam", "trim", "lining", "sleeve", "ease"])
    for old in used:
        t = tags[old]
        arm = t["p_arm_l"] > 0.5 or t["p_arm_r"] > 0.5 or t["p_leg_l"] > 0.5 or t["p_leg_r"] > 0.5
        ease = garment["sleeve_ease"] if arm else garment["ease"]
        cage.add(points[old] + normals[old] * ease, {}, seam=t["seam"], sleeve=1.0 if arm else 0.0, ease=ease)
    for f in faces:
        cage.face(*[remap[i] for i in f])

    for loop in boundary_loops(cage):
        add_band(cage, loop, garment)
    return cage


def cage_normals(body_cage):
    """Outward vertex normals of the body cage (Blender makes the winding consistent)."""
    obj = body_cage.to_object("_normals")
    normals = ss.unity_normals(obj)
    bpy.data.objects.remove(obj)
    return normals


def boundary_loops(cage):
    """Closed loops of boundary edges, each ordered."""
    count = {}
    for f in cage.faces:
        for k in range(len(f)):
            edge = tuple(sorted((f[k], f[(k + 1) % len(f)])))
            count[edge] = count.get(edge, 0) + 1
    edges = [e for e, c in count.items() if c == 1]
    neighbours = {}
    for a, b in edges:
        neighbours.setdefault(a, []).append(b)
        neighbours.setdefault(b, []).append(a)
    loops, seen = [], set()
    for start in sorted(neighbours):
        if start in seen:
            continue
        loop, previous, current = [start], None, start
        seen.add(start)
        while True:
            options = [n for n in neighbours[current] if n != previous]
            nxt = options[0]
            if nxt == start:
                break
            loop.append(nxt)
            seen.add(nxt)
            previous, current = current, nxt
        loops.append(loop)
    return loops


def add_band(cage, loop, garment):
    """A band past an open edge (hem, cuff, collar or waistband), then a facing turned back inside it."""
    p = np.array([cage.position(i) for i in loop])
    centre = p.mean(axis=0)
    # Which way the edge opens: along the limb for cuffs and leg hems, up for a collar, down for a hem, up for a waist.
    if garment["slot"] == "shoes":
        kind, out = "collar", np.array((0.0, 1.0, 0.0))
    elif garment["slot"] == "top":
        if abs(centre[0]) > 0.3:
            kind, out = "cuff", np.array((math.copysign(1.0, centre[0]), 0.0, 0.0))
        elif centre[1] > 1.4:
            kind, out = "collar", np.array((0.0, 1.0, 0.0))
        else:
            kind, out = "hem", np.array((0.0, -1.0, 0.0))
    else:
        if centre[1] > 1.0:
            kind, out = "waist", np.array((0.0, 1.0, 0.0))
        else:
            kind, out = "leg", np.array((0.0, -1.0, 0.0))
    band = garment["bands"][kind]
    radial = p - centre
    radial -= np.outer(radial @ out, out)
    radial /= np.maximum(np.linalg.norm(radial, axis=1, keepdims=True), 1e-9)
    thickness = garment["thickness"]

    def ring(offset_along, offset_out, **tags):
        return [cage.add(p[k] + out * offset_along + radial[k] * offset_out, {}, **tags) for k in range(len(loop))]

    length, flare, depth = band["length"], band["flare"], band["depth"]
    seam_ring = ring(length * 0.08, 0.0, trim=1, seam=1)           # the stitch line where the band starts
    edge = ring(length, flare, trim=1)                               # the folded edge
    lip = ring(length + thickness * 0.5, flare - thickness * 0.8, trim=1, lining=1)
    facing = ring(length - depth, -thickness, lining=1)              # the facing, back up inside
    inner = ring(-depth * 0.4, -thickness * 1.2, lining=1)
    sequence = [loop, seam_ring, edge, lip, facing, inner]
    for a, b in zip(sequence, sequence[1:]):
        cage.bridge(a, b)


# ---------------------------------------------------------------------------------------------------- settling

def settle(obj, body, garment):
    """Spans hollows and keeps clear of the body: smoothing along the normal, then pushing out, a few times over."""
    collider = sh.Collider(body)
    edges = ss.vertex_neighbours(obj)
    lining = ss.point_attribute(obj, "lining") > 0.5
    clearance = garment["clearance"]
    for _ in range(garment["settle"]):
        p = ss.unity_positions(obj)
        n = ss.unity_normals(obj)
        smoothed = ss.laplacian(p, edges, 1, 1.0)
        step = np.einsum("ij,ij->i", smoothed - p, n)
        p = p + n * np.clip(step, 0.0, 0.004)[:, None] * 0.8   # only outward: fill hollows, never shrink onto the body
        for i in range(len(p)):
            need = clearance * (0.5 if lining[i] else 1.0)
            p[i], _ = collider.push_out(p[i], need)
        ss.set_unity_positions(obj, p)
        obj.data.update()


def face_outward(obj):
    """Open shells can come out inside-out: flip them if most normals point toward the body's axis."""
    p = ss.unity_positions(obj)
    n = ss.unity_normals(obj)
    axis = np.stack([np.where(np.abs(p[:, 0]) > 0.2, p[:, 0], np.where(p[:, 1] < 0.84, np.sign(p[:, 0]) * 0.088, 0.0)),
                     np.where(np.abs(p[:, 0]) > 0.2, 1.40, p[:, 1]), np.zeros(len(p))], axis=1)
    if np.mean(np.einsum("ij,ij->i", n, p - axis)) < 0:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()


def add_folds_and_seams(obj, garment):
    p = ss.unity_positions(obj)
    n = ss.unity_normals(obj)
    seam = ss.point_attribute(obj, "seam")
    trim = ss.point_attribute(obj, "trim")
    sleeve = ss.point_attribute(obj, "sleeve")
    offset = np.zeros(len(p))
    for fold in garment["folds"]:
        if fold["kind"] == "elbow":
            along = np.abs(p[:, 0])
            # Inside the elbow (front and underneath) the sleeve bunches.
            facing = np.clip(np.einsum("ij,j->i", n, (0.0, -0.5, 0.85)), 0, 1)
            mask = sleeve * facing
        elif fold["kind"] == "waist":
            along = p[:, 1]
            mask = (1 - sleeve) * np.clip(np.abs(n[:, 0]) * 1.4 - 0.3, 0, 1)  # at the sides
        else:
            along = p[:, 1]
            mask = np.clip(np.abs(n[:, 0]) * 1.2, 0, 1) * (1 - sleeve)
        u = (along - fold["centre"]) / fold["span"]
        window = np.clip(1 - u * u, 0, 1) ** 2
        offset += fold["amplitude"] * np.sin(u * math.pi * fold["count"]) * window * mask * (1 - trim)
    groove = garment["seam_depth"] * ss.smoothstep(0.62, 0.9, seam)
    p = p + n * (offset - groove)[:, None]
    ss.set_unity_positions(obj, p)


def shape_sole(obj, sole):
    """A shoe's sole: flat on the floor, flared out round its edge, with the toe sprung up off the floor."""
    p = ss.unity_positions(obj)
    low = 1.0 - ss.smoothstep(0.0, sole["height"], p[:, 1])
    centre_x = np.sign(p[:, 0]) * 0.088
    p[:, 0] += np.sign(p[:, 0] - centre_x) * sole["flare"] * low
    p[:, 1] = np.maximum(p[:, 1], 0.0)
    p[:, 1] += sole["toe_spring"] * ss.smoothstep(sole["toe_from"], sole["toe_to"], p[:, 2]) ** 2
    ss.set_unity_positions(obj, p)


# ---------------------------------------------------------------------------------------------------- the part

def build_garment(garment_id, body, rig):
    garment = GARMENTS[garment_id]
    cage = build_shell(garment_id)
    obj = ss.subdivide(cage.to_object(garment_id))
    obj.data.shade_smooth()
    face_outward(obj)
    settle(obj, body, garment)
    add_folds_and_seams(obj, garment)
    if "sole" in garment:
        shape_sole(obj, garment["sole"])
    paint_garment_colour(obj)
    ct.bind_to_rig(obj, rig)
    ct.transfer_from_body(obj, body)
    ss.rest_shape_keys(obj)
    return obj


def paint_garment_colour(obj):
    """Vertex colours: no body region; recolour region 1 (the mask texture adds trim, lining and buttons)."""
    colours = np.zeros((len(obj.data.vertices), 4))
    colours[:, 1] = 1.0
    ss.set_colour_attribute(obj, colours)


def sidecar(garment_id):
    garment = GARMENTS[garment_id]
    return {"kind": "part", "slot": garment["slot"], "covers": garment["covers"], "followsBodyShapes": True}
