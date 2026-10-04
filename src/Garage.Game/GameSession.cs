using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Garage.Data;
using Garage.Data.Json;
using Garage.Sim.Components;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Vehicle;

namespace Garage.Game
{
    /// <summary>
    /// Application layer of a game: the whole loop the CLI and Unity share (board, quotes, shop and stock, repairs,
    /// delivery, money, reputation, time, upgrades, save slots, tutorial). Game rules live in Garage.Sim; this class
    /// orchestrates them, validates commands with readable errors and publishes <see cref="GameEvent"/>s.
    /// </summary>
    public sealed class GameSession
    {
        private readonly Dictionary<string, CarWork> _work = new Dictionary<string, CarWork>(StringComparer.OrdinalIgnoreCase);
        private bool _syncing;
        private double _lastMoney, _lastRep, _lastMinute;
        private int _lastDay;

        /// <summary>Creates a new game.</summary>
        public GameSession(ContentDatabase content, ulong seed, bool training)
            : this(content, new Workshop(content, seed), training)
        {
        }

        private GameSession(ContentDatabase content, Workshop ws, bool training)
        {
            Content = content;
            Workshop = ws;
            Training = training;
            Events.Raised += OnAnyEvent;
            Snapshot();
        }

        /// <summary>Content.</summary>
        public ContentDatabase Content { get; }

        /// <summary>Workshop (meta-game state, rules).</summary>
        public Workshop Workshop { get; private set; }

        /// <summary>Training mode.</summary>
        public bool Training { get; set; }

        /// <summary>Event hub (subscribe once; never poll).</summary>
        public GameEventBus Events { get; } = new GameEventBus();

        /// <summary>Stock, orders, cart and old-parts box.</summary>
        public Inventory Inventory { get; private set; } = new Inventory();

        /// <summary>Job whose car is on the lift.</summary>
        public Job? ActiveJob { get; private set; }

        /// <summary>Work context of the active car (null if none).</summary>
        public CarWork? Work => ActiveJob == null ? null : WorkFor(ActiveJob);

        /// <summary>Running tutorial (null if none).</summary>
        public TutorialRunner? Tutorial { get; private set; }

        /// <summary>Money.</summary>
        public double Money => Workshop.Money;

        /// <summary>Reputation 0–100.</summary>
        public double Reputation => Workshop.Reputation;

        /// <summary>Day.</summary>
        public int Day => Workshop.Day;

        /// <summary>Clock text (HH:MM).</summary>
        public string Clock => $"{(int)(Workshop.Minute / 60):00}:{(int)(Workshop.Minute % 60):00}";

        /// <summary>Jobs accepted and not delivered.</summary>
        public List<Job> ActiveJobs => Workshop.Jobs.Where(j => j.Status == JobStatus.InProgress).ToList();

        /// <summary>Work context of a job (created on first use).</summary>
        public CarWork WorkFor(Job job)
        {
            if (!_work.TryGetValue(job.Definition.Id, out CarWork w))
            {
                w = new CarWork(job.Car, job, Workshop, Events, Training);
                _work[job.Definition.Id] = w;
            }

            return w;
        }

        /// <summary>Sandbox context (no economy, all tools) for a bare car.</summary>
        public static CarWork Sandbox(Car car, bool training, GameEventBus? bus = null) =>
            new CarWork(car, null, null, bus ?? new GameEventBus(), training) { ScannerPlugged = true };

        // ------------------------------------------------------------ state sync

        private void Snapshot()
        {
            _lastMoney = Workshop.Money;
            _lastRep = Workshop.Reputation;
            _lastDay = Workshop.Day;
            _lastMinute = Workshop.Minute;
        }

        private void OnAnyEvent(GameEvent e)
        {
            if (!_syncing)
            {
                Sync();
            }
        }

