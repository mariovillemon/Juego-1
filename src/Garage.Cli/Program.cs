using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Garage.Data;
using Garage.Data.Json;
using Garage.Sim.Dyno;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Vehicle;

namespace Garage.Cli;

/// <summary>Command line entry point.</summary>
public static class Program
{
    private const string Usage = @"Uso: garage [comando] [opciones]

Comandos:
  play                     Partida completa (por defecto)
  sandbox                  Trabajar directamente en un coche sin economía
  validate                 Valida todos los JSON contra sus schemas (incluye mods)
  dyno                     Pasada de banco rápida de un coche de serie
  list                     Lista coches, escenarios y encargos
  export-modes             Regenera data/base/failure_modes.json desde la librería integrada

Opciones:
  --formacion | --training Modo formación (explica el razonamiento diagnóstico)
  --realista               Modo realista (sin pistas) [por defecto]
  --seed N                 Semilla de la partida
  --car ID                 Coche (sandbox/dyno)
  --scenario ID            Escenario de avería (sandbox)
  --difficulty N           Avería aleatoria de dificultad 1-5 (sandbox)
  --load FICHERO           Cargar partida guardada
  --script FICHERO         Lee las respuestas de un fichero (una por línea), para demos y pruebas
  --data DIR               Directorio de datos (por defecto se busca ./data)
  --mods DIR               Directorio de mods (por defecto ./mods)";

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        var opts = ParseArgs(args, out string command);
        if (opts.ContainsKey("help") || command == "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        string? root = opts.TryGetValue("data", out string? d) ? d : ContentDatabase.FindDataRoot(Environment.CurrentDirectory) ?? ContentDatabase.FindDataRoot(AppContext.BaseDirectory);
        if (root == null || !Directory.Exists(root))
        {
            Console.Error.WriteLine("No encuentro el directorio data/ (use --data).");
            return 2;
        }

        string modsDir = opts.TryGetValue("mods", out string? m) ? m : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "mods");
        ContentDatabase db = ContentDatabase.Load(root, modsDir);
        bool training = opts.ContainsKey("formacion") || opts.ContainsKey("training");
        ulong seed = opts.TryGetValue("seed", out string? s) && ulong.TryParse(s, out ulong sv) ? sv : 20261003;
        Ui ui = opts.TryGetValue("script", out string? script)
            ? new Ui(Console.In, Console.Out, File.ReadAllLines(script))
            : new Ui(Console.In, Console.Out);

