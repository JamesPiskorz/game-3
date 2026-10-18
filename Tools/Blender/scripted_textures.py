"""Procedural textures for the scripted parts (art spike, decision 0009).

Each part is unwrapped, then Cycles bakes what the textures are computed from into float images: surface position and
normal, ambient occlusion (the part alone) and curvature (pointiness), plus construction data the part carries as
attributes (which part of the body, hair position along a clump, garment trims, linings and seams). numpy then
writes three textures per part:

* ``_base``: the painted detail the toon shader multiplies its region colours by (value only, sRGB). Crevices get a
  stepped darker value, the way painted cel shadows sit in creases; the face gets its eyes, lashes, brows and mouth
  shading, each line with a contrast floor; hair gets a dark line down each clump's sides; seams get a line; clothes
  get painted fold and shadow strokes; shoes get laces and a sole edge.
* ``_mask``: recolour regions 1-4 (RGBA, linear), decided by how the part was built (iris, brows and lashes, eye
  whites, lips on the body; base, tips and one highlight stroke per clump on hair; fabric, trims, lining and buttons on
  clothes; upper, accents, lining and sole on shoes).
* ``_light``: the lightmap graphics-plan step G1.1 asks for, ready for when the shader reads it (RGBA, linear):
  R shadow bias (0.5 neutral, lower falls into shade sooner) from occlusion and concavity; G specular mask (the
  hair's highlight band); B metal mask (buttons); A material type (0 skin, 32 hair, 64 cloth, 128 metal).

Positions are converted to Unity terms (Y up, +Z forward, character's left at -X) before anything is drawn.
"""

import math
import os
import struct
import zlib

import bpy
import numpy as np

import scripted_body as sb
import scripted_shapes as ss

MATERIAL = {"skin": 0, "hair": 32, "cloth": 64, "metal": 128}


# ---------------------------------------------------------------------------------------------------- unwrapping

def _edit_select(obj, select_face):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    # Face select mode, with vertex and edge flags cleared: in vertex mode, edit mode would re-derive the face
    # selection from whatever vertices an earlier step left selected.
    bpy.context.tool_settings.mesh_select_mode = (False, False, True)
    obj.data.vertices.foreach_set("select", [False] * len(obj.data.vertices))
    obj.data.edges.foreach_set("select", [False] * len(obj.data.edges))
    for polygon in obj.data.polygons:
        polygon.select = select_face(polygon)
    bpy.ops.object.mode_set(mode="EDIT")


def _object_mode():
    bpy.ops.object.mode_set(mode="OBJECT")


def uv_area_ratio(obj, faces):
    """UV area per unit of surface area (square metres) over the given faces."""
    uv = obj.data.uv_layers.active.data
    uv_area = mesh_area = 0.0
    for polygon in faces:
        loops = list(polygon.loop_indices)
        pts = [uv[i].uv for i in loops]
        uv_area += abs(sum(pts[k].x * pts[(k + 1) % len(pts)].y - pts[(k + 1) % len(pts)].x * pts[k].y for k in range(len(pts)))) * 0.5
        mesh_area += polygon.area
    return uv_area / max(mesh_area, 1e-12)


def scale_uvs(obj, faces, factor):
    uv = obj.data.uv_layers.active.data
    for polygon in faces:
        for i in polygon.loop_indices:
            uv[i].uv = uv[i].uv * factor


def unwrap(obj, groups, margin=0.004):
    """
    UVs for a part. ``groups`` is a list of (face filter, relative texel density, keep existing UVs): each group is
    unwrapped on its own (Smart UV Project, or kept), scaled to its density, then everything is packed together.
    """
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name="UVMap")
    # Decide every group's faces before any unwrapping changes what the filters see.
    members = [{p.index for p in obj.data.polygons if select_face(p)} for select_face, _, _ in groups]
    reference = None
    for indices, (_, density, keep) in zip(members, groups):
        if not indices:
            continue
        if not keep:
            _edit_select(obj, lambda p: p.index in indices)
            bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=0.0, scale_to_bounds=False)
            _object_mode()
        faces = [obj.data.polygons[i] for i in sorted(indices)]
        ratio = uv_area_ratio(obj, faces)
        if reference is None:
            reference = ratio / (density * density)
        scale_uvs(obj, faces, math.sqrt(reference * density * density / ratio))
    _edit_select(obj, lambda p: True)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=margin, shape_method="CONCAVE")
    _object_mode()


