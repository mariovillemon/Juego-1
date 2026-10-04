using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Garage.Game;
using Garage.Sim.Dyno;
using Garage.Sim.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>
    /// Roller dyno monitor: move the car onto the rollers, run a pull in a gear (replayed in real time on the
    /// graph), compare with previous pulls, browse datalog channels and export CSV.
    /// </summary>
    public sealed class DynoPanel : CarToolPanel
    {
        private PlotTexture _plot, _channelPlot;
        private TMP_InputField _gear;
        private TextMeshProUGUI _summary;
        private RectTransform _channels;
        private DynoResult _shown;
        private float _replayT = -1;
        private string _channel = "lambda";

        protected override string Title => "Banco de potencia";

        protected override Vector2 SizeFraction => new Vector2(0.88f, 0.9f);

        protected override void Build()
        {
            RectTransform r = UiKit.Row(Body, "Controls");
            UiKit.Button(r, "Llevar el coche al banco", () =>
            {
                DynoBay bay = UnityEngine.Object.FindFirstObjectByType<DynoBay>();
                Ui.Toast(bay != null ? bay.MoveCarToDyno() : "No hay banco en esta escena.", bay != null);
            });
            UiKit.Size(UiKit.Label(r, "Marcha", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 70);
            _gear = UiKit.Input(r, "3", true, 60);
            UiKit.Button(r, "PASADA", Pull, UiTheme.ButtonPrimary);
            UiKit.Button(r, "Exportar CSV", Export);
            _summary = UiKit.Label(Body, "Potencia (rojo) y par (azul) frente a rpm. Las pasadas anteriores aparecen atenuadas.", UiTheme.FontSmall);
            UiKit.Size(_summary, 54);
            _plot = new PlotTexture(Body, 1000, 360, 320);
            _channels = UiKit.Row(Body, "Channels", 28, 3);
            _channelPlot = new PlotTexture(Body, 1000, 160, 140);
        }

        private void Pull()
        {
            if (Work == null)
            {
                Ui.Toast("No hay coche.", false);
                return;
            }

            CommandResult res = Work.RunDyno((int)UiKit.ParseNumber(_gear.text, 3));
            if (!res.Ok)
            {
                _summary.text = res.Message;
                return;
            }

            _shown = Work.DynoRuns[Work.DynoRuns.Count - 1];
            _summary.text = res.Message;
            _replayT = 0;
            UiKit.Clear(_channels);
            foreach (string ch in _shown.Log.Channels.Where(c => c != "time_s" && c != "rpm"))
            {
                string c = ch;
                Button b = UiKit.Button(_channels, c, () => { _channel = c; Draw(1f); });
                b.GetComponentInChildren<TextMeshProUGUI>().fontSize = 11;
            }
        }

        public override void Tick()
        {
            if (_shown == null || _replayT < 0)
            {
                return;
            }

            double duration = Math.Max(1, _shown.Log.Column("time_s").LastOrDefault());
            _replayT += Time.unscaledDeltaTime;
            float f = Mathf.Clamp01((float)(_replayT / Math.Min(duration, 8)));
            Draw(f);
            if (f >= 1)
            {
                _replayT = -1;
            }
        }

        private void Draw(float fraction)
        {
            if (_shown == null)
            {
                return;
            }

            double maxRpm = Work.Car.Definition.Engine.RedlineRpm + 500;
            double maxY = 50;
            foreach (DynoResult run in Work.DynoRuns)
            {
                maxY = Math.Max(maxY, Math.Max(run.Log.Column("power_ps").DefaultIfEmpty(0).Max(), run.Log.Column("torque_nm").DefaultIfEmpty(0).Max()));
            }

            maxY *= 1.1;
            _plot.Clear();
            foreach (DynoResult run in Work.DynoRuns)
            {
                if (run == _shown)
                {
                    continue;
                }

                _plot.Series(run.Log.Column("rpm"), run.Log.Column("power_ps"), 1000, maxRpm, 0, maxY, new Color(0.5f, 0.2f, 0.18f));
                _plot.Series(run.Log.Column("rpm"), run.Log.Column("torque_nm"), 1000, maxRpm, 0, maxY, new Color(0.18f, 0.3f, 0.5f));
            }

            int n = Mathf.Max(2, (int)(_shown.Log.Rows.Count * fraction));
            double[] rpm = _shown.Log.Column("rpm").Take(n).ToArray();
            _plot.Series(rpm, _shown.Log.Column("power_ps").Take(n).ToArray(), 1000, maxRpm, 0, maxY, new Color(1f, 0.3f, 0.2f));
            _plot.Series(rpm, _shown.Log.Column("torque_nm").Take(n).ToArray(), 1000, maxRpm, 0, maxY, new Color(0.3f, 0.7f, 1f));
            _plot.Apply();

            double[] ch = _shown.Log.Column(_channel).Take(n).ToArray();
            _channelPlot.Clear();
            if (ch.Length > 1)
            {
                double min = ch.Min(), max = ch.Max();
                if (max - min < 1e-6)
                {
                    max = min + 1;
                }

                _channelPlot.Series(rpm, ch, 1000, maxRpm, min, max, UiTheme.Accent);
                TitleLabel.text = $"Banco de potencia — canal {_channel}: {min:0.##}…{max:0.##}";
            }

            _channelPlot.Apply();
        }

        private void Export()
        {
            if (_shown == null)
            {
                Ui.Toast("Haz antes una pasada.", false);
                return;
            }

            string dir = Path.Combine(Application.persistentDataPath, "datalogs");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"dyno_{Work.Car.Definition.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            _shown.Log.SaveCsv(file);
            Ui.Toast("Datalog exportado: " + file, true, 10f);
        }
    }

    /// <summary>
    /// Workshop laptop with the calibration editor: coloured tables, cell and range edits, interpolation, undo,
    /// compare with stock, scalars and a 3D view of the selected map. Writing needs the flash interface (bought)
    /// connected to the car's OBD port.
    /// </summary>
    public sealed class EcuPanel : CarToolPanel
    {
        private RectTransform _tables, _grid, _scalars;
        private TextMeshProUGUI _sel, _status;
        private TMP_InputField _value;
        private PlotTexture _view3d;
        private string _table;
        private int _r0, _c0, _r1, _c1;
        private float _yaw = 35f;

        protected override string Title => "Portátil — editor de mapas ECU";

        protected override Vector2 SizeFraction => new Vector2(0.94f, 0.92f);

        protected override void Build()
        {
            _status = UiKit.Label(Body, "", UiTheme.FontSmall);
            UiKit.Size(_status, 26);
            RectTransform cols = UiKit.Row(Body, "Cols", 0, 10);
            UiKit.Size(cols, flexibleHeight: 1);
            RectTransform left = UiKit.Column(cols, "Left", 6);
            UiKit.Size(left, preferredWidth: 300, flexibleHeight: 1);
            _tables = UiKit.Scroll(left, "Tables");
            _scalars = UiKit.Scroll(left, "Scalars");
            RectTransform mid = UiKit.Column(cols, "Mid", 6);
            UiKit.Size(mid, flexibleWidth: 1, flexibleHeight: 1);
            _grid = UiKit.Rect(mid, "Grid");
            UiKit.Size(_grid, flexibleHeight: 1, flexibleWidth: 1);
            _sel = UiKit.Label(mid, "", UiTheme.FontSmall);
            UiKit.Size(_sel, 26);
            RectTransform edit = UiKit.Row(mid, "Edit");
            _value = UiKit.Input(edit, "0", true, 110);
            UiKit.Button(edit, "Fijar", () => Each((t, r, c) => Work.Ecu.SetCell(t, r, c, UiKit.ParseNumber(_value.text))));
            UiKit.Button(edit, "Sumar", () => Do(() => Work.Ecu.AddToRegion(_table, _r0, _r1, _c0, _c1, UiKit.ParseNumber(_value.text))));
            UiKit.Button(edit, "×Factor", () => Do(() => Work.Ecu.ScaleRegion(_table, _r0, _r1, _c0, _c1, UiKit.ParseNumber(_value.text, 1))));
            UiKit.Button(edit, "Interpolar", () => Do(() => Work.Ecu.Interpolate(_table, _r0, _r1, _c0, _c1)));
            RectTransform edit2 = UiKit.Row(mid, "Edit2");
            UiKit.Button(edit2, "Deshacer", () => Do(() => Work.Ecu.Undo()));
            UiKit.Button(edit2, "Comparar con serie", Compare);
            UiKit.Button(edit2, "Restaurar serie", () => Do(() => Work.Ecu.RestoreStock()), UiTheme.ButtonDanger);
            UiKit.Button(edit2, "Banco de potencia", () => Ui.Dyno.Open(), UiTheme.ButtonPrimary);
            RectTransform right = UiKit.Column(cols, "Right", 6);
            UiKit.Size(right, preferredWidth: 420, flexibleHeight: 1);
            _view3d = new PlotTexture(right, 600, 480, 360);
            RectTransform rot = UiKit.Row(right, "Rot");
            UiKit.Button(rot, "Girar <", () => { _yaw -= 15; Draw3D(); });
            UiKit.Button(rot, "Girar >", () => { _yaw += 15; Draw3D(); });
            MakeOutput(right, 120);
        }

        private void Do(Func<CommandResult> cmd)
        {
            if (Work == null || _table == null)
            {
                return;
            }

            Run(cmd);
            Refresh();
        }

        private void Each(Func<string, int, int, CommandResult> cmd)
        {
            if (Work == null || _table == null)
            {
                return;
            }

            CommandResult last = null;
            for (int r = Math.Min(_r0, _r1); r <= Math.Max(_r0, _r1); r++)
            {
                for (int c = Math.Min(_c0, _c1); c <= Math.Max(_c0, _c1); c++)
                {
                    last = cmd(_table, r, c);
                    if (!last.Ok)
                    {
                        break;
                    }
                }
            }

            if (last != null)
            {
                Output.text = last.Message;
            }

            Refresh();
        }

        private void Compare()
        {
            if (Work == null)
            {
                return;
            }

            Output.text = string.Join("\n", Work.Ecu.CompareWithStock().Where(d => d.ChangedCells > 0).Select(d => $"{d.Table}: {d.ChangedCells} celdas, máx. Δ {d.MaxDelta:0.###} {d.Unit}").DefaultIfEmpty("Calibración idéntica a la de serie."));
        }

        public override void Refresh()
        {
            if (Work == null)
            {
                _status.text = "No hay coche conectado.";
                return;
            }

            CommandResult can = Work.Ecu.CanWrite();
            _status.text = $"Calibración {Work.Ecu.Calibration.Id} · {(can.Ok ? "<color=#7c7>conectado: lectura/escritura</color>" : "<color=#fb0>SOLO LECTURA</color> — " + can.Message)} · niveles de deshacer {Work.Ecu.UndoDepth}";
            UiKit.Clear(_tables);
            foreach (string id in Work.Ecu.TableIds)
            {
                string t = id;
                UiKit.Button(_tables, $"{id} [{Work.Ecu.Table(id).Unit}]", () => { _table = t; _r0 = _c0 = _r1 = _c1 = 0; Refresh(); }, id == _table ? UiTheme.RowSelected : UiTheme.Row);
            }

            UiKit.Clear(_scalars);
            foreach (KeyValuePair<string, double> kv in Work.Ecu.Calibration.Scalars.OrderBy(k => k.Key))
            {
                string key = kv.Key;
                RectTransform row = UiKit.Row(_scalars, key, 28);
                UiKit.Size(UiKit.Label(row, $"{key} = {kv.Value:0.###}", UiTheme.FontSmall), flexibleWidth: 1);
                UiKit.Button(row, "Fijar", () => Do(() => Work.Ecu.SetScalar(key, UiKit.ParseNumber(_value.text, kv.Value))), UiTheme.Button, 70);
            }

            _table ??= Work.Ecu.TableIds.FirstOrDefault();
            BuildGrid();
            Draw3D();
        }

        private void BuildGrid()
        {
            UiKit.Clear(_grid);
            Map3D m = _table == null ? null : Work.Ecu.Table(_table);
            if (m == null)
            {
                return;
            }

            m.Range(out double min, out double max);
            GridLayoutGroup g = _grid.GetComponent<GridLayoutGroup>();
            if (g == null)
            {
                g = _grid.gameObject.AddComponent<GridLayoutGroup>();
            }

            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = m.X.Count + 1;
            g.spacing = new Vector2(2, 2);
            float w = Mathf.Clamp(820f / (m.X.Count + 1), 34, 70);
            g.cellSize = new Vector2(w, 24);
            Cell(m.Y.Name + "\\" + m.X.Name, Color.black, null, 9);
            for (int c = 0; c < m.X.Count; c++)
            {
                Cell(m.X[c].ToString("0.##"), UiTheme.Header, null, 11);
            }

            int rLo = Math.Min(_r0, _r1), rHi = Math.Max(_r0, _r1), cLo = Math.Min(_c0, _c1), cHi = Math.Max(_c0, _c1);
            for (int r = 0; r < m.Y.Count; r++)
            {
                Cell(m.Y[r].ToString("0.##"), UiTheme.Header, null, 11);
                for (int c = 0; c < m.X.Count; c++)
                {
                    int rr = r, cc = c;
                    double v = m[r, c];
                    float f = max - min < 1e-9 ? 0.5f : (float)((v - min) / (max - min));
                    Color col = Color.HSVToRGB(Mathf.Lerp(0.62f, 0.0f, f), 0.65f, 0.55f);
                    bool sel = r >= rLo && r <= rHi && c >= cLo && c <= cHi;
                    double stock = Work.Ecu.StockTable(_table)?[r, c] ?? v;
                    string text = (Math.Abs(v - stock) > 1e-9 ? "*" : "") + v.ToString(max > 50 ? "0" : "0.0#");
                    Cell(text, sel ? Color.Lerp(col, Color.white, 0.45f) : col, () => Pick(rr, cc), 11);
                }
            }

            _sel.text = $"Selección filas {rLo}–{rHi}, columnas {cLo}–{cHi} (clic = celda, Shift+clic = rango) · {m.Id} en {m.Unit} · * = modificado";
        }

        private void Cell(string text, Color color, UnityEngine.Events.UnityAction click, float size)
        {
            Image img = UiKit.Panel(_grid, "Cell", color);
            TextMeshProUGUI t = UiKit.Label(img.transform, text, size, Color.white, TextAlignmentOptions.Center);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.Fill(t.rectTransform, 1);
            if (click != null)
            {
                Button b = img.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(click);
            }
        }

        private void Pick(int r, int c)
        {
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (shift)
            {
                _r1 = r;
                _c1 = c;
            }
            else
            {
                _r0 = _r1 = r;
                _c0 = _c1 = c;
                _value.text = Work.Ecu.Table(_table)[r, c].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            }

            BuildGrid();
        }

        /// <summary>Isometric wireframe of the selected map.</summary>
        private void Draw3D()
        {
            _view3d.Clear();
            Map3D m = _table == null || Work == null ? null : Work.Ecu.Table(_table);
            if (m != null && m.X.Count > 1 && m.Y.Count > 1)
            {
                m.Range(out double min, out double max);
                double span = Math.Max(1e-9, max - min);
                float yaw = _yaw * Mathf.Deg2Rad;
                Vector2 P(int r, int c)
                {
                    float x = c / (float)(m.X.Count - 1) - 0.5f;
                    float z = r / (float)(m.Y.Count - 1) - 0.5f;
                    float h = (float)((m[r, c] - min) / span);
                    float rx = x * Mathf.Cos(yaw) - z * Mathf.Sin(yaw);
                    float rz = x * Mathf.Sin(yaw) + z * Mathf.Cos(yaw);
                    return new Vector2(_view3d.Width * (0.5f + rx * 0.75f), _view3d.Height * (0.22f + rz * 0.35f + h * 0.55f));
                }

                for (int r = 0; r < m.Y.Count; r++)
                {
                    for (int c = 0; c < m.X.Count; c++)
                    {
                        Vector2 a = P(r, c);
                        Color32 col = Color.HSVToRGB(Mathf.Lerp(0.62f, 0f, (float)((m[r, c] - min) / span)), 0.8f, 1f);
                        if (c + 1 < m.X.Count)
                        {
                            Vector2 b = P(r, c + 1);
                            _view3d.Line((int)a.x, (int)a.y, (int)b.x, (int)b.y, col);
                        }

                        if (r + 1 < m.Y.Count)
                        {
                            Vector2 b = P(r + 1, c);
                            _view3d.Line((int)a.x, (int)a.y, (int)b.x, (int)b.y, col);
                        }
                    }
                }
            }

            _view3d.Apply();
        }
    }
}
