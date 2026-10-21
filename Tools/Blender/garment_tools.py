"""Blender side of generated clothing (decision 0008).

Garments cut from a Hunyuan3D scan of a dressed character and made into parts for the canonical generated body:

* the scan aligned to the canonical skeleton (the same move that aligns the masculine base), so a text-to-image
  outfit, which never quite shares our body, becomes comparable with it,
* split into garments by its texture's colour blocks and the body regions they sit on (Tools/Blender/outfits.json),
* each garment conformed to the body at its layer offset (style bible section 3) and settled with cloth simulation,
* skinned and given every body shape by transfer from the body, with a recolour mask from the colour blocks.

Coordinates: Blender is Z-up with the front at -Y, the character's left at +X (as in character_tools).
"""

import json
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import body_tools as bt
import character_tools as ct
import prop_tools as pt

OUTFITS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "outfits.json")
REGION_NAMES = {index: name for name, index in ct.REGIONS.items()}


def load_outfit(key):
    with open(OUTFITS, encoding="utf-8") as f:
        return json.load(f)[key]


# ---------------------------------------------------------------------------------------------------- the body

def load_body(fbx_path):
    """
    The canonical body from make_generated_body.py's FBX, with its transforms baked so its mesh data is in world
    space (character_tools.transfer_from_body reads raw vertex positions), plus its joints from the .skeleton.json.
    The imported armature is dropped: parts get one rebuilt from the same skeleton, exactly as the body's was.
    Returns (body, skeleton, joints in Blender coordinates).
    """
    bpy.ops.import_scene.fbx(filepath=fbx_path, axis_forward="-Z", axis_up="Y", automatic_bone_orientation=False)
    body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    armature = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for modifier in list(body.modifiers):
        body.modifiers.remove(modifier)
    bpy.data.objects.remove(armature)
    with open(os.path.splitext(fbx_path)[0] + ".skeleton.json", encoding="utf-8") as f:
        bones = json.load(f)["bones"]
    skeleton = [(b["name"], b["parent"], tuple(b["position"])) for b in bones]
    joints = {name: np.array(ct.unity_to_blender(position)) for name, _, position in skeleton}
    return body, skeleton, joints


def body_regions(body):
    """The body region name of every body vertex, from the `Col` attribute's R channel."""
    mesh = body.data
    attribute = mesh.color_attributes[ct.COLOUR_ATTRIBUTE]
    values = np.empty(len(attribute.data) * 4)
    # The FBX importer stores the linear region bytes as sRGB bytes: reading "color" would linearise them and turn
    # every region id into 0 or 1. "color_srgb" gives the bytes back as they were written.
    attribute.data.foreach_get("color_srgb" if attribute.data_type == "BYTE_COLOR" else "color", values)
    values = values.reshape(-1, 4)
    if attribute.domain == "CORNER":  # an FBX import brings vertex colours back per face corner
        loop_vertex = np.empty(len(mesh.loops), dtype=np.int64)
        mesh.loops.foreach_get("vertex_index", loop_vertex)
        per_vertex = np.zeros((len(mesh.vertices), 4))
        per_vertex[loop_vertex] = values
        values = per_vertex
    ids = np.rint(values[:, 0] * 255).astype(int)
    return np.array([REGION_NAMES.get(i, "Head") for i in ids])


# ---------------------------------------------------------------------------------------------------- the scan

def load_scan(textured_glb, height):
    """The textured scan: imported, placed, welded (UV seams split it), aligned joints-to-joints later."""
    scan = pt.import_glb(textured_glb)
    pt.place(scan, height)
    pt.clean(scan, weld_distance=height * 1e-6, min_island_fraction=0.0005)
    return scan


def colour_clusters(scan, clusters=8, seed=0):
    """
    Per-face colour cluster ids from the scan's albedo (prop_tools.textured_colours, which also copes with the
    wrapper's upside-down UVs), k-means in OKLab, plus each cluster's mean linear colour and the face areas.
    """
    image = pt.base_colour_image(scan)
    colours, areas, _ = pt.textured_colours(scan, image)
    points = pt.linear_to_oklab(colours)
    weights = areas / areas.sum()
    rng = np.random.default_rng(seed)
    centres = [points[rng.choice(len(points), p=weights)]]
    for _ in range(1, clusters):
        distance = np.min([((points - c) ** 2).sum(1) for c in centres], axis=0)
        centres.append(points[rng.choice(len(points), p=distance / distance.sum())])
    centres = np.array(centres)
    for _ in range(40):
        labels = np.argmin(((points[:, None, :] - centres[None]) ** 2).sum(-1), axis=1)
        moved = np.array([np.average(points[labels == k], axis=0, weights=weights[labels == k]) if (labels == k).any() else centres[k]
                          for k in range(len(centres))])
        if np.allclose(moved, centres, atol=1e-6):
            break
        centres = moved
    mean_colour = np.array([np.average(colours[labels == k], axis=0, weights=areas[labels == k]) if (labels == k).any() else np.zeros(3)
                            for k in range(len(centres))])
    return labels, mean_colour, areas


def lit_colours(scan, labels, areas, share=0.2):
    """
    Each colour cluster's colour where it's lit: the area-weighted mean of its lightest ``share`` of faces. The
    generated texture bakes in the image's soft shading, so a cluster's plain mean came out darker than the garment
    looks in the image; the game's toon shader adds its own shade.
    """
    colours, _, _ = pt.textured_colours(scan, pt.base_colour_image(scan))
    light = pt.linear_to_oklab(colours)[:, 0]
    result = np.zeros((labels.max() + 1, 3))
    for k in range(labels.max() + 1):
        faces = np.flatnonzero(labels == k)
        if len(faces) == 0:
            continue
        lit = faces[light[faces] >= np.quantile(light[faces], 1.0 - share)]
        result[k] = np.average(colours[lit], axis=0, weights=areas[lit])
    return result


VIVID = 1.4  # chroma boost for colours taken from the source image
TRUSTED = None  # the clusters image_colours last took from the image (the rest are the texture's, lightened)


def image_colours(scan, labels, image_path, fallback, share=1.0, least=30, hue_reach=0.035, gain_reach=0.25):
    """
    Each colour cluster's colour as the source image paints it (the game's sunlight brightens it further). The scan (placed, not yet aligned) is the
    image's front view in 3D, so each face seen from the front samples the image pixel in front of it, the figure's
    height in the image matching the scan's; a cluster takes the mean of the lightest ``share`` of its faces'
    samples. Hunyuan's texture comes out a quarter darker than the image it was made from, and matching by colour
    alone took khaki for skin. The image is lighter than the texture by a gain (the median lightness ratio over the
    clusters whose samples match the texture's hue and chroma within ``hue_reach``); a cluster's samples are trusted
    if they match that well and their ratio is within ``gain_reach`` of the gain: thin or narrow features (soles, a
    waistband, a tank top, legs close together) sample their neighbours. Any other cluster keeps its ``fallback``
    colour (linear) lightened by the gain.
    """
    from mathutils import Vector
    image = bpy.data.images.load(image_path, check_existing=True)
    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    srgb = pixels.reshape(height, width, 4)[::-1, :, :3].astype(float)  # Blender's rows run bottom-up
    lab = pt.linear_to_oklab(pt.srgb_to_linear(srgb).reshape(-1, 3)).reshape(height, width, 3)
    edges = np.concatenate([lab[:, :12], lab[:, -12:]], axis=1)
    foreground = ((lab - np.median(edges, axis=1)[:, None, :]) ** 2).sum(axis=2) > 0.06 ** 2
    rows = np.flatnonzero(foreground[:, width // 4: 3 * width // 4].sum(axis=1) > 8)
    top, bottom = rows.min(), rows.max()
    distance = ((lab - np.median(edges, axis=1)[:, None, :]) ** 2).sum(axis=2)
    centre_x = float(np.median(np.nonzero(distance[top:bottom] > 0.07 ** 2)[1]))  # the figure's middle (T-pose)
    co = bt.vertices(scan)
    scale = (bottom - top) / float(co[:, 2].max() - co[:, 2].min())  # pixels per metre
    floor = float(co[:, 2].min())
    print(f"[colour] image {width}x{height}: figure rows {top}-{bottom}, centre x {centre_x:.0f}, {scale:.0f} px/m")

    mesh = scan.data
    centres = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)
    normals = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    tree = BVHTree.FromPolygons([tuple(v) for v in co], [tuple(p.vertices) for p in mesh.polygons])
    samples = np.full((len(centres), 3), np.nan)
    for f in np.flatnonzero(normals[:, 1] < -0.3):
        x, z = centres[f, 0], centres[f, 2]
        _, _, hit, _ = tree.ray_cast(Vector((x, -5.0, z)), Vector((0.0, 1.0, 0.0)), 10.0)
        if hit != f:
            continue
        px, py = int(round(centre_x + x * scale)), int(round(bottom - (z - floor) * scale))
        if 0 <= px < width and 0 <= py < height:
            samples[f] = srgb[py, px]
    fallback = np.array(fallback, dtype=float)
    texture = pt.linear_to_oklab(fallback)
    seen, matches, ratios, counts = {}, {}, {}, {}
    for k in range(len(fallback)):
        taken = samples[(labels == k) & ~np.isnan(samples[:, 0])]
        if len(taken) < least:
            continue
        linear = pt.srgb_to_linear(taken)
        light = pt.linear_to_oklab(linear)[:, 0]
        seen[k] = linear[light >= np.quantile(light, 1.0 - share)].mean(axis=0)
        lab_k = pt.linear_to_oklab(seen[k])
        matches[k] = np.linalg.norm(lab_k[1:] - texture[k][1:]) < hue_reach
        ratios[k], counts[k] = lab_k[0] / max(texture[k][0], 1e-6), len(taken)
    candidates = [k for k in seen if matches[k]]
    if candidates:
        order = sorted(candidates, key=lambda k: ratios[k])
        weights = np.cumsum([counts[k] for k in order])
        gain = float(ratios[order[int(np.searchsorted(weights, weights[-1] / 2))]])  # weighted median
    else:
        gain = 1.0
    # Lightened in OKLab: lightness times the gain, but never more than halfway to white (whites don't clip), with
    # chroma following lightness up to a fifth more.
    light = texture[:, 0]
    lifted = np.minimum(light * gain, light + 0.5 * (1.0 - light))
    scaled = texture.copy()
    scaled[:, 0] = lifted
    scaled[:, 1:] *= np.minimum(lifted / np.maximum(light, 1e-6), 1.2)[:, None]
    result, trusted = np.clip(pt.oklab_to_linear(scaled), 0.0, 1.0), []
    for k in candidates:
        if abs(ratios[k] / gain - 1.0) < gain_reach:
            result[k] = seen[k]
            trusted.append(k)
    print(f"[colour] image colours for clusters {sorted(trusted)}; the rest lightened by the gain {gain:.2f}")
    global TRUSTED
    TRUSTED = set(trusted)
    # More saturated, as cel-shaded art paints them: the image is a soft 3D render, and the game's light and ambient
    # grey them further (a target cel render of the same outfit had twice the chroma ours rendered with).
    vivid = pt.linear_to_oklab(result)
    vivid[:, 1:] *= VIVID
    # Light neutrals are white (a collar, canvas sneakers): the image's whites averaged over its soft shading came out
    # pinkish grey.
    white = (vivid[:, 0] > 0.78) & (np.hypot(vivid[:, 1], vivid[:, 2]) < 0.04)
    vivid[white, 0] = np.maximum(vivid[white, 0], 0.97)
    vivid[white, 1:] *= 0.3
    return np.clip(pt.oklab_to_linear(vivid), 0.0, 1.0)


def face_regions(scan, body, regions):
    """The body region under each scan face: that of the nearest body vertex to the face's centre."""
    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], [tuple(p.vertices) for p in body.data.polygons])
    polygon_vertices = [list(p.vertices) for p in body.data.polygons]
    centres = np.empty(len(scan.data.polygons) * 3)
    scan.data.polygons.foreach_get("center", centres)
    body_co = bt.vertices(body)
    result = []
    for c in centres.reshape(-1, 3):
        location, _, index, _ = tree.find_nearest(Vector(tuple(c)))
        corners = polygon_vertices[index]
        nearest = corners[int(np.argmin(np.linalg.norm(body_co[corners] - np.array(location), axis=1)))]
        result.append(regions[nearest])
    return np.array(result)


def skin_faces(scan):
    """
    Faces that are bare skin in every outfit: the front of the face and the hands (the T-pose's extremes).

    Both are straight from the image. The top and back of the head aren't: Hunyuan paints the views it has to invent,
    and has given a bald head dark grey hair there.
    """
    mesh = scan.data
    centres, normals = (np.empty(len(mesh.polygons) * 3) for _ in range(2))
    mesh.polygons.foreach_get("center", centres)
    mesh.polygons.foreach_get("normal", normals)
    centres, normals = centres.reshape(-1, 3), normals.reshape(-1, 3)
    x, z = centres[:, 0], centres[:, 2]
    face = (z > z.max() - 0.22) & (z < z.max() - 0.06) & (np.abs(x) < 0.06) & (normals[:, 1] < -0.5)
    hands = np.abs(x) > np.abs(x).max() - 0.08
    return face | hands


def hint_distances(outfit, lab):
    """
    (clusters x garments) OKLab distance from each colour cluster to the nearest of each garment's "colours" (the
    prompt's, in outfits.json), or None if any garment has none.
    """
    hints = [[pt.linear_to_oklab(pt.srgb_to_linear(pt.hex_to_srgb(h.lstrip("#")))) for h in g.get("colours", [])]
             for g in outfit["garments"]]
    if not all(hints):
        return None
    return np.array([[min(pt.colour_distance(lab[k], h) for h in hs) for hs in hints] for k in range(len(lab))])


def palette_clusters(outfit, lab, index, skin):
    """
    The colour clusters that may be a recolour region of garment ``index``: not skin, and (given the outfit's colour
    hints) nearer one of this garment's colours than any other garment's, ties included (two garments can both be
    white). Faces of other clusters on the garment, like khaki shorts' texture reaching onto a shirt's hem, take the
    nearest allowed region instead.
    """
    distances = hint_distances(outfit, lab)
    allowed = set(range(len(lab))) - set(skin)
    if distances is not None:
        allowed = {k for k in allowed if distances[k, index] <= distances[k].min() + 0.02 ** 2}
    return allowed


# A cluster with less than this share of its garment-region area on regions only one garment lists is decided by the
# outfit's colour hints instead: the few faces it has there are stray pixels, not its garment.
SHARED_ONLY = 0.5


def assign_garments(outfit, labels, mean_colour, areas, regions, bare):
    """
    The garment of each scan face (an index into outfit["garments"], -1 for skin or unclaimed).

    Skin is the colour cluster covering most of ``bare`` (skin_faces: matching body regions instead depended on the
    alignment being exact), and any cluster close to it.
    Each other cluster belongs to the garment whose regions it covers most, counting only regions no other garment
    lists. A cluster lying mostly on regions two garments share (a waistband between a top and shorts, a belt, boot
    shafts over trouser legs) goes instead to the garment whose "colours" (the prompt's colours, as hex, in
    outfits.json) are nearest its own, ties going to the one it covers most alone (a tank top's black trim and boots'
    black soles both list black). A face goes to the garments that list its region; among several, to its cluster's
    garment, or if that isn't one of them, to the one whose colours are nearest.
    """
    garments = outfit["garments"]
    lab = pt.linear_to_oklab(mean_colour)
    skin_cluster = int(np.bincount(labels[bare], weights=areas[bare], minlength=len(lab)).argmax())
    # Lightness counts half (as for palettes): skin in shadow is still skin, and khaki shorts aren't.
    skin = {k for k in range(len(lab)) if pt.colour_distance(lab[k], lab[skin_cluster]) < 0.06 ** 2}

    claims = [set(g["regions"]) for g in garments]
    exclusive = [c - set().union(*(o for j, o in enumerate(claims) if j != i)) for i, c in enumerate(claims)]
    claimed = list(set().union(*claims))
    distances = hint_distances(outfit, lab)
    owner, scores = {}, {}
    for k in range(len(lab)):
        if k in skin:
            continue
        scores[k] = [areas[(labels == k) & np.isin(regions, list(e))].sum() for e in exclusive]
        on_garments = areas[(labels == k) & np.isin(regions, claimed)].sum()
        if max(scores[k]) < SHARED_ONLY * on_garments and distances is not None:
            owner[k] = _nearest_colour(distances[k], scores[k], range(len(garments)))
        else:
            owner[k] = int(np.argmax(scores[k])) if max(scores[k]) > 0 else -1

    result = np.full(len(labels), -1)
    for f in range(len(labels)):
        k = labels[f]
        if k in skin:
            continue
        listing = [i for i, c in enumerate(claims) if regions[f] in c]
        if len(listing) == 1:
            result[f] = listing[0]
        elif len(listing) > 1:
            if owner.get(k, -1) in listing:
                result[f] = owner[k]
            else:
                result[f] = _nearest_colour(distances[k], scores[k], listing) if distances is not None else listing[0]
    return result, skin_cluster, skin


