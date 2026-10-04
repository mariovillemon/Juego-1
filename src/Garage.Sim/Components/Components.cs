using System;
using System.Collections.Generic;
using Garage.Sim.Faults;

namespace Garage.Sim.Components
{
    /// <summary>
    /// Kinds of simulated components. Data files reference these by name (case insensitive).
    /// New behaviour requires code, new instances/parameters only require data.
    /// </summary>
    public enum ComponentKind
    {
        /// <summary>Hot-film mass air flow sensor.</summary>
        MafSensor,
        /// <summary>Manifold absolute pressure sensor.</summary>
        MapSensor,
        /// <summary>Intake air temperature sensor (NTC).</summary>
        IatSensor,
        /// <summary>Engine coolant temperature sensor (NTC).</summary>
        EctSensor,
        /// <summary>Throttle position sensor (track 1).</summary>
        TpsSensor,
        /// <summary>Accelerator pedal position sensor.</summary>
        AppSensor,
        /// <summary>Crankshaft position sensor (variable reluctance, 60-2).</summary>
        CkpSensor,
        /// <summary>Camshaft position sensor (Hall).</summary>
        CmpSensor,
        /// <summary>Narrowband zirconia oxygen sensor.</summary>
        O2Narrowband,
        /// <summary>Wideband (planar, LSU type) lambda sensor.</summary>
        O2Wideband,
        /// <summary>Knock sensor (piezo).</summary>
        KnockSensor,
        /// <summary>Fuel rail pressure sensor.</summary>
        FuelPressureSensor,
        /// <summary>Boost pressure sensor (pre-throttle).</summary>
        BoostSensor,
        /// <summary>Fuel injector.</summary>
        Injector,
        /// <summary>Coil-on-plug ignition coil.</summary>
        IgnitionCoil,
        /// <summary>Spark plug.</summary>
        SparkPlug,
        /// <summary>Electronic throttle body (motor).</summary>
        ElectronicThrottle,
        /// <summary>Wastegate control solenoid (N75 type).</summary>
        WastegateSolenoid,
        /// <summary>Turbocharger.</summary>
        Turbocharger,
        /// <summary>Charge air cooler.</summary>
        Intercooler,
        /// <summary>Wax thermostat.</summary>
        Thermostat,
        /// <summary>Radiator cooling fan.</summary>
        CoolingFan,
        /// <summary>Electric fuel pump.</summary>
        FuelPump,
        /// <summary>Fuel pressure regulator.</summary>
        FuelPressureRegulator,
        /// <summary>EGR valve.</summary>
        EgrValve,
        /// <summary>Canister purge valve.</summary>
        PurgeValve,
        /// <summary>A cylinder (rings, valves, head gasket section).</summary>
        Cylinder,
        /// <summary>Intake manifold gasket.</summary>
        IntakeGasket,
        /// <summary>Vacuum hose (PCV, brake booster...).</summary>
        VacuumHose,
        /// <summary>Charge pipe / boost hose.</summary>
        BoostHose,
        /// <summary>Exhaust manifold and pipes.</summary>
        Exhaust,
        /// <summary>Three-way catalyst.</summary>
        Catalyst,
        /// <summary>Air filter.</summary>
        AirFilter,
        /// <summary>Fuel filter.</summary>
        FuelFilter,
        /// <summary>Timing chain/belt.</summary>
        TimingDrive,
        /// <summary>Head gasket.</summary>
        HeadGasket,
        /// <summary>12 V battery.</summary>
        Battery,
        /// <summary>Alternator with regulator.</summary>
        Alternator,
        /// <summary>Fuse.</summary>
        Fuse,
        /// <summary>Relay.</summary>
        Relay,
        /// <summary>Engine/body ground strap.</summary>
        GroundStrap,
        /// <summary>Electronic control module on the CAN bus (ECM, ABS, IPC, BCM).</summary>
        ControlModule,
        /// <summary>CAN bus wiring.</summary>
        CanBus,
        /// <summary>Diesel glow plug.</summary>
        GlowPlug,
        /// <summary>Downstream (post-cat) oxygen sensor.</summary>
        O2Downstream,
        /// <summary>EVAP charcoal canister.</summary>
        EvapCanister,
        /// <summary>EVAP canister vent valve (normally open, closed for the leak test).</summary>
        EvapVentValve,
        /// <summary>Fuel tank pressure sensor (EVAP).</summary>
        EvapPressureSensor,
        /// <summary>Fuel filler cap.</summary>
        FuelCap,
        /// <summary>Variable valve timing oil control valve (cam phaser solenoid).</summary>
        VvtSolenoid,
        /// <summary>Gasoline direct injection high pressure pump (with metering valve).</summary>
        HighPressurePump,
        /// <summary>Variable geometry turbo vane actuator.</summary>
        VgtActuator,
        /// <summary>Diesel particulate filter.</summary>
        ParticulateFilter,
        /// <summary>DPF differential pressure sensor.</summary>
        DpfPressureSensor,
        /// <summary>Exhaust gas temperature sensor (before the DPF).</summary>
        ExhaustTempSensor,
    }

