"""
Hair from Hunyuan3D scans of a character wearing a hairstyle (decision 0008, step 3), for make_generated_hair.py.

The scan is the outfit-01 character with hair in a colour far from skin and clothes. The hair is the scan surface in
that colour: over the head it is a cap whose open edge is the hairline, so the canonical bald head sits inside it like
a wig; under a bob or a ponytail it has its own underside. It is fitted to the canonical head, pushed off the body,
cut into a front piece (in front of the ears, below the crown) and a back piece, given the hair recolour mask (base to
tip down its length, the highlight "angel ring" round the crown) and spring chains where it hangs free.
"""

import json
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import body_tools as bt
import character_tools as ct
import garment_tools as gt
import prop_tools as pt

HAIRS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hairs.json")

HEAD_OFFSET = 0.004   # metres hair stands off the scalp at least
BODY_OFFSET = 0.022   # and off the torso: outside a coat (style bible §3: outer layers sit at 16 mm)
RING = (0.17, 0.22)   # the angel ring, as a share of the way from the top of the head to the bottom: a narrow sheen


def load_hair(key):
    with open(HAIRS, encoding="utf-8") as f:
        return json.load(f)[key]


def head_frame(body, regions):
    """The canonical head: its centre, the back of the skull, the crown and its radius (Blender coordinates)."""
    co = bt.vertices(body)[regions == "Head"]
    centre = (co.min(0) + co.max(0)) / 2
    return {"centre": centre, "back": float(co[:, 1].max()), "crown": float(co[:, 2].max()),
            "radius": float((co.max(0) - co.min(0)).max() / 2)}


def match_arm_height(scan, body):
    """
    Scales the scan about the floor so its arms are as high as the body's. The scan was scaled to the bald body's
    crown height, and hair makes its crown higher, so the body inside came out small. Returns the factor.
    """
    def arm_height(co):
        spread = np.abs(co[:, 0])
        return float(np.median(co[spread > 0.6 * spread.max(), 2]))

    factor = arm_height(bt.vertices(body)) / arm_height(bt.vertices(scan))
    scan.data.transform(Matrix.Diagonal((factor, factor, factor, 1.0)))
    scan.data.update()
    return factor


def hair_clusters(mean_colour, hint, hue_reach=40.0):
    """
    The colour clusters that are hair: the prompt's hair colour's hue, within ``hue_reach`` degrees and at least a
    third of its chroma, at any lightness (strands in shadow and the texture's own highlights are still hair).
    """
    lab = pt.linear_to_oklab(mean_colour)
    target = pt.linear_to_oklab(pt.srgb_to_linear(pt.hex_to_srgb(hint.lstrip("#"))))
    target_hue, target_chroma = math.atan2(target[2], target[1]), math.hypot(target[1], target[2])
    found = set()
    for k, (_, a, b) in enumerate(lab):
        turn = abs((math.degrees(math.atan2(b, a) - target_hue) + 180.0) % 360.0 - 180.0)
        if math.hypot(a, b) > target_chroma / 3 and turn < hue_reach:
            found.add(k)
    return found


def hair_mask(scan, labels, areas, clusters):
    """Scan faces that are hair (0) or not (-1): hair-coloured, small gaps filled, stray specks dropped."""
    mask = np.where(np.isin(labels, list(clusters)), 0, -1)
    mask = gt.fill_holes(scan, mask, areas, largest=0.006)
    return gt.drop_islands(scan, mask, areas, keep_fraction=0.08)