def _nearest_colour(distances, scores, candidates):
    """Of ``candidates``, the garment whose colours are nearest (``distances``), ties to the higher ``scores``."""
    candidates = list(candidates)
    best = min(distances[i] for i in candidates)
    return max((i for i in candidates if distances[i] <= best + 1e-4), key=lambda i: scores[i])


def fill_holes(scan, garment, areas, largest=0.004):
    """
    Pockets of non-garment faces wholly surrounded by one garment join it: shading in the texture reads as skin here
    and there (a collar's shadow, a crease in the shorts) and would otherwise leave holes. Pockets bigger than
    ``largest`` of the surface stay as they are (real skin showing through, like the neck opening).
    """
    bm = bmesh.new()
    bm.from_mesh(scan.data)
    bm.faces.ensure_lookup_table()
    result = garment.copy()
    seen = np.zeros(len(garment), dtype=bool)
    total = areas.sum()
    for start in range(len(garment)):
        if seen[start] or garment[start] >= 0:
            continue
        pocket, border, stack = [], set(), [start]
        seen[start] = True
        while stack:
            f = stack.pop()
            pocket.append(f)
            for edge in bm.faces[f].edges:
                for other in edge.link_faces:
                    if garment[other.index] >= 0:
                        border.add(int(garment[other.index]))
                    elif not seen[other.index]:
                        seen[other.index] = True
                        stack.append(other.index)
        if len(border) == 1 and areas[pocket].sum() < largest * total:
            result[pocket] = border.pop()
    bm.free()
    return result


def drop_islands(scan, garment, areas, keep_fraction=0.15):
    """
    Pieces of a garment not joined to its main ones go back to skin: shadowed skin or a stray colour patch on a leg
    isn't part of the shorts. A piece stays if it is at least ``keep_fraction`` of the garment's largest (so both
    shoes stay).
    """
    bm = bmesh.new()
    bm.from_mesh(scan.data)
    bm.faces.ensure_lookup_table()
    result = garment.copy()
    seen = np.zeros(len(garment), dtype=bool)
    pieces = {}
    for start in range(len(garment)):
        if seen[start] or garment[start] < 0:
            continue
        piece, stack = [], [start]
        seen[start] = True
        while stack:
            f = stack.pop()
            piece.append(f)
            for edge in bm.faces[f].edges:
                for other in edge.link_faces:
                    if not seen[other.index] and garment[other.index] == garment[start]:
                        seen[other.index] = True
                        stack.append(other.index)
        pieces.setdefault(garment[start], []).append(piece)
    bm.free()
    for g, found in pieces.items():
        largest = max(areas[p].sum() for p in found)
        for piece in found:
            if areas[piece].sum() < keep_fraction * largest:
                result[piece] = -1
    return result


# ---------------------------------------------------------------------------------------------------- the part

# How far each slot sits off the body. The style bible's layering order (section 3) with tighter gaps than its
# stand-in figures (12-22 mm): those suit capsule tubes, and on the generated body they made every garment bulky.
# Every outfit's pieces mix with every other's, so tops and bottoms meet at one waist line: every bottom reaches up to
# it and every untucked top down past it (mixed, a T-shirt cut to sit over its own shorts' waistband left a strip of
# midriff over another outfit's low joggers). Metres above the hip joint, in the bind pose.
WAIST_ABOVE_HIPS = 0.175
WAIST_OVERLAP = 0.015  # how far below the waist line an untucked top reaches

LAYER_OFFSET = {"legwear": 0.002, "top": 0.006, "bottom": 0.006, "onepiece": 0.006, "shoes": 0.006, "outer": 0.016}

# The bones a slot may follow (character_tools.transfer_from_body's mask), so a sleeve can't follow the torso.
SIDES = (".L", ".R")
FOLLOWS = {
    "top": ["Hips", "Spine", "Chest", "Neck"] + [b + s for b in ("UpperArm", "LowerArm") for s in SIDES],
    "outer": ["Hips", "Spine", "Chest", "Neck"] + [b + s for b in ("UpperArm", "LowerArm", "Hand") for s in SIDES],
    "bottom": ["Hips", "Spine"] + [b + s for b in ("UpperLeg", "LowerLeg") for s in SIDES],
    "onepiece": ["Hips", "Spine", "Chest", "Neck"] + [b + s for b in ("UpperArm", "LowerArm", "UpperLeg", "LowerLeg") for s in SIDES],
    "shoes": [b + s for b in ("Foot", "LowerLeg") for s in SIDES],
    "legwear": ["Hips"] + [b + s for b in ("UpperLeg", "LowerLeg", "Foot") for s in SIDES],
}


def extract(scan, garment, index, name):
    """A new object holding only the scan faces of garment ``index``, with each face's colour cluster kept."""
    part = scan.copy()
    part.data = scan.data.copy()
    part.name = part.data.name = name
    bpy.context.scene.collection.objects.link(part)
    for modifier in list(part.modifiers):
        part.modifiers.remove(modifier)
    part.vertex_groups.clear()
    bm = bmesh.new()
    bm.from_mesh(part.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[f] for f in np.flatnonzero(garment != index)], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(part.data)
    bm.free()
    part.data.update()
    return part


def conform(part, body, offset, passes=3):
    """
    Pushes every vertex at least ``offset`` outside the body along the body's surface normal, then relaxes the
    pushed-out movement so no dent or ridge is left. Text-to-image outfits sit on bodies a little different from the
    canonical one: tight garments end up inside it in places, and this is what puts them back on top.
    """
    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], [tuple(p.vertices) for p in body.data.polygons])
    mesh = part.data
    edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", edges)
    a, b = edges[0::2], edges[1::2]
    count = np.maximum(np.bincount(np.concatenate([a, b]), minlength=len(mesh.vertices)), 1).astype(float)
    co = bt.vertices(part)
    for _ in range(passes):
        push = np.zeros_like(co)
        for i, p in enumerate(co):
            location, normal, _, _ = tree.find_nearest(Vector(tuple(p)))
            signed = np.dot(p - np.array(location), np.array(normal))
            if signed < offset:
                push[i] = np.array(normal) * (offset - signed)
        relaxed = push
        for _ in range(3):  # spread the push so neighbours move together, but never below what a vertex needs
            total = np.zeros_like(relaxed)
            np.add.at(total, a, relaxed[b])
            np.add.at(total, b, relaxed[a])
            relaxed = 0.5 * relaxed + 0.5 * total / count[:, None]
        needs_more = np.linalg.norm(push, axis=1) > np.linalg.norm(relaxed, axis=1)
        co = co + np.where(needs_more[:, None], push, relaxed)
    mesh.vertices.foreach_set("co", co.ravel())
    mesh.update()


def smooth_outside(part, body, offset, passes=5, iterations=6):
    """Relaxes the garment's surface (umbrella smoothing) and pushes it back outside the body after each pass, so
    conforming's small dents and the scan's facets even out without anything sinking into the body."""
    for _ in range(passes):
        bt.relax(part, np.ones(len(part.data.vertices)), iterations, taubin=True)
        conform(part, body, offset, passes=1)


def settle(part, body, offset, frames=12, bending=40.0):
    """
    Cloth simulation against the body, so the conformed garment drapes and nothing clips: the body is a collider,
    vertices lying close on the body (waistbands, shoulders, the tops of shoes) are pinned, and the rest falls under
    gravity for ``frames`` frames before the result is applied.
    """
    scene = bpy.context.scene
    collision = body.modifiers.new("Collision", "COLLISION")
    body.collision.thickness_outer = offset * 0.8
    body.collision.thickness_inner = 0.002
    body.collision.cloth_friction = 8.0

    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], [tuple(p.vertices) for p in body.data.polygons])
    co = bt.vertices(part)
    gap = np.array([(Vector(tuple(p)) - tree.find_nearest(Vector(tuple(p)))[0]).length for p in co])
    pin = np.clip(1.0 - (gap - offset) / 0.006, 0.0, 1.0)  # within 6 mm of where it should sit: held
    bt.set_group(part, "Pin", pin)

    cloth = part.modifiers.new("Cloth", "CLOTH")
    settings = cloth.settings
    settings.quality = 8
    settings.mass = 0.25
    # Stiff, heavily damped and brief: a settle, not a fall. Soft cloth (bending 1) buckled under the collision
    # pushes and crumpled every garment like paper.
    settings.tension_stiffness = settings.compression_stiffness = 40.0
    settings.shear_stiffness = 20.0
    settings.bending_stiffness = bending
    settings.air_damping = 8.0
    settings.vertex_group_mass = "Pin"
    settings.pin_stiffness = 1.0
    cloth.collision_settings.use_collision = True
    cloth.collision_settings.distance_min = offset * 0.5
    cloth.collision_settings.use_self_collision = False
    cloth.point_cache.frame_start = 1
    cloth.point_cache.frame_end = frames
    scene.frame_start, scene.frame_end = 1, frames
    for frame in range(1, frames + 1):
        scene.frame_set(frame)
    bpy.context.view_layer.objects.active = part
    bpy.ops.object.modifier_apply(modifier=cloth.name)
    scene.frame_set(1)
    body.modifiers.remove(collision)
    part.vertex_groups.remove(part.vertex_groups["Pin"])
    return float(pin.mean())


def tag_clusters(scan, labels):
    """Stores each scan face's colour cluster as a face attribute, so it survives extraction and decimation."""
    attribute = scan.data.attributes.new("cluster", "INT", "FACE")
    attribute.data.foreach_set("value", np.asarray(labels, dtype=np.int32))


def vertex_clusters(part):
    """
    The colour cluster of each vertex (the most common among its faces, by area), stored as a point attribute so it
    survives decimation and cloth simulation. Returns the array.
    """
    mesh = part.data
    face_cluster = np.empty(len(mesh.polygons), dtype=np.int32)
    mesh.attributes["cluster"].data.foreach_get("value", face_cluster)
    votes = [dict() for _ in mesh.vertices]
    for polygon, cluster in zip(mesh.polygons, face_cluster):
        for v in polygon.vertices:
            votes[v][cluster] = votes[v].get(cluster, 0.0) + polygon.area
    result = np.array([max(v, key=v.get) if v else -1 for v in votes], dtype=np.int32)
    if "vcluster" in mesh.attributes:
        mesh.attributes.remove(mesh.attributes["vcluster"])
    mesh.attributes.new("vcluster", "INT", "POINT").data.foreach_set("value", result)
    return result


def read_vertex_clusters(part):
    values = np.empty(len(part.data.vertices), dtype=np.int32)
    part.data.attributes["vcluster"].data.foreach_get("value", values)
    return values


def recolour_mask(part, vertex_cluster, areas_by_cluster, lab, min_share=0.04, sigma=0.012, sole=False, most=3):
    """
    The `Col` attribute for a part: R = 0 (parts carry no region), G, B, A = recolour regions 1-3 by the part's own
    colour blocks, largest first (main fabric, then trim, then a secondary panel, as the style bible's convention).
    Blocks that are shades of one colour (_shades: the texture's shading split burgundy into a lit and a dark cluster,
    and the dark one came out as blotches) count as one block, coloured as the largest. A block under ``min_share`` of
    the part is stray texture, not a panel: it joins the region nearest its colour (``lab``: each cluster's OKLab
    colour). Trims along the garment's edges become even bands (_edge_trims), and with ``sole`` a shoe's sole a band
    round its bottom (_sole). Each region is blurred over the surface
    (``sigma`` metres) and the
    mesh cut along its edge, and the regions are written per face corner: the shader blends vertex colours across a
    triangle, which on the body's long slivers smeared every colour edge into streaks. Returns the colour cluster of
    each region, in order. At most ``most`` regions: a garment the prompt gives one colour has one, as the scan's
    shading and stray patches (skin on a black cocktail dress, purple on a blue gown) are no panel of its own.
    """
    if not areas_by_cluster:  # no colour block of its own (white socks between white shoes and pale skin): its commonest
        known = vertex_cluster[vertex_cluster >= 0]
        areas_by_cluster = {int(np.bincount(known).argmax()) if len(known) else 0: 1.0}
    shade_of = _shades(areas_by_cluster, lab)
    blocks = {}
    for k, area in areas_by_cluster.items():
        blocks[shade_of[k]] = blocks.get(shade_of[k], 0.0) + area
    total = max(sum(blocks.values()), 1e-12)
    ranked = [k for k in sorted(blocks, key=lambda k: -blocks[k]) if blocks[k] / total >= min_share][:most]
    ranked = ranked or sorted(blocks, key=lambda k: -blocks[k])[:1]
    region_of = {k: ranked.index(shade_of[k]) if shade_of.get(k) in ranked else
                 int(np.argmin([pt.colour_distance(lab[k], lab[r]) for r in ranked])) for k in range(len(lab))}
    region = np.array([region_of.get(int(k), 0) if k >= 0 else 0 for k in vertex_cluster])
    trims, sole_line = [], None
    if len(ranked) > 1:
        region, trims = _edge_trims(part, region, len(ranked))
        if sole:
            region, sole_line = _sole(part, region, len(ranked))
    mesh = part.data
    if len(ranked) > 1:
        neighbours = _surface_neighbours(part, sigma)
        fields = [_blur(neighbours, (region == r).astype(float)) for r in range(len(ranked))]
        bm = bmesh.new()
        bm.from_mesh(mesh)
        layers = [bm.verts.layers.float.new(f"region{r}") for r in range(len(ranked))]
        for vertex in bm.verts:
            for layer, field in zip(layers, fields):
                vertex[layer] = field[vertex.index]
        for layer in layers[1:]:  # each region's edge, where its share crosses a half
            bm.verts.index_update()
            bm.verts.ensure_lookup_table()
            field = np.array([v[layer] for v in bm.verts])
            _cut_along(bm, np.where(np.abs(field - 0.5) < 1e-4, 0.5 + 1e-4, field), 0.5)
        # Trim bands are cut exactly at their width from their edge: the blurred fields left them uneven.
        from mathutils.kdtree import KDTree
        bands = []
        for points, width, trim in trims:
            tree = KDTree(len(points))
            for i, p in enumerate(points):
                tree.insert(p, i)
            tree.balance()
            bm.verts.index_update()
            bm.verts.ensure_lookup_table()
            field = np.array([tree.find(v.co)[2] for v in bm.verts]) - width
            _cut_along(bm, np.where(np.abs(field) < 1e-5, 1e-5, field), 0.0)
            bands.append((tree, width, trim))
        if sole_line is not None:  # the sole's top, level all round
            bm.verts.index_update()
            bm.verts.ensure_lookup_table()
            field = np.array([v.co.z for v in bm.verts]) - sole_line[1]
            _cut_along(bm, np.where(np.abs(field) < 1e-5, 1e-5, field), 0.0)
        face_region = []
        for f in bm.faces:
            r = int(np.argmax([np.mean([v[layer] for v in f.verts]) for layer in layers]))
            centre = f.calc_center_median()
            for tree, width, trim in bands:
                d = tree.find(centre)[2]
                if d < width:
                    r = trim
                elif r == trim and d < width + 0.03:
                    r = 0
            if sole_line is not None:
                r = sole_line[0] if centre.z < sole_line[1] else (0 if r == sole_line[0] else r)
            face_region.append(r)
        for layer in layers:
            bm.verts.layers.float.remove(layer)
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
    else:
        face_region = [0] * len(mesh.polygons)
    if ct.COLOUR_ATTRIBUTE in mesh.color_attributes:
        mesh.color_attributes.remove(mesh.color_attributes[ct.COLOUR_ATTRIBUTE])
    colours = mesh.color_attributes.new(ct.COLOUR_ATTRIBUTE, "FLOAT_COLOR", "CORNER")
    corner_region = np.repeat(np.array(face_region), [len(p.vertices) for p in mesh.polygons])
    values = np.zeros((len(mesh.loops), 4))
    values[np.arange(len(mesh.loops)), corner_region + 1] = 1.0
    colours.data.foreach_set("color", values.ravel())
    mesh.color_attributes.active_color = colours
    return ranked


def _sole(part, region, regions, height=0.016, look=0.035, least=0.08):
    """
    ``region`` with a shoe's sole an even band ``height`` metres up from its lowest point, in the region other than
    the upper's that covers most of the bottom ``look`` metres (at least ``least`` of it): red rubber soles came out
    as pink specks on white blobs.
    """
    z = bt.vertices(part)[:, 2]
    low = z < z.min() + look
    counts = np.bincount(region[low], minlength=regions) / max(low.sum(), 1)
    trim = int(np.argmax(counts[1:])) + 1
    if counts[trim] < least:
        return region, None
    result = region.copy()
    result[result == trim] = 0  # the scan's red ran up the heel in patches
    result[z < z.min() + height] = trim
    return result, (trim, float(z.min()) + height)


