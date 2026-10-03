using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Engine sound in layers driven by the simulation: firing pulses at the real firing frequency (rpm/120 × cylinders),
    /// intake/load noise, turbo whine, and per-cylinder dropouts when it misfires. Procedural until recorded CC0/CC-BY
    /// samples are assigned to <see cref="sampleLayers"/> (crossfaded by rpm and pitched).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class EngineAudio : MonoBehaviour
    {
        public SimulationRunner runner;
        [Tooltip("Muestras grabadas a rpm conocidas (opcional). Se mezclan por rpm.")] public AudioClip[] sampleLayers;
        public float[] sampleRpm = { 900, 2500, 4500, 6500 };

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
        private AudioSource[] _layers;

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
            if (sampleLayers != null && sampleLayers.Length > 0)
            {
                _layers = new AudioSource[sampleLayers.Length];
                for (int i = 0; i < sampleLayers.Length; i++)
                {
                    _layers[i] = gameObject.AddComponent<AudioSource>();
                    _layers[i].clip = sampleLayers[i];
                    _layers[i].loop = true;
                    _layers[i].spatialBlend = 1f;
                    _layers[i].Play();
                    AudioBuses.Register(_layers[i], AudioBus.Engine);
                }
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
            if (_layers != null)
            {
                for (int i = 0; i < _layers.Length && i < sampleRpm.Length; i++)
                {
                    float d = Mathf.Abs(_rpm - sampleRpm[i]);
                    _layers[i].volume = Mathf.Clamp01(1f - d / 1800f) * (0.4f + 0.6f * _load);
                    _layers[i].pitch = Mathf.Max(0.3f, _rpm / sampleRpm[i]);
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
            if (_layers != null || _sampleRate == 0)
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