def fit_head(scan, body, regions, skin_cluster, labels, neck_z, shoulder_z, iterations=12):
    """
    Moves the scan's head onto the canonical head: the translation (iterated closest points, ignoring pairs more
    than a few centimetres apart) between the scan's bare face and the canonical head's surface, applied fully above
    the neck and fading out by the shoulders. Aligning by joints puts the head bone in the right place, but the hair
    has to sit on this exact head. No scale: fitted to a patch like the face, scale shrinks it towards the surface
    every pass (it shrank a bob by a fifth and bared the crown). Returns the translation.
    """
    mesh = scan.data
    co = bt.vertices(scan)
    normals = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    centres = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)
    face = (labels == skin_cluster) & (centres[:, 2] > neck_z + 0.04) & (normals[:, 1] < -0.3)
    points = centres[face]
    body_co = bt.vertices(body)
    head_polygons = [tuple(p.vertices) for p in body.data.polygons if (regions[list(p.vertices)] == "Head").all()]
    tree = BVHTree.FromPolygons([tuple(v) for v in body_co], head_polygons)

    offset = np.zeros(3)
    for iteration in range(iterations):
        moved = points + offset
        nearest = np.array([tree.find_nearest(Vector(tuple(p)))[0] for p in moved])
        gaps = nearest - moved
        reach = 0.05 if iteration < 3 else 0.02
        close = np.linalg.norm(gaps, axis=1) < reach
        if close.sum() < 10:
            break
        offset = offset + (np.median(gaps[close], axis=0) if iteration < 3 else gaps[close].mean(0))
    weight = np.clip((co[:, 2] - shoulder_z) / max(neck_z - shoulder_z, 1e-6), 0.0, 1.0)[:, None]
    mesh.vertices.foreach_set("co", (co + weight * offset).ravel())
    mesh.update()
    return offset


def push_off_body(part, body, neck_z):
    """
    Hair nearer the body than HEAD_OFFSET (on the head and neck) or BODY_OFFSET (on the torso), or inside it, goes
    out to it. The offset grows over the 10 cm below the neck: a step there left a ridge across long hair's back.
    """
    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], [tuple(p.vertices) for p in body.data.polygons])
    co = bt.vertices(part)
    moved = 0
    for _ in range(3):  # in a fold, the nearest face can change once a vertex has moved
        for i, p in enumerate(co):
            location, normal, _, _ = tree.find_nearest(Vector(tuple(p)))
            below = min(max((neck_z - location.z) / 0.1, 0.0), 1.0)
            offset = HEAD_OFFSET + (BODY_OFFSET - HEAD_OFFSET) * below * below * (3 - 2 * below)
            if (Vector(tuple(p)) - location).dot(normal) < offset:
                co[i] = tuple(location + normal * offset)
                moved += 1
    part.data.vertices.foreach_set("co", co.ravel())
    part.data.update()
    return moved


