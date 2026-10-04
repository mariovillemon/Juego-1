using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Sim.Ecu;
using Garage.Sim.Maps;

namespace Garage.Game
{
    /// <summary>One cell difference between the current and stock map.</summary>
    public readonly struct MapDiff
    {
        /// <summary>Creates a diff summary.</summary>
        public MapDiff(string table, int changedCells, double maxDelta, string unit)
        {
            Table = table;
            ChangedCells = changedCells;
            MaxDelta = maxDelta;
            Unit = unit;
        }

        /// <summary>Table id.</summary>
        public string Table { get; }

        /// <summary>Modified cells.</summary>
        public int ChangedCells { get; }

        /// <summary>Largest absolute change.</summary>
        public double MaxDelta { get; }

        /// <summary>Unit.</summary>
        public string Unit { get; }
    }

    /// <summary>
    /// Calibration editor for the car on the lift: cell and region edits, scalars, bilinear interpolation of a
    /// region, undo, compare with stock and restore. Writing needs the flash interface plugged into the OBD port.
    /// </summary>
    public sealed class EcuEditor
    {
        private readonly CarWork _w;
        private readonly Stack<EcuCalibration> _undo = new Stack<EcuCalibration>();

        internal EcuEditor(CarWork w)
        {
            _w = w;
        }

        /// <summary>Current calibration.</summary>
        public EcuCalibration Calibration => _w.Car.Ecu.Calibration;

        /// <summary>Table ids, sorted.</summary>
        public List<string> TableIds => Calibration.Tables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        /// <summary>Gets a table.</summary>
        public Map3D? Table(string id) => Calibration.Table(id);

        /// <summary>Stock version of a table.</summary>
        public Map3D? StockTable(string id) => _w.Car.StockCalibration.Table(id);

        /// <summary>Undo levels available.</summary>
        public int UndoDepth => _undo.Count;

        /// <summary>Whether writes are possible now (and why not).</summary>
        public CommandResult CanWrite()
        {
            if (!_w.Owns(CarWork.EcuFlash))
            {
                return CommandResult.Fail(CommandError.ToolLocked, "Necesitas la interfaz de reprogramación para escribir en la ECU.");
            }

            if (!_w.ScannerPlugged)
            {
                return CommandResult.Fail(CommandError.InvalidState, "Conecta el portátil al coche por el conector OBD.");
            }

            return CommandResult.Success("");
        }

        private CommandResult Write(string id, Action change)
        {
            CommandResult gate = CanWrite();
            if (!gate.Ok)
            {
                _w.Bus.Publish(GameEventKind.CommandFailed, "", gate.Message);
                return gate;
            }

            _undo.Push(Calibration.Clone());
            change();
            _w.Bus.Publish(GameEventKind.CalibrationChanged, id);
            return CommandResult.Success("Escrito en la ECU (flash OK, checksum corregido).");
        }

        /// <summary>Sets one cell.</summary>
        public CommandResult SetCell(string table, int row, int col, double value)
        {
            Map3D? t = Table(table);
            if (t == null || row < 0 || row >= t.Y.Count || col < 0 || col >= t.X.Count)
            {
                return CommandResult.Fail(CommandError.InvalidArgument, "Celda fuera de rango.");
            }

            return Write(table, () => Table(table)![row, col] = value);
        }

        /// <summary>Adds a value to a rectangular region.</summary>
        public CommandResult AddToRegion(string table, int r0, int r1, int c0, int c1, double delta)
        {
            Map3D? t = Table(table);
            if (t == null)
            {
                return CommandResult.Fail(CommandError.NotFound, "No existe esa tabla.");
            }

            Clamp(t, ref r0, ref r1, ref c0, ref c1);
            return Write(table, () => Table(table)!.AddToRegion(r0, r1, c0, c1, delta));
        }

