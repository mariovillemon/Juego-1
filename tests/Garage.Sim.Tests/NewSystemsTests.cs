using System.Linq;
using Garage.Sim.Ecu;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace Garage.Sim.Tests;

/// <summary>
/// Phase 4 systems: dual throttle/pedal sensors, EVAP leak test, VVT, GDI rail, VGT, DPF and EGR. As everywhere,
/// faults only change physics or wiring; the codes come out of the ECU's own diagnosis.
/// </summary>
public class NewSystemsTests
{
    private readonly ITestOutputHelper _o;

    public NewSystemsTests(ITestOutputHelper o) => _o = o;

    private Car Run(string car, params FaultInstance[] faults) => TestContent.Car(car, faults: faults).Started();

    private void Log(Car c, string what) => _o.WriteLine($"{what}: codes={string.Join(",", c.Codes())} limp={c.Ecu.LimpMode}/{c.Ecu.LimpReason}");

    [Theory]
    [InlineData("velmora_lumen")]
    [InlineData("aurex_civa")]
    [InlineData("nordak_fjord_crd")]
    [InlineData("kessler_vento")]
    public void NewCars_HealthyRunWithoutCodes(string id)
    {
        Car car = Run(id).Idle(60).Cruise(2500, 0.35, 120).Cruise(3500, 1.0, 10);
        Log(car, id);
        Assert.Empty(car.Codes());
    }

    // ------------------------------------------------------------- dual sensors

    [Fact]
    public void TpsTrackB_Biased_GivesCorrelationCode_AndLimp()
    {
        Car car = Run("velmora_lumen", TestContent.Fault("signal_bias_high", "tps2", 0.4)).Idle(5);
        Log(car, "tps2 bias");
        Assert.Contains("P2135", car.Codes());
        Assert.True(car.Ecu.LimpMode);
        Assert.Contains("P2106", car.Codes());
    }

    [Fact]
    public void PedalTrackE_Open_GivesLowCode_AndReducedPower()
    {
        Car car = Run("aurex_civa", TestContent.Fault("wire_open", "app2", 1, pin: "signal")).Idle(5);
        car.Cruise(3500, 1.0, 5);
        Log(car, "app2 open");
        Assert.Contains("P2127", car.Codes());
        Assert.True(car.Ecu.LimpMode);
        Assert.True(car.Engine.State.ThrottlePosition < 0.2, $"throttle {car.Engine.State.ThrottlePosition}");
    }

    // ---------------------------------------------------------------------- EVAP

    private Car EvapTest(params FaultInstance[] faults)
    {
        Car car = Run("aurex_civa", faults).Idle(260);
        _o.WriteLine($"evap phase={car.Ecu.EvapPhase} tank={car.Evap.TankKpa:0.00} decay={(car.Ecu.Live.TryGetValue("evap_decay_kpa_s", out double d) ? d : double.NaN):0.000}");
        Log(car, "evap");
        return car;
    }

    [Fact]
    public void Evap_SealedSystem_PassesAndCompletesReadiness()
    {
        Car car = EvapTest();
        Assert.Empty(car.Codes());
        Assert.True(car.Ecu.Readiness.IsComplete(ReadinessMonitor.Evap));
    }

    [Fact]
    public void Evap_OneMillimetreLeak_GivesP0442()
    {
        Car c = EvapTest(TestContent.Fault("leak_evap", "evap_canister", 1.0)); Assert.True(c.Codes().Contains("P0442"), string.Join(",", c.Codes()) + " " + (c.Ecu.Live.TryGetValue("evap_decay_kpa_s", out double d) ? d : -99));
    }

    [Fact]
    public void Evap_HalfMillimetreLeak_GivesP0456()
    {
        Assert.Contains("P0456", EvapTest(TestContent.Fault("leak_evap", "evap_canister", 0.45)).Codes());
    }

    [Fact]
    public void Evap_LooseCap_GivesLargeLeak_OrCapCodeAfterRefuel()
    {
        Assert.Contains("P0455", EvapTest(TestContent.Fault("cap_loose", "fuel_cap", 4.5)).Codes());
        Car car = TestContent.Car("aurex_civa", faults: TestContent.Fault("cap_loose", "fuel_cap", 4.5)).Started();
        car.Evap.Refuel();
        car.Idle(260);
        Assert.Contains("P0457", car.Codes());
    }

    [Fact]
    public void Evap_BlockedPurge_GivesP0441()
    {
        Assert.Contains("P0441", EvapTest(TestContent.Fault("stuck_closed", "purge_valve", 1)).Codes());
    }

    [Fact]
    public void Evap_VentStuckClosed_GivesP0446()
    {
        Assert.Contains("P0446", EvapTest(TestContent.Fault("stuck_closed", "evap_vent", 1)).Codes());
    }

    [Fact]
    public void Evap_PurgeStuckOpen_GivesP0496()
    {
        Assert.Contains("P0496", EvapTest(TestContent.Fault("stuck_open", "purge_valve", 1)).Codes());
    }

    // ----------------------------------------------------------------------- VVT