def _edge_trims(part, region, regions, band=0.02, dominant=0.15, widths=(0.008, 0.02)):
    """
    ``region`` (per vertex) with each edge trim made an even band: a collar round the neck opening, cuffs round the
    sleeve ends, a waistband, a tank top's edging. A region other than the main one (0) that covers at least
    ``dominant`` of the ``band`` metres along an edge loop is that loop's trim; it then covers that loop's whole band
    out to its own width (how far it reached from the loop, within ``widths``), and only there: the scan's cuffs
    reached the sleeve ends in patches, and white specks elsewhere on the shirt were its highlights.
    """
    from mathutils.kdtree import KDTree
    bm = bmesh.new()
    bm.from_mesh(part.data)
    bm.edges.ensure_lookup_table()
    boundary = {e for e in bm.edges if e.is_boundary}
    loops, seen = [], set()
    for start in boundary:
        if start in seen:
            continue
        stack, verts = [start], set()
        seen.add(start)
        while stack:
            edge = stack.pop()
            verts.update(v.index for v in edge.verts)
            for v in edge.verts:
                for other in v.link_edges:
                    if other in boundary and other not in seen:
                        seen.add(other)
                        stack.append(other)
        loops.append(sorted(verts))
    bm.free()

    co = bt.vertices(part)
    no_trim = np.zeros(len(co), dtype=np.int32)
    if "no_trim" in part.data.attributes:  # pieces whose edges aren't garment edges (a hood's cut)
        part.data.attributes["no_trim"].data.foreach_get("value", no_trim)
    result = region.copy()
    made = []
    near_trim = {r: np.zeros(len(co), dtype=bool) for r in range(1, regions)}
    for loop in loops:
        if len(loop) < 8 or no_trim[loop].mean() > 0.5:
            continue
        tree = KDTree(len(loop))
        for i, v in enumerate(loop):
            tree.insert(co[v], i)
        tree.balance()
        distance = np.array([tree.find(p)[2] for p in co])
        inside = distance < band
        counts = np.bincount(region[inside], minlength=regions) / max(inside.sum(), 1)
        trim = int(np.argmax(counts[1:])) + 1 if regions > 1 else 0
        print(f"[trim] edge loop of {len(loop)}: shares {np.round(counts, 2).tolist()}"
              + (f", region {trim} band" if trim and counts[trim] >= dominant else ""))
        if trim == 0 or counts[trim] < dominant:
            continue
        reach = distance[(region == trim) & (distance < 2.5 * band)]
        width = float(np.clip(np.percentile(reach, 80), *widths)) if len(reach) else widths[0]
        result[(distance >= width) & (distance < width + 0.025) & (result == trim)] = 0  # ragged leftovers past the band
        result[distance < width] = trim
        near_trim[trim] |= distance < width
        made.append((co[loop].copy(), width, trim))
    for trim, near in near_trim.items():
        if near.any():  # a trim colour away from its edges is stray texture
            result[(result == trim) & ~near] = 0
    return result, made


def _shades(areas_by_cluster, lab, hue_reach=25.0, alike=0.55, grey=0.025, grey_alike=0.6):
    """
    Each colour cluster's block: the largest cluster it is a shade of (lit and shaded versions of one colour). Two
    coloured clusters are shades of one colour if their hues are within ``hue_reach`` degrees and their saturations
    (chroma over lightness, which shading keeps) within ``alike`` of each other; two greys (chroma under ``grey``) if
    their lightnesses are within ``grey_alike``. A colour and a grey never are: black trim stays apart from burgundy.
    """
    clusters = sorted(areas_by_cluster, key=lambda k: -areas_by_cluster[k])
    light = {k: float(lab[k][0]) for k in clusters}
    chroma = {k: float(math.hypot(lab[k][1], lab[k][2])) for k in clusters}
    hue = {k: math.degrees(math.atan2(lab[k][2], lab[k][1])) for k in clusters}

    def same(a, b):
        if chroma[a] < grey and chroma[b] < grey:
            return min(light[a], light[b]) / max(light[a], light[b], 1e-9) >= grey_alike
        if chroma[a] < grey or chroma[b] < grey:
            return False
        turn = abs((hue[a] - hue[b] + 180.0) % 360.0 - 180.0)
        sat_a, sat_b = chroma[a] / max(light[a], 1e-9), chroma[b] / max(light[b], 1e-9)
        return turn < hue_reach and min(sat_a, sat_b) / max(sat_a, sat_b) >= alike

    block = {}
    for k in clusters:  # largest first, so each block is named after its largest cluster
        block[k] = next((block[j] for j in clusters if j in block and same(j, k)), k)
    return block


def extract_hood(scan, garment, index, body, joints, areas, name, reach=0.03, budget=3000):
    """
    A hood (down, resting round the neck and on the upper back) as its own piece, from the scan's geometry: the
    garment's faces around the neck that stand more than ``reach`` off the body. A fitted garment can only stand a few
    centimetres off the skin along its normals, so it flattened a rolled hood into a high collar. The piece is lifted
    5 mm off the scan surface (outside the fitted garment where the two coincide), kept off the body, and follows the
    chest and neck with every body shape. Returns it, or None if there's no hood.
    """
    from mathutils import Vector
    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(body)], [tuple(p.vertices) for p in body.data.polygons])
    centres = np.empty(len(scan.data.polygons) * 3)
    scan.data.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)
    neck, shoulder_z = np.asarray(joints["Neck"], dtype=float), float(joints["UpperArm.L"][2])
    zone = (centres[:, 2] > shoulder_z - 0.06) & (np.hypot(centres[:, 0] - neck[0], centres[:, 1] - neck[1]) < 0.2)
    off = np.zeros(len(centres))
    for f in np.flatnonzero(zone & (garment == index)):
        off[f] = tree.find_nearest(Vector(tuple(centres[f])))[3]
    chosen = zone & (garment == index) & (off > reach)
    # Trim the fringe the cut-off leaves (faces hanging on by one edge), then drop stray bits.
    bm = bmesh.new()
    bm.from_mesh(scan.data)
    bm.faces.ensure_lookup_table()
    neighbours = [[o.index for e in f.edges for o in e.link_faces if o is not f] for f in bm.faces]
    bm.free()
    for _ in range(3):
        chosen = chosen & np.array([sum(chosen[o] for o in n) >= 2 for n in neighbours])
    mask = drop_islands(scan, np.where(chosen, 0, -1), areas, keep_fraction=0.3)
    if not (mask == 0).any():
        return None
    hood = extract(scan, mask, 0, name + ".hood")
    vertex_clusters(hood)
    pt.decimate(hood, budget)
    _relax_edges(hood)
    mesh = hood.data
    mesh.calc_loop_triangles()
    triangles = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int64)
    mesh.loop_triangles.foreach_get("vertices", triangles)
    co = bt.vertices(hood)
    pairs = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", pairs)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    held = np.array([v.is_boundary for v in bm.verts])
    bm.free()
    co = _fair(co, (pairs[0::2], pairs[1::2]), held)  # the scan's folds were crumpled lumps
    co = co + _vertex_normals(co, triangles.reshape(-1, 3)) * 0.005
    mesh.vertices.foreach_set("co", _pushed_out(co, tree, LAYER_OFFSET["outer"]).ravel())
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")  # decimation leaves a few
    bm.to_mesh(mesh)
    bm.free()
    ct.transfer_from_body(hood, body, bones=["Chest", "Neck"])
    # Its edges aren't trims: one is where it was cut from the hoodie's body (mint bands spread over the back).
    hood.data.attributes.new("no_trim", "INT", "POINT").data.foreach_set("value", np.ones(len(mesh.vertices), dtype=np.int32))
    chest = hood.vertex_groups.get("Chest") or hood.vertex_groups.new(name="Chest")
    for v in mesh.vertices:
        if sum(g.weight for g in v.groups) < 1e-6:
            chest.add([v.index], 1.0, "REPLACE")
    return hood


TAUBIN = (0.5, -0.53)  # smoothing factors that don't shrink a curve


def _relax_edges(part, iterations=8):
    """
    Straightens a piece's open edges along their own line (each edge vertex towards its two edge neighbours),
    Taubin-style so edge loops keep their size.
    """
    bm = bmesh.new()
    bm.from_mesh(part.data)
    along = {}
    for v in bm.verts:
        ends = [e.other_vert(v).index for e in v.link_edges if e.is_boundary]
        if v.is_boundary and len(ends) == 2:
            along[v.index] = ends
    bm.free()
    co = bt.vertices(part)
    for step in range(iterations):
        moved = co.copy()
        for i, (a, b) in along.items():
            moved[i] = co[i] + TAUBIN[step % 2] * (0.5 * (co[a] + co[b]) - co[i])
        co = moved
    part.data.vertices.foreach_set("co", co.ravel())
    part.data.update()


def _upper_envelope(x, y):
    """``y`` raised to its least concave majorant over increasing ``x``: the line a taut cloth takes over the bumps."""
    hull = []
    for i in range(len(x)):
        while len(hull) >= 2:
            a, b = hull[-2], hull[-1]
            if (y[b] - y[a]) * (x[i] - x[a]) <= (y[i] - y[a]) * (x[b] - x[a]):
                hull.pop()
            else:
                break
        hull.append(i)
    return np.interp(x, x[hull], y[hull])


def build_skirt(body, scan, garment, index, joints, name, top=None, hem=None, offset=0.012, step=0.012, segments=64,
                legs=0.5, waist_hug=0.04, sigma=1.5, pleats=0, pleat_depth=0.008, flare=False, smooth=0, straight=False):
    """
    A skirt (on its own, or a dress's below the waist) as one tube, which the body's surface can't make: built from
    the body, a garment wraps each leg on its own. Each ring ``step`` metres apart, from ``top`` (default: the waist
    line, garment_tools.WAIST_ABOVE_HIPS) down to ``hem`` (default: where the scan's skirt ends), takes the scan's
    skirt outline there (its convex hull round the body's middle, so the image decides the flare), but never closer
    than ``offset`` to the hull of the body in the game's relaxed pose, legs and all, so they stay inside however far
    apart they stand. The top ``waist_hug`` metres close in to the waist. Weighted to the hips, handing up to ``legs``
    of each side to that side's thigh down the skirt, so it moves with the legs without tearing between them. Built in
    the pose and taken back to the bind pose, and the same for each body shape (its own tube round that shape). With
    ``pleats``, the outline below the waist folds in and out that many times round, ``pleat_depth`` deep, opening out
    down the skirt (a pleated skirt; the scan's pleats are shading, not shape). The outline is the same on both sides
    (the scan's is lopsided), and with ``flare`` only ever widens going down and is smoothed harder (an A-line skirt
    or a gown: the scan's folds made a lumpy sack).
    Returns the skirt object (one recolour cluster: the scan skirt's commonest), or None if the scan has no skirt.
    """
    waist = float(joints["Hips"][2]) + WAIST_ABOVE_HIPS
    top = waist if top is None else top
    mesh = scan.data
    centres = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)
    skirt_faces = (garment == index) & (centres[:, 2] < top)
    if skirt_faces.sum() < 50:
        return None
    floor = float(bt.vertices(body)[:, 2].min())
    if hem is None:
        # Where the skirt runs out: going down, the first height its faces fall under a sixth of what the top has, for
        # three centimetres (stray dark faces lower down, in shadow, dragged a mini skirt to mid-calf).
        z_faces = centres[skirt_faces, 2]
        bins = np.arange(top, floor, -0.01)
        counts = np.array([((z_faces <= b) & (z_faces > b - 0.01)).sum() for b in bins])
        reference = float(np.median(counts[:8])) if len(counts) >= 8 else float(counts.max())
        low = counts < reference / 6.0
        hem = float(bins[-1])
        for i in range(len(bins) - 2):
            if low[i] and low[i + 1] and low[i + 2]:
                hem = float(bins[i])
                break
    hem = max(hem, floor + 0.015)
    zs = np.arange(top, hem - 1e-6, -step)
    if len(zs) < 3:
        return None
    thetas = np.linspace(-np.pi, np.pi, segments, endpoint=False)
    kernel = np.exp(-0.5 * (np.arange(-5, 6) / sigma) ** 2)
    kernel /= kernel.sum()

    def blur(grid):
        padded = np.concatenate([np.repeat(grid[:1], 5, axis=0), grid, np.repeat(grid[-1:], 5, axis=0)], axis=0)
        grid = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 0, padded)
        padded = np.concatenate([grid[:, -5:], grid, grid[:, :5]], axis=1)
        return np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 1, padded)

    skinning = bt.pose_skinning(joints)
    body_w, body_names = bt.weight_matrix(body)
    lower = body_w[:, [i for i, n in enumerate(body_names) if n.startswith(("Hips", "Spine", "UpperLeg", "LowerLeg", "Foot"))]].sum(axis=1) > 0.5
    centre_y = float(np.median(bt.vertices(body)[lower & (np.abs(bt.vertices(body)[:, 2] - float(joints["Hips"][2])) < 0.05), 1]))
    axis = np.array([0.0, centre_y])

    scan_points = np.unique(np.array([v for p, keep in zip(mesh.polygons, skirt_faces) if keep for v in p.vertices]))
    scan_co = bt.vertices(scan)[scan_points]
    scan_r = np.zeros((len(zs), segments))
    for i, z in enumerate(zs):
        slab = np.abs(scan_co[:, 2] - z) < step
        if slab.sum() >= 6:
            scan_r[i] = _hull_radii(scan_co[slab, :2] - axis, thetas)
    for i in range(1, len(zs)):  # rows the scan left empty take the row above
        if not scan_r[i].any():
            scan_r[i] = scan_r[i - 1]
    mirrored = (np.pi - thetas + np.pi) % (2 * np.pi) - np.pi  # x to -x
    scan_r = 0.5 * (scan_r + np.array([np.interp(mirrored, thetas, row, period=2 * np.pi) for row in scan_r]))
    if flare:
        scan_r = np.maximum.accumulate(scan_r, axis=0)  # rows run top to bottom
        scan_r = blur(blur(scan_r))
    hips_row = int(np.argmin(np.abs(zs - (float(joints["Hips"][2]) - 0.05))))
    if straight:  # below the hips it only narrows or hangs straight: a slip dress's front bulged where its slit opened,
        # and a cheongsam's over the thighs, 3 cm out at a millimetre and a half a row
        for i in range(hips_row + 1, len(zs)):
            scan_r[i] = np.minimum(scan_r[i], scan_r[i - 1] + 0.0005)
    for _ in range(smooth):  # a straight dress the scan's folds made lumpy
        scan_r = blur(scan_r)
    scan_r = blur(scan_r)

    # Weights: hips, handing up to ``legs`` to each side's thigh down the skirt.
    hip_z = float(joints["Hips"][2])
    leg_share = legs * np.clip((top - zs) / max(top - (hip_z - 0.12), 1e-6), 0.0, 1.0)
    side = np.cos(thetas)  # +x is the character's left
    left = 0.5 + 0.5 * side  # handing over gradually round the ring: a sharp hand-over at the front and back folded
    # the skirt there whenever the legs stood apart differently from how it was built
    names = ["Hips", "UpperLeg.L", "UpperLeg.R"]
    weights = np.zeros((len(zs) * segments, 3))
    for i in range(len(zs)):
        rows = slice(i * segments, (i + 1) * segments)
        weights[rows, 0] = 1.0 - leg_share[i]
        weights[rows, 1] = leg_share[i] * left
        weights[rows, 2] = leg_share[i] * (1.0 - left)

    def tube(body_rest):
        posed = bt.skin_positions(body_rest, body_w, body_names, skinning)[lower]
        body_r = np.zeros((len(zs), segments))
        for i, z in enumerate(zs):
            slab = np.abs(posed[:, 2] - z) < step
            if slab.sum() >= 6:
                body_r[i] = _hull_radii(posed[slab, :2] - axis, thetas)
        for i in range(1, len(zs)):
            if not body_r[i].any():
                body_r[i] = body_r[i - 1]
        # Spread a little before it's blurred: blurred alone, the narrow peak a calf makes sank and the calf poked out,
        # and unblurred the rows rippled.
        grown = body_r.copy()
        for shift in (-2, -1, 1, 2):
            grown = np.maximum(grown, np.roll(body_r, shift, axis=1))
        grown[1:] = np.maximum(grown[1:], body_r[:-1])
        grown[:-1] = np.maximum(grown[:-1], body_r[1:])
        body_r = np.maximum(blur(grown), body_r)
        r = np.maximum(scan_r, body_r + offset)
        hug = np.clip((top - zs) / waist_hug, 0.0, 1.0)[:, None]  # in to the waist over the top few centimetres
        r = body_r + offset * 0.75 + (r - body_r - offset * 0.75) * hug * hug * (3 - 2 * hug)
        # Below the hips, cloth spans the knees (forward in the relaxed pose) instead of tucking back in under them: a
        # cheongsam bulged over them. A flared skirt hangs straight down off its widest point; a straight one is taut
        # across them and still tapers to its hem (each column's upper concave envelope).
        if flare:
            r[hips_row:] = np.maximum.accumulate(r[hips_row:], axis=0)
        elif straight:
            for k in range(segments):
                r[hips_row:, k] = _upper_envelope(-zs[hips_row:], r[hips_row:, k])
        # Each ring convex: where a ring dipped in (between a leg's outline and the scan's), its sides turned away
        # from the camera, and the outline pass drew them through the front of the skirt as dark streaks.
        ring_xy = np.stack([np.cos(thetas), np.sin(thetas)], axis=1)
        r = np.array([_hull_radii(row[:, None] * ring_xy, thetas) for row in r])
        if pleats:
            wave = 1.0 - np.abs(((thetas / (2 * np.pi) * pleats) % 1.0) * 2.0 - 1.0)  # a triangle wave round the skirt
            opening = np.clip((top - waist_hug - zs) / 0.12, 0.0, 1.0)[:, None]
            r = r + pleat_depth * opening * wave[None, :]
        ring = np.stack([np.cos(thetas), np.sin(thetas)], axis=1)
        out = np.zeros((len(zs), segments, 3))
        out[:, :, :2] = axis + r[:, :, None] * ring[None]
        out[:, :, 2] = zs[:, None]
        return bt.unskin_positions(out.reshape(-1, 3), weights, names, skinning)

    rest = tube(bt.vertices(body))
    faces = [(i * segments + k, i * segments + (k + 1) % segments, (i + 1) * segments + (k + 1) % segments, (i + 1) * segments + k)
             for i in range(len(zs) - 1) for k in range(segments)]
    skirt_mesh = bpy.data.meshes.new(name + ".skirt")
    skirt_mesh.from_pydata([tuple(p) for p in rest], [], faces)
    skirt_mesh.update()
    skirt = bpy.data.objects.new(name + ".skirt", skirt_mesh)
    bpy.context.scene.collection.objects.link(skirt)
    groups = [skirt.vertex_groups.new(name=n) for n in names]
    for column, group in enumerate(groups):
        for v in np.flatnonzero(weights[:, column] > 1e-4):
            group.add([int(v)], float(weights[v, column]), "REPLACE")
    if body.data.shape_keys:
        skirt.shape_key_add(name="Basis", from_mix=False)
        for key in body.data.shape_keys.key_blocks[1:]:
            shaped = skirt.shape_key_add(name=key.name, from_mix=False)
            shaped.value = 0.0
            shaped.data.foreach_set("co", tube(_key_positions(key)).ravel())
    labels_attr = np.empty(len(mesh.polygons), dtype=np.int32)
    mesh.attributes["cluster"].data.foreach_get("value", labels_attr)
    commonest = int(np.bincount(labels_attr[skirt_faces]).argmax())
    skirt_mesh.attributes.new("vcluster", "INT", "POINT").data.foreach_set("value", np.full(len(rest), commonest, dtype=np.int32))
    return skirt


