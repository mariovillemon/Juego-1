using System;
using System.Collections.Generic;
using System.IO;
using Garage.Game.Settings;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Garage.Unity
{
    /// <summary>
    /// Bridge between the pure <see cref="GameSettings"/> and Unity: loads and saves
    /// <c>persistentDataPath/settings.json</c>, applies graphics/audio/camera options, builds input actions from the
    /// key bindings (and re-applies them after a rebind) and owns the shared <see cref="Localizer"/>.
    /// </summary>
    public static class GameOptions
    {
        private static GameSettings _current;
        private static readonly List<(InputAction Action, int Index, string Id)> Bound = new List<(InputAction, int, string)>();

        /// <summary>Raised after <see cref="Apply"/> (panels refresh their texts, cameras their FOV).</summary>
        public static event Action Changed;

        /// <summary>Settings file.</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        /// <summary>Current settings (loaded on first use).</summary>
        public static GameSettings Current
        {
            get
            {
                if (_current == null)
                {
                    Load();
                }

                return _current;
            }
        }

        /// <summary>Shortcut for a localized string.</summary>
        public static string T(string key) => Localizer.Current.Get(key);

        /// <summary>Shortcut for a formatted localized string.</summary>
        public static string T(string key, params object[] args) => Localizer.Current.Format(key, args);

        /// <summary>Readable name of the key bound to an action ("E").</summary>
        public static string KeyName(string id) => KeyBindings.Display(Current.Bindings.PathOf(id));

        /// <summary>Reads the file and the string tables, then applies everything.</summary>
        public static void Load()
        {
            string text = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    text = File.ReadAllText(FilePath);
                }
            }
            catch (IOException e)
            {
                Debug.LogWarning("No se pudo leer settings.json: " + e.Message);
            }

            _current = GameSettings.Parse(text);
            var loc = new Localizer();
            try
            {
                loc.LoadFolder(Path.Combine(SimulationRunner.DataRoot, "locale"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Tablas de idioma: " + e.Message);
            }

            Localizer.Current = loc;
            Apply();
        }

        /// <summary>Writes the file.</summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, Current.ToJson().ToJson());
            }
            catch (IOException e)
            {
                Debug.LogWarning("No se pudo guardar settings.json: " + e.Message);
            }
        }

        /// <summary>Applies the current values to Unity.</summary>
        public static void Apply()
        {
            GameSettings s = Current;
            if (QualitySettings.names.Length > 0)
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp(s.Quality, 0, QualitySettings.names.Length - 1), true);
            }

            QualitySettings.vSyncCount = s.VSync ? 1 : 0;
            FullScreenMode mode = s.Window switch
            {
                WindowMode.ExclusiveFullscreen => FullScreenMode.ExclusiveFullScreen,
                WindowMode.Windowed => FullScreenMode.Windowed,
                _ => FullScreenMode.FullScreenWindow,
            };
            if (!Application.isEditor)
            {
                int w = s.ResolutionWidth > 0 ? s.ResolutionWidth : Screen.currentResolution.width;
                int h = s.ResolutionHeight > 0 ? s.ResolutionHeight : Screen.currentResolution.height;
                Screen.SetResolution(w, h, mode);
            }

            AudioListener.volume = (float)s.Volume("Master");
            Localizer.Current.SetLanguage(s.Language);
            foreach (CinemachineCamera cam in UnityEngine.Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                LensSettings lens = cam.Lens;
                lens.FieldOfView = (float)s.FieldOfView;
                cam.Lens = lens;
            }

            foreach (FocusDepthOfField dof in UnityEngine.Object.FindObjectsByType<FocusDepthOfField>(FindObjectsSortMode.None))
            {
                dof.blurWhenInspecting = s.InspectBlur;
            }

            ApplyBindings();
            Changed?.Invoke();
        }

        /// <summary>Gain of an audio bus from the options (master is applied by the listener).</summary>
        public static float BusGain(AudioBus bus) => (float)Current.Volume(bus.ToString());

        /// <summary>Creates and enables a button action bound to a rebindable id.</summary>
        public static InputAction Button(string id)
        {
            var a = new InputAction(id, InputActionType.Button, Current.Bindings.PathOf(id));
            Track(a, 0, id);
            a.Enable();
            return a;
        }

        /// <summary>Registers a binding of an action (e.g. a composite part) to follow rebinds.</summary>
        public static void Track(InputAction action, int bindingIndex, string id) => Bound.Add((action, bindingIndex, id));

        /// <summary>Forgets an action (call before disposing it).</summary>
        public static void Untrack(InputAction action) => Bound.RemoveAll(b => b.Action == action);

        private static void ApplyBindings()
        {
            for (int i = Bound.Count - 1; i >= 0; i--)
            {
                (InputAction action, int index, string id) = Bound[i];
                try
                {
                    action.ApplyBindingOverride(index, Current.Bindings.PathOf(id));
                }
                catch (Exception)
                {
                    Bound.RemoveAt(i); // disposed with its scene
                }
            }
        }
    }
}
