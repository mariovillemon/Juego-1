using System.Collections.Generic;

namespace Garage.Sim.Ecu
{
    /// <summary>
    /// Debounced test: a malfunction must persist for FailTime seconds while enabled to report a failure;
    /// a healthy condition for PassTime seconds reports a pass (test complete).
    /// </summary>
    public sealed class DebouncedTest
    {
        private double _failTimer;
        private double _passTimer;

        /// <summary>Creates a test.</summary>
        public DebouncedTest(string code, double failTime, double passTime)
        {
            Code = code;
            FailTime = failTime;
            PassTime = passTime;
        }

        /// <summary>DTC reported.</summary>
        public string Code { get; }

        /// <summary>Seconds of continuous malfunction to fail.</summary>
        public double FailTime { get; }

        /// <summary>Seconds of continuous good condition to pass.</summary>
        public double PassTime { get; }

        /// <summary>Completed at least once this trip.</summary>
        public bool Completed { get; private set; }

        /// <summary>Last result was failure.</summary>
        public bool Failing { get; private set; }

        /// <summary>Feeds one evaluation.</summary>
        public void Update(bool enabled, bool malfunction, double dt, DtcStore store, System.Func<FreezeFrame> frame)
        {
            if (!enabled)
            {
                _failTimer = 0;
                _passTimer = 0;
                return;
            }

            if (malfunction)
            {
                _passTimer = 0;
                _failTimer += dt;
                if (_failTimer >= FailTime)
                {
                    store.Fail(Code, frame);
                    Failing = true;
                    Completed = true;
                }
            }
            else
            {
                _failTimer = 0;
                _passTimer += dt;
                if (_passTimer >= PassTime)
                {
                    store.Pass(Code);
                    Failing = false;
                    Completed = true;
                }
            }
        }

        /// <summary>Resets per trip state.</summary>
        public void ResetTrip()
        {
            _failTimer = 0;
            _passTimer = 0;
            Completed = false;
        }
    }

    /// <summary>OBD-II readiness monitors.</summary>
    public enum ReadinessMonitor
    {
        /// <summary>Misfire (continuous).</summary>
        Misfire,
        /// <summary>Fuel system (continuous).</summary>
        FuelSystem,
        /// <summary>Comprehensive components (continuous).</summary>
        Components,
        /// <summary>Catalyst.</summary>
        Catalyst,
        /// <summary>Heated catalyst.</summary>
        HeatedCatalyst,
        /// <summary>Evaporative system.</summary>
        Evap,
        /// <summary>Secondary air.</summary>
        SecondaryAir,
        /// <summary>Oxygen sensor.</summary>
        O2Sensor,
        /// <summary>Oxygen sensor heater.</summary>
        O2Heater,
        /// <summary>EGR / VVT.</summary>
        Egr,
    }

    /// <summary>Readiness status set.</summary>
    public sealed class Readiness
    {
        private readonly Dictionary<ReadinessMonitor, bool> _supported = new Dictionary<ReadinessMonitor, bool>();
        private readonly Dictionary<ReadinessMonitor, bool> _complete = new Dictionary<ReadinessMonitor, bool>();

        /// <summary>Creates readiness for a set of supported monitors.</summary>
        public Readiness(IEnumerable<ReadinessMonitor> supported)
        {
            foreach (ReadinessMonitor m in System.Enum.GetValues(typeof(ReadinessMonitor)))
            {
                _supported[m] = false;
                _complete[m] = false;
            }

            foreach (ReadinessMonitor m in supported)
            {
                _supported[m] = true;
            }
        }

        /// <summary>Supported?</summary>
        public bool IsSupported(ReadinessMonitor m) => _supported[m];

        /// <summary>Complete?</summary>
        public bool IsComplete(ReadinessMonitor m) => !_supported[m] || _complete[m];

        /// <summary>Marks complete.</summary>
        public void SetComplete(ReadinessMonitor m) => _complete[m] = true;

        /// <summary>Resets all to incomplete (after clearing codes).</summary>
        public void Reset()
        {
            foreach (ReadinessMonitor m in System.Enum.GetValues(typeof(ReadinessMonitor)))
            {
                _complete[m] = false;
            }
        }

        /// <summary>All supported monitors complete (inspection requirement, usually ≤1 incomplete allowed).</summary>
        public int IncompleteCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<ReadinessMonitor, bool> s in _supported)
                {
                    if (s.Value && !_complete[s.Key])
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>Supported monitors.</summary>
        public IEnumerable<ReadinessMonitor> Supported
        {
            get
            {
                foreach (KeyValuePair<ReadinessMonitor, bool> s in _supported)
                {
                    if (s.Value)
                    {
                        yield return s.Key;
                    }
                }
            }
        }
    }
}