def stand_hood(hood, joints, rise=0.35, out=0.0):
    """
    A hood (down) made to stand up round the neck like a cowl: stretched up from its base by ``rise`` of its height
    and out from the neck by ``out``, more towards its top. The scan's hood lay flat on the shoulders as a thin ruff;
    the image's stands up beside the neck with its lining showing at the front. The basis and every shape key alike.
    """
    mesh = hood.data
    co = bt.vertices(hood)
    base = float(co[:, 2].min())
    height = max(float(co[:, 2].max()) - base, 1e-6)
    axis = np.asarray(joints["Neck"], dtype=float)[:2]

    def stood(points):
        t = np.clip((points[:, 2] - base) / height, 0.0, 1.0)
        points = points.copy()
        points[:, 2] = base + (points[:, 2] - base) * (1.0 + rise)
        points[:, :2] = axis + (points[:, :2] - axis) * (1.0 + out * t)[:, None]
        return points

    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else []
    for key in keys:
        key.data.foreach_set("co", stood(_key_positions(key)).ravel())
    mesh.vertices.foreach_set("co", (stood(co) if not keys else _key_positions(mesh.shape_keys.reference_key)).ravel())
    mesh.update()


def join_into(part, extra):
    """Joins ``extra`` into ``part``: vertex groups, shape keys and attributes are matched by name."""
    bpy.ops.object.select_all(action="DESELECT")
    extra.select_set(True)
    part.select_set(True)
    bpy.context.view_layer.objects.active = part
    bpy.ops.object.join()


def flare_sole(part, height=0.024, flare=0.01, flat=0.008, angles=72, toe=0.05, toe_rise=0.45):
    """
    Gives a shoe a sneaker's sole: for each foot, the bottom ``height`` metres are pushed out horizontally to the
    footprint's convex hull plus ``flare`` (full over the lower half, easing to nothing at the top), and the bottom
    ``flat`` metres pressed down to the floor, so it stands on a flat, wider sole with a straight edge. Fitted to the
    foot, the shoes were rounded slippers with no sole edge. The basis and every shape key alike.
    """
    mesh = part.data
    co = bt.vertices(part)
    mesh.update()
    normal_z = np.array([v.normal.z for v in mesh.vertices])
    thetas = np.linspace(-np.pi, np.pi, angles, endpoint=False)
    shift = np.zeros_like(co)
    for side in (1.0, -1.0):
        foot = co[:, 0] * side > 0
        if foot.sum() < 10:
            continue
        # Each shoe's own floor, under most of its bottom: the heel poked 5 mm below the rest, and the arch and the
        # curled-up toes stood 2 cm off it, so the sole band missed the toe.
        bottom = foot & (normal_z < -0.5)
        floor = float(np.percentile(co[bottom, 2], 20)) if bottom.sum() > 20 else float(co[foot, 2].min())
        # The footprint: everything up to the toe box's top, which reaches further forward than the shoe's bottom
        # (the toes curl up), so the sole runs under all of it and shows round the toe.
        print_ = foot & (co[:, 2] < floor + toe + 0.01)
        centre = co[print_, :2].mean(axis=0)
        hull = _hull_radii(co[print_, :2] - centre, thetas) + flare
        d = co[:, :2] - centre
        radius = np.linalg.norm(d, axis=1)
        angle = np.arctan2(d[:, 1], d[:, 0])
        j = ((angle + np.pi) / (2 * np.pi) * angles) % angles
        j0, t = j.astype(int), j - j.astype(int)
        target = hull[j0] * (1 - t) + hull[(j0 + 1) % angles] * t
        weight = np.clip((floor + height - co[:, 2]) / (height / 2), 0.0, 1.0)
        weight = weight * weight * (3 - 2 * weight)
        grow = np.where(foot, np.maximum(target - radius, 0.0) * weight, 0.0)
        shift[:, :2] += d / np.maximum(radius, 1e-9)[:, None] * grow[:, None]
        down = foot & ((co[:, 2] < floor + flat) | ((normal_z < -0.3) & (co[:, 2] < floor + 0.025)))
        shift[down, 2] = floor - co[down, 2]
        # A sneaker's low toe box: in front of the instep the top comes down to a line rising from the toe (fitted
        # to the foot, the toes made a bulb).
        tip = float(co[foot, 1].min())
        cap = floor + toe + toe_rise * (co[:, 1] - tip)
        box = foot & (co[:, 2] > cap) & (co[:, 1] < tip + 0.09) & (co[:, 2] < floor + 0.09)
        shift[box, 2] = cap[box] - co[box, 2]
        shift[foot, 2] = np.maximum(co[foot, 2] + shift[foot, 2], floor) - co[foot, 2]  # nothing under the sole
    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else []
    for key in keys:
        key.data.foreach_set("co", (_key_positions(key) + shift).ravel())
    mesh.vertices.foreach_set("co", (co + shift).ravel())
    mesh.update()


def level_soles(part, flat=0.012):
    """
    Both shoes' soles pressed flat onto one floor, the pair's lowest point: everything within ``flat`` metres above
    each shoe's own sole goes down to it. The push-out off the toes after flare_sole dragged a few points of one boot's flat sole a
    centimetre under it, so the boots stood at different heights on spikes. The basis and every shape key alike.
    """
    mesh = part.data
    co = bt.vertices(part)
    mesh.update()
    normal_z = np.array([v.normal.z for v in mesh.vertices])
    floor = float(co[:, 2].min())
    shift = np.zeros(len(co))
    for side in (1.0, -1.0):
        foot = co[:, 0] * side > 0
        bottom = foot & (normal_z < -0.5)
        if bottom.sum() < 20:
            continue
        # This shoe's own sole (most of its bottom): one boot's stood 1-2 cm higher than the other, with only its
        # inner edge reaching the floor, a spike under it.
        own = float(np.percentile(co[bottom, 2], 30))
        low = foot & (co[:, 2] < max(own, floor) + flat)
        shift[low] = floor - co[low, 2]
    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else []
    for key in keys:
        moved = _key_positions(key)
        moved[:, 2] += shift
        key.data.foreach_set("co", moved.ravel())
    co[:, 2] += shift
    mesh.vertices.foreach_set("co", co.ravel())
    mesh.update()
    return int((shift < 0).sum())


def round_shoe(part, step=0.006, angles=72, sigma=2.0):
    """
    Each shoe wrapped like a sneaker's upper: every cross-section across the foot (a slice ``step`` metres deep along
    its length) is pushed out to its convex hull round its centre, the hulls blurred along the foot and round it, so
    the toe box and the sides are one smooth shell. Fitted to the foot, the toes and the tendons showed through as
    lumps. Only outwards. The basis and every shape key alike.
    """
    mesh = part.data
    co = bt.vertices(part)
    thetas = np.linspace(-np.pi, np.pi, angles, endpoint=False)
    shift = np.zeros_like(co)
    kernel = np.exp(-0.5 * (np.arange(-6, 7) / sigma) ** 2)
    kernel /= kernel.sum()

    def blur_rows(grid):  # along the foot (edges held)
        padded = np.concatenate([np.repeat(grid[:1], 6, axis=0), grid, np.repeat(grid[-1:], 6, axis=0)], axis=0)
        return np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 0, padded)

    for side in (1.0, -1.0):
        foot = np.nonzero(co[:, 0] * side > 0)[0]
        if len(foot) < 30:
            continue
        y = co[foot, 1]
        ys = np.arange(y.min(), y.max() + step, step)
        centres = np.zeros((len(ys), 2))
        for i, yy in enumerate(ys):
            slab = np.abs(y - yy) < step
            centres[i] = co[foot][slab][:, [0, 2]].mean(axis=0) if slab.sum() >= 3 else np.nan
        good = ~np.isnan(centres[:, 0])
        for k in (0, 1):
            centres[:, k] = np.interp(ys, ys[good], centres[good, k])
        centres = blur_rows(centres)
        grid = np.zeros((len(ys), angles))
        for i, yy in enumerate(ys):
            slab = np.abs(y - yy) < step
            if slab.sum() >= 3:
                grid[i] = _hull_radii(co[foot][slab][:, [0, 2]] - centres[i], thetas)
        grid = blur_rows(grid)
        padded = np.concatenate([grid[:, -6:], grid, grid[:, :6]], axis=1)  # round the foot (wrapping)
        grid = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 1, padded)
        fi = np.clip((y - ys[0]) / step, 0, len(ys) - 1.001)
        i0, ti = fi.astype(int), fi - fi.astype(int)
        i1 = np.minimum(i0 + 1, len(ys) - 1)
        centre = centres[i0] * (1 - ti)[:, None] + centres[i1] * ti[:, None]
        d = co[foot][:, [0, 2]] - centre
        radius = np.linalg.norm(d, axis=1)
        fj = ((np.arctan2(d[:, 1], d[:, 0]) + np.pi) / (2 * np.pi) * angles) % angles
        j0, tj = fj.astype(int), fj - fj.astype(int)
        j1 = (j0 + 1) % angles
        hull = (grid[i0, j0] * (1 - ti) * (1 - tj) + grid[i1, j0] * ti * (1 - tj)
                + grid[i0, j1] * (1 - ti) * tj + grid[i1, j1] * ti * tj)
        grow = np.maximum(hull - radius, 0.0)
        out = d / np.maximum(radius, 1e-9)[:, None] * grow[:, None]
        shift[foot, 0] += out[:, 0]
        # Out round it, not through the floor: the sole's level (its lowest few percent; the push-out can leave a
        # stray point poking a centimetre under it).
        floor = float(np.percentile(co[foot, 2], 2))
        shift[foot, 2] += np.maximum(co[foot, 2] + out[:, 1], floor) - co[foot, 2]
    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else []
    for key in keys:
        key.data.foreach_set("co", (_key_positions(key) + shift).ravel())
    mesh.vertices.foreach_set("co", (co + shift).ravel())
    mesh.update()
    return float(np.linalg.norm(shift, axis=1).max())


def lace_lines(part, region, lines=4, spacing=0.012, line=0.004, half_width=0.016, above=0.035):
    """
    A sneaker's laces drawn as ``lines`` short bars across the top of each shoe, ``line`` metres thick and ``spacing``
    apart, from the front of the collar down the instep towards the toe, in recolour ``region``: from the front the
    shoes were white lumps, and the references draw laces as a few strokes. Only above ``above`` metres off the floor.
    Returns the faces drawn.
    """
    mesh = part.data
    co = bt.vertices(part)
    floor = float(co[:, 2].min())
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    boundary = np.array([v.is_boundary for v in bm.verts])
    feet = []
    for side in (1.0, -1.0):
        foot = co[:, 0] * side > 0
        collar = foot & boundary & (co[:, 2] > floor + above)
        if not collar.any():
            continue
        front = co[np.flatnonzero(collar)[np.argmin(co[collar, 1])]]  # the collar's front: the tongue's top
        tip = co[np.flatnonzero(foot)[np.argmin(co[foot, 1])]]
        feet.append((side, front, tip))

    def centre_x(p, front, tip):
        t = np.clip((front[1] - p[1]) / max(front[1] - tip[1], 1e-6), 0.0, 1.0)
        return front[0] + (tip[0] - front[0]) * t

    def field(p):
        best = -1.0
        for side, front, tip in feet:
            if p[0] * side <= 0:
                continue
            along = (front[1] - 0.008 - p[1]) / spacing  # bars from just in front of the collar, towards the toe
            k = np.clip(np.round(along), 0, lines - 1)
            bar = line / 2 - abs(along - k) * spacing
            best = max(best, min(bar, half_width - abs(p[0] - centre_x(p, front, tip)), p[2] - floor - above))
        return best

    # Each straight edge cut on its own: a bar's field is a tent, and triangles across its ridge zigzagged the cut.
    cuts = [lambda p: p[2] - floor - above]
    for side, front, tip in feet:
        for sign in (1.0, -1.0):
            cuts.append(lambda p, f=front, t=tip, s=sign: (s * (p[0] - centre_x(p, f, t)) + half_width) if p[0] * side > 0 else 1.0)
        for k in range(lines):
            for sign in (1.0, -1.0):
                cuts.append(lambda p, y=front[1] - 0.008 - k * spacing, s=sign: s * (p[1] - y) + line / 2)
    return _paint(bm, mesh, field, region, cuts)


def paint_straps(part, region, joints, below=0.035):
    """
    A strapped top's straps, everything above ``below`` metres under the shoulder joints, painted in recolour
    ``region`` (its edging's colour): the image's straps are black, and the edge bands left burgundy slivers between.
    Returns the faces painted.
    """
    mesh = part.data
    line = float(joints["UpperArm.L"][2]) - below
    bm = bmesh.new()
    bm.from_mesh(mesh)
    field = lambda p: p[2] - line
    return _paint(bm, mesh, field, region, [field])


def draw_toe_caps(part, region, back=0.045, line=0.003, above=0.02):
    """
    A sneaker's toe-cap seam: the line round each toe box ``back`` metres behind its tip, from the sole up over the top
    and down to the sole on the other side, ``line`` metres wide, in recolour ``region``. Returns the faces drawn.
    """
    mesh = part.data
    co = bt.vertices(part)
    floor = float(co[:, 2].min())
    tips = {side: float(co[co[:, 0] * side > 0, 1].min()) for side in (1.0, -1.0) if (co[:, 0] * side > 0).any()}
    bm = bmesh.new()
    bm.from_mesh(mesh)

    def seam(p, side):
        return line / 2 - abs(p[1] - (tips[side] + back))

    def field(p):
        side = 1.0 if p[0] > 0 else -1.0
        if side not in tips:
            return -1.0
        return min(seam(p, side), p[2] - floor - above)

    cuts = [lambda p: p[2] - floor - above]
    for side, tip in tips.items():
        for sign in (1.0, -1.0):
            cuts.append(lambda p, y=tip + back, s=sign: s * (p[1] - y) + line / 2)
    return _paint(bm, mesh, field, region, cuts)