        switch (command)
        {
            case "validate":
                return Validate(db);
            case "export-modes":
                {
                    JsonValue arr = JsonValue.NewArray();
                    foreach (FailureModeDefinition fm in FailureModeLibrary.Default.All)
                    {
                        arr.Add(ContentDatabase.FailureModeToJson(fm));
                    }

                    JsonValue doc = JsonValue.NewObject().Set("$schema", "../schemas/failure_mode.schema.json").Set("modes", arr);
                    string path = Path.Combine(root, "base", "failure_modes.json");
                    File.WriteAllText(path, doc.ToJson() + "\n");
                    Console.WriteLine($"Escrito {path} ({arr.Count} modos).");
                    return 0;
                }

            case "list":
                Console.WriteLine("Coches:");
                foreach (string id in db.Ids("cars"))
                {
                    CarDefinition c = db.Car(id);
                    Console.WriteLine($"  {id,-20} {c.DisplayName,-32} {c.Engine.Name}{(c.Experimental ? " [experimental]" : "")}");
                }

                Console.WriteLine("Escenarios:");
                foreach (string id in db.Ids("scenarios"))
                {
                    Console.WriteLine($"  {id,-26} {db.Raw("scenarios")[id].Str("car"),-18} {db.Raw("scenarios")[id].Str("name")}");
                }

                Console.WriteLine("Encargos:");
                foreach (JobDefinition j in db.JobDefinitions)
                {
                    Console.WriteLine($"  {j.Id,-8} {j.CarId,-18} {j.Goal,-10} «{j.Complaint}»");
                }

                return 0;
            case "dyno":
                {
                    string carId = opts.TryGetValue("car", out string? cid) ? cid : "aurex_strada_gt";
                    Car car = db.CreateCar(carId, seed, warm: true);
                    DynoResult r = DynoRun.Pull(car);
                    Console.WriteLine(car.Definition.DisplayName);
                    Console.WriteLine(r.Summary);
                    Console.WriteLine(r.RenderAscii());
                    if (opts.TryGetValue("csv", out string? csv))
                    {
                        r.Log.SaveCsv(csv);
                        Console.WriteLine($"CSV: {csv}");
                    }

                    return 0;
                }

            case "sandbox":
                return Sandbox(db, ui, opts, seed, training);
            default:
                {
                    if (!db.Report.Ok)
                    {
                        Console.WriteLine("Atención: hay errores en los datos (ejecute 'garage validate').");
                    }

                    Workshop ws = opts.TryGetValue("load", out string? load) ? SaveGame.Load(db, load) : new Workshop(db, seed);
                    new GameShell(ui, db, ws, training).Run();
                    return 0;
                }
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args, out string command)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        command = "play";
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                string key = a.Substring(2);
                bool flag = i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal);
                o[key] = flag ? "true" : args[++i];
            }
            else if (a == "-h")
            {
                o["help"] = "true";
            }
            else
            {
                command = a.ToLowerInvariant();
            }
        }

        return o;
    }

    private static int Validate(ContentDatabase db)
    {
        LoadReport r = db.Report;
        foreach (string msg in r.Messages)
        {
            Console.WriteLine("  " + msg);
        }

        foreach (ValidationError e in r.Errors)
        {
            Console.WriteLine("ERROR " + e);
        }

        int carsOk = 0;
        foreach (string id in db.Ids("cars"))
        {
            try
            {
                db.CreateCar(id, 1);
                carsOk++;
            }
            catch (Exception ex) when (ex is ContentException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                Console.WriteLine($"ERROR coche {id}: {ex.Message}");
                return 1;
            }
        }

        Console.WriteLine($"{r.FilesRead} ficheros, {db.Catalog.Count} DTC, {db.FailureModes.Count} modos de fallo, {carsOk} coches, {db.Ids("scenarios").Count} escenarios, {db.JobDefinitions.Count} encargos.");
        Console.WriteLine(r.Ok ? "Datos válidos." : $"{r.Errors.Count} errores.");
        return r.Ok ? 0 : 1;
    }

    private static int Sandbox(ContentDatabase db, Ui ui, Dictionary<string, string> opts, ulong seed, bool training)
    {
        string carId = opts.TryGetValue("car", out string? c) ? c : "aurex_strada_gt";
        var faults = new List<FaultInstance>();
        if (opts.TryGetValue("scenario", out string? sc))
        {
            faults = db.ScenarioFaults(sc);
            carId = db.Raw("scenarios")[sc].Str("car");
        }
        else if (opts.TryGetValue("difficulty", out string? dif) && int.TryParse(dif, out int difficulty))
        {
            faults = new FaultGenerator(db.FailureModes).Generate(db.CreateCar(carId, seed), difficulty, new Garage.Sim.Core.DeterministicRandom(seed));
        }

        Car car = db.CreateCar(carId, seed, faults, warm: false);
        ui.Title($"Sandbox: {car.Definition.DisplayName} ({faults.Count} averías ocultas)");
        new CarShell(ui, car, null, null, training).Run();
        if (faults.Count > 0 && ui.Confirm("¿Revelar las averías?"))
        {
            foreach (FaultInstance f in car.Faults.All)
            {
                ui.Info($"{f.Mode.Name} en {car.Parts.Get(f.ComponentId)?.Name ?? f.ComponentId} {(string.IsNullOrEmpty(f.Pin) ? "" : "(pin " + f.Pin + ")")} — {f.Condition.Describe()} — {(f.Repaired ? "REPARADA" : "pendiente")}");
            }
        }

        return 0;
    }
}

/// <summary>Workshop level menus.</summary>
public sealed class GameShell
{
    private readonly Ui _ui;
    private readonly ContentDatabase _db;
    private Workshop _ws;
    private readonly bool _training;

    public GameShell(Ui ui, ContentDatabase db, Workshop ws, bool training)
    {
        _ui = ui;
        _db = db;
        _ws = ws;
        _training = training;
    }

    public void Run()
    {
        _ui.Title("TALLER VILLA-MOTOR — Simulador de diagnosis");
        _ui.Info(_training ? "Modo FORMACIÓN: verás el razonamiento diagnóstico." : "Modo REALISTA: sin pistas.");
        while (true)
        {
            FlushMessages();
            string header = $"Día {_ws.Day}  {(int)(_ws.Minute / 60):00}:{(int)(_ws.Minute % 60):00} | Caja {_ws.Money:0} € | Reputación {_ws.Reputation:0}/100";
            int o = _ui.Menu(header, new[] { "Tablón de encargos", "Coches en el taller", "Tienda de herramientas y mejoras", "Guardar partida", "Cargar partida", "Terminar el día" }, zeroIsBack: false);
            switch (o)
            {
                case 0:
                    if (_ui.Confirm("¿Guardar antes de salir?"))
                    {
                        Save();
                    }

                    return;
                case 1: Board(); break;
                case 2: Garage(); break;
                case 3: Shop(); break;
                case 4: Save(); break;
                case 5:
                    {
                        string f = _ui.Ask("Fichero [saves/partida.json]");
                        f = f.Length == 0 ? Path.Combine("saves", "partida.json") : f;
                        if (File.Exists(f))
                        {
                            _ws = SaveGame.Load(_db, f);
                            _ui.Info($"Partida cargada: día {_ws.Day}, {_ws.Money:0} €.");
                        }
                        else
                        {
                            _ui.Info("No existe ese fichero.");
                        }

                        break;
                    }

                case 6: _ws.EndDay(); break;
            }
        }
    }

