using Garage.Game;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// LCD of the physical multimeter: shows the function/range selected in the dial (CarWork's meter) and the last
    /// reading taken with the probes (MeterMeasured events), like a real 3½ digit DMM.
    /// </summary>
    public sealed class MultimeterToolView : DeviceScreen
    {
        private string _last = "----";
        private string _where = "";
        private bool _subscribed;

        protected override void Start()
        {
            background = new Color(0.55f, 0.6f, 0.5f);
            foreground = new Color(0.05f, 0.06f, 0.05f);
            refreshHz = 2f;
            base.Start();
            Text.fontSize = 22;
        }

        private void OnEvent(GameEvent e)
        {
            if (e.Kind != GameEventKind.MeterMeasured)
            {
                return;
            }

            string d = e.Text;
            int b = d.IndexOf(']');
            int end = d.IndexOf("   (", System.StringComparison.Ordinal);
            _last = b >= 0 && end > b ? d.Substring(b + 1, end - b - 1).Trim() : (double.IsNaN(e.Value) ? "Err" : e.Value.ToString("0.00"));
            _where = e.Subject;
        }

        protected override void Refresh()
        {
            if (!_subscribed && Runner.Session != null)
            {
                Runner.Session.Events.Raised += OnEvent;
                _subscribed = true;
            }

            var m = Runner.Work?.Session.Multimeter;
            Text.text = $"<size=90><mspace=0.6em>{_last}</mspace></size>\n{m?.Mode}  {m?.Range}\n{_where}";
        }

        protected override void OnDestroy()
        {
            if (_subscribed && Runner != null && Runner.Session != null)
            {
                Runner.Session.Events.Raised -= OnEvent;
            }

            base.OnDestroy();
        }
    }
}