def draw_cords(part, region, length=0.13, width=0.007, apart=0.05):
    """
    A hoodie's drawstrings drawn as two stripes ``width`` metres wide and ``length`` long, ``apart`` metres apart,
    hanging down the front from the neck opening, in recolour ``region``. Returns the faces drawn.
    """
    mesh = part.data
    co = bt.vertices(part)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    boundary = np.array([v.is_boundary for v in bm.verts])
    # The neck opening's front (not the hem's): open edge at the middle of the front, in the top of the garment.
    front = boundary & (np.abs(co[:, 0]) < 0.03) & (co[:, 1] < float(np.median(co[:, 1]))) & (co[:, 2] > np.percentile(co[:, 2], 75))
    top = float(co[front, 2][co[front, 1] < np.percentile(co[front, 1], 30)].min()) if front.any() else float(co[:, 2].max())
    middle_y = float(np.median(co[:, 1]))

    def field(p):
        if p[1] > middle_y:
            return -1.0
        across = width / 2 - abs(abs(p[0]) - apart / 2)
        return min(across, top - 0.004 - p[2], p[2] - (top - length))

    cuts = [lambda p: top - 0.004 - p[2], lambda p: p[2] - (top - length),
            lambda p: abs(p[0]) - (apart - width) / 2, lambda p: (apart + width) / 2 - abs(p[0])]
    return _paint(bm, mesh, field, region, cuts)


def draw_fly(part, region, below=0.03, length=0.075, width=0.004, offset=0.012):
    """
    A pair of trousers' fly drawn as a stripe ``width`` metres wide down the front, from ``below`` metres under the
    waist's front to ``length`` further, ``offset`` metres off the centre line (the fly's stitched edge), in recolour
    ``region``. Returns the faces drawn.
    """
    mesh = part.data
    co = bt.vertices(part)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    boundary = np.array([v.is_boundary for v in bm.verts])
    front = boundary & (np.abs(co[:, 0]) < 0.02) & (co[:, 1] < float(np.median(co[:, 1]))) & (co[:, 2] > np.percentile(co[:, 2], 75))
    top = (float(co[front, 2].min()) if front.any() else float(co[:, 2].max())) - below
    middle_y = float(np.median(co[:, 1]))

    def field(p):
        if p[1] > middle_y:
            return -1.0
        return min(width / 2 - abs(p[0] - offset), top - p[2], p[2] - (top - length))

    cuts = [lambda p: top - p[2], lambda p: p[2] - (top - length),
            lambda p: p[0] - (offset - width / 2), lambda p: (offset + width / 2) - p[0]]
    return _paint(bm, mesh, field, region, cuts)


def draw_pockets(part, region, below=0.03, inner=0.09, outer=0.15, drop=0.06, width=0.004):
    """
    Front pocket openings drawn as a slanted stripe ``width`` metres wide on each hip, from ``inner`` metres off the
    centre line just under the waistband (``below`` the waist's top) out and down to the side, ``outer`` metres off
    it and ``drop`` lower, in recolour ``region``. Returns the faces drawn.
    """
    mesh = part.data
    co = bt.vertices(part)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    boundary = np.array([v.is_boundary for v in bm.verts])
    front = boundary & (np.abs(co[:, 0]) < 0.02) & (co[:, 1] < float(np.median(co[:, 1]))) & (co[:, 2] > np.percentile(co[:, 2], 75))
    top = (float(co[front, 2].min()) if front.any() else float(co[:, 2].max())) - below
    middle_y = float(np.median(co[:, 1]))
    direction = np.array([outer - inner, -drop])
    direction /= np.linalg.norm(direction)
    normal = np.array([-direction[1], direction[0]])

    def across(p):  # signed distance from the pocket line, in the front view (|x|, z)
        return (abs(p[0]) - inner) * normal[0] + (p[2] - top) * normal[1]

    def along(p):
        return (abs(p[0]) - inner) * direction[0] + (p[2] - top) * direction[1]

    reach = float(np.hypot(outer - inner, drop))

    def field(p):
        if p[1] > middle_y:
            return -1.0
        return min(width / 2 - abs(across(p)), along(p), reach - along(p))

    cuts = [lambda p: across(p) + width / 2, lambda p: width / 2 - across(p), along, lambda p: reach - along(p)]
    return _paint(bm, mesh, field, region, cuts)


def paint_sole(part, region, height=0.016):
    """
    A shoe's sole painted as a level band ``height`` metres up from its lowest point, in recolour ``region``: a thin
    dark sole is too small a colour block in the scan to get a region of its own. Returns the faces painted.
    """
    mesh = part.data
    floor = float(bt.vertices(part)[:, 2].min())
    bm = bmesh.new()
    bm.from_mesh(mesh)
    field = lambda p: floor + height - p[2]
    return _paint(bm, mesh, field, region, [field])


def draw_buckle(part, region, width=0.03, height=0.022, line=0.004, below=0.0):
    """
    A belt buckle drawn as a rectangular frame ``width`` by ``height`` metres, ``line`` thick, at the front middle of
    the garment's top edge (its waistband), in recolour ``region``. Returns the faces drawn.
    """
    mesh = part.data
    co = bt.vertices(part)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    boundary = np.array([v.is_boundary for v in bm.verts])
    front = boundary & (np.abs(co[:, 0]) < 0.02) & (co[:, 1] < float(np.median(co[:, 1]))) & (co[:, 2] > np.percentile(co[:, 2], 75))
    top = (float(co[front, 2].min()) if front.any() else float(co[:, 2].max())) - below
    centre_z = top - height / 2
    middle_y = float(np.median(co[:, 1]))

    def field(p):
        if p[1] > middle_y:
            return -1.0
        dx, dz = abs(p[0]) - width / 2, abs(p[2] - centre_z) - height / 2
        outside = max(dx, dz)
        return min(-outside, outside + line)  # inside the outer box, outside the inner one

    cuts = [lambda p, s=s_: s * p[0] + width / 2 for s_ in (1.0, -1.0)] +            [lambda p, s=s_: s * p[0] + width / 2 - line for s_ in (1.0, -1.0)] +            [lambda p, s=s_: s * (p[2] - centre_z) + height / 2 for s_ in (1.0, -1.0)] +            [lambda p, s=s_: s * (p[2] - centre_z) + height / 2 - line for s_ in (1.0, -1.0)]
    return _paint(bm, mesh, field, region, cuts)


def _paint(bm, mesh, field, region, cuts):
    """
    Cuts ``bm`` along each of ``cuts`` (straight fields, cut where they cross zero) and paints the faces where
    ``field`` is positive in recolour ``region``.
    """
    for cut in cuts:
        bm.verts.ensure_lookup_table()
        values = np.array([cut(v.co) for v in bm.verts])
        # Only near the painted area: each cut is a whole plane, and cutting it right through the mesh tripled a
        # shoe's triangles.
        near = np.array([field(v.co) > -0.015 for v in bm.verts])
        _cut_along(bm, np.where(np.abs(values) < 1e-6, 1e-6, values), 0.0, near=near)
    drawn = [f for f in bm.faces if field(f.calc_center_median()) > 0]
    colours = bm.loops.layers.float_color.get(ct.COLOUR_ATTRIBUTE)
    one_hot = [0.0, 0.0, 0.0, 0.0]
    one_hot[region + 1] = 1.0
    for f in drawn:
        for loop in f.loops:
            loop[colours] = one_hot
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return len(drawn)


def collar_normals(part, region, joints, strength=0.85, lift=1.0):
    """
    A collar band (recolour ``region`` above the shoulders) shaded as one ring: its normals blended ``strength`` of the
    way to point straight out from the neck (tipped ``lift`` upwards), so the toon step goes round it smoothly. Where
    the collar met the shoulders its surface was bumpy and it broke into white and lavender patches. Returns the
    vertices set.
    """
    mesh = part.data
    co = bt.vertices(part)
    values = np.empty(len(mesh.loops) * 4)
    mesh.color_attributes[ct.COLOUR_ATTRIBUTE].data.foreach_get("color", values)
    corner = values.reshape(-1, 4)[:, 1:].argmax(axis=1)
    loop_vertex = np.empty(len(mesh.loops), dtype=np.int64)
    mesh.loops.foreach_get("vertex_index", loop_vertex)
    collar = np.zeros(len(co), dtype=bool)
    collar[loop_vertex[corner == region]] = True
    collar &= co[:, 2] > float(joints["UpperArm.L"][2])
    neck = np.asarray(joints["Neck"], dtype=float)
    out = co - neck
    out[:, 2] = 0.0
    out /= np.maximum(np.linalg.norm(out, axis=1), 1e-9)[:, None]
    out[:, 2] = lift
    out /= np.linalg.norm(out, axis=1)[:, None]
    normals = np.empty(len(co) * 3)
    mesh.vertices.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)
    mixed = np.where(collar[:, None], normals * (1.0 - strength) + out * strength, normals)
    mixed /= np.maximum(np.linalg.norm(mixed, axis=1), 1e-9)[:, None]
    mesh.polygons.foreach_set("use_smooth", np.ones(len(mesh.polygons), dtype=bool))
    mesh.normals_split_custom_set_from_vertices([tuple(n) for n in mixed])
    mesh.update()
    return int(collar.sum())


def round_shaft(part, above=0.1, step=0.008, angles=72, sigma=2.0):
    """
    A boot's shaft wrapped like the stiff tube it is: above ``above`` metres off the floor, each horizontal slice of
    each boot is pushed out to its convex hull round its centre, the hulls blurred up the shaft and round it, eased in
    over the first 4 cm so the ankle blends. The scan's folds and the calf's shape showed through as lumps. Only
    outwards. The basis and every shape key alike. Returns the largest move in metres.
    """
    mesh = part.data
    co = bt.vertices(part)
    floor = float(co[:, 2].min())
    thetas = np.linspace(-np.pi, np.pi, angles, endpoint=False)
    kernel = np.exp(-0.5 * (np.arange(-6, 7) / sigma) ** 2)
    kernel /= kernel.sum()
    shift = np.zeros_like(co)
    for side in (1.0, -1.0):
        shaft = np.nonzero((co[:, 0] * side > 0) & (co[:, 2] > floor + above))[0]
        if len(shaft) < 30:
            continue
        z = co[shaft, 2]
        zs = np.arange(z.min(), z.max() + step, step)
        centres = np.zeros((len(zs), 2))
        grid = np.zeros((len(zs), angles))
        for i, zz in enumerate(zs):
            slab = np.abs(z - zz) < step
            centres[i] = co[shaft][slab][:, :2].mean(axis=0) if slab.sum() >= 3 else np.nan
        good = ~np.isnan(centres[:, 0])
        for k in (0, 1):
            centres[:, k] = np.interp(zs, zs[good], centres[good, k])
        for i, zz in enumerate(zs):
            slab = np.abs(z - zz) < step
            if slab.sum() >= 3:
                grid[i] = _hull_radii(co[shaft][slab][:, :2] - centres[i], thetas)
        padded = np.concatenate([np.repeat(grid[:1], 6, axis=0), grid, np.repeat(grid[-1:], 6, axis=0)], axis=0)
        grid = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 0, padded)
        padded = np.concatenate([grid[:, -6:], grid, grid[:, :6]], axis=1)
        grid = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), 1, padded)
        fi = np.clip((z - zs[0]) / step, 0, len(zs) - 1.001)
        i0, ti = fi.astype(int), fi - fi.astype(int)
        i1 = np.minimum(i0 + 1, len(zs) - 1)
        centre = centres[i0] * (1 - ti)[:, None] + centres[i1] * ti[:, None]
        d = co[shaft, :2] - centre
        radius = np.linalg.norm(d, axis=1)
        fj = ((np.arctan2(d[:, 1], d[:, 0]) + np.pi) / (2 * np.pi) * angles) % angles
        j0, tj = fj.astype(int), fj - fj.astype(int)
        j1 = (j0 + 1) % angles
        hull = (grid[i0, j0] * (1 - ti) * (1 - tj) + grid[i1, j0] * ti * (1 - tj)
                + grid[i0, j1] * (1 - ti) * tj + grid[i1, j1] * ti * tj)
        ease = np.clip((z - floor - above) / 0.04, 0.0, 1.0)
        grow = np.maximum(hull - radius, 0.0) * ease
        shift[shaft, :2] += d / np.maximum(radius, 1e-9)[:, None] * grow[:, None]
    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else []
    for key in keys:
        key.data.foreach_set("co", (_key_positions(key) + shift).ravel())
    mesh.vertices.foreach_set("co", (co + shift).ravel())
    mesh.update()
    return float(np.linalg.norm(shift, axis=1).max())


def trim_region(part):
    """The recolour region (1-2) the garment's edge trims are in (a T-shirt's cuffs), or None if it has none."""
    mesh = part.data
    values = np.empty(len(mesh.loops) * 4)
    mesh.color_attributes[ct.COLOUR_ATTRIBUTE].data.foreach_get("color", values)
    corner = values.reshape(-1, 4)[:, 1:].argmax(axis=1)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    counts = np.zeros(3)
    for f in bm.faces:
        if any(e.is_boundary for e in f.edges):
            counts[corner[f.loops[0].index]] += 1
    bm.free()
    return int(counts[1:].argmax()) + 1 if counts[1:].max() > 0 else None


def add_band(part, width, region, edge="top"):
    """
    A band ``width`` metres deep along the garment's top edge (the open edge loop that sits highest: a waistband,
    a belt) in recolour ``region`` (0-2), cut along its lower line so it's clean. The scan's thin brown belt was
    shading on navy, and no colour block of its own. With ``edge`` "bottom", along every edge loop but the top one
    instead (a pair of shorts' turned-up cuffs).
    """
    from mathutils.kdtree import KDTree
    mesh = part.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    boundary = [v for v in bm.verts if v.is_boundary]
    loops, seen = [], set()
    for start in boundary:
        if start.index in seen:
            continue
        stack, loop = [start], []
        seen.add(start.index)
        while stack:
            v = stack.pop()
            loop.append(v.index)
            for e in v.link_edges:
                w = e.other_vert(v)
                if e.is_boundary and w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        loops.append(loop)
    co = np.array([v.co[:] for v in bm.verts])
    top = max(loops, key=lambda l: co[l, 2].mean())
    if edge == "bottom":
        top = [v for loop in loops if loop is not top for v in loop]
    tree = KDTree(len(top))
    for i, v in enumerate(top):
        tree.insert(co[v], i)
    tree.balance()
    field = np.array([tree.find(p)[2] for p in co]) - width
    value = _cut_along(bm, np.where(np.abs(field) < 1e-5, 1e-5, field), 0.0)
    banded = [f for f in bm.faces if np.mean([value[v] for v in f.verts]) < 0]
    colours = bm.loops.layers.float_color.get(ct.COLOUR_ATTRIBUTE)
    one_hot = [0.0, 0.0, 0.0, 0.0]
    one_hot[region + 1] = 1.0
    for f in banded:
        for loop in f.loops:
            loop[colours] = one_hot
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return len(banded)


def limit_influences(part):
    """At most character_tools.MAX_INFLUENCES bones per vertex, normalised, as the validator requires."""
    if not part.vertex_groups:  # not skinned yet (loose layers get their weights by transfer later)
        return
    bpy.ops.object.select_all(action="DESELECT")
    part.select_set(True)
    bpy.context.view_layer.objects.active = part
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=ct.MAX_INFLUENCES)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)