    private void FlushMessages()
    {
        foreach (string m in _ws.Messages)
        {
            _ui.Info("» " + m);
        }

        _ws.Messages.Clear();
    }

    private void Save()
    {
        string f = _ui.Ask("Fichero [saves/partida.json]");
        f = f.Length == 0 ? Path.Combine("saves", "partida.json") : f;
        SaveGame.Save(_ws, f);
        _ui.Info($"Partida guardada en {f} (formato v{SaveGame.FormatVersion}).");
    }

    private void Board()
    {
        List<Job> offers = _ws.Offers(3);
        var labels = offers.Select(j => $"{j.Customer.Name,-26} {j.Car.Definition.DisplayName,-30} {j.Definition.Goal,-10} plazo {j.Definition.DeadlineDays} d").ToList();
        int n = _ui.Menu("Tablón de encargos", labels);
        if (n == 0)
        {
            return;
        }

        Job job = offers[n - 1];
        _ui.Title("Encargo");
        _ui.Info($"{job.Customer.Name}: {job.Customer.Description}");
        _ui.Info($"Coche: {job.Car.Definition.DisplayName}, {job.Car.OdometerKm:0} km");
        _ui.Info($"Dice: «{job.Definition.Complaint}»");
        _ui.Info($"Objetivo: {CarShell.GoalText(job.Definition)}");
        double suggested = _ws.SuggestQuote(job);
        double amount = _ui.AskNumber($"Presupuesto a proponer en € (sugerido {suggested:0}; 0 = rechazar el encargo)", suggested);
        if (amount <= 0)
        {
            job.Status = JobStatus.Cancelled;
            _ui.Info("Encargo rechazado.");
            return;
        }

        if (!_ws.ProposeQuote(job, amount))
        {
            FlushMessages();
            double retry = _ui.AskNumber("Nuevo presupuesto (0 = dejarlo)", 0);
            if (retry > 0 && job.Status == JobStatus.Offered)
            {
                _ws.ProposeQuote(job, retry);
            }
        }

        FlushMessages();
    }

    private void Garage()
    {
        var active = _ws.Jobs.Where(j => j.Status == JobStatus.InProgress).ToList();
        if (active.Count == 0)
        {
            _ui.Info("No hay coches en el taller. Acepta un encargo en el tablón.");
            return;
        }

        int n = _ui.Menu("Coches en el taller", active.Select(j => $"{j.Car.Definition.DisplayName,-30} de {j.Customer.Name} — día límite {j.AcceptedDay + j.Definition.DeadlineDays}").ToList());
        if (n == 0)
        {
            return;
        }

        Job job = active[n - 1];
        while (true)
        {
            int o = _ui.Menu($"{job.Car.Definition.DisplayName} — {job.Customer.Name}", new[] { "Trabajar en el coche", "Ver factura provisional", "Entregar el coche y cobrar" });
            if (o == 0)
            {
                return;
            }

            if (o == 1)
            {
                new CarShell(_ui, job.Car, job, _ws, _training).Run();
            }
            else if (o == 2)
            {
                _ui.Title("Factura provisional");
                foreach (InvoiceLine l in job.Lines)
                {
                    _ui.Info($"{l.Description,-72} {l.Amount,8:0.00} €");
                }

                _ui.Info($"Tiempo total en el coche: {job.LabourMinutes:0} min ({job.LabourMinutes / 60 * _ws.LabourRate:0.00} € de mano de obra) | Presupuesto: {job.QuotedAmount:0} €");
            }
            else
            {
                if (!_ui.Confirm("Se hará la prueba final y se entregará. ¿Seguro?"))
                {
                    continue;
                }

                JobOutcome r = _ws.Deliver(job);
                _ui.Title(r.Success ? "TRABAJO TERMINADO" : "EL CLIENTE NO QUEDA SATISFECHO");
                foreach (string note in r.Notes)
                {
                    _ui.Info("- " + note);
                }

                _ui.Info($"Cobrado: {r.Payment:0.00} €   Reputación {r.ReputationDelta:+0.0;-0.0}");
                if (_training)
                {
                    _ui.Info("Averías reales del coche (modo formación):");
                    foreach (FaultInstance f in job.Car.Faults.All.Where(f => f.Origin != "test"))
                    {
                        _ui.Info($"   {f.Mode.Name} en {job.Car.Parts.Get(f.ComponentId)?.Name} {(string.IsNullOrEmpty(f.Pin) ? "" : "(" + f.Pin + ")")} — {(f.Repaired ? "reparada" : "SIN REPARAR")}");
                    }
                }

                FlushMessages();
                return;
            }
        }
    }

    private void Shop()
    {
        var items = _ws.Content.Upgrades.ToList();
        int n = _ui.Menu("Tienda", items.Select(u => $"{(_ws.Has(u.Id) ? "[TIENES] " : "")}{u.Name,-40} {u.Price,8:0} €  rep ≥ {u.ReputationRequired:0}  — {u.Description}").ToList());
        if (n > 0)
        {
            _ui.Info(_ws.Buy(items[n - 1].Id));
        }
    }
}
