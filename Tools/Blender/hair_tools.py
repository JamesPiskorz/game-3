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
BODY_OFFSET = 0.04   # and off the torso: outside a coat (style bible §3: 16 mm) and a loose top (2.5 cm of ease poked through)
RING = (0.25, 0.272)  # the angel ring, as a share of the way from the top of the head to the bottom: a narrow sheen
RING_TEETH, RING_DEPTH = 16, 0.032  # its lower edge zigzags down in this many teeth round the head, this deep


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


FRINGE_OFFSET = 0.007  # metres a fringe stands off the forehead: well in front of the brows painted 2 mm proud of it


def push_off_body(part, body, neck_z, joints=None):
    """
    Hair nearer the body than HEAD_OFFSET (on the head and neck) or BODY_OFFSET (on the torso), or inside it, goes
    out to it. The offset grows over the 10 cm below the neck: a step there left a ridge across long hair's back.
    Given ``joints``, a fringe over the brows stands FRINGE_OFFSET off the forehead, clear of the brows painted on it.
    (Pushed further, the fringe's inner layer went through its outer one and crumpled.)
    """
    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], [tuple(p.vertices) for p in body.data.polygons])
    co = bt.vertices(part)
    moved = 0
    forehead = None
    if joints is not None:  # across between the outer eye corners, from the eyes' top to above the brows
        brow = float(max(joints["Brow.L"][2], joints["Brow.R"][2]))
        eye = float(max(joints["Eye.L"][2], joints["Eye.R"][2]))
        forehead = (float(abs(joints["Eye.L"][0])) + 0.03, eye, brow + 0.04)
    for _ in range(3):  # in a fold, the nearest face can change once a vertex has moved
        for i, p in enumerate(co):
            location, normal, _, _ = tree.find_nearest(Vector(tuple(p)))
            below = min(max((neck_z - location.z) / 0.1, 0.0), 1.0)
            offset = HEAD_OFFSET + (BODY_OFFSET - HEAD_OFFSET) * below * below * (3 - 2 * below)
            if forehead is not None and normal.y < -0.5 and abs(location.x) < forehead[0] and forehead[1] < location.z < forehead[2]:
                offset = FRINGE_OFFSET
            if (Vector(tuple(p)) - location).dot(normal) < offset:
                co[i] = tuple(location + normal * offset)
                moved += 1
    if forehead is not None:
        # Measured straight in from the front as well: next to the brows a fringe point's nearest surface is often a
        # brow's rim, facing up or down, so it only kept HEAD_OFFSET and the brows showed through the fringe.
        clear = FRINGE_OFFSET - 0.002
        for i, p in enumerate(co):
            if abs(p[0]) < forehead[0] and forehead[1] < p[2] < forehead[2]:
                hit, _, _, _ = tree.ray_cast(Vector((p[0], p[1] - 0.2, p[2])), Vector((0.0, 1.0, 0.0)), 0.4)
                if hit is not None and p[1] > hit.y - clear and p[1] < hit.y + 0.02:
                    co[i][1] = hit.y - clear
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