def covered_regions(part, body, regions, candidates, reach=0.08, share=0.995, skirt_axis=None):
    """
    Body regions the part hides: those (among ``candidates``) where at least ``share`` of the skin, looking out along
    its normal, meets the part within ``reach``. The shader hides covered skin, so nothing pokes through. All but a
    few: a region hidden under a part that stops short of its edge (a low sneaker on the Foot region) left a hole.
    A skirt hangs clear of the legs, so with ``skirt_axis`` (a point on its vertical axis) the trunk's skin also counts
    as covered where it meets the part looking straight out from that axis, within a skirt's reach: looking along the
    normal, the crotch never met the tube, and the uncovered pelvis's outline showed through a sundress as black
    blotches. Not the legs: a knee striding through a skirt's front would vanish.
    """
    tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(part)], [tuple(p.vertices) for p in part.data.polygons])
    co = bt.vertices(body)
    normals = np.empty(len(co) * 3)
    body.data.vertices.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)

    def meets(i, trunk):
        if tree.ray_cast(Vector(tuple(co[i])), Vector(tuple(normals[i])), reach)[0] is not None:
            return True
        if skirt_axis is None or not trunk:
            return False
        out = np.array([co[i][0] - skirt_axis[0], co[i][1] - skirt_axis[1], 0.0])
        if np.linalg.norm(out) < 1e-6:
            return False
        return tree.ray_cast(Vector(tuple(co[i])), Vector(tuple(out / np.linalg.norm(out))), 0.5)[0] is not None

    covered = []
    for region in candidates:
        members = np.flatnonzero(regions == region)
        if not len(members):
            continue
        hits = sum(1 for i in members if meets(i, region in ("LowerTorso", "Pelvis")))
        if hits >= share * len(members):
            covered.append(region)
    return covered


# ---------------------------------------------------------------------------------------------------- fitted garments

# Built from the body; everything else keeps the scan's geometry. Shoes too: a scan's shoes are shaped for its own
# feet, and ours poked through their toes.
FITTED = {"top", "bottom", "legwear", "onepiece", "shoes"}
COVER_SMOOTHING = 0.03  # metres: the blur on the body's coverage before the cut, so hems are smooth curves; a garment
                        # with fine edges (a tank top's straps) sets a smaller "edge_blur" in outfits.json
CLOSING = 0.03  # metres: the blur for closing gaps in the coverage (closes slits up to about twice this), and the lift's


def _blur(neighbours, values):
    """A per-vertex field blurred by a _surface_neighbours matrix."""
    rows, cols, weights = neighbours
    count = len(values)
    return (np.bincount(rows, weights=weights * values[cols], minlength=count)
            / np.maximum(np.bincount(rows, weights=weights, minlength=count), 1e-12))


def _scoop(body, joints, depth, straps, soft=0.015):
    """
    A scoop neckline as an uncovered field: on the front, everything above a curve ``depth`` metres below the neck
    joint at the centre, rising to the neck joint's height at the straps' inner edges. The scan's neckline sat at
    the collarbone in blotches.
    """
    co = bt.vertices(body)
    neck = np.asarray(joints["Neck"], dtype=float)
    reach = 0.5 * abs(float(joints["UpperArm.L"][0])) - straps / 2
    t = np.clip(np.abs(co[:, 0]) / max(reach, 1e-6), 0.0, 1.0)
    edge = neck[2] - depth * np.sqrt(1.0 - t * t)  # a round scoop, not a V
    front = co[:, 1] < float(joints["Chest"][1])
    return np.where(front & (co[:, 2] > neck[2] - depth - 0.02), np.clip((co[:, 2] - edge) / soft + 0.5, 0.0, 1.0), 0.0)


def _straps(body, joints, width, soft=0.015):
    """
    Shoulder straps ``width`` metres wide as a coverage field: a band over each shoulder, centred halfway between the
    neck and the shoulder joint, from the chest up over the top of the shoulder and down the back. A tank top's straps
    are too thin for the scan to show (it painted them on the skin, or not at all).
    """
    zone, side = _strap_lines(body, joints, width)
    band = np.clip((width / 2 - np.abs(side)) / soft + 0.5, 0.0, 1.0)
    return np.where(zone, band, 0.0)


def _strap_lines(body, joints, width):
    """
    Where shoulder straps ``width`` wide may lie (a mask of body vertices) and each vertex's signed distance across
    the strap's centre line (inside within half the width either way): the band's two edges are straight lines in it.
    """
    co = bt.vertices(body)
    weights, names = bt.weight_matrix(body)
    torso = [i for i, n in enumerate(names) if n in ("Chest", "Spine", "Neck")]
    # Inside the shoulder joints counts too: one shoulder's top is mostly the upper arm's, and the strap split there.
    inside = (np.abs(co[:, 0]) < abs(float(joints["UpperArm.L"][0])) - 0.015) & (co[:, 2] < float(joints["Neck"][2]) + 0.02)
    on_torso = (weights[:, torso].sum(axis=1) > 0.5) | inside
    centre = 0.5 * abs(float(joints["UpperArm.L"][0]))
    # Measured across the body as it stands in the relaxed pose: the shoulder's skin is partly the upper arm's, and
    # a strap even in the bind pose spread to 5 cm once the arms came down.
    posed = bt.skin_positions(co, weights, names, bt.pose_skinning(joints))
    side = np.abs(posed[:, 0]) - centre
    low = float(joints["Chest"][2])
    # Up over the top of the shoulder (the highest torso skin under the strap), and no further up the neck.
    under = on_torso & (np.abs(side) < width)
    high = float(co[under, 2].max()) + 0.005 if under.any() else float(joints["Neck"][2]) + 0.03
    return on_torso & (co[:, 2] > low) & (co[:, 2] < high), side


def _extended_down(body, covered, distance):
    """Coverage moved ``distance`` metres further down the torso: each torso point takes the coverage above it."""
    from mathutils.kdtree import KDTree
    co = bt.vertices(body)
    weights, names = bt.weight_matrix(body)
    torso = [i for i, n in enumerate(names) if n in ("Hips", "Spine", "Chest")]
    on_torso = weights[:, torso].sum(axis=1) > 0.5
    tree = KDTree(len(co))
    for i, p in enumerate(co):
        tree.insert(p, i)
    tree.balance()
    result = covered.copy()
    for i in np.flatnonzero(on_torso & (covered < 1.0)):
        above = tree.find(co[i] + np.array([0.0, 0.0, distance]))[1]
        result[i] = max(result[i], covered[above])
    return result


def _body_adjacency(mesh):
    edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", edges)
    return edges[0::2], edges[1::2]


def project_garment(body, scan, garment, labels, index, offset, name, joints, always=None, max_loose=0.06,
                    reach_out=0.15, reach_in=0.10, edge_blur=None, drape=False, extend_down=0.0, ease=0.0,
                    straps=0.0, scoop=0.0, gather=0.0, sleeve_ease=0.0, flare=(0.0, 0.0), top=None, neck=None, hem=None,
                    tucked=None, waist=None, measure_from=None, ring=None):
    """
    A fitted garment built from the canonical body itself: the body's own surface over the regions the scan's
    garment covers, lifted by the layer offset, or by the scan's own looseness where it hangs further out (a flared
    hem, baggy shorts). The scan decides where the garment is (sleeve length, hem, neckline) and its colours; the
    body decides its shape, so the fit is exact, and every vertex carries the body's own skin weights and shape keys.

    Built this way because aligning a text-to-image outfit by its joints only moves bones: its garments kept the
    outfit model's proportions (shoulders wider than ours) and came out boxy and lumpy when pushed onto our body.

    Each body vertex looks along its normal both ways for the nearest scan surface: outwards up to ``reach_out`` (a
    shirt hanging off the bust), inwards up to ``reach_in`` (our hips are fuller than the outfit model's, so its shorts
    lie inside them). Coverage is smoothed over the body and the garment cut along its half-way line, so hems and
    sleeve ends are smooth curves rather than the body's triangle edges. ``always`` (a mask of body vertices) is
    covered whatever the scan shows: a shoe encloses the whole foot even where our toes reach past the scan's shoe.

    Where the garment is and how loose, it reads off the T-posed scan; its shape it gets in the game's arms-down pose
    (_shape_in_pose), with the slack round each limb evened out (_even_limb_slack) and, with ``drape``, hanging like
    cloth from the bust and shoulder blades (_draped). ``joints``: the body's skeleton.
    """
    scan_tree = BVHTree.FromPolygons([tuple(v) for v in bt.vertices(scan)], [tuple(p.vertices) for p in scan.data.polygons])
    mesh = body.data
    co = bt.vertices(body)
    normals = np.empty(len(co) * 3)
    mesh.vertices.foreach_get("normal", normals)
    normals = normals.reshape(-1, 3)

    covered = np.zeros(len(co))
    loose = np.zeros(len(co))
    cluster = np.full(len(co), -1, dtype=np.int32)
    # The body the scan is measured against: the masculine shape for an outfit generated on the masculine base, whose
    # broader frame would otherwise read as loose cloth.
    look_from, look_along = co, normals
    if measure_from is not None:
        mesh.calc_loop_triangles()
        triangles = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int64)
        mesh.loop_triangles.foreach_get("vertices", triangles)
        look_from, look_along = measure_from, _vertex_normals(measure_from, triangles.reshape(-1, 3))
    for i in range(len(co)):
        point, normal = Vector(tuple(look_from[i])), Vector(tuple(look_along[i]))
        _, _, out_face, out_distance = scan_tree.ray_cast(point, normal, reach_out)
        _, _, in_face, in_distance = scan_tree.ray_cast(point, -normal, reach_in)
        if out_face is None and in_face is None:
            continue
        face, distance = ((out_face, out_distance) if in_face is None or (out_face is not None and out_distance <= in_distance)
                          else (in_face, -in_distance))
        if garment[face] != index:
            continue
        covered[i] = 1.0
        loose[i] = distance
        cluster[i] = labels[face]

    a, b = _body_adjacency(mesh)
    if always is not None:
        covered[always] = 1.0
    if ring is not None:
        # A level band all round the torso, ``ring["width"]`` deep: under the cups for a bra or bikini top ("at":
        # "bottom"), along the top edge for briefs or bikini bottoms ("at": "top"). The scan showed these garments
        # from the front; round the back their thin bands were lost, or never painted.
        torso_r = np.isin(body_regions(body), ["UpperTorso", "LowerTorso", "Pelvis"])
        front = torso_r & (co[:, 1] < float(joints["Chest"][1])) & (covered > 0)
        if front.any():
            width, z = ring.get("width", 0.025), co[:, 2]
            if ring.get("at", "bottom") == "bottom":
                low = float(np.percentile(z[front], 3))
                band = (z > low - 0.003) & (z < low + width)
                if ring.get("back") == "band":  # a bra's back is its band and straps only (the straps come later)
                    covered = np.where(torso_r & (co[:, 1] > float(joints["Chest"][1]) + 0.02) & ~band, 0.0, covered)
            else:
                high = float(np.percentile(z[front], 97))
                band = (z < high + 0.003) & (z > high - width)
            covered = np.where(torso_r & band, 1.0, covered)
    # Thin features (a tank top's straps) need the closing as fine as their edges: blurred over 3 cm, a 1 cm strap
    # never reached the closing's threshold and vanished.
    closing = CLOSING if edge_blur is None else min(CLOSING, 1.5 * edge_blur)
    wide, fine = _surface_neighbours(body, closing), _surface_neighbours(body, COVER_SMOOTHING)
    # Closing (grow, then shrink back): fills slits the texture left, like the skin-coloured seam Hunyuan paints down
    # each side of a pair of shorts where its front and back views meet. Hems and the neck opening stay where they were.
    covered = (_blur(wide, (_blur(wide, covered) > 0.15).astype(float)) > 0.85).astype(float)
    if extend_down > 0:  # a top reaches down over the waistband: no midriff showing once it hangs loose
        covered = _extended_down(body, covered, extend_down)
    if edge_blur is not None and edge_blur != COVER_SMOOTHING:
        fine = _surface_neighbours(body, edge_blur)
    covered = _blur(fine, covered)  # no speckle, smooth edges
    if top is not None:  # no higher than this (low-top sneakers stop under the ankle)
        co_body = bt.vertices(body)
        if np.ndim(top):  # [front, side, back]: a low-top's collar, up the tongue and the heel, down under the ankle bones
            front, side, back = top
            ankle = np.where((co_body[:, 0] > 0)[:, None], np.asarray(joints["Foot.L"])[:2], np.asarray(joints["Foot.R"])[:2])
            u = co_body[:, :2] - ankle
            u /= np.maximum(np.linalg.norm(u, axis=1), 1e-9)[:, None]
            ahead, behind = np.maximum(-u[:, 1], 0.0), np.maximum(u[:, 1], 0.0)
            top = side + (front - side) * ahead ** 2 + (back - side) * behind ** 2
        covered = np.minimum(covered, np.clip((top - co_body[:, 2]) / 0.015 + 0.5, 0.0, 1.0))
    if neck is not None:
        # A crew neck: round the neck up to this far above the neck joint, a level ring. The scan's collar climbed the
        # neck in folds, and it covered the neck only in patches, so its edge was ragged.
        on_neck = np.isin(body_regions(body), ["Neck", "Head"])
        cap = float(joints["Neck"][2]) + neck
        covered = np.where(on_neck, np.clip((cap - bt.vertices(body)[:, 2]) / 0.015 + 0.5, 0.0, 1.0), covered)
    if waist is not None:
        torso = np.isin(body_regions(body), ["UpperTorso", "LowerTorso", "Pelvis"])
        z = bt.vertices(body)[:, 2]
        line = float(joints["Hips"][2]) + WAIST_ABOVE_HIPS
        if waist == "bottom":  # up to the waist line at least, and all of the pelvis (a coat over the jeans hid it)
            reach = np.clip((line - z) / 0.015 + 0.5, 0.0, 1.0)
            covered = np.where(torso & (z > float(joints["Hips"][2])), np.maximum(covered, reach), covered)
            covered = np.where(body_regions(body) == "Pelvis", 1.0, covered)
        elif waist == "top":  # down past it at least
            reach = np.clip((z - (line - WAIST_OVERLAP)) / 0.015 + 0.5, 0.0, 1.0)
            covered = np.where(torso & (z < line + 0.08), np.maximum(covered, reach), covered)
    hem_drop = hem
    if hem is not None:
        # Shorts' or joggers' legs end in a level hem this far below the hip joints, covering the legs above it: the
        # scan's hem was a ragged colour edge.
        on_thigh = np.isin(body_regions(body), ["ThighL", "ThighR", "CalfL", "CalfR"])
        level = float(joints["UpperLeg.L"][2]) - hem
        covered = np.where(on_thigh, np.clip((bt.vertices(body)[:, 2] - level) / 0.015 + 0.5, 0.0, 1.0), covered)
    if tucked is not None:
        # Tucked in: a top ends in a level hem this far above the hip joint, so the belt over the bottom's waist shows
        # (the image's tank top is tucked into its jeans; hanging over, it hid the belt). It reaches that line too: a
        # shirt the scan shows ending at its belt left a ragged hem above the waistband.
        on_torso = np.isin(body_regions(body), ["UpperTorso", "LowerTorso", "Pelvis"])
        level = float(joints["Hips"][2]) + tucked
        z = bt.vertices(body)[:, 2]
        ramp = np.clip((z - level) / 0.015 + 0.5, 0.0, 1.0)
        covered = np.where(on_torso, np.minimum(covered, ramp), covered)
        covered = np.where(z < level - 0.02, 0.0, covered)  # nothing below it: a dress's skirt is its own tube
        covered = np.where(on_torso & (z < level + 0.1), np.maximum(covered, ramp), covered)
    if scoop > 0:
        covered = np.minimum(covered, 1.0 - _scoop(body, joints, scoop, straps))
    if straps > 0:
        # Above the armpits a strapped top is only its straps: the scan's coverage spread over the shoulder caps.
        top_line = float(joints["UpperArm.L"][2]) - 0.035
        co_body = bt.vertices(body)
        # Ramps wider than the body's triangles (about 1 cm), so the cut finds the line between vertices instead of
        # snapping to the triangles' edges: a strap cut from a 4 mm ramp had sawtooth edges.
        covered = np.minimum(covered, np.clip((top_line - co_body[:, 2]) / 0.015 + 0.5, 0.0, 1.0))
        unstrapped = covered.copy()
        covered = np.maximum(covered, _straps(body, joints, straps))
    covered = np.where(np.abs(covered - 0.5) < 1e-4, 0.5 + 1e-4, covered)
    inside = covered > 0.5
    for _ in range(4):  # vertices the smoothing covered take a neighbour's colour
        missing = inside & (cluster < 0)
        if not missing.any():
            break
        for i, j in ((a, b), (b, a)):
            take = missing[i] & (cluster[j] >= 0)
            cluster[i[take]] = cluster[j[take]]

    body_weights = bt.weight_matrix(body)
    lift = np.clip(loose, max(offset, ease), max(max_loose, ease))  # ease: at least this loose (baggy joggers)
    lift[~inside] = offset
    if sleeve_ease > 0:  # short sleeves stand off the arm, open at the end, as the image's do
        arm = [i for i, n in enumerate(body_weights[1]) if n.startswith(("UpperArm", "LowerArm"))]
        on_arm = body_weights[0][:, arm].sum(axis=1)
        lift = np.where(inside, np.maximum(lift, sleeve_ease * np.clip(on_arm, 0.0, 1.0)), lift)
    lift = _even_limb_slack(co, body_weights[0], body_weights[1], joints, lift, inside)
    # Even, so the garment is as smooth as the body; blended only between parts that move together.
    lift = np.maximum(_blur(_surface_neighbours(body, CLOSING, affinity=body_weights[0]), lift), offset)
    if flare[0] > 0 and inside.any():  # flared at the hem (A-line shorts): more ease over the last stretch
        hem = float(co[inside, 2].min())
        t = np.clip((hem + flare[1] - co[:, 2]) / flare[1], 0.0, 1.0)
        # Away from the inner thigh only: flared inwards too, the two legs met in a skirt.
        outward = np.clip(0.4 + normals[:, 0] * np.sign(co[:, 0]), 0.0, 1.0)
        outward = _blur(_surface_neighbours(body, 0.03), outward)  # the inner thigh's normals made the hem bulge
        lift = np.where(inside, lift + flare[0] * t * t * (3 - 2 * t) * outward, lift)
    if gather > 0 and inside.any():  # gathered at the hem (joggers' ankle cuffs): the ease goes over the last stretch
        hem = float(co[inside, 2].min())
        t = np.clip((co[:, 2] - hem) / gather, 0.0, 1.0)
        lift = offset + (lift - offset) * t * t * (3 - 2 * t)
    if waist == "bottom":
        # Fitted from the waist line down to the top of the thigh, easing out below: any top, however short or loose,
        # hangs over it (baggy joggers came out through a camp shirt's hem).
        line = float(joints["Hips"][2]) + WAIST_ABOVE_HIPS
        t = np.clip((line - 0.12 - co[:, 2]) / 0.10, 0.0, 1.0)
        lift = offset + (lift - offset) * t * t * (3 - 2 * t)
    if tucked is not None:  # the hem band half the layer offset off the skin: inside any bottom's waistband
        band = co[:, 2] < float(joints["Hips"][2]) + tucked + 0.04
        lift = np.where(band, np.minimum(lift, offset * 0.5), lift)
    if neck is not None:  # a crew neck hugs the neck: the scan's thick collar stood 2 cm off it all round
        across = np.linalg.norm(co[:, :2] - np.array([0.0, float(joints["Neck"][1])]), axis=1)
        near = np.clip((0.11 - across) / 0.03, 0.0, 1.0) * np.clip((co[:, 2] - float(joints["Neck"][2]) + 0.05) / 0.04, 0.0, 1.0)
        lift = lift - np.maximum(lift - offset - 0.002, 0.0) * near

    part = body.copy()
    part.data = body.data.copy()
    part.name = part.data.name = name
    bpy.context.scene.collection.objects.link(part)
    for modifier in list(part.modifiers):
        part.modifiers.remove(modifier)
    if "lift" in part.data.attributes:
        part.data.attributes.remove(part.data.attributes["lift"])
    part.data.attributes.new("lift", "FLOAT", "POINT").data.foreach_set("value", lift)

    if "vcluster" in part.data.attributes:
        part.data.attributes.remove(part.data.attributes["vcluster"])
    part.data.attributes.new("vcluster", "INT", "POINT").data.foreach_set("value", cluster)
    bm = bmesh.new()
    bm.from_mesh(part.data)
    if straps > 0:
        # Each strap's edges cut along their own straight lines: cut as one band's level line, a strap about as narrow
        # as the body's triangles lost its middle wherever two vertices straddled it, and came out as a string of beads.
        zone, side = _strap_lines(body, joints, straps)
        bm.verts.ensure_lookup_table()
        fields = {"cover": {v: unstrapped[v.index] for v in bm.verts},
                  "left": {v: side[v.index] + straps / 2 for v in bm.verts},
                  "right": {v: straps / 2 - side[v.index] for v in bm.verts},
                  "zone": {v: float(zone[v.index]) for v in bm.verts}}
        _cut_fields(bm, fields, "cover", 0.5)
        _cut_fields(bm, fields, "left", 0.0, within="zone")
        _cut_fields(bm, fields, "right", 0.0, within="zone")
        kept = {v: fields["cover"][v] >= 0.5 - 1e-6 or (fields["zone"][v] >= 1.0 - 1e-6 and fields["left"][v] >= -1e-6
                                                         and fields["right"][v] >= -1e-6) for v in bm.verts}
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(kept[v] for v in f.verts)], context="FACES")
    else:
        value = _cut_along(bm, covered, 0.5)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(value[v] < 0.5 - 1e-6 for v in f.verts)], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(part.data)
    bm.free()
    part.data.update()
    _smooth_edges(part, body, body_weights, lift, cluster)
    # A strapped top drapes only below the armpits (its straps lie on the shoulders).
    drape_below = float(joints["UpperArm.L"][2]) - 0.06 if straps > 0 else None
    # Tucked in: the last few centimetres stay close and undraped, so any bottom's waistband sits outside them.
    drape_above = float(joints["Hips"][2]) + tucked + 0.02 if tucked is not None else None
    _shape_in_pose(part, body, body_weights, joints, offset, drape=drape, drape_below=drape_below, drape_above=drape_above)
    if hem_drop is not None:
        _level_hems(part, float(joints["UpperLeg.L"][2]) - hem_drop / 2)
        _span_crotch(part, body, joints, offset)
    return part, int(inside.sum())


