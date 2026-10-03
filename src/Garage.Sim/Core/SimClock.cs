namespace Garage.Sim.Core
{
    /// <summary>
    /// Simulation clock. Simulated seconds advance in fixed steps; game minutes are
    /// tracked separately because diagnostic actions cost workshop time.
    /// </summary>
    public sealed class SimClock
    {
        /// <summary>Total simulated seconds.</summary>
        public double SimSeconds { get; private set; }

        /// <summary>Total game minutes spent (workshop time).</summary>
        public double GameMinutes { get; private set; }

        /// <summary>Number of fixed steps executed.</summary>
        public long Ticks { get; private set; }

        /// <summary>Advances simulated time by one step.</summary>
        public void Advance(double dt)
        {
            SimSeconds += dt;
            Ticks++;
        }

        /// <summary>Adds game time spent by an action.</summary>
        public void Spend(double minutes)
        {
            if (minutes > 0)
            {
                GameMinutes += minutes;
            }
        }
    }
}
