using System.Collections.Generic;
using System.Linq;
using Garage.Game;
using Garage.Sim.Components;
using SimComponent = Garage.Sim.Components.Component;
using Garage.Sim.Electrical;
using Garage.Sim.Game;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>Button that must be held down; reports progress (Shift = ×3 speed).</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float seconds = 0.8f;
        public System.Action Completed;
        public Image fill;

        private bool _held;
        private float _t;

        public void OnPointerDown(PointerEventData e) => _held = true;

        public void OnPointerUp(PointerEventData e) => _held = false;

        public void OnPointerExit(PointerEventData e) => _held = false;

        private void Update()
        {
            bool fast = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            _t = _held ? _t + Time.unscaledDeltaTime * (fast ? 3f : 1f) : Mathf.Max(0, _t - Time.unscaledDeltaTime * 2f);
            if (fill != null)
            {
                fill.fillAmount = Mathf.Clamp01(_t / seconds);
            }

            if (_t >= seconds)
            {
                _t = 0;
                _held = false;
                Completed?.Invoke();
            }
        }
    }

    /// <summary>
    /// A component of the car: information and known state, inspection, connector, measurements and the
    /// replacement sequence (unplug → loosen fasteners → extract → choose part from stock → fit → torque in order →
    /// plug). The sequence is presentation; the labour time and the part change are applied by GameSession.
    /// </summary>
    public sealed class PartPanel : UiPanel
    {
        private ComponentSlot _slot;
        private TextMeshProUGUI _info, _stepText;
        private RectTransform _parts, _actions;
        private Button _holdButton;
        private HoldButton _hold;
        private readonly List<string> _steps = new List<string>();
        private int _step = -1;
        private string _chosenPart = "";
        private bool _urgent;

        protected override string Title => "Pieza";

        protected override Vector2 SizeFraction => new Vector2(0.7f, 0.82f);

        protected override void Build()
        {
            _info = UiKit.Label(Body, "", UiTheme.FontBody);
            _info.alignment = TextAlignmentOptions.TopLeft;
            UiKit.Size(_info, 150);
            _actions = UiKit.Row(Body, "Actions");
            UiKit.Size(UiKit.Label(Body, "<b>Sustitución</b> — elige el recambio y mantén pulsado cada paso (Shift acelera)", UiTheme.FontSmall, UiTheme.TextDim), 24);
            _parts = UiKit.Scroll(Body, "Parts");
            _stepText = UiKit.Label(Body, "", UiTheme.FontBody, UiTheme.Accent);
            UiKit.Size(_stepText, 30);
            _holdButton = UiKit.Button(Body, "Mantener pulsado", null, UiTheme.ButtonPrimary);
            Image fill = UiKit.Panel(_holdButton.transform, "Fill", new Color(1f, 0.69f, 0f, 0.35f));
            UiKit.Fill(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0;
            fill.raycastTarget = false;
            fill.transform.SetAsFirstSibling();
            _hold = _holdButton.gameObject.AddComponent<HoldButton>();
            _hold.fill = fill;
            _hold.Completed = Advance;
        }

        /// <summary>Opens for a slot.</summary>
        public void Show(ComponentSlot slot)
        {
            if (slot == null || Runner.Car == null)
            {
                return;
            }

            if (_slot != slot)
            {
                _step = -1;
                _chosenPart = "";
            }

            _slot = slot;
            Runner.Session.Notify(GameEventKind.PartSelected, slot.componentId);
            Open();
        }

        public override void Refresh()
        {
            if (_slot == null || Runner.Car == null)
            {
                return;
            }

            GameSession s = Runner.Session;
            SimComponent c = Runner.Car.Parts.Get(_slot.componentId);
            if (c == null)
            {
                return;
            }

            TitleLabel.text = c.Name;
            Circuit circuit = Runner.Car.CircuitOf(c.Id);
            _info.text = $"<b>{c.Name}</b>  <color=#999>({c.Id} · {c.Location})</color>\n" +
                $"Conector: {(circuit == null ? "no tiene" : circuit.ConnectorConnected ? "conectado" : "<color=#fb0>DESCONECTADO</color>")}\n" +
                $"Mano de obra de sustitución: {c.ReplaceMinutes:0} min" + (c.PartId.Length > 0 ? $" · montada: {s.Content.Parts.Get(c.PartId)?.Name}" : "") + "\n" +
                "<color=#999>El estado real sólo se conoce midiendo, inspeccionando o en banco.</color>";

            UiKit.Clear(_actions);
            UiKit.Button(_actions, "Inspeccionar", () => { Report(Runner.Work.Inspect(c.Id)); });
            if (circuit != null)
            {
                UiKit.Button(_actions, circuit.ConnectorConnected ? "Desconectar" : "Conectar", () => { Report(Runner.Work.SetConnector(c.Id, !circuit.ConnectorConnected)); Refresh(); });
                UiKit.Button(_actions, "Multímetro", () => { Close(); Ui.Meter.ShowConnector(c.Id); });
                UiKit.Button(_actions, "Osciloscopio", () => { Close(); Ui.Mechanical.ScopeOn(c.Id); });
                UiKit.Button(_actions, "Reparar cableado", () => { Report(s.RepairWiring(c.Id, null)); });
            }

            UiKit.Button(_actions, "Ver en tienda", () => { Close(); Ui.Shop.OpenFor(FirstWord(c.Name)); });

            UiKit.Clear(_parts);
            if (s.ActiveJob == null)
            {
                UiKit.Size(UiKit.Label(_parts, "Sin encargo activo (modo libre): se sustituye por una pieza nueva sin coste.", UiTheme.FontSmall), 28);
            }
            else
            {
                foreach (PartDefinition p in s.PartsFor(Runner.Car, c.Id))
                {
                    PartDefinition part = p;
                    int stock = s.Inventory.Count(p.Id);
                    RectTransform row = UiKit.Row(_parts, p.Id, 32);
                    bool chosen = _chosenPart == p.Id;
                    UiKit.Size(UiKit.Label(row, $"{(chosen ? "<color=#fb0>></color> " : "")}{p.Name} <color=#999>· {Garage.Game.Inventory.QualityText(p.Quality)} · fiab. {p.Reliability:P0} · {p.Price:0.00} €</color>", UiTheme.FontSmall), flexibleWidth: 1);
                    UiKit.Size(UiKit.Label(row, stock > 0 ? $"stock {stock}" : "<color=#999>sin stock</color>", UiTheme.FontSmall), preferredWidth: 90);
                    if (stock > 0)
                    {
                        UiKit.Button(row, "Usar", () => { _chosenPart = part.Id; _urgent = false; Refresh(); }, UiTheme.Button, 90);
                    }

                    UiKit.Button(row, "Compra urgente", () => { _chosenPart = part.Id; _urgent = true; Refresh(); }, UiTheme.Button, 160);
                }
            }

            BuildSteps(c, circuit != null);
            _stepText.text = _step < 0 ? "Pulsa y mantén para empezar a desmontar." : _step < _steps.Count ? $"Paso {_step + 1}/{_steps.Count}: {_steps[_step]}" : "Hecho.";
            UiKit.SetText(_holdButton, _step < 0 ? "Empezar desmontaje (mantener)" : _step < _steps.Count ? _steps[_step] + " (mantener)" : "Terminado");
            bool needsPart = _step >= 0 && _step < _steps.Count && _steps[_step].StartsWith("Colocar") && _chosenPart.Length == 0 && s.ActiveJob != null;
            _holdButton.interactable = !needsPart;
            if (needsPart)
            {
                _stepText.text = "Elige el recambio en la lista (de tu almacén o compra urgente).";
            }
        }

        public override void Close()
        {
            base.Close();
            if (_slot != null && !_slot.gameObject.activeSelf)
            {
                _slot.gameObject.SetActive(true);
            }
        }

        private static string FirstWord(string name)
        {
            int i = name.IndexOf(' ');
            return i > 0 ? name.Substring(0, i) : name;
        }

        private void BuildSteps(SimComponent c, bool connector)
        {
            _steps.Clear();
            BoltSequence bolts = _slot.GetComponent<BoltSequence>();
            int n = bolts != null ? bolts.boltCount : (ComponentKinds.IsSensor(c.Kind) ? 1 : c.ReplaceMinutes > 40 ? 4 : 2);
            if (connector)
            {
                _steps.Add("Soltar la pestaña y desconectar el conector");
            }

            for (int i = 0; i < n; i++)
            {
                _steps.Add($"Aflojar tornillo {i + 1}/{n}");
            }

            _steps.Add("Extraer la pieza (va a la caja de piezas viejas)");
            _steps.Add("Colocar la pieza nueva");
            int[] order = n == 4 ? new[] { 1, 3, 2, 4 } : Enumerable.Range(1, n).ToArray();
            foreach (int b in order)
            {
                _steps.Add($"Apretar tornillo {b} al par");
            }

            if (connector)
            {
                _steps.Add("Conectar el conector hasta oír el clic");
            }
        }

        private void Advance()
        {
            if (_slot == null)
            {
                return;
            }

            // The part disappears from the car once extracted and comes back when the new one is fitted.
            if (_step >= 0 && _step < _steps.Count && _steps[_step].StartsWith("Extraer"))
            {
                _slot.gameObject.SetActive(false);
            }
            else if (_step >= 0 && _step < _steps.Count && _steps[_step].StartsWith("Colocar"))
            {
                _slot.gameObject.SetActive(true);
            }

            _step++;

            if (_step >= _steps.Count)
            {
                Finish();
                return;
            }

            Refresh();
        }

        private void Finish()
        {
            GameSession s = Runner.Session;
            if (s.ActiveJob == null)
            {
                Runner.Car.ReplaceComponent(_slot.componentId);
                Ui.Toast($"{_slot.componentName} sustituido (modo libre).", true);
            }
            else
            {
                Report(s.InstallPart(_slot.componentId, _chosenPart, _urgent));
            }

            _step = -1;
            _chosenPart = "";
            Refresh();
        }
    }
}
