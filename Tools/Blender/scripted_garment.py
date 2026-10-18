"""Scripted garments as shells cut from the body's own cage (art spike, decision 0009).

A garment takes the faces of the body cage it covers (so its edge loops and seam lines are the body's), eases them out
along the cage normals, adds bands at its open edges (hem, cuffs, collar, waistband) each with a turned-back facing
so the edge shows a thickness, and is subdivided once. Then it settles: a few rounds of smoothing along the normal
(which fills hollows the cloth would span, like under the bust) and pushing back out of the body to a clearance.
Seams are shallow grooves along the cage's seam lines; folds are small ripples where cloth bunches (elbows, waist,
the backs of the knees). Ease, band sizes, seam depth and folds are parameters.

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
        placket=0.013,          # half width of the trim strip down the front
        buttons=[1.40, 1.30, 1.20, 1.10],
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
        placket=0.0,
        buttons=[],
    ),
}


# ---------------------------------------------------------------------------------------------------- shell

def covered(garment, p, tags):
    """Whether a body cage vertex is under the garment."""
    x, y, z = p
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
    if garment["slot"] == "top":
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


# ---------------------------------------------------------------------------------------------------- the part

def build_garment(garment_id, body, rig):
    garment = GARMENTS[garment_id]
    cage = build_shell(garment_id)
    obj = ss.subdivide(cage.to_object(garment_id))
    obj.data.shade_smooth()
    face_outward(obj)
    settle(obj, body, garment)
    add_folds_and_seams(obj, garment)
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
