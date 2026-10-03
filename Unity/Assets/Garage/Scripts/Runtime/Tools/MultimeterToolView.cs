using Garage.Sim.Tools;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Digital multimeter: big LCD digits of the measurement between the two probes configured
    /// (component:side:pin) with the selected function and range, as a real 3½ digit DMM.
    /// </summary>
    public sealed class MultimeterToolView : DeviceScreen
    {
        public MeterMode mode = MeterMode.DcVolts;
        public MeterRange range = MeterRange.Auto;
        public string redProbe = "map:harness:ref";
        public string blackProbe = "gnd";

        private DiagnosticSession _session;

        protected override void Start()
        {
            background = new Color(0.55f, 0.6f, 0.5f);
            foreground = new Color(0.05f, 0.06f, 0.05f);
            refreshHz = 2f;
            base.Start();
            Text.fontSize = 22;
        }

        protected override void Refresh()
        {
            if (_session == null || _session.Car != Runner.Car)
            {
                _session = new DiagnosticSession(Runner.Car);
            }

            _session.Multimeter.Mode = mode;
            _session.Multimeter.Range = range;
            ToolResult r = _session.Multimeter.Measure(redProbe, blackProbe);
            string display = r.Display;
            int b = display.IndexOf(']');
            int e = display.IndexOf("   (");
            string digits = b >= 0 && e > b ? display.Substring(b + 1, e - b - 1).Trim() : display;
            Text.text = $"<size=90><mspace=0.6em>{digits}</mspace></size>\n{mode}  {range}\nROJA {redProbe}\nNEGRA {blackProbe}";
        }
    }
}
