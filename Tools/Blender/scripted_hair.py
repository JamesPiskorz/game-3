"""Scripted hair from guide curves (art spike, decision 0009).

Each clump is grown as a guide curve: it starts under the scalp, follows its comb direction over the skull (pushed out
of the head and body by collision, so it lies on them), and gravity takes over as it leaves the head. A tapered,
lens-shaped ribbon is swept along the curve, with its tip curled. Count, width, taper and curl are parameters of each
group of clumps. A cap over the scalp hides the gaps between clumps.

Spring chains come from the curves: each chain is the average of the hanging parts of the clumps assigned to it, and
each clump vertex is weighted along its own curve to that chain's bones (blended between neighbouring chains by where
the clump sits round the head). Bones follow the part convention: Hair.<Name>.<n>, hanging from Head.

As with the stand-ins, hair is split into a back piece (cap, crown and length) and a front piece (bangs and the side
locks that frame the face). Unity terms throughout (Y up, +Z forward, character's left at -X).
"""

import math

import bmesh
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

import character_tools as ct
import scripted_body as sb
import scripted_shapes as ss

HEAD_CENTRE = np.asarray(sb.HEAD["centre"])

# Clump groups. Azimuth is round the head from the back (0) toward the character's left (+); elevation is the root's
# angle above the head centre. Each group: how many clumps, where their roots sit, how much the comb leans down (0 =
# straight away from the crown, 1 = straight down the scalp), how long, how wide (before the root narrows it), taper
# exponent past the taper start, thickness, curl at the tip (degrees, + curls toward the body), and how far off the
# head they lie (layer offset, metres; outer layers further out).
BACK_GROUPS = [
    # Outer layer: from round the crown's whorl, over the back of the head, to the middle of the back.
    dict(name="outer", count=16, azimuth=(-124, 124), elevation=(70, 78), down=0.25, length=(0.72, 0.80),
         width=0.052, taper=1.0, thickness=0.010, curl=26, offset=0.018, gravity=7.0, stiff=0.10, sides=6, rows=24, seed=11),
    # Inner layer, staggered and a little shorter, so the outer clumps never show a gap.
    dict(name="inner", count=13, azimuth=(-112, 112), elevation=(26, 38), down=0.5, length=(0.50, 0.56),
         width=0.046, taper=0.9, thickness=0.009, curl=16, offset=0.011, gravity=9.0, stiff=0.05, sides=6, rows=20, seed=23),
]
TAPER_START = 0.45  # clumps keep their width to here, then taper to a point
FRONT_GROUPS = [
    # Bangs swept toward the character's left from a parting right of centre, ending around the brows.
    dict(name="bangs", count=11, parting=0.028, spread=(-0.08, 0.075), length=(0.11, 0.135), width=0.026, taper=0.9,
         thickness=0.008, curl=12, offset=0.015, sweep=0.45, sides=6, rows=12, seed=41),
    # Side locks: two per side, from the temples down past the jaw to the chest.
    dict(name="locks", per_side=2, length=(0.30, 0.25), width=0.032, taper=1.1, thickness=0.009, curl=22, offset=0.012,
         sides=6, rows=22, seed=53),
]
# Spring chains: name, azimuth of the clumps it carries, bones.
BACK_CHAINS = [("Hair.Back.R", -60.0, 3), ("Hair.Back.C", 0.0, 3), ("Hair.Back.L", 60.0, 3)]
LOCK_CHAINS = {-1: ("Hair.SideLock.L", 3), 1: ("Hair.SideLock.R", 3)}
HANG_Y = 1.585      # below this a clump hangs free of the skull and rides its chain
FREE_GRAVITY = 32.0  # how hard gravity turns a clump once it hangs free (per metre)
FREE_INWARD = 1.5    # and how much it is drawn toward the body axis
BODY_AXIS_Z = -0.03
CAP_OFFSET = 0.010  # the scalp cap's distance off the head
# Hairline height above the head centre by angle round the head from the front (degrees): the cap ends here.
HAIRLINE = [(0, 0.083), (40, 0.070), (65, 0.040), (95, 0.005), (125, -0.045), (150, -0.075), (180, -0.085)]


# ---------------------------------------------------------------------------------------------------- collision

