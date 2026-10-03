using System;
using System.Collections.Generic;
using Garage.Sim.Components;

namespace Garage.Sim.Vehicle
{
    /// <summary>Component lookup by id, kind and cylinder.</summary>
    public sealed class ComponentRegistry
    {
        private readonly Dictionary<string, Component> _byId = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Component> _all = new List<Component>();

        /// <summary>All components in insertion order.</summary>
        public IReadOnlyList<Component> All => _all;

        /// <summary>Adds a component.</summary>
        public void Add(Component c)
        {
            if (_byId.ContainsKey(c.Id))
            {
                throw new ArgumentException($"Duplicate component id '{c.Id}'.");
            }

            _byId[c.Id] = c;
            _all.Add(c);
        }

        /// <summary>Replaces a component instance (part swap) keeping the id.</summary>
        public void Replace(Component c)
        {
            int i = _all.FindIndex(x => string.Equals(x.Id, c.Id, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                Add(c);
                return;
            }

            _all[i] = c;
            _byId[c.Id] = c;
        }

        /// <summary>Gets by id or null.</summary>
        public Component? Get(string id) => _byId.TryGetValue(id, out Component c) ? c : null;

        /// <summary>First component of a kind (optionally for a cylinder) or null.</summary>
        public Component? Find(ComponentKind kind, int cylinder = -1)
        {
            foreach (Component c in _all)
            {
                if (c.Kind == kind && (cylinder < 0 || c.Cylinder == cylinder))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>All components of a kind.</summary>
        public IEnumerable<Component> OfKind(ComponentKind kind)
        {
            foreach (Component c in _all)
            {
                if (c.Kind == kind)
                {
                    yield return c;
                }
            }
        }

        /// <summary>Id of first component of a kind or empty string.</summary>
        public string IdOf(ComponentKind kind, int cylinder = -1) => Find(kind, cylinder)?.Id ?? "";
    }
}
