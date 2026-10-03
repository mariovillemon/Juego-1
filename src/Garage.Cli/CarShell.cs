using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Garage.Data;
using Garage.Sim.Components;
using Garage.Sim.Dyno;
using Garage.Sim.Ecu;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Maps;
using Garage.Sim.Tools;
using Garage.Sim.Vehicle;

namespace Garage.Cli;

/// <summary>Working on one car: tools, repairs, ECU editor, dyno.</summary>
public sealed class CarShell
{
    private readonly Ui _ui;
    private readonly Car _car;
    private readonly Job? _job;
    private readonly Workshop? _ws;
    private readonly bool _training;
    private readonly DiagnosticSession _session;
    private double _chargedMinutes;

    public CarShell(Ui ui, Car car, Job? job, Workshop? ws, bool training)
    {
        _ui = ui;
        _car = car;
        _job = job;
        _ws = ws;
        _training = training;
        _session = new DiagnosticSession(car, ws?.TimeFactor ?? 1.0);
    }

    private bool Owns(string tool) => _ws == null || _ws.Has(tool);

    private void SyncTime()
    {
        double delta = _session.MinutesSpent - _chargedMinutes;
        _chargedMinutes = _session.MinutesSpent;
        if (_ws != null && _job != null && delta > 0)
        {
            _ws.ChargeDiagnosis(_job, delta);
        }
    }

    private string Status()
    {
        var s = _car.Engine.State;
        string engine = s.Rpm > 300 ? $"motor en marcha {s.Rpm:0} rpm" : (_car.KeyOn ? "contacto puesto, motor parado" : "contacto quitado");
        string clock = _ws != null ? $" | Día {_ws.Day} {(int)(_ws.Minute / 60):00}:{(int)(_ws.Minute % 60):00} | {_ws.Money:0} €" : "";
        return $"{_car.Definition.DisplayName} — {engine} | ECT {s.CoolantC:0} °C | {(_car.Ecu.Dtcs.MilOn ? "MIL ENCENDIDA" : "MIL apagada")}{clock}";
    }

    public void Run()
    {
        while (true)
        {
            SyncTime();
            int o = _ui.Menu(Status(), new[]
            {
                "Ficha del coche y queja del cliente",
                "Motor: arrancar / parar / prueba de conducción",
                "Escáner OBD-II",
                "Multímetro y esquemas eléctricos",
                "Osciloscopio",
                "Pruebas mecánicas (presión, compresión, fugas, humo, inspección)",
                "Reparar: sustituir pieza / reparar cableado",
                "ECU: ver y editar mapas",
                "Banco de potencia",
                _training ? "Formación: razonamiento diagnóstico" : "(modo realista: sin pistas)",
            });
            switch (o)
            {
                case 0: return;
                case 1: Sheet(); break;
                case 2: EngineMenu(); break;
                case 3: ScannerMenu(); break;
                case 4: MeterMenu(); break;
                case 5: ScopeMenu(); break;
                case 6: MechanicalMenu(); break;
                case 7: RepairMenu(); break;
                case 8: new EcuEditor(_ui, _car, Owns("tool_ecu_flash")).Run(); break;
                case 9: DynoMenu(); break;
                case 10:
                    if (_training)
                    {
                        _ui.Title("Formación");
                        foreach (string t in TrainingAdvisor.Advise(_car))
                        {
                            _ui.Info("• " + t);
                        }
                    }
                    else
                    {
                        _ui.Info("Modo realista: no hay pistas. Arranca la partida con --formacion para ver el razonamiento.");
                    }

                    break;
            }
        }
    }

    private void Sheet()
    {
        _ui.Title("Ficha");
        CarDefinition d = _car.Definition;
        _ui.Info($"{d.DisplayName} — {d.Segment}{(d.Experimental ? "  [MODELO EXPERIMENTAL]" : "")}");
        _ui.Info($"Motor: {d.Engine.Name}, {d.Engine.DisplacementL:0.0} L, {d.Engine.Cylinders} cil. | VIN {d.Vin}");
        _ui.Info($"Kilómetros: {_car.OdometerKm:0} | Antigüedad {d.Appearance.AgeYears:0} años | Mantenimiento {d.Appearance.Maintenance:P0}");
        _ui.Info($"Aspecto: suciedad {d.Appearance.Dirt:P0}, óxido {d.Appearance.Rust:P0}, grasa {d.Appearance.Grease:P0}");
        if (_job != null)
        {
            _ui.Info($"Cliente: {_job.Customer.Name} ({_job.Customer.Description})");
            _ui.Info($"Queja: «{_job.Definition.Complaint}»");
            _ui.Info($"Objetivo: {GoalText(_job.Definition)} | Presupuesto aceptado: {_job.QuotedAmount:0} € | Plazo: {_job.Definition.DeadlineDays} días");
            if (_job.Lines.Count > 0)
            {
                _ui.Info("Trabajos realizados:");
                foreach (InvoiceLine l in _job.Lines)
                {
                    _ui.Info($"   {l.Description,-70} {l.Amount,8:0.00} €");
                }
            }
        }
    }