        /// <summary>Publishes state changes (money, reputation, clock, new day, deliveries, messages).</summary>
        public void Sync()
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            try
            {
                if (Workshop.Day != _lastDay)
                {
                    for (int d = _lastDay + 1; d <= Workshop.Day; d++)
                    {
                        Events.Publish(GameEventKind.DayStarted, "", $"Día {d}", d);
                        List<string> arrived = Inventory.Receive(d);
                        if (arrived.Count > 0)
                        {
                            Events.Publish(GameEventKind.PartsDelivered, string.Join(",", arrived), $"Han llegado {arrived.Count} pedidos de piezas.");
                        }
                    }
                }

                if (Math.Abs(Workshop.Money - _lastMoney) > 1e-6)
                {
                    Events.Publish(GameEventKind.MoneyChanged, "", "", Workshop.Money);
                }

                if (Math.Abs(Workshop.Reputation - _lastRep) > 1e-6)
                {
                    Events.Publish(GameEventKind.ReputationChanged, "", "", Workshop.Reputation);
                }

                if (Math.Abs(Workshop.Minute - _lastMinute) > 1e-6 || Workshop.Day != _lastDay)
                {
                    Events.Publish(GameEventKind.TimeChanged, "", Clock, Workshop.Minute);
                }

                foreach (string m in Workshop.Messages)
                {
                    Events.Publish(GameEventKind.MessagePosted, "", m);
                }

                Workshop.Messages.Clear();

                // Jobs cancelled by the clock (deadline) leave the lift.
                if (ActiveJob != null && ActiveJob.Status != JobStatus.InProgress)
                {
                    ActiveJob = null;
                    Events.Publish(GameEventKind.ActiveJobChanged);
                }

                Snapshot();
            }
            finally
            {
                _syncing = false;
            }
        }

        private CommandResult Fail(CommandError e, string msg)
        {
            Events.Publish(GameEventKind.CommandFailed, "", msg);
            return CommandResult.Fail(e, msg);
        }

        /// <summary>Front-end (presentation) event: player moved, picked a tool, plugged the scanner…</summary>
        public void Notify(GameEventKind kind, string subject = "")
        {
            if (kind == GameEventKind.ScannerPlugged || kind == GameEventKind.ScannerUnplugged)
            {
                if (Work == null)
                {
                    return;
                }

                Work.ScannerPlugged = kind == GameEventKind.ScannerPlugged;
            }

            Events.Publish(kind, subject);
        }

        // ---------------------------------------------------------------- board

        /// <summary>Job board offers.</summary>
        public List<Job> Offers(int wanted = 3)
        {
            int before = Workshop.Jobs.Count;
            List<Job> offers = Workshop.Offers(wanted);
            if (Workshop.Jobs.Count != before)
            {
                Events.Publish(GameEventKind.OffersRefreshed);
            }

            return offers;
        }

