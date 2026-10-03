using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Garage.Sim.Core;
using Garage.Sim.Engine;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Dyno
{
    /// <summary>Generic channel recorder (datalog).</summary>
    public sealed class Datalog
    {
        private readonly List<string> _channels;
        private readonly List<double[]> _rows = new List<double[]>();

        /// <summary>Creates a log with channel names.</summary>
        public Datalog(IEnumerable<string> channels)
        {
            _channels = new List<string>(channels);
        }

        /// <summary>Channel names.</summary>
        public IReadOnlyList<string> Channels => _channels;

        /// <summary>Rows (one value per channel).</summary>
        public IReadOnlyList<double[]> Rows => _rows;

        /// <summary>Adds a row.</summary>
        public void Add(params double[] values)
        {
            if (values.Length != _channels.Count)
            {
                throw new ArgumentException("Row width does not match channel count.");
            }

            _rows.Add(values);
        }

        /// <summary>Column values of a channel.</summary>
        public double[] Column(string channel)
        {
            int c = _channels.IndexOf(channel);
            if (c < 0)
            {
                throw new KeyNotFoundException(channel);
            }

            var r = new double[_rows.Count];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = _rows[i][c];
            }

            return r;
        }

        /// <summary>CSV text (invariant culture, comma separated, header row).</summary>
        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", _channels));
            foreach (double[] r in _rows)
            {
                for (int i = 0; i < r.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(r[i].ToString("0.###", CultureInfo.InvariantCulture));
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>Writes the CSV to a file.</summary>
        public void SaveCsv(string path) => File.WriteAllText(path, ToCsv());
    }

    /// <summary>Result of a dyno pull.</summary>
    public sealed class DynoResult
    {
        /// <summary>Creates a result.</summary>
        public DynoResult(Datalog log)
        {
            Log = log;
        }

        /// <summary>Full datalog.</summary>
        public Datalog Log { get; }

        /// <summary>Peak engine power (PS, corrected estimate).</summary>
        public double PeakPowerPs { get; set; }

        /// <summary>Rpm of peak power.</summary>
        public double PeakPowerRpm { get; set; }

        /// <summary>Peak engine torque (N·m).</summary>
        public double PeakTorqueNm { get; set; }

        /// <summary>Rpm of peak torque.</summary>
        public double PeakTorqueRpm { get; set; }

        /// <summary>Peak wheel power (PS).</summary>
        public double PeakWheelPs { get; set; }

        /// <summary>Pull aborted (engine damage, seizure, overboost cut...).</summary>
        public bool Aborted { get; set; }

        /// <summary>Why the pull ended.</summary>
        public string EndReason { get; set; } = "";

        /// <summary>Max knock retard seen.</summary>
        public double MaxKnockRetard { get; set; }

        /// <summary>Min lambda at high load.</summary>
        public double MinLambda { get; set; } = 9;

        /// <summary>Max lambda at high load (lean is dangerous).</summary>
        public double MaxLambdaUnderLoad { get; set; }

        /// <summary>Damage done during the pull (max part delta).</summary>
        public double DamageDelta { get; set; }

        /// <summary>Warnings for the operator (Spanish).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Summary line.</summary>
        public string Summary =>
            $"Potencia máx: {PeakPowerPs:0} CV a {PeakPowerRpm:0} rpm | Par máx: {PeakTorqueNm:0} N·m a {PeakTorqueRpm:0} rpm | En rueda: {PeakWheelPs:0} CV" +
            (Aborted ? $" | ABORTADA: {EndReason}" : "");

        /// <summary>ASCII power/torque chart.</summary>
        public string RenderAscii(int width = 70, int height = 16)
        {
            double[] rpm = Log.Column("rpm");
            double[] ps = Log.Column("power_ps");
            double[] nm = Log.Column("torque_nm");
            if (rpm.Length < 2)
            {
                return "(sin datos)";
            }

            double rMin = rpm[0];
            double rMax = rpm[rpm.Length - 1];
            double yMax = Math.Max(MaxOf(ps), MaxOf(nm)) * 1.1;
            var grid = new char[height, width];
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    grid[r, c] = r == height - 1 ? '─' : ' ';
                }
            }

            void Plot(double[] ys, char ch)
            {
                for (int i = 0; i < rpm.Length; i++)
                {
                    int c = (int)Math.Round((rpm[i] - rMin) / Math.Max(1, rMax - rMin) * (width - 1));
                    int r = (int)Math.Round((1 - ys[i] / yMax) * (height - 2));
                    if (c >= 0 && c < width && r >= 0 && r < height - 1)
                    {
                        grid[r, c] = grid[r, c] == ' ' || grid[r, c] == ch ? ch : '#';
                    }
                }
            }

            Plot(nm, 'T');
            Plot(ps, 'P');
            var sb = new StringBuilder();
            sb.AppendLine("  P = potencia (CV)   T = par (N·m)");
            for (int r = 0; r < height; r++)
            {
                double v = yMax * (1 - (double)r / (height - 2));
                sb.Append(r < height - 1 ? v.ToString("0", CultureInfo.InvariantCulture).PadLeft(5) + " │" : "      └");
                for (int c = 0; c < width; c++)
                {
                    sb.Append(grid[r, c]);
                }

                sb.AppendLine();
            }

            sb.Append("       ").Append(rMin.ToString("0", CultureInfo.InvariantCulture)).Append(" rpm").Append(new string(' ', Math.Max(1, width - 18))).Append(rMax.ToString("0", CultureInfo.InvariantCulture)).Append(" rpm");
            return sb.ToString();
        }

        private static double MaxOf(double[] a)
        {
            double m = 0;
            foreach (double v in a)
            {
                m = Math.Max(m, v);
            }

            return m;
        }
    }

    /// <summary>
    /// Inertia roller dyno. The car is strapped in a gear, the throttle is held wide open and the rollers are
    /// accelerated; wheel power is derived from roller force and speed, engine power estimated with the
    /// drivetrain loss and corrected to standard conditions (SAE J1349 style factor).
    /// </summary>
    public static class DynoRun
    {
        /// <summary>Datalog channels recorded during a pull.</summary>
        public static readonly string[] Channels =
        {
            "time_s", "rpm", "speed_kmh", "torque_nm", "power_ps", "wheel_ps", "lambda", "lambda_target", "boost_kpa", "boost_target_kpa",
            "advance_deg", "knock_retard_deg", "knock_intensity", "map_kpa", "maf_gps", "iat_c", "ect_c", "egt_c", "injector_ms", "stft_pct",
        };

        /// <summary>Performs a wide open throttle pull in a gear from startRpm to endRpm.</summary>
        public static DynoResult Pull(Car car, int gear = 0, double startRpm = 2000, double endRpm = 0, double sampleHz = 20, double rollerMassKg = 1100)
        {
            EngineDefinition e = car.Definition.Engine;
            if (gear <= 0)
            {
                gear = Math.Min(car.Definition.GearRatios.Length, e.IsDiesel ? 4 : 3);
            }

            if (endRpm <= 0)
            {
                endRpm = Math.Min(car.Ecu.Calibration.Scalar(Ecu.EcuCalibration.Keys.RevLimit, e.RedlineRpm), e.RedlineRpm + 300) - 50;
            }

            var log = new Datalog(Channels);
            var result = new DynoResult(log);
            if (car.Engine.State.Rpm < 400 && !car.Start())
            {
                result.Aborted = true;
                result.EndReason = "el motor no arranca";
                return result;
            }

            double damageBefore = car.Engine.Damage.Worst();
            double ratio = car.Definition.GearRatios[gear - 1] * car.Definition.FinalDrive;
            double r = car.Definition.WheelRadiusM;
            car.Gear = gear;
            car.DynoRollerMassKg = rollerMassKg;

            // Stabilise at start rpm under the brake, then release and go WOT.
            car.Mode = LoadMode.DynoHoldRpm;
            car.DynoHoldRpm = startRpm;
            car.SetVehicleSpeed(startRpm / 60.0 * 2 * Math.PI / ratio * r * 3.6);
            car.Pedal = 0.2;
            car.RunFor(3);
            car.Pedal = 1.0;
            car.RunFor(1.0);
            car.Mode = LoadMode.DynoInertia;

            double dt = Car.DefaultDt;
            double sampleEvery = 1.0 / sampleHz;
            double acc = 0;
            double t = 0;
            double correction = CorrectionFactor(car.Environment.AmbientC, car.Environment.BaroKpa);
            double lastRpm = car.Engine.State.Rpm;
            double timeout = 60;
            while (t < timeout)
            {
                car.Step(dt);
                t += dt;
                acc += dt;
                EngineState s = car.Engine.State;
                if (s.Seized || car.Engine.Damage.RodFailed)
                {
                    result.Aborted = true;
                    result.EndReason = "rotura de motor (biela)";
                    break;
                }

                if (car.Engine.Damage.AnyPistonBroken)
                {
                    result.Aborted = true;
                    result.EndReason = "pistón roto: humo y pérdida de compresión";
                    break;
                }

                if (s.Rpm >= endRpm)
                {
                    result.EndReason = "fin de pasada";
                    break;
                }

                if (t > 3 && s.Rpm < lastRpm - 300)
                {
                    result.Aborted = true;
                    result.EndReason = "el motor pierde vueltas (corte/limp)";
                    break;
                }

                lastRpm = Math.Max(lastRpm, s.Rpm);
                if (acc >= sampleEvery)
                {
                    acc = 0;
                    double wheelKw = car.WheelForceN * car.VehicleSpeedKmh / 3.6 / 1000.0;
                    double engineKw = wheelKw / car.Definition.DrivetrainEfficiency * correction;
                    double engineNm = engineKw * 1000 / Math.Max(1, s.Rpm * 2 * Math.PI / 60);
                    double knock = 0;
                    foreach (double k in s.CylinderKnock)
                    {
                        knock = Math.Max(knock, k);
                    }

                    var l = car.Ecu.Live;
                    log.Add(t, s.Rpm, car.VehicleSpeedKmh, engineNm, Physics.KwToPs(engineKw), Physics.KwToPs(wheelKw), s.ExhaustLambda,
                        l.TryGetValue("lambda_target", out double lt) ? lt : 1, s.BoostKpa, l.TryGetValue("boost_target_kpa", out double bt) ? bt : 0,
                        s.SparkAdvanceDeg, car.Ecu.KnockRetard, knock, s.ManifoldKpa, s.MafFlowGps, s.ChargeAirC, s.CoolantC, s.ExhaustGasC,
                        car.Ecu.Outputs.InjectorPulseMs[0], car.Ecu.Stft * 100);
                    double ps = Physics.KwToPs(engineKw);
                    if (ps > result.PeakPowerPs)
                    {
                        result.PeakPowerPs = ps;
                        result.PeakPowerRpm = s.Rpm;
                    }

                    if (engineNm > result.PeakTorqueNm && s.Rpm > startRpm + 200)
                    {
                        result.PeakTorqueNm = engineNm;
                        result.PeakTorqueRpm = s.Rpm;
                    }

                    result.PeakWheelPs = Math.Max(result.PeakWheelPs, Physics.KwToPs(wheelKw));
                    result.MaxKnockRetard = Math.Max(result.MaxKnockRetard, car.Ecu.KnockRetard);
                    if (s.RelativeLoad > 0.8)
                    {
                        result.MinLambda = Math.Min(result.MinLambda, s.ExhaustLambda);
                        result.MaxLambdaUnderLoad = Math.Max(result.MaxLambdaUnderLoad, s.ExhaustLambda);
                    }
                }
            }

            if (t >= timeout && result.EndReason.Length == 0)
            {
                result.EndReason = "tiempo agotado";
            }

            // Coast down
            car.Pedal = 0;
            car.Mode = LoadMode.DynoHoldRpm;
            car.DynoHoldRpm = 1500;
            car.RunFor(2);
            car.Mode = LoadMode.Neutral;
            car.Gear = 0;
            car.SetVehicleSpeed(0);

            result.DamageDelta = car.Engine.Damage.Worst() - damageBefore;
            if (result.MaxKnockRetard > 3)
            {
                result.Warnings.Add($"Retardo por detonación de hasta {result.MaxKnockRetard:0.0}°: el avance es excesivo para este combustible.");
            }

            if (result.MaxLambdaUnderLoad > 0.95 && !e.IsDiesel)
            {
                result.Warnings.Add($"Mezcla pobre a plena carga (λ {result.MaxLambdaUnderLoad:0.00}): riesgo de fundir pistones.");
            }

            if (result.DamageDelta > 0.05)
            {
                result.Warnings.Add($"Daño acumulado durante la pasada: +{result.DamageDelta * 100:0} %.");
            }

            return result;
        }

        /// <summary>SAE J1349-like correction factor to 25 °C / 99 kPa dry air.</summary>
        public static double CorrectionFactor(double ambientC, double baroKpa)
        {
            double cf = 1.18 * (99.0 / baroKpa) * Math.Sqrt((ambientC + 273.15) / 298.15) - 0.18;
            return MathUtil.Clamp(cf, 0.9, 1.1);
        }
    }
}