class Collider:
    """
    Pushes points out of the body (head included) to a given clearance, using the body mesh at rest. The arms can be
    left out: the rest pose is a T-pose, and hair grown against outstretched arms would splay out along them.
    """

    def __init__(self, body, with_arms=True):
        bm = bmesh.new()
        bm.from_mesh(body.data)
        if not with_arms:
            arm = np.maximum(ss.point_attribute(body, "p_arm_l"), ss.point_attribute(body, "p_arm_r")) > 0.5
            bm.verts.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(arm[v.index] for v in f.verts)], context="FACES")
        self.tree = BVHTree.FromBMesh(bm)
        bm.free()

    def nearest(self, p):
        location, normal, _, distance = self.tree.find_nearest(ct.unity_to_blender(tuple(p)))
        q = np.array((-location.x, location.z, -location.y))
        n = np.array((-normal.x, normal.z, -normal.y))
        inside = np.dot(p - q, n) < 0
        return q, n, (-distance if inside else distance)

    def push_out(self, p, clearance):
        q, n, d = self.nearest(p)
        if d < clearance:
            p = q + n * clearance
        return p, n


# ---------------------------------------------------------------------------------------------------- guide curves

def grow(collider, root, comb, length, clearance, gravity, stiff, curl, rows, drift=(0.0, 0.0, 0.0)):
    """A guide curve from root: combed, then pulled down by gravity, kept clear of the body. Returns points."""
    steps = rows * 4
    ds = length / steps
    p = np.asarray(root, dtype=float)
    d = np.asarray(comb, dtype=float)
    _, normal, _ = collider.nearest(p)
    d = d - normal * np.dot(d, normal)  # combed along the scalp, not into it
    d /= np.linalg.norm(d)
    points = [p.copy()]
    for step in range(steps):
        s = (step + 1) * ds
        _, _, distance = collider.nearest(p)
        if distance < clearance + 0.008:
            # Lying on the head or body: combed, with gravity coming in past the stiff root.
            pull = gravity * ss.smoothstep(0.0, stiff, s) * ds
            inward = np.zeros(3)
        else:
            # Hanging free: hair drapes nearly straight down, drawn in a little toward the body.
            pull = FREE_GRAVITY * ds
            inward = -np.array((p[0], 0.0, p[2] - BODY_AXIS_Z))
            inward *= FREE_INWARD * ds / max(np.linalg.norm(inward), 1e-9)
        d = d + np.array((0.0, -1.0, 0.0)) * pull + inward + np.asarray(drift) * ds
        d /= np.linalg.norm(d)
        # The root starts inside the cap and comes out through it over the first few centimetres.
        reach = clearance * (0.1 + 0.9 * ss.smoothstep(0.0, 0.05, s))
        moved, _ = collider.push_out(p + d * ds, reach)
        d = moved - p
        d /= max(np.linalg.norm(d), 1e-9)
        p = moved
        points.append(p.copy())
    points = np.array(points)
    if curl:
        points = curl_tip(points, collider, math.radians(curl))
    return resample(points, rows)


def curl_tip(points, collider, angle, portion=0.35):
    """Bends the last part of the curve about its side axis, more toward the tip (positive: toward the body)."""
    n = len(points)
    start = int(n * (1 - portion))
    out = points.copy()
    for i in range(start + 1, n):
        t = (i - start) / (n - 1 - start)
        pivot = out[i - 1]
        segment = points[i] - points[i - 1]
        _, normal, _ = collider.nearest(pivot)
        tangent = segment / max(np.linalg.norm(segment), 1e-9)
        side = np.cross(tangent, normal)
        side /= max(np.linalg.norm(side), 1e-9)
        # Turning about the side axis by -angle swings the tangent toward the body; the turn grows to angle at the tip.
        out[i] = pivot + rotate(segment, side, -angle * t * t)
    return out


def rotate(v, axis, angle):
    """Rodrigues rotation of v about a unit axis."""
    c, s = math.cos(angle), math.sin(angle)
    return v * c + np.cross(axis, v) * s + axis * np.dot(axis, v) * (1 - c)


def resample(points, rows):
    """rows + 1 points evenly spaced along the curve."""
    lengths = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))])
    targets = np.linspace(0.0, lengths[-1], rows + 1)
    return np.stack([np.interp(targets, lengths, points[:, k]) for k in range(3)], axis=1)


