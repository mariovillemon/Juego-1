using Garage.Sim.Faults;
using Garage.Sim.Vehicle;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Garage.Unity
{
    /// <summary>
    /// Visual wear from simulation data: dirt, grease, rust and dust from age/km/maintenance, applied to the car
    /// materials (darkening, roughening, rust tint) and as HDRP decals on the body and floor. Faulty parts get a
    /// matching look (cracked hose, corroded connector, sooty plug).
    /// </summary>
    public sealed class WearController : MonoBehaviour
    {
        [Tooltip("Material de decal para suciedad (se crea si falta)")] public Material dirtDecal;
        [Tooltip("Material de decal para óxido")] public Material rustDecal;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        /// <summary>Dirt 0..1 last applied.</summary>
        public float Dirt { get; private set; }

        /// <summary>Rust 0..1 last applied.</summary>
        public float Rust { get; private set; }

        private bool _leaking;

        /// <summary>Applies wear to every renderer under the car root.</summary>
        public void Apply(Car car, Transform root)
        {
            AppearanceDefinition a = car.Definition.Appearance;
            Dirt = (float)a.Dirt;
            Rust = (float)a.Rust;
            float grease = (float)a.Grease;
            float fade = (float)a.PaintFade;
            Color dirtTint = new Color(0.32f, 0.27f, 0.2f);
            Color rustTint = new Color(0.35f, 0.16f, 0.07f);
            Transform bay = root.Find("EngineBay");
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                Material m = r.material; // instance per renderer
                if (!m.HasProperty(BaseColor))
                {
                    continue;
                }

                Color c = m.GetColor(BaseColor);
                bool underHood = bay != null && r.transform.IsChildOf(bay);
                float dirtAmount = underHood ? Mathf.Clamp01(Dirt * 0.6f + grease * 0.6f) : Dirt * 0.45f;
                c = Color.Lerp(c, Desaturate(c), fade * 0.5f);
                c = Color.Lerp(c, dirtTint, dirtAmount * 0.55f);
                c = Color.Lerp(c, rustTint, underHood ? Rust * 0.35f : Rust * 0.15f);
                // Dust: a car that sits unwashed gets a light, flat film on the bodywork.
                float dust = underHood ? 0f : Mathf.Clamp01((1f - (float)a.Maintenance) * (float)a.AgeYears / 15f) * 0.35f;
                c = Color.Lerp(c, new Color(0.55f, 0.52f, 0.47f, c.a), dust);
                m.SetColor(BaseColor, c);
                if (m.HasProperty(Smoothness))
                {
                    m.SetFloat(Smoothness, Mathf.Lerp(m.GetFloat(Smoothness), 0.2f, dirtAmount));
                }
            }

            _leaking = false;
            foreach (var slot in root.GetComponentsInChildren<ComponentSlot>())
            {
                ApplyFaultLook(car, slot);
            }

            AddDecals(root);
            if (_leaking)
            {
                AddPuddle(root);
            }
        }

        private static Color Desaturate(Color c)
        {
            float g = c.grayscale;
            return new Color(g, g, g, c.a);
        }

        private void ApplyFaultLook(Car car, ComponentSlot slot)
        {
            Renderer[] renderers = slot.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }

            foreach (FaultInstance f in car.Faults.Unrepaired())
            {
                if (f.ComponentId != slot.componentId)
                {
                    continue;
                }

                Color? look = null;
                switch (f.Effect)
                {
                    case EffectKind.Leak when f.Severity > 0.5f:
                        look = new Color(0.05f, 0.05f, 0.05f); // perished, cracked rubber
                        break;
                    case EffectKind.ConnectorCorrosion:
                        look = new Color(0.25f, 0.4f, 0.3f); // verdigris
                        break;
                    case EffectKind.Wear when slot.kind == Garage.Sim.Components.ComponentKind.SparkPlug:
                        look = new Color(0.08f, 0.07f, 0.06f); // sooty
                        break;
                    case EffectKind.Restriction when slot.kind == Garage.Sim.Components.ComponentKind.Catalyst:
                        look = new Color(0.3f, 0.28f, 0.45f); // heat discolouration
                        break;
                    case EffectKind.Restriction when slot.kind == Garage.Sim.Components.ComponentKind.AirFilter:
                        look = new Color(0.22f, 0.2f, 0.16f); // clogged, dusty
                        break;
                }

                if (f.Mode.Id.Contains("leak_fluid") || f.Mode.Id.Contains("oil"))
                {
                    _leaking = true;
                }

                if (look == null)
                {
                    continue;
                }

                foreach (Renderer r in renderers)
                {
                    Material m = r.material;
                    if (m.HasProperty(BaseColor))
                    {
                        m.SetColor(BaseColor, look.Value);
                    }
                }
            }
        }

        private void AddDecals(Transform root)
        {
            if (Dirt < 0.2f && Rust < 0.1f)
            {
                return;
            }

            Material mat = Rust > Dirt ? rustDecal : dirtDecal;
            if (mat == null)
            {
                Shader s = Shader.Find("HDRP/Decal");
                if (s == null)
                {
                    return;
                }

                mat = new Material(s) { name = "WearDecal", enableInstancing = true };
                mat.SetColor("_BaseColor", Rust > Dirt ? new Color(0.35f, 0.16f, 0.07f, Rust) : new Color(0.25f, 0.2f, 0.15f, Dirt));
            }

            foreach (float z in new[] { -1.5f, 0f, 1.5f })
            {
                var go = new GameObject("WearDecal");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0.95f, 0.4f, z);
                go.transform.localRotation = Quaternion.Euler(0, -90, 0);
                var d = go.AddComponent<DecalProjector>();
                d.material = mat;
                d.size = new Vector3(1.4f, 0.5f, 0.4f);
                d.fadeFactor = Mathf.Clamp01(Mathf.Max(Dirt, Rust));
            }
        }

        /// <summary>Dark puddle decal on the floor under the engine of a car that leaks fluids.</summary>
        private void AddPuddle(Transform root)
        {
            Shader s = Shader.Find("HDRP/Decal");
            if (s == null)
            {
                return;
            }

            var mat = new Material(s) { name = "OilPuddle", enableInstancing = true };
            mat.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.015f, 0.85f));
            var go = new GameObject("OilPuddle");
            go.transform.SetParent(root, false);
            Transform bay = root.Find("EngineBay");
            go.transform.localPosition = new Vector3(0, 0.02f, bay != null ? bay.localPosition.z : 1.4f);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var d = go.AddComponent<DecalProjector>();
            d.material = mat;
            d.size = new Vector3(0.7f, 0.5f, 0.6f);
        }
    }
}
