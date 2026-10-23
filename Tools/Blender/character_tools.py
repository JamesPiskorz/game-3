"""Blender side of the character content pipeline (PLAN.md section 4).

Everything an artist's part needs before it reaches Unity, scripted so it runs headless:

* the shared rig (same bone names and rest pose for the body and every part),
* weight and body shape-key transfer from the body onto a part (nearest surface point, barycentric blend),
* a validator that blocks exports Unity would turn into clipping or broken skinning,
* FBX export with the settings Unity expects.

Coordinates: Unity is Y-up, +Z forward, character's left at -X. Blender is Z-up, -Y forward, character's left
at +X. Positions are written in Unity terms and converted with ``unity_to_blender``.
"""

import json
import math
import os

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# Mirrors Assets/_Game/Characters/Editor/StandIns/StandInSkeleton.cs (body bones).
SKELETON = [
    ("Hips", None, (0.0, 0.95, 0.0)),
    ("Spine", "Hips", (0.0, 1.07, 0.0)),
    ("Chest", "Spine", (0.0, 1.24, 0.0)),
    ("Neck", "Chest", (0.0, 1.44, 0.0)),
    ("Head", "Neck", (0.0, 1.54, 0.0)),
    ("UpperArm.L", "Chest", (-0.16, 1.4, 0.0)),
    ("LowerArm.L", "UpperArm.L", (-0.44, 1.4, 0.0)),
    ("Hand.L", "LowerArm.L", (-0.69, 1.4, 0.0)),
    ("UpperArm.R", "Chest", (0.16, 1.4, 0.0)),
    ("LowerArm.R", "UpperArm.R", (0.44, 1.4, 0.0)),
    ("Hand.R", "LowerArm.R", (0.69, 1.4, 0.0)),
    ("UpperLeg.L", "Hips", (-0.09, 0.9, 0.0)),
    ("LowerLeg.L", "UpperLeg.L", (-0.09, 0.5, 0.0)),
    ("Foot.L", "LowerLeg.L", (-0.09, 0.09, 0.0)),
    ("UpperLeg.R", "Hips", (0.09, 0.9, 0.0)),
    ("LowerLeg.R", "UpperLeg.R", (0.09, 0.5, 0.0)),
    ("Foot.R", "LowerLeg.R", (0.09, 0.09, 0.0)),
]
POSITIONS = {name: position for name, _, position in SKELETON}
# Bones with several children point at this one; leaf bones point along a fixed direction (Unity axes).
PRIMARY_CHILD = {"Hips": "Spine", "Chest": "Neck"}
LEAF_DIRECTION = {"Head": (0, 0.2, 0), "Hand.L": (-0.12, 0, 0), "Hand.R": (0.12, 0, 0), "Foot.L": (0, 0, 0.14), "Foot.R": (0, 0, 0.14)}

# Mirrors Game.Characters.Modular.BodyRegions (bit index = region ID).
REGIONS = {
    "Head": 0, "Neck": 1, "UpperTorso": 2, "LowerTorso": 3, "Pelvis": 4,
    "UpperArmL": 5, "UpperArmR": 6, "ForearmL": 7, "ForearmR": 8, "HandL": 9, "HandR": 10,
    "ThighL": 11, "ThighR": 12, "CalfL": 13, "CalfR": 14, "FootL": 15, "FootR": 16,
}

BODY_SHAPES = ["body.weight"]
COLOUR_ATTRIBUTE = "Col"  # R = region ID / 255, GBA = recolour regions 1-3 (Toon shader vertex-colour mask)
MAX_INFLUENCES = 4


def unity_to_blender(v):
    x, y, z = v
    return Vector((-x, -z, y))


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ---------------------------------------------------------------------------------------------------- rig

