using Garage.Game;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>A tool that can be picked up, held and put down on a surface.</summary>
    public sealed class ToolItem : MonoBehaviour
    {
        [Tooltip("Id de herramienta (tool_scanner, tool_multimeter, tool_scope, tool_fuel_gauge, tool_compression, tool_leakdown, tool_smoke)")]
        public string toolId = CarWork.Scanner;
        public string displayName = "Escáner OBD";
        [Tooltip("Posición en la mano relativa a la cámara")] public Vector3 holdOffset = new Vector3(0.22f, -0.22f, 0.45f);
        public Vector3 holdEuler = new Vector3(-60, 0, 0);

        /// <summary>Snapped into the OBD port.</summary>
        public bool Plugged { get; set; }
    }

    /// <summary>OBD-II diagnostic connector under the dashboard (driver side). Snap point for the scan tool.</summary>
    public sealed class ObdPort : MonoBehaviour
    {
        /// <summary>Tool currently plugged.</summary>
        public ToolItem Plugged { get; set; }
    }

    /// <summary>A station the player uses (office computer, laptop, dyno console, old-parts box, car door).</summary>
    public sealed class Usable : MonoBehaviour
    {
        public enum Kind
        {
            Board,
            Laptop,
            DynoConsole,
            OldPartsBox,
            Shop,
            CarDoor,
            Upgrades,
        }

        public Kind kind;
        public string label = "Usar";
    }

    /// <summary>Second bay with the roller dyno: moves the current car onto the rollers and back.</summary>
    public sealed class DynoBay : MonoBehaviour
    {
        public Transform dynoAnchor;
        public Transform liftAnchor;
        public CarAssembler assembler;
        public SimulationRunner runner;

        /// <summary>Car currently on the dyno.</summary>
        public bool OnDyno { get; private set; }

        /// <summary>Automatic transition (no driving physics): the car is pushed onto the rollers.</summary>
        public string MoveCarToDyno()
        {
            if (assembler == null || assembler.CarRoot == null)
            {
                return "No hay coche en el elevador.";
            }

            Transform target = OnDyno ? liftAnchor : dynoAnchor;
            if (target == null)
            {
                return "Falta el anclaje del banco.";
            }

            assembler.CarRoot.SetPositionAndRotation(target.position, target.rotation);
            OnDyno = !OnDyno;
            runner.Session.SpendMinutes(10);
            return OnDyno ? "Coche colocado en el banco y amarrado (10 min)." : "Coche devuelto al elevador (10 min).";
        }

        /// <summary>The assembler rebuilt the car: it starts on the lift.</summary>
        public void OnCarRebuilt() => OnDyno = false;
    }

    /// <summary>
    /// Tutorial highlight: a floating amber marker above the scene object a tutorial step refers to
    /// ("tool:&lt;id&gt;", "obd", a component id) — UI targets are announced by the tutorial card.
    /// </summary>
    public static class TutorialHighlight
    {
        private static GameObject _marker;

        public static void Set(string highlight)
        {
            Transform target = Find(highlight);
            if (target == null)
            {
                if (_marker != null)
                {
                    _marker.SetActive(false);
                }

                return;
            }

            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _marker.name = "TutorialMarker";
                Object.Destroy(_marker.GetComponent<Collider>());
                _marker.transform.localScale = Vector3.one * 0.06f;
                Material m = UnityCompat.LitMaterial("Marker", new Color(1f, 0.69f, 0f), 0f, 0.5f);
                if (m.HasProperty("_EmissiveColor"))
                {
                    m.SetColor("_EmissiveColor", new Color(1f, 0.69f, 0f) * 40f);
                }

                _marker.GetComponent<Renderer>().sharedMaterial = m;
                _marker.AddComponent<MarkerBob>();
            }

            _marker.SetActive(true);
            _marker.GetComponent<MarkerBob>().target = target;
        }

        private static Transform Find(string id)
        {
            if (string.IsNullOrEmpty(id) || id.StartsWith("ui:"))
            {
                return null;
            }

            if (id == "obd")
            {
                ObdPort p = Object.FindFirstObjectByType<ObdPort>();
                return p != null ? p.transform : null;
            }

            if (id.StartsWith("tool:"))
            {
                string tool = id.Substring(5);
                foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
                {
                    if (t.toolId == tool)
                    {
                        return t.transform;
                    }
                }

                return null;
            }

            foreach (ComponentSlot s in Object.FindObjectsByType<ComponentSlot>(FindObjectsSortMode.None))
            {
                if (s.componentId == id)
                {
                    return s.transform;
                }
            }

            return null;
        }

        private sealed class MarkerBob : MonoBehaviour
        {
            public Transform target;

            private void Update()
            {
                if (target == null)
                {
                    gameObject.SetActive(false);
                    return;
                }

                transform.position = target.position + Vector3.up * (0.18f + Mathf.Sin(Time.unscaledTime * 4f) * 0.03f);
            }
        }
    }
}