def blunt_cut(part, frame, joints, below_brow=0.009, band=0.05, column=0.008):
    """
    A blunt fringe: straight across ``below_brow`` metres under the brows, resting on the upper lash line (hiding the
    brows, as the image's does), in
    front of the face between the outer eye corners; the side locks keep their length. Where the fringe stops short
    of that line (the scan's dipped in a notch between two lobes) its lowest ``band`` metres are stretched down to it,
    column by column ``column`` metres wide; whatever hangs below it is cut away along it. Returns the faces removed.
    """
    line = float(max(joints["Brow.L"][2], joints["Brow.R"][2])) - below_brow
    eye_x = float(abs(joints["Eye.L"][0])) + 0.03
    front_y = frame["centre"][1] - 0.04
    co = bt.vertices(part)
    in_front = (np.abs(co[:, 0]) < eye_x + 0.01) & (co[:, 1] < front_y)
    bm = bmesh.new()
    bm.from_mesh(part.data)
    bm.verts.ensure_lookup_table()
    edge = np.array([v.is_boundary for v in bm.verts])
    bm.free()
    columns = np.round(co[:, 0] / column).astype(int)
    stretched = co.copy()
    for c in np.unique(columns[in_front & edge]):
        here = in_front & (columns == c)
        bottom = co[here & edge, 2].min()
        if bottom <= line or abs(c * column) > eye_x:
            continue
        top = bottom + band
        moving = here & (co[:, 2] < top)
        stretched[moving, 2] = top - (top - co[moving, 2]) * (top - line) / (top - bottom)
    part.data.vertices.foreach_set("co", stretched.ravel())
    part.data.update()

    bm = bmesh.new()
    bm.from_mesh(part.data)
    geometry = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geometry, plane_co=(0.0, 0.0, line), plane_no=(0.0, 0.0, 1.0))
    cut = [f for f in bm.faces
           if (c := f.calc_center_median()).z < line - 1e-4 and abs(c.x) < eye_x and c.y < front_y]
    bmesh.ops.delete(bm, geom=cut, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(part.data)
    bm.free()
    part.data.update()
    return len(cut)


def cut_length(part, joints, below_chin):
    """
    Cuts the hair straight across ``below_chin`` metres under the chin (the Head joint), all round: the bob in the
    image ends at the jaw, and Hunyuan's hung nearly to the shoulders. Returns the faces removed.
    """
    line = float(joints["Head"][2]) - below_chin
    bm = bmesh.new()
    bm.from_mesh(part.data)
    geometry = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geometry, plane_co=(0.0, 0.0, line), plane_no=(0.0, 0.0, 1.0))
    cut = [f for f in bm.faces if f.calc_center_median().z < line - 1e-4]
    bmesh.ops.delete(bm, geom=cut, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(part.data)
    bm.free()
    part.data.update()
    return len(cut)


def tame(part, frame, joints, squeeze=0.15, curl=0.0, curl_length=0.035):
    """
    Draws hair below the eyes in towards the head's vertical axis, up to ``squeeze`` of its distance by the chin: a
    bob from the image hangs straight down, and Hunyuan's flared out into a bell. With ``curl``, the ends curve under
    as well, drawn in that much more over their last ``curl_length`` metres (a bob's ends turn in; the scan's flicked
    out in points). push_off_body afterwards keeps it off the face and neck.
    """
    co = bt.vertices(part)
    eye = float(max(joints["Eye.L"][2], joints["Eye.R"][2]))
    chin = float(joints["Head"][2])
    t = np.clip((eye - co[:, 2]) / max(eye - chin, 1e-6), 0.0, 1.0)
    scale = 1.0 - squeeze * t * t * (3 - 2 * t)
    if curl > 0:
        u = np.clip((co[:, 2] - float(co[:, 2].min())) / curl_length, 0.0, 1.0)
        scale *= 1.0 - curl * (1.0 - u) ** 2
    axis = np.array([0.0, frame["centre"][1]])
    co[:, :2] = axis + (co[:, :2] - axis) * scale[:, None]
    part.data.vertices.foreach_set("co", co.ravel())
    part.data.update()


def round_normals(part, frame, strength=0.75, forward=0.45, up=0.35):
    """
    Blends the hair's normals towards a sphere's round the head's centre (``strength``), as anime hair is shaded: the
    toon step then crosses the hair in one clean curve instead of following every lock and dent of the scan. Writes
    custom split normals, which the FBX export carries.
    """
    mesh = part.data
    co = bt.vertices(part)
    sphere = co - frame["centre"]
    sphere /= np.maximum(np.linalg.norm(sphere, axis=1), 1e-9)[:, None]
    normals = np.empty(len(co) * 3)
    mesh.vertices.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    facing = np.sign((normals * sphere).sum(axis=1))[:, None]  # the underside of a lock faces in: keep it so
    facing[facing == 0] = 1.0
    # Only near the head: tails hanging half a metre below it flipped between facing towards and away from its centre
    # in jagged bands.
    near = np.clip((0.28 - np.linalg.norm(co - frame["centre"], axis=1)) / 0.1, 0.0, 1.0)[:, None]
    strength = strength * near
    mixed = normals * (1.0 - strength) + sphere * facing * strength
    # Leaning towards the viewer, as the face's do, so the toon step leaves most of the hair lit and shades its far
    # side and underneath, as in the references (round alone, half the bob was in shade).
    mixed[:, 1] -= forward * (facing[:, 0] > 0) * near[:, 0]
    # And up, towards the sun, so the back of the head isn't half in shade from behind.
    mixed[:, 2] += up * (facing[:, 0] > 0) * near[:, 0]
    mixed /= np.maximum(np.linalg.norm(mixed, axis=1), 1e-9)[:, None]
    mesh.polygons.foreach_set("use_smooth", np.ones(len(mesh.polygons), dtype=bool))
    mesh.normals_split_custom_set_from_vertices([tuple(n) for n in mixed])
    mesh.update()


def thin(part, body, neck_z, keep=0.6):
    """
    Hair above the neck drawn in towards the scalp so it stands off it only ``keep`` as far (over HEAD_OFFSET): the
    image's bob hugs the head, and Hunyuan's stood out from it like a helmet. Each point moves along the line to its
    nearest point on the body.
    """
    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], [tuple(p.vertices) for p in body.data.polygons])
    co = bt.vertices(part)
    for i, p in enumerate(co):
        if p[2] < neck_z:
            continue
        location, _, _, distance = tree.find_nearest(Vector(tuple(p)))
        if location is None or distance <= HEAD_OFFSET:
            continue
        target = HEAD_OFFSET + (distance - HEAD_OFFSET) * keep
        co[i] = np.asarray(location) + (p - np.asarray(location)) * (target / distance)
    part.data.vertices.foreach_set("co", co.ravel())
    part.data.update()


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


