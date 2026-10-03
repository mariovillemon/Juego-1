using System;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Faults;

namespace Garage.Sim.Vehicle
{
    /// <summary>Battery and charging system.</summary>
    public sealed class ElectricalSystem
    {
        private readonly Car _car;

        /// <summary>Creates the system.</summary>
        public ElectricalSystem(Car car)
        {
            _car = car;
        }

        /// <summary>State of charge 0..1.</summary>
        public double StateOfCharge { get; set; } = 0.9;

        /// <summary>System voltage at the ECU.</summary>
        public double SystemVolts { get; private set; } = 12.6;

        /// <summary>Alternator output current (A).</summary>
        public double AlternatorAmps { get; private set; }

        /// <summary>Total load current (A).</summary>
        public double LoadAmps { get; private set; }

        /// <summary>Open circuit battery voltage.</summary>
        public double OpenCircuitVolts => 11.85 + 0.85 * MathUtil.Clamp01(StateOfCharge);

        /// <summary>Battery internal resistance (ohms).</summary>
        public double InternalOhms
        {
            get
            {
                Component? b = _car.Parts.Find(ComponentKind.Battery);
                double r = 0.012;
                if (b != null)
                {
                    r *= 1 + 5 * _car.Faults.Max(b.Id, EffectKind.Weak) + 2 * (1 - b.Health);
                    if (_car.Faults.Has(b.Id, EffectKind.Dead))
                    {
                        r = 1.0;
                    }
                }

                Component? g = _car.Parts.Find(ComponentKind.GroundStrap);
                if (g != null)
                {
                    r += _car.Faults.Sum(g.Id, EffectKind.WireHighResistance) + _car.Faults.Sum(g.Id, EffectKind.ConnectorCorrosion);
                }

                return r;
            }
        }

        /// <summary>Updates voltage and charge.</summary>
        public void Step(double dt, bool keyOn, bool cranking, bool engineRunning, double rpm, bool fanOn, bool pumpOn)
        {
            double load = 0;
            if (keyOn)
            {
                load += 12 + (fanOn ? 18 : 0) + (pumpOn ? 7 : 0);
            }

            if (cranking)
            {
                load += 160;
            }

            LoadAmps = load;
            Component? alt = _car.Parts.Find(ComponentKind.Alternator);
            double altMax = 0;
            if (engineRunning && rpm > 600)
            {
                altMax = 110 * MathUtil.Clamp01((rpm - 600) / 1400.0 + 0.4);
                if (alt != null)
                {
                    altMax *= 1 - _car.Faults.Max(alt.Id, EffectKind.Weak) - (_car.Faults.Has(alt.Id, EffectKind.Dead) ? 1 : 0);
                    altMax *= 0.7 + 0.3 * alt.Health;
                }

                altMax = Math.Max(0, altMax);
            }

            double regulator = 14.3;
            if (alt != null && _car.Faults.Has(alt.Id, EffectKind.StuckOpen))
            {
                regulator = 16.4; // regulator failed full field → overcharge
            }

            double ocv = OpenCircuitVolts;
            double rint = InternalOhms;
            double v;
            if (altMax > 0)
            {
                // Alternator tries to hold regulator voltage; limited by its current capability.
                double needed = load + (regulator - ocv) / rint;
                double current = Math.Min(altMax, Math.Max(0, needed));
                AlternatorAmps = current;
                v = ocv + (current - load) * rint;
                v = Math.Min(v, regulator);
            }
            else
            {
                AlternatorAmps = 0;
                v = ocv - load * rint;
            }

            SystemVolts = Math.Max(0, v);
            double netAmps = AlternatorAmps - load;
            StateOfCharge = MathUtil.Clamp01(StateOfCharge + netAmps * dt / (60 * 3600));
        }
    }
}