def tidy(part, largest_hole=0.12, smallest_island=0.03):
    """
    Fills holes in the hair whose edge is shorter than ``largest_hole`` metres (a centre parting the texture painted
    in skin colour showed the scalp through the crown) and drops loose pieces under ``smallest_island`` of the faces
    (strands that hung in front of the outfit model's shoulders ended up stuck to the chest). Returns (holes, pieces).
    """
    bm = bmesh.new()
    bm.from_mesh(part.data)
    bm.edges.ensure_lookup_table()
    boundary = {e for e in bm.edges if e.is_boundary}
    holes, seen = [], set()
    for start in boundary:
        if start in seen:
            continue
        loop, stack = [], [start]
        seen.add(start)
        while stack:
            edge = stack.pop()
            loop.append(edge)
            for vertex in edge.verts:
                for other in vertex.link_edges:
                    if other in boundary and other not in seen:
                        seen.add(other)
                        stack.append(other)
        if sum(e.calc_length() for e in loop) < largest_hole:
            holes.append(loop)
    for loop in holes:
        bmesh.ops.holes_fill(bm, edges=loop, sides=0)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])

    bm.faces.ensure_lookup_table()
    islands, seen = [], set()
    for start in bm.faces:
        if start in seen:
            continue
        island, stack = [], [start]
        seen.add(start)
        while stack:
            face = stack.pop()
            island.append(face)
            for edge in face.edges:
                for other in edge.link_faces:
                    if other not in seen:
                        seen.add(other)
                        stack.append(other)
        islands.append(island)
    dropped = [island for island in islands if len(island) < smallest_island * len(bm.faces)]
    for island in dropped:
        bmesh.ops.delete(bm, geom=island, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(part.data)
    bm.free()
    part.data.update()
    return len(holes), len(dropped)


def scalp_mask(body, regions, frame, joints):
    """
    The canonical head's vertices that hair grows from: above a hairline that runs across the forehead (4.5 cm above
    the brows), over the ears, and down the back of the head to the nape, blended round the head by direction.
    """
    co = bt.vertices(body)
    head = regions == "Head"
    brow = float(max(joints["Brow.L"][2], joints["Brow.R"][2]))
    eye = float(max(joints["Eye.L"][2], joints["Eye.R"][2]))
    nape = float(joints["Head"][2]) + 0.035
    d = co - frame["centre"]
    facing = np.clip(-d[:, 1] / np.maximum(np.hypot(d[:, 0], d[:, 1]), 1e-9), -1.0, 1.0)  # 1 front, 0 side, -1 back
    line = np.where(facing > 0, eye + 0.025 + (brow + 0.045 - eye - 0.025) * facing, eye + 0.025 + (nape - eye - 0.025) * -facing)
    return head & (co[:, 2] > line)


def cover_scalp(hair, body, regions, frame, joints, reach=0.08):
    """
    Covers the scalp wherever the scan's hair doesn't: the canonical head's own surface there, just off it, joined to
    the hair. Hunyuan invents the back of the head, and painted the nape below a high ponytail as bare skin. A scalp
    vertex is covered if a ray out along its normal meets the hair within ``reach``. Returns the faces added.
    """
    mesh = body.data
    co = bt.vertices(body)
    normals = np.empty(len(co) * 3)
    mesh.vertices.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    scalp = scalp_mask(body, regions, frame, joints)
    hair_tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(hair)], [tuple(p.vertices) for p in hair.data.polygons])
    bare = np.zeros(len(co), dtype=bool)
    for i in np.flatnonzero(scalp):
        hit, _, _, _ = hair_tree.ray_cast(Vector(tuple(co[i] + normals[i] * 0.001)), Vector(tuple(normals[i])), reach)
        bare[i] = hit is None
    # Grow the bare patch by two rings so it tucks under the hair's ragged edge.
    edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", edges)
    a, b = edges[0::2], edges[1::2]
    grown = bare.copy()
    for _ in range(2):
        ring = grown.copy()
        ring[a[grown[b]]] = True
        ring[b[grown[a]]] = True
        grown = ring & scalp
    faces = [list(p.vertices) for p in mesh.polygons if grown[list(p.vertices)].all()]
    if not faces:
        return 0
    used = sorted({v for f in faces for v in f})
    index = {v: i for i, v in enumerate(used)}
    lifted = co[used] + normals[used] * (HEAD_OFFSET + 0.001)
    bm = bmesh.new()
    bm.from_mesh(hair.data)
    verts = [bm.verts.new(tuple(p)) for p in lifted]
    for f in faces:
        try:
            bm.faces.new([verts[index[v]] for v in f])
        except ValueError:
            pass
    bm.to_mesh(hair.data)
    bm.free()
    hair.data.update()
    return len(faces)


def off_the_chest(part, body, regions, shoulder_z):
    """
    Drops hair lying on the front of the torso below the shoulders: the game's long hair falls behind them. Strands
    that hung in front of the outfit model's shoulders ended up draped over our body's bust. Returns faces dropped.
    """
    tree, polygons = _region_tree(body, regions, ("UpperTorso", "LowerTorso", "Pelvis", "Neck", "Head"))
    mesh = part.data
    centres = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)
    drop = []
    for f, c in enumerate(centres):
        if c[2] >= shoulder_z - 0.02:
            continue
        location, normal, _, distance = tree.find_nearest(Vector(tuple(c)))
        if location is not None and normal.y < -0.4 and distance < 0.08:
            drop.append(f)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[f] for f in drop], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return len(drop)


def _region_tree(body, regions, keep):
    """A BVH of the body's faces whose vertices are all in ``keep`` regions, and those faces' vertex triples."""
    polygons = [tuple(p.vertices) for p in body.data.polygons if np.isin(regions[list(p.vertices)], keep).all()]
    return BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], polygons), polygons


