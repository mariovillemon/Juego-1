using System;
using System.Collections.Generic;
using Garage.Sim.Faults;

namespace Garage.Sim.Electrical
{
    /// <summary>Electrical topology of a component circuit.</summary>
    public enum CircuitTopology
    {
        /// <summary>Two wire NTC with ECU pull-up (ECT, IAT). Pins: signal, ground.</summary>
        Thermistor,
        /// <summary>5 V ratiometric 3-wire sensor (MAP, TPS, APP, rail pressure). Pins: ref, signal, ground.</summary>
        Ratiometric,
        /// <summary>12 V powered sensor with analog output (MAF, wideband interface). Pins: supply, signal, ground.</summary>
        Powered,
        /// <summary>Two wire self generating sensor (VR crank sensor, knock). Pins: signal, signal_low.</summary>
        Generator,
        /// <summary>Hall effect open collector (CMP). Pins: ref, signal, ground.</summary>
        Hall,
        /// <summary>Heated zirconia sensor. Pins: signal, signal_low, heater_supply, heater_control.</summary>
        HeatedOxygen,
        /// <summary>Load fed from a fuse and switched to ground by the ECU (injector, coil, solenoid). Pins: supply, control.</summary>
        LowSideLoad,
    }

    /// <summary>Pin information for wiring diagrams.</summary>
    public sealed class PinInfo
    {
        /// <summary>Creates pin info.</summary>
        public PinInfo(string role, string ecuPin, int componentPin, string wireColor)
        {
            Role = role;
            EcuPin = ecuPin;
            ComponentPin = componentPin;
            WireColor = wireColor;
        }

        /// <summary>Pin role (signal, ground, ref...).</summary>
        public string Role { get; }

        /// <summary>ECU connector cavity or fuse box terminal (e.g. "A23", "F12").</summary>
        public string EcuPin { get; }

        /// <summary>Pin number on the component connector.</summary>
        public int ComponentPin { get; }

        /// <summary>Wire color code (DIN style abbreviations in Spanish: NE, BL, RJ, VE, AM, AZ, MA, GR, VI, NA).</summary>
        public string WireColor { get; }
    }

    /// <summary>
    /// Physical circuit of one component: ECU pins, wires, connector and the component itself.
    /// The owner sets the drive state (key, battery, sensor value...) and reads solved voltages.
    /// </summary>
    public sealed class Circuit
    {
        /// <summary>Nominal wire resistance (ohms).</summary>
        public const double WireOhms = 0.03;

        /// <summary>Nominal connector contact resistance (ohms).</summary>
        public const double ContactOhms = 0.005;

        /// <summary>ECU driver on resistance (ohms).</summary>
        public const double DriverOnOhms = 0.15;

        private readonly List<PinInfo> _pins;
        private ResistiveNetwork? _net;
        private int _faultSignature = int.MinValue;
        private bool _structureDirty = true;
        private double[] _v = Array.Empty<double>();

        private int _bSensor = -1;
        private int _bSensor2 = -1;
        private int _bRef = -1;
        private int _bSupply = -1;
        private int _bDriver = -1;
        private int _bBattery = -1;

        /// <summary>Creates a circuit.</summary>
        public Circuit(string componentId, CircuitTopology topology, IList<PinInfo> pins)
        {
            ComponentId = componentId;
            Topology = topology;
            _pins = new List<PinInfo>(pins);
        }

        /// <summary>Owning component id.</summary>
        public string ComponentId { get; }

        /// <summary>Topology.</summary>
        public CircuitTopology Topology { get; }

        /// <summary>Pins.</summary>
        public IReadOnlyList<PinInfo> Pins => _pins;

        /// <summary>Ignition key on.</summary>
        public bool KeyOn { get; set; } = true;

        /// <summary>Battery/system voltage.</summary>
        public double BatteryVolts { get; set; } = 12.6;

        /// <summary>Feeding fuse intact.</summary>
        public bool FuseOk { get; set; } = true;

        /// <summary>Component connector plugged in.</summary>
        public bool ConnectorConnected
        {
            get => _connected;
            set
            {
                if (_connected != value)
                {
                    _connected = value;
                    _structureDirty = true;
                }
            }
        }

        private bool _connected = true;

        /// <summary>ECU driver transistor on (low side loads, heater).</summary>
        public bool DriverOn { get; set; }

