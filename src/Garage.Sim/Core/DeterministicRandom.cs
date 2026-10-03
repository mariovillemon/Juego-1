using System;

namespace Garage.Sim.Core
{
    /// <summary>
    /// Seedable xorshift128+ pseudo random generator. Platform independent and
    /// bit-exact across runtimes, so simulations are reproducible in tests and in Unity.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _s0;
        private ulong _s1;
        private double? _spareGaussian;

        /// <summary>Creates a generator from a seed. The same seed always yields the same sequence.</summary>
        public DeterministicRandom(ulong seed)
        {
            Seed = seed;
            _s0 = SplitMix(ref seed);
            _s1 = SplitMix(ref seed);
            if (_s0 == 0 && _s1 == 0)
            {
                _s1 = 1;
            }
        }

        /// <summary>The seed this generator was created with.</summary>
        public ulong Seed { get; }

        private static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Returns the next raw 64 bit value.</summary>
        public ulong NextULong()
        {
            ulong x = _s0;
            ulong y = _s1;
            _s0 = y;
            x ^= x << 23;
            _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return _s1 + y;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform double in [min, max).</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Uniform integer in [min, maxExclusive).</summary>
        public int Next(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
            {
                return min;
            }

            return min + (int)(NextULong() % (ulong)(maxExclusive - min));
        }

        /// <summary>Returns true with the given probability.</summary>
        public bool Chance(double probability) => NextDouble() < probability;

        /// <summary>Normally distributed value (Box-Muller).</summary>
        public double Gaussian(double mean = 0.0, double stdDev = 1.0)
        {
            if (_spareGaussian.HasValue)
            {
                double s = _spareGaussian.Value;
                _spareGaussian = null;
                return mean + stdDev * s;
            }

            double u1 = 1.0 - NextDouble();
            double u2 = NextDouble();
            double mag = Math.Sqrt(-2.0 * Math.Log(u1));
            _spareGaussian = mag * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * mag * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>Picks a random element.</summary>
        public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> items)
        {
            if (items.Count == 0)
            {
                throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
            }

            return items[Next(0, items.Count)];
        }

        /// <summary>Creates an independent child generator derived from this one and a salt.</summary>
        public DeterministicRandom Fork(ulong salt) => new DeterministicRandom(NextULong() ^ (salt * 0x9E3779B97F4A7C15UL));
    }
}
