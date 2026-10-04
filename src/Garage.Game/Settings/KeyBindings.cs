using System.Collections.Generic;
using System.Linq;
using Garage.Data.Json;

namespace Garage.Game.Settings
{
    /// <summary>A rebindable action.</summary>
    public sealed class BindableAction
    {
        /// <summary>Creates the action.</summary>
        public BindableAction(string id, string defaultPath, string labelKey)
        {
            Id = id;
            DefaultPath = defaultPath;
            LabelKey = labelKey;
        }

        /// <summary>Stable id.</summary>
        public string Id { get; }

        /// <summary>Default Input System path ("&lt;Keyboard&gt;/e").</summary>
        public string DefaultPath { get; }

        /// <summary>Localization key of the label.</summary>
        public string LabelKey { get; }
    }

    /// <summary>
    /// Keyboard bindings as Input System control paths. Only overrides are stored. Rebinding to a key that another
    /// action uses swaps the two, so no key is ever left bound twice. Escape (pause) is fixed.
    /// </summary>
    public sealed class KeyBindings
    {
        /// <summary>All rebindable actions in display order.</summary>
        public static readonly IReadOnlyList<BindableAction> Actions = new[]
        {
            new BindableAction("forward", "<Keyboard>/w", "key.forward"),
            new BindableAction("back", "<Keyboard>/s", "key.back"),
            new BindableAction("left", "<Keyboard>/a", "key.left"),
            new BindableAction("right", "<Keyboard>/d", "key.right"),
            new BindableAction("crouch", "<Keyboard>/leftCtrl", "key.crouch"),
            new BindableAction("lamp", "<Keyboard>/f", "key.lamp"),
            new BindableAction("use", "<Keyboard>/e", "key.use"),
            new BindableAction("drop", "<Keyboard>/g", "key.drop"),
            new BindableAction("unplug", "<Keyboard>/q", "key.unplug"),
            new BindableAction("repair", "<Keyboard>/r", "key.repair"),
            new BindableAction("connector", "<Keyboard>/c", "key.connector"),
            new BindableAction("key", "<Keyboard>/k", "key.ignition"),
            new BindableAction("road", "<Keyboard>/t", "key.road"),
            new BindableAction("car", "<Keyboard>/v", "key.car"),
            new BindableAction("board", "<Keyboard>/tab", "key.board"),
            new BindableAction("sheet", "<Keyboard>/j", "key.sheet"),
            new BindableAction("shop", "<Keyboard>/p", "key.shop"),
            new BindableAction("inventory", "<Keyboard>/i", "key.inventory"),
            new BindableAction("upgrades", "<Keyboard>/u", "key.upgrades"),
            new BindableAction("laptop", "<Keyboard>/l", "key.laptop"),
            new BindableAction("help", "<Keyboard>/f1", "key.help"),
            new BindableAction("fast", "<Keyboard>/leftShift", "key.fast"),
        };

        /// <summary>Paths that cannot be assigned (reserved for pause).</summary>
        public const string Reserved = "<Keyboard>/escape";

        private readonly Dictionary<string, string> _overrides = new Dictionary<string, string>();

        /// <summary>Current path of an action (default when not overridden; empty for unknown ids).</summary>
        public string PathOf(string id)
        {
            if (_overrides.TryGetValue(id, out string p))
            {
                return p;
            }

            BindableAction? a = Find(id);
            return a?.DefaultPath ?? "";
        }

        /// <summary>Short readable key name ("E", "LEFTCTRL").</summary>
        public static string Display(string path)
        {
            int i = path.LastIndexOf('/');
            return (i >= 0 ? path.Substring(i + 1) : path).ToUpperInvariant();
        }

        /// <summary>Binds an action to a path; if another action had it, that one takes the old key.
        /// Returns the id of the swapped action, or null.</summary>
        public string? Rebind(string id, string path)
        {
            BindableAction? a = Find(id);
            if (a == null || string.IsNullOrEmpty(path) || path == Reserved)
            {
                return null;
            }

            string old = PathOf(id);
            string? other = Actions.FirstOrDefault(x => x.Id != id && PathOf(x.Id) == path)?.Id;
            Set(id, path);
            if (other != null)
            {
                Set(other, old);
            }

            return other;
        }

        /// <summary>Back to the defaults.</summary>
        public void ResetAll() => _overrides.Clear();

        /// <summary>True when every action has a distinct key.</summary>
        public bool AllUnique() => Actions.Select(a => PathOf(a.Id)).Distinct().Count() == Actions.Count;

        /// <summary>Overrides as JSON.</summary>
        public JsonValue ToJson()
        {
            JsonValue o = JsonValue.NewObject();
            foreach (KeyValuePair<string, string> kv in _overrides.OrderBy(k => k.Key))
            {
                o.Set(kv.Key, kv.Value);
            }

            return o;
        }

        /// <summary>Loads overrides; unknown ids, the reserved key and duplicates are dropped.</summary>
        public void LoadOverrides(JsonValue o)
        {
            _overrides.Clear();
            foreach (KeyValuePair<string, JsonValue> m in o.Members)
            {
                string path = m.Value.StringValue;
                if (Find(m.Key) != null && path.StartsWith("<", System.StringComparison.Ordinal) && path != Reserved)
                {
                    _overrides[m.Key] = path;
                }
            }

            if (!AllUnique())
            {
                _overrides.Clear();
            }
        }

        private static BindableAction? Find(string id) => Actions.FirstOrDefault(a => a.Id == id);

        private void Set(string id, string path)
        {
            BindableAction a = Find(id)!;
            if (a.DefaultPath == path)
            {
                _overrides.Remove(id);
            }
            else
            {
                _overrides[id] = path;
            }
        }
    }
}
