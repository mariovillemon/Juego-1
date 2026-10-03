using System;
using System.Collections.Generic;
using Garage.Sim.Components;
using Garage.Sim.Ecu;
using Garage.Sim.Faults;

namespace Garage.Sim.Vehicle
{
    /// <summary>A module on the simplified high speed CAN bus.</summary>
    public sealed class CanModule
    {
        /// <summary>Creates a module.</summary>
        public CanModule(string id, string name, string lostCode, DtcStore store)
        {
            Id = id;
            Name = name;
            LostCommCode = lostCode;
            Dtcs = store;
        }

        /// <summary>Module id (ecm, abs, ipc, bcm).</summary>
        public string Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Code other modules set when this one goes silent (U0100, U0121...).</summary>
        public string LostCommCode { get; }

        /// <summary>DTC memory of this module.</summary>
        public DtcStore Dtcs { get; }

        /// <summary>Module powered and transmitting.</summary>
        public bool Alive { get; set; } = true;

        /// <summary>Connected to the bus (stub not open).</summary>
        public bool Connected { get; set; } = true;

        /// <summary>Last time each peer was heard.</summary>
        public Dictionary<string, double> LastHeard { get; } = new Dictionary<string, double>();

        /// <summary>Message period (s).</summary>
        public double PeriodS { get; set; } = 0.02;
    }

    /// <summary>
    /// Simplified CAN: modules broadcast periodically; receivers time out peers after 0.5 s and store U codes.
    /// Bus short → bus off on every module (U0073 in ECM, U0001 elsewhere).
    /// </summary>
    public sealed class CanNetwork
    {
        private readonly List<CanModule> _modules = new List<CanModule>();
        private readonly Car _car;
        private double _time;

        /// <summary>Creates the network for a car.</summary>
        public CanNetwork(Car car)
        {
            _car = car;
        }

        /// <summary>Modules.</summary>
        public IReadOnlyList<CanModule> Modules => _modules;

        /// <summary>Bus off due to a short.</summary>
        public bool BusOff { get; private set; }

        /// <summary>ECM receives ABS (wheel speed) messages.</summary>
        public bool EcmSeesAbs { get; private set; } = true;

        /// <summary>Adds a module.</summary>
        public void Add(CanModule m) => _modules.Add(m);

        /// <summary>Gets a module.</summary>
        public CanModule? Get(string id) => _modules.Find(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Lost communication code per module id.</summary>
        public static string LostCodeFor(string id)
        {
            switch (id.ToLowerInvariant())
            {
                case "ecm": return "U0100";
                case "abs": return "U0121";
                case "ipc": return "U0155";
                case "bcm": return "U0140";
                case "tcm": return "U0101";
                default: return "U0001";
            }
        }

        /// <summary>Display name per module id.</summary>
        public static string NameFor(string id)
        {
            switch (id.ToLowerInvariant())
            {
                case "ecm": return "Motor (ECM)";
                case "abs": return "ABS/ESP";
                case "ipc": return "Cuadro de instrumentos";
                case "bcm": return "Carrocería (BCM)";
                case "tcm": return "Cambio (TCM)";
                default: return id;
            }
        }

        /// <summary>Advances the bus.</summary>
        public void Step(double dt)
        {
            _time += dt;
            if (!_car.KeyOn)
            {
                foreach (CanModule m in _modules)
                {
                    m.LastHeard.Clear();
                }

                return;
            }

            Component? bus = _car.Parts.Find(ComponentKind.CanBus);
            FaultSet faults = _car.Faults;
            BusOff = bus != null && (faults.Has(bus.Id, EffectKind.WireShortGround) || faults.Has(bus.Id, EffectKind.WireShortPower));
            foreach (CanModule m in _modules)
            {
                Component? mc = _car.Parts.Get("module_" + m.Id);
                m.Alive = mc == null || !(faults.Has(mc.Id, EffectKind.Dead) || faults.Has(mc.Id, EffectKind.WireOpen, "supply"));
                m.Connected = bus == null || !faults.Has(bus.Id, EffectKind.WireOpen, m.Id);
            }

            foreach (CanModule rx in _modules)
            {
                if (!rx.Alive)
                {
                    continue;
                }

                foreach (CanModule tx in _modules)
                {
                    if (tx == rx)
                    {
                        continue;
                    }

                    bool heard = !BusOff && tx.Alive && tx.Connected && rx.Connected;
                    if (heard || !rx.LastHeard.ContainsKey(tx.Id))
                    {
                        rx.LastHeard[tx.Id] = heard ? _time : (rx.LastHeard.ContainsKey(tx.Id) ? rx.LastHeard[tx.Id] : _time);
                    }

                    bool lost = _time - rx.LastHeard[tx.Id] > 0.5;
                    if (lost && !BusOff && rx.Connected)
                    {
                        rx.Dtcs.Fail(tx.LostCommCode, null);
                    }
                    else if (!lost)
                    {
                        rx.Dtcs.Pass(tx.LostCommCode);
                    }
                }

                if (BusOff)
                {
                    rx.Dtcs.Fail(rx.Id == "ecm" ? "U0073" : "U0001", null);
                }
            }

            CanModule? ecm = Get("ecm");
            CanModule? abs = Get("abs");
            EcmSeesAbs = abs == null || ecm == null || (ecm.LastHeard.TryGetValue("abs", out double t) && _time - t <= 0.5);
        }
    }
}
