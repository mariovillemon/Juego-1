using System.Collections.Generic;
using Garage.Sim.Components;
using SimComponent = Garage.Sim.Components.Component;
using Garage.Sim.Vehicle;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Builds the visual car from its data: a placeholder body with an open engine bay, a procedural engine sized
    /// by cylinder count, and one interactive slot per simulated component at a plausible real-world location.
    /// Slots never overlap each other or the engine (an overlap resolver nudges them apart) and small parts get a
    /// slightly enlarged hitbox so they are easy to aim at. Real models replace placeholders by name
    /// (see docs/ART_PIPELINE.md): a prefab at Resources/CarParts/&lt;slot&gt; or Resources/CarParts/&lt;Kind&gt;.
    /// </summary>
    public sealed class CarAssembler : MonoBehaviour
    {
        [Tooltip("Altura del coche sobre el elevador")] public float liftHeight = 0f;
        [Tooltip("Tamaño mínimo de la zona clicable de una pieza (m)")] public float minHitbox = 0.05f;
        public WearController wear;

        private readonly Dictionary<string, ComponentSlot> _slots = new Dictionary<string, ComponentSlot>();
        private readonly List<Bounds> _occupied = new List<Bounds>();
        private readonly Dictionary<ComponentKind, int> _kindCount = new Dictionary<ComponentKind, int>();

        /// <summary>Slots by component id.</summary>
        public IReadOnlyDictionary<string, ComponentSlot> Slots => _slots;

        /// <summary>Root of the current car.</summary>
        public Transform CarRoot { get; private set; }

        private Material _paint, _dark, _rubber, _glass, _alu, _castIron, _steel, _copper, _sensor, _hose, _battery;
        private float _len, _ew; // car length, engine width
        private int _cyl;

        /// <summary>Destroys the previous car and builds a new one.</summary>
        public void Build(Car car)
        {
            if (CarRoot != null)
            {
                DestroyImmediate(CarRoot.gameObject);
            }

            _slots.Clear();
            _occupied.Clear();
            _kindCount.Clear();
            CarDefinition def = car.Definition;
            var root = new GameObject($"Car_{def.Id}");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0, liftHeight, 0);
            CarRoot = root.transform;

            ColorUtility.TryParseHtmlString(def.Appearance.PaintColor, out Color paint);
            _paint = UnityCompat.LitMaterial("CarPaint", paint, 0.3f, 0.75f);
            _dark = UnityCompat.LitMaterial("BlackPlastic", new Color(0.04f, 0.04f, 0.045f), 0f, 0.35f);
            _rubber = UnityCompat.LitMaterial("Rubber", new Color(0.025f, 0.025f, 0.025f), 0f, 0.2f);
            _glass = UnityCompat.LitMaterial("Glass", new Color(0.1f, 0.12f, 0.13f, 0.4f), 0f, 0.95f);
            _alu = UnityCompat.LitMaterial("EngineAluminium", new Color(0.58f, 0.58f, 0.6f), 0.9f, 0.45f);
            _castIron = UnityCompat.LitMaterial("CastIron", new Color(0.22f, 0.21f, 0.2f), 0.8f, 0.25f);
            _steel = UnityCompat.LitMaterial("ExhaustSteel", new Color(0.35f, 0.27f, 0.2f), 0.9f, 0.3f);
            _copper = UnityCompat.LitMaterial("Coil", new Color(0.12f, 0.12f, 0.14f), 0.1f, 0.55f);
            _sensor = UnityCompat.LitMaterial("SensorBody", new Color(0.08f, 0.08f, 0.09f), 0f, 0.4f);
            _hose = UnityCompat.LitMaterial("Hose", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f);
            _battery = UnityCompat.LitMaterial("BatteryCase", new Color(0.1f, 0.12f, 0.16f), 0f, 0.5f);

            _len = def.MassKg > 1500 ? 4.6f : (def.MassKg < 1150 ? 3.9f : 4.3f);
            _cyl = Mathf.Max(1, def.Engine.Cylinders);
            _ew = 0.11f * _cyl + 0.16f;

            BuildBody(root.transform, def);

            Transform bay = new GameObject("EngineBay").transform;
            bay.SetParent(root.transform, false);
            bay.localPosition = new Vector3(0, 0.68f, _len * 0.5f - 0.75f);
            BuildEngine(bay);

            foreach (SimComponent c in car.Parts.All)
            {
                int n = _kindCount.TryGetValue(c.Kind, out int k) ? k : 0;
                _kindCount[c.Kind] = n + 1;
                Spec s = SpecFor(c, n);

                GameObject prefab = Resources.Load<GameObject>("CarParts/" + c.VisualSlot) ?? Resources.Load<GameObject>("CarParts/" + c.Kind);
                GameObject go;
                if (prefab != null)
                {
                    go = Instantiate(prefab, bay);
                    go.transform.localPosition = s.Pos;
                    go.transform.localRotation = Quaternion.Euler(s.Rot);
                }
                else
                {
                    go = Part(bay, c.VisualSlot, s.Shape, s.Pos, s.Size, s.Mat);
                    go.transform.localRotation = Quaternion.Euler(s.Rot);
                }

                go.name = "Slot_" + c.VisualSlot;
                Resolve(go, bay);
                FitHitbox(go);

                ComponentSlot slot = go.AddComponent<ComponentSlot>();
                slot.componentId = c.Id;
                slot.componentName = c.Name;
                slot.kind = c.Kind;
                slot.hasConnector = car.CircuitOf(c.Id) != null;
                _slots[c.Id] = slot;

                if (slot.hasConnector && ComponentKinds.IsSensor(c.Kind))
                {
                    // Small connector plug, purely visual (clicks go to the parent slot).
                    GameObject plug = Part(go.transform, "Connector", PrimitiveType.Cube, new Vector3(0, 0.6f, 0), new Vector3(0.7f, 0.35f, 0.7f), _dark);
                    Destroy(plug.GetComponent<Collider>());
                }
            }

            if (wear != null)
            {
                wear.Apply(car, root.transform);
            }
        }

        // ---------------------------------------------------------------- body

        private void BuildBody(Transform root, CarDefinition def)
        {
            const float bayLen = 1.2f;
            float frontZ = _len * 0.5f;
            float wheelR = (float)def.WheelRadiusM;
            float bayStart;

            GameObject model = Resources.Load<GameObject>("CarBodies/" + def.Id);
            if (model != null)
            {
                Instantiate(model, root).name = "Body_Model";
                bayStart = frontZ - bayLen;
            }
            else
            {
                var mats = new CarBodyBuilder.Mats
                {
                    Paint = _paint,
                    Glass = _glass,
                    Dark = _dark,
                    Rubber = _rubber,
                    Rim = UnityCompat.LitMaterial("Rim", new Color(0.62f, 0.63f, 0.65f), 1f, 0.7f),
                    Chrome = UnityCompat.LitMaterial("Chrome", new Color(0.8f, 0.8f, 0.82f), 1f, 0.9f),
                    HeadLamp = Lamp("HeadLamp", new Color(0.9f, 0.92f, 0.95f), 0.4f),
                    TailLamp = Lamp("TailLamp", new Color(0.6f, 0.02f, 0.02f), 0.6f),
                    Disc = UnityCompat.LitMaterial("BrakeDisc", new Color(0.3f, 0.28f, 0.27f), 0.9f, 0.4f),
                };
                bayStart = CarBodyBuilder.Build(root, CarBodyBuilder.StyleFor(def.MassKg, def.Engine.Cylinders), _len, wheelR, bayLen, mats);
            }

            // Engine bay internals (open top): firewall, floor pan, radiator.
            Static(root, "Firewall", PrimitiveType.Cube, new Vector3(0, 0.62f, bayStart + 0.02f), new Vector3(1.5f, 0.7f, 0.04f), _dark);
            Static(root, "Bay_Floor", PrimitiveType.Cube, new Vector3(0, 0.27f, frontZ - bayLen / 2), new Vector3(1.3f, 0.03f, bayLen - 0.2f), _dark);
            Static(root, "Radiator", PrimitiveType.Cube, new Vector3(0, 0.6f, frontZ - 0.24f), new Vector3(1.15f, 0.4f, 0.04f), _alu);

            // Hood hinged at the windshield, propped open.
            var hinge = new GameObject("Hood_Hinge").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(0, 0.97f, bayStart);
            hinge.localRotation = Quaternion.Euler(-70, 0, 0);
            Static(hinge, "Hood", PrimitiveType.Cube, new Vector3(0, 0, bayLen / 2 - 0.05f), new Vector3(1.6f, 0.025f, bayLen - 0.1f), _paint);
        }

        private static Material Lamp(string name, Color c, float smooth)
        {
            Material m = UnityCompat.LitMaterial(name, c, 0f, 0.95f);
            if (m.HasProperty("_EmissiveColor"))
            {
                m.SetColor("_EmissiveColor", c * smooth * 2f);
            }

            return m;
        }

        // -------------------------------------------------------------- engine

        // Bay space: x across the car (cylinders in a row, transverse), y up, z forward. Intake faces the
        // firewall (-z), exhaust faces the radiator (+z).
        private const float BlockH = 0.32f, BlockD = 0.34f, HeadH = 0.13f;
        private float BlockTop => 0.0f + BlockH / 2;
        private float HeadTop => BlockTop + HeadH;
        private float CoverTop => HeadTop + 0.05f;

        private void BuildEngine(Transform bay)
        {
            var e = new GameObject("Engine").transform;
            e.SetParent(bay, false);
            Track(Static(e, "Block", PrimitiveType.Cube, new Vector3(0, 0, 0), new Vector3(_ew, BlockH, BlockD), _castIron));
            Track(Static(e, "Head", PrimitiveType.Cube, new Vector3(0, BlockTop + HeadH / 2, 0), new Vector3(_ew, HeadH, BlockD * 0.9f), _alu));
            Track(Static(e, "CamCover", PrimitiveType.Cube, new Vector3(0, HeadTop + 0.025f, 0.02f), new Vector3(_ew - 0.04f, 0.05f, BlockD * 0.55f), _dark));
            Track(Static(e, "OilPan", PrimitiveType.Cube, new Vector3(0, -BlockH / 2 - 0.06f, 0), new Vector3(_ew - 0.04f, 0.12f, BlockD * 0.85f), _alu));
            Track(Static(e, "Transmission", PrimitiveType.Cylinder, new Vector3(_ew / 2 + 0.17f, -0.08f, -0.02f), new Vector3(0.3f, 0.16f, 0.3f), _alu, new Vector3(0, 0, 90)));

            // Intake plenum along the firewall side, with one runner per cylinder.
            float plenumZ = -BlockD / 2 - 0.14f;
            Track(Static(e, "IntakePlenum", PrimitiveType.Cube, new Vector3(0, HeadTop - 0.02f, plenumZ), new Vector3(_ew * 0.9f, 0.09f, 0.1f), _dark));
            for (int i = 0; i < _cyl; i++)
            {
                Static(e, "Runner_" + i, PrimitiveType.Cube, new Vector3(CylX(i), HeadTop - 0.06f, -BlockD / 2 - 0.05f), new Vector3(0.05f, 0.05f, 0.1f), _dark);
            }

            // Exhaust manifold on the radiator side.
            Track(Static(e, "ExhaustManifold", PrimitiveType.Cube, new Vector3(0, BlockTop + 0.02f, BlockD / 2 + 0.05f), new Vector3(_ew * 0.85f, 0.07f, 0.08f), _steel));
            // Air box on the right of the engine, above the transmission.
            Track(Static(e, "AirBoxLower", PrimitiveType.Cube, new Vector3(_ew / 2 + 0.2f, 0.2f, -0.12f), new Vector3(0.28f, 0.08f, 0.3f), _dark));
        }

        private float CylX(int i) => _cyl == 1 ? 0 : -_ew / 2 + 0.08f + (_ew - 0.16f) * i / (_cyl - 1);

        // --------------------------------------------------------------- slots

        private struct Spec
        {
            public Vector3 Pos, Size, Rot;
            public PrimitiveType Shape;
            public Material Mat;

            public Spec(Vector3 pos, Vector3 size, PrimitiveType shape, Material mat, Vector3 rot = default)
            {
                Pos = pos; Size = size; Shape = shape; Mat = mat; Rot = rot;
            }
        }

        private Spec SpecFor(SimComponent c, int n)
        {
            const PrimitiveType Box = PrimitiveType.Cube, Cyl = PrimitiveType.Cylinder, Ball = PrimitiveType.Sphere;
            float x = c.Cylinder >= 0 ? CylX(Mathf.Min(c.Cylinder, _cyl - 1)) : 0;
            float side = _ew / 2;
            float plenumZ = -BlockD / 2 - 0.14f;
            Vector3 flat = new Vector3(90, 0, 0);
            Vector3 across = new Vector3(0, 0, 90);
            switch (c.Kind)
            {
                // Per-cylinder parts on top of the head.
                case ComponentKind.IgnitionCoil: return new Spec(new Vector3(x, CoverTop + 0.045f, 0.0f), new Vector3(0.045f, 0.045f, 0.045f), Cyl, _copper);
                case ComponentKind.SparkPlug: return new Spec(new Vector3(x, CoverTop + 0.02f, 0.075f), new Vector3(0.022f, 0.02f, 0.022f), Cyl, _alu);
                case ComponentKind.GlowPlug: return new Spec(new Vector3(x, CoverTop + 0.02f, 0.075f), new Vector3(0.02f, 0.02f, 0.02f), Cyl, _alu);
                case ComponentKind.Injector: return new Spec(new Vector3(x, HeadTop + 0.01f, -BlockD / 2 - 0.015f), new Vector3(0.024f, 0.04f, 0.024f), Cyl, _dark, new Vector3(-30, 0, 0));
                case ComponentKind.IntakeGasket: return new Spec(new Vector3(x, HeadTop - 0.06f, -BlockD / 2 + 0.004f), new Vector3(0.06f, 0.06f, 0.008f), Box, _dark);
                case ComponentKind.Cylinder: return new Spec(new Vector3(x, -0.02f, BlockD / 2 + 0.006f), new Vector3(0.07f, 0.14f, 0.012f), Box, _castIron);
                case ComponentKind.HeadGasket: return new Spec(new Vector3(0, BlockTop + 0.004f, BlockD / 2 + 0.003f), new Vector3(_ew, 0.008f, 0.01f), Box, _steel);
                case ComponentKind.TimingDrive: return new Spec(new Vector3(-side - 0.025f, 0.05f, 0), new Vector3(0.05f, 0.36f, 0.3f), Box, _dark);

                // Intake side.
                case ComponentKind.ElectronicThrottle: return new Spec(new Vector3(side - 0.02f, HeadTop - 0.02f, plenumZ - 0.0f + 0.0f) + new Vector3(0.1f, 0, 0), new Vector3(0.09f, 0.09f, 0.07f), Box, _alu);
                case ComponentKind.MapSensor: return new Spec(new Vector3(-0.05f, HeadTop + 0.045f, plenumZ), new Vector3(0.035f, 0.025f, 0.035f), Box, _sensor);
                case ComponentKind.BoostSensor: return new Spec(new Vector3(0.12f, HeadTop + 0.045f, plenumZ), new Vector3(0.035f, 0.025f, 0.035f), Box, _sensor);
                case ComponentKind.PurgeValve: return new Spec(new Vector3(-side + 0.06f, HeadTop + 0.05f, plenumZ - 0.03f), new Vector3(0.04f, 0.04f, 0.04f), Box, _sensor);
                case ComponentKind.EgrValve: return new Spec(new Vector3(-side + 0.04f, BlockTop - 0.04f, -BlockD / 2 - 0.05f), new Vector3(0.07f, 0.07f, 0.06f), Box, _alu);
                case ComponentKind.VacuumHose: return new Spec(new Vector3(-0.15f - 0.08f * n, HeadTop + 0.03f, plenumZ - 0.12f), new Vector3(0.018f, 0.1f, 0.018f), Cyl, _hose, flat);
                case ComponentKind.KnockSensor: return new Spec(new Vector3(0.08f * n, -0.03f, -BlockD / 2 - 0.02f), new Vector3(0.035f, 0.035f, 0.03f), Cyl, _sensor, flat);
                case ComponentKind.CkpSensor: return new Spec(new Vector3(side - 0.05f, -BlockH / 2 - 0.02f, -BlockD / 2 - 0.02f), new Vector3(0.025f, 0.03f, 0.025f), Cyl, _sensor, flat);
                case ComponentKind.CmpSensor: return new Spec(new Vector3(-side - 0.02f, HeadTop - 0.04f, -0.1f), new Vector3(0.025f, 0.03f, 0.025f), Cyl, _sensor, across);
                case ComponentKind.FuelPressureSensor: return new Spec(new Vector3(side - 0.06f, HeadTop + 0.03f, -BlockD / 2 - 0.035f), new Vector3(0.025f, 0.035f, 0.025f), Cyl, _sensor);
                case ComponentKind.FuelPressureRegulator: return new Spec(new Vector3(-side + 0.06f, HeadTop + 0.03f, -BlockD / 2 - 0.035f), new Vector3(0.035f, 0.035f, 0.035f), Cyl, _alu);

                // Air path on the right.
                case ComponentKind.AirFilter: return new Spec(new Vector3(side + 0.2f, 0.29f, -0.12f), new Vector3(0.28f, 0.08f, 0.3f), Box, _dark);
                case ComponentKind.MafSensor: return new Spec(new Vector3(side + 0.2f, 0.29f, -0.32f), new Vector3(0.07f, 0.07f, 0.08f), Box, _sensor);
                case ComponentKind.IatSensor: return new Spec(new Vector3(side + 0.11f, 0.29f, -0.32f), new Vector3(0.025f, 0.03f, 0.025f), Cyl, _sensor, across);

                // Exhaust side / front.
                case ComponentKind.Turbocharger: return new Spec(new Vector3(side - 0.12f, BlockTop - 0.06f, BlockD / 2 + 0.17f), new Vector3(0.18f, 0.18f, 0.18f), Ball, _steel);
                case ComponentKind.WastegateSolenoid: return new Spec(new Vector3(side + 0.05f, BlockTop + 0.08f, BlockD / 2 + 0.12f), new Vector3(0.04f, 0.04f, 0.04f), Box, _sensor);
                case ComponentKind.BoostHose: return new Spec(new Vector3(side + 0.1f + 0.08f * n, -0.12f, BlockD / 2 + 0.25f), new Vector3(0.05f, 0.11f, 0.05f), Cyl, _hose);
                case ComponentKind.Intercooler: return new Spec(new Vector3(0, -0.4f, BlockD / 2 + 0.38f), new Vector3(0.7f, 0.14f, 0.05f), Box, _alu);
                case ComponentKind.O2Narrowband:
                case ComponentKind.O2Wideband: return new Spec(new Vector3(-0.1f + 0.1f * n, BlockTop + 0.09f, BlockD / 2 + 0.06f), new Vector3(0.022f, 0.035f, 0.022f), Cyl, _sensor);
                case ComponentKind.Catalyst: return new Spec(new Vector3(-0.12f, -0.12f, BlockD / 2 + 0.12f), new Vector3(0.12f, 0.22f, 0.12f), Cyl, _steel);
                case ComponentKind.O2Downstream: return new Spec(new Vector3(-0.12f, -0.27f, BlockD / 2 + 0.12f), new Vector3(0.022f, 0.035f, 0.022f), Cyl, _sensor, flat);
                case ComponentKind.Exhaust: return new Spec(new Vector3(-0.12f, -0.42f, -0.3f), new Vector3(0.06f, 0.35f, 0.06f), Cyl, _steel, flat);
                case ComponentKind.CoolingFan: return new Spec(new Vector3(0, -0.1f + 0.0f, BlockD / 2 + 0.32f) + new Vector3(-0.3f + 0.3f * n, 0.2f, 0), new Vector3(0.28f, 0.03f, 0.28f), Cyl, _dark, flat);
                case ComponentKind.Thermostat: return new Spec(new Vector3(-side - 0.04f, HeadTop - 0.02f, 0.1f), new Vector3(0.06f, 0.06f, 0.06f), Ball, _alu);
                case ComponentKind.EctSensor: return new Spec(new Vector3(-side - 0.04f, HeadTop + 0.03f, 0.1f), new Vector3(0.022f, 0.03f, 0.022f), Cyl, _sensor);
                case ComponentKind.Alternator: return new Spec(new Vector3(-side + 0.12f, -0.07f, BlockD / 2 + 0.1f), new Vector3(0.13f, 0.07f, 0.13f), Cyl, _alu, flat);

                // Electrical, left of the engine.
                case ComponentKind.Battery: return new Spec(new Vector3(-0.58f, -0.3f + 0.19f / 2 + 0.25f, 0.18f), new Vector3(0.2f, 0.19f, 0.28f), Box, _battery);
                case ComponentKind.ControlModule: return new Spec(new Vector3(-0.58f, 0.0f + 0.04f * n, -0.22f), new Vector3(0.18f, 0.035f, 0.15f), Box, _alu);
                case ComponentKind.Fuse: return new Spec(new Vector3(-0.64f + 0.03f * (n % 5), 0.12f, -0.02f + 0.04f * (n / 5)), new Vector3(0.02f, 0.025f, 0.012f), Box, Fuse(n));
                case ComponentKind.Relay: return new Spec(new Vector3(-0.5f + 0.05f * (n % 3), 0.13f, -0.02f + 0.05f * (n / 3)), new Vector3(0.035f, 0.035f, 0.035f), Box, _dark);
                case ComponentKind.GroundStrap: return new Spec(new Vector3(-side - 0.08f, -0.08f, -0.05f), new Vector3(0.1f, 0.012f, 0.025f), Box, _copper);
                case ComponentKind.CanBus: return new Spec(new Vector3(0, BlockTop + 0.05f, -0.52f), new Vector3(1.1f, 0.025f, 0.025f), Box, _hose);

                // Outside the bay: fuel system under the car, pedal in the footwell.
                case ComponentKind.FuelPump: return new Spec(new Vector3(0.3f, -0.5f, -_len + 0.9f), new Vector3(0.12f, 0.12f, 0.12f), Cyl, _dark);
                case ComponentKind.FuelFilter: return new Spec(new Vector3(0.45f, -0.5f, -_len + 1.4f), new Vector3(0.06f, 0.08f, 0.06f), Cyl, _alu, flat);
                case ComponentKind.AppSensor: return new Spec(new Vector3(-0.35f, -0.25f, -0.95f), new Vector3(0.06f, 0.12f, 0.04f), Box, _sensor);
                case ComponentKind.TpsSensor: return new Spec(new Vector3(side + 0.13f, HeadTop - 0.02f, plenumZ - 0.06f), new Vector3(0.03f, 0.03f, 0.02f), Box, _sensor);

                default:
                    // Unknown kinds: a shelf along the firewall.
                    return new Spec(new Vector3(-0.4f + 0.08f * n, BlockTop + 0.12f, -0.5f), new Vector3(0.05f, 0.05f, 0.05f), Box, ComponentKinds.IsSensor(c.Kind) ? _sensor : _alu);
            }
        }

        private Material Fuse(int n)
        {
            Color[] amp = { new Color(0.9f, 0.1f, 0.1f), new Color(0.1f, 0.3f, 0.9f), new Color(0.95f, 0.8f, 0.1f), new Color(0.2f, 0.7f, 0.2f) };
            return UnityCompat.LitMaterial("Fuse", amp[n % amp.Length], 0f, 0.6f);
        }

        // ------------------------------------------------------------ placement

        /// <summary>Moves a freshly placed slot until it no longer overlaps the engine or another slot.</summary>
        private void Resolve(GameObject go, Transform bay)
        {
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r == null)
            {
                return;
            }

            Vector3 up = bay.up * 0.012f;
            Vector3 outward = go.transform.position - bay.position;
            outward.y = 0;
            outward = outward.sqrMagnitude < 1e-4f ? bay.forward : outward.normalized;
            outward *= 0.012f;

            for (int step = 0; step < 40 && Overlaps(Shrink(r.bounds)); step++)
            {
                go.transform.position += step % 2 == 0 ? up : outward;
            }

            _occupied.Add(r.bounds);
        }

        private bool Overlaps(Bounds b)
        {
            foreach (Bounds o in _occupied)
            {
                if (o.Intersects(b))
                {
                    return true;
                }
            }

            return false;
        }

        private static Bounds Shrink(Bounds b)
        {
            b.Expand(-0.004f); // touching surfaces are fine, interpenetration is not
            return b;
        }

        private void Track(GameObject g) => _occupied.Add(Shrink(g.GetComponent<Renderer>().bounds));

        /// <summary>Box hitbox matching the visual, enlarged to <see cref="minHitbox"/> on each axis for tiny parts.</summary>
        private void FitHitbox(GameObject go)
        {
            foreach (Collider old in go.GetComponents<Collider>())
            {
                if (!(old is BoxCollider))
                {
                    Destroy(old);
                }
            }

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = go.AddComponent<BoxCollider>();
            }
            MeshFilter mf = go.GetComponent<MeshFilter>();
            Vector3 local = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.one;
            Vector3 s = go.transform.lossyScale;
            box.size = new Vector3(
                Mathf.Max(local.x, minHitbox / Mathf.Max(1e-4f, Mathf.Abs(s.x))),
                Mathf.Max(local.y, minHitbox / Mathf.Max(1e-4f, Mathf.Abs(s.y))),
                Mathf.Max(local.z, minHitbox / Mathf.Max(1e-4f, Mathf.Abs(s.z))));
            box.center = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
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

        /// <summary>Non-interactive geometry: on Ignore Raycast so it never steals the aim from a component.</summary>
        private static GameObject Static(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, Vector3 rot = default)
        {
            GameObject g = Part(parent, name, type, pos, scale, m);
            g.transform.localRotation = Quaternion.Euler(rot);
            g.layer = 2; // Ignore Raycast
            return g;
        }
    }
}
