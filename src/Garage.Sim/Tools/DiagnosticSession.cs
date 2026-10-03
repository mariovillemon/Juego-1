using System;
using System.Collections.Generic;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Tools
{
    /// <summary>Workshop time cost of each diagnostic action (minutes).</summary>
    public static class TimeCosts
    {
        /// <summary>Connect the scan tool.</summary>
        public const double ScanConnect = 2;
        /// <summary>Read codes of one module.</summary>
        public const double ReadCodes = 1;
        /// <summary>Clear codes.</summary>
        public const double ClearCodes = 1;
        /// <summary>One live data snapshot.</summary>
        public const double LiveData = 0.5;
        /// <summary>Actuator test.</summary>
        public const double ActuatorTest = 3;
        /// <summary>Multimeter measurement (includes access/back-probing).</summary>
        public const double Measurement = 3;
        /// <summary>Unplug/plug a connector.</summary>
        public const double Connector = 2;
        /// <summary>Fuel pressure gauge hookup + test.</summary>
        public const double FuelPressure = 15;
        /// <summary>Compression test per cylinder (plugs out amortised).</summary>
        public const double CompressionPerCylinder = 8;
        /// <summary>Leak down per cylinder.</summary>
        public const double LeakDownPerCylinder = 12;
        /// <summary>Smoke test.</summary>
        public const double SmokeTest = 20;
        /// <summary>Oscilloscope capture.</summary>
        public const double ScopeCapture = 4;
        /// <summary>Looking up a wiring diagram.</summary>
        public const double WiringDiagram = 1;
        /// <summary>Visual inspection.</summary>
        public const double Inspection = 5;
    }

    /// <summary>Result of a tool action: what the tool shows plus time spent.</summary>
    public sealed class ToolResult
    {
        /// <summary>Creates a result.</summary>
        public ToolResult(string display, double minutes, bool ok = true)
        {
            Display = display;
            Minutes = minutes;
            Ok = ok;
        }

        /// <summary>Text as shown on the tool.</summary>
        public string Display { get; }

        /// <summary>Workshop minutes spent.</summary>
        public double Minutes { get; }

        /// <summary>False when the action was not possible.</summary>
        public bool Ok { get; }

        /// <summary>Optional numeric value.</summary>
        public double Value { get; set; } = double.NaN;

        /// <inheritdoc />
        public override string ToString() => Display;
    }

    /// <summary>
    /// A diagnostic session on one car. Owns the tools and charges workshop time to the car clock
    /// (scaled by the workshop's speed factor, e.g. after training).
    /// </summary>
    public sealed class DiagnosticSession
    {
        private readonly List<string> _log = new List<string>();

        /// <summary>Creates a session.</summary>
        public DiagnosticSession(Car car, double timeFactor = 1.0)
        {
            Car = car;
            TimeFactor = timeFactor;
            Scanner = new ScanTool(this);
            Multimeter = new Multimeter(this);
            Scope = new Oscilloscope(this);
            Mechanical = new MechanicalTests(this);
        }

        /// <summary>The car.</summary>
        public Car Car { get; }

        /// <summary>Multiplier on time costs (training upgrades &lt; 1).</summary>
        public double TimeFactor { get; set; }

        /// <summary>Scan tool.</summary>
        public ScanTool Scanner { get; }

        /// <summary>Multimeter.</summary>
        public Multimeter Multimeter { get; }

        /// <summary>Oscilloscope.</summary>
        public Oscilloscope Scope { get; }

        /// <summary>Fuel pressure, compression, leak-down and smoke tests.</summary>
        public MechanicalTests Mechanical { get; }

        /// <summary>Total minutes spent in this session.</summary>
        public double MinutesSpent { get; private set; }

        /// <summary>History of actions.</summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary>Charges time and logs.</summary>
        public ToolResult Charge(ToolResult r, string action)
        {
            double m = r.Minutes * TimeFactor;
            MinutesSpent += m;
            Car.Clock.Spend(m);
            _log.Add($"[{MinutesSpent,6:0.0} min] {action}");
            return r;
        }
    }
}
