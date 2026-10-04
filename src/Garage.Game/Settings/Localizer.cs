using System;
using System.Collections.Generic;
using System.IO;
using Garage.Data.Json;

namespace Garage.Game.Settings
{
    /// <summary>
    /// String tables per language (<c>data/locale/&lt;lang&gt;.json</c>: <c>{"language": "es", "strings": {key: text}}</c>).
    /// Lookups fall back to Spanish (the reference table) and then to the key itself, so a missing translation is
    /// visible but never breaks the UI. <c>{0}</c>-style placeholders are filled with <see cref="string.Format(string, object[])"/>.
    /// </summary>
    public sealed class Localizer
    {
        /// <summary>Reference language: every key must exist in it.</summary>
        public const string Reference = "es";

        private readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Shared instance used by the UI.</summary>
        public static Localizer Current { get; set; } = new Localizer();

        /// <summary>Active language.</summary>
        public string Language { get; set; } = Reference;

        /// <summary>Raised when the language changes.</summary>
        public event Action? Changed;

        /// <summary>Languages loaded.</summary>
        public IEnumerable<string> Loaded => _tables.Keys;

        /// <summary>Keys of a table (empty when not loaded).</summary>
        public IEnumerable<string> Keys(string lang) => _tables.TryGetValue(lang, out var t) ? t.Keys : (IEnumerable<string>)Array.Empty<string>();

        /// <summary>Loads every <c>*.json</c> table from a folder (missing folder: nothing loaded).</summary>
        public void LoadFolder(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (string f in Directory.GetFiles(dir, "*.json"))
            {
                LoadTable(File.ReadAllText(f));
            }
        }

        /// <summary>Loads one table from JSON text.</summary>
        public void LoadTable(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            string lang = root.Str("language");
            if (lang.Length == 0)
            {
                throw new FormatException("Tabla de idioma sin 'language'.");
            }

            if (!_tables.TryGetValue(lang, out Dictionary<string, string> table))
            {
                table = new Dictionary<string, string>();
                _tables[lang] = table;
            }

            foreach (KeyValuePair<string, JsonValue> m in root["strings"].Members)
            {
                table[m.Key] = m.Value.StringValue;
            }
        }

        /// <summary>Switches language and notifies listeners.</summary>
        public void SetLanguage(string lang)
        {
            if (lang == Language)
            {
                return;
            }

            Language = lang;
            Changed?.Invoke();
        }

        /// <summary>Text for a key in the active language.</summary>
        public string Get(string key)
        {
            if (_tables.TryGetValue(Language, out var t) && t.TryGetValue(key, out string s))
            {
                return s;
            }

            if (_tables.TryGetValue(Reference, out var r) && r.TryGetValue(key, out s))
            {
                return s;
            }

            return key;
        }

        /// <summary>Formatted text for a key.</summary>
        public string Format(string key, params object[] args)
        {
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key), args);
            }
            catch (FormatException)
            {
                return Get(key);
            }
        }

        /// <summary>Keys present in the reference table but missing from another one.</summary>
        public List<string> Missing(string lang)
        {
            var list = new List<string>();
            if (!_tables.TryGetValue(Reference, out var r))
            {
                return list;
            }

            _tables.TryGetValue(lang, out var t);
            foreach (string k in r.Keys)
            {
                if (t == null || !t.ContainsKey(k))
                {
                    list.Add(k);
                }
            }

            return list;
        }
    }
}
