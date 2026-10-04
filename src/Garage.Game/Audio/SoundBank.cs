using System;
using System.Collections.Generic;
using System.Linq;
using Garage.Data;
using Garage.Data.Json;

namespace Garage.Game.Audio
{
    /// <summary>Kind of sound entry in data/base/audio.json.</summary>
    public enum SoundKind
    {
        /// <summary>Engine loop recorded at a known rpm and load (crossfaded by <see cref="EngineSoundMixer"/>).</summary>
        EngineLayer,

        /// <summary>One-shot played on a game event (connector click, ratchet…).</summary>
        Event,

        /// <summary>Continuous loop (workshop ambience, fan, turbo whine, simulation cues).</summary>
        Loop,
    }

    /// <summary>One sound of the bank.</summary>
    public sealed class SoundDefinition
    {
        /// <summary>Id (also the clip name under Resources/Audio/).</summary>
        public string Id { get; set; } = "";

        /// <summary>Kind.</summary>
        public SoundKind Kind { get; set; }

        /// <summary>Mixer bus (engine, workshop, tools, ambience, ui).</summary>
        public string Bus { get; set; } = "workshop";

        /// <summary>Engine layers: recording rpm.</summary>
        public double Rpm { get; set; }

        /// <summary>Engine layers: 0 = off load (overrun/free rev), 1 = on load.</summary>
        public double Load { get; set; }

        /// <summary>Engine layers: "exterior" or "interior".</summary>
        public string Perspective { get; set; } = "exterior";

        /// <summary>Events: GameEventKind names that trigger it.</summary>
        public List<string> Triggers { get; } = new List<string>();

        /// <summary>Linear volume 0–1.</summary>
        public double Volume { get; set; } = 1;

        /// <summary>Freesound search used by tools/fetch-audio (CC0 only).</summary>
        public string Query { get; set; } = "";
    }

    /// <summary>The sound bank (data/base/audio.json): what exists, how it is mixed and when it plays.</summary>
    public sealed class SoundBank
    {
        /// <summary>All sounds.</summary>
        public List<SoundDefinition> Sounds { get; } = new List<SoundDefinition>();

        /// <summary>Engine layers of a perspective.</summary>
        public List<SoundDefinition> EngineLayers(string perspective) =>
            Sounds.Where(s => s.Kind == SoundKind.EngineLayer && s.Perspective == perspective).ToList();

        /// <summary>Event sounds triggered by an event kind.</summary>
        public IEnumerable<SoundDefinition> ForEvent(GameEventKind kind) =>
            Sounds.Where(s => s.Kind == SoundKind.Event && s.Triggers.Contains(kind.ToString()));

        /// <summary>Loads the bank from content (empty if the file is missing).</summary>
        public static SoundBank Load(ContentDatabase db)
        {
            var bank = new SoundBank();
            foreach (KeyValuePair<string, JsonValue> kv in db.Raw("audio").OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                JsonValue j = kv.Value;
                var s = new SoundDefinition
                {
                    Id = kv.Key,
                    Kind = j.Str("kind") switch { "engine_layer" => SoundKind.EngineLayer, "loop" => SoundKind.Loop, _ => SoundKind.Event },
                    Bus = j.Str("bus", "workshop"),
                    Rpm = j.Num("rpm"),
                    Load = j.Num("load"),
                    Perspective = j.Str("perspective", "exterior"),
                    Volume = j.Num("volume", 1),
                    Query = j.Str("query"),
                };
                foreach (JsonValue t in j["triggers"].Items)
                {
                    s.Triggers.Add(t.StringValue);
                }

                bank.Sounds.Add(s);
            }

            return bank;
        }
    }
}
