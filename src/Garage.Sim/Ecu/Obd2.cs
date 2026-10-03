using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Garage.Sim.Core;

namespace Garage.Sim.Ecu
{
    /// <summary>Definition of a mode 01 PID with its SAE J1979 scaling.</summary>
    public sealed class PidDefinition
    {
        /// <summary>Creates a PID definition.</summary>
        public PidDefinition(int pid, string key, string name, string unit, int bytes, Func<double, byte[]> encode, Func<byte[], double> decode)
        {
            Pid = pid;
            Key = key;
            Name = name;
            Unit = unit;
            Bytes = bytes;
            Encode = encode;
            Decode = decode;
        }

        /// <summary>PID number.</summary>
        public int Pid { get; }

        /// <summary>ECU live data key that feeds it.</summary>
        public string Key { get; }

        /// <summary>Display name (Spanish).</summary>
        public string Name { get; }

        /// <summary>Unit.</summary>
        public string Unit { get; }

        /// <summary>Data bytes.</summary>
        public int Bytes { get; }

        /// <summary>Physical value → bytes.</summary>
        public Func<double, byte[]> Encode { get; }

        /// <summary>Bytes → physical value.</summary>
        public Func<byte[], double> Decode { get; }
    }

    /// <summary>A decoded OBD reading with the raw response frame.</summary>
    public sealed class PidReading
    {
        /// <summary>Creates a reading.</summary>
        public PidReading(PidDefinition def, double value, byte[] raw)
        {
            Definition = def;
            Value = value;
            Raw = raw;
        }

        /// <summary>Definition.</summary>
        public PidDefinition Definition { get; }

        /// <summary>Decoded value (what a scan tool shows).</summary>
        public double Value { get; }

        /// <summary>Raw data bytes.</summary>
        public byte[] Raw { get; }