def transfer_shapes(part, body, regions):
    """
    Every body shape key onto the hair, as character_tools.transfer_from_body does, but sampled from the head and
    trunk only: long hair hanging past the hips took its shapes from the nearest arm or thigh and tore apart.
    """
    mesh = body.data
    basis = bt.vertices(body)
    keep = ("Head", "Neck", "UpperTorso", "LowerTorso", "Pelvis")
    triangles = []
    mesh.calc_loop_triangles()
    for t in mesh.loop_triangles:
        if np.isin(regions[list(t.vertices)], keep).all():
            triangles.append(tuple(t.vertices))
    tree = BVHTree.FromPolygons([tuple(v) for v in basis], triangles)
    keys = [k for k in (mesh.shape_keys.key_blocks if mesh.shape_keys else []) if k.name != "Basis"]
    if not keys:
        return
    co = bt.vertices(part)
    samples = []
    for p in co:
        location, _, index, _ = tree.find_nearest(Vector(tuple(p)))
        a, b, c = triangles[index]
        samples.append(((a, b, c), ct.barycentric(location, Vector(tuple(basis[a])), Vector(tuple(basis[b])), Vector(tuple(basis[c])))))
    corners = np.array([s[0] for s in samples])
    shares = np.array([s[1] for s in samples])
    if not part.data.shape_keys:
        part.shape_key_add(name="Basis", from_mix=False)
    for key in keys:
        shaped = np.empty(len(key.data) * 3)
        key.data.foreach_get("co", shaped)
        delta = shaped.reshape(-1, 3) - basis
        target = part.data.shape_keys.key_blocks.get(key.name) or part.shape_key_add(name=key.name, from_mix=False)
        target.value = 0.0
        target.data.foreach_set("co", (co + (delta[corners] * shares[:, :, None]).sum(1)).ravel())


def clear_eyes(part, frame, joints):
    """
    Shortens the fringe so it stops at the canonical brows: the game draws its own eyes and brows, and expressions
    must read. A fringe made for the outfit model's face can hang lower on ours (one covered the eyes). The hair in
    front of the face, between the outer eye corners, is squashed up towards a line 3.5 cm above the brows; the
    locks framing the face keep their length.
    """
    co = bt.vertices(part)
    brow = float(max(joints["Brow.L"][2], joints["Brow.R"][2]))
    eye_x = float(abs(joints["Eye.L"][0])) + 0.03
    top, floor = brow + 0.035, brow - 0.004
    front = co[:, 1] < frame["centre"][1] - 0.04
    across = 1.0 - np.clip((np.abs(co[:, 0]) - eye_x) / 0.02, 0.0, 1.0)
    fringe = front & (across > 0) & (co[:, 2] < top) & (co[:, 2] > float(joints["Mouth"][2]))  # the face, not the chest
    # How low it hangs is judged over the bridge of the nose, where only a fringe reaches (the locks beside the
    # cheeks curl in under a narrow jaw).
    over_eyes = fringe & (np.abs(co[:, 0]) < 0.035) & (co[:, 1] < float(joints["Eye.L"][1]))
    if not over_eyes.any() or co[over_eyes, 2].min() >= floor:
        return 0.0
    lowest = co[over_eyes, 2].min()
    squash = (top - floor) / (top - lowest)
    target = top - (top - co[:, 2]) * squash
    co[:, 2] = np.where(fringe, co[:, 2] + across * (target - co[:, 2]), co[:, 2])
    part.data.vertices.foreach_set("co", co.ravel())
    part.data.update()
    return float(floor - lowest)


