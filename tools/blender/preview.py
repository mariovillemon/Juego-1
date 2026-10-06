"""Renders a quick studio preview of a generated FBX (Cycles, CPU) to check models without Unity.

    python tools/blender/preview.py model.fbx out.png [yaw_deg] [pitch_deg] [samples]
"""
import math
import os
import sys

import bpy  # noqa: I001
from mathutils import Vector


def main(fbx, out, yaw=35.0, pitch=18.0, samples=32):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in meshes:
        if o.name.startswith("UCX_") or "_LOD1" in o.name or "_LOD2" in o.name:
            o.hide_render = True
    vis = [o for o in meshes if not o.hide_render]
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in vis:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    center = (lo + hi) / 2
    radius = (hi - lo).length / 2
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    sc.render.filepath = out
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.exposure = -1.5
    world = bpy.data.worlds.new("W")
    sc.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sky_type = "NISHITA"
    sky.sun_elevation = math.radians(35)
    world.node_tree.links.new(sky.outputs[0], world.node_tree.nodes["Background"].inputs[0])
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.3
    bpy.ops.mesh.primitive_plane_add(size=radius * 40, location=(center.x, center.y, lo.z))
    floor = bpy.context.active_object
    fm = bpy.data.materials.new("Floor")
    fm.use_nodes = True
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.35, 0.35, 0.36, 1)
    fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.6
    floor.data.materials.append(fm)
    for name, loc, energy, size in (("Key", (1.2, -1.0, 1.4), 900, 3), ("Fill", (-1.4, -0.4, 0.8), 300, 4), ("Rim", (0, 1.5, 1.2), 500, 3)):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy * radius * radius * 0.08
        ld.size = size * radius
        lo_ = bpy.data.objects.new(name, ld)
        sc.collection.objects.link(lo_)
        lo_.location = center + Vector(loc) * radius * 2
        lo_.rotation_euler = (center - lo_.location).to_track_quat("-Z", "Y").to_euler()
    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = 50
    cam = bpy.data.objects.new("Cam", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam
    y, p = math.radians(yaw), math.radians(pitch)
    d = radius * 2.6
    cam.location = center + Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))) * d
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    main(os.path.abspath(a[0]), os.path.abspath(a[1]), *(float(x) for x in a[2:4]), *(int(x) for x in a[4:5]))
