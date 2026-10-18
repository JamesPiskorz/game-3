"""Blender side of the environment prop pipeline (graphics plan G5.1).

Turns a generated mesh (a Hunyuan3D 2.1 GLB, Docs/hunyuan3d.md) into a prop the toon environment shader can draw,
scripted so it runs headless:

* placement: real height in metres, pivot at the centre of the base, the generator's front kept as the front,
* clean-up: weld, drop floating islands, decimate to a triangle budget, consistent outward normals,
* flat colours from a location palette: the generated albedo is clustered into a few colour regions and each region
  snapped to the nearest palette colour, one material per colour named ``pal_RRGGBB`` (Game/Environment/Toon takes
  one flat colour per material; Assets/_Game/Town/Editor/PropModelImporter.cs reads the colour from the name),
* a validator, then FBX export with the same Unity settings as the characters.

Coordinates: Blender is Z-up with the front at -Y (as in character_tools). Hunyuan3D's front, the side its input
image saw, already arrives at -Y, and the FBX export turns that into Unity's +Z.
"""

import json
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix
from mathutils.bvhtree import BVHTree

from character_tools import export_fbx, reset_scene, write_sidecar  # noqa: F401 (re-exported for scripts)

PALETTES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "palettes.json")
MATERIAL_PREFIX = "pal_"


# ---------------------------------------------------------------------------------------------------- colour

def load_palette(spec):
    """A location name from palettes.json ("cafe"), or comma-separated sRGB hex ("F0ECE4,6E4834"). Returns hex strings."""
    with open(PALETTES, encoding="utf-8") as f:
        named = json.load(f)
    hexes = named[spec] if spec in named else [h.strip().lstrip("#") for h in spec.split(",") if h.strip()]
    for h in hexes:
        if len(h) != 6 or any(c not in "0123456789abcdefABCDEF" for c in h):
            raise ValueError(f"'{spec}' is neither a palette in palettes.json nor a list of RRGGBB colours")
    return [h.upper() for h in hexes]


def hex_to_srgb(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_oklab(rgb):
    """OKLab: distances in it track how different two colours look, unlike RGB."""
    lms = rgb @ np.array([[0.4122214708, 0.2119034982, 0.0883024619],
                          [0.5363325363, 0.6806995451, 0.2817188376],
                          [0.0514459929, 0.1073969566, 0.6299787005]])
    lms = np.cbrt(lms)
    return lms @ np.array([[0.2104542553, 1.9779984951, 0.0259040371],
                           [0.7936177850, -2.4285922050, 0.7827717662],
                           [-0.0040720468, 0.4505937099, -0.8086757660]])


def base_colour_image(obj):
    """The image feeding the Principled BSDF's Base Color (Hunyuan3D's albedo), or None for an untextured mesh."""
    for material in obj.data.materials:
        if material is None or material.node_tree is None:
            continue
        for link in material.node_tree.links:
            if link.to_socket.name == "Base Color" and link.from_node.type == "TEX_IMAGE" and link.from_node.image:
                return link.from_node.image
    return None


def textured_colours(obj, image):
    """
    ``face_colours``, reading the UVs whichever way up makes the colours coherent across neighbouring faces. Returns
    (colours, areas, flipped).

    The Hunyuan3D wrapper's GLB export flips V twice (its own glTF conversion, then trimesh's), so its textures land
    upside down for every glTF reader, Blender included; its OBJ export is right. Sampled the right way up, adjacent
    faces mostly share a colour; the wrong way, every UV island reads another island and the surface turns to noise.
    Measuring that copes with both, and with correct GLBs from anywhere else.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    pairs = np.array([[e.link_faces[0].index, e.link_faces[1].index] for e in bm.edges if len(e.link_faces) == 2])
    bm.free()
    best = None
    for flipped in (False, True):
        colours, areas = face_colours(obj, image, flipped)
        lab = linear_to_oklab(colours)
        noise = np.linalg.norm(lab[pairs[:, 0]] - lab[pairs[:, 1]], axis=1).mean()
        if best is None or noise < best[0]:
            best = (noise, colours, areas, flipped)
    return best[1], best[2], best[3]


def face_colours(obj, image, flip_v=False):
    """Linear RGB per polygon: the median of the albedo at seven points of each triangle, area-weighted per polygon."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    count = len(mesh.loop_triangles)
    loops = np.empty(count * 3, dtype=np.int64)
    mesh.loop_triangles.foreach_get("loops", loops)
    polygon = np.empty(count, dtype=np.int64)
    mesh.loop_triangles.foreach_get("polygon_index", polygon)
    area = np.empty(count)
    mesh.loop_triangles.foreach_get("area", area)
    uv = np.empty(len(mesh.loops) * 2)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    corners = uv.reshape(-1, 2)[loops.reshape(-1, 3)]  # (triangles, 3, 2)
    if flip_v:
        corners[..., 1] = 1.0 - corners[..., 1]

    # Centre, towards each corner, and towards each edge's midpoint. The median shrugs off texture smears and seams.
    weights = np.array([[1 / 3, 1 / 3, 1 / 3], [2 / 3, 1 / 6, 1 / 6], [1 / 6, 2 / 3, 1 / 6], [1 / 6, 1 / 6, 2 / 3],
                        [0.4, 0.4, 0.2], [0.2, 0.4, 0.4], [0.4, 0.2, 0.4]])
    samples = np.einsum("kj,njc->nkc", weights, corners)
    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)  # byte images come back sRGB-encoded, bottom row first (as Blender's UVs)
    pixels = pixels.reshape(height, width, 4)[..., :3]
    x = np.clip((samples[..., 0] % 1.0) * width, 0, width - 1).astype(np.int64)
    y = np.clip((samples[..., 1] % 1.0) * height, 0, height - 1).astype(np.int64)
    triangle_colour = np.median(srgb_to_linear(pixels[y, x]), axis=1)

    polygons = len(mesh.polygons)
    colour = np.zeros((polygons, 3))
    polygon_area = np.zeros(polygons)
    np.add.at(colour, polygon, triangle_colour * area[:, None])
    np.add.at(polygon_area, polygon, area)
    return colour / np.maximum(polygon_area, 1e-12)[:, None], polygon_area


