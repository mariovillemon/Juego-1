using System.Collections.Generic;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>Dyno room monitor: live power/torque trace versus rpm while the car runs on the rollers.</summary>
    public sealed class DynoView : DeviceScreen
    {
        private readonly List<double> _rpm = new List<double>();
        private readonly List<double> _power = new List<double>();
        private readonly List<double> _torque = new List<double>();

        protected override void Start()
        {
            refreshHz = 10f;
            base.Start();
        }

        protected override void Refresh()
        {
            var s = Runner.Car.Engine.State;
            if (Runner.Car.Pedal > 0.9 && s.Rpm > 1500)
            {
                _rpm.Add(s.Rpm);
                _power.Add(Garage.Sim.Core.Physics.KwToPs(s.PowerKw));
                _torque.Add(s.TorqueNm);
            }
            else if (Runner.Car.Pedal < 0.1 && _rpm.Count > 0 && s.Rpm < 1500)
            {
                _rpm.Clear();
                _power.Clear();
                _torque.Clear();
            }

            ClearGraph(new Color(0.02f, 0.02f, 0.05f));
            double max = 50;
            foreach (double p in _power)
            {
                max = System.Math.Max(max, p);
            }

            foreach (double t in _torque)
            {
                max = System.Math.Max(max, t);
            }

            Plot(_power.ToArray(), 0, max * 1.1, new Color(1f, 0.3f, 0.2f));
            Plot(_torque.ToArray(), 0, max * 1.1, new Color(0.3f, 0.7f, 1f));
            ApplyGraph();
            Text.text = $"<b>BANCO DE RODILLOS</b>   {s.Rpm:0} rpm   {Garage.Sim.Core.Physics.KwToPs(s.PowerKw):0} CV   {s.TorqueNm:0} N·m\nλ {s.ExhaustLambda:0.00}   turbo {s.BoostKpa:0} kPa   avance {s.SparkAdvanceDeg:0.0}°   EGT {s.ExhaustGasC:0} °C";
        }
    }
}