def build_armature(name="Armature", skeleton=None):
    skeleton = skeleton or SKELETON
    positions = {bone_name: position for bone_name, _, position in skeleton}
    data = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    bones = {}
    for bone_name, parent, position in skeleton:
        bone = data.edit_bones.new(bone_name)
        bone.head = unity_to_blender(position)
        children = [child for child, child_parent, _ in skeleton if child_parent == bone_name]
        if bone_name in PRIMARY_CHILD:
            bone.tail = unity_to_blender(positions[PRIMARY_CHILD[bone_name]])
        elif len(children) == 1:
            bone.tail = unity_to_blender(positions[children[0]])
        else:  # leaves; face bones point forward
            bone.tail = bone.head + unity_to_blender(LEAF_DIRECTION.get(bone_name, (0, 0, 0.02)))
        bone.roll = 0.0
        if parent:
            bone.parent = bones[parent]
        bones[bone_name] = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def bind_to_rig(obj, rig):
    obj.parent = rig
    modifier = obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig


# ---------------------------------------------------------------------------------------------------- meshes

class MeshRecorder:
    """
    Builds a mesh with per-vertex skin weights, region IDs and shape-key deltas.

    Each vertex stores the index of its record in an int layer. BMesh iteration order is NOT creation order:
    operators like create_uvsphere free temporary vertices, and later vertices reuse those slots. Lists indexed by
    creation order silently put the head's weights on the arms.
    """

    def __init__(self):
        self.bm = bmesh.new()
        self.record = self.bm.verts.layers.int.new("record")
        self.weights = []   # per record: {bone: weight}
        self.colours = []   # per record: (r, g, b, a)
        self.deltas = []    # per record: {shape: Vector}

    def vertex(self, unity_position, weights, region=None, mask=(0.0, 0.0, 0.0), deltas=None):
        v = self.bm.verts.new(unity_to_blender(unity_position))
        v[self.record] = len(self.weights)
        self.weights.append(weights)
        region_value = 0.0 if region is None else REGIONS[region] / 255.0
        self.colours.append((region_value, mask[0], mask[1], mask[2]))
        self.deltas.append(deltas or {})
        return v

    def tube(self, start, end, r0, r1, bone, region, parent=None, child=None, sides=16, rings=8, mask=(0.0, 0.0, 0.0), offset=0.0,
             shapes=True, width=1.0, depth=1.0, caps=True):
        a, b = Vector(start), Vector(end)
        axis = (b - a).normalized()
        reference = Vector((0, 1, 0)) if abs(axis.dot(Vector((0, 0, 1)))) > 0.9 else Vector((0, 0, 1))
        u = axis.cross(reference).normalized()
        w = u.cross(axis)
        grid = []
        for ring in range(rings + 1):
            t = ring / rings
            centre = a.lerp(b, t)
            radius = r0 + (r1 - r0) * t
            row = []
            for side in range(sides):
                angle = 2 * math.pi * side / sides
                spoke = u * (math.cos(angle) * width) + w * (math.sin(angle) * depth)
                normal = (u * (math.cos(angle) / width) + w * (math.sin(angle) / depth)).normalized()
                position = centre + spoke * radius + normal * offset
                deltas = {"body.weight": normal * (radius * 0.22)} if shapes else {}
                row.append(self.vertex(tuple(position), tube_weights(bone, parent, child, t), region, mask, deltas))
            grid.append(row)
        for ring in range(rings):
            for side in range(sides):
                n = (side + 1) % sides
                self.bm.faces.new((grid[ring][side], grid[ring][n], grid[ring + 1][n], grid[ring + 1][side]))
        if caps:
            self.bm.faces.new(list(reversed(grid[0])))
            self.bm.faces.new(grid[-1])

    def sphere(self, centre, radius, bone, region, mask=(0.0, 0.0, 0.0)):
        result = bmesh.ops.create_uvsphere(self.bm, u_segments=20, v_segments=12, radius=radius)
        for v in result["verts"]:
            v.co += unity_to_blender(centre)
            v[self.record] = len(self.weights)
            self.weights.append({bone: 1.0})
            self.colours.append((REGIONS[region] / 255.0, mask[0], mask[1], mask[2]))
            self.deltas.append({})

    def finish(self, name, rig):
        # Put the records in final vertex order, then drop the bookkeeping layer.
        self.bm.verts.ensure_lookup_table()
        order = [v[self.record] for v in self.bm.verts]
        self.weights = [self.weights[r] for r in order]
        self.colours = [self.colours[r] for r in order]
        self.deltas = [self.deltas[r] for r in order]
        self.bm.verts.layers.int.remove(self.record)

        mesh = bpy.data.meshes.new(name)
        self.bm.verts.index_update()
        self.bm.normal_update()
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.to_mesh(mesh)
        self.bm.free()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)

        attribute = mesh.color_attributes.new(COLOUR_ATTRIBUTE, "FLOAT_COLOR", "POINT")
        for i, colour in enumerate(self.colours):
            attribute.data[i].color = colour
        mesh.color_attributes.active_color = attribute

        for i, weights in enumerate(self.weights):
            for bone, weight in weights.items():
                if weight <= 0:
                    continue
                group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                group.add([i], weight, "REPLACE")

        shape_names = sorted({name for deltas in self.deltas for name in deltas})
        if shape_names:
            obj.shape_key_add(name="Basis", from_mix=False)
            for shape in shape_names:
                key = obj.shape_key_add(name=shape, from_mix=False)
                for i, deltas in enumerate(self.deltas):
                    if shape in deltas:
                        key.data[i].co = mesh.vertices[i].co + unity_to_blender(tuple(deltas[shape])) - unity_to_blender((0, 0, 0))
        bind_to_rig(obj, rig)
        return obj


