using System;
using System.Collections.Generic;
using System.Linq;

namespace Garage.Game.Audio
{
    /// <summary>Gain and pitch of one engine layer.</summary>
    public readonly struct LayerMix
    {
        /// <summary>Creates a mix value.</summary>
        public LayerMix(string id, double volume, double pitch)
        {
            Id = id;
            Volume = volume;
            Pitch = pitch;
        }

        /// <summary>Layer id.</summary>
        public string Id { get; }

        /// <summary>Linear volume 0–1.</summary>
        public double Volume { get; }

        /// <summary>Playback rate (1 = recorded rpm).</summary>
        public double Pitch { get; }
    }

    /// <summary>
    /// Engine sound from recorded layers, as racing games do: the two layers whose recording rpm brackets the current
    /// rpm are crossfaded with equal power and pitched by rpm/recordedRpm; on-load and off-load sets are crossfaded by
    /// engine load; exterior and interior sets by how far the listener is inside the car. Pure C#, unit tested.
    /// </summary>
    public static class EngineSoundMixer
    {
        /// <summary>Mix for a set of layers (one perspective).</summary>
        public static List<LayerMix> Mix(IReadOnlyList<SoundDefinition> layers, double rpm, double load, double gain = 1)
        {
            var result = new List<LayerMix>();
            if (layers.Count == 0 || rpm < 50)
            {
                result.AddRange(layers.Select(l => new LayerMix(l.Id, 0, 1)));
                return result;
            }

            load = Math.Max(0, Math.Min(1, load));
            var weights = new Dictionary<string, double>();
            foreach (SoundDefinition l in layers)
            {
                weights[l.Id] = 0;
            }

            // Load bands present in the set (e.g. 0 and 1); equal-power crossfade between the two around `load`.
            List<double> loads = layers.Select(l => l.Load).Distinct().OrderBy(x => x).ToList();
            foreach ((double band, double bandWeight) in Bracket(loads, load))
            {
                List<SoundDefinition> set = layers.Where(l => Math.Abs(l.Load - band) < 1e-9).OrderBy(l => l.Rpm).ToList();
                foreach ((double r, double w) in Bracket(set.Select(l => l.Rpm).ToList(), rpm))
                {
                    SoundDefinition l = set.First(x => Math.Abs(x.Rpm - r) < 1e-9);
                    weights[l.Id] += w * bandWeight;
                }
            }

            foreach (SoundDefinition l in layers)
            {
                double pitch = l.Rpm > 0 ? Math.Max(0.4, Math.Min(2.5, rpm / l.Rpm)) : 1;
                result.Add(new LayerMix(l.Id, Math.Min(1, weights[l.Id] * l.Volume * gain), pitch));
            }

            return result;
        }

        /// <summary>
        /// The one or two values of a sorted list that bracket x, with equal-power weights (w1² + w2² = 1). Outside
        /// the range the nearest value takes all the weight.
        /// </summary>
        public static List<(double Value, double Weight)> Bracket(IList<double> sorted, double x)
        {
            var r = new List<(double, double)>();
            if (sorted.Count == 0)
            {
                return r;
            }

            if (x <= sorted[0] || sorted.Count == 1)
            {
                r.Add((sorted[0], 1));
                return r;
            }

            if (x >= sorted[sorted.Count - 1])
            {
                r.Add((sorted[sorted.Count - 1], 1));
                return r;
            }

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (x >= sorted[i] && x <= sorted[i + 1])
                {
                    double t = (x - sorted[i]) / (sorted[i + 1] - sorted[i]);
                    r.Add((sorted[i], Math.Cos(t * Math.PI / 2)));
                    r.Add((sorted[i + 1], Math.Sin(t * Math.PI / 2)));
                    break;
                }
            }

            return r;
        }
    }
}
