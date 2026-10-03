using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Garage.Unity.EditorTools
{
    /// <summary>Main menu and dedicated dyno room scenes (idempotent builders).</summary>
    public static class OtherSceneBuilders
    {
        public const string MainMenuPath = EditorUtil.ScenesDir + "/MainMenu.unity";
        public const string DynoPath = EditorUtil.ScenesDir + "/Dyno.unity";

        [MenuItem("Garage/Setup/Build Main Menu Scene", priority = 3)]
        public static void BuildMainMenuMenu() => BuildMainMenu(true);

        [MenuItem("Garage/Setup/Build Dyno Scene", priority = 4)]
        public static void BuildDynoMenu() => BuildDyno(true);

        /// <summary>Main menu: the workshop exterior at dusk with a slowly orbiting camera and the menu.</summary>
        public static Scene BuildMainMenu(bool save)
        {
            EditorUtil.EnsureFolder(EditorUtil.ScenesDir);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Transform lighting = new GameObject("__Lighting").transform;
            LightingBuilder.BuildWorkshopLights(lighting, 12f, 8f, 5f);
            Transform sun = lighting.Find("Sun");
            WorkshopController.ApplySun(sun, 19.5f, 40f);
            Material asphalt = WorkshopSceneBuilder.Mat("Asphalt", new Color(0.08f, 0.08f, 0.08f), 0f, 0.25f);
            WorkshopSceneBuilder.Box(new GameObject("__Set").transform, "Ground", new Vector3(0, -0.1f, 0), new Vector3(40, 0.2f, 40), asphalt);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>();
            cam.AddComponent<HDAdditionalCameraData>();
            cam.AddComponent<AudioListener>();
            cam.transform.position = new Vector3(8, 2.2f, 12);
            cam.transform.LookAt(new Vector3(0, 1.5f, 0));
            cam.AddComponent<MainMenuController>();
            if (save)
            {
                EditorSceneManager.SaveScene(scene, MainMenuPath);
                WorkshopSceneBuilder.AddToBuild(MainMenuPath);
            }

            return scene;
        }

        /// <summary>Dyno room: a smaller cell with the rollers, extraction and the monitor wall.</summary>
        public static Scene BuildDyno(bool save)
        {
            EditorUtil.EnsureFolder(EditorUtil.ScenesDir);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Transform room = new GameObject("__DynoRoom").transform;
            Material concrete = WorkshopSceneBuilder.Mat("Concrete_Floor", new Color(0.3f, 0.29f, 0.27f), 0f, 0.35f);
            Material wall = WorkshopSceneBuilder.Mat("Acoustic_Panel", new Color(0.2f, 0.2f, 0.22f), 0f, 0.05f);
            WorkshopSceneBuilder.Box(room, "Floor", new Vector3(0, -0.1f, 0), new Vector3(6, 0.2f, 9), concrete);
            WorkshopSceneBuilder.Box(room, "Wall_L", new Vector3(-3.1f, 2, 0), new Vector3(0.2f, 4, 9), wall);
            WorkshopSceneBuilder.Box(room, "Wall_R", new Vector3(3.1f, 2, 0), new Vector3(0.2f, 4, 9), wall);
            WorkshopSceneBuilder.Box(room, "Wall_B", new Vector3(0, 2, -4.6f), new Vector3(6.4f, 4, 0.2f), wall);
            WorkshopSceneBuilder.Box(room, "Ceiling", new Vector3(0, 4.1f, 0), new Vector3(6.4f, 0.2f, 9.4f), wall);
            WorkshopSceneBuilder.Box(room, "Extraction_Duct", new Vector3(0, 0.4f, -4.2f), new Vector3(0.4f, 0.4f, 0.6f), WorkshopSceneBuilder.Mat("Bare_Steel", new Color(0.55f, 0.55f, 0.55f), 1f, 0.55f));
            Transform lighting = new GameObject("__Lighting").transform;
            LightingBuilder.BuildWorkshopLights(lighting, 6f, 9f, 4f);
            LightingBuilder.AddProbes(lighting, 6f, 9f, 4f);
            var sim = new GameObject("__Simulation");
            SimulationRunner runner = sim.AddComponent<SimulationRunner>();
            var anchor = new GameObject("CarAnchor");
            CarAssembler assembler = anchor.AddComponent<CarAssembler>();
            assembler.wear = anchor.AddComponent<WearController>();
            GameBootstrap boot = sim.AddComponent<GameBootstrap>();
            boot.runner = runner;
            boot.assembler = assembler;
            boot.sandboxCarId = "aurex_strada_gt";
            GameObject monitor = WorkshopSceneBuilder.Box(room, "MonitorWall", new Vector3(2.4f, 1.8f, -3), new Vector3(0.05f, 1.2f, 2f), wall, false);
            monitor.AddComponent<DynoView>().screenRenderer = monitor.GetComponent<Renderer>();
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>();
            cam.AddComponent<HDAdditionalCameraData>();
            cam.AddComponent<AudioListener>();
            cam.transform.position = new Vector3(-2.2f, 1.7f, 3.5f);
            cam.transform.LookAt(new Vector3(0, 0.8f, 0));
            if (save)
            {
                EditorSceneManager.SaveScene(scene, DynoPath);
                WorkshopSceneBuilder.AddToBuild(DynoPath);
            }

            return scene;
        }

        [MenuItem("Garage/Setup/Run All Setup Steps", priority = 0)]
        public static void All()
        {
            HdrpSetup.Configure();
            BuildMainMenu(true);
            BuildDyno(true);
            WorkshopSceneBuilder.Build(true);
        }
    }
}
