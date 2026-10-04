using System;
using System.Collections.Generic;
using Garage.Data.Json;
using Garage.Sim.Components;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Vehicle;

namespace Garage.Data
{
    /// <summary>Meta-game content mapping.</summary>
    public sealed partial class ContentDatabase : IGameContent
    {
        private PartsCatalog? _parts;
        private List<CustomerProfile>? _customers;
        private List<JobDefinition>? _jobs;
        private List<UpgradeDefinition>? _upgrades;

        /// <inheritdoc />
        public IReadOnlyList<string> CarIds => Ids("cars");

        /// <inheritdoc />
        public PartsCatalog Parts
        {
            get
            {
                if (_parts == null)
                {
                    _parts = new PartsCatalog();
                    foreach (string id in Ids("parts"))
                    {
                        JsonValue p = Raw("parts")[id];
                        if (!ComponentKinds.TryParse(p.Str("kind"), out ComponentKind kind))
                        {
                            continue;
                        }

                        var def = new PartDefinition
                        {
                            Id = id,
                            Kind = kind,
                            Name = p.Str("name"),
                            Quality = Enum.TryParse(p.Str("quality"), true, out PartQuality q) ? q : PartQuality.Oem,
                            Price = p.Num("price"),
                            Reliability = p.Num("reliability", 1),
                            Fits = p["fits"].Count > 0 ? p["fits"].ToStringList() : new List<string> { "*" },
                        };
                        foreach (KeyValuePair<string, JsonValue> kv in p["params"].Members)
                        {
                            def.Params[kv.Key] = kv.Value.NumberValue;
                        }

                        _parts.Add(def);
                    }
                }

                return _parts;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<CustomerProfile> Customers
        {
            get
            {
                if (_customers == null)
                {
                    _customers = new List<CustomerProfile>();
                    foreach (string id in Ids("customers"))
                    {
                        JsonValue c = Raw("customers")[id];
                        _customers.Add(new CustomerProfile
                        {
                            Id = id,
                            Name = c.Str("name"),
                            Description = c.Str("description"),
                            Budget = c.Num("budget"),
                            PatienceDays = c.Int("patienceDays", 3),
                            Usage = c.Str("usage"),
                            Personality = c.Str("personality", "honest"),
                        });
                    }
                }

                return _customers;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<JobDefinition> JobDefinitions
        {
            get
            {
                if (_jobs == null)
                {
                    _jobs = new List<JobDefinition>();
                    foreach (string id in Ids("jobs"))
                    {
                        _jobs.Add(MapJob(Raw("jobs")[id]));
                    }
                }

                return _jobs;
            }
        }

        /// <summary>Maps a job definition.</summary>
        public static JobDefinition MapJob(JsonValue j) => new JobDefinition
        {
            Id = j.Str("id"),
            CustomerId = j.Str("customer"),
            CarId = j.Str("car"),
            Goal = Enum.TryParse(j.Str("goal"), true, out JobGoal g) ? g : JobGoal.Repair,
            ScenarioId = j.Str("scenario"),
            GenerateDifficulty = j["generate"].Int("difficulty", 0),
            PowerTargetPs = j.Num("powerTargetPs"),
            Complaint = j.Str("complaint"),
            Budget = j.Num("budget"),
            DeadlineDays = j.Int("deadlineDays", 3),
            Guided = j.Bool("guided"),
        };

        /// <summary>Serialises a job definition.</summary>
        public static JsonValue JobToJson(JobDefinition d)
        {
            JsonValue o = JsonValue.NewObject().Set("id", d.Id).Set("customer", d.CustomerId).Set("car", d.CarId)
                .Set("goal", d.Goal.ToString().ToLowerInvariant()).Set("complaint", d.Complaint).Set("budget", d.Budget).Set("deadlineDays", d.DeadlineDays);
            if (d.ScenarioId.Length > 0)
            {
                o.Set("scenario", d.ScenarioId);
            }

            if (d.GenerateDifficulty > 0)
            {
                o.Set("generate", JsonValue.NewObject().Set("difficulty", d.GenerateDifficulty));
            }

            if (d.PowerTargetPs > 0)
            {
                o.Set("powerTargetPs", d.PowerTargetPs);
            }

            if (d.Guided)
            {
                o.Set("guided", true);
            }

            return o;
        }

        /// <inheritdoc />
        public IReadOnlyList<UpgradeDefinition> Upgrades
        {
            get
            {
                if (_upgrades == null)
                {
                    _upgrades = new List<UpgradeDefinition>();
                    foreach (string id in Ids("upgrades"))
                    {
                        JsonValue u = Raw("upgrades")[id];
                        _upgrades.Add(new UpgradeDefinition
                        {
                            Id = id,
                            Name = u.Str("name"),
                            Type = u.Str("type"),
                            Price = u.Num("price"),
                            ReputationRequired = u.Num("reputationRequired"),
                            Description = u.Str("description"),
                        });
                    }
                }

                return _upgrades;
            }
        }

        /// <inheritdoc />
        public List<FaultInstance> ScenarioFaults(string scenarioId)
        {
            var list = new List<FaultInstance>();
            foreach (JsonValue f in Require("scenarios", scenarioId)["faults"].Items)
            {
                list.Add(MapFault(f));
            }

            return list;
        }

        /// <inheritdoc />
        Car IGameContent.CreateCar(string carId, ulong seed, IEnumerable<FaultInstance>? faults, bool warm, double ambientC) => CreateCar(carId, seed, faults, warm, ambientC);
    }
}