def head_point(azimuth_deg, elevation_deg, radius=0.2):
    """A point on the scalp (on the head surface) by angle round from the back and elevation above the centre."""
    a, e = math.radians(azimuth_deg), math.radians(elevation_deg)
    direction = np.array((-math.sin(a) * math.cos(e), math.sin(e), -math.cos(a) * math.cos(e)))
    origin = HEAD_CENTRE + np.array((0.0, 0.0, -0.01))
    return ss.ray_surface(sb.head_sdf, origin, direction[None, :])[0], direction


# ---------------------------------------------------------------------------------------------------- clump meshes

class HairMesh:
    """Clump ribbons and their per-vertex data, turned into one mesh with UVs per clump."""

    def __init__(self):
        self.positions, self.uvs_per_loop, self.faces = [], [], []
        self.attributes = {"hair_v": [], "hair_outer": [], "hair_edge": [], "clump": []}
        self.arc = []        # arc length from the root, per vertex
        self.clump_of = []   # clump index, per vertex
        self.curves = []     # per clump: points, hang index, azimuth, group name, side

    def vertex(self, p, v, outer, edge, clump, arc):
        self.positions.append(np.asarray(p))
        self.attributes["hair_v"].append(v)
        self.attributes["hair_outer"].append(outer)
        self.attributes["hair_edge"].append(edge)
        self.attributes["clump"].append(clump)
        self.arc.append(arc)
        self.clump_of.append(len(self.curves))
        return len(self.positions) - 1

    def add_clump(self, collider, points, width, taper, thickness, sides, azimuth, group, side=0, twist=0.0):
        rows = len(points) - 1
        clump_id = len(self.curves)
        tag = ss.hash01(clump_id, 7)
        arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))])
        length = arc[-1]
        normal_prev = None
        rings = []
        for i, c in enumerate(points):
            t = i / rows
            tangent = points[min(i + 1, rows)] - points[max(i - 1, 0)]
            tangent /= np.linalg.norm(tangent)
            q, n, d = collider.nearest(c)
            out = c - q if d > 1e-4 else n
            out = out / max(np.linalg.norm(out), 1e-9)
            if normal_prev is not None and d > 0.06:
                out = normal_prev  # far from the body: carry the frame along
            out = out - tangent * np.dot(out, tangent)
            out /= np.linalg.norm(out)
            normal_prev = out
            bi = np.cross(tangent, out)
            angle = twist * t
            out, bi = out * math.cos(angle) + bi * math.sin(angle), bi * math.cos(angle) - out * math.sin(angle)
            root = 0.4 + 0.6 * ss.smoothstep(0.0, 0.12, t)  # narrow where it leaves the scalp
            tip = 1.0 if t < TAPER_START else (1.0 - (t - TAPER_START) / (1.0 - TAPER_START)) ** taper
            w = width * root * tip * (1.0 + 0.15 * math.sin(math.pi * min(t / TAPER_START, 1.0)))
            # Thin where it lies on the scalp, fuller where it hangs.
            th = thickness * (0.35 + 0.65 * ss.smoothstep(0.12, 0.4, t)) * tip ** 0.6 + 0.0006
            if i == rows:
                rings.append([self.vertex(c, 1.0, 1.0, 0.5, tag, arc[i])])
                continue
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides
                ca, sa = math.cos(a), math.sin(a)
                lift = th * sa * (1.0 if sa >= 0 else 0.35)
                ring.append(self.vertex(c + bi * (w * ca) + out * lift, t, max(sa, 0.0), abs(ca), tag, arc[i]))
            rings.append(ring)
        # Faces: quads along the clump, a fan to the tip, a cap at the root (hidden in the scalp).
        perimeter = 2 * width + 0.01
        for i in range(rows):
            a, b = rings[i], rings[i + 1]
            for k in range(sides):
                k2 = (k + 1) % sides
                u0, u1 = k / sides * perimeter, (k + 1) / sides * perimeter
                v0, v1 = arc[i], arc[i + 1]
                if len(b) == 1:
                    self.faces.append(((a[k], a[k2], b[0]), ((u0, v0), (u1, v0), ((u0 + u1) / 2, v1))))
                else:
                    self.faces.append(((a[k], a[k2], b[k2], b[k]), ((u0, v0), (u1, v0), (u1, v1), (u0, v1))))
        root = rings[0]
        centre = self.vertex(points[0], 0.0, 0.0, 0.0, tag, 0.0)
        for k in range(sides):
            k2 = (k + 1) % sides
            self.faces.append(((root[k2], root[k], centre), ((0.0, -0.002), (0.004, -0.002), (0.002, -0.006))))
        hang = next((i for i, p in enumerate(points) if p[1] < HANG_Y), None)
        self.curves.append(dict(points=points, arc=arc, hang=hang, azimuth=azimuth, group=group, side=side, length=length))

    def to_object(self, name):
        bm = bmesh.new()
        verts = [bm.verts.new(ct.unity_to_blender(tuple(p))) for p in self.positions]
        uv_layer = bm.loops.layers.uv.new("UVMap")
        for indices, uvs in self.faces:
            face = bm.faces.new([verts[i] for i in indices])
            for loop, uv in zip(face.loops, uvs):
                loop[uv_layer].uv = uv
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        mesh.shade_smooth()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        for key, values in self.attributes.items():
            ss.set_point_attribute(obj, key, values)
        return obj