    public static string GoalText(JobDefinition d) => d.Goal switch
    {
        JobGoal.Power => $"más potencia (≥ {d.PowerTargetPs:0} CV) sin comprometer la fiabilidad",
        JobGoal.Track => $"preparación para circuito (≥ {d.PowerTargetPs:0} CV y aguantar pasadas seguidas)",
        JobGoal.Inspection => "pasar la inspección técnica (emisiones, sin MIL, monitores listos)",
        _ => "reparar la avería",
    };

    private void EngineMenu()
    {
        int o = _ui.Menu("Motor", new[] { "Arrancar", "Parar (quitar contacto)", "Poner contacto sin arrancar", "Ralentí 1 min y describir lo que se percibe", "Prueba de carretera (crucero 2 min a 2500 rpm)", "Acelerón en vacío" });
        switch (o)
        {
            case 1:
                _ui.Info(_car.Start() ? $"Arranca. Ralentí {_car.Engine.State.Rpm:0} rpm." : "El motor gira pero no arranca.");
                Charge(2, "Arranque");
                break;
            case 2:
                _car.Key = KeyPosition.Off;
                _car.RunFor(1);
                _ui.Info("Contacto quitado (fin de ciclo de conducción OBD).");
                break;
            case 3:
                _car.Key = KeyPosition.On;
                _car.RunFor(2.5);
                _ui.Info("Contacto puesto: se oye la bomba de combustible cebando." + (_car.Engine.State.FuelRailKpa < 100 ? " (¿o no? no se oye la bomba)" : ""));
                break;
            case 4:
                if (_car.Engine.State.Rpm < 300 && !_car.Start())
                {
                    _ui.Info("No arranca.");
                    break;
                }

                _car.Mode = LoadMode.Neutral;
                _car.Gear = 0;
                _car.Pedal = 0;
                _car.RunFor(60);
                DescribeCues();
                Charge(2, "Escuchar el motor al ralentí");
                break;
            case 5:
                if (_car.Engine.State.Rpm < 300 && !_car.Start())
                {
                    _ui.Info("No arranca.");
                    break;
                }

                int gear = Math.Min(3, _car.Definition.GearRatios.Length);
                _car.Gear = gear;
                _car.Mode = LoadMode.DynoHoldRpm;
                double ratio = _car.Definition.GearRatios[gear - 1] * _car.Definition.FinalDrive;
                _car.DynoHoldRpm = 2500;
                _car.SetVehicleSpeed(2500 / 60.0 * 2 * Math.PI / ratio * _car.Definition.WheelRadiusM * 3.6);
                _car.Pedal = 0.3;
                _car.RunFor(120);
                _car.Pedal = 0;
                _car.Mode = LoadMode.Neutral;
                _car.Gear = 0;
                _car.SetVehicleSpeed(0);
                _car.RunFor(5);
                DescribeCues();
                Charge(15, "Prueba de carretera");
                break;
            case 6:
                if (_car.Engine.State.Rpm < 300 && !_car.Start())
                {
                    _ui.Info("No arranca.");
                    break;
                }

                _car.Pedal = 1;
                _car.RunFor(1.2);
                double peak = _car.Engine.State.Rpm;
                _car.Pedal = 0;
                _car.RunFor(3);
                _ui.Info($"Sube hasta {peak:0} rpm y vuelve a ralentí.");
                DescribeCues();
                break;
        }
    }

    private void DescribeCues()
    {
        var cues = _car.Cues;
        if (cues.Count == 0)
        {
            _ui.Info("No se percibe nada anormal.");
        }

        foreach (SensoryCue c in cues.OrderByDescending(c => c.Intensity))
        {
            _ui.Info($"[{CueName(c.Channel)}] {c.Description}");
        }
    }

