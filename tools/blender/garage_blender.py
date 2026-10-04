"""Helpers shared by the procedural model generators (Blender 4.2+, run headless).

Conventions (see docs/ART_PIPELINE.md):
- 1 Blender unit = 1 m. Modelled with Unity axes in mind (+Y up, +Z forward) and exported with axis conversion so
  the FBX imports in Unity with the same orientation and scale (no 100x).
- Every asset is an empty parent "<Name>" with children "<Name>_LOD0/1/2" (Unity builds the LODGroup on import)
  and optional "UCX_<Name>_n" boxes, turned into convex MeshColliders by Unity's GarageModelPostprocessor.
- Materials are Principled BSDF with realistic PBR values; HDRP converts them to HDRP/Lit on import.
"""
import math
import os

import bpy  # noqa: I001  (import bpy before mathutils: the module registers it)
from mathutils import Matrix, Vector

_MATS = {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _MATS.clear()


def mat(name, color, metallic=0.0, roughness=0.5):
    if name in _MATS:
        return _MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    _MATS[name] = m
    return m


# Positions are written in Unity space (x right, y up, z forward). Blender's convention is front = -Y, Z up; with
# the exporter's (-Z forward, Y up, baked) settings an object facing Blender -Y faces Unity +Z, and the change of
# handedness maps Blender +X to Unity -X. Hence U(x, y, z) = (-x, -z, y).
def U(x, y, z):
    return Vector((-x, -z, y))


def _finish(obj, material, bevel=0.0, smooth=True):
    if material is not None:
        obj.data.materials.append(material)
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
    if smooth:
        for p in obj.data.polygons:
            p.use_smooth = True
        mod = obj.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
        mod.keep_sharp = True
    return obj


def box(name, center, size, material=None, bevel=0.004, parent=None):
    """Box at a Unity-space centre with Unity-space size (x, y, z)."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=U(*center))
    o = bpy.context.active_object
    o.name = name
    o.scale = (size[0], size[2], size[1])
    bpy.ops.object.transform_apply(scale=True)
    if parent:
        o.parent = parent
    return _finish(o, material, bevel)


def cylinder(name, center, radius, length, axis="y", material=None, verts=24, bevel=0.002, parent=None):
    """Cylinder along a Unity axis."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=length, location=U(*center))
    o = bpy.context.active_object
    o.name = name
    if axis == "x":
        o.rotation_euler = (0, math.radians(90), 0)
    elif axis == "z":
        o.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True)
    if parent:
        o.parent = parent
    return _finish(o, material, bevel)


def torus(name, center, major, minor, axis="y", material=None, parent=None):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=32, minor_segments=12, location=U(*center))
    o = bpy.context.active_object
    o.name = name
    if axis == "x":
        o.rotation_euler = (0, math.radians(90), 0)
    elif axis == "z":
        o.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True)
    if parent:
        o.parent = parent
    return _finish(o, material, 0)


def sphere(name, center, radius, material=None, parent=None, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=radius, location=U(*center))
    o = bpy.context.active_object
    o.name = name
    o.scale = (scale[0], scale[2], scale[1])
    bpy.ops.object.transform_apply(scale=True)
    if parent:
        o.parent = parent
    return _finish(o, material, 0)


def tube(name, points, radius, material=None, parent=None):
    """Hose/pipe through Unity-space points (curve with bevel, converted to mesh)."""
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = radius
    curve.bevel_resolution = 4
    curve.use_fill_caps = True
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for bp, p in zip(spline.bezier_points, points):
        bp.co = U(*p)
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    o = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.active_object
    if parent:
        o.parent = parent
    return _finish(o, material, 0)


def join(name, objects):
    """Applies modifiers and joins objects into one mesh named name."""
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        for m in list(o.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    return o


def make_lods(name, mesh_obj, ratios=(1.0, 0.45, 0.15)):
    """Creates <name> empty with <name>_LOD0..n children (decimated copies)."""
    root = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(root)
    mesh_obj.name = f"{name}_LOD0"
    mesh_obj.parent = root
    for i, r in enumerate(ratios[1:], start=1):
        c = mesh_obj.copy()
        c.data = mesh_obj.data.copy()
        c.name = f"{name}_LOD{i}"
        bpy.context.collection.objects.link(c)
        mod = c.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = r
        bpy.context.view_layer.objects.active = c
        bpy.ops.object.modifier_apply(modifier="Decimate")
        c.parent = root
    return root


def collider(root, name, center, size):
    """Simplified box collider (Unity: convex MeshCollider via the UCX_ prefix)."""
    o = box(f"UCX_{name}", center, size, None, 0)
    o.parent = root
    return o


def export(root, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for c in root.children_recursive:
        c.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        object_types={"EMPTY", "MESH"},
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        path_mode="STRIP",
    )
    tris = sum(len(c.data.polygons) for c in root.children if c.type == "MESH" and c.name.endswith("_LOD0"))
    print(f"  {os.path.basename(path)}: LOD0 {tris} caras")
