using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Garage.Data;
using Garage.Game;
using Garage.Sim.Components;
using Garage.Sim.Dyno;
using Garage.Sim.Ecu;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Maps;
using Garage.Sim.Tools;
using Garage.Sim.Vehicle;

namespace Garage.Cli;

/// <summary>Working on one car: tools, repairs, ECU editor, dyno. Every action goes through <see cref="CarWork"/>.</summary>
public sealed class CarShell
{
    private readonly Ui _ui;
    private readonly CarWork _w;
    private readonly GameSession? _session;

    public CarShell(Ui ui, CarWork work, GameSession? session)
    {
        _ui = ui;
        _w = work;
        _session = session;
        _w.ScannerPlugged = true; // in the terminal the scan tool is always at hand
    }

    private Car _car => _w.Car;

    private Job? _job => _w.Job;

    private bool _training => _w.Training;

    private string Status()
    {
        string clock = _session != null ? $" | Día {_session.Day} {_session.Clock} | {_session.Money:0} €" : "";
        return _w.StatusLine + clock;
    }

    public void Run()
    {
        while (true)
        {
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
                case 8: new EcuEditorShell(_ui, _w).Run(); break;
                case 9: DynoMenu(); break;
                case 10:
                    if (_training)
                    {
                        _ui.Title("Formación");
                        foreach (string t in _w.Advice())
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

    private void Show(CommandResult r)
    {
        foreach (string line in r.Message.Split('\n'))
        {
            _ui.Info(line.TrimEnd('\r'));
        }
    }

    private void EngineMenu()
    {
        int o = _ui.Menu("Motor", new[] { "Arrancar", "Parar (quitar contacto)", "Poner contacto sin arrancar", "Ralentí 1 min y describir lo que se percibe", "Prueba de carretera (crucero 2 min a 2500 rpm)", "Acelerón en vacío" });
        switch (o)
        {
            case 1: Show(_w.Start()); break;
            case 2: Show(_w.Stop()); break;
            case 3: Show(_w.IgnitionOn()); break;
            case 4: Show(_w.ListenIdle()); break;
            case 5: Show(_w.RoadTest()); break;
            case 6: Show(_w.Rev()); break;
        }
    }

    private void ScannerMenu()
    {
        if (!_w.Owns(CarWork.Scanner))
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
                case 1: Show(_w.ScanConnect()); break;
                case 2: Show(_w.ReadCodes("ecm")); break;
                case 3: Show(_w.ReadCodes(_ui.Ask("Módulo (ecm, abs, ipc, bcm)").ToLowerInvariant())); break;
                case 4:
                    {
                        string m = _ui.Ask("Módulo a borrar (ecm, abs, ipc, bcm)");
                        Show(_w.ClearCodes(string.IsNullOrEmpty(m) ? "ecm" : m.ToLowerInvariant()));
                        break;
                    }

                case 5: Show(_w.FreezeFrame()); break;
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
                            Show(_w.LiveData(o == 7));
                        }

                        break;
                    }

                case 8: Show(_w.Readiness()); break;
                case 9:
                    {
                        int t = _ui.Menu("Prueba de actuador", new[] { "Equilibrado de cilindros (corte de inyectores)", "Ventilador", "Bomba de combustible", "Electroválvula de wastegate", "Válvula de purga", "Barrido de mariposa" });
                        if (t > 0)
                        {
                            Show(_w.ActuatorTest((ActuatorTest)(t - 1)));
                        }

                        break;
                    }
            }
        }
    }

    private void MeterMenu()
    {
        if (!_w.Owns(CarWork.Meter))
        {
            _ui.Info("No tienes multímetro.");
            return;
        }

        while (true)
        {
            Multimeter m = _w.Session.Multimeter;
            int o = _ui.Menu($"Multímetro [{m.Mode}, escala {m.Range}]", new[] { "Esquema de pines de un componente", "Seleccionar función (V / Ω / continuidad)", "Seleccionar escala", "Medir entre dos puntos", "Desconectar conector", "Conectar conector", "Listar componentes con circuito" });
            switch (o)
            {
                case 0: return;
                case 1: Show(_w.WiringDiagram(_ui.Ask("Componente (p. ej. ect, map, inj1)"))); break;
                case 2:
                    {
                        int f = _ui.Menu("Función", new[] { "Voltios DC", "Ohmios", "Continuidad" });
                        if (f > 0)
                        {
                            _w.SetMeterMode((MeterMode)(f - 1));
                        }

                        break;
                    }

                case 3:
                    {
                        string[] names = Enum.GetNames(typeof(MeterRange));
                        int f = _ui.Menu("Escala", names);
                        if (f > 0)
                        {
                            _w.SetMeterRange((MeterRange)(f - 1));
                        }

                        break;
                    }

                case 4:
                    {
                        _ui.Info("Puntos: componente:lado:pin (lado = ecu | harness | comp), o gnd / bat. Ej.: map:harness:ref");
                        string red = _ui.Ask("Punta roja");
                        string black = _ui.Ask("Punta negra");
                        Show(_w.Measure(red, black));
                        break;
                    }

                case 5: Show(_w.SetConnector(_ui.Ask("Componente"), false)); break;
                case 6: Show(_w.SetConnector(_ui.Ask("Componente"), true)); break;
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
        if (!_w.Owns(CarWork.Scope))
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
        _w.Capture(comp, pin, window, out Waveform? w);
        if (w != null)
        {
            _ui.Line(w.RenderAscii());
        }
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
                    if (!_w.Owns(CarWork.FuelGauge))
                    {
                        _ui.Info("No tienes manómetro de combustible.");
                        break;
                    }

                    int f = _ui.Menu("Condición", new[] { "Contacto, sin arrancar", "Ralentí", "Plena carga (banco)", "Residual a los 5 minutos" });
                    if (f > 0)
                    {
                        Show(_w.FuelPressure((FuelPressureMode)(f - 1)));
                    }

                    break;
                case 2:
                case 3: Show(_w.CompressionTest(o == 3)); break;
                case 4:
                    if (!_w.Owns(CarWork.LeakDown))
                    {
                        _ui.Info("No tienes comprobador de fugas.");
                        break;
                    }

                    Show(_w.LeakDownTest((int)_ui.AskNumber("Cilindro", 1)));
                    break;
                case 5:
                case 6: Show(_w.SmokeTest(o == 6)); break;
                case 7: Show(_w.Inspect(_ui.Ask("Componente"))); break;
                case 8: Show(_w.CrankingTest()); break;
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
                        if (_session != null)
                        {
                            Show(_session.RepairWiring(c, pin.Length == 0 ? null : pin));
                        }
                        else
                        {
                            _ui.Info(_car.RepairWiring(c, pin.Length == 0 ? null : pin) > 0 ? "Reparado." : "No había daño en ese cableado.");
                        }

                        break;
                    }

                case 3:
                    if (_session != null)
                    {
                        Show(_session.RefillCoolant());
                    }
                    else
                    {
                        _car.RefillCoolant();
                        _ui.Info("Refrigerante rellenado.");
                    }

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

        if (_session == null || _job == null)
        {
            _car.ReplaceComponent(id);
            _ui.Info($"(sandbox) {comp.Name} sustituido por uno nuevo.");
            return;
        }

        List<PartDefinition> parts = _session.PartsFor(_car, id);
        if (parts.Count == 0)
        {
            _ui.Info("No hay recambios para ese componente en el catálogo.");
            return;
        }

        var options = parts.Select(p => $"{p.Name,-52} {p.Price,8:0.00} € fiabilidad {p.Reliability:P0}{(_session.Inventory.Count(p.Id) > 0 ? $" [en stock: {_session.Inventory.Count(p.Id)}]" : "")}").ToList();
        int n = _ui.Menu($"Recambios para {comp.Name} (mano de obra {comp.ReplaceMinutes:0} min)", options);
        if (n > 0)
        {
            Show(_session.InstallPart(id, parts[n - 1].Id, buyIfMissing: true));
        }
    }

    private void DynoMenu()
    {
        if (!_w.Owns(CarWork.Dyno))
        {
            _ui.Info("No tienes banco de potencia (cómpralo en la tienda).");
            return;
        }

        int gear = (int)_ui.AskNumber($"Marcha (1-{_car.Definition.GearRatios.Length}) [3]", 3);
        CommandResult res = _w.RunDyno(gear);
        DynoResult r = _w.DynoRuns[_w.DynoRuns.Count - 1];
        _ui.Title("Banco de potencia");
        _ui.Info(r.Summary);
        _ui.Line(r.RenderAscii());
        foreach (string w in r.Warnings)
        {
            _ui.Info("¡! " + w);
        }

        if (!res.Ok)
        {
            _ui.Info(res.Message);
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

/// <summary>ECU map viewer/editor in the style of a professional calibration tool (uses <see cref="EcuEditor"/>).</summary>
public sealed class EcuEditorShell
{
    private readonly Ui _ui;
    private readonly CarWork _w;

    public EcuEditorShell(Ui ui, CarWork w)
    {
        _ui = ui;
        _w = w;
    }

    private EcuEditor Ecu => _w.Ecu;

    public void Run()
    {
        while (true)
        {
            bool canWrite = Ecu.CanWrite().Ok;
            List<string> tables = Ecu.TableIds;
            int o = _ui.Menu($"Calibración {Ecu.Calibration.Id} {(canWrite ? "" : "(SOLO LECTURA: compra la interfaz de reprogramación)")}", new[] { "Ver una tabla", "Editar una celda", "Sumar un valor a una región", "Ver/editar escalares (limitadores, inyectores...)", "Comparar con el mapa de serie", "Restaurar mapa de serie", "Deshacer el último cambio" });
            switch (o)
            {
                case 0: return;
                case 1:
                    {
                        string? t = PickTable(tables);
                        if (t != null)
                        {
                            Print(Ecu.Table(t)!);
                        }

                        break;
                    }

                case 2:
                case 3:
                    {
                        if (!canWrite)
                        {
                            _ui.Info(Ecu.CanWrite().Message);
                            break;
                        }

                        string? id = PickTable(tables);
                        if (id == null)
                        {
                            break;
                        }

                        Map3D t = Ecu.Table(id)!;
                        Print(t);
                        CommandResult r;
                        if (o == 2)
                        {
                            int row = (int)_ui.AskNumber($"Fila (0-{t.Y.Count - 1}, {t.Y.Name})", 0);
                            int c = (int)_ui.AskNumber($"Columna (0-{t.X.Count - 1}, {t.X.Name})", 0);
                            if (row < 0 || row >= t.Y.Count || c < 0 || c >= t.X.Count)
                            {
                                _ui.Info("Fuera de rango.");
                                break;
                            }

                            r = Ecu.SetCell(id, row, c, _ui.AskNumber($"Nuevo valor (actual {t[row, c]:0.###})", t[row, c]));
                        }
                        else
                        {
                            int r0 = (int)_ui.AskNumber("Fila inicial", 0);
                            int r1 = (int)_ui.AskNumber("Fila final", t.Y.Count - 1);
                            int c0 = (int)_ui.AskNumber("Columna inicial", 0);
                            int c1 = (int)_ui.AskNumber("Columna final", t.X.Count - 1);
                            r = Ecu.AddToRegion(id, r0, r1, c0, c1, _ui.AskNumber("Valor a sumar (negativo para restar)", 0));
                        }

                        _ui.Info(r.Message);
                        Print(Ecu.Table(id)!);
                        break;
                    }

                case 4:
                    {
                        var keys = Ecu.Calibration.Scalars.Keys.OrderBy(k => k).ToList();
                        for (int i = 0; i < keys.Count; i++)
                        {
                            _ui.Info($"{i + 1,2}. {keys[i],-28} {Ecu.Calibration.Scalars[keys[i]]:0.###}");
                        }

                        if (!canWrite)
                        {
                            break;
                        }

                        int n = (int)_ui.AskNumber("Número a modificar (0 = ninguno)", 0);
                        if (n > 0 && n <= keys.Count)
                        {
                            _ui.Info(Ecu.SetScalar(keys[n - 1], _ui.AskNumber("Nuevo valor", Ecu.Calibration.Scalars[keys[n - 1]])).Message);
                        }

                        break;
                    }

                case 5:
                    foreach (MapDiff d in Ecu.CompareWithStock())
                    {
                        _ui.Info($"{d.Table,-20} celdas modificadas {d.ChangedCells,4}   máx. diferencia {d.MaxDelta:0.###} {d.Unit}");
                    }

                    break;
                case 6:
                    if (canWrite && _ui.Confirm("¿Restaurar el mapa de serie?"))
                    {
                        Ecu.RestoreStock();
                        _ui.Info("Mapa de serie restaurado.");
                    }

                    break;
                case 7:
                    _ui.Info(Ecu.Undo().Message);
                    break;
            }
        }
    }

    private string? PickTable(List<string> tables)
    {
        int n = _ui.Menu("Tablas", tables.Select(t => $"{t} [{Ecu.Table(t)!.Unit}]").ToList());
        return n > 0 ? tables[n - 1] : null;
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
