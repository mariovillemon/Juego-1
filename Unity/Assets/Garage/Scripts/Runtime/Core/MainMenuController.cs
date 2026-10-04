using Garage.Game;
using Garage.Game.Settings;
using Garage.Unity.UI;
using TMPro;
using UnityEngine;
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
        private Canvas _canvas;
        private GameObject _menu, _options;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;
            GameOptions.Load();
            _canvas = UiKit.Canvas("MainMenu", 5);
            Localizer.Current.Changed += BuildMenu;
            BuildMenu();
        }

        private void OnDestroy() => Localizer.Current.Changed -= BuildMenu;

        private static string T(string k) => GameOptions.T(k);

        private void BuildMenu()
        {
            if (_menu != null)
            {
                Destroy(_menu);
            }

            Image panel = UiKit.Panel(_canvas.transform, "Panel", UiTheme.Panel);
            _menu = panel.gameObject;
            UiKit.Anchor(panel.rectTransform, new Vector2(0.05f, 0.12f), new Vector2(0.4f, 0.88f));
            RectTransform col = UiKit.Column(panel.transform, "Col", 10);
            UiKit.Fill(col, 24);
            UiKit.Size(UiKit.Label(col, "<b>TALLER</b>", 48), 64);
            UiKit.Size(UiKit.Label(col, T("menu.subtitle"), UiTheme.FontBody, UiTheme.TextDim), 30);
            UiKit.Button(col, T("menu.new"), () => Launch(0), UiTheme.ButtonPrimary);
            _tutButton = UiKit.Button(col, "", () => { _tutorial = !_tutorial; Labels(); });
            _trainButton = UiKit.Button(col, "", () => { _training = !_training; Labels(); });
            UiKit.Size(UiKit.Label(col, T("menu.load"), UiTheme.FontBody, UiTheme.TextDim), 28);
            _slots = UiKit.Scroll(col, "Slots");
            UiKit.Button(col, T("menu.dyno"), () => LoadingScreen.Load("Dyno"));
            UiKit.Button(col, T("menu.options"), ShowOptions);
            UiKit.Button(col, T("menu.quit"), Application.Quit, UiTheme.ButtonDanger);
            Labels();
            foreach (SaveSlotInfo s in new SaveSlots(SimulationRunner.SaveDir).List())
            {
                SaveSlotInfo slot = s;
                Button b = UiKit.Button(_slots, GameOptions.T("menu.slot", s.Slot, s.Summary), () => Launch(slot.Slot), UiTheme.Row);
                b.interactable = s.Used;
                b.GetComponentInChildren<TextMeshProUGUI>().fontSize = UiTheme.FontSmall;
            }
        }

        private void ShowOptions()
        {
            if (_options != null)
            {
                return;
            }

            Image panel = UiKit.Panel(_canvas.transform, "OptionsPanel", UiTheme.Panel);
            _options = panel.gameObject;
            UiKit.Anchor(panel.rectTransform, new Vector2(0.42f, 0.08f), new Vector2(0.95f, 0.92f));
            RectTransform col = UiKit.Column(panel.transform, "Col", 8, true);
            UiKit.Fill(col, 16);
            OptionsView.Create(col, () =>
            {
                Destroy(_options);
                _options = null;
            });
        }

        private void Labels()
        {
            UiKit.SetText(_tutButton, T(_tutorial ? "menu.tutorial_on" : "menu.tutorial_off"));
            UiKit.SetText(_trainButton, T(_training ? "menu.training" : "menu.realistic"));
        }

        private void Launch(int slot)
        {
            GameLaunch.FromMenu = true;
            GameLaunch.LoadSlot = slot;
            GameLaunch.Tutorial = _tutorial;
            GameLaunch.Training = _training;
            LoadingScreen.Load("Workshop");
        }

        private void Update()
        {
            transform.RotateAround(pivot, Vector3.up, orbitSpeed * Time.deltaTime);
            transform.LookAt(pivot);
        }
    }
}
