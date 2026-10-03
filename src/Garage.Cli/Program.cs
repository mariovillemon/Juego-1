using System;
using System.Globalization;
using Garage.Data;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;

namespace Garage.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        string root = ContentDatabase.FindDataRoot(Environment.CurrentDirectory) ?? "data";
        var db = ContentDatabase.Load(root);
        foreach (var e in db.Report.Errors) Console.WriteLine("ERR " + e);
        string carId = args[0];
        var faults = new System.Collections.Generic.List<FaultInstance>();
        if (args.Length > 2) faults.Add(new FaultInstance(db.FailureModes.Get(args[1]), args[2], args.Length > 3 ? double.Parse(args[3]) : 1, null, args.Length > 4 ? args[4] : null));
        var car = db.CreateCar(carId, 42, faults, warm: true);
        car.Key = KeyPosition.On; car.RunFor(2.1); car.Key = KeyPosition.Crank;
        for (int k = 0; k < 8; k++) { car.RunFor(0.25); Print(car); }
        car.Key = KeyPosition.On;
        for (int k = 0; k < 20; k++) { car.RunFor(5); Print(car); }
        return 0;
    }

    static void Print(Car car)
    {
        var s = car.Engine.State; var l = car.Ecu.Live;
        Console.WriteLine($"t {car.Clock.SimSeconds,5:0.0} rpm {s.Rpm,5:0} map {s.ManifoldKpa,5:0.0} maf {s.MafFlowGps,6:0.0} thr {s.ThrottlePosition*100,4:0.0} lam {s.ExhaustLambda,5:0.000} T {s.TorqueNm,5:0} adv {s.SparkAdvanceDeg,4:0.0} burn {s.CylinderBurn[0]:0.00} ect {s.CoolantC,4:0} o2v {l["o2_b1s1_v"]:0.00} o2T {s.O2SensorC:0} stft {l["stft"],5:0.0} ltft {l["ltft"],5:0.0} fs {l["fuel_status"]} pw {l["injector_pw_ms"]:0.00} limp {car.Ecu.LimpMode}{car.Ecu.LimpReason} codes {string.Join(",", car.Ecu.Dtcs.PendingCodes())}");
    }
}
