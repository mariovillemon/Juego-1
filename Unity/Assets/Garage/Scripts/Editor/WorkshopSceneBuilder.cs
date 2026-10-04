using System.Collections.Generic;
using System.IO;
using Garage.Game;
using Garage.Unity.UI;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Garage.Unity.EditorTools
{
    /// <summary>
    /// Garage/Setup/Build Workshop Scene: procedurally builds a real-scale workshop (12 × 8 × 5 m) with concrete
    /// floor, walls, sectional door, two-post lift, pit, benches, shelving, roller dyno and office; physical lights
    /// (lumens, Kelvin), light and reflection probes, physically based sky with time of day, decals, the player and
    /// all simulation bridges. Re-running regenerates the scene file (idempotent).
    /// </summary>
    public static class WorkshopSceneBuilder
    {
        public const string ScenePath = EditorUtil.ScenesDir + "/Workshop.unity";
        public const float Width = 12f;
        public const float Depth = 8f;
        public const float Height = 5f;

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [MenuItem("Garage/Setup/Build Workshop Scene", priority = 2)]
        public static void BuildMenu()
        {
            Build(true);
        }

        /// <summary>Builds the scene; when save is false it is left unsaved (tests).</summary>
        public static Scene Build(bool save)
        {
            EditorUtil.EnsureFolder(EditorUtil.ScenesDir);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Mats.Clear();

            Transform world = new GameObject("__Workshop").transform;
            BuildShell(world);
            BuildEquipment(world);
            Transform lighting = new GameObject("__Lighting").transform;
            LightingBuilder.BuildWorkshopLights(lighting, Width, Depth, Height);
            LightingBuilder.AddProbes(lighting, Width, Depth, Height);
            BuildDecals(world);
            DressWithDownloadedModels(world);
            Transform sim = BuildSimulation(world, lighting);
            BuildPlayer(sim);

            if (save)
            {
                EditorSceneManager.SaveScene(scene, ScenePath);
                AddToBuild(ScenePath);
                Debug.Log("[Garage] Escena del taller generada en " + ScenePath);
            }

            return scene;
        }

        /// <summary>Adds a scene to the build settings if missing.</summary>
        public static void AddToBuild(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == path))
            {
                list.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
        }

        /// <summary>Gets/creates a named placeholder material with realistic PBR values.</summary>
        public static Material Mat(string name, Color albedo, float metallic, float smoothness)
        {
            if (Mats.TryGetValue(name, out Material m))
            {
                return m;
            }

            m = EditorUtil.SaveMaterial(UnityCompat.LitMaterial(name, albedo, metallic, smoothness), name);
            Mats[name] = m;
            return m;
        }

        /// <summary>Creates a static, collidable box.</summary>
        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material m, bool isStatic = true)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = center;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (isStatic)
            {
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            }

            return g;
        }

        private static void BuildShell(Transform world)
        {
            // Albedos in the realistic range (see docs/VISUAL_STYLE.md): concrete ≈ 0.25–0.35 linear.
            Material concrete = Mat("Concrete_Floor", new Color(0.30f, 0.29f, 0.27f), 0f, 0.35f);
            Material wall = Mat("Painted_Block_Wall", new Color(0.55f, 0.55f, 0.52f), 0f, 0.15f);
            Material steel = Mat("Painted_Steel", new Color(0.18f, 0.24f, 0.35f), 0.2f, 0.4f);
            Material roof = Mat("Sandwich_Panel", new Color(0.6f, 0.6f, 0.6f), 0.5f, 0.35f);
            Material door = Mat("Sectional_Door", new Color(0.62f, 0.63f, 0.64f), 0.6f, 0.45f);

            Transform shell = new GameObject("Shell").transform;
            shell.SetParent(world, false);

            // Floor in pieces around the pit (pit 1.0 m wide × 5 m long × 1.5 m deep at x=+3).
            float pitX = 3f;
            float pitW = 1.0f;
            float pitL = 5f;
            Box(shell, "Floor_Left", new Vector3((-Width / 2 + pitX - pitW / 2) / 2, -0.1f, 0), new Vector3(pitX - pitW / 2 + Width / 2, 0.2f, Depth), concrete);
            float rightStart = pitX + pitW / 2;
            Box(shell, "Floor_Right", new Vector3((rightStart + Width / 2) / 2, -0.1f, 0), new Vector3(Width / 2 - rightStart, 0.2f, Depth), concrete);
            float endLen = (Depth - pitL) / 2;
            Box(shell, "Floor_PitFront", new Vector3(pitX, -0.1f, pitL / 2 + endLen / 2), new Vector3(pitW, 0.2f, endLen), concrete);
            Box(shell, "Floor_PitBack", new Vector3(pitX, -0.1f, -pitL / 2 - endLen / 2), new Vector3(pitW, 0.2f, endLen), concrete);
            Transform pit = new GameObject("Pit").transform;
            pit.SetParent(shell, false);
            Box(pit, "Pit_Bottom", new Vector3(pitX, -1.55f, 0), new Vector3(pitW, 0.1f, pitL), concrete);
            Box(pit, "Pit_WallL", new Vector3(pitX - pitW / 2 - 0.05f, -0.8f, 0), new Vector3(0.1f, 1.5f, pitL), wall);
            Box(pit, "Pit_WallR", new Vector3(pitX + pitW / 2 + 0.05f, -0.8f, 0), new Vector3(0.1f, 1.5f, pitL), wall);
            Box(pit, "Pit_WallF", new Vector3(pitX, -0.8f, pitL / 2 + 0.05f), new Vector3(pitW, 1.5f, 0.1f), wall);
            Box(pit, "Pit_WallB", new Vector3(pitX, -0.8f, -pitL / 2 - 0.05f), new Vector3(pitW, 1.5f, 0.1f), wall);
            for (int s = 0; s < 6; s++)
            {
                Box(pit, $"Pit_Step_{s}", new Vector3(pitX, -1.5f + s * 0.25f + 0.125f, -pitL / 2 + 0.15f + s * 0.25f), new Vector3(pitW * 0.9f, 0.05f, 0.25f), steel);
            }

            // Walls 0.25 m thick; front wall (+z) has the sectional door opening 3.5 × 3.2 m.
            Box(shell, "Wall_Back", new Vector3(0, Height / 2, -Depth / 2 - 0.125f), new Vector3(Width + 0.5f, Height, 0.25f), wall);
            Box(shell, "Wall_Left", new Vector3(-Width / 2 - 0.125f, Height / 2, 0), new Vector3(0.25f, Height, Depth), wall);
            Box(shell, "Wall_Right", new Vector3(Width / 2 + 0.125f, Height / 2, 0), new Vector3(0.25f, Height, Depth), wall);
            float doorW = 3.5f;
            float doorH = 3.2f;
            float doorX = 2.5f;
            float leftW = doorX - doorW / 2 + Width / 2;
            Box(shell, "Wall_Front_L", new Vector3(-Width / 2 + leftW / 2, Height / 2, Depth / 2 + 0.125f), new Vector3(leftW, Height, 0.25f), wall);
            float rightW = Width / 2 - (doorX + doorW / 2);
            Box(shell, "Wall_Front_R", new Vector3(Width / 2 - rightW / 2, Height / 2, Depth / 2 + 0.125f), new Vector3(rightW, Height, 0.25f), wall);
            Box(shell, "Wall_Front_Lintel", new Vector3(doorX, (Height + doorH) / 2, Depth / 2 + 0.125f), new Vector3(doorW, Height - doorH, 0.25f), wall);
            Box(shell, "Roof", new Vector3(0, Height + 0.1f, 0), new Vector3(Width + 0.5f, 0.2f, Depth + 0.5f), roof);

            // Sectional door, half open (panels of 0.5 m rolled up near the ceiling).
            Transform sectional = new GameObject("SectionalDoor").transform;
            sectional.SetParent(shell, false);
            for (int p = 0; p < 6; p++)
            {
                bool lowered = p < 2;
                Vector3 pos = lowered ? new Vector3(doorX, doorH - 0.25f - p * 0.5f + 1.0f, Depth / 2 + 0.05f) : new Vector3(doorX, doorH + 0.05f, Depth / 2 - 0.3f - (p - 2) * 0.5f);
                Vector3 size = lowered ? new Vector3(doorW, 0.49f, 0.04f) : new Vector3(doorW, 0.04f, 0.49f);
                Box(sectional, $"Door_Panel_{p}", pos, size, door);
            }

            Box(sectional, "Door_Rail_L", new Vector3(doorX - doorW / 2 - 0.05f, doorH / 2 + 0.3f, Depth / 2 - 0.5f), new Vector3(0.06f, doorH + 0.6f, 0.06f), steel);
            Box(sectional, "Door_Rail_R", new Vector3(doorX + doorW / 2 + 0.05f, doorH / 2 + 0.3f, Depth / 2 - 0.5f), new Vector3(0.06f, doorH + 0.6f, 0.06f), steel);

            // Outside apron so the exterior light has something to bounce on.
            Box(shell, "Exterior_Apron", new Vector3(doorX, -0.12f, Depth / 2 + 4f), new Vector3(10f, 0.2f, 8f), Mat("Asphalt", new Color(0.08f, 0.08f, 0.08f), 0f, 0.25f));
        }

        private static void BuildEquipment(Transform world)
        {
            Material liftRed = Mat("Lift_Paint_Red", new Color(0.45f, 0.05f, 0.04f), 0.1f, 0.55f);
            Material steel = Mat("Bare_Steel", new Color(0.55f, 0.55f, 0.55f), 1f, 0.55f);
            Material wood = Mat("Bench_Top_Wood", new Color(0.35f, 0.24f, 0.14f), 0f, 0.3f);
            Material grey = Mat("Cabinet_Grey", new Color(0.25f, 0.26f, 0.27f), 0.3f, 0.45f);
            Material rubber = Mat("Rubber", new Color(0.02f, 0.02f, 0.02f), 0f, 0.25f);
            Material glass = Mat("Office_Glass", new Color(0.12f, 0.14f, 0.15f), 0f, 0.95f);

            Transform eq = new GameObject("Equipment").transform;
            eq.SetParent(world, false);

            // Two-post lift at x = -2.5 (bay 1).
            Transform lift = new GameObject("TwoPostLift").transform;
            lift.SetParent(eq, false);
            lift.localPosition = new Vector3(-2.5f, 0, 0);
            Box(lift, "Post_L", new Vector3(-1.6f, 1.45f, 0), new Vector3(0.3f, 2.9f, 0.35f), liftRed);
            Box(lift, "Post_R", new Vector3(1.6f, 1.45f, 0), new Vector3(0.3f, 2.9f, 0.35f), liftRed);
            Box(lift, "Crossbeam", new Vector3(0, 2.95f, 0), new Vector3(3.5f, 0.15f, 0.25f), liftRed);
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    Box(lift, "Arm", new Vector3(sx * 1.05f, 0.12f, sz * 0.55f), new Vector3(1.1f, 0.08f, 0.12f), steel);
                    Box(lift, "Pad", new Vector3(sx * 0.6f, 0.17f, sz * 0.95f), new Vector3(0.14f, 0.04f, 0.14f), rubber);
                }
            }

            UseModel(lift, "Workshop/TwoPostLift", Vector3.zero, "Post_L", "Post_R", "Crossbeam", "Arm", "Pad");

            Transform carAnchor = new GameObject("CarAnchor").transform;
            carAnchor.SetParent(lift, false);
            carAnchor.localPosition = new Vector3(0, 0.2f, 0);

            // Benches along the back wall with vice and tool stations.
            Transform benches = new GameObject("Workbenches").transform;
            benches.SetParent(eq, false);
            for (int b = 0; b < 3; b++)
            {
                float x = -4.5f + b * 2.1f;
                Box(benches, $"Bench_{b}_Top", new Vector3(x, 0.9f, -Depth / 2 + 0.4f), new Vector3(2f, 0.05f, 0.75f), wood);
                Box(benches, $"Bench_{b}_Cabinet", new Vector3(x, 0.44f, -Depth / 2 + 0.4f), new Vector3(1.96f, 0.86f, 0.7f), grey);
                UseModel(benches, "Workshop/Workbench", new Vector3(x, 0, -Depth / 2 + 0.4f), $"Bench_{b}_Top", $"Bench_{b}_Cabinet");
            }

            // Shelving on the left wall.
            Transform shelves = new GameObject("Shelving").transform;
            shelves.SetParent(eq, false);
            for (int s = 0; s < 2; s++)
            {
                float z = -1.5f + s * 2.2f;
                for (int level = 0; level < 5; level++)
                {
                    Box(shelves, $"Shelf_{s}_{level}", new Vector3(-Width / 2 + 0.35f, 0.15f + level * 0.5f, z), new Vector3(0.6f, 0.03f, 2f), grey);
                }

                Box(shelves, $"Shelf_{s}_PostA", new Vector3(-Width / 2 + 0.35f, 1.15f, z - 1f), new Vector3(0.6f, 2.3f, 0.04f), steel);
                Box(shelves, $"Shelf_{s}_PostB", new Vector3(-Width / 2 + 0.35f, 1.15f, z + 1f), new Vector3(0.6f, 2.3f, 0.04f), steel);
            }

            // Roller dyno sunk in the floor of bay 2 (x = +3 is the pit, dyno at x = +0.5 near the door).
            Transform dyno = new GameObject("RollerDyno").transform;
            dyno.SetParent(eq, false);
            dyno.localPosition = new Vector3(0.6f, 0, 1.5f);
            Box(dyno, "Dyno_Cover", new Vector3(0, -0.02f, 0), new Vector3(2.4f, 0.04f, 1.2f), steel);
            foreach (float z in new[] { -0.25f, 0.25f })
            {
                GameObject roller = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                roller.name = "Roller";
                roller.transform.SetParent(dyno, false);
                roller.transform.localPosition = new Vector3(0, -0.05f, z);
                roller.transform.localRotation = Quaternion.Euler(0, 0, 90);
                roller.transform.localScale = new Vector3(0.25f, 1.1f, 0.25f);
                roller.GetComponent<Renderer>().sharedMaterial = steel;
            }

            Box(dyno, "Dyno_Monitor_Stand", new Vector3(1.8f, 0.8f, -0.8f), new Vector3(0.1f, 1.6f, 0.1f), grey);
            GameObject monitor = Box(dyno, "Dyno_Monitor", new Vector3(1.8f, 1.7f, -0.8f), new Vector3(0.9f, 0.55f, 0.05f), grey, false);
            GameObject screen = Box(monitor.transform, "Screen", new Vector3(0, 0, 0.6f), new Vector3(0.92f, 0.9f, 0.1f), rubber, false);
            screen.AddComponent<DynoView>().screenRenderer = screen.GetComponent<Renderer>();

            // Office in the back-right corner with glass partition and desk.
            Transform office = new GameObject("Office").transform;
            office.SetParent(eq, false);
            office.localPosition = new Vector3(Width / 2 - 1.75f, 0, -Depth / 2 + 1.5f);
            Box(office, "Glass_Partition", new Vector3(-1.75f, 1.25f, 0), new Vector3(0.04f, 2.5f, 3f), glass);
            Box(office, "Desk", new Vector3(0, 0.75f, 0), new Vector3(1.4f, 0.04f, 0.7f), wood);
            Box(office, "Desk_Leg", new Vector3(0, 0.37f, 0), new Vector3(1.3f, 0.74f, 0.05f), grey);
            GameObject pc = Box(office, "Office_Computer", new Vector3(-0.3f, 0.98f, -0.15f), new Vector3(0.5f, 0.32f, 0.04f), grey, false);
            Usable board = pc.AddComponent<Usable>();
            board.kind = Usable.Kind.Board;
            board.label = "ordenador de recepción: tablón de encargos";
            GameObject tablet = Box(office, "Parts_Catalogue", new Vector3(0.35f, 0.78f, 0.05f), new Vector3(0.25f, 0.015f, 0.18f), grey, false);
            Usable shop = tablet.AddComponent<Usable>();
            shop.kind = Usable.Kind.Shop;
            shop.label = "catálogo de recambios (tienda)";
            GameObject cork = Box(office, "Tools_Catalogue", new Vector3(0.9f, 1.5f, -0.3f), new Vector3(0.04f, 0.6f, 0.9f), wood, false);
            Usable upg = cork.AddComponent<Usable>();
            upg.kind = Usable.Kind.Upgrades;
            upg.label = "catálogo de herramientas y mejoras";

            // Hand tools on the first bench (pick up with E) and a tool cart near the lift.
            Transform tools = new GameObject("ToolStations").transform;
            tools.SetParent(eq, false);
            GameObject scanner = Device<ScannerToolView>(tools, "Scanner", new Vector3(-4.8f, 0.95f, -Depth / 2 + 0.35f), new Vector3(0.12f, 0.03f, 0.2f), grey);
            MakeTool(scanner, CarWork.Scanner, "escáner OBD");
            GameObject meter = Device<MultimeterToolView>(tools, "Multimeter", new Vector3(-4.4f, 0.95f, -Depth / 2 + 0.35f), new Vector3(0.09f, 0.04f, 0.18f), Mat("Meter_Yellow", new Color(0.8f, 0.6f, 0.05f), 0f, 0.4f));
            MakeTool(meter, CarWork.Meter, "multímetro");
            GameObject scope = Device<OscilloscopeView>(tools, "Oscilloscope", new Vector3(-2.4f, 1.05f, -Depth / 2 + 0.35f), new Vector3(0.35f, 0.2f, 0.25f), grey);
            MakeTool(scope, CarWork.Scope, "osciloscopio");
            GameObject laptop = Device<EcuMapEditorView>(tools, "Laptop_ECU", new Vector3(-0.3f, 0.95f, -Depth / 2 + 0.4f), new Vector3(0.36f, 0.02f, 0.25f), grey);
            Usable lu = laptop.AddComponent<Usable>();
            lu.kind = Usable.Kind.Laptop;
            lu.label = "portátil: editor de mapas ECU y esquemas";
            var surface = new GameObject("MapSurface3D");
            surface.transform.SetParent(tools, false);
            surface.transform.localPosition = new Vector3(0.3f, 1.05f, -Depth / 2 + 0.35f);
            surface.transform.localScale = Vector3.one * 0.4f;
            laptop.GetComponentInChildren<EcuMapEditorView>().surface = surface.AddComponent<MeshFilter>();
            surface.AddComponent<MeshRenderer>().sharedMaterial = Mat("MapSurface", Color.white, 0f, 0.5f);

            Transform cart = new GameObject("ToolCart").transform;
            cart.SetParent(eq, false);
            cart.localPosition = new Vector3(-0.4f, 0, -1.2f);
            Material cartRed = Mat("ToolCart_Red", new Color(0.5f, 0.06f, 0.05f), 0.3f, 0.5f);
            Box(cart, "Cart_Body", new Vector3(0, 0.45f, 0), new Vector3(0.7f, 0.8f, 0.45f), cartRed);
            Box(cart, "Cart_Top", new Vector3(0, 0.87f, 0), new Vector3(0.72f, 0.04f, 0.47f), steel);
            UseModel(cart, "Workshop/ToolCart", Vector3.zero, "Cart_Body", "Cart_Top");
            MakeTool(Box(cart, "FuelGauge", new Vector3(-0.22f, 0.93f, 0), new Vector3(0.1f, 0.08f, 0.1f), grey, false), CarWork.FuelGauge, "manómetro de combustible");
            MakeTool(Box(cart, "CompressionTester", new Vector3(-0.05f, 0.93f, 0.1f), new Vector3(0.08f, 0.08f, 0.16f), Mat("Gauge_Blue", new Color(0.1f, 0.2f, 0.45f), 0.2f, 0.5f), false), CarWork.Compression, "compresímetro");
            MakeTool(Box(cart, "LeakDownTester", new Vector3(0.1f, 0.93f, -0.1f), new Vector3(0.12f, 0.08f, 0.08f), grey, false), CarWork.LeakDown, "comprobador de fugas");
            MakeTool(Box(cart, "SmokeMachine", new Vector3(0.25f, 0.99f, 0.05f), new Vector3(0.18f, 0.2f, 0.28f), Mat("Smoke_Orange", new Color(0.8f, 0.35f, 0.05f), 0.1f, 0.4f), false), CarWork.Smoke, "máquina de humo");

            GameObject bin = Box(eq, "OldPartsBox", new Vector3(-3.3f, 0.2f, -Depth / 2 + 1.1f), new Vector3(0.6f, 0.4f, 0.4f), Mat("Box_Blue", new Color(0.1f, 0.18f, 0.35f), 0f, 0.4f), false);
            Usable binU = bin.AddComponent<Usable>();
            binU.kind = Usable.Kind.OldPartsBox;
            binU.label = "caja de piezas viejas y almacén";

            Usable dynoUse = monitor.AddComponent<Usable>();
            dynoUse.kind = Usable.Kind.DynoConsole;
            dynoUse.label = "consola del banco de potencia";
            var dynoAnchor = new GameObject("DynoCarAnchor").transform;
            dynoAnchor.SetParent(dyno, false);
            dynoAnchor.localPosition = new Vector3(0, 0.02f, -1.3f);
        }

        /// <summary>
        /// If a generated model exists (Assets/Garage/Resources/&lt;path&gt;.fbx), places it under the parent at the
        /// given local position and removes the placeholder children with the given names (prefix match).
        /// </summary>
        private static void UseModel(Transform parent, string resourcePath, Vector3 localPos, params string[] placeholders)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Garage/Resources/{resourcePath}.fbx");
            if (asset == null)
            {
                return;
            }

            var remove = new List<GameObject>();
            foreach (Transform c in parent)
            {
                foreach (string p in placeholders)
                {
                    if (c.name.StartsWith(p))
                    {
                        remove.Add(c.gameObject);
                    }
                }
            }

            foreach (GameObject g in remove)
            {
                Object.DestroyImmediate(g);
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            // LOD0 contributes to GI and gets an exact static collider; lower LODs are only drawn.
            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.name.EndsWith("_LOD0"))
                {
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
                    if (r.GetComponent<Collider>() == null)
                    {
                        r.gameObject.AddComponent<MeshCollider>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                    }
                }
            }
        }

        private static void MakeTool(GameObject go, string toolId, string name)
        {
            ToolItem t = go.AddComponent<ToolItem>();
            t.toolId = toolId;
            t.displayName = name;
        }

        private static GameObject Device<T>(Transform parent, string name, Vector3 pos, Vector3 size, Material body)
            where T : DeviceScreen
        {
            GameObject device = Box(parent, name, pos, size, body, false);
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(device.transform, false);
            screen.transform.localPosition = new Vector3(0, 0.51f, 0);
            screen.transform.localRotation = Quaternion.Euler(90, 0, 0);
            screen.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            T view = screen.AddComponent<T>();
            view.screenRenderer = screen.GetComponent<Renderer>();
            return device;
        }

        /// <summary>Places the CC0 models downloaded by Garage/Assets/Download Free Assets on the shelving.</summary>
        private static void DressWithDownloadedModels(Transform world)
        {
            string dir = Path.Combine(EditorUtil.ProjectRoot, AssetDownloader.ModelsDir);
            if (!Directory.Exists(dir))
            {
                return;
            }

            Transform dressing = new GameObject("Dressing_CC0").transform;
            dressing.SetParent(world, false);
            int i = 0;
            foreach (string file in Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories))
            {
                string assetPath = "Assets" + file.Substring(Path.Combine(EditorUtil.ProjectRoot, "Assets").Length).Replace('\\', '/');
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null)
                {
                    continue;
                }

                // Shelving on the left wall: two units × 5 levels (see BuildEquipment).
                int unit = i % 2, level = 1 + (i / 2) % 4;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, dressing);
                go.transform.localPosition = new Vector3(-Width / 2 + 0.35f, 0.17f + level * 0.5f, -1.5f + unit * 2.2f + ((i / 8) - 0.5f) * 0.8f);
                go.transform.localRotation = Quaternion.Euler(0, 90, 0);
                foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.GetComponent<Collider>() == null)
                    {
                        r.gameObject.AddComponent<MeshCollider>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                    }
                }

                i++;
            }
        }

        private static void BuildDecals(Transform world)
        {
            Transform decals = new GameObject("Decals").transform;
            decals.SetParent(world, false);
            Material oil = DecalFactory.OilStain();
            Material tyre = DecalFactory.TyreMarks();
            Material sticker = DecalFactory.WorkshopSign();
            var rng = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = new Vector3(-2.5f + (float)(rng.NextDouble() - 0.5) * 3f, 0.05f, (float)(rng.NextDouble() - 0.5) * 4f);
                AddDecal(decals, "OilStain", oil, p, Quaternion.Euler(90, (float)rng.NextDouble() * 360f, 0), new Vector3(0.3f + (float)rng.NextDouble() * 0.6f, 0.3f + (float)rng.NextDouble() * 0.6f, 0.2f));
            }

            AddDecal(decals, "TyreMarks", tyre, new Vector3(2.5f, 0.05f, 2.5f), Quaternion.Euler(90, 0, 0), new Vector3(1.8f, 4f, 0.2f));
            AddDecal(decals, "WorkshopSign", sticker, new Vector3(0, 3.6f, -Depth / 2 + 0.02f), Quaternion.identity, new Vector3(2.5f, 0.8f, 0.2f));
        }

        private static void AddDecal(Transform parent, string name, Material m, Vector3 pos, Quaternion rot, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            DecalProjector d = go.AddComponent<DecalProjector>();
            d.material = m;
            d.size = size;
        }

        private static Transform BuildSimulation(Transform world, Transform lighting)
        {
            var sim = new GameObject("__Simulation");
            SimulationRunner runner = sim.AddComponent<SimulationRunner>();
            WorkshopController wc = sim.AddComponent<WorkshopController>();
            wc.runner = runner;
            wc.sun = lighting.Find("Sun")?.GetComponent<Light>();
            Transform anchor = world.Find("Equipment/TwoPostLift/CarAnchor");
            CarAssembler assembler = anchor.gameObject.AddComponent<CarAssembler>();
            assembler.wear = anchor.gameObject.AddComponent<WearController>();
            var engineAudio = new GameObject("EngineAudio");
            engineAudio.transform.SetParent(anchor, false);
            engineAudio.transform.localPosition = new Vector3(0, 0.9f, 1.4f);
            engineAudio.AddComponent<AudioSource>();
            engineAudio.AddComponent<EngineAudio>().runner = runner;
            CueAudioAndSmoke cues = sim.AddComponent<CueAudioAndSmoke>();
            cues.runner = runner;
            cues.assembler = assembler;
            var smokeGo = new GameObject("ExhaustSmoke");
            smokeGo.transform.SetParent(anchor, false);
            smokeGo.transform.localPosition = new Vector3(0.5f, 0.3f, -2.3f);
            smokeGo.transform.localRotation = Quaternion.Euler(0, 180, 0);
            ParticleSystem ps = smokeGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 3f;
            main.startSpeed = 1.2f;
            main.startSize = 0.4f;
            var em = ps.emission;
            em.rateOverTime = 0;
            cues.exhaustSmoke = ps;
            sim.AddComponent<AudioBuses>();
            GameAudio gameAudio = sim.AddComponent<GameAudio>();
            gameAudio.runner = runner;
            gameAudio.assembler = assembler;
            AudioReverbZone reverb = sim.AddComponent<AudioReverbZone>();
            reverb.reverbPreset = AudioReverbPreset.Hangar;
            reverb.minDistance = 6f;
            reverb.maxDistance = 20f;

            GameBootstrap boot = sim.AddComponent<GameBootstrap>();
            boot.runner = runner;
            boot.assembler = assembler;
            boot.workshop = wc;
            DynoBay bay = sim.AddComponent<DynoBay>();
            bay.runner = runner;
            bay.assembler = assembler;
            bay.liftAnchor = anchor;
            bay.dynoAnchor = world.Find("Equipment/RollerDyno/DynoCarAnchor");

            var uiGo = new GameObject("__UI");
            GameUI gameUi = uiGo.AddComponent<GameUI>();
            gameUi.runner = runner;
            return sim.transform;
        }

        private static void BuildPlayer(Transform sim)
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0.5f, 0.05f, 2.8f);
            player.transform.rotation = Quaternion.Euler(0, 200, 0);
            player.AddComponent<CharacterController>();
            var head = new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = new Vector3(0, 1.65f, 0);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 70f;
            camGo.AddComponent<HDAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CinemachineBrain>();
            camGo.transform.SetParent(head, false);

            var vcamGo = new GameObject("CM_FirstPerson");
            vcamGo.transform.SetParent(head, false);
            CinemachineCamera vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = 70f;
            vcam.Lens.NearClipPlane = 0.03f;

            var lampGo = new GameObject("InspectionLamp");
            lampGo.transform.SetParent(head, false);
            lampGo.transform.localPosition = new Vector3(0.18f, -0.15f, 0.2f);
            Light lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.spotAngle = 60f;
            lamp.range = 6f;
            lamp.shadows = LightShadows.Soft;
            UnityCompat.EnsureHd(lamp);
            UnityCompat.SetTemperature(lamp, 6000f);
            UnityCompat.SetLightIntensity(lamp, 450f, "Lumen"); // typical LED work light
            lamp.enabled = false;

            FirstPersonController fpc = player.AddComponent<FirstPersonController>();
            fpc.head = head;
            fpc.inspectionLamp = lamp;
            InteractionSystem interaction = player.AddComponent<InteractionSystem>();
            interaction.viewCamera = cam;
            interaction.runner = sim.GetComponent<SimulationRunner>();
            interaction.ui = Object.FindFirstObjectByType<GameUI>();

            Volume volume = Object.FindFirstObjectByType<Volume>();
            FocusDepthOfField dof = player.AddComponent<FocusDepthOfField>();
            dof.viewCamera = cam;
            dof.volume = volume;
        }
    }

    /// <summary>Procedural decal textures (saved as PNG so they work offline and can be replaced by art).</summary>
    public static class DecalFactory
    {
        private static Material Make(string name, Texture2D tex, Color tint)
        {
            EditorUtil.EnsureFolder(EditorUtil.GeneratedDir + "/Textures");
            string texPath = $"{EditorUtil.GeneratedDir}/Textures/{name}.png";
            File.WriteAllBytes(Path.Combine(EditorUtil.ProjectRoot, texPath), tex.EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            Shader s = Shader.Find("HDRP/Decal");
            var m = new Material(s != null ? s : Shader.Find("Unlit/Transparent")) { name = name, enableInstancing = true };
            m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetColor("_BaseColor", tint);
            return EditorUtil.SaveMaterial(m, "Decal_" + name);
        }

        private static float Noise(System.Random r) => (float)r.NextDouble();

        /// <summary>Dark radial oil stain with irregular edges.</summary>
        public static Material OilStain()
        {
            var t = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var r = new System.Random(3);
            var blobs = new Vector3[8];
            for (int i = 0; i < blobs.Length; i++)
            {
                blobs[i] = new Vector3(0.3f + Noise(r) * 0.4f, 0.3f + Noise(r) * 0.4f, 0.1f + Noise(r) * 0.25f);
            }

            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float a = 0;
                    foreach (Vector3 b in blobs)
                    {
                        float d = Vector2.Distance(new Vector2(x / 255f, y / 255f), new Vector2(b.x, b.y));
                        a = Mathf.Max(a, Mathf.Clamp01(1 - d / b.z));
                    }

                    a = Mathf.SmoothStep(0, 1, a) * (0.75f + 0.25f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f));
                    t.SetPixel(x, y, new Color(0.03f, 0.025f, 0.02f, a * 0.85f));
                }
            }

            return Make("OilStain", t, Color.white);
        }

        /// <summary>Tyre tread marks.</summary>
        public static Material TyreMarks()
        {
            var t = new Texture2D(128, 512, TextureFormat.RGBA32, false);
            for (int y = 0; y < 512; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    bool track = (x > 10 && x < 40) || (x > 88 && x < 118);
                    float tread = (y / 6) % 2 == 0 ? 1f : 0.6f;
                    float fade = Mathf.PerlinNoise(x * 0.08f, y * 0.02f);
                    t.SetPixel(x, y, new Color(0.02f, 0.02f, 0.02f, track ? 0.35f * tread * fade : 0));
                }
            }

            return Make("TyreMarks", t, Color.white);
        }

        /// <summary>Workshop name sign (fictional brand).</summary>
        public static Material WorkshopSign()
        {
            var t = new Texture2D(512, 160, TextureFormat.RGBA32, false);
            for (int y = 0; y < 160; y++)
            {
                for (int x = 0; x < 512; x++)
                {
                    bool border = x < 8 || x > 503 || y < 8 || y > 151;
                    bool stripe = y > 60 && y < 100 && ((x / 24) % 2 == 0);
                    t.SetPixel(x, y, border ? new Color(0.9f, 0.85f, 0.1f, 1) : stripe ? new Color(0.1f, 0.1f, 0.1f, 1) : new Color(0.75f, 0.1f, 0.08f, 1));
                }
            }

            return Make("WorkshopSign", t, Color.white);
        }
    }
}
