using System.Linq;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace Garage.Sim.Tests;

/// <summary>
/// Symptoms and codes must emerge from the simulation — no fault is scripted to set a code.
/// </summary>
public class EmergentBehaviourTests
{
    private readonly ITestOutputHelper _out;

    public EmergentBehaviourTests(ITestOutputHelper output) => _out = output;

    private void Dump(Car car)
    {
        var l = car.Ecu.Live;
        _out.WriteLine($"rpm={l["rpm"]:0} stft={l["stft"]:0.0} ltft={l["ltft"]:0.0} ltftIdle={l["ltft_idle"]:0.0} ect={l["ect"]:0.0} map={l["map"]:0.0} maf={l["maf"]:0.0} fs={l["fuel_status"]} codes={string.Join(",", car.Codes())}");
    }

    [Fact]
    public void HealthyCar_RunsWithoutCodes()
    {
        Car car = TestContent.Car().Started().Idle(60).Cruise(2500, 0.3, 60);
        Dump(car);
        Assert.Empty(car.Codes());
        Assert.InRange(car.Ecu.Live["ltft"], -6, 6);
    }

    [Fact]
    public void VacuumLeak_GivesHighLtft_AndP0171()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("leak_vacuum", "pcv_hose", 0.8)).Started().Idle(150);
        Dump(car);
        Assert.True(car.Ecu.LtftIdle > 0.15, $"LTFT idle {car.Ecu.LtftIdle}");
        Assert.Contains("P0171", car.Codes());
        Assert.Contains(car.Cues, c => c.Id == "sound.vacuum_hiss");
    }

    [Fact]
    public void ThermostatStuckOpen_KeepsEctLow_AndSetsP0128()
    {
        Car car = TestContent.Car("velmora_pico", warm: false, faults: TestContent.Fault("stuck_open", "thermostat", 1.0)).Started().Cruise(2500, 0.25, 900);
        Dump(car);
        Assert.True(car.Ecu.Live["ect"] < 75, $"ECT {car.Ecu.Live["ect"]}");
        Assert.Contains("P0128", car.Codes());
    }

    [Fact]
    public void HealthyThermostat_WarmsUp_WithoutP0128()
    {
        Car car = TestContent.Car("velmora_pico", warm: false).Started().Cruise(2500, 0.25, 900);
        Dump(car);
        Assert.InRange(car.Ecu.Live["ect"], 85, 100);
        Assert.DoesNotContain("P0128", car.Codes());
    }

    [Theory]
    [InlineData("ect", "signal", "wire_open", "P0118")]
    [InlineData("ect", "signal", "wire_short_ground", "P0117")]
    [InlineData("ect", "ground", "wire_open", "P0118")]
    [InlineData("iat", "signal", "wire_open", "P0113")]
    [InlineData("map", "signal", "wire_open", "P0107")]
    [InlineData("map", "ref", "wire_open", "P0107")]
    [InlineData("map", "ground", "wire_open", "P0108")]
    [InlineData("tps", "signal", "wire_short_ground", "P0122")]
    [InlineData("app", "ground", "wire_open", "P2123")]
    [InlineData("maf", "signal", "wire_open", "P0102")]
    [InlineData("boost", "signal", "wire_short_ground", "P0237")]
    [InlineData("fuel_pressure", "signal", "wire_short_power", "P0193")]
    public void OpenOrShortedSensorWire_SetsTheMatchingCircuitCode(string component, string pin, string mode, string code)
    {
        Car car = TestContent.Car(faults: TestContent.Fault(mode, component, 1, pin)).Started().Idle(5);
        Dump(car);
        Assert.Contains(code, car.Codes());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DeadCoil_SetsMisfireCodeForThatCylinder(int cylinder)
    {
        Car car = TestContent.Car(faults: TestContent.Fault("dead", $"coil{cylinder}", 1)).Started().Cruise(2000, 0.25, 40);
        Dump(car);
        Assert.Contains($"P030{cylinder}", car.Codes());
        Assert.All(Enumerable.Range(1, 4).Where(c => c != cylinder), c => Assert.DoesNotContain($"P030{c}", car.Codes()));
        Assert.Contains(car.Cues, c => c.Id == "vibration.misfire");
    }

    [Fact]
    public void WornSparkPlug_MisfiresInCorrectCylinder()
    {
        Car car = TestContent.Car("velmora_pico", faults: TestContent.Fault("wear", "plug3", 1.0)).Started().Cruise(2500, 0.9, 40);
        Dump(car);
        Assert.Contains("P0303", car.Codes());
    }

    [Fact]
    public void InjectorControlWireOpen_SetsCircuitAndMisfireCodes()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("wire_open", "inj2", 1, "control")).Started().Cruise(2000, 0.25, 40);
        Dump(car);
        Assert.Contains("P0202", car.Codes());
        Assert.Contains("P0302", car.Codes());
    }

    [Fact]
    public void CrankSensorOpen_CranksButDoesNotStart_AndSetsP0335()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("wire_open", "ckp", 1, "signal"));
        bool started = car.Start(3);
        Assert.False(started);
        Assert.Contains("P0335", car.Codes());
    }

    [Fact]
    public void BlownPumpFuse_NoStart_NoRailPressure()
    {
        Car car = TestContent.Car("velmora_pico", faults: TestContent.Fault("dead", "fuse_pump", 1));
        Assert.False(car.Start(3));
        Assert.True(car.Engine.State.FuelRailKpa < 50, $"rail {car.Engine.State.FuelRailKpa}");
    }

    [Fact]
    public void BoostLeak_CausesUnderboost()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("leak_boost", "charge_pipe", 1.0)).Started().Idle(20).Cruise(3500, 1.0, 12);
        Dump(car);
        _out.WriteLine($"boost={car.Engine.State.BoostKpa:0} target={car.Ecu.Live["boost_target_kpa"]:0}");
        Assert.Contains("P0299", car.Codes());
    }

    [Fact]
    public void WastegateStuckClosed_Overboosts_AndEntersLimp()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("stuck_closed", "turbo", 1.0)).Started().Idle(10).Cruise(3500, 1.0, 8);
        Dump(car);
        Assert.Contains("P0234", car.Codes());
        Assert.True(car.Ecu.LimpMode);
    }

    [Fact]
    public void O2HeaterOpen_SetsP0135()
    {
        Car car = TestContent.Car("velmora_pico", faults: TestContent.Fault("heater_open", "o2_up", 1)).Started().Idle(10);
        Assert.Contains("P0135", car.Codes());
    }

    [Fact]
    public void DeadAlternator_DropsVoltage_AndSetsP0562()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("dead", "alternator", 1)).Started().Cruise(2500, 0.3, 30);
        _out.WriteLine($"V={car.BatteryVolts:0.00}");
        Assert.True(car.BatteryVolts < 12.8);
        car.Electrical.StateOfCharge = 0.1;
        car.RunFor(30);
        Assert.Contains("P0562", car.Codes());
    }

    [Fact]
    public void CanStubOpen_ClusterIsolated_OthersReportU0155()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("can_stub_open", "can_bus", 1, "ipc")).Started().Idle(3);
        CanModule ecm = car.Can.Get("ecm")!;
        CanModule abs = car.Can.Get("abs")!;
        Assert.True(ecm.Dtcs.HasCode("U0155"));
        Assert.True(abs.Dtcs.HasCode("U0155"));
        Assert.False(abs.Dtcs.HasCode("U0121"));
    }

    [Fact]
    public void AbsModuleDead_EcmLosesSpeed_AndReportsU0121()
    {
        Car car = TestContent.Car("kessler_rapace", faults: TestContent.Fault("dead", "module_abs", 1)).Started().Cruise(2500, 0.3, 5);
        Assert.True(car.Can.Get("ecm")!.Dtcs.HasCode("U0121"));
        Assert.Equal(0, car.Ecu.Live["speed"]);
    }

    [Fact]
    public void FanDead_EngineOverheatsAtIdle()
    {
        Car car = TestContent.Car("velmora_pico", faults: TestContent.Fault("dead", "fan", 1)).Started();
        car.Environment.AmbientC = 35;
        car.Idle(900);
        Dump(car);
        Assert.True(car.Engine.State.CoolantC > 110, $"ECT {car.Engine.State.CoolantC}");
    }

    [Fact]
    public void StretchedTimingChain_SetsCamCorrelationCode()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("slack", "timing_chain", 0.8)).Started().Idle(20);
        Assert.Contains("P0016", car.Codes());
    }

    [Fact]
    public void WornCatalyst_SetsP0420()
    {
        Car car = TestContent.Car("velmora_pico", faults: TestContent.Fault("wear", "catalyst", 1.0)).Started().Cruise(2200, 0.25, 200);
        Dump(car);
        Assert.Contains("P0420", car.Codes());
    }

    [Fact]
    public void HealthyCatalyst_NoP0420_AndMonitorCompletes()
    {
        Car car = TestContent.Car("velmora_pico").Started().Cruise(2200, 0.25, 200);
        Assert.DoesNotContain("P0420", car.Codes());
        Assert.True(car.Ecu.Readiness.IsComplete(Garage.Sim.Ecu.ReadinessMonitor.Catalyst));
    }
}
