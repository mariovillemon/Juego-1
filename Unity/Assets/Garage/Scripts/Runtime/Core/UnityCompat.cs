using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Garage.Unity
{
    /// <summary>
    /// Version-tolerant helpers for HDRP/Unity APIs that moved between releases (light units moved from
    /// HDRP to the core Light in Unity 2023+). Reflection keeps the project compiling across 6000.0.x.
    /// </summary>
    public static class UnityCompat
    {
        /// <summary>Sets intensity with a physical unit ("Lumen", "Lux", "Candela", "Nits", "Ev100").</summary>
        public static bool SetLightIntensity(Light light, float value, string unit)
        {
            PropertyInfo unitProp = typeof(Light).GetProperty("lightUnit", BindingFlags.Public | BindingFlags.Instance);
            if (unitProp != null && unitProp.CanWrite)
            {
                try
                {
                    unitProp.SetValue(light, Enum.Parse(unitProp.PropertyType, unit));
                    light.intensity = value;
                    return true;
                }
                catch (ArgumentException)
                {
                }
            }

            HDAdditionalLightData hd = light.GetComponent<HDAdditionalLightData>();
            if (hd == null)
            {
                hd = light.gameObject.AddComponent<HDAdditionalLightData>();
            }

            foreach (MethodInfo m in typeof(HDAdditionalLightData).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] p = m.GetParameters();
                if (m.Name == "SetIntensity" && p.Length == 2 && p[1].ParameterType.IsEnum)
                {
                    try
                    {
                        m.Invoke(hd, new object[] { value, Enum.Parse(p[1].ParameterType, unit) });
                        return true;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            light.intensity = value;
            return false;
        }

        /// <summary>Reads the light unit name, or null if it cannot be determined.</summary>
        public static string GetLightUnit(Light light)
        {
            PropertyInfo unitProp = typeof(Light).GetProperty("lightUnit", BindingFlags.Public | BindingFlags.Instance);
            if (unitProp != null)
            {
                return unitProp.GetValue(light)?.ToString();
            }

            HDAdditionalLightData hd = light.GetComponent<HDAdditionalLightData>();
            PropertyInfo hdProp = hd == null ? null : typeof(HDAdditionalLightData).GetProperty("lightUnit");
            return hdProp?.GetValue(hd)?.ToString();
        }

        /// <summary>Configures temperature in Kelvin.</summary>
        public static void SetTemperature(Light light, float kelvin)
        {
            light.useColorTemperature = true;
            light.colorTemperature = kelvin;
            light.color = Color.white;
        }

        /// <summary>Ensures a light has the HDRP additional data component.</summary>
        public static HDAdditionalLightData EnsureHd(Light light)
        {
            HDAdditionalLightData hd = light.GetComponent<HDAdditionalLightData>();
            return hd != null ? hd : light.gameObject.AddComponent<HDAdditionalLightData>();
        }

        /// <summary>Creates an HDRP Lit material (falls back to Standard if HDRP shader is missing).</summary>
        public static Material LitMaterial(string name, Color baseColor, float metallic, float smoothness)
        {
            Shader s = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            var m = new Material(s) { name = name };
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_Color", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>Layer used by device screens (scanner, scope...). Hidden from the main camera.</summary>
        public const int DeviceUiLayer = 31;
    }
}