def tube_tails(part, frame, beside, step=0.015, segments=20, sigma=1.5):
    """
    Rebuilds tails hanging beside the head (twin tails, more than ``beside`` metres out from its middle) from just under
    their ties as closed tubes: each horizontal slice ``step`` metres deep is the convex hull of the scan's tail
    there, the hulls blurred down the tail, closing to a point at its tip. The scan's tails were thin sheets torn
    open where the texture shaded them out of the hair's colour. Returns the tubes made.
    """
    co = bt.vertices(part)
    top = float(frame["crown"]) - 0.04  # from just under the ties: the scan tore the tails open right up to them
    thetas = np.linspace(-np.pi, np.pi, segments, endpoint=False)
    kernel = np.exp(-0.5 * (np.arange(-4, 5) / sigma) ** 2)
    kernel /= kernel.sum()
    bm = bmesh.new()
    bm.from_mesh(part.data)
    bm.verts.ensure_lookup_table()
    made = 0
    doomed = set()
    for side in (1.0, -1.0):
        tail = ((co[:, 0] - frame["centre"][0]) * side > beside) & (co[:, 2] < top)
        if tail.sum() < 50:
            continue
        z = co[tail, 2]
        zs = np.arange(top, float(z.min()) - 1e-6, -step)
        centres, radii, keep = [], [], []
        for zz in zs:
            slab = tail & (np.abs(co[:, 2] - zz) < step)
            if slab.sum() < 4:
                continue
            centre = co[slab, :2].mean(axis=0)
            centres.append(centre)
            radii.append(np.maximum(gt._hull_radii(co[slab, :2] - centre, thetas), 0.006))
            keep.append(zz)
        if len(keep) < 4:
            continue
        centres, radii, keep = np.array(centres), np.array(radii), np.array(keep)
        padded = np.concatenate([np.repeat(radii[:1], 4, axis=0), radii, np.repeat(radii[-1:], 4, axis=0)], axis=0)
        radii = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 0, padded)
        padded = np.concatenate([np.repeat(centres[:1], 4, axis=0), centres, np.repeat(centres[-1:], 4, axis=0)], axis=0)
        centres = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 0, padded)
        taper = np.clip((keep - keep[-1]) / 0.06, 0.25, 1.0)  # the last 6 cm close to a point
        rings = []
        for c, r, zz, t in zip(centres, radii, keep, taper):
            rings.append([bm.verts.new((c[0] + np.cos(a) * ri * t, c[1] + np.sin(a) * ri * t, zz)) for a, ri in zip(thetas, r)])
        for upper, lower in zip(rings[:-1], rings[1:]):
            for k in range(segments):
                bm.faces.new((upper[k], upper[(k + 1) % segments], lower[(k + 1) % segments], lower[k]))
        tip = bm.verts.new((centres[-1][0], centres[-1][1], keep[-1] - step * 0.5))
        for k in range(segments):
            bm.faces.new((rings[-1][k], rings[-1][(k + 1) % segments], tip))
        doomed |= set(np.flatnonzero(tail & (co[:, 2] < top - step)).tolist())
        made += 1
    bm.verts.ensure_lookup_table()
    gone = [f for f in bm.faces if all(v.index in doomed for v in f.verts)]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(part.data)
    bm.free()
    part.data.update()
    return made


