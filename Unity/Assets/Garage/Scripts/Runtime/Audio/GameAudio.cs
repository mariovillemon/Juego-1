using System.Collections.Generic;
using Garage.Game;
using Garage.Game.Audio;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Plays the sound bank (data/base/audio.json): one-shots for game events (connector click, scanner beep,
    /// ratchet, impact gun, starter, cash, errors…) and the workshop ambience loop. Clips come from
    /// Resources/Audio/&lt;id&gt; (fetched by tools/fetch-audio); without a clip a short procedural sound is
    /// synthesised so the game is never silent.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public SimulationRunner runner;
        public CarAssembler assembler;

        private SoundBank _bank;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _ui;
        private bool _subscribed;

        private void Start()
        {
            if (runner == null)
            {
                runner = FindFirstObjectByType<SimulationRunner>();
            }

            _ui = gameObject.AddComponent<AudioSource>();
            _ui.spatialBlend = 0f;
            _ui.playOnAwake = false;
            if (runner != null && runner.Content != null)
            {
                _bank = SoundBank.Load(runner.Content);
                foreach (SoundDefinition s in _bank.Sounds)
                {
                    if (s.Kind == SoundKind.Loop && s.Bus == "ambience")
                    {
                        var go = new GameObject("Ambience_" + s.Id);
                        go.transform.SetParent(transform, false);
                        AudioSource a = go.AddComponent<AudioSource>();
                        a.clip = Clip(s.Id);
                        a.loop = true;
                        a.spatialBlend = 0f;
                        a.volume = (float)s.Volume;
                        a.Play();
                        AudioBuses.Register(a, AudioBus.Ambience);
                    }
                }
            }
        }

        private void Update()
        {
            if (!_subscribed && runner != null && runner.Session != null)
            {
                runner.Session.Events.Raised += OnEvent;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && runner != null && runner.Session != null)
            {
                runner.Session.Events.Raised -= OnEvent;
            }
        }

        private void OnEvent(GameEvent e)
        {
            if (_bank == null)
            {
                return;
            }

            foreach (SoundDefinition s in _bank.ForEvent(e.Kind))
            {
                AudioBus bus = s.Bus == "ui" ? AudioBus.UI : s.Bus == "engine" ? AudioBus.Engine : AudioBus.Tools;
                float vol = (float)s.Volume * AudioBuses.GainOf(bus);
                if (s.Bus == "ui" || assembler == null || assembler.CarRoot == null)
                {
                    _ui.PlayOneShot(Clip(s.Id), vol);
                }
                else
                {
                    AudioSource.PlayClipAtPoint(Clip(s.Id), assembler.CarRoot.position + Vector3.up * 0.8f + assembler.CarRoot.forward * 1.3f, vol);
                }
            }
        }

        private AudioClip Clip(string id)
        {
            if (_clips.TryGetValue(id, out AudioClip c))
            {
                return c;
            }

            c = Resources.Load<AudioClip>("Audio/" + id);
            if (c == null)
            {
                c = Synth(id);
            }

            _clips[id] = c;
            return c;
        }

        /// <summary>Procedural stand-ins (clicks, beeps, ratchet, rumble) until recorded clips are fetched.</summary>
        private static AudioClip Synth(string id)
        {
            const int rate = 44100;
            float seconds = id.StartsWith("ambience") ? 8f : id.Contains("impact") || id.Contains("starter") ? 1.2f : id.Contains("ratchet") ? 0.8f : 0.25f;
            var data = new float[(int)(rate * seconds)];
            var rng = new System.Random(id.GetHashCode());
            float lp = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float n = (float)(rng.NextDouble() * 2 - 1);
                float v;
                if (id.Contains("beep") || id == "ui.cash")
                {
                    v = Mathf.Sin(2 * Mathf.PI * (id == "ui.cash" ? 1320 : 2400) * t) * (t < seconds * 0.6f ? 0.4f : 0f);
                }
                else if (id.Contains("error"))
                {
                    v = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 180 * t)) * 0.15f * Mathf.Exp(-t * 8);
                }
                else if (id.Contains("ratchet"))
                {
                    v = (i % (rate / 18) < 220 ? n : 0) * 0.7f;
                }
                else if (id.Contains("impact"))
                {
                    v = (i % (rate / 25) < 400 ? n : 0) * 0.6f + n * 0.1f;
                }
                else if (id.Contains("starter"))
                {
                    lp += (n - lp) * 0.1f;
                    v = lp * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 9 * t)) * 0.9f;
                }
                else if (id.StartsWith("ambience"))
                {
                    lp += (n - lp) * 0.01f;
                    v = lp * 0.25f;
                }
                else
                {
                    v = n * Mathf.Exp(-t * 60) * 0.8f; // click
                }

                data[i] = v;
            }

            AudioClip clip = AudioClip.Create(id, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
