using System.Linq;
using Garage.Sim.Dyno;
using Garage.Sim.Ecu;
using Garage.Sim.Maps;
using Garage.Sim.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace Garage.Sim.Tests;

public class DamageAndDeterminismTests
{
    private readonly ITestOutputHelper _out;

    public DamageAndDeterminismTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void ExcessiveAdvance_CausesKnock_DamageAndPistonFailure()
    {
        Car car = TestContent.Car("aurex_strada_gt", seed: 3);
        Map3D adv = car.Ecu.Calibration.Table(EcuCalibration.IgnitionAdvance)!;
        adv.AddToRegion(0, 99, 0, 99, 14); // +14° everywhere: classic bad tune
        car.Ecu.Calibration.SetScalar(EcuCalibration.Keys.KnockMax, 0); // knock control disabled by the "tuner"
        car.Started().Cruise(3500, 1.0, 1);
        double knock = car.Engine.State.CylinderKnock.Max();
        _out.WriteLine($"knock={knock:0.00} adv={car.Engine.State.SparkAdvanceDeg:0.0} kla={car.Engine.State.KnockLimitDeg:0.0}");
        Assert.True(knock > 0.5);
        car.Cruise(3500, 1.0, 5);
        Assert.True(car.Engine.Damage.Piston.Max() > 0.1);
        car.Cruise(3500, 1.0, 40);
        _out.WriteLine($"piston={string.Join(",", car.Engine.Damage.Piston.Select(p => p.ToString("0.00")))}");
        Assert.True(car.Engine.Damage.AnyPistonBroken);
        Assert.Contains(car.Engine.State.CylinderCompression, c => c < 0.5);
    }

    [Fact]
    public void KnockControl_ProtectsTheEngine_WhenSensorWorks()
    {
        Car car = TestContent.Car("aurex_strada_gt", seed: 3);
        car.Ecu.Calibration.Table(EcuCalibration.IgnitionAdvance)!.AddToRegion(0, 99, 0, 99, 5);
        car.Started().Cruise(3500, 1.0, 20);
        _out.WriteLine($"retard={car.Ecu.KnockRetard:0.0} piston={car.Engine.Damage.Piston.Max():0.000}");
        Assert.True(car.Ecu.KnockRetard > 2);
        Assert.False(car.Engine.Damage.AnyPistonBroken);
    }

    [Fact]
    public void OverRevving_DamagesRodBearing()
    {
        Car car = TestContent.Car("velmora_pico", seed: 9);
        car.Ecu.Calibration.SetScalar(EcuCalibration.Keys.RevLimit, 9000);
        car.Started().Cruise(7600, 1.0, 20);
        _out.WriteLine($"rod={car.Engine.Damage.RodBearing:0.00}");
        Assert.True(car.Engine.Damage.RodBearing > 0.5);
    }

    [Fact]
    public void Overheating_DamagesHeadGasket_ThenChainsToCoolantLoss()
    {
        Car car = TestContent.Car("velmora_pico", faults: TestContent.Fault("dead", "fan", 1));
        car.Environment.AmbientC = 40;
        car.Started().Idle(1800);
        _out.WriteLine($"ect={car.Engine.State.CoolantC:0} hg={car.Engine.Damage.HeadGasket:0.00} level={car.Engine.State.CoolantLevel:0.00}");
        Assert.True(car.Engine.Damage.HeadGasket > 0.3);
        Assert.Contains("P0217", car.Codes());
    }

    [Fact]
    public void SameSeedAndInputs_GiveIdenticalResults()
    {
        string Run(ulong seed)
        {
            Car car = TestContent.Car(seed: seed, faults: TestContent.Fault("signal_dropout", "ckp", 1, null, new Faults.FaultCondition(Faults.ConditionKind.Intermittent, 0.1, 3)));
            car.Start();
            car.Cruise(2500, 0.4, 30);
            var s = car.Engine.State;
            return $"{s.Rpm:R}|{s.CoolantC:R}|{s.ExhaustLambda:R}|{car.Ecu.Stft:R}|{string.Join(",", car.Codes())}|{string.Join(",", car.Ecu.MisfireCounts)}";
        }

        Assert.Equal(Run(77), Run(77));
    }

    [Fact]
    public void DifferentSeeds_CanDiverge_WithRandomFaults()
    {
        var gen = new Faults.FaultGenerator(TestContent.Db.FailureModes);
        Car a = TestContent.Car();
        Car b = TestContent.Car();
        var fa = gen.Generate(a, 3, new Core.DeterministicRandom(1));
        var fb = gen.Generate(b, 3, new Core.DeterministicRandom(2));
        var fa2 = gen.Generate(TestContent.Car(), 3, new Core.DeterministicRandom(1));
        Assert.Equal(fa.Select(f => f.ToString()), fa2.Select(f => f.ToString()));
        Assert.NotEqual(fa.Select(f => f.ToString()), fb.Select(f => f.ToString()));
    }

    [Fact]
    public void Dyno_StockAurex_MakesAbout245Ps()
    {
        Car car = TestContent.Car("aurex_strada_gt");
        DynoResult r = DynoRun.Pull(car);
        _out.WriteLine(r.Summary);
        _out.WriteLine(r.RenderAscii());
        Assert.False(r.Aborted, r.EndReason);
        Assert.InRange(r.PeakPowerPs, 215, 270);
        Assert.InRange(r.PeakTorqueNm, 300, 380);
        Assert.Contains("rpm,", r.Log.ToCsv());
        Assert.True(r.Log.Rows.Count > 50);
    }

    [Theory]
    [InlineData("velmora_pico", 80, 105)]
    [InlineData("kessler_rapace", 330, 430)]
    [InlineData("nordak_atlas_td", 120, 175)]
    public void Dyno_StockCars_AreRealistic(string carId, double minPs, double maxPs)
    {
        Car car = TestContent.Car(carId);
        DynoResult r = DynoRun.Pull(car);
        _out.WriteLine(r.Summary);
        Assert.InRange(r.PeakPowerPs, minPs, maxPs);
    }

    [Fact]
    public void MoreBoost_MorePower_ButLeanAndKnockRisk()
    {
        Car stock = TestContent.Car("aurex_strada_gt");
        double basePs = DynoRun.Pull(stock).PeakPowerPs;
        Car tuned = TestContent.Car("aurex_strada_gt");
        tuned.Ecu.Calibration.Table(EcuCalibration.BoostTarget)!.AddToRegion(0, 99, 0, 99, 25);
        tuned.Ecu.Calibration.SetScalar(EcuCalibration.Keys.OverboostLimit, 175);
        tuned.Ecu.Calibration.SetScalar(EcuCalibration.Keys.TorqueLimit, 500);
        DynoResult r = DynoRun.Pull(tuned);
        _out.WriteLine($"{basePs:0} -> {r.Summary}; {string.Join(" ", r.Warnings)}");
        Assert.True(r.PeakPowerPs > basePs + 5);
        // More boost on the stock ignition map knocks: the tune "works" on the dyno but costs engine life.
        Assert.True(r.MaxKnockRetard > 1 || r.DamageDelta > 0.01);
    }
}