def split_front_back(hair, frame, front_name, back_name, beside=None):
    """
    The front piece (in front of the ears and below the top of the head: fringe and the locks framing the face) and
    the back piece (the rest), cut apart along a smooth line so they meet without a gap when worn together. With
    ``beside``, hair more than that many metres out from the head's middle goes to the back piece too: twin tails hang
    beside the head, and the cut at its middle sliced each one lengthwise between the pieces.
    """
    co = bt.vertices(hair)
    middle_y, hairline_z = frame["centre"][1], frame["crown"] - 0.045
    field = np.minimum(middle_y - co[:, 1], hairline_z - co[:, 2])
    if beside is not None:
        field = np.minimum(field, beside - np.abs(co[:, 0] - frame["centre"][0]))
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
    the crown, its lower edge a zigzag as anime hair's sheen is drawn, cut along its edges so they are clean lines.
    R = 0: hair carries no body region.
    """
    mesh = part.data
    centre = frame["centre"]

    def along(co):
        d = co - centre
        return np.degrees(np.arccos(np.clip(d[:, 2] / np.maximum(np.linalg.norm(d, axis=1), 1e-9), -1, 1))) / 180.0

    def teeth(co):  # 0..1 round the head: a triangle wave in the azimuth
        d = co - centre
        turn = (np.arctan2(d[:, 0], -d[:, 1]) / (2 * np.pi) * RING_TEETH) % 1.0
        return 1.0 - np.abs(turn * 2.0 - 1.0)

    def lower(co):
        return RING[1] + RING_DEPTH * teeth(co)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    for edge in (lambda co: along(co) - RING[0], lambda co: along(co) - lower(co)):
        bm.verts.index_update()
        bm.verts.ensure_lookup_table()
        field = edge(np.array([v.co[:] for v in bm.verts]))
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
    ring = (face_along > RING[0]) & (face_along < lower(centres)) & ((normals * radial).sum(1) > 0.2)

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


def clear_torso_in_pose(part, body, joints, offset=BODY_OFFSET, step=0.02, angles=72):
    """
    Hair below the neck pushed out, in the game's relaxed pose (body_tools.RELAXED), to ``offset`` metres outside the
    torso's outline there (each horizontal slice's convex hull, arms left out), then taken back to the bind pose. Built
    in the T-pose, long hair cleared the back by its offset; with the arms down the shoulder blades and a top's cloth
    stretched across them came out through it. Every shape key alike, against the body in that shape. Returns the
    vertices moved in the basis.
    """
    from garment_tools import _hull_radii, _key_positions
    skinning = bt.pose_skinning(joints)
    hair_w, hair_names = bt.weight_matrix(part)
    body_w, body_names = bt.weight_matrix(body)
    torso = body_w[:, [i for i, n in enumerate(body_names) if n in ("Hips", "Spine", "Chest", "Neck")]].sum(axis=1) > 0.3  # with the shoulders
    neck = float(joints["Neck"][2])
    thetas = np.linspace(-np.pi, np.pi, angles, endpoint=False)
    body_keys = {k.name: k for k in body.data.shape_keys.key_blocks} if body.data.shape_keys else {}
    mesh = part.data
    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else [None]
    moved_basis = 0
    for key in keys:
        rest = bt.vertices(part) if key is None else _key_positions(key)
        body_rest = bt.vertices(body) if key is None or key.name not in body_keys else _key_positions(body_keys[key.name])
        posed_body = bt.skin_positions(body_rest, body_w, body_names, skinning)[torso]
        posed = bt.skin_positions(rest, hair_w, hair_names, skinning)
        below = np.flatnonzero(posed[:, 2] < neck - 0.02)
        if not len(below):
            continue
        zs = np.arange(float(posed_body[:, 2].min()), neck, step)
        centres = np.full((len(zs), 2), np.nan)
        hulls = np.zeros((len(zs), angles))
        for i, z in enumerate(zs):
            slab = np.abs(posed_body[:, 2] - z) < step
            if slab.sum() >= 8:
                centres[i] = posed_body[slab, :2].mean(axis=0)
                hulls[i] = _hull_radii(posed_body[slab, :2] - centres[i], thetas)
        good = ~np.isnan(centres[:, 0])
        if good.sum() < 2:
            continue
        p = posed[below]
        fi = np.clip(np.interp(p[:, 2], zs[good], np.arange(good.sum())), 0, good.sum() - 1.001)
        i0, ti = fi.astype(int), fi - fi.astype(int)
        c, h = centres[good], hulls[good]
        centre = c[i0] * (1 - ti)[:, None] + c[i0 + 1] * ti[:, None]
        d = p[:, :2] - centre
        radius = np.linalg.norm(d, axis=1)
        fj = ((np.arctan2(d[:, 1], d[:, 0]) + np.pi) / (2 * np.pi) * angles) % angles
        j0, tj = fj.astype(int), fj - fj.astype(int)
        j1 = (j0 + 1) % angles
        hull = (h[i0, j0] * (1 - ti) * (1 - tj) + h[i0 + 1, j0] * ti * (1 - tj)
                + h[i0, j1] * (1 - ti) * tj + h[i0 + 1, j1] * ti * tj)
        inside = radius < hull + offset
        p[inside, :2] = centre[inside] + d[inside] / np.maximum(radius[inside], 1e-9)[:, None] * (hull[inside] + offset)[:, None]
        posed[below] = p
        unposed = bt.unskin_positions(posed, hair_w, hair_names, skinning)
        (mesh.vertices if key is None else key.data).foreach_set("co", unposed.ravel())
        if key is None or key == mesh.shape_keys.reference_key:
            moved_basis = int(inside.sum())
    if mesh.shape_keys:
        mesh.vertices.foreach_set("co", _key_positions(mesh.shape_keys.reference_key).ravel())
    mesh.update()
    return moved_basis


def fit_chain(part, frame, name, count, side=0, beside=0.085):
    """
    A spring chain along hair that hangs free behind the head (a ponytail), or with ``side`` (1 left, -1 right) beside
    it, more than ``beside`` metres out from the head's middle (a twin tail): its centreline, from z-slices of that
    hair, cut into ``count`` bones. Gives those vertices chain weights (fading to Head at the root) and returns the
    bones as (name, parent, Blender position), or [] if nothing hangs there.
    """
    co = bt.vertices(part)
    if side:
        tail = np.flatnonzero((co[:, 0] - frame["centre"][0]) * side > beside)
    else:
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
