using System.Collections.Generic;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>Audio groups (engine, workshop, tools, ambience, UI).</summary>
    public enum AudioBus
    {
        Engine,
        Workshop,
        Tools,
        Ambience,
        UI,
    }

    /// <summary>
    /// Lightweight mixer: per-bus volumes applied to registered sources. If an AudioMixer asset with groups named
    /// like the buses is assigned (created in the editor), sources are routed to it instead (see docs/DECISIONS.md D-55).
    /// </summary>
    public sealed class AudioBuses : MonoBehaviour
    {
        public UnityEngine.Audio.AudioMixer mixer;
        [Range(0, 1)] public float engine = 1f;
        [Range(0, 1)] public float workshop = 0.8f;
        [Range(0, 1)] public float tools = 0.9f;
        [Range(0, 1)] public float ambience = 0.6f;
        [Range(0, 1)] public float ui = 0.7f;

        private static AudioBuses _instance;
        private readonly List<(AudioSource Source, AudioBus Bus, float Base)> _sources = new List<(AudioSource, AudioBus, float)>();

        private void Awake() => _instance = this;

        /// <summary>Registers a source in a bus.</summary>
        public static void Register(AudioSource source, AudioBus bus)
        {
            if (_instance == null)
            {
                return;
            }

            if (_instance.mixer != null)
            {
                var groups = _instance.mixer.FindMatchingGroups(bus.ToString());
                if (groups.Length > 0)
                {
                    source.outputAudioMixerGroup = groups[0];
                    return;
                }
            }

            _instance._sources.Add((source, bus, source.volume));
        }

        /// <summary>Current gain of a bus, for sources whose volume is driven every frame (not registered).</summary>
        public static float GainOf(AudioBus b) => _instance == null ? 1f : _instance.Gain(b);

        private float Gain(AudioBus b) => b switch
        {
            AudioBus.Engine => engine,
            AudioBus.Workshop => workshop,
            AudioBus.Tools => tools,
            AudioBus.Ambience => ambience,
            _ => ui,
        };

        private void Update()
        {
            foreach (var (source, bus, baseVolume) in _sources)
            {
                if (source != null)
                {
                    source.volume = baseVolume * Gain(bus);
                }
            }
        }
    }
}
