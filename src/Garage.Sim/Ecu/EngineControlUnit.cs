using System;
using System.Collections.Generic;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Electrical;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Ecu
{
    /// <summary>Fuel system status as in PID 03.</summary>
    public enum FuelSystemStatus
    {
        /// <summary>Engine off.</summary>
        Off = 0,
        /// <summary>Open loop, conditions not yet met (cold).</summary>
        OpenLoopCold = 1,
        /// <summary>Closed loop using O2 sensor.</summary>
        ClosedLoop = 2,
        /// <summary>Open loop due to load / decel.</summary>
        OpenLoopLoad = 4,
        /// <summary>Open loop due to system fault.</summary>
        OpenLoopFault = 8,
        /// <summary>Closed loop with a fault in at least one O2 sensor.</summary>
        ClosedLoopFault = 16,
    }

    /// <summary>Raw ECU outputs before electrical realisation.</summary>
    public sealed class EcuOutputs
    {
        /// <summary>Creates outputs for n cylinders.</summary>
        public EcuOutputs(int n)
        {
            InjectorPulseMs = new double[n];
            FireCylinder = new bool[n];
        }

        /// <summary>Throttle target 0..1.</summary>
        public double ThrottleTarget { get; set; }

        /// <summary>Throttle motor enabled (false in limp: spring position).</summary>
        public bool ThrottleMotorEnabled { get; set; } = true;

        /// <summary>Pulse width per cylinder (ms).</summary>
        public double[] InjectorPulseMs { get; }

        /// <summary>Ignition coil drive enabled per cylinder.</summary>
        public bool[] FireCylinder { get; }

        /// <summary>Spark advance (deg BTDC).</summary>
        public double SparkAdvanceDeg { get; set; }

        /// <summary>Wastegate duty 0..1.</summary>
        public double WastegateDuty { get; set; }

        /// <summary>Fuel pump relay command.</summary>
        public bool FuelPumpRelay { get; set; }

        /// <summary>Fan relay command.</summary>
        public bool FanRelay { get; set; }

        /// <summary>Purge duty.</summary>
        public double PurgeDuty { get; set; }

        /// <summary>EGR command.</summary>
        public double EgrCommand { get; set; }

        /// <summary>O2 heaters on.</summary>
        public bool O2HeaterOn { get; set; }

        /// <summary>Glow plugs.</summary>
        public bool GlowOn { get; set; }

        /// <summary>Diesel quantity mg/stroke.</summary>
        public double DieselMg { get; set; }
    }

    /// <summary>
    /// Simulated engine control module: reads sensors through their circuits, applies realistic
    /// substitution strategies, runs closed loop fuel, idle, knock and boost control, and the OBD-II
    /// diagnostic executive (monitors, DTC status, freeze frame, readiness).
    /// </summary>
    public sealed partial class EngineControlUnit
    {
        private readonly Car _car;
        private readonly int _n;
        private readonly Dictionary<string, DebouncedTest> _tests = new Dictionary<string, DebouncedTest>();
        private readonly Dictionary<string, double> _live = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private double _stft;
        private double _ltftIdle;
        private double _ltftCruise;
        private double _idleIntegral;
        private double _boostIntegral;
        private double _knockRetard;
        private double _runTime;
        private double _keyOnTime;
        private double _closedLoopTime;
        private bool _wasRich;
        private int _o2Switches;
        private double _o2WindowTime;
        private double _o2Min = 9;
        private double _o2Max;
        private int _downSwitches;
        private bool _downWasRich;
        private int _upCrossings;
        private bool _upWasRich;
        private double _catTestTime;
        private double _revsInWindow;
        private readonly int[] _misfireWindow;
        private double _revsInShortWindow;
        private readonly int[] _misfireShort;
        private readonly bool[] _fuelCut;
        private double _ectAtStart = double.NaN;
        private double _maxEctThisTrip;
        private double _airIntegralG;
        private double _evapTime;
        private double _dither;
        private double _lastEctValid = 20;
        private double _limpTimer;
        private double _heaterCheckTime;
        private double _stallTimer;
        private bool _wasRunning;

        /// <summary>Creates the ECU for a car.</summary>
        public EngineControlUnit(Car car, EcuCalibration calibration, DtcCatalog catalog)
        {
            _car = car;
            _n = car.Definition.Engine.Cylinders;
            Calibration = calibration;
            Dtcs = new DtcStore(catalog);
            Catalog = catalog;
            Outputs = new EcuOutputs(_n);
            _misfireWindow = new int[_n];
            _misfireShort = new int[_n];
            _fuelCut = new bool[_n];
            MisfireCounts = new int[_n];
            var supported = new List<ReadinessMonitor> { ReadinessMonitor.Misfire, ReadinessMonitor.FuelSystem, ReadinessMonitor.Components };
            if (car.Definition.Engine.IsDiesel)
            {
                supported.Add(ReadinessMonitor.Egr);
                supported.Add(ReadinessMonitor.Catalyst);
            }
            else
            {
                supported.Add(ReadinessMonitor.Catalyst);
                supported.Add(ReadinessMonitor.Evap);
                if (car.Parts.Find(ComponentKind.O2Narrowband) != null || car.Parts.Find(ComponentKind.O2Wideband) != null)
                {
                    supported.Add(ReadinessMonitor.O2Sensor);
                    supported.Add(ReadinessMonitor.O2Heater);
                }
            }

            Readiness = new Readiness(supported);
        }

        /// <summary>Active calibration (editable).</summary>
        public EcuCalibration Calibration { get; set; }

        /// <summary>DTC memory.</summary>
        public DtcStore Dtcs { get; }

        /// <summary>DTC catalog.</summary>
        public DtcCatalog Catalog { get; }

        /// <summary>Readiness monitors.</summary>
        public Readiness Readiness { get; }

        /// <summary>Last outputs.</summary>
        public EcuOutputs Outputs { get; }

        /// <summary>Current fuel system status.</summary>
        public FuelSystemStatus FuelStatus { get; private set; }

        /// <summary>Short term fuel trim (fraction).</summary>
        public double Stft => _stft;

        /// <summary>Long term fuel trim of the active cell (fraction).</summary>
        public double Ltft => ActiveCellIdle ? _ltftIdle : _ltftCruise;

        /// <summary>Idle LTFT cell.</summary>
        public double LtftIdle => _ltftIdle;

        /// <summary>Cruise LTFT cell.</summary>
        public double LtftCruise => _ltftCruise;

        /// <summary>Knock retard currently applied (deg).</summary>
        public double KnockRetard => _knockRetard;

        /// <summary>Limp mode active.</summary>
        public bool LimpMode { get; private set; }

        /// <summary>Reason for limp mode.</summary>
        public string LimpReason { get; private set; } = "";

        /// <summary>Misfire counts per cylinder (current/last window, as mode 06 / live data).</summary>
        public int[] MisfireCounts { get; }

        /// <summary>Live values (believed by the ECU).</summary>
        public IReadOnlyDictionary<string, double> Live => _live;

        /// <summary>Believed rpm (from CKP).</summary>
        public double Rpm { get; private set; }

        /// <summary>Crank signal synchronised.</summary>
        public bool CrankSync { get; private set; }

        /// <summary>Cam signal valid.</summary>
        public bool CamSync { get; private set; }

        private bool ActiveCellIdle => Rpm < 1100 && _lastLoad < 0.35;

        private double _lastLoad;

        /// <summary>Freezes idle speed control (scan tool cylinder balance test).</summary>
        public bool FreezeIdle { get; set; }

        /// <summary>Sets fuel trims (save/load, tests).</summary>
        public void SetTrims(double stft, double ltftIdle, double ltftCruise)
        {
            _stft = stft;
            _ltftIdle = ltftIdle;
            _ltftCruise = ltftCruise;
        }

        /// <summary>Resets adaptations (as after battery disconnection or a scan tool reset).</summary>
        public void ResetAdaptations()
        {
            _stft = 0;
            _ltftIdle = 0;
            _ltftCruise = 0;
            _idleIntegral = 0;
            _knockRetard = 0;
        }

        private DebouncedTest Test(string code, double fail, double pass)
        {
            if (!_tests.TryGetValue(code, out DebouncedTest t))
            {
                t = new DebouncedTest(code, fail, pass);
                _tests[code] = t;
            }

            return t;
        }

        private void Check(string code, bool enabled, bool malfunction, double dt, double fail = 1.0, double pass = 2.0)
        {
            Test(code, fail, pass).Update(enabled, malfunction, dt, Dtcs, Snapshot);
        }

        /// <summary>Freeze frame from current live data.</summary>
        public FreezeFrame Snapshot()
        {
            var f = new FreezeFrame { Time = _car.Clock.SimSeconds };
            string[] keys = { "fuel_status", "load", "ect", "stft", "ltft", "map", "rpm", "speed", "timing", "iat", "maf", "tps", "battery_v" };
            foreach (string k in keys)
            {
                if (_live.TryGetValue(k, out double v))
                {
                    f.Values[k] = v;
                }
            }

            return f;
        }

        /// <summary>Called at key-on: resets per trip test state.</summary>
        public void OnKeyOn()
        {
            foreach (DebouncedTest t in _tests.Values)
            {
                t.ResetTrip();
            }

            _keyOnTime = 0;
            _runTime = 0;
            _ectAtStart = double.NaN;
            _maxEctThisTrip = -50;
            _closedLoopTime = 0;
            _airIntegralG = 0;
            LimpMode = false;
            LimpReason = "";
            _heaterCheckTime = 0;
        }

        /// <summary>Called at key-off: ends the OBD driving cycle.</summary>
        public void OnKeyOff()
        {
            bool warmup = !double.IsNaN(_ectAtStart) && _maxEctThisTrip >= 70 && _maxEctThisTrip - _ectAtStart >= 22;
            Dtcs.EndTrip(warmup);
        }

        /// <summary>Clears codes (mode 04) — also resets readiness and trims like a real ECU.</summary>
        public void ClearCodes()
        {
            Array.Clear(_fuelCut, 0, _n);
            Dtcs.Clear();
            Readiness.Reset();
            foreach (DebouncedTest t in _tests.Values)
            {
                t.ResetTrip();
            }

            Array.Clear(_misfireWindow, 0, _n);
            Array.Clear(_misfireShort, 0, _n);
            Array.Clear(MisfireCounts, 0, _n);
            _revsInWindow = 0;
            _revsInShortWindow = 0;
        }

        private double Volts(ComponentKind kind, string role = "signal")
        {
            Component? c = _car.Parts.Find(kind);
            if (c == null)
            {
                return double.NaN;
            }

            Circuit? circuit = _car.CircuitOf(c.Id);
            return circuit == null ? double.NaN : circuit.EcuPinVoltage(role);
        }

        private double GeneratorVolts(ComponentKind kind)
        {
            Component? c = _car.Parts.Find(kind);
            Circuit? circuit = c == null ? null : _car.CircuitOf(c.Id);
            return circuit == null ? double.NaN : circuit.EcuPinVoltage("signal") - circuit.EcuPinVoltage("signal_low");
        }

        private static bool Missing(double v) => double.IsNaN(v);

        /// <summary>One control step.</summary>
        public void Update(double dt)
        {
            CarDefinition def = _car.Definition;
            EngineDefinition eng = def.Engine;
            EcuCalibration cal = Calibration;
            bool keyOn = _car.KeyOn;
            if (!keyOn)
            {
                Outputs.FuelPumpRelay = false;
                Outputs.FanRelay = false;
                Outputs.O2HeaterOn = false;
                Array.Clear(Outputs.InjectorPulseMs, 0, _n);
                for (int i = 0; i < _n; i++)
                {
                    Outputs.FireCylinder[i] = false;
                }

                FuelStatus = FuelSystemStatus.Off;
                return;
            }

            _keyOnTime += dt;
            Dtcs.TimeSinceClearS += dt;
            double vbat = _car.BatteryVolts;
            bool voltageOk = vbat > 10.5;
            double trueRpm = _car.Engine.State.Rpm;

            // ---------------- Crank / cam ----------------
            double ckpAmp = GeneratorVolts(ComponentKind.CkpSensor);
            double expectedAmp = SensorCurves.CkpPeakVoltage(trueRpm);
            CrankSync = trueRpm > 30 && !Missing(ckpAmp) && ckpAmp > 0.12 && ckpAmp > expectedAmp * 0.25;
            Rpm = CrankSync ? trueRpm : 0;
            bool engineTurning = trueRpm > 60;
            Check("P0335", engineTurning && _car.Cranking || (engineTurning && _runTime > 1), !CrankSync, dt, 1.0, 2.0);
            double rpm = Rpm;
            // Run detection from crank speed only (as an ECU does): hysteresis between cranking and running.
            bool running = rpm > (_wasRunning ? 300 : 450);
            _wasRunning = running;
            if (running)
            {
                _runTime += dt;
                _stallTimer = 0;
            }
            else
            {
                _stallTimer += dt;
                if (_stallTimer > 1.0)
                {
                    _runTime = 0;
                }
            }

            Component? cmp = _car.Parts.Find(ComponentKind.CmpSensor);
            if (cmp != null)
            {
                Circuit c = _car.CircuitOf(cmp.Id)!;
                double hi = _car.HallLevels(cmp.Id, out double lo);
                CamSync = hi - lo > 2.0;
                Check("P0340", CrankSync && trueRpm > 100, !CamSync, dt, 2.0, 3.0);
                double camOffset = _car.Engine.State.CamOffsetDeg;
                Check("P0016", CamSync && running && _runTime > 5, Math.Abs(camOffset) > 6, dt, 5, 10);
                _live["cam_offset_deg"] = CamSync ? camOffset : 0;
                _ = c;
            }
            else
            {
                CamSync = true;
            }

            // ---------------- Temperatures ----------------
            double ectV = Volts(ComponentKind.EctSensor);
            bool ectLow = ectV < 0.08;
            bool ectHigh = ectV > 4.92;
            double ect = Missing(ectV) ? 90 : SensorCurves.NtcTemperature(SensorCurves.NtcOhmsFromVoltage(ectV));
            Check("P0117", keyOn && _keyOnTime > 0.5, ectLow, dt, 1, 2);
            Check("P0118", keyOn && _keyOnTime > 0.5, ectHigh, dt, 1, 2);
            bool ectValid = !ectLow && !ectHigh;
            if (ectValid)
            {
                _lastEctValid = ect;
            }
            else
            {
                ect = Math.Min(90, _lastEctValid + _runTime * 0.1);
            }

            if (double.IsNaN(_ectAtStart) && _keyOnTime > 0.2)
            {
                _ectAtStart = ect;
            }

            _maxEctThisTrip = Math.Max(_maxEctThisTrip, ect);

            double iatV = Volts(ComponentKind.IatSensor);
            bool iatLow = iatV < 0.08;
            bool iatHigh = iatV > 4.92;
            double iat = Missing(iatV) ? 25 : SensorCurves.NtcTemperature(SensorCurves.NtcOhmsFromVoltage(iatV));
            Check("P0112", _keyOnTime > 0.5, iatLow, dt, 1, 2);
            Check("P0113", _keyOnTime > 0.5, iatHigh, dt, 1, 2);
            if (iatLow || iatHigh)
            {
                iat = 25;
            }

            // ---------------- Pedal and throttle ----------------
            double appV = Volts(ComponentKind.AppSensor);
            bool appLow = !Missing(appV) && appV < 0.25;
            bool appHigh = !Missing(appV) && appV > 4.7;
            double pedal = Missing(appV) ? _car.Pedal : MathUtil.Clamp01((appV - 0.5) / 4.0);
            Check("P2122", _keyOnTime > 0.3, appLow, dt, 0.5, 1);
            Check("P2123", _keyOnTime > 0.3, appHigh, dt, 0.5, 1);
            if (appLow || appHigh)
            {
                pedal = 0;
                EnterLimp("APP");
            }

            double tpsV = Volts(ComponentKind.TpsSensor);
            bool tpsLow = !Missing(tpsV) && tpsV < 0.25;
            bool tpsHigh = !Missing(tpsV) && tpsV > 4.7;
            double tps = Missing(tpsV) ? _car.Engine.State.ThrottlePosition : MathUtil.Clamp01((tpsV - 0.5) / 4.0);
            Check("P0122", _keyOnTime > 0.3, tpsLow, dt, 0.5, 1);
            Check("P0123", _keyOnTime > 0.3, tpsHigh, dt, 0.5, 1);
            bool tpsFault = tpsLow || tpsHigh;

            // ---------------- Pressures ----------------
            double baro = _live.TryGetValue("baro", out double b) ? b : 101.3;
            Component? mapSensor = _car.Parts.Find(ComponentKind.MapSensor);
            double mapMax = mapSensor?.Param("range_max_kpa", eng.IsTurbo ? 250 : 105) ?? 105;
            double mapMin = mapSensor?.Param("range_min_kpa", 10) ?? 10;
            double mapV = Volts(ComponentKind.MapSensor);
            bool mapLow = !Missing(mapV) && mapV < 0.25;
            bool mapHigh = !Missing(mapV) && mapV > 4.7;
            double map = Missing(mapV) ? _car.Engine.State.ManifoldKpa : SensorCurves.LinearValue(mapV, mapMin, mapMax);
            Check("P0107", _keyOnTime > 0.5, mapLow, dt, 1, 2);
            Check("P0108", _keyOnTime > 0.5, mapHigh, dt, 1, 2);
            bool mapValid = !mapLow && !mapHigh;
            if (!running && rpm < 1 && _keyOnTime > 0.3 && mapValid)
            {
                baro = map;
            }

            double boostMeasured = map - baro;
            Component? boostSensor = _car.Parts.Find(ComponentKind.BoostSensor);
            bool boostValid = true;
            if (boostSensor != null)
            {
                double bv = Volts(ComponentKind.BoostSensor);
                double bmax = boostSensor.Param("range_max_kpa", 300);
                bool bl = bv < 0.25;
                bool bh = bv > 4.7;
                Check("P0237", _keyOnTime > 0.5, bl, dt, 1, 2);
                Check("P0238", _keyOnTime > 0.5, bh, dt, 1, 2);
                boostValid = !bl && !bh;
                if (boostValid)
                {
                    boostMeasured = SensorCurves.LinearValue(bv, 20, bmax) - baro;
                }
            }

            if (!mapValid)
            {
                double ve = cal.Lookup(EcuCalibration.VeEstimate, rpm, 60, 0.8);
                map = MathUtil.Lerp(30, baro, tps) + 0 * ve;
            }

            // ---------------- MAF / air mass ----------------
            double mafMax = cal.Scalar(EcuCalibration.Keys.MafMaxFlow, 150);
            double mafV = Volts(ComponentKind.MafSensor);
            bool hasMaf = !Missing(mafV);
            bool mafLow = hasMaf && mafV < 0.3 && _keyOnTime > 1;
            bool mafHigh = hasMaf && mafV > 4.95;
            double maf = hasMaf ? SensorCurves.MafFlow(mafV, mafMax) : 0;
            Check("P0102", keyOn && (running || _keyOnTime > 1), mafLow, dt, 1, 2);
            Check("P0103", keyOn, mafHigh, dt, 1, 2);
            double sdAir = SpeedDensityGps(map, iat, rpm);
            bool mafValid = hasMaf && !mafLow && !mafHigh;
            if (hasMaf)
            {
                bool mafRationality = running && _runTime > 10 && rpm > 1500 && mapValid && Math.Abs(maf - sdAir) > 0.45 * Math.Max(sdAir, 3);
                Check("P0101", running && _runTime > 10 && rpm > 1500 && mafValid, mafRationality, dt, 8, 10);
            }

            double airGps = mafValid ? maf : sdAir;
            double airPerCylG = rpm > 50 ? airGps * 120.0 / (rpm * _n) : 0;
            double rhoStd = Physics.AirDensity(101.325, 25);
            double load = airPerCylG / 1000.0 / (rhoStd * eng.DisplacementM3 / _n);
            _lastLoad = load;
            if (running)
            {
                _airIntegralG += airGps * dt;
            }

            // ---------------- Rail pressure ----------------
            Component? railSensor = _car.Parts.Find(ComponentKind.FuelPressureSensor);
            double railTarget = cal.Scalar(EcuCalibration.Keys.RailTarget, eng.RailPressureKpa);
            double rail = _car.Engine.State.FuelRailKpa;
            bool railValid = false;
            if (railSensor != null)
            {
                double rv = Volts(ComponentKind.FuelPressureSensor);
                double rmax = railSensor.Param("range_max_kpa", eng.IsDiesel ? 200000 : 1000);
                bool rl = rv < 0.25;
                bool rh = rv > 4.7;
                Check("P0192", _keyOnTime > 0.5, rl, dt, 1, 2);
                Check("P0193", _keyOnTime > 0.5, rh, dt, 1, 2);
                railValid = !rl && !rh;
                rail = railValid ? SensorCurves.LinearValue(rv, 0, rmax) : railTarget;
                if (eng.IsDiesel)
                {
                    railTarget = eng.RailPressureKpa * MathUtil.Remap(rpm, 800, 3500, 0.35, 1.0);
                }

                Check("P0087", railValid && running && _runTime > 3, rail < railTarget * 0.75, dt, 3, 5);
                Check("P0088", railValid && running && _runTime > 3, rail > railTarget * 1.25, dt, 3, 5);
            }

            // ---------------- Battery ----------------
            Check("P0562", keyOn && running && _runTime > 5, vbat < 11.5, dt, 10, 5);
            Check("P0563", keyOn && running, vbat > 16.0, dt, 5, 5);

            // ---------------- Speed ----------------
            double speed = _car.Can.EcmSeesAbs ? _car.VehicleSpeedKmh : 0;

            // ---------------- Throttle and idle control ----------------
            double idleTarget = MathUtil.Remap(ect, 0, 70, cal.Scalar(EcuCalibration.Keys.IdleColdRpm, 1100), cal.Scalar(EcuCalibration.Keys.IdleRpm, 800));
            double pedalThrottle = cal.Lookup(EcuCalibration.PedalToThrottle, pedal * 100, pedal * 100) / 100.0;
            double throttle = pedalThrottle;
            bool idling = pedal < 0.02 && !eng.IsDiesel;
            double idleBase = eng.IsTurbo ? 0.028 : 0.035;
            if (idling)
            {
                double err = idleTarget - rpm;
                if (FreezeIdle)
                {
                    err = 0;
                }

                if (running)
                {
                    _idleIntegral = MathUtil.Clamp(_idleIntegral + err * 0.00002 * dt, -0.03, 0.12);
                }

                throttle = Math.Max(0.003, idleBase + _idleIntegral + MathUtil.Clamp(err * 0.00003, -0.015, 0.05));
                if (!running)
                {
                    throttle = idleBase + 0.01 + Math.Max(0, _idleIntegral);
                }

                Check("P0506", running && _runTime > 20 && ect > 70 && speed < 1, rpm < idleTarget - 100, dt, 10, 10);
                Check("P0507", running && _runTime > 20 && ect > 70 && speed < 1, rpm > idleTarget + 200, dt, 10, 10);
            }
            else
            {
                throttle = Math.Max(throttle, idleBase + Math.Max(0, _idleIntegral));
            }

            // Electronic throttle supervision: command vs TPS
            bool hasEtc = !eng.IsDiesel && _car.Parts.Find(ComponentKind.ElectronicThrottle) != null;
            bool throttleMotorOk = !hasEtc || _car.ActuatorEnergized(ComponentKind.ElectronicThrottle);
            double thrErr = Math.Abs(tps - Outputs.ThrottleTarget);
            Check("P2101", hasEtc && _keyOnTime > 1 && !tpsFault && Outputs.ThrottleMotorEnabled, thrErr > 0.12, dt, 0.6, 1);
            Check("P2100", hasEtc && _keyOnTime > 1, !throttleMotorOk, dt, 0.3, 1);
            if (hasEtc && (tpsFault || HasActive("P2101") || !throttleMotorOk))
            {
                EnterLimp("ETC");
            }

            // ---------------- Limp mode handling ----------------
            if (LimpMode)
            {
                _limpTimer += dt;
                throttle = Math.Min(throttle, 0.12);
            }

            bool motorEnabled = !(LimpMode && LimpReason == "ETC");
            Outputs.ThrottleMotorEnabled = motorEnabled;
            Outputs.ThrottleTarget = MathUtil.Clamp01(throttle);

            // ---------------- O2 sensors ----------------
            Component? wb = _car.Parts.Find(ComponentKind.O2Wideband);
            Component? nb = _car.Parts.Find(ComponentKind.O2Narrowband);
            Component? down = _car.Parts.Find(ComponentKind.O2Downstream);
            double lambdaMeas = 1;
            bool o2Ready = false;
            bool o2Fault = false;
            double o2V = double.NaN;
            Outputs.O2HeaterOn = running || _keyOnTime > 2;
            if (wb != null)
            {
                o2V = Volts(ComponentKind.O2Wideband);
                lambdaMeas = SensorCurves.WidebandLambdaFromVoltage(o2V);
                o2Ready = _car.Engine.State.O2SensorC > 650 && _runTime > 8;
                bool wbLow = o2V < 0.3;
                bool wbHigh = o2V > 4.5;
                Check("P0131", o2Ready, wbLow, dt, 3, 5);
                Check("P0132", o2Ready, wbHigh, dt, 3, 5);
                o2Fault = wbLow || wbHigh || HasActive("P0135") || HasActive("P0130");
                bool stuck = Math.Abs(o2V - 1.5) < 0.02 && Math.Abs(_stft) > 0.15;
                Check("P0130", o2Ready && FuelStatus == FuelSystemStatus.ClosedLoop && _closedLoopTime > 10, stuck, dt, 8, 10);
            }
            else if (nb != null)
            {
                o2V = Volts(ComponentKind.O2Narrowband);
                o2Ready = _runTime > 8 && (_o2Max - _o2Min) > 0.3 || (_car.Engine.State.O2SensorC > 380 && _runTime > 15);
                lambdaMeas = o2V > 0.45 ? 0.98 : 1.02;
                Check("P0132", running && _runTime > 10, o2V > 1.15, dt, 3, 5);
                o2Fault = o2V > 1.15 || HasActive("P0135") || HasActive("P0134") || HasActive("P0131");
            }

            if (wb != null || nb != null)
            {
                string id = (wb ?? nb)!.Id;
                double heaterAmps = _car.HeaterCurrent(id);
                _heaterCheckTime += dt;
                Check("P0135", Outputs.O2HeaterOn && _heaterCheckTime > 2, heaterAmps < 0.3, dt, 2, 3);
                if (_heaterCheckTime > 5)
                {
                    Readiness.SetComplete(ReadinessMonitor.O2Heater);
                }
            }

            if (down != null)
            {
                double dv = Volts(ComponentKind.O2Downstream);
                _live["o2_b1s2_v"] = dv;
                double heaterAmps = _car.HeaterCurrent(down.Id);
                Check("P0141", Outputs.O2HeaterOn && _heaterCheckTime > 2, heaterAmps < 0.3, dt, 2, 3);
                Check("P0138", running && _runTime > 20, dv > 1.15, dt, 3, 5);
                bool downRich = dv > 0.5;
                if (downRich != _downWasRich && Math.Abs(dv - 0.45) > 0.1)
                {
                    _downSwitches++;
                    _downWasRich = downRich;
                }

                Check("P0137", running && _closedLoopTime > 60 && _car.Engine.State.O2DownstreamC > 400, dv < 0.06, dt, 20, 30);
            }

            // ---------------- Fueling ----------------
            double lambdaTarget = cal.Lookup(EcuCalibration.LambdaTarget, rpm, load, 1.0);
            bool powerEnrich = lambdaTarget < 0.98;
            bool decelCut = !eng.IsDiesel && pedal < 0.01 && rpm > idleTarget + 900 && ect > 50 && running && !LimpMode;
            double revLimit = cal.Scalar(EcuCalibration.Keys.RevLimit, eng.RedlineRpm);
            bool revCut = rpm > revLimit;
            double speedLimit = cal.Scalar(EcuCalibration.Keys.SpeedLimit, 250);
            bool speedCut = speed > speedLimit;
            bool limpRevCut = LimpMode && rpm > 3000;

            bool closedLoopConditions = running && ect > 40 && o2Ready && !o2Fault && !powerEnrich && !decelCut && _runTime > 5 && !eng.IsDiesel;
            if (!running)
            {
                FuelStatus = rpm > 0 ? FuelSystemStatus.OpenLoopCold : FuelSystemStatus.Off;
            }
            else if (closedLoopConditions)
            {
                FuelStatus = FuelSystemStatus.ClosedLoop;
            }
            else if (o2Fault && running && ect > 40)
            {
                FuelStatus = FuelSystemStatus.OpenLoopFault;
            }
            else if (powerEnrich || decelCut)
            {
                FuelStatus = FuelSystemStatus.OpenLoopLoad;
            }
            else
            {
                FuelStatus = FuelSystemStatus.OpenLoopCold;
            }

            double ltft = ActiveCellIdle ? _ltftIdle : _ltftCruise;
            if (FuelStatus == FuelSystemStatus.ClosedLoop)
            {
                _closedLoopTime += dt;
                _dither += dt;
                double ditherTarget = lambdaTarget * (1 + 0.02 * Math.Sin(2 * Math.PI * 1.0 * _dither));
                if (wb != null)
                {
                    double err = lambdaMeas - ditherTarget;
                    _stft = MathUtil.Clamp(_stft + 0.9 * err * dt, -0.25, 0.25);
                }
                else
                {
                    bool rich = o2V > 0.45;
                    if (rich != _wasRich)
                    {
                        _stft += rich ? -0.015 : 0.015;
                        _wasRich = rich;
                    }

                    _stft = MathUtil.Clamp(_stft + (rich ? -0.06 : 0.06) * dt, -0.25, 0.25);
                }

                // Long term learning: transfer the average STFT into the active LTFT cell.
                if (ect > 65)
                {
                    double transfer = _stft * dt / 12.0;
                    double before = ltft;
                    ltft = MathUtil.Clamp(ltft + transfer, -0.25, 0.25);
                    _stft -= ltft - before;
                    if (ActiveCellIdle)
                    {
                        _ltftIdle = ltft;
                    }
                    else
                    {
                        _ltftCruise = ltft;
                    }
                }

                // O2 activity tracking
                _o2WindowTime += dt;
                if (!Missing(o2V))
                {
                    _o2Min = Math.Min(_o2Min, o2V);
                    _o2Max = Math.Max(_o2Max, o2V);
                }

                bool upRich = wb != null ? lambdaMeas < lambdaTarget : o2V > 0.45;
                if (upRich != _upWasRich)
                {
                    _upCrossings++;
                    _o2Switches++;
                    _upWasRich = upRich;
                }

                if (_o2WindowTime > 20)
                {
                    bool slow = nb != null && _o2Switches < 6 && (_o2Max - _o2Min) > 0.5;
                    bool noActivity = nb != null && (_o2Max - _o2Min) < 0.15;
                    Check("P0133", true, slow, _o2WindowTime, 20, 20);
                    Check("P0134", true, noActivity, _o2WindowTime, 20, 20);
                    Readiness.SetComplete(ReadinessMonitor.O2Sensor);
                    _o2WindowTime = 0;
                    _o2Switches = 0;
                    _o2Min = 9;
                    _o2Max = 0;
                }
            }
            else
            {
                _stft = MathUtil.FirstOrder(_stft, 0, 1.0, dt);
                if (nb != null && running && ect > 40 && _runTime > 60 && !powerEnrich && !decelCut)
                {
                    // Stuck sensor never lets the loop close: count as no activity.
                    double v = Missing(o2V) ? 0.45 : o2V;
                    Check("P0134", true, Math.Abs(v - 0.45) < 0.08, dt, 30, 30);
                    Check("P0131", true, v < 0.06, dt, 20, 30);
                }
            }

            double totalTrim = _stft + ltft;
            bool fuelMonitor = FuelStatus == FuelSystemStatus.ClosedLoop && _closedLoopTime > 5;
            Check("P0171", fuelMonitor, totalTrim > 0.25 || (ltft >= 0.245 && _stft > 0.08), dt, 5, 20);
            Check("P0172", fuelMonitor, totalTrim < -0.25 || (ltft <= -0.245 && _stft < -0.08), dt, 5, 20);
            if (fuelMonitor && _closedLoopTime > 30)
            {
                Readiness.SetComplete(ReadinessMonitor.FuelSystem);
            }

            double warmup = cal.Lookup(EcuCalibration.EctFuelCorrection, ect, 1.0);
            double fuelG;
            if (!running && rpm > 30)
            {
                // Cranking: speed-density estimate, rich mixture depending on temperature.
                double crankAir = SpeedDensityGps(mapValid ? map : baro, iat, Math.Max(rpm, 150)) * 120.0 / (Math.Max(rpm, 150) * _n);
                fuelG = crankAir / (Physics.AfrGasoline * 0.75) * warmup;
            }
            else
            {
                double corr = FuelStatus == FuelSystemStatus.ClosedLoop ? (1 + _stft + ltft) : (1 + ltft);
                double cl = FuelStatus == FuelSystemStatus.ClosedLoop ? 1.0 : warmup;
                fuelG = airPerCylG / (Physics.AfrGasoline * lambdaTarget) * corr * cl;
            }

            double flowGms = cal.Scalar(EcuCalibration.Keys.InjectorFlow, eng.InjectorFlowCcMin) * Physics.GasolineDensity / 60000.0;
            double railForComp = railSensor != null ? Math.Max(50, rail) : eng.InjectorRefPressureKpa;
            flowGms *= Math.Sqrt(railForComp / eng.InjectorRefPressureKpa);
            double dead = cal.Scalar(EcuCalibration.Keys.InjectorDeadTime, 0.5) * Math.Pow(14.0 / Math.Max(8, vbat), 1.3);
            double pw = fuelG > 0 ? fuelG / flowGms + dead : 0;
            bool cut = decelCut || revCut || speedCut || limpRevCut || !CrankSync || !voltageOk && rpm < 30;
            if (eng.IsDiesel)
            {
                double q = cal.Lookup(EcuCalibration.DieselQuantity, rpm, pedal * 100, 10);
                if (idling || pedal < 0.02)
                {
                    double err = idleTarget - rpm;
                    _idleIntegral = MathUtil.Clamp(_idleIntegral + err * 0.004 * dt, -3, 6);
                    q = 6 + _idleIntegral + MathUtil.Clamp(err * 0.01, -3, 6);
                }

                if (!running)
                {
                    q = 30;
                }

                double smokeLimit = airPerCylG * 1000 / (Physics.AfrDiesel * cal.Scalar("smoke_limit_lambda", 1.15));
                if (running)
                {
                    q = Math.Min(q, Math.Max(4, smokeLimit));
                }

                Outputs.DieselMg = cut ? 0 : Math.Max(0, q);
                Outputs.GlowOn = ect < 60 && _keyOnTime < 30;
                pw = 0;
            }

            for (int i = 0; i < _n; i++)
            {
                Outputs.InjectorPulseMs[i] = cut ? 0 : pw;
                Outputs.FireCylinder[i] = CrankSync && !eng.IsDiesel;
            }

            // ---------------- Ignition ----------------
            double adv = cal.Lookup(EcuCalibration.IgnitionAdvance, rpm, load, 10);
            adv += cal.Lookup(EcuCalibration.IatIgnitionCorrection, iat, 0);
            adv += cal.Lookup(EcuCalibration.EctIgnitionCorrection, ect, 0);
            if (idling && running && !FreezeIdle)
            {
                adv += MathUtil.Clamp((idleTarget - rpm) * 0.02, -5, 6);
            }

            if (!running)
            {
                adv = 8;
            }

            // Knock control
            Component? knock = _car.Parts.Find(ComponentKind.KnockSensor);
            bool knockSensorOk = true;
            if (knock != null && !eng.IsDiesel)
            {
                double amp = Math.Abs(GeneratorVolts(ComponentKind.KnockSensor)) * 1000;
                double background = SensorCurves.KnockSensorMillivolts(rpm, 0);
                bool low = rpm > 1500 && amp < background * 0.3;
                bool high = amp > 4000;
                Check("P0327", running && rpm > 1500, low, dt, 2, 3);
                Check("P0328", running, high, dt, 2, 3);
                Check("P0325", running && rpm > 1500, low || high, dt, 2.5, 3);
                knockSensorOk = !low && !high;
                bool knockEvent = knockSensorOk && rpm > 1200 && amp > background * 1.6;
                if (knockEvent)
                {
                    _knockRetard = Math.Min(cal.Scalar(EcuCalibration.Keys.KnockMax, 10), _knockRetard + cal.Scalar(EcuCalibration.Keys.KnockStep, 1.5) * dt * 10);
                }
                else
                {
                    _knockRetard = Math.Max(0, _knockRetard - 0.8 * dt);
                }

                _live["knock_mv"] = amp;
            }

            if (!knockSensorOk)
            {
                adv -= 6;
            }

            if (LimpMode)
            {
                adv -= 4;
            }

            adv -= _knockRetard;
            if (eng.IsDiesel)
            {
                adv = cal.Lookup(EcuCalibration.IgnitionAdvance, rpm, load, 6);
            }

            Outputs.SparkAdvanceDeg = adv;

            // ---------------- Boost control ----------------
            double boostTarget = 0;
            if (eng.IsTurbo)
            {
                boostTarget = cal.Lookup(EcuCalibration.BoostTarget, rpm, pedal * 100, 0);
                double torqueLimit = cal.Scalar(EcuCalibration.Keys.TorqueLimit, 9999);
                if (_car.Engine.State.TorqueNm > torqueLimit)
                {
                    boostTarget = Math.Max(0, boostTarget - (_car.Engine.State.TorqueNm - torqueLimit) * 0.5);
                }

                double baseDuty = cal.Lookup(EcuCalibration.WastegateDuty, rpm, pedal * 100, 0) / 100.0;
                double err = boostTarget - boostMeasured;
                bool controlling = boostTarget > 20 && pedal > 0.3 && running;
                if (controlling)
                {
                    _boostIntegral = MathUtil.Clamp(_boostIntegral + err * 0.004 * dt, -0.3, 0.3);
                }
                else
                {
                    _boostIntegral = MathUtil.FirstOrder(_boostIntegral, 0, 2, dt);
                }

                double duty = MathUtil.Clamp01(baseDuty + _boostIntegral + err * 0.004);
                double overboost = cal.Scalar(EcuCalibration.Keys.OverboostLimit, (eng.Turbo?.MaxSafeBoostKpa ?? 170) - 10);
                Check("P0234", running && boostValid, boostMeasured > overboost || (controlling && boostMeasured > boostTarget + 35), dt, 0.5, 3);
                Check("P0299", running && boostValid && controlling && boostTarget > 50 && rpm > 2500, boostMeasured < boostTarget - 30, dt, 4, 4);
                if (HasActive("P0234"))
                {
                    EnterLimp("OVERBOOST");
                }

                if (LimpMode)
                {
                    duty = 0;
                    boostTarget = 0;
                }

                Outputs.WastegateDuty = duty;
            }

            // ---------------- Fuel pump, fan, purge, EGR ----------------
            Outputs.FuelPumpRelay = _keyOnTime < 2.0 || (CrankSync && rpm > 30);
            double fanOn = cal.Scalar(EcuCalibration.Keys.FanOn, 102);
            double fanOff = cal.Scalar(EcuCalibration.Keys.FanOff, 97);
            if (!ectValid)
            {
                Outputs.FanRelay = true;
            }
            else if (ect > fanOn)
            {
                Outputs.FanRelay = true;
            }
            else if (ect < fanOff)
            {
                Outputs.FanRelay = false;
            }

            bool purgeOk = FuelStatus == FuelSystemStatus.ClosedLoop && ect > 70 && _closedLoopTime > 20;
            Outputs.PurgeDuty = purgeOk ? 0.25 : 0;
            if (purgeOk)
            {
                _evapTime += dt;
                if (_evapTime > 60)
                {
                    Component? purge = _car.Parts.Find(ComponentKind.PurgeValve);
                    bool stuck = purge != null && _car.Faults.Has(purge.Id, Faults.EffectKind.StuckOpen);
                    Check("P0441", true, stuck, 60, 1, 1);
                    Readiness.SetComplete(ReadinessMonitor.Evap);
                    _evapTime = 0;
                }
            }

            Outputs.EgrCommand = eng.IsDiesel && running && load < 0.6 && rpm < 3000 && ect > 60 ? 0.35 : 0;
            if (eng.IsDiesel && running && _runTime > 30)
            {
                Readiness.SetComplete(ReadinessMonitor.Egr);
            }

            // ---------------- Coolant / thermostat ----------------
            bool thermostatTest = running && ectValid && _airIntegralG > 4500 * eng.DisplacementL && _car.Environment.AmbientC > -7;
            Check("P0128", thermostatTest, ect < 75, dt, 5, 30);
            Check("P0217", running && ectValid, ect > 120, dt, 5, 10);
            Check("P0116", running && ectValid && _runTime > 1200, ect < 30, dt, 30, 60);

            // ---------------- Misfire monitor ----------------
            UpdateMisfire(dt, running && CrankSync && !decelCut && !revCut, rpm);

            // ---------------- Catalyst monitor ----------------
            if (FuelStatus == FuelSystemStatus.ClosedLoop && ect > 75 && rpm > 1300 && rpm < 3500 && _closedLoopTime > 30 && _car.Engine.State.CatalystC > 400 && down != null)
            {
                _catTestTime += dt;
                if (_catTestTime > 25)
                {
                    double ratio = _upCrossings > 0 ? (double)_downSwitches / _upCrossings : 0;
                    Check("P0420", true, ratio > 0.55, 25, 1, 1);
                    Readiness.SetComplete(ReadinessMonitor.Catalyst);
                    _live["cat_switch_ratio"] = ratio;
                    _catTestTime = 0;
                    _downSwitches = 0;
                    _upCrossings = 0;
                }
            }

            // ---------------- Actuator circuit monitors ----------------
            if (_keyOnTime > 0.5 && voltageOk)
            {
                for (int i = 0; i < _n; i++)
                {
                    Component? inj = _car.Parts.Find(ComponentKind.Injector, i);
                    if (inj != null)
                    {
                        CircuitCheck(inj.Id, $"P02{i + 1:00}", LowCode(i), HighCode(i), dt, running);
                    }

                    Component? coil = _car.Parts.Find(ComponentKind.IgnitionCoil, i);
                    if (coil != null)
                    {
                        CircuitCheck(coil.Id, $"P035{i + 1}", $"P035{i + 1}", $"P035{i + 1}", dt, running);
                    }
                }

                SimpleCircuitCheck(ComponentKind.WastegateSolenoid, "P0243", "P0245", "P0246", dt);
                SimpleCircuitCheck(ComponentKind.PurgeValve, "P0443", "P0458", "P0459", dt);
                if (_car.FanRelayId.Length > 0)
                {
                    CircuitCheck(_car.FanRelayId, "P0480", "P0480", "P0480", dt, true);
                }

                if (_car.PumpRelayId.Length > 0)
                {
                    CircuitCheck(_car.PumpRelayId, "P0230", "P0231", "P0232", dt, true);
                }
            }

            if (_runTime > 10)
            {
                Readiness.SetComplete(ReadinessMonitor.Components);
            }

            if (Dtcs.MilOn)
            {
                Dtcs.DistanceWithMilKm += speed / 3600.0 * dt;
            }

            // ---------------- Live data ----------------
            _live["rpm"] = rpm;
            _live["ect"] = ect;
            _live["iat"] = iat;
            _live["map"] = map;
            _live["baro"] = baro;
            _live["maf"] = hasMaf ? maf : sdAir;
            _live["tps"] = tps * 100;
            _live["app"] = pedal * 100;
            _live["throttle_cmd"] = Outputs.ThrottleTarget * 100;
            _live["stft"] = _stft * 100;
            _live["ltft"] = ltft * 100;
            _live["ltft_idle"] = _ltftIdle * 100;
            _live["ltft_cruise"] = _ltftCruise * 100;
            _live["load"] = Math.Min(100, load * 100);
            _live["abs_load"] = load * 100;
            _live["timing"] = adv;
            _live["knock_retard"] = _knockRetard;
            _live["speed"] = speed;
            _live["battery_v"] = vbat;
            _live["fuel_status"] = (double)FuelStatus;
            _live["lambda_target"] = lambdaTarget;
            _live["lambda_b1s1"] = wb != null ? lambdaMeas : double.NaN;
            _live["o2_b1s1_v"] = Missing(o2V) ? 0 : o2V;
            _live["fuel_rail_kpa"] = rail;
            _live["boost_kpa"] = boostMeasured;
            _live["boost_target_kpa"] = boostTarget;
            _live["wastegate_duty"] = Outputs.WastegateDuty * 100;
            _live["injector_pw_ms"] = Outputs.InjectorPulseMs[0];
            _live["run_time"] = _runTime;
            _live["mil"] = Dtcs.MilOn ? 1 : 0;
            _live["fan"] = Outputs.FanRelay ? 1 : 0;
            _live["limp"] = LimpMode ? 1 : 0;
            _live["dtc_count"] = Dtcs.ConfirmedCodes().Count;
            _live["diesel_mg"] = Outputs.DieselMg;
            _live["ckp_sync"] = CrankSync ? 1 : 0;
            _live["cmp_sync"] = CamSync ? 1 : 0;
            for (int i = 0; i < _n; i++)
            {
                _live[$"misfire_cyl{i + 1}"] = MisfireCounts[i];
            }
        }

        private static string LowCode(int i) => i switch { 0 => "P0261", 1 => "P0264", 2 => "P0267", 3 => "P0270", 4 => "P0273", _ => "P0276" };

        private static string HighCode(int i) => i switch { 0 => "P0262", 1 => "P0265", 2 => "P0268", 3 => "P0271", 4 => "P0274", _ => "P0277" };

        private void SimpleCircuitCheck(ComponentKind kind, string open, string low, string high, double dt)
        {
            Component? c = _car.Parts.Find(kind);
            if (c != null)
            {
                CircuitCheck(c.Id, open, low, high, dt, true);
            }
        }

        /// <summary>
        /// Low side driver diagnostics: driver off → control pin should sit near battery voltage (through the load);
        /// ~0 V means open load/supply or short to ground; driver on with very high current means short to battery.
        /// </summary>
        private void CircuitCheck(string componentId, string openCode, string lowCode, string highCode, double dt, bool enabled)
        {
            if (!_car.ProbeLowSide(componentId, out double vOff, out double ampsOn, out double nominalAmps))
            {
                return;
            }

            double vbat = _car.BatteryVolts;
            bool shortToGround = vOff < 1.0 && ampsOn < 0.05 && _car.HasShortToGround(componentId);
            bool open = vOff < vbat * 0.4 && !shortToGround;
            bool shortHigh = ampsOn > nominalAmps * 3.0 + 1.0;
            if (openCode == lowCode && lowCode == highCode)
            {
                Check(openCode, enabled, open || shortToGround || shortHigh, dt, 0.5, 1);
                return;
            }

            Check(openCode, enabled, open, dt, 0.5, 1);
            Check(lowCode, enabled, shortToGround, dt, 0.5, 1);
            Check(highCode, enabled, shortHigh, dt, 0.5, 1);
        }

        /// <summary>
        /// Injector shut off on a cylinder with catalyst-damaging misfire (protects the catalyst, as most OEM
        /// strategies do). Cleared when the engine stops.
        /// </summary>
        public bool IsFuelCut(int cylinder) => cylinder >= 0 && cylinder < _n && _fuelCut[cylinder];

        private void UpdateMisfire(double dt, bool enabled, double rpm)
        {
            if (rpm < 300)
            {
                Array.Clear(_fuelCut, 0, _n);
            }

            if (!enabled)
            {
                return;
            }

            double revs = rpm / 60.0 * dt;
            _revsInWindow += revs;
            _revsInShortWindow += revs;
            int[] ev = _car.Engine.State.MisfireEvents;
            for (int i = 0; i < _n; i++)
            {
                _misfireWindow[i] += ev[i];
                _misfireShort[i] += ev[i];
            }

            // 200 rev window: catalyst damaging misfire → flashing MIL
            if (_revsInShortWindow >= 200)
            {
                int total = 0;
                foreach (int m in _misfireShort)
                {
                    total += m;
                }

                double firings = _revsInShortWindow / 2.0 * _n;
                if (total / firings > 0.15 && rpm > 600)
                {
                    Dtcs.MilFlashing = true;
                    ReportMisfireCodes(_misfireShort, total, true);
                    double perCylinder = _revsInShortWindow / 2.0;
                    for (int i = 0; i < _n; i++)
                    {
                        if (_misfireShort[i] / perCylinder > 0.5)
                        {
                            _fuelCut[i] = true;
                        }
                    }
                }

                Array.Clear(_misfireShort, 0, _n);
                _revsInShortWindow = 0;
            }

            if (_revsInWindow >= 1000)
            {
                int total = 0;
                foreach (int m in _misfireWindow)
                {
                    total += m;
                }

                Array.Copy(_misfireWindow, MisfireCounts, _n);
                double firings = _revsInWindow / 2.0 * _n;
                if (total / firings > 0.02)
                {
                    ReportMisfireCodes(_misfireWindow, total, false);
                }
                else
                {
                    Dtcs.Pass("P0300");
                    for (int i = 0; i < _n; i++)
                    {
                        Dtcs.Pass($"P030{i + 1}");
                    }
                }

                Readiness.SetComplete(ReadinessMonitor.Misfire);
                Array.Clear(_misfireWindow, 0, _n);
                _revsInWindow = 0;
            }
        }

        private void ReportMisfireCodes(int[] counts, int total, bool confirm)
        {
            int contributing = 0;
            for (int i = 0; i < _n; i++)
            {
                if (counts[i] > total * 0.15)
                {
                    contributing++;
                    string code = $"P030{i + 1}";
                    Dtcs.Fail(code, Snapshot);
                    if (confirm)
                    {
                        Dtcs.ConfirmNow(code);
                    }
                }
            }

            if (contributing > 2 || contributing == 0)
            {
                Dtcs.Fail("P0300", Snapshot);
                if (confirm)
                {
                    Dtcs.ConfirmNow("P0300");
                }
            }
        }

        private void EnterLimp(string reason)
        {
            if (!LimpMode)
            {
                LimpMode = true;
                LimpReason = reason;
                _limpTimer = 0;
            }
        }

        private bool HasActive(string code)
        {
            DtcRecord? r = Dtcs.Get(code);
            return r != null && r.CurrentlyFailing && (r.Pending || r.Confirmed);
        }

        private double SpeedDensityGps(double mapKpa, double iatC, double rpm)
        {
            EngineDefinition eng = _car.Definition.Engine;
            double ve = Calibration.Lookup(EcuCalibration.VeEstimate, rpm, mapKpa, 0.85);
            return ve * Physics.AirDensity(mapKpa, iatC + 10) * eng.DisplacementM3 * rpm / 120.0 * 1000;
        }
    }
}
