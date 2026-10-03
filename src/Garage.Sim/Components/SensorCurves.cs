using System;
using Garage.Sim.Core;

namespace Garage.Sim.Components
{
    /// <summary>
    /// Physical-to-electrical transfer functions of the simulated sensors and their inverses
    /// (what the ECU uses to convert a pin voltage back to a physical value).
    /// Orders of magnitude follow common OEM parts (Bosch NTC M12, 1/3-bar MAP, HFM hot film MAF,
    /// zirconia narrowband, LSU 4.9 wideband); see docs/DECISIONS.md.
    /// </summary>
    public static class SensorCurves
    {
        /// <summary>Default NTC resistance at 25 °C (ohms).</summary>
        public const double NtcR25 = 2000.0;

        /// <summary>Default NTC Beta (K).</summary>
        public const double NtcBeta = 3450.0;

        /// <summary>ECU pull-up resistor of thermistor inputs (ohms).</summary>
        public const double NtcPullUp = 2490.0;

        /// <summary>Reference supply.</summary>
        public const double Vref = 5.0;

        /// <summary>NTC resistance for a temperature.</summary>
        public static double NtcResistance(double tempC, double r25 = NtcR25, double beta = NtcBeta)
        {
            double t = Physics.ToKelvin(tempC);
            return r25 * Math.Exp(beta * (1.0 / t - 1.0 / 298.15));
        }

        /// <summary>NTC temperature for a resistance.</summary>
        public static double NtcTemperature(double ohms, double r25 = NtcR25, double beta = NtcBeta)
        {
            if (ohms <= 0)
            {
                return 200;
            }

            double invT = 1.0 / 298.15 + Math.Log(ohms / r25) / beta;
            return 1.0 / invT - Physics.KelvinOffset;
        }

        /// <summary>Voltage at the ECU pin of a thermistor divider.</summary>
        public static double NtcVoltage(double ohms, double pullUp = NtcPullUp) => Vref * ohms / (ohms + pullUp);

        /// <summary>Thermistor resistance from the divider voltage (ECU side inverse).</summary>
        public static double NtcOhmsFromVoltage(double v, double pullUp = NtcPullUp)
        {
            v = MathUtil.Clamp(v, 0.001, Vref - 0.001);
            return pullUp * v / (Vref - v);
        }

        /// <summary>Linear 0.5–4.5 V ratiometric sensor: output ratio (0..1 of Vref) for a physical value.</summary>
        public static double LinearRatio(double value, double min, double max) => (0.5 + 4.0 * (value - min) / (max - min)) / Vref;

        /// <summary>Inverse of <see cref="LinearRatio"/> given a voltage (assuming 5 V reference).</summary>
        public static double LinearValue(double volts, double min, double max) => min + (volts - 0.5) / 4.0 * (max - min);

        /// <summary>Hot-film MAF output voltage for an air flow (g/s). Non linear, ~1 V at zero flow.</summary>
        public static double MafVoltage(double gramsPerSecond, double maxGps)
        {
            double x = MathUtil.Clamp(gramsPerSecond / maxGps, 0, 1.2);
            return MathUtil.Clamp(1.0 + 3.8 * Math.Pow(x, 0.45), 0, 5.0);
        }

        /// <summary>Inverse of <see cref="MafVoltage"/>.</summary>
        public static double MafFlow(double volts, double maxGps)
        {
            if (volts <= 1.0)
            {
                return 0;
            }

            return maxGps * Math.Pow((volts - 1.0) / 3.8, 1.0 / 0.45);
        }

        /// <summary>Narrowband zirconia (Nernst) output voltage. Only valid when hot (&gt;~350 °C).</summary>
        public static double NarrowbandVoltage(double lambda, double sensorTempC)
        {
            double activity = MathUtil.SmoothStep(250, 400, sensorTempC);
            double v = 0.08 + 0.82 / (1.0 + Math.Exp((lambda - 1.0) * 70.0));
            return MathUtil.Lerp(0.45, v, activity);
        }

        /// <summary>Internal resistance of a zirconia cell vs temperature (ohms).</summary>
        public static double ZirconiaInternalResistance(double sensorTempC) => 80.0 + 2.0e6 * Math.Exp(-(sensorTempC - 20.0) / 45.0);

        private static readonly double[] LsuLambda = { 0.65, 0.70, 0.80, 0.90, 1.00, 1.10, 1.20, 1.43, 1.70, 2.42, 10.0 };
        private static readonly double[] LsuIpMa = { -2.22, -1.82, -1.11, -0.50, 0.00, 0.33, 0.60, 0.94, 1.24, 1.66, 2.54 };

        /// <summary>Wideband pump current (mA) for a lambda (LSU 4.9 characteristic, approximate).</summary>
        public static double WidebandPumpCurrent(double lambda) => Interp(LsuLambda, LsuIpMa, lambda);

        /// <summary>Lambda for a pump current (inverse table).</summary>
        public static double WidebandLambda(double ipMa) => Interp(LsuIpMa, LsuLambda, ipMa);

        /// <summary>Equivalent voltage of the ECU wideband interface (1.5 V at λ=1, ~1.05 V/mA).</summary>
        public static double WidebandVoltage(double lambda) => 1.5 + 1.052 * WidebandPumpCurrent(lambda);

        /// <summary>Lambda from the ECU wideband equivalent voltage.</summary>
        public static double WidebandLambdaFromVoltage(double volts) => WidebandLambda((volts - 1.5) / 1.052);

        /// <summary>Peak voltage of a variable reluctance crank sensor at a given rpm.</summary>
        public static double CkpPeakVoltage(double rpm, double airGapFactor = 1.0) => 0.0045 * rpm / Math.Max(0.2, airGapFactor);

        /// <summary>Knock sensor output (mV peak) for a mechanical noise level and knock intensity.</summary>
        public static double KnockSensorMillivolts(double rpm, double knockIntensity) => 20 + rpm * 0.02 + 900 * knockIntensity;

        private static double Interp(double[] xs, double[] ys, double x)
        {
            if (x <= xs[0])
            {
                return ys[0];
            }

            for (int i = 1; i < xs.Length; i++)
            {
                if (x <= xs[i])
                {
                    double t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
                    return ys[i - 1] + (ys[i] - ys[i - 1]) * t;
                }
            }

            return ys[ys.Length - 1];
        }
    }
}
