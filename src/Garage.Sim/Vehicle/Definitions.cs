using System.Collections.Generic;
using Garage.Sim.Components;
using Garage.Sim.Electrical;
using Garage.Sim.Maps;

namespace Garage.Sim.Vehicle
{
    /// <summary>Engine family. Hybrid and Electric are reserved for future phases.</summary>
    public enum EngineKind
    {
        /// <summary>Naturally aspirated spark ignition.</summary>
        GasolineNA,
        /// <summary>Turbocharged spark ignition.</summary>
        GasolineTurbo,
        /// <summary>Turbo diesel common rail (experimental).</summary>
        DieselTurbo,
        /// <summary>Reserved.</summary>
        Hybrid,
        /// <summary>Reserved.</summary>
        Electric,
    }

    /// <summary>Turbocharger physical description.</summary>
    public sealed class TurboDefinition
    {
        /// <summary>Maximum boost (gauge kPa) the turbo can produce with the wastegate closed.</summary>
        public double MaxBoostKpa { get; set; } = 150;

        /// <summary>Rpm at which full boost is available at full load.</summary>
        public double FullSpoolRpm { get; set; } = 2200;

        /// <summary>Rpm at which the turbo starts producing boost.</summary>
        public double SpoolStartRpm { get; set; } = 1300;

        /// <summary>Boost (gauge kPa) with the wastegate actuator spring only (solenoid off).</summary>
        public double WastegateSpringKpa { get; set; } = 45;

        /// <summary>Boost above which the turbo is over-sped and damaged (gauge kPa).</summary>
        public double MaxSafeBoostKpa { get; set; } = 170;

        /// <summary>Compressor isentropic efficiency.</summary>
        public double CompressorEfficiency { get; set; } = 0.72;

        /// <summary>Intercooler effectiveness 0..1.</summary>
        public double IntercoolerEffectiveness { get; set; } = 0.7;

        /// <summary>Maximum compressor mass flow (g/s) before boost collapses.</summary>
        public double MaxFlowGps { get; set; } = 200;

        /// <summary>Spool time constant (s).</summary>
        public double SpoolTimeConstant { get; set; } = 0.45;
    }

    /// <summary>Physical engine definition loaded from data.</summary>
    public sealed class EngineDefinition
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Kind.</summary>
        public EngineKind Kind { get; set; } = EngineKind.GasolineTurbo;

        /// <summary>Number of cylinders.</summary>
        public int Cylinders { get; set; } = 4;

        /// <summary>Displacement in litres.</summary>
        public double DisplacementL { get; set; } = 2.0;

        /// <summary>Bore mm.</summary>
        public double BoreMm { get; set; } = 82.5;

        /// <summary>Stroke mm.</summary>
        public double StrokeMm { get; set; } = 92.8;

        /// <summary>Geometric compression ratio.</summary>
        public double CompressionRatio { get; set; } = 9.6;

        /// <summary>Firing order (1 based cylinder numbers).</summary>
        public int[] FiringOrder { get; set; } = { 1, 3, 4, 2 };

        /// <summary>Rated redline.</summary>
        public double RedlineRpm { get; set; } = 6500;

        /// <summary>Mechanical limit: above this rpm rod/valvetrain damage accumulates.</summary>
        public double MechanicalLimitRpm { get; set; } = 7000;

        /// <summary>Throttle bore diameter (mm).</summary>
        public double ThrottleDiameterMm { get; set; } = 60;

        /// <summary>Rotating inertia including flywheel (kg·m²).</summary>
        public double InertiaKgM2 { get; set; } = 0.16;

        /// <summary>Physical volumetric efficiency table (x = rpm, y = manifold kPa absolute).</summary>
        public Map3D? VolumetricEfficiency { get; set; }

        /// <summary>MBT spark advance (x = rpm, y = relative load 0..2.5).</summary>
        public Map3D? MbtAdvance { get; set; }

        /// <summary>Base knock margin in degrees at RON 95, 1 bar, 40 °C charge.</summary>
        public double KnockMarginDeg { get; set; } = 6;

        /// <summary>Fuel octane (RON) the car is filled with.</summary>
        public double FuelRon { get; set; } = 95;

        /// <summary>Turbo (null for NA engines).</summary>
        public TurboDefinition? Turbo { get; set; }

        /// <summary>Real injector flow at the reference pressure (cc/min).</summary>
        public double InjectorFlowCcMin { get; set; } = 330;

        /// <summary>Injector reference pressure (kPa gauge).</summary>
        public double InjectorRefPressureKpa { get; set; } = 400;

        /// <summary>Regulated rail pressure (kPa gauge). For diesel, max rail pressure.</summary>
        public double RailPressureKpa { get; set; } = 400;

        /// <summary>Fuel pump maximum deadhead pressure (kPa gauge).</summary>
        public double PumpMaxPressureKpa { get; set; } = 650;

        /// <summary>Coolant + block thermal capacity (kJ/K).</summary>
        public double ThermalCapacityKjK { get; set; } = 45;

        /// <summary>Thermostat opening start (°C).</summary>
        public double ThermostatOpenC { get; set; } = 88;

        /// <summary>Radiator heat transfer at full opening and nominal airflow (kW/K).</summary>
        public double RadiatorKwK { get; set; } = 0.55;

        /// <summary>Friction scaling (1 = nominal FMEP).</summary>
        public double FrictionFactor { get; set; } = 1.0;

        /// <summary>Peak indicated efficiency.</summary>
        public double IndicatedEfficiency { get; set; } = 0.385;

        /// <summary>Displacement in m³.</summary>
        public double DisplacementM3 => DisplacementL / 1000.0;

        /// <summary>Gasoline direct injection (high pressure pump, rail at tens of bar).</summary>
        public bool DirectInjection { get; set; }

