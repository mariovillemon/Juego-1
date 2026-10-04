using System.IO;
using System.Linq;
using Garage.Data;
using Garage.Sim.Ecu;
using Garage.Sim.Game;
using Xunit;
using Xunit.Abstractions;

namespace Garage.Sim.Tests;

public class GameTests
{
    private readonly ITestOutputHelper _out;

    public GameTests(ITestOutputHelper o) => _out = o;

    private static Workshop NewGame(ulong seed = 11) => new(TestContent.Db, seed);

    [Fact]
    public void JobBoard_FirstJobComesFromData_WithHiddenFaults()
    {
        Workshop w = NewGame();
        var offers = w.Offers(3);
        Assert.Equal(3, offers.Count);
        Assert.Equal("job_01", offers[0].Definition.Id);
        Assert.NotEmpty(offers[0].OriginalFaults);
        Assert.Equal("pcv_hose", offers[0].OriginalFaults[0].ComponentId);
    }

    [Fact]
    public void FullRepairCycle_CorrectPart_PaysAndRaisesReputation()
    {
        Workshop w = NewGame();
        Job job = w.Offers(1)[0];
        Assert.True(w.ProposeQuote(job, 200));
        double money = w.Money;
        double rep = w.Reputation;
        _out.WriteLine(w.InstallPart(job, "pcv_hose", "part_vacuumhose_oem"));
        job.Car.Key = Garage.Sim.Vehicle.KeyPosition.On;
        new Garage.Sim.Tools.DiagnosticSession(job.Car).Scanner.ClearCodes();
        JobOutcome o = w.Deliver(job);
        _out.WriteLine(string.Join("\n", o.Notes));
        Assert.True(o.Success);
        Assert.True(w.Money > money);
        Assert.True(w.Reputation > rep);
        Assert.Equal(JobStatus.Delivered, job.Status);
    }

    [Fact]
    public void PartsCannon_IsDetected_AndFailsIfFaultRemains()
    {
        Workshop w = NewGame();
        Job job = w.Offers(1)[0];
        w.ProposeQuote(job, 200);
        w.InstallPart(job, "maf", "part_mafsensor_aftermarket");
        JobOutcome o = w.Deliver(job);
        Assert.False(o.Success);
        Assert.Equal(1, o.UnneededParts);
        Assert.True(o.RemainingFaults > 0);
        Assert.True(o.ReputationDelta < 0);
    }

    [Fact]
    public void ExpensiveQuote_IsRejected()
    {
        Workshop w = NewGame();
        Job job = w.Offers(1)[0];
        Assert.False(w.ProposeQuote(job, 5000));
        Assert.Equal(JobStatus.Cancelled, job.Status);
    }

    [Fact]
    public void Time_RollsDays_AndChargesRent()
    {
        Workshop w = NewGame();
        double money = w.Money;
        w.SpendMinutes(12 * 60);
        Assert.Equal(2, w.Day);
        Assert.True(w.Money < money);
    }

    [Fact]
    public void Upgrades_RequireMoneyAndReputation()
    {
        Workshop w = NewGame();
        Assert.Contains("reputación", w.Buy("tool_scope"));
        Assert.Contains("Comprado", w.Buy("tool_compression"));
        Assert.True(w.Has("tool_compression"));
    }

