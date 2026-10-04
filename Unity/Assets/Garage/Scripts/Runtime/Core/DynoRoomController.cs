using Garage.Sim.Vehicle;
using Garage.Unity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Garage.Unity
{
    /// <summary>
    /// Free dyno room (no job, no money): orbit camera (right mouse drag or WASD, wheel to zoom), car selector,
    /// start/stop, throttle held with Space, full pull with the live monitor, and Esc back to the main menu.
    /// Lives on the room camera.
    /// </summary>
    public sealed class DynoRoomController : MonoBehaviour
    {
        public SimulationRunner runner;
        public Vector3 pivot = new Vector3(0, 0.8f, 0);
        public float distance = 5f;

        private float _yaw = -35f, _pitch = 15f;
        private int _carIndex;
        private TextMeshProUGUI _carLabel, _status, _result;
        private Button _engineButton;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;
            runner ??= FindFirstObjectByType<SimulationRunner>();
            UiKit.EnsureEventSystem();
            Canvas c = UiKit.Canvas("DynoUI", 10);
            Image panel = UiKit.Panel(c.transform, "Panel", UiTheme.Panel);
            UiKit.Anchor(panel.rectTransform, new Vector2(0.01f, 0.35f), new Vector2(0.3f, 0.98f));
            RectTransform col = UiKit.Column(panel.transform, "Col", 8);
            UiKit.Fill(col, 14);
            UiKit.Size(UiKit.Label(col, "<b>Banco de potencia</b> (modo libre)", UiTheme.FontTitle), 40);
            RectTransform carRow = UiKit.Row(col, "Car");
            UiKit.Button(carRow, "◀", () => ChangeCar(-1), UiTheme.Button, 44);
            _carLabel = UiKit.Label(carRow, "", UiTheme.FontBody, UiTheme.Text, TextAlignmentOptions.Center);
            UiKit.Size(_carLabel, flexibleWidth: 1);
            UiKit.Button(carRow, "▶", () => ChangeCar(1), UiTheme.Button, 44);
            _engineButton = UiKit.Button(col, "", ToggleEngine, UiTheme.ButtonPrimary);
            UiKit.Button(col, "Pasada a fondo (medir potencia)", Pull);
            UiKit.Button(col, "Volver al menú (Esc)", () => LoadingScreen.Load("MainMenu"), UiTheme.ButtonDanger);
            _status = UiKit.Label(col, "", UiTheme.FontSmall, UiTheme.TextDim);
            UiKit.Size(_status, 40);
            _result = UiKit.Label(col, "", UiTheme.FontSmall, UiTheme.Text);
            UiKit.Size(_result, 120);
            UiKit.Size(UiKit.Label(col, "Espacio: acelerar · clic derecho + ratón / WASD: girar cámara · rueda: zoom", UiTheme.FontSmall, UiTheme.TextDim), 40);
            if (runner != null && runner.Content != null)
            {
                _carIndex = Mathf.Max(0, IndexOf(runner.Car?.Definition.Id));
            }

            Labels();
        }

        private int IndexOf(string id)
        {
            var ids = runner.Content.CarIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return i;
                }
            }

            return 0;
        }

        private void ChangeCar(int dir)
        {
            if (runner?.Content == null)
            {
                return;
            }

            var ids = runner.Content.CarIds;
            _carIndex = ((_carIndex + dir) % ids.Count + ids.Count) % ids.Count;
            runner.LoadSandbox(ids[_carIndex]);
            _result.text = "";
            Labels();
        }

        private void ToggleEngine()
        {
            Car car = runner?.Car;
            if (car == null)
            {
                return;
            }

            if (car.Engine.State.Running)
            {
                car.Key = KeyPosition.Off;
            }
            else
            {
                car.Key = KeyPosition.On;
                if (!car.Start())
                {
                    _status.text = "No arranca.";
                }
            }

            Labels();
        }

        private void Pull()
        {
            if (runner?.Work == null)
            {
                return;
            }

            if (!runner.Car.Engine.State.Running)
            {
                runner.Car.Key = KeyPosition.On;
                runner.Car.Start();
            }

            var r = runner.Work.RunDyno();
            _result.text = r.Message;
            Labels();
        }

        private void Labels()
        {
            Car car = runner?.Car;
            if (car == null)
            {
                _carLabel.text = "(sin coche)";
                return;
            }

            CarDefinition d = car.Definition;
            _carLabel.text = $"{d.Brand} {d.Model} ({d.Year})";
            UiKit.SetText(_engineButton, car.Engine.State.Running ? "Parar motor" : "Arrancar motor");
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                LoadingScreen.Load("MainMenu");
                return;
            }

            Car car = runner?.Car;
            if (car != null)
            {
                bool gas = kb != null && kb.spaceKey.isPressed && car.Engine.State.Running;
                car.Pedal = Mathf.MoveTowards((float)car.Pedal, gas ? 1f : 0f, Time.deltaTime * 3f);
                _status.text = car.Engine.State.Running ? $"{car.Engine.State.Rpm:0} rpm · {Garage.Sim.Core.Physics.KwToPs(car.Engine.State.PowerKw):0} CV" : "Motor parado";
                UiKit.SetText(_engineButton, car.Engine.State.Running ? "Parar motor" : "Arrancar motor");
            }

            float dx = 0, dy = 0;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                dx = d.x * 0.2f;
                dy = -d.y * 0.2f;
            }

            if (kb != null)
            {
                dx += ((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0)) * 60f * Time.deltaTime;
                dy += ((kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0)) * 40f * Time.deltaTime;
            }

            if (mouse != null)
            {
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.002f, 2f, 8f);
            }

            _yaw += dx;
            _pitch = Mathf.Clamp(_pitch + dy, -5f, 70f);
            transform.position = pivot + Quaternion.Euler(_pitch, _yaw, 0) * new Vector3(0, 0, -distance);
            transform.LookAt(pivot);
        }
    }
}