    private static string CueName(CueChannel c) => c switch
    {
        CueChannel.Sound => "oído",
        CueChannel.Smoke => "humo",
        CueChannel.Smell => "olor",
        CueChannel.Vibration => "vibración",
        CueChannel.Warning => "testigo",
        _ => "vista",
    };

    private void Charge(double minutes, string what)
    {
        _session.Charge(new ToolResult("", minutes), what);
    }

    private void ScannerMenu()
    {
        if (!Owns("tool_scanner"))
        {
            _ui.Info("No tienes escáner.");
            return;
        }

        while (true)
        {
            int o = _ui.Menu("Escáner OBD-II", new[] { "Conectar / identificar", "Leer códigos (motor)", "Leer códigos de otro módulo", "Borrar códigos", "Freeze frame", "Datos en vivo (se actualizan)", "Datos en vivo con tramas hexadecimales", "Monitores de disponibilidad (readiness)", "Pruebas de actuadores" });
            switch (o)
            {
                case 0: return;
                case 1: Show(_session.Scanner.Connect()); break;
                case 2: Show(_session.Scanner.ReadCodes("ecm", _training)); break;
                case 3:
                    {
                        string m = _ui.Ask("Módulo (ecm, abs, ipc, bcm)");
                        Show(_session.Scanner.ReadCodes(m.ToLowerInvariant(), _training));
                        break;
                    }

                case 4:
                    {
                        string m = _ui.Ask("Módulo a borrar (ecm, abs, ipc, bcm)");
                        Show(_session.Scanner.ClearCodes(string.IsNullOrEmpty(m) ? "ecm" : m.ToLowerInvariant()));
                        break;
                    }

                case 5: Show(_session.Scanner.FreezeFrame()); break;
                case 6:
                case 7:
                    {
                        int frames = (int)_ui.AskNumber("Número de refrescos (1 s cada uno)", 3);
                        for (int i = 0; i < Math.Max(1, Math.Min(30, frames)); i++)
                        {
                            if (i > 0)
                            {
                                _car.RunFor(1);
                            }

                            _ui.Info($"--- t+{i} s ---");
                            Show(_session.Scanner.LiveData(null, o == 7));
                        }

                        break;
                    }

                case 8: Show(_session.Scanner.Readiness()); break;
                case 9:
                    {
                        int t = _ui.Menu("Prueba de actuador", new[] { "Equilibrado de cilindros (corte de inyectores)", "Ventilador", "Bomba de combustible", "Electroválvula de wastegate", "Válvula de purga", "Barrido de mariposa" });
                        if (t > 0)
                        {
                            Show(_session.Scanner.RunActuatorTest((ActuatorTest)(t - 1)));
                        }

                        break;
                    }
            }
        }
    }

    private void Show(ToolResult r)
    {
        foreach (string line in r.Display.Split('\n'))
        {
            _ui.Info(line.TrimEnd('\r'));
        }
    }

    private void MeterMenu()
    {
        if (!Owns("tool_multimeter"))
        {
            _ui.Info("No tienes multímetro.");
            return;
        }

        while (true)
        {
            Multimeter m = _session.Multimeter;
            int o = _ui.Menu($"Multímetro [{m.Mode}, escala {m.Range}]", new[] { "Esquema de pines de un componente", "Seleccionar función (V / Ω / continuidad)", "Seleccionar escala", "Medir entre dos puntos", "Desconectar conector", "Conectar conector", "Listar componentes con circuito" });
            switch (o)
            {
                case 0: return;
                case 1: Show(m.WiringDiagram(_ui.Ask("Componente (p. ej. ect, map, inj1)"))); break;
                case 2:
                    {
                        int f = _ui.Menu("Función", new[] { "Voltios DC", "Ohmios", "Continuidad" });
                        if (f > 0)
                        {
                            m.Mode = (MeterMode)(f - 1);
                        }

                        break;
                    }

                case 3:
                    {
                        string[] names = Enum.GetNames(typeof(MeterRange));
                        int f = _ui.Menu("Escala", names);
                        if (f > 0)
                        {
                            m.Range = (MeterRange)(f - 1);
                        }

                        break;
                    }

                case 4:
                    {
                        _ui.Info("Puntos: componente:lado:pin (lado = ecu | harness | comp), o gnd / bat. Ej.: map:harness:ref");
                        string red = _ui.Ask("Punta roja");
                        string black = _ui.Ask("Punta negra");
                        Show(m.Measure(red, black));
                        break;
                    }

                case 5: Show(m.SetConnector(_ui.Ask("Componente"), false)); break;
                case 6: Show(m.SetConnector(_ui.Ask("Componente"), true)); break;
                case 7:
                    foreach (KeyValuePair<string, Garage.Sim.Electrical.Circuit> c in _car.Circuits)
                    {
                        _ui.Info($"{c.Key,-18} {_car.Parts.Get(c.Key)!.Name}");
                    }

                    break;
            }
        }
    }

