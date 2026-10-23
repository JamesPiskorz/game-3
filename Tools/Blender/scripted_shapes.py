"""Shape helpers for the scripted character parts: signed-distance primitives (numpy) and a quad cage builder.

Used by scripted_body.py, scripted_hair.py and scripted_garment.py. Positions are Unity terms (Y up, +Z forward,
character's left at -X) until a cage is turned into a Blender mesh, as in character_tools.
"""

import math

import bmesh
import bpy
import numpy as np

import character_tools as ct

# ---------------------------------------------------------------------------------------------------- SDF primitives
# Vectorised over points of shape (n, 3). Exact or near-exact distances (after Inigo Quilez), negative inside.


def sd_sphere(p, centre, radius):
    return np.linalg.norm(p - np.asarray(centre), axis=1) - radius


def sd_ellipsoid(p, centre, radii):
    q = (p - np.asarray(centre)) / np.asarray(radii)
    k0 = np.linalg.norm(q, axis=1)
    k1 = np.linalg.norm(q / np.asarray(radii), axis=1)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)


def sd_round_cone(p, a, b, ra, rb):
    """A capsule whose radius goes from ra at a to rb at b."""
    a, b = np.asarray(a, dtype=float), np.asarray(b, dtype=float)
    ba = b - a
    l2 = ba.dot(ba)
    rr = ra - rb
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    pa = p - a
    y = pa.dot(ba)
    z = y - l2
    x = pa * l2 - np.outer(y, ba)
    x2 = (x * x).sum(axis=1)
    y2 = y * y * l2
    z2 = z * z * l2
    k = np.sign(rr) * rr * rr * x2
    out = (np.sqrt(x2 * a2 * il2) + y * rr) * il2 - ra
    first = np.sign(z) * a2 * z2 > k
    out = np.where(first, np.sqrt(x2 + z2) * il2 - rb, out)
    second = np.sign(y) * a2 * y2 < k
    out = np.where(second, np.sqrt(x2 + y2) * il2 - ra, out)
    return out


def smooth_min(a, b, k):
    h = np.maximum(k - np.abs(a - b), 0.0) / k
    return np.minimum(a, b) - h * h * k * 0.25


def smooth_max(a, b, k):
    return -smooth_min(-a, -b, k)


