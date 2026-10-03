using System.Linq;
using Garage.Sim.Game;
using Garage.Sim.Tools;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Garage.Unity
{
    /// <summary>
    /// Look-at interaction: highlights the component under the crosshair and lets the player inspect it,
    /// unplug/plug its connector, loosen/tighten its fasteners (in order and to torque) and replace it.
    /// Every action goes through the Garage.Sim API so the simulation stays the single source of truth.
    /// </summary>
    public sealed class InteractionSystem : MonoBehaviour
    {
        public Camera viewCamera;
        public SimulationRunner runner;
        public float reach = 2.2f;

        private InputAction _use;
        private InputAction _connector;
        private InputAction _replace;
        private InputAction _wrench;
        private ComponentSlot _focused;
        private Renderer _focusedRenderer;
        private Color _originalColor;
        private string _message = "";
        private float _messageTime;
        private DiagnosticSession _session;

        private void Awake()
        {
            _use = new InputAction("Inspect", InputActionType.Button, "<Keyboard>/e");
            _connector = new InputAction("Connector", InputActionType.Button, "<Keyboard>/c");
            _replace = new InputAction("Replace", InputActionType.Button, "<Keyboard>/r");
            _wrench = new InputAction("Wrench", InputActionType.Button, "<Mouse>/leftButton");
        }

        private void OnEnable()
        {
            _use.Enable();
            _connector.Enable();
            _replace.Enable();
            _wrench.Enable();
        }

        private void OnDisable()
        {
            _use.Disable();
            _connector.Disable();
            _replace.Disable();
            _wrench.Disable();
            Unhighlight();
        }

        private DiagnosticSession Session
        {
            get
            {
                if (_session == null || _session.Car != runner.Car)
                {
                    _session = new DiagnosticSession(runner.Car, runner.Workshop?.TimeFactor ?? 1.0);
                }

                return _session;
            }
        }

        private void Update()
        {
            if (viewCamera == null || runner == null || runner.Car == null)
            {
                return;
            }

            ComponentSlot hit = null;
            if (Physics.Raycast(viewCamera.transform.position, viewCamera.transform.forward, out RaycastHit h, reach))
            {
                hit = h.collider.GetComponentInParent<ComponentSlot>();
            }

            if (hit != _focused)
            {
                Unhighlight();
                _focused = hit;
                if (_focused != null)
                {
                    _focusedRenderer = _focused.GetComponent<Renderer>();
                    if (_focusedRenderer != null)
                    {
                        _originalColor = _focusedRenderer.material.GetColor("_BaseColor");
                        _focusedRenderer.material.SetColor("_BaseColor", _originalColor * 1.6f + new Color(0.05f, 0.05f, 0.0f));
                    }
                }
            }

            if (_focused == null)
            {
                return;
            }

            if (_use.WasPressedThisFrame())
            {
                Show(Session.Mechanical.Inspect(_focused.componentId).Display);
            }

            if (_connector.WasPressedThisFrame() && _focused.hasConnector)
            {
                var c = runner.Car.CircuitOf(_focused.componentId);
                Show(Session.Multimeter.SetConnector(_focused.componentId, !c.ConnectorConnected).Display);
            }

            if (_wrench.WasPressedThisFrame())
            {
                BoltSequence bolts = _focused.GetComponent<BoltSequence>();
                if (bolts == null)
                {
                    bolts = _focused.gameObject.AddComponent<BoltSequence>();
                }

                Show(bolts.Turn());
            }

            if (_replace.WasPressedThisFrame())
            {
                BoltSequence bolts = _focused.GetComponent<BoltSequence>();
                if (bolts != null && !bolts.AllLoose)
                {
                    Show("Primero afloja todos los tornillos (clic izquierdo).");
                }
                else if (runner.Job != null && runner.Workshop != null)
                {
                    PartDefinition part = runner.Workshop.Content.Parts.For(_focused.kind, runner.Car.Definition.Id).FirstOrDefault(p => p.Quality == PartQuality.Oem);
                    Show(part == null ? "No hay recambio en el catálogo." : runner.Workshop.InstallPart(runner.Job, _focused.componentId, part.Id));
                    if (bolts != null)
                    {
                        bolts.ResetTight();
                    }
                }
                else
                {
                    runner.Car.ReplaceComponent(_focused.componentId);
                    Show($"{_focused.componentName} sustituido.");
                }
            }
        }

        private void Unhighlight()
        {
            if (_focusedRenderer != null)
            {
                _focusedRenderer.material.SetColor("_BaseColor", _originalColor);
            }

            _focusedRenderer = null;
        }

        private void Show(string msg)
        {
            _message = msg;
            _messageTime = Time.time;
        }

        private void OnGUI()
        {
            var center = new Rect(Screen.width / 2f - 3, Screen.height / 2f - 3, 6, 6);
            GUI.Box(center, GUIContent.none);
            if (_focused != null)
            {
                string hints = "[E] inspeccionar  [Clic] llave  [R] sustituir" + (_focused.hasConnector ? "  [C] conector" : "");
                GUI.Label(new Rect(Screen.width / 2f + 12, Screen.height / 2f - 10, 600, 40), $"{_focused.componentName}\n{hints}");
            }

            if (Time.time - _messageTime < 6f && !string.IsNullOrEmpty(_message))
            {
                GUI.Box(new Rect(20, Screen.height - 110, Screen.width - 40, 90), _message);
            }
        }
    }
}
