using System.Collections.Generic;
using Garage.Sim.Components;
using SimComponent = Garage.Sim.Components.Component;
using Garage.Sim.Vehicle;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Builds the visual car from its data: a placeholder body plus an anchor per component VisualSlot with a
    /// primitive placeholder. Real models replace the placeholders by name (see docs/ART_PIPELINE.md):
    /// a prefab at Resources/CarParts/&lt;slot&gt; or Resources/CarParts/&lt;Kind&gt; is used when present.
    /// </summary>
    public sealed class CarAssembler : MonoBehaviour
    {
        [Tooltip("Altura del coche sobre el elevador")] public float liftHeight = 0f;
        public WearController wear;

        private readonly Dictionary<string, ComponentSlot> _slots = new Dictionary<string, ComponentSlot>();

        /// <summary>Slots by component id.</summary>
        public IReadOnlyDictionary<string, ComponentSlot> Slots => _slots;

        /// <summary>Root of the current car.</summary>
        public Transform CarRoot { get; private set; }

        /// <summary>Destroys the previous car and builds a new one.</summary>
        public void Build(Car car)
        {
            if (CarRoot != null)
            {
                DestroyImmediate(CarRoot.gameObject);
            }

            _slots.Clear();
            CarDefinition def = car.Definition;
            var root = new GameObject($"Car_{def.Id}");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0, liftHeight, 0);
            CarRoot = root.transform;

            ColorUtility.TryParseHtmlString(def.Appearance.PaintColor, out Color paint);
            Material body = UnityCompat.LitMaterial("CarPaint", paint, 0.3f, 0.75f);
            Material dark = UnityCompat.LitMaterial("BlackPlastic", new Color(0.04f, 0.04f, 0.045f), 0f, 0.35f);
            Material rubber = UnityCompat.LitMaterial("Tyre", new Color(0.025f, 0.025f, 0.025f), 0f, 0.2f);
            Material glass = UnityCompat.LitMaterial("Glass", new Color(0.1f, 0.12f, 0.13f, 0.4f), 0f, 0.95f);
            Material engine = UnityCompat.LitMaterial("EngineAluminium", new Color(0.55f, 0.55f, 0.56f), 0.9f, 0.45f);

            // Real-scale placeholder body (≈4.3 × 1.8 × 1.45 m compact).
            float length = def.MassKg > 1500 ? 4.6f : (def.MassKg < 1150 ? 3.9f : 4.3f);
            Part(root.transform, "Body_Lower", PrimitiveType.Cube, new Vector3(0, 0.55f, 0), new Vector3(1.8f, 0.6f, length), body);
            Part(root.transform, "Body_Cabin", PrimitiveType.Cube, new Vector3(0, 1.1f, -0.2f), new Vector3(1.6f, 0.55f, length * 0.5f), glass);
            Part(root.transform, "Hood_Open", PrimitiveType.Cube, new Vector3(0, 1.45f, length * 0.33f), new Vector3(1.6f, 0.03f, 1.0f), body).transform.localRotation = Quaternion.Euler(-60, 0, 0);
            foreach (var w in new[] { new Vector3(-0.8f, 0.32f, length * 0.32f), new Vector3(0.8f, 0.32f, length * 0.32f), new Vector3(-0.8f, 0.32f, -length * 0.32f), new Vector3(0.8f, 0.32f, -length * 0.32f) })
            {
                GameObject wheel = Part(root.transform, "Wheel", PrimitiveType.Cylinder, w, new Vector3(2 * (float)def.WheelRadiusM, 0.11f, 2 * (float)def.WheelRadiusM), rubber);
                wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }

            Transform bay = new GameObject("EngineBay").transform;
            bay.SetParent(root.transform, false);
            bay.localPosition = new Vector3(0, 0.85f, length * 0.33f);
            Part(bay, "EngineBlock", PrimitiveType.Cube, new Vector3(0, 0, 0), new Vector3(0.6f, 0.45f, 0.5f), engine);

            int i = 0;
            foreach (SimComponent c in car.Parts.All)
            {
                Vector3 pos = SlotPosition(c, i++, car.Definition.Engine.Cylinders);
                GameObject prefab = Resources.Load<GameObject>("CarParts/" + c.VisualSlot) ?? Resources.Load<GameObject>("CarParts/" + c.Kind);
                GameObject go;
                if (prefab != null)
                {
                    go = Instantiate(prefab, bay);
                    go.transform.localPosition = pos;
                }
                else
                {
                    go = Part(bay, c.VisualSlot, ShapeFor(c.Kind), pos, SizeFor(c.Kind), ComponentKinds.IsSensor(c.Kind) ? dark : engine);
                }

                go.name = "Slot_" + c.VisualSlot;
                ComponentSlot slot = go.AddComponent<ComponentSlot>();
                slot.componentId = c.Id;
                slot.componentName = c.Name;
                slot.kind = c.Kind;
                slot.hasConnector = car.CircuitOf(c.Id) != null;
                _slots[c.Id] = slot;
            }

            if (wear != null)
            {
                wear.Apply(car, root.transform);
            }
        }

        private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        private static PrimitiveType ShapeFor(ComponentKind k)
        {
            switch (k)
            {
                case ComponentKind.VacuumHose:
                case ComponentKind.BoostHose:
                case ComponentKind.SparkPlug:
                case ComponentKind.IgnitionCoil:
                case ComponentKind.Injector:
                    return PrimitiveType.Cylinder;
                case ComponentKind.Turbocharger:
                case ComponentKind.Thermostat:
                    return PrimitiveType.Sphere;
                default:
                    return PrimitiveType.Cube;
            }
        }

        private static Vector3 SizeFor(ComponentKind k)
        {
            switch (k)
            {
                case ComponentKind.Battery: return new Vector3(0.18f, 0.19f, 0.28f);
                case ComponentKind.Turbocharger: return new Vector3(0.22f, 0.22f, 0.22f);
                case ComponentKind.Intercooler: return new Vector3(0.6f, 0.15f, 0.06f);
                case ComponentKind.IgnitionCoil: return new Vector3(0.03f, 0.08f, 0.03f);
                case ComponentKind.SparkPlug: return new Vector3(0.02f, 0.04f, 0.02f);
                case ComponentKind.Injector: return new Vector3(0.02f, 0.05f, 0.02f);
                case ComponentKind.VacuumHose: return new Vector3(0.02f, 0.2f, 0.02f);
                case ComponentKind.BoostHose: return new Vector3(0.06f, 0.3f, 0.06f);
                case ComponentKind.ElectronicThrottle: return new Vector3(0.1f, 0.1f, 0.08f);
                case ComponentKind.Alternator: return new Vector3(0.14f, 0.14f, 0.14f);
                default: return ComponentKinds.IsSensor(k) ? new Vector3(0.035f, 0.035f, 0.05f) : new Vector3(0.08f, 0.08f, 0.08f);
            }
        }

        /// <summary>Deterministic placement around the engine block (placeholder until models define anchors).</summary>
        private static Vector3 SlotPosition(SimComponent c, int index, int cylinders)
        {
            if (c.Cylinder >= 0)
            {
                float x = -0.24f + 0.48f * c.Cylinder / Mathf.Max(1, cylinders - 1);
                switch (c.Kind)
                {
                    case ComponentKind.IgnitionCoil: return new Vector3(x, 0.3f, 0.05f);
                    case ComponentKind.SparkPlug: return new Vector3(x, 0.22f, 0.05f);
                    case ComponentKind.Injector: return new Vector3(x, 0.2f, -0.18f);
                    case ComponentKind.IntakeGasket: return new Vector3(x, 0.12f, -0.27f);
                    default: return new Vector3(x, 0f, 0f);
                }
            }

            switch (c.Kind)
            {
                case ComponentKind.Battery: return new Vector3(-0.65f, 0f, 0.2f);
                case ComponentKind.Turbocharger: return new Vector3(0.45f, -0.05f, 0.25f);
                case ComponentKind.Intercooler: return new Vector3(0f, -0.3f, 0.75f);
                case ComponentKind.ElectronicThrottle: return new Vector3(-0.35f, 0.15f, -0.3f);
                case ComponentKind.Alternator: return new Vector3(0.35f, -0.1f, 0.3f);
                case ComponentKind.ControlModule:
                case ComponentKind.Fuse:
                case ComponentKind.Relay: return new Vector3(-0.7f, 0.1f, -0.3f + 0.04f * (index % 10));
                default:
                    float a = index * 2.39996f;
                    return new Vector3(Mathf.Cos(a) * 0.55f, 0.1f + (index % 5) * 0.06f, Mathf.Sin(a) * 0.45f);
            }
        }
    }
}