# Lightness counts half as much as hue and chroma when matching a palette colour (as CIE94 does for textiles). The
# generated albedo carries baked light and shade, and toon shading brings its own: at full weight a dark mustard
# chair matched a green of the same lightness rather than the palette's yellow.
LIGHTNESS_WEIGHT = 0.5


def colour_distance(a, b):
    """Squared OKLab distance with lightness down-weighted; broadcasts."""
    d = a - b
    return (LIGHTNESS_WEIGHT * d[..., 0]) ** 2 + d[..., 1] ** 2 + d[..., 2] ** 2


def palette_labels(colours, areas, palette, regions, seed=0):
    """
    Index into ``palette`` per polygon, using at most ``regions`` palette colours.

    The polygons' colours are clustered (k-means in OKLab, lightness down-weighted as for matching), each cluster takes
    the nearest palette colour, and only the ``regions`` largest colours by area are kept, the rest joining their
    nearest kept colour. Clustering first means the object's own colour structure decides the regions and the palette
    only decides their colours, so light and shade baked into the albedo land in one region rather than straddling two
    palette colours. It over-clusters (twice ``regions``) with seeds picked without regard to area, so a small but
    distinct part, like a chair's legs, still gets a cluster of its own.
    """
    points = linear_to_oklab(colours)
    target = linear_to_oklab(srgb_to_linear(np.array([hex_to_srgb(h) for h in palette])))
    scaled = points * np.array([LIGHTNESS_WEIGHT, 1.0, 1.0])
    weights = areas / max(areas.sum(), 1e-12)
    rng = np.random.default_rng(seed)

    centres = [scaled[rng.choice(len(scaled), p=weights)]]
    for _ in range(1, min(2 * regions, len(scaled))):  # k-means++ seeding
        distance = np.min([((scaled - c) ** 2).sum(1) for c in centres], axis=0)
        if distance.sum() <= 0:
            break
        centres.append(scaled[rng.choice(len(scaled), p=distance / distance.sum())])
    centres = np.array(centres)
    for _ in range(30):
        assignment = np.argmin(((scaled[:, None, :] - centres[None]) ** 2).sum(-1), axis=1)
        moved = centres.copy()
        for k in range(len(centres)):
            mask = assignment == k
            if weights[mask].sum() > 0:
                moved[k] = np.average(scaled[mask], axis=0, weights=weights[mask])
        if np.allclose(moved, centres, atol=1e-6):
            break
        centres = moved
    centres = centres / np.array([LIGHTNESS_WEIGHT, 1.0, 1.0])
    labels = np.argmin(colour_distance(centres[:, None, :], target[None]), axis=1)[assignment]

    used = sorted(set(labels.tolist()), key=lambda i: -areas[labels == i].sum())
    keep = used[:regions]
    for dropped in used[regions:]:
        labels[labels == dropped] = keep[int(np.argmin(colour_distance(target[keep], target[dropped])))]
    return labels


# ---------------------------------------------------------------------------------------------------- geometry

def import_glb(path):
    """Imports a GLB as one mesh object with its transforms applied. Objects already in the scene are left alone."""
    existing = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    imported = [o for o in bpy.context.scene.objects if o not in existing]
    imported_names = [o.name for o in imported]  # joining frees the joined objects: keep names, not handles
    meshes = [o for o in imported if o.type == "MESH"]
    if not meshes:
        raise ValueError(f"{path} has no meshes")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    for name in imported_names:
        if name != obj.name and name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name])
    obj.parent = None
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    obj.name = os.path.splitext(os.path.basename(path))[0]
    return obj


