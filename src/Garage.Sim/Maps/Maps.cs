using System;
using System.Collections.Generic;
using System.Globalization;
using Garage.Sim.Core;

namespace Garage.Sim.Maps
{
    /// <summary>Strictly increasing breakpoint axis of a calibration map.</summary>
    public sealed class Axis
    {
        private readonly double[] _values;

        /// <summary>Creates an axis. Values must be strictly increasing.</summary>
        public Axis(string name, string unit, IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0)
            {
                throw new ArgumentException("Axis needs at least one breakpoint.", nameof(values));
            }

            _values = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0 && values[i] <= values[i - 1])
                {
                    throw new ArgumentException($"Axis '{name}' must be strictly increasing (index {i}).", nameof(values));
                }

                _values[i] = values[i];
            }

            Name = name;
            Unit = unit;
        }

        /// <summary>Axis name (e.g. "rpm").</summary>
        public string Name { get; }

        /// <summary>Axis unit.</summary>
        public string Unit { get; }

        /// <summary>Number of breakpoints.</summary>
        public int Count => _values.Length;

        /// <summary>Breakpoint value.</summary>
        public double this[int i] => _values[i];

        /// <summary>Copy of the breakpoints.</summary>
        public double[] ToArray() => (double[])_values.Clone();

        /// <summary>Finds the lower index and the fractional position for interpolation (saturates at edges).</summary>
        public void Locate(double v, out int index, out double frac)
        {
            if (_values.Length == 1 || v <= _values[0])
            {
                index = 0;
                frac = 0;
                return;
            }

            if (v >= _values[_values.Length - 1])
            {
                index = _values.Length - 2;
                frac = 1;
                return;
            }

            int lo = 0;
            int hi = _values.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_values[mid] <= v)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            index = lo;
            frac = (v - _values[lo]) / (_values[lo + 1] - _values[lo]);
        }

        /// <summary>Index of the breakpoint closest to v.</summary>
        public int Nearest(double v)
        {
            int best = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < _values.Length; i++)
            {
                double d = Math.Abs(_values[i] - v);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }
    }

    /// <summary>One dimensional calibration curve (e.g. coolant temperature correction).</summary>
    public sealed class Map2D
    {
        private readonly double[] _values;

        /// <summary>Creates a curve.</summary>
        public Map2D(string id, string unit, Axis x, IReadOnlyList<double> values)
        {
            if (values.Count != x.Count)
            {
                throw new ArgumentException($"Map '{id}' has {values.Count} values but axis has {x.Count}.");
            }

            Id = id;
            Unit = unit;
            X = x;
            _values = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                _values[i] = values[i];
            }
        }

        /// <summary>Identifier.</summary>
        public string Id { get; }

        /// <summary>Output unit.</summary>
        public string Unit { get; }

        /// <summary>Input axis.</summary>
        public Axis X { get; }

        /// <summary>Raw cell access.</summary>
        public double this[int i]
        {
            get => _values[i];
            set => _values[i] = value;
        }

        /// <summary>Interpolated lookup.</summary>
        public double Lookup(double x)
        {
            if (_values.Length == 1)
            {
                return _values[0];
            }

            X.Locate(x, out int i, out double f);
            return MathUtil.Lerp(_values[i], _values[i + 1], f);
        }

        /// <summary>Deep copy.</summary>
        public Map2D Clone() => new Map2D(Id, Unit, X, _values);

        /// <summary>Copy of the values.</summary>
        public double[] ToArray() => (double[])_values.Clone();
    }

    /// <summary>
    /// Two dimensional calibration table (rows = Y axis, typically load; columns = X axis, typically rpm)
    /// with bilinear interpolation, like the tables of a real ECU.
    /// </summary>
    public sealed class Map3D
    {
        private readonly double[,] _cells;

        /// <summary>Creates a table; values are row major [y, x].</summary>
        public Map3D(string id, string unit, Axis x, Axis y, double[,] values)
        {
            if (values.GetLength(0) != y.Count || values.GetLength(1) != x.Count)
            {
                throw new ArgumentException($"Map '{id}' size {values.GetLength(0)}x{values.GetLength(1)} does not match axes {y.Count}x{x.Count}.");
            }

            Id = id;
            Unit = unit;
            X = x;
            Y = y;
            _cells = (double[,])values.Clone();
        }

        /// <summary>Identifier (e.g. "ignition_advance").</summary>
        public string Id { get; }

        /// <summary>Output unit.</summary>
        public string Unit { get; }

        /// <summary>Column axis.</summary>
        public Axis X { get; }

        /// <summary>Row axis.</summary>
        public Axis Y { get; }

        /// <summary>Raw cell access [row, column].</summary>
        public double this[int row, int col]
        {
            get => _cells[row, col];
            set => _cells[row, col] = value;
        }

        /// <summary>Bilinear interpolated lookup.</summary>
        public double Lookup(double x, double y)
        {
            X.Locate(x, out int xi, out double xf);
            Y.Locate(y, out int yi, out double yf);
            int xi1 = Math.Min(xi + 1, X.Count - 1);
            int yi1 = Math.Min(yi + 1, Y.Count - 1);
            double a = MathUtil.Lerp(_cells[yi, xi], _cells[yi, xi1], xf);
            double b = MathUtil.Lerp(_cells[yi1, xi], _cells[yi1, xi1], xf);
            return MathUtil.Lerp(a, b, yf);
        }

        /// <summary>Minimum and maximum cell values.</summary>
        public void Range(out double min, out double max)
        {
            min = double.MaxValue;
            max = double.MinValue;
            foreach (double v in _cells)
            {
                if (v < min)
                {
                    min = v;
                }

                if (v > max)
                {
                    max = v;
                }
            }
        }

        /// <summary>Adds a constant to every cell in a rectangular region.</summary>
        public void AddToRegion(int row0, int row1, int col0, int col1, double delta)
        {
            for (int r = Math.Max(0, row0); r <= Math.Min(Y.Count - 1, row1); r++)
            {
                for (int c = Math.Max(0, col0); c <= Math.Min(X.Count - 1, col1); c++)
                {
                    _cells[r, c] += delta;
                }
            }
        }

        /// <summary>Deep copy.</summary>
        public Map3D Clone() => new Map3D(Id, Unit, X, Y, _cells);

        /// <summary>Copy of the cells.</summary>
        public double[,] ToArray() => (double[,])_cells.Clone();

        /// <summary>Human readable table (for logs and the CLI).</summary>
        public string Format(string numberFormat = "0.0")
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Y.Name.PadRight(8)).Append('|');
            for (int c = 0; c < X.Count; c++)
            {
                sb.Append(X[c].ToString("0", CultureInfo.InvariantCulture).PadLeft(7));
            }

            sb.AppendLine();
            for (int r = 0; r < Y.Count; r++)
            {
                sb.Append(Y[r].ToString("0.##", CultureInfo.InvariantCulture).PadLeft(8)).Append('|');
                for (int c = 0; c < X.Count; c++)
                {
                    sb.Append(_cells[r, c].ToString(numberFormat, CultureInfo.InvariantCulture).PadLeft(7));
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
