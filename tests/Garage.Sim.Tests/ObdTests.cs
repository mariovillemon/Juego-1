using Garage.Sim.Ecu;
using Garage.Sim.Vehicle;
using Xunit;

namespace Garage.Sim.Tests;

public class ObdTests
{
    private static double RoundTrip(int pid, double v)
    {
        PidDefinition d = Obd2.Find(pid)!;
        return d.Decode(d.Encode(v));
    }

    [Fact]
    public void Rpm_Uses256APlusBOver4()
    {
        PidDefinition d = Obd2.Find(0x0C)!;
        byte[] b = d.Encode(1726);
        Assert.Equal(new byte[] { 0x1A, 0xF8 }, b);
        Assert.Equal(1726, d.Decode(b));
    }

    [Theory]
    [InlineData(0x05, -40)]
    [InlineData(0x05, 92)]
    [InlineData(0x0F, 25)]
    [InlineData(0x0B, 101)]
    [InlineData(0x0D, 120)]
    public void IntegerPids_RoundTripExactly(int pid, double v) => Assert.Equal(v, RoundTrip(pid, v));

    [Fact]
    public void FuelTrim_IsAMinus128Times100Over128()
    {
        PidDefinition d = Obd2.Find(0x06)!;
        Assert.Equal(0, d.Decode(new byte[] { 128 }));
        Assert.Equal(-100, d.Decode(new byte[] { 0 }));
        Assert.Equal(99.22, d.Decode(new byte[] { 255 }), 2);
        Assert.Equal(25, RoundTrip(0x07, 25), 0);
    }

    [Fact]
    public void Maf_IsWordOver100()
    {
        Assert.Equal(new byte[] { 0x0F, 0xA0 }, Obd2.Find(0x10)!.Encode(40.0));
        Assert.Equal(655.35, Obd2.Find(0x10)!.Decode(new byte[] { 0xFF, 0xFF }), 2);
    }

    [Fact]
    public void TimingAdvance_IsAOver2Minus64()
    {
        Assert.Equal(-64, Obd2.Find(0x0E)!.Decode(new byte[] { 0 }));
        Assert.Equal(12.5, RoundTrip(0x0E, 12.5));
    }

    [Fact]
    public void Load_ThrottleAndPedal_AreAOver255()
    {
        Assert.Equal(100, Obd2.Find(0x04)!.Decode(new byte[] { 255 }), 6);
        Assert.Equal(50.2, RoundTrip(0x11, 50.2), 0);
    }

    [Fact]
    public void ModuleVoltage_AndO2_AndLambda()
    {
        Assert.Equal(14.2, RoundTrip(0x42, 14.2), 3);
        Assert.Equal(0.45, RoundTrip(0x14, 0.45), 2);
        Assert.Equal(0.88, RoundTrip(0x24, 0.88), 3);
        Assert.Equal(1.0, RoundTrip(0x44, 1.0), 3);
    }

    [Theory]
    [InlineData("P0171", 0x01, 0x71)]
    [InlineData("P0300", 0x03, 0x00)]
    [InlineData("U0100", 0xC1, 0x00)]
    [InlineData("P2101", 0x21, 0x01)]
    [InlineData("C1234", 0x52, 0x34)]
    [InlineData("B0001", 0x80, 0x01)]
    [InlineData("P242F", 0x24, 0x2F)]
    public void Dtc_EncodesPerJ1979(string code, byte hi, byte lo)
    {
        Assert.Equal(new[] { hi, lo }, Obd2.EncodeDtc(code));
        Assert.Equal(code, Obd2.DecodeDtc(hi, lo));
    }

    [Fact]
    public void LiveCar_PidsMatchEcuBeliefs()
    {
        Car car = TestContent.Car().Started().Idle(15);
        PidReading rpm = Obd2.ReadPid(car, 0x0C)!;
        Assert.InRange(rpm.Value, 650, 950);
        Assert.StartsWith("41 0C", rpm.Frame);
        Assert.Null(Obd2.ReadPid(car, 0x14)); // wideband car: no narrowband PID
        Assert.NotNull(Obd2.ReadPid(car, 0x24));
        byte[] mask = Obd2.SupportedMask(car, 0x00);
        Assert.True((mask[1] & 0x10) != 0); // PID 0x0C supported (bit 12)
    }

    [Fact]
    public void MonitorStatus_ReportsMilAndCount()
    {
        Car car = TestContent.Car(faults: TestContent.Fault("wire_open", "ect", 1, "signal")).Started().Idle(3);
        byte[] b = Obd2.MonitorStatus(car);
        Assert.True((b[0] & 0x80) != 0);
        Assert.True((b[0] & 0x7F) >= 1);
    }
}