def place(obj, height, yaw=0.0):
    """Turns the prop by ``yaw`` degrees about up, scales it to ``height`` metres, and puts its base centre at the origin."""
    mesh = obj.data
    mesh.transform(Matrix.Rotation(math.radians(yaw), 4, "Z"))
    co = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    low, high = co.min(0), co.max(0)
    scale = height / (high[2] - low[2])
    centre = (low + high) / 2
    mesh.transform(Matrix.Diagonal((scale, scale, scale, 1.0)) @ Matrix.Translation((-centre[0], -centre[1], -low[2])))
    mesh.update()


def clean(obj, weld_distance, min_island_fraction):
    """
    Welds coincident vertices, drops islands under the area fraction, patches non-manifold spots, fixes winding.
    Returns the number of islands dropped.

    Keep ``weld_distance`` tiny (about a millionth of the size): it only has to rejoin the exact duplicates that UV
    seams split. Marching-cubes output is full of near-coincident vertices that are distinct, and welding those makes
    the fins and holes this is meant to prevent.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld_distance)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=weld_distance)

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
    total = sum(f.calc_area() for f in bm.faces)
    dropped = [i for i in islands if sum(f.calc_area() for f in i) < min_island_fraction * total]
    bmesh.ops.delete(bm, geom=[f for island in dropped for f in island], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    patch_non_manifold(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return len(dropped)


def patch_non_manifold(bm, attempts=3):
    """
    Generated meshes are closed, so any open edge or fin is a local defect (Hunyuan3D's texture remesh leaves a few):
    remove the faces around it and fill the hole.
    """
    for _ in range(attempts):
        bad = [e for e in bm.edges if not e.is_manifold]
        if not bad:
            return
        faces = list({f for e in bad for f in e.link_faces})
        bmesh.ops.delete(bm, geom=faces, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        filled = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)["faces"]
        bmesh.ops.triangulate(bm, faces=[f for f in filled if len(f.verts) > 3])


def triangle_count(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def decimate(obj, max_triangles):
    """Collapse decimation down to the budget (collapse can land a little over, so it repeats)."""
    for _ in range(4):
        triangles = triangle_count(obj)
        if triangles <= max_triangles:
            return
        modifier = obj.modifiers.new("Decimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max_triangles / triangles * 0.98
        modifier.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)


def smooth_labels(obj, labels, passes=2):
    """A few passes of an area-weighted majority vote over each face and its edge neighbours: clears the texture's speckle before decimation."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    neighbours = [[other.index for edge in face.edges for other in edge.link_faces if other is not face] for face in bm.faces]
    areas = np.array([face.calc_area() for face in bm.faces])
    bm.free()

    for _ in range(passes):
        voted = labels.copy()
        for i, around in enumerate(neighbours):
            votes = {labels[i]: areas[i]}
            for j in around:
                votes[labels[j]] = votes.get(labels[j], 0.0) + areas[j]
            voted[i] = max(votes, key=votes.get)
        labels = voted
    return labels


def transfer_labels(obj, source_mesh, source_labels):
    """Each polygon takes the label of the nearest polygon on the undecimated source."""
    bm = bmesh.new()
    bm.from_mesh(source_mesh)
    bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm)
    labels = np.empty(len(obj.data.polygons), dtype=np.int64)
    for polygon in obj.data.polygons:
        _, _, index, _ = tree.find_nearest(polygon.center)
        labels[polygon.index] = source_labels[index]
    bm.free()
    return labels


