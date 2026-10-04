using Garage.Game;
using Garage.Sim.Vehicle;
using Garage.Unity.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Garage.Unity
{
    /// <summary>
    /// First-person interaction: raycast from the camera, highlight, contextual prompts, pick up / hold / put down
    /// tools, plug the scan tool into the OBD port, use tools on components and open the matching panels.
    /// Every game action goes through GameSession/CarWork; this script only presents and routes input.
    /// </summary>
    public sealed class InteractionSystem : MonoBehaviour
    {
        public Camera viewCamera;
        public SimulationRunner runner;
        public GameUI ui;
        public float reach = 2.4f;

        private InputAction _use, _drop, _repair, _connector, _key, _carPanel, _click, _unplug, _road;
        private ToolItem _held;
        private Renderer _hlRenderer;
        private Color _hlColor;
        private Vector3 _lastPos;
        private float _walked;
        private bool _movedReported;
        private int _keyStage;

        private void Awake()
        {
            _use = Make("<Keyboard>/e");
            _drop = Make("<Keyboard>/g");
            _repair = Make("<Keyboard>/r");
            _connector = Make("<Keyboard>/c");
            _key = Make("<Keyboard>/k");
            _carPanel = Make("<Keyboard>/v");
            _click = Make("<Mouse>/leftButton");
            _unplug = Make("<Keyboard>/q");
            _road = Make("<Keyboard>/t");
            _lastPos = transform.position;
        }

        private static InputAction Make(string binding)
        {
            var a = new InputAction(binding, InputActionType.Button, binding);
            a.Enable();
            return a;
        }

        private void OnDestroy()
        {
            foreach (InputAction a in new[] { _use, _drop, _repair, _connector, _key, _carPanel, _click, _unplug, _road })
            {
                a?.Dispose();
            }
        }

        private GameSession Session => runner != null ? runner.Session : null;

        private void Update()
        {
            if (viewCamera == null || runner == null || ui == null || Session == null)
            {
                return;
            }

            TrackWalking();
            FollowPluggedTools();
            ui.HeldTool = _held != null ? _held.displayName : "";
            if (UiState.AnyOpen)
            {
                Unhighlight();
                return;
            }

            Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            Component target = null;
            if (Physics.Raycast(ray, out RaycastHit hit, reach))
            {
                target = FirstOf(hit.collider);
                if (target is ToolItem ti && ti == _held)
                {
                    target = null;
                }
            }

            Highlight(target);
            ui.Prompt = PromptFor(target, hit);
            HandleGlobalKeys(hit);
            switch (target)
            {
                case ToolItem tool:
                    if (_use.WasPressedThisFrame())
                    {
                        PickUp(tool);
                    }

                    break;
                case ObdPort port:
                    HandlePort(port);
                    break;
                case ComponentSlot slot:
                    HandleSlot(slot);
                    break;
                case Usable u:
                    if (_use.WasPressedThisFrame())
                    {
                        Use(u);
                    }

                    break;
                default:
                    if (_held != null && _click.WasPressedThisFrame())
                    {
                        UseHeldInAir();
                    }

                    break;
            }
        }

        /// <summary>Interactive component on a collider (explicit null checks: Unity's fake-null breaks ??).</summary>
        private static Component FirstOf(Collider c)
        {
            ToolItem t = c.GetComponentInParent<ToolItem>();
            if (t != null)
            {
                return t;
            }

            ObdPort p = c.GetComponentInParent<ObdPort>();
            if (p != null)
            {
                return p;
            }

            ComponentSlot s = c.GetComponentInParent<ComponentSlot>();
            if (s != null)
            {
                return s;
            }

            Usable u = c.GetComponentInParent<Usable>();
            return u != null ? u : null;
        }

        // --------------------------------------------------------------- input

        private void HandleGlobalKeys(RaycastHit hit)
        {
            CarWork w = runner.Work;
            if (_drop.WasPressedThisFrame() && _held != null)
            {
                Drop(hit);
            }

            if (_carPanel.WasPressedThisFrame())
            {
                ui.CarActions.Open();
            }

            if (w == null)
            {
                return;
            }

            if (_key.WasPressedThisFrame())
            {
                // Key cycle: ON (pump primes) → START → OFF.
                CommandResult r;
                if (w.EngineRunning)
                {
                    r = w.Stop();
                    _keyStage = 0;
                }
                else if (_keyStage == 0 || !w.Car.KeyOn)
                {
                    r = w.IgnitionOn();
                    _keyStage = 1;
                }
                else
                {
                    r = w.Start();
                    _keyStage = 0;
                }

                ui.Toast(r.Message, r.Ok);
            }

            if (_road.WasPressedThisFrame())
            {
                CommandResult r = w.RoadTest();
                ui.Toast(r.Message, r.Ok, 10f);
            }
        }

        private void HandlePort(ObdPort port)
        {
            if (port.Plugged != null)
            {
                if (_use.WasPressedThisFrame())
                {
                    ui.Scanner.Open();
                }
                else if (_unplug.WasPressedThisFrame())
                {
                    Unplug(port);
                }

                return;
            }

            if (_held != null && _held.toolId == CarWork.Scanner && _use.WasPressedThisFrame())
            {
                ToolItem t = _held;
                _held = null;
                t.Plugged = true;
                port.Plugged = t;
                SetColliders(t, true);
                Session.Notify(GameEventKind.ScannerPlugged);
                ui.Toast("Escáner enchufado al conector OBD (clic).", true);
            }
        }

        private void Unplug(ObdPort port)
        {
            ToolItem t = port.Plugged;
            port.Plugged = null;
            t.Plugged = false;
            Session.Notify(GameEventKind.ScannerUnplugged);
            if (_held == null)
            {
                Hold(t);
            }
        }

        private void HandleSlot(ComponentSlot slot)
        {
            CarWork w = runner.Work;
            if (_use.WasPressedThisFrame() || _repair.WasPressedThisFrame())
            {
                ui.OpenPart(slot);
                return;
            }

            if (_connector.WasPressedThisFrame() && slot.hasConnector && w != null)
            {
                var c = runner.Car.CircuitOf(slot.componentId);
                CommandResult r = w.SetConnector(slot.componentId, c != null && !c.ConnectorConnected);
                ui.Toast(r.Message, r.Ok);
                return;
            }

            if (_held == null || !_click.WasPressedThisFrame() || w == null)
            {
                return;
            }

            switch (_held.toolId)
            {
                case CarWork.Meter:
                    ui.Meter.ShowConnector(slot.hasConnector ? slot.componentId : "");
                    break;
                case CarWork.Scope:
                    ui.Mechanical.ScopeOn(slot.componentId);
                    break;
                case CarWork.Scanner:
                    ui.Scanner.Open();
                    break;
                default:
                    ui.Mechanical.Open();
                    break;
            }
        }

        private void UseHeldInAir()
        {
            switch (_held.toolId)
            {
                case CarWork.Scanner:
                    ui.Scanner.Open();
                    break;
                case CarWork.Meter:
                    ui.Meter.Open();
                    break;
                default:
                    ui.Mechanical.Open();
                    break;
            }
        }

        private void Use(Usable u)
        {
            switch (u.kind)
            {
                case Usable.Kind.Board: ui.Board.Open(); break;
                case Usable.Kind.Laptop: ui.Ecu.Open(); break;
                case Usable.Kind.DynoConsole: ui.Dyno.Open(); break;
                case Usable.Kind.OldPartsBox: ui.Inventory.Open(); break;
                case Usable.Kind.Shop: ui.Shop.Open(); break;
                case Usable.Kind.Upgrades: ui.Upgrades.Open(); break;
                default: ui.CarActions.Open(); break;
            }
        }

        private string PromptFor(Component target, RaycastHit hit)
        {
            string held = _held != null ? $"\nG: soltar {_held.displayName}" : "";
            switch (target)
            {
                case ToolItem t:
                    return $"E: coger {t.displayName}{held}";
                case ObdPort p:
                    if (p.Plugged != null)
                    {
                        return "Conector OBD — E: pantalla del escáner · Q: desenchufar";
                    }

                    return _held != null && _held.toolId == CarWork.Scanner ? "Conector OBD — E: enchufar el escáner" : "Conector OBD (trae el escáner)";
                case ComponentSlot s:
                    string extra = _held == null ? "" : _held.toolId == CarWork.Meter ? " · Clic: medir en su conector" : _held.toolId == CarWork.Scope ? " · Clic: osciloscopio" : $" · Clic: usar {_held.displayName}";
                    return $"<b>{s.componentName}</b>\nE: ver pieza · R: sustituir{(s.hasConnector ? " · C: conector" : "")}{extra}{held}";
                case Usable u:
                    return $"E: {u.label}{held}";
                default:
                    return _held != null ? $"Clic: usar {_held.displayName}{held}" : "";
            }
        }

        // --------------------------------------------------------------- tools

        private void PickUp(ToolItem t)
        {
            if (_held != null)
            {
                ui.Toast("Ya tienes algo en la mano (G para soltarlo).", false);
                return;
            }

            CarWork w = runner.Work;
            bool owned = w == null ? Session.Workshop.Has(t.toolId) : w.Owns(t.toolId);
            if (!owned)
            {
                ui.Toast($"Ese {t.displayName} es de demostración: compra la herramienta (U).", false);
                return;
            }

            Hold(t);
            Session.Notify(GameEventKind.ToolPickedUp, t.toolId);
        }

        private void Hold(ToolItem t)
        {
            _held = t;
            SetColliders(t, false);
            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
            }

            t.transform.SetParent(viewCamera.transform, false);
            t.transform.localPosition = t.holdOffset;
            t.transform.localRotation = Quaternion.Euler(t.holdEuler);
        }

        private void Drop(RaycastHit hit)
        {
            ToolItem t = _held;
            _held = null;
            t.transform.SetParent(null, true);
            Vector3 p = hit.collider != null ? hit.point + hit.normal * 0.03f : viewCamera.transform.position + viewCamera.transform.forward * 0.8f;
            t.transform.SetPositionAndRotation(p, Quaternion.Euler(0, viewCamera.transform.eulerAngles.y, 0));
            SetColliders(t, true);
            Session.Notify(GameEventKind.ToolPlaced, t.toolId);
        }

        private static void SetColliders(ToolItem t, bool on)
        {
            foreach (Collider c in t.GetComponentsInChildren<Collider>())
            {
                c.enabled = on;
            }
        }

        private void FollowPluggedTools()
        {
            foreach (ObdPort port in FindObjectsByType<ObdPort>(FindObjectsSortMode.None))
            {
                if (port.Plugged != null)
                {
                    port.Plugged.transform.SetPositionAndRotation(port.transform.position + port.transform.forward * 0.05f, port.transform.rotation);
                }
            }

            // A scan tool left plugged into a car that has gone (delivered, swapped) drops to the floor.
            foreach (ToolItem t in FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
            {
                if (t.Plugged && FindPortWith(t) == null)
                {
                    t.Plugged = false;
                    t.transform.position = transform.position + transform.forward * 0.6f + Vector3.up * 0.1f;
                }
            }
        }

        private static ObdPort FindPortWith(ToolItem t)
        {
            foreach (ObdPort p in FindObjectsByType<ObdPort>(FindObjectsSortMode.None))
            {
                if (p.Plugged == t)
                {
                    return p;
                }
            }

            return null;
        }

        private void TrackWalking()
        {
            _walked += Vector3.Distance(transform.position, _lastPos);
            _lastPos = transform.position;
            if (!_movedReported && _walked > 3f)
            {
                _movedReported = true;
                Session.Notify(GameEventKind.PlayerMoved);
            }
        }

        // ----------------------------------------------------------- highlight

        private void Highlight(Component target)
        {
            Renderer r = target != null ? target.GetComponentInChildren<Renderer>() : null;
            if (r == _hlRenderer)
            {
                return;
            }

            Unhighlight();
            if (r != null && r.material.HasProperty("_BaseColor"))
            {
                _hlRenderer = r;
                _hlColor = r.material.GetColor("_BaseColor");
                r.material.SetColor("_BaseColor", _hlColor * 1.6f + new Color(0.06f, 0.05f, 0f));
            }
        }

        private void Unhighlight()
        {
            if (_hlRenderer != null)
            {
                _hlRenderer.material.SetColor("_BaseColor", _hlColor);
            }

            _hlRenderer = null;
        }
    }
}
