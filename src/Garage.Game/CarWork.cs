using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Sim.Components;
using Garage.Sim.Dyno;
using Garage.Sim.Engine;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Tools;
using Garage.Sim.Vehicle;

namespace Garage.Game
{
    /// <summary>
    /// Everything the player can do to the car on the lift: engine, diagnostic tools, ECU editor and dyno.
    /// Shared by the CLI and Unity. Time spent is charged to the job and the workshop clock; tool ownership is
    /// checked here (in sandbox, without a workshop, every tool is available).
    /// </summary>
    public sealed class CarWork
    {
        /// <summary>Tool ids (match data/base/upgrades.json).</summary>
        public const string Scanner = "tool_scanner", Meter = "tool_multimeter", Scope = "tool_scope", FuelGauge = "tool_fuel_gauge",
            Compression = "tool_compression", LeakDown = "tool_leakdown", Smoke = "tool_smoke", Dyno = "tool_dyno", EcuFlash = "tool_ecu_flash";

        private readonly Workshop? _ws;
        private readonly GameEventBus _bus;
        private double _chargedMinutes;

        /// <summary>Creates the work context for a car.</summary>
        public CarWork(Car car, Job? job, Workshop? ws, GameEventBus bus, bool training)
        {
            Car = car;
            Job = job;
            _ws = ws;
            _bus = bus;
            Training = training;
            Session = new DiagnosticSession(car, ws?.TimeFactor ?? 1.0);
            Ecu = new EcuEditor(this);
        }

        /// <summary>The car.</summary>
        public Car Car { get; }

        /// <summary>Job (null in sandbox).</summary>
        public Job? Job { get; }

        /// <summary>Training mode (explanations).</summary>
        public bool Training { get; }

        /// <summary>Underlying tool session.</summary>
        public DiagnosticSession Session { get; }

        /// <summary>ECU map editor.</summary>
        public EcuEditor Ecu { get; }

        /// <summary>Dyno pulls made on this car, oldest first.</summary>
        public List<DynoResult> DynoRuns { get; } = new List<DynoResult>();

        /// <summary>
        /// Scan tool (or flash interface) physically plugged into the OBD port. The CLI plugs it implicitly; Unity
        /// sets it when the player snaps the tool into the connector.
        /// </summary>
        public bool ScannerPlugged { get; set; }

        /// <summary>Whether a tool is available.</summary>
        public bool Owns(string tool) => _ws == null || _ws.Has(tool);

        internal GameEventBus Bus => _bus;

        private CommandResult Locked(string tool, string name) => Fail(CommandError.ToolLocked, $"No tienes {name}: cómprala en la tienda de herramientas.");

        private CommandResult Fail(CommandError e, string msg)
        {
            _bus.Publish(GameEventKind.CommandFailed, "", msg);
            return CommandResult.Fail(e, msg);
        }

        /// <summary>Charges the session's new minutes to the job and clock.</summary>
        internal void SyncTime()
        {
            double delta = Session.MinutesSpent - _chargedMinutes;
            _chargedMinutes = Session.MinutesSpent;
            if (_ws != null && Job != null && delta > 0)
            {
                _ws.ChargeDiagnosis(Job, delta);
            }
        }

        private CommandResult Done(ToolResult r, string tool, string component = "")
        {
            SyncTime();
            _bus.Publish(GameEventKind.ToolUsed, tool, r.Display);
            if (component.Length > 0)
            {
                _bus.Publish(GameEventKind.ComponentTested, component, r.Display);
            }

            return CommandResult.From(r);
        }

        /// <summary>Charges a fixed time for a manual action.</summary>
        public void Charge(double minutes, string what)
        {
            Session.Charge(new ToolResult("", minutes), what);
            SyncTime();
        }

        // ------------------------------------------------------------------ engine

        /// <summary>Readable status line.</summary>
        public string StatusLine
        {
            get
            {
                EngineState s = Car.Engine.State;
                string engine = s.Rpm > 300 ? $"motor en marcha {s.Rpm:0} rpm" : (Car.KeyOn ? "contacto puesto, motor parado" : "contacto quitado");
                return $"{Car.Definition.DisplayName} — {engine} | ECT {s.CoolantC:0} °C | {(Car.Ecu.Dtcs.MilOn ? "MIL ENCENDIDA" : "MIL apagada")}";
            }
        }

        /// <summary>Engine running.</summary>
        public bool EngineRunning => Car.Engine.State.Rpm > 300;