def smoothstep(edge0, edge1, x):
    t = np.clip((np.asarray(x, dtype=float) - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def gradient(sdf, p, eps=1e-4):
    """Normalised SDF gradient (outward surface normal) at points p."""
    g = np.zeros_like(p)
    for axis in range(3):
        offset = np.zeros(3)
        offset[axis] = eps
        g[:, axis] = sdf(p + offset) - sdf(p - offset)
    return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-12)


def ray_surface(sdf, origin, directions, near=0.005, far=0.4, steps=48):
    """Where rays from origin first cross the surface (bisection; the shape must be star-shaped about origin)."""
    origin = np.asarray(origin, dtype=float)
    lo = np.full(len(directions), near)
    hi = np.full(len(directions), far)
    for _ in range(steps):
        mid = (lo + hi) * 0.5
        inside = sdf(origin + directions * mid[:, None]) < 0.0
        lo = np.where(inside, mid, lo)
        hi = np.where(inside, hi, mid)
    return origin + directions * ((lo + hi) * 0.5)[:, None]


# ---------------------------------------------------------------------------------------------------- deterministic noise

def hash01(*values):
    """A stable pseudo-random number in [0, 1) from integers (no global RNG state, so builds repeat exactly)."""
    h = 2166136261
    for v in values:
        h = ((h ^ (int(v) & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
        h ^= h >> 13
        h = (h * 1274126177) & 0xFFFFFFFF
    return (h & 0xFFFFFF) / float(0x1000000)


# ---------------------------------------------------------------------------------------------------- quad cage

class Cage:
    """
    A quad (and rare triangle) cage with per-vertex skin weights and float tags, built ring by ring. Tags ride through
    subdivision as point attributes, so anything decided on the cage (which part a vertex belongs to, how fleshy it is,
    how far to project it) is still known on the final mesh.
    """

    def __init__(self, tag_names):
        self.tag_names = list(tag_names)
        self.points = []
        self.weights = []
        self.tags = []
        self.faces = []

    def add(self, position, weights, **tags):
        unknown = set(tags) - set(self.tag_names)
        if unknown:
            raise ValueError(f"unknown tags {unknown}")
        self.points.append(np.asarray(position, dtype=float))
        self.weights.append(dict(weights))
        self.tags.append([float(tags.get(name, 0.0)) for name in self.tag_names])
        return len(self.points) - 1

    def ring(self, positions, weights, **tags):
        return [self.add(p, weights, **tags) for p in positions]

    def face(self, *indices):
        if len(set(indices)) != len(indices):
            raise ValueError(f"degenerate face {indices}")
        self.faces.append(tuple(indices))

    def bridge(self, a, b):
        """Quads between two closed rings of the same length, a[k] facing b[k]."""
        if len(a) != len(b):
            raise ValueError("bridge needs rings of the same length")
        n = len(a)
        for k in range(n):
            self.face(a[k], a[(k + 1) % n], b[(k + 1) % n], b[k])

    def position(self, index):
        return self.points[index]

    def to_object(self, name, rig=None):
        """A Blender mesh object: weights as vertex groups, tags as float point attributes, normals made consistent."""
        bm = bmesh.new()
        verts = [bm.verts.new(ct.unity_to_blender(tuple(p))) for p in self.points]
        bm.verts.ensure_lookup_table()
        for face in self.faces:
            bm.faces.new([verts[i] for i in face])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        mesh.shade_smooth()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        for column, tag in enumerate(self.tag_names):
            attribute = mesh.attributes.new(tag, "FLOAT", "POINT")
            attribute.data.foreach_set("value", [t[column] for t in self.tags])
        for i, weights in enumerate(self.weights):
            for bone, weight in weights.items():
                if weight <= 0:
                    continue
                group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                group.add([i], weight, "ADD")
        if rig is not None:
            ct.bind_to_rig(obj, rig)
        return obj


def subdivide(obj, levels=1):
    """Catmull-Clark, vertices on the limit surface; returns a new object (weights and attributes interpolated)."""
    modifier = obj.modifiers.new("Subdivision", "SUBSURF")
    modifier.levels = levels
    modifier.render_levels = levels
    modifier.use_limit_surface = True
    modifier.uv_smooth = "PRESERVE_BOUNDARIES"
    # The armature binding (if any) must not pose the result: only the subdivision is applied.
    armature = obj.modifiers.get("Armature")
    if armature:
        armature.show_viewport = False
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
    result = bpy.data.objects.new(obj.name, mesh)
    bpy.context.scene.collection.objects.link(result)
    if len(result.vertex_groups) == 0:
        # Older versions keep group names on the object, not the mesh: copy them in order.
        for group in obj.vertex_groups:
            result.vertex_groups.new(name=group.name)
    name = obj.name
    bpy.data.objects.remove(obj)
    result.name = name
    return result


# ---------------------------------------------------------------------------------------------------- mesh access (numpy)

def unity_positions(obj):
    """Vertex positions in Unity terms, shape (n, 3)."""
    count = len(obj.data.vertices)
    co = np.empty(count * 3)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    return np.stack([-co[:, 0], co[:, 2], -co[:, 1]], axis=1)


def set_unity_positions(obj, positions):
    co = np.stack([-positions[:, 0], -positions[:, 2], positions[:, 1]], axis=1)
    obj.data.vertices.foreach_set("co", co.ravel())
    obj.data.update()


def unity_normals(obj):
    count = len(obj.data.vertices)
    n = np.empty(count * 3)
    obj.data.vertices.foreach_get("normal", n)
    n = n.reshape(-1, 3)
    return np.stack([-n[:, 0], n[:, 2], -n[:, 1]], axis=1)


def to_blender_vectors(v):
    return np.stack([-v[:, 0], -v[:, 2], v[:, 1]], axis=1)


def point_attribute(obj, name):
    attribute = obj.data.attributes[name]
    values = np.empty(len(attribute.data))
    attribute.data.foreach_get("value", values)
    return values


def set_point_attribute(obj, name, values):
    attribute = obj.data.attributes.get(name) or obj.data.attributes.new(name, "FLOAT", "POINT")
    attribute.data.foreach_set("value", np.asarray(values, dtype=float))


def weight_table(obj):
    """Skin weights as an (n, bones) array plus the bone names, from the vertex groups."""
    names = [g.name for g in obj.vertex_groups]
    table = np.zeros((len(obj.data.vertices), len(names)))
    for v in obj.data.vertices:
        for g in v.groups:
            table[v.index, g.group] = g.weight
    return table, names


def set_weight_table(obj, table, names, max_influences=ct.MAX_INFLUENCES, threshold=1e-3):
    """Keeps the strongest influences per vertex, normalises them, and rewrites the vertex groups."""
    obj.vertex_groups.clear()
    groups = {}
    order = np.argsort(-table, axis=1)[:, :max_influences]
    for i in range(table.shape[0]):
        chosen = [(j, table[i, j]) for j in order[i] if table[i, j] > threshold]
        total = sum(w for _, w in chosen) or 1.0
        for j, w in chosen:
            if j not in groups:
                groups[j] = obj.vertex_groups.new(name=names[j])
            groups[j].add([i], w / total, "REPLACE")


def vertex_neighbours(obj):
    """Adjacency as (index pairs) arrays for Laplacian smoothing."""
    count = len(obj.data.edges)
    edges = np.empty(count * 2, dtype=np.int64)
    obj.data.edges.foreach_get("vertices", edges)
    return edges.reshape(-1, 2)


def laplacian(values, edges, iterations, strength=0.5, pinned=None):
    """Smooths per-vertex values (n,) or (n, k) along mesh edges."""
    values = np.array(values, dtype=float)
    n = values.shape[0]
    degree = np.bincount(edges.ravel(), minlength=n).astype(float)
    for _ in range(iterations):
        total = np.zeros_like(values)
        np.add.at(total, edges[:, 0], values[edges[:, 1]])
        np.add.at(total, edges[:, 1], values[edges[:, 0]])
        shape = (-1,) + (1,) * (values.ndim - 1)
        average = total / np.maximum(degree, 1).reshape(shape)
        updated = values + (average - values) * strength
        if pinned is not None:
            updated[pinned] = values[pinned]
        values = updated
    return values


def set_colour_attribute(obj, colours):
    """The recolour/region colour attribute (character_tools.COLOUR_ATTRIBUTE), linear float, per vertex."""
    mesh = obj.data
    attribute = mesh.color_attributes.get(ct.COLOUR_ATTRIBUTE) or mesh.color_attributes.new(ct.COLOUR_ATTRIBUTE, "FLOAT_COLOR", "POINT")
    attribute.data.foreach_set("color", np.asarray(colours, dtype=float).ravel())
    mesh.color_attributes.active_color = attribute


def set_custom_normals(obj, normals):
    """Per-vertex custom normals (Unity terms); exported by the FBX writer as the mesh's normals."""
    blender = to_blender_vectors(np.asarray(normals))
    blender /= np.maximum(np.linalg.norm(blender, axis=1, keepdims=True), 1e-12)
    obj.data.normals_split_custom_set_from_vertices([tuple(v) for v in blender])


def add_shape_keys(obj, deltas):
    """Shape keys from per-vertex Unity-space offsets: {name: (n, 3) array}."""
    if not deltas:
        return
    if obj.data.shape_keys is None:
        obj.shape_key_add(name="Basis", from_mix=False)
    count = len(obj.data.vertices)
    base = np.empty(count * 3)
    obj.data.vertices.foreach_get("co", base)
    for name in sorted(deltas):
        key = obj.shape_key_add(name=name, from_mix=False)
        key.data.foreach_set("co", (base + to_blender_vectors(deltas[name]).ravel()))
    rest_shape_keys(obj)


def rest_shape_keys(obj):
    """Sets every shape key's value to 0 (Blender 5 adds new keys at 1, which shows every body shape at full)."""
    if obj.data.shape_keys:
        for key in obj.data.shape_keys.key_blocks:
            key.value = 0.0