def smooth(part, iterations=2, factor=0.5):
    """A light smoothing of the scan's surface noise (keeps the strand grooves); the open edges stay put."""
    bm = bmesh.new()
    bm.from_mesh(part.data)
    inner = [v for v in bm.verts if not v.is_boundary]
    for _ in range(iterations):
        bmesh.ops.smooth_vert(bm, verts=inner, factor=factor, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(part.data)
    bm.free()
    part.data.update()


def split_front_back(hair, frame, front_name, back_name):
    """
    The front piece (in front of the ears and below the top of the head: fringe and the locks framing the face) and
    the back piece (the rest), cut apart along a smooth line so they meet without a gap when worn together.
    """
    co = bt.vertices(hair)
    middle_y, hairline_z = frame["centre"][1], frame["crown"] - 0.045
    field = np.minimum(middle_y - co[:, 1], hairline_z - co[:, 2])
    bm = bmesh.new()
    bm.from_mesh(hair.data)
    value = gt._cut_along(bm, np.where(np.abs(field) < 1e-5, 1e-5, field), 0.0)
    front_faces = {f.index for f in bm.faces if np.mean([value[v] for v in f.verts]) > 0}
    bm.to_mesh(hair.data)
    bm.free()
    hair.data.update()

    pieces = []
    for name, keep_front in ((front_name, True), (back_name, False)):
        piece = hair.copy()
        piece.data = hair.data.copy()
        piece.name = piece.data.name = name
        bpy.context.scene.collection.objects.link(piece)
        bm = bmesh.new()
        bm.from_mesh(piece.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if (f.index in front_faces) != keep_front], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(piece.data)
        bm.free()
        piece.data.update()
        pieces.append(piece if len(piece.data.polygons) else None)
    return pieces


def hair_mask_colours(part, frame, tip_from, tip_to):
    """
    The `Col` attribute for hair, per face corner: G = base and B = tip, blending down the hair's length from
    ``tip_from`` to ``tip_to`` (heights, shared by both pieces so they match), and A = the highlight "angel ring" round
    the crown, cut along its edges so they are clean lines. R = 0: hair carries no body region.
    """
    mesh = part.data
    centre = frame["centre"]

    def along(co):
        d = co - centre
        return np.degrees(np.arccos(np.clip(d[:, 2] / np.maximum(np.linalg.norm(d, axis=1), 1e-9), -1, 1))) / 180.0

    bm = bmesh.new()
    bm.from_mesh(mesh)
    for level in RING:
        bm.verts.index_update()
        bm.verts.ensure_lookup_table()
        field = along(np.array([v.co[:] for v in bm.verts])) - level
        gt._cut_along(bm, np.where(np.abs(field) < 1e-5, 1e-5, field), 0.0)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    co = bt.vertices(part)
    tip = np.clip((tip_from - co[:, 2]) / max(tip_from - tip_to, 1e-6), 0.0, 1.0)
    tip = tip * tip * (3 - 2 * tip)
    centres = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)
    normals = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    radial = centres - centre
    radial /= np.maximum(np.linalg.norm(radial, axis=1), 1e-9)[:, None]
    face_along = along(centres)
    ring = (face_along > RING[0]) & (face_along < RING[1]) & ((normals * radial).sum(1) > 0.2)

    loop_vertex = np.empty(len(mesh.loops), dtype=np.int64)
    mesh.loops.foreach_get("vertex_index", loop_vertex)
    loop_face = np.repeat(np.arange(len(mesh.polygons)), [len(p.vertices) for p in mesh.polygons])
    values = np.zeros((len(mesh.loops), 4))
    values[:, 1] = 1.0 - tip[loop_vertex]
    values[:, 2] = tip[loop_vertex]
    lit = ring[loop_face]
    values[lit] = (0.0, 0.0, 0.0, 1.0)
    if ct.COLOUR_ATTRIBUTE in mesh.color_attributes:
        mesh.color_attributes.remove(mesh.color_attributes[ct.COLOUR_ATTRIBUTE])
    colours = mesh.color_attributes.new(ct.COLOUR_ATTRIBUTE, "FLOAT_COLOR", "CORNER")
    colours.data.foreach_set("color", values.ravel())
    mesh.color_attributes.active_color = colours
    return int(ring.sum())


