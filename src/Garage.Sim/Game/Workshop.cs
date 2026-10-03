using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Dyno;
using Garage.Sim.Ecu;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Game
{
    /// <summary>Content the meta-game needs (implemented by Garage.Data.ContentDatabase).</summary>
    public interface IGameContent
    {
        /// <summary>Car ids.</summary>
        IReadOnlyList<string> CarIds { get; }

        /// <summary>Builds a car with faults.</summary>
        Car CreateCar(string carId, ulong seed, IEnumerable<FaultInstance>? faults = null, bool warm = false, double ambientC = 20);

        /// <summary>Faults of a scenario.</summary>
        List<FaultInstance> ScenarioFaults(string scenarioId);

        /// <summary>Failure modes.</summary>
        FailureModeLibrary FailureModes { get; }

        /// <summary>Job definitions.</summary>
        IReadOnlyList<JobDefinition> JobDefinitions { get; }

        /// <summary>Customers.</summary>
        IReadOnlyList<CustomerProfile> Customers { get; }

        /// <summary>Parts.</summary>
        PartsCatalog Parts { get; }

        /// <summary>Upgrades and tools.</summary>
        IReadOnlyList<UpgradeDefinition> Upgrades { get; }
    }

    /// <summary>
    /// The player's workshop: money, reputation, time, tools, jobs, economy and comebacks.
    /// All randomness comes from a seeded generator so a save reproduces the same game.
    /// </summary>
    public sealed class Workshop
    {
        /// <summary>Opening time (minutes from midnight).</summary>
        public const double OpenMinute = 8 * 60;

        /// <summary>Closing time.</summary>
        public const double CloseMinute = 19 * 60;

        private readonly IGameContent _content;
        private readonly List<Job> _jobs = new List<Job>();
        private readonly List<Comeback> _comebacks = new List<Comeback>();
        private readonly HashSet<string> _owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tool_multimeter", "tool_scanner" };
        private readonly HashSet<string> _usedJobDefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DeterministicRandom _rng;

        /// <summary>Creates a new game.</summary>
        public Workshop(IGameContent content, ulong seed)
        {
            _content = content;
            Seed = seed;
            _rng = new DeterministicRandom(seed);
        }

        /// <summary>Game seed.</summary>
        public ulong Seed { get; }

        /// <summary>Money (€).</summary>
        public double Money { get; set; } = 3000;

        /// <summary>Reputation 0..100.</summary>
        public double Reputation { get; set; } = 20;

        /// <summary>Current day (1 based).</summary>
        public int Day { get; set; } = 1;

        /// <summary>Minute of the day.</summary>
        public double Minute { get; set; } = OpenMinute;

        /// <summary>Labour rate (€/h).</summary>
        public double LabourRate { get; set; } = 55;

        /// <summary>Mark-up on parts.</summary>
        public double PartsMarkup { get; set; } = 0.25;

        /// <summary>Jobs (all states).</summary>
        public IReadOnlyList<Job> Jobs => _jobs;

        /// <summary>Owned tools and upgrades.</summary>
        public IReadOnlyCollection<string> Owned => _owned;

        /// <summary>Pending comebacks.</summary>
        public IReadOnlyList<Comeback> Comebacks => _comebacks;

        /// <summary>Messages for the player (news feed).</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <summary>Content.</summary>
        public IGameContent Content => _content;

        /// <summary>Number of generated jobs (for unique ids).</summary>
        public int GeneratedCount { get; set; }

        /// <summary>Has a tool/upgrade.</summary>
        public bool Has(string id) => _owned.Contains(id);

        /// <summary>Grants an item (save loading).</summary>
        public void Grant(string id) => _owned.Add(id);

        /// <summary>Marks a data job as used (save loading).</summary>
        public void MarkJobUsed(string id) => _usedJobDefs.Add(id);

        /// <summary>Data jobs already used.</summary>
        public IReadOnlyCollection<string> UsedJobs => _usedJobDefs;

        /// <summary>Restores RNG state from a seed (save loading).</summary>
        public void ReseedRandom(ulong seed) => _rng = new DeterministicRandom(seed);

        /// <summary>Restores a job (save loading).</summary>
        public void RestoreJob(Job job) => _jobs.Add(job);

        /// <summary>Adds a comeback (save loading).</summary>
        public void AddComeback(Comeback c) => _comebacks.Add(c);

        /// <summary>Time factor for diagnostic actions (training upgrade).</summary>
        public double TimeFactor => Has("upg_training") ? 0.85 : 1.0;

        /// <summary>Spends workshop minutes; rolls over to the next day after closing.</summary>
        public void SpendMinutes(double minutes)
        {
            Minute += minutes;
            while (Minute >= CloseMinute)
            {
                Minute = OpenMinute + (Minute - CloseMinute);
                NewDay();
            }
        }

        /// <summary>Advances to the next morning.</summary>
        public void EndDay()
        {
            Minute = OpenMinute;
            NewDay();
        }

        private void NewDay()
        {
            Day++;
            double rent = 120;
            Money -= rent;
            Messages.Add($"Día {Day}: gastos fijos del local -{rent:0} €.");
            foreach (Comeback c in _comebacks.Where(c => c.Day <= Day).ToList())
            {
                _comebacks.Remove(c);
                Money -= c.Refund;
                Reputation = MathUtil.Clamp(Reputation - 6, 0, 100);
                Messages.Add($"¡Devolución! {c.Reason}. Reembolso -{c.Refund:0} € y reputación -6.");
            }

            foreach (Job j in _jobs.Where(j => j.Status == JobStatus.InProgress))
            {
                if (Day - j.AcceptedDay > j.Definition.DeadlineDays + j.Customer.PatienceDays / 2)
                {
                    j.Status = JobStatus.Cancelled;
                    Reputation = MathUtil.Clamp(Reputation - 8, 0, 100);
                    Messages.Add($"{j.Customer.Name} se ha llevado el coche sin terminar: plazo superado. Reputación -8.");
                }
            }
        }

        /// <summary>Offers on the job board (creates new ones if needed).</summary>
        public List<Job> Offers(int wanted = 3)
        {
            var offers = _jobs.Where(j => j.Status == JobStatus.Offered).ToList();
            while (offers.Count < wanted)
            {
                Job j = CreateOffer();
                _jobs.Add(j);
                offers.Add(j);
            }

            return offers;
        }

        private Job CreateOffer()
        {
            JobDefinition? def = _content.JobDefinitions.FirstOrDefault(d => !_usedJobDefs.Contains(d.Id));
            if (def != null)
            {
                _usedJobDefs.Add(def.Id);
                return BuildJob(def, _rng.NextULong());
            }

            // Procedural job: random customer, car and faults by reputation-based difficulty.
            GeneratedCount++;
            CustomerProfile cust = _rng.Pick(_content.Customers);
            string carId = _rng.Pick(_content.CarIds);
            int difficulty = 1 + (int)Math.Min(4, Reputation / 22);
            var gen = new JobDefinition
            {
                Id = $"gen_{GeneratedCount:000}",
                CustomerId = cust.Id,
                CarId = carId,
                Goal = JobGoal.Repair,
                GenerateDifficulty = difficulty,
                Complaint = "",
                Budget = cust.Budget,
                DeadlineDays = Math.Max(1, cust.PatienceDays),
            };
            return BuildJob(gen, _rng.NextULong());
        }

        /// <summary>Builds a job (car + faults) from a definition and seed. Deterministic.</summary>
        public Job BuildJob(JobDefinition def, ulong seed)
        {
            CustomerProfile cust = _content.Customers.FirstOrDefault(c => c.Id == def.CustomerId) ?? _content.Customers[0];
            var rng = new DeterministicRandom(seed);
            List<FaultInstance> faults;
            if (def.ScenarioId.Length > 0)
            {
                faults = _content.ScenarioFaults(def.ScenarioId);
            }
            else if (def.GenerateDifficulty > 0)
            {
                Car probe = _content.CreateCar(def.CarId, seed);
                faults = new FaultGenerator(_content.FailureModes).Generate(probe, def.GenerateDifficulty, rng.Fork(1));
            }
            else
            {
                faults = new List<FaultInstance>();
            }

            Car car = _content.CreateCar(def.CarId, seed, faults, warm: false);
            var job = new Job(def, cust, car, seed);
            job.OriginalFaults.AddRange(faults);
            string observed = CustomerDrive(car);
            if (def.Complaint.Length == 0)
            {
                def.Complaint = observed;
            }

            return job;
        }

        /// <summary>
        /// Simulates how the customer used the car before bringing it in (two cold-start trips), so it arrives with
        /// realistic DTC memory, adapted fuel trims and freeze frame. Returns what the customer noticed.
        /// </summary>
        public static string CustomerDrive(Car car, int trips = 2)
        {
            const double dt = 0.05;
            var noticed = new List<SensoryCue>();
            bool everStarted = false;
            for (int t = 0; t < trips; t++)
            {
                car.Soak(false);
                bool started = car.Start(4);
                everStarted |= started;
                if (started)
                {
                    car.Pedal = 0;
                    car.RunFor(60, dt);
                    noticed.AddRange(car.Cues);
                    int gear = Math.Min(3, car.Definition.GearRatios.Length);
                    car.Gear = gear;
                    car.Mode = LoadMode.DynoHoldRpm;
                    double ratio = car.Definition.GearRatios[gear - 1] * car.Definition.FinalDrive;
                    car.DynoHoldRpm = 2400;
                    car.SetVehicleSpeed(2400 / 60.0 * 2 * Math.PI / ratio * car.Definition.WheelRadiusM * 3.6);
                    car.Pedal = 0.3;
                    car.RunFor(420, dt);
                    car.Pedal = 0.9;
                    car.RunFor(6, dt);
                    noticed.AddRange(car.Cues);
                    car.Pedal = 0;
                    car.Mode = LoadMode.Neutral;
                    car.Gear = 0;
                    car.SetVehicleSpeed(0);
                    car.RunFor(30, dt);
                    noticed.AddRange(car.Cues);
                }

                car.Key = KeyPosition.Off;
                car.RunFor(1, dt);
            }

            // The car sat overnight at the workshop door.
            car.Soak(false);
            car.Clock.Spend(0);
            if (!everStarted)
            {
                return "No arranca: el motor gira pero no se pone en marcha.";
            }

            var best = noticed.Where(c => c.Id != "sound.fan" && c.Id != "sound.turbo_spool" || c.Description.Contains("sirena"))
                .GroupBy(c => c.Id).Select(g => g.OrderByDescending(c => c.Intensity).First())
                .OrderByDescending(c => c.Intensity).Take(2).ToList();
            if (best.Count == 0)
            {
                return car.Ecu.Dtcs.MilOn ? "Se le encendió la luz del motor; aparte de eso, va normal." : "Notaba algo raro, pero ahora mismo no sabría decir qué.";
            }

            return string.Join(". ", best.Select(c => c.Description)) + ".";
        }

        /// <summary>Estimated quote for a job (diagnosis + expected work).</summary>
        public double SuggestQuote(Job job)
        {
            double diagnosis = LabourRate * 1.0;
            return Math.Round(diagnosis + job.Customer.Budget * 0.5, 0);
        }

        /// <summary>Presents a quote; the customer accepts or rejects depending on budget and personality.</summary>
        public bool ProposeQuote(Job job, double amount)
        {
            double tolerance = job.Customer.Personality switch
            {
                "haggler" => 0.85,
                "enthusiast" => 1.25,
                "demanding" => 0.95,
                "impatient" => 1.1,
                _ => 1.0,
            };
            double limit = Math.Max(job.Customer.Budget, job.Definition.Budget) * tolerance * (0.9 + Reputation / 250);
            if (amount <= limit)
            {
                job.Status = JobStatus.InProgress;
                job.QuotedAmount = amount;
                job.AcceptedDay = Day;
                job.Lines.Add(new InvoiceLine("Diagnosis electrónica (1 h)", LabourRate));
                Messages.Add($"{job.Customer.Name} acepta el presupuesto de {amount:0} €.");
                return true;
            }

            if (job.Customer.Personality != "haggler" || amount > limit * 1.3)
            {
                job.Status = JobStatus.Cancelled;
                Messages.Add($"{job.Customer.Name} rechaza el presupuesto ({amount:0} € supera lo que está dispuesto a pagar).");
            }
            else
            {
                Messages.Add($"{job.Customer.Name} regatea: «a {limit:0} € lo dejo».");
            }

            return false;
        }

        /// <summary>Replaces a component with a part from the catalog.</summary>
        public string InstallPart(Job job, string componentId, string partId)
        {
            Component? comp = job.Car.Parts.Get(componentId);
            PartDefinition? part = _content.Parts.Get(partId);
            if (comp == null || part == null)
            {
                return "Pieza o componente no válido.";
            }

            if (part.Kind != comp.Kind)
            {
                return $"La pieza {part.Name} no corresponde a {comp.Name}.";
            }

            if (Money < part.Price)
            {
                return "No hay dinero suficiente para comprar la pieza.";
            }

            Money -= part.Price;
            double labour = comp.ReplaceMinutes;
            job.LabourMinutes += labour;
            SpendMinutes(labour * TimeFactor);
            job.Car.ReplaceComponent(componentId, part.Reliability);
            foreach (KeyValuePair<string, double> p in part.Params)
            {
                comp.SetParam(p.Key, p.Value);
            }

            comp.PartId = part.Id;
            if (part.Quality == PartQuality.Used || part.Reliability < 0.95)
            {
                // Unreliable parts may fail again later: schedule a latent fault.
                var rng = new DeterministicRandom(job.Seed ^ StableHash.Of(componentId + part.Id));
                if (!rng.Chance(part.Reliability))
                {
                    FailureModeDefinition? mode = _content.FailureModes.All.FirstOrDefault(m => comp.Accepts(m) && (m.Effect == EffectKind.Dead || m.Effect == EffectKind.Weak || m.Effect == EffectKind.Wear));
                    if (mode != null)
                    {
                        job.Car.AddFault(new FaultInstance(mode, componentId, 0.7, new FaultCondition(ConditionKind.Intermittent, 0.2, 30)) { Origin = "pieza defectuosa" });
                    }
                }
            }

            double sale = Math.Round(part.Price * (1 + PartsMarkup), 2);
            job.Lines.Add(new InvoiceLine($"{part.Name} ({comp.Name})", sale, componentId, part.Id));
            job.Lines.Add(new InvoiceLine($"Mano de obra: sustituir {comp.Name} ({labour:0} min)", Math.Round(labour / 60 * LabourRate, 2), componentId));
            return $"Instalado {part.Name} en {comp.Name}. Coste {part.Price:0.00} €, {labour:0} min.";
        }

        /// <summary>Repairs wiring on a component circuit.</summary>
        public string RepairWiring(Job job, string componentId, string? pin)
        {
            int n = job.Car.RepairWiring(componentId, pin);
            double labour = 45;
            job.LabourMinutes += labour;
            SpendMinutes(labour * TimeFactor);
            Money -= 6;
            job.Lines.Add(new InvoiceLine($"Reparación de cableado {componentId}{(pin == null ? "" : ":" + pin)} (empalme termorretráctil)", Math.Round(6 * 1.25 + labour / 60 * LabourRate, 2), componentId));
            return n > 0 ? $"Cableado de {componentId} reparado." : $"Has reparado el cableado de {componentId}, pero no había ningún daño en él.";
        }

        /// <summary>Charges diagnostic time to the job and the clock.</summary>
        public void ChargeDiagnosis(Job job, double minutes)
        {
            job.LabourMinutes += minutes;
            SpendMinutes(minutes);
        }

        /// <summary>Buys a tool or upgrade.</summary>
        public string Buy(string upgradeId)
        {
            UpgradeDefinition? u = _content.Upgrades.FirstOrDefault(x => x.Id == upgradeId);
            if (u == null)
            {
                return "No existe esa mejora.";
            }

            if (Has(u.Id))
            {
                return "Ya lo tienes.";
            }

            if (Reputation < u.ReputationRequired)
            {
                return $"Necesitas reputación {u.ReputationRequired:0} (tienes {Reputation:0}).";
            }

            if (Money < u.Price)
            {
                return "Dinero insuficiente.";
            }

            Money -= u.Price;
            _owned.Add(u.Id);
            if (u.Id == "upg_parts_account")
            {
                PartsMarkup += 0.1;
            }

            return $"Comprado: {u.Name}.";
        }

        /// <summary>Delivers the car: evaluates the work, gets paid, updates reputation and schedules comebacks.</summary>
        public JobOutcome Deliver(Job job)
        {
            JobOutcome o = JobEvaluator.Evaluate(job);
            double labourCharge = job.LabourMinutes / 60.0 * LabourRate;
            double billedLabour = job.Lines.Where(l => l.Description.StartsWith("Mano de obra", StringComparison.Ordinal) || l.Description.StartsWith("Diagnosis", StringComparison.Ordinal)).Sum(l => l.Amount);
            double invoice = Math.Max(job.PartsTotal, 0) + Math.Max(0, labourCharge - billedLabour);
            double cap = job.QuotedAmount > 0 ? job.QuotedAmount * 1.1 : invoice;
            o.Payment = o.Success ? Math.Min(invoice, cap) : Math.Min(invoice, cap) * 0.4;
            bool late = Day - job.AcceptedDay > job.Definition.DeadlineDays;
            o.ReputationDelta = o.Success ? 5 + (late ? -4 : 2) - o.UnneededParts * 1.5 : -6;
            if (o.Success && invoice > cap)
            {
                o.Notes.Add($"El trabajo costó {invoice:0} € pero el presupuesto era {job.QuotedAmount:0} €: el cliente solo paga hasta un 10 % más.");
            }

            if (late)
            {
                o.Notes.Add("Entregado fuera de plazo.");
            }

            Money += o.Payment;
            Reputation = MathUtil.Clamp(Reputation + o.ReputationDelta, 0, 100);
            job.Status = JobStatus.Delivered;
            var rng = new DeterministicRandom(job.Seed ^ (ulong)Day);
            if (rng.Chance(o.ComebackRisk))
            {
                _comebacks.Add(new Comeback
                {
                    JobId = job.Definition.Id,
                    Day = Day + 2 + rng.Next(0, 6),
                    Reason = $"{job.Customer.Name} vuelve con el {job.Car.Definition.Model}: la avería ha reaparecido",
                    Refund = Math.Round(o.Payment * 0.5, 0),
                });
                o.Notes.Add("Hay riesgo de que el cliente vuelva…");
            }

            Messages.Add($"Entregado {job.Car.Definition.DisplayName} a {job.Customer.Name}: {(o.Success ? "OK" : "NO resuelto")}, cobrado {o.Payment:0} €, reputación {o.ReputationDelta:+0;-0}.");
            return o;
        }
    }

    /// <summary>Evaluates a job against the ground truth and with objective tests (road test, emissions, dyno).</summary>
    public static class JobEvaluator
    {
        /// <summary>Evaluates.</summary>
        public static JobOutcome Evaluate(Job job)
        {
            var o = new JobOutcome();
            Car car = job.Car;
            int remaining = 0;
            foreach (FaultInstance f in car.Faults.Unrepaired())
            {
                if (f.Origin != "test")
                {
                    remaining++;
                }
            }

            o.RemainingFaults = remaining;
            var faultyComponents = new HashSet<string>(job.OriginalFaults.Select(f => f.ComponentId), StringComparer.OrdinalIgnoreCase);
            foreach (InvoiceLine l in job.Lines)
            {
                if (l.PartId.Length > 0 && !faultyComponents.Contains(l.ComponentId) && job.Definition.Goal == JobGoal.Repair)
                {
                    o.UnneededParts++;
                }
            }

            if (o.UnneededParts > 0)
            {
                o.Notes.Add($"Se cambiaron {o.UnneededParts} piezas que estaban bien (\"cambiar por probar\").");
            }

            // Road test: start, warm idle and cruise, then check the MIL.
            car.Key = KeyPosition.Off;
            car.RunFor(1);
            bool runs = car.Start(4);
            if (runs)
            {
                car.Pedal = 0;
                car.Mode = LoadMode.Neutral;
                car.RunFor(60);
                int gear = Math.Min(3, car.Definition.GearRatios.Length);
                car.Gear = gear;
                car.Mode = LoadMode.DynoHoldRpm;
                double ratio = car.Definition.GearRatios[gear - 1] * car.Definition.FinalDrive;
                car.DynoHoldRpm = 2500;
                car.SetVehicleSpeed(2500 / 60.0 * 2 * Math.PI / ratio * car.Definition.WheelRadiusM * 3.6);
                car.Pedal = 0.3;
                car.RunFor(120);
                car.Mode = LoadMode.Neutral;
                car.Gear = 0;
                car.Pedal = 0;
            }

            bool mil = car.Ecu.Dtcs.MilOn;
            switch (job.Definition.Goal)
            {
                case JobGoal.Repair:
                    o.Success = runs && remaining == 0 && !mil;
                    if (!runs)
                    {
                        o.Notes.Add("El coche no arranca en la prueba final.");
                    }
                    else if (mil)
                    {
                        o.Notes.Add(remaining == 0
                            ? "El testigo de avería sigue encendido: la avería está reparada pero quedaron códigos memorizados (bórralos tras reparar)."
                            : "El testigo de avería sigue encendido en la prueba de carretera.");
                    }

                    if (remaining > 0)
                    {
                        o.Notes.Add($"Quedan {remaining} averías sin resolver.");
                        o.ComebackRisk = 0.85;
                    }

                    break;
                case JobGoal.Inspection:
                    {
                        double lambda = car.Engine.State.ExhaustLambda;
                        bool emissions = car.Definition.Engine.IsDiesel ? car.Engine.State.SmokeOpacity < 0.3 : car.Engine.State.CatalystEfficiency > 0.85 && Math.Abs(lambda - 1) < 0.03;
                        int incomplete = car.Ecu.Readiness.IncompleteCount;
                        o.Success = runs && !mil && emissions && incomplete <= 1;
                        o.Notes.Add($"ITV simulada: MIL {(mil ? "ON" : "OFF")}, eficiencia catalizador {car.Engine.State.CatalystEfficiency:P0}, λ {lambda:0.000}, monitores incompletos {incomplete}.");
                        if (incomplete > 1)
                        {
                            o.Notes.Add("Monitores incompletos: si borras códigos justo antes de la ITV, el coche no pasa.");
                        }

                        o.ComebackRisk = remaining > 0 ? 0.6 : 0.05;
                        break;
                    }

                default:
                    {
                        DynoResult r = DynoRun.Pull(car);
                        o.MeasuredPowerPs = r.PeakPowerPs;
                        bool safe = r.MaxKnockRetard < 3 && (car.Definition.Engine.IsDiesel || r.MaxLambdaUnderLoad < 0.93) && r.DamageDelta < 0.05 && !r.Aborted;
                        bool durable = true;
                        if (job.Definition.Goal == JobGoal.Track && !r.Aborted)
                        {
                            DynoResult r2 = DynoRun.Pull(car);
                            durable = !r2.Aborted && r2.PeakPowerPs > r.PeakPowerPs * 0.95 && car.Engine.Damage.Worst() < 0.3;
                        }

                        o.Success = runs && r.PeakPowerPs >= job.Definition.PowerTargetPs && safe && durable && !mil;
                        o.Notes.Add($"Banco: {r.Summary}. Objetivo {job.Definition.PowerTargetPs:0} CV.");
                        o.Notes.AddRange(r.Warnings);
                        o.ComebackRisk = safe ? 0.08 : 0.7;
                        break;
                    }
            }

            return o;
        }
    }
}
