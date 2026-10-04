using System;

namespace Garage.Sim.Engine
{
    /// <summary>Actuator commands produced by the ECU and realised by the car (after electrical checks).</summary>
    public sealed class EngineCommands
    {
        /// <summary>Creates commands for n cylinders.</summary>
        public EngineCommands(int cylinders)
        {
            InjectorPulseMs = new double[cylinders];
            InjectorEnabled = new bool[cylinders];
            SparkEnabled = new bool[cylinders];
        }

        /// <summary>Commanded throttle 0..1 (already resolved to the actual electrical drive).</summary>
        public double Throttle { get; set; }

        /// <summary>Whether the throttle motor is actually driven (false = spring limp position).</summary>
        public bool ThrottleDriven { get; set; } = true;

        /// <summary>Injector pulse width per cylinder (ms).</summary>
        public double[] InjectorPulseMs { get; }

        /// <summary>Injector electrically able to open.</summary>
        public bool[] InjectorEnabled { get; }

        /// <summary>Diesel injected quantity (mg/stroke).</summary>
        public double DieselMgPerStroke { get; set; }

        /// <summary>Spark advance (deg BTDC). For diesel: start of injection.</summary>
        public double SparkAdvanceDeg { get; set; } = 10;

        /// <summary>Spark delivered (coil electrically driven and crank sync available).</summary>
        public bool[] SparkEnabled { get; }

        /// <summary>Wastegate solenoid effective duty 0..1.</summary>
        public double WastegateDuty { get; set; }

        /// <summary>Fuel pump powered.</summary>
        public bool FuelPumpPowered { get; set; }

        /// <summary>Fuel pump supply voltage.</summary>
        public double FuelPumpVolts { get; set; } = 13.5;

        /// <summary>Fan running.</summary>
        public bool FanOn { get; set; }

        /// <summary>EGR command 0..1.</summary>
        public double EgrCommand { get; set; }

        /// <summary>Purge valve duty 0..1.</summary>
        public double PurgeDuty { get; set; }

        /// <summary>Glow plugs on (diesel).</summary>
        public bool GlowOn { get; set; }

        /// <summary>Ignition system supply voltage.</summary>
        public double IgnitionVolts { get; set; } = 14;

        /// <summary>Intake cam advance target (deg crank) when the VVT oil control valve is driven.</summary>
        public double CamTargetDeg { get; set; }

        /// <summary>VVT oil control valve electrically driven.</summary>
        public bool VvtDriven { get; set; } = true;

        /// <summary>GDI rail pressure target (kPa) set through the pump metering valve.</summary>
        public double RailTargetKpa { get; set; }

        /// <summary>GDI metering valve electrically driven (normally open: undriven = full delivery).</summary>
        public bool MeteringDriven { get; set; } = true;

        /// <summary>VGT vane closure command 0 (open, low boost) .. 1 (closed, high boost).</summary>
        public double VgtCommand { get; set; }

        /// <summary>VGT actuator electrically driven.</summary>
        public bool VgtDriven { get; set; } = true;

        /// <summary>Diesel post injection for DPF regeneration (mg/stroke, burns in the exhaust).</summary>
        public double PostInjectionMg { get; set; }

        /// <summary>Unmetered air area added by the EVAP purge (mm²; full EVAP systems only).</summary>
        public double PurgeAreaMm2 { get; set; }
    }

    /// <summary>Ambient conditions.</summary>
    public sealed class EnvironmentState
    {
        /// <summary>Ambient temperature °C.</summary>
        public double AmbientC { get; set; } = 20;

        /// <summary>Barometric pressure kPa.</summary>
        public double BaroKpa { get; set; } = 101.3;

        /// <summary>Relative humidity 0..1.</summary>
        public double Humidity { get; set; } = 0.5;
    }

    /// <summary>Accumulated mechanical damage (0..1, 1 = broken).</summary>
    public sealed class EngineDamage
    {
        /// <summary>Creates damage state for n cylinders.</summary>
        public EngineDamage(int cylinders)
        {
            Piston = new double[cylinders];
            ExhaustValve = new double[cylinders];
        }

        /// <summary>Piston/ring land damage per cylinder (detonation, melting).</summary>
        public double[] Piston { get; }

        /// <summary>Exhaust valve damage per cylinder (EGT).</summary>
        public double[] ExhaustValve { get; }

        /// <summary>Connecting rod bearing damage (over-rev, severe knock, low oil).</summary>
        public double RodBearing { get; set; }

        /// <summary>Head gasket damage (overheating, cylinder pressure).</summary>
        public double HeadGasket { get; set; }

        /// <summary>Turbocharger damage (overspeed, EGT).</summary>
        public double Turbo { get; set; }