    [Fact]
    public void PowerJob_StockFailsTarget_TunedPasses()
    {
        Workshop w = NewGame();
        JobDefinition def = TestContent.Db.JobDefinitions.First(j => j.Id == "job_03");
        Job stock = w.BuildJob(def, 5);
        Assert.False(JobEvaluator.Evaluate(stock).Success);

        Job tuned = w.BuildJob(def, 5);
        w.Money = 10000;
        _out.WriteLine(w.InstallPart(tuned, "intercooler", "perf_intercooler"));
        tuned.Car.Definition.Engine.FuelRon = 98; // the customer is told to fill up with 98
        EcuCalibration cal = tuned.Car.Ecu.Calibration;
        cal.Table(EcuCalibration.BoostTarget)!.AddToRegion(0, 99, 4, 99, 30);
        cal.Table(EcuCalibration.BoostTarget)!.AddToRegion(4, 99, 9, 99, 25); // hold boost to redline
        cal.Table(EcuCalibration.WastegateDuty)!.AddToRegion(0, 99, 4, 99, 22);
        cal.Table(EcuCalibration.IgnitionAdvance)!.AddToRegion(7, 99, 0, 99, 2.5);
        cal.Table(EcuCalibration.LambdaTarget)!.AddToRegion(7, 99, 0, 99, -0.02);
        cal.SetScalar(EcuCalibration.Keys.OverboostLimit, 180);
        cal.SetScalar(EcuCalibration.Keys.TorqueLimit, 480);
        JobOutcome o = JobEvaluator.Evaluate(tuned);
        _out.WriteLine(string.Join("\n", o.Notes));
        _out.WriteLine($"limp={tuned.Car.Ecu.LimpMode} {tuned.Car.Ecu.LimpReason} codes={string.Join(",", tuned.Car.Codes())}");
        var log = Garage.Sim.Dyno.DynoRun.Pull(tuned.Car).Log;
        foreach (var row in log.Rows.Where((r, i) => i % 15 == 0))
        {
            _out.WriteLine(string.Join(" ", row.Select(v => v.ToString("0.0"))));
        }

        Assert.True(o.MeasuredPowerPs > 265, $"{o.MeasuredPowerPs}");
    }

    [Fact]
    public void SaveAndLoad_RoundTripsWorkshopAndCarState()
    {
        Workshop w = NewGame(21);
        Job job = w.Offers(2)[0];
        w.ProposeQuote(job, 250);
        w.InstallPart(job, "maf", "part_mafsensor_oem");
        job.Car.Ecu.Calibration.Table(EcuCalibration.IgnitionAdvance)![2, 3] = 33.3;
        job.Car.Engine.Damage.Piston[1] = 0.4;
        string path = Path.Combine(Path.GetTempPath(), $"garage_save_{System.Guid.NewGuid():N}.json");
        try
        {
            SaveGame.Save(w, path);
            var loader = new ContentLoader(Path.Combine(TestContent.DataRoot, "schemas"));
            var errors = new Garage.Data.Json.SchemaValidator(loader.Schema).Validate(Garage.Data.Json.JsonValue.Parse(File.ReadAllText(path)), loader.Schema("savegame.schema.json")!, path);
            Assert.Empty(errors);
            Workshop w2 = SaveGame.Load(TestContent.Db, path);
            Assert.Equal(w.Money, w2.Money, 6);
            Assert.Equal(w.Reputation, w2.Reputation, 6);
            Assert.Equal(w.Day, w2.Day);
            Job j2 = w2.Jobs.First(j => j.Definition.Id == job.Definition.Id);
            Assert.Equal(JobStatus.InProgress, j2.Status);
            Assert.Equal(33.3, j2.Car.Ecu.Calibration.Table(EcuCalibration.IgnitionAdvance)![2, 3], 6);
            Assert.Equal(0.4, j2.Car.Engine.Damage.Piston[1], 6);
            Assert.Equal(job.Car.Faults.All.Count, j2.Car.Faults.All.Count);
            Assert.Equal(job.Lines.Count, j2.Lines.Count);
            Assert.Equal("part_mafsensor_oem", j2.Car.Parts.Get("maf")!.PartId);
            // The rest of the game continues identically after load.
            Assert.Equal(w.Offers(4).Last().Definition.Id, w2.Offers(4).Last().Definition.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveWithUnknownVersion_IsRejected()
    {
        var root = Garage.Data.Json.JsonValue.Parse("{\"formatVersion\": 99, \"workshop\": {\"money\":1,\"reputation\":1,\"day\":1}}");
        Assert.Throws<ContentException>(() => SaveGame.FromJson(TestContent.Db, root));
    }

    [Fact]
    public void GeneratedJobs_AreDeterministicPerSeed()
    {
        Workshop a = NewGame(99);
        Workshop b = NewGame(99);
        var ja = a.Offers(18).Last();
        var jb = b.Offers(18).Last();
        Assert.StartsWith("gen_", ja.Definition.Id);
        Assert.Equal(ja.Definition.CarId, jb.Definition.CarId);
        Assert.Equal(ja.OriginalFaults.Select(f => f.ToString()), jb.OriginalFaults.Select(f => f.ToString()));
    }
}
