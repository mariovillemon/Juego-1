using System;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Electrical;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Ecu
{
    /// <summary>EVAP leak test phases.</summary>
    public enum EvapTestPhase
    {
        /// <summary>Waiting for enabling conditions.</summary>
        Idle,

        /// <summary>Vent closed, purge closed: tank must not be pulled into vacuum (purge valve sealing).</summary>
        SealCheck,

        /// <summary>Vent closed, purge open: engine vacuum pulls the tank down.</summary>
        Pulldown,

        /// <summary>Everything closed: vacuum decay measures the leak size.</summary>
        Hold,

        /// <summary>Vent reopened: pressure must return to atmospheric.</summary>
        Release,
    }

    /// <summary>
    /// ECU strategies for the optional systems: variable valve timing, GDI rail, VGT, diesel particulate filter with
    /// regeneration, EGR flow monitor, exhaust temperature and the EVAP leak test. Each block runs only when the car
    /// has the hardware. All diagnosis uses what the ECU can measure (sensor voltages, driver feedback), never the
    /// simulation's ground truth.
    /// </summary>
    public sealed partial class EngineControlUnit
    {
        private EvapTestPhase _evapPhase;
        private double _evapPhaseTime;
        private double _evapP0;
        private double _evapStftBefore;
        private double _evapStftSum;
        private int _evapStftSamples;
        private bool _evapDoneThisTrip;
        private double _evapRestTime;
        private bool _regenActive;
        private double _postMg;
        private double _egrMonitorTime;
        private double _egrFracSum;
        private int _egrSamples;

        /// <summary>EVAP leak test phase (live data / tests).</summary>
        public EvapTestPhase EvapPhase => _evapPhase;

        /// <summary>DPF regeneration in progress.</summary>
        public bool DpfRegenerating => _regenActive;

        /// <summary>Soot load estimated by the ECU from the differential pressure (g).</summary>
        public double DpfSootEstimateG { get; private set; }

        private struct SystemInputs
        {
            public double Rpm, Load, Ect, Baro, Maf, SdAir, Speed;
            public bool Running, KeyOn, MafValid;
        }

        private void UpdateSystems(double dt, SystemInputs x)
        {
            UpdateVvt(dt, x);
            UpdateVgtCommand();
            UpdateDieselAftertreatment(dt, x);
            UpdateEgrMonitor(dt, x);
            UpdateEvap(dt, x);
            if (_car.Parts.Find(ComponentKind.HighPressurePump) != null && _keyOnTime > 0.5)
            {
                SimpleCircuitCheck(ComponentKind.HighPressurePump, "P0090", "P0090", "P0090", dt);
            }
        }

        // ---------------------------------------------------------------- VVT

        private void UpdateVvt(double dt, SystemInputs x)
        {
            Component? ocv = _car.Parts.Find(ComponentKind.VvtSolenoid);
            if (ocv == null)
            {
                Outputs.CamTargetDeg = 0;
                return;
            }

            double target = x.Running && x.Ect > 40 ? Calibration.Lookup(EcuCalibration.CamTarget, x.Rpm, x.Load, 0) : 0;
            Outputs.CamTargetDeg = target;
            if (_keyOnTime > 0.5)
            {
                SimpleCircuitCheck(ComponentKind.VvtSolenoid, "P0010", "P0010", "P0010", dt);
            }

            // Actual cam position from the CMP edge vs crank teeth (includes any chain stretch offset).
            double actual = CamSync ? _car.Engine.State.CamPhaseDeg + _car.Engine.State.CamOffsetDeg : double.NaN;
            bool enabled = x.Running && _runTime > 10 && x.Ect > 60 && x.Rpm > 1200 && CamSync && !double.IsNaN(actual);
            Check("P0011", enabled, actual - target > 9, dt, 5, 10);
            Check("P0012", enabled && target > 10, target - actual > 9, dt, 5, 10);
            _live["cam_target_deg"] = target;
            _live["cam_actual_deg"] = double.IsNaN(actual) ? 0 : actual;
        }

        // ---------------------------------------------------------------- VGT

        private void UpdateVgtCommand()
        {
            if (_car.Parts.Find(ComponentKind.VgtActuator) == null)
            {
                Outputs.VgtCommand = 0;
                return;
            }

            // The boost controller's duty drives the vanes (more closure = more boost); with a fault the vanes open.
            // Base closure keeps exhaust energy (and EGR drive pressure) at part load; with a fault the vanes open.
            bool running = _car.Engine.State.Running;
            Outputs.VgtCommand = LimpMode || !running ? 0 : MathUtil.Clamp01(0.3 + 0.7 * Outputs.WastegateDuty);
            _live["vgt_cmd_pct"] = Outputs.VgtCommand * 100;
            _live["vgt_pos_pct"] = _car.Engine.State.VgtPosition * 100;
        }

        // ------------------------------------------------------- DPF + EGT

        private void UpdateDieselAftertreatment(double dt, SystemInputs x)
        {
            Component? dpSensor = _car.Parts.Find(ComponentKind.DpfPressureSensor);
            Component? egtSensor = _car.Parts.Find(ComponentKind.ExhaustTempSensor);
            if (dpSensor == null)
            {
                Outputs.PostInjectionMg = 0;
                return;
            }

            // Exhaust temperature sensor (regeneration control).
            double egt = _car.Engine.State.ExhaustGasC;
            bool egtValid = egtSensor == null;
            if (egtSensor != null)
            {
                double ev = VoltsOf(egtSensor);
                bool lo = ev < 0.25;
                bool hi = ev > 4.7;
                Check("P0545", _keyOnTime > 0.5, lo, dt, 1, 2);
                Check("P0546", _keyOnTime > 0.5, hi, dt, 1, 2);
                egtValid = !lo && !hi;
                egt = egtValid ? SensorCurves.LinearValue(ev, egtSensor.Param("range_min_c", 0), egtSensor.Param("range_max_c", 1000)) : 0;
            }

            double dv = VoltsOf(dpSensor);
            bool dpLow = dv < 0.25;
            bool dpHigh = dv > 4.7;
            Check("P2454", _keyOnTime > 0.5, dpLow, dt, 1, 2);
            Check("P2455", _keyOnTime > 0.5, dpHigh, dt, 1, 2);
            bool dpValid = !dpLow && !dpHigh;
            double dp = dpValid ? SensorCurves.LinearValue(dv, dpSensor.Param("range_min_kpa", 0), dpSensor.Param("range_max_kpa", 100)) : 0;
            Check("P2452", _keyOnTime > 0.5, !dpValid, dt, 2, 2);
            Check("P2453", dpValid && x.KeyOn && !x.Running && x.Rpm < 1 && _keyOnTime > 2, Math.Abs(dp) > 3, dt, 2, 3);

            // Soot estimate from Δp and the exhaust volume flow the ECU computes from MAF and EGT.
            EngineDefinition eng = _car.Definition.Engine;
            double maxFlowGps = eng.DisplacementM3 * 1.2 * eng.RedlineRpm / 120.0 * 2.0 * 1000;
            double vol = (x.MafValid ? x.Maf : x.SdAir) / Math.Max(1, maxFlowGps) * Physics.ToKelvin(egtValid ? egt : 300) / 600.0;
            double ashAllow = 1 + _car.Engine.State.DpfAshG / 40.0; // ash learnt at the last service (kept simple)
            if (dpValid && vol > 0.12 && x.Running)
            {
                double estimate = Math.Max(0, (dp / (6.0 * vol) - ashAllow) * 15.0);
                DpfSootEstimateG = MathUtil.FirstOrder(DpfSootEstimateG, estimate, 20, dt);
                bool tooLow = vol > 0.3 && dp < 0.35 * 6.0 * vol;
                Check("P244A", true, tooLow, dt, 10, 20);
                Check("P2002", true, tooLow, dt, 15, 20);
                Check("P244B", true, estimate > Calibration.Scalar("dpf_limit_g", 45) * 1.3, dt, 10, 20);
            }

            double start = Calibration.Scalar("dpf_regen_start_g", 22);
            double stop = Calibration.Scalar("dpf_regen_stop_g", 4);
            double limit = Calibration.Scalar("dpf_limit_g", 45);
            Check("P2463", x.Running && _runTime > 30, DpfSootEstimateG > limit, dt, 10, 20);
            bool canRegen = x.Running && x.Rpm > 1300 && x.Load > 0.15 && x.Ect > 60 && egtValid && dpValid && !LimpMode && DpfSootEstimateG < limit;
            if (!_regenActive && canRegen && DpfSootEstimateG > start)
            {
                _regenActive = true;
            }

            if (_regenActive && (DpfSootEstimateG < stop || !canRegen))
            {
                _regenActive = false;
            }

            // Closed loop on the DPF inlet temperature: ~620 °C.
            _postMg = _regenActive ? MathUtil.Clamp(_postMg + (620 - egt) * 0.02 * dt, 0, 18) : MathUtil.FirstOrder(_postMg, 0, 1, dt);
            Outputs.PostInjectionMg = _postMg;
            _live["dpf_dp_kpa"] = dp;
            _live["dpf_soot_g"] = DpfSootEstimateG;
            _live["dpf_regen"] = _regenActive ? 1 : 0;
            _live["egt_c"] = egt;
            _live["nox_ppm"] = _car.Engine.State.NoxPpm;
        }

        // ---------------------------------------------------------------- EGR

        /// <summary>
        /// EGR flow monitor (Euro 6 diesel): the fresh air the MAF measures must drop when EGR opens. The ECU compares
        /// MAF with its own speed-density estimate of the total cylinder charge.
        /// </summary>
        private void UpdateEgrMonitor(double dt, SystemInputs x)
        {
            Component? egr = _car.Parts.Find(ComponentKind.EgrValve);
            if (egr == null || _car.Parts.Find(ComponentKind.ParticulateFilter) == null)
            {
                return;
            }

            if (_keyOnTime > 0.5)
            {
                SimpleCircuitCheck(ComponentKind.EgrValve, "P0403", "P0403", "P0403", dt);
            }

            if (!x.Running || !x.MafValid || x.SdAir < 5 || _runTime < 30 || x.Ect < 60)
            {
                return;
            }

            double fraction = 1 - x.Maf / x.SdAir;
            _egrFracSum += fraction;
            _egrSamples++;
            _egrMonitorTime += dt;
            _live["egr_rate_pct"] = Math.Max(0, fraction * 100);
            if (_egrMonitorTime < 10)
            {
                return;
            }

            double avg = _egrFracSum / Math.Max(1, _egrSamples);
            bool commanded = Outputs.EgrCommand > 0.25;
            Check("P0401", commanded, avg < 0.06, _egrMonitorTime, 10, 10);
            Check("P0402", Outputs.EgrCommand < 0.01, avg > 0.15, _egrMonitorTime, 10, 10);
            if (commanded)
            {
                Readiness.SetComplete(ReadinessMonitor.Egr);
            }

            _egrMonitorTime = 0;
            _egrFracSum = 0;
            _egrSamples = 0;
        }

        // ---------------------------------------------------------------- EVAP

        /// <summary>
        /// Engine-running vacuum decay test: seal check (purge closed), pull-down (purge open, vent closed), hold
        /// (all closed: the decay rate gives the leak size), release (vent open). Purge flow is judged from the
        /// short-term fuel trim reaction when the purge opens.
        /// </summary>
        private void UpdateEvap(double dt, SystemInputs x)
        {
            Component? sensor = _car.Parts.Find(ComponentKind.EvapPressureSensor);
            if (sensor == null || _car.Parts.Find(ComponentKind.EvapCanister) == null)
            {
                Outputs.EvapVentClosed = false;
                return;
            }

            double v = VoltsOf(sensor);
            bool lo = v < 0.25;
            bool hi = v > 4.7;
            Check("P0452", _keyOnTime > 0.5, lo, dt, 1, 2);
            Check("P0453", _keyOnTime > 0.5, hi, dt, 1, 2);
            bool valid = !lo && !hi;
            double p = valid ? SensorCurves.LinearValue(v, sensor.Param("range_min_kpa", -4), sensor.Param("range_max_kpa", 2)) : 0;
            _live["evap_kpa"] = p;
            if (_keyOnTime > 0.5)
            {
                SimpleCircuitCheck(ComponentKind.EvapVentValve, "P0449", "P0449", "P0449", dt);
            }

            // Rationality with the vent open and the purge closed: the tank must sit near atmospheric pressure.
            _evapRestTime = _evapPhase == EvapTestPhase.Idle && Outputs.PurgeDuty < 0.01 ? _evapRestTime + dt : 0;
            Check("P0451", valid && _evapRestTime > 8 && !x.Running, Math.Abs(p) > 1.0, dt, 4, 6);

            // The test needs manifold vacuum to pull the tank down (no boost; turbo engines test at light load).
            bool vacuum = _car.Engine.State.ManifoldKpa < x.Baro - 25;
            bool conditions = valid && x.Running && FuelStatus == FuelSystemStatus.ClosedLoop && _closedLoopTime > 30 && x.Ect > 70 && x.Rpm < 3000 && !LimpMode && vacuum;
            _evapPhaseTime += dt;
            switch (_evapPhase)
            {
                case EvapTestPhase.Idle:
                    Outputs.EvapVentClosed = false;
                    if (conditions && !_evapDoneThisTrip && _runTime > 90)
                    {
                        Enter(EvapTestPhase.SealCheck);
                        _evapP0 = p;
                    }

                    break;
                case EvapTestPhase.SealCheck:
                    Outputs.EvapVentClosed = true;
                    Outputs.PurgeDuty = 0;
                    if (!conditions)
                    {
                        Abort();
                    }
                    else if (_evapPhaseTime > 6)
                    {
                        Check("P0496", true, p < _evapP0 - 1.0, 6, 1, 1);
                        _evapStftBefore = _stft + Ltft;
                        _evapStftSum = 0;
                        _evapStftSamples = 0;
                        Enter(EvapTestPhase.Pulldown);
                    }

                    break;
                case EvapTestPhase.Pulldown:
                    Outputs.EvapVentClosed = true;
                    Outputs.PurgeDuty = 0.6;
                    _evapStftSum += _stft + Ltft;
                    _evapStftSamples++;
                    if (!conditions)
                    {
                        Abort();
                    }
                    else if (p < -1.6)
                    {
                        _evapP0 = p;
                        Enter(EvapTestPhase.Hold);
                    }
                    else if (_evapPhaseTime > 25)
                    {
                        // No vacuum: either the purge does not flow (no trim reaction) or there is a large leak.
                        double trimShift = Math.Abs(_evapStftSum / Math.Max(1, _evapStftSamples) - _evapStftBefore);
                        bool purgeFlows = trimShift > 0.012;
                        Check("P0441", true, !purgeFlows, 25, 1, 1);
                        if (purgeFlows)
                        {
                            bool refuelled = _car.Evap.RefuelledThisTrip;
                            Check(refuelled ? "P0457" : "P0455", true, true, 25, 1, 1);
                        }

                        Finish();
                    }

                    break;
                case EvapTestPhase.Hold:
                    Outputs.EvapVentClosed = true;
                    Outputs.PurgeDuty = 0;
                    if (!conditions)
                    {
                        Abort();
                    }
                    else if (_evapPhaseTime > 12)
                    {
                        double rate = (p - _evapP0) / _evapPhaseTime;
                        _live["evap_decay_kpa_s"] = rate;
                        Check("P0442", true, rate > 0.09, 12, 1, 1);
                        Check("P0456", true, rate > 0.035 && rate <= 0.09, 12, 1, 1);
                        Check("P0455", true, false, 12, 1, 1);
                        Check("P0441", true, false, 12, 1, 1);
                        Enter(EvapTestPhase.Release);
                    }

                    break;
                case EvapTestPhase.Release:
                    Outputs.EvapVentClosed = false;
                    Outputs.PurgeDuty = 0;
                    if (_evapPhaseTime > 6)
                    {
                        Check("P0446", true, p < -0.5, 6, 1, 1);
                        Finish();
                    }

                    break;
            }
        }

        private void Enter(EvapTestPhase phase)
        {
            _evapPhase = phase;
            _evapPhaseTime = 0;
        }

        private void Abort()
        {
            Outputs.EvapVentClosed = false;
            Enter(EvapTestPhase.Idle);
        }

        private void Finish()
        {
            _evapDoneThisTrip = true;
            Readiness.SetComplete(ReadinessMonitor.Evap);
            Abort();
        }

        /// <summary>Resets the per-trip EVAP state (key off).</summary>
        private void ResetSystemsTrip()
        {
            _evapDoneThisTrip = false;
            Enter(EvapTestPhase.Idle);
            Outputs.EvapVentClosed = false;
            _regenActive = false;
            _postMg = 0;
            _car.Evap.RefuelledThisTrip = false;
        }
    }
}