        /// <summary>
        /// Topology dependent drive value: thermistor ohms, ratiometric ratio (0..1), powered/generator/oxygen source volts,
        /// low side load ohms.
        /// </summary>
        public double SensorValue { get; set; }

        /// <summary>Secondary value: source internal resistance (generator/oxygen) or heater ohms (oxygen).</summary>
        public double SensorValue2 { get; set; } = 100;

        /// <summary>Internal resistance of the sensor source (ohms).</summary>
        public double SourceOhms { get; set; } = 100;

        /// <summary>ECU bias voltage on oxygen sensor signal inputs (0.45 V narrowband, 0 V wideband interface).</summary>
        public double BiasVolts { get; set; } = 0.45;

        /// <summary>Hall output transistor on (pulling signal low).</summary>
        public bool HallOn { get; set; }

        /// <summary>Has a pin of the given role.</summary>
        public bool HasPin(string role) => _pins.Exists(p => p.Role == role);

        /// <summary>Gets a pin by role.</summary>
        public PinInfo? Pin(string role) => _pins.Find(p => p.Role == role);

        /// <summary>Marks the network for rebuild (after repairs).</summary>
        public void Invalidate() => _structureDirty = true;

        /// <summary>Default pin roles of a topology.</summary>
        public static string[] RolesOf(CircuitTopology t)
        {
            switch (t)
            {
                case CircuitTopology.Thermistor: return new[] { "signal", "ground" };
                case CircuitTopology.Ratiometric: return new[] { "ref", "signal", "ground" };
                case CircuitTopology.Powered: return new[] { "supply", "signal", "ground" };
                case CircuitTopology.Generator: return new[] { "signal", "signal_low" };
                case CircuitTopology.Hall: return new[] { "ref", "signal", "ground" };
                case CircuitTopology.HeatedOxygen: return new[] { "signal", "signal_low", "heater_supply", "heater_control" };
                default: return new[] { "supply", "control" };
            }
        }

        private static int Signature(FaultSet faults, string id)
        {
            unchecked
            {
                int h = 17;
                foreach (FaultInstance f in faults.ActiveOn(id))
                {
                    h = h * 31 + (int)f.Effect;
                    h = h * 31 + f.Pin.GetHashCode();
                    h = h * 31 + (int)(f.Magnitude * 1000);
                }

                return h;
            }
        }