# ---------------------------------------------------------------------------------------------------- baking

def _bake_material(obj, build_emission):
    material = bpy.data.materials.new("_bake")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    links.new(emission.outputs["Emission"], output.inputs["Surface"])
    links.new(build_emission(nodes, links), emission.inputs["Color"])
    target = nodes.new("ShaderNodeTexImage")
    nodes.active = target
    obj.data.materials.clear()
    obj.data.materials.append(material)
    return material, target


def bake(obj, size, build_emission, samples=1, margin=8):
    """Bakes an emission network to a float image; returns (size, size, 4) linear floats (row 0 at the bottom)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.seed = 0
    scene.render.bake.margin = margin
    scene.render.bake.margin_type = "EXTEND"
    material, target = _bake_material(obj, build_emission)
    image = bpy.data.images.new("_bake", size, size, alpha=True, float_buffer=True)
    image.colorspace_settings.is_data = True
    target.image = image
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type="EMIT", use_clear=True)
    pixels = np.empty(size * size * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    obj.data.materials.clear()
    bpy.data.materials.remove(material)
    return pixels.reshape(size, size, 4)


def _geometry(output):
    def build(nodes, links):
        geometry = nodes.new("ShaderNodeNewGeometry")
        return geometry.outputs[output]
    return build


def _ao(distance):
    def build(nodes, links):
        ao = nodes.new("ShaderNodeAmbientOcclusion")
        ao.only_local = True
        ao.samples = 16
        ao.inputs["Distance"].default_value = distance
        ao.inputs["Color"].default_value = (1, 1, 1, 1)
        return ao.outputs["AO"]
    return build


def _attributes(names):
    """Up to three float point attributes into RGB."""
    def build(nodes, links):
        combine = nodes.new("ShaderNodeCombineXYZ")
        for axis, name in zip("XYZ", names):
            attribute = nodes.new("ShaderNodeAttribute")
            attribute.attribute_type = "GEOMETRY"
            attribute.attribute_name = name
            links.new(attribute.outputs["Fac"], combine.inputs[axis])
        return combine.outputs["Vector"]
    return build


class Bakes:
    """Everything a part's textures are computed from, per texel, in Unity terms."""

    def __init__(self, obj, size, attributes=(), ao_distance=0.06, ao_samples=48):
        position = bake(obj, size, _geometry("Position"))
        normal = bake(obj, size, _geometry("Normal"))
        self.position = np.stack([-position[..., 0], position[..., 2], -position[..., 1]], axis=-1)
        self.normal = np.stack([-normal[..., 0], normal[..., 2], -normal[..., 1]], axis=-1)
        self.ao = bake(obj, size, _ao(ao_distance), samples=ao_samples)[..., 0]
        self.curvature = bake(obj, size, _geometry("Pointiness"))[..., 0]
        self.attributes = {}
        for i in range(0, len(attributes), 3):
            names = attributes[i:i + 3]
            values = bake(obj, size, _attributes(names))
            for k, name in enumerate(names):
                self.attributes[name] = values[..., k]
        self.size = size


# ---------------------------------------------------------------------------------------------------- shared channels

def occlusion(bakes):
    """0 open, 1 deep in a crease."""
    return np.clip(1.0 - bakes.ao, 0.0, 1.0)


def concavity(bakes):
    c = bakes.curvature - np.median(bakes.curvature)
    return np.clip(-c * 6.0, 0.0, 1.0)