    /// <summary>Common interface of every simulated component.</summary>
    public interface IComponent
    {
        /// <summary>Unique id within the car (e.g. "ect", "inj3").</summary>
        string Id { get; }

        /// <summary>Kind.</summary>
        ComponentKind Kind { get; }

        /// <summary>Display name (Spanish).</summary>
        string Name { get; }

        /// <summary>Numeric parameters from data.</summary>
        IReadOnlyDictionary<string, double> Parameters { get; }

        /// <summary>Health 0..1 (wear). 1 = new.</summary>
        double Health { get; set; }

        /// <summary>Cylinder index (0 based) for per-cylinder parts; -1 otherwise.</summary>
        int Cylinder { get; }

        /// <summary>Visual anchor slot used by the Unity assembly.</summary>
        string VisualSlot { get; }

        /// <summary>Kinds of failure modes this component accepts (by default its kind name).</summary>
        IReadOnlyList<string> FailureTags { get; }
    }

    /// <summary>Default component implementation; behaviour lives in the engine/ECU models that read it.</summary>
    public sealed class Component : IComponent
    {
        private readonly Dictionary<string, double> _parameters;

        /// <summary>Creates a component.</summary>
        public Component(string id, ComponentKind kind, string name, IDictionary<string, double>? parameters = null, int cylinder = -1, string? visualSlot = null, IEnumerable<string>? extraTags = null)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Cylinder = cylinder;
            VisualSlot = visualSlot ?? id;
            _parameters = parameters == null ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, double>(parameters, StringComparer.OrdinalIgnoreCase);
            var tags = new List<string> { kind.ToString() };
            if (extraTags != null)
            {
                tags.AddRange(extraTags);
            }

            FailureTags = tags;
        }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public ComponentKind Kind { get; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, double> Parameters => _parameters;

        /// <inheritdoc />
        public double Health { get; set; } = 1.0;

        /// <inheritdoc />
        public int Cylinder { get; }

        /// <inheritdoc />
        public string VisualSlot { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> FailureTags { get; }

        /// <summary>Installed part quality/reliability 0..1 (affects premature failure).</summary>
        public double Reliability { get; set; } = 1.0;

        /// <summary>Installed part number.</summary>
        public string PartId { get; set; } = "";

        /// <summary>Where it is on the car (Spanish).</summary>
        public string Location { get; set; } = "";

        /// <summary>Labour minutes to replace it.</summary>
        public double ReplaceMinutes { get; set; } = 30;

        /// <summary>Parameter with default.</summary>
        public double Param(string key, double fallback) => _parameters.TryGetValue(key, out double v) ? v : fallback;

        /// <summary>Sets/overrides a parameter.</summary>
        public void SetParam(string key, double value) => _parameters[key] = value;

        /// <summary>Whether a failure mode can target this component.</summary>
        public bool Accepts(FailureModeDefinition mode)
        {
            foreach (string t in mode.AppliesTo)
            {
                foreach (string tag in FailureTags)
                {
                    if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override string ToString() => $"{Id} ({Kind})";
    }

    /// <summary>Helpers about component kinds.</summary>
    public static class ComponentKinds
    {
        /// <summary>Parses a kind name (case insensitive).</summary>
        public static bool TryParse(string name, out ComponentKind kind) => Enum.TryParse(name, true, out kind);

        /// <summary>True for sensors.</summary>
        public static bool IsSensor(ComponentKind k)
        {
            switch (k)
            {
                case ComponentKind.MafSensor:
                case ComponentKind.MapSensor:
                case ComponentKind.IatSensor:
                case ComponentKind.EctSensor:
                case ComponentKind.TpsSensor:
                case ComponentKind.AppSensor:
                case ComponentKind.CkpSensor:
                case ComponentKind.CmpSensor:
                case ComponentKind.O2Narrowband:
                case ComponentKind.O2Wideband:
                case ComponentKind.O2Downstream:
                case ComponentKind.KnockSensor:
                case ComponentKind.FuelPressureSensor:
                case ComponentKind.BoostSensor:
                case ComponentKind.EvapPressureSensor:
                case ComponentKind.DpfPressureSensor:
                case ComponentKind.ExhaustTempSensor:
                    return true;
                default:
                    return false;
            }
        }
    }
}