        /// <summary>Ignition on without cranking (fuel pump primes).</summary>
        public CommandResult IgnitionOn()
        {
            Car.Key = KeyPosition.On;
            Car.RunFor(2.5);
            _bus.Publish(GameEventKind.IgnitionOn);
            return CommandResult.Success("Contacto puesto: se oye la bomba de combustible cebando." + (Car.Engine.State.FuelRailKpa < 100 ? " (¿o no? no se oye la bomba)" : ""));
        }

        /// <summary>Cranks the engine.</summary>
        public CommandResult Start()
        {
            bool ok = Car.Start();
            Charge(2, "Arranque");
            if (ok)
            {
                _bus.Publish(GameEventKind.EngineStarted);
                return CommandResult.Success($"Arranca. Ralentí {Car.Engine.State.Rpm:0} rpm.");
            }

            return Fail(CommandError.InvalidState, "El motor gira pero no arranca.");
        }

        /// <summary>Key off (ends the OBD drive cycle).</summary>
        public CommandResult Stop()
        {
            Car.Key = KeyPosition.Off;
            Car.RunFor(1);
            _bus.Publish(GameEventKind.EngineStopped);
            return CommandResult.Success("Contacto quitado (fin de ciclo de conducción OBD).");
        }

        private bool EnsureRunning() => EngineRunning || Car.Start();

        /// <summary>Idles a minute and describes the sensory cues.</summary>
        public CommandResult ListenIdle()
        {
            if (!EnsureRunning())
            {
                return Fail(CommandError.InvalidState, "No arranca.");
            }

            Car.Mode = LoadMode.Neutral;
            Car.Gear = 0;
            Car.Pedal = 0;
            Car.RunFor(60);
            Charge(2, "Escuchar el motor al ralentí");
            return CommandResult.Success(DescribeCues());
        }

        /// <summary>Two-minute cruise at 2500 rpm in 3rd and back to idle.</summary>
        public CommandResult RoadTest()
        {
            if (!EnsureRunning())
            {
                return Fail(CommandError.InvalidState, "No arranca.");
            }

            int gear = Math.Min(3, Car.Definition.GearRatios.Length);
            Car.Gear = gear;
            Car.Mode = LoadMode.DynoHoldRpm;
            double ratio = Car.Definition.GearRatios[gear - 1] * Car.Definition.FinalDrive;
            Car.DynoHoldRpm = 2500;
            Car.SetVehicleSpeed(2500 / 60.0 * 2 * Math.PI / ratio * Car.Definition.WheelRadiusM * 3.6);
            Car.Pedal = 0.3;
            Car.RunFor(120);
            Car.Pedal = 0;
            Car.Mode = LoadMode.Neutral;
            Car.Gear = 0;
            Car.SetVehicleSpeed(0);
            Car.RunFor(5);
            Charge(15, "Prueba de carretera");
            string text = DescribeCues() + (Car.Ecu.Dtcs.MilOn ? "\nEl testigo de avería está encendido." : "\nTestigo de avería apagado.");
            _bus.Publish(GameEventKind.RoadTestDone, "", text);
            CheckFailure();
            return CommandResult.Success(text);
        }

        /// <summary>Free rev.</summary>
        public CommandResult Rev()
        {
            if (!EnsureRunning())
            {
                return Fail(CommandError.InvalidState, "No arranca.");
            }

            Car.Pedal = 1;
            Car.RunFor(1.2);
            double peak = Car.Engine.State.Rpm;
            Car.Pedal = 0;
            Car.RunFor(3);
            return CommandResult.Success($"Sube hasta {peak:0} rpm y vuelve a ralentí.\n{DescribeCues()}");
        }

        /// <summary>Current sensory cues as text.</summary>
        public string DescribeCues()
        {
            var cues = Car.Cues;
            if (cues.Count == 0)
            {
                return "No se percibe nada anormal.";
            }

            return string.Join("\n", cues.OrderByDescending(c => c.Intensity).Select(c => $"[{CueName(c.Channel)}] {c.Description}"));
        }

        /// <summary>Spanish channel name.</summary>
        public static string CueName(CueChannel c) => c switch
        {
            CueChannel.Sound => "oído",
            CueChannel.Smoke => "humo",
            CueChannel.Smell => "olor",
            CueChannel.Vibration => "vibración",
            CueChannel.Warning => "testigo",
            _ => "vista",
        };