    [Fact]
    public void Vvt_StuckRetarded_GivesP0012_AndLessMidRangeTorque()
    {
        Car good = Run("aurex_civa").Idle(30).Cruise(2500, 1.0, 10);
        double goodTorque = good.Engine.State.TorqueNm;
        Car car = Run("aurex_civa", TestContent.Fault("stuck_closed", "vvt_valve", 1)).Idle(30).Cruise(2500, 1.0, 15);
        Log(car, "vvt stuck retarded");
        Assert.Contains("P0012", car.Codes());
        Assert.True(car.Engine.State.TorqueNm < goodTorque * 0.97, $"{car.Engine.State.TorqueNm} vs {goodTorque}");
    }

    [Fact]
    public void Vvt_StuckAdvanced_GivesP0011()
    {
        Car car = Run("aurex_civa", TestContent.Fault("stuck_open", "vvt_valve", 1)).Idle(30).Cruise(2000, 0.08, 30);
        Log(car, "vvt stuck advanced");
        Assert.Contains("P0011", car.Codes());
    }

    [Fact]
    public void Vvt_SolenoidOpen_GivesP0010()
    {
        Car car = Run("aurex_civa", TestContent.Fault("wire_open", "vvt_valve", 1, pin: "control")).Idle(5);
        Assert.Contains("P0010", car.Codes());
    }

    // ----------------------------------------------------------------------- GDI

    [Fact]
    public void Gdi_WeakHighPressurePump_GivesLowRailUnderLoad_P0087()
    {
        Car car = Run("velmora_lumen", TestContent.Fault("weak", "hp_pump", 0.7)).Idle(20).Cruise(3500, 1.0, 12);
        Log(car, "hp pump weak");
        Assert.Contains("P0087", car.Codes());
    }

    [Fact]
    public void Gdi_MeteringValveStuckOpen_GivesOverPressure_P0088()
    {
        Car car = Run("velmora_lumen", TestContent.Fault("stuck_open", "hp_pump", 1)).Idle(20);
        Log(car, "metering stuck open");
        Assert.Contains("P0088", car.Codes());
        Assert.True(car.Engine.State.FuelRailKpa > 15000);
    }

    // -------------------------------------------------------------- diesel Euro 6

    [Fact]
    public void Vgt_StuckOpen_GivesUnderboost_P0299()
    {
        Car car = Run("nordak_fjord_crd", TestContent.Fault("stuck_open", "vgt_actuator", 1)).Idle(20).Cruise(3000, 1.0, 15);
        Log(car, "vgt stuck open");
        Assert.Contains("P0299", car.Codes());
    }

    [Fact]
    public void Vgt_StuckClosed_GivesOverboost_P0234()
    {
        Car car = Run("nordak_fjord_crd", TestContent.Fault("stuck_closed", "vgt_actuator", 1)).Idle(20).Cruise(3000, 1.0, 15);
        Log(car, "vgt stuck closed");
        Assert.Contains("P0234", car.Codes());
    }

    [Fact]
    public void Dpf_LoadedFilter_RegeneratesWhileDriving()
    {
        Car car = Run("nordak_fjord_crd");
        car.Engine.State.DpfSootG = 30;
        car.Idle(20).Cruise(2500, 0.4, 60);
        Assert.True(car.Ecu.DpfRegenerating || car.Engine.State.DpfSootG < 25, "regeneration should start");
        double egtDuring = car.Engine.State.ExhaustGasC;
        car.Cruise(2500, 0.4, 600);
        _o.WriteLine($"soot {car.Engine.State.DpfSootG:0.0} g, estimate {car.Ecu.DpfSootEstimateG:0.0}, egt during regen {egtDuring:0}");
        Assert.True(egtDuring > 520, $"EGT {egtDuring}");
        Assert.True(car.Engine.State.DpfSootG < 10, $"soot {car.Engine.State.DpfSootG}");
        Assert.Empty(car.Codes());
    }

    [Fact]
    public void Dpf_Overloaded_GivesP2463()
    {
        Car car = Run("nordak_fjord_crd");
        car.Engine.State.DpfSootG = 60;
        car.Idle(20).Cruise(3000, 0.6, 90);
        Log(car, "dpf 60 g");
        Assert.Contains("P2463", car.Codes());
    }

    [Fact]
    public void Dpf_Cracked_GivesLowDeltaP_P2002()
    {
        Car car = Run("nordak_fjord_crd", TestContent.Fault("dpf_cracked", "dpf", 1)).Idle(20).Cruise(3500, 1.0, 40);
        Log(car, "dpf cracked");
        Assert.Contains("P2002", car.Codes());
    }

    [Fact]
    public void DpSensor_SignalOpen_GivesP2454()
    {
        Car car = Run("nordak_fjord_crd", TestContent.Fault("wire_open", "dpf_dp", 1, pin: "signal")).Idle(5);
        Assert.Contains("P2454", car.Codes());
    }

    [Fact]
    public void Egr_Blocked_GivesInsufficientFlow_P0401()
    {
        Car car = Run("nordak_fjord_crd", TestContent.Fault("stuck_closed", "egr_valve", 1)).Idle(40).Cruise(2500, 0.35, 60);
        Log(car, "egr blocked");
        Assert.Contains("P0401", car.Codes());
    }
}
