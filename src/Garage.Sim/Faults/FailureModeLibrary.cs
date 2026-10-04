using System;
using System.Collections.Generic;

namespace Garage.Sim.Faults
{
    /// <summary>
    /// Registry of failure modes. <see cref="Default"/> holds the built-in generic set; data/base/failure_modes.json
    /// mirrors it and mods may add or override modes.
    /// </summary>
    public sealed class FailureModeLibrary
    {
        private readonly Dictionary<string, FailureModeDefinition> _modes = new Dictionary<string, FailureModeDefinition>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] Sensors =
        {
            "MafSensor", "MapSensor", "IatSensor", "EctSensor", "TpsSensor", "AppSensor", "O2Narrowband", "O2Wideband", "O2Downstream",
            "FuelPressureSensor", "BoostSensor", "EvapPressureSensor", "DpfPressureSensor", "ExhaustTempSensor",
        };

        private static readonly string[] Electrical =
        {
            "MafSensor", "MapSensor", "IatSensor", "EctSensor", "TpsSensor", "AppSensor", "O2Narrowband", "O2Wideband", "O2Downstream",
            "FuelPressureSensor", "BoostSensor", "CkpSensor", "CmpSensor", "KnockSensor", "Injector", "IgnitionCoil", "WastegateSolenoid",
            "PurgeValve", "Relay", "ElectronicThrottle", "EvapPressureSensor", "DpfPressureSensor", "ExhaustTempSensor", "EvapVentValve",
            "VvtSolenoid", "HighPressurePump", "VgtActuator",
        };

        private static readonly string[] ElectricalAndGround = Concat(Electrical, "GroundStrap");

        private static string[] Concat(string[] a, string extra)
        {
            var r = new string[a.Length + 1];
            a.CopyTo(r, 0);
            r[a.Length] = extra;
            return r;
        }

        /// <summary>Built-in library.</summary>
        public static FailureModeLibrary Default { get; } = CreateDefault();

        /// <summary>All modes.</summary>
        public IEnumerable<FailureModeDefinition> All => _modes.Values;

        /// <summary>Number of modes.</summary>
        public int Count => _modes.Count;

        /// <summary>Registers or overrides a mode.</summary>
        public void Register(FailureModeDefinition m) => _modes[m.Id] = m;

        /// <summary>Gets a mode by id.</summary>
        public FailureModeDefinition Get(string id)
        {
            if (_modes.TryGetValue(id, out FailureModeDefinition m))
            {
                return m;
            }

            throw new KeyNotFoundException($"Unknown failure mode '{id}'.");
        }

        /// <summary>Tries to get a mode.</summary>
        public bool TryGet(string id, out FailureModeDefinition mode) => _modes.TryGetValue(id, out mode!);

        /// <summary>Copy of this library (so data can extend it without touching the default).</summary>
        public FailureModeLibrary Clone()
        {
            var l = new FailureModeLibrary();
            foreach (FailureModeDefinition m in _modes.Values)
            {
                l.Register(m);
            }

            return l;
        }

        private static FailureModeDefinition M(string id, string name, string family, EffectKind e, double min, double max, int diff, string repair, string pin, string desc, string[] applies, params ConditionKind[] cond)
        {
            return new FailureModeDefinition
            {
                Id = id,
                Name = name,
                Family = family,
                Effect = e,
                MagnitudeMin = min,
                MagnitudeMax = max,
                Difficulty = diff,
                Repair = repair,
                Pin = pin,
                Description = desc,
                AppliesTo = new List<string>(applies),
                Conditions = new List<ConditionKind>(cond),
            };
        }

