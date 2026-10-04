using System.Collections.Generic;
using Garage.Game;
using Garage.Sim.Faults;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Plays the sensory cues the simulation emits (vacuum hiss, rod knock, fan, exhaust tick...) and drives exhaust
    /// smoke particles (black/white/blue). Clips are looked up in Resources/Audio/&lt;cue id&gt; (e.g. sound.vacuum_hiss);
    /// procedural noise is used when no clip exists yet.
    /// </summary>
    public sealed class CueAudioAndSmoke : MonoBehaviour
    {
        public SimulationRunner runner;
        public CarAssembler assembler;
        public ParticleSystem exhaustSmoke;
        public float refreshHz = 5f;

        private readonly Dictionary<string, AudioSource> _sources = new Dictionary<string, AudioSource>();
        private float _timer;
        private bool _subscribed;

        private void OnGameEvent(GameEvent e)
        {
            if (e.Kind != GameEventKind.EngineFailure)
            {
                return;
            }

            // Visible and audible consequence of a catastrophic failure: a bang and a cloud of smoke.
            if (exhaustSmoke != null)
            {
                var main = exhaustSmoke.main;
                main.startColor = e.Subject.Contains("junta") ? new Color(0.92f, 0.92f, 0.92f, 0.8f) : new Color(0.15f, 0.15f, 0.17f, 0.85f);
                exhaustSmoke.Emit(200);
            }

            Vector3 at = assembler != null && assembler.CarRoot != null ? assembler.CarRoot.position + Vector3.up * 0.8f : transform.position;
            AudioSource.PlayClipAtPoint(Bang(), at, 1f);
        }

        private static AudioClip Bang()
        {
            const int rate = 44100;
            var data = new float[rate / 2];
            var rng = new System.Random(17);
            float lp = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += (n - lp) * 0.2f;
                data[i] = lp * Mathf.Exp(-i / (rate * 0.08f)) * 1.6f;
            }

            AudioClip clip = AudioClip.Create("bang", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (_subscribed && runner != null && runner.Session != null)
            {
                runner.Session.Events.Raised -= OnGameEvent;
            }
        }

        private void Update()
        {
            if (!_subscribed && runner != null && runner.Session != null)
            {
                runner.Session.Events.Raised += OnGameEvent;
                _subscribed = true;
            }

            _timer += Time.deltaTime;
            if (_timer < 1f / refreshHz || runner == null || runner.Car == null)
            {
                return;
            }

            _timer = 0;
            var active = new HashSet<string>();
            Color smoke = Color.clear;
            float smokeRate = 0;
            foreach (SensoryCue cue in runner.Car.Cues)
            {
                if (cue.Channel == CueChannel.Sound)
                {
                    active.Add(cue.Id);
                    AudioSource s = SourceFor(cue);
                    s.volume = (float)cue.Intensity;
                }
                else if (cue.Channel == CueChannel.Smoke)
                {
                    smokeRate = Mathf.Max(smokeRate, (float)cue.Intensity * 40f);
                    smoke = cue.Id == "smoke.black" ? new Color(0.05f, 0.05f, 0.05f, 0.8f) : cue.Id == "smoke.blue" ? new Color(0.5f, 0.55f, 0.7f, 0.5f) : new Color(0.9f, 0.9f, 0.9f, 0.6f);
                }
            }

            foreach (var kv in _sources)
            {
                if (!active.Contains(kv.Key))
                {
                    kv.Value.volume = Mathf.MoveTowards(kv.Value.volume, 0, 0.3f);
                }
            }

            if (exhaustSmoke != null)
            {
                var em = exhaustSmoke.emission;
                em.rateOverTime = smokeRate;
                var main = exhaustSmoke.main;
                main.startColor = smoke;
            }
        }

        private AudioSource SourceFor(SensoryCue cue)
        {
            if (_sources.TryGetValue(cue.Id, out AudioSource s))
            {
                return s;
            }

            var go = new GameObject("Cue_" + cue.Id);
            Transform parent = transform;
            if (assembler != null && assembler.CarRoot != null)
            {
                Transform slot = assembler.CarRoot.Find("EngineBay/Slot_" + cue.Location);
                parent = slot != null ? slot : assembler.CarRoot;
            }

            go.transform.SetParent(parent, false);
            s = go.AddComponent<AudioSource>();
            s.spatialBlend = 1f;
            s.loop = true;
            s.clip = Resources.Load<AudioClip>("Audio/" + cue.Id) ?? ProceduralClip(cue.Id);
            s.Play();
            AudioBuses.Register(s, cue.Id == "sound.fan" ? AudioBus.Workshop : AudioBus.Engine);
            _sources[cue.Id] = s;
            return s;
        }

        private static AudioClip ProceduralClip(string id)
        {
            const int rate = 44100;
            var data = new float[rate];
            var rng = new System.Random(id.Length * 7919);
            float lp = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                switch (id)
                {
                    case "sound.vacuum_hiss":
                        data[i] = n * 0.3f; // white noise ≈ hiss
                        break;
                    case "sound.rod_knock":
                    case "sound.exhaust_tick":
                        data[i] = (i % 2205 < 120 ? n : 0) * 0.8f;
                        break;
                    default:
                        lp += (n - lp) * 0.05f; // rumble (fan, chain)
                        data[i] = lp * 0.6f;
                        break;
                }
            }

            AudioClip clip = AudioClip.Create(id, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