        /// <summary>Finds a job by id.</summary>
        public Job? FindJob(string id) => Workshop.Jobs.FirstOrDefault(j => string.Equals(j.Definition.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Turns an offer down.</summary>
        public CommandResult RejectOffer(Job job)
        {
            if (job.Status != JobStatus.Offered)
            {
                return Fail(CommandError.InvalidState, "Ese encargo ya no está en el tablón.");
            }

            job.Status = JobStatus.Cancelled;
            Events.Publish(GameEventKind.JobRejected, job.Definition.Id, $"Has rechazado el encargo de {job.Customer.Name}.");
            return CommandResult.Success("Encargo rechazado.");
        }

        /// <summary>New quote for a job, with the diagnosis hour.</summary>
        public QuoteDraft NewQuote(Job job) => new QuoteDraft(job, Workshop.LabourRate);

        /// <summary>Suggested amount.</summary>
        public double SuggestQuote(Job job) => Workshop.SuggestQuote(job);

        /// <summary>Sends a quote to the customer.</summary>
        public QuoteDecision SendQuote(Job job, double amount)
        {
            if (job.Status != JobStatus.Offered)
            {
                var d0 = new QuoteDecision(QuoteAnswer.Rejected, 0, "Ese encargo ya no está en el tablón.");
                Events.Publish(GameEventKind.CommandFailed, "", d0.Message);
                return d0;
            }

            if (amount <= 0)
            {
                RejectOffer(job);
                return new QuoteDecision(QuoteAnswer.Rejected, 0, "Encargo rechazado.");
            }

            QuoteDecision d = Workshop.EvaluateQuote(job, amount);
            switch (d.Answer)
            {
                case QuoteAnswer.Accepted:
                    Events.Publish(GameEventKind.JobAccepted, job.Definition.Id, d.Message, amount);
                    if (ActiveJob == null)
                    {
                        SetActiveJob(job);
                    }

                    break;
                case QuoteAnswer.Counter:
                    Events.Publish(GameEventKind.QuoteCountered, job.Definition.Id, d.Message, d.Amount);
                    break;
                default:
                    Events.Publish(GameEventKind.JobRejected, job.Definition.Id, d.Message);
                    break;
            }

            Sync();
            return d;
        }

        /// <summary>Sends a work-sheet quote.</summary>
        public QuoteDecision SendQuote(QuoteDraft q) => SendQuote(q.Job, q.Total);

        /// <summary>Puts a job's car on the lift.</summary>
        public CommandResult SetActiveJob(Job? job)
        {
            if (job != null && job.Status != JobStatus.InProgress)
            {
                return Fail(CommandError.InvalidState, "Primero acepta el encargo.");
            }

            if (ActiveJob != null && Work != null)
            {
                Work.ScannerPlugged = false;
            }

            ActiveJob = job;
            Events.Publish(GameEventKind.ActiveJobChanged, job?.Definition.Id ?? "");
            return CommandResult.Success(job == null ? "Elevador libre." : $"{job.Car.Definition.DisplayName} en el elevador.");
        }

        // ------------------------------------------------------------ shop/stock

        /// <summary>Parts that fit a component of a car, cheapest first per quality.</summary>
        public List<PartDefinition> PartsFor(Car car, string componentId)
        {
            Component? c = car.Parts.Get(componentId);
            return c == null ? new List<PartDefinition>() : Content.Parts.For(c.Kind, car.Definition.Id);
        }

        /// <summary>Catalogue with optional filters (car compatibility, component kind, quality).</summary>
        public List<PartDefinition> Catalog(string? carId = null, ComponentKind? kind = null, PartQuality? quality = null, string? text = null)
        {
            IEnumerable<PartDefinition> q = Content.Parts.All;
            if (!string.IsNullOrEmpty(carId))
            {
                q = q.Where(p => p.FitsCar(carId!));
            }

            if (kind != null)
            {
                q = q.Where(p => p.Kind == kind);
            }

            if (quality != null)
            {
                q = q.Where(p => p.Quality == quality);
            }

            if (!string.IsNullOrEmpty(text))
            {
                q = q.Where(p => p.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return q.OrderBy(p => p.Kind.ToString(), StringComparer.Ordinal).ThenBy(p => p.Price).ToList();
        }

        /// <summary>Adds a part to the cart.</summary>
        public CommandResult AddToCart(string partId, int qty = 1)
        {
            PartDefinition? p = Content.Parts.Get(partId);
            if (p == null || qty <= 0)
            {
                return Fail(CommandError.NotFound, "No existe esa pieza.");
            }

            CartLine? line = Inventory.Cart.FirstOrDefault(l => l.Part.Id == p.Id);
            if (line == null)
            {
                Inventory.Cart.Add(new CartLine { Part = p, Quantity = qty });
            }
            else
            {
                line.Quantity += qty;
            }

            return CommandResult.Success($"{p.Name} ×{qty} al carrito.");
        }

        /// <summary>Removes a part from the cart.</summary>
        public void RemoveFromCart(string partId) => Inventory.Cart.RemoveAll(l => l.Part.Id == partId);

        /// <summary>Pays the cart: immediate parts go to stock, the rest are ordered.</summary>
        public CommandResult Checkout()
        {
            if (Inventory.Cart.Count == 0)
            {
                return Fail(CommandError.InvalidState, "El carrito está vacío.");
            }

            double total = Inventory.CartTotal;
            if (Workshop.Money < total)
            {
                return Fail(CommandError.NotEnoughMoney, $"No hay dinero suficiente: el pedido cuesta {total:0.00} € y tienes {Workshop.Money:0.00} €.");
            }

            Workshop.Money -= total;
            var ids = new List<string>();
            int ordered = 0;
            foreach (CartLine l in Inventory.Cart)
            {
                int lead = Inventory.LeadTimeDays(l.Part.Quality);
                if (lead == 0)
                {
                    Inventory.Add(l.Part.Id, l.Quantity);
                }
                else
                {
                    Inventory.Orders.Add(new PartOrder { PartId = l.Part.Id, Quantity = l.Quantity, ArrivalDay = Workshop.Day + lead });
                    ordered++;
                }

                ids.Add(l.Part.Id);
            }

            Inventory.Cart.Clear();
            string msg = $"Compra de {total:0.00} €." + (ordered > 0 ? $" {ordered} referencias llegarán en los próximos días." : " Todo en stock.");
            Events.Publish(GameEventKind.PartsOrdered, string.Join(",", ids), msg, total);
            return CommandResult.Success(msg);
        }

        // ---------------------------------------------------------------- repair

        /// <summary>
        /// Fits a part on the active car. Uses stock; with <paramref name="buyIfMissing"/> the part is bought on the
        /// spot from the local supplier (urgent purchase, immediate, decision D-58).
        /// </summary>
        public CommandResult InstallPart(string componentId, string partId, bool buyIfMissing = false)
        {
            Job? job = ActiveJob;
            if (job == null)
            {
                return Fail(CommandError.NoActiveJob, "No hay ningún coche en el elevador.");
            }

            Component? comp = job.Car.Parts.Get(componentId);
            PartDefinition? part = Content.Parts.Get(partId);
            if (comp == null || part == null)
            {
                return Fail(CommandError.NotFound, "Pieza o componente no válido.");
            }

            if (part.Kind != comp.Kind)
            {
                return Fail(CommandError.IncompatiblePart, $"La pieza {part.Name} no corresponde a {comp.Name}.");
            }

            if (!part.FitsCar(job.Car.Definition.Id))
            {
                return Fail(CommandError.IncompatiblePart, $"{part.Name} no es compatible con el {job.Car.Definition.DisplayName}.");
            }

            bool fromStock = Inventory.Count(part.Id) > 0;
            if (!fromStock && !buyIfMissing)
            {
                return Fail(CommandError.NotInStock, $"No tienes {part.Name} en el almacén: cómprala en la tienda.");
            }

            if (!fromStock && Workshop.Money < part.Price)
            {
                return Fail(CommandError.NotEnoughMoney, "No hay dinero suficiente para comprar la pieza.");
            }

            // Ground truth of the removed part, revealed when it is inspected in the old-parts box.
            List<FaultInstance> faults = job.Car.Faults.Unrepaired().Where(f => string.Equals(f.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(f.Pin)).ToList();
            var removed = new RemovedPart
            {
                JobId = job.Definition.Id,
                ComponentId = componentId,
                Name = comp.Name,
                Day = Workshop.Day,
                WasFaulty = faults.Count > 0 || comp.Health < 0.6,
                Finding = faults.Count > 0
                    ? "Defecto apreciable en banco: " + string.Join("; ", faults.Select(f => f.Mode.Name))
                    : comp.Health < 0.6 ? "Pieza muy desgastada." : "Pieza en buen estado: no se aprecia ningún defecto.",
            };

            if (fromStock)
            {
                Inventory.Take(part.Id);
            }

            string msg = Workshop.InstallPart(job, componentId, partId, prepaid: fromStock);
            Inventory.OldParts.Add(removed);
            Events.Publish(GameEventKind.PartRemoved, componentId, $"{comp.Name} desmontado a la caja de piezas viejas.");
            Events.Publish(GameEventKind.PartInstalled, componentId, msg);
            return CommandResult.Success(msg);
        }

        /// <summary>Bench inspection of a removed part.</summary>
        public CommandResult InspectOldPart(int index)
        {
            if (index < 0 || index >= Inventory.OldParts.Count)
            {
                return Fail(CommandError.NotFound, "No existe esa pieza en la caja.");
            }

            RemovedPart p = Inventory.OldParts[index];
            p.Inspected = true;
            Workshop.SpendMinutes(5);
            Sync();
            return CommandResult.Success($"{p.Name}: {p.Finding}");
        }

        /// <summary>Repairs wiring of a component on the active car.</summary>
        public CommandResult RepairWiring(string componentId, string? pin)
        {
            if (ActiveJob == null)
            {
                return Fail(CommandError.NoActiveJob, "No hay ningún coche en el elevador.");
            }

            string msg = Workshop.RepairWiring(ActiveJob, componentId, pin);
            Events.Publish(GameEventKind.WiringRepaired, componentId, msg);
            return CommandResult.Success(msg);
        }

        /// <summary>Tops up coolant.</summary>
        public CommandResult RefillCoolant()
        {
            if (ActiveJob == null)
            {
                return Fail(CommandError.NoActiveJob, "No hay ningún coche en el elevador.");
            }

            ActiveJob.Car.RefillCoolant();
            Work!.Charge(10, "Rellenar refrigerante");
            return CommandResult.Success("Refrigerante rellenado.");
        }

        // ------------------------------------------------------------- delivery

        /// <summary>Final test, invoice and delivery of a job.</summary>
        public JobOutcome? Deliver(Job job, out CommandResult result)
        {
            if (job.Status != JobStatus.InProgress)
            {
                result = Fail(CommandError.InvalidState, "Ese encargo no está en curso.");
                return null;
            }

            JobOutcome o = Workshop.Deliver(job);
            _work.Remove(job.Definition.Id);
            if (ActiveJob == job)
            {
                ActiveJob = null;
                Events.Publish(GameEventKind.ActiveJobChanged);
            }

            Events.Publish(GameEventKind.JobDelivered, job.Definition.Id, o.Success ? "Trabajo terminado" : "El cliente no queda satisfecho", o.Payment);
            result = CommandResult.Success($"Cobrado {o.Payment:0.00} €, reputación {o.ReputationDelta:+0.0;-0.0}.");
            return o;
        }

        // ------------------------------------------------------- time & upgrades

        /// <summary>Closes the workshop for today.</summary>
        public void EndDay()
        {
            Workshop.EndDay();
            Sync();
        }

        /// <summary>Spends time on a manual task (moving the car, tidying…).</summary>
        public void SpendMinutes(double minutes)
        {
            Workshop.SpendMinutes(minutes);
            Sync();
        }

        /// <summary>Buys a tool or upgrade.</summary>
        public CommandResult BuyUpgrade(string id)
        {
            UpgradeDefinition? u = Content.Upgrades.FirstOrDefault(x => x.Id == id);
            if (u == null)
            {
                return Fail(CommandError.NotFound, "No existe esa mejora.");
            }

            if (Workshop.Has(id))
            {
                return Fail(CommandError.InvalidState, "Ya lo tienes.");
            }

            if (Workshop.Reputation < u.ReputationRequired)
            {
                return Fail(CommandError.ReputationTooLow, $"Necesitas reputación {u.ReputationRequired:0} (tienes {Workshop.Reputation:0}).");
            }

            if (Workshop.Money < u.Price)
            {
                return Fail(CommandError.NotEnoughMoney, $"Dinero insuficiente: cuesta {u.Price:0} € y tienes {Workshop.Money:0} €.");
            }

            string msg = Workshop.Buy(id);
            Events.Publish(GameEventKind.UpgradeBought, id, msg);
            return CommandResult.Success(msg);
        }

        // -------------------------------------------------------------- tutorial

        /// <summary>Available tutorials.</summary>
        public List<TutorialDefinition> Tutorials() => TutorialDefinition.LoadAll(Content);

        /// <summary>Starts a tutorial: puts its scripted job first on the board.</summary>
        public CommandResult StartTutorial(string id, int stepIndex = 0)
        {
            TutorialDefinition? def = Tutorials().FirstOrDefault(t => t.Id == id);
            if (def == null)
            {
                return Fail(CommandError.NotFound, "No existe ese tutorial.");
            }

            string jobId = def.Id + "_job";
            if (FindJob(jobId) == null && def.CarId.Length > 0)
            {
                var jd = new JobDefinition
                {
                    Id = jobId,
                    CustomerId = def.CustomerId,
                    CarId = def.CarId,
                    ScenarioId = def.ScenarioId,
                    Complaint = def.Complaint,
                    Budget = def.Budget,
                    DeadlineDays = def.DeadlineDays,
                    Goal = JobGoal.Repair,
                };
                Workshop.RestoreJob(Workshop.BuildJob(jd, StableSeed(jobId)));
                Workshop.MarkJobUsed(jobId);
                Events.Publish(GameEventKind.OffersRefreshed);
            }

            Tutorial?.Stop();
            Tutorial = new TutorialRunner(def, Events, Training, stepIndex);
            Events.Publish(GameEventKind.TutorialStepChanged, Tutorial.Current?.Id ?? "");
            return CommandResult.Success(def.Name);
        }

        /// <summary>Skips the running tutorial.</summary>
        public void SkipTutorial() => Tutorial?.Skip();

        private ulong StableSeed(string s)
        {
            ulong h = 1469598103934665603UL ^ Workshop.Seed;
            foreach (char c in s)
            {
                h = (h ^ c) * 1099511628211UL;
            }

            return h;
        }

        // ----------------------------------------------------------------- saves

        /// <summary>Serialises the whole game (workshop + session state).</summary>
        public JsonValue ToJson()
        {
            JsonValue root = SaveGame.ToJson(Workshop);
            JsonValue s = JsonValue.NewObject();
            s.Set("training", Training);
            s.Set("activeJob", ActiveJob?.Definition.Id ?? "");
            JsonValue stock = JsonValue.NewObject();
            foreach (KeyValuePair<string, int> kv in Inventory.Stock)
            {
                stock.Set(kv.Key, kv.Value);
            }

            s.Set("stock", stock);
            JsonValue orders = JsonValue.NewArray();
            foreach (PartOrder o in Inventory.Orders)
            {
                orders.Add(JsonValue.NewObject().Set("part", o.PartId).Set("qty", o.Quantity).Set("day", o.ArrivalDay));
            }

            s.Set("orders", orders);
            JsonValue old = JsonValue.NewArray();
            foreach (RemovedPart p in Inventory.OldParts)
            {
                old.Add(JsonValue.NewObject().Set("job", p.JobId).Set("component", p.ComponentId).Set("name", p.Name).Set("day", p.Day)
                    .Set("finding", p.Finding).Set("faulty", p.WasFaulty).Set("inspected", p.Inspected));
            }

            s.Set("oldParts", old);
            if (Tutorial != null && !Tutorial.Finished)
            {
                s.Set("tutorial", JsonValue.NewObject().Set("id", Tutorial.Definition.Id).Set("step", Tutorial.Index));
            }

            root.Set("session", s);
            root.Set("summary", JsonValue.NewObject().Set("day", Workshop.Day).Set("money", Workshop.Money).Set("reputation", Workshop.Reputation)
                .Set("car", ActiveJob?.Car.Definition.DisplayName ?? "").Set("clock", Clock));
            return root;
        }

        /// <summary>Replaces this game with a saved one (subscribers stay attached).</summary>
        public void LoadJson(JsonValue root)
        {
            Workshop loaded = SaveGame.FromJson(Content, root);
            Tutorial?.Stop();
            Tutorial = null;
            _work.Clear();
            Workshop = loaded;
            Inventory = new Inventory();
            ActiveJob = null;
            JsonValue s = root["session"];
            Training = s.Bool("training", Training);
            foreach (KeyValuePair<string, JsonValue> kv in s["stock"].Members)
            {
                Inventory.Add(kv.Key, (int)kv.Value.NumberValue);
            }

            foreach (JsonValue o in s["orders"].Items)
            {
                Inventory.Orders.Add(new PartOrder { PartId = o.Str("part"), Quantity = o.Int("qty", 1), ArrivalDay = o.Int("day") });
            }

            foreach (JsonValue p in s["oldParts"].Items)
            {
                Inventory.OldParts.Add(new RemovedPart
                {
                    JobId = p.Str("job"),
                    ComponentId = p.Str("component"),
                    Name = p.Str("name"),
                    Day = p.Int("day"),
                    Finding = p.Str("finding"),
                    WasFaulty = p.Bool("faulty"),
                    Inspected = p.Bool("inspected"),
                });
            }

            Job? active = FindJob(s.Str("activeJob"));
            ActiveJob = active != null && active.Status == JobStatus.InProgress ? active : null;
            Snapshot();
            if (s["tutorial"].Has("id"))
            {
                StartTutorial(s["tutorial"].Str("id"), s["tutorial"].Int("step"));
            }

            Events.Publish(GameEventKind.MoneyChanged, "", "", Workshop.Money);
            Events.Publish(GameEventKind.ReputationChanged, "", "", Workshop.Reputation);
            Events.Publish(GameEventKind.TimeChanged, "", Clock, Workshop.Minute);
            Events.Publish(GameEventKind.ActiveJobChanged, ActiveJob?.Definition.Id ?? "");
        }

        /// <summary>Loads a new session from JSON.</summary>
        public static GameSession FromJson(ContentDatabase content, JsonValue root)
        {
            var s = new GameSession(content, 1, false);
            s.LoadJson(root);
            return s;
        }

        /// <summary>Saves to a file.</summary>
        public void SaveFile(string path, string slotName = "")
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, ToJson().ToJson());
            Events.Publish(GameEventKind.GameSaved, slotName.Length > 0 ? slotName : path, $"Partida guardada (día {Day}).");
        }

        /// <summary>Loads from a file into this session.</summary>
        public void LoadFile(string path, string slotName = "")
        {
            LoadJson(JsonValue.Parse(File.ReadAllText(path)));
            Events.Publish(GameEventKind.GameLoaded, slotName.Length > 0 ? slotName : path, $"Partida cargada: día {Day}, {Money:0} €.");
        }
    }

    /// <summary>Summary of a save slot.</summary>
    public sealed class SaveSlotInfo
    {
        /// <summary>Slot number.</summary>
        public int Slot { get; set; }

        /// <summary>File path.</summary>
        public string Path { get; set; } = "";

        /// <summary>Has a save.</summary>
        public bool Used { get; set; }

        /// <summary>Saved at (local time).</summary>
        public DateTime SavedAt { get; set; }

        /// <summary>Readable summary.</summary>
        public string Summary { get; set; } = "";
    }

    /// <summary>Numbered save slots in a directory (slot_1.json…).</summary>
    public sealed class SaveSlots
    {
        /// <summary>Number of slots.</summary>
        public const int Count = 5;

        /// <summary>Creates the slot manager.</summary>
        public SaveSlots(string directory)
        {
            Directory = directory;
        }

        /// <summary>Folder.</summary>
        public string Directory { get; }

        /// <summary>Path of a slot.</summary>
        public string PathOf(int slot) => System.IO.Path.Combine(Directory, $"slot_{slot}.json");

        /// <summary>Lists all slots.</summary>
        public List<SaveSlotInfo> List()
        {
            var list = new List<SaveSlotInfo>();
            for (int i = 1; i <= Count; i++)
            {
                string p = PathOf(i);
                var info = new SaveSlotInfo { Slot = i, Path = p, Summary = "(vacía)" };
                if (File.Exists(p))
                {
                    try
                    {
                        JsonValue root = JsonValue.Parse(File.ReadAllText(p));
                        JsonValue s = root["summary"];
                        info.Used = true;
                        info.SavedAt = DateTime.TryParse(root.Str("savedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t) ? t.ToLocalTime() : File.GetLastWriteTime(p);
                        string car = s.Str("car");
                        info.Summary = $"Día {s.Int("day")} {s.Str("clock")} · {s.Num("money"):0} € · rep. {s.Num("reputation"):0}{(car.Length > 0 ? " · " + car : "")}";
                    }
                    catch (JsonParseException)
                    {
                        info.Summary = "(dañada)";
                    }
                }

                list.Add(info);
            }

            return list;
        }

        /// <summary>Saves a session to a slot.</summary>
        public void Save(GameSession s, int slot) => s.SaveFile(PathOf(slot), "slot_" + slot);

        /// <summary>Loads a slot into a session.</summary>
        public CommandResult Load(GameSession s, int slot)
        {
            string p = PathOf(slot);
            if (!File.Exists(p))
            {
                return CommandResult.Fail(CommandError.NotFound, "Ranura vacía.");
            }

            s.LoadFile(p, "slot_" + slot);
            return CommandResult.Success($"Partida cargada: día {s.Day}.");
        }
    }
}