    private void ScopeMenu()
    {
        if (!Owns("tool_scope"))
        {
            _ui.Info("No tienes osciloscopio (cómpralo en la tienda).");
            return;
        }

        string comp = _ui.Ask("Componente (ckp, cmp, inj1, coil1, o2_up, maf...)");
        string pin = _ui.Ask("Pin (signal, control...) [signal]");
        if (pin.Length == 0)
        {
            pin = "signal";
        }

        double window = _ui.AskNumber("Ventana en ms (≥200 graba en tiempo real) [40]", 40);
        Waveform w = _session.Scope.Capture(comp, pin, window);
        _ui.Line(w.RenderAscii());
    }

    private void MechanicalMenu()
    {
        while (true)
        {
            int o = _ui.Menu("Pruebas mecánicas", new[] { "Manómetro de combustible", "Compresión en seco", "Compresión en húmedo", "Prueba de fugas de un cilindro", "Máquina de humo (admisión)", "Máquina de humo (escape)", "Inspección visual de un componente", "Sondear la masa motor (caída de tensión en arranque)" });
            switch (o)
            {
                case 0: return;
                case 1:
                    if (!Owns("tool_fuel_gauge"))
                    {
                        _ui.Info("No tienes manómetro de combustible.");
                        break;
                    }

                    int f = _ui.Menu("Condición", new[] { "Contacto, sin arrancar", "Ralentí", "Plena carga (banco)", "Residual a los 5 minutos" });
                    if (f > 0)
                    {
                        Show(_session.Mechanical.FuelPressure((FuelPressureMode)(f - 1)));
                    }

                    break;
                case 2:
                case 3:
                    if (!Owns("tool_compression"))
                    {
                        _ui.Info("No tienes compresímetro.");
                        break;
                    }

                    Show(_session.Mechanical.Compression(o == 3));
                    break;
                case 4:
                    if (!Owns("tool_leakdown"))
                    {
                        _ui.Info("No tienes comprobador de fugas.");
                        break;
                    }

                    Show(_session.Mechanical.LeakDown((int)_ui.AskNumber("Cilindro", 1)));
                    break;
                case 5:
                case 6:
                    if (!Owns("tool_smoke"))
                    {
                        _ui.Info("No tienes máquina de humo.");
                        break;
                    }

                    Show(_session.Mechanical.Smoke(o == 6));
                    break;
                case 7: Show(_session.Mechanical.Inspect(_ui.Ask("Componente"))); break;
                case 8:
                    {
                        double before = _car.BatteryVolts;
                        _car.Key = KeyPosition.Crank;
                        _car.RunFor(0.5);
                        double during = _car.BatteryVolts;
                        _car.Key = KeyPosition.On;
                        _car.RunFor(0.5);
                        Show(_session.Charge(new ToolResult($"Tensión en batería en reposo {before:0.00} V; durante el arranque {during:0.00} V (debe quedar > 9,6 V).", 5), "Prueba de arranque"));
                        break;
                    }
            }
        }
    }

    private void RepairMenu()
    {
        while (true)
        {
            int o = _ui.Menu("Reparar", new[] { "Sustituir un componente", "Reparar cableado de un componente", "Rellenar refrigerante", "Ver lista de componentes" });
            switch (o)
            {
                case 0: return;
                case 1: Replace(); break;
                case 2:
                    {
                        string c = _ui.Ask("Componente");
                        string pin = _ui.Ask("Pin (vacío = todo el mazo del componente)");
                        string msg = _ws != null && _job != null ? _ws.RepairWiring(_job, c, pin.Length == 0 ? null : pin) : (_car.RepairWiring(c, pin.Length == 0 ? null : pin) > 0 ? "Reparado." : "No había daño en ese cableado.");
                        _ui.Info(msg);
                        break;
                    }

                case 3:
                    _car.RefillCoolant();
                    _ui.Info("Refrigerante rellenado.");
                    break;
                case 4:
                    foreach (Component c in _car.Parts.All)
                    {
                        _ui.Info($"{c.Id,-20} {c.Name,-48} {c.Location}");
                    }

                    break;
            }
        }
    }