def tube_weights(bone, parent, child, t):
    weights = {bone: 1.0}
    if parent and t < 0.2:
        share = 0.5 * (1 - t / 0.2)
        weights[parent] = share
        weights[bone] -= share
    if child and t > 0.8:
        share = 0.5 * (t - 0.8) / 0.2
        weights[child] = share
        weights[bone] -= share
    return weights


def build_body(rig):
    """A simple test body: capsule torso and limbs, sphere head, region IDs, one body shape key."""
    m = MeshRecorder()
    m.tube((0, 0.84, 0), (0, 1.07, 0), 0.15, 0.13, "Hips", "Pelvis", child="Spine", width=1.22, depth=0.78)
    m.tube((0, 1.07, 0), (0, 1.24, 0), 0.13, 0.145, "Spine", "LowerTorso", parent="Hips", child="Chest", width=1.22, depth=0.75)
    m.tube((0, 1.24, 0), (0, 1.45, 0), 0.145, 0.13, "Chest", "UpperTorso", parent="Spine", child="Neck", width=1.22, depth=0.75)
    m.tube((0, 1.42, 0), (0, 1.58, 0), 0.05, 0.045, "Neck", "Neck", parent="Chest", child="Head", shapes=False)
    m.sphere((0, 1.64, 0), 0.11, "Head", "Head")
    for side, x in (("L", -1.0), ("R", 1.0)):
        m.tube((0.15 * x, 1.4, 0), (0.44 * x, 1.4, 0), 0.052, 0.042, "UpperArm." + side, "UpperArm" + side, parent="Chest", child="LowerArm." + side)
        m.tube((0.44 * x, 1.4, 0), (0.69 * x, 1.4, 0), 0.041, 0.032, "LowerArm." + side, "Forearm" + side, parent="UpperArm." + side, child="Hand." + side)
        m.tube((0.69 * x, 1.4, 0), (0.84 * x, 1.4, 0), 0.03, 0.022, "Hand." + side, "Hand" + side, parent="LowerArm." + side, shapes=False)
        m.tube((0.09 * x, 0.9, 0), (0.09 * x, 0.5, 0), 0.078, 0.055, "UpperLeg." + side, "Thigh" + side, parent="Hips", child="LowerLeg." + side)
        m.tube((0.09 * x, 0.5, 0), (0.09 * x, 0.08, 0), 0.054, 0.035, "LowerLeg." + side, "Calf" + side, parent="UpperLeg." + side, child="Foot." + side)
        m.tube((0.09 * x, 0.045, -0.04), (0.09 * x, 0.04, 0.16), 0.042, 0.034, "Foot." + side, "Foot" + side, parent="LowerLeg." + side, shapes=False)
    return m.finish("Body", rig)


