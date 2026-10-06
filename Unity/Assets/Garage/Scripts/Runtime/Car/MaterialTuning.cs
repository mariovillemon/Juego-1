using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Physically based values for the generated models' materials, chosen by material name (the FBX importer only
    /// carries colour, metallic and roughness): clear-coated car paint, real transparent glass and lamp lenses,
    /// polished chrome, rubber, cast metals. Applied on import (GarageModelPostprocessor) and again at runtime by
    /// <see cref="ModelFix"/> in case the import rules did not run. Idempotent.
    /// </summary>
    public static class MaterialTuning
    {
        private static readonly HashSet<int> Done = new HashSet<int>();

        private sealed class Spec
        {
            public float Smooth = -1, Metal = -1, Coat = -1, Alpha = -1;
            public Color? Tint;
        }

        private static readonly (string Prefix, Spec Spec)[] Table =
        {
            ("CarPaint", new Spec { Smooth = 0.86f, Metal = 0.35f, Coat = 1f }),
            ("Glass", new Spec { Smooth = 0.98f, Metal = 0f, Alpha = 0.32f, Tint = new Color(0.09f, 0.11f, 0.12f) }),
            ("HeadLampLens", new Spec { Smooth = 0.98f, Metal = 0f, Alpha = 0.18f, Coat = 1f }),
            ("TailLampLens", new Spec { Smooth = 0.95f, Metal = 0f, Alpha = 0.8f, Coat = 1f }),
            ("IndicatorLens", new Spec { Smooth = 0.95f, Metal = 0f, Alpha = 0.75f, Coat = 1f }),
            ("Chrome", new Spec { Smooth = 0.95f, Metal = 1f }),
            ("Rim", new Spec { Smooth = 0.72f, Metal = 1f, Coat = 0.6f }),
            ("Tyre", new Spec { Smooth = 0.22f, Metal = 0f }),
            ("Rubber", new Spec { Smooth = 0.25f, Metal = 0f }),
            ("Hose", new Spec { Smooth = 0.35f, Metal = 0f }),
            ("BlackTrim", new Spec { Smooth = 0.75f, Metal = 0f, Coat = 0.5f }),
            ("TexturedPlastic", new Spec { Smooth = 0.28f, Metal = 0f }),
            ("BlackPlastic", new Spec { Smooth = 0.45f, Metal = 0f }),
            ("Grille", new Spec { Smooth = 0.5f, Metal = 0.1f }),
            ("CastAluminium", new Spec { Smooth = 0.45f, Metal = 1f }),
            ("CastIron", new Spec { Smooth = 0.3f, Metal = 0.85f }),
            ("ExhaustSteel", new Spec { Smooth = 0.4f, Metal = 1f }),
            ("HeatShield", new Spec { Smooth = 0.55f, Metal = 1f }),
            ("Steel", new Spec { Smooth = 0.6f, Metal = 1f }),
            ("BrakeDisc", new Spec { Smooth = 0.55f, Metal = 1f }),
            ("Caliper", new Spec { Smooth = 0.6f, Metal = 0.2f, Coat = 0.7f }),
            ("SeatFabric", new Spec { Smooth = 0.05f, Metal = 0f }),
            ("Interior", new Spec { Smooth = 0.3f, Metal = 0f }),
            ("Underbody", new Spec { Smooth = 0.15f, Metal = 0f }),
        };

        /// <summary>Tunes every material of a model's renderers.</summary>
        public static void Apply(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    Tune(m);
                }
            }
        }

        /// <summary>Tunes one material by its name (unknown names are left untouched).</summary>
        public static void Tune(Material m)
        {
            if (m == null || !Done.Add(m.GetInstanceID()))
            {
                return;
            }

            Spec s = null;
            foreach ((string prefix, Spec spec) in Table)
            {
                if (m.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    s = spec;
                    break;
                }
            }

            if (s == null)
            {
                return;
            }

            if (s.Smooth >= 0)
            {
                Set(m, "_Smoothness", s.Smooth);
            }

            if (s.Metal >= 0)
            {
                Set(m, "_Metallic", s.Metal);
            }

            if (s.Coat >= 0)
            {
                // HDRP Lit "Coat Mask": a second specular lobe on top of the base, i.e. automotive clear coat.
                Set(m, "_CoatMask", s.Coat);
                m.EnableKeyword("_MATERIAL_FEATURE_CLEAR_COAT");
            }

            if (s.Alpha >= 0)
            {
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                if (s.Tint.HasValue)
                {
                    c = s.Tint.Value;
                }

                c.a = s.Alpha;
                if (m.HasProperty("_BaseColor"))
                {
                    m.SetColor("_BaseColor", c);
                }

                MakeTransparent(m);
            }

            Validate(m);
        }

        private static void Set(Material m, string prop, float v)
        {
            if (m.HasProperty(prop))
            {
                m.SetFloat(prop, v);
            }
        }

        /// <summary>HDRP Lit transparent surface (alpha blend, no depth write, preserved specular).</summary>
        private static void MakeTransparent(Material m)
        {
            if (Invoke("SetSurfaceType", m, true))
            {
                return;
            }

            Set(m, "_SurfaceType", 1);
            Set(m, "_BlendMode", 0);
            Set(m, "_EnableBlendModePreserveSpecularLighting", 1);
            Set(m, "_ZWrite", 0);
            Set(m, "_TransparentZWrite", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_BLENDMODE_ALPHA");
            m.EnableKeyword("_BLENDMODE_PRESERVE_SPECULAR_LIGHTING");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>Lets HDRP rebuild keywords and passes after property changes (HDMaterial.ValidateMaterial).</summary>
        private static void Validate(Material m) => Invoke("ValidateMaterial", m, null);

        private static Type _hdMaterial;

        private static bool Invoke(string method, Material m, object extra)
        {
            _hdMaterial ??= Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");
            if (_hdMaterial == null)
            {
                return false;
            }

            foreach (MethodInfo mi in _hdMaterial.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] p = mi.GetParameters();
                if (mi.Name != method || p.Length == 0 || p[0].ParameterType != typeof(Material))
                {
                    continue;
                }

                try
                {
                    if (extra == null && p.Length == 1)
                    {
                        mi.Invoke(null, new object[] { m });
                        return true;
                    }

                    if (extra is bool b && p.Length == 2 && p[1].ParameterType == typeof(bool))
                    {
                        mi.Invoke(null, new object[] { m, b });
                        return true;
                    }
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogWarning($"HDMaterial.{method}: {e.InnerException?.Message}");
                }
            }

            return false;
        }
    }
}
