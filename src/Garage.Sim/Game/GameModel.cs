using System;
using System.Collections.Generic;
using Garage.Sim.Components;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Game
{
    /// <summary>Part quality tier.</summary>
    public enum PartQuality
    {
        /// <summary>Original equipment.</summary>
        Oem,
        /// <summary>Aftermarket replacement.</summary>
        Aftermarket,
        /// <summary>Used (scrapyard).</summary>
        Used,
        /// <summary>Performance upgrade.</summary>
        Performance,
    }

    /// <summary>Part in the catalog.</summary>
    public sealed class PartDefinition
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Component kind it replaces.</summary>
        public ComponentKind Kind { get; set; }

        /// <summary>Name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Quality.</summary>
        public PartQuality Quality { get; set; }

        /// <summary>Price to the workshop (€).</summary>
        public double Price { get; set; }

        /// <summary>Probability of not failing prematurely (0..1).</summary>
        public double Reliability { get; set; } = 1;

        /// <summary>Car ids it fits ("*" = any).</summary>
        public List<string> Fits { get; set; } = new List<string> { "*" };

        /// <summary>Parameter overrides when installed (performance parts).</summary>
        public Dictionary<string, double> Params { get; set; } = new Dictionary<string, double>();

        /// <summary>True if it fits a car.</summary>
        public bool FitsCar(string carId) => Fits.Contains("*") || Fits.Contains(carId);
    }

    /// <summary>Parts catalog.</summary>
    public sealed class PartsCatalog
    {
        private readonly List<PartDefinition> _parts = new List<PartDefinition>();

        /// <summary>All parts.</summary>
        public IReadOnlyList<PartDefinition> All => _parts;

        /// <summary>Adds a part.</summary>
        public void Add(PartDefinition p) => _parts.Add(p);

        /// <summary>Gets by id.</summary>
        public PartDefinition? Get(string id) => _parts.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Parts for a component kind on a car, cheapest last.</summary>
        public List<PartDefinition> For(ComponentKind kind, string carId)
        {
            var l = _parts.FindAll(p => p.Kind == kind && p.FitsCar(carId));
            l.Sort((a, b) => b.Price.CompareTo(a.Price));
            return l;
        }
    }

    /// <summary>Customer profile.</summary>
    public sealed class CustomerProfile
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Budget (€).</summary>
        public double Budget { get; set; }

        /// <summary>Patience in days.</summary>
        public int PatienceDays { get; set; } = 3;

        /// <summary>Car usage.</summary>
        public string Usage { get; set; } = "commute";

        /// <summary>Personality: honest, enthusiast, impatient, haggler, demanding.</summary>
        public string Personality { get; set; } = "honest";
    }

    /// <summary>Workshop upgrade or tool.</summary>
    public sealed class UpgradeDefinition
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Name.</summary>
        public string Name { get; set; } = "";

        /// <summary>tool or workshop.</summary>
        public string Type { get; set; } = "tool";

        /// <summary>Price.</summary>
        public double Price { get; set; }

        /// <summary>Reputation needed to buy.</summary>
        public double ReputationRequired { get; set; }

        /// <summary>Description.</summary>
        public string Description { get; set; } = "";
    }

    /// <summary>What the customer wants.</summary>
    public enum JobGoal
    {
        /// <summary>Fix the complaint.</summary>
        Repair,
        /// <summary>More power.</summary>
        Power,
        /// <summary>Pass the technical inspection (emissions, no MIL, readiness).</summary>
        Inspection,
        /// <summary>Track preparation: power + durability.</summary>
        Track,
    }

    /// <summary>Job definition from data.</summary>
    public sealed class JobDefinition
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Customer id.</summary>
        public string CustomerId { get; set; } = "";

        /// <summary>Car id.</summary>
        public string CarId { get; set; } = "";

        /// <summary>Goal.</summary>
        public JobGoal Goal { get; set; }

        /// <summary>Scenario id (faults), optional.</summary>
        public string ScenarioId { get; set; } = "";

        /// <summary>Random fault difficulty when no scenario (0 = none).</summary>
        public int GenerateDifficulty { get; set; }

        /// <summary>Power target (PS) for power/track jobs.</summary>
        public double PowerTargetPs { get; set; }

        /// <summary>Customer's words.</summary>
        public string Complaint { get; set; } = "";

        /// <summary>Budget (€).</summary>
        public double Budget { get; set; }

        /// <summary>Days to deliver.</summary>
        public int DeadlineDays { get; set; } = 3;

        /// <summary>Guided (tutorial) job: parts fitted never turn out defective, so the lesson stays deterministic.</summary>
        public bool Guided { get; set; }
    }

    /// <summary>Job status.</summary>
    public enum JobStatus
    {
        /// <summary>On the board.</summary>
        Offered,
        /// <summary>Quote accepted, car in the workshop.</summary>
        InProgress,
        /// <summary>Delivered and paid.</summary>
        Delivered,
        /// <summary>Customer rejected the quote or left.</summary>
        Cancelled,
    }

    /// <summary>A line on the invoice.</summary>
    public sealed class InvoiceLine
    {
        /// <summary>Creates a line.</summary>
        public InvoiceLine(string description, double amount, string componentId = "", string partId = "")
        {
            Description = description;
            Amount = amount;
            ComponentId = componentId;
            PartId = partId;
        }

        /// <summary>Description.</summary>
        public string Description { get; }

        /// <summary>Amount (€).</summary>
        public double Amount { get; }

        /// <summary>Component affected.</summary>
        public string ComponentId { get; }

        /// <summary>Part installed.</summary>
        public string PartId { get; }
    }

    /// <summary>A job in progress: a car with hidden faults and the work done so far.</summary>
    public sealed class Job
    {
        /// <summary>Creates a job.</summary>
        public Job(JobDefinition def, CustomerProfile customer, Car car, ulong seed)
        {
            Definition = def;
            Customer = customer;
            Car = car;
            Seed = seed;
        }

        /// <summary>Definition.</summary>
        public JobDefinition Definition { get; }

        /// <summary>Customer.</summary>
        public CustomerProfile Customer { get; }

        /// <summary>The car.</summary>
        public Car Car { get; }

        /// <summary>Seed used to build the car and faults.</summary>
        public ulong Seed { get; }

        /// <summary>Status.</summary>
        public JobStatus Status { get; set; } = JobStatus.Offered;

        /// <summary>Day the car arrived.</summary>
        public int AcceptedDay { get; set; }

        /// <summary>Quoted amount (€).</summary>
        public double QuotedAmount { get; set; }

        /// <summary>Invoice lines (parts, labour).</summary>
        public List<InvoiceLine> Lines { get; } = new List<InvoiceLine>();

        /// <summary>Workshop minutes spent on the car.</summary>
        public double LabourMinutes { get; set; }

        /// <summary>Baseline power measured on arrival (PS), 0 if unknown.</summary>
        public double BaselinePowerPs { get; set; }

        /// <summary>Faults present when the car arrived (ground truth, hidden from the player).</summary>
        public List<FaultInstance> OriginalFaults { get; } = new List<FaultInstance>();

        /// <summary>Total of invoice lines.</summary>
        public double PartsTotal
        {
            get
            {
                double t = 0;
                foreach (InvoiceLine l in Lines)
                {
                    t += l.Amount;
                }

                return t;
            }
        }
    }

    /// <summary>Evaluation at delivery.</summary>
    public sealed class JobOutcome
    {
        /// <summary>Goal met.</summary>
        public bool Success { get; set; }

        /// <summary>Money received.</summary>
        public double Payment { get; set; }

        /// <summary>Reputation change.</summary>
        public double ReputationDelta { get; set; }

        /// <summary>Probability the car comes back (0..1).</summary>
        public double ComebackRisk { get; set; }

        /// <summary>Faults still present.</summary>
        public int RemainingFaults { get; set; }

        /// <summary>Parts replaced that were not faulty.</summary>
        public int UnneededParts { get; set; }

        /// <summary>Measured power (PS) if a dyno check was done.</summary>
        public double MeasuredPowerPs { get; set; }

        /// <summary>Explanations (Spanish).</summary>
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>A scheduled return of an unhappy customer.</summary>
    public sealed class Comeback
    {
        /// <summary>Job id.</summary>
        public string JobId { get; set; } = "";

        /// <summary>Day it happens.</summary>
        public int Day { get; set; }

        /// <summary>Reason.</summary>
        public string Reason { get; set; } = "";

        /// <summary>Refund owed (€).</summary>
        public double Refund { get; set; }
    }

    /// <summary>Customer answer to a quote.</summary>
    public enum QuoteAnswer
    {
        /// <summary>Accepted: the job starts.</summary>
        Accepted,

        /// <summary>The customer proposes a lower amount (the job stays on offer).</summary>
        Counter,

        /// <summary>Rejected: the customer leaves.</summary>
        Rejected,
    }

    /// <summary>Structured answer to a quote.</summary>
    public sealed class QuoteDecision
    {
        /// <summary>Creates a decision.</summary>
        public QuoteDecision(QuoteAnswer answer, double amount, string message)
        {
            Answer = answer;
            Amount = amount;
            Message = message;
        }

        /// <summary>Answer.</summary>
        public QuoteAnswer Answer { get; }

        /// <summary>Accepted amount or counter-offer.</summary>
        public double Amount { get; }

        /// <summary>What the customer says.</summary>
        public string Message { get; }
    }
}