# ---------------------------------------------------------------------------------------------------- the pieces

def build_back(collider):
    hair = HairMesh()
    for group in BACK_GROUPS:
        count = group["count"]
        for i in range(count):
            r = ss.hash01(group["seed"], i)
            f = (i + 0.5) / count
            azimuth = group["azimuth"][0] + (group["azimuth"][1] - group["azimuth"][0]) * f + (r - 0.5) * 6
            elevation = group["elevation"][0] + (group["elevation"][1] - group["elevation"][0]) * ss.hash01(group["seed"], i, 2)
            root, direction = head_point(azimuth, elevation)
            # Combed away from the crown along this azimuth, leaning down by the group's amount.
            away = np.array((direction[0], 0.0, direction[2]))
            away /= max(np.linalg.norm(away), 1e-9)
            comb = away * (1 - group["down"]) + np.array((0.0, -1.0, 0.0)) * group["down"]
            length = group["length"][0] + (group["length"][1] - group["length"][0]) * ss.hash01(group["seed"], i, 3)
            # Clumps at the sides drift back, so they fall behind the shoulders and leave room for the side locks.
            side_drift = np.array((0.0, 0.0, -1.5)) * ss.smoothstep(60, 118, abs(azimuth))
            points = grow(collider, root, comb, length, group["offset"] + 0.002 * ss.hash01(group["seed"], i, 4),
                          group["gravity"], group["stiff"], group["curl"] * (0.7 + 0.6 * r), group["rows"], drift=side_drift)
            hair.add_clump(collider, points, group["width"] * (0.85 + 0.3 * r), group["taper"], group["thickness"], group["sides"],
                           azimuth, group["name"], twist=(r - 0.5) * 0.6)
    return hair


def build_front(collider):
    hair = HairMesh()
    bangs = FRONT_GROUPS[0]
    for i in range(bangs["count"]):
        r = ss.hash01(bangs["seed"], i)
        f = i / (bangs["count"] - 1)
        x = bangs["spread"][0] + (bangs["spread"][1] - bangs["spread"][0]) * f
        # Roots along the front hairline; those left of the parting sweep left, the few right of it sweep right.
        root = ss.ray_surface(sb.head_sdf, HEAD_CENTRE, normalise(np.array((x, 0.105, 0.075)))[None, :])[0]
        sweep = -bangs["sweep"] if x < bangs["parting"] else bangs["sweep"] * 0.8
        comb = np.array((sweep, -0.25, 0.85))
        length = bangs["length"][0] + (bangs["length"][1] - bangs["length"][0]) * r
        length *= 1.0 - 0.25 * ss.smoothstep(0.02, 0.07, x)  # shorter toward the parting's short side
        points = grow(collider, root, comb, length, bangs["offset"], 7.0, 0.035, bangs["curl"] * (0.6 + 0.8 * r), bangs["rows"])
        hair.add_clump(collider, points, bangs["width"] * (0.85 + 0.3 * r), bangs["taper"], bangs["thickness"], bangs["sides"], 180.0, "bangs")
    locks = FRONT_GROUPS[1]
    for side in (-1, 1):
        for j in range(locks["per_side"]):
            r = ss.hash01(locks["seed"], side, j)
            root = ss.ray_surface(sb.head_sdf, HEAD_CENTRE, normalise(np.array((side * 0.085, 0.06, 0.05 - 0.03 * j)))[None, :])[0]
            comb = np.array((side * 0.15, -1.0, 0.25))
            points = grow(collider, root, comb, locks["length"][j], locks["offset"] + 0.004 * j, 6.0, 0.03,
                          locks["curl"] * (0.8 + 0.4 * r), locks["rows"], drift=(-side * 0.4, 0.0, 0.6))
            hair.add_clump(collider, points, locks["width"] * (1.0 - 0.15 * j), locks["taper"], locks["thickness"], locks["sides"],
                           side * 120.0, "locks", side=side, twist=side * 0.4)
    return hair


