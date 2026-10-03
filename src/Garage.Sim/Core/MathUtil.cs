using System;

namespace Garage.Sim.Core
{
    /// <summary>Small numeric helpers used across the simulation.</summary>
    public static class MathUtil
    {
        /// <summary>Clamps a value to [min, max].</summary>
        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        /// <summary>Clamps a value to [0, 1].</summary>
        public static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

        /// <summary>Linear interpolation.</summary>
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>Inverse linear interpolation, clamped to [0,1].</summary>
        public static double InverseLerp(double a, double b, double v) => Math.Abs(b - a) < 1e-12 ? 0.0 : Clamp01((v - a) / (b - a));

        /// <summary>Maps v from [a0,a1] to [b0,b1] clamped.</summary>
        public static double Remap(double v, double a0, double a1, double b0, double b1) => Lerp(b0, b1, InverseLerp(a0, a1, v));

        /// <summary>First order low pass filter step: moves current toward target with time constant tau.</summary>
        public static double FirstOrder(double current, double target, double tau, double dt)
        {
            if (tau <= 1e-9)
            {
                return target;
            }

            double alpha = 1.0 - Math.Exp(-dt / tau);
            return current + (target - current) * alpha;
        }

        /// <summary>Smooth step between edge0 and edge1.</summary>
        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = InverseLerp(edge0, edge1, x);
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>Approximate equality.</summary>
        public static bool Near(double a, double b, double eps = 1e-6) => Math.Abs(a - b) <= eps;
    }

    /// <summary>Physical constants and conversions (SI units unless stated).</summary>
    public static class Physics
    {
        /// <summary>Specific gas constant of dry air, J/(kg·K).</summary>
        public const double RAir = 287.05;

        /// <summary>Ratio of specific heats of air.</summary>
        public const double Gamma = 1.4;

        /// <summary>Standard atmospheric pressure, kPa.</summary>
        public const double StandardBaroKpa = 101.325;

        /// <summary>Lower heating value of gasoline, J/kg (Heywood: 43–44 MJ/kg).</summary>
        public const double LhvGasoline = 43.4e6;

        /// <summary>Lower heating value of diesel, J/kg.</summary>
        public const double LhvDiesel = 42.8e6;

        /// <summary>Stoichiometric AFR of gasoline (E0).</summary>
        public const double AfrGasoline = 14.7;

        /// <summary>Stoichiometric AFR of diesel.</summary>
        public const double AfrDiesel = 14.5;

        /// <summary>Gasoline density, kg/L.</summary>
        public const double GasolineDensity = 0.745;

        /// <summary>Diesel density, kg/L.</summary>
        public const double DieselDensity = 0.835;

        /// <summary>Kelvin offset.</summary>
        public const double KelvinOffset = 273.15;

        /// <summary>Converts Celsius to Kelvin.</summary>
        public static double ToKelvin(double celsius) => celsius + KelvinOffset;

        /// <summary>Air density from pressure (kPa) and temperature (°C), kg/m³.</summary>
        public static double AirDensity(double pressureKpa, double tempC) => pressureKpa * 1000.0 / (RAir * ToKelvin(tempC));

        /// <summary>Power in kW from torque (N·m) and rpm.</summary>
        public static double PowerKw(double torqueNm, double rpm) => torqueNm * rpm * 2.0 * Math.PI / 60.0 / 1000.0;

        /// <summary>kW to metric horsepower (CV/PS).</summary>
        public static double KwToPs(double kw) => kw / 0.73549875;

        /// <summary>
        /// Compressible flow through an orifice (kg/s). Upstream pressure p0 (kPa), temperature t0 (°C),
        /// downstream p (kPa), effective area (m²), discharge coefficient included in area.
        /// </summary>
        public static double OrificeFlow(double areaM2, double p0Kpa, double t0C, double pKpa)
        {
            if (areaM2 <= 0 || p0Kpa <= 0)
            {
                return 0;
            }

            bool reverse = pKpa > p0Kpa;
            double up = reverse ? pKpa : p0Kpa;
            double down = reverse ? p0Kpa : pKpa;
            double pr = down / up;
            const double critical = 0.528;
            double psi;
            if (pr <= critical)
            {
                psi = Math.Sqrt(Gamma) * Math.Pow(2.0 / (Gamma + 1.0), (Gamma + 1.0) / (2.0 * (Gamma - 1.0)));
            }
            else
            {
                psi = Math.Sqrt(2.0 * Gamma / (Gamma - 1.0) * (Math.Pow(pr, 2.0 / Gamma) - Math.Pow(pr, (Gamma + 1.0) / Gamma)));
            }

            double flow = areaM2 * up * 1000.0 / Math.Sqrt(RAir * ToKelvin(t0C)) * psi;
            return reverse ? -flow : flow;
        }
    }
}

namespace Garage.Sim.Core
{
    /// <summary>Stable (process independent) hashing, unlike string.GetHashCode.</summary>
    public static class StableHash
    {
        /// <summary>FNV-1a 32 bit hash of a string.</summary>
        public static uint Of(string s)
        {
            uint h = 2166136261;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619;
            }

            return h;
        }
    }
}