    private void Replace()
    {
        string id = _ui.Ask("Componente a sustituir (id)");
        Component? comp = _car.Parts.Get(id);
        if (comp == null)
        {
            _ui.Info("No existe ese componente.");
            return;
        }

        if (_ws == null || _job == null)
        {
            _car.ReplaceComponent(id);
            _ui.Info($"(sandbox) {comp.Name} sustituido por uno nuevo.");
            return;
        }

        List<PartDefinition> parts = _ws.Content.Parts.For(comp.Kind, _car.Definition.Id);
        if (parts.Count == 0)
        {
            _ui.Info("No hay recambios para ese componente en el catálogo.");
            return;
        }

        var options = parts.Select(p => $"{p.Name,-52} {p.Price,8:0.00} € fiabilidad {p.Reliability:P0}").ToList();
        int n = _ui.Menu($"Recambios para {comp.Name} (mano de obra {comp.ReplaceMinutes:0} min)", options);
        if (n > 0)
        {
            _ui.Info(_ws.InstallPart(_job, id, parts[n - 1].Id));
        }
    }

    private void DynoMenu()
    {
        if (!Owns("tool_dyno"))
        {
            _ui.Info("No tienes banco de potencia (cómpralo en la tienda).");
            return;
        }

        int gear = (int)_ui.AskNumber($"Marcha (1-{_car.Definition.GearRatios.Length}) [3]", 3);
        DynoResult r = DynoRun.Pull(_car, gear);
        Charge(20, "Pasada en banco");
        _ui.Title("Banco de potencia");
        _ui.Info(r.Summary);
        _ui.Line(r.RenderAscii());
        foreach (string w in r.Warnings)
        {
            _ui.Info("¡! " + w);
        }

        if (_job != null && _job.BaselinePowerPs <= 0)
        {
            _job.BaselinePowerPs = r.PeakPowerPs;
        }

        if (_ui.Confirm("¿Exportar datalog a CSV?"))
        {
            string file = Path.Combine("saves", $"dyno_{_car.Definition.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            Directory.CreateDirectory("saves");
            r.Log.SaveCsv(file);
            _ui.Info($"Guardado {file} ({r.Log.Rows.Count} filas, {r.Log.Channels.Count} canales).");
        }
    }
}

/// <summary>ECU map viewer/editor in the style of a professional calibration tool.</summary>
public sealed class EcuEditor
{
    private readonly Ui _ui;
    private readonly Car _car;
    private readonly bool _canWrite;

    public EcuEditor(Ui ui, Car car, bool canWrite)
    {
        _ui = ui;
        _car = car;
        _canWrite = canWrite;
    }

