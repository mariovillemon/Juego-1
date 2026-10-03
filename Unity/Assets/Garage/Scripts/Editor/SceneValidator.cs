using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Garage.Unity.EditorTools
{
    /// <summary>
    /// Garage/Validate/Check Scene: real-world scale, lights without physical units, materials without textures and
    /// static geometry without collision. Prints a report and returns the list of issues (used by tests).
    /// </summary>
    public static class SceneValidator
    {
        [MenuItem("Garage/Validate/Check Scene", priority = 40)]
        public static void CheckMenu()
        {
            List<string> issues = Check();
            if (issues.Count == 0)
            {
                Debug.Log("[Garage] Escena correcta: escala, luces, materiales y colisiones OK.");
                return;
            }

            foreach (string i in issues)
            {
                Debug.LogWarning("[Garage] " + i);
            }

            Debug.Log($"[Garage] Validación: {issues.Count} avisos.");
        }

        /// <summary>Runs all checks on the open scene.</summary>
        public static List<string> Check()
        {
            var issues = new List<string>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                Vector3 size = r.bounds.size;
                if (r is MeshRenderer && (size.x > 60 || size.y > 30 || size.z > 60))
                {
                    issues.Add($"Escala sospechosa: {Path(r.transform)} mide {size} m (1 unidad = 1 m).");
                }

                if (r is MeshRenderer && size.magnitude < 0.002f)
                {
                    issues.Add($"Objeto diminuto: {Path(r.transform)} ({size.magnitude * 1000:0.0} mm).");
                }

                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && m.shader != null && m.shader.name == "HDRP/Lit" && m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") == null)
                    {
                        issues.Add($"Material sin texturas (placeholder): {m.name} en {Path(r.transform)}. Ejecuta Garage/Assets/Download Free Assets.");
                        break;
                    }
                }

                bool isStatic = GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.ContributeGI);
                if (isStatic && r.GetComponent<Collider>() == null)
                {
                    issues.Add($"Geometría estática sin colisión: {Path(r.transform)}.");
                }
            }

            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.gameObject.layer == UnityCompat.DeviceUiLayer)
                {
                    continue;
                }

                string unit = UnityCompat.GetLightUnit(l);
                bool physical = unit == "Lumen" || unit == "Lux" || unit == "Candela" || unit == "Nits" || unit == "Ev100";
                if (!physical)
                {
                    issues.Add($"Luz sin unidades físicas: {Path(l.transform)} (unidad '{unit ?? "?"}').");
                }

                if (!l.useColorTemperature && l.type != LightType.Directional)
                {
                    issues.Add($"Luz sin temperatura de color en Kelvin: {Path(l.transform)}.");
                }
            }

            return issues;
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