def painted_shadow(bakes, strength=1.0):
    """Stepped darkening in creases, like painted cel shadows: 1 open, about 0.8 in the deepest folds."""
    occ = occlusion(bakes)
    return 1.0 - strength * (0.11 * ss.smoothstep(0.2, 0.28, occ) + 0.08 * ss.smoothstep(0.4, 0.5, occ))


def shadow_bias(bakes):
    occ = occlusion(bakes)
    return np.clip(0.5 - 0.42 * ss.smoothstep(0.12, 0.5, occ) - 0.15 * concavity(bakes), 0.0, 1.0)


def coverage_aa(distance, width=0.0004):
    """Antialiased coverage of a shape from its signed distance in metres."""
    return np.clip(0.5 - distance / width, 0.0, 1.0)


def save(path, pixels):
    """
    Writes an (h, w, 3 or 4) array in [0, 1] (row 0 at the bottom, as bakes come out) as an 8-bit PNG. Written
    directly rather than through Blender's image saving, which drops the colour of fully transparent pixels: a mask
    keeps region 4 in alpha, so that would erase regions 1-3 wherever there's no region 4.
    """
    data = np.round(np.clip(pixels, 0.0, 1.0) * 255.0).astype(np.uint8)[::-1]
    h, w, channels = data.shape
    raw = b"".join(b"\x00" + data[row].tobytes() for row in range(h))
    colour_type = 6 if channels == 4 else 2

    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, colour_type, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


# ---------------------------------------------------------------------------------------------------- body

# The face, drawn in face space: (x, y) on the front of the head, metres from the head centre. Original design.
FACE = {
    "eye": dict(centre=(0.0330, -0.0085), half=(0.0200, 0.0175), tilt=4.0),   # the eye opening (eye white)
    "iris": dict(offset=(-0.0013, -0.0018), half=(0.0127, 0.0166)),
    "pupil": dict(offset=(-0.0004, 0.0004), half=(0.0052, 0.0077)),
    "lash": dict(thickness=0.0044, flick=0.0078),
    "crease": dict(lift=0.0042, thickness=0.0008),
    "brow": dict(start=(0.012, 0.0265), end=(0.051, 0.0255), arch=0.0045, thickness=0.0036, taper=0.45),
    "mouth": dict(centre=(0.0, -0.064), half_width=0.0098, smile=0.0013, thickness=0.0021),
    "nose_tick": dict(centre=(0.0, -0.0415), half=(0.0032, 0.0011)),
}
# Contrast floor: the detail value under each line, so lines stay darker than the skin round them whatever the light
# and region colours do (the detail map multiplies lit and shaded colour alike), and survive at full-body distance.
LINE_FLOOR = dict(lines=0.38, mouth=0.55, iris_rim=0.45, pupil=0.22)


def sd_ellipse(x, y, cx, cy, hx, hy):
    """Approximate signed distance to an axis-aligned ellipse (good near the boundary)."""
    qx, qy = (x - cx) / hx, (y - cy) / hy
    k = np.sqrt(qx * qx + qy * qy)
    return (k - 1.0) * min(hx, hy)


def eye_shape(x, y, side):
    """Signed distance to the eye opening: a rounded almond, flatter underneath, lifted at the outer corner."""
    e = FACE["eye"]
    cx, cy = side * e["centre"][0], e["centre"][1]
    t = math.radians(e["tilt"]) * side * -1  # outer corners up on both sides
    dx, dy = x - cx, y - cy
    rx = dx * math.cos(t) - dy * math.sin(t)
    ry = dx * math.sin(t) + dy * math.cos(t)
    hx, hy = e["half"]
    upper = sd_ellipse(rx, ry, 0, -0.002, hx, hy * 1.05)
    lower = sd_ellipse(rx, ry, 0, 0.0035, hx * 1.02, hy * 0.85)
    return np.where(ry > 0, upper, np.maximum(upper, lower)), (rx, ry)