        private void Build(FaultSet faults)
        {
            var n = new ResistiveNetwork();
            _bBattery = n.AddSource("BAT", "GND", BatteryVolts, 0.01, "battery");
            foreach (PinInfo p in _pins)
            {
                string r = p.Role;
                double wireR = WireOhms + PinSum(faults, EffectKind.WireHighResistance, r);
                bool open = faults.Has(ComponentId, EffectKind.WireOpen, r);
                n.AddResistor("E:" + r, "H:" + r, open ? ResistiveNetwork.Open : wireR, "wire:" + r);
                double contact = ContactOhms + PinSum(faults, EffectKind.ConnectorCorrosion, r);
                n.AddResistor("H:" + r, "C:" + r, _connected ? contact : ResistiveNetwork.Open, "connector:" + r);
                if (faults.Has(ComponentId, EffectKind.WireShortGround, r))
                {
                    n.AddResistor("H:" + r, "GND", 0.05, "short_gnd:" + r);
                }

                if (faults.Has(ComponentId, EffectKind.WireShortPower, r))
                {
                    n.AddResistor("H:" + r, "BAT", 0.05, "short_bat:" + r);
                }
            }

            _bSensor = _bSensor2 = _bRef = _bSupply = _bDriver = -1;
            switch (Topology)
            {
                case CircuitTopology.Thermistor:
                    _bRef = n.AddSource("E:signal", "GND", 5, Components.SensorCurves.NtcPullUp, "ecu_pullup");
                    n.AddResistor("E:ground", "GND", 0.02, "ecu_ground");
                    _bSensor = n.AddResistor("C:signal", "C:ground", 2000, "ntc");
                    break;
                case CircuitTopology.Ratiometric:
                case CircuitTopology.Hall:
                    _bRef = n.AddSource("E:ref", "GND", 5, 0.5, "ecu_5v_ref");
                    if (Topology == CircuitTopology.Hall)
                    {
                        n.AddSource("E:signal", "GND", 5, 10000, "ecu_pullup");
                        _bSensor = n.AddResistor("C:signal", "C:ground", ResistiveNetwork.Open, "hall_switch");
                    }
                    else
                    {
                        n.AddResistor("E:signal", "GND", 100000, "ecu_pulldown");
                        _bSensor = n.AddSource("C:signal", "C:ground", 0, 100, "sensor_out");
                    }

                    n.AddResistor("E:ground", "GND", 0.02, "ecu_ground");
                    n.AddResistor("C:ref", "C:ground", 4700, "sensor_load");
                    break;
                case CircuitTopology.Powered:
                    _bSupply = n.AddSource("E:supply", "GND", BatteryVolts, 0.1, "fused_supply");
                    n.AddResistor("E:signal", "GND", 100000, "ecu_pulldown");
                    n.AddResistor("E:ground", "GND", 0.02, "ecu_ground");
                    n.AddResistor("C:supply", "C:ground", 180, "sensor_heater");
                    _bSensor = n.AddSource("C:signal", "C:ground", 0, 100, "sensor_out");
                    break;
                case CircuitTopology.Generator:
                    n.AddResistor("E:signal", "E:signal_low", 20000, "ecu_input");
                    n.AddResistor("E:signal_low", "GND", 0.02, "ecu_ground");
                    _bSensor = n.AddSource("C:signal", "C:signal_low", 0, 800, "coil");
                    break;
                case CircuitTopology.HeatedOxygen:
                    _bRef = n.AddSource("E:signal", "GND", 0.45, 1.0e6, "ecu_bias");
                    n.AddResistor("E:signal_low", "GND", 0.02, "ecu_ground");
                    _bSensor = n.AddSource("C:signal", "C:signal_low", 0.45, 100, "nernst_cell");
                    _bSupply = n.AddSource("E:heater_supply", "GND", BatteryVolts, 0.1, "fused_supply");
                    _bSensor2 = n.AddResistor("C:heater_supply", "C:heater_control", 4, "heater");
                    n.AddResistor("E:heater_control", "GND", 22000, "ecu_diag_pulldown");
                    _bDriver = n.AddResistor("E:heater_control", "GND", ResistiveNetwork.Open, "ecu_driver");
                    break;
                case CircuitTopology.LowSideLoad:
                    _bSupply = n.AddSource("E:supply", "GND", BatteryVolts, 0.05, "fused_supply");
                    _bSensor = n.AddResistor("C:supply", "C:control", 15, "load");
                    n.AddResistor("E:control", "GND", 22000, "ecu_diag_pulldown");
                    _bDriver = n.AddResistor("E:control", "GND", ResistiveNetwork.Open, "ecu_driver");
                    break;
            }

            _net = n;
            _structureDirty = false;
        }

        private double PinSum(FaultSet faults, EffectKind kind, string role)
        {
            double s = 0;
            foreach (FaultInstance f in faults.ActiveOn(ComponentId))
            {
                if (f.Effect == kind && (f.Pin == role || string.IsNullOrEmpty(f.Pin)))
                {
                    s += f.Magnitude;
                }
            }

            return s;
        }

        private void Prepare(FaultSet faults)
        {
            int sig = Signature(faults, ComponentId);
            if (_structureDirty || sig != _faultSignature || _net == null)
            {
                _faultSignature = sig;
                Build(faults);
            }

            ResistiveNetwork n = _net!;
            n.SetVoltage(_bBattery, BatteryVolts);
            double feed = KeyOn && FuseOk ? BatteryVolts : 0;
            if (_bSupply >= 0)
            {
                n.SetVoltage(_bSupply, feed);
                n.SetResistance(_bSupply, FuseOk ? 0.08 : ResistiveNetwork.Open);
            }

            switch (Topology)
            {
                case CircuitTopology.Thermistor:
                    n.SetVoltage(_bRef, KeyOn ? 5 : 0);
                    n.SetResistance(_bSensor, Math.Max(1, SensorValue));
                    break;
                case CircuitTopology.Ratiometric:
                    n.SetVoltage(_bRef, KeyOn ? 5 : 0);
                    break;
                case CircuitTopology.Hall:
                    n.SetVoltage(_bRef, KeyOn ? 5 : 0);
                    n.SetResistance(_bSensor, HallOn ? 20 : ResistiveNetwork.Open);
                    break;
                case CircuitTopology.Generator:
                    n.SetVoltage(_bSensor, SensorValue);
                    n.SetResistance(_bSensor, SourceOhms);
                    break;
                case CircuitTopology.HeatedOxygen:
                    n.SetVoltage(_bRef, KeyOn ? BiasVolts : 0);
                    n.SetVoltage(_bSensor, SensorValue);
                    n.SetResistance(_bSensor, SourceOhms);
                    n.SetResistance(_bSensor2, Math.Max(0.1, SensorValue2));
                    n.SetResistance(_bDriver, KeyOn && DriverOn ? DriverOnOhms : ResistiveNetwork.Open);
                    break;
                case CircuitTopology.LowSideLoad:
                    n.SetResistance(_bSensor, Math.Max(0.05, SensorValue));
                    n.SetResistance(_bDriver, KeyOn && DriverOn ? DriverOnOhms : ResistiveNetwork.Open);
                    break;
            }
        }