def follow_head(part, joints, shoulder_z):
    """
    Head weights for the hair, handing over down the spine below the shoulders (Chest, then Spine, then Hips, by
    height), so long hair rests on the back instead of swinging out with the head, and stays within the validator's
    reach of its bones.
    """
    anchors = [("Head", shoulder_z + 0.04), ("Chest", float(joints["Chest"][2])),
               ("Spine", float(joints["Spine"][2])), ("Hips", float(joints["Hips"][2]))]
    co = bt.vertices(part)
    groups = {name: part.vertex_groups.get(name) or part.vertex_groups.new(name=name) for name, _ in anchors}
    for i, z in enumerate(co[:, 2]):
        weights = {anchors[-1][0]: 1.0}
        for (upper, upper_z), (lower, lower_z) in zip(anchors, anchors[1:]):
            if z >= upper_z:
                weights = {upper: 1.0}
                break
            if z >= lower_z:
                share = (upper_z - z) / max(upper_z - lower_z, 1e-6)
                weights = {upper: 1.0 - share, lower: share}
                break
        for group in list(part.vertex_groups):
            group.remove([i])
        for name, weight in weights.items():
            if weight > 1e-4:
                groups[name].add([i], float(weight), "REPLACE")


def fit_chain(part, frame, name, count):
    """
    A spring chain along hair that hangs free behind the head (a ponytail): its centreline, from z-slices of the hair
    out past the back of the skull, cut into ``count`` bones. Gives those vertices chain weights (fading to Head at
    the root) and returns the bones as (name, parent, Blender position), or [] if nothing hangs there.
    """
    co = bt.vertices(part)
    tail = np.flatnonzero(co[:, 1] > frame["back"] + 0.01)
    if len(tail) < 20:
        return []
    points = co[tail]
    top, bottom = points[:, 2].max(), points[:, 2].min()
    line = []
    for z in np.arange(top, bottom - 1e-6, -0.02):
        near = points[np.abs(points[:, 2] - z) < 0.012]
        if len(near):
            line.append(near.mean(0))
    line = np.array(line)
    if len(line) < 2:
        return []
    lengths = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(line, axis=0), axis=1))]
    joints = np.array([np.array([np.interp(s, lengths, line[:, k]) for k in range(3)]) for s in np.linspace(0, lengths[-1], count + 1)])
    bones = [(f"{name}.{i + 1}", "Head" if i == 0 else f"{name}.{i}", joints[i]) for i in range(count)]

    groups = [part.vertex_groups.get(b[0]) or part.vertex_groups.new(name=b[0]) for b in bones]
    if "Head" not in part.vertex_groups:
        part.vertex_groups.new(name="Head")
    segment = lengths[-1] / count
    for i in map(int, tail):
        p = co[i]
        # Where along the chain: the nearest point on its polyline, as arc length.
        best, best_s = 1e9, 0.0
        for j in range(len(line) - 1):
            a, b = line[j], line[j + 1]
            ab = b - a
            t = float(np.clip(np.dot(p - a, ab) / max(np.dot(ab, ab), 1e-12), 0.0, 1.0))
            d = float(np.linalg.norm(a + t * ab - p))
            if d < best:
                best, best_s = d, lengths[j] + t * float(np.linalg.norm(ab))
        position = best_s / segment - 0.5  # bone k owns the middle of its segment
        k = int(np.clip(np.floor(position), 0, count - 1))
        share = float(np.clip(position - k, 0.0, 1.0)) if position >= 0 and k < count - 1 else 0.0
        weights = {groups[k].name: 1.0 - share}
        if share > 0:
            weights[groups[k + 1].name] = share
        rooted = float(np.clip(best_s / (0.5 * segment), 0.0, 1.0))  # the first half-bone fades in from the head
        weights = {n: w * rooted for n, w in weights.items()}
        weights["Head"] = weights.get("Head", 0.0) + (1.0 - rooted)
        for group in list(part.vertex_groups):
            group.remove([i])
        for n, w in weights.items():
            if w > 1e-4:
                part.vertex_groups[n].add([i], w, "REPLACE")
    return bones