def draw_face(fx, fy, front):
    """Mask weights and detail values for the face from face-space coordinates; front is 0..1 (faces forward)."""
    shape = fx.shape
    iris = np.zeros(shape)
    brows = np.zeros(shape)
    white = np.zeros(shape)
    lips = np.zeros(shape)
    value = np.ones(shape)
    for side in (-1, 1):
        d_eye, (rx, ry) = eye_shape(fx, fy, side)
        inside = coverage_aa(d_eye)
        e, ir, pu = FACE["eye"], FACE["iris"], FACE["pupil"]
        cx = side * e["centre"][0] + side * ir["offset"][0]
        cy = e["centre"][1] + ir["offset"][1]
        d_iris = sd_ellipse(fx, fy, cx, cy, *ir["half"])
        in_iris = coverage_aa(d_iris) * inside
        d_pupil = sd_ellipse(fx, fy, cx + side * pu["offset"][0], cy + pu["offset"][1], *pu["half"])
        in_pupil = coverage_aa(d_pupil) * in_iris
        # Iris value: darker under the lid, lighter toward the bottom, a darker rim, the pupil darkest.
        t = np.clip((fy - (cy - ir["half"][1])) / (2 * ir["half"][1]), 0, 1)
        iris_value = 1.0 - 0.45 * t ** 1.4
        rim = ss.smoothstep(-0.0028, -0.0004, d_iris)
        iris_value *= 1.0 - (1.0 - LINE_FLOOR["iris_rim"]) * rim
        iris_value = iris_value * (1 - in_pupil) + LINE_FLOOR["pupil"] * in_pupil
        # Catch-lights in the eye-white colour: upper left as you look at the face, the same side on both eyes.
        light_big = coverage_aa(sd_ellipse(fx, fy, cx + 0.0042, cy + 0.0055, 0.0029, 0.0033))
        light_small = coverage_aa(sd_ellipse(fx, fy, cx - 0.004, cy - 0.006, 0.0012, 0.0012))
        lights = np.maximum(light_big, light_small) * in_iris
        white_here = inside * (1 - in_iris) + lights
        iris_here = in_iris * (1 - lights)
        # Upper lash line along the top of the opening, thickening to a flick at the outer corner.
        hx, hy = e["half"]
        outer = np.clip(side * rx / hx, -1, 1)
        lash_thick = FACE["lash"]["thickness"] * (0.6 + 0.6 * np.clip(outer, 0, 1))
        upper_band = (d_eye > -lash_thick * 0.35) & (d_eye < lash_thick) & (ry > -hy * 0.15)
        lash = upper_band.astype(float) * coverage_aa(np.abs(d_eye - lash_thick * 0.3) - lash_thick * 0.65)
        flick = coverage_aa(sd_ellipse(rx, ry, side * (hx + FACE["lash"]["flick"] * 0.5), hy * 0.25, FACE["lash"]["flick"], 0.0012))
        lash = np.maximum(lash, flick * (side * rx > hx * 0.7))
        lower_lash = ((ry < -hy * 0.3) & (side * rx > 0)).astype(float) * coverage_aa(np.abs(d_eye) - 0.0005) * 0.7
        crease = coverage_aa(np.abs(d_eye - FACE["crease"]["lift"]) - FACE["crease"]["thickness"]) * (ry > hy * 0.45) * (side * rx > -hx * 0.5) * 0.35
        dark = np.clip(lash + lower_lash + crease, 0, 1)
        # Brow: an arched, tapering stroke.
        b = FACE["brow"]
        sx, sy = b["start"]
        ex, ey = b["end"]
        u = np.clip((side * fx - sx) / (ex - sx), 0, 1)
        line_y = sy + (ey - sy) * u + b["arch"] * np.sin(np.pi * u) * 0.9
        thick = b["thickness"] * (1.0 - b["taper"] * u)
        on_brow = ((side * fx >= sx - 0.001) & (side * fx <= ex + 0.001)).astype(float)
        brow = coverage_aa(np.abs(fy - line_y) - thick * 0.5) * on_brow
        dark = np.maximum(dark, brow)
        iris = np.maximum(iris, iris_here * (1 - dark))
        white = np.maximum(white, white_here * (1 - dark))
        brows = np.maximum(brows, dark)
        value = np.where(iris_here > 0, value * (1 - iris_here) + iris_value * iris_here, value)
        value = np.minimum(value, 1.0 - (1.0 - LINE_FLOOR["lines"]) * dark)
        # Soft shade under the upper lid on the eye white.
        value *= 1.0 - 0.2 * white_here * ss.smoothstep(-0.004, 0.0, d_eye) * (ry > 0)
    m = FACE["mouth"]
    mx = (fx - m["centre"][0]) / m["half_width"]
    curve = m["centre"][1] + m["smile"] * mx * mx
    taper = np.clip(1 - np.abs(mx) ** 4, 0, 1)
    lips = coverage_aa(np.abs(fy - curve) - m["thickness"] * 0.5 * taper) * (np.abs(mx) < 1)
    value *= 1.0 - (1.0 - LINE_FLOOR["mouth"]) * lips
    value *= 1.0 - 0.08 * coverage_aa(sd_ellipse(fx, fy, 0, m["centre"][1] - 0.0035, 0.005, 0.0016), 0.002)  # lower lip
    n = FACE["nose_tick"]
    value *= 1.0 - 0.22 * coverage_aa(sd_ellipse(fx, fy, n["centre"][0], n["centre"][1], *n["half"]))
    front = np.clip(front, 0, 1)
    return iris * front, brows * front, white * front, lips * front, 1 - (1 - value) * front


