using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Data.Json;

namespace Garage.Game.Settings
{
    /// <summary>How the game window is shown.</summary>
    public enum WindowMode
    {
        /// <summary>Borderless window covering the screen.</summary>
        FullscreenWindow,

        /// <summary>Exclusive fullscreen.</summary>
        ExclusiveFullscreen,

        /// <summary>Normal window.</summary>
        Windowed,
    }

    /// <summary>
    /// Player options (graphics, controls, audio, language and key bindings). Plain data with clamping and JSON
    /// persistence; the Unity side applies it. Unknown or out-of-range values in the file fall back to defaults.
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>Quality preset names, matching the HDRP assets made by Configure HDRP.</summary>
        public static readonly string[] QualityLevels = { "Low", "Medium", "High", "Ultra" };

        /// <summary>Audio buses with their own volume.</summary>
        public static readonly string[] Buses = { "Master", "Engine", "Workshop", "Tools", "Ambience", "UI" };

        /// <summary>Supported languages.</summary>
        public static readonly string[] Languages = { "es", "en" };

        /// <summary>Quality preset index (0..3).</summary>
        public int Quality { get; set; } = 2;

        /// <summary>Resolution width (0 = native).</summary>
        public int ResolutionWidth { get; set; }

        /// <summary>Resolution height (0 = native).</summary>
        public int ResolutionHeight { get; set; }

        /// <summary>Window mode.</summary>
        public WindowMode Window { get; set; } = WindowMode.FullscreenWindow;

        /// <summary>Vertical sync.</summary>
        public bool VSync { get; set; } = true;

        /// <summary>Mouse sensitivity multiplier (0.1..5).</summary>
        public double MouseSensitivity { get; set; } = 1.0;

        /// <summary>Invert vertical look.</summary>
        public bool InvertY { get; set; }

        /// <summary>Vertical field of view in degrees (50..100).</summary>
        public double FieldOfView { get; set; } = 70;

        /// <summary>Depth of field when inspecting parts.</summary>
        public bool InspectBlur { get; set; }

        /// <summary>Language code.</summary>
        public string Language { get; set; } = "es";

        /// <summary>Volume per bus 0..1.</summary>
        public Dictionary<string, double> Volumes { get; } = Buses.ToDictionary(b => b, _ => 1.0);

        /// <summary>Key bindings.</summary>
        public KeyBindings Bindings { get; } = new KeyBindings();

        /// <summary>Volume of a bus (1 when unknown).</summary>
        public double Volume(string bus) => Volumes.TryGetValue(bus, out double v) ? v : 1.0;

        /// <summary>Effective linear gain of a bus (bus × master).</summary>
        public double Gain(string bus) => bus == "Master" ? Volume("Master") : Volume(bus) * Volume("Master");

        /// <summary>Sets a bus volume (clamped).</summary>
        public void SetVolume(string bus, double v)
        {
            if (Volumes.ContainsKey(bus))
            {
                Volumes[bus] = Clamp(v, 0, 1);
            }
        }

        /// <summary>Clamps every value into its valid range.</summary>
        public void Sanitize()
        {
            Quality = (int)Clamp(Quality, 0, QualityLevels.Length - 1);
            MouseSensitivity = Clamp(MouseSensitivity, 0.1, 5);
            FieldOfView = Clamp(FieldOfView, 50, 100);
            if (ResolutionWidth < 640 || ResolutionHeight < 360)
            {
                ResolutionWidth = 0;
                ResolutionHeight = 0;
            }

            if (Array.IndexOf(Languages, Language) < 0)
            {
                Language = "es";
            }

            foreach (string b in Buses)
            {
                Volumes[b] = Clamp(Volumes[b], 0, 1);
            }
        }

        /// <summary>Serialises to JSON.</summary>
        public JsonValue ToJson()
        {
            JsonValue o = JsonValue.NewObject();
            o.Set("version", 1);
            o.Set("quality", Quality);
            o.Set("resolutionWidth", ResolutionWidth);
            o.Set("resolutionHeight", ResolutionHeight);
            o.Set("window", Window.ToString());
            o.Set("vsync", VSync);
            o.Set("mouseSensitivity", MouseSensitivity);
            o.Set("invertY", InvertY);
            o.Set("fov", FieldOfView);
            o.Set("inspectBlur", InspectBlur);
            o.Set("language", Language);
            JsonValue vol = JsonValue.NewObject();
            foreach (string b in Buses)
            {
                vol.Set(b, Volumes[b]);
            }

            o.Set("volumes", vol);
            o.Set("bindings", Bindings.ToJson());
            return o;
        }

        /// <summary>Reads settings; missing or invalid entries keep their defaults.</summary>
        public static GameSettings FromJson(JsonValue o)
        {
            var s = new GameSettings();
            if (!o.IsObject)
            {
                return s;
            }

            s.Quality = o.Int("quality", s.Quality);
            s.ResolutionWidth = o.Int("resolutionWidth");
            s.ResolutionHeight = o.Int("resolutionHeight");
            if (Enum.TryParse(o.Str("window"), out WindowMode w) && Enum.IsDefined(typeof(WindowMode), w))
            {
                s.Window = w;
            }

            s.VSync = o.Bool("vsync", s.VSync);
            s.MouseSensitivity = o.Num("mouseSensitivity", s.MouseSensitivity);
            s.InvertY = o.Bool("invertY", s.InvertY);
            s.FieldOfView = o.Num("fov", s.FieldOfView);
            s.InspectBlur = o.Bool("inspectBlur", s.InspectBlur);
            s.Language = o.Str("language", s.Language);
            JsonValue vol = o["volumes"];
            foreach (string b in Buses)
            {
                s.Volumes[b] = vol.Num(b, 1.0);
            }

            s.Bindings.LoadOverrides(o["bindings"]);
            s.Sanitize();
            return s;
        }

        /// <summary>Parses a settings file's text; corrupt text gives defaults.</summary>
        public static GameSettings Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new GameSettings();
            }

            try
            {
                return FromJson(JsonValue.Parse(text!));
            }
            catch (JsonParseException)
            {
                return new GameSettings();
            }
        }

        private static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : Math.Max(lo, Math.Min(hi, v));
    }
}