def build_vest(rig):
    """A close-fitting vest over the torso. Built without weights or shape keys: those come from transfer."""
    m = MeshRecorder()
    m.tube((0, 1.07, 0), (0, 1.24, 0), 0.13, 0.145, "Spine", None, offset=0.012, shapes=False, width=1.22, depth=0.75, caps=False, mask=(1, 0, 0))
    m.tube((0, 1.24, 0), (0, 1.43, 0), 0.145, 0.132, "Chest", None, offset=0.012, shapes=False, width=1.22, depth=0.75, caps=False, mask=(1, 0, 0))
    vest = m.finish("Vest", rig)
    strip_skinning(vest)
    return vest


def build_scarf(rig):
    """A loose ring around the neck, also skinned by transfer."""
    m = MeshRecorder()
    major, minor, rings, sides = 0.085, 0.024, 24, 10
    grid = []
    for i in range(rings):
        theta = 2 * math.pi * i / rings
        row = []
        for j in range(sides):
            phi = 2 * math.pi * j / sides
            radius = major + minor * math.cos(phi)
            position = (radius * math.cos(theta), 1.44 + minor * math.sin(phi), radius * math.sin(theta))
            row.append(m.vertex(position, {"Neck": 1.0}, None, (1.0, 0.0, 0.0)))
        grid.append(row)
    for i in range(rings):
        for j in range(sides):
            m.bm.faces.new((grid[i][j], grid[(i + 1) % rings][j], grid[(i + 1) % rings][(j + 1) % sides], grid[i][(j + 1) % sides]))
    scarf = m.finish("Scarf", rig)
    strip_skinning(scarf)
    return scarf


def strip_skinning(obj):
    obj.vertex_groups.clear()
    if obj.data.shape_keys:
        obj.shape_key_clear()


# ---------------------------------------------------------------------------------------------------- transfer

def transfer_from_body(part, body, bones=None):
    """
    Gives every part vertex the skin weights and body shape-key offsets of the nearest point on the body surface
    (barycentric blend of that triangle's three vertices), so the part follows the body through every pose and
    body shape. This is the scripted step that keeps clothing from clipping (decision 0001).

    ``bones`` limits which bones the part may follow. Loose accessories need it: a scarf's outer edge is nearer the
    top of the upper arm than the neck, and without a mask it swings with the arms.
    """
    mesh = body.data
    mesh.calc_loop_triangles()
    basis = [v.co.copy() for v in mesh.vertices]
    triangles = [tuple(t.vertices) for t in mesh.loop_triangles]
    tree = BVHTree.FromPolygons(basis, triangles)

    group_names = {g.index: g.name for g in body.vertex_groups}
    body_weights = [{group_names[g.group]: g.weight for g in v.groups if g.weight > 0} for v in mesh.vertices]
    shape_keys = [k for k in (mesh.shape_keys.key_blocks if mesh.shape_keys else []) if k.name != "Basis"]

    samples = []
    for v in part.data.vertices:
        location, _, index, _ = tree.find_nearest(v.co)
        a, b, c = triangles[index]
        samples.append(((a, b, c), barycentric(location, basis[a], basis[b], basis[c])))

    strip_skinning(part)
    for i, (corners, bary) in enumerate(samples):
        blended = {}
        for corner, share in zip(corners, bary):
            for bone, weight in body_weights[corner].items():
                blended[bone] = blended.get(bone, 0.0) + weight * share
        if bones is not None:
            blended = {bone: weight for bone, weight in blended.items() if bone in bones} or {bones[0]: 1.0}
        strongest = sorted(blended.items(), key=lambda item: -item[1])[:MAX_INFLUENCES]
        total = sum(w for _, w in strongest) or 1.0
        for bone, weight in strongest:
            group = part.vertex_groups.get(bone) or part.vertex_groups.new(name=bone)
            group.add([i], weight / total, "REPLACE")

    if shape_keys:
        part.shape_key_add(name="Basis", from_mix=False)
        for key in shape_keys:
            target = part.shape_key_add(name=key.name, from_mix=False)
            target.value = 0.0  # added at full strength; exported, that's the default weight Unity shows it at
            for i, (corners, bary) in enumerate(samples):
                delta = Vector((0, 0, 0))
                for corner, share in zip(corners, bary):
                    delta += (key.data[corner].co - basis[corner]) * share
                target.data[i].co = part.data.vertices[i].co + delta


