using System.Collections.Generic;
using Garage.Game.Audio;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Engine sound in layers driven by the simulation: firing pulses at the real firing frequency (rpm/120 × cylinders),
    /// intake/load noise, turbo whine, and per-cylinder dropouts when it misfires. When the recorded layers of the sound
    /// bank exist (data/base/audio.json → Resources/Audio/engine.*), they are mixed instead by EngineSoundMixer:
    /// equal-power crossfade by rpm and load, pitched by rpm, exterior/interior by the listener's position.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class EngineAudio : MonoBehaviour
    {
        public SimulationRunner runner;
        [Tooltip("Distancia (m) del oyente al asiento del conductor por debajo de la cual se oye la capa interior")] public float interiorDistance = 0.9f;

        private volatile float _rpm;
        private volatile float _load;
        private volatile float _turbo;
        private volatile float _misfire;
        private volatile float _knock;
        private int _cylinders = 4;
        private double _phase;
        private double _turboPhase;
        private uint _noise = 2463534242;
        private int _sampleRate;
        private readonly List<(SoundDefinition Def, AudioSource Source, bool Interior)> _layers = new List<(SoundDefinition, AudioSource, bool)>();
        private List<SoundDefinition> _ext, _int;
        private bool _useSamples;

        private void Start()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.loop = true;
            src.playOnAwake = true;
            if (!src.isPlaying)
            {
                src.Play();
            }

            AudioBuses.Register(src, AudioBus.Engine);
            if (runner != null && runner.Content != null)
            {
                // Recorded layers (Resources/Audio/<id>, fetched by tools/fetch-audio) replace the synthesis when
                // the full exterior set exists; interior layers are optional.
                SoundBank bank = SoundBank.Load(runner.Content);
                _ext = bank.EngineLayers("exterior");
                _int = bank.EngineLayers("interior");
                _useSamples = _ext.Count > 0 && _ext.TrueForAll(l => Resources.Load<AudioClip>("Audio/" + l.Id) != null);
                if (_useSamples)
                {
                    AddLayers(_ext, false);
                    if (_int.TrueForAll(l => Resources.Load<AudioClip>("Audio/" + l.Id) != null))
                    {
                        AddLayers(_int, true);
                    }
                }
            }
        }

        private void AddLayers(List<SoundDefinition> defs, bool interior)
        {
            foreach (SoundDefinition d in defs)
            {
                AudioSource a = gameObject.AddComponent<AudioSource>();
                a.clip = Resources.Load<AudioClip>("Audio/" + d.Id);
                a.loop = true;
                a.spatialBlend = interior ? 0f : 1f;
                a.volume = 0;
                a.Play();
                _layers.Add((d, a, interior));
            }
        }

        private void Update()
        {
            if (runner == null || runner.Car == null)
            {
                return;
            }

            var s = runner.Car.Engine.State;
            _cylinders = runner.Car.Definition.Engine.Cylinders;
            _rpm = (float)s.Rpm;
            _load = Mathf.Clamp01((float)s.RelativeLoad);
            _turbo = (float)s.TurboSpeed;
            float miss = 0;
            foreach (double b in s.CylinderBurn)
            {
                miss = Mathf.Max(miss, 1f - (float)b);
            }

            _misfire = miss;
            float k = 0;
            foreach (double kk in s.CylinderKnock)
            {
                k = Mathf.Max(k, (float)kk);
            }

            _knock = k;
            if (_useSamples)
            {
                bool hasInterior = _layers.Exists(l => l.Interior);
                float interior = 0f;
                Camera cam = Camera.main;
                if (hasInterior && cam != null)
                {
                    interior = Mathf.Clamp01(1f - (Vector3.Distance(cam.transform.position, transform.position) - interiorDistance) / 0.5f);
                }

                var ext = EngineSoundMixer.Mix(_ext, _rpm, _load, 1 - interior);
                var inn = hasInterior ? EngineSoundMixer.Mix(_int, _rpm, _load, interior) : new List<LayerMix>();
                foreach (var (def, source, isInterior) in _layers)
                {
                    LayerMix m = (isInterior ? inn : ext).Find(x => x.Id == def.Id);
                    source.volume = (float)m.Volume * AudioBuses.GainOf(AudioBus.Engine);
                    source.pitch = (float)m.Pitch;
                }
            }
        }

        private float Noise()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            return (_noise / (float)uint.MaxValue) * 2f - 1f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_useSamples || _sampleRate == 0)
            {
                return;
            }

            float rpm = _rpm;
            float fireHz = rpm / 120f * _cylinders;
            double inc = fireHz / _sampleRate;
            double tInc = (2000 + 30000 * _turbo) / _sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                _phase += inc;
                int pulse = (int)_phase;
                double frac = _phase - pulse;
                bool skip = _misfire > 0.05f && (pulse % _cylinders == 0) && Noise() < _misfire * 2f - 1f + 0.5f;
                float env = rpm < 50 ? 0 : Mathf.Exp((float)(-frac * 9.0));
                float s = skip ? 0 : env * (0.5f + 0.5f * _load) * (Noise() * 0.4f + 0.6f);
                s += Noise() * 0.03f * _load;
                _turboPhase += tInc;
                s += Mathf.Sin((float)(_turboPhase * 2 * Mathf.PI)) * 0.04f * _turbo;
                if (_knock > 0.1f && frac < 0.02)
                {
                    s += Noise() * _knock * 0.6f;
                }

                s *= 0.35f;
                for (int c = 0; c < channels; c++)
                {
                    data[i + c] += s;
                }
            }

            if (_phase > 1e6)
            {
                _phase -= 1e6;
            }
        }
    }
}