def body_textures(obj, folder, name, size=2048):
    def is_face(polygon):
        return polygon.normal.y < -0.15  # Blender -Y is the character's front
    head = ss.point_attribute(obj, "p_head")
    head_faces = lambda p: all(head[v] > 0.5 for v in p.vertices) and is_face(p)
    unwrap(obj, [(head_faces, 3.0, False), (lambda p: not head_faces(p), 1.0, False)])
    bakes = Bakes(obj, size, attributes=("p_head", "p_neck", "hand"), ao_distance=0.12)
    p, n = bakes.position, bakes.normal
    c = np.asarray(sb.HEAD["centre"])
    on_head = bakes.attributes["p_head"] > 0.5
    front = on_head * ss.smoothstep(0.25, 0.55, n[..., 2]) * (p[..., 2] - c[2] > 0.04)
    iris, brows, white, lips, face_value = draw_face(p[..., 0] - c[0], p[..., 1] - c[1], front)

    shadow = painted_shadow(bakes)
    shadow = np.where(on_head, 1.0, shadow)       # the face map (G1.4) handles the face, not baked creases
    # A painted shadow under the jaw on the neck, the way the reference paints it.
    neck = bakes.attributes["p_neck"] > 0.5
    under_jaw = neck * ss.smoothstep(1.5, 1.545, p[..., 1]) * ss.smoothstep(-0.2, 0.3, p[..., 2] - c[2] + 0.05)
    shadow = shadow * (1 - 0.14 * under_jaw)
    value = shadow * face_value
    base = np.repeat(value[..., None], 3, axis=2)
    mask = np.stack([iris, brows, white, lips], axis=-1)
    bias = shadow_bias(bakes)
    bias = np.where(on_head, 0.5, bias) - 0.3 * under_jaw
    light = np.stack([np.clip(bias, 0, 1), np.zeros_like(bias), np.zeros_like(bias), np.full_like(bias, MATERIAL["skin"] / 255.0)], axis=-1)
    write_set(folder, name, base, mask, light)


# ---------------------------------------------------------------------------------------------------- hair

