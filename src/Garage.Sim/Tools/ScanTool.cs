using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Garage.Sim.Components;
using Garage.Sim.Ecu;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Tools
{
    /// <summary>Available actuator tests.</summary>
    public enum ActuatorTest
    {
        /// <summary>Cut one injector and measure the rpm drop (cylinder balance).</summary>
        CylinderBalance,
        /// <summary>Run the radiator fan.</summary>
        Fan,
        /// <summary>Run the fuel pump with the engine stopped.</summary>
        FuelPump,
        /// <summary>Cycle the wastegate solenoid.</summary>
        Wastegate,
        /// <summary>Open the purge valve.</summary>
        Purge,
        /// <summary>Sweep the throttle body.</summary>
        ThrottleSweep,
    }

    /// <summary>
    /// OBD-II scan tool: codes per module, freeze frame, live data (with raw frames), readiness and actuator tests.
    /// Communicates only if the module is powered and reachable on the bus, like a real tool.
    /// </summary>
    public sealed class ScanTool
    {
        private readonly DiagnosticSession _s;

        internal ScanTool(DiagnosticSession session)
        {
            _s = session;
        }

        private Car Car => _s.Car;

        /// <summary>True if the module answers.</summary>
        public bool CanTalkTo(string module)
        {
            if (!Car.KeyOn)
            {
                return false;
            }

            CanModule? m = Car.Can.Get(module);
            if (m == null)
            {
                return false;
            }

            if (Car.Can.BusOff)
            {
                return false;
            }

            return m.Alive && m.Connected;
        }

        /// <summary>Connects and identifies the vehicle (mode 09).</summary>
        public ToolResult Connect()
        {
            if (!Car.KeyOn)
            {
                return _s.Charge(new ToolResult("SIN RESPUESTA: ponga el contacto (llave en ON).", TimeCosts.ScanConnect, false), "Escáner: conexión fallida (contacto quitado)");
            }

            var sb = new StringBuilder();
            sb.AppendLine("ELM-SIM v2.1  Protocolo: ISO 15765-4 CAN (11 bit, 500 kbaud)");
            sb.AppendLine($"VIN: {Obd2.Vin(Car)}   Vehículo: {Car.Definition.DisplayName}");
            foreach (CanModule m in Car.Can.Modules)
            {
                sb.AppendLine($"  Módulo {m.Name,-24} {(CanTalkTo(m.Id) ? "OK" : "NO RESPONDE")}");
            }

            return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.ScanConnect), "Escáner: conexión");
        }

        private DtcStore? Store(string module)
        {
            if (module == "ecm")
            {
                return Car.Ecu.Dtcs;
            }

            return Car.Can.Get(module)?.Dtcs;
        }

        /// <summary>Reads stored (mode 03) and pending (mode 07) codes of a module.</summary>
        public ToolResult ReadCodes(string module = "ecm", bool training = false)
        {
            if (!CanTalkTo(module))
            {
                return _s.Charge(new ToolResult($"{CanNetwork.NameFor(module)}: NO HAY COMUNICACIÓN", TimeCosts.ReadCodes, false), $"Escáner: {module} sin comunicación");
            }

            DtcStore store = Store(module)!;
            List<string> confirmed = store.ConfirmedCodes();
            List<string> pending = store.PendingCodes();
            List<string> permanent = store.PermanentCodes();
            var sb = new StringBuilder();
            sb.AppendLine($"== {CanNetwork.NameFor(module)} ==  MIL: {(store.MilOn ? (store.MilFlashing ? "PARPADEANDO" : "ENCENDIDA") : "apagada")}");
            sb.AppendLine($"Modo 03 (confirmados): {(confirmed.Count == 0 ? "ninguno" : "")}   [{Obd2.CodesFrame(3, confirmed)}]");
            foreach (string c in confirmed)
            {
                AppendCode(sb, c, store, training);
            }

            sb.AppendLine($"Modo 07 (pendientes): {(pending.Count == 0 ? "ninguno" : "")}");
            foreach (string c in pending)
            {
                if (!confirmed.Contains(c))
                {
                    AppendCode(sb, c, store, training);
                }
            }

            if (permanent.Count > 0)
            {
                sb.AppendLine("Modo 0A (permanentes): " + string.Join(", ", permanent));
            }

            return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.ReadCodes), $"Escáner: lectura de códigos {module}");
        }

        private void AppendCode(StringBuilder sb, string code, DtcStore store, bool training)
        {
            DtcDefinition d = Car.Ecu.Catalog.Get(code);
            DtcRecord? r = store.Get(code);
            sb.AppendLine($"  {code}  {d.DescriptionEs}  ({r?.StatusText ?? ""}, ocurrencias {r?.Occurrences ?? 0})");
            if (training && d.Causes.Count > 0)
            {
                sb.AppendLine($"        Causas típicas: {string.Join("; ", d.Causes)}");
            }
        }

        /// <summary>Clears codes (mode 04).</summary>
        public ToolResult ClearCodes(string module = "ecm")
        {
            if (!CanTalkTo(module))
            {
                return _s.Charge(new ToolResult("NO HAY COMUNICACIÓN", TimeCosts.ClearCodes, false), $"Escáner: borrado {module} fallido");
            }

            if (module == "ecm")
            {
                Car.Ecu.ClearCodes();
            }
            else
            {
                Car.Can.Get(module)!.Dtcs.Clear();
            }

            string extra = Car.Engine.State.Rpm > 50 ? "" : "";
            return _s.Charge(new ToolResult("Códigos borrados (44). Monitores de disponibilidad reiniciados." + extra, TimeCosts.ClearCodes), $"Escáner: borrado de códigos {module}");
        }

        /// <summary>Freeze frame (mode 02).</summary>
        public ToolResult FreezeFrame()
        {
            if (!CanTalkTo("ecm"))
            {
                return _s.Charge(new ToolResult("NO HAY COMUNICACIÓN", TimeCosts.ReadCodes, false), "Escáner: freeze frame fallido");
            }

            FreezeFrame? f = Car.Ecu.Dtcs.FreezeFrame;
            if (f == null)
            {
                return _s.Charge(new ToolResult("Sin datos congelados almacenados.", TimeCosts.ReadCodes), "Escáner: freeze frame vacío");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Freeze frame — código {f.Code}");
            foreach (KeyValuePair<string, double> kv in f.Values)
            {
                PidDefinition? p = Obd2.FindByKey(kv.Key);
                sb.AppendLine($"  {(p?.Name ?? kv.Key),-36} {FormatValue(kv.Key, kv.Value),10} {p?.Unit ?? ""}");
            }

            return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.ReadCodes), "Escáner: freeze frame");
        }

        /// <summary>Formats a live value for display.</summary>
        public static string FormatValue(string key, double v)
        {
            if (key == "fuel_status")
            {
                return ((FuelSystemStatus)(int)v) switch
                {
                    FuelSystemStatus.ClosedLoop => "CL",
                    FuelSystemStatus.OpenLoopCold => "OL",
                    FuelSystemStatus.OpenLoopLoad => "OL-Drive",
                    FuelSystemStatus.OpenLoopFault => "OL-Fault",
                    FuelSystemStatus.ClosedLoopFault => "CL-Fault",
                    _ => "OFF",
                };
            }

            if (key.StartsWith("lambda", StringComparison.Ordinal))
            {
                return v.ToString("0.000", CultureInfo.InvariantCulture);
            }

            return v.ToString(Math.Abs(v) >= 100 ? "0" : "0.0#", CultureInfo.InvariantCulture);
        }

        /// <summary>Standard set of PIDs shown in the live data screen.</summary>
        public static readonly int[] DefaultLivePids = { 0x0C, 0x05, 0x0F, 0x0B, 0x10, 0x11, 0x04, 0x06, 0x07, 0x0E, 0x03, 0x14, 0x24, 0x15, 0x0A, 0x42, 0x0D, 0x44 };

        /// <summary>Live data readings (mode 01).</summary>
        public List<PidReading> ReadLive(IEnumerable<int>? pids = null)
        {
            var list = new List<PidReading>();
            if (!CanTalkTo("ecm"))
            {
                return list;
            }

            foreach (int pid in pids ?? DefaultLivePids)
            {
                PidReading? r = Obd2.ReadPid(Car, pid);
                if (r != null)
                {
                    list.Add(r);
                }
            }

            return list;
        }

        /// <summary>Live data screen as text (charges time).</summary>
        public ToolResult LiveData(IEnumerable<int>? pids = null, bool showFrames = false)
        {
            if (!CanTalkTo("ecm"))
            {
                return _s.Charge(new ToolResult("NO HAY COMUNICACIÓN CON EL MOTOR", TimeCosts.LiveData, false), "Escáner: datos en vivo fallido");
            }

            var sb = new StringBuilder();
            foreach (PidReading r in ReadLive(pids))
            {
                sb.Append($"  {r.Definition.Name,-36} {FormatValue(r.Definition.Key, r.Value),9} {r.Definition.Unit,-6}");
                if (showFrames)
                {
                    sb.Append("  ").Append(r.Frame);
                }

                sb.AppendLine();
            }

            sb.AppendLine($"  {"Corrección LTFT celda ralentí / crucero",-36} {Car.Ecu.LtftIdle * 100,4:0.0} / {Car.Ecu.LtftCruise * 100:0.0} %");
            sb.AppendLine($"  {"Retardo por detonación",-36} {Car.Ecu.KnockRetard,9:0.0} °");
            if (Car.Definition.Engine.IsTurbo)
            {
                sb.AppendLine($"  {"Presión turbo real / objetivo",-36} {Car.Ecu.Live["boost_kpa"],5:0} / {Car.Ecu.Live["boost_target_kpa"]:0} kPa");
            }

            sb.Append($"  {"Fallos de encendido (último ciclo)",-36} ");
            for (int i = 0; i < Car.Ecu.MisfireCounts.Length; i++)
            {
                sb.Append($"C{i + 1}:{Car.Ecu.MisfireCounts[i]} ");
            }

            return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.LiveData), "Escáner: datos en vivo");
        }

        /// <summary>Readiness monitors (PID 01).</summary>
        public ToolResult Readiness()
        {
            if (!CanTalkTo("ecm"))
            {
                return _s.Charge(new ToolResult("NO HAY COMUNICACIÓN", TimeCosts.ReadCodes, false), "Escáner: readiness fallido");
            }

            byte[] b = Obd2.MonitorStatus(Car);
            var sb = new StringBuilder();
            sb.AppendLine($"PID 01: 41 01 {b[0]:X2} {b[1]:X2} {b[2]:X2} {b[3]:X2}   MIL {(Car.Ecu.Dtcs.MilOn ? "ON" : "OFF")}, DTC {b[0] & 0x7F}");
            foreach (ReadinessMonitor m in Car.Ecu.Readiness.Supported)
            {
                sb.AppendLine($"  {MonitorName(m),-28} {(Car.Ecu.Readiness.IsComplete(m) ? "COMPLETO" : "INCOMPLETO")}");
            }

            return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.ReadCodes), "Escáner: monitores de disponibilidad");
        }

        /// <summary>Spanish monitor name.</summary>
        public static string MonitorName(ReadinessMonitor m) => m switch
        {
            ReadinessMonitor.Misfire => "Fallos de encendido",
            ReadinessMonitor.FuelSystem => "Sistema de combustible",
            ReadinessMonitor.Components => "Componentes",
            ReadinessMonitor.Catalyst => "Catalizador",
            ReadinessMonitor.HeatedCatalyst => "Catalizador calefactado",
            ReadinessMonitor.Evap => "Sistema EVAP",
            ReadinessMonitor.SecondaryAir => "Aire secundario",
            ReadinessMonitor.O2Sensor => "Sondas lambda",
            ReadinessMonitor.O2Heater => "Calefactores de sondas",
            _ => "EGR",
        };

        /// <summary>Runs an actuator test.</summary>
        public ToolResult RunActuatorTest(ActuatorTest test, int cylinder = 0)
        {
            if (!CanTalkTo("ecm"))
            {
                return _s.Charge(new ToolResult("NO HAY COMUNICACIÓN", TimeCosts.ActuatorTest, false), "Escáner: prueba de actuador fallida");
            }

            Car car = Car;
            switch (test)
            {
                case ActuatorTest.CylinderBalance:
                    {
                        if (!car.Engine.State.Running)
                        {
                            return _s.Charge(new ToolResult("La prueba de equilibrado requiere el motor en marcha.", 1, false), "Escáner: equilibrado sin motor");
                        }

                        var sb = new StringBuilder("Equilibrado de cilindros (corte de inyector, caída de rpm):\n");
                        int n = car.Definition.Engine.Cylinders;
                        car.Ecu.FreezeIdle = true;
                        car.RunFor(1);
                        var drops = new double[n];
                        for (int c = 0; c < n; c++)
                        {
                            double before = car.Engine.State.Rpm;
                            Component? inj = car.Parts.Find(ComponentKind.Injector, c);
                            if (inj == null)
                            {
                                continue;
                            }

                            var cut = new Faults.FaultInstance(Faults.FailureModeLibrary.Default.Get("dead"), inj.Id, 1) { Origin = "test" };
                            car.AddFault(cut);
                            car.RunFor(2);
                            double during = car.Engine.State.Rpm;
                            cut.Repaired = true;
                            cut.ForceActive(false);
                            car.InvalidateCircuits();
                            car.RunFor(3);
                            drops[c] = Math.Max(0, before - during);
                        }

                        double maxDrop = 1;
                        foreach (double d in drops)
                        {
                            maxDrop = Math.Max(maxDrop, d);
                        }

                        for (int c = 0; c < n; c++)
                        {
                            sb.AppendLine($"  Cilindro {c + 1}: -{drops[c],4:0} rpm {(drops[c] < maxDrop * 0.35 ? "  <-- aporta poco o nada" : "")}");
                        }

                        car.Ecu.FreezeIdle = false;
                        return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.ActuatorTest * 2), "Escáner: equilibrado de cilindros");
                    }

                case ActuatorTest.Fan:
                    {
                        bool ok = car.FanRelayId.Length == 0 || car.ActuatorEnergized(car.FanRelayId);
                        Component? fan = car.Parts.Find(ComponentKind.CoolingFan);
                        bool fanOk = fan == null || !car.Faults.Has(fan.Id, Faults.EffectKind.Dead);
                        string s = ok && fanOk ? "Ventilador activado: se oye girar." : (ok ? "Relé conmuta (clic) pero el ventilador no gira." : "No se oye el relé ni el ventilador.");
                        return _s.Charge(new ToolResult(s, TimeCosts.ActuatorTest), "Escáner: prueba de ventilador");
                    }

                case ActuatorTest.FuelPump:
                    {
                        bool relay = car.PumpRelayId.Length == 0 || (car.ActuatorEnergized(car.PumpRelayId) && !car.Faults.Has(car.PumpRelayId, Faults.EffectKind.StuckOpen));
                        string s = relay ? "Relé de bomba activado (clic). " : "No se oye el relé de bomba. ";
                        car.Ecu.Outputs.FuelPumpRelay = true;
                        car.RunFor(0.02);
                        s += car.Engine.State.FuelRailKpa > 100 ? "Se oye zumbido de bomba en el depósito." : "No se oye la bomba.";
                        return _s.Charge(new ToolResult(s, TimeCosts.ActuatorTest), "Escáner: prueba de bomba");
                    }

                case ActuatorTest.Wastegate:
                    {
                        Component? wg = car.Parts.Find(ComponentKind.WastegateSolenoid);
                        if (wg == null)
                        {
                            return _s.Charge(new ToolResult("El vehículo no tiene electroválvula de turbo.", 1, false), "Escáner: wastegate n/a");
                        }

                        bool e = car.ActuatorEnergized(wg.Id);
                        return _s.Charge(new ToolResult(e ? "Electroválvula de wastegate: se oye tic-tic al 50 % de ciclo." : "Electroválvula de wastegate: no hace ruido.", TimeCosts.ActuatorTest), "Escáner: prueba wastegate");
                    }

                case ActuatorTest.Purge:
                    {
                        Component? p = car.Parts.Find(ComponentKind.PurgeValve);
                        if (p == null)
                        {
                            return _s.Charge(new ToolResult("Sin válvula de purga.", 1, false), "Escáner: purga n/a");
                        }

                        bool e = car.ActuatorEnergized(p.Id);
                        return _s.Charge(new ToolResult(e ? "Válvula de purga: hace clic al activarla." : "Válvula de purga: no responde.", TimeCosts.ActuatorTest), "Escáner: prueba purga");
                    }

                default:
                    {
                        bool e = car.ActuatorEnergized(ComponentKind.ElectronicThrottle);
                        bool stuck = car.Faults.Has(car.Parts.IdOf(ComponentKind.ElectronicThrottle), Faults.EffectKind.StuckOpen);
                        string s = !e ? "La mariposa no se mueve (motor sin corriente)." : (stuck ? "La mariposa se mueve a tirones y se queda clavada." : "Barrido de mariposa 0→100 %: TPS sigue a la orden.");
                        return _s.Charge(new ToolResult(s, TimeCosts.ActuatorTest), "Escáner: barrido de mariposa");
                    }
            }
        }
    }
}
