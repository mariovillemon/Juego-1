using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Sim.Game;

namespace Garage.Game
{
    /// <summary>A part order waiting for delivery.</summary>
    public sealed class PartOrder
    {
        /// <summary>Part id.</summary>
        public string PartId { get; set; } = "";

        /// <summary>Quantity.</summary>
        public int Quantity { get; set; } = 1;

        /// <summary>Day it arrives (morning).</summary>
        public int ArrivalDay { get; set; }
    }

    /// <summary>A part taken off a car, kept in the old-parts box.</summary>
    public sealed class RemovedPart
    {
        /// <summary>Job it came from.</summary>
        public string JobId { get; set; } = "";

        /// <summary>Component id on that car.</summary>
        public string ComponentId { get; set; } = "";

        /// <summary>Readable name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Day removed.</summary>
        public int Day { get; set; }

        /// <summary>What a bench inspection reveals.</summary>
        public string Finding { get; set; } = "";

        /// <summary>Whether it was really faulty (ground truth; shown only after inspection).</summary>
        public bool WasFaulty { get; set; }

        /// <summary>Inspected on the bench.</summary>
        public bool Inspected { get; set; }
    }

    /// <summary>One line of the shopping cart.</summary>
    public sealed class CartLine
    {
        /// <summary>Part.</summary>
        public PartDefinition Part { get; set; } = new PartDefinition();

        /// <summary>Quantity.</summary>
        public int Quantity { get; set; } = 1;

        /// <summary>Line total.</summary>
        public double Total => Part.Price * Quantity;
    }

    /// <summary>Parts stock, pending orders, shopping cart and old-parts box.</summary>
    public sealed class Inventory
    {
        private readonly Dictionary<string, int> _stock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Stock by part id.</summary>
        public IReadOnlyDictionary<string, int> Stock => _stock;

        /// <summary>Pending orders.</summary>
        public List<PartOrder> Orders { get; } = new List<PartOrder>();

        /// <summary>Shopping cart.</summary>
        public List<CartLine> Cart { get; } = new List<CartLine>();

        /// <summary>Old parts box.</summary>
        public List<RemovedPart> OldParts { get; } = new List<RemovedPart>();

        /// <summary>Cart total.</summary>
        public double CartTotal => Cart.Sum(l => l.Total);

        /// <summary>Units in stock.</summary>
        public int Count(string partId) => _stock.TryGetValue(partId, out int n) ? n : 0;

        /// <summary>Adds stock.</summary>
        public void Add(string partId, int qty = 1)
        {
            _stock[partId] = Count(partId) + qty;
        }

        /// <summary>Takes one unit; false if none.</summary>
        public bool Take(string partId)
        {
            int n = Count(partId);
            if (n <= 0)
            {
                return false;
            }

            if (n == 1)
            {
                _stock.Remove(partId);
            }
            else
            {
                _stock[partId] = n - 1;
            }

            return true;
        }

        /// <summary>Delivery delay in days by quality (decision D-58: OEM comes from the importer next day).</summary>
        public static int LeadTimeDays(PartQuality q) => q switch
        {
            PartQuality.Oem => 1,
            PartQuality.Performance => 2,
            _ => 0,
        };

        /// <summary>Readable lead time.</summary>
        public static string LeadTimeText(PartQuality q)
        {
            int d = LeadTimeDays(q);
            return d == 0 ? "inmediata" : d == 1 ? "mañana" : $"{d} días";
        }

        /// <summary>Readable quality.</summary>
        public static string QualityText(PartQuality q) => q switch
        {
            PartQuality.Oem => "Original",
            PartQuality.Aftermarket => "Recambio",
            PartQuality.Used => "Usada",
            _ => "Competición",
        };

        /// <summary>Moves orders due on <paramref name="day"/> into stock; returns the delivered part ids.</summary>
        public List<string> Receive(int day)
        {
            var arrived = new List<string>();
            foreach (PartOrder o in Orders.Where(o => o.ArrivalDay <= day).ToList())
            {
                Orders.Remove(o);
                Add(o.PartId, o.Quantity);
                arrived.Add(o.PartId);
            }

            return arrived;
        }
    }
}
