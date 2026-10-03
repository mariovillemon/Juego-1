using System;
using System.Collections.Generic;

namespace Garage.Sim.Electrical
{
    /// <summary>
    /// Linear DC resistive network solved by nodal analysis. Voltage sources are modelled as
    /// Norton equivalents (source + series resistance). Node 0 is chassis ground.
    /// Used for every sensor/actuator circuit so that the ECU and the multimeter read real node voltages.
    /// </summary>
    public sealed class ResistiveNetwork
    {
        /// <summary>Resistance considered an open circuit (ohms).</summary>
        public const double Open = 1e10;

        private readonly List<string> _nodes = new List<string> { "GND" };
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal) { { "GND", 0 } };
        private readonly List<Branch> _branches = new List<Branch>();

        private struct Branch
        {
            public int A;
            public int B;
            public double R;
            public double V;
            public string Tag;
        }

        /// <summary>Node names.</summary>
        public IReadOnlyList<string> Nodes => _nodes;

        /// <summary>Gets or creates a node by name.</summary>
        public int Node(string name)
        {
            if (!_index.TryGetValue(name, out int i))
            {
                i = _nodes.Count;
                _nodes.Add(name);
                _index[name] = i;
            }

            return i;
        }

        /// <summary>True if the node exists.</summary>
        public bool HasNode(string name) => _index.ContainsKey(name);

        /// <summary>Adds a resistor between two nodes.</summary>
        public int AddResistor(string a, string b, double ohms, string tag = "") => AddSource(a, b, 0, ohms, tag);

        /// <summary>Adds a voltage source of value volts (a positive relative to b) with series resistance.</summary>
        public int AddSource(string a, string b, double volts, double seriesOhms, string tag = "")
        {
            _branches.Add(new Branch { A = Node(a), B = Node(b), R = Math.Max(1e-4, seriesOhms), V = volts, Tag = tag });
            return _branches.Count - 1;
        }

        /// <summary>Updates the source voltage of a branch.</summary>
        public void SetVoltage(int branch, double volts)
        {
            Branch b = _branches[branch];
            b.V = volts;
            _branches[branch] = b;
        }

        /// <summary>Updates the resistance of a branch.</summary>
        public void SetResistance(int branch, double ohms)
        {
            Branch b = _branches[branch];
            b.R = Math.Max(1e-4, ohms);
            _branches[branch] = b;
        }

        /// <summary>Solves node voltages. Optional extra current injection (amps) from node "from" to node "to" through the external world (used by the ohmmeter).</summary>
        public double[] Solve(int injectInto = -1, int injectOutOf = -1, double amps = 0, bool sourcesOff = false, int meterA = -1, int meterB = -1, double meterOhms = 0)
        {
            int n = _nodes.Count;
            var g = new double[n, n];
            var i = new double[n];
            foreach (Branch br in _branches)
            {
                double c = 1.0 / br.R;
                g[br.A, br.A] += c;
                g[br.B, br.B] += c;
                g[br.A, br.B] -= c;
                g[br.B, br.A] -= c;
                if (!sourcesOff && br.V != 0)
                {
                    double cur = br.V * c;
                    i[br.A] += cur;
                    i[br.B] -= cur;
                }
            }

            if (meterA >= 0 && meterB >= 0 && meterOhms > 0)
            {
                double c = 1.0 / meterOhms;
                g[meterA, meterA] += c;
                g[meterB, meterB] += c;
                g[meterA, meterB] -= c;
                g[meterB, meterA] -= c;
            }

            if (injectInto >= 0)
            {
                i[injectInto] += amps;
            }

            if (injectOutOf >= 0)
            {
                i[injectOutOf] -= amps;
            }

            // Leak every node weakly to ground so floating islands are defined (like real stray capacitance/leakage).
            for (int k = 0; k < n; k++)
            {
                g[k, k] += 1e-11;
            }

            int m = n - 1;
            var a = new double[m, m + 1];
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < m; c++)
                {
                    a[r, c] = g[r + 1, c + 1];
                }

                a[r, m] = i[r + 1];
            }

            var x = GaussSolve(a, m);
            var v = new double[n];
            for (int k = 0; k < m; k++)
            {
                v[k + 1] = x[k];
            }

            return v;
        }

        private static double[] GaussSolve(double[,] a, int m)
        {
            for (int col = 0; col < m; col++)
            {
                int pivot = col;
                double best = Math.Abs(a[col, col]);
                for (int r = col + 1; r < m; r++)
                {
                    double v = Math.Abs(a[r, col]);
                    if (v > best)
                    {
                        best = v;
                        pivot = r;
                    }
                }

                if (best < 1e-300)
                {
                    continue;
                }

                if (pivot != col)
                {
                    for (int c = col; c <= m; c++)
                    {
                        double t = a[col, c];
                        a[col, c] = a[pivot, c];
                        a[pivot, c] = t;
                    }
                }

                for (int r = col + 1; r < m; r++)
                {
                    double f = a[r, col] / a[col, col];
                    if (f == 0)
                    {
                        continue;
                    }

                    for (int c = col; c <= m; c++)
                    {
                        a[r, c] -= f * a[col, c];
                    }
                }
            }

            var x = new double[m];
            for (int r = m - 1; r >= 0; r--)
            {
                double s = a[r, m];
                for (int c = r + 1; c < m; c++)
                {
                    s -= a[r, c] * x[c];
                }

                x[r] = Math.Abs(a[r, r]) < 1e-300 ? 0 : s / a[r, r];
            }

            return x;
        }
    }
}
