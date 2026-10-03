using System;
using System.Collections.Generic;
using Garage.Sim.Components;
using Garage.Sim.Core;
using Garage.Sim.Electrical;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Faults
{
    /// <summary>
    /// Generates random fault combinations for a car by difficulty (1..5) with a seed, so jobs are never the same.
    /// 1: one obvious fault; 3: subtle or conditional faults; 5: multiple, intermittent and chained faults.
    /// </summary>
    public sealed class FaultGenerator
    {
        private readonly FailureModeLibrary _library;

        /// <summary>Creates a generator over a library.</summary>
        public FaultGenerator(FailureModeLibrary library)
        {
            _library = library;
        }

        /// <summary>Generates faults (not yet added to the car).</summary>
        public List<FaultInstance> Generate(Car car, int difficulty, DeterministicRandom rng)
        {
            difficulty = Math.Max(1, Math.Min(5, difficulty));
            int count = difficulty <= 2 ? 1 : difficulty == 3 ? rng.Next(1, 3) : difficulty == 4 ? 2 : rng.Next(2, 4);
            var result = new List<FaultInstance>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int guard = 0;
            while (result.Count < count && guard++ < 200)
            {
                Component comp = rng.Pick(car.Parts.All);
                if (used.Contains(comp.Id) || comp.Kind == ComponentKind.CanBus && difficulty < 4)
                {
                    continue;
                }

                var candidates = new List<FailureModeDefinition>();
                foreach (FailureModeDefinition m in _library.All)
                {
                    if (comp.Accepts(m) && m.Difficulty <= difficulty + 1 && m.Difficulty >= Math.Max(1, difficulty - 2))
                    {
                        candidates.Add(m);
                    }
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                FailureModeDefinition mode = rng.Pick(candidates);
                string? pin = null;
                Circuit? circuit = car.CircuitOf(comp.Id);
                if (mode.Family == "wiring")
                {
                    if (circuit == null)
                    {
                        continue;
                    }

                    var roles = new List<string>();
                    foreach (PinInfo p in circuit.Pins)
                    {
                        roles.Add(p.Role);
                    }

                    pin = roles.Contains(mode.Pin) && rng.Chance(0.5) ? mode.Pin : rng.Pick(roles);
                }
                else if (mode.Family == "network")
                {
                    pin = rng.Pick(new[] { "abs", "ipc", "bcm" });
                }

                FaultCondition cond = FaultCondition.AlwaysOn;
                if (difficulty >= 3 && mode.Conditions.Count > 1 && rng.Chance(0.25 * (difficulty - 2)))
                {
                    ConditionKind k = rng.Pick(mode.Conditions);
                    cond = k switch
                    {
                        ConditionKind.Cold => new FaultCondition(k, rng.Range(35, 60)),
                        ConditionKind.Hot => new FaultCondition(k, rng.Range(80, 95)),
                        ConditionKind.AboveLoad => new FaultCondition(k, rng.Range(0.6, 1.1)),
                        ConditionKind.Intermittent => new FaultCondition(k, rng.Range(0.15, 0.5), rng.Range(4, 20)),
                        ConditionKind.Vibration => new FaultCondition(k, 2800, 3600),
                        _ => FaultCondition.AlwaysOn,
                    };
                }

                double severity = difficulty <= 2 ? rng.Range(0.7, 1.0) : rng.Range(0.35, 0.9);
                result.Add(new FaultInstance(mode, comp.Id, severity, cond, pin) { Origin = "generado" });
                used.Add(comp.Id);
            }

            return result;
        }
    }
}
