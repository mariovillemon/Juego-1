using System.Collections.Generic;
using Garage.Game;
using Garage.Sim.Game;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>
    /// Root of the player UI (uGUI, built in code): HUD (money, reputation, day/time, job, tool in hand,
    /// contextual prompts), news feed, toasts, tutorial card and every modal panel. Listens to the game event bus
    /// instead of polling, and maps the hot keys. Added to the scene by Garage/Setup/Build Workshop Scene.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        public SimulationRunner runner;

        private readonly List<UiPanel> _panels = new List<UiPanel>();
        private readonly List<(TextMeshProUGUI label, float until)> _feed = new List<(TextMeshProUGUI, float)>();
        private Canvas _canvas;
        private TextMeshProUGUI _money, _rep, _clock, _job, _tool, _prompt, _status;
        private RectTransform _feedRoot;
        private GameObject _tutorialCard;
        private TextMeshProUGUI _tutorialTitle, _tutorialText, _tutorialExplain;
        private InputAction _board, _sheet, _shop, _inventory, _upgrades, _pause, _laptop, _help;

        /// <summary>Simulation bridge.</summary>
        public SimulationRunner Runner => runner;

        /// <summary>Session shortcut.</summary>
        public GameSession Session => runner != null ? runner.Session : null;

        public BoardPanel Board { get; private set; }
        public WorksheetPanel Worksheet { get; private set; }
        public ShopPanel Shop { get; private set; }
        public InventoryPanel Inventory { get; private set; }
        public UpgradesPanel Upgrades { get; private set; }
        public OptionsPanel Options { get; private set; }

        public PausePanel Pause { get; private set; }

        public DeliveryPanel Delivery { get; private set; }
        public ScannerPanel Scanner { get; private set; }
        public MultimeterPanel Meter { get; private set; }
        public PartPanel Part { get; private set; }
        public CarPanel CarActions { get; private set; }
        public DynoPanel Dyno { get; private set; }
        public EcuPanel Ecu { get; private set; }
        public WiringPanel Wiring { get; private set; }
        public MechanicalPanel Mechanical { get; private set; }

        /// <summary>Text shown under the crosshair (set by the interaction system every frame).</summary>
        public string Prompt { get; set; } = "";

        /// <summary>Tool in hand (set by the interaction system).</summary>
        public string HeldTool { get; set; } = "";

        private void Awake()
        {
            if (runner == null)
            {
                runner = FindFirstObjectByType<SimulationRunner>();
            }

            UiState.Reset();
            _canvas = UiKit.Canvas("PlayerUI", 10);
            _canvas.transform.SetParent(transform, false);
            BuildHud();
            Board = Add(new BoardPanel());
            Worksheet = Add(new WorksheetPanel());
            Shop = Add(new ShopPanel());
            Inventory = Add(new InventoryPanel());
            Upgrades = Add(new UpgradesPanel());
            Delivery = Add(new DeliveryPanel());
            Scanner = Add(new ScannerPanel());
            Meter = Add(new MultimeterPanel());
            Part = Add(new PartPanel());
            CarActions = Add(new CarPanel());
            Dyno = Add(new DynoPanel());
            Ecu = Add(new EcuPanel());
            Wiring = Add(new WiringPanel());
            Mechanical = Add(new MechanicalPanel());
            Pause = Add(new PausePanel());
            Options = Add(new OptionsPanel());

            _board = GameOptions.Button("board");
            _sheet = GameOptions.Button("sheet");
            _shop = GameOptions.Button("shop");
            _inventory = GameOptions.Button("inventory");
            _upgrades = GameOptions.Button("upgrades");
            _pause = Key("<Keyboard>/escape");
            _laptop = GameOptions.Button("laptop");
            _help = GameOptions.Button("help");
        }

        private static InputAction Key(string binding)
        {
            var a = new InputAction(binding, InputActionType.Button, binding);
            a.Enable();
            return a;
        }

        private T Add<T>(T p) where T : UiPanel
        {
            p.Create(this, _canvas.transform);
            _panels.Add(p);
            return p;
        }

        private void Start()
        {
            if (Session != null)
            {
                Session.Events.Raised += OnEvent;
            }

            GameOptions.Apply(); // FOV, blur and bindings for this scene's objects
            RefreshHud();
            RefreshTutorial();
        }

        private void OnDestroy()
        {
            if (Session != null)
            {
                Session.Events.Raised -= OnEvent;
            }

            foreach (InputAction a in new[] { _board, _sheet, _shop, _inventory, _upgrades, _pause, _laptop, _help })
            {
                GameOptions.Untrack(a);
                a?.Dispose();
            }

            UiState.Reset();
        }

        private void OnEvent(GameEvent e)
        {
            switch (e.Kind)
            {
                case GameEventKind.MessagePosted:
                case GameEventKind.PartsDelivered:
                case GameEventKind.DayStarted:
                case GameEventKind.UpgradeBought:
                case GameEventKind.GameSaved:
                case GameEventKind.GameLoaded:
                    Toast(e.Text, true);
                    break;
                case GameEventKind.CommandFailed:
                    Toast(e.Text, false);
                    break;
                case GameEventKind.EngineFailure:
                    Toast(e.Text, false);
                    break;
                case GameEventKind.TutorialStepChanged:
                case GameEventKind.TutorialCompleted:
                    RefreshTutorial();
                    if (e.Text.Length > 0)
                    {
                        Toast("Formación: " + e.Text, true, 14f);
                    }

                    break;
            }

            RefreshHud();
            foreach (UiPanel p in _panels)
            {
                if (p.IsOpen)
                {
                    p.Refresh();
                }
            }
        }

        private void Update()
        {
            if (_pause.WasPressedThisFrame() && !OptionsView.Capturing)
            {
                if (!UiState.CloseTop())
                {
                    Pause.Open();
                }
            }
            else if (!AnyTextFieldFocused())
            {
                if (_board.WasPressedThisFrame())
                {
                    Board.Toggle();
                }
                else if (_sheet.WasPressedThisFrame())
                {
                    Worksheet.Toggle();
                }
                else if (_shop.WasPressedThisFrame())
                {
                    Shop.Toggle();
                }
                else if (_inventory.WasPressedThisFrame())
                {
                    Inventory.Toggle();
                }
                else if (_upgrades.WasPressedThisFrame())
                {
                    Upgrades.Toggle();
                }
                else if (_laptop.WasPressedThisFrame())
                {
                    Ecu.Toggle();
                }
                else if (_help.WasPressedThisFrame())
                {
                    Toast(HelpText, true, 12f);
                }
            }

            foreach (UiPanel p in _panels)
            {
                if (p.IsOpen)
                {
                    p.Tick();
                }
            }

            _prompt.text = UiState.AnyOpen ? "" : Prompt;
            _tool.text = string.IsNullOrEmpty(HeldTool) ? GameOptions.T("hud.free_hands") : GameOptions.T("hud.holding", HeldTool);
            if (runner != null && runner.Work != null)
            {
                _status.text = runner.Work.StatusLine;
            }
            else
            {
                _status.text = GameOptions.T("hud.lift_free", K("board"));
            }

            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime > _feed[i].until)
                {
                    if (_feed[i].label != null)
                    {
                        Destroy(_feed[i].label.transform.parent.gameObject);
                    }

                    _feed.RemoveAt(i);
                }
            }
        }

        private static bool AnyTextFieldFocused()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            return es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }

        /// <summary>Key reference.</summary>
        public static string HelpText => GameOptions.T("hud.help", K("forward"), K("left"), K("back"), K("right"), K("crouch"), K("lamp"), K("use"), K("drop"),
            K("repair"), K("connector"), K("key"), K("car"), K("board"), K("sheet"), K("shop"), K("inventory"), K("upgrades"), K("laptop"));

        private static string K(string id) => GameOptions.KeyName(id);

        // ------------------------------------------------------------------ HUD

        private void BuildHud()
        {
            Image bar = UiKit.Panel(_canvas.transform, "HudBar", UiTheme.HudBack);
            UiKit.Anchor(bar.rectTransform, new Vector2(0, 1), new Vector2(1, 1));
            bar.rectTransform.offsetMin = new Vector2(0, -40);
            bar.rectTransform.offsetMax = Vector2.zero;
            bar.raycastTarget = false;
            RectTransform row = UiKit.Row(bar.transform, "Row", 40, 24);
            UiKit.Fill(row, 4);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 16, 0, 0);
            _money = Hud(row, 180);
            _rep = Hud(row, 180);
            _clock = Hud(row, 180);
            _job = Hud(row, 0);
            _tool = Hud(row, 300);

            _status = UiKit.Label(_canvas.transform, "", UiTheme.FontSmall, UiTheme.TextDim);
            UiKit.Anchor(_status.rectTransform, new Vector2(0, 1), new Vector2(0.7f, 1));
            _status.rectTransform.offsetMin = new Vector2(16, -70);
            _status.rectTransform.offsetMax = new Vector2(0, -42);

            TextMeshProUGUI cross = UiKit.Label(_canvas.transform, "+", 26, new Color(1, 1, 1, 0.75f), TextAlignmentOptions.Center);
            UiKit.Anchor(cross.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            cross.rectTransform.sizeDelta = new Vector2(30, 30);

            _prompt = UiKit.Label(_canvas.transform, "", UiTheme.FontHud, UiTheme.Text, TextAlignmentOptions.Top);
            UiKit.Anchor(_prompt.rectTransform, new Vector2(0.3f, 0.5f), new Vector2(0.7f, 0.5f));
            _prompt.rectTransform.offsetMin = new Vector2(0, -140);
            _prompt.rectTransform.offsetMax = new Vector2(0, -24);
            _prompt.outlineWidth = 0.15f;
            _prompt.outlineColor = Color.black;

            _feedRoot = UiKit.Column(_canvas.transform, "Feed", 6);
            UiKit.Anchor(_feedRoot, new Vector2(0, 0), new Vector2(0.45f, 0.4f));
            _feedRoot.offsetMin = new Vector2(16, 16);
            _feedRoot.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;

            Image card = UiKit.Panel(_canvas.transform, "TutorialCard", UiTheme.HudBack);
            UiKit.Anchor(card.rectTransform, new Vector2(0.72f, 0.55f), new Vector2(1, 0.93f));
            card.rectTransform.offsetMax = new Vector2(-16, 0);
            _tutorialCard = card.gameObject;
            RectTransform col = UiKit.Column(card.transform, "Col", 6);
            UiKit.Fill(col, 12);
            _tutorialTitle = UiKit.Label(col, "", UiTheme.FontBody, UiTheme.Accent);
            UiKit.Size(_tutorialTitle, 28);
            _tutorialText = UiKit.Label(col, "", UiTheme.FontSmall);
            UiKit.Size(_tutorialText, flexibleHeight: 1);
            _tutorialExplain = UiKit.Label(col, "", UiTheme.FontSmall, UiTheme.Ok);
            UiKit.Size(_tutorialExplain, flexibleHeight: 1);
            Button skip = UiKit.Button(col, "Saltar tutorial", () => Session?.SkipTutorial(), UiTheme.Button);
            UiKit.Size(skip, 30);
            _tutorialCard.SetActive(false);
        }

        private static TextMeshProUGUI Hud(Transform row, float width)
        {
            TextMeshProUGUI t = UiKit.Label(row, "", UiTheme.FontHud);
            if (width > 0)
            {
                UiKit.Size(t, preferredWidth: width);
            }
            else
            {
                UiKit.Size(t, flexibleWidth: 1);
            }

            return t;
        }

        private void RefreshHud()
        {
            GameSession s = Session;
            if (s == null)
            {
                return;
            }

            _money.text = $"<color={UiTheme.Hex(s.Money < 0 ? UiTheme.Bad : UiTheme.Text)}>{s.Money:0} €</color>";
            _rep.text = $"Reputación {s.Reputation:0}/100";
            _clock.text = $"Día {s.Day} · {s.Clock}";
            Job j = s.ActiveJob;
            _job.text = j == null ? "<color=#999>Sin coche en el elevador</color>" : $"{j.Car.Definition.DisplayName} — {j.Customer.Name} · límite día {j.AcceptedDay + j.Definition.DeadlineDays}";
        }

        private void RefreshTutorial()
        {
            TutorialRunner t = Session?.Tutorial;
            bool show = t != null && !t.Finished;
            _tutorialCard.SetActive(show);
            if (!show)
            {
                TutorialHighlight.Set(null);
                return;
            }

            TutorialStep step = t.Current;
            _tutorialTitle.text = $"Tutorial {t.Index + 1}/{t.Definition.Steps.Count}: {step.Title}";
            _tutorialText.text = step.Text;
            _tutorialExplain.text = t.LastExplanation;
            TutorialHighlight.Set(step.Highlight);
        }

        /// <summary>Shows a short message bottom-left.</summary>
        public void Toast(string text, bool ok, float seconds = 6f)
        {
            if (string.IsNullOrEmpty(text) || _feedRoot == null)
            {
                return;
            }

            Image bg = UiKit.Panel(_feedRoot, "Toast", UiTheme.HudBack);
            TextMeshProUGUI t = UiKit.Label(bg.transform, text, UiTheme.FontSmall, ok ? UiTheme.Text : UiTheme.Bad);
            UiKit.Fill(t.rectTransform, 8);
            VerticalLayoutGroup v = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 8, 6, 6);
            v.childControlHeight = true;
            v.childControlWidth = true;
            _feed.Add((t, Time.unscaledTime + seconds));
            while (_feed.Count > 6)
            {
                Destroy(_feed[0].label.transform.parent.gameObject);
                _feed.RemoveAt(0);
            }
        }

        /// <summary>Opens the part panel for a component slot.</summary>
        public void OpenPart(ComponentSlot slot) => Part.Show(slot);
    }
}
