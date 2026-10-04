using System.Collections.Generic;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>Body silhouettes for the procedural car.</summary>
    public enum BodyStyle
    {
        SmallHatch,
        Hatch,
        Sedan,
        Coupe,
    }

    /// <summary>
    /// Procedural car body: lofted rounded cross-sections (lower shell, fenders with real wheel arches, nose,
    /// greenhouse, roof), lathed tyres with spoked rims and brake discs, lights, mirrors and handles.
    /// Real-world dimensions; the front <c>bayLen</c> metres are left open for the engine bay.
    /// A model at Resources/CarBodies/&lt;carId&gt; replaces all of this (see docs/ART_PIPELINE.md).
    /// </summary>
    public static class CarBodyBuilder
    {
        public struct Mats
        {
            public Material Paint, Glass, Dark, Rubber, Rim, Chrome, HeadLamp, TailLamp, Disc;
        }

        private struct Dims
        {
            public float Len, HalfW, Bottom, Belt, Roof, WheelR, Track, Wheelbase;
            public float WindshieldBase, RoofFront, RoofRear, RearGlassBase; // z positions
        }

        /// <summary>Builds the body under <paramref name="root"/>. Returns the z of the firewall.</summary>
        public static float Build(Transform root, BodyStyle style, float len, float wheelR, float bayLen, Mats m)
        {
            Dims d = DimsFor(style, len, wheelR, bayLen);
            float front = len / 2, rear = -len / 2, firewall = front - bayLen;
            var frontAxle = front - 0.85f;
            var rearAxle = frontAxle - d.Wheelbase;

            // Lower shell from the rear bumper to the firewall, with rear arches.
            Loft(root, "Body_Shell", m.Paint, rear, firewall, 0.04f, z =>
            {
                float t = Mathf.InverseLerp(rear, rear + 0.35f, z); // rounded tail
                float hw = d.HalfW - 0.06f * (1 - t * t);
                float top = d.Belt - 0.05f * (1 - t);
                float bottom = Mathf.Max(d.Bottom + 0.08f * (1 - t), Arch(z, rearAxle, d));
                return Section(hw - 0.05f, hw, bottom, top, 0.1f);
            });

            // Front fenders (left and right) around the bay, with front arches.
            foreach (float sgn in new[] { -1f, 1f })
            {
                Loft(root, sgn < 0 ? "Fender_L" : "Fender_R", m.Paint, firewall, front - 0.18f, 0.04f, z =>
                {
                    float t = Mathf.InverseLerp(front - 0.5f, front - 0.18f, z);
                    float top = d.Belt - 0.06f * t * t;
                    float bottom = Mathf.Max(d.Bottom, Arch(z, frontAxle, d));
                    var s = Section(0.0f, 0.0f, bottom, top, 0.06f, d.HalfW - 0.2f, d.HalfW - 0.03f * t);
                    return Mirror(s, sgn);
                });
            }

            // Nose: bumper + grille, full width, below the radiator top.
            Loft(root, "Bumper_Front", m.Paint, front - 0.2f, front, 0.025f, z =>
            {
                float t = Mathf.InverseLerp(front - 0.2f, front, z);
                float hw = d.HalfW - 0.03f - 0.12f * t * t;
                return Section(hw - 0.04f, hw, d.Bottom + 0.06f + 0.06f * t, d.Belt - 0.22f - 0.05f * t, 0.08f);
            });
            Box(root, "Grille", m.Dark, new Vector3(0, d.Belt - 0.32f, front - 0.005f), new Vector3(d.HalfW * 1.1f, 0.12f, 0.02f));

            // Greenhouse (glass) and roof panel.
            float roofTopAt(float z)
            {
                if (z > d.RoofFront)
                {
                    return Mathf.Lerp(d.Roof, d.Belt, Smooth(Mathf.InverseLerp(d.RoofFront, d.WindshieldBase, z)));
                }

                if (z < d.RoofRear)
                {
                    return Mathf.Lerp(d.Roof, d.Belt, Smooth(Mathf.InverseLerp(d.RoofRear, d.RearGlassBase, z)));
                }

                return d.Roof;
            }

            Loft(root, "Body_Cabin", m.Glass, d.RearGlassBase, d.WindshieldBase, 0.04f, z =>
            {
                float top = Mathf.Max(d.Belt + 0.01f, roofTopAt(z));
                float hTop = d.HalfW - 0.07f - 0.17f * Mathf.InverseLerp(d.Belt, d.Roof, top);
                return Section(d.HalfW - 0.07f, hTop, d.Belt - 0.01f, top, 0.05f);
            });
            Loft(root, "Roof", m.Paint, d.RoofRear - 0.05f, d.RoofFront + 0.05f, 0.05f, z =>
            {
                float top = roofTopAt(z) + 0.015f;
                float hw = d.HalfW - 0.23f;
                return Section(hw, hw - 0.02f, top - 0.03f, top, 0.015f);
            });

            // Pillars (paint) so the glass reads as windows.
            foreach (float sgn in new[] { -1f, 1f })
            {
                Box(root, "BPillar", m.Paint, new Vector3(sgn * (d.HalfW - 0.15f), (d.Belt + d.Roof) / 2, (d.RoofFront + d.RoofRear) / 2 + 0.1f), new Vector3(0.05f, d.Roof - d.Belt, 0.09f), new Vector3(0, 0, -sgn * 17));
                Box(root, "Mirror", m.Paint, new Vector3(sgn * (d.HalfW + 0.06f), d.Belt + 0.08f, d.WindshieldBase - 0.15f), new Vector3(0.12f, 0.08f, 0.06f));
                Box(root, "DoorHandle", m.Chrome, new Vector3(sgn * (d.HalfW + 0.005f), d.Belt - 0.12f, (d.RoofFront + d.RoofRear) / 2 - 0.2f), new Vector3(0.02f, 0.025f, 0.13f));
                Box(root, "HeadLamp", m.HeadLamp, new Vector3(sgn * (d.HalfW - 0.2f), d.Belt - 0.13f, front - 0.06f), new Vector3(0.3f, 0.09f, 0.12f), new Vector3(0, -sgn * 12, 0));
                Box(root, "TailLamp", m.TailLamp, new Vector3(sgn * (d.HalfW - 0.15f), d.Belt - 0.1f, rear + 0.04f), new Vector3(0.28f, 0.08f, 0.06f));
            }

            Box(root, "Plate_F", m.Chrome, new Vector3(0, d.Bottom + 0.25f, front + 0.005f), new Vector3(0.52f, 0.11f, 0.01f));
            Box(root, "Plate_R", m.Chrome, new Vector3(0, d.Belt - 0.22f, rear - 0.005f), new Vector3(0.52f, 0.11f, 0.01f));
            Box(root, "Underbody", m.Dark, new Vector3(0, d.Bottom + 0.02f, (rear + front) / 2), new Vector3(d.HalfW * 1.7f, 0.04f, len * 0.9f));

            foreach (float z in new[] { frontAxle, rearAxle })
            {
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Wheel(root, new Vector3(sgn * d.Track / 2, wheelR, z), sgn, wheelR, m);
                }
            }

            return firewall;
        }

        /// <summary>Choice of silhouette from simple car data.</summary>
        public static BodyStyle StyleFor(double massKg, int cylinders)
        {
            if (cylinders >= 6) return BodyStyle.Coupe;
            if (massKg > 1450) return BodyStyle.Sedan;
            if (massKg < 1150) return BodyStyle.SmallHatch;
            return BodyStyle.Hatch;
        }

        private static Dims DimsFor(BodyStyle style, float len, float wheelR, float bayLen)
        {
            float front = len / 2, rear = -len / 2;
            var d = new Dims { Len = len, WheelR = wheelR, HalfW = 0.88f, Bottom = 0.16f, Belt = 0.98f, Roof = 1.45f, Track = 1.52f };
            d.Wheelbase = len * 0.6f;
            d.WindshieldBase = front - bayLen;
            switch (style)
            {
                case BodyStyle.SmallHatch:
                    d.HalfW = 0.83f; d.Roof = 1.48f; d.Track = 1.44f;
                    d.RoofFront = d.WindshieldBase - 0.75f; d.RoofRear = rear + 0.45f; d.RearGlassBase = rear + 0.12f;
                    break;
                case BodyStyle.Hatch:
                    d.RoofFront = d.WindshieldBase - 0.8f; d.RoofRear = rear + 0.6f; d.RearGlassBase = rear + 0.18f;
                    break;
                case BodyStyle.Sedan:
                    d.HalfW = 0.91f; d.Roof = 1.46f; d.Track = 1.56f;
                    d.RoofFront = d.WindshieldBase - 0.85f; d.RoofRear = rear + 1.35f; d.RearGlassBase = rear + 0.85f;
                    break;
                default: // coupe
                    d.HalfW = 0.92f; d.Roof = 1.32f; d.Belt = 0.93f; d.Bottom = 0.12f; d.Track = 1.6f;
                    d.RoofFront = d.WindshieldBase - 0.95f; d.RoofRear = rear + 1.55f; d.RearGlassBase = rear + 0.75f;
                    break;
            }

            return d;
        }

        /// <summary>Bottom of the body over a wheel: a circular arch 6 cm larger than the tyre.</summary>
        private static float Arch(float z, float axle, Dims d)
        {
            float r = d.WheelR + 0.06f;
            float dz = z - axle;
            return Mathf.Abs(dz) >= r ? 0 : d.WheelR + Mathf.Sqrt(r * r - dz * dz);
        }

        private static float Smooth(float t) => t * t * (3 - 2 * t);

        /// <summary>
        /// Rounded trapezoid cross-section (CCW, seen from the front), symmetric unless an explicit inner/outer x
        /// range is given (used for fenders).
        /// </summary>
        private static List<Vector2> Section(float hwBottom, float hwTop, float y0, float y1, float r, float xIn = float.NaN, float xOut = float.NaN)
        {
            Vector2[] c;
            if (float.IsNaN(xIn))
            {
                c = new[] { new Vector2(-hwBottom, y0), new Vector2(hwBottom, y0), new Vector2(hwTop, y1), new Vector2(-hwTop, y1) };
            }
            else
            {
                c = new[] { new Vector2(xIn, y0), new Vector2(xOut - 0.03f, y0), new Vector2(xOut, y1 - 0.05f), new Vector2(xIn, y1) };
            }

            var pts = new List<Vector2>();
            const int seg = 4;
            for (int i = 0; i < 4; i++)
            {
                Vector2 prev = c[(i + 3) % 4], cur = c[i], next = c[(i + 1) % 4];
                float rr = Mathf.Min(r, (cur - prev).magnitude * 0.45f, (next - cur).magnitude * 0.45f);
                Vector2 a = cur + (prev - cur).normalized * rr;
                Vector2 b = cur + (next - cur).normalized * rr;
                for (int s = 0; s <= seg; s++)
                {
                    float t = s / (float)seg;
                    pts.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * cur + t * t * b);
                }
            }

            return pts;
        }

        private static List<Vector2> Mirror(List<Vector2> s, float sgn)
        {
            if (sgn > 0)
            {
                return s;
            }

            var m = new List<Vector2>(s.Count);
            for (int i = s.Count - 1; i >= 0; i--)
            {
                m.Add(new Vector2(-s[i].x, s[i].y));
            }

            return m;
        }

        /// <summary>Lofts sections sampled every <paramref name="step"/> metres along z, with end caps.</summary>
        private static GameObject Loft(Transform parent, string name, Material mat, float z0, float z1, float step, System.Func<float, List<Vector2>> section)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt((z1 - z0) / step) + 1);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int ring = -1;
            for (int i = 0; i < n; i++)
            {
                float z = Mathf.Lerp(z0, z1, i / (float)(n - 1));
                List<Vector2> s = section(z);
                ring = s.Count;
                foreach (Vector2 p in s)
                {
                    verts.Add(new Vector3(p.x, p.y, z));
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < ring; j++)
                {
                    int a = i * ring + j, b = i * ring + (j + 1) % ring, c = a + ring, e = b + ring;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(e);
                }
            }

            // Caps with their own vertices (hard edge).
            foreach (int end in new[] { 0, n - 1 })
            {
                int start = verts.Count;
                Vector3 centre = Vector3.zero;
                for (int j = 0; j < ring; j++)
                {
                    Vector3 v = verts[end * ring + j];
                    verts.Add(v);
                    centre += v;
                }

                verts.Add(centre / ring);
                int ci = verts.Count - 1;
                for (int j = 0; j < ring; j++)
                {
                    int a = start + j, b = start + (j + 1) % ring;
                    if (end == 0) { tris.Add(ci); tris.Add(a); tris.Add(b); }
                    else { tris.Add(ci); tris.Add(b); tris.Add(a); }
                }
            }

            // Triangles above assume clockwise sections; reverse everything for counter-clockwise ones.
            float area = 0;
            for (int j = 0; j < ring; j++)
            {
                Vector3 p = verts[j], q = verts[(j + 1) % ring];
                area += p.x * q.y - q.x * p.y;
            }

            if (area > 0)
            {
                tris.Reverse();
            }

            return MeshObject(parent, name, mat, verts, tris);
        }

        private static GameObject MeshObject(Transform parent, string name, Material mat, List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.layer = 2; // Ignore Raycast: never steals the aim from a component
            return go;
        }

        private static GameObject Box(Transform parent, string name, Material mat, Vector3 pos, Vector3 size, Vector3 rot = default)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(rot);
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            g.layer = 2;
            return g;
        }

        /// <summary>Tyre lathed from a rounded profile, spoked rim, hub and brake disc.</summary>
        private static void Wheel(Transform parent, Vector3 pos, float sgn, float r, Mats m)
        {
            var w = new GameObject("Wheel").transform;
            w.SetParent(parent, false);
            w.localPosition = pos;

            float width = 0.2f, rimR = r * 0.64f;
            var profile = new List<Vector2>(); // (radius, axial) around the x axis
            const int seg = 6;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * i / seg;
                profile.Add(new Vector2(r - 0.035f + Mathf.Sin(a) * 0.035f, -width / 2 + width * i / seg + Mathf.Cos(a) * -0.0f));
            }

            profile.Insert(0, new Vector2(rimR, -width / 2 + 0.01f));
            profile.Add(new Vector2(rimR, width / 2 - 0.01f));

            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int around = 40;
            for (int k = 0; k <= around; k++)
            {
                float ang = 2 * Mathf.PI * k / around;
                foreach (Vector2 p in profile)
                {
                    verts.Add(new Vector3(p.y, Mathf.Cos(ang) * p.x, Mathf.Sin(ang) * p.x));
                }
            }

            int pc = profile.Count;
            for (int k = 0; k < around; k++)
            {
                for (int j = 0; j < pc - 1; j++)
                {
                    int a = k * pc + j, b = a + 1, c = a + pc, e = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(e);
                }
            }

            GameObject tyre = MeshObject(w, "Tyre", m.Rubber, verts, tris);
            Object.Destroy(tyre.GetComponent<MeshCollider>());

            // Rim face, hub and five spokes on the outer side.
            float outer = sgn * (width / 2 - 0.03f);
            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Place(barrel, w, "RimBarrel", m.Rim, new Vector3(0, 0, 0), new Vector3(rimR * 2, width / 2 - 0.012f, rimR * 2), new Vector3(0, 0, 90));
            Object.Destroy(barrel.GetComponent<Collider>());
            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Place(face, w, "RimLip", m.Chrome, new Vector3(outer, 0, 0), new Vector3(rimR * 2.04f, 0.006f, rimR * 2.04f), new Vector3(0, 0, 90));
            Object.Destroy(face.GetComponent<Collider>());
            GameObject inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Place(inner, w, "RimDish", m.Dark, new Vector3(outer - sgn * 0.005f, 0, 0), new Vector3(rimR * 1.9f, 0.004f, rimR * 1.9f), new Vector3(0, 0, 90));
            Object.Destroy(inner.GetComponent<Collider>());
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Place(disc, w, "BrakeDisc", m.Disc, new Vector3(-sgn * 0.02f, 0, 0), new Vector3(rimR * 1.6f, 0.012f, rimR * 1.6f), new Vector3(0, 0, 90));
            Object.Destroy(disc.GetComponent<Collider>());
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Place(hub, w, "Hub", m.Chrome, new Vector3(outer + sgn * 0.005f, 0, 0), new Vector3(0.11f, 0.012f, 0.11f), new Vector3(0, 0, 90));
            Object.Destroy(hub.GetComponent<Collider>());
            for (int i = 0; i < 5; i++)
            {
                GameObject spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                float a = i * 72f;
                Place(spoke, w, "Spoke", m.Rim, new Vector3(outer + sgn * 0.002f, 0, 0), new Vector3(0.025f, rimR * 1.9f, 0.05f), new Vector3(a, 0, 0));
                Object.Destroy(spoke.GetComponent<Collider>());
            }
        }

        private static void Place(GameObject g, Transform parent, string name, Material mat, Vector3 pos, Vector3 scale, Vector3 rot)
        {
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(rot);
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            g.layer = 2;
        }
    }
}