# The highlight ring, in degrees above the head centre: one stroke per clump where the clump crosses the ring, widest
# along the clump's middle and pinched to nothing at its sides, with a tooth hanging down the middle (the painted
# "angel ring" of the reference, broken up by the clumps instead of drawn round the head).
ANGEL_RING = dict(elevation=44.0, above=4.0, below=4.5, tooth=8.0, tooth_width=0.45, jitter=5.0, reach=0.18)


def hair_textures(obj, folder, name, size=1024):
    # The clumps keep the UVs they were built with; the scalp cap (all its UVs at 0) gets its own.
    unwrap(obj, [(lambda p: is_cap(obj, p), 1.0, False), (lambda p: not is_cap(obj, p), 1.0, True)])
    bakes = Bakes(obj, size, attributes=("hair_v", "hair_outer", "hair_edge", "clump"), ao_distance=0.04)
    p = bakes.position
    v = bakes.attributes["hair_v"]
    outer = bakes.attributes["hair_outer"]
    edge = bakes.attributes["hair_edge"]
    clump = bakes.attributes["clump"]
    c = np.asarray(sb.HEAD["centre"])
    rel = p - c
    elevation = np.degrees(np.arctan2(rel[..., 1], np.hypot(rel[..., 0], rel[..., 2])))
    near_head = np.linalg.norm(rel, axis=-1) < ANGEL_RING["reach"]

    tips = ss.smoothstep(0.6, 0.95, v)
    r = ANGEL_RING
    across = np.clip(1.0 - edge * edge, 0.0, 1.0) ** 0.7  # 1 along the clump's middle, 0 at its sides
    d = elevation - (r["elevation"] + (clump - 0.5) * r["jitter"])
    tooth = r["tooth"] * np.clip(1.0 - edge / r["tooth_width"], 0.0, 1.0) ** 1.5
    stroke = (d < r["above"] * across) & (d > -(r["below"] * across + tooth)) & (across > 0.05)
    on_clump = v > 0.03  # the scalp cap (v = 0) gets no ring: unbroken, it reads as a pasted-on patch
    ring = stroke * near_head * on_clump * ss.smoothstep(0.35, 0.6, outer)
    # Flat colour along each clump, darker on its underside, with a painted dark line down each side (so clumps read
    # as separate shapes even where the outline doesn't reach) and a faint strand texture.
    strands = 0.5 + 0.5 * np.sin(edge * 14.0 + clump * 40.0)
    side_line = ss.smoothstep(0.9, 0.98, edge)
    value = 1.0 - 0.03 * strands * outer - 0.12 * (1 - outer) - 0.2 * side_line
    value *= painted_shadow(bakes, strength=1.0)
    value = np.where(ring > 0.5, 1.0, np.clip(value, 0, 1))
    region2 = tips * (1 - ring)
    region3 = ring
    region1 = np.clip(1 - region2 - region3, 0, 1)
    base = np.repeat(value[..., None], 3, axis=2)
    mask = np.stack([region1, region2, region3, np.zeros_like(v)], axis=-1)
    light = np.stack([shadow_bias(bakes), ring.astype(float), np.zeros_like(v), np.full_like(v, MATERIAL["hair"] / 255.0)], axis=-1)
    write_set(folder, name, base, mask, light)


def is_cap(obj, polygon):
    uv = obj.data.uv_layers["UVMap"].data
    return all(uv[i].uv.length_squared == 0.0 for i in polygon.loop_indices)


# ---------------------------------------------------------------------------------------------------- garments

def fold_stroke(u, v, a, b, half_width):
    """Coverage of a tapered stroke from a to b in a 2D chart (metres): widest in the middle, pointed at both ends."""
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    ab = b - a
    t = np.clip(((u - a[0]) * ab[0] + (v - a[1]) * ab[1]) / ab.dot(ab), 0.0, 1.0)
    d = np.hypot(u - (a[0] + t * ab[0]), v - (a[1] + t * ab[1]))
    return coverage_aa(d - half_width * np.sin(math.pi * t) ** 0.6, 0.0009)


