using System;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Faults;

namespace Garage.Sim.Engine
{
    /// <summary>
    /// Optional engine subsystems, active only when the car has the matching components: variable valve timing,
    /// gasoline direct injection high pressure rail, variable geometry turbo, diesel particulate filter and the
    /// engine-out NOx estimate. Kept apart from the core mean-value model for readability.
    /// </summary>
    public sealed partial class EngineModel
    {
        private double _hpRail;
        private bool _hpInit;

        // ------------------------------------------------------------------ VVT

        /// <summary>
        /// Intake cam phaser driven by engine oil through the oil control valve. Undriven (or stuck closed) it rests
        /// on the lock pin at full retard (0°). Stuck open = held advanced. Sludge (clog) slows it down.
        /// </summary>
        private void UpdateCamPhaser(double dt, EngineCommands cmd, double rpm)
        {
            EngineState s = State;
            Component? ocv = _parts.Find(ComponentKind.VvtSolenoid);
            if (ocv == null)
            {
                s.CamPhaseDeg = 0;
                return;
            }

            double target = cmd.VvtDriven ? MathUtil.Clamp(cmd.CamTargetDeg, 0, 40) : 0;
            if (_faults.Has(ocv.Id, EffectKind.StuckOpen))
            {
                target = 32 * Math.Max(0.5, _faults.Max(ocv.Id, EffectKind.StuckOpen));
            }
            else if (_faults.Has(ocv.Id, EffectKind.StuckClosed) || _faults.Has(ocv.Id, EffectKind.Dead))
            {
                target = 0;
            }

            double clog = _faults.Max(ocv.Id, EffectKind.Clog);
            double oil = MathUtil.Clamp01(s.OilPressureKpa / 250.0);
            double rate = 80.0 * MathUtil.SmoothStep(600, 1500, rpm) * oil * (1 - 0.92 * clog);
            double step = rate * dt;
            s.CamPhaseDeg += MathUtil.Clamp(target - s.CamPhaseDeg, -step, step);
        }

        /// <summary>Volumetric efficiency change from intake cam advance: more torque low/mid, less at the top end,
        /// and less fresh charge at part load (internal EGR from valve overlap).</summary>
        private double VvtVeFactor(double rpm, double mapKpa)
        {
            double p = State.CamPhaseDeg / 30.0;
            if (p <= 0)
            {
                return 1.0;
            }

            double fullLoad = 0.07 * MathUtil.SmoothStep(1000, 1800, rpm) * (1 - MathUtil.SmoothStep(3500, 5000, rpm)) - 0.07 * MathUtil.SmoothStep(4800, 6500, rpm);
            double partLoadLoss = 0.18 * (1 - MathUtil.SmoothStep(50, 95, mapKpa));
            return Math.Max(0.6, 1 + p * (fullLoad - partLoadLoss));
        }

        /// <summary>Residual gas fraction added by valve overlap at low load (rough idle when stuck advanced).</summary>
        private double InternalEgr(double rpm, double load)
        {
            double p = State.CamPhaseDeg / 30.0;
            return p <= 0 ? 0 : p * 0.2 * (1 - MathUtil.SmoothStep(0.25, 0.6, load)) * (1 - MathUtil.SmoothStep(2500, 4000, rpm));
        }

        // ------------------------------------------------------------------ GDI

        /// <summary>
        /// Cam driven high pressure pump with an inlet metering valve (normally open). The low pressure pump feeds it;
        /// the ECU closes the valve partially to hit the rail target. Undriven or stuck open = full delivery up to
        /// the relief valve; stuck closed or a dead pump = only the low pressure reaches the injectors.
        /// </summary>
        private void UpdateHighPressureRail(double dt, EngineCommands cmd, double rpm)
        {
            EngineState s = State;
            double lp = s.FuelRailKpa;
            s.LowFuelKpa = lp;
            Component? hp = _parts.Find(ComponentKind.HighPressurePump);
            if (!_hpInit)
            {
                _hpRail = lp;
                _hpInit = true;
            }

            double target = lp;
            if (hp != null)
            {
                double maxKpa = hp.Param("max_kpa", 22000);
                double weak = _faults.Max(hp.Id, EffectKind.Weak);
                double capacity = maxKpa * MathUtil.SmoothStep(80, 700, rpm) * (1 - weak) * MathUtil.Clamp01(lp / 250.0) * (0.85 + 0.15 * hp.Health);
                double demand = MathUtil.Clamp01(State.FuelFlowGps / Math.Max(0.1, _def.InjectorFlowCcMin * _def.Cylinders * Physics.GasolineDensity / 60.0));
                capacity *= 1 - demand * (0.15 + 1.2 * weak);
                double wanted = cmd.MeteringDriven ? cmd.RailTargetKpa : maxKpa;
                if (_faults.Has(hp.Id, EffectKind.StuckOpen))
                {
                    wanted = maxKpa;
                }

                if (_faults.Has(hp.Id, EffectKind.StuckClosed) || _faults.Has(hp.Id, EffectKind.Dead))
                {
                    capacity = 0;
                }

                target = Math.Max(lp, Math.Min(wanted, capacity));
            }

            double tau = target > _hpRail ? 0.08 : (rpm > 50 ? 0.12 : 30);
            _hpRail = MathUtil.FirstOrder(_hpRail, target, tau, dt);
            s.FuelRailKpa = Math.Max(0, _hpRail);
        }

