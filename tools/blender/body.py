"""Generic compact hatchback body (≈4.3 × 1.76 × 1.48 m) for CarAssembler: lofted rounded sections with real wheel
arches and an open 1.2 m engine bay, smoothed with a subdivision surface, plus four wheels (tyre, 5-spoke rim,
disc, hub). Same layout as CarBodyBuilder (Unity) so the engine bay and slots line up.

    python tools/blender/body.py [output.fbx]   (default Unity/Assets/Garage/Resources/CarBodies/Hatch.fbx)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import garage_blender as g  # noqa: E402
import bpy  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
L, HW, BOTTOM, BELT, ROOF, WR, BAY = 4.3, 0.88, 0.16, 0.98, 1.45, 0.31, 1.2
FRONT, REAR = L / 2, -L / 2
FIREWALL = FRONT - BAY
FRONT_AXLE = FRONT - 0.85
REAR_AXLE = FRONT_AXLE - L * 0.6


def arch(z, axle):
    r = WR + 0.06
    dz = z - axle
    return 0 if abs(dz) >= r else WR + math.sqrt(r * r - dz * dz)


def section(hw_bottom, hw_top, y0, y1, r=0.08, seg=4, x_in=None, x_out=None):
    if x_in is None:
        c = [(-hw_bottom, y0), (hw_bottom, y0), (hw_top, y1), (-hw_top, y1)]
    else:
        c = [(x_in, y0), (x_out - 0.03, y0), (x_out, y1 - 0.05), (x_in, y1)]
    pts = []
    for i in range(4):
        px, py = c[(i + 3) % 4]
        cx, cy = c[i]
        nx, ny = c[(i + 1) % 4]
        lp = math.hypot(px - cx, py - cy)
        ln = math.hypot(nx - cx, ny - cy)
        rr = min(r, lp * 0.45, ln * 0.45)
        ax, ay = cx + (px - cx) / lp * rr, cy + (py - cy) / lp * rr
        bx, by = cx + (nx - cx) / ln * rr, cy + (ny - cy) / ln * rr
        for s in range(seg + 1):
            t = s / seg
            pts.append(((1 - t) ** 2 * ax + 2 * (1 - t) * t * cx + t * t * bx, (1 - t) ** 2 * ay + 2 * (1 - t) * t * cy + t * t * by))
    return pts


def loft(name, z0, z1, step, fn, material, subdiv=1):
    n = max(2, int(math.ceil((z1 - z0) / step)) + 1)
    verts, faces = [], []
    ring = 0
    for i in range(n):
        z = z0 + (z1 - z0) * i / (n - 1)
        s = fn(z)
        ring = len(s)
        verts += [tuple(g.U(x, y, z)) for x, y in s]
    for i in range(n - 1):
        for j in range(ring):
            a, b = i * ring + j, i * ring + (j + 1) % ring
            faces.append((a, b, b + ring, a + ring))
    faces.append(tuple(range(ring - 1, -1, -1)))
    faces.append(tuple((n - 1) * ring + j for j in range(ring)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    o.data.materials.append(material)
    if subdiv:
        m = o.modifiers.new("Subdiv", "SUBSURF")
        m.levels = subdiv
        m.render_levels = subdiv
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def smooth(t):
    return t * t * (3 - 2 * t)


def build(out):
    g.reset()
    m = {
        "paint": g.mat("CarPaint", (0.55, 0.56, 0.58), 0.3, 0.25),
        "glass": g.mat("Glass", (0.08, 0.09, 0.1), 0.0, 0.05),
        "black": g.mat("BlackPlastic", (0.03, 0.03, 0.035), 0.0, 0.55),
        "rubber": g.mat("Tyre", (0.02, 0.02, 0.02), 0.0, 0.8),
        "rim": g.mat("Rim", (0.62, 0.63, 0.65), 1.0, 0.3),
        "chrome": g.mat("Chrome", (0.8, 0.8, 0.82), 1.0, 0.1),
        "lamp": g.mat("HeadLamp", (0.9, 0.92, 0.95), 0.0, 0.05),
        "tail": g.mat("TailLamp", (0.6, 0.02, 0.02), 0.0, 0.1),
        "disc": g.mat("BrakeDisc", (0.3, 0.28, 0.27), 0.9, 0.5),
    }
    parts = []

    def shell(z):
        t = min(1, max(0, (z - REAR) / 0.35))
        hw = HW - 0.06 * (1 - t * t)
        top = BELT - 0.05 * (1 - t)
        bottom = max(BOTTOM + 0.08 * (1 - t), arch(z, REAR_AXLE))
        return section(hw - 0.05, hw, bottom, top, 0.1)

    parts.append(loft("shell", REAR, FIREWALL, 0.04, shell, m["paint"]))
    for sgn in (-1, 1):
        def fender(z, sgn=sgn):
            t = min(1, max(0, (z - (FRONT - 0.5)) / 0.32))
            top = BELT - 0.06 * t * t
            bottom = max(BOTTOM, arch(z, FRONT_AXLE))
            s = section(0, 0, bottom, top, 0.06, x_in=HW - 0.2, x_out=HW - 0.03 * t)
            return s if sgn > 0 else [(-x, y) for x, y in reversed(s)]
        parts.append(loft(f"fender{sgn}", FIREWALL, FRONT - 0.18, 0.04, fender, m["paint"]))

    def nose(z):
        t = (z - (FRONT - 0.2)) / 0.2
        hw = HW - 0.03 - 0.12 * t * t
        return section(hw - 0.04, hw, BOTTOM + 0.06 + 0.06 * t, BELT - 0.22 - 0.05 * t, 0.08)

    parts.append(loft("nose", FRONT - 0.2, FRONT, 0.025, nose, m["paint"]))

    roof_front, roof_rear, rear_glass = FIREWALL - 0.8, REAR + 0.6, REAR + 0.18

    def roof_top(z):
        if z > roof_front:
            return ROOF + (BELT - ROOF) * smooth((z - roof_front) / (FIREWALL - roof_front))
        if z < roof_rear:
            return ROOF + (BELT - ROOF) * smooth((roof_rear - z) / (roof_rear - rear_glass))
        return ROOF

    def cabin(z):
        top = max(BELT + 0.01, roof_top(z))
        k = (top - BELT) / (ROOF - BELT)
        return section(HW - 0.07, HW - 0.07 - 0.17 * k, BELT - 0.01, top, 0.05)

    parts.append(loft("cabin", rear_glass, FIREWALL, 0.04, cabin, m["glass"]))

    def roof(z):
        top = roof_top(z) + 0.015
        return section(HW - 0.23, HW - 0.25, top - 0.03, top, 0.015)

    parts.append(loft("roof", roof_rear - 0.05, roof_front + 0.05, 0.05, roof, m["paint"]))
    for sgn in (-1, 1):
        parts.append(g.box(f"lamp{sgn}", (sgn * (HW - 0.2), BELT - 0.13, FRONT - 0.06), (0.3, 0.09, 0.12), m["lamp"], 0.02))
        parts.append(g.box(f"tail{sgn}", (sgn * (HW - 0.15), BELT - 0.1, REAR + 0.04), (0.28, 0.08, 0.06), m["tail"], 0.015))
        parts.append(g.box(f"mirror{sgn}", (sgn * (HW + 0.06), BELT + 0.08, FIREWALL - 0.15), (0.12, 0.08, 0.06), m["paint"], 0.02))
        parts.append(g.box(f"handle{sgn}", (sgn * (HW + 0.005), BELT - 0.12, -0.3), (0.02, 0.025, 0.13), m["chrome"], 0.005))
    parts.append(g.box("grille", (0, BELT - 0.32, FRONT - 0.005), (HW * 1.1, 0.12, 0.02), m["black"], 0.01))
    parts.append(g.box("underbody", (0, BOTTOM + 0.02, 0), (HW * 1.7, 0.04, L * 0.9), m["black"], 0))

    for z in (FRONT_AXLE, REAR_AXLE):
        for sgn in (-1, 1):
            x = sgn * 0.76
            parts.append(g.torus(f"tyre{sgn}{z:.1f}", (x, WR, z), WR - 0.07, 0.075, "x", m["rubber"]))
            parts.append(g.cylinder(f"rim{sgn}{z:.1f}", (x, WR, z), WR * 0.64, 0.17, "x", m["rim"], 32))
            parts.append(g.cylinder(f"disc{sgn}{z:.1f}", (x - sgn * 0.03, WR, z), WR * 0.5, 0.025, "x", m["disc"], 32))
            parts.append(g.cylinder(f"dish{sgn}{z:.1f}", (x + sgn * 0.086, WR, z), WR * 0.58, 0.01, "x", m["black"], 32))
            parts.append(g.cylinder(f"hub{sgn}{z:.1f}", (x + sgn * 0.092, WR, z), 0.06, 0.012, "x", m["chrome"], 24))

    mesh = g.join("Hatch_tmp", parts)
    root = g.make_lods("Hatch", mesh, (1.0, 0.4, 0.12))
    g.collider(root, "Hatch_0", (0, 0.55, -0.6), (HW * 2, 0.8, L - BAY))
    g.export(root, out)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    build(os.path.abspath(args[0]) if args else os.path.join(ROOT, "Unity", "Assets", "Garage", "Resources", "CarBodies", "Hatch.fbx"))
