using System;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Faults;

namespace Garage.Sim.Vehicle
{
    /// <summary>
    /// Evaporative emission system: fuel tank vapour space connected to the charcoal canister, which vents to the
    /// atmosphere through the (normally open) vent valve and is purged into the intake manifold through the purge
    /// valve. Leaks are holes of an equivalent diameter (fault magnitude in mm on the cap or the canister/lines).
    /// The tank pressure (gauge, kPa) is what the ECU's leak test watches. Only active when the car has a canister.
    /// </summary>
    public sealed class EvapSystem
    {
        /// <summary>Purge orifice area at 100 % duty (m²).</summary>
        public const double PurgeAreaM2 = 4.0e-6;

        /// <summary>Vent valve open area (m²).</summary>
        public const double VentAreaM2 = 15e-6;

        private const double VapourVolumeM3 = 0.035;
        private const double R = 287.0;
        private readonly Car _car;

        /// <summary>Creates the system for a car.</summary>
        public EvapSystem(Car car)
        {
            _car = car;
        }

        /// <summary>Car has a full EVAP system (canister present).</summary>
        public bool Present => _car.Parts.Find(ComponentKind.EvapCanister) != null;

        /// <summary>Tank vapour space pressure relative to atmosphere (kPa; negative = vacuum).</summary>
        public double TankKpa { get; private set; }

        /// <summary>Purge valve opening actually reached 0..1.</summary>
        public double PurgeOpening { get; private set; }

        /// <summary>Vent valve actually closed.</summary>
        public bool VentClosed { get; private set; }

        /// <summary>Total leak area to atmosphere (m²) from cap, canister and lines.</summary>
        public double LeakAreaM2 { get; private set; }

        /// <summary>Air + vapour flow drawn into the manifold by the purge (g/s).</summary>
        public double PurgeFlowGps { get; private set; }

        /// <summary>Equivalent unmetered-air area the purge adds to the intake (mm², used by the engine model).</summary>
        public double PurgeAreaMm2 { get; private set; }

        /// <summary>The tank was refuelled during this trip (the ECU blames the cap for a large leak).</summary>
        public bool RefuelledThisTrip { get; set; }

        /// <summary>Fills the tank: pressure equalised, refuel flag for the ECU.</summary>
        public void Refuel()
        {
            TankKpa = 0;
            RefuelledThisTrip = true;
        }

        /// <summary>Advances the tank pressure one step.</summary>
        public void Step(double dt, double purgeCommand, bool ventCommandClosed, double manifoldKpa, double baroKpa, double fuelTempC)
        {
            if (!Present)
            {
                PurgeAreaMm2 = 0;
                PurgeFlowGps = 0;
                TankKpa = 0;
                return;
            }

            Component? purge = _car.Parts.Find(ComponentKind.PurgeValve);
            Component? vent = _car.Parts.Find(ComponentKind.EvapVentValve);
            Component? canister = _car.Parts.Find(ComponentKind.EvapCanister);
            Component? cap = _car.Parts.Find(ComponentKind.FuelCap);
            FaultSet f = _car.Faults;

            double opening = purge != null && _car.ActuatorEnergized(purge.Id) ? MathUtil.Clamp01(purgeCommand) : 0;
            if (purge != null && f.Has(purge.Id, EffectKind.StuckOpen))
            {
                opening = Math.Max(opening, f.Max(purge.Id, EffectKind.StuckOpen));
            }

            if (purge != null && (f.Has(purge.Id, EffectKind.StuckClosed) || f.Has(purge.Id, EffectKind.Clog)))
            {
                opening *= 1 - Math.Max(f.Has(purge.Id, EffectKind.StuckClosed) ? 1 : 0, f.Max(purge.Id, EffectKind.Clog));
            }

            PurgeOpening = opening;
            bool closed = vent != null && ventCommandClosed && _car.ActuatorEnergized(vent.Id);
            if (vent != null && f.Has(vent.Id, EffectKind.StuckClosed))
            {
                closed = true;
            }

            if (vent != null && f.Has(vent.Id, EffectKind.StuckOpen))
            {
                closed = false;
            }

            VentClosed = closed;
            double leakMm = (cap != null ? f.Sum(cap.Id, EffectKind.Leak) : 0) + (canister != null ? f.Sum(canister.Id, EffectKind.Leak) : 0);
            LeakAreaM2 = Math.PI / 4 * Math.Pow(leakMm / 1000.0, 2);

            double tankAbs = baroKpa + TankKpa;
            double t = Math.Max(-30, fuelTempC);
            double massOut = opening > 0 && manifoldKpa < tankAbs ? Physics.OrificeFlow(PurgeAreaM2 * opening * 0.7, tankAbs, t, manifoldKpa) : 0;
            double massIn = 0;
            double ventArea = closed ? 0 : VentAreaM2;
            double toAtmArea = ventArea + LeakAreaM2;
            if (toAtmArea > 0)
            {
                massIn += tankAbs < baroKpa ? Physics.OrificeFlow(toAtmArea * 0.7, baroKpa, 20, tankAbs) : -Physics.OrificeFlow(toAtmArea * 0.7, tankAbs, t, baroKpa);
            }

            // Fuel vapour generation (warmer fuel evaporates more): slowly builds pressure in a sealed tank.
            double vapour = 1.5e-7 * Math.Exp((t - 20) / 15.0);
            double dm = (massIn - massOut + vapour) * dt;
            TankKpa += dm * R * Physics.ToKelvin(t) / VapourVolumeM3 / 1000.0;
            TankKpa = MathUtil.Clamp(TankKpa, -12, 6);

            PurgeFlowGps = massOut * 1000;
            // With the vent open the purge mostly pulls fresh air through the canister: an unmetered air leak.
            PurgeAreaMm2 = PurgeAreaM2 * 1e6 * opening * (closed ? 0.25 : 1.0);
        }
    }
}
