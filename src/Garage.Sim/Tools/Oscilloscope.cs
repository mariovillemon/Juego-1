using System;
using System.Globalization;
using System.Text;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Electrical;
using Garage.Sim.Engine;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Tools
{
    /// <summary>A captured waveform.</summary>
    public sealed class Waveform
    {
        /// <summary>Creates a waveform.</summary>
        public Waveform(double[] timeMs, double[] volts, string description)
        {
            TimeMs = timeMs;
            Volts = volts;
            Description = description;
        }

        /// <summary>Sample times (ms).</summary>
        public double[] TimeMs { get; }

        /// <summary>Sample voltages.</summary>
        public double[] Volts { get; }

        /// <summary>What was captured.</summary>
        public string Description { get; }

        /// <summary>Peak voltage.</summary>
        public double Max
        {
            get
            {
                double m = double.MinValue;
                foreach (double v in Volts)
                {
                    m = Math.Max(m, v);
                }

                return m;
            }
        }

        /// <summary>Minimum voltage.</summary>
        public double Min
        {
            get
            {
                double m = double.MaxValue;
                foreach (double v in Volts)
                {
                    m = Math.Min(m, v);
                }

                return m;
            }
        }

        /// <summary>Renders an ASCII oscillogram (rows × cols).</summary>
        public string RenderAscii(int rows = 12, int cols = 72, double voltsPerDiv = 0)
        {
            double lo = Math.Min(0, Min);
            double hi = Max;
            if (voltsPerDiv > 0)
            {
                hi = lo + voltsPerDiv * 8;
            }

            if (hi - lo < 1e-6)
            {
                hi = lo + 1;
            }

            var grid = new char[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    grid[r, c] = (c % 9 == 0 || r == rows - 1) ? '·' : ' ';
                }
            }

            for (int c = 0; c < cols; c++)
            {
                int i0 = c * Volts.Length / cols;
                int i1 = Math.Max(i0 + 1, (c + 1) * Volts.Length / cols);
                double vmin = double.MaxValue;
                double vmax = double.MinValue;
                for (int i = i0; i < i1 && i < Volts.Length; i++)
                {
                    vmin = Math.Min(vmin, Volts[i]);
                    vmax = Math.Max(vmax, Volts[i]);
                }

                int rmin = (int)Math.Round((1 - MathUtil.Clamp01((vmax - lo) / (hi - lo))) * (rows - 1));
                int rmax = (int)Math.Round((1 - MathUtil.Clamp01((vmin - lo) / (hi - lo))) * (rows - 1));
                for (int r = rmin; r <= rmax; r++)
                {
                    grid[r, c] = '█';
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(Description);
            for (int r = 0; r < rows; r++)
            {
                double v = hi - (hi - lo) * r / (rows - 1);
                sb.Append(v.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(8)).Append(" |");
                for (int c = 0; c < cols; c++)
                {
                    sb.Append(grid[r, c]);
                }

                sb.AppendLine();
            }

            sb.Append("         0").Append(new string(' ', cols - 12)).Append(TimeMs[TimeMs.Length - 1].ToString("0", CultureInfo.InvariantCulture)).Append(" ms");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Oscilloscope. Fast signals (crank, cam, injector, coil) are synthesised from the circuit levels and the engine
    /// state; slow signals (O2, MAF, TPS, MAP, temperatures) are recorded by actually stepping the simulation.
    /// </summary>
    public sealed class Oscilloscope
    {
        private readonly DiagnosticSession _s;

        internal Oscilloscope(DiagnosticSession s)
        {
            _s = s;
        }

        /// <summary>Captures a waveform on a component pin (ECU side) over a window in ms.</summary>
        public Waveform Capture(string componentId, string pin = "signal", double windowMs = 20, int samples = 600)
        {
            Car car = _s.Car;
            Component? comp = car.Parts.Get(componentId);
            Circuit? c = car.CircuitOf(componentId);
            if (comp == null || c == null)
            {
                return new Waveform(new[] { 0.0, windowMs }, new[] { 0.0, 0.0 }, $"{componentId}: sin señal accesible");
            }

            _s.Charge(new ToolResult("", TimeCosts.ScopeCapture), $"Osciloscopio: {componentId}:{pin} {windowMs:0} ms");
            var t = new double[samples];
            var v = new double[samples];
            for (int i = 0; i < samples; i++)
            {
                t[i] = windowMs * i / (samples - 1);
            }

            string node = "E:" + pin;
            EngineState es = car.Engine.State;
            double rpm = es.Rpm;
            string desc = $"{comp.Name} — pin {Multimeter.RoleName(pin)} ({windowMs:0} ms)";

            if (windowMs >= 200)
            {
                // Real time recording.
                double dt = Car.DefaultDt;
                int steps = Math.Max(1, (int)Math.Round(windowMs / 1000.0 / dt));
                var rec = new double[steps];
                for (int k = 0; k < steps; k++)
                {
                    car.Step(dt);
                    c.Solve(car.Faults);
                    rec[k] = c.Voltage(node) - (c.Topology == CircuitTopology.Generator ? c.Voltage("E:signal_low") : 0);
                }

                for (int i = 0; i < samples; i++)
                {
                    int k = Math.Min(steps - 1, (int)(t[i] / windowMs * steps));
                    v[i] = rec[k];
                }

                return new Waveform(t, v, desc);
            }

            switch (comp.Kind)
            {
                case ComponentKind.CkpSensor:
                    {
                        c.Solve(car.Faults);
                        double amp = Math.Abs(c.EcuPinVoltage("signal") - c.EcuPinVoltage("signal_low"));
                        double degPerMs = rpm * 6 / 1000.0;
                        for (int i = 0; i < samples; i++)
                        {
                            double ang = (es.CrankAngleDeg + t[i] * degPerMs) % 360.0;
                            int tooth = (int)(ang / 6.0);
                            double phase = (ang % 6.0) / 6.0;
                            double val = Math.Sin(2 * Math.PI * phase);
                            if (tooth >= 58)
                            {
                                val = tooth == 59 && phase > 0.7 ? -1.8 * Math.Sin(Math.PI * (phase - 0.7) / 0.3) : 0.05 * Math.Sin(2 * Math.PI * phase);
                            }
                            else if (tooth == 0 && phase < 0.5)
                            {
                                val *= 1.6;
                            }

                            v[i] = amp * val + _noise() * 0.02;
                        }

                        desc += $" — rueda 60-2, {rpm:0} rpm, pico {amp:0.00} V";
                        break;
                    }

                case ComponentKind.CmpSensor:
                    {
                        double high = car.HallLevels(componentId, out double low);
                        double degPerMs = rpm * 6 / 1000.0;
                        for (int i = 0; i < samples; i++)
                        {
                            double ang = (es.CrankAngleDeg + t[i] * degPerMs) % 720.0;
                            v[i] = rpm < 1 ? high : ((ang < 360 || (ang > 540 && ang < 600)) ? high : low);
                        }

                        desc += $" — nivel alto {high:0.00} V, bajo {low:0.00} V";
                        break;
                    }

                case ComponentKind.Injector:
                case ComponentKind.IgnitionCoil:
                case ComponentKind.WastegateSolenoid:
                case ComponentKind.PurgeValve:
                    {
                        bool coil = comp.Kind == ComponentKind.IgnitionCoil;
                        car.ProbeLowSide(componentId, out _, out _, out _);
                        bool wasOn = c.DriverOn;
                        c.KeyOn = car.KeyOn;
                        c.BatteryVolts = car.BatteryVolts;
                        c.DriverOn = false;
                        c.Solve(car.Faults);
                        double off = c.Voltage(node);
                        c.DriverOn = true;
                        c.Solve(car.Faults);
                        double on = c.Voltage(node);
                        c.DriverOn = wasOn;
                        bool energized = car.ActuatorEnergized(componentId);
                        double cycleMs = rpm > 1 ? 120000.0 / rpm : windowMs * 2;
                        double onMs = coil ? 3.0 : Math.Max(0.1, car.Ecu.Outputs.InjectorPulseMs[Math.Max(0, comp.Cylinder)]);
                        if (comp.Kind == ComponentKind.WastegateSolenoid || comp.Kind == ComponentKind.PurgeValve)
                        {
                            cycleMs = comp.Kind == ComponentKind.WastegateSolenoid ? 1000.0 / 30 : 1000.0 / 16;
                            double duty = comp.Kind == ComponentKind.WastegateSolenoid ? car.Ecu.Outputs.WastegateDuty : car.Ecu.Outputs.PurgeDuty;
                            onMs = cycleMs * duty;
                        }

                        double shorted = car.Faults.Max(componentId, EffectKind.InternalShort);
                        double spike = energized ? (coil ? 380 * (1 - 0.7 * shorted) : 65 * (1 - 0.6 * shorted)) : 0;
                        double burn = coil ? 35 + 20 * MathUtil.Clamp01(car.Engine.Damage.PlugFouling) : 0;
                        int cyl = Math.Max(0, comp.Cylinder);
                        bool fired = coil && car.Engine.State.CylinderBurn[cyl] > 0.5;
                        double offset = cycleMs * 0.2;
                        double sampleMs = windowMs / (samples - 1);
                        for (int i = 0; i < samples; i++)
                        {
                            // Peak detect: keep the sub-sample furthest from the rest level (like a DSO in peak mode).
                            double best = off;
                            for (int k = 0; k < 6; k++)
                            {
                                double cand = Eval((t[i] + k * sampleMs / 6 + offset) % cycleMs);
                                if (Math.Abs(cand - off) > Math.Abs(best - off))
                                {
                                    best = cand;
                                }
                            }

                            v[i] = best + _noise() * 0.05;
                        }

                        double Eval(double tc)
                        {
                            double val = off;
                            if (rpm > 1 || comp.Kind == ComponentKind.WastegateSolenoid || comp.Kind == ComponentKind.PurgeValve)
                            {
                                if (tc < onMs)
                                {
                                    val = on;
                                }
                                else if (tc < onMs + 0.08)
                                {
                                    val = off + spike;
                                }
                                else if (coil && tc < onMs + 1.6 && energized)
                                {
                                    val = off + (fired ? burn : burn * 2.5) * (1 + 0.1 * Math.Sin(tc * 40));
                                }
                                else if (tc < onMs + 2.2 && energized)
                                {
                                    val = off + (coil ? 15 : 3) * Math.Exp(-(tc - onMs) * 3) * Math.Sin((tc - onMs) * 30);
                                }
                            }

                            return val;
                        }

                        desc += $" — reposo {off:0.0} V, activado {on:0.0} V, pico {(spike > 0 ? off + spike : off):0} V, apertura {onMs:0.00} ms";
                        break;
                    }

                default:
                    {
                        c.Solve(car.Faults);
                        double baseV = c.Voltage(node);
                        for (int i = 0; i < samples; i++)
                        {
                            v[i] = baseV + _noise() * 0.01;
                        }

                        desc += " (señal lenta: use ventana ≥ 200 ms)";
                        break;
                    }
            }

            return new Waveform(t, v, desc);
        }

        private double _noise() => _s.Car.Rng.NextDouble() - 0.5;
    }
}