        // ------------------------------------------------------------------ VGT

        /// <summary>Electric vane actuator of a variable geometry turbo. Returns false when the car has none.</summary>
        private bool UpdateVgt(double dt, EngineCommands cmd, out double closure)
        {
            EngineState s = State;
            Component? vgt = _parts.Find(ComponentKind.VgtActuator);
            if (vgt == null)
            {
                closure = 0;
                return false;
            }

            double target = cmd.VgtDriven ? MathUtil.Clamp01(cmd.VgtCommand) : 0; // spring opens the vanes
            if (_faults.Has(vgt.Id, EffectKind.StuckOpen) || _faults.Has(vgt.Id, EffectKind.Dead))
            {
                target = Math.Min(target, 0.1);
            }
            else if (_faults.Has(vgt.Id, EffectKind.StuckClosed))
            {
                target = 0.95;
            }

            double sticky = _faults.Max(vgt.Id, EffectKind.Clog) + F(ComponentKind.Turbocharger, EffectKind.Clog);
            double tau = 0.25 / Math.Max(0.08, 1 - sticky);
            double deadband = 0.25 * sticky;
            if (Math.Abs(target - s.VgtPosition) > deadband)
            {
                s.VgtPosition = MathUtil.FirstOrder(s.VgtPosition, target, tau, dt);
            }

            closure = s.VgtPosition;
            return true;
        }

        // ------------------------------------------------------------------ DPF + NOx

        /// <summary>Exhaust restriction added by the particulate filter (soot, ash, blocked substrate).</summary>
        private double DpfRestriction()
        {
            Component? dpf = _parts.Find(ComponentKind.ParticulateFilter);
            if (dpf == null)
            {
                return 0;
            }

            double crack = _faults.Max(dpf.Id, EffectKind.Leak);
            return (State.DpfSootG / 40.0 + State.DpfAshG / 80.0 + _faults.Max(dpf.Id, EffectKind.Restriction)) * 0.3 * (1 - 0.8 * crack);
        }

        /// <summary>
        /// Soot loading (engine-out soot grows with smoke and fuel), passive regeneration with NO2 at 350–500 °C,
        /// active regeneration above ≈580 °C (post injection), ash build-up, differential pressure and NOx.
        /// </summary>
        private void UpdateAftertreatment(double dt, double rpm, double lambdaExh)
        {
            EngineState s = State;
            double exhaustKgS = _prevEngineAir + s.FuelFlowGps / 1000.0;
            double maxFlow = _def.DisplacementM3 * 1.2 * _def.RedlineRpm / 120.0 * (_def.IsTurbo ? 2.0 : 1.0);
            double volumetric = exhaustKgS / Math.Max(1e-6, maxFlow) * Physics.ToKelvin(s.ExhaustGasC) / 600.0;

            Component? dpf = _parts.Find(ComponentKind.ParticulateFilter);
            if (dpf != null && _def.IsDiesel)
            {
                double crack = _faults.Max(dpf.Id, EffectKind.Leak);
                double sootGps = s.Running ? s.FuelFlowGps * (0.0012 + 0.025 * s.SmokeOpacity) * (1 + 1.5 * s.EgrFraction) : 0;
                double passive = s.DpfSootG * 4e-4 * MathUtil.SmoothStep(330, 470, s.ExhaustGasC) * (1 - MathUtil.SmoothStep(550, 650, s.ExhaustGasC));
                double active = s.DpfSootG * 3.5e-3 * MathUtil.SmoothStep(540, 620, s.ExhaustGasC);
                double burnt = (passive + active) * dt;
                s.DpfSootG = Math.Max(0, s.DpfSootG + sootGps * (1 - 0.9 * crack) * dt - burnt);
                s.DpfAshG += burnt * 0.01 + s.FuelFlowGps * 1e-6 * dt;
                double restriction = _faults.Max(dpf.Id, EffectKind.Restriction);
                double dp = 6.0 * volumetric * (1 + s.DpfSootG / 15.0 + s.DpfAshG / 40.0 + 4 * restriction) * (1 - 0.85 * crack);
                s.DpfDeltaKpa = MathUtil.FirstOrder(s.DpfDeltaKpa, dp, 0.5, dt);
            }
            else
            {
                s.DpfDeltaKpa = 0;
            }

            // Engine-out NOx: high combustion temperature (load, lean-ish mixture, advance); EGR dilution cuts it.
            if (!s.Running)
            {
                s.NoxPpm = 0;
            }
            else if (_def.IsDiesel)
            {
                double load = MathUtil.Clamp01(s.RelativeLoad / 1.6);
                s.NoxPpm = (90 + 1100 * load) * Math.Max(0.15, 1 - 2.2 * s.EgrFraction) * (1 + 0.03 * (s.SparkAdvanceDeg - 6));
            }
            else
            {
                double peak = Math.Exp(-Math.Pow((lambdaExh - 1.08) / 0.12, 2));
                s.NoxPpm = 2500 * MathUtil.Clamp01(s.RelativeLoad) * peak * Math.Max(0.2, 1 - 2 * s.EgrFraction);
            }
        }
    }
}
