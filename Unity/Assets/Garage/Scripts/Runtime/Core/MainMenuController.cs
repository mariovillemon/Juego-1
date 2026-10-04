using Garage.Game;
using Garage.Unity.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Garage.Unity
{
    /// <summary>Main menu (uGUI built in code): new game with options, load a slot, dyno room, quit. Slow camera orbit.</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        public Vector3 pivot = new Vector3(0, 1.5f, 0);
        public float orbitSpeed = 3f;

        private bool _tutorial = true;
        private bool _training = true;
        private Button _tutButton, _trainButton;
        private RectTransform _slots;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;
            Canvas c = UiKit.Canvas("MainMenu", 5);
            Image panel = UiKit.Panel(c.transform, "Panel", UiTheme.Panel);
            UiKit.Anchor(panel.rectTransform, new Vector2(0.05f, 0.12f), new Vector2(0.4f, 0.88f));
            RectTransform col = UiKit.Column(panel.transform, "Col", 10);
            UiKit.Fill(col, 24);
            UiKit.Size(UiKit.Label(col, "<b>TALLER</b>", 48), 64);
            UiKit.Size(UiKit.Label(col, "Simulador de diagnosis y reprogramación", UiTheme.FontBody, UiTheme.TextDim), 30);
            UiKit.Button(col, "Nueva partida", () => Launch(0), UiTheme.ButtonPrimary);
            _tutButton = UiKit.Button(col, "", () => { _tutorial = !_tutorial; Labels(); });
            _trainButton = UiKit.Button(col, "", () => { _training = !_training; Labels(); });
            UiKit.Size(UiKit.Label(col, "Cargar partida", UiTheme.FontBody, UiTheme.TextDim), 28);
            _slots = UiKit.Scroll(col, "Slots");
            UiKit.Button(col, "Sala del banco de potencia (modo libre)", () => SceneManager.LoadScene("Dyno"));
            UiKit.Button(col, "Salir", Application.Quit, UiTheme.ButtonDanger);
            Labels();
            foreach (SaveSlotInfo s in new SaveSlots(SimulationRunner.SaveDir).List())
            {
                SaveSlotInfo slot = s;
                Button b = UiKit.Button(_slots, $"Ranura {s.Slot}: {s.Summary}", () => Launch(slot.Slot), UiTheme.Row);
                b.interactable = s.Used;
                b.GetComponentInChildren<TextMeshProUGUI>().fontSize = UiTheme.FontSmall;
            }
        }

        private void Labels()
        {
            UiKit.SetText(_tutButton, _tutorial ? "Tutorial del primer coche: SÍ" : "Tutorial del primer coche: NO");
            UiKit.SetText(_trainButton, _training ? "Modo formación (explicaciones): SÍ" : "Modo realista (sin pistas)");
        }

        private void Launch(int slot)
        {
            GameLaunch.FromMenu = true;
            GameLaunch.LoadSlot = slot;
            GameLaunch.Tutorial = _tutorial;
            GameLaunch.Training = _training;
            SceneManager.LoadScene("Workshop");
        }

        private void Update()
        {
            transform.RotateAround(pivot, Vector3.up, orbitSpeed * Time.deltaTime);
            transform.LookAt(pivot);
        }
    }
}
