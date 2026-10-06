"""Compact hatchback body (≈4.30 × 1.76 × 1.46 m) for CarAssembler, built as one continuous surface.

The shell is lofted from real design curves (side silhouette, beltline, plan-view width, tumblehome, ground line):
every station is a cross-section with a fixed number of points, so the mesh is a clean quad grid. Materials are
assigned per face from the same curves (glass, black pillars, lamps, grille, plastic trim), the wheel arches and
panel gaps are cut with exact booleans, and the shell is solidified so it reads correctly from inside (open bay,
through the glass). Also builds the interior (dashboard, wheel, seats), detailed wheels (tyre with sidewall and
tread grooves, 5-spoke alloy, disc and caliper) and a separate hood (Hatch_Hood.fbx, pivot on the hinge).

Layout shared with CarAssembler (do not change without updating it): length 4.3, front at +z 2.15, open engine
bay 1.2 m long (firewall at z 0.95), wheel radius 0.31, front axle 0.85 m behind the front.

    python tools/blender/body.py [output_dir]   (default Unity/Assets/Garage/Resources/CarBodies)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import garage_blender as g  # noqa: E402
import bmesh  # noqa: E402
import bpy  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
L, HW, WR, BAY = 4.3, 0.88, 0.31, 1.2
FRONT, REAR = L / 2, -L / 2
FIREWALL = FRONT - BAY
FRONT_AXLE = FRONT - 0.85
REAR_AXLE = FRONT_AXLE - 2.58
TRACK = 0.755

ROOF_Y = 1.455
WS_BASE = FIREWALL - 0.02          # windshield base (z)
ROOF_FRONT = WS_BASE - 0.92        # top of the windshield
ROOF_REAR = -1.62                  # top of the hatch glass
HATCH_BASE = REAR + 0.16           # bottom of the rear glass


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


# ------------------------------------------------------------------ design curves (functions of z)

def bottom(z):
    """Ground line of the body side (sill), rising at both overhangs."""
    y = 0.19
    y += 0.12 * smooth((z - (FRONT - 0.55)) / 0.55)
    y += 0.16 * smooth(((REAR + 0.6) - z) / 0.6)
    return y


def belt(z):
    """Beltline (bottom of the side glass / hood shut line): a gentle wedge rising to the rear."""
    y = lerp(0.93, 1.00, smooth((FIREWALL - z) / (FIREWALL - REAR)))
    # Hood shut line falls towards the nose; the tail closes over the bumper.
    y -= 0.13 * smooth((z - (FIREWALL - 0.1)) / (FRONT - FIREWALL + 0.1)) ** 1.4
    y -= 0.05 * smooth(((REAR + 0.25) - z) / 0.25)
    return min(y, top(z) - 0.035)


def top(z):
    """Centreline silhouette: nose, hood, windshield, roof, hatch."""
    if z > FRONT - 0.32:                       # nose: rounds down to the grille
        t = (z - (FRONT - 0.32)) / 0.32
        return lerp(0.905, 0.80, t ** 1.8)
    if z > WS_BASE:                            # hood, slightly crowned upwards to the cowl
        t = (z - WS_BASE) / (FRONT - 0.32 - WS_BASE)
        return lerp(0.985, 0.905, t)
    if z > ROOF_FRONT:                         # windshield with a soft blend into the roof
        t = (WS_BASE - z) / (WS_BASE - ROOF_FRONT)
        return lerp(0.985, ROOF_Y, 1 - (1 - t) ** 1.25) - 0.02 * math.sin(math.pi * t)
    if z > ROOF_REAR:                          # roof: slight downward slope to the spoiler
        t = (ROOF_FRONT - z) / (ROOF_FRONT - ROOF_REAR)
        return ROOF_Y - 0.03 * t * t
    if z > HATCH_BASE:                         # hatch glass, steep
        t = (ROOF_REAR - z) / (ROOF_REAR - HATCH_BASE)
        return lerp(ROOF_Y - 0.03, 1.05, smooth(t * 1.1))
    t = (HATCH_BASE - z) / (HATCH_BASE - REAR)   # tailgate down to the bumper
    return lerp(1.05, 0.98, t)


def half_width(z):
    """Plan view: full width between the arches, rounded at the nose and tail."""
    w = HW
    w -= 0.13 * smooth((z - (FRONT - 0.55)) / 0.55) ** 2
    w -= 0.07 * smooth(((REAR + 0.35) - z) / 0.35) ** 2
    return w


def greenhouse_k(z):
    return max(0.0, min(1.0, (top(z) - belt(z)) / (ROOF_Y - belt(z))))


def section(z):
    """Half section (x >= 0) from the underbody centre to the roof centre: 12 control points."""
    b, bl, tp, hw = bottom(z), belt(z), top(z), half_width(z)
    k = greenhouse_k(z)
    h = max(0.03, tp - bl)
    gw = hw - 0.06 - 0.16 * k                   # tumblehome: roof narrower than the shoulders
    shoulder = lerp(b, bl, 0.72)
    return [
        (0.0, b - 0.02),
        (hw * 0.6, b - 0.02),
        (hw - 0.05, b + 0.005),
        (hw - 0.012, b + 0.09),
        (hw + 0.004, lerp(b, bl, 0.45)),          # door bulge
        (hw + 0.006, shoulder),                    # shoulder line (crease)
        (hw - 0.012, bl - 0.025),
        (hw - 0.04, bl + 0.005),                   # belt
        (lerp(hw - 0.045, gw, 0.18), bl + 0.12 * h),
        (gw, bl + 0.86 * h),
        (gw - 0.05 - 0.05 * k, tp - 0.004),
        (gw * 0.5, tp + 0.012 * (0.3 + k)),
        (0.0, tp + 0.016 * (0.3 + k)),
    ]


SEG = 3   # Catmull-Rom samples per section segment


def catmull(points, n):
    out = []
    p = [points[0]] + points + [points[-1]]
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for s in range(n):
            t = s / n
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[c]) + (-p0[c] + p2[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t2
                                    + (-p0[c] + 3 * p1[c] - 3 * p2[c] + p3[c]) * t3) for c in range(2)))
    out.append(points[-1])
    return out


def stations():
    """z stations, denser at the ends; plus collapsing cap stations beyond the bumpers."""
    zs = []
    z = REAR
    while z < FRONT - 1e-6:
        zs.append(z)
        end = min(z - REAR, FRONT - z)
        z += 0.012 if end < 0.25 else 0.035
    zs.append(FRONT)
    return zs


# ------------------------------------------------------------------ shell

MATS = {}


def materials():
    m = {
        "paint": g.mat("CarPaint", (0.5, 0.04, 0.03), 0.4, 0.22),
        "glass": g.mat("Glass", (0.05, 0.06, 0.065), 0.0, 0.02),
        "trim": g.mat("BlackTrim", (0.015, 0.015, 0.017), 0.0, 0.35),
        "plastic": g.mat("TexturedPlastic", (0.03, 0.03, 0.032), 0.0, 0.7),
        "lamp": g.mat("HeadLampLens", (0.85, 0.87, 0.9), 0.0, 0.02),
        "reflector": g.mat("Chrome", (0.85, 0.85, 0.87), 1.0, 0.06),
        "tail": g.mat("TailLampLens", (0.25, 0.0, 0.005), 0.0, 0.05),
        "grille": g.mat("Grille", (0.02, 0.02, 0.022), 0.2, 0.45),
        "rubber": g.mat("Tyre", (0.025, 0.025, 0.025), 0.0, 0.85),
        "rim": g.mat("Rim", (0.66, 0.67, 0.69), 1.0, 0.25),
        "disc": g.mat("BrakeDisc", (0.32, 0.3, 0.29), 0.9, 0.45),
        "caliper": g.mat("Caliper", (0.45, 0.06, 0.05), 0.3, 0.4),
        "interior": g.mat("Interior", (0.06, 0.06, 0.065), 0.0, 0.8),
        "fabric": g.mat("SeatFabric", (0.09, 0.09, 0.1), 0.0, 0.95),
        "plate": g.mat("Plate", (0.9, 0.9, 0.88), 0.0, 0.4),
        "under": g.mat("Underbody", (0.05, 0.05, 0.05), 0.0, 0.9),
        "indicator": g.mat("IndicatorLens", (0.9, 0.45, 0.05), 0.0, 0.05),
    }
    MATS.update(m)
    return m


def face_material(z, x, y, idx, n_half):
    """Material key for a face from its centre (Unity space) and its index along the half section."""
    ax = abs(x)
    b, bl, tp = bottom(z), belt(z), top(z)
    # Underbody and lower sills.
    if y < b + 0.004:
        return "under"
    # Greenhouse (above the belt, behind the windshield base).
    if y > bl + 0.01 and ROOF_REAR - 0.25 < z < WS_BASE + 0.005 or (HATCH_BASE - 0.02 < z <= ROOF_REAR and y > bl + 0.02):
        roof_edge = y > tp - 0.035
        if HATCH_BASE - 0.02 < z <= ROOF_REAR:            # rear glass and its black surround
            gw = half_width(z) - 0.06 - 0.16 * greenhouse_k(z)
            if ax < gw - 0.09 and y > bl + 0.07:
                return "glass"
            return "trim" if ax < gw - 0.04 and y > bl + 0.03 else "paint"
        if z > ROOF_FRONT - 0.02:                          # windshield (A-pillars stay body colour)
            gw = half_width(z) - 0.06 - 0.16 * greenhouse_k(z)
            return "glass" if ax < gw - 0.035 else "paint"
        if roof_edge:
            return "paint"
        side = ax > half_width(z) - 0.3
        if side:
            if -0.58 < z < -0.50 or z < -1.47:            # B-pillar and C-pillar
                return "trim" if -0.58 < z < -0.50 else "paint"
            if y > tp - 0.06:
                return "trim"                              # window frame under the roof rail
            return "glass"
        return "paint"
    # Bumpers: unpainted lower valance.
    if (z > FRONT - 0.25 or z < REAR + 0.3) and y < b + 0.07:
        return "plastic"
    # Side protection strip along the sills.
    if y < b + 0.08 and REAR_AXLE + 0.4 < z < FRONT_AXLE - 0.4:
        return "plastic"
    return "paint"


def build_shell(m):
    mesh = bpy.data.meshes.new("Shell")
    obj = bpy.data.objects.new("Shell", mesh)
    bpy.context.collection.objects.link(obj)
    order = ["paint", "glass", "trim", "plastic", "lamp", "tail", "grille", "under", "indicator"]
    for key in order:
        mesh.materials.append(m[key])
    bm = bmesh.new()
    zs = stations()
    loops = []          # (z, [(x, y)]) in Unity space, rear cap -> body -> front cap
    for z in zs:
        half = catmull(section(z), SEG)
        loops.append((z, [(-x, y) for x, y in reversed(half)] + half[1:-1]))

    def end_rings(z, ring, sign):
        """Rounded bumper face: a quarter round of radius r, then a flat face closing to the centre."""
        r = 0.07
        cx = sum(p[0] for p in ring) / len(ring)
        cy = sum(p[1] for p in ring) / len(ring)
        out = []

        def inset(d):
            res = []
            for x, y in ring:
                vx, vy = cx - x, cy - y
                ln = math.hypot(vx, vy) or 1
                d2 = min(d, ln * 0.98)
                res.append((x + vx / ln * d2, y + vy / ln * d2))
            return res

        for i in range(1, 7):
            t = math.pi / 2 * i / 6
            out.append((z + sign * r * math.sin(t), inset(r * (1 - math.cos(t)))))
        for f in (0.7, 0.4, 0.15):
            pts = [(cx + (x - cx) * f, cy + (y - cy) * f) for x, y in out[-1][1]]
            out.append((z + sign * r, pts))
        return out, (cx, cy, z + sign * r)

    rear, rear_c = end_rings(loops[0][0], loops[0][1], -1)
    front, front_c = end_rings(loops[-1][0], loops[-1][1], 1)
    loops = list(reversed(rear)) + loops + front
    rings = [[bm.verts.new(g.U(x, y, z)) for x, y in pts] for z, pts in loops]
    n = len(rings[0])

    def assign(f, z_sample):
        ctr = f.calc_center_median()
        f.material_index = order.index(face_material(z_sample, -ctr.x, ctr.z, 0, n))

    for i in range(len(rings) - 1):
        a_, b_ = rings[i], rings[i + 1]
        za, zb = loops[i][0], loops[i + 1][0]
        zc = min(FRONT - 0.005, max(REAR + 0.005, (za + zb) / 2))
        for j in range(n):
            f = bm.faces.new((a_[j], a_[(j + 1) % n], b_[(j + 1) % n], b_[j]))
            assign(f, zc)
    for ring, (cx, cy, cz), sign in ((rings[0], rear_c, -1), (rings[-1], front_c, 1)):
        c = bm.verts.new(g.U(cx, cy, cz))
        for j in range(n):
            f = bm.faces.new((ring[j], ring[(j + 1) % n], c))
            assign(f, FRONT - 0.005 if sign > 0 else REAR + 0.005)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    mesh.validate()
    return obj


def cut(obj, cutter):
    mod = obj.modifiers.new("Cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def arches_and_gaps(shell, m):
    # Wheel arches: a cylinder per side through the whole body at each axle.
    for z in (FRONT_AXLE, REAR_AXLE):
        c = g.cylinder("arch", (0, WR, z), WR + 0.075, 2.2, "x", None, 64, 0)
        cut(shell, c)
    # Panel gaps (3 mm wide, 6 mm deep) on both sides: front door, door split, rear door, fuel flap.
    gaps = [(FIREWALL - 0.03, 0.30), (-0.54, 0.25), (-1.46, 0.30)]
    for zgap, ybot in gaps:
        for sgn in (-1, 1):
            hgt = belt(zgap) - (bottom(zgap) + 0.06)
            x = sgn * (half_width(zgap) + 0.004)
            c = g.box("gap", (x, bottom(zgap) + 0.06 + hgt / 2, zgap), (0.016, hgt, 0.003), None, 0)
            cut(shell, c)


def patch(name, shell, center, u_dir, v_dir, w, h, material, offset=0.004, thickness=0.012, res=(16, 8), round_k=0.35):
    """Lamp/grille panel conforming to the body: a rounded-rectangle grid shrink-wrapped onto the shell."""
    import mathutils
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    cu, cv = res
    c = mathutils.Vector(center)
    U_, V_ = mathutils.Vector(u_dir).normalized(), mathutils.Vector(v_dir).normalized()
    grid = []
    for i in range(cu + 1):
        row = []
        for j in range(cv + 1):
            u, v = i / cu * 2 - 1, j / cv * 2 - 1
            # Squircle: pull the corners in for rounded outlines.
            k = round_k
            su = u * (1 - k * (v * v) * (u * u) ** 0.5 * 0.5)
            sv = v * (1 - k * (u * u) * (v * v) ** 0.5 * 0.5)
            p = c + U_ * (su * w / 2) + V_ * (sv * h / 2)
            row.append(bm.verts.new(g.U(p.x, p.y, p.z)))
        grid.append(row)
    for i in range(cu):
        for j in range(cv):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    me.materials.append(material)
    sw = o.modifiers.new("Wrap", "SHRINKWRAP")
    sw.target = shell
    sw.wrap_method = "NEAREST_SURFACEPOINT"
    sw.offset = offset
    so = o.modifiers.new("Solid", "SOLIDIFY")
    so.thickness = thickness
    so.offset = -1
    for p_ in me.polygons:
        p_.use_smooth = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier="Wrap")
    bpy.ops.object.modifier_apply(modifier="Solid")
    return o


def lamps_and_grille(shell, m):
    parts = []
    zf, zr = FRONT + 0.04, REAR - 0.04
    for sgn in (-1, 1):
        # Headlamp: wraps from the front face round the corner, swept back and up.
        parts.append(patch("headlamp", shell, (sgn * 0.55, 0.715, zf + 0.02), (sgn * 0.85, 0.08, -0.5), (0, 1, 0.1), 0.40, 0.10, m["lamp"], res=(20, 8)))
        parts.append(patch("indicator", shell, (sgn * (half_width(FRONT - 0.35) + 0.02), belt(FRONT - 0.35) - 0.1, FRONT - 0.35), (0, 0, 1), (0, 1, 0), 0.07, 0.025, m["indicator"], res=(6, 3)))
        # Tail lamp: on the rear corner, wrapping onto the side.
        parts.append(patch("taillamp", shell, (sgn * 0.56, 0.9, zr - 0.04), (1, 0, 0), (0, 1, 0), 0.4, 0.13, m["tail"], res=(18, 8)))
        parts.append(patch("taillamp_side", shell, (sgn * (half_width(REAR + 0.12) + 0.05), 0.9, REAR + 0.1), (0, 0, 1), (0, 1, 0), 0.16, 0.12, m["tail"], res=(8, 6)))
        # Fog lamp recess in the bumper.
        parts.append(patch("fog", shell, (sgn * 0.6, bottom(FRONT) + 0.13, zf), (1, 0, 0), (0, 1, 0), 0.12, 0.06, m["grille"], res=(8, 4)))
    # Upper grille between the headlamps and lower air intake.
    parts.append(patch("grille", shell, (0, belt(FRONT - 0.1) - 0.13, zf + 0.04), (1, 0, 0), (0, 1, 0.2), 0.72, 0.1, m["grille"], res=(24, 6)))
    parts.append(patch("intake", shell, (0, bottom(FRONT) + 0.12, zf + 0.04), (1, 0, 0), (0, 1, 0), 0.9, 0.12, m["grille"], res=(24, 6)))
    # Rear reflector strip and diffuser.
    parts.append(patch("diffuser", shell, (0, bottom(REAR) + 0.05, zr), (1, 0, 0), (0, 1, 0), 1.2, 0.1, m["plastic"], res=(24, 4)))
    return parts


def open_bay(shell):
    """Removes the hood panel (returned as its own object) so the engine bay is open."""
    bpy.context.view_layer.objects.active = shell
    me = shell.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    hood_faces = []
    for f in bm.faces:
        c = f.calc_center_median()
        z, x, y = -c.y, -c.x, c.z
        if FIREWALL + 0.012 < z < FRONT - 0.07 and abs(x) < half_width(z) - 0.11 and y > belt(z) - 0.005 and f.normal.z > 0.35:
            hood_faces.append(f)
    hood_me = bpy.data.meshes.new("Hood")
    hb = bmesh.new()
    vmap = {}
    for f in hood_faces:
        vs = []
        for v in f.verts:
            if v not in vmap:
                vmap[v] = hb.verts.new(v.co)
            vs.append(vmap[v])
        nf = hb.faces.new(vs)
        nf.material_index = 0
        nf.smooth = True
    hb.to_mesh(hood_me)
    hb.free()
    hood_me.materials.append(me.materials[0])
    bmesh.ops.delete(bm, geom=hood_faces, context="FACES")
    bm.to_mesh(me)
    bm.free()
    hood = bpy.data.objects.new("Hood", hood_me)
    bpy.context.collection.objects.link(hood)
    return hood


def solidify(obj, thickness):
    mod = obj.modifiers.new("Solid", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = -1
    mod.use_even_offset = False
    mod.thickness_clamp = 1.0
    mod.material_offset_rim = 0
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


# ------------------------------------------------------------------ wheels

def lathe(name, profile, z_axis_center, segments, material, x_dir):
    """Revolves an (r, w) profile (w along the axle, outward positive) around the wheel axis (Unity x)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    cx, cy, cz = z_axis_center
    rings = []
    for i in range(segments):
        a = 2 * math.pi * i / segments
        rings.append([bm.verts.new(g.U(cx + x_dir * w, cy + r * math.cos(a), cz + r * math.sin(a))) for r, w in profile])
    for i in range(segments):
        ra, rb = rings[i], rings[(i + 1) % segments]
        for j in range(len(profile) - 1):
            bm.faces.new((ra[j], ra[j + 1], rb[j + 1], rb[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


def wheel(m, x, z, sgn):
    parts = []
    c = (x, WR, z)
    # Tyre 205/55 R16: section width 0.205, rim radius 0.203. Profile from inner bead round the tread to the outer bead.
    rr, tw = 0.205, 0.103
    prof = [(rr, -tw * 0.86), (rr + 0.03, -tw * 0.97), (WR - 0.04, -tw), (WR - 0.012, -tw * 0.93), (WR, -tw * 0.78)]
    grooves = [-0.06, -0.02, 0.02, 0.06]
    w = -tw * 0.78
    for gz in grooves:
        prof += [(WR, gz - 0.006), (WR - 0.008, gz - 0.005), (WR - 0.008, gz + 0.005), (WR, gz + 0.006)]
    prof += [(WR, tw * 0.78), (WR - 0.012, tw * 0.93), (WR - 0.04, tw), (rr + 0.03, tw * 0.97), (rr, tw * 0.86)]
    del w
    parts.append(lathe("tyre", prof, c, 72, m["rubber"], sgn))
    # Rim barrel and lip.
    rim = [(0.19, -0.09), (0.2, -0.085), (0.2, 0.07), (0.208, 0.08), (0.196, 0.088), (0.17, 0.075)]
    parts.append(lathe("rim", rim, c, 48, m["rim"], sgn))
    # Five spokes from the hub to the barrel, slightly dished.
    from mathutils import Matrix
    centre = g.U(*c)
    for i in range(5):
        a = 2 * math.pi * i / 5
        spoke = g.box("spoke", (x + sgn * 0.06, WR + 0.105, z), (0.03, 0.17, 0.045), m["rim"], 0.008)
        rot = Matrix.Translation(centre) @ Matrix.Rotation(a, 4, "X") @ Matrix.Translation(-centre)
        spoke.data.transform(rot @ spoke.matrix_world)
        spoke.matrix_world = Matrix.Identity(4)
        parts.append(spoke)
    parts.append(g.cylinder("hub", (x + sgn * 0.065, WR, z), 0.055, 0.03, "x", m["rim"], 32, 0.004))
    parts.append(g.cylinder("cap", (x + sgn * 0.082, WR, z), 0.03, 0.008, "x", m["trim"], 24, 0.002))
    for i in range(5):
        a = 2 * math.pi * i / 5 + math.pi / 5
        parts.append(g.cylinder("nut", (x + sgn * 0.08, WR + 0.045 * math.cos(a), z + 0.045 * math.sin(a)), 0.009, 0.012, "x", m["reflector"], 6, 0.001))
    parts.append(g.cylinder("disc", (x - sgn * 0.0, WR, z), 0.14, 0.024, "x", m["disc"], 48, 0.002))
    parts.append(g.box("caliper", (x - sgn * 0.005, WR + 0.1, z - 0.07), (0.07, 0.07, 0.11), m["caliper"], 0.012))
    return parts


# ------------------------------------------------------------------ details

def details(m):
    parts = []
    # Headlamp reflectors and projectors behind the lens faces.
    for sgn in (-1, 1):
        zc = FRONT - 0.13
        parts.append(g.sphere("projector", (sgn * (half_width(zc) - 0.18), belt(zc) - 0.09, zc - 0.02), 0.04, m["reflector"]))
        parts.append(g.box("lamp_house", (sgn * (half_width(zc) - 0.2), belt(zc) - 0.09, zc - 0.07), (0.34, 0.11, 0.08), m["reflector"], 0.02))
        # Side mirrors: housing on a small arm at the window's front corner.
        zm = WS_BASE - 0.2
        xm = sgn * (half_width(zm) + 0.08)
        parts.append(g.sphere("mirror", (xm, belt(zm) + 0.1, zm), 0.075, m["paint"], scale=(1.2, 0.85, 0.75)))
        parts.append(g.box("mirror_glass", (xm + sgn * 0.0, belt(zm) + 0.1, zm - 0.05), (0.15, 0.09, 0.005), m["reflector"], 0.01))
        parts.append(g.box("mirror_arm", (sgn * (half_width(zm) + 0.01), belt(zm) + 0.04, zm + 0.02), (0.08, 0.03, 0.06), m["trim"], 0.008))
        # Flush door handles.
        for zh in (0.35, -0.85):
            parts.append(g.box("handle", (sgn * (half_width(zh) + 0.006), belt(zh) - 0.075, zh), (0.012, 0.022, 0.15), m["paint"], 0.006))
        # Wheel-arch liners (black, hide the interior through the arches).
        for za in (FRONT_AXLE, REAR_AXLE):
            arc = []
            for i in range(17):
                a = math.pi * i / 16
                arc.append((sgn * (TRACK - 0.02), WR + (WR + 0.07) * math.sin(a) * 1.0 - 0.0, za + (WR + 0.07) * math.cos(a)))
            parts.append(g.tube("liner", arc, 0.09, m["under"]))
        # Inner engine bay aprons and strut towers (the bay is open in the game).
        parts.append(g.box("apron", (sgn * 0.70, 0.62, (FIREWALL + FRONT - 0.15) / 2), (0.03, 0.6, BAY - 0.2), m["under"], 0.01))
        parts.append(g.cylinder("strut", (sgn * 0.62, 0.8, FRONT_AXLE), 0.08, 0.2, "y", m["under"], 24, 0.01))
    # Wipers parked at the windshield base.
    for xw in (-0.45, 0.05):
        parts.append(g.box("wiper", (xw + 0.2, top(WS_BASE) + 0.025, WS_BASE - 0.06), (0.55, 0.012, 0.02), m["trim"], 0.004))
    parts.append(g.box("cowl", (0, top(WS_BASE) + 0.005, WS_BASE + 0.02), (1.3, 0.02, 0.07), m["plastic"], 0.006))
    # Number plates.
    parts.append(g.box("plate_f", (0, bottom(FRONT) + 0.2, FRONT + 0.018), (0.52, 0.11, 0.008), m["plate"], 0.004))
    parts.append(g.box("plate_r", (0, bottom(REAR) + 0.5, REAR - 0.012), (0.52, 0.11, 0.008), m["plate"], 0.004))
    # Roof spoiler, antenna, exhaust tip, rear wiper.
    parts.append(g.box("spoiler", (0, top(ROOF_REAR) - 0.005, ROOF_REAR - 0.05), (1.05, 0.03, 0.16), m["paint"], 0.012))
    parts.append(g.cylinder("antenna", (0, ROOF_Y + 0.06, -1.25), 0.008, 0.16, "y", m["trim"], 8, 0))
    parts.append(g.cylinder("exhaust", (-0.45, bottom(REAR) - 0.02, REAR + 0.02), 0.035, 0.18, "z", m["reflector"], 24, 0.002))
    parts.append(g.box("rear_wiper", (0.05, top(-1.85) + 0.01, -1.85), (0.38, 0.01, 0.015), m["trim"], 0.003))
    return parts


def interior(m):
    parts = []
    dash_z = WS_BASE - 0.32
    parts.append(g.box("floor", (0, bottom(0) + 0.05, -0.45), (1.5, 0.04, 2.6), m["interior"], 0))
    parts.append(g.box("dash", (0, 0.86, dash_z), (1.5, 0.2, 0.42), m["interior"], 0.05))
    parts.append(g.box("cluster", (-0.37, 0.98, dash_z - 0.12), (0.32, 0.09, 0.08), m["interior"], 0.03))
    parts.append(g.box("console", (0, 0.6, dash_z - 0.1), (0.24, 0.42, 0.38), m["interior"], 0.03))
    parts.append(g.torus("steering", (-0.37, 0.93, dash_z - 0.28), 0.18, 0.016, "z", m["interior"]))
    sw = bpy.context.active_object
    sw.rotation_euler = (math.radians(-22), 0, 0)
    parts.append(g.cylinder("column", (-0.37, 0.88, dash_z - 0.15), 0.03, 0.28, "z", m["interior"], 16, 0))
    for zs, sx in ((-0.55, 0.37), (-1.35, 0.37)):
        for sgn in (-1, 1):
            if zs < -1 and sgn > 0:
                parts.append(g.box("rear_bench", (0, 0.55, zs), (1.25, 0.14, 0.5), m["fabric"], 0.05))
                parts.append(g.box("rear_back", (0, 0.88, zs - 0.27), (1.25, 0.55, 0.12), m["fabric"], 0.05))
                continue
            if zs < -1:
                continue
            parts.append(g.box("seat", (sgn * sx, 0.55, zs), (0.5, 0.13, 0.52), m["fabric"], 0.05))
            parts.append(g.box("seatback", (sgn * sx, 0.9, zs - 0.27), (0.5, 0.62, 0.12), m["fabric"], 0.05))
            parts.append(g.box("headrest", (sgn * sx, 1.27, zs - 0.3), (0.26, 0.17, 0.09), m["fabric"], 0.04))
    return parts


# ------------------------------------------------------------------ build

def build(out_dir):
    g.reset()
    m = materials()
    shell = build_shell(m)
    arches_and_gaps(shell, m)
    lamps = lamps_and_grille(shell, m)
    hood = open_bay(shell)
    solidify(shell, 0.008)
    solidify(hood, 0.012)
    parts = [shell] + lamps + details(m) + interior(m)
    for z in (FRONT_AXLE, REAR_AXLE):
        for sgn in (-1, 1):
            parts += wheel(m, sgn * TRACK, z, sgn)
    mesh = g.join("Hatch_tmp", parts)
    root = g.make_lods("Hatch", mesh, (1.0, 0.35, 0.1))
    g.collider(root, "Hatch_0", (0, 0.6, -0.55), (HW * 2, 0.85, L - BAY))
    g.collider(root, "Hatch_1", (0, 0.45, FIREWALL + BAY / 2), (HW * 2, 0.5, BAY))
    g.export(root, os.path.join(out_dir, "Hatch.fbx"))

    # Hood: own file, pivot on the hinge line at the windshield base, closed position.
    bpy.ops.object.select_all(action="DESELECT")
    for o in list(bpy.context.scene.objects):
        if o is not hood:
            bpy.data.objects.remove(o, do_unlink=True)
    pivot = g.U(0, top(FIREWALL + 0.012), FIREWALL + 0.012)
    hood.data.transform(__import__("mathutils").Matrix.Translation(-pivot))
    hroot = g.make_lods("Hatch_Hood", hood, (1.0, 0.4))
    g.export(hroot, os.path.join(out_dir, "Hatch_Hood.fbx"))


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    build(os.path.abspath(args[0]) if args else os.path.join(ROOT, "Unity", "Assets", "Garage", "Resources", "CarBodies"))
