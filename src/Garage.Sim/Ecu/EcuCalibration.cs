using System;
using System.Collections.Generic;
using Garage.Sim.Maps;

namespace Garage.Sim.Ecu
{
    /// <summary>
    /// ECU calibration ("the map"): named tables, curves and scalars. Everything is editable,
    /// which is what tuning in the game means. Well known ids are exposed as constants.
    /// </summary>
    public sealed class EcuCalibration
    {
        /// <summary>Lambda target table (rpm × relative load).</summary>
        public const string LambdaTarget = "lambda_target";
        /// <summary>Ignition advance table (rpm × relative load), degrees BTDC.</summary>
        public const string IgnitionAdvance = "ignition_advance";
        /// <summary>Boost target table (rpm × pedal %), kPa gauge.</summary>
        public const string BoostTarget = "boost_target";
        /// <summary>Wastegate base duty table (rpm × pedal %), %.</summary>
        public const string WastegateDuty = "wastegate_duty";
        /// <summary>Speed density VE estimate used as MAF substitute (rpm × MAP kPa).</summary>
        public const string VeEstimate = "ve_estimate";
        /// <summary>Diesel fuel quantity (rpm × pedal %), mg/stroke.</summary>
        public const string DieselQuantity = "diesel_quantity";
        /// <summary>Pedal to throttle curve (pedal % → throttle %).</summary>
        public const string PedalToThrottle = "pedal_to_throttle";
        /// <summary>Warm-up enrichment multiplier vs coolant °C.</summary>
        public const string EctFuelCorrection = "ect_fuel_correction";
        /// <summary>Ignition correction vs intake air °C (degrees).</summary>
        public const string IatIgnitionCorrection = "iat_ignition_correction";
        /// <summary>Ignition correction vs coolant °C (degrees).</summary>
        public const string EctIgnitionCorrection = "ect_ignition_correction";

        private readonly Dictionary<string, Map3D> _tables = new Dictionary<string, Map3D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Map2D> _curves = new Dictionary<string, Map2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _scalars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Calibration id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Tables.</summary>
        public IReadOnlyDictionary<string, Map3D> Tables => _tables;

        /// <summary>Curves.</summary>
        public IReadOnlyDictionary<string, Map2D> Curves => _curves;

        /// <summary>Scalars.</summary>
        public IReadOnlyDictionary<string, double> Scalars => _scalars;

        /// <summary>Adds or replaces a table.</summary>
        public void SetTable(Map3D map) => _tables[map.Id] = map;

        /// <summary>Adds or replaces a curve.</summary>
        public void SetCurve(Map2D map) => _curves[map.Id] = map;

        /// <summary>Sets a scalar.</summary>
        public void SetScalar(string id, double v) => _scalars[id] = v;

        /// <summary>Table or null.</summary>
        public Map3D? Table(string id) => _tables.TryGetValue(id, out Map3D m) ? m : null;

        /// <summary>Curve or null.</summary>
        public Map2D? Curve(string id) => _curves.TryGetValue(id, out Map2D m) ? m : null;

        /// <summary>Scalar with default.</summary>
        public double Scalar(string id, double fallback) => _scalars.TryGetValue(id, out double v) ? v : fallback;

        /// <summary>Table lookup with fallback value.</summary>
        public double Lookup(string id, double x, double y, double fallback)
        {
            Map3D? m = Table(id);
            return m == null ? fallback : m.Lookup(x, y);
        }

        /// <summary>Curve lookup with fallback value.</summary>
        public double Lookup(string id, double x, double fallback)
        {
            Map2D? m = Curve(id);
            return m == null ? fallback : m.Lookup(x);
        }

        /// <summary>Deep copy (tuning works on a copy).</summary>
        public EcuCalibration Clone()
        {
            var c = new EcuCalibration { Id = Id, Description = Description };
            foreach (KeyValuePair<string, Map3D> t in _tables)
            {
                c._tables[t.Key] = t.Value.Clone();
            }

            foreach (KeyValuePair<string, Map2D> t in _curves)
            {
                c._curves[t.Key] = t.Value.Clone();
            }

            foreach (KeyValuePair<string, double> s in _scalars)
            {
                c._scalars[s.Key] = s.Value;
            }

            return c;
        }

        /// <summary>Well known scalar ids and defaults.</summary>
        public static class Keys
        {
            /// <summary>Fuel cut rev limit.</summary>
            public const string RevLimit = "rev_limit_rpm";
            /// <summary>Vehicle speed limiter.</summary>
            public const string SpeedLimit = "speed_limit_kmh";
            /// <summary>Torque limiter (reduces boost).</summary>
            public const string TorqueLimit = "torque_limit_nm";
            /// <summary>Warm idle target.</summary>
            public const string IdleRpm = "idle_rpm";
            /// <summary>Cold idle target.</summary>
            public const string IdleColdRpm = "idle_cold_rpm";
            /// <summary>Injector flow the ECU believes (cc/min at reference pressure).</summary>
            public const string InjectorFlow = "injector_flow_ccmin";
            /// <summary>Injector dead time at 14 V (ms).</summary>
            public const string InjectorDeadTime = "injector_dead_time_ms";
            /// <summary>MAF sensor full scale flow (g/s).</summary>
            public const string MafMaxFlow = "maf_max_gps";
            /// <summary>Fan on temperature.</summary>
            public const string FanOn = "fan_on_c";
            /// <summary>Fan off temperature.</summary>
            public const string FanOff = "fan_off_c";
            /// <summary>Knock retard per detected event (deg).</summary>
            public const string KnockStep = "knock_retard_step_deg";
            /// <summary>Max knock retard (deg).</summary>
            public const string KnockMax = "knock_retard_max_deg";
            /// <summary>Overboost cut threshold (kPa gauge).</summary>
            public const string OverboostLimit = "overboost_limit_kpa";
            /// <summary>Rail pressure target (kPa gauge).</summary>
            public const string RailTarget = "rail_pressure_target_kpa";
            /// <summary>MAP sensor range max (kPa abs).</summary>
            public const string MapSensorMax = "map_sensor_max_kpa";
        }
    }
}