        /// <summary>Multiplies a region by a factor (e.g. +5 % fuel).</summary>
        public CommandResult ScaleRegion(string table, int r0, int r1, int c0, int c1, double factor)
        {
            Map3D? t = Table(table);
            if (t == null)
            {
                return CommandResult.Fail(CommandError.NotFound, "No existe esa tabla.");
            }

            Clamp(t, ref r0, ref r1, ref c0, ref c1);
            return Write(table, () =>
            {
                Map3D m = Table(table)!;
                for (int r = r0; r <= r1; r++)
                {
                    for (int c = c0; c <= c1; c++)
                    {
                        m[r, c] *= factor;
                    }
                }
            });
        }

        /// <summary>Fills the inside of a region by bilinear interpolation of its four corners.</summary>
        public CommandResult Interpolate(string table, int r0, int r1, int c0, int c1)
        {
            Map3D? t = Table(table);
            if (t == null)
            {
                return CommandResult.Fail(CommandError.NotFound, "No existe esa tabla.");
            }

            Clamp(t, ref r0, ref r1, ref c0, ref c1);
            return Write(table, () =>
            {
                Map3D m = Table(table)!;
                double a = m[r0, c0], b = m[r0, c1], c = m[r1, c0], d = m[r1, c1];
                for (int r = r0; r <= r1; r++)
                {
                    double v = r1 == r0 ? 0 : (r - r0) / (double)(r1 - r0);
                    for (int col = c0; col <= c1; col++)
                    {
                        double u = c1 == c0 ? 0 : (col - c0) / (double)(c1 - c0);
                        m[r, col] = (1 - v) * ((1 - u) * a + u * b) + v * ((1 - u) * c + u * d);
                    }
                }
            });
        }

        /// <summary>Sets a scalar (limiters, injector size…).</summary>
        public CommandResult SetScalar(string id, double value)
        {
            if (!Calibration.Scalars.ContainsKey(id))
            {
                return CommandResult.Fail(CommandError.NotFound, "No existe ese escalar.");
            }

            return Write(id, () => Calibration.SetScalar(id, value));
        }

        /// <summary>Restores the stock calibration.</summary>
        public CommandResult RestoreStock() => Write("*", () => _w.Car.Ecu.Calibration = _w.Car.StockCalibration.Clone());

        /// <summary>Undoes the last write.</summary>
        public CommandResult Undo()
        {
            if (_undo.Count == 0)
            {
                return CommandResult.Fail(CommandError.InvalidState, "Nada que deshacer.");
            }

            CommandResult gate = CanWrite();
            if (!gate.Ok)
            {
                return gate;
            }

            _w.Car.Ecu.Calibration = _undo.Pop();
            _w.Bus.Publish(GameEventKind.CalibrationChanged, "undo");
            return CommandResult.Success("Deshecho: calibración anterior escrita en la ECU.");
        }

        /// <summary>Differences with stock, per table.</summary>
        public List<MapDiff> CompareWithStock()
        {
            var list = new List<MapDiff>();
            foreach (string id in TableIds)
            {
                Map3D a = Table(id)!;
                Map3D? b = StockTable(id);
                if (b == null)
                {
                    continue;
                }

                int changed = 0;
                double maxDelta = 0;
                for (int r = 0; r < a.Y.Count; r++)
                {
                    for (int c = 0; c < a.X.Count; c++)
                    {
                        double d = Math.Abs(a[r, c] - b[r, c]);
                        if (d > 1e-9)
                        {
                            changed++;
                            maxDelta = Math.Max(maxDelta, d);
                        }
                    }
                }

                list.Add(new MapDiff(id, changed, maxDelta, a.Unit));
            }

            return list;
        }

        private static void Clamp(Map3D t, ref int r0, ref int r1, ref int c0, ref int c1)
        {
            if (r1 < r0)
            {
                (r0, r1) = (r1, r0);
            }

            if (c1 < c0)
            {
                (c0, c1) = (c1, c0);
            }

            r0 = Math.Max(0, r0);
            c0 = Math.Max(0, c0);
            r1 = Math.Min(t.Y.Count - 1, r1);
            c1 = Math.Min(t.X.Count - 1, c1);
        }
    }
}
