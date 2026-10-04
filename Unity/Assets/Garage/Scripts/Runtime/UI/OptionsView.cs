using System;
using System.Linq;
using Garage.Game.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>
    /// Options screen content shared by the main menu and the pause menu: graphics, controls, audio and key
    /// tabs. Values change with ◀ ▶ steppers and are applied and saved at once. Rebinding waits for the next key
    /// (Esc cancels); a key used by another action is swapped.
    /// </summary>
    public sealed class OptionsView : MonoBehaviour
    {
        private static readonly int[][] Resolutions = { new[] { 0, 0 }, new[] { 1280, 720 }, new[] { 1600, 900 }, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 } };

        private RectTransform _tabs, _content;
        private int _tab;
        private string _capturing;
        private TextMeshProUGUI _hint;
        private Action _back;

        /// <summary>Waiting for a key (the pause key must not close the panel meanwhile).</summary>
        public static bool Capturing { get; private set; }

        /// <summary>Builds the view inside a container.</summary>
        public static OptionsView Create(RectTransform parent, Action back)
        {
            RectTransform root = UiKit.Column(parent, "Options", 8, true);
            UiKit.Size(root, flexibleHeight: 1, flexibleWidth: 1);
            OptionsView v = root.gameObject.AddComponent<OptionsView>();
            v._back = back;
            v._tabs = UiKit.Row(root, "Tabs");
            v._content = UiKit.Scroll(root, "Content");
            v._hint = UiKit.Label(root, "", UiTheme.FontSmall, UiTheme.Accent);
            UiKit.Size(v._hint, 24);
            v.Rebuild();
            Localizer.Current.Changed += v.Rebuild;
            return v;
        }

        private void OnDestroy()
        {
            Localizer.Current.Changed -= Rebuild;
            Capturing = false;
        }

        private static string T(string k) => GameOptions.T(k);

        /// <summary>Rebuilds all texts (after a language change too).</summary>
        public void Rebuild()
        {
            if (_tabs == null)
            {
                return;
            }

            UiKit.Clear(_tabs);
            string[] tabs = { "opt.tab.graphics", "opt.tab.controls", "opt.tab.audio", "opt.tab.keys" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                UiKit.Button(_tabs, T(tabs[i]), () => { _tab = idx; Rebuild(); }, i == _tab ? UiTheme.ButtonPrimary : UiTheme.Button);
            }

            if (_back != null)
            {
                UiKit.Button(_tabs, T("opt.back"), () => _back(), UiTheme.ButtonDanger);
            }

            UiKit.Clear(_content);
            GameSettings s = GameOptions.Current;
            switch (_tab)
            {
                case 0:
                    Stepper(T("opt.quality"), T("opt.quality." + GameSettings.QualityLevels[s.Quality]), d => s.Quality = Wrap(s.Quality + d, GameSettings.QualityLevels.Length));
                    int r = Array.FindIndex(Resolutions, x => x[0] == s.ResolutionWidth && x[1] == s.ResolutionHeight);
                    string res = s.ResolutionWidth == 0 ? T("opt.native") : $"{s.ResolutionWidth}×{s.ResolutionHeight}";
                    Stepper(T("opt.resolution"), res, d =>
                    {
                        int n = Wrap(Math.Max(0, r) + d, Resolutions.Length);
                        s.ResolutionWidth = Resolutions[n][0];
                        s.ResolutionHeight = Resolutions[n][1];
                    });
                    Stepper(T("opt.window"), T("opt.window." + s.Window), d => s.Window = (WindowMode)Wrap((int)s.Window + d, 3));
                    Toggle(T("opt.vsync"), s.VSync, v => s.VSync = v);
                    Toggle(T("opt.blur"), s.InspectBlur, v => s.InspectBlur = v);
                    Stepper(T("opt.fov"), $"{s.FieldOfView:0}°", d => s.FieldOfView += 5 * d);
                    break;
                case 1:
                    Stepper(T("opt.sensitivity"), $"{s.MouseSensitivity:0.0}", d => s.MouseSensitivity = Math.Round(s.MouseSensitivity + 0.1 * d, 1));
                    Toggle(T("opt.invert"), s.InvertY, v => s.InvertY = v);
                    int li = Array.IndexOf(GameSettings.Languages, s.Language);
                    Stepper(T("opt.language"), T("opt.lang." + s.Language), d => s.Language = GameSettings.Languages[Wrap(li + d, GameSettings.Languages.Length)]);
                    break;
                case 2:
                    foreach (string bus in GameSettings.Buses)
                    {
                        string b = bus;
                        Stepper(T("opt.vol." + b), $"{s.Volume(b) * 100:0} %", d => s.SetVolume(b, Math.Round(s.Volume(b) + 0.1 * d, 1)));
                    }

                    break;
                default:
                    foreach (BindableAction a in KeyBindings.Actions)
                    {
                        BindableAction action = a;
                        RectTransform row = UiKit.Row(_content, a.Id);
                        UiKit.Size(UiKit.Label(row, T(a.LabelKey)), flexibleWidth: 1);
                        string key = _capturing == a.Id ? "…" : KeyBindings.Display(s.Bindings.PathOf(a.Id));
                        UiKit.Button(row, key, () => StartCapture(action.Id), _capturing == a.Id ? UiTheme.ButtonPrimary : UiTheme.Button, 160);
                    }

                    UiKit.Button(_content, T("opt.reset_keys"), () => { s.Bindings.ResetAll(); Commit(); });
                    break;
            }
        }

        private static int Wrap(int v, int n) => ((v % n) + n) % n;

        private void Stepper(string label, string value, Action<int> change)
        {
            RectTransform row = UiKit.Row(_content, label);
            UiKit.Size(UiKit.Label(row, label), flexibleWidth: 1);
            UiKit.Button(row, "◀", () => { change(-1); Commit(); }, UiTheme.Button, 44);
            UiKit.Size(UiKit.Label(row, value, UiTheme.FontBody, UiTheme.Text, TextAlignmentOptions.Center), preferredWidth: 240);
            UiKit.Button(row, "▶", () => { change(1); Commit(); }, UiTheme.Button, 44);
        }

        private void Toggle(string label, bool value, Action<bool> set)
        {
            RectTransform row = UiKit.Row(_content, label);
            UiKit.Size(UiKit.Label(row, label), flexibleWidth: 1);
            UiKit.Button(row, value ? T("opt.on") : T("opt.off"), () => { set(!value); Commit(); }, value ? UiTheme.ButtonPrimary : UiTheme.Button, 160);
        }

        private void Commit()
        {
            GameOptions.Current.Sanitize();
            GameOptions.Apply();
            GameOptions.Save();
            Rebuild();
        }

        private void StartCapture(string id)
        {
            _capturing = id;
            Capturing = true;
            _hint.text = T("opt.press_key");
            Rebuild();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (_capturing == null || kb == null)
            {
                return;
            }

            KeyControl pressed = kb.allKeys.FirstOrDefault(k => k != null && k.wasPressedThisFrame);
            if (pressed == null)
            {
                return;
            }

            string id = _capturing;
            _capturing = null;
            _hint.text = "";
            if (pressed.name != "escape")
            {
                string swapped = GameOptions.Current.Bindings.Rebind(id, "<Keyboard>/" + pressed.name);
                if (swapped != null)
                {
                    BindableAction other = KeyBindings.Actions.First(a => a.Id == swapped);
                    _hint.text = GameOptions.T("opt.swapped", T(other.LabelKey));
                }
            }

            Commit();
            StartCoroutine(EndCaptureNextFrame());
        }

        private System.Collections.IEnumerator EndCaptureNextFrame()
        {
            yield return null;
            Capturing = false;
        }
    }

    /// <summary>Options window in the game (opened from the pause menu).</summary>
    public sealed class OptionsPanel : UiPanel
    {
        protected override string Title => GameOptions.T("opt.title");

        protected override Vector2 SizeFraction => new Vector2(0.6f, 0.8f);

        protected override void Build() => OptionsView.Create(Body, null);
    }
}
