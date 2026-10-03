using System;
using System.Collections.Generic;
using Garage.Data;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Tests;

/// <summary>Shared access to the real game content in data/ (loaded once).</summary>
public static class TestContent
{
    private static readonly Lazy<ContentDatabase> Lazy = new(() =>
    {
        string root = ContentDatabase.FindDataRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("data/ not found");
        return ContentDatabase.Load(root);
    });

    public static ContentDatabase Db => Lazy.Value;

    public static string DataRoot => ContentDatabase.FindDataRoot(AppContext.BaseDirectory)!;

    public static FaultInstance Fault(string mode, string component, double severity = 0.7, string? pin = null, FaultCondition? condition = null)
        => new(Db.FailureModes.Get(mode), component, severity, condition, pin);

    public static Car Car(string id = "aurex_strada_gt", ulong seed = 42, bool warm = true, params FaultInstance[] faults)
        => Db.CreateCar(id, seed, faults, warm);
}

/// <summary>Driving helpers.</summary>
public static class Drive
{
    public static Car Started(this Car car)
    {
        if (!car.Start())
        {
            throw new InvalidOperationException("Engine did not start");
        }

        return car;
    }

    public static Car Idle(this Car car, double seconds)
    {
        car.Pedal = 0;
        car.Mode = LoadMode.Neutral;
        car.Gear = 0;
        car.RunFor(seconds);
        return car;
    }

    /// <summary>Holds the engine at an rpm on the dyno in 3rd gear with a fixed pedal (steady cruise).</summary>
    public static Car Cruise(this Car car, double rpm, double pedal, double seconds)
    {
        int gear = Math.Min(3, car.Definition.GearRatios.Length);
        if (car.Gear != gear || car.Mode != LoadMode.DynoHoldRpm)
        {
            car.Gear = gear;
            car.Mode = LoadMode.DynoHoldRpm;
            double ratio = car.Definition.GearRatios[gear - 1] * car.Definition.FinalDrive;
            car.SetVehicleSpeed(rpm / 60.0 * 2 * Math.PI / ratio * car.Definition.WheelRadiusM * 3.6);
        }

        car.DynoHoldRpm = rpm;
        car.Pedal = pedal;
        car.RunFor(seconds);
        return car;
    }

    public static List<string> Codes(this Car car)
    {
        var l = new List<string>(car.Ecu.Dtcs.PendingCodes());
        foreach (string c in car.Ecu.Dtcs.ConfirmedCodes())
        {
            if (!l.Contains(c))
            {
                l.Add(c);
            }
        }

        return l;
    }
}
