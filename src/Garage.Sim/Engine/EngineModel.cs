using System;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Engine
{
    /// <summary>
    /// Mean value engine model. Computes air path, turbo, fuel delivery, per-cylinder combustion,
    /// torque, temperatures and damage each fixed step. Engine speed is integrated by the drivetrain.
    /// Faults never appear here as "symptoms": they only change physical parameters.
    /// </summary>
    public sealed class EngineModel
    {
        private readonly EngineDefinition _def;
        private readonly ComponentRegistry _parts;
        private readonly FaultSet _faults;
        private readonly DeterministicRandom _rng;
        private readonly double[] _cylPhase;
        private double _boostState;
        private double _railState;
        private double _mafFiltered;
        private double _prevEngineAir;
        private double _catOxygenStorage = 0.5;
        private double _coolantLossAccum;
        private readonly double[] _lambdaHistory = CreateHistory();
        private int _historyIndex;

        /// <summary>Creates the engine.</summary>
        public EngineModel(EngineDefinition def, ComponentRegistry parts, FaultSet faults, DeterministicRandom rng)
        {
            _def = def;
            _parts = parts;
            _faults = faults;
            _rng = rng;
            State = new EngineState(def.Cylinders);
            Damage = new EngineDamage(def.Cylinders);
            _cylPhase = new double[def.Cylinders];
            for (int i = 0; i < def.Cylinders; i++)
            {
                _cylPhase[i] = (double)i / def.Cylinders;
            }
        }

        private static double[] CreateHistory()
        {
            var h = new double[96];
            for (int i = 0; i < h.Length; i++)
            {
                h[i] = 1.0;
            }

            return h;
        }

        /// <summary>Definition.</summary>
        public EngineDefinition Definition => _def;

        /// <summary>Current physical state.</summary>
        public EngineState State { get; }

        /// <summary>Accumulated damage.</summary>
        public EngineDamage Damage { get; private set; }

        /// <summary>Replaces damage (repairs/engine rebuild/save load).</summary>
        public void SetDamage(EngineDamage d) => Damage = d;

        /// <summary>Initialises temperatures to ambient (cold soak) or to warm.</summary>
        public void Soak(double ambientC, bool warm)
        {
            double t = warm ? 90 : ambientC;
            State.CoolantC = t;
            State.OilC = warm ? 95 : ambientC;
            State.ExhaustGasC = warm ? 300 : ambientC;
            State.CatalystC = warm ? 400 : ambientC;
            State.O2SensorC = warm ? 600 : ambientC;
            State.O2DownstreamC = warm ? 450 : ambientC;
            State.IntakeAirC = ambientC;
            State.ChargeAirC = ambientC + (warm ? 10 : 0);
            _railState = 0;
        }

        private double F(ComponentKind k, EffectKind e, int cyl = -1)
        {
            Component? c = _parts.Find(k, cyl);
            return c == null ? 0 : _faults.Max(c.Id, e);
        }

        private bool Has(ComponentKind k, EffectKind e, int cyl = -1)
        {
            Component? c = _parts.Find(k, cyl);
            return c != null && _faults.Has(c.Id, e);
        }

        private double LeakAreaMm2(ComponentKind kind, out double[] perCylinder)
        {
            perCylinder = new double[_def.Cylinders];
            double total = 0;
            foreach (Component c in _parts.OfKind(kind))
            {
                double a = _faults.Sum(c.Id, EffectKind.Leak);
                if (a <= 0)
                {
                    continue;
                }

                total += a;
                if (c.Cylinder >= 0 && c.Cylinder < _def.Cylinders)
                {
                    perCylinder[c.Cylinder] += a;
                }
            }

            return total;
        }

        /// <summary>Engine air demand (kg/s) at a manifold pressure.</summary>
        private double EngineFlow(double mapKpa, double chargeC, double rpm, double camLoss)
        {
            double ve = _def.VolumetricEfficiency?.Lookup(rpm, mapKpa) ?? 0.85;
            ve *= 1.0 - camLoss;
            return ve * Physics.AirDensity(mapKpa, chargeC) * _def.DisplacementM3 * rpm / 120.0;
        }

        /// <summary>Advances the engine physics one step. Returns brake torque at the current rpm.</summary>
        public double Step(double dt, EngineCommands cmd, EnvironmentState env, double vehicleSpeedMs, bool cranking)
        {
            EngineState s = State;
            int n = _def.Cylinders;
            double rpm = Math.Max(0, s.Rpm);
            double baro = env.BaroKpa;
            s.IntakeAirC = MathUtil.FirstOrder(s.IntakeAirC, env.AmbientC + (s.Running ? 4 + s.CoolantC * 0.05 : 0), 60, dt);

            if (Damage.RodFailed)
            {
                s.Seized = true;
            }

            // ---------------- Cam timing (stretched chain/belt) ----------------
            double slack = F(ComponentKind.TimingDrive, EffectKind.Slack) + F(ComponentKind.TimingDrive, EffectKind.Wear) * 8;
            s.CamOffsetDeg = slack;
            double camLoss = MathUtil.Clamp(Math.Abs(slack) * 0.012, 0, 0.35);

            // ---------------- Air filter and turbo ----------------
            double airFilterRestriction = F(ComponentKind.AirFilter, EffectKind.Restriction) + F(ComponentKind.AirFilter, EffectKind.Clog);
            double maxFlowKgS = _def.DisplacementM3 * 1.2 * 7000 / 120.0 * (_def.IsTurbo ? 2.2 : 1.0);
            double flowFrac = _prevEngineAir / Math.Max(1e-6, maxFlowKgS);
            double filterDrop = 3.0 * flowFrac * flowFrac * (1 + 12 * airFilterRestriction);
            double p1 = baro - filterDrop;

            double boostTarget = 0;
            double chargeC = s.IntakeAirC;
            if (_def.Turbo != null)
            {
                TurboDefinition t = _def.Turbo!;
                double turboWear = F(ComponentKind.Turbocharger, EffectKind.Wear) + (Damage.TurboFailed ? 0.8 : Damage.Turbo * 0.2);
                double exhaustEnergy = MathUtil.Clamp01(_prevEngineAir / Math.Max(1e-6, _def.DisplacementM3 * 1.2 * t.FullSpoolRpm / 120.0))
                    * MathUtil.SmoothStep(t.SpoolStartRpm * 0.6, t.FullSpoolRpm, rpm);
                double capacity = t.MaxBoostKpa * Math.Min(1.0, exhaustEnergy * 1.25) * (1 - MathUtil.Clamp01(turboWear));

                // Compressor flow limit: leaks downstream of the compressor steal flow the turbo cannot supply.
                double demandGps = (_prevEngineAir * 1000) + State.BoostLeakGps;
                if (demandGps > t.MaxFlowGps)
                {
                    capacity *= Math.Pow(t.MaxFlowGps / demandGps, 3);
                }

                // Wastegate: solenoid duty bleeds actuator pressure → higher opening pressure.
                double duty = MathUtil.Clamp01(cmd.WastegateDuty);
                if (Has(ComponentKind.WastegateSolenoid, EffectKind.StuckOpen))
                {
                    duty = 1;
                }
                else if (Has(ComponentKind.WastegateSolenoid, EffectKind.StuckClosed) || Has(ComponentKind.WastegateSolenoid, EffectKind.Dead))
                {
                    duty = 0;
                }

                double wgPressure = t.WastegateSpringKpa + duty * (t.MaxBoostKpa * 1.1 - t.WastegateSpringKpa);
                if (Has(ComponentKind.Turbocharger, EffectKind.StuckOpen))
                {
                    wgPressure = 12;
                }
                else if (Has(ComponentKind.Turbocharger, EffectKind.StuckClosed))
                {
                    wgPressure = 999;
                }

                // Actuator hose leak behaves like zero duty.
                if (Has(ComponentKind.WastegateSolenoid, EffectKind.Leak))
                {
                    wgPressure = t.WastegateSpringKpa;
                }

                boostTarget = Math.Max(0, Math.Min(capacity, wgPressure));
                if (!_def.IsDiesel)
                {
                    // Little exhaust energy at closed throttle: boost collapses.
                    boostTarget *= MathUtil.SmoothStep(0.05, 0.35, s.ThrottlePosition);
                }
                else
                {
                    // Diesel exhaust energy follows fuelling, not airflow.
                    boostTarget *= MathUtil.SmoothStep(6, 30, cmd.DieselMgPerStroke);
                }

                double tau = t.SpoolTimeConstant * (1.5 - exhaustEnergy);
                _boostState = MathUtil.FirstOrder(_boostState, boostTarget, Math.Max(0.08, tau), dt);
                s.TurboSpeed = MathUtil.Clamp01(_boostState / Math.Max(1, t.MaxBoostKpa));

                double pr = (p1 + _boostState) / Math.Max(1, p1);
                double t1 = Physics.ToKelvin(s.IntakeAirC);
                double t2 = t1 * (1 + (Math.Pow(pr, 0.2857) - 1) / t.CompressorEfficiency);
                double icLeak = F(ComponentKind.Intercooler, EffectKind.Restriction);
                Component? ic = _parts.Find(ComponentKind.Intercooler);
                double eff = (ic?.Param("effectiveness", t.IntercoolerEffectiveness) ?? t.IntercoolerEffectiveness) * (1 - icLeak) * MathUtil.Clamp(0.6 + vehicleSpeedMs / 40.0, 0.6, 1.0);
                double tc = t2 - eff * (t2 - Physics.ToKelvin(env.AmbientC));
                chargeC = tc - Physics.KelvinOffset;
            }

            double preThrottle = p1 + _boostState;
            s.PreThrottleKpa = preThrottle;
            s.BoostKpa = preThrottle - baro;

            // ---------------- Throttle ----------------
            double thr = MathUtil.Clamp01(cmd.Throttle);
            if (!cmd.ThrottleDriven)
            {
                thr = 0.07;
            }

            if (Has(ComponentKind.ElectronicThrottle, EffectKind.StuckOpen))
            {
                thr = Math.Max(0.02, F(ComponentKind.ElectronicThrottle, EffectKind.StuckOpen));
            }
            else if (Has(ComponentKind.ElectronicThrottle, EffectKind.StuckClosed))
            {
                thr = 0.0;
            }

            if (_def.IsDiesel)
            {
                thr = 1.0;
            }

            s.ThrottlePosition = MathUtil.FirstOrder(s.ThrottlePosition, thr, 0.04, dt);
            double dirty = F(ComponentKind.ElectronicThrottle, EffectKind.Restriction);
            double d = _def.ThrottleDiameterMm / 1000.0;
            double fullArea = Math.PI / 4 * d * d;
            double angle0 = 7.0 * Math.PI / 180;
            double angle = angle0 + s.ThrottlePosition * (83.0 * Math.PI / 180);
            double areaFrac = 1 - Math.Cos(angle) / Math.Cos(angle0);
            double throttleArea = 0.82 * fullArea * Math.Max(0, areaFrac) * (1 - dirty * (1 - s.ThrottlePosition));
            throttleArea += 1.0e-6 * (1 - dirty); // bypass / closed gap leakage (≈1 mm²)

            // ---------------- Leaks ----------------
            double vacuumLeakMm2 = LeakAreaMm2(ComponentKind.VacuumHose, out double[] perCylA) + LeakAreaMm2(ComponentKind.IntakeGasket, out double[] perCylB);
            if (Has(ComponentKind.PurgeValve, EffectKind.StuckOpen))
            {
                vacuumLeakMm2 += 3.0;
            }

            double boostLeakMm2 = LeakAreaMm2(ComponentKind.BoostHose, out _) + LeakAreaMm2(ComponentKind.Intercooler, out _);

            // EGR (dilution)
            double egrArea = 0;
            Component? egr = _parts.Find(ComponentKind.EgrValve);
            if (egr != null)
            {
                double egrCmd = cmd.EgrCommand;
                if (_faults.Has(egr.Id, EffectKind.StuckOpen))
                {
                    egrCmd = Math.Max(egrCmd, _faults.Max(egr.Id, EffectKind.StuckOpen));
                }
                else if (_faults.Has(egr.Id, EffectKind.StuckClosed) || _faults.Has(egr.Id, EffectKind.Clog))
                {
                    egrCmd = egrCmd * (1 - Math.Max(_faults.Max(egr.Id, EffectKind.Clog), _faults.Has(egr.Id, EffectKind.StuckClosed) ? 1 : 0));
                }

                egrArea = egrCmd * egr.Param("max_area_mm2", 120) * 1e-6;
            }

            // ---------------- Manifold pressure (bisection on mass balance) ----------------
            double exhaustRestriction = F(ComponentKind.Exhaust, EffectKind.Restriction) + F(ComponentKind.Catalyst, EffectKind.Restriction) + (Damage.CatalystMelted ? 0.6 : 0);
            double exhaustKpa = baro + 4 * flowFrac * flowFrac * 100 / 100 * 10 * (1 + 10 * exhaustRestriction) + (_def.IsTurbo ? _boostState * 0.9 : 0);
            s.ExhaustKpa = exhaustKpa;
            double leakArea = vacuumLeakMm2 * 1e-6 * 0.85;

            double mapKpa;
            double thrFlow;
            double leakFlow;
            double egrFlow;
            double engFlow;
            if (rpm < 1)
            {
                mapKpa = preThrottle;
                thrFlow = leakFlow = egrFlow = engFlow = 0;
                if (!_def.IsTurbo)
                {
                    mapKpa = baro;
                }
            }
            else
            {
                double lo = 1.0;
                double hi = preThrottle;
                for (int it = 0; it < 40; it++)
                {
                    double mid = 0.5 * (lo + hi);
                    double inflow = Physics.OrificeFlow(throttleArea, preThrottle, chargeC, mid)
                        + Physics.OrificeFlow(leakArea, baro, env.AmbientC, mid)
                        + Physics.OrificeFlow(egrArea, exhaustKpa, 450, mid);
                    double outflow = EngineFlow(mid, chargeC, rpm, camLoss);
                    if (inflow > outflow)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                mapKpa = 0.5 * (lo + hi);
                thrFlow = Physics.OrificeFlow(throttleArea, preThrottle, chargeC, mapKpa);
                leakFlow = Math.Max(0, Physics.OrificeFlow(leakArea, baro, env.AmbientC, mapKpa));
                egrFlow = Math.Max(0, Physics.OrificeFlow(egrArea, exhaustKpa, 450, mapKpa));
                engFlow = EngineFlow(mapKpa, chargeC, rpm, camLoss);
            }

            s.ManifoldKpa = MathUtil.FirstOrder(s.ManifoldKpa, mapKpa, 0.03, dt);
            double boostLeakFlow = _def.IsTurbo ? Math.Max(0, Physics.OrificeFlow(boostLeakMm2 * 1e-6 * 0.85, preThrottle, chargeC, baro)) : 0;
            s.BoostLeakGps = boostLeakFlow * 1000;
            double mafTrue = (thrFlow + boostLeakFlow) * 1000;
            _mafFiltered = MathUtil.FirstOrder(_mafFiltered, mafTrue, 0.02, dt);
            s.MafFlowGps = Math.Max(0, _mafFiltered);
            s.UnmeteredAirGps = leakFlow * 1000;
            s.EngineAirGps = engFlow * 1000;
            s.EgrFraction = engFlow > 1e-9 ? MathUtil.Clamp01(egrFlow / engFlow) : 0;
            s.ChargeAirC = MathUtil.FirstOrder(s.ChargeAirC, chargeC + (s.Running ? (s.CoolantC - chargeC) * 0.08 : 0), 2.0, dt);
            _prevEngineAir = engFlow;

            double airPerCycleKg = rpm > 1 ? engFlow * 120.0 / rpm : 0;
            double rhoStd = Physics.AirDensity(101.325, 25);
            s.RelativeLoad = airPerCycleKg / (rhoStd * _def.DisplacementM3);

            // ---------------- Fuel system ----------------
            double totalInjMs = 0;
            for (int i = 0; i < n; i++)
            {
                totalInjMs += cmd.InjectorPulseMs[i];
            }

            Component? pump = _parts.Find(ComponentKind.FuelPump);
            double pumpCap = pump == null ? 1 : 1 - _faults.Max(pump.Id, EffectKind.Weak) - (pump.Id.Length > 0 && _faults.Has(pump.Id, EffectKind.Dead) ? 1 : 0);
            pumpCap = MathUtil.Clamp01(pumpCap) * (0.8 + 0.2 * (pump?.Health ?? 1));
            double pumpMax = cmd.FuelPumpPowered ? (pump?.Param("max_pressure_kpa", _def.PumpMaxPressureKpa) ?? _def.PumpMaxPressureKpa) * pumpCap * MathUtil.Clamp01((cmd.FuelPumpVolts - 6) / 7.5) : 0;
            double regTarget = _def.RailPressureKpa;
            if (Has(ComponentKind.FuelPressureRegulator, EffectKind.StuckClosed))
            {
                regTarget *= 1.45;
            }
            else if (Has(ComponentKind.FuelPressureRegulator, EffectKind.StuckOpen) || Has(ComponentKind.FuelPressureRegulator, EffectKind.Leak))
            {
                regTarget *= 1 - 0.35 * Math.Max(F(ComponentKind.FuelPressureRegulator, EffectKind.Leak), Has(ComponentKind.FuelPressureRegulator, EffectKind.StuckOpen) ? 1 : 0);
            }

            double filterClog = F(ComponentKind.FuelFilter, EffectKind.Clog) + F(ComponentKind.FuelFilter, EffectKind.Restriction);
            double fuelDemandFrac = MathUtil.Clamp01(State.FuelFlowGps / (_def.InjectorFlowCcMin * n * Physics.GasolineDensity / 60.0));
            double flowDrop = (_def.PumpMaxPressureKpa * 0.35) * fuelDemandFrac * fuelDemandFrac * (1 + 6 * filterClog) + 250 * filterClog * fuelDemandFrac;
            double railTarget = Math.Max(0, Math.Min(regTarget, pumpMax - flowDrop));
            if (_def.IsDiesel)
            {
                railTarget = cmd.FuelPumpPowered && rpm > 50 ? Math.Min(_def.RailPressureKpa * MathUtil.Remap(rpm, 150, 3000, 0.25, 1.0), pumpMax * 300) : 0;
            }

            double dribbleSum = 0;
            for (int i = 0; i < n; i++)
            {
                dribbleSum += F(ComponentKind.Injector, EffectKind.Dribble, i);
            }

            if (railTarget > _railState)
            {
                _railState = MathUtil.FirstOrder(_railState, railTarget, 0.25, dt);
            }
            else
            {
                double leakTau = cmd.FuelPumpPowered ? 0.2 : 600.0 / (1 + 60 * dribbleSum + 40 * F(ComponentKind.FuelPressureRegulator, EffectKind.Leak));
                _railState = MathUtil.FirstOrder(_railState, railTarget, leakTau, dt);
            }

            s.FuelRailKpa = Math.Max(0, _railState);

            // ---------------- Combustion per cylinder ----------------
            double mbt = _def.MbtAdvance?.Lookup(rpm, s.RelativeLoad) ?? 25;
            s.MbtAdvanceDeg = mbt;
            double spark = cmd.SparkAdvanceDeg;
            s.SparkAdvanceDeg = spark;

            // Knock limited advance: octane, cylinder pressure (MAP), charge temperature, coolant, mixture, deposits.
            double cr = _def.CompressionRatio;
            double deposits = Damage.PlugFouling * 0.5;
            double mapBar = s.ManifoldKpa / 100.0;
            double lambdaAvgPrev = 0;
            for (int i = 0; i < n; i++)
            {
                lambdaAvgPrev += s.CylinderLambda[i];
            }

            lambdaAvgPrev /= n;
            double kla = mbt + _def.KnockMarginDeg + 1.2 * (_def.FuelRon - 95) - 9.0 * (mapBar - 1.0) - 0.18 * (s.ChargeAirC - 40)
                - 0.12 * Math.Max(0, s.CoolantC - 95) + 18.0 * MathUtil.Clamp(1.0 - lambdaAvgPrev, -0.15, 0.2) - 1.5 * (cr - 9.6) - 4 * deposits
                + 0.0006 * (rpm - 3000);
            s.KnockLimitDeg = kla;

            // Ignition energy
            double coilVolts = cmd.IgnitionVolts;
            double gasQuality = _def.IsDiesel ? 1 - 0.25 * s.EgrFraction : Math.Max(0.2, 1 - s.EgrFraction * 1.6);
            double injDeadMs = 0.5 * Math.Pow(14.0 / Math.Max(8, cmd.IgnitionVolts), 1.3);
            double flowGms = _def.InjectorFlowCcMin * Physics.GasolineDensity / 60000.0;
            double fuelDensity = _def.IsDiesel ? Physics.DieselDensity : Physics.GasolineDensity;
            double afrStoich = _def.IsDiesel ? Physics.AfrDiesel : Physics.AfrGasoline;
            double lhv = _def.IsDiesel ? Physics.LhvDiesel : Physics.LhvGasoline;

            double indicatedWork = 0;
            double totalFuelG = 0;
            double totalBurntFuelKg = 0;
            double totalAirForSensor = 0;
            double totalFuelForSensor = 0;
            double unburnt = 0;
            double maxKnock = 0;
            double avgRoughness = 0;
            double engineAirPerCylKg = airPerCycleKg / n;
            double unmeteredTotalKg = rpm > 1 ? leakFlow * 120.0 / rpm : 0;
            double perCylLeakSum = 0;
            for (int i = 0; i < n; i++)
            {
                perCylLeakSum += perCylA[i] + perCylB[i];
            }

            for (int i = 0; i < n; i++)
            {
                s.MisfireEvents[i] = 0;
                s.FiringEvents[i] = 0;

                // Compression health
                Component? cylComp = _parts.Find(ComponentKind.Cylinder, i);
                double compression = 1.0;
                if (cylComp != null)
                {
                    compression -= _faults.Max(cylComp.Id, EffectKind.Wear) * 0.5 + _faults.Max(cylComp.Id, EffectKind.Leak);
                    compression *= 0.88 + 0.12 * cylComp.Health;
                }

                compression -= Damage.Piston[i] >= 1 ? 0.7 : Damage.Piston[i] * 0.1;
                compression -= Damage.ExhaustValve[i] >= 1 ? 0.6 : 0;
                if ((Damage.HeadGasketBlown || Has(ComponentKind.HeadGasket, EffectKind.Leak)) && (i == 1 || i == 2))
                {
                    compression -= 0.25 + 0.3 * F(ComponentKind.HeadGasket, EffectKind.Leak);
                }

                compression = MathUtil.Clamp(compression, 0.02, 1.05);
                s.CylinderCompression[i] = compression;

                // Air trapped (unmetered air distribution: cylinder-specific leaks go mostly to their cylinder)
                double cylAir = engineAirPerCylKg * (0.6 + 0.4 * compression) * (1 - s.EgrFraction);
                if (perCylLeakSum > 0 && vacuumLeakMm2 > 0)
                {
                    double share = (perCylA[i] + perCylB[i]) / vacuumLeakMm2;
                    double generalShare = 1 - perCylLeakSum / vacuumLeakMm2;
                    double extra = unmeteredTotalKg * (share * 0.7 + (perCylLeakSum / vacuumLeakMm2) * 0.3 / n + generalShare / n) - unmeteredTotalKg / n;
                    cylAir = Math.Max(0, cylAir + extra);
                }

                // Fuel
                double fuelKg;
                Component? inj = _parts.Find(ComponentKind.Injector, i);
                double clog = 0;
                double dribble = 0;
                bool stuckOpen = false;
                bool dead = false;
                if (inj != null)
                {
                    clog = _faults.Max(inj.Id, EffectKind.Clog) + (1 - inj.Health) * 0.08;
                    dribble = _faults.Max(inj.Id, EffectKind.Dribble);
                    stuckOpen = _faults.Has(inj.Id, EffectKind.StuckOpen);
                    dead = _faults.Has(inj.Id, EffectKind.Dead) || _faults.Has(inj.Id, EffectKind.StuckClosed);
                }

                if (_def.IsDiesel)
                {
                    double q = cmd.InjectorEnabled[i] && !dead ? cmd.DieselMgPerStroke * (1 - clog) * (1 + dribble) : 0;
                    q *= MathUtil.Clamp01(s.FuelRailKpa / Math.Max(1, _def.RailPressureKpa * 0.2));
                    fuelKg = q * 1e-6;
                }
                else
                {
                    double pressureFactor = Math.Sqrt(Math.Max(0, s.FuelRailKpa) / _def.InjectorRefPressureKpa);
                    double effMs = cmd.InjectorEnabled[i] && !dead ? Math.Max(0, cmd.InjectorPulseMs[i] - injDeadMs) : 0;
                    double injFlow = inj == null ? flowGms : inj.Param("flow_ccmin", _def.InjectorFlowCcMin) * Physics.GasolineDensity / 60000.0;
                    fuelKg = effMs * injFlow * pressureFactor * (1 - clog) / 1000.0;
                    fuelKg += dribble * 0.012 * pressureFactor / 1000.0 + (stuckOpen ? 0.06 * pressureFactor / 1000.0 : 0);
                }

                s.CylinderFuelMg[i] = fuelKg * 1e6;
                totalFuelG += fuelKg * 1000;
                double lambda = fuelKg > 1e-12 ? cylAir / (fuelKg * afrStoich) : 99;
                s.CylinderLambda[i] = Math.Min(lambda, 99);

                // Ignition quality
                double misfireP;
                if (_def.IsDiesel)
                {
                    double compTemp = compression * (s.CoolantC + 60 + (cmd.GlowOn ? 250 : 0) + Math.Min(rpm, 1000) * 0.15);
                    misfireP = fuelKg <= 0 ? 1 : MathUtil.Clamp01(1 - MathUtil.SmoothStep(80, 220, compTemp));
                    if (compression < 0.55)
                    {
                        misfireP = Math.Max(misfireP, 0.9);
                    }
                }
                else
                {
                    Component? coil = _parts.Find(ComponentKind.IgnitionCoil, i);
                    Component? plug = _parts.Find(ComponentKind.SparkPlug, i);
                    double available = 0;
                    if (cmd.SparkEnabled[i])
                    {
                        available = 36.0 * MathUtil.Clamp01((coilVolts - 5) / 6.0);
                        if (coil != null)
                        {
                            available *= 1 - _faults.Max(coil.Id, EffectKind.Weak) - 0.85 * _faults.Max(coil.Id, EffectKind.InternalShort);
                            if (_faults.Has(coil.Id, EffectKind.Dead))
                            {
                                available = 0;
                            }

                            available *= 0.9 + 0.1 * coil.Health;
                        }
                    }

                    double gap = 0.8;
                    double fouling = Damage.PlugFouling;
                    if (plug != null)
                    {
                        gap = plug.Param("gap_mm", 0.8) + _faults.Max(plug.Id, EffectKind.Wear) * 1.2 + (1 - plug.Health) * 0.35;
                        fouling += _faults.Max(plug.Id, EffectKind.Weak);
                    }

                    double cylPressureFactor = 0.6 + 0.6 * mapBar * compression;
                    double required = (4 + 12 * gap) * cylPressureFactor;
                    available *= 1 - MathUtil.Clamp01(fouling) * 0.6;
                    double ignitionMiss = available <= 0.1 ? 1 : MathUtil.SmoothStep(available * 0.82, available * 1.02, required);
                    double leanMiss = MathUtil.SmoothStep(1.32, 1.6, lambda / Math.Max(0.3, gasQuality));
                    double richMiss = MathUtil.SmoothStep(0.62, 0.45, lambda);
                    double compMiss = MathUtil.SmoothStep(0.62, 0.35, compression);
                    double dilutionMiss = MathUtil.SmoothStep(0.25, 0.45, s.EgrFraction);
                    misfireP = 1 - (1 - ignitionMiss) * (1 - leanMiss) * (1 - richMiss) * (1 - compMiss) * (1 - dilutionMiss);
                    if (fuelKg <= 1e-12)
                    {
                        misfireP = 1;
                    }
                }

                // Discrete combustion events
                double eventsPerStep = rpm / 120.0 * dt;
                _cylPhase[i] += eventsPerStep;
                int burnedEvents = 0;
                int events = 0;
                while (_cylPhase[i] >= 1.0)
                {
                    _cylPhase[i] -= 1.0;
                    events++;
                    bool miss = _rng.Chance(misfireP);
                    if (miss)
                    {
                        s.MisfireEvents[i]++;
                    }
                    else
                    {
                        burnedEvents++;
                    }
                }

                s.FiringEvents[i] = events;
                double instantBurn = events > 0 ? (double)burnedEvents / events : 1 - misfireP;
                s.CylinderBurn[i] = MathUtil.FirstOrder(s.CylinderBurn[i], instantBurn, 0.12, dt);
                if (rpm < 1)
                {
                    s.CylinderBurn[i] = 0;
                }

                double burn = s.CylinderBurn[i];
                avgRoughness += 1 - burn;

                // Knock
                double knock = 0;
                if (!_def.IsDiesel && burn > 0.2)
                {
                    knock = MathUtil.Clamp((spark - kla) / 6.0, 0, 1.5) * MathUtil.SmoothStep(0.25, 0.6, s.RelativeLoad) * (0.9 + 0.2 * _rng.NextDouble());
                }

                s.CylinderKnock[i] = knock;
                maxKnock = Math.Max(maxKnock, knock);

                // Energy
                double burntFuel = Math.Min(fuelKg, cylAir / afrStoich) * burn;
                totalBurntFuelKg += burntFuel;
                unburnt += fuelKg * (1 - burn);
                double eta = _def.IndicatedEfficiency * LambdaFactor(lambda) * gasQuality;
                if (_def.IsDiesel)
                {
                    eta = 0.43 * (1 - 0.0004 * Math.Pow(spark - 8, 2));
                }
                else
                {
                    double dev = spark - mbt;
                    eta *= MathUtil.Clamp(1 - 0.0006 * dev * dev, 0.2, 1);
                    eta *= 1 - knock * 0.05;
                }

                eta *= MathUtil.Clamp(0.55 + 0.45 * compression, 0, 1);
                indicatedWork += burntFuel * lhv * eta;

                totalAirForSensor += cylAir;
                totalFuelForSensor += fuelKg * burn;
            }

            // Torque
            double vd = _def.DisplacementM3;
            double indicatedTorque = indicatedWork / (4 * Math.PI);
            double kRpm = rpm / 1000.0;
            double fmepBar = (0.9 + 0.1 * kRpm + 0.025 * kRpm * kRpm) * _def.FrictionFactor * (1 + MathUtil.Clamp((80 - s.OilC) / 100.0, 0, 0.6));
            fmepBar += Damage.RodBearing * 0.4 + Damage.Piston.Length * 0;
            double frictionTorque = fmepBar * 1e5 * vd / (4 * Math.PI);
            double pumpingTorque = _def.IsDiesel ? 0 : Math.Max(0, (exhaustKpa - s.ManifoldKpa) * 1000 * vd / (4 * Math.PI)) * 0.55;
            double accessory = rpm > 1 ? (500 + (cmd.FanOn ? 300 : 0)) / Math.Max(80, rpm * 2 * Math.PI / 60) : 0;
            if (rpm < 1)
            {
                frictionTorque = 0;
                pumpingTorque = 0;
            }

            s.IndicatedTorqueNm = indicatedTorque;
            s.LossTorqueNm = frictionTorque + pumpingTorque + accessory;
            double brake = indicatedTorque - s.LossTorqueNm;
            if (s.Seized)
            {
                brake = -500;
            }

            s.TorqueNm = brake;
            s.FuelFlowGps = totalFuelG * rpm / 120.0;
            s.FuelPowerKw = totalBurntFuelKg * lhv * rpm / 120.0 / 1000.0;
            s.Roughness = MathUtil.Clamp01(avgRoughness / n * 2.5 + (s.Seized ? 1 : 0));
            s.PeakCylinderBar = mapBar * Math.Pow(cr, 1.3) * 2.6 * (1 + maxKnock * 0.5);
            s.Running = rpm > 350 && indicatedTorque > frictionTorque * 0.6 && !s.Seized;

            // Exhaust lambda seen by the upstream sensor (misfired O2 reads lean; exhaust leaks draw air)
            double lambdaExh = totalFuelForSensor > 1e-12 ? totalAirForSensor / (totalFuelForSensor * afrStoich) : (rpm > 1 ? 9 : 1);
            if (unburnt > 0 && totalFuelForSensor > 0)
            {
                lambdaExh = totalAirForSensor / ((totalFuelForSensor + unburnt * 0.15) * afrStoich);
            }

            double exhLeak = F(ComponentKind.Exhaust, EffectKind.Leak);
            if (exhLeak > 0 && rpm > 1)
            {
                lambdaExh *= 1 + exhLeak * 0.12 * MathUtil.Remap(rpm, 800, 3500, 1, 0.2);
            }

            // Transport delay from the exhaust valves to the sensor (gas travel + mixing): ~2 engine cycles plus pipe.
            _lambdaHistory[_historyIndex] = MathUtil.Clamp(lambdaExh, 0.5, 9);
            double delay = rpm > 50 ? 0.05 + 240.0 / rpm : 0.5;
            int lag = (int)MathUtil.Clamp(Math.Round(delay / dt), 0, _lambdaHistory.Length - 1);
            double delayed = _lambdaHistory[(_historyIndex - lag + _lambdaHistory.Length) % _lambdaHistory.Length];
            _historyIndex = (_historyIndex + 1) % _lambdaHistory.Length;
            s.ExhaustLambda = MathUtil.FirstOrder(s.ExhaustLambda, delayed, 0.06, dt);

            // ---------------- Thermal ----------------
            double fuelPowerW = s.FuelPowerKw * 1000;
            double qCoolant = fuelPowerW * MathUtil.Lerp(0.30, 0.22, MathUtil.Clamp01(s.RelativeLoad / 1.5));
            double retard = Math.Max(0, mbt - spark);
            qCoolant *= 1 + retard * 0.006;
            Component? thermostat = _parts.Find(ComponentKind.Thermostat);
            double open = MathUtil.SmoothStep(_def.ThermostatOpenC, _def.ThermostatOpenC + 12, s.CoolantC);
            if (thermostat != null)
            {
                if (_faults.Has(thermostat.Id, EffectKind.StuckOpen))
                {
                    open = Math.Max(open, MathUtil.Clamp(_faults.Max(thermostat.Id, EffectKind.StuckOpen), 0.4, 1));
                }
                else if (_faults.Has(thermostat.Id, EffectKind.StuckClosed))
                {
                    open = Math.Min(open, 0.04);
                }
            }

            double airflowFactor = 0.02 + vehicleSpeedMs / 22.0 + (cmd.FanOn ? 0.9 : 0);
            double coolantLevel = s.CoolantLevel;
            double radiatorW = _def.RadiatorKwK * 1000 * open * airflowFactor * (s.CoolantC - env.AmbientC) * coolantLevel;
            double radiatorRestriction = 0;
            radiatorW *= 1 - radiatorRestriction;
            double heaterW = 5 * (s.CoolantC - env.AmbientC);
            double heatCap = _def.ThermalCapacityKjK * 1000 * (0.4 + 0.6 * coolantLevel);
            if (s.Running || rpm > 1)
            {
                s.CoolantC += (qCoolant - radiatorW - heaterW) / heatCap * dt;
            }
            else
            {
                s.CoolantC = MathUtil.FirstOrder(s.CoolantC, env.AmbientC, 3600, dt);
            }

            // Head gasket leak consumes coolant; low coolant reduces heat removal → chain to overheating.
            if (Damage.HeadGasketBlown || Has(ComponentKind.HeadGasket, EffectKind.Leak))
            {
                double rate = (Damage.HeadGasketBlown ? 1 : F(ComponentKind.HeadGasket, EffectKind.Leak)) * 0.0015 * (s.Running ? 1 : 0);
                s.CoolantLevel = Math.Max(0.1, s.CoolantLevel - rate * dt);
                _coolantLossAccum += rate * dt;
            }

            s.OilC = MathUtil.FirstOrder(s.OilC, s.CoolantC + (s.Running ? 5 + s.RelativeLoad * 12 : 0), s.Running ? 240 : 3600, dt);
            s.OilPressureKpa = s.Running || rpm > 100 ? MathUtil.Clamp(80 + rpm * 0.08 - Math.Max(0, s.OilC - 90) * 1.2 - Damage.RodBearing * 120, 0, 550) : 0;

            double egtTarget = env.AmbientC;
            if (rpm > 1 && (totalBurntFuelKg > 0 || unburnt > 0))
            {
                double avgLambda = lambdaExh;
                if (_def.IsDiesel)
                {
                    // Diesel: overall lean; exhaust temperature follows fuelling (1/λ). Idle ~200 °C, full load ~750 °C.
                    egtTarget = 120 + 850 / Math.Max(1.05, avgLambda) * MathUtil.Remap(rpm, 800, 4000, 0.9, 1.05);
                }
                else
                {
                    double mixture = avgLambda < 1 ? -(1 - avgLambda) * 650 : 250 * (avgLambda - 1) * Math.Max(0, 1 - (avgLambda - 1) / 0.6);
                    egtTarget = 380 + 520 * MathUtil.Clamp01(s.RelativeLoad / (_def.IsTurbo ? 1.9 : 1.0)) * MathUtil.Remap(rpm, 800, 6000, 0.75, 1.1)
                        + retard * 9 + mixture;
                }
            }

            s.ExhaustGasC = MathUtil.FirstOrder(s.ExhaustGasC, egtTarget, s.Running ? 1.2 : 20, dt);

            // Catalyst: light-off, conversion and unburnt fuel oxidation (misfire chain)
            double unburntPowerW = unburnt * lhv * rpm / 120.0;
            double catHeat = s.ExhaustGasC - 60 + unburntPowerW / 30.0;
            s.CatalystC = MathUtil.FirstOrder(s.CatalystC, Math.Max(env.AmbientC, catHeat), 18, dt);
            Component? cat = _parts.Find(ComponentKind.Catalyst);
            double catHealth = 1;
            if (cat != null)
            {
                catHealth = cat.Health * (1 - _faults.Max(cat.Id, EffectKind.Wear));
            }

            catHealth *= 1 - Damage.Catalyst;
            double lightOff = MathUtil.SmoothStep(220, 330, s.CatalystC);
            s.CatalystEfficiency = MathUtil.Clamp01(catHealth * lightOff * 0.99);
            double storageCap = s.CatalystEfficiency;
            _catOxygenStorage = MathUtil.Clamp01(_catOxygenStorage + (s.ExhaustLambda - 1) * 4 * dt);
            double lambdaPost = 1 + (s.ExhaustLambda - 1) * (1 - storageCap);
            if (storageCap > 0.5 && (_catOxygenStorage <= 0.01 || _catOxygenStorage >= 0.99))
            {
                lambdaPost = 1 + (s.ExhaustLambda - 1) * 0.4;
            }

            s.PostCatLambda = MathUtil.FirstOrder(s.PostCatLambda, lambdaPost - 0.004 * storageCap, 0.6 * (0.2 + storageCap), dt);
            s.PostCatSwitchingRatio = MathUtil.Clamp01(1 - storageCap);

            // O2 sensor element temperatures (heater handled by car → passed as warm-up target)
            s.O2SensorC = MathUtil.FirstOrder(s.O2SensorC, Math.Max(s.ExhaustGasC * 0.9, O2HeaterTargetC), 12, dt);
            s.O2DownstreamC = MathUtil.FirstOrder(s.O2DownstreamC, Math.Max(s.CatalystC * 0.8, O2DownstreamHeaterTargetC), 20, dt);

            // Smoke
            double smoke = 0;
            if (_def.IsDiesel)
            {
                smoke = MathUtil.SmoothStep(1.35, 1.05, lambdaExh) * MathUtil.Clamp01(s.RelativeLoad);
            }
            else
            {
                smoke = MathUtil.SmoothStep(0.85, 0.7, lambdaExh) * 0.6;
            }

            s.SmokeOpacity = MathUtil.Clamp01(smoke);

            // ---------------- Damage ----------------
            UpdateDamage(dt, rpm, maxKnock);

            // Crank angle for waveforms
            s.CrankAngleDeg = (s.CrankAngleDeg + rpm * 6 * dt) % 720.0;
            return brake;
        }

        /// <summary>Upstream O2 heater-driven element target (set by car each step).</summary>
        public double O2HeaterTargetC { get; set; }

        /// <summary>Downstream O2 heater target.</summary>
        public double O2DownstreamHeaterTargetC { get; set; }

        /// <summary>Total coolant lost to combustion (fraction of system).</summary>
        public double CoolantConsumed => _coolantLossAccum;

        /// <summary>Indicated efficiency multiplier vs lambda (rich side gives a little power, lean burns slowly).</summary>
        public static double LambdaFactor(double lambda)
        {
            if (lambda < 1.0)
            {
                if (lambda >= 0.86)
                {
                    return 1 + 0.55 * (1 - lambda);
                }

                return Math.Max(0.3, 1.077 - 1.4 * (0.86 - lambda));
            }

            return Math.Max(0.2, 1 + 0.2 * (lambda - 1) - 2.2 * Math.Pow(Math.Max(0, lambda - 1.12), 2));
        }

        private void UpdateDamage(double dt, double rpm, double maxKnock)
        {
            EngineState s = State;
            EngineDamage d = Damage;
            for (int i = 0; i < _def.Cylinders; i++)
            {
                double k = s.CylinderKnock[i];
                if (k > 0.05)
                {
                    // Heavy detonation under load breaks ring lands in seconds; light knock takes minutes.
                    d.Piston[i] = Math.Min(1.2, d.Piston[i] + 0.09 * k * k * MathUtil.Clamp(s.RelativeLoad, 0.3, 2.5) * dt);
                }

                // Lean high load → melting
                if (!_def.IsDiesel && s.CylinderLambda[i] > 1.05 && s.RelativeLoad > 1.1 && s.CylinderBurn[i] > 0.5)
                {
                    d.Piston[i] = Math.Min(1.2, d.Piston[i] + 0.02 * (s.CylinderLambda[i] - 1.0) * 10 * dt);
                }

                if (s.ExhaustGasC > 980)
                {
                    d.ExhaustValve[i] = Math.Min(1.2, d.ExhaustValve[i] + (s.ExhaustGasC - 980) * 0.0004 * dt);
                }
            }

            if (rpm > _def.MechanicalLimitRpm)
            {
                d.RodBearing = Math.Min(1.2, d.RodBearing + (rpm - _def.MechanicalLimitRpm) / 1000.0 * 0.6 * dt);
            }

            if (maxKnock > 0.8)
            {
                d.RodBearing = Math.Min(1.2, d.RodBearing + (maxKnock - 0.8) * 0.01 * dt);
            }

            if (s.OilPressureKpa < 40 && rpm > 500)
            {
                d.RodBearing = Math.Min(1.2, d.RodBearing + 0.01 * dt);
            }

            if (s.CoolantC > 115)
            {
                d.HeadGasket = Math.Min(1.2, d.HeadGasket + (s.CoolantC - 115) * 0.0025 * dt);
            }

            if (s.PeakCylinderBar > 140)
            {
                d.HeadGasket = Math.Min(1.2, d.HeadGasket + (s.PeakCylinderBar - 140) * 0.0005 * dt);
            }

            if (_def.Turbo != null)
            {
                if (s.BoostKpa > _def.Turbo.MaxSafeBoostKpa)
                {
                    d.Turbo = Math.Min(1.2, d.Turbo + (s.BoostKpa - _def.Turbo.MaxSafeBoostKpa) / 20.0 * 0.04 * dt);
                }

                if (s.ExhaustGasC > 1000)
                {
                    d.Turbo = Math.Min(1.2, d.Turbo + (s.ExhaustGasC - 1000) * 0.0003 * dt);
                }
            }

            if (s.CatalystC > 900)
            {
                d.Catalyst = Math.Min(1.2, d.Catalyst + (s.CatalystC - 900) * 0.00012 * dt);
            }

            if (s.CylinderLambda.Length > 0)
            {
                double richest = 9;
                foreach (double l in s.CylinderLambda)
                {
                    richest = Math.Min(richest, l);
                }

                if (richest < 0.8 && rpm > 1)
                {
                    d.PlugFouling = Math.Min(1, d.PlugFouling + (0.8 - richest) * 0.0015 * dt);
                }
            }
        }

        /// <summary>Cranking compression pressure of a cylinder (bar gauge), as a compression tester reads.</summary>
        public double CrankingCompressionBar(int cylinder, double baroKpa)
        {
            double health = State.CylinderCompression[cylinder];
            double absBar = baroKpa / 100.0 * Math.Pow(_def.CompressionRatio, 1.22) * MathUtil.Clamp(health, 0, 1.05);
            return Math.Max(0, absBar - baroKpa / 100.0);
        }

        /// <summary>Forces state of the boost integrator (used by tests and save loading).</summary>
        internal void ResetDynamicState()
        {
            _boostState = 0;
            _mafFiltered = 0;
            _prevEngineAir = 0;
        }
    }
}