def painted_folds(p, sleeve, folds):
    """
    Fold and shadow shapes painted into the detail map, the way the reference paints cloth: dark tapered strokes where
    cloth bunches or drags (inside the elbows, from the armpits, at the waist, along the hem), mirrored left and right
    with a small offset so the sides don't match exactly. Torso strokes are drawn in (|x|, y) on the front or back;
    sleeve strokes in (distance out along the arm, distance round it from the top toward the front). Returns 0..1.
    """
    if not folds:
        return np.zeros(p.shape[:2])
    ink = np.zeros(p.shape[:2])
    on_sleeve = (sleeve > 0.5) & (p[..., 1] > 1.2)  # arms only: trouser legs are drawn in the torso's chart
    front = p[..., 2] > 0.0
    round_arm = np.arctan2(p[..., 2] + 0.004, p[..., 1] - 1.40) * folds["arm_radius"]
    for side in (-1, 1):
        mine = (p[..., 0] * side) > 0
        shift = folds["asymmetry"] * (side > 0)
        u_torso, v_torso = np.abs(p[..., 0]), p[..., 1] + shift
        for where, strokes in (("front", folds["front"]), ("back", folds["back"])):
            facing = front if where == "front" else ~front
            for a, b, w in strokes:
                ink = np.maximum(ink, fold_stroke(u_torso, v_torso, a, b, w) * facing * mine * ~on_sleeve)
        u_arm = np.abs(p[..., 0]) + shift
        for a, b, w in folds["sleeve"]:
            ink = np.maximum(ink, fold_stroke(u_arm, round_arm, a, b, w) * mine * on_sleeve)
    return ink


