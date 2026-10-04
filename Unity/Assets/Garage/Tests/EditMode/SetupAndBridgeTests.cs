using System.IO;
using System.Linq;
using Garage.Data;
using Garage.Game;
using Garage.Sim.Game;
using Garage.Unity.UI;
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

        [Test]
        public void WorkshopScene_HasPlayerUi_HandTools_AndStations()
        {
            WorkshopSceneBuilder.Build(false);
            Assert.IsNotNull(Object.FindFirstObjectByType<GameUI>());
            Assert.IsNotNull(Object.FindFirstObjectByType<DynoBay>());
            var tools = Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None).Select(t => t.toolId).ToList();
            CollectionAssert.IsSubsetOf(new[] { CarWork.Scanner, CarWork.Meter, CarWork.Scope }, tools);
            var usables = Object.FindObjectsByType<Usable>(FindObjectsSortMode.None).Select(u => u.kind).ToList();
            CollectionAssert.IsSubsetOf(new[] { Usable.Kind.Board, Usable.Kind.Laptop, Usable.Kind.DynoConsole, Usable.Kind.OldPartsBox }, usables);
        }

        [Test]
        public void CarAssembler_AddsObdPort()
        {
            ContentDatabase db = ContentDatabase.Load(SimulationRunner.DataRoot);
            var go = new GameObject("Assembler");
            go.AddComponent<CarAssembler>().Build(db.CreateCar("aurex_strada_gt", 1));
            Assert.IsNotNull(go.GetComponentInChildren<ObdPort>());
            Object.DestroyImmediate(go);
        }

        [Test]
        public void GameSession_FullLoopWithStreamingAssetsData()
        {
            ContentDatabase db = ContentDatabase.Load(SimulationRunner.DataRoot);
            var s = new GameSession(db, 5, true);
            Assert.IsTrue(s.StartTutorial("tut_first_car").Ok);
            Job job = s.Offers().First(j => j.Definition.Id == "tut_first_car_job");
            Assert.AreEqual(QuoteAnswer.Accepted, s.SendQuote(job, s.SuggestQuote(job)).Answer);
            s.Notify(GameEventKind.ScannerPlugged);
            s.Work.IgnitionOn();
            Assert.IsTrue(s.Work.ReadCodes().Ok);
            PartDefinition coil = s.PartsFor(job.Car, "coil2").First(p => p.Quality == PartQuality.Aftermarket);
            Assert.IsTrue(s.InstallPart("coil2", coil.Id, buyIfMissing: true).Ok);
            Assert.IsTrue(s.Work.ClearCodes().Ok);
            JobOutcome o = s.Deliver(job, out CommandResult r);
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(o.Success, string.Join(" | ", o.Notes));
        }
    }
}