def normalise(v):
    return v / np.linalg.norm(v)


def build_cap(body, name):
    """The scalp under the clumps: the head's skin above the hairline, lifted off it, thinning to nothing at the edge."""
    p = ss.unity_positions(body)
    head = ss.point_attribute(body, "p_head") > 0.5
    rel = p - HEAD_CENTRE
    angle = np.degrees(np.arctan2(np.abs(rel[:, 0]), rel[:, 2]))
    hairline = np.interp(angle, [a for a, _ in HAIRLINE], [h for _, h in HAIRLINE])
    above = rel[:, 1] - hairline
    keep = head & (above > -0.004)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for layer in list(bm.verts.layers.shape):
        bm.verts.layers.shape.remove(layer)
    for layer in list(bm.verts.layers.deform):
        bm.verts.layers.deform.remove(layer)
    bm.verts.ensure_lookup_table()
    remove = [v for v in bm.verts if not keep[v.index]]
    bmesh.ops.delete(bm, geom=remove, context="VERTS")
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    for attribute in [a.name for a in mesh.attributes if a.name.startswith("p_") or a.name in ("flesh", "project", "hand")]:
        mesh.attributes.remove(mesh.attributes[attribute])
    obj.vertex_groups.clear()
    q = ss.unity_positions(obj)
    n = sb.head_proxy_normals(q)
    rel = q - HEAD_CENTRE
    angle = np.degrees(np.arctan2(np.abs(rel[:, 0]), rel[:, 2]))
    edge = rel[:, 1] - np.interp(angle, [a for a, _ in HAIRLINE], [h for _, h in HAIRLINE])
    lift = CAP_OFFSET * ss.smoothstep(-0.004, 0.02, edge) + 0.0008
    ss.set_unity_positions(obj, q + n * lift[:, None])
    ss.set_point_attribute(obj, "hair_v", np.zeros(len(q)))
    ss.set_point_attribute(obj, "hair_outer", np.ones(len(q)))
    ss.set_point_attribute(obj, "hair_edge", np.zeros(len(q)))
    ss.set_point_attribute(obj, "clump", np.full(len(q), 0.5))
    return obj


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.view_layer.objects.active
    result.name = name
    result.data.name = name
    return result


# ---------------------------------------------------------------------------------------------------- spring chains

def chain_bones(hair, chains, select):
    """Bones for each chain: the average of the hanging parts of the clumps it carries. Returns {name: [(head, tail)]}."""
    bones = {}
    for name, key, count in chains:
        curves = [c for c in hair.curves if select(c, key) and c["hang"] is not None and c["hang"] < len(c["points"]) - 3]
        samples = []
        for c in curves:
            hanging = c["points"][c["hang"]:]
            samples.append(resample(hanging, count * 4))
        mean = np.mean(samples, axis=0)
        joints = resample(mean, count)
        bones[name] = [(joints[i], joints[i + 1]) for i in range(count)]
    return bones


def back_chain_weights(azimuth):
    """Share of each back chain for a clump at this azimuth (linear between neighbouring chains)."""
    names = [name for name, _, _ in BACK_CHAINS]
    angles = [a for _, a, _ in BACK_CHAINS]
    a = min(max(azimuth, angles[0]), angles[-1])
    for i in range(len(angles) - 1):
        if angles[i] <= a <= angles[i + 1]:
            f = (a - angles[i]) / (angles[i + 1] - angles[i])
            return {names[i]: 1 - f, names[i + 1]: f}
    return {names[-1]: 1.0}


