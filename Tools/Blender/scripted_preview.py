"""Quick Blender-side previews for the scripted character: test poses and Workbench contact sheets.

These are for checking deformation and shape while building parts; the review that counts is the Unity render with the
game's toon shader (Game -> Characters -> Review -> Render Scripted Character Sheet).
"""

import math
import os

import bpy
import numpy as np
from mathutils import Matrix

import character_tools as ct

# Test poses: (bone, axis in Unity terms, degrees), applied parents first. Each turns the bone about an axis through
# its head, in the character's frame, on top of whatever its parent already did.
POSES = {
    "rest": [],
    "relaxed": [  # matches Game.Characters.Building.Poses.Relaxed: arms down, elbows soft
        ("UpperArm.L", (0, 0, 1), -72), ("UpperArm.R", (0, 0, 1), 72),
        ("LowerArm.L", (0, 1, 0), -12), ("LowerArm.R", (0, 1, 0), 12),
    ],
    "reach": [  # one arm forward and up, the other elbow folded, a knee raised, the head and chest turned
        ("Chest", (0, 1, 0), 14), ("Head", (0, 1, 0), -28), ("Head", (1, 0, 0), 10),
        ("UpperArm.L", (0, 1, 0), 80), ("UpperArm.L", (0, 0, 1), -20),
        ("UpperArm.R", (0, 0, 1), 60), ("LowerArm.R", (0, 1, 0), 125),
        ("UpperLeg.L", (1, 0, 0), -80), ("LowerLeg.L", (1, 0, 0), 105), ("Foot.L", (1, 0, 0), -20),
    ],
    "sit": [
        ("UpperArm.L", (0, 0, 1), -65), ("UpperArm.R", (0, 0, 1), 65),
        ("LowerArm.L", (0, 1, 0), -35), ("LowerArm.R", (0, 1, 0), 35),
        ("UpperLeg.L", (1, 0, 0), -90), ("UpperLeg.R", (1, 0, 0), -90),
        ("LowerLeg.L", (1, 0, 0), 90), ("LowerLeg.R", (1, 0, 0), 90),
        ("Spine", (1, 0, 0), 6),
    ],
}


def pose(rig, name):
    """Puts the rig in a named test pose (from rest)."""
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for bone_name, axis, degrees in POSES[name]:
        bone = rig.pose.bones.get(bone_name)
        if bone is None:
            continue
        head = bone.head.copy()  # armature space, posed
        rotation = Matrix.Rotation(math.radians(degrees), 4, ct.unity_to_blender(axis).normalized())
        bone.matrix = Matrix.Translation(head) @ rotation @ Matrix.Translation(-head) @ bone.matrix
        bpy.context.view_layer.update()


def deformation_report(obj, rig):
    """Smallest posed/rest triangle area ratio and the count of squashed triangles (< 25% of rest area)."""
    rest = _triangle_areas(obj, posed=False)
    posed = _triangle_areas(obj, posed=True)
    ratio = posed / np.maximum(rest, 1e-12)
    return float(ratio.min()), int((ratio < 0.25).sum()), int((ratio > 2.5).sum())


def _triangle_areas(obj, posed):
    if posed:
        depsgraph = bpy.context.evaluated_depsgraph_get()
        mesh = obj.evaluated_get(depsgraph).to_mesh()
    else:
        mesh = obj.data
    mesh.calc_loop_triangles()
    co = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    tris = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int64)
    mesh.loop_triangles.foreach_get("vertices", tris)
    tris = tris.reshape(-1, 3)
    a, b, c = co[tris[:, 0]], co[tris[:, 1]], co[tris[:, 2]]
    areas = np.linalg.norm(np.cross(b - a, c - a), axis=1) * 0.5
    if posed:
        obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
    return areas


# ---------------------------------------------------------------------------------------------------- rendering

VIEWS = {  # camera position and target in Unity terms
    "front": ((0, 0.95, 6), (0, 0.95, 0)),
    "side": ((-6, 0.95, 0), (0, 0.95, 0)),
    "back": ((0, 0.95, -6), (0, 0.95, 0)),
    "three_quarter": ((-3.6, 1.2, 4.6), (0, 0.95, 0)),
    "face": ((0, 1.64, 2.0), (0, 1.64, 0)),
    "face_side": ((-2.0, 1.64, 0.15), (0, 1.64, 0)),
    "face_three_quarter": ((-1.3, 1.68, 1.55), (0, 1.645, 0)),
    "hand": ((-0.75, 2.6, 0.0), (-0.76, 1.4, 0.0)),
    "hand_front": ((-0.76, 1.42, 2.0), (-0.76, 1.4, 0.0)),
    "hand_below": ((-0.95, 0.2, 0.5), (-0.76, 1.4, 0.0)),
    "shoulder": ((-0.2, 1.42, 3.0), (-0.2, 1.42, 0.0)),
    "upper": ((0, 1.3, 4.0), (0, 1.3, 0.0)),
    "upper_back": ((0, 1.3, -4.0), (0, 1.3, 0.0)),
    "waist_three_quarter": ((-2.4, 1.2, 2.4), (0, 1.05, 0.0)),
    "upper_side": ((-4.0, 1.3, 0.0), (0, 1.3, 0.0)),
    "head_top": ((0, 3.0, 0.01), (0, 1.64, 0.0)),
    "head_front": ((0, 1.62, 3.0), (0, 1.62, 0.0)),
    "head_side": ((-3.0, 1.62, 0.0), (0, 1.62, 0.0)),
    "head_back": ((0, 1.62, -3.0), (0, 1.62, 0.0)),
    "upper_three_quarter": ((-2.6, 1.45, 3.2), (0, 1.3, 0.0)),
    "shoulder_side": ((-3.0, 1.42, 0.0), (-0.2, 1.42, 0.0)),
    "shoulder_top": ((-0.2, 3.5, 0.001), (-0.2, 1.4, 0.0)),
}


def setup_scene(resolution=(640, 960)):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0.8, 0.78, 0.76)
    scene.display.shading.show_cavity = False
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    camera = bpy.data.objects.get("PreviewCamera")
    if camera is None:
        camera = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
        scene.collection.objects.link(camera)
    scene.camera = camera
    return camera


def render(path, view, ortho_scale=2.1, resolution=(640, 960), wireframe=False):
    camera = setup_scene(resolution)
    position, target = VIEWS[view]
    eye, at = ct.unity_to_blender(position), ct.unity_to_blender(target)
    direction = (at - eye).normalized()
    camera.location = eye
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = ortho_scale
    scene = bpy.context.scene
    scene.display.shading.show_xray = False
    scene.display.shading.type = "SOLID"
    for obj in scene.objects:
        if obj.type == "MESH":
            obj.show_wire = wireframe
            obj.show_all_edges = wireframe
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def contact_sheet(paths, out_path, columns):
    """Lays rendered PNGs out in a grid (all the same size)."""
    images = [bpy.data.images.load(p) for p in paths]
    w, h = images[0].size
    rows = (len(images) + columns - 1) // columns
    sheet = np.ones((rows * h, columns * w, 4), dtype=np.float32)
    for i, image in enumerate(images):
        pixels = np.empty(w * h * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        r, c = i // columns, i % columns
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = pixels.reshape(h, w, 4)
        bpy.data.images.remove(image)
    out = bpy.data.images.new("sheet", columns * w, rows * h, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = out_path
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)
    for p in paths:
        os.remove(p)
