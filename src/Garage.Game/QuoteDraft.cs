using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Sim.Game;

namespace Garage.Game
{
    /// <summary>One line of a quote: a part, labour or diagnosis.</summary>
    public sealed class QuoteLine
    {
        /// <summary>Description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Amount charged to the customer.</summary>
        public double Amount { get; set; }

        /// <summary>Part id (empty for labour).</summary>
        public string PartId { get; set; } = "";

        /// <summary>Labour hours (0 for parts).</summary>
        public double Hours { get; set; }
    }

    /// <summary>A quote being prepared for a job (work sheet).</summary>
    public sealed class QuoteDraft
    {
        /// <summary>Creates an empty quote with the diagnosis hour.</summary>
        public QuoteDraft(Job job, double labourRate)
        {
            Job = job;
            LabourRate = labourRate;
            Lines.Add(new QuoteLine { Description = "Diagnosis electrónica", Hours = 1, Amount = labourRate });
        }

        /// <summary>Job.</summary>
        public Job Job { get; }

        /// <summary>Labour rate used for labour lines.</summary>
        public double LabourRate { get; }

        /// <summary>Lines.</summary>
        public List<QuoteLine> Lines { get; } = new List<QuoteLine>();

        /// <summary>Total.</summary>
        public double Total => Math.Round(Lines.Sum(l => l.Amount), 2);

        /// <summary>Adds a part at its sale price.</summary>
        public QuoteLine AddPart(PartDefinition part, double markup)
        {
            var l = new QuoteLine { Description = part.Name, PartId = part.Id, Amount = Math.Round(part.Price * (1 + markup), 2) };
            Lines.Add(l);
            return l;
        }

        /// <summary>Adds labour hours.</summary>
        public QuoteLine AddLabour(string description, double hours)
        {
            var l = new QuoteLine { Description = description, Hours = hours, Amount = Math.Round(hours * LabourRate, 2) };
            Lines.Add(l);
            return l;
        }

        /// <summary>Removes a line.</summary>
        public void Remove(QuoteLine l) => Lines.Remove(l);
    }
}
