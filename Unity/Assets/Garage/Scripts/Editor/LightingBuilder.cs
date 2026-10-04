using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Garage.Unity.EditorTools
{
    /// <summary>
    /// Physical lighting for the workshop: LED high-bay/panels in lumens at 4500 K, a sun directional light in lux
    /// entering through the door, a global volume with physically based sky, light probe grid and reflection probes.
    /// Real-world reference: workshop task lighting ≈ 500 lx on the floor (EN 12464-1 vehicle repair: 300–500 lx).
    /// </summary>
    public static class LightingBuilder
    {
        /// <summary>Creates all lights under the given root.</summary>
        public static void BuildWorkshopLights(Transform root, float width, float depth, float height)
        {
            // 3 × 3 grid of LED high-bay fixtures, 13 000 lm each (≈ 117 klm over 96 m² ≈ 600–700 lx with losses ≈ 500 lx).
            Material fixture = WorkshopSceneBuilder.Mat("LED_Fixture_Emissive", new Color(0.9f, 0.9f, 0.9f), 0f, 0.6f);
            for (int ix = 0; ix < 3; ix++)
            {
                for (int iz = 0; iz < 3; iz++)
                {
                    Vector3 pos = new Vector3(-width / 3 + ix * width / 3, height - 0.35f, -depth / 3 + iz * depth / 3);
                    var go = new GameObject($"LED_HighBay_{ix}{iz}");
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = pos;
                    go.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    Light l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.spotAngle = 120f;
                    l.innerSpotAngle = 80f;
                    l.range = 15f;
                    l.shadows = (ix + iz) % 2 == 0 ? LightShadows.Soft : LightShadows.None;
                    UnityCompat.EnsureHd(l);
                    UnityCompat.SetTemperature(l, 4500f);
                    UnityCompat.SetLightIntensity(l, 13000f, "Lumen");
                    GameObject housing = WorkshopSceneBuilder.Box(go.transform, "Housing", new Vector3(0, 0, -0.05f), new Vector3(0.6f, 0.6f, 0.08f), fixture, false);
                    Object.DestroyImmediate(housing.GetComponent<Collider>());
                }
            }

            // Bench task light (fluorescent-like, 4000 K).
            var bench = new GameObject("Bench_TaskLight");
            bench.transform.SetParent(root, false);
            bench.transform.localPosition = new Vector3(-2.4f, 2.2f, -depth / 2 + 0.5f);
            bench.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Light b = bench.AddComponent<Light>();
            b.type = LightType.Spot;
            b.spotAngle = 140f;
            b.range = 5f;
            UnityCompat.EnsureHd(b);
            UnityCompat.SetTemperature(b, 4000f);
            UnityCompat.SetLightIntensity(b, 5200f, "Lumen");

            // Office warm light.
            var office = new GameObject("Office_Light");
            office.transform.SetParent(root, false);
            office.transform.localPosition = new Vector3(width / 2 - 1.75f, 2.6f, -depth / 2 + 1.5f);
            Light o = office.AddComponent<Light>();
            o.type = LightType.Point;
            o.range = 5f;
            UnityCompat.EnsureHd(o);
            UnityCompat.SetTemperature(o, 3000f);
            UnityCompat.SetLightIntensity(o, 1600f, "Lumen");

            // Sun: up to 100 000 lx direct; daylight colour handled by the physically based sky.
            var sun = new GameObject("Sun");
            sun.transform.SetParent(root, false);
            Light s = sun.AddComponent<Light>();
            s.type = LightType.Directional;
            s.shadows = LightShadows.Soft;
            UnityCompat.EnsureHd(s);
            UnityCompat.SetTemperature(s, 5600f);
            UnityCompat.SetLightIntensity(s, 30000f, "Lux");
            WorkshopController.ApplySun(sun.transform, 10f, 40f);

            // Global volume (exposure, tonemapping, sky...).
            var volGo = new GameObject("GlobalVolume");
            volGo.transform.SetParent(root, false);
            Volume v = volGo.AddComponent<Volume>();
            v.isGlobal = true;
            v.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(HdrpSetup.VolumeProfilePath) ?? HdrpSetup.CreateVolumeProfile();
        }

        /// <summary>Light probe grid (1.5 m) and reflection probes for the bays and office.</summary>
        public static void AddProbes(Transform root, float width, float depth, float height)
        {
            var probes = new GameObject("LightProbes");
            probes.transform.SetParent(root, false);
            LightProbeGroup group = probes.AddComponent<LightProbeGroup>();
            var positions = new System.Collections.Generic.List<Vector3>();
            for (float x = -width / 2 + 0.75f; x < width / 2; x += 1.5f)
            {
                for (float z = -depth / 2 + 0.75f; z < depth / 2; z += 1.5f)
                {
                    foreach (float y in new[] { 0.3f, 1.6f, 3.5f })
                    {
                        positions.Add(new Vector3(x, y, z));
                    }
                }
            }

            group.probePositions = positions.ToArray();

            Vector3[] centers = { new Vector3(-2.5f, 1.6f, 0f), new Vector3(2.5f, 1.6f, 0f), new Vector3(width / 2 - 1.75f, 1.4f, -depth / 2 + 1.5f) };
            Vector3[] sizes = { new Vector3(6f, height, depth), new Vector3(6f, height, depth), new Vector3(3.5f, 3f, 3f) };
            for (int i = 0; i < centers.Length; i++)
            {
                var go = new GameObject($"ReflectionProbe_{i}");
                go.transform.SetParent(root, false);
                go.transform.localPosition = centers[i];
                ReflectionProbe rp = go.AddComponent<ReflectionProbe>();
                rp.size = sizes[i];
                rp.mode = ReflectionProbeMode.Baked;
                if (go.GetComponent<HDAdditionalReflectionData>() == null)
                {
                    go.AddComponent<HDAdditionalReflectionData>();
                }
            }
        }

        [MenuItem("Garage/Setup/Bake Lighting and Occlusion", priority = 5)]
        public static void Bake()
        {
            StaticOcclusionCulling.Compute();
            Lightmapping.BakeAsync();
            Debug.Log("[Garage] Bake de oclusión lanzado y bake de iluminación en segundo plano (Window > Rendering > Lighting).");
        }
    }
}
