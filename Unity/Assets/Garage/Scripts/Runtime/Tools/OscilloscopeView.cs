using Garage.Sim.Tools;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>Two-channel oscilloscope screen with trace, V/div and time base like a workshop scope.</summary>
    public sealed class OscilloscopeView : DeviceScreen
    {
        public string channelA = "ckp";
        public string pinA = "signal";
        public string channelB = "inj1";
        public string pinB = "control";
        [Tooltip("Ventana en ms")] public float windowMs = 40f;

        private DiagnosticSession _session;

        protected override void Refresh()
        {
            if (_session == null || _session.Car != Runner.Car)
            {
                _session = new DiagnosticSession(Runner.Car);
            }

            Waveform a = _session.Scope.Capture(channelA, pinA, Mathf.Min(windowMs, 150));
            Waveform b = string.IsNullOrEmpty(channelB) ? null : _session.Scope.Capture(channelB, pinB, Mathf.Min(windowMs, 150));
            ClearGraph(new Color(0.01f, 0.02f, 0.03f));
            double lo = System.Math.Min(a.Min, b?.Min ?? 0);
            double hi = System.Math.Max(a.Max, b?.Max ?? 1);
            Plot(a.Volts, lo, hi, new Color(1f, 0.9f, 0.1f));
            if (b != null)
            {
                Plot(b.Volts, lo, hi, new Color(0.2f, 0.8f, 1f));
            }

            ApplyGraph();
            Text.text = $"<color=#FFE61A>A: {a.Description}</color>\n<color=#33CCFF>B: {b?.Description}</color>\n{(hi - lo) / 8:0.00} V/div   {windowMs / 10:0.0} ms/div";
        }
    }
}
