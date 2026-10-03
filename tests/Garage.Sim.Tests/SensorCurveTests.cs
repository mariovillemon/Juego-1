using System;
using Garage.Sim.Components;
using Xunit;

namespace Garage.Sim.Tests;

public class SensorCurveTests
{
    [Theory]
    [InlineData(-20, 15000, 30000)]
    [InlineData(20, 2000, 3000)]
    [InlineData(90, 150, 350)]
    public void Ntc_ResistanceInTypicalOemRange(double tempC, double min, double max)
    {
        double r = SensorCurves.NtcResistance(tempC);
        Assert.InRange(r, min, max);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(110)]
    public void Ntc_RoundTripThroughEcuDivider(double tempC)
    {
        double v = SensorCurves.NtcVoltage(SensorCurves.NtcResistance(tempC));
        double back = SensorCurves.NtcTemperature(SensorCurves.NtcOhmsFromVoltage(v));
        Assert.Equal(tempC, back, 3);
    }

    [Fact]
    public void Ntc_HotEngineReadsLowVoltage_ColdReadsHigh()
    {
        Assert.InRange(SensorCurves.NtcVoltage(SensorCurves.NtcResistance(90)), 0.2, 0.7);
        Assert.InRange(SensorCurves.NtcVoltage(SensorCurves.NtcResistance(-20)), 4.2, 4.95);
    }

    [Fact]
    public void Linear_0_5To4_5Volts()
    {
        Assert.Equal(0.5, SensorCurves.LinearRatio(10, 10, 250) * 5, 6);
        Assert.Equal(4.5, SensorCurves.LinearRatio(250, 10, 250) * 5, 6);
        Assert.Equal(130, SensorCurves.LinearValue(2.5, 10, 250), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(150)]
    public void Maf_RoundTrip(double gps)
    {
        double v = SensorCurves.MafVoltage(gps, 200);
        Assert.Equal(gps, SensorCurves.MafFlow(v, 200), 6);
    }

    [Fact]
    public void Maf_IdleAboutOneVolt_FullScaleNearFive()
    {
        Assert.InRange(SensorCurves.MafVoltage(3, 200), 1.3, 1.9);
        Assert.InRange(SensorCurves.MafVoltage(200, 200), 4.7, 4.81);
    }

    [Fact]
    public void Narrowband_IsBinaryAroundStoichiometric()
    {
        Assert.True(SensorCurves.NarrowbandVoltage(0.95, 700) > 0.75);
        Assert.True(SensorCurves.NarrowbandVoltage(1.05, 700) < 0.15);
        Assert.Equal(0.49, SensorCurves.NarrowbandVoltage(1.0, 700), 1);
        // Cold sensor sits at the ECU bias
        Assert.Equal(0.45, SensorCurves.NarrowbandVoltage(0.8, 20), 3);
    }

    [Fact]
    public void Zirconia_InternalResistanceDropsWithTemperature()
    {
        Assert.True(SensorCurves.ZirconiaInternalResistance(20) > 1e6);
        Assert.True(SensorCurves.ZirconiaInternalResistance(700) < 200);
    }

    [Theory]
    [InlineData(0.7)]
    [InlineData(0.85)]
    [InlineData(1.0)]
    [InlineData(1.3)]
    [InlineData(2.0)]
    public void Wideband_PumpCurrentIsMonotonic_AndInvertible(double lambda)
    {
        double ip = SensorCurves.WidebandPumpCurrent(lambda);
        Assert.Equal(lambda, SensorCurves.WidebandLambda(ip), 3);
        Assert.True(SensorCurves.WidebandPumpCurrent(lambda + 0.05) > ip);
        Assert.Equal(lambda, SensorCurves.WidebandLambdaFromVoltage(SensorCurves.WidebandVoltage(lambda)), 3);
    }

    [Fact]
    public void Wideband_StoichiometricIsZeroCurrent()
    {
        Assert.Equal(0, SensorCurves.WidebandPumpCurrent(1.0), 6);
        Assert.Equal(1.5, SensorCurves.WidebandVoltage(1.0), 6);
    }

    [Fact]
    public void Ckp_AmplitudeRisesWithRpm()
    {
        Assert.InRange(SensorCurves.CkpPeakVoltage(250), 0.8, 1.5);
        Assert.InRange(SensorCurves.CkpPeakVoltage(6000), 20, 35);
        Assert.True(SensorCurves.CkpPeakVoltage(1000, 2.0) < SensorCurves.CkpPeakVoltage(1000, 1.0));
    }

    [Fact]
    public void Knock_SensorSignalGrowsWithIntensity()
    {
        Assert.True(SensorCurves.KnockSensorMillivolts(3000, 1) > 5 * SensorCurves.KnockSensorMillivolts(3000, 0));
    }
}