        private void CheckFailure()
        {
            EngineDamage d = Car.Engine.Damage;
            string? what = Car.Engine.State.Seized ? "motor gripado"
                : d.RodFailed ? "biela"
                : d.AnyPistonBroken ? "pistón"
                : d.HeadGasketBlown ? "junta de culata"
                : d.TurboFailed ? "turbo"
                : d.CatalystMelted ? "catalizador fundido" : null;
            if (what != null)
            {
                _bus.Publish(GameEventKind.EngineFailure, what, $"¡Avería grave: {what}!");
            }
        }

        // ----------------------------------------------------------------- scanner

        private CommandResult? ScannerGate()
        {
            if (!Owns(Scanner))
            {
                return Locked(Scanner, "escáner");
            }

            if (!ScannerPlugged)
            {
                return Fail(CommandError.InvalidState, "El escáner no está enchufado al conector OBD.");
            }

            return null;
        }

        /// <summary>Connects and identifies.</summary>
        public CommandResult ScanConnect()
        {
            CommandResult? g = ScannerGate();
            if (g != null)
            {
                return g;
            }

            ToolResult r = Session.Scanner.Connect();
            CommandResult res = Done(r, Scanner);
            if (r.Ok)
            {
                _bus.Publish(GameEventKind.ScannerConnected);
            }

            return res;
        }

        /// <summary>Reads codes of a module.</summary>
        public CommandResult ReadCodes(string module = "ecm")
        {
            CommandResult? g = ScannerGate();
            if (g != null)
            {
                return g;
            }

            ToolResult r = Session.Scanner.ReadCodes(module, Training);
            CommandResult res = Done(r, Scanner);
            if (r.Ok && Session.Scanner.CanTalkTo(module))
            {
                IEnumerable<string> codes = module == "ecm"
                    ? Car.Ecu.Dtcs.ConfirmedCodes().Concat(Car.Ecu.Dtcs.PendingCodes()).Distinct()
                    : Array.Empty<string>();
                _bus.Publish(GameEventKind.DtcRead, string.Join(",", codes), r.Display);
            }

            return res;
        }

        /// <summary>Clears codes.</summary>
        public CommandResult ClearCodes(string module = "ecm")
        {
            CommandResult? g = ScannerGate();
            if (g != null)
            {
                return g;
            }

            ToolResult r = Session.Scanner.ClearCodes(module);
            CommandResult res = Done(r, Scanner);
            if (r.Ok)
            {
                _bus.Publish(GameEventKind.DtcCleared, module);
            }

            return res;
        }

        /// <summary>Freeze frame.</summary>
        public CommandResult FreezeFrame() => ScannerGate() ?? Done(Session.Scanner.FreezeFrame(), Scanner);

        /// <summary>Live data (one refresh).</summary>
        public CommandResult LiveData(bool frames = false)
        {
            CommandResult? g = ScannerGate();
            if (g != null)
            {
                return g;
            }

            CommandResult res = Done(Session.Scanner.LiveData(null, frames), Scanner);
            if (res.Ok)
            {
                _bus.Publish(GameEventKind.LiveDataRead);
            }

            return res;
        }

        /// <summary>Live data as values (for graphs). Empty if the scanner cannot talk.</summary>
        public List<Garage.Sim.Ecu.PidReading> LiveValues()
        {
            if (ScannerGate() != null || !Session.Scanner.CanTalkTo("ecm"))
            {
                return new List<Garage.Sim.Ecu.PidReading>();
            }

            return Session.Scanner.ReadLive();
        }

        /// <summary>Readiness monitors.</summary>
        public CommandResult Readiness() => ScannerGate() ?? Done(Session.Scanner.Readiness(), Scanner);

        /// <summary>Actuator test.</summary>
        public CommandResult ActuatorTest(ActuatorTest test, int cylinder = 0) => ScannerGate() ?? Done(Session.Scanner.RunActuatorTest(test, cylinder), Scanner);

        // -------------------------------------------------------------- multimeter

        /// <summary>Selects the meter function.</summary>
        public void SetMeterMode(MeterMode m) => Session.Multimeter.Mode = m;

        /// <summary>Selects the meter range.</summary>
        public void SetMeterRange(MeterRange r) => Session.Multimeter.Range = r;

        /// <summary>Measures between two probe points (component:side:pin, gnd, bat).</summary>
        public CommandResult Measure(string red, string black)
        {
            if (!Owns(Meter))
            {
                return Locked(Meter, "multímetro");
            }

            ToolResult r = Session.Multimeter.Measure(red, black);
            Multimeter.TryParseProbe(red, out string? comp, out _);
            if (comp == null)
            {
                Multimeter.TryParseProbe(black, out comp, out _);
            }

            CommandResult res = Done(r, Meter, comp ?? "");
            _bus.Publish(GameEventKind.MeterMeasured, comp ?? "", r.Display, r.Value);
            return res;
        }