def _span_crotch(part, body, joints, offset, iterations=60):
    """
    A pair of shorts spans the crotch: the surface there is smoothed (fills the hollows either side, never inside
    the body), so it no longer shows the body's shape between the legs. The basis and every shape key alike.
    """
    mesh = part.data
    co = bt.vertices(part)
    crotch = float(joints["UpperLeg.L"][2]) - 0.06
    weight = np.clip((0.07 - np.abs(co[:, 0])) / 0.03, 0.0, 1.0) * np.clip(1.0 - np.abs(co[:, 2] - crotch - 0.03) / 0.07, 0.0, 1.0)
    weight = weight[:, None] * 0.5
    pairs = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", pairs)
    a, b = pairs[0::2], pairs[1::2]
    count = np.maximum(np.bincount(np.concatenate([a, b]), minlength=len(co)), 1)[:, None]

    def smoothed(points):
        points = points.copy()
        for _ in range(iterations):
            total = np.zeros_like(points)
            np.add.at(total, a, points[b])
            np.add.at(total, b, points[a])
            points += weight * (total / count - points)
        return points

    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else []
    for key in keys:
        key.data.foreach_set("co", smoothed(_key_positions(key)).ravel())
    mesh.vertices.foreach_set("co", (smoothed(co) if not keys else _key_positions(mesh.shape_keys.reference_key)).ravel())
    mesh.update()
    _push_out(part, body, offset)


def _level_hems(part, below, reach=0.04):
    """
    Each open edge loop lower than ``below`` (a pair of shorts' leg openings) made level at its mean height, the
    surface above eased after it over ``reach`` metres. Lifted off the tapering thigh along its normals, the hem rose and
    fell by 3 cm. The basis and every shape key alike.
    """
    from mathutils.kdtree import KDTree
    mesh = part.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary = [v for v in bm.verts if v.is_boundary]
    loops, seen = [], set()
    for start in boundary:
        if start.index in seen:
            continue
        stack, loop = [start], []
        seen.add(start.index)
        while stack:
            v = stack.pop()
            loop.append(v.index)
            for e in v.link_edges:
                w = e.other_vert(v)
                if e.is_boundary and w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        loops.append(loop)
    bm.free()
    co = bt.vertices(part)
    hems = [l for l in loops if co[l, 2].mean() < below and len(l) > 8]
    if not hems:
        return 0
    points = np.concatenate(hems)
    delta = np.concatenate([co[l, 2].mean() - co[l, 2] for l in hems])
    tree = KDTree(len(points))
    for i, v in enumerate(points):
        tree.insert(co[v], i)
    tree.balance()
    shift = np.zeros(len(co))
    for i, p in enumerate(co):
        _, j, d = tree.find(p)
        t = max(0.0, 1.0 - d / reach)
        shift[i] = delta[j] * t * t * (3 - 2 * t)
    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else []
    for key in keys:
        moved = _key_positions(key)
        moved[:, 2] += shift
        key.data.foreach_set("co", moved.ravel())
    co[:, 2] += shift
    mesh.vertices.foreach_set("co", co.ravel())
    mesh.update()
    return len(hems)


def _smooth_edges(part, body, body_weights, lift, cluster, iterations=40):
    """
    Straightens the garment's open edges (hems, cuffs, necklines, boot tops) along their own line, with the ring of
    vertices inside them, keeping everything on the body's surface. The cut follows the scan's colour boundary, which
    the texture paints in blotches where two garments meet; blurring the coverage hard enough to hide that also wiped
    out a tank top's straps. Each moved vertex takes the body's weights, shapes, lift and colour cluster at the point
    it lands on. The part must still lie on the body (straight after the cut, before it is lifted or posed).
    """
    mesh = part.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    along = {}
    for v in bm.verts:
        if v.is_boundary:
            ends = [e.other_vert(v).index for e in v.link_edges if e.is_boundary]
            if len(ends) == 2:
                along[v.index] = ends
    inner = {}
    for i in along:
        for e in bm.verts[i].link_edges:
            w = e.other_vert(bm.verts[i])
            if not w.is_boundary and w.index not in inner:
                inner[w.index] = [x.other_vert(w).index for x in w.link_edges]
    bm.free()
    if not along:
        return

    body_mesh = body.data
    body_mesh.calc_loop_triangles()
    triangles = np.empty(len(body_mesh.loop_triangles) * 3, dtype=np.int64)
    body_mesh.loop_triangles.foreach_get("vertices", triangles)
    triangles = triangles.reshape(-1, 3)
    body_co = bt.vertices(body)
    tree = BVHTree.FromPolygons([tuple(v) for v in body_co], [tuple(t) for t in triangles])

    co = bt.vertices(part)
    moving = np.array(sorted(set(along) | set(inner)))
    for step in range(iterations):
        factor = TAUBIN[step % 2]  # Taubin: smooth, then un-shrink, so hem loops don't creep up or down the limb
        target = co.copy()
        for i, (a, b) in along.items():
            target[i] = co[i] + factor * (0.5 * (co[a] + co[b]) - co[i])
        for i, ring in inner.items():
            target[i] = co[i] + factor * (co[ring].mean(axis=0) - co[i])
        for i in moving:
            location, _, _, _ = tree.find_nearest(Vector(tuple(target[i])))
            co[i] = location

    # What the body has where each moved vertex now lies: barycentric on its nearest triangle.
    weights, names = body_weights
    keys = {k.name: _key_positions(k) for k in body_mesh.shape_keys.key_blocks} if body_mesh.shape_keys else {}
    groups = {g.name: g for g in part.vertex_groups}
    vcluster = np.empty(len(mesh.vertices), dtype=np.int32)
    mesh.attributes["vcluster"].data.foreach_get("value", vcluster)
    part_lift = np.empty(len(mesh.vertices))
    mesh.attributes["lift"].data.foreach_get("value", part_lift)
    part_keys = {k.name: _key_positions(k) for k in mesh.shape_keys.key_blocks} if mesh.shape_keys else {}
    for i in moving:
        _, _, index, _ = tree.find_nearest(Vector(tuple(co[i])))
        corners = triangles[index]
        share = np.array(ct.barycentric(Vector(tuple(co[i])), *(Vector(tuple(body_co[c])) for c in corners)))
        share = np.clip(share, 0.0, None)
        share /= max(share.sum(), 1e-12)
        part_lift[i] = float(share @ lift[corners])
        vcluster[i] = int(cluster[corners[int(np.argmax(share))]])
        for name, positions in part_keys.items():
            if name in keys:
                positions[i] = share @ keys[name][corners]
        blended = share @ weights[corners]
        strongest = np.argsort(-blended)[:ct.MAX_INFLUENCES]
        for group in part.vertex_groups:
            group.remove([int(i)])
        total = max(blended[strongest].sum(), 1e-12)
        for column in strongest:
            if blended[column] > 1e-4:
                group = groups.get(names[column]) or part.vertex_groups.new(name=names[column])
                groups[names[column]] = group
                group.add([int(i)], float(blended[column] / total), "REPLACE")
    mesh.vertices.foreach_set("co", co.ravel())
    for name, positions in part_keys.items():
        key = mesh.shape_keys.key_blocks[name]
        if name in keys:
            key.data.foreach_set("co", positions.ravel())
    if mesh.shape_keys:
        mesh.shape_keys.reference_key.data.foreach_set("co", co.ravel())
    mesh.attributes["vcluster"].data.foreach_set("value", vcluster)
    mesh.attributes["lift"].data.foreach_set("value", part_lift)
    mesh.update()


def _push_out(part, body, offset):
    """
    Garment vertices nearer the body than ``offset``, or inside it, go out to ``offset``, in every shape key against
    the body in that shape: the masculine shape folds the chest under itself in places, which the garment copies, and
    bridging can tip a vertex in at the armpits.
    """
    polygons = [tuple(p.vertices) for p in body.data.polygons]
    body_keys = body.data.shape_keys.key_blocks if body.data.shape_keys else {}
    keys = list(part.data.shape_keys.key_blocks) if part.data.shape_keys else [None]
    for key in keys:
        if key is not None and key.name not in body_keys:
            continue
        surface = bt.vertices(body) if key is None else _key_positions(body_keys[key.name])
        tree = BVHTree.FromPolygons([tuple(v) for v in surface], polygons)
        co = bt.vertices(part) if key is None else _key_positions(key)
        for _ in range(3):  # in a fold, the nearest face can change once a vertex has moved
            for i, p in enumerate(co):
                location, normal, _, _ = tree.find_nearest(Vector(tuple(p)))
                if location is not None and (Vector(tuple(p)) - location).dot(normal) < offset:
                    co[i] = tuple(location + normal * offset)
        (part.data.vertices if key is None else key.data).foreach_set("co", co.ravel())
    if part.data.shape_keys:
        part.data.vertices.foreach_set("co", _key_positions(part.data.shape_keys.reference_key).ravel())
    part.data.update()


def _key_positions(key):
    co = np.empty(len(key.data) * 3)
    key.data.foreach_get("co", co)
    return co.reshape(-1, 3)


BRIDGE = 0.045  # metres: the reach of hollow bridging


def _bridged(co, neighbours, triangles, edge, iterations=24, edge_rings=None):
    """
    A fitted garment's hollows filled the way cloth spans them (between the buttocks, under the bust, between the toes)
    instead of following the skin into them: each vertex moves out towards the blurred surface around it, never in, so
    it never sinks into the body. ``neighbours`` is a _surface_neighbours blur (by bone affinity, so cloth doesn't web
    an arm to the torso or one thigh to the other). Vertices on the garment's ``edge`` stay put. (Smoothing over mesh
    neighbours only filled the narrowest creases: it stalls once each vertex's neighbours look flat.)
    """
    rows, cols, weights = neighbours
    count = len(co)
    total = np.maximum(np.bincount(rows, weights=weights, minlength=count), 1e-12)
    # Bridging eases in over the first rings in from the edge: next to it a narrow strip (a tank top's strap) bulged
    # out into a fin between its held edges.
    hold = np.ones(count) if edge.dtype != bool else (~edge).astype(float)
    if edge.dtype == bool and edge_rings is not None:
        a, b = edge_rings
        ring = np.where(edge, 0, 99)
        for step in range(1, 5):
            near = np.zeros(count, dtype=bool)
            near[a[ring[b] == step - 1]] = True
            near[b[ring[a] == step - 1]] = True
            ring = np.where(near & (ring > step), step, ring)
        hold = np.clip(ring / 4.0, 0.0, 1.0)
    co = co.copy()
    for _ in range(iterations):
        blurred = np.stack([np.bincount(rows, weights=weights * co[cols, k], minlength=count) for k in range(3)], 1) / total[:, None]
        normals = _vertex_normals(co, triangles)
        out = np.maximum(((blurred - co) * normals).sum(axis=1), 0.0)
        co += (out * hold)[:, None] * normals
    return co


def _fair(co, edges, held, iterations=24):
    """
    Taubin smoothing of a garment's surface (its ``held`` edge vertices stay put): the small dents bridging and
    draping leave turn the toon outline's inverted hull into dark squiggles across the cloth.
    """
    a, b = edges
    count = np.maximum(np.bincount(np.concatenate([a, b]), minlength=len(co)), 1)[:, None]
    free = (~held)[:, None]
    co = co.copy()
    for step in range(iterations):
        total = np.zeros_like(co)
        np.add.at(total, a, co[b])
        np.add.at(total, b, co[a])
        co = co + free * TAUBIN[step % 2] * (total / count - co)
    return co


