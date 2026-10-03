using System;
using Garage.Sim.Core;
using Garage.Sim.Electrical;
using Garage.Sim.Engine;
using Garage.Sim.Faults;
using Garage.Sim.Maps;
using Xunit;

namespace Garage.Sim.Tests;

public class CoreTests
{
    [Fact]
    public void Random_IsDeterministicPerSeed()
    {
        var a = new DeterministicRandom(123);
        var b = new DeterministicRandom(123);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong());
        }

        Assert.NotEqual(new DeterministicRandom(1).NextULong(), new DeterministicRandom(2).NextULong());
    }

    [Fact]
    public void Random_RangesAndGaussianAreSane()
    {
        var r = new DeterministicRandom(7);
        double sum = 0;
        for (int i = 0; i < 20000; i++)
        {
            double d = r.NextDouble();
            Assert.InRange(d, 0, 1);
            sum += r.Gaussian();
            int k = r.Next(3, 9);
            Assert.InRange(k, 3, 8);
        }

        Assert.InRange(sum / 20000, -0.05, 0.05);
    }

    [Fact]
    public void Map3D_BilinearInterpolationAndSaturation()
    {
        var m = new Map3D("t", "", new Axis("x", "", new double[] { 0, 10 }), new Axis("y", "", new double[] { 0, 1 }), new double[,] { { 0, 10 }, { 20, 30 } });
        Assert.Equal(15, m.Lookup(5, 0.5), 9);
        Assert.Equal(0, m.Lookup(-5, -1), 9);
        Assert.Equal(30, m.Lookup(50, 9), 9);
        m[0, 0] = 4;
        Assert.Equal(4, m.Lookup(0, 0));
        Map3D c = m.Clone();
        c[0, 0] = 99;
        Assert.Equal(4, m[0, 0]);
        Assert.Contains("10", m.Format());
    }

    [Fact]
    public void Axis_RejectsNonIncreasing()
    {
        Assert.Throws<ArgumentException>(() => new Axis("x", "", new double[] { 1, 1 }));
    }

    [Fact]
    public void Map2D_Interpolates()
    {
        var m = new Map2D("c", "", new Axis("x", "", new double[] { 0, 100 }), new double[] { 1, 2 });
        Assert.Equal(1.5, m.Lookup(50), 9);
    }

    [Fact]
    public void OrificeFlow_ChokesBelowCriticalRatio()
    {
        double a = Physics.OrificeFlow(1e-4, 100, 20, 30);
        double b = Physics.OrificeFlow(1e-4, 100, 20, 10);
        Assert.Equal(a, b, 9);
        Assert.True(Physics.OrificeFlow(1e-4, 100, 20, 90) < a);
        Assert.True(Physics.OrificeFlow(1e-4, 100, 20, 120) < 0);
    }

    [Fact]
    public void Network_SolvesVoltageDivider()
    {
        var n = new ResistiveNetwork();
        n.AddSource("A", "GND", 10, 0.001);
        n.AddResistor("A", "B", 1000);
        n.AddResistor("B", "GND", 1000);
        double[] v = n.Solve();
        Assert.Equal(5, v[n.Node("B")], 2);
    }

    [Fact]
    public void Circuit_OhmmeterReadsNtcWithConnectorUnplugged()
    {
        var c = new Circuit("ect", CircuitTopology.Thermistor, new[] { new PinInfo("signal", "A40", 1, "AM"), new PinInfo("ground", "A41", 2, "MA") });
        c.SensorValue = 2500;
        c.KeyOn = false;
        var faults = new FaultSet();
        // Connected: the ECU input is in parallel (a classic trap for apprentices).
        Assert.InRange(c.MeasureResistance(faults, "C:signal", "C:ground"), 1000, 1500);
        c.ConnectorConnected = false;
        Assert.Equal(2500, c.MeasureResistance(faults, "C:signal", "C:ground"), 0);
        Assert.True(double.IsPositiveInfinity(c.MeasureResistance(faults, "E:signal", "C:signal")));
        // Harness wire end to end
        Assert.InRange(c.MeasureResistance(faults, "E:signal", "H:signal"), 0.0, 0.1);
    }

    [Fact]
    public void Circuit_OpenWireIsOL_ShortToGroundIsZeroVolts()
    {
        var pins = new[] { new PinInfo("ref", "A1", 1, "GR"), new PinInfo("signal", "A2", 2, "VE"), new PinInfo("ground", "A3", 3, "MA") };
        var c = new Circuit("map", CircuitTopology.Ratiometric, pins) { SensorValue = 0.5 };
        var faults = new FaultSet();
        c.Solve(faults);
        Assert.Equal(2.5, c.EcuPinVoltage("signal"), 1);
        faults.Add(new FaultInstance(FailureModeLibrary.Default.Get("wire_short_ground"), "map", 1, null, "signal"));
        c.Solve(faults);
        Assert.InRange(c.EcuPinVoltage("signal"), 0, 0.05);
    }

    [Fact]
    public void LambdaFactor_PeaksRichOfStoich()
    {
        Assert.True(EngineModel.LambdaFactor(0.88) > EngineModel.LambdaFactor(1.0));
        Assert.True(EngineModel.LambdaFactor(1.0) > EngineModel.LambdaFactor(1.4));
        Assert.True(EngineModel.LambdaFactor(0.7) < EngineModel.LambdaFactor(0.88));
    }

    [Fact]
    public void StableHash_IsStable()
    {
        Assert.Equal(StableHash.Of("abc"), StableHash.Of("abc"));
        Assert.Equal(0x1A47E90Bu, StableHash.Of("abc"));
    }

    [Fact]
    public void FaultCondition_GatesActivation()
    {
        var f = new FaultInstance(FailureModeLibrary.Default.Get("wire_open"), "x", 1, new FaultCondition(ConditionKind.Hot, 80));
        var rng = new DeterministicRandom(1);
        f.Evaluate(new ConditionContext { CoolantC = 20 }, rng);
        Assert.False(f.Active);
        f.Evaluate(new ConditionContext { CoolantC = 95 }, rng);
        Assert.True(f.Active);
        Assert.Contains("caliente", f.Condition.Describe());
    }

    [Fact]
    public void IntermittentFault_TogglesOverTime()
    {
        var f = new FaultInstance(FailureModeLibrary.Default.Get("signal_dropout"), "ckp", 1, new FaultCondition(ConditionKind.Intermittent, 0.5, 2));
        var rng = new DeterministicRandom(5);
        int on = 0;
        for (int i = 0; i < 5000; i++)
        {
            f.Evaluate(new ConditionContext { Dt = 0.02 }, rng);
            on += f.Active ? 1 : 0;
        }

        Assert.InRange(on, 500, 4500);
    }
}