        /// <summary>Wiring diagram of a component.</summary>
        public CommandResult WiringDiagram(string componentId) => !Owns(Meter) ? Locked(Meter, "multímetro") : Done(Session.Multimeter.WiringDiagram(componentId), Meter);

        /// <summary>Unplugs/plugs a component connector.</summary>
        public CommandResult SetConnector(string componentId, bool connected)
        {
            ToolResult r = Session.Multimeter.SetConnector(componentId, connected);
            CommandResult res = Done(r, "hands");
            if (r.Ok)
            {
                _bus.Publish(GameEventKind.ConnectorChanged, componentId, r.Display, connected ? 1 : 0);
            }

            return res;
        }

        // ------------------------------------------------------- scope & mechanics

        /// <summary>Oscilloscope capture; the waveform is returned through <paramref name="wave"/>.</summary>
        public CommandResult Capture(string componentId, string pin, double windowMs, out Waveform? wave)
        {
            wave = null;
            if (!Owns(Scope))
            {
                return Locked(Scope, "osciloscopio");
            }

            wave = Session.Scope.Capture(componentId, pin, windowMs);
            SyncTime();
            _bus.Publish(GameEventKind.ToolUsed, Scope, wave.Description);
            _bus.Publish(GameEventKind.ComponentTested, componentId, wave.Description);
            return CommandResult.Success(wave.Description);
        }

        /// <summary>Fuel pressure test.</summary>
        public CommandResult FuelPressure(FuelPressureMode mode) => !Owns(FuelGauge) ? Locked(FuelGauge, "manómetro de combustible") : Done(Session.Mechanical.FuelPressure(mode), FuelGauge, "fuel_pump");

        /// <summary>Compression test.</summary>
        public CommandResult CompressionTest(bool wet) => !Owns(Compression) ? Locked(Compression, "compresímetro") : Done(Session.Mechanical.Compression(wet), Compression);

        /// <summary>Leak-down test.</summary>
        public CommandResult LeakDownTest(int cylinder) => !Owns(LeakDown) ? Locked(LeakDown, "comprobador de fugas") : Done(Session.Mechanical.LeakDown(cylinder), LeakDown);

        /// <summary>Smoke test.</summary>
        public CommandResult SmokeTest(bool exhaust) => !Owns(Smoke) ? Locked(Smoke, "máquina de humo") : Done(Session.Mechanical.Smoke(exhaust), Smoke);

        /// <summary>Visual inspection of a component.</summary>
        public CommandResult Inspect(string componentId) => Done(Session.Mechanical.Inspect(componentId), "eyes", componentId);

        /// <summary>Cranking voltage drop test.</summary>
        public CommandResult CrankingTest()
        {
            double before = Car.BatteryVolts;
            Car.Key = KeyPosition.Crank;
            Car.RunFor(0.5);
            double during = Car.BatteryVolts;
            Car.Key = KeyPosition.On;
            Car.RunFor(0.5);
            return Done(Session.Charge(new ToolResult($"Tensión en batería en reposo {before:0.00} V; durante el arranque {during:0.00} V (debe quedar > 9,6 V).", 5), "Prueba de arranque"), Meter, "battery");
        }

        /// <summary>Training advice (empty in realistic mode).</summary>
        public List<string> Advice() => Training ? TrainingAdvisor.Advise(Car) : new List<string>();

        // ------------------------------------------------------------------- dyno

        /// <summary>Dyno pull in a gear (0 = default).</summary>
        public CommandResult RunDyno(int gear = 0)
        {
            if (!Owns(Dyno))
            {
                return Locked(Dyno, "banco de potencia");
            }

            DynoResult r = DynoRun.Pull(Car, gear);
            DynoRuns.Add(r);
            Charge(20, "Pasada en banco");
            if (Job != null && Job.BaselinePowerPs <= 0)
            {
                Job.BaselinePowerPs = r.PeakPowerPs;
            }

            _bus.Publish(GameEventKind.DynoRunCompleted, "", r.Summary, r.PeakPowerPs);
            CheckFailure();
            string text = r.Summary + (r.Warnings.Count > 0 ? "\n¡! " + string.Join("\n¡! ", r.Warnings) : "");
            return CommandResult.Success(text);
        }
    }
}
