using System.IO;
using Garage.Data;
using Garage.Sim.Vehicle;
using Garage.Unity;
using Garage.Unity.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace Garage.Unity.Tests
{
    public class SetupAndBridgeTests
    {
        [Test]
        public void SimulationData_IsSyncedIntoStreamingAssets()
        {
            Assert.IsTrue(Directory.Exists(Path.Combine(SimulationRunner.DataRoot, "base")), "Ejecuta tools/sync-sim-to-unity");
        }

        [Test]
        public void Bridge_LoadsContent_AndCarRunsInsideUnity()
        {
            ContentDatabase db = ContentDatabase.Load(SimulationRunner.DataRoot);
            Assert.IsTrue(db.Report.Ok, string.Join("\n", db.Report.Errors));
            Car car = db.CreateCar("aurex_strada_gt", 1, warm: true);
            Assert.IsTrue(car.Start());
            car.RunFor(5);
            Assert.Greater(car.Engine.State.Rpm, 600);
        }

        [Test]
        public void WorkshopScene_IsGeneratedWithExpectedHierarchy()
        {
            WorkshopSceneBuilder.Build(false);
            Assert.IsNotNull(GameObject.Find("__Workshop/Shell/Floor_Left"));
            Assert.IsNotNull(GameObject.Find("__Workshop/Equipment/TwoPostLift/CarAnchor"));
            Assert.IsNotNull(GameObject.Find("__Workshop/Equipment/RollerDyno"));
            Assert.IsNotNull(GameObject.Find("__Lighting/Sun"));
            Assert.IsNotNull(Object.FindFirstObjectByType<GameBootstrap>());
            Assert.IsNotNull(Object.FindFirstObjectByType<LightProbeGroup>());
            Assert.GreaterOrEqual(Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length, 3);
        }

        [Test]
        public void WorkshopScene_HasPhysicalLightsAndCollisions()
        {
            WorkshopSceneBuilder.Build(false);
            var issues = SceneValidator.Check();
            Assert.IsFalse(issues.Exists(i => i.StartsWith("Luz sin unidades")), string.Join("\n", issues));
            Assert.IsFalse(issues.Exists(i => i.StartsWith("Geometría estática sin colisión")), string.Join("\n", issues));
            Assert.IsFalse(issues.Exists(i => i.StartsWith("Escala sospechosa")), string.Join("\n", issues));
        }

        [Test]
        public void OtherScenes_Build()
        {
            OtherSceneBuilders.BuildMainMenu(false);
            Assert.IsNotNull(Object.FindFirstObjectByType<MainMenuController>());
            OtherSceneBuilders.BuildDyno(false);
            Assert.IsNotNull(Object.FindFirstObjectByType<DynoView>());
        }

        [Test]
        public void CarAssembler_CreatesSlotPerComponent()
        {
            ContentDatabase db = ContentDatabase.Load(SimulationRunner.DataRoot);
            Car car = db.CreateCar("velmora_pico", 1);
            var go = new GameObject("Assembler");
            var asm = go.AddComponent<CarAssembler>();
            asm.Build(car);
            Assert.AreEqual(car.Parts.All.Count, asm.Slots.Count);
            Object.DestroyImmediate(go);
        }
    }
}
