"""Procedural low/mid-poly models of the game, real scale, exported as FBX with LODs and simple colliders.

Run (Blender 4.2+ or the `bpy` Python module):
    blender --background --python tools/blender/models.py -- [output_root]
    python tools/blender/models.py [output_root]      # with `pip install bpy==4.2.0`
Default output_root: Unity/Assets/Garage/Resources (CarParts/, Engines/, Workshop/).

Part models are built in the frame of the placeholder they replace (Unity primitive orientation, centred at the
origin, same size), so CarAssembler can swap them 1:1. See docs/ART_PIPELINE.md.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import garage_blender as g  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def M():
    return {
        "alu": g.mat("CastAluminium", (0.56, 0.56, 0.57), 0.9, 0.45),
        "iron": g.mat("CastIron", (0.2, 0.2, 0.19), 0.8, 0.7),
        "black": g.mat("BlackPlastic", (0.03, 0.03, 0.035), 0.0, 0.55),
        "rubber": g.mat("Rubber", (0.02, 0.02, 0.02), 0.0, 0.8),
        "steel": g.mat("Steel", (0.55, 0.55, 0.55), 1.0, 0.35),
        "exhaust": g.mat("ExhaustSteel", (0.32, 0.25, 0.19), 0.9, 0.55),
        "copper": g.mat("Copper", (0.72, 0.42, 0.25), 1.0, 0.35),
        "red": g.mat("PaintRed", (0.45, 0.04, 0.03), 0.1, 0.4),
        "brass": g.mat("Brass", (0.75, 0.6, 0.3), 1.0, 0.3),
        "grey": g.mat("PaintGrey", (0.25, 0.26, 0.27), 0.3, 0.5),
        "wood": g.mat("Wood", (0.33, 0.22, 0.13), 0.0, 0.7),
        "blue": g.mat("PaintBlue", (0.08, 0.16, 0.38), 0.2, 0.45),
        "ceramic": g.mat("Ceramic", (0.85, 0.85, 0.82), 0.0, 0.3),
    }


def finish(name, parts, out, collider=None, lods=(1.0, 0.45, 0.15)):
    mesh = g.join(name + "_tmp", parts)
    root = g.make_lods(name, mesh, lods)
    if collider:
        g.collider(root, name, *collider)
    g.export(root, out)


# --------------------------------------------------------------------------------------------- engine

def engine(cyl, out):
    """Inline engine without the parts that are separate interactive slots (coils, plugs, injectors, sensors,
    throttle, alternator, turbo...). Same envelope and cylinder spacing as CarAssembler (CylX, BlockH/D, HeadH),
    so the slots sit on the right bosses. Intake on -z (firewall), exhaust on +z (radiator), timing end on -x."""
    m = M()
    m["hose"] = g.mat("Hose", (0.025, 0.025, 0.025), 0.0, 0.6)
    m["loom"] = g.mat("WireLoom", (0.02, 0.02, 0.02), 0.0, 0.75)
    m["yellow"] = g.mat("DipstickYellow", (0.9, 0.65, 0.05), 0.0, 0.45)
    m["blue_filter"] = g.mat("OilFilterBlue", (0.05, 0.12, 0.35), 0.4, 0.35)
    m["heatshield"] = g.mat("HeatShield", (0.62, 0.6, 0.56), 1.0, 0.5)
    ew = 0.11 * cyl + 0.16
    bh, bd, hh = 0.32, 0.34, 0.13
    bt = bh / 2
    ht = bt + hh
    cx = [0.0] if cyl == 1 else [-ew / 2 + 0.08 + (ew - 0.16) * i / (cyl - 1) for i in range(cyl)]
    p = []
    # Block: deck, bore bulges on both faces, crankcase skirt flaring out, main bearing ribs.
    p.append(g.box("deck", (0, bt - 0.09, 0), (ew, 0.18, bd - 0.04), m["iron"], 0.012))
    for x in cx:
        for side in (-1, 1):
            p.append(g.cylinder("bore", (x, bt - 0.09, side * (bd / 2 - 0.05)), 0.052, 0.17, "y", m["iron"], 28, 0.006))
    p.append(g.box("skirt", (0, -0.07, 0), (ew, 0.14, bd), m["iron"], 0.014))
    for i in range(cyl + 1):
        x = -ew / 2 + 0.03 + (ew - 0.06) * i / cyl
        for side in (-1, 1):
            p.append(g.box("web", (x, -0.07, side * (bd / 2 + 0.004)), (0.014, 0.12, 0.012), m["iron"], 0.003))
    for x in cx[:: max(1, cyl // 2)]:
        p.append(g.cylinder("freeze_plug", (x, -0.02, -bd / 2 - 0.004), 0.018, 0.01, "z", m["steel"], 20, 0.002))
    # Cylinder head with cam carriers and the valve cover (centre channel left free for the coil slots).
    p.append(g.box("head", (0, bt + hh / 2, 0), (ew, hh, bd * 0.9), m["alu"], 0.01))
    p.append(g.box("head_gasket_line", (0, bt + 0.002, 0), (ew + 0.004, 0.004, bd * 0.91), m["steel"], 0.001))
    cover_w = bd * 0.55
    for side in (-1, 1):
        p.append(g.box("cover_side", (0, ht + 0.022, 0.02 + side * (cover_w / 2 - 0.035)), (ew - 0.04, 0.045, 0.07), m["black"], 0.016))
        for i in range(int((ew - 0.08) / 0.035)):
            x = -ew / 2 + 0.05 + i * 0.035
            p.append(g.box("cover_rib", (x, ht + 0.046, 0.02 + side * (cover_w / 2 - 0.035)), (0.006, 0.006, 0.055), m["black"], 0.001))
    p.append(g.box("cover_floor", (0, ht + 0.006, 0.02), (ew - 0.05, 0.012, cover_w - 0.1), m["black"], 0.004))
    p.append(g.box("cover_flange", (0, ht + 0.003, 0.02), (ew - 0.02, 0.008, cover_w + 0.01), m["alu"], 0.003))
    for x in (-ew / 2 + 0.03, ew / 2 - 0.03):
        for z in (0.02 - cover_w / 2 + 0.01, 0.02 + cover_w / 2 - 0.01):
            p.append(g.cylinder("cover_bolt", (x, ht + 0.012, z), 0.006, 0.008, "y", m["steel"], 6, 0.001))
    p.append(g.cylinder("oilcap", (ew / 2 - 0.08, ht + 0.06, 0.02 + cover_w / 2 - 0.035), 0.024, 0.02, "y", m["black"], 24, 0.003))
    p.append(g.tube("pcv_hose", [(-ew / 2 + 0.06, ht + 0.045, 0.02 - cover_w / 2 + 0.02), (-ew / 2 + 0.04, ht + 0.03, -bd / 2 - 0.06), (-ew / 2 + 0.08, ht - 0.01, -bd / 2 - 0.12)], 0.008, m["hose"]))
    # Sump with fins and drain plug.
    p.append(g.box("oilpan", (0, -bh / 2 - 0.06, 0), (ew - 0.04, 0.12, bd * 0.85), m["alu"], 0.014))
    for i in range(int((ew - 0.08) / 0.05)):
        p.append(g.box("pan_fin", (-ew / 2 + 0.06 + i * 0.05, -bh / 2 - 0.1, 0), (0.006, 0.05, bd * 0.8), m["alu"], 0.002))
    p.append(g.cylinder("drainplug", (0.05, -bh / 2 - 0.125, 0.05), 0.01, 0.012, "y", m["steel"], 6))
    # Oil filter on the intake side, dipstick tube with yellow handle.
    p.append(g.cylinder("oil_filter", (ew / 2 - 0.1, -0.08, -bd / 2 - 0.05), 0.038, 0.09, "z", m["blue_filter"], 32, 0.006))
    p.append(g.cylinder("filter_base", (ew / 2 - 0.1, -0.08, -bd / 2 - 0.005), 0.042, 0.012, "z", m["alu"], 32, 0.002))
    p.append(g.tube("dipstick", [(-ew / 2 + 0.12, -0.12, -bd / 2 - 0.01), (-ew / 2 + 0.13, 0.08, -bd / 2 - 0.03), (-ew / 2 + 0.14, ht + 0.02, -bd / 2 - 0.05)], 0.005, m["steel"]))
    p.append(g.torus("dipstick_handle", (-ew / 2 + 0.14, ht + 0.04, -bd / 2 - 0.05), 0.016, 0.005, "z", m["yellow"]))
    # Gearbox: bell housing with bolt bosses and a ribbed case.
    p.append(g.cylinder("bell", (ew / 2 + 0.07, -0.05, -0.02), 0.17, 0.14, "x", m["alu"], 40, 0.01))
    for i in range(8):
        a = 2 * math.pi * i / 8
        p.append(g.cylinder("bell_boss", (ew / 2 + 0.01, -0.05 + 0.172 * math.cos(a), -0.02 + 0.172 * math.sin(a)), 0.012, 0.02, "x", m["alu"], 12, 0.002))
    p.append(g.cylinder("gearbox", (ew / 2 + 0.22, -0.08, -0.02), 0.13, 0.22, "x", m["alu"], 40, 0.01))
    for i in range(4):
        p.append(g.torus("case_rib", (ew / 2 + 0.15 + i * 0.045, -0.08, -0.02), 0.131, 0.006, "x", m["alu"]))
    # Intake: plenum, curved runners into the head, throttle flange at +x.
    pz = -bd / 2 - 0.14
    p.append(g.cylinder("plenum", (0, ht - 0.02, pz), 0.055, ew * 0.9, "x", m["black"], 32, 0.01))
    for x in cx:
        p.append(g.tube("runner", [(x, ht - 0.02, pz + 0.03), (x, ht + 0.01, pz + 0.08), (x, ht - 0.03, -bd / 2 - 0.01), (x, ht - 0.07, -bd / 2 + 0.01)], 0.024, m["black"]))
    p.append(g.cylinder("throttle_flange", (ew * 0.45 + 0.01, ht - 0.02, pz), 0.06, 0.012, "x", m["alu"], 32, 0.002))
    # Fuel rail along the injector bosses.
    p.append(g.cylinder("fuel_rail", (0, ht + 0.02, -bd / 2 - 0.035), 0.012, ew - 0.06, "x", m["steel"], 16, 0.002))
    for x in cx:
        p.append(g.box("rail_clamp", (x + 0.03, ht + 0.02, -bd / 2 - 0.035), (0.012, 0.03, 0.03), m["alu"], 0.002))
    # Exhaust: primaries, collector and a heat shield with ribs.
    for x in cx:
        p.append(g.tube("primary", [(x, bt + 0.05, bd / 2), (x, bt + 0.03, bd / 2 + 0.05), (x * 0.6, bt - 0.02, bd / 2 + 0.09), (ew / 2 - 0.12, bt - 0.06, bd / 2 + 0.11)], 0.019, m["exhaust"]))
    p.append(g.box("heatshield", (0, bt + 0.02, bd / 2 + 0.115), (ew - 0.06, 0.12, 0.012), m["heatshield"], 0.004))
    for i in range(int((ew - 0.1) / 0.03)):
        p.append(g.box("shield_rib", (-ew / 2 + 0.06 + i * 0.03, bt + 0.02, bd / 2 + 0.122), (0.004, 0.1, 0.004), m["heatshield"], 0.001))
    # Timing end: plastic cover, water pump and the serpentine accessory belt.
    p.append(g.box("timing_cover", (-ew / 2 - 0.02, 0.02, 0.0), (0.035, bh + hh - 0.06, bd * 0.75), m["black"], 0.02))
    pulleys = [((-0.12, 0.0), 0.07, "crank"), ((0.07, 0.07), 0.05, "water_pump"), ((-0.02, 0.15), 0.03, "tensioner"), ((-0.17, 0.15), 0.055, "ac")]
    xb = -ew / 2 - 0.05
    for (yy, zz), r, n in pulleys:
        p.append(g.cylinder(n, (xb, yy, zz), r, 0.025, "x", m["steel"], 36, 0.003))
        p.append(g.cylinder(n + "_hub", (xb - 0.015, yy, zz), r * 0.35, 0.01, "x", m["steel"], 18, 0.002))
    belt = []
    hull = [((-0.12, 0.0), 0.07), ((-0.17, 0.15), 0.055), ((-0.02, 0.15), 0.03), ((0.07, 0.07), 0.05)]
    for i, ((yy, zz), r) in enumerate(hull):
        (y0, z0), _ = hull[i - 1]
        (y1, z1), _ = hull[(i + 1) % len(hull)]
        a0 = math.atan2(zz - z0, yy - y0) - math.pi / 2
        a1 = math.atan2(z1 - zz, y1 - yy) - math.pi / 2
        while a1 < a0:
            a1 += 2 * math.pi
        for k in range(6):
            a = a0 + (a1 - a0) * k / 5
            belt.append((xb, yy + (r + 0.004) * math.cos(a), zz + (r + 0.004) * math.sin(a)))
    belt.append(belt[0])
    p.append(g.tube("belt", belt, 0.005, m["rubber"]))
    # Coolant: thermostat housing outlet and the upper/lower radiator hoses heading to the front.
    p.append(g.tube("upper_hose", [(-ew / 2 + 0.02, ht - 0.03, bd / 2 - 0.02), (-ew / 2 - 0.02, ht - 0.02, bd / 2 + 0.12), (-ew / 2 + 0.05, ht - 0.06, bd / 2 + 0.3)], 0.019, m["hose"]))
    p.append(g.tube("lower_hose", [(-ew / 2 - 0.02, -0.06, 0.07), (-ew / 2 - 0.04, -0.12, bd / 2 + 0.1), (-ew / 2 + 0.08, -0.2, bd / 2 + 0.3)], 0.019, m["hose"]))
    p.append(g.tube("heater_hose", [(ew / 2 - 0.05, ht - 0.04, -bd / 2 + 0.02), (ew / 2 - 0.02, ht - 0.1, -bd / 2 - 0.15), (ew / 2 - 0.1, ht - 0.12, -bd / 2 - 0.3)], 0.011, m["hose"]))
    # Wiring harness along the intake side with branch leads to each injector and coil.
    loom = [(-ew / 2 + 0.02, ht + 0.06, -bd / 2 - 0.08), (0, ht + 0.065, -bd / 2 - 0.085), (ew / 2 - 0.02, ht + 0.06, -bd / 2 - 0.08), (ew / 2 + 0.05, ht, -bd / 2 - 0.2)]
    p.append(g.tube("loom", loom, 0.012, m["loom"]))
    for x in cx:
        p.append(g.tube("lead_inj", [(x, ht + 0.065, -bd / 2 - 0.085), (x + 0.01, ht + 0.05, -bd / 2 - 0.05), (x + 0.012, ht + 0.035, -bd / 2 - 0.035)], 0.004, m["loom"]))
        p.append(g.tube("lead_coil", [(x, ht + 0.065, -bd / 2 - 0.085), (x + 0.015, ht + 0.07, -0.06), (x + 0.02, ht + 0.06, -0.02)], 0.004, m["loom"]))
    # Engine mounts.
    p.append(g.box("mount_l", (-ew / 2 - 0.02, 0.1, bd / 2 - 0.05), (0.06, 0.06, 0.08), m["alu"], 0.008))
    p.append(g.cylinder("mount_rubber", (-ew / 2 - 0.02, 0.15, bd / 2 - 0.05), 0.03, 0.04, "y", m["rubber"], 20, 0.004))
    finish(f"Engine_I{cyl}", p, out, ((0, -0.03, 0), (ew + 0.1, bh + hh + 0.2, bd + 0.2)), lods=(1.0, 0.4, 0.12))


# ---------------------------------------------------------------------------------------------- parts

def coil(out):
    m = M()
    p = [g.cylinder("body", (0, -0.01, 0), 0.016, 0.07, "y", m["black"]),
         g.box("head", (0, 0.035, 0.005), (0.04, 0.022, 0.05), m["black"], 0.004),
         g.box("plug", (0, 0.04, 0.03), (0.022, 0.014, 0.016), m["grey"], 0.002),
         g.cylinder("boot", (0, -0.05, 0), 0.012, 0.02, "y", m["rubber"])]
    finish("IgnitionCoil", p, out, ((0, 0, 0), (0.045, 0.09, 0.06)))


def spark_plug(out):
    m = M()
    p = [g.cylinder("insulator", (0, 0.01, 0), 0.006, 0.026, "y", m["ceramic"]),
         g.cylinder("hex", (0, -0.008, 0), 0.0105, 0.01, "y", m["steel"], 6),
         g.cylinder("thread", (0, -0.017, 0), 0.007, 0.01, "y", m["steel"]),
         g.cylinder("terminal", (0, 0.025, 0), 0.003, 0.006, "y", m["steel"])]
    finish("SparkPlug", p, out, ((0, 0, 0), (0.022, 0.04, 0.022)))


def injector(out):
    m = M()
    p = [g.cylinder("body", (0, 0.0, 0), 0.009, 0.05, "y", m["black"]),
         g.box("connector", (0, 0.03, 0.008), (0.014, 0.012, 0.018), m["grey"], 0.002),
         g.cylinder("nozzle", (0, -0.032, 0), 0.005, 0.016, "y", m["steel"]),
         g.torus("oring", (0, -0.022, 0), 0.008, 0.002, "y", m["rubber"])]
    finish("Injector", p, out, ((0, 0, 0), (0.024, 0.08, 0.024)))


def turbo(out):
    m = M()
    p = [g.torus("turbine_volute", (0.02, 0, 0), 0.05, 0.028, "x", m["exhaust"]),
         g.cylinder("center", (0, 0, 0), 0.03, 0.05, "x", m["iron"]),
         g.torus("compressor_volute", (-0.04, 0, 0), 0.055, 0.025, "x", m["alu"]),
         g.cylinder("inlet", (-0.075, 0, 0), 0.03, 0.03, "x", m["alu"]),
         g.cylinder("outlet", (-0.04, 0.06, 0), 0.018, 0.04, "y", m["alu"]),
         g.cylinder("actuator", (-0.02, -0.06, 0.04), 0.018, 0.03, "z", m["steel"])]
    finish("Turbocharger", p, out, ((0, 0, 0), (0.18, 0.18, 0.18)))


def intercooler(out):
    m = M()
    p = [g.box("core", (0, 0, 0), (0.56, 0.12, 0.045), m["alu"], 0.002)]
    for i in range(14):
        p.append(g.box(f"fin{i}", (-0.26 + i * 0.04, 0, 0.024), (0.004, 0.115, 0.004), m["iron"], 0))
    p += [g.box("tank_l", (-0.31, 0, 0), (0.06, 0.14, 0.05), m["alu"], 0.008),
          g.box("tank_r", (0.31, 0, 0), (0.06, 0.14, 0.05), m["alu"], 0.008),
          g.cylinder("pipe_l", (-0.33, 0.05, 0), 0.025, 0.04, "x", m["alu"]),
          g.cylinder("pipe_r", (0.33, 0.05, 0), 0.025, 0.04, "x", m["alu"])]
    finish("Intercooler", p, out, ((0, 0, 0), (0.7, 0.14, 0.05)))


def alternator(out):
    m = M()
    p = [g.cylinder("body", (0, 0, 0), 0.06, 0.09, "y", m["alu"], 32),
         g.cylinder("pulley", (0, 0.06, 0), 0.03, 0.02, "y", m["steel"], 24),
         g.box("regulator", (0, -0.05, 0.05), (0.04, 0.03, 0.03), m["black"], 0.003),
         g.cylinder("b_plus", (0.03, -0.05, 0.03), 0.006, 0.02, "y", m["copper"])]
    for i in range(10):
        a = i * math.pi / 5
        p.append(g.box(f"vent{i}", (math.cos(a) * 0.058, 0, math.sin(a) * 0.058), (0.006, 0.07, 0.006), m["iron"], 0))
    finish("Alternator", p, out, ((0, 0, 0), (0.13, 0.14, 0.13)))


def battery(out):
    m = M()
    p = [g.box("case", (0, -0.005, 0), (0.2, 0.18, 0.28), m["black"], 0.006),
         g.box("lid", (0, 0.088, 0), (0.205, 0.015, 0.285), m["grey"], 0.004),
         g.cylinder("pos", (0.06, 0.1, 0.1), 0.009, 0.02, "y", m["brass"]),
         g.cylinder("neg", (-0.06, 0.1, 0.1), 0.008, 0.02, "y", m["brass"]),
         g.box("pos_cap", (0.06, 0.112, 0.1), (0.025, 0.008, 0.025), m["red"], 0.002)]
    finish("Battery", p, out, ((0, 0, 0), (0.2, 0.19, 0.28)))


def box_sensor(name, size, out):
    m = M()
    sx, sy, sz = size
    p = [g.box("body", (0, -sy * 0.15, 0), (sx, sy * 0.7, sz), m["black"], 0.003),
         g.box("connector", (0, sy * 0.3, -sz * 0.1), (sx * 0.6, sy * 0.4, sz * 0.5), m["grey"], 0.002),
         g.cylinder("port", (0, -sy * 0.55, 0), min(sx, sz) * 0.18, sy * 0.25, "y", m["black"])]
    finish(name, p, out, ((0, 0, 0), size))


def threaded_sensor(name, diameter, height, out):
    m = M()
    p = [g.cylinder("thread", (0, -height * 0.32, 0), diameter * 0.35, height * 0.36, "y", m["brass"]),
         g.cylinder("hex", (0, -height * 0.08, 0), diameter * 0.5, height * 0.14, "y", m["steel"], 6),
         g.cylinder("body", (0, height * 0.15, 0), diameter * 0.42, height * 0.3, "y", m["black"]),
         g.box("connector", (0, height * 0.38, 0), (diameter * 0.8, height * 0.22, diameter * 0.6), m["grey"], 0.002)]
    finish(name, p, out, ((0, 0, 0), (diameter, height, diameter)))


def throttle(out):
    m = M()
    p = [g.cylinder("bore", (0, 0, 0), 0.034, 0.06, "z", m["alu"], 32),
         g.box("motor", (0.035, -0.02, 0), (0.03, 0.05, 0.05), m["black"], 0.004),
         g.box("tps", (-0.04, 0, 0), (0.012, 0.04, 0.04), m["black"], 0.003),
         g.cylinder("plate", (0, 0, 0), 0.03, 0.002, "z", m["steel"], 24)]
    finish("ElectronicThrottle", p, out, ((0, 0, 0), (0.09, 0.09, 0.07)))


def hose(name, radius, length, out):
    m = M()
    h = length
    p = [g.tube("hose", [(0, -h / 2, 0), (radius * 0.6, 0, radius * 0.4), (0, h / 2, 0)], radius, m["rubber"]),
         g.torus("clamp_a", (0, -h / 2 + radius * 0.8, 0), radius * 1.05, radius * 0.18, "y", m["steel"]),
         g.torus("clamp_b", (0, h / 2 - radius * 0.8, 0), radius * 1.05, radius * 0.18, "y", m["steel"])]
    finish(name, p, out, ((0, 0, 0), (radius * 2.4, h, radius * 2.4)))


def ecu(out):
    m = M()
    p = [g.box("case", (0, 0, 0), (0.18, 0.03, 0.15), m["alu"], 0.003)]
    for i in range(8):
        p.append(g.box(f"fin{i}", (-0.07 + i * 0.02, 0.018, 0), (0.004, 0.006, 0.13), m["alu"], 0))
    p.append(g.box("connector", (0, 0, -0.08), (0.14, 0.022, 0.02), m["black"], 0.003))
    finish("ControlModule", p, out, ((0, 0, 0), (0.18, 0.035, 0.15)))


def relay(out):
    m = M()
    p = [g.box("cube", (0, 0.003, 0), (0.03, 0.03, 0.03), m["black"], 0.002)]
    for i, (x, z) in enumerate([(-0.008, -0.008), (0.008, -0.008), (-0.008, 0.008), (0.008, 0.008)]):
        p.append(g.box(f"pin{i}", (x, -0.016, z), (0.004, 0.006, 0.001), m["brass"], 0))
    finish("Relay", p, out, ((0, 0, 0), (0.035, 0.035, 0.035)))


def thermostat(out):
    m = M()
    p = [g.sphere("housing", (0, 0, 0), 0.026, m["alu"]),
         g.cylinder("outlet", (0.03, 0, 0), 0.014, 0.03, "x", m["alu"]),
         g.cylinder("flange", (0, -0.022, 0), 0.03, 0.006, "y", m["alu"])]
    finish("Thermostat", p, out, ((0, 0, 0), (0.06, 0.06, 0.06)))


def airbox(out):
    m = M()
    p = [g.box("lower", (0, -0.015, 0), (0.28, 0.05, 0.3), m["black"], 0.012),
         g.box("lid", (0, 0.025, 0), (0.27, 0.03, 0.29), m["black"], 0.012)]
    for i, (x, z) in enumerate([(-0.14, -0.08), (-0.14, 0.08), (0.14, -0.08), (0.14, 0.08)]):
        p.append(g.box(f"clip{i}", (x, 0.0, z), (0.01, 0.04, 0.02), m["steel"], 0.001))
    p.append(g.cylinder("snorkel", (0, 0.0, -0.17), 0.035, 0.05, "z", m["black"]))
    finish("AirFilter", p, out, ((0, 0, 0), (0.28, 0.08, 0.3)))


def maf(out):
    m = M()
    p = [g.cylinder("tube", (0, 0, 0), 0.034, 0.08, "z", m["black"], 32),
         g.box("sensor", (0, 0.035, 0), (0.03, 0.02, 0.04), m["black"], 0.003),
         g.box("connector", (0, 0.05, 0.012), (0.02, 0.014, 0.014), m["grey"], 0.002)]
    finish("MafSensor", p, out, ((0, 0, 0), (0.07, 0.07, 0.08)))


def catalyst(out):
    m = M()
    p = [g.cylinder("can", (0, 0, 0), 0.055, 0.16, "y", m["exhaust"], 32),
         g.cylinder("cone_in", (0, 0.095, 0), 0.03, 0.03, "y", m["exhaust"]),
         g.cylinder("cone_out", (0, -0.095, 0), 0.03, 0.03, "y", m["exhaust"]),
         g.cylinder("shield", (0, 0, 0.004), 0.058, 0.12, "y", m["steel"], 32)]
    finish("Catalyst", p, out, ((0, 0, 0), (0.12, 0.22, 0.12)))


def fan(out):
    m = M()
    p = [g.torus("shroud", (0, 0, 0), 0.135, 0.012, "y", m["black"]),
         g.cylinder("motor", (0, -0.005, 0), 0.04, 0.03, "y", m["black"])]
    for i in range(7):
        a = i * 2 * math.pi / 7
        b = g.box(f"blade{i}", (math.cos(a) * 0.08, 0, math.sin(a) * 0.08), (0.09, 0.004, 0.03), m["black"], 0)
        b.rotation_euler = (0, 0, -a)
        p.append(b)
    finish("CoolingFan", p, out, ((0, 0, 0), (0.28, 0.03, 0.28)))


# ------------------------------------------------------------------------------------------- workshop

def lift(out):
    """Two-post lift (≈3.5 m between posts, 2.9 m tall), same layout as the scene placeholder."""
    m = M()
    p = []
    for sx in (-1, 1):
        p.append(g.box(f"post{sx}", (sx * 1.6, 1.45, 0), (0.3, 2.9, 0.35), m["red"], 0.01))
        p.append(g.box(f"base{sx}", (sx * 1.6, 0.01, 0), (0.6, 0.02, 0.6), m["red"], 0.005))
        p.append(g.box(f"carriage{sx}", (sx * 1.48, 0.25, 0), (0.12, 0.4, 0.3), m["steel"], 0.006))
        for sz in (-1, 1):
            p.append(g.box(f"arm{sx}{sz}", (sx * 1.05, 0.12, sz * 0.55), (1.1, 0.08, 0.12), m["steel"], 0.004))
            p.append(g.cylinder(f"pad{sx}{sz}", (sx * 0.6, 0.17, sz * 0.95), 0.07, 0.04, "y", m["rubber"]))
    p.append(g.box("crossbeam", (0, 2.95, 0), (3.5, 0.15, 0.25), m["red"], 0.008))
    p.append(g.box("motor", (1.85, 1.2, 0.12), (0.2, 0.35, 0.2), m["grey"], 0.01))
    p.append(g.tube("hydraulic", [(1.85, 1.0, 0.12), (1.0, 2.8, 0.1), (-1.6, 2.85, 0.1)], 0.008, m["black"]))
    finish("TwoPostLift", p, out)


def workbench(out):
    m = M()
    p = [g.box("top", (0, 0.9, 0), (2.0, 0.05, 0.75), m["wood"], 0.006),
         g.box("cabinet", (0, 0.44, 0), (1.96, 0.86, 0.7), m["grey"], 0.006)]
    for i in range(4):
        p.append(g.box(f"drawer{i}", (-0.5, 0.75 - i * 0.18, 0.352), (0.8, 0.15, 0.01), m["grey"], 0.003))
        p.append(g.box(f"handle{i}", (-0.5, 0.75 - i * 0.18, 0.362), (0.2, 0.015, 0.012), m["steel"], 0.002))
    p += [g.box("vice_base", (0.8, 0.96, 0.25), (0.18, 0.08, 0.15), m["blue"], 0.006),
          g.box("vice_jaw", (0.8, 1.02, 0.31), (0.16, 0.06, 0.04), m["steel"], 0.003),
          g.box("pegboard", (0, 1.55, -0.36), (1.9, 1.1, 0.02), m["grey"], 0.004)]
    finish("Workbench", p, out, ((0, 0.45, 0), (2.0, 0.9, 0.75)))


def tool_cart(out):
    m = M()
    p = [g.box("body", (0, 0.45, 0), (0.7, 0.8, 0.45), m["red"], 0.01),
         g.box("top", (0, 0.87, 0), (0.72, 0.04, 0.47), m["steel"], 0.004)]
    for i in range(5):
        p.append(g.box(f"drawer{i}", (0, 0.75 - i * 0.14, 0.227), (0.64, 0.12, 0.008), m["red"], 0.003))
        p.append(g.box(f"handle{i}", (0, 0.75 - i * 0.14, 0.236), (0.4, 0.012, 0.012), m["steel"], 0.002))
    for x in (-0.3, 0.3):
        for z in (-0.18, 0.18):
            p.append(g.cylinder(f"wheel{x}{z}", (x, 0.04, z), 0.04, 0.03, "x", m["rubber"]))
    finish("ToolCart", p, out, ((0, 0.45, 0), (0.7, 0.9, 0.45)))


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    out_root = os.path.abspath(args[0]) if args else os.path.join(ROOT, "Unity", "Assets", "Garage", "Resources")
    parts = os.path.join(out_root, "CarParts")
    jobs = [
        (engine, 4, os.path.join(out_root, "Engines", "Engine_I4.fbx")),
        (engine, 6, os.path.join(out_root, "Engines", "Engine_I6.fbx")),
        (coil, None, os.path.join(parts, "IgnitionCoil.fbx")),
        (spark_plug, None, os.path.join(parts, "SparkPlug.fbx")),
        (injector, None, os.path.join(parts, "Injector.fbx")),
        (turbo, None, os.path.join(parts, "Turbocharger.fbx")),
        (intercooler, None, os.path.join(parts, "Intercooler.fbx")),
        (alternator, None, os.path.join(parts, "Alternator.fbx")),
        (battery, None, os.path.join(parts, "Battery.fbx")),
        (throttle, None, os.path.join(parts, "ElectronicThrottle.fbx")),
        (ecu, None, os.path.join(parts, "ControlModule.fbx")),
        (relay, None, os.path.join(parts, "Relay.fbx")),
        (thermostat, None, os.path.join(parts, "Thermostat.fbx")),
        (airbox, None, os.path.join(parts, "AirFilter.fbx")),
        (maf, None, os.path.join(parts, "MafSensor.fbx")),
        (catalyst, None, os.path.join(parts, "Catalyst.fbx")),
        (fan, None, os.path.join(parts, "CoolingFan.fbx")),
        (lift, None, os.path.join(out_root, "Workshop", "TwoPostLift.fbx")),
        (workbench, None, os.path.join(out_root, "Workshop", "Workbench.fbx")),
        (tool_cart, None, os.path.join(out_root, "Workshop", "ToolCart.fbx")),
    ]
    box_sensors = {"MapSensor": (0.035, 0.025, 0.035), "BoostSensor": (0.035, 0.025, 0.035), "PurgeValve": (0.04, 0.04, 0.04),
                   "WastegateSolenoid": (0.04, 0.04, 0.04), "TpsSensor": (0.03, 0.03, 0.02)}
    threaded = {"EctSensor": (0.022, 0.06), "IatSensor": (0.025, 0.06), "CkpSensor": (0.025, 0.06), "CmpSensor": (0.025, 0.06),
                "O2Narrowband": (0.022, 0.07), "O2Wideband": (0.022, 0.07), "O2Downstream": (0.022, 0.07),
                "FuelPressureSensor": (0.025, 0.07), "KnockSensor": (0.035, 0.06)}
    hoses = {"VacuumHose": (0.009, 0.2), "BoostHose": (0.025, 0.22)}
    print(f"Generando modelos en {out_root}")
    for fn, arg, path in jobs:
        g.reset()
        fn(arg, path) if arg is not None else fn(path)
    for name, size in box_sensors.items():
        g.reset()
        box_sensor(name, size, os.path.join(parts, name + ".fbx"))
    for name, (d, h) in threaded.items():
        g.reset()
        threaded_sensor(name, d, h, os.path.join(parts, name + ".fbx"))
    for name, (r, length) in hoses.items():
        g.reset()
        hose(name, r, length, os.path.join(parts, name + ".fbx"))


if __name__ == "__main__":
    main()