        /// <summary>True if the engine is turbocharged.</summary>
        public bool IsTurbo => Turbo != null;

        /// <summary>True for diesel.</summary>
        public bool IsDiesel => Kind == EngineKind.DieselTurbo;
    }

    /// <summary>Circuit description of a component from data.</summary>
    public sealed class CircuitDefinition
    {
        /// <summary>Topology.</summary>
        public CircuitTopology Topology { get; set; }

        /// <summary>Pins.</summary>
        public List<PinInfo> Pins { get; set; } = new List<PinInfo>();

        /// <summary>Feeding fuse component id (optional).</summary>
        public string Fuse { get; set; } = "";
    }

    /// <summary>Component instance description from data.</summary>
    public sealed class ComponentDefinition
    {
        /// <summary>Id within the car.</summary>
        public string Id { get; set; } = "";

        /// <summary>Kind.</summary>
        public ComponentKind Kind { get; set; }

        /// <summary>Display name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Cylinder (0 based) or -1.</summary>
        public int Cylinder { get; set; } = -1;

        /// <summary>Visual slot.</summary>
        public string VisualSlot { get; set; } = "";

        /// <summary>Location description (Spanish) for the player.</summary>
        public string Location { get; set; } = "";

        /// <summary>Parameters.</summary>
        public Dictionary<string, double> Parameters { get; set; } = new Dictionary<string, double>();

        /// <summary>Circuit (null for non electrical components).</summary>
        public CircuitDefinition? Circuit { get; set; }

        /// <summary>Feeding fuse id for components without an ECU circuit (fuel pump, fan).</summary>
        public string Fuse { get; set; } = "";

        /// <summary>Default part id for replacement.</summary>
        public string PartId { get; set; } = "";

        /// <summary>Minutes of labour to replace.</summary>
        public double ReplaceMinutes { get; set; } = 30;
    }

    /// <summary>Visual appearance inputs (feed the Unity wear shaders).</summary>
    public sealed class AppearanceDefinition
    {
        /// <summary>Age in years.</summary>
        public double AgeYears { get; set; } = 5;

        /// <summary>Odometer km.</summary>
        public double Kilometers { get; set; } = 80000;

        /// <summary>Maintenance level 0 (neglected) .. 1 (impeccable).</summary>
        public double Maintenance { get; set; } = 0.7;

        /// <summary>Paint color as hex (#RRGGBB).</summary>
        public string PaintColor { get; set; } = "#8A8D91";

        /// <summary>Climate: 0 dry, 1 coastal/salted roads (rust).</summary>
        public double Humidity { get; set; } = 0.3;

        /// <summary>Derived dirt 0..1.</summary>
        public double Dirt => Garage.Sim.Core.MathUtil.Clamp01(0.15 + (1 - Maintenance) * 0.7 + Kilometers / 600000.0);

        /// <summary>Derived rust 0..1.</summary>
        public double Rust => Garage.Sim.Core.MathUtil.Clamp01((AgeYears / 25.0) * (0.4 + Humidity) * (1.3 - Maintenance));

        /// <summary>Derived grease/oil film on engine parts 0..1.</summary>
        public double Grease => Garage.Sim.Core.MathUtil.Clamp01(Kilometers / 300000.0 + (1 - Maintenance) * 0.4);

        /// <summary>Derived paint fade 0..1.</summary>
        public double PaintFade => Garage.Sim.Core.MathUtil.Clamp01(AgeYears / 30.0 * (1.2 - Maintenance * 0.6));
    }

    /// <summary>Car description from data: a combination of components.</summary>
    public sealed class CarDefinition
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Fictional brand.</summary>
        public string Brand { get; set; } = "";

        /// <summary>Fictional model.</summary>
        public string Model { get; set; } = "";

        /// <summary>Model year.</summary>
        public int Year { get; set; } = 2015;

        /// <summary>Body/segment description.</summary>
        public string Segment { get; set; } = "";

        /// <summary>Engine.</summary>
        public EngineDefinition Engine { get; set; } = new EngineDefinition();

        /// <summary>Stock calibration id.</summary>
        public string CalibrationId { get; set; } = "";

        /// <summary>Vehicle mass (kg) with driver.</summary>
        public double MassKg { get; set; } = 1350;

        /// <summary>Wheel rolling radius (m).</summary>
        public double WheelRadiusM { get; set; } = 0.315;

        /// <summary>Gear ratios.</summary>
        public double[] GearRatios { get; set; } = { 3.46, 2.05, 1.30, 1.03, 0.84, 0.69 };

        /// <summary>Final drive ratio.</summary>
        public double FinalDrive { get; set; } = 3.65;

        /// <summary>Drivetrain efficiency 0..1.</summary>
        public double DrivetrainEfficiency { get; set; } = 0.88;

        /// <summary>Aerodynamic drag area CdA (m²).</summary>
        public double DragAreaM2 { get; set; } = 0.68;

        /// <summary>Components.</summary>
        public List<ComponentDefinition> Components { get; set; } = new List<ComponentDefinition>();

        /// <summary>CAN modules present (ecm, abs, ipc, bcm).</summary>
        public List<string> Modules { get; set; } = new List<string> { "ecm", "abs", "ipc", "bcm" };

        /// <summary>Appearance.</summary>
        public AppearanceDefinition Appearance { get; set; } = new AppearanceDefinition();

        /// <summary>Marked experimental (e.g. diesel model not complete).</summary>
        public bool Experimental { get; set; }

        /// <summary>VIN (fictional, 17 chars).</summary>
        public string Vin { get; set; } = "VF0GARAGE00000001";

        /// <summary>Display name.</summary>
        public string DisplayName => $"{Brand} {Model} ({Year})";
    }
}
