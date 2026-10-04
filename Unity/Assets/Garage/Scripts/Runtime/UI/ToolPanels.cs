using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Game;
using Garage.Sim.Ecu;
using Garage.Sim.Electrical;
using Garage.Sim.Tools;
using Garage.Sim.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>Shared helpers for panels that act on the car on the lift.</summary>
    public abstract class CarToolPanel : UiPanel
    {
        protected CarWork Work => Runner.Work;

        protected Car Car => Runner.Car;

        /// <summary>Output console (monospace-ish text).</summary>
        protected TextMeshProUGUI Output;

        protected void MakeOutput(Transform parent, float minHeight = 200)
        {
            RectTransform scroll = UiKit.Scroll(parent, "Output");
            Output = UiKit.Label(scroll, "", UiTheme.FontSmall, new Color(0.75f, 0.95f, 0.8f));
            Output.alignment = TextAlignmentOptions.TopLeft;
            ContentSizeFitter f = Output.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiKit.Size(scroll.parent.parent, minHeight);
        }

        /// <summary>Runs a command and shows its text (errors also go to the feed).</summary>
        protected void Run(Func<CommandResult> cmd)
        {
            if (Work == null)
            {
                Ui.Toast("No hay coche en el elevador.", false);
                return;
            }

            CommandResult r = cmd();
            if (Output != null)
            {
                Output.text = r.Ok ? r.Message : $"<color={UiTheme.Hex(UiTheme.Bad)}>{r.Message}</color>";
            }
        }
    }

    /// <summary>
    /// Scan tool software (full screen view of the handheld): identification, read/clear codes, freeze frame, live
    /// data with a graph of any PID, readiness and actuator tests. Needs the tool plugged into the OBD port.
    /// </summary>
    public sealed class ScannerPanel : CarToolPanel
    {
        private PlotTexture _plot;
        private TextMeshProUGUI _live;
        private RectTransform _pidButtons;
        private readonly List<double> _history = new List<double>();
        private string _pidKey = "rpm";
        private bool _liveOn;
        private float _timer;

        protected override string Title => "Escáner OBD-II";

        protected override Vector2 SizeFraction => new Vector2(0.86f, 0.88f);

        protected override void Build()
        {
            RectTransform row = UiKit.Row(Body, "Commands");
            UiKit.Button(row, "Conectar", () => Run(() => Work.ScanConnect()));
            UiKit.Button(row, "Leer códigos", () => Run(() => Work.ReadCodes("ecm")));
            UiKit.Button(row, "Borrar códigos", () => Run(() => Work.ClearCodes("ecm")), UiTheme.ButtonDanger);
            UiKit.Button(row, "Freeze frame", () => Run(() => Work.FreezeFrame()));
            UiKit.Button(row, "Readiness", () => Run(() => Work.Readiness()));
            UiKit.Button(row, "Datos en vivo", ToggleLive, UiTheme.ButtonPrimary);
            RectTransform row2 = UiKit.Row(Body, "Modules");
            foreach (string m in new[] { "abs", "ipc", "bcm" })
            {
                string mod = m;
                UiKit.Button(row2, "Códigos " + m.ToUpperInvariant(), () => Run(() => Work.ReadCodes(mod)));
            }

            foreach (ActuatorTest t in Enum.GetValues(typeof(ActuatorTest)))
            {
                ActuatorTest test = t;
                UiKit.Button(row2, ActuatorName(t), () => Run(() => Work.ActuatorTest(test)));
            }

            RectTransform cols = UiKit.Row(Body, "Cols", 0, 10);
            UiKit.Size(cols, flexibleHeight: 1);
            RectTransform left = UiKit.Column(cols, "Left", 6);
            UiKit.Size(left, flexibleWidth: 1, flexibleHeight: 1);
            MakeOutput(left);
            RectTransform right = UiKit.Column(cols, "Right", 6);
            UiKit.Size(right, flexibleWidth: 1, flexibleHeight: 1);
            _plot = new PlotTexture(right, 800, 260, 230);
            _pidButtons = UiKit.Row(right, "Pids", 30, 4);
            _live = UiKit.Label(right, "", UiTheme.FontSmall, new Color(0.75f, 0.95f, 0.8f));
            _live.alignment = TextAlignmentOptions.TopLeft;
            UiKit.Size(_live, flexibleHeight: 1);
        }

        private static string ActuatorName(ActuatorTest t) => t switch
        {
            ActuatorTest.CylinderBalance => "Equilibrado cil.",
            ActuatorTest.Fan => "Ventilador",
            ActuatorTest.FuelPump => "Bomba comb.",
            ActuatorTest.Wastegate => "Wastegate",
            ActuatorTest.Purge => "Purga",
            _ => "Barrido mariposa",
        };

        private void ToggleLive()
        {
            if (!_liveOn)
            {
                CommandResult r = Work?.LiveData();
                if (r == null || !r.Ok)
                {
                    Run(() => r ?? CommandResult.Fail(CommandError.NoActiveJob, "No hay coche."));
                    return;
                }
            }

            _liveOn = !_liveOn;
            _history.Clear();
        }

        public override void Close()
        {
            base.Close();
            _liveOn = false;
        }

        public override void Tick()
        {
            if (!_liveOn || Work == null)
            {
                return;
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < 0.25f)
            {
                return;
            }

            _timer = 0;
            List<PidReading> values = Work.LiveValues();
            if (values.Count == 0)
            {
                _live.text = "SIN COMUNICACIÓN";
                return;
            }

            if (_pidButtons.childCount == 0)
            {
                foreach (PidReading p in values.Take(10))
                {
                    string key = p.Definition.Key;
                    Button b = UiKit.Button(_pidButtons, key, () => { _pidKey = key; _history.Clear(); });
                    b.GetComponentInChildren<TextMeshProUGUI>().fontSize = 12;
                }
            }

            _live.text = string.Join("\n", values.Select(r => $"{r.Definition.Name,-36} {ScanTool.FormatValue(r.Definition.Key, r.Value),9} {r.Definition.Unit}"));
            PidReading sel = values.FirstOrDefault(v => v.Definition.Key == _pidKey) ?? values[0];
            _history.Add(sel.Value);
            if (_history.Count > 240)
            {
                _history.RemoveAt(0);
            }

            double min = _history.Min(), max = _history.Max();
            if (max - min < 1e-6)
            {
                max = min + 1;
            }

            _plot.Clear();
            _plot.Series(_history, min - (max - min) * 0.1, max + (max - min) * 0.1, UiTheme.Accent);
            _plot.Apply();
            TitleLabel.text = $"Escáner OBD-II — gráfica: {sel.Definition.Name} ({min:0.##}…{max:0.##} {sel.Definition.Unit})";
        }
    }

    /// <summary>
    /// Digital multimeter: dial (function and range), the two probe points and the reading. Probe points are set
    /// from the connector pin view or here with the selectors (component, side, pin; or ground/battery).
    /// </summary>
    public sealed class MultimeterPanel : CarToolPanel
    {
        private TextMeshProUGUI _reading, _probes;
        private RectTransform _pins;
        private string _red = "bat", _black = "gnd";
        private string _component = "";
        private string _side = "harness";
        private bool _settingRed = true;

        protected override string Title => "Multímetro";

        protected override Vector2 SizeFraction => new Vector2(0.7f, 0.82f);

        protected override void Build()
        {
            _reading = UiKit.Label(Body, "----", 64, new Color(0.08f, 0.1f, 0.08f), TextAlignmentOptions.Center);
            Image lcd = _reading.gameObject.AddComponent<Image>();
            lcd.color = new Color(0.58f, 0.63f, 0.53f);
            UiKit.Size(_reading, 96);
            RectTransform dial = UiKit.Row(Body, "Dial");
            UiKit.Button(dial, "V DC", () => { Work?.SetMeterMode(MeterMode.DcVolts); Refresh(); });
            UiKit.Button(dial, "Ω", () => { Work?.SetMeterMode(MeterMode.Ohms); Refresh(); });
            UiKit.Button(dial, "Continuidad", () => { Work?.SetMeterMode(MeterMode.Continuity); Refresh(); });
            UiKit.Button(dial, "Escala +", () => Range(1));
            UiKit.Button(dial, "Escala -", () => Range(-1));
            _probes = UiKit.Label(Body, "", UiTheme.FontBody);
            UiKit.Size(_probes, 56);
            RectTransform which = UiKit.Row(Body, "Which");
            UiKit.Button(which, "Colocar punta ROJA", () => { _settingRed = true; Refresh(); }, new Color(0.45f, 0.1f, 0.1f));
            UiKit.Button(which, "Colocar punta NEGRA", () => { _settingRed = false; Refresh(); }, new Color(0.1f, 0.1f, 0.1f));
            UiKit.Button(which, "Masa (chasis)", () => SetProbe("gnd"));
            UiKit.Button(which, "Borne + batería", () => SetProbe("bat"));
            RectTransform side = UiKit.Row(Body, "Side");
            UiKit.Button(side, "Lado mazo (back-probe)", () => { _side = "harness"; Refresh(); });
            UiKit.Button(side, "Lado componente", () => { _side = "comp"; Refresh(); });
            UiKit.Button(side, "Lado ECU", () => { _side = "ecu"; Refresh(); });
            UiKit.Button(side, "Conectar/desconectar conector", () =>
            {
                if (Work != null && _component.Length > 0)
                {
                    Circuit c = Car.CircuitOf(_component);
                    Run(() => Work.SetConnector(_component, c != null && !c.ConnectorConnected));
                    Refresh();
                }
            });
            _pins = UiKit.Scroll(Body, "Pins");
            RectTransform act = UiKit.Row(Body, "Act");
            UiKit.Button(act, "MEDIR", Measure, UiTheme.ButtonPrimary);
            MakeOutput(Body, 70);
        }

        /// <summary>Opens the pin view of a component's connector (aimed at with the meter in hand).</summary>
        public void ShowConnector(string componentId)
        {
            _component = componentId ?? "";
            Open();
        }

        private void Range(int dir)
        {
            if (Work == null)
            {
                return;
            }

            MeterRange r = Work.Session.Multimeter.Range;
            int n = Enum.GetValues(typeof(MeterRange)).Length;
            Work.SetMeterRange((MeterRange)(((int)r + dir + n) % n));
            Refresh();
        }

        private void SetProbe(string point)
        {
            if (_settingRed)
            {
                _red = point;
                _settingRed = false;
            }
            else
            {
                _black = point;
            }

            Refresh();
        }

        private void Measure()
        {
            if (Work == null)
            {
                return;
            }

            CommandResult r = Work.Measure(_red, _black);
            int b = r.Message.IndexOf(']');
            int e = r.Message.IndexOf("   (", StringComparison.Ordinal);
            _reading.text = b >= 0 && e > b ? r.Message.Substring(b + 1, e - b - 1).Trim() : (r.Tool != null && !double.IsNaN(r.Tool.Value) ? r.Tool.Value.ToString("0.00") : "Err");
            Output.text = r.Message;
        }

        public override void Refresh()
        {
            if (Work == null)
            {
                _probes.text = "Sin coche.";
                return;
            }

            Multimeter m = Work.Session.Multimeter;
            _probes.text = $"Función <b>{m.Mode}</b> · escala <b>{m.Range}</b>\n<color=#e55>ROJA</color> {_red}    <color=#aaa>NEGRA</color> {_black}    — colocando: {(_settingRed ? "<color=#e55>roja</color>" : "negra")}";
            UiKit.Clear(_pins);
            Circuit c = _component.Length > 0 ? Car.CircuitOf(_component) : null;
            if (c == null)
            {
                foreach (KeyValuePair<string, Circuit> kv in Car.Circuits)
                {
                    string id = kv.Key;
                    UiKit.Button(_pins, $"{Car.Parts.Get(id)?.Name ?? id}", () => { _component = id; Refresh(); }, UiTheme.Row);
                }

                return;
            }

            UiKit.Button(_pins, $"< {Car.Parts.Get(_component)?.Name} — conector {(c.ConnectorConnected ? "conectado" : "DESCONECTADO")} (lado: {_side})", () => { _component = ""; Refresh(); }, UiTheme.RowSelected);
            foreach (PinInfo p in c.Pins)
            {
                string point = $"{_component}:{_side}:{p.Role}";
                UiKit.Button(_pins, $"Pin {p.ComponentPin} · {Multimeter.RoleName(p.Role)} · cable {p.WireColor} · ECU {p.EcuPin}", () => SetProbe(point), UiTheme.Row);
            }
        }
    }

    /// <summary>Wiring diagrams of the car on the lift (workshop laptop/tablet).</summary>
    public sealed class WiringPanel : CarToolPanel
    {
        private RectTransform _list;

        protected override string Title => "Esquemas eléctricos";

        protected override void Build()
        {
            RectTransform cols = UiKit.Row(Body, "Cols", 0, 10);
            UiKit.Size(cols, flexibleHeight: 1);
            _list = UiKit.Scroll(cols, "Components");
            UiKit.Size(_list.parent.parent, preferredWidth: 380);
            RectTransform right = UiKit.Column(cols, "Right", 6);
            UiKit.Size(right, flexibleWidth: 1, flexibleHeight: 1);
            MakeOutput(right, 400);
        }

        public override void Refresh()
        {
            UiKit.Clear(_list);
            if (Car == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Circuit> kv in Car.Circuits)
            {
                string id = kv.Key;
                UiKit.Button(_list, Car.Parts.Get(id)?.Name ?? id, () => Run(() => Work.WiringDiagram(id)), UiTheme.Row);
            }
        }
    }

    /// <summary>Engine and car actions (key, start, listen, road test, rev) and training reasoning.</summary>
    public sealed class CarPanel : CarToolPanel
    {
        protected override string Title => "Coche";

        protected override Vector2 SizeFraction => new Vector2(0.6f, 0.7f);

        protected override void Build()
        {
            RectTransform r1 = UiKit.Row(Body, "R1");
            UiKit.Button(r1, "Poner contacto", () => Run(() => Work.IgnitionOn()));
            UiKit.Button(r1, "Arrancar", () => Run(() => Work.Start()), UiTheme.ButtonPrimary);
            UiKit.Button(r1, "Parar (quitar contacto)", () => Run(() => Work.Stop()), UiTheme.ButtonDanger);
            RectTransform r2 = UiKit.Row(Body, "R2");
            UiKit.Button(r2, "Escuchar al ralentí (1 min)", () => Run(() => Work.ListenIdle()));
            UiKit.Button(r2, "Prueba de carretera", () => Run(() => Work.RoadTest()));
            UiKit.Button(r2, "Acelerón", () => Run(() => Work.Rev()));
            UiKit.Button(r2, "Rellenar refrigerante", () => Run(() => Runner.Session.RefillCoolant()));
            RectTransform r3 = UiKit.Row(Body, "R3");
            UiKit.Button(r3, "Razonamiento diagnóstico (formación)", () =>
            {
                List<string> a = Work?.Advice();
                Output.text = a == null || a.Count == 0 ? "Modo realista: sin pistas." : string.Join("\n", a.Select(x => "• " + x));
            });
            UiKit.Button(r3, "Esquemas eléctricos", () => Ui.Wiring.Open());
            UiKit.Button(r3, "Pruebas mecánicas", () => Ui.Mechanical.Open());
            MakeOutput(Body, 260);
        }

        public override void Refresh()
        {
            TitleLabel.text = Work == null ? "Coche" : Work.StatusLine;
        }
    }

    /// <summary>Mechanical tests (fuel pressure, compression, leak-down, smoke) and the oscilloscope.</summary>
    public sealed class MechanicalPanel : CarToolPanel
    {
        private PlotTexture _scope;
        private TMP_InputField _cyl, _comp, _pin, _window;

        protected override string Title => "Pruebas mecánicas y osciloscopio";

        protected override Vector2 SizeFraction => new Vector2(0.82f, 0.86f);

        protected override void Build()
        {
            RectTransform r1 = UiKit.Row(Body, "Fuel");
            UiKit.Size(UiKit.Label(r1, "Manómetro combustible:", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 200);
            UiKit.Button(r1, "Contacto", () => Run(() => Work.FuelPressure(FuelPressureMode.KeyOnPrime)));
            UiKit.Button(r1, "Ralentí", () => Run(() => Work.FuelPressure(FuelPressureMode.Idle)));
            UiKit.Button(r1, "Carga", () => Run(() => Work.FuelPressure(FuelPressureMode.Load)));
            UiKit.Button(r1, "Residual 5 min", () => Run(() => Work.FuelPressure(FuelPressureMode.Residual)));
            RectTransform r2 = UiKit.Row(Body, "Comp");
            UiKit.Size(UiKit.Label(r2, "Compresión / fugas:", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 200);
            UiKit.Button(r2, "En seco", () => Run(() => Work.CompressionTest(false)));
            UiKit.Button(r2, "En húmedo", () => Run(() => Work.CompressionTest(true)));
            _cyl = UiKit.Input(r2, "1", true, 70);
            UiKit.Button(r2, "Fugas cilindro", () => Run(() => Work.LeakDownTest((int)UiKit.ParseNumber(_cyl.text, 1))));
            RectTransform r3 = UiKit.Row(Body, "Smoke");
            UiKit.Size(UiKit.Label(r3, "Máquina de humo:", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 200);
            UiKit.Button(r3, "Admisión", () => Run(() => Work.SmokeTest(false)));
            UiKit.Button(r3, "Escape", () => Run(() => Work.SmokeTest(true)));
            UiKit.Button(r3, "Caída de tensión al arrancar", () => Run(() => Work.CrankingTest()));
            RectTransform r4 = UiKit.Row(Body, "Scope");
            UiKit.Size(UiKit.Label(r4, "Osciloscopio:", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 200);
            _comp = UiKit.Input(r4, "ckp", false, 140);
            _pin = UiKit.Input(r4, "signal", false, 140);
            _window = UiKit.Input(r4, "40", true, 90);
            UiKit.Button(r4, "Capturar", Capture, UiTheme.ButtonPrimary);
            _scope = new PlotTexture(Body, 900, 260, 220);
            MakeOutput(Body, 140);
        }

        /// <summary>Opens with the scope probe on a component.</summary>
        public void ScopeOn(string componentId)
        {
            Open();
            _comp.text = componentId;
            Capture();
        }

        private void Capture()
        {
            if (Work == null)
            {
                return;
            }

            CommandResult r = Work.Capture(_comp.text, string.IsNullOrEmpty(_pin.text) ? "signal" : _pin.text, UiKit.ParseNumber(_window.text, 40), out Waveform w);
            Output.text = r.Message;
            _scope.Clear();
            if (w != null && w.Volts.Length > 1)
            {
                double min = Math.Min(0, w.Volts.Min()), max = Math.Max(1, w.Volts.Max() * 1.1);
                _scope.Series(w.TimeMs, w.Volts, w.TimeMs[0], w.TimeMs[w.TimeMs.Length - 1], min, max, new Color(0.3f, 1f, 0.4f));
                Output.text = $"{w.Description}\nVentana {w.TimeMs[w.TimeMs.Length - 1] - w.TimeMs[0]:0} ms · {min:0.0}…{max:0.0} V";
            }

            _scope.Apply();
        }
    }
}
