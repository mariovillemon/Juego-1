using System;
using System.Collections.Generic;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Ecu;
using Garage.Sim.Electrical;
using Garage.Sim.Engine;
using Garage.Sim.Faults;

namespace Garage.Sim.Vehicle
{
    /// <summary>Ignition key position.</summary>
    public enum KeyPosition
    {
        /// <summary>Off.</summary>
        Off,
        /// <summary>Ignition on (KOEO when engine stopped).</summary>
        On,
        /// <summary>Starter engaged.</summary>
        Crank,
    }

    /// <summary>How the drivetrain is loaded.</summary>
    public enum LoadMode
    {
        /// <summary>Neutral, engine revs freely.</summary>
        Neutral,
        /// <summary>In gear on the road.</summary>
        Road,
        /// <summary>On the inertia dyno (rollers free).</summary>
        DynoInertia,
        /// <summary>On the dyno with the brake holding a target engine rpm.</summary>
        DynoHoldRpm,
    }

    /// <summary>
    /// A simulated car: engine, components, circuits, ECU, CAN, electrical system and drivetrain.
    /// Deterministic for a given seed and input sequence.
    /// </summary>
    public sealed class Car
    {
        /// <summary>Default fixed step (s).</summary>
        public const double DefaultDt = 0.02;

        private readonly Dictionary<string, Circuit> _circuits = new Dictionary<string, Circuit>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _fuseOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _lagState = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProbeResult> _probeCache = new Dictionary<string, ProbeResult>(StringComparer.OrdinalIgnoreCase);
        private readonly DeterministicRandom _noiseRng;
        private KeyPosition _key = KeyPosition.Off;
        private double _vehicleSpeedMs;
        private double _probeTimer;
        private int _chainFlags;

        private struct ProbeResult
        {
            public double VOff;
            public double AmpsOn;
            public double Nominal;
            public bool Energized;
        }

