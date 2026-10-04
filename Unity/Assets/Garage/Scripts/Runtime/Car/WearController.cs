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
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                Material m = r.material; // instance per renderer
                if (!m.HasProperty(BaseColor))
                {
                    continue;
                }

                Color c = m.GetColor(BaseColor);
                bool underHood = r.transform.parent != null && r.transform.parent.name == "EngineBay";
                float dirtAmount = underHood ? Mathf.Clamp01(Dirt * 0.6f + grease * 0.6f) : Dirt * 0.45f;
                c = Color.Lerp(c, Desaturate(c), fade * 0.5f);
                c = Color.Lerp(c, dirtTint, dirtAmount * 0.55f);
                c = Color.Lerp(c, rustTint, underHood ? Rust * 0.35f : Rust * 0.15f);
                m.SetColor(BaseColor, c);
                if (m.HasProperty(Smoothness))
                {
                    m.SetFloat(Smoothness, Mathf.Lerp(m.GetFloat(Smoothness), 0.2f, dirtAmount));
                }
            }

            foreach (var slot in root.GetComponentsInChildren<ComponentSlot>())
            {
                ApplyFaultLook(car, slot);
            }

            AddDecals(root);
        }

        private static Color Desaturate(Color c)
        {
            float g = c.grayscale;
            return new Color(g, g, g, c.a);
        }

        private static void ApplyFaultLook(Car car, ComponentSlot slot)
        {
            Renderer r = slot.GetComponent<Renderer>();
            if (r == null)
            {
                return;
            }

            foreach (FaultInstance f in car.Faults.Unrepaired())
            {
                if (f.ComponentId != slot.componentId)
                {
                    continue;
                }

                Material m = r.material;
                switch (f.Effect)
                {
                    case EffectKind.Leak when f.Severity > 0.5f:
                        m.SetColor(BaseColor, new Color(0.05f, 0.05f, 0.05f)); // perished rubber, cracks via decal
                        break;
                    case EffectKind.ConnectorCorrosion:
                        m.SetColor(BaseColor, new Color(0.25f, 0.4f, 0.3f)); // verdigris
                        break;
                    case EffectKind.Wear when slot.kind == Garage.Sim.Components.ComponentKind.SparkPlug:
                        m.SetColor(BaseColor, new Color(0.08f, 0.07f, 0.06f)); // sooty
                        break;
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
    }
}
