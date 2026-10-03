using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Garage.Sim.Electrical;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Tools
{
    /// <summary>Multimeter function.</summary>
    public enum MeterMode
    {
        /// <summary>DC volts.</summary>
        DcVolts,
        /// <summary>Resistance.</summary>
        Ohms,
        /// <summary>Continuity (beeper).</summary>
        Continuity,
    }

    /// <summary>Multimeter range (manual ranges as on a classic DMM; Auto picks the best).</summary>
    public enum MeterRange
    {
        /// <summary>Autorange.</summary>
        Auto,
        /// <summary>200 mV.</summary>
        V200m,
        /// <summary>2 V.</summary>
        V2,
        /// <summary>20 V.</summary>
        V20,
        /// <summary>200 V.</summary>
        V200,
        /// <summary>200 Ω.</summary>
        R200,
        /// <summary>2 kΩ.</summary>
        R2k,
        /// <summary>20 kΩ.</summary>
        R20k,
        /// <summary>200 kΩ.</summary>
        R200k,
        /// <summary>2 MΩ.</summary>
        R2M,
        /// <summary>20 MΩ.</summary>
        R20M,
    }

    /// <summary>
    /// Digital multimeter. Probes are test points "component:side:pin" where side is ecu (back-probing the ECU connector),
    /// harness (harness side of the component connector) or comp (component pins); plus "gnd" (chassis) and "bat" (battery +).
    /// Readings come from the actual circuit network, so wrong ranges overflow and live circuits corrupt ohm readings.
    /// </summary>
    public sealed class Multimeter
    {
        private readonly DiagnosticSession _s;

        internal Multimeter(DiagnosticSession s)
        {
            _s = s;
        }

        /// <summary>Selected function.</summary>
        public MeterMode Mode { get; set; } = MeterMode.DcVolts;

        /// <summary>Selected range.</summary>
        public MeterRange Range { get; set; } = MeterRange.Auto;

        private static string NodeOf(string side, string pin)
        {
            switch (side.ToLowerInvariant())
            {
                case "ecu": return "E:" + pin;
                case "harness":
                case "h": return "H:" + pin;
                default: return "C:" + pin;
            }
        }

        /// <summary>Parses a probe position into (componentId or null for global, node name).</summary>
        public static bool TryParseProbe(string probe, out string? component, out string node)
        {
            component = null;
            node = "";
            string p = probe.Trim().ToLowerInvariant();
            if (p == "gnd" || p == "masa" || p == "chassis")
            {
                node = "GND";
                return true;
            }

            if (p == "bat" || p == "bat+" || p == "battery")
            {
                node = "BAT";
                return true;
            }

            string[] parts = probe.Split(':');
            if (parts.Length != 3)
            {
                return false;
            }

            component = parts[0];
            node = NodeOf(parts[1], parts[2]);
            return true;
        }

        /// <summary>Measures between two probes with the current mode and range.</summary>
        public ToolResult Measure(string redProbe, string blackProbe)
        {
            if (!TryParseProbe(redProbe, out string? compA, out string nodeA) || !TryParseProbe(blackProbe, out string? compB, out string nodeB))
            {
                return new ToolResult("Punta mal colocada. Formato: componente:lado:pin (lado = ecu|harness|comp) o gnd/bat.", 0, false);
            }

            string? comp = compA ?? compB;
            if (compA != null && compB != null && !string.Equals(compA, compB, StringComparison.OrdinalIgnoreCase))
            {
                return _s.Charge(new ToolResult("Las puntas están en circuitos distintos: mida respecto a masa (gnd) o batería (bat).", 1, false), "Multímetro: medida entre circuitos");
            }

            Car car = _s.Car;
            double value;
            if (comp == null)
            {
                // Both global: battery voltage or zero.
                value = Mode == MeterMode.DcVolts ? (nodeA == nodeB ? 0 : (nodeA == "BAT" ? car.BatteryVolts : -car.BatteryVolts)) : (nodeA == nodeB ? 0 : 0.01);
            }
            else
            {
                Circuit? c = car.CircuitOf(comp);
                if (c == null)
                {
                    return _s.Charge(new ToolResult($"'{comp}' no tiene circuito eléctrico accesible.", 1, false), "Multímetro: componente sin circuito");
                }

                car.RefreshCircuits();
                string pinA = nodeA.Length > 2 ? nodeA.Substring(2) : "";
                string pinB = nodeB.Length > 2 ? nodeB.Substring(2) : "";
                if ((nodeA != "GND" && nodeA != "BAT" && !c.HasPin(pinA)) || (nodeB != "GND" && nodeB != "BAT" && !c.HasPin(pinB)))
                {
                    return _s.Charge(new ToolResult($"Ese pin no existe en {comp}. Pines: {string.Join(", ", PinRoles(c))}", 1, false), "Multímetro: pin inexistente");
                }

                if (Mode == MeterMode.DcVolts)
                {
                    value = c.MeasureVoltage(car.Faults, nodeA, nodeB);
                }
                else
                {
                    value = c.MeasureResistance(car.Faults, nodeA, nodeB);
                    if (!double.IsInfinity(value))
                    {
                        value += 0.2; // test lead resistance
                    }
                }
            }

            string display = Format(value, out bool beep);
            string label = Mode == MeterMode.DcVolts ? "V DC" : (Mode == MeterMode.Ohms ? "Ω" : "continuidad");
            var r = new ToolResult($"[{RangeLabel()}] {display}{(beep ? "  *BIP*" : "")}   ({label}, roja {redProbe} / negra {blackProbe})", TimeCosts.Measurement)
            {
                Value = value,
            };
            return _s.Charge(r, $"Multímetro {label}: {redProbe} → {blackProbe} = {display}");
        }

        private static IEnumerable<string> PinRoles(Circuit c)
        {
            foreach (PinInfo p in c.Pins)
            {
                yield return p.Role;
            }
        }

        private string RangeLabel()
        {
            switch (Range)
            {
                case MeterRange.V200m: return "200mV";
                case MeterRange.V2: return "2V";
                case MeterRange.V20: return "20V";
                case MeterRange.V200: return "200V";
                case MeterRange.R200: return "200Ω";
                case MeterRange.R2k: return "2kΩ";
                case MeterRange.R20k: return "20kΩ";
                case MeterRange.R200k: return "200kΩ";
                case MeterRange.R2M: return "2MΩ";
                case MeterRange.R20M: return "20MΩ";
                default: return "AUTO";
            }
        }

        /// <summary>Formats a reading like a 3½ digit display for the selected range.</summary>
        public string Format(double value, out bool beep)
        {
            beep = false;
            if (Mode == MeterMode.Continuity)
            {
                beep = !double.IsInfinity(value) && value < 30;
                return double.IsInfinity(value) || value > 199.9 ? "OL" : value.ToString("0.0", CultureInfo.InvariantCulture) + " Ω";
            }

            if (Mode == MeterMode.DcVolts)
            {
                double abs = Math.Abs(value);
                MeterRange r = Range;
                if (r == MeterRange.Auto || r >= MeterRange.R200)
                {
                    r = abs < 0.1999 ? MeterRange.V200m : abs < 1.999 ? MeterRange.V2 : abs < 19.99 ? MeterRange.V20 : MeterRange.V200;
                }

                switch (r)
                {
                    case MeterRange.V200m: return abs > 0.1999 ? "OL" : (value * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " mV";
                    case MeterRange.V2: return abs > 1.999 ? "OL" : value.ToString("0.000", CultureInfo.InvariantCulture) + " V";
                    case MeterRange.V20: return abs > 19.99 ? "OL" : value.ToString("0.00", CultureInfo.InvariantCulture) + " V";
                    default: return abs > 199.9 ? "OL" : value.ToString("0.0", CultureInfo.InvariantCulture) + " V";
                }
            }

            if (double.IsInfinity(value) || double.IsNaN(value))
            {
                return "OL";
            }

            if (value < 0)
            {
                return "-" + Math.Abs(value).ToString("0.0", CultureInfo.InvariantCulture) + " Ω (?)";
            }

            MeterRange rr = Range;
            if (rr == MeterRange.Auto || rr < MeterRange.R200)
            {
                rr = value < 199.9 ? MeterRange.R200 : value < 1999 ? MeterRange.R2k : value < 19990 ? MeterRange.R20k : value < 199900 ? MeterRange.R200k : value < 1999000 ? MeterRange.R2M : MeterRange.R20M;
            }

            switch (rr)
            {
                case MeterRange.R200: return value > 199.9 ? "OL" : value.ToString("0.0", CultureInfo.InvariantCulture) + " Ω";
                case MeterRange.R2k: return value > 1999 ? "OL" : (value / 1000).ToString("0.000", CultureInfo.InvariantCulture) + " kΩ";
                case MeterRange.R20k: return value > 19990 ? "OL" : (value / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " kΩ";
                case MeterRange.R200k: return value > 199900 ? "OL" : (value / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " kΩ";
                case MeterRange.R2M: return value > 1999000 ? "OL" : (value / 1e6).ToString("0.000", CultureInfo.InvariantCulture) + " MΩ";
                default: return value > 19.99e6 ? "OL" : (value / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " MΩ";
            }
        }

        /// <summary>Wiring diagram of a component: pins, ECU cavities and colours.</summary>
        public ToolResult WiringDiagram(string componentId)
        {
            Circuit? c = _s.Car.CircuitOf(componentId);
            Components.Component? comp = _s.Car.Parts.Get(componentId);
            if (c == null || comp == null)
            {
                return _s.Charge(new ToolResult($"No hay esquema eléctrico para '{componentId}'.", TimeCosts.WiringDiagram, false), "Esquema: no disponible");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Esquema: {comp.Name}  [{c.Topology}]  conector {(c.ConnectorConnected ? "conectado" : "DESCONECTADO")}");
            sb.AppendLine("  Pin  Función          Color    Destino");
            foreach (PinInfo p in c.Pins)
            {
                string dest = p.EcuPin.StartsWith("F", StringComparison.Ordinal) ? $"Caja fusibles {p.EcuPin}" : $"ECU {p.EcuPin}";
                sb.AppendLine($"  {p.ComponentPin,3}  {RoleName(p.Role),-16} {p.WireColor,-8} {dest}");
            }

            sb.AppendLine($"  Puntos de medida: {componentId}:ecu:<pin>, {componentId}:harness:<pin>, {componentId}:comp:<pin>, gnd, bat");
            return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.WiringDiagram), $"Esquema eléctrico {componentId}");
        }

        /// <summary>Spanish pin role name.</summary>
        public static string RoleName(string role)
        {
            switch (role)
            {
                case "signal": return "Señal";
                case "ground": return "Masa sensor";
                case "ref": return "Referencia 5 V";
                case "supply": return "Alimentación";
                case "control": return "Control (ECU)";
                case "signal_low": return "Señal (-)";
                case "heater_supply": return "Alim. calefactor";
                case "heater_control": return "Control calefactor";
                default: return role;
            }
        }

        /// <summary>Unplugs or plugs a component connector.</summary>
        public ToolResult SetConnector(string componentId, bool connected)
        {
            if (_s.Car.CircuitOf(componentId) == null)
            {
                return new ToolResult($"'{componentId}' no tiene conector.", 0, false);
            }

            _s.Car.SetConnector(componentId, connected);
            return _s.Charge(new ToolResult(connected ? $"Conector de {componentId} conectado." : $"Conector de {componentId} desconectado.", TimeCosts.Connector), $"Conector {componentId} {(connected ? "conectado" : "desconectado")}");
        }
    }
}