def skin_hair(obj, hair, chain_shares, bone_counts, cap_vertices=0):
    """Head weights on the skull, chain bones along each clump's hanging part (blended at the joints)."""
    table = {}
    arcs = np.asarray(hair.arc)
    for i in range(len(hair.positions)):
        curve = hair.curves[hair.clump_of[i]]
        weights = {"Head": 1.0}
        shares = chain_shares(curve)
        if shares and curve["hang"] is not None:
            hang_arc = curve["arc"][curve["hang"]]
            hang_length = max(curve["length"] - hang_arc, 1e-6)
            h = (arcs[i] - hang_arc) / hang_length
            if h > -0.08:
                into = ss.smoothstep(-0.08, 0.04, h)
                weights = {"Head": 1.0 - into}
                for chain, share in shares.items():
                    count = bone_counts[chain]
                    segment = min(max(h, 0.0) * count, count - 1e-6)
                    lower = int(segment)
                    frac = segment - lower
                    # Each bone owns its stretch, sharing half with its neighbour right at the joint.
                    to_next = 0.5 * ss.smoothstep(0.65, 1.0, frac) if lower + 1 < count else 0.0
                    to_previous = 0.5 * (1 - ss.smoothstep(0.0, 0.35, frac)) if lower > 0 else 0.0
                    for bone, w in ((f"{chain}.{lower + 1}", 1 - to_next - to_previous), (f"{chain}.{lower + 2}", to_next),
                                    (f"{chain}.{lower}", to_previous)):
                        if w > 0:
                            weights[bone] = weights.get(bone, 0.0) + w * share * into
        table[i] = weights
    names = sorted({b for w in table.values() for b in w})
    array = np.zeros((cap_vertices + len(hair.positions), len(names)))
    if cap_vertices:
        array[:cap_vertices, names.index("Head")] = 1.0
    for i, weights in table.items():
        for bone, w in weights.items():
            array[cap_vertices + i, names.index(bone)] = w
    ss.set_weight_table(obj, array, names)


def build_rig_with(bones):
    """The shared rig plus part-only chain bones {chain: [(head, tail), ...]}, each chain hanging from Head."""
    rig = ct.build_armature()
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    edit = rig.data.edit_bones
    for chain, segments in bones.items():
        parent = edit["Head"]
        for i, (head, tail) in enumerate(segments):
            bone = edit.new(f"{chain}.{i + 1}")
            bone.head = ct.unity_to_blender(tuple(head))
            bone.tail = ct.unity_to_blender(tuple(tail))
            bone.roll = 0.0
            bone.parent = parent
            bone.use_connect = False
            parent = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def build_hair(body):
    """Returns ((back object, back rig, back chain roots), (front object, front rig, front chain roots))."""
    collider = Collider(body, with_arms=False)

    back = build_back(collider)
    chain_select = lambda curve, chain: back_chain_weights(curve["azimuth"]).get(chain, 0.0) > 0.35
    back_bones = chain_bones(back, [(name, name, count) for name, _, count in BACK_CHAINS], chain_select)
    back_mesh = back.to_object("HairBackClumps")
    cap = build_cap(body, "HairCap")
    cap_count = len(cap.data.vertices)
    back_obj = join([cap, back_mesh], "HairBack")
    back_rig = build_rig_with(back_bones)
    skin_hair(back_obj, back, lambda c: back_chain_weights(c["azimuth"]),
              {name: count for name, _, count in BACK_CHAINS}, cap_vertices=cap_count)
    ct.bind_to_rig(back_obj, back_rig)

    front = build_front(collider)
    lock_chains = [(name, side, count) for side, (name, count) in LOCK_CHAINS.items()]
    front_bones = chain_bones(front, lock_chains, lambda curve, side: curve["group"] == "locks" and curve["side"] == side)
    front_obj = front.to_object("HairFront")
    front_rig = build_rig_with(front_bones)
    skin_hair(front_obj, front, lambda c: {LOCK_CHAINS[c["side"]][0]: 1.0} if c["group"] == "locks" else {},
              {name: count for name, count in LOCK_CHAINS.values()})
    ct.bind_to_rig(front_obj, front_rig)

    paint_hair_colour(back_obj)
    paint_hair_colour(front_obj)
    return ((back_obj, back_rig, [f"{name}.1" for name, _, _ in BACK_CHAINS]),
            (front_obj, front_rig, [f"{name}.1" for name, _ in LOCK_CHAINS.values()]))


def paint_hair_colour(obj):
    """Vertex colours: no body region; recolour region 1 everywhere (the mask texture refines it)."""
    colours = np.zeros((len(obj.data.vertices), 4))
    colours[:, 1] = 1.0
    ss.set_colour_attribute(obj, colours)
