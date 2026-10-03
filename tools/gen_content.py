#!/usr/bin/env python3
"""
Generates the base game content in data/base from compact Python descriptions.

The JSON files are the source of truth for the game (they are hand-editable and moddable);
this script exists so the initial content is reproducible and internally coherent:
stock ignition maps are derived from the same knock-limit formula the engine model uses,
boost/wastegate maps from the turbo definition, VE estimates from the physical VE, etc.

Usage: python3 tools/gen_content.py   (from the repository root)
"""
import json
import math
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
BASE = os.path.join(ROOT, "data", "base")


def dump(path, obj):
    full = os.path.join(BASE, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8") as f:
        f.write(compact_json(obj))
        f.write("\n")


def compact_json(obj, indent=0):
    """Pretty JSON with numeric arrays kept on one line (readable maps)."""
    sp = "  " * indent
    if isinstance(obj, dict):
        if not obj:
            return "{}"
        items = []
        for k, v in obj.items():
            items.append(f'{sp}  {json.dumps(k, ensure_ascii=False)}: {compact_json(v, indent + 1)}')
        return "{\n" + ",\n".join(items) + "\n" + sp + "}"
    if isinstance(obj, list):
        if all(isinstance(x, (int, float, str, bool)) for x in obj):
            return "[" + ", ".join(json.dumps(r(x), ensure_ascii=False) for x in obj) + "]"
        items = [f"{sp}  {compact_json(x, indent + 1)}" for x in obj]
        return "[\n" + ",\n".join(items) + "\n" + sp + "]"
    return json.dumps(r(obj), ensure_ascii=False)


def r(x):
    if isinstance(x, float):
        v = round(x, 4)
        return int(v) if v == int(v) and abs(v) < 1e9 else v
    return x


def smooth(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)


def table(id_, unit, xname, xunit, xs, yname, yunit, ys, fn):
    return {
        "id": id_, "unit": unit,
        "x": {"name": xname, "unit": xunit, "values": xs},
        "y": {"name": yname, "unit": yunit, "values": ys},
        "values": [[r(fn(x, y)) for x in xs] for y in ys],
    }


def curve(id_, unit, xname, xunit, xs, vals):
    return {"id": id_, "unit": unit, "x": {"name": xname, "unit": xunit, "values": xs}, "values": vals}


# ---------------------------------------------------------------- engines
RPM = [800, 1200, 1600, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500, 6000, 6500, 7000, 7500]
MAP_AXIS_T = [20, 40, 60, 80, 100, 130, 160, 200, 250]
MAP_AXIS_NA = [15, 25, 40, 55, 70, 85, 100, 110]
LOAD_T = [0.1, 0.2, 0.35, 0.5, 0.7, 0.9, 1.1, 1.4, 1.7, 2.0, 2.4]
LOAD_NA = [0.1, 0.2, 0.3, 0.4, 0.55, 0.7, 0.85, 1.0, 1.1]


def ve_fn(peak_rpm, peak, low_map_loss=0.18, high_rpm_drop=0.12, redline=6500):
    def f(rpm, mapkpa):
        rpm_shape = 1 - 0.18 * ((rpm - peak_rpm) / 3500.0) ** 2
        if rpm > peak_rpm:
            rpm_shape -= high_rpm_drop * ((rpm - peak_rpm) / max(1.0, redline - peak_rpm)) ** 2
        map_shape = 1 - low_map_loss * max(0.0, (100 - mapkpa) / 85.0) ** 1.5
        return max(0.35, min(1.05, peak * rpm_shape * map_shape))
    return f


def mbt_fn(base_low, base_high, rpm_gain):
    def f(rpm, load):
        v = base_low - (base_low - base_high) * min(1.0, load / 1.0) - 3.0 * max(0.0, load - 1.0)
        v += rpm_gain * (rpm - 2500) / 1000.0
        if rpm < 1200:
            v -= 4
        return max(8.0, min(45.0, v))
    return f


ENGINES = {
    "t20_turbo": dict(
        name="T20 2.0 Turbo 16V (gasolina, inyección indirecta)", kind="GasolineTurbo", cylinders=4,
        displacementL=1.984, boreMm=82.5, strokeMm=92.8, compressionRatio=9.6, firingOrder=[1, 3, 4, 2],
        redlineRpm=6500, mechanicalLimitRpm=7100, throttleDiameterMm=60, inertiaKgM2=0.16,
        knockMarginDeg=-3.0, fuelRon=95, injectorFlowCcMin=440, injectorRefPressureKpa=400,
        railPressureKpa=400, pumpMaxPressureKpa=680, thermalCapacityKjK=45, thermostatOpenC=88,
        radiatorKwK=0.9, frictionFactor=1.0, indicatedEfficiency=0.385,
        ve=(ve_fn(4200, 0.97, redline=6500), MAP_AXIS_T), mbt=mbt_fn(40, 27, 1.0), loads=LOAD_T,
        turbo=dict(maxBoostKpa=160, fullSpoolRpm=2300, spoolStartRpm=1300, wastegateSpringKpa=40,
                   maxSafeBoostKpa=175, compressorEfficiency=0.72, intercoolerEffectiveness=0.68, spoolTimeConstant=0.45, maxFlowGps=200)),
    "k14_na": dict(
        name="K14 1.4 16V atmosférico", kind="GasolineNA", cylinders=4,
        displacementL=1.390, boreMm=76.5, strokeMm=75.6, compressionRatio=10.5, firingOrder=[1, 3, 4, 2],
        redlineRpm=6200, mechanicalLimitRpm=6900, throttleDiameterMm=48, inertiaKgM2=0.11,
        knockMarginDeg=-1.0, fuelRon=95, injectorFlowCcMin=165, injectorRefPressureKpa=350,
        railPressureKpa=350, pumpMaxPressureKpa=600, thermalCapacityKjK=34, thermostatOpenC=87,
        radiatorKwK=0.45, frictionFactor=0.95, indicatedEfficiency=0.375,
        ve=(ve_fn(4400, 0.93, low_map_loss=0.2, redline=6200), MAP_AXIS_NA), mbt=mbt_fn(42, 28, 1.1), loads=LOAD_NA),
    "d20_tdi": dict(
        name="D20 2.0 Turbodiésel common rail (experimental)", kind="DieselTurbo", cylinders=4,
        displacementL=1.997, boreMm=85.0, strokeMm=88.0, compressionRatio=16.5, firingOrder=[1, 3, 4, 2],
        redlineRpm=4600, mechanicalLimitRpm=5100, throttleDiameterMm=60, inertiaKgM2=0.22,
        knockMarginDeg=0, fuelRon=95, injectorFlowCcMin=900, injectorRefPressureKpa=160000,
        railPressureKpa=160000, pumpMaxPressureKpa=650, thermalCapacityKjK=52, thermostatOpenC=87,
        radiatorKwK=0.8, frictionFactor=1.15, indicatedEfficiency=0.43,
        ve=(ve_fn(2500, 0.92, low_map_loss=0.05, redline=4600), MAP_AXIS_T), mbt=mbt_fn(12, 8, 0.6), loads=LOAD_T,
        turbo=dict(maxBoostKpa=165, fullSpoolRpm=1900, spoolStartRpm=1100, wastegateSpringKpa=30,
                   maxSafeBoostKpa=185, compressorEfficiency=0.72, intercoolerEffectiveness=0.72, spoolTimeConstant=0.5, maxFlowGps=190)),
    "v30_turbo": dict(
        name="R6 3.0 Biturbo 24V", kind="GasolineTurbo", cylinders=6,
        displacementL=2.979, boreMm=84.0, strokeMm=89.6, compressionRatio=10.2, firingOrder=[1, 5, 3, 6, 2, 4],
        redlineRpm=7000, mechanicalLimitRpm=7600, throttleDiameterMm=74, inertiaKgM2=0.21,
        knockMarginDeg=-2.0, fuelRon=98, injectorFlowCcMin=550, injectorRefPressureKpa=400,
        railPressureKpa=400, pumpMaxPressureKpa=720, thermalCapacityKjK=60, thermostatOpenC=90,
        radiatorKwK=1.2, frictionFactor=1.0, indicatedEfficiency=0.39,
        ve=(ve_fn(4800, 1.0, redline=7000), MAP_AXIS_T), mbt=mbt_fn(40, 27, 1.0), loads=LOAD_T,
        turbo=dict(maxBoostKpa=150, fullSpoolRpm=2100, spoolStartRpm=1200, wastegateSpringKpa=45,
                   maxSafeBoostKpa=165, compressorEfficiency=0.74, intercoolerEffectiveness=0.75, spoolTimeConstant=0.35, maxFlowGps=330)),
}


def kla(e, mbt, rpm, mapkpa, lam, charge_c=45.0, ect=90.0):
    """Same knock-limited-advance formula as EngineModel (keep in sync)."""
    map_bar = mapkpa / 100.0
    return (mbt + e["knockMarginDeg"] + 1.2 * (95 - 95) - 11.0 * (map_bar - 1.0) - 0.18 * (charge_c - 40)
            - 0.12 * max(0, ect - 95) + 18.0 * max(-0.15, min(0.2, 1.0 - lam)) - 1.5 * (e["compressionRatio"] - 9.6)
            + 0.0006 * (rpm - 3000))


def write_engines():
    for eid, e in ENGINES.items():
        vef, maps = e["ve"]
        out = {k: v for k, v in e.items() if k not in ("ve", "mbt", "loads", "turbo")}
        out = {"id": eid, **out}
        out["ve"] = table("ve", "fraction", "rpm", "rpm", RPM, "map", "kPa", maps, vef)
        out["mbt"] = table("mbt", "deg", "rpm", "rpm", RPM, "load", "rel", e["loads"], e["mbt"])
        if "turbo" in e:
            out["turbo"] = e["turbo"]
        dump(f"engines/{eid}.json", out)


# ---------------------------------------------------------------- calibrations
PEDAL = [0, 10, 20, 35, 50, 70, 85, 100]


def lambda_target_fn(turbo, diesel=False):
    def f(rpm, load):
        if diesel:
            return 1.0
        if turbo:
            enrich = smooth(0.95, 1.6, load)
            lam = 1.0 - 0.14 * enrich
            if rpm > 5000:
                lam -= 0.03 * smooth(5000, 6500, rpm) * enrich
            return lam
        enrich = smooth(0.78, 0.98, load)
        return 1.0 - 0.12 * enrich
    return f


def ignition_fn(e, lam_fn, safety):
    mbtf = e["mbt"]

    def f(rpm, load):
        if e["kind"] == "DieselTurbo":
            return 2 + 6 * min(1.0, rpm / 3500.0) - 2 * min(1.0, load)
        mbt = mbtf(rpm, load)
        mapkpa = load * 100 / 0.95
        lam = lam_fn(rpm, load)
        limit = kla(e, mbt, rpm, mapkpa, lam) - safety
        adv = min(mbt - 1.0, limit)
        if rpm <= 900 and load < 0.35:
            adv = min(adv, 12.0)
        return max(-2.0, adv)
    return f


def boost_target_fn(e, peak, taper_from, taper_to_frac):
    def f(rpm, pedal):
        if "turbo" not in e:
            return 0
        p = smooth(25, 90, pedal)
        spool = smooth(e["turbo"]["spoolStartRpm"] * 0.9, e["turbo"]["fullSpoolRpm"], rpm)
        taper = 1.0 - (1 - taper_to_frac) * smooth(taper_from, e["redlineRpm"], rpm)
        return peak * p * max(0.25, spool) * taper
    return f


def wastegate_fn(e, boost_fn):
    def f(rpm, pedal):
        if "turbo" not in e:
            return 0
        t = e["turbo"]
        target = boost_fn(rpm, pedal)
        if target <= t["wastegateSpringKpa"]:
            return 0
        return max(0.0, min(95.0, (target - t["wastegateSpringKpa"]) / (t["maxBoostKpa"] * 1.1 - t["wastegateSpringKpa"]) * 100 * 0.92))
    return f


def diesel_quantity_fn(maxq):
    def f(rpm, pedal):
        base = maxq * smooth(5, 95, pedal)
        shape = 1.0 - 0.25 * ((rpm - 2000) / 2600.0) ** 2
        return max(0.0, base * shape)
    return f


CALIBRATIONS = {
    "t20_stock": dict(engine="t20_turbo", desc="Mapa de serie T20 2.0T 245 CV", peak_boost=100, taper_from=5200, taper_to=0.82,
                      safety=3.0, scalars=dict(rev_limit_rpm=6600, speed_limit_kmh=250, torque_limit_nm=370, idle_rpm=780,
                                               idle_cold_rpm=1150, injector_flow_ccmin=440, injector_dead_time_ms=0.5,
                                               maf_max_gps=200, fan_on_c=102, fan_off_c=97, knock_retard_step_deg=1.5,
                                               knock_retard_max_deg=10, overboost_limit_kpa=150, rail_pressure_target_kpa=400,
                                               map_sensor_max_kpa=250)),
    "k14_stock": dict(engine="k14_na", desc="Mapa de serie K14 1.4 90 CV", safety=1.5,
                      scalars=dict(rev_limit_rpm=6300, speed_limit_kmh=185, idle_rpm=820, idle_cold_rpm=1200,
                                   injector_flow_ccmin=165, injector_dead_time_ms=0.55, fan_on_c=101, fan_off_c=96,
                                   knock_retard_step_deg=1.5, knock_retard_max_deg=10, rail_pressure_target_kpa=350,
                                   map_sensor_max_kpa=105)),
    "d20_stock": dict(engine="d20_tdi", desc="Mapa de serie D20 2.0 TD 150 CV (experimental)", peak_boost=115, taper_from=3500,
                      taper_to=0.8, safety=0, maxq=62,
                      scalars=dict(rev_limit_rpm=4700, speed_limit_kmh=215, torque_limit_nm=360, idle_rpm=820, idle_cold_rpm=950,
                                   maf_max_gps=150, fan_on_c=102, fan_off_c=97, overboost_limit_kpa=160,
                                   rail_pressure_target_kpa=160000, smoke_limit_lambda=1.15)),
    "v30_stock": dict(engine="v30_turbo", desc="Mapa de serie R6 3.0 biturbo 380 CV", peak_boost=95, taper_from=5500, taper_to=0.85,
                      safety=2.0, scalars=dict(rev_limit_rpm=7100, speed_limit_kmh=250, torque_limit_nm=520, idle_rpm=720,
                                               idle_cold_rpm=1000, injector_flow_ccmin=550, injector_dead_time_ms=0.48,
                                               maf_max_gps=300, fan_on_c=104, fan_off_c=98, knock_retard_step_deg=1.5,
                                               knock_retard_max_deg=10, overboost_limit_kpa=140, rail_pressure_target_kpa=400,
                                               map_sensor_max_kpa=250)),
}


def write_calibrations():
    for cid, c in CALIBRATIONS.items():
        e = ENGINES[c["engine"]]
        turbo = "turbo" in e
        diesel = e["kind"] == "DieselTurbo"
        loads = e["loads"]
        lam_fn = lambda_target_fn(turbo, diesel)
        tables = [
            table("lambda_target", "lambda", "rpm", "rpm", RPM, "load", "rel", loads, lam_fn),
            table("ignition_advance", "deg", "rpm", "rpm", RPM, "load", "rel", loads, ignition_fn(e, lam_fn, c["safety"])),
        ]
        vef, maps = e["ve"]
        tables.append(table("ve_estimate", "fraction", "rpm", "rpm", RPM, "map", "kPa", maps, lambda x, y: vef(x, y) * 0.99))
        if turbo:
            bf = boost_target_fn(e, c["peak_boost"], c["taper_from"], c["taper_to"])
            tables.append(table("boost_target", "kPa", "rpm", "rpm", RPM, "pedal", "%", PEDAL, bf))
            tables.append(table("wastegate_duty", "%", "rpm", "rpm", RPM, "pedal", "%", PEDAL, wastegate_fn(e, bf)))
        if diesel:
            tables.append(table("diesel_quantity", "mg/stroke", "rpm", "rpm", RPM, "pedal", "%", PEDAL, diesel_quantity_fn(c["maxq"])))
        curves = [
            curve("pedal_to_throttle", "%", "pedal", "%", [0, 10, 25, 50, 75, 100], [0, 4, 14, 38, 70, 100]),
            curve("ect_fuel_correction", "factor", "ect", "°C", [-20, 0, 20, 40, 60, 80], [1.45, 1.3, 1.15, 1.07, 1.02, 1.0]),
            curve("iat_ignition_correction", "deg", "iat", "°C", [-10, 20, 40, 55, 70, 90], [1.0, 0.0, 0.0, -1.5, -4.0, -7.0]),
            curve("ect_ignition_correction", "deg", "ect", "°C", [0, 40, 90, 105, 115], [3.0, 1.5, 0.0, -2.0, -5.0]),
        ]
        dump(f"calibrations/{cid}.json", {"id": cid, "engine": c["engine"], "description": c["desc"], "tables": tables,
                                          "curves": curves, "scalars": c["scalars"]})


# ---------------------------------------------------------------- templates
def pins(*items):
    return [{"role": a, "ecuPin": b, "pin": i + 1, "color": c} for i, (a, b, c) in enumerate(items)]


def comp(id_, kind, name, location, slot=None, params=None, circuit=None, part=None, minutes=30, per=False, fuse=None):
    d = {"id": id_, "kind": kind, "name": name, "location": location, "visualSlot": slot or id_}
    if per:
        d["perCylinder"] = True
    if params:
        d["params"] = params
    if circuit:
        d["circuit"] = circuit
    if fuse:
        d["fuse"] = fuse
    d["partId"] = part or ("part_" + kind.lower())
    d["replaceMinutes"] = minutes
    return d


def circ(topology, pin_list, fuse=None):
    c = {"topology": topology, "pins": pin_list}
    if fuse:
        c["fuse"] = fuse
    return c


INJ_PINS = ["A01", "A02", "A03", "A04", "A05", "A06"]
COIL_PINS = ["A13", "A14", "A15", "A16", "A17", "A18"]


def common_electrical():
    return [
        comp("battery", "Battery", "Batería 12 V", "Vano motor, lado izquierdo", params={"capacity_ah": 70}, minutes=15),
        comp("alternator", "Alternator", "Alternador", "Frontal del motor, correa auxiliar", minutes=75),
        comp("ground_strap", "GroundStrap", "Masa motor-carrocería", "Junto a la caja de cambios", minutes=20),
        comp("fuse_ecu", "Fuse", "Fusible ECU F01 (10 A)", "Caja de fusibles del vano motor", params={"amps": 10}, minutes=2),
        comp("fuse_pump", "Fuse", "Fusible bomba F05 (20 A)", "Caja de fusibles del vano motor", params={"amps": 20}, minutes=2),
        comp("fuse_o2", "Fuse", "Fusible sondas F09 (15 A)", "Caja de fusibles del vano motor", params={"amps": 15}, minutes=2),
        comp("fuse_injectors", "Fuse", "Fusible inyectores/solenoides F10 (15 A)", "Caja de fusibles del vano motor", params={"amps": 15}, minutes=2),
        comp("fuse_fan", "Fuse", "Fusible ventilador F20 (40 A)", "Caja de fusibles del vano motor", params={"amps": 40}, minutes=2),
        comp("can_bus", "CanBus", "Bus CAN motor", "Arnés principal", minutes=60),
        comp("module_abs", "ControlModule", "Módulo ABS/ESP", "Grupo hidráulico, vano motor", minutes=90),
        comp("module_ipc", "ControlModule", "Cuadro de instrumentos", "Salpicadero", minutes=60),
        comp("module_bcm", "ControlModule", "Módulo de carrocería BCM", "Bajo el salpicadero", minutes=60),
        comp("fan", "CoolingFan", "Electroventilador", "Detrás del radiador", fuse="fuse_fan", minutes=60),
        comp("fan_relay", "Relay", "Relé ventilador", "Caja de fusibles", params={"ohms": 85},
             circuit=circ("LowSideLoad", pins(("supply", "F20", "RJ"), ("control", "A94", "VE/BL")), "fuse_ecu"), minutes=5),
        comp("fuel_pump_relay", "Relay", "Relé bomba de combustible", "Caja de fusibles", params={"ohms": 85},
             circuit=circ("LowSideLoad", pins(("supply", "F05", "RJ"), ("control", "A93", "NE/VI")), "fuse_ecu"), minutes=5),
        comp("fuel_pump", "FuelPump", "Bomba de combustible", "Depósito (bajo asiento trasero)", fuse="fuse_pump", minutes=90),
        comp("fuel_filter", "FuelFilter", "Filtro de combustible", "Bajo el piso, junto al depósito", minutes=30),
    ]


def gasoline_common(turbo, wideband, maf):
    c = []
    if maf:
        c.append(comp("maf", "MafSensor", "Caudalímetro (MAF)", "Tubo de admisión tras el filtro", slot="maf",
                      params={"max_gps": 200 if turbo else 110},
                      circuit=circ("Powered", pins(("supply", "F12", "RJ/NE"), ("signal", "A23", "VE"), ("ground", "A24", "MA")), "fuse_injectors"),
                      minutes=15))
    c += [
        comp("map", "MapSensor", "Sensor de presión de colector (MAP)", "Colector de admisión", slot="map_sensor",
             params={"range_min_kpa": 10, "range_max_kpa": 250 if turbo else 105},
             circuit=circ("Ratiometric", pins(("ref", "A31", "GR"), ("signal", "A32", "VE/AM"), ("ground", "A33", "MA"))), minutes=15),
        comp("iat", "IatSensor", "Sensor de temperatura de aire (IAT)", "Colector de admisión" if turbo else "Caja del filtro",
             params={"post_intercooler": 1 if turbo else 0, "r25": 2000, "beta": 3450, "time_constant_s": 3.0},
             circuit=circ("Thermistor", pins(("signal", "A34", "AZ"), ("ground", "A35", "MA"))), minutes=10),
        comp("ect", "EctSensor", "Sensor de temperatura de refrigerante (ECT)", "Caja del termostato, culata", slot="ect_sensor",
             params={"r25": 2000, "beta": 3450, "time_constant_s": 2.0},
             circuit=circ("Thermistor", pins(("signal", "A40", "AM/NE"), ("ground", "A41", "MA"))), minutes=20),
        comp("tps", "TpsSensor", "Sensor de posición de mariposa (TPS)", "Cuerpo de mariposa",
             circuit=circ("Ratiometric", pins(("ref", "A50", "GR"), ("signal", "A51", "BL"), ("ground", "A52", "MA"))), minutes=45),
        comp("app", "AppSensor", "Sensor de pedal de acelerador (APP)", "Pedal del acelerador",
             circuit=circ("Ratiometric", pins(("ref", "B10", "GR/RJ"), ("signal", "B11", "VI"), ("ground", "B12", "MA/BL"))), minutes=25),
        comp("ckp", "CkpSensor", "Sensor de cigüeñal (CKP)", "Bloque, junto al volante motor", slot="ckp_sensor",
             params={"coil_ohms": 850, "air_gap_factor": 1.0},
             circuit=circ("Generator", pins(("signal", "A60", "NE"), ("signal_low", "A61", "BL"))), minutes=35),
        comp("cmp", "CmpSensor", "Sensor de árbol de levas (CMP)", "Tapa de culata, lado distribución",
             circuit=circ("Hall", pins(("ref", "A62", "GR"), ("signal", "A63", "VE/BL"), ("ground", "A64", "MA"))), minutes=20),
        comp("knock", "KnockSensor", "Sensor de detonación", "Bloque motor, entre cilindros 2 y 3",
             circuit=circ("Generator", pins(("signal", "A80", "NE/BL"), ("signal_low", "A81", "MA/BL"))), minutes=60),
    ]
    if wideband:
        c.append(comp("o2_up", "O2Wideband", "Sonda lambda de banda ancha (B1S1)", "Salida del turbo, antes del catalizador" if turbo else "Colector de escape",
                      slot="o2_upstream", params={"heater_ohms": 3.3},
                      circuit=circ("HeatedOxygen", pins(("signal", "A70", "NE"), ("signal_low", "A71", "GR"), ("heater_supply", "F09", "BL"), ("heater_control", "A72", "BL/VE")), "fuse_o2"),
                      minutes=25))
    else:
        c.append(comp("o2_up", "O2Narrowband", "Sonda lambda (B1S1)", "Colector de escape", slot="o2_upstream",
                      params={"heater_ohms": 4.5, "time_constant_s": 0.05},
                      circuit=circ("HeatedOxygen", pins(("signal", "A70", "NE"), ("signal_low", "A71", "GR"), ("heater_supply", "F09", "BL"), ("heater_control", "A72", "BL/VE")), "fuse_o2"),
                      minutes=25))
    c += [
        comp("o2_down", "O2Downstream", "Sonda lambda postcatalizador (B1S2)", "Tras el catalizador", slot="o2_downstream",
             params={"heater_ohms": 4.5},
             circuit=circ("HeatedOxygen", pins(("signal", "A73", "NE"), ("signal_low", "A74", "GR"), ("heater_supply", "F09", "BL"), ("heater_control", "A75", "BL/RJ")), "fuse_o2"),
             minutes=25),
        comp("fuel_pressure", "FuelPressureSensor", "Sensor de presión de rampa", "Rampa de inyectores", params={"range_max_kpa": 1000},
             circuit=circ("Ratiometric", pins(("ref", "A84", "GR"), ("signal", "A85", "VE/NE"), ("ground", "A86", "MA"))), minutes=30),
        comp("inj{n}", "Injector", "Inyector cilindro {n}", "Rampa de inyección", slot="injector_{n}", per=True, params={"ohms": 12.5},
             circuit=circ("LowSideLoad", [{"role": "supply", "ecuPin": "F10", "pin": 1, "color": "RJ/BL"},
                                          {"role": "control", "ecuPin": INJ_PINS, "pin": 2, "color": "NE/AM"}], "fuse_injectors"),
             minutes=25),
        comp("coil{n}", "IgnitionCoil", "Bobina cilindro {n}", "Sobre la bujía (bobina tipo lápiz)", slot="coil_{n}", per=True,
             params={"ohms": 0.7},
             circuit=circ("LowSideLoad", [{"role": "supply", "ecuPin": "F11", "pin": 1, "color": "RJ/NE"},
                                          {"role": "control", "ecuPin": COIL_PINS, "pin": 2, "color": "VE/BL"}], "fuse_injectors"),
             minutes=8),
        comp("plug{n}", "SparkPlug", "Bujía cilindro {n}", "Culata, bajo la bobina", slot="spark_plug_{n}", per=True,
             params={"gap_mm": 0.75}, minutes=10),
        comp("cyl{n}", "Cylinder", "Cilindro {n} (segmentos, válvulas)", "Bloque/culata", slot="cylinder_{n}", per=True, minutes=600),
        comp("intake_gasket{n}", "IntakeGasket", "Junta de colector, cilindro {n}", "Unión colector-culata", slot="intake_gasket_{n}", per=True, minutes=120),
        comp("throttle", "ElectronicThrottle", "Cuerpo de mariposa motorizado", "Entrada del colector", slot="throttle_body", params={"ohms": 1.6},
             circuit=circ("LowSideLoad", pins(("supply", "F13", "RJ"), ("control", "A90", "AM")), "fuse_ecu"), minutes=40),
        comp("purge_valve", "PurgeValve", "Electroválvula de purga del cánister", "Colector de admisión", params={"ohms": 24},
             circuit=circ("LowSideLoad", pins(("supply", "F10", "RJ/BL"), ("control", "A92", "GR/VE")), "fuse_injectors"), minutes=15),
        comp("pcv_hose", "VacuumHose", "Manguito de respiradero (PCV)", "Tapa de balancines a colector", slot="vacuum_hose_pcv", minutes=20),
        comp("brake_booster_hose", "VacuumHose", "Manguito del servofreno", "Colector a servofreno", slot="vacuum_hose_brake", minutes=15),
        comp("thermostat", "Thermostat", "Termostato", "Caja del termostato", minutes=60),
        comp("head_gasket", "HeadGasket", "Junta de culata", "Entre bloque y culata", minutes=720),
        comp("timing_chain", "TimingDrive", "Cadena de distribución", "Tapa de distribución", minutes=480),
        comp("exhaust", "Exhaust", "Colector y línea de escape", "Escape", slot="exhaust_manifold", minutes=90),
        comp("catalyst", "Catalyst", "Catalizador", "Bajo el vehículo", minutes=60),
        comp("air_filter", "AirFilter", "Filtro de aire", "Caja del filtro", minutes=5),
        comp("fuel_regulator", "FuelPressureRegulator", "Regulador de presión de combustible", "Módulo de bomba", minutes=60),
    ]
    if turbo:
        c += [
            comp("boost", "BoostSensor", "Sensor de presión de sobrealimentación", "Tubo tras el intercooler", params={"range_max_kpa": 300},
                 circuit=circ("Ratiometric", pins(("ref", "A36", "GR"), ("signal", "A37", "BL/AM"), ("ground", "A38", "MA"))), minutes=15),
            comp("turbo", "Turbocharger", "Turbocompresor", "Colector de escape", minutes=300),
            comp("wastegate_valve", "WastegateSolenoid", "Electroválvula de wastegate (N75)", "Junto a la caja del filtro", params={"ohms": 26},
                 circuit=circ("LowSideLoad", pins(("supply", "F10", "RJ/BL"), ("control", "A91", "VI/BL")), "fuse_injectors"), minutes=20),
            comp("intercooler", "Intercooler", "Intercooler", "Frontal, tras el paragolpes", minutes=120),
            comp("charge_pipe", "BoostHose", "Manguito de presión (turbo-intercooler)", "Lado derecho del motor", slot="boost_hose", minutes=30),
        ]
    return c


def diesel_components():
    c = [
        comp("maf", "MafSensor", "Caudalímetro (MAF)", "Tubo de admisión", params={"max_gps": 150},
             circuit=circ("Powered", pins(("supply", "F12", "RJ/NE"), ("signal", "A23", "VE"), ("ground", "A24", "MA")), "fuse_injectors"), minutes=15),
        comp("map", "MapSensor", "Sensor de presión de sobrealimentación (MAP)", "Colector de admisión", slot="map_sensor",
             params={"range_min_kpa": 20, "range_max_kpa": 300},
             circuit=circ("Ratiometric", pins(("ref", "A31", "GR"), ("signal", "A32", "VE/AM"), ("ground", "A33", "MA"))), minutes=15),
        comp("iat", "IatSensor", "Sensor de temperatura de aire (IAT)", "Colector de admisión", params={"post_intercooler": 1, "time_constant_s": 3},
             circuit=circ("Thermistor", pins(("signal", "A34", "AZ"), ("ground", "A35", "MA"))), minutes=10),
        comp("ect", "EctSensor", "Sensor de temperatura de refrigerante (ECT)", "Caja del termostato", slot="ect_sensor", params={"time_constant_s": 2},
             circuit=circ("Thermistor", pins(("signal", "A40", "AM/NE"), ("ground", "A41", "MA"))), minutes=20),
        comp("app", "AppSensor", "Sensor de pedal de acelerador (APP)", "Pedal",
             circuit=circ("Ratiometric", pins(("ref", "B10", "GR/RJ"), ("signal", "B11", "VI"), ("ground", "B12", "MA/BL"))), minutes=25),
        comp("ckp", "CkpSensor", "Sensor de cigüeñal (CKP)", "Volante motor", slot="ckp_sensor", params={"coil_ohms": 850},
             circuit=circ("Generator", pins(("signal", "A60", "NE"), ("signal_low", "A61", "BL"))), minutes=35),
        comp("cmp", "CmpSensor", "Sensor de árbol de levas (CMP)", "Culata",
             circuit=circ("Hall", pins(("ref", "A62", "GR"), ("signal", "A63", "VE/BL"), ("ground", "A64", "MA"))), minutes=20),
        comp("fuel_pressure", "FuelPressureSensor", "Sensor de presión de rail", "Rail común", params={"range_max_kpa": 200000},
             circuit=circ("Ratiometric", pins(("ref", "A84", "GR"), ("signal", "A85", "VE/NE"), ("ground", "A86", "MA"))), minutes=30),
        comp("inj{n}", "Injector", "Inyector common rail cilindro {n}", "Culata", slot="injector_{n}", per=True, params={"ohms": 0.6},
             circuit=circ("LowSideLoad", [{"role": "supply", "ecuPin": "F10", "pin": 1, "color": "RJ/BL"},
                                          {"role": "control", "ecuPin": INJ_PINS, "pin": 2, "color": "NE/AM"}], "fuse_injectors"), minutes=60),
        comp("glow{n}", "GlowPlug", "Calentador cilindro {n}", "Culata", slot="glow_plug_{n}", per=True, params={"ohms": 0.9}, minutes=40),
        comp("cyl{n}", "Cylinder", "Cilindro {n}", "Bloque/culata", slot="cylinder_{n}", per=True, minutes=600),
        comp("egr_valve", "EgrValve", "Válvula EGR", "Colector de admisión", params={"ohms": 8, "max_area_mm2": 150},
             circuit=circ("LowSideLoad", pins(("supply", "F10", "RJ/BL"), ("control", "A95", "GR/AM")), "fuse_injectors"), minutes=90),
        comp("turbo", "Turbocharger", "Turbo de geometría variable", "Colector de escape", minutes=360),
        comp("wastegate_valve", "WastegateSolenoid", "Electroválvula de control del turbo", "Vano motor", params={"ohms": 26},
             circuit=circ("LowSideLoad", pins(("supply", "F10", "RJ/BL"), ("control", "A91", "VI/BL")), "fuse_injectors"), minutes=20),
        comp("intercooler", "Intercooler", "Intercooler", "Frontal", minutes=120),
        comp("charge_pipe", "BoostHose", "Manguito de presión", "Lado izquierdo", slot="boost_hose", minutes=30),
        comp("brake_booster_hose", "VacuumHose", "Tubo de vacío (bomba de vacío)", "Vano motor", slot="vacuum_hose_brake", minutes=15),
        comp("thermostat", "Thermostat", "Termostato", "Caja del termostato", minutes=60),
        comp("head_gasket", "HeadGasket", "Junta de culata", "Entre bloque y culata", minutes=720),
        comp("timing_chain", "TimingDrive", "Correa de distribución", "Tapa de distribución", minutes=300),
        comp("exhaust", "Exhaust", "Colector y línea de escape", "Escape", slot="exhaust_manifold", minutes=90),
        comp("catalyst", "Catalyst", "Catalizador de oxidación (DOC)", "Bajo el vehículo", minutes=60),
        comp("air_filter", "AirFilter", "Filtro de aire", "Caja del filtro", minutes=5),
        comp("fuel_regulator", "FuelPressureRegulator", "Válvula reguladora de presión de rail", "Bomba de alta presión", minutes=90),
    ]
    return c


def write_templates():
    dump("templates/gasoline_turbo.json", {"id": "gasoline_turbo", "description": "Gasolina turbo, inyección indirecta, MAF + MAP, sonda de banda ancha", "components": gasoline_common(True, True, True) + common_electrical()})
    dump("templates/gasoline_na_sd.json", {"id": "gasoline_na_sd", "description": "Gasolina atmosférico speed-density (sin MAF), sonda de banda estrecha", "components": gasoline_common(False, False, False) + common_electrical()})
    dump("templates/diesel_turbo.json", {"id": "diesel_turbo", "description": "Turbodiésel common rail (experimental)", "components": diesel_components() + common_electrical()})


# ---------------------------------------------------------------- cars
CARS = [
    dict(id="aurex_strada_gt", brand="Aurex", model="Strada GT", year=2016, segment="Compacto deportivo 5 puertas",
         engine="t20_turbo", template="gasoline_turbo", calibration="t20_stock", massKg=1420, wheelRadiusM=0.316,
         gearRatios=[3.36, 2.09, 1.47, 1.10, 0.87, 0.73], finalDrive=3.94, drivetrainEfficiency=0.88, dragAreaM2=0.68,
         vin="AUX1GT5D0G0012345", appearance=dict(ageYears=8, kilometers=118000, maintenance=0.55, paintColor="#B1121B", humidity=0.3)),
    dict(id="velmora_pico", brand="Velmora", model="Pico 1.4", year=2011, segment="Utilitario urbano",
         engine="k14_na", template="gasoline_na_sd", calibration="k14_stock", massKg=1090, wheelRadiusM=0.29,
         gearRatios=[3.42, 1.95, 1.28, 0.97, 0.78], finalDrive=4.07, drivetrainEfficiency=0.9, dragAreaM2=0.62,
         vin="VLM0PC14B00067890", removeComponents=[], appearance=dict(ageYears=14, kilometers=196000, maintenance=0.35, paintColor="#D9D4C7", humidity=0.6)),
    dict(id="nordak_atlas_td", brand="Nordak", model="Atlas 2.0 TD", year=2014, segment="Berlina familiar diésel",
         engine="d20_tdi", template="diesel_turbo", calibration="d20_stock", massKg=1560, wheelRadiusM=0.325,
         gearRatios=[3.77, 1.96, 1.26, 0.88, 0.69, 0.56], finalDrive=3.65, drivetrainEfficiency=0.88, dragAreaM2=0.7,
         experimental=True, vin="NDK2ATD0E00024680", appearance=dict(ageYears=11, kilometers=245000, maintenance=0.6, paintColor="#2E3440", humidity=0.4)),
    dict(id="kessler_rapace", brand="Kessler", model="Rapace S", year=2019, segment="Deportivo de altas prestaciones",
         engine="v30_turbo", template="gasoline_turbo", calibration="v30_stock", massKg=1610, wheelRadiusM=0.34,
         gearRatios=[4.11, 2.32, 1.54, 1.18, 1.0, 0.85, 0.67], finalDrive=3.15, drivetrainEfficiency=0.86, dragAreaM2=0.62,
         vin="KSL3RPS0K00013579", fuelRon=98,
         componentOverrides=[{"id": "maf", "params": {"max_gps": 300}}],
         appearance=dict(ageYears=5, kilometers=42000, maintenance=0.9, paintColor="#1C3F94", humidity=0.2)),
]


def write_cars():
    for c in CARS:
        out = dict(c)
        out["modules"] = ["ecm", "abs", "ipc", "bcm"]
        dump(f"cars/{c['id']}.json", out)


if __name__ == "__main__":
    write_engines()
    write_calibrations()
    write_templates()
    write_cars()
    import gen_catalogs  # noqa: E402  (DTC catalog, failure modes, parts, customers, scenarios, jobs)
    gen_catalogs.main(BASE, dump)
    print("Contenido generado en", BASE)