def barycentric(p, a, b, c):
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
    d20, d21 = v2.dot(v0), v2.dot(v1)
    denominator = d00 * d11 - d01 * d01
    if abs(denominator) < 1e-12:
        return (1.0, 0.0, 0.0)
    v = (d11 * d20 - d01 * d21) / denominator
    w = (d00 * d21 - d01 * d20) / denominator
    return (1.0 - v - w, v, w)


# ---------------------------------------------------------------------------------------------------- validation

def validate_part(part, rig, body, max_triangles=12000, max_bone_distance=None, follows_body_shapes=True):
    """
    Problems that would break the part in Unity. Empty list = OK to export. Parts that don't follow the body shapes
    (hair, hats: sidecar followsBodyShapes false) skip the shape-key check.
    """
    problems = []
    bone_names = {bone.name for bone in rig.data.bones}
    for group in part.vertex_groups:
        if group.name not in bone_names:
            problems.append(f"{part.name}: vertex group '{group.name}' is not a rig bone")
    for v in part.data.vertices:
        total = sum(g.weight for g in v.groups)
        if total < 0.99 or total > 1.01:
            problems.append(f"{part.name}: vertex {v.index} weights sum to {total:.3f}")
            break
        if len([g for g in v.groups if g.weight > 0]) > MAX_INFLUENCES:
            problems.append(f"{part.name}: vertex {v.index} has more than {MAX_INFLUENCES} influences")
            break
    reach = MAX_BONE_DISTANCE if max_bone_distance is None else max_bone_distance
    bone_heads = {bone.name: rig.matrix_world @ bone.head_local for bone in rig.data.bones}
    bone_tails = {bone.name: rig.matrix_world @ bone.tail_local for bone in rig.data.bones}
    group_names = {g.index: g.name for g in part.vertex_groups}
    for v in part.data.vertices:
        for g in v.groups:
            name = group_names[g.group]
            if g.weight > 0.25 and name in bone_heads and distance_to_segment(part.matrix_world @ v.co, bone_heads[name], bone_tails[name]) > reach:
                problems.append(f"{part.name}: vertex {v.index} follows '{name}' from over {reach} m away")
                break
        else:
            continue
        break
    body_shapes = {k.name for k in body.data.shape_keys.key_blocks if k.name != "Basis"} if body.data.shape_keys else set()
    part_shapes = {k.name for k in part.data.shape_keys.key_blocks} if part.data.shape_keys else set()
    for missing in sorted(body_shapes - part_shapes) if follows_body_shapes else []:
        problems.append(f"{part.name}: missing body shape key '{missing}' (it would clip when the shape is used)")
    part.data.calc_loop_triangles()
    if len(part.data.loop_triangles) > max_triangles:
        problems.append(f"{part.name}: {len(part.data.loop_triangles)} triangles is over the {max_triangles} budget")
    if COLOUR_ATTRIBUTE not in part.data.color_attributes:
        problems.append(f"{part.name}: no '{COLOUR_ATTRIBUTE}' colour attribute (recolour mask)")
    return problems


MAX_BONE_DISTANCE = 0.35


def distance_to_segment(p, a, b):
    ab = b - a
    t = 0.0 if ab.length_squared == 0 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (a + ab * t - p).length


# ---------------------------------------------------------------------------------------------------- export

def export_fbx(path, objects):
    """FBX for Unity: Y-up, forward -Z, scale baked in, no leaf bones, deform bones only, linear vertex colours."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        use_armature_deform_only=True,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_mesh_modifiers=False,  # keep shape keys; the armature binding is exported as skin clusters
        colors_type="LINEAR",      # region IDs are data, not colours: no sRGB conversion
        bake_anim=False,
    )


def write_sidecar(path, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