    public void Run()
    {
        while (true)
        {
            EcuCalibration cal = _car.Ecu.Calibration;
            var tables = cal.Tables.Keys.OrderBy(k => k).ToList();
            int o = _ui.Menu($"Calibración {cal.Id} {(_canWrite ? "" : "(SOLO LECTURA: compra la interfaz de reprogramación)")}", new[] { "Ver una tabla", "Editar una celda", "Sumar un valor a una región", "Ver/editar escalares (limitadores, inyectores...)", "Comparar con el mapa de serie", "Restaurar mapa de serie" });
            switch (o)
            {
                case 0: return;
                case 1:
                    {
                        Map3D? t = PickTable(tables);
                        if (t != null)
                        {
                            Print(t);
                        }

                        break;
                    }

                case 2:
                case 3:
                    {
                        if (!_canWrite)
                        {
                            _ui.Info("Necesitas la interfaz de reprogramación para escribir en la ECU.");
                            break;
                        }

                        Map3D? t = PickTable(tables);
                        if (t == null)
                        {
                            break;
                        }

                        Print(t);
                        if (o == 2)
                        {
                            int r = (int)_ui.AskNumber($"Fila (0-{t.Y.Count - 1}, {t.Y.Name})", 0);
                            int c = (int)_ui.AskNumber($"Columna (0-{t.X.Count - 1}, {t.X.Name})", 0);
                            if (r < 0 || r >= t.Y.Count || c < 0 || c >= t.X.Count)
                            {
                                _ui.Info("Fuera de rango.");
                                break;
                            }

                            double v = _ui.AskNumber($"Nuevo valor (actual {t[r, c]:0.###})", t[r, c]);
                            t[r, c] = v;
                        }
                        else
                        {
                            int r0 = (int)_ui.AskNumber("Fila inicial", 0);
                            int r1 = (int)_ui.AskNumber("Fila final", t.Y.Count - 1);
                            int c0 = (int)_ui.AskNumber("Columna inicial", 0);
                            int c1 = (int)_ui.AskNumber("Columna final", t.X.Count - 1);
                            double d = _ui.AskNumber("Valor a sumar (negativo para restar)", 0);
                            t.AddToRegion(r0, r1, c0, c1, d);
                        }

                        _ui.Info("Escrito en la ECU (flash OK, checksum corregido).");
                        Print(t);
                        break;
                    }

                case 4:
                    {
                        var keys = cal.Scalars.Keys.OrderBy(k => k).ToList();
                        for (int i = 0; i < keys.Count; i++)
                        {
                            _ui.Info($"{i + 1,2}. {keys[i],-28} {cal.Scalars[keys[i]]:0.###}");
                        }

                        if (!_canWrite)
                        {
                            break;
                        }

                        int n = (int)_ui.AskNumber("Número a modificar (0 = ninguno)", 0);
                        if (n > 0 && n <= keys.Count)
                        {
                            cal.SetScalar(keys[n - 1], _ui.AskNumber("Nuevo valor", cal.Scalars[keys[n - 1]]));
                        }

                        break;
                    }

                case 5:
                    {
                        foreach (string id in tables)
                        {
                            Map3D a = cal.Table(id)!;
                            Map3D? b = _car.StockCalibration.Table(id);
                            if (b == null)
                            {
                                continue;
                            }

                            int changed = 0;
                            double maxDelta = 0;
                            for (int r = 0; r < a.Y.Count; r++)
                            {
                                for (int c = 0; c < a.X.Count; c++)
                                {
                                    double d = a[r, c] - b[r, c];
                                    if (Math.Abs(d) > 1e-9)
                                    {
                                        changed++;
                                        maxDelta = Math.Max(maxDelta, Math.Abs(d));
                                    }
                                }
                            }

                            _ui.Info($"{id,-20} celdas modificadas {changed,4}   máx. diferencia {maxDelta:0.###} {a.Unit}");
                        }

                        break;
                    }

                case 6:
                    if (_canWrite && _ui.Confirm("¿Restaurar el mapa de serie?"))
                    {
                        _car.Ecu.Calibration = _car.StockCalibration.Clone();
                        _ui.Info("Mapa de serie restaurado.");
                    }

                    break;
            }
        }
    }

    private Map3D? PickTable(List<string> tables)
    {
        int n = _ui.Menu("Tablas", tables.Select(t => $"{t} [{_car.Ecu.Calibration.Table(t)!.Unit}]").ToList());
        return n > 0 ? _car.Ecu.Calibration.Table(tables[n - 1]) : null;
    }

    private void Print(Map3D t)
    {
        t.Range(out double min, out double max);
        _ui.Title($"{t.Id} ({t.Unit})  filas: {t.Y.Name} [{t.Y.Unit}]  columnas: {t.X.Name} [{t.X.Unit}]   rango {min:0.##}…{max:0.##}");
        _ui.Line("  (intensidad del color = valor:  ░ bajo  ▒ medio  ▓ alto  █ máximo)");
        string fmt = max > 50 ? "0" : "0.0#";
        var header = "   fila |" + string.Concat(Enumerable.Range(0, t.X.Count).Select(c => t.X[c].ToString("0", CultureInfo.InvariantCulture).PadLeft(7)));
        _ui.Line(header);
        for (int r = 0; r < t.Y.Count; r++)
        {
            var line = $"{r,2} {t.Y[r].ToString("0.##", CultureInfo.InvariantCulture),4}|";
            for (int c = 0; c < t.X.Count; c++)
            {
                double v = t[r, c];
                double f = max - min < 1e-9 ? 0 : (v - min) / (max - min);
                char shade = f < 0.25 ? '░' : f < 0.5 ? '▒' : f < 0.75 ? '▓' : '█';
                line += (shade + v.ToString(fmt, CultureInfo.InvariantCulture)).PadLeft(7);
            }

            _ui.Line(line);
        }
    }
}