        /// <summary>Catalyst thermal damage.</summary>
        public double Catalyst { get; set; }

        /// <summary>Spark plug fouling from rich running/oil (0..1).</summary>
        public double PlugFouling { get; set; }

        /// <summary>True when the connecting rod failed (engine seized).</summary>
        public bool RodFailed => RodBearing >= 1.0;

        /// <summary>True when the head gasket is blown.</summary>
        public bool HeadGasketBlown => HeadGasket >= 1.0;

        /// <summary>True when the turbo failed.</summary>
        public bool TurboFailed => Turbo >= 1.0;

        /// <summary>True when the catalyst melted.</summary>
        public bool CatalystMelted => Catalyst >= 1.0;

        /// <summary>True if any piston is broken.</summary>
        public bool AnyPistonBroken
        {
            get
            {
                foreach (double p in Piston)
                {
                    if (p >= 1.0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Highest damage value of all parts.</summary>
        public double Worst()
        {
            double w = Math.Max(Math.Max(RodBearing, HeadGasket), Math.Max(Turbo, Catalyst));
            foreach (double p in Piston)
            {
                w = Math.Max(w, p);
            }

            foreach (double p in ExhaustValve)
            {
                w = Math.Max(w, p);
            }

            return w;
        }

        /// <summary>Deep copy.</summary>
        public EngineDamage Clone()
        {
            var d = new EngineDamage(Piston.Length)
            {
                RodBearing = RodBearing,
                HeadGasket = HeadGasket,
                Turbo = Turbo,
                Catalyst = Catalyst,
                PlugFouling = PlugFouling,
            };
            Array.Copy(Piston, d.Piston, Piston.Length);
            Array.Copy(ExhaustValve, d.ExhaustValve, ExhaustValve.Length);
            return d;
        }
    }

    /// <summary>Physical ("true") state of the engine after a step. Sensors observe this.</summary>
    public sealed class EngineState
    {
        /// <summary>Creates a state for n cylinders.</summary>
        public EngineState(int cylinders)
        {
            CylinderLambda = new double[cylinders];
            CylinderBurn = new double[cylinders];
            CylinderKnock = new double[cylinders];
            CylinderCompression = new double[cylinders];
            CylinderFuelMg = new double[cylinders];
            MisfireEvents = new int[cylinders];
            FiringEvents = new int[cylinders];
            Array.Fill(CylinderLambda, 1.0);
            Array.Fill(CylinderCompression, 1.0);
        }

        /// <summary>Engine speed.</summary>
        public double Rpm { get; set; }

        /// <summary>Engine running on its own (combustion sustaining).</summary>
        public bool Running { get; set; }

        /// <summary>Engine mechanically seized.</summary>
        public bool Seized { get; set; }

        /// <summary>Manifold absolute pressure kPa.</summary>
        public double ManifoldKpa { get; set; } = 101.3;

        /// <summary>Pre-throttle absolute pressure kPa.</summary>
        public double PreThrottleKpa { get; set; } = 101.3;

        /// <summary>Boost gauge kPa (pre-throttle minus baro).</summary>
        public double BoostKpa { get; set; }

        /// <summary>Turbo shaft speed proxy 0..1.</summary>
        public double TurboSpeed { get; set; }

        /// <summary>Throttle actual opening 0..1.</summary>
        public double ThrottlePosition { get; set; }

        /// <summary>Intake air temperature at the filter °C.</summary>
        public double IntakeAirC { get; set; } = 20;

        /// <summary>Charge (manifold) air temperature °C.</summary>
        public double ChargeAirC { get; set; } = 20;

        /// <summary>Coolant temperature °C.</summary>
        public double CoolantC { get; set; } = 20;

        /// <summary>Oil temperature °C.</summary>
        public double OilC { get; set; } = 20;

        /// <summary>Exhaust gas temperature (pre turbine) °C.</summary>
        public double ExhaustGasC { get; set; } = 20;

        /// <summary>Catalyst temperature °C.</summary>
        public double CatalystC { get; set; } = 20;

        /// <summary>Upstream oxygen sensor element temperature °C.</summary>
        public double O2SensorC { get; set; } = 20;

        /// <summary>Downstream oxygen sensor element temperature °C.</summary>
        public double O2DownstreamC { get; set; } = 20;

        /// <summary>Air mass flow through the MAF (g/s).</summary>
        public double MafFlowGps { get; set; }

        /// <summary>Air mass flow into the cylinders (g/s).</summary>
        public double EngineAirGps { get; set; }

        /// <summary>Unmetered air (vacuum leaks) g/s.</summary>
        public double UnmeteredAirGps { get; set; }

        /// <summary>Boost leak flow (metered but lost) g/s.</summary>
        public double BoostLeakGps { get; set; }

        /// <summary>Fuel rail pressure kPa gauge.</summary>
        public double FuelRailKpa { get; set; }

        /// <summary>Fuel mass flow g/s.</summary>
        public double FuelFlowGps { get; set; }

        /// <summary>Per cylinder lambda.</summary>
        public double[] CylinderLambda { get; }

        /// <summary>Per cylinder combustion completeness (filtered 0..1; 0 = dead cylinder).</summary>
        public double[] CylinderBurn { get; }

        /// <summary>Per cylinder knock intensity (0..~1.5).</summary>
        public double[] CylinderKnock { get; }

        /// <summary>Per cylinder compression health factor (1 = nominal).</summary>
        public double[] CylinderCompression { get; }

        /// <summary>Per cylinder fuel per injection (mg).</summary>
        public double[] CylinderFuelMg { get; }

        /// <summary>Misfire events this step per cylinder.</summary>
        public int[] MisfireEvents { get; }

        /// <summary>Firing (combustion opportunities) this step per cylinder.</summary>
        public int[] FiringEvents { get; }

        /// <summary>Exhaust lambda seen by the upstream sensor.</summary>
        public double ExhaustLambda { get; set; } = 1.0;

        /// <summary>Lambda after the catalyst seen by the downstream sensor.</summary>
        public double PostCatLambda { get; set; } = 1.0;

        /// <summary>Catalyst conversion efficiency 0..1.</summary>
        public double CatalystEfficiency { get; set; }

        /// <summary>Oxygen storage oscillation amplitude downstream 0..1 (1 = mirrors upstream).</summary>
        public double PostCatSwitchingRatio { get; set; }

        /// <summary>Spark advance actually applied (deg).</summary>
        public double SparkAdvanceDeg { get; set; }

        /// <summary>MBT advance at this point (deg).</summary>
        public double MbtAdvanceDeg { get; set; }

        /// <summary>Knock limited advance (deg).</summary>
        public double KnockLimitDeg { get; set; }

        /// <summary>Indicated torque N·m.</summary>
        public double IndicatedTorqueNm { get; set; }

        /// <summary>Friction + pumping + accessory torque N·m.</summary>
        public double LossTorqueNm { get; set; }

        /// <summary>Brake torque N·m.</summary>
        public double TorqueNm { get; set; }

        /// <summary>Brake power kW.</summary>
        public double PowerKw => Core.Physics.PowerKw(TorqueNm, Rpm);

        /// <summary>Relative load (cylinder air mass / standard air mass).</summary>
        public double RelativeLoad { get; set; }

        /// <summary>Exhaust back pressure kPa abs.</summary>
        public double ExhaustKpa { get; set; } = 101.3;

        /// <summary>Peak cylinder pressure estimate (bar).</summary>
        public double PeakCylinderBar { get; set; }

        /// <summary>Roughness / vibration 0..1.</summary>
        public double Roughness { get; set; }

        /// <summary>Coolant level 0..1.</summary>
        public double CoolantLevel { get; set; } = 1.0;

        /// <summary>Oil pressure kPa.</summary>
        public double OilPressureKpa { get; set; }

        /// <summary>Camshaft timing offset vs crank (deg).</summary>
        public double CamOffsetDeg { get; set; }

        /// <summary>EGR dilution fraction.</summary>
        public double EgrFraction { get; set; }

        /// <summary>Smoke opacity (diesel) / visible smoke 0..1.</summary>
        public double SmokeOpacity { get; set; }

        /// <summary>Fuel energy power (kW) released.</summary>
        public double FuelPowerKw { get; set; }

        /// <summary>Crank angle accumulator (deg, 0..720) for waveforms.</summary>
        public double CrankAngleDeg { get; set; }

        /// <summary>Actual intake cam advance (deg crank) set by the VVT phaser.</summary>
        public double CamPhaseDeg { get; set; }

        /// <summary>Low pressure fuel supply (kPa) feeding the GDI high pressure pump.</summary>
        public double LowFuelKpa { get; set; }

        /// <summary>VGT vane closure actually reached 0..1.</summary>
        public double VgtPosition { get; set; }

        /// <summary>Soot loaded in the DPF (g).</summary>
        public double DpfSootG { get; set; }

        /// <summary>Ash in the DPF (g, only removed by replacing/cleaning it).</summary>
        public double DpfAshG { get; set; }

        /// <summary>DPF differential pressure (kPa).</summary>
        public double DpfDeltaKpa { get; set; }

        /// <summary>Engine-out NOx estimate (ppm).</summary>
        public double NoxPpm { get; set; }
    }
}
