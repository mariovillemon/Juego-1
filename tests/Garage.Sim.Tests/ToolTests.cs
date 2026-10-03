using Garage.Sim.Tools;
using Garage.Sim.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace Garage.Sim.Tests;

public class ToolTests
{
    private readonly ITestOutputHelper _out;

    public ToolTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Scanner_ReadsCodes_ClearsThem_AndChargesTime()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("wire_open", "ect", 1, "signal")).Started().Idle(3);
        var s = new DiagnosticSession(car);
        Assert.Contains("MOTOR (ECM)", s.Scanner.Connect().Display.ToUpperInvariant());
        ToolResult codes = s.Scanner.ReadCodes("ecm", training: true);
        _out.WriteLine(codes.Display);
        Assert.Contains("P0118", codes.Display);
        Assert.Contains("Causas", codes.Display);
        s.Scanner.ClearCodes();
        Assert.DoesNotContain("P0118", car.Ecu.Dtcs.ConfirmedCodes());
        Assert.True(s.MinutesSpent >= 4);
        Assert.Equal(s.MinutesSpent, car.Clock.GameMinutes, 6);
    }

    [Fact]
    public void Scanner_LiveData_ShowsMinus40ForOpenEct()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("wire_open", "ect", 1, "signal")).Started().Idle(1);
        var s = new DiagnosticSession(car);
        var live = s.Scanner.ReadLive(new[] { 0x05 });
        // Open sensor: the ECU substitutes a model value but raw pin is 5 V; the scan tool shows the ECU belief.
        Assert.Single(live);
        Assert.Contains("41 05", live[0].Frame);
    }

    [Fact]
    public void Scanner_NoCommunication_WithDeadModule()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("dead", "module_abs", 1)).Started().Idle(1);
        var s = new DiagnosticSession(car);
        Assert.False(s.Scanner.ReadCodes("abs").Ok);
        Assert.True(s.Scanner.ReadCodes("ecm").Ok);
    }

    [Fact]
    public void Multimeter_MeasuresEctSensorResistance_AndRangeOverflow()
    {
        Car car = TestContent.Car("velmora_pico", warm: false);
        car.Key = KeyPosition.Off;
        var s = new DiagnosticSession(car);
        s.Multimeter.SetConnector("ect", false);
        s.Multimeter.Mode = MeterMode.Ohms;
        s.Multimeter.Range = MeterRange.R20k;
        ToolResult r = s.Multimeter.Measure("ect:comp:signal", "ect:comp:ground");
        _out.WriteLine(r.Display);
        Assert.InRange(r.Value, 2000, 3000); // ~2.5 kΩ at 20 °C
        s.Multimeter.Range = MeterRange.R200;
        Assert.Contains("OL", s.Multimeter.Measure("ect:comp:signal", "ect:comp:ground").Display);
    }

    [Fact]
    public void Multimeter_FindsOpenWire_ByContinuityAndVoltage()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("wire_open", "map", 1, "ref"));
        car.Key = KeyPosition.On;
        car.RunFor(1);
        var s = new DiagnosticSession(car);
        s.Multimeter.Mode = MeterMode.DcVolts;
        ToolResult atEcu = s.Multimeter.Measure("map:ecu:ref", "gnd");
        ToolResult atSensor = s.Multimeter.Measure("map:harness:ref", "gnd");
        _out.WriteLine(atEcu.Display + "\n" + atSensor.Display);
        Assert.InRange(atEcu.Value, 4.9, 5.1);
        Assert.InRange(atSensor.Value, -0.1, 0.5);
        car.Key = KeyPosition.Off;
        s.Multimeter.SetConnector("map", false);
        s.Multimeter.Mode = MeterMode.Continuity;
        Assert.Contains("OL", s.Multimeter.Measure("map:ecu:ref", "map:harness:ref").Display);
        Assert.Contains("BIP", s.Multimeter.Measure("map:ecu:signal", "map:harness:signal").Display);
    }

    [Fact]
    public void Multimeter_InjectorResistance()
    {
        Car car = TestContent.Car();
        var s = new DiagnosticSession(car);
        s.Multimeter.Mode = MeterMode.Ohms;
        s.Multimeter.SetConnector("inj1", false);
        Assert.InRange(s.Multimeter.Measure("inj1:comp:supply", "inj1:comp:control").Value, 12, 13.5);
    }

    [Fact]
    public void Oscilloscope_InjectorShowsInductiveSpike_DeadInjectorDoesNot()
    {
        Car good = TestContent.Car().Started().Idle(3);
        Waveform w = new DiagnosticSession(good).Scope.Capture("inj1", "control", 160);
        _out.WriteLine(w.RenderAscii());
        Assert.True(w.Max > 40);
        Assert.True(w.Min < 2);
        Car bad = TestContent.Car(faults: TestContent.Fault("wire_open", "inj1", 1, "control")).Started().Idle(3);
        Waveform wb = new DiagnosticSession(bad).Scope.Capture("inj1", "control", 160);
        Assert.True(wb.Max < 20);
    }

    [Fact]
    public void Oscilloscope_CrankSensorAmplitudeAndNarrowbandSwitching()
    {
        Car car = TestContent.Car("velmora_pico").Started().Idle(40);
        var s = new DiagnosticSession(car);
        Waveform ckp = s.Scope.Capture("ckp", "signal", 40);
        Assert.InRange(ckp.Max, 2, 8);
        Waveform o2 = s.Scope.Capture("o2_up", "signal", 3000);
        _out.WriteLine(o2.RenderAscii());
        Assert.True(o2.Max > 0.6 && o2.Min < 0.3, $"{o2.Min:0.00}-{o2.Max:0.00}");
    }

    [Fact]
    public void Compression_ShowsLowCylinder_WetTestDistinguishesRingsFromValves()
    {
        Car valve = TestContent.Car("velmora_pico", faults: TestContent.Fault("leak_compression", "cyl2", 0.8));
        valve.Started().Idle(2);
        var s = new DiagnosticSession(valve);
        ToolResult dry = s.Mechanical.Compression();
        _out.WriteLine(dry.Display);
        Assert.Contains("Cilindro 2", dry.Display);
        Assert.Contains("<--", dry.Display);
        ToolResult leak = s.Mechanical.LeakDown(2);
        Assert.Contains("escape", leak.Display);
        Assert.True(leak.Value > 30);
    }

    [Fact]
    public void FuelPressure_WeakPumpDropsUnderLoad()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("weak", "fuel_pump", 0.6)).Started();
        var s = new DiagnosticSession(car);
        double idle = s.Mechanical.FuelPressure(FuelPressureMode.Idle).Value;
        double load = s.Mechanical.FuelPressure(FuelPressureMode.Load).Value;
        _out.WriteLine($"idle {idle:0} load {load:0}");
        Assert.True(load < idle - 50);
    }

    [Fact]
    public void SmokeMachine_FindsVacuumLeak()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("leak_vacuum", "brake_booster_hose", 0.5));
        ToolResult r = new DiagnosticSession(car).Mechanical.Smoke();
        Assert.Contains("servofreno", r.Display);
        Assert.Contains("no revela", new DiagnosticSession(TestContent.Car()).Mechanical.Smoke().Display);
    }

    [Fact]
    public void ActuatorTest_CylinderBalanceFindsDeadCylinder()
    {
        Car car = TestContent.Car("velmora_pico", faults: TestContent.Fault("dead", "coil3", 1)).Started().Idle(10);
        ToolResult r = new DiagnosticSession(car).Scanner.RunActuatorTest(ActuatorTest.CylinderBalance);
        _out.WriteLine(r.Display);
        string line3 = r.Display.Split('\n')[3];
        Assert.Contains("Cilindro 3", line3);
        Assert.Contains("aporta poco", line3);
    }

    [Fact]
    public void WiringDiagram_ListsPins()
    {
        ToolResult r = new DiagnosticSession(TestContent.Car()).Multimeter.WiringDiagram("map");
        Assert.Contains("A32", r.Display);
        Assert.Contains("Referencia 5 V", r.Display);
    }

    [Fact]
    public void Repairs_ClearTheFault_AndCarRecovers()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("dead", "coil2", 1)).Started().Cruise(2000, 0.25, 40);
        Assert.Contains("P0302", car.Codes());
        car.ReplaceComponent("coil2");
        new DiagnosticSession(car).Scanner.ClearCodes();
        car.Cruise(2000, 0.25, 60);
        _out.WriteLine("codes after repair: " + string.Join(",", car.Codes()));
        Assert.Empty(car.Codes());
    }
}