def _pushed_out(co, tree, offset, passes=3):
    """``co`` with every point nearer the surface in ``tree`` than ``offset`` (or inside it) moved out to ``offset``."""
    co = co.copy()
    for _ in range(passes):  # in a fold, the nearest face can change once a point has moved
        for i, p in enumerate(co):
            location, normal, _, _ = tree.find_nearest(Vector(tuple(p)))
            if location is not None and (Vector(tuple(p)) - location).dot(normal) < offset:
                co[i] = tuple(location + normal * offset)
    return co


# The limb bones and the joint each runs to: slack round a sleeve or a shorts leg is evened out along them.
LIMBS = {"UpperArm.L": "LowerArm.L", "UpperArm.R": "LowerArm.R", "LowerArm.L": "Hand.L", "LowerArm.R": "Hand.R",
         "UpperLeg.L": "LowerLeg.L", "UpperLeg.R": "LowerLeg.R", "LowerLeg.L": "Foot.L", "LowerLeg.R": "Foot.R"}


def _even_limb_slack(co, weights, names, joints, lift, inside, ring=0.015):
    """
    The lift round each limb made the same all the way round, ring by ring along the bone (``ring`` metres apart).
    On the T-posed outfit model a loose sleeve hangs under the arm; kept as it was, that slack swung up beside the
    shoulder when the arms came down, like a shoulder pad. A tube of even slack sits right in any pose.
    """
    evened, share = np.zeros(len(co)), np.zeros(len(co))
    for bone, child in LIMBS.items():
        if bone not in names or bone not in joints or child not in joints:
            continue
        w = weights[:, names.index(bone)]
        a, b = np.asarray(joints[bone], dtype=float), np.asarray(joints[child], dtype=float)
        axis = b - a
        bins = np.floor(((co - a) @ axis) / max(np.linalg.norm(axis), 1e-9) / ring).astype(int)
        mask = (w > 0.3) & inside
        means = {k: float(lift[mask & (bins == k)].mean()) for k in np.unique(bins[mask])}
        ring_lift = np.array([means.get(k, np.nan) for k in bins])
        known = ~np.isnan(ring_lift)
        evened[known] += w[known] * ring_lift[known]
        share[known] += w[known]
    share = np.clip(share, 0.0, 1.0)
    return np.where(share > 0, evened + (1.0 - share) * lift, lift)


# How fast a draped top comes back in below what it hangs from (metres in per metre down).
DRAPE_SLOPE = 0.06  # nearly straight down from the bust, as the references' T-shirts hang


def _hull_radii(points, angles):
    """The radius, from the origin, of the convex hull of 2D ``points`` along each direction in ``angles``."""
    pts = sorted(map(tuple, points))
    if len(pts) < 3:
        return np.zeros(len(angles))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    hull = np.array(lower[:-1] + upper[:-1])
    a, b = hull, np.roll(hull, -1, axis=0)
    radii = np.zeros(len(angles))
    for k, theta in enumerate(angles):  # the ray from the origin at theta against each hull edge
        d = np.array([np.cos(theta), np.sin(theta)])
        e = b - a
        denom = d[0] * e[:, 1] - d[1] * e[:, 0]
        ok = np.abs(denom) > 1e-12
        t = np.where(ok, (a[:, 0] * e[:, 1] - a[:, 1] * e[:, 0]) / np.where(ok, denom, 1.0), -1.0)
        u = np.where(ok, (a[:, 0] * d[1] - a[:, 1] * d[0]) / np.where(ok, denom, 1.0), -1.0)
        hit = ok & (t > 0) & (u >= -1e-9) & (u <= 1 + 1e-9)
        radii[k] = t[hit].max() if hit.any() else 0.0
    return radii


def _draped(co, weights, names, joints, slope=DRAPE_SLOPE, angles=120, step=0.01, edges=None, relax=40, below=None,
            held=None, above=None):
    """
    A top hanging like cloth, in the pose (arms down). Round the torso's vertical axis, each horizontal slice of the
    torso is wrapped by its convex hull (cloth under tension spans the cleavage and the abs), each column of that
    hangs from what's above it, coming in only ``slope`` metres per metre of drop (it falls from the bust and the
    shoulder blades and hangs loose over the waist), and the result is blurred, so it is one smooth surface; then
    each torso point is pushed out to it, and (given the mesh's ``edges``) the moved points are spread evenly over it
    again. Fitted to the skin, the shirt showed the abs, the ribs and the cleavage.
    """
    torso = [i for i, n in enumerate(names) if n in ("Hips", "Spine", "Chest", "Neck")]
    share = weights[:, torso].sum(axis=1) if torso else np.zeros(len(co))
    on_torso = share > 0.6
    if on_torso.sum() < 50:
        return co
    axis = np.array([0.0, float(joints["Chest"][1])])
    d = co[:, :2] - axis
    radius = np.linalg.norm(d, axis=1)
    angle = np.arctan2(d[:, 1], d[:, 0])
    thetas = np.linspace(-np.pi, np.pi, angles, endpoint=False)
    z0, z1 = co[on_torso, 2].min(), co[on_torso, 2].max()
    heights = np.arange(z0, z1 + step, step)
    grid = np.zeros((len(heights), angles))
    for i, z in enumerate(heights):
        slab = on_torso & (np.abs(co[:, 2] - z) < step)
        if slab.sum() >= 3:
            grid[i] = _hull_radii(d[slab], thetas)
    for i in range(len(heights) - 2, -1, -1):  # hang: from the top down
        grid[i] = np.maximum(grid[i], grid[i + 1] - slope * step)
    for axis_, sigma in ((0, 2.0), (1, 2.0)):  # blur over height and round the axis (wrapping)
        kernel = np.exp(-0.5 * (np.arange(-6, 7) / sigma) ** 2)
        kernel /= kernel.sum()
        padded = np.concatenate([grid[:, -6:], grid, grid[:, :6]], axis=1) if axis_ == 1 else             np.concatenate([np.repeat(grid[:1], 6, axis=0), grid, np.repeat(grid[-1:], 6, axis=0)], axis=0)
        grid = np.apply_along_axis(lambda r: np.convolve(r, kernel, mode="valid"), axis_, padded)
    # Each point's drape radius, bilinear in (height, angle).
    fi = np.clip((co[:, 2] - z0) / step, 0, len(heights) - 1.001)
    fj = ((angle + np.pi) / (2 * np.pi) * angles) % angles
    i0, j0 = fi.astype(int), fj.astype(int)
    ti, tj = fi - i0, fj - j0
    j1 = (j0 + 1) % angles
    i1 = np.minimum(i0 + 1, len(heights) - 1)
    drape = (grid[i0, j0] * (1 - ti) * (1 - tj) + grid[i1, j0] * ti * (1 - tj)
             + grid[i0, j1] * (1 - ti) * tj + grid[i1, j1] * ti * tj)
    blend = np.clip((share - 0.3) / 0.5, 0.0, 1.0)
    # Out to nothing at the neck: the blur over height carried the chest's wider hull up into the neckline, and the
    # collar stood 4 cm off the throat in folds.
    neck = float(joints["Neck"][2])
    blend *= np.clip((neck + 0.01 - co[:, 2]) / 0.05, 0.0, 1.0)
    if below is not None:  # only below this height: pushed out to the shoulders' hull, a tank top's straps were fins
        blend *= np.clip((below - co[:, 2]) / 0.06, 0.0, 1.0)
    if above is not None:  # only above this height, fading in over 10 cm: a tucked top blouses gently into the waistband
        blend *= np.clip((co[:, 2] - above) / 0.10, 0.0, 1.0)

    def onto(points):  # each point out to the drape where it is (never in)
        dd = points[:, :2] - axis
        rr = np.linalg.norm(dd, axis=1)
        aa = np.arctan2(dd[:, 1], dd[:, 0])
        gi = np.clip((points[:, 2] - z0) / step, 0, len(heights) - 1.001)
        gj = ((aa + np.pi) / (2 * np.pi) * angles) % angles
        a0, b0 = gi.astype(int), gj.astype(int)
        ta, tb = gi - a0, gj - b0
        a1, b1 = np.minimum(a0 + 1, len(heights) - 1), (b0 + 1) % angles
        hang = (grid[a0, b0] * (1 - ta) * (1 - tb) + grid[a1, b0] * ta * (1 - tb)
                + grid[a0, b1] * (1 - ta) * tb + grid[a1, b1] * ta * tb)
        target = rr + blend * np.maximum(hang - rr, 0.0)
        moved = points.copy()
        moved[:, :2] = axis + dd * (target / np.maximum(rr, 1e-9))[:, None]
        return moved

    out = onto(co)
    # Collapsing a fold (the cleavage's two walls) onto the drape leaves its triangles degenerate and flipped: spread the
    # points that moved evenly over the drape again (uniform Laplacian, then back onto it).
    if edges is not None:
        a, b = edges
        count = np.maximum(np.bincount(np.concatenate([a, b]), minlength=len(co)), 1)[:, None]
        moved = np.linalg.norm(out - co, axis=1) > 0.001
        if held is not None:  # the garment's edges stay put: relaxing drew the hem 2 cm up
            moved &= ~held
        for _ in range(relax):
            total = np.zeros_like(out)
            np.add.at(total, a, out[b])
            np.add.at(total, b, out[a])
            out[moved] = onto(0.5 * out + 0.5 * total / count)[moved]
    return out


def _shape_in_pose(part, body, body_weights, joints, offset, drape=False, drape_below=None, drape_above=None):
    """
    The garment's final shape, made in the game's everyday pose (body_tools.RELAXED: arms down) and taken back to the
    bind pose: the body under it and the garment (the body's surface where it covers it, with its ``lift`` attribute)
    are both skinned into the pose; there the garment is lifted off the posed surface, its hollows bridged and any
    point inside the body pushed out; then each point is unskinned. Skinned back into that pose in the game, it is
    exactly that shape, instead of a T-pose fit bent by the bones (which bunched the shoulders). Every shape key the
    same way, against the body in that shape.
    """
    mesh = part.data
    skinning = bt.pose_skinning(joints)
    weights, names = bt.weight_matrix(part)
    body_w, body_names = body_weights
    lift = np.empty(len(mesh.vertices))
    mesh.attributes["lift"].data.foreach_get("value", lift)
    mesh.calc_loop_triangles()
    triangles = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int64)
    mesh.loop_triangles.foreach_get("vertices", triangles)
    triangles = triangles.reshape(-1, 3)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    edge = np.array([v.is_boundary for v in bm.verts])
    bm.free()
    body_polygons = [tuple(p.vertices) for p in body.data.polygons]
    pairs = np.empty(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", pairs)
    mesh_edges = (pairs[0::2], pairs[1::2])
    body_keys = {k.name: k for k in body.data.shape_keys.key_blocks} if body.data.shape_keys else {}

    keys = list(mesh.shape_keys.key_blocks) if mesh.shape_keys else [None]
    posed_basis = bt.skin_positions(bt.vertices(part), weights, names, skinning)
    neighbours = _surface_neighbours(part, BRIDGE, affinity=weights, co=posed_basis)
    for key in keys:
        rest = bt.vertices(part) if key is None else _key_positions(key)
        body_rest = bt.vertices(body) if key is None or key.name not in body_keys else _key_positions(body_keys[key.name])
        tree = BVHTree.FromPolygons([tuple(v) for v in bt.skin_positions(body_rest, body_w, body_names, skinning)], body_polygons)
        posed = bt.skin_positions(rest, weights, names, skinning)
        shaped = posed + _vertex_normals(posed, triangles) * lift[:, None]
        shaped = _bridged(shaped, neighbours, triangles, edge, edge_rings=mesh_edges)
        if drape:
            shaped = _draped(shaped, weights, names, joints, edges=mesh_edges, below=drape_below, above=drape_above, held=edge)
        shaped = _pushed_out(_fair(shaped, mesh_edges, edge), tree, offset)
        unposed = bt.unskin_positions(shaped, weights, names, skinning)
        (mesh.vertices if key is None else key.data).foreach_set("co", unposed.ravel())
    if mesh.shape_keys:
        mesh.vertices.foreach_set("co", _key_positions(mesh.shape_keys.reference_key).ravel())
    mesh.attributes.remove(mesh.attributes["lift"])
    mesh.update()


def _vertex_normals(co, triangles):
    corners = co[triangles]
    face_normals = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
    normals = np.zeros_like(co)
    for k in range(3):
        np.add.at(normals, triangles[:, k], face_normals)
    return normals / np.maximum(np.linalg.norm(normals, axis=1), 1e-12)[:, None]


def _surface_neighbours(obj, sigma, affinity=None, co=None):
    """
    A Gaussian blur over ``obj``'s surface as a sparse matrix (rows, cols, weights): neighbours by distance, each
    weighted by its share of the surface and by how much its normal agrees (none if it faces away, as across the gap
    between two legs). Distance rather than mesh connectivity, since the body is decimated into long slivers.
    ``affinity`` (vertices x bones skin weights) also weights neighbours by how alike they move, so an arm and the
    torso beside it, or the two thighs, don't blend. ``co`` replaces the mesh's positions (a posed copy).
    """
    from mathutils.kdtree import KDTree
    co = bt.vertices(obj) if co is None else co
    mesh = obj.data
    mesh.calc_loop_triangles()
    triangles = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int64)
    mesh.loop_triangles.foreach_get("vertices", triangles)
    triangles = triangles.reshape(-1, 3)
    corners = co[triangles]
    triangle_area = 0.5 * np.linalg.norm(np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0]), axis=1)
    area = np.zeros(len(co))
    for k in range(3):
        np.add.at(area, triangles[:, k], triangle_area / 3)
    normals = _vertex_normals(co, triangles)
    tree = KDTree(len(co))
    for i, p in enumerate(co):
        tree.insert(p, i)
    tree.balance()
    rows, cols, distances = [], [], []
    for i, p in enumerate(co):
        found = tree.find_range(p, 2.5 * sigma)
        rows.append(np.full(len(found), i, dtype=np.int64))
        cols.append(np.fromiter((f[1] for f in found), dtype=np.int64, count=len(found)))
        distances.append(np.fromiter((f[2] for f in found), dtype=float, count=len(found)))
    rows, cols, distances = np.concatenate(rows), np.concatenate(cols), np.concatenate(distances)
    weights = area[cols] * np.exp(-0.5 * (distances / sigma) ** 2) * np.clip((normals[rows] * normals[cols]).sum(axis=1), 0.0, 1.0)
    if affinity is not None:
        length = np.maximum(np.linalg.norm(affinity, axis=1), 1e-12)
        alike = (affinity[rows] * affinity[cols]).sum(axis=1) / (length[rows] * length[cols])
        weights *= np.clip(alike, 0.0, 1.0) ** 2
    return rows, cols, weights


def _cut_fields(bm, fields, which, level, within=None):
    """
    _cut_along for several fields at once (``fields``: name to {vertex: value}): cuts along ``fields[which]``'s
    ``level`` and gives each new vertex every field's value interpolated along its edge (the "zone" mask, the lower
    end's). With ``within`` (a field's name), only edges with both ends at 1 in it are cut.
    """
    field = fields[which]
    made = []
    crossing = [e for e in bm.edges if (field[e.verts[0]] - level) * (field[e.verts[1]] - level) < 0]
    if within is not None:
        crossing = [e for e in crossing if min(fields[within][e.verts[0]], fields[within][e.verts[1]]) >= 1.0 - 1e-6]
    for edge in crossing:
        first, second = edge.verts
        t = (level - field[first]) / (field[second] - field[first])
        values = {name: (f[first] + (f[second] - f[first]) * t) if name != "zone" else min(f[first], f[second])
                  for name, f in fields.items()}
        _, vertex = bmesh.utils.edge_split(edge, first, t)
        for name, f in fields.items():
            f[vertex] = values[name]
        field[vertex] = level
        made.append(vertex)
    bmesh.ops.connect_verts(bm, verts=made)


def _cut_along(bm, field, level, near=None):
    """
    Splits ``bm``'s edges where ``field`` (one value per vertex, by index) crosses ``level`` and joins the splits
    across each face, so a later cut can follow the field's level line instead of the mesh's edges. New vertices take
    the edge's interpolated position, weights and shape keys. With ``near`` (per vertex), only edges with both ends
    near are cut. Returns each vertex's field value (the new ones' ``level``).
    """
    bm.verts.ensure_lookup_table()
    value = {v: field[v.index] for v in bm.verts}
    made = []
    crossing = [e for e in bm.edges if (value[e.verts[0]] - level) * (value[e.verts[1]] - level) < 0]
    if near is not None:
        crossing = [e for e in crossing if near[e.verts[0].index] and near[e.verts[1].index]]
    for edge in crossing:
        first, second = edge.verts
        _, vertex = bmesh.utils.edge_split(edge, first, (level - value[first]) / (value[second] - value[first]))
        value[vertex] = level
        made.append(vertex)
    bmesh.ops.connect_verts(bm, verts=made)
    return value