def merge_specks(obj, labels, min_region_fraction):
    """
    Colour regions under the area fraction join the neighbouring colour they share the most edge length with, so a
    seam or a noisy patch in the generated texture doesn't become a speck of another colour.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    areas = np.array([f.calc_area() for f in bm.faces])
    total = areas.sum()
    labels = labels.copy()
    for _ in range(20):
        regions, seen = [], np.zeros(len(bm.faces), dtype=bool)
        for start in bm.faces:
            if seen[start.index]:
                continue
            region, stack = [], [start]
            seen[start.index] = True
            while stack:
                face = stack.pop()
                region.append(face.index)
                for edge in face.edges:
                    for other in edge.link_faces:
                        if not seen[other.index] and labels[other.index] == labels[start.index]:
                            seen[other.index] = True
                            stack.append(other)
            regions.append(region)
        small = sorted((r for r in regions if areas[r].sum() < min_region_fraction * total), key=lambda r: areas[r].sum())
        if not small:
            break
        changed = False
        for region in small:
            members, own = set(region), labels[region[0]]
            border = {}
            for index in region:
                for edge in bm.faces[index].edges:
                    for other in edge.link_faces:
                        if other.index not in members and labels[other.index] != own:
                            border[labels[other.index]] = border.get(labels[other.index], 0.0) + edge.calc_length()
            if border:
                labels[region] = max(border, key=border.get)
                changed = True
        if not changed:
            break
    bm.free()
    return labels


def shade(obj, sharp_angle):
    """Smooth shading with edges sharper than ``sharp_angle`` degrees split, so hard corners stay hard in the toon bands."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    limit = math.radians(sharp_angle)
    for face in bm.faces:
        face.smooth = True
    for edge in bm.edges:
        edge.smooth = edge.is_manifold and edge.calc_face_angle(0.0) <= limit
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def assign_palette_materials(obj, labels, palette):
    """One material per palette colour used, named pal_RRGGBB, with the colour on the base colour for Blender previews."""
    mesh = obj.data
    mesh.materials.clear()
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])  # flat colours: the generated UVs and textures go
    used = [i for i in range(len(palette)) if np.any(labels == i)]
    slot = {}
    for i in used:
        name = MATERIAL_PREFIX + palette[i]
        material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        linear = tuple(srgb_to_linear(hex_to_srgb(palette[i]))) + (1.0,)
        material.diffuse_color = linear
        if material.node_tree is not None:
            for node in material.node_tree.nodes:
                if node.type == "BSDF_PRINCIPLED":
                    node.inputs["Base Color"].default_value = linear
        slot[i] = len(mesh.materials)
        mesh.materials.append(material)
    mesh.polygons.foreach_set("material_index", np.array([slot[i] for i in labels], dtype=np.int32))
    mesh.update()
    return [palette[i] for i in used]


# ---------------------------------------------------------------------------------------------------- preview

def render_preview(obj, path, size=(1500, 600)):
    """Workbench render in the material colours: front, three-quarter and side, left to right, with outlines."""
    scene = bpy.context.scene
    height = obj.dimensions.z
    spacing = max(obj.dimensions.x, obj.dimensions.y, height) * 1.25
    copies = []
    for i, yaw in enumerate((0.0, 45.0, 90.0)):
        copy = obj.copy()
        copy.rotation_mode = "XYZ"  # the glTF importer leaves objects in quaternion mode
        copy.rotation_euler = (0.0, 0.0, math.radians(yaw))
        copy.location = ((i - 1) * spacing, 0.0, 0.0)
        scene.collection.objects.link(copy)
        copies.append(copy)
    obj.hide_render = True

    camera = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = spacing * 3.1
    camera.location = (0.0, -10.0 * spacing, height * 0.5 + 2.0 * spacing)
    camera.rotation_euler = (math.atan2(10.0, 2.0), 0.0, 0.0)  # looking at the props' middle, a little from above
    scene.camera = camera

    scene.world = scene.world or bpy.data.worlds.new("Preview")
    scene.world.color = (0.18, 0.18, 0.2)
    scene.render.film_transparent = False
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.filepath = path
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_object_outline = True
    shading.show_cavity = False
    bpy.ops.render.render(write_still=True)

    for copy in copies:
        bpy.data.objects.remove(copy)
    bpy.data.objects.remove(camera)
    obj.hide_render = False


# ---------------------------------------------------------------------------------------------------- validation

def validate_prop(obj, palette, height, max_triangles):
    """Problems that would make the prop wrong in Unity. Empty list = OK to export."""
    problems = []
    mesh = obj.data
    triangles = triangle_count(obj)
    if triangles > max_triangles:
        problems.append(f"{obj.name}: {triangles} triangles is over the {max_triangles} budget")

    co = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    low, high = co.min(0), co.max(0)
    if abs((high[2] - low[2]) - height) > 0.01 * height:
        problems.append(f"{obj.name}: {high[2] - low[2]:.3f} m tall, asked for {height:.3f} m")
    if abs(low[2]) > 1e-4 or max(abs(low[0] + high[0]), abs(low[1] + high[1])) > 0.01 * height:
        problems.append(f"{obj.name}: the pivot isn't at the centre of the base")

    bm = bmesh.new()
    bm.from_mesh(mesh)
    open_edges = sum(1 for e in bm.edges if not e.is_manifold)
    if open_edges:
        problems.append(f"{obj.name}: {open_edges} non-manifold edges (holes or fins)")
    volume = bm.calc_volume(signed=True)
    if volume <= 0:
        problems.append(f"{obj.name}: normals point inward (signed volume {volume:.4f})")
    bm.free()

    allowed = {MATERIAL_PREFIX + h for h in palette}
    if not mesh.materials:
        problems.append(f"{obj.name}: no materials")
    for material in mesh.materials:
        if material is None or material.name not in allowed:
            problems.append(f"{obj.name}: material '{material.name if material else None}' is not a palette colour")
    return problems
