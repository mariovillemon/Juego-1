using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Data;
using Garage.Data.Json;

namespace Garage.Game
{
    /// <summary>A tutorial step: instructions plus the event that completes it.</summary>
    public sealed class TutorialStep
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Title.</summary>
        public string Title { get; set; } = "";

        /// <summary>Instructions.</summary>
        public string Text { get; set; } = "";

        /// <summary>Diagnostic reasoning shown in training mode after completing the step.</summary>
        public string Explanation { get; set; } = "";

        /// <summary>Completing event kind.</summary>
        public GameEventKind Event { get; set; }

        /// <summary>Required subject fragment (empty = any).</summary>
        public string Subject { get; set; } = "";

        /// <summary>Scene object or UI element to highlight.</summary>
        public string Highlight { get; set; } = "";

        /// <summary>Whether an event completes this step.</summary>
        public bool Matches(GameEvent e) => e.Kind == Event && (Subject.Length == 0 || e.Subject.IndexOf(Subject, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>A data-driven tutorial (data/base/tutorials.json).</summary>
    public sealed class TutorialDefinition
    {
        /// <summary>Id.</summary>
        public string Id { get; set; } = "";

        /// <summary>Name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Customer of the scripted job.</summary>
        public string CustomerId { get; set; } = "";

        /// <summary>Car of the scripted job.</summary>
        public string CarId { get; set; } = "";

        /// <summary>Fault scenario of the scripted job.</summary>
        public string ScenarioId { get; set; } = "";

        /// <summary>What the customer says.</summary>
        public string Complaint { get; set; } = "";

        /// <summary>Budget of the scripted job.</summary>
        public double Budget { get; set; } = 500;

        /// <summary>Deadline of the scripted job.</summary>
        public int DeadlineDays { get; set; } = 3;

        /// <summary>Steps in order.</summary>
        public List<TutorialStep> Steps { get; } = new List<TutorialStep>();

        /// <summary>Reads all tutorials from content.</summary>
        public static List<TutorialDefinition> LoadAll(ContentDatabase db)
        {
            var list = new List<TutorialDefinition>();
            foreach (KeyValuePair<string, JsonValue> kv in db.Raw("tutorials").OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                JsonValue t = kv.Value;
                JsonValue job = t["job"];
                var def = new TutorialDefinition
                {
                    Id = kv.Key,
                    Name = t.Str("name"),
                    CustomerId = job.Str("customer"),
                    CarId = job.Str("car"),
                    ScenarioId = job.Str("scenario"),
                    Complaint = job.Str("complaint"),
                    Budget = job.Num("budget", 500),
                    DeadlineDays = job.Int("deadlineDays", 3),
                };
                foreach (JsonValue s in t["steps"].Items)
                {
                    if (!Enum.TryParse(s.Str("event"), out GameEventKind kind))
                    {
                        throw new ContentException($"Tutorial {kv.Key}: evento desconocido '{s.Str("event")}'.");
                    }

                    def.Steps.Add(new TutorialStep
                    {
                        Id = s.Str("id"),
                        Title = s.Str("title"),
                        Text = s.Str("text"),
                        Explanation = s.Str("explanation"),
                        Event = kind,
                        Subject = s.Str("subject"),
                        Highlight = s.Str("highlight"),
                    });
                }

                list.Add(def);
            }

            return list;
        }
    }

    /// <summary>Runs a tutorial by observing game events.</summary>
    public sealed class TutorialRunner
    {
        private readonly GameEventBus _bus;
        private readonly bool _training;

        /// <summary>Starts observing.</summary>
        public TutorialRunner(TutorialDefinition def, GameEventBus bus, bool training, int startIndex = 0)
        {
            Definition = def;
            _bus = bus;
            _training = training;
            Index = startIndex;
            _bus.Raised += OnEvent;
        }

        /// <summary>Definition.</summary>
        public TutorialDefinition Definition { get; }

        /// <summary>Current step index (== Steps.Count when finished).</summary>
        public int Index { get; private set; }

        /// <summary>Current step or null when finished.</summary>
        public TutorialStep? Current => Index < Definition.Steps.Count ? Definition.Steps[Index] : null;

        /// <summary>Finished or skipped.</summary>
        public bool Finished => Current == null;

        /// <summary>Explanation of the last completed step (training mode only).</summary>
        public string LastExplanation { get; private set; } = "";

        private void OnEvent(GameEvent e)
        {
            TutorialStep? step = Current;
            if (step == null || e.Kind == GameEventKind.TutorialStepChanged || e.Kind == GameEventKind.TutorialCompleted || !step.Matches(e))
            {
                return;
            }

            LastExplanation = _training ? step.Explanation : "";
            Index++;
            if (Current == null)
            {
                Stop();
                _bus.Publish(GameEventKind.TutorialCompleted, Definition.Id, LastExplanation);
            }
            else
            {
                _bus.Publish(GameEventKind.TutorialStepChanged, Current.Id, LastExplanation);
            }
        }

        /// <summary>Skips the rest.</summary>
        public void Skip()
        {
            if (Finished)
            {
                return;
            }

            Index = Definition.Steps.Count;
            Stop();
            _bus.Publish(GameEventKind.TutorialCompleted, Definition.Id, "Tutorial saltado.");
        }

        /// <summary>Stops listening.</summary>
        public void Stop() => _bus.Raised -= OnEvent;
    }
}