        private static FailureModeLibrary CreateDefault()
        {
            var l = new FailureModeLibrary();
            ConditionKind[] all = { ConditionKind.Always, ConditionKind.Cold, ConditionKind.Hot, ConditionKind.Intermittent, ConditionKind.Vibration };
            ConditionKind[] mech = { ConditionKind.Always };

            l.Register(M("signal_bias_high", "Señal sesgada alta", "signal", EffectKind.SignalOffset, 0.04, 0.2, 3, "replace", "", "La señal del sensor indica un valor mayor que el real.", Sensors, all));
            l.Register(M("signal_bias_low", "Señal sesgada baja", "signal", EffectKind.SignalOffset, -0.04, -0.2, 3, "replace", "", "La señal del sensor indica un valor menor que el real.", Sensors, all));
            l.Register(M("signal_gain_high", "Ganancia alta", "signal", EffectKind.SignalGain, 0.08, 0.35, 3, "replace", "", "El sensor exagera la magnitud medida.", Sensors, mech));
            l.Register(M("signal_gain_low", "Ganancia baja / sensor sucio", "signal", EffectKind.SignalGain, -0.08, -0.35, 3, "replace", "", "El sensor subestima la magnitud medida.", Sensors, mech));
            l.Register(M("signal_stuck", "Señal congelada", "signal", EffectKind.SignalStuck, 0.1, 0.9, 2, "replace", "", "La salida del sensor no cambia.", Sensors, ConditionKind.Always, ConditionKind.Hot, ConditionKind.Intermittent));
            l.Register(M("signal_noisy", "Señal con ruido", "signal", EffectKind.SignalNoise, 0.02, 0.1, 3, "replace", "", "La señal presenta ruido o picos.", Sensors, ConditionKind.Always, ConditionKind.Vibration));
            l.Register(M("signal_slow", "Respuesta lenta", "signal", EffectKind.SignalLag, 0.4, 4.0, 4, "replace", "", "El sensor responde con retraso (envejecido o contaminado).", Sensors, mech));
            l.Register(M("signal_dropout", "Pérdida de señal", "signal", EffectKind.SignalDropout, 1, 1, 3, "replace", "", "El sensor deja de generar señal.", new[] { "MafSensor", "MapSensor", "TpsSensor", "CkpSensor", "CmpSensor", "O2Narrowband", "O2Wideband", "KnockSensor", "EvapPressureSensor", "DpfPressureSensor" }, ConditionKind.Always, ConditionKind.Hot, ConditionKind.Intermittent));
            l.Register(M("ckp_weak_signal", "Señal CKP débil (entrehierro)", "signal", EffectKind.SignalGain, -0.5, -0.9, 4, "replace", "", "Amplitud del sensor de cigüeñal insuficiente.", new[] { "CkpSensor" }, ConditionKind.Always, ConditionKind.Hot));

            l.Register(M("wire_open", "Cable abierto", "wiring", EffectKind.WireOpen, 1, 1, 2, "repair_wire", "signal", "Circuito abierto en el cable indicado.", Electrical, ConditionKind.Always, ConditionKind.Vibration, ConditionKind.Intermittent));
            l.Register(M("wire_short_ground", "Corto a masa", "wiring", EffectKind.WireShortGround, 1, 1, 2, "repair_wire", "signal", "El cable toca masa.", Electrical, ConditionKind.Always, ConditionKind.Vibration));
            l.Register(M("wire_short_power", "Corto a positivo", "wiring", EffectKind.WireShortPower, 1, 1, 3, "repair_wire", "signal", "El cable toca tensión de batería.", Electrical, mech));
            l.Register(M("wire_high_resistance", "Alta resistencia en cable", "wiring", EffectKind.WireHighResistance, 80, 3000, 4, "repair_wire", "signal", "Cable dañado o empalme deficiente.", ElectricalAndGround, ConditionKind.Always, ConditionKind.Hot, ConditionKind.Vibration));
            l.Register(M("connector_corrosion", "Conector oxidado", "wiring", EffectKind.ConnectorCorrosion, 50, 2500, 4, "repair_wire", "ground", "Pin del conector con óxido verde.", ElectricalAndGround, ConditionKind.Always, ConditionKind.Cold));

            l.Register(M("leak_vacuum", "Fuga de vacío", "leak", EffectKind.Leak, 1.5, 12, 2, "replace", "", "Entrada de aire no medido tras el caudalímetro.", new[] { "VacuumHose", "IntakeGasket" }, ConditionKind.Always, ConditionKind.Cold, ConditionKind.Hot));
            l.Register(M("leak_boost", "Fuga de presión de turbo", "leak", EffectKind.Leak, 50, 1200, 3, "replace", "", "Manguito o intercooler pierde aire a presión.", new[] { "BoostHose", "Intercooler" }, ConditionKind.Always, ConditionKind.AboveLoad));
            l.Register(M("leak_fluid", "Fuga interna", "leak", EffectKind.Leak, 0.2, 1, 4, "replace", "", "Fuga de fluido/gases (junta, regulador).", new[] { "HeadGasket", "FuelPressureRegulator", "WastegateSolenoid" }, mech));
            l.Register(M("leak_exhaust", "Fuga de escape", "leak", EffectKind.Leak, 0.2, 1, 3, "replace", "", "Fuga en colector/escape antes de la sonda.", new[] { "Exhaust" }, ConditionKind.Always, ConditionKind.Cold));
            l.Register(M("leak_evap", "Fuga en el sistema EVAP", "leak", EffectKind.Leak, 0.3, 2.5, 3, "replace", "", "Poro o grieta en cánister/tuberías de vapor (diámetro equivalente en mm).", new[] { "EvapCanister", "FuelCap" }, ConditionKind.Always, ConditionKind.Hot));
            l.Register(M("cap_loose", "Tapón de combustible mal cerrado", "leak", EffectKind.Leak, 3.5, 5, 1, "replace", "", "Tapón suelto o junta del tapón dañada: fuga grande de vapores.", new[] { "FuelCap" }, mech));
            l.Register(M("dpf_cracked", "Filtro de partículas agrietado", "mechanical", EffectKind.Leak, 0.5, 1, 4, "replace", "", "El monolito agrietado deja pasar el hollín: baja la presión diferencial.", new[] { "ParticulateFilter" }, mech));
            l.Register(M("leak_compression", "Pérdida de compresión", "mechanical", EffectKind.Leak, 0.25, 0.8, 4, "replace", "", "Válvula quemada o segmentos rotos.", new[] { "Cylinder" }, mech));

            l.Register(M("restriction", "Obstrucción", "mechanical", EffectKind.Restriction, 0.2, 0.9, 2, "replace", "", "Paso de flujo reducido.", new[] { "AirFilter", "FuelFilter", "Exhaust", "Catalyst", "ElectronicThrottle", "Intercooler", "ParticulateFilter" }, mech));
            l.Register(M("clog", "Obstruido/sucio", "mechanical", EffectKind.Clog, 0.12, 0.6, 3, "replace", "", "Depósitos reducen el caudal.", new[] { "Injector", "FuelFilter", "AirFilter", "EgrValve", "VvtSolenoid", "VgtActuator" }, mech));
            l.Register(M("wear", "Desgaste", "mechanical", EffectKind.Wear, 0.25, 1, 3, "replace", "", "Componente desgastado o contaminado.", new[] { "Cylinder", "SparkPlug", "Turbocharger", "Catalyst", "TimingDrive", "MafSensor", "O2Narrowband", "O2Downstream" }, mech));
            l.Register(M("stuck_open", "Atascado abierto", "actuator", EffectKind.StuckOpen, 0.4, 1, 2, "replace", "", "El elemento no cierra.", new[] { "Thermostat", "EgrValve", "PurgeValve", "WastegateSolenoid", "Turbocharger", "Relay", "Injector", "Alternator", "EvapVentValve", "VvtSolenoid", "HighPressurePump", "VgtActuator" }, ConditionKind.Always, ConditionKind.Cold));
            l.Register(M("throttle_stuck", "Mariposa atascada", "actuator", EffectKind.StuckOpen, 0.12, 0.3, 3, "replace", "", "La mariposa motorizada se queda clavada.", new[] { "ElectronicThrottle" }, ConditionKind.Always, ConditionKind.Cold));
            l.Register(M("stuck_closed", "Atascado cerrado", "actuator", EffectKind.StuckClosed, 1, 1, 2, "replace", "", "El elemento no abre.", new[] { "Thermostat", "EgrValve", "Turbocharger", "Relay", "FuelPressureRegulator", "WastegateSolenoid", "Injector", "EvapVentValve", "VvtSolenoid", "HighPressurePump", "VgtActuator" }, ConditionKind.Always, ConditionKind.Hot));
            l.Register(M("weak", "Rendimiento bajo", "electrical", EffectKind.Weak, 0.2, 0.8, 3, "replace", "", "Capacidad reducida (bobina, bomba, batería, alternador...).", new[] { "IgnitionCoil", "FuelPump", "Battery", "Alternator", "SparkPlug", "KnockSensor", "HighPressurePump" }, ConditionKind.Always, ConditionKind.Hot, ConditionKind.AboveLoad));
            l.Register(M("dead", "Averiado (sin funcionamiento)", "electrical", EffectKind.Dead, 1, 1, 1, "replace", "", "El componente no funciona.", new[] { "IgnitionCoil", "FuelPump", "CoolingFan", "Fuse", "Alternator", "Battery", "Injector", "ControlModule", "CmpSensor", "CkpSensor", "WastegateSolenoid", "VvtSolenoid", "VgtActuator", "EvapVentValve" }, ConditionKind.Always, ConditionKind.Hot, ConditionKind.Intermittent));
            l.Register(M("slack", "Holgura de distribución", "mechanical", EffectKind.Slack, 4, 14, 5, "replace", "", "Cadena estirada: desfase árbol de levas/cigüeñal.", new[] { "TimingDrive" }, mech));
            l.Register(M("internal_short", "Cortocircuito interno", "electrical", EffectKind.InternalShort, 0.3, 0.95, 4, "replace", "", "Espiras en corto (bobina, inyector, solenoide).", new[] { "IgnitionCoil", "Injector", "WastegateSolenoid", "PurgeValve", "CkpSensor", "VvtSolenoid", "EvapVentValve" }, ConditionKind.Always, ConditionKind.Hot));
            l.Register(M("heater_open", "Calefactor de sonda abierto", "electrical", EffectKind.InternalShort, 1, 1, 2, "replace", "", "La resistencia calefactora de la sonda está cortada.", new[] { "O2Narrowband", "O2Wideband", "O2Downstream" }, mech));
            l.Register(M("dribble", "Inyector gotea", "actuator", EffectKind.Dribble, 0.15, 0.7, 4, "replace", "", "Inyector no cierra bien: aporta combustible extra.", new[] { "Injector" }, mech));
            l.Register(M("can_stub_open", "Ramal CAN abierto", "network", EffectKind.WireOpen, 1, 1, 4, "repair_wire", "ipc", "Un módulo queda aislado del bus.", new[] { "CanBus" }, ConditionKind.Always, ConditionKind.Intermittent));
            l.Register(M("can_short", "Bus CAN en corto", "network", EffectKind.WireShortGround, 1, 1, 5, "repair_wire", "can_h", "CAN-H en corto a masa: bus off.", new[] { "CanBus" }, mech));
            return l;
        }
    }
}