        /// <summary>Creates a car. Use <see cref="CarFactory"/> normally.</summary>
        public Car(CarDefinition definition, EcuCalibration calibration, DtcCatalog catalog, ulong seed)
        {
            Definition = definition;
            Rng = new DeterministicRandom(seed);
            _noiseRng = Rng.Fork(7);
            Parts = new ComponentRegistry();
            Faults = new FaultSet();
            foreach (ComponentDefinition cd in definition.Components)
            {
                var comp = new Component(cd.Id, cd.Kind, cd.Name, cd.Parameters, cd.Cylinder, string.IsNullOrEmpty(cd.VisualSlot) ? cd.Id : cd.VisualSlot)
                {
                    PartId = cd.PartId,
                };
                Parts.Add(comp);
                if (cd.Circuit != null)
                {
                    var circuit = new Circuit(cd.Id, cd.Circuit.Topology, cd.Circuit.Pins);
                    if (cd.Kind == ComponentKind.O2Wideband)
                    {
                        circuit.BiasVolts = 0;
                    }

                    _circuits[cd.Id] = circuit;
                    if (!string.IsNullOrEmpty(cd.Circuit.Fuse))
                    {
                        _fuseOf[cd.Id] = cd.Circuit.Fuse;
                    }
                }

                if (cd.Fuse.Length > 0 && !_fuseOf.ContainsKey(cd.Id))
                {
                    _fuseOf[cd.Id] = cd.Fuse;
                }

                if (cd.Kind == ComponentKind.Relay)
                {
                    if (cd.Id.IndexOf("fan", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        FanRelayId = cd.Id;
                    }
                    else if (cd.Id.IndexOf("pump", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        PumpRelayId = cd.Id;
                    }
                }
            }

            ApplyWear();
            Engine = new EngineModel(definition.Engine, Parts, Faults, Rng.Fork(11));
            Electrical = new ElectricalSystem(this);
            Can = new CanNetwork(this);
            foreach (string m in definition.Modules)
            {
                DtcStore store = m == "ecm" ? null! : new DtcStore(catalog);
                Can.Add(new CanModule(m, CanNetwork.NameFor(m), CanNetwork.LostCodeFor(m), store));
            }

            Ecu = new EngineControlUnit(this, calibration.Clone(), catalog);
            StockCalibration = calibration.Clone();
            ReplaceEcmStore();
            Engine.Soak(Environment.AmbientC, false);
        }

        private void ReplaceEcmStore()
        {
            // ECM module on the bus shares the ECU DTC memory.
            var list = new List<CanModule>(Can.Modules);
            var rebuilt = new CanNetwork(this);
            foreach (CanModule m in list)
            {
                rebuilt.Add(m.Id == "ecm" ? new CanModule("ecm", m.Name, m.LostCommCode, Ecu.Dtcs) : m);
            }

            Can = rebuilt;
        }

        /// <summary>Definition.</summary>
        public CarDefinition Definition { get; }

        /// <summary>Random source (deterministic).</summary>
        public DeterministicRandom Rng { get; }

        /// <summary>Components.</summary>
        public ComponentRegistry Parts { get; }

        /// <summary>Faults.</summary>
        public FaultSet Faults { get; }

        /// <summary>Engine physics.</summary>
        public EngineModel Engine { get; }

        /// <summary>ECU.</summary>
        public EngineControlUnit Ecu { get; }

        /// <summary>Stock calibration (for "restore" and comparison).</summary>
        public EcuCalibration StockCalibration { get; }

        /// <summary>Electrical system.</summary>
        public ElectricalSystem Electrical { get; }

        /// <summary>CAN network.</summary>
        public CanNetwork Can { get; private set; }

        /// <summary>Clock.</summary>
        public SimClock Clock { get; } = new SimClock();

        /// <summary>Environment.</summary>
        public EnvironmentState Environment { get; } = new EnvironmentState();

        /// <summary>Accelerator pedal 0..1 (driver input).</summary>
        public double Pedal { get; set; }

        /// <summary>Selected gear (1 based, 0 = neutral).</summary>
        public int Gear { get; set; }

        /// <summary>Drivetrain load mode.</summary>
        public LoadMode Mode { get; set; } = LoadMode.Neutral;

        /// <summary>Target rpm for <see cref="LoadMode.DynoHoldRpm"/>.</summary>
        public double DynoHoldRpm { get; set; } = 2500;

        /// <summary>Dyno roller equivalent mass (kg).</summary>
        public double DynoRollerMassKg { get; set; } = 1200;

        /// <summary>Dyno brake force currently applied (N), read-only output.</summary>
        public double DynoBrakeForceN { get; private set; }

        /// <summary>Wheel force (N) delivered to the road/rollers.</summary>
        public double WheelForceN { get; private set; }

        /// <summary>Fan relay component id.</summary>
        public string FanRelayId { get; } = "";

        /// <summary>Fuel pump relay component id.</summary>
        public string PumpRelayId { get; } = "";

        /// <summary>Key position.</summary>
        public KeyPosition Key
        {
            get => _key;
            set
            {
                bool wasOn = KeyOn;
                _key = value;
                bool nowOn = KeyOn;
                if (!wasOn && nowOn)
                {
                    Ecu.OnKeyOn();
                }
                else if (wasOn && !nowOn)
                {
                    Ecu.OnKeyOff();
                    Engine.State.Rpm = 0;
                }
            }
        }

        /// <summary>Ignition on.</summary>
        public bool KeyOn => _key != KeyPosition.Off;

        /// <summary>Starter engaged.</summary>
        public bool Cranking => _key == KeyPosition.Crank;

        /// <summary>System voltage.</summary>
        public double BatteryVolts => Electrical.SystemVolts;

        /// <summary>Vehicle speed km/h.</summary>
        public double VehicleSpeedKmh => _vehicleSpeedMs * 3.6;

        /// <summary>Odometer km.</summary>
        public double OdometerKm { get; set; }

        /// <summary>All circuits.</summary>
        public IReadOnlyDictionary<string, Circuit> Circuits => _circuits;

        /// <summary>Circuit of a component or null.</summary>
        public Circuit? CircuitOf(string componentId) => _circuits.TryGetValue(componentId, out Circuit c) ? c : null;

        /// <summary>Sensory cues the last step produced.</summary>
        public IReadOnlyList<SensoryCue> Cues => SensoryCueAnalyzer.Analyze(this);

        private void ApplyWear()
        {
            AppearanceDefinition a = Definition.Appearance;
            double kmWear = MathUtil.Clamp01(a.Kilometers / 400000.0);
            double neglect = 1 - a.Maintenance;
            foreach (Component c in Parts.All)
            {
                double baseWear = kmWear * 0.6 + neglect * 0.25;
                switch (c.Kind)
                {
                    case ComponentKind.SparkPlug:
                    case ComponentKind.AirFilter:
                    case ComponentKind.FuelFilter:
                        baseWear = MathUtil.Clamp01(neglect * 0.7 + kmWear * 0.3);
                        break;
                    case ComponentKind.Battery:
                        baseWear = MathUtil.Clamp01(a.AgeYears / 10.0);
                        break;
                }

                c.Health = MathUtil.Clamp(1 - baseWear * 0.5, 0.3, 1);
            }

            OdometerKm = a.Kilometers;
        }

        /// <summary>Adds a fault.</summary>
        public void AddFault(FaultInstance f)
        {
            Faults.Add(f);
            InvalidateCircuits();
        }

        /// <summary>Forces circuit rebuild and probe refresh.</summary>
        public void InvalidateCircuits()
        {
            foreach (Circuit c in _circuits.Values)
            {
                c.Invalidate();
            }

            _probeCache.Clear();
        }

        /// <summary>Sets warm or cold engine state.</summary>
        public void Soak(bool warm)
        {
            Engine.Soak(Environment.AmbientC, warm);
        }

        /// <summary>Starts the engine: cranks up to maxSeconds. Returns true if it runs.</summary>
        public bool Start(double maxSeconds = 4.0)
        {
            if (Key == KeyPosition.Off)
            {
                Key = KeyPosition.On;
                RunFor(2.1);
            }

            Key = KeyPosition.Crank;
            double t = 0;
            while (t < maxSeconds)
            {
                Step(DefaultDt);
                t += DefaultDt;
                if (Engine.State.Rpm > 650)
                {
                    break;
                }
            }

            Key = KeyPosition.On;
            RunFor(1.5);
            return Engine.State.Rpm > 400;
        }

        /// <summary>Turns the key off then on again (ends an OBD drive cycle).</summary>
        public void KeyCycle(double offSeconds = 2)
        {
            Key = KeyPosition.Off;
            RunFor(offSeconds);
        }

        /// <summary>Runs the simulation for a duration at the default step.</summary>
        public void RunFor(double seconds, double dt = DefaultDt)
        {
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++)
            {
                Step(dt);
            }
        }

        /// <summary>One fixed simulation step.</summary>
        public void Step(double dt)
        {
            EngineState es = Engine.State;

            // 1. Fault conditions
            var ctx = new ConditionContext { CoolantC = es.CoolantC, Load = es.RelativeLoad, Rpm = es.Rpm, Vibration = es.Roughness, Dt = dt };
            int before = ActiveSignature();
            Faults.Evaluate(ctx, Rng);
            if (ActiveSignature() != before)
            {
                _probeCache.Clear();
            }

            // 2. Electrical
            bool pumpOn = Ecu.Outputs.FuelPumpRelay;
            Electrical.Step(dt, KeyOn, Cranking, es.Running, es.Rpm, Ecu.Outputs.FanRelay, pumpOn);

            // 3. Sensors → circuits
            UpdateSensorCircuits(dt);

            // 4. ECU
            Ecu.Update(dt);

            // 5. Actuators electrically realised
            EngineCommands cmd = BuildCommands(dt);

            // 6. Engine physics
            double brakeTorque = Engine.Step(dt, cmd, Environment, _vehicleSpeedMs, Cranking);

            // 7. Drivetrain
            IntegrateDrivetrain(dt, brakeTorque);

            // 8. CAN
            Can.Step(dt);

            // 9. Damage chains
            ApplyDamageChains();

            Clock.Advance(dt);
        }

        private int ActiveSignature()
        {
            unchecked
            {
                int h = 0;
                int i = 0;
                foreach (FaultInstance f in Faults.All)
                {
                    if (f.Active)
                    {
                        h += (i + 1) * 7919;
                    }

                    i++;
                }

                return h;
            }
        }

        private static double Span(Component c, ComponentKind k, out double min)
        {
            min = 0;
            switch (k)
            {
                case ComponentKind.EctSensor:
                case ComponentKind.IatSensor:
                    min = -40;
                    return 160;
                case ComponentKind.MapSensor:
                case ComponentKind.BoostSensor:
                    min = c.Param("range_min_kpa", 10);
                    return c.Param("range_max_kpa", 250) - min;
                case ComponentKind.MafSensor:
                    return c.Param("max_gps", 150);
                case ComponentKind.FuelPressureSensor:
                    return c.Param("range_max_kpa", 1000);
                case ComponentKind.O2Narrowband:
                case ComponentKind.O2Downstream:
                    min = 0.7;
                    return 0.3;
                case ComponentKind.O2Wideband:
                    min = 0.7;
                    return 0.8;
                case ComponentKind.CkpSensor:
                    return 1;
                case ComponentKind.KnockSensor:
                    return 1;
                default:
                    return 1;
            }
        }

        private double ApplySignalFaults(Component c, double value)
        {
            double span = Span(c, c.Kind, out double min);
            FaultInstance? stuck = Faults.Find(c.Id, EffectKind.SignalStuck);
            if (stuck != null)
            {
                return min + stuck.Magnitude * span;
            }

            double gain = Faults.Sum(c.Id, EffectKind.SignalGain);
            if (gain != 0)
            {
                value = min + (value - min) * (1 + gain);
            }

            value += Faults.Sum(c.Id, EffectKind.SignalOffset) * span;
            double noise = Faults.Max(c.Id, EffectKind.SignalNoise);
            if (noise > 0)
            {
                value += _noiseRng.Gaussian(0, noise * span);
            }

            double lag = Faults.Max(c.Id, EffectKind.SignalLag);
            double tau = lag + c.Param("time_constant_s", 0.0) + (1 - c.Health) * c.Param("aging_lag_s", 0.0);
            if (!_lagState.TryGetValue(c.Id, out double prev))
            {
                prev = value;
            }

            double filtered = MathUtil.FirstOrder(prev, value, tau, DefaultDt);
            _lagState[c.Id] = filtered;
            return filtered;
        }

        private bool FuseOk(string componentId)
        {
            if (!_fuseOf.TryGetValue(componentId, out string fuse))
            {
                return true;
            }

            return !Faults.Has(fuse, EffectKind.Dead);
        }

        private void UpdateSensorCircuits(double dt)
        {
            EngineState s = Engine.State;
            double vbat = BatteryVolts;
            foreach (KeyValuePair<string, Circuit> kv in _circuits)
            {
                Circuit circuit = kv.Value;
                Component c = Parts.Get(kv.Key)!;
                circuit.KeyOn = KeyOn;
                circuit.BatteryVolts = vbat;
                circuit.FuseOk = FuseOk(c.Id);
                bool dropout = Faults.Has(c.Id, EffectKind.SignalDropout);
                bool dead = Faults.Has(c.Id, EffectKind.Dead);
                double value;
                switch (c.Kind)
                {
                    case ComponentKind.EctSensor:
                        value = ApplySignalFaults(c, s.CoolantLevel < 0.4 ? MathUtil.Lerp(s.CoolantC, s.CoolantC + 15, 0.5) : s.CoolantC);
                        circuit.SensorValue = dropout || dead ? ResistiveNetwork.Open : SensorCurves.NtcResistance(value, c.Param("r25", SensorCurves.NtcR25), c.Param("beta", SensorCurves.NtcBeta));
                        break;
                    case ComponentKind.IatSensor:
                        value = ApplySignalFaults(c, c.Param("post_intercooler", Definition.Engine.IsTurbo ? 1 : 0) > 0.5 ? s.ChargeAirC : s.IntakeAirC);
                        circuit.SensorValue = dropout || dead ? ResistiveNetwork.Open : SensorCurves.NtcResistance(value, c.Param("r25", SensorCurves.NtcR25), c.Param("beta", SensorCurves.NtcBeta));
                        break;
                    case ComponentKind.MapSensor:
                        value = ApplySignalFaults(c, s.ManifoldKpa);
                        circuit.SensorValue = dropout || dead ? 0 : MathUtil.Clamp(SensorCurves.LinearRatio(value, c.Param("range_min_kpa", 10), c.Param("range_max_kpa", 250)), 0.02, 0.985);
                        break;
                    case ComponentKind.BoostSensor:
                        value = ApplySignalFaults(c, s.PreThrottleKpa);
                        circuit.SensorValue = dropout || dead ? 0 : MathUtil.Clamp(SensorCurves.LinearRatio(value, 20, c.Param("range_max_kpa", 300)), 0.02, 0.985);
                        break;
                    case ComponentKind.FuelPressureSensor:
                        value = ApplySignalFaults(c, s.FuelRailKpa);
                        circuit.SensorValue = dropout || dead ? 0 : MathUtil.Clamp(SensorCurves.LinearRatio(value, 0, c.Param("range_max_kpa", 1000)), 0.02, 0.985);
                        break;
                    case ComponentKind.TpsSensor:
                        value = ApplySignalFaults(c, s.ThrottlePosition);
                        circuit.SensorValue = dropout || dead ? 0 : MathUtil.Clamp(SensorCurves.LinearRatio(value, 0, 1), 0.02, 0.985);
                        break;
                    case ComponentKind.AppSensor:
                        value = ApplySignalFaults(c, Pedal);
                        circuit.SensorValue = dropout || dead ? 0 : MathUtil.Clamp(SensorCurves.LinearRatio(value, 0, 1), 0.02, 0.985);
                        break;
                    case ComponentKind.MafSensor:
                        value = ApplySignalFaults(c, s.MafFlowGps);
                        double dirty = Faults.Max(c.Id, EffectKind.Wear);
                        value *= 1 - dirty * 0.35;
                        circuit.SensorValue = dropout || dead ? 0 : SensorCurves.MafVoltage(Math.Max(0, value), c.Param("max_gps", 150));
                        break;
                    case ComponentKind.O2Wideband:
                        {
                            value = ApplySignalFaults(c, s.ExhaustLambda);
                            bool hot = s.O2SensorC > 650;
                            circuit.SensorValue = dropout || dead ? 0 : (hot ? SensorCurves.WidebandVoltage(value) : 1.5);
                            circuit.SourceOhms = 100;
                            circuit.SensorValue2 = Faults.Has(c.Id, EffectKind.InternalShort) ? ResistiveNetwork.Open : c.Param("heater_ohms", 3.3);
                            circuit.DriverOn = Ecu.Outputs.O2HeaterOn;
                            break;
                        }

                    case ComponentKind.O2Narrowband:
                    case ComponentKind.O2Downstream:
                        {
                            bool down = c.Kind == ComponentKind.O2Downstream;
                            double lambda = down ? s.PostCatLambda : s.ExhaustLambda;
                            value = ApplySignalFaults(c, lambda);
                            double temp = down ? s.O2DownstreamC : s.O2SensorC;
                            double contaminated = Faults.Max(c.Id, EffectKind.Wear);
                            double v = SensorCurves.NarrowbandVoltage(value, temp);
                            v = MathUtil.Lerp(v, 0.45, contaminated * 0.8);
                            circuit.SensorValue = dropout || dead ? 0 : v;
                            circuit.SourceOhms = SensorCurves.ZirconiaInternalResistance(temp);
                            circuit.SensorValue2 = Faults.Has(c.Id, EffectKind.InternalShort) ? ResistiveNetwork.Open : c.Param("heater_ohms", 4.5);
                            circuit.DriverOn = Ecu.Outputs.O2HeaterOn;
                            break;
                        }

                    case ComponentKind.CkpSensor:
                        {
                            double gainLoss = Faults.Sum(c.Id, EffectKind.SignalGain);
                            double amp = SensorCurves.CkpPeakVoltage(s.Rpm, c.Param("air_gap_factor", 1.0)) * Math.Max(0, 1 + gainLoss);
                            circuit.SensorValue = dropout || dead ? 0 : amp;
                            circuit.SourceOhms = c.Param("coil_ohms", 850) * (1 - 0.9 * Faults.Max(c.Id, EffectKind.InternalShort));
                            break;
                        }

                    case ComponentKind.KnockSensor:
                        {
                            double ki = 0;
                            foreach (double k in s.CylinderKnock)
                            {
                                ki = Math.Max(ki, k);
                            }

                            double mv = SensorCurves.KnockSensorMillivolts(s.Rpm, ki) * (s.Rpm > 1 ? 1 : 0);
                            if (Faults.Has(c.Id, EffectKind.Weak))
                            {
                                mv *= 1 - Faults.Max(c.Id, EffectKind.Weak);
                            }

                            circuit.SensorValue = dropout || dead ? 0 : mv / 1000.0;
                            circuit.SourceOhms = 4700;
                            break;
                        }

                    case ComponentKind.CmpSensor:
                        circuit.HallOn = false;
                        break;
                    case ComponentKind.Injector:
                    case ComponentKind.IgnitionCoil:
                    case ComponentKind.WastegateSolenoid:
                    case ComponentKind.PurgeValve:
                    case ComponentKind.Relay:
                    case ComponentKind.ElectronicThrottle:
                    case ComponentKind.EgrValve:
                    case ComponentKind.FuelPump:
                    case ComponentKind.CoolingFan:
                    case ComponentKind.GlowPlug:
                        {
                            double ohms = c.Param("ohms", 15) * (1 - 0.9 * Faults.Max(c.Id, EffectKind.InternalShort));
                            if (dead)
                            {
                                ohms = ResistiveNetwork.Open;
                            }

                            circuit.SensorValue = ohms;
                            break;
                        }
                }
            }

            foreach (KeyValuePair<string, Circuit> kv in _circuits)
            {
                Component c = Parts.Get(kv.Key)!;
                if (ComponentKinds.IsSensor(c.Kind))
                {
                    kv.Value.Solve(Faults);
                }
            }

            // O2 heaters warm the element only if current flows.
            Component? up = Parts.Find(ComponentKind.O2Wideband) ?? Parts.Find(ComponentKind.O2Narrowband);
            Engine.O2HeaterTargetC = up != null && HeaterCurrent(up.Id) > 0.3 ? (up.Kind == ComponentKind.O2Wideband ? 780 : 650) : Environment.AmbientC;
            Component? downC = Parts.Find(ComponentKind.O2Downstream);
            Engine.O2DownstreamHeaterTargetC = downC != null && HeaterCurrent(downC.Id) > 0.3 ? 600 : Environment.AmbientC;
        }

        /// <summary>Heater current of an oxygen sensor (A) as the ECU driver measures it.</summary>
        public double HeaterCurrent(string componentId)
        {
            Circuit? c = CircuitOf(componentId);
            if (c == null || !c.DriverOn || !KeyOn)
            {
                return 0;
            }

            return Math.Max(0, c.DriverCurrent("heater_control"));
        }

        /// <summary>Hall sensor high and low levels at the ECU pin (V).</summary>
        public double HallLevels(string componentId, out double low)
        {
            Circuit? c = CircuitOf(componentId);
            low = 0;
            if (c == null)
            {
                return 0;
            }

            Component comp = Parts.Get(componentId)!;
            bool dead = Faults.Has(comp.Id, EffectKind.Dead) || Faults.Has(comp.Id, EffectKind.SignalDropout);
            c.HallOn = false;
            c.Solve(Faults);
            double high = c.EcuPinVoltage("signal");
            c.HallOn = !dead;
            // The Hall IC needs its supply to switch.
            double supply = c.Voltage("C:ref") - c.Voltage("C:ground");
            if (supply < 3.5)
            {
                c.HallOn = false;
            }

            c.Solve(Faults);
            low = c.EcuPinVoltage("signal");
            c.HallOn = false;
            return Engine.State.Rpm > 1 ? high : high;
        }

        /// <summary>Probes a low side load circuit like the ECU driver diagnostics do.</summary>
        public bool ProbeLowSide(string componentId, out double vOff, out double ampsOn, out double nominalAmps)
        {
            vOff = ampsOn = nominalAmps = 0;
            Circuit? c = CircuitOf(componentId);
            if (c == null || c.Topology != CircuitTopology.LowSideLoad)
            {
                return false;
            }

            if (!_probeCache.TryGetValue(componentId, out ProbeResult r))
            {
                Component comp = Parts.Get(componentId)!;
                bool wasOn = c.DriverOn;
                c.KeyOn = KeyOn;
                c.BatteryVolts = BatteryVolts;
                c.FuseOk = FuseOk(componentId);
                c.DriverOn = false;
                c.Solve(Faults);
                r.VOff = c.EcuPinVoltage("control");
                c.DriverOn = true;
                c.Solve(Faults);
                r.AmpsOn = Math.Max(0, c.DriverCurrent("control"));
                double loadAmps = (c.Voltage("C:supply") - c.Voltage("C:control")) / Math.Max(0.05, c.SensorValue);
                r.Nominal = BatteryVolts / Math.Max(0.5, comp.Param("ohms", 15));
                r.Energized = loadAmps > r.Nominal * 0.5;
                c.DriverOn = wasOn;
                _probeCache[componentId] = r;
            }

            vOff = r.VOff;
            ampsOn = r.AmpsOn;
            nominalAmps = r.Nominal;
            return true;
        }

        /// <summary>Whether a short to ground exists on any pin of a component circuit.</summary>
        public bool HasShortToGround(string componentId)
        {
            foreach (FaultInstance f in Faults.ActiveOn(componentId))
            {
                if (f.Effect == EffectKind.WireShortGround)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if the actuator of a kind would be energised when driven (load current flows).</summary>
        public bool ActuatorEnergized(ComponentKind kind, int cylinder = -1)
        {
            Component? c = Parts.Find(kind, cylinder);
            if (c == null)
            {
                return true;
            }

            return ActuatorEnergized(c.Id);
        }

        /// <summary>True if the actuator would be energised.</summary>
        public bool ActuatorEnergized(string componentId)
        {
            if (!KeyOn)
            {
                return false;
            }

            if (!ProbeLowSide(componentId, out _, out _, out _))
            {
                return true;
            }

            return _probeCache[componentId].Energized;
        }

        private EngineCommands BuildCommands(double dt)
        {
            int n = Definition.Engine.Cylinders;
            var cmd = new EngineCommands(n);
            EcuOutputs o = Ecu.Outputs;
            _probeTimer += dt;
            if (_probeTimer > 0.25)
            {
                _probeCache.Clear();
                _probeTimer = 0;
            }

            cmd.Throttle = o.ThrottleTarget;
            cmd.ThrottleDriven = KeyOn && o.ThrottleMotorEnabled && ActuatorEnergized(ComponentKind.ElectronicThrottle);
            for (int i = 0; i < n; i++)
            {
                Component? inj = Parts.Find(ComponentKind.Injector, i);
                Component? coil = Parts.Find(ComponentKind.IgnitionCoil, i);
                cmd.InjectorPulseMs[i] = o.InjectorPulseMs[i];
                cmd.InjectorEnabled[i] = KeyOn && (inj == null || ActuatorEnergized(inj.Id));
                cmd.SparkEnabled[i] = KeyOn && o.FireCylinder[i] && (coil == null || ActuatorEnergized(coil.Id));
            }

            cmd.DieselMgPerStroke = o.DieselMg;
            cmd.SparkAdvanceDeg = o.SparkAdvanceDeg;
            Component? wg = Parts.Find(ComponentKind.WastegateSolenoid);
            cmd.WastegateDuty = wg == null || ActuatorEnergized(wg.Id) ? o.WastegateDuty : 0;

            // Fuel pump: relay coil driven + contacts + fuse + pump itself.
            bool pump = o.FuelPumpRelay && KeyOn;
            if (PumpRelayId.Length > 0)
            {
                pump = pump && ActuatorEnergized(PumpRelayId) && !Faults.Has(PumpRelayId, EffectKind.StuckOpen);
                if (Faults.Has(PumpRelayId, EffectKind.StuckClosed) && KeyOn)
                {
                    pump = true;
                }
            }

            Component? pumpComp = Parts.Find(ComponentKind.FuelPump);
            if (pumpComp != null)
            {
                pump = pump && FuseOk(pumpComp.Id) && !Faults.Has(pumpComp.Id, EffectKind.WireOpen);
            }

            cmd.FuelPumpPowered = pump;
            cmd.FuelPumpVolts = BatteryVolts;

            bool fan = o.FanRelay && KeyOn;
            if (FanRelayId.Length > 0)
            {
                fan = fan && ActuatorEnergized(FanRelayId) && !Faults.Has(FanRelayId, EffectKind.StuckOpen);
            }

            Component? fanComp = Parts.Find(ComponentKind.CoolingFan);
            if (fanComp != null)
            {
                fan = fan && FuseOk(fanComp.Id) && !Faults.Has(fanComp.Id, EffectKind.Dead) && !Faults.Has(fanComp.Id, EffectKind.WireOpen);
            }

            cmd.FanOn = fan;
            cmd.EgrCommand = o.EgrCommand;
            cmd.PurgeDuty = o.PurgeDuty;
            cmd.GlowOn = o.GlowOn;
            cmd.IgnitionVolts = BatteryVolts;
            return cmd;
        }

        private void IntegrateDrivetrain(double dt, double engineTorque)
        {
            EngineState s = Engine.State;
            EngineDefinition e = Definition.Engine;
            double j = e.InertiaKgM2;
            double starter = 0;
            if (Cranking && !s.Seized)
            {
                double v = BatteryVolts;
                double strength = MathUtil.Clamp01((v - 6.5) / 3.5);
                starter = 100 * strength * Math.Max(0, 1 - s.Rpm / 380.0);
            }

            double omega = s.Rpm * 2 * Math.PI / 60;
            double r = Definition.WheelRadiusM;
            bool coupled = Gear > 0 && Gear <= Definition.GearRatios.Length && Mode != LoadMode.Neutral;
            if (s.Seized)
            {
                s.Rpm = 0;
                if (coupled)
                {
                    _vehicleSpeedMs = 0;
                }

                return;
            }

            if (!coupled)
            {
                double torque = engineTorque + starter;
                if (s.Rpm < 5 && torque < 0)
                {
                    torque = 0;
                }

                omega += torque / j * dt;
                omega = Math.Max(0, omega);
                s.Rpm = omega * 60 / (2 * Math.PI);
                _vehicleSpeedMs = Math.Max(0, _vehicleSpeedMs - 0.5 * dt);
                WheelForceN = 0;
                return;
            }

            double ratio = Definition.GearRatios[Gear - 1] * Definition.FinalDrive;
            double eff = Definition.DrivetrainEfficiency;
            double mass = Mode == LoadMode.Road ? Definition.MassKg : DynoRollerMassKg;
            double jEq = j * ratio * ratio / (r * r);
            double wheelForce = (engineTorque + starter) * ratio * (engineTorque > 0 ? eff : 1) / r;
            WheelForceN = wheelForce;
            double resist;
            if (Mode == LoadMode.Road)
            {
                double rho = 1.2;
                resist = 0.012 * mass * 9.81 + 0.5 * rho * Definition.DragAreaM2 * _vehicleSpeedMs * _vehicleSpeedMs;
                if (_vehicleSpeedMs < 0.05 && wheelForce < resist)
                {
                    resist = Math.Max(0, wheelForce);
                }

                DynoBrakeForceN = 0;
            }
            else
            {
                resist = 40 + 0.8 * _vehicleSpeedMs * _vehicleSpeedMs * 0.3;
                if (Mode == LoadMode.DynoHoldRpm)
                {
                    double targetV = DynoHoldRpm * 2 * Math.PI / 60 / ratio * r;
                    double err = _vehicleSpeedMs - targetV;
                    double brake = Math.Max(0, wheelForce - resist + err * 6000);
                    DynoBrakeForceN = brake;
                    resist += brake;
                }
                else
                {
                    DynoBrakeForceN = 0;
                }
            }

            double accel = (wheelForce - resist) / (mass + jEq);
            _vehicleSpeedMs = Math.Max(0, _vehicleSpeedMs + accel * dt);
            s.Rpm = _vehicleSpeedMs / r * ratio * 60 / (2 * Math.PI);
            if (s.Rpm < 600 && !Cranking && Mode == LoadMode.Road)
            {
                // Clutch slips below idle: decouple.
                s.Rpm = Math.Max(s.Rpm, s.Running ? 650 : s.Rpm);
            }

            OdometerKm += _vehicleSpeedMs * dt / 1000.0;
        }

        /// <summary>Sets vehicle speed directly (tests, dyno setup).</summary>
        public void SetVehicleSpeed(double kmh) => _vehicleSpeedMs = Math.Max(0, kmh / 3.6);

        private void ApplyDamageChains()
        {
            EngineDamage d = Engine.Damage;
            if ((_chainFlags & 1) == 0 && d.Catalyst >= 1.0)
            {
                _chainFlags |= 1;
                Component? cat = Parts.Find(ComponentKind.Catalyst);
                if (cat != null)
                {
                    AddFault(new FaultInstance(FailureModeLibrary.Default.Get("restriction"), cat.Id, 0.7) { Origin = "cadena: fallos de encendido → catalizador fundido" });
                }
            }

            if ((_chainFlags & 2) == 0 && d.HeadGasket >= 1.0)
            {
                _chainFlags |= 2;
                Component? hg = Parts.Find(ComponentKind.HeadGasket);
                if (hg != null)
                {
                    AddFault(new FaultInstance(FailureModeLibrary.Default.Get("leak_fluid"), hg.Id, 0.8) { Origin = "cadena: sobrecalentamiento → junta de culata" });
                }
            }

            if ((_chainFlags & 4) == 0 && d.Turbo >= 1.0)
            {
                _chainFlags |= 4;
                Component? t = Parts.Find(ComponentKind.Turbocharger);
                if (t != null)
                {
                    AddFault(new FaultInstance(FailureModeLibrary.Default.Get("wear"), t.Id, 0.9) { Origin = "cadena: sobrevelocidad → turbo dañado" });
                }
            }
        }

        /// <summary>Replaces a component with a new part: repairs its faults and related damage.</summary>
        public int ReplaceComponent(string componentId, double reliability = 1.0)
        {
            Component? c = Parts.Get(componentId);
            if (c == null)
            {
                return 0;
            }

            int repaired = Faults.RepairComponent(componentId);
            c.Health = 1.0;
            c.Reliability = reliability;
            EngineDamage d = Engine.Damage;
            switch (c.Kind)
            {
                case ComponentKind.Cylinder:
                    if (c.Cylinder >= 0)
                    {
                        d.Piston[c.Cylinder] = 0;
                        d.ExhaustValve[c.Cylinder] = 0;
                    }

                    break;
                case ComponentKind.HeadGasket:
                    d.HeadGasket = 0;
                    Engine.State.CoolantLevel = 1;
                    _chainFlags &= ~2;
                    break;
                case ComponentKind.Turbocharger:
                    d.Turbo = 0;
                    _chainFlags &= ~4;
                    break;
                case ComponentKind.Catalyst:
                    d.Catalyst = 0;
                    _chainFlags &= ~1;
                    break;
                case ComponentKind.SparkPlug:
                    d.PlugFouling = Math.Max(0, d.PlugFouling - 1.0 / Definition.Engine.Cylinders);
                    break;
            }

            _lagState.Remove(componentId);
            InvalidateCircuits();
            return repaired;
        }

        /// <summary>Repairs wiring of a component (one pin or all).</summary>
        public int RepairWiring(string componentId, string? pin = null)
        {
            int n = Faults.RepairWiring(componentId, pin);
            InvalidateCircuits();
            return n;
        }

        /// <summary>Rebuilds the engine short block: clears all mechanical damage.</summary>
        public void RebuildEngine()
        {
            Engine.SetDamage(new EngineDamage(Definition.Engine.Cylinders));
            Engine.State.Seized = false;
            Engine.State.CoolantLevel = 1;
            _chainFlags = 0;
        }

        /// <summary>Tops up coolant.</summary>
        public void RefillCoolant() => Engine.State.CoolantLevel = 1;

        /// <summary>Plug or unplug a component connector.</summary>
        public void SetConnector(string componentId, bool connected)
        {
            Circuit? c = CircuitOf(componentId);
            if (c != null)
            {
                c.ConnectorConnected = connected;
                _probeCache.Clear();
            }
        }
    }
}