        /// <summary>Response frame as an ELM327 would print it, e.g. "41 0C 1A F8".</summary>
        public string Frame
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append("41 ").Append(Definition.Pid.ToString("X2", CultureInfo.InvariantCulture));
                foreach (byte b in Raw)
                {
                    sb.Append(' ').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// OBD-II service implementation over the simulated ECU: mode 01 PIDs with real formulas, mode 02 freeze frame,
    /// mode 03/07/0A codes with J1979 two byte encoding, mode 04 clear and mode 09 VIN.
    /// </summary>
    public static class Obd2
    {
        private static byte B(double v) => (byte)MathUtil.Clamp(Math.Round(v), 0, 255);

        private static byte[] W(double v)
        {
            int x = (int)MathUtil.Clamp(Math.Round(v), 0, 65535);
            return new[] { (byte)(x >> 8), (byte)(x & 0xFF) };
        }

        private static int Word(byte[] b) => (b[0] << 8) | b[1];

        /// <summary>Supported PID definitions (excluding bitmask PIDs 00/20/40 which are computed).</summary>
        public static IReadOnlyList<PidDefinition> Pids { get; } = new List<PidDefinition>
        {
            new PidDefinition(0x03, "fuel_status", "Estado del sistema de combustible", "", 2, v => new[] { B(v), (byte)0 }, b => b[0]),
            new PidDefinition(0x04, "load", "Carga calculada", "%", 1, v => new[] { B(v * 255 / 100) }, b => b[0] * 100.0 / 255),
            new PidDefinition(0x05, "ect", "Temperatura refrigerante", "°C", 1, v => new[] { B(v + 40) }, b => b[0] - 40),
            new PidDefinition(0x06, "stft", "Corrección corto plazo B1 (STFT)", "%", 1, v => new[] { B(v * 128 / 100 + 128) }, b => (b[0] - 128) * 100.0 / 128),
            new PidDefinition(0x07, "ltft", "Corrección largo plazo B1 (LTFT)", "%", 1, v => new[] { B(v * 128 / 100 + 128) }, b => (b[0] - 128) * 100.0 / 128),
            new PidDefinition(0x0A, "fuel_rail_kpa", "Presión de combustible (relativa)", "kPa", 1, v => new[] { B(v / 3) }, b => b[0] * 3.0),
            new PidDefinition(0x0B, "map", "Presión absoluta colector (MAP)", "kPa", 1, v => new[] { B(v) }, b => b[0]),
            new PidDefinition(0x0C, "rpm", "Régimen motor", "rpm", 2, v => W(v * 4), b => Word(b) / 4.0),
            new PidDefinition(0x0D, "speed", "Velocidad del vehículo", "km/h", 1, v => new[] { B(v) }, b => b[0]),
            new PidDefinition(0x0E, "timing", "Avance de encendido (cil. 1)", "°APMS", 1, v => new[] { B((v + 64) * 2) }, b => b[0] / 2.0 - 64),
            new PidDefinition(0x0F, "iat", "Temperatura aire de admisión", "°C", 1, v => new[] { B(v + 40) }, b => b[0] - 40),
            new PidDefinition(0x10, "maf", "Caudal de aire (MAF)", "g/s", 2, v => W(v * 100), b => Word(b) / 100.0),
            new PidDefinition(0x11, "tps", "Posición de mariposa", "%", 1, v => new[] { B(v * 255 / 100) }, b => b[0] * 100.0 / 255),
            new PidDefinition(0x14, "o2_b1s1_v", "Sonda B1S1 tensión", "V", 2, v => new[] { B(v * 200), (byte)0xFF }, b => b[0] / 200.0),
            new PidDefinition(0x15, "o2_b1s2_v", "Sonda B1S2 tensión", "V", 2, v => new[] { B(v * 200), (byte)0xFF }, b => b[0] / 200.0),
            new PidDefinition(0x1C, "obd_standard", "Norma OBD", "", 1, v => new[] { B(v) }, b => b[0]),
            new PidDefinition(0x1F, "run_time", "Tiempo desde arranque", "s", 2, v => W(v), b => Word(b)),
            new PidDefinition(0x21, "distance_mil", "Distancia con MIL encendida", "km", 2, v => W(v), b => Word(b)),
            new PidDefinition(0x23, "rail_gauge_kpa", "Presión de rail (relativa)", "kPa", 2, v => W(v / 10), b => Word(b) * 10.0),
            new PidDefinition(0x24, "lambda_b1s1", "Sonda B1S1 banda ancha (lambda)", "λ", 4, v =>
            {
                byte[] l = W(v * 32768);
                byte[] u = W(SensorVoltageForLambda(v) * 8192);
                return new[] { l[0], l[1], u[0], u[1] };
            }, b => Word(b) * 2.0 / 65536),
            new PidDefinition(0x31, "distance_since_clear", "Distancia desde borrado", "km", 2, v => W(v), b => Word(b)),
            new PidDefinition(0x33, "baro", "Presión barométrica", "kPa", 1, v => new[] { B(v) }, b => b[0]),
            new PidDefinition(0x42, "battery_v", "Tensión del módulo de control", "V", 2, v => W(v * 1000), b => Word(b) / 1000.0),
            new PidDefinition(0x43, "abs_load", "Carga absoluta", "%", 2, v => W(v * 255 / 100), b => Word(b) * 100.0 / 255),
            new PidDefinition(0x44, "lambda_target", "Lambda solicitada", "λ", 2, v => W(v * 32768), b => Word(b) * 2.0 / 65536),
            new PidDefinition(0x46, "ambient", "Temperatura ambiente", "°C", 1, v => new[] { B(v + 40) }, b => b[0] - 40),
            new PidDefinition(0x49, "app", "Posición del pedal D", "%", 1, v => new[] { B(v * 255 / 100) }, b => b[0] * 100.0 / 255),
            new PidDefinition(0x4C, "throttle_cmd", "Mariposa solicitada", "%", 1, v => new[] { B(v * 255 / 100) }, b => b[0] * 100.0 / 255),
            new PidDefinition(0x4E, "time_since_clear_min", "Tiempo desde borrado de códigos", "min", 2, v => W(v), b => Word(b)),
            new PidDefinition(0x51, "fuel_type", "Tipo de combustible", "", 1, v => new[] { B(v) }, b => b[0]),
        };

        private static double SensorVoltageForLambda(double lambda) => Components.SensorCurves.WidebandVoltage(lambda);

        /// <summary>Gets a PID definition.</summary>
        public static PidDefinition? Find(int pid)
        {
            foreach (PidDefinition p in Pids)
            {
                if (p.Pid == pid)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>Finds a PID by live key.</summary>
        public static PidDefinition? FindByKey(string key)
        {
            foreach (PidDefinition p in Pids)
            {
                if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>Builds the value source for PIDs not stored in ECU live data.</summary>
        private static bool TryValue(EngineControlUnit ecu, Vehicle.Car car, string key, out double v)
        {
            switch (key)
            {
                case "obd_standard": v = 6; return true; // EOBD
                case "distance_mil": v = ecu.Dtcs.DistanceWithMilKm; return true;
                case "distance_since_clear": v = ecu.Dtcs.TimeSinceClearS / 3600.0 * 40; return true;
                case "time_since_clear_min": v = ecu.Dtcs.TimeSinceClearS / 60.0; return true;
                case "ambient": v = car.Environment.AmbientC; return true;
                case "fuel_type": v = car.Definition.Engine.IsDiesel ? 4 : 1; return true;
                case "rail_gauge_kpa": return ecu.Live.TryGetValue("fuel_rail_kpa", out v);
            }

            return ecu.Live.TryGetValue(key, out v) && !double.IsNaN(v);
        }

        /// <summary>True if the vehicle supports a PID (has the sensor).</summary>
        public static bool Supports(Vehicle.Car car, int pid)
        {
            Vehicle.ComponentRegistry p = car.Parts;
            switch (pid)
            {
                case 0x10: return p.Find(Components.ComponentKind.MafSensor) != null;
                case 0x11: return p.Find(Components.ComponentKind.TpsSensor) != null;
                case 0x14: return p.Find(Components.ComponentKind.O2Narrowband) != null;
                case 0x15: return p.Find(Components.ComponentKind.O2Downstream) != null;
                case 0x24: return p.Find(Components.ComponentKind.O2Wideband) != null;
                case 0x0A: return p.Find(Components.ComponentKind.FuelPressureSensor) != null && !car.Definition.Engine.IsDiesel;
                case 0x23: return p.Find(Components.ComponentKind.FuelPressureSensor) != null && car.Definition.Engine.IsDiesel;
                case 0x0E: return !car.Definition.Engine.IsDiesel;
                case 0x06:
                case 0x07: return !car.Definition.Engine.IsDiesel;
                default: return Find(pid) != null;
            }
        }

        /// <summary>Mode 01 request for one PID. Returns null if not supported.</summary>
        public static PidReading? ReadPid(Vehicle.Car car, int pid)
        {
            PidDefinition? def = Find(pid);
            if (def == null || !Supports(car, pid) || !TryValue(car.Ecu, car, def.Key, out double v))
            {
                return null;
            }

            byte[] raw = def.Encode(v);
            return new PidReading(def, def.Decode(raw), raw);
        }

        /// <summary>Supported PID bitmask for a range base (0x00, 0x20, 0x40): 4 bytes as J1979.</summary>
        public static byte[] SupportedMask(Vehicle.Car car, int basePid)
        {
            uint mask = 0;
            for (int i = 1; i <= 32; i++)
            {
                int pid = basePid + i;
                bool supported = (pid == 0x01 || Find(pid) != null) && Supports(car, pid);
                if (i == 32 && basePid < 0x40)
                {
                    supported = true; // next range available
                }

                if (supported)
                {
                    mask |= 1u << (32 - i);
                }
            }

            return new[] { (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask };
        }

        /// <summary>PID 01 (monitor status since DTCs cleared) four data bytes.</summary>
        public static byte[] MonitorStatus(Vehicle.Car car)
        {
            EngineControlUnit ecu = car.Ecu;
            int count = Math.Min(127, ecu.Dtcs.ConfirmedCodes().Count);
            byte a = (byte)((ecu.Dtcs.MilOn ? 0x80 : 0) | count);
            Readiness r = ecu.Readiness;
            byte bb = 0;
            if (car.Definition.Engine.IsDiesel)
            {
                bb |= 0x08;
            }

            ReadinessMonitor[] cont = { ReadinessMonitor.Misfire, ReadinessMonitor.FuelSystem, ReadinessMonitor.Components };
            for (int i = 0; i < 3; i++)
            {
                if (r.IsSupported(cont[i]))
                {
                    bb |= (byte)(1 << i);
                    if (!r.IsComplete(cont[i]))
                    {
                        bb |= (byte)(1 << (i + 4));
                    }
                }
            }

            ReadinessMonitor[] nonCont = { ReadinessMonitor.Catalyst, ReadinessMonitor.HeatedCatalyst, ReadinessMonitor.Evap, ReadinessMonitor.SecondaryAir, ReadinessMonitor.Egr, ReadinessMonitor.O2Sensor, ReadinessMonitor.O2Heater, ReadinessMonitor.Egr };
            int[] bits = { 0, 1, 2, 3, -1, 5, 6, 7 };
            byte c = 0;
            byte d = 0;
            for (int i = 0; i < nonCont.Length; i++)
            {
                if (bits[i] < 0)
                {
                    continue;
                }

                if (r.IsSupported(nonCont[i]))
                {
                    c |= (byte)(1 << bits[i]);
                    if (!r.IsComplete(nonCont[i]))
                    {
                        d |= (byte)(1 << bits[i]);
                    }
                }
            }

            return new[] { a, bb, c, d };
        }

        /// <summary>Encodes a DTC string ("P0171") into two bytes per SAE J1979.</summary>
        public static byte[] EncodeDtc(string code)
        {
            if (code == null || code.Length != 5)
            {
                throw new ArgumentException("DTC must have 5 characters.", nameof(code));
            }

            int system = code[0] switch { 'P' => 0, 'C' => 1, 'B' => 2, 'U' => 3, _ => throw new ArgumentException("Unknown DTC system.") };
            int d1 = code[1] - '0';
            int rest = int.Parse(code.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int value = (system << 14) | (d1 << 12) | rest;
            return new[] { (byte)(value >> 8), (byte)(value & 0xFF) };
        }

        /// <summary>Decodes two J1979 bytes into a DTC string.</summary>
        public static string DecodeDtc(byte hi, byte lo)
        {
            char sys = "PCBU"[hi >> 6];
            int d1 = (hi >> 4) & 0x3;
            int rest = ((hi & 0x0F) << 8) | lo;
            return sys.ToString() + d1.ToString(CultureInfo.InvariantCulture) + rest.ToString("X3", CultureInfo.InvariantCulture);
        }

        /// <summary>Mode 03/07/0A raw response: "43 01 71 03 00 ..." style frame.</summary>
        public static string CodesFrame(int mode, IList<string> codes)
        {
            var sb = new StringBuilder();
            sb.Append((0x40 + mode).ToString("X2", CultureInfo.InvariantCulture));
            sb.Append(' ').Append(codes.Count.ToString("X2", CultureInfo.InvariantCulture));
            foreach (string c in codes)
            {
                byte[] b = EncodeDtc(c);
                sb.Append(' ').Append(b[0].ToString("X2", CultureInfo.InvariantCulture)).Append(' ').Append(b[1].ToString("X2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>Mode 09 PID 02: VIN.</summary>
        public static string Vin(Vehicle.Car car) => car.Definition.Vin;
    }
}