def garment_textures(obj, folder, name, garment, size=1024):
    unwrap(obj, [(lambda p: True, 1.0, False)])
    bakes = Bakes(obj, size, attributes=("trim", "lining", "seam", "sleeve"), ao_distance=0.025)  # creases, not the gap between the legs
    p, n = bakes.position, bakes.normal
    trim = ss.smoothstep(0.4, 0.6, bakes.attributes["trim"])
    lining = ss.smoothstep(0.4, 0.6, bakes.attributes["lining"])
    seam = bakes.attributes["seam"]
    sleeve = bakes.attributes["sleeve"]
    torso = (sleeve < 0.5)
    placket = np.zeros_like(trim)
    buttons = np.zeros_like(trim)
    if garment["placket"] > 0:
        front = torso & (p[..., 2] > 0.0) & (n[..., 2] > 0.3)
        placket = coverage_aa(np.abs(p[..., 0]) - garment["placket"], 0.0006) * front
        for y in garment["buttons"]:
            d = np.hypot(p[..., 0], p[..., 1] - y) - 0.0058
            buttons = np.maximum(buttons, coverage_aa(d, 0.0006) * front)
    region2 = np.clip(np.maximum(trim, placket) * (1 - lining) * (1 - buttons), 0, 1)
    region3 = lining * (1 - buttons)
    region4 = buttons
    shoe_lines = np.zeros_like(trim)
    if "sole" in garment:
        # Shoes: the sole is region 4; the toe cap, a stripe down the outer side and the eyelet strips join the
        # collar in region 2; the laces are drawn on the upper.
        s = garment["sole"]
        sole_line = s["height"] + s["toe_spring"] * ss.smoothstep(s["toe_from"], s["toe_to"], p[..., 2]) ** 2
        sole = coverage_aa(p[..., 1] - sole_line, 0.0008) * (1 - lining)
        side = np.sign(p[..., 0])
        x_rel = (p[..., 0] - side * garment["laces"]["centre"]) * side  # + toward the outer side of each foot
        toe = ss.smoothstep(garment["toe_cap"] - 0.002, garment["toe_cap"] + 0.002, p[..., 2])
        st = garment["stripe"]
        outer_side = ss.smoothstep(0.2, 0.5, n[..., 0] * side)
        stripe = fold_stroke(p[..., 2], p[..., 1], st["start"], st["end"], st["half_width"]) * outer_side
        lc = garment["laces"]
        instep = ss.smoothstep(0.25, 0.5, n[..., 1]) * (p[..., 2] > lc["rows"][0] - 0.012) * (p[..., 2] < lc["rows"][-1] + 0.012)
        eyelets = coverage_aa(np.abs(np.abs(x_rel) - lc["half_width"] - 0.003) - 0.0028, 0.0006) * instep
        between = coverage_aa(np.abs(x_rel) - lc["half_width"], 0.0006) * instep
        laces = np.zeros_like(trim)
        for z in lc["rows"]:
            laces = np.maximum(laces, coverage_aa(np.abs(p[..., 2] - z) - lc["thickness"] * 0.5, 0.0005))
        laces *= between
        shoe_lines = between * (1 - laces)  # the tongue, in shadow between the laces
        shoe_lines = np.maximum(shoe_lines, 1.4 * coverage_aa(np.abs(p[..., 1] - sole_line) - 0.0012, 0.0006))  # sole edge
        accent = np.clip(np.maximum.reduce([toe, stripe, eyelets]) * (1 - sole) * (1 - laces), 0, 1)
        region2 = np.clip(accent * (1 - sole), 0, 1)  # the collar stays the upper's colour: dark, it reads as a strap
        region3 = region3 * (1 - sole)
        region4 = sole
    region1 = np.clip(1 - region2 - region3 - region4, 0, 1)
    # Seam lines (the grooves), and a stitch line just inside each placket edge.
    seam_line = ss.smoothstep(0.86, 0.97, seam / max(seam.max(), 1e-6)) * (1 - lining)
    stitch = coverage_aa(np.abs(np.abs(p[..., 0]) - (garment["placket"] - 0.0025)) - 0.0004, 0.0004) * (placket > 0)
    stitch *= (np.sin(p[..., 1] * 2 * math.pi / 0.005) > -0.2)
    value = painted_shadow(bakes) * (1 - 0.22 * seam_line) * (1 - 0.25 * stitch)
    for y in garment["buttons"]:
        d = np.hypot(p[..., 0], p[..., 1] - y)
        value *= 1 - 0.3 * coverage_aa(np.abs(d - 0.0048) - 0.0006, 0.0005) * (buttons > 0)  # button rim
        value *= 1 - 0.35 * coverage_aa(np.hypot(np.abs(p[..., 0]) - 0.0017, p[..., 1] - y) - 0.0007, 0.0004) * (buttons > 0)
    value *= 1 - 0.15 * lining
    value *= 1 - 0.22 * shoe_lines
    folds = garment.get("painted_folds")
    if folds:
        fabric = region1 * (1 - seam_line)
        value *= 1 - folds["depth"] * painted_folds(p, sleeve, folds) * fabric
        # A painted light-to-dark fall down the garment, as the reference paints cloth.
        value *= 1 - folds["fall"] * (1 - ss.smoothstep(folds["fall_from"][0], folds["fall_from"][1], p[..., 1])) * fabric
    base = np.repeat(np.clip(value, 0, 1)[..., None], 3, axis=2)
    mask = np.stack([region1, region2, region3, region4], axis=-1)
    material = np.where(buttons > 0.5, MATERIAL["metal"], MATERIAL["cloth"]) / 255.0
    light = np.stack([shadow_bias(bakes), np.zeros_like(trim), buttons, material], axis=-1)
    write_set(folder, name, base, mask, light)


def write_set(folder, name, base, mask, light):
    save(os.path.join(folder, name + "_base.png"), base)
    save(os.path.join(folder, name + "_mask.png"), mask)
    save(os.path.join(folder, name + "_light.png"), light)