        /// <summary>Solves the circuit for the current state and caches node voltages.</summary>
        public void Solve(FaultSet faults)
        {
            Prepare(faults);
            ResistiveNetwork n = _net!;
            if (Topology == CircuitTopology.Ratiometric || Topology == CircuitTopology.Powered)
            {
                // Dependent source: output depends on the supply seen at the sensor pins. Fixed point iterations.
                int cs = n.Node(Topology == CircuitTopology.Ratiometric ? "C:ref" : "C:supply");
                int cg = n.Node("C:ground");
                double supply = Topology == CircuitTopology.Ratiometric ? 5 : BatteryVolts;
                for (int it = 0; it < 4; it++)
                {
                    double outV;
                    if (Topology == CircuitTopology.Ratiometric)
                    {
                        outV = Math.Max(0, supply) * SensorValue;
                    }
                    else
                    {
                        outV = supply > 9 ? SensorValue : SensorValue * Math.Max(0, supply) / 9.0;
                    }

                    n.SetVoltage(_bSensor, outV);
                    _v = n.Solve();
                    supply = _v[cs] - _v[cg];
                }
            }
            else
            {
                _v = n.Solve();
            }
        }

        /// <summary>Solved voltage of a node (0 if node unknown).</summary>
        public double Voltage(string node)
        {
            if (_net == null || !_net.HasNode(node))
            {
                return 0;
            }

            int i = _net.Node(node);
            return i < _v.Length ? _v[i] : 0;
        }

        /// <summary>Voltage at the ECU side of a pin.</summary>
        public double EcuPinVoltage(string role) => Voltage("E:" + role);

        /// <summary>Current through the ECU driver (amps) when on.</summary>
        public double DriverCurrent(string role = "control") => Voltage("E:" + role) / DriverOnOhms;

        /// <summary>Measures voltage between two nodes with a 10 MΩ meter.</summary>
        public double MeasureVoltage(FaultSet faults, string nodeA, string nodeB)
        {
            Prepare(faults);
            ResistiveNetwork n = _net!;
            if (!n.HasNode(nodeA) || !n.HasNode(nodeB))
            {
                return 0;
            }

            int a = n.Node(nodeA);
            int b = n.Node(nodeB);
            double[] v = n.Solve(meterA: a, meterB: b, meterOhms: 10e6);
            return v[a] - v[b];
        }

        /// <summary>
        /// Measures resistance between two nodes by injecting 1 mA. If the circuit is powered the reading
        /// is corrupted by the circuit's own sources, as with a real ohmmeter. Returns +infinity for open.
        /// </summary>
        public double MeasureResistance(FaultSet faults, string nodeA, string nodeB)
        {
            Prepare(faults);
            ResistiveNetwork n = _net!;
            if (!n.HasNode(nodeA) || !n.HasNode(nodeB))
            {
                return double.PositiveInfinity;
            }

            int a = n.Node(nodeA);
            int b = n.Node(nodeB);
            const double amps = 1e-3;
            double[] baseV = n.Solve();
            double[] v = n.Solve(a, b, amps);
            double r = ((v[a] - v[b]) - (baseV[a] - baseV[b])) / amps;
            bool powered = Math.Abs(baseV[a] - baseV[b]) > 0.05;
            if (powered)
            {
                r += (baseV[a] - baseV[b]) / amps;
            }

            return r > 5e7 ? double.PositiveInfinity : r;
        }
    }
}
