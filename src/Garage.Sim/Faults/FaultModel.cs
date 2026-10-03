using System;
using System.Collections.Generic;
using Garage.Sim.Core;

namespace Garage.Sim.Faults
{
    /// <summary>Generic effect a failure mode has on a component. Components interpret these; nothing is scenario specific.</summary>
    public enum EffectKind
    {
        /// <summary>Sensor output offset in physical units (bias).</summary>
        SignalOffset,
        /// <summary>Sensor output multiplicative error (1 + magnitude).</summary>
        SignalGain,
        /// <summary>Sensor output frozen at a physical value (magnitude).</summary>
        SignalStuck,
        /// <summary>Random noise added to the signal (std dev in physical units).</summary>
        SignalNoise,
        /// <summary>Slow response: extra time constant in seconds.</summary>
        SignalLag,
        /// <summary>Signal drops out completely (reads as open).</summary>
        SignalDropout,
        /// <summary>Wire open circuit on a pin.</summary>
        WireOpen,
        /// <summary>Wire short to ground on a pin.</summary>
        WireShortGround,
        /// <summary>Wire short to battery positive on a pin.</summary>
        WireShortPower,
        /// <summary>Additional resistance in a wire (ohms).</summary>
        WireHighResistance,
        /// <summary>Corroded connector pin (ohms).</summary>
        ConnectorCorrosion,
        /// <summary>Leak; magnitude is an equivalent area in mm² (air) or fraction (fluids).</summary>
        Leak,
        /// <summary>Flow restriction; magnitude 0..1 fraction of flow lost.</summary>
        Restriction,
        /// <summary>Mechanical wear; magnitude 0..1.</summary>
        Wear,
        /// <summary>Valve or actuator stuck open; magnitude is the stuck position 0..1.</summary>
        StuckOpen,
        /// <summary>Valve or actuator stuck closed.</summary>
        StuckClosed,
        /// <summary>Clogged injector/filter; magnitude 0..1 flow reduction.</summary>
        Clog,
        /// <summary>Weak component (coil, pump, battery); magnitude 0..1 capability loss.</summary>
        Weak,
        /// <summary>Component fully dead.</summary>
        Dead,
        /// <summary>Mechanical slack (timing chain stretch in crank degrees).</summary>
        Slack,
        /// <summary>Internal short of a winding; magnitude 0..1 of turns shorted.</summary>
        InternalShort,
        /// <summary>Component leaks fuel (injector dribble); magnitude is extra fuel fraction.</summary>
        Dribble,
    }

    /// <summary>When a fault manifests.</summary>
    public enum ConditionKind
    {
        /// <summary>Always active.</summary>
        Always,
        /// <summary>Only while engine coolant is below Threshold °C.</summary>
        Cold,
        /// <summary>Only while engine coolant is above Threshold °C.</summary>
        Hot,
        /// <summary>Only above Threshold engine load (0..1).</summary>
        AboveLoad,
        /// <summary>Only above Threshold rpm.</summary>
        AboveRpm,
        /// <summary>Only below Threshold rpm.</summary>
        BelowRpm,
        /// <summary>With vibration (rough running or rpm band between Threshold and Threshold2).</summary>
        Vibration,
        /// <summary>Randomly toggles; Threshold = duty (0..1), Threshold2 = mean period s.</summary>
        Intermittent,
    }

    /// <summary>Condition gating a fault.</summary>
    public sealed class FaultCondition
    {
        /// <summary>Creates a condition.</summary>
        public FaultCondition(ConditionKind kind, double threshold = 0, double threshold2 = 0)
        {
            Kind = kind;
            Threshold = threshold;
            Threshold2 = threshold2;
        }

        /// <summary>Always active condition.</summary>
        public static FaultCondition AlwaysOn => new FaultCondition(ConditionKind.Always);

        /// <summary>Kind.</summary>
        public ConditionKind Kind { get; }

        /// <summary>Primary threshold.</summary>
        public double Threshold { get; }

        /// <summary>Secondary threshold.</summary>
        public double Threshold2 { get; }

        /// <summary>Short Spanish description for UI.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case ConditionKind.Cold: return $"en frío (<{Threshold:0} °C)";
                case ConditionKind.Hot: return $"en caliente (>{Threshold:0} °C)";
                case ConditionKind.AboveLoad: return $"con carga >{Threshold * 100:0} %";
                case ConditionKind.AboveRpm: return $"por encima de {Threshold:0} rpm";
                case ConditionKind.BelowRpm: return $"por debajo de {Threshold:0} rpm";
                case ConditionKind.Vibration: return "con vibración";
                case ConditionKind.Intermittent: return "intermitente";
                default: return "permanente";
            }
        }
    }

    /// <summary>Data-defined failure mode (loaded from failure_modes.json).</summary>
    public sealed class FailureModeDefinition
    {
        /// <summary>Unique id, e.g. "wire_open".</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name (Spanish).</summary>
        public string Name { get; set; } = "";

        /// <summary>Family for grouping/tests: signal, wiring, mechanical, leak, actuator...</summary>
        public string Family { get; set; } = "";

        /// <summary>Generic effect.</summary>
        public EffectKind Effect { get; set; }

        /// <summary>Component kinds this mode applies to.</summary>
        public List<string> AppliesTo { get; set; } = new List<string>();

        /// <summary>Pin role targeted by wiring faults (signal, ground, ref, supply, control, heater...). Empty = any/none.</summary>
        public string Pin { get; set; } = "";

        /// <summary>Magnitude at severity 0.</summary>
        public double MagnitudeMin { get; set; }

        /// <summary>Magnitude at severity 1.</summary>
        public double MagnitudeMax { get; set; } = 1;

        /// <summary>Difficulty contribution 1..5.</summary>
        public int Difficulty { get; set; } = 1;

        /// <summary>Repair action: replace, repair_wire, clean, adjust.</summary>
        public string Repair { get; set; } = "replace";

        /// <summary>Description (Spanish).</summary>
        public string Description { get; set; } = "";

        /// <summary>Allowed conditions for generator (empty = Always only).</summary>
        public List<ConditionKind> Conditions { get; set; } = new List<ConditionKind>();

        /// <summary>Magnitude for a severity 0..1.</summary>
        public double MagnitudeFor(double severity) => MathUtil.Lerp(MagnitudeMin, MagnitudeMax, MathUtil.Clamp01(severity));
    }

    /// <summary>Snapshot of state used to evaluate fault conditions.</summary>
    public struct ConditionContext
    {
        /// <summary>Coolant temperature °C.</summary>
        public double CoolantC;
        /// <summary>Load 0..1.</summary>
        public double Load;
        /// <summary>Engine speed.</summary>
        public double Rpm;
        /// <summary>Vibration level 0..1.</summary>
        public double Vibration;
        /// <summary>Step.</summary>
        public double Dt;
    }

    /// <summary>A concrete fault on a component instance.</summary>
    public sealed class FaultInstance
    {
        private double _toggleTimer;

        /// <summary>Creates a fault.</summary>
        public FaultInstance(FailureModeDefinition mode, string componentId, double severity, FaultCondition? condition = null, string? pin = null)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            ComponentId = componentId;
            Severity = MathUtil.Clamp01(severity);
            Condition = condition ?? FaultCondition.AlwaysOn;
            Pin = string.IsNullOrEmpty(pin) ? mode.Pin : pin!;
            Magnitude = mode.MagnitudeFor(Severity);
            Active = Condition.Kind == ConditionKind.Always;
        }

        /// <summary>Failure mode.</summary>
        public FailureModeDefinition Mode { get; }

        /// <summary>Target component id.</summary>
        public string ComponentId { get; }

        /// <summary>Pin role for wiring faults.</summary>
        public string Pin { get; }

        /// <summary>Severity 0..1.</summary>
        public double Severity { get; }

        /// <summary>Effect magnitude derived from severity.</summary>
        public double Magnitude { get; set; }

        /// <summary>Gate condition.</summary>
        public FaultCondition Condition { get; }

        /// <summary>Whether the fault currently manifests.</summary>
        public bool Active { get; private set; }

        /// <summary>True once repaired (kept for history/evaluation).</summary>
        public bool Repaired { get; set; }

        /// <summary>Optional origin (e.g. "chain:misfire→catalyst").</summary>
        public string Origin { get; set; } = "";

        /// <summary>Effect shortcut.</summary>
        public EffectKind Effect => Mode.Effect;

        /// <summary>Re-evaluates the gating condition.</summary>
        public void Evaluate(in ConditionContext ctx, DeterministicRandom rng)
        {
            if (Repaired)
            {
                Active = false;
                return;
            }

            switch (Condition.Kind)
            {
                case ConditionKind.Always: Active = true; break;
                case ConditionKind.Cold: Active = ctx.CoolantC < Condition.Threshold; break;
                case ConditionKind.Hot: Active = ctx.CoolantC > Condition.Threshold; break;
                case ConditionKind.AboveLoad: Active = ctx.Load > Condition.Threshold; break;
                case ConditionKind.AboveRpm: Active = ctx.Rpm > Condition.Threshold; break;
                case ConditionKind.BelowRpm: Active = ctx.Rpm > 50 && ctx.Rpm < Condition.Threshold; break;
                case ConditionKind.Vibration:
                    Active = ctx.Vibration > 0.35 || (Condition.Threshold2 > Condition.Threshold && ctx.Rpm >= Condition.Threshold && ctx.Rpm <= Condition.Threshold2);
                    break;
                case ConditionKind.Intermittent:
                    _toggleTimer -= ctx.Dt;
                    if (_toggleTimer <= 0)
                    {
                        double duty = Condition.Threshold <= 0 ? 0.3 : Condition.Threshold;
                        double period = Condition.Threshold2 <= 0 ? 8 : Condition.Threshold2;
                        Active = rng.Chance(duty);
                        _toggleTimer = period * rng.Range(0.3, 1.7) * (Active ? duty : 1 - duty) * 2;
                    }

                    break;
            }
        }

        /// <summary>Forces the active state (tests, scripted conditions).</summary>
        public void ForceActive(bool active) => Active = active;

        /// <inheritdoc />
        public override string ToString() => $"{Mode.Id}@{ComponentId}{(string.IsNullOrEmpty(Pin) ? "" : ":" + Pin)} sev={Severity:0.00} ({Condition.Describe()})";
    }

    /// <summary>All faults of a car with fast queries by component and effect.</summary>
    public sealed class FaultSet
    {
        private readonly List<FaultInstance> _faults = new List<FaultInstance>();

        /// <summary>All faults including repaired ones.</summary>
        public IReadOnlyList<FaultInstance> All => _faults;

        /// <summary>Adds a fault.</summary>
        public void Add(FaultInstance fault) => _faults.Add(fault);

        /// <summary>Removes (repairs) every fault on a component. Returns how many were repaired.</summary>
        public int RepairComponent(string componentId)
        {
            int n = 0;
            foreach (FaultInstance f in _faults)
            {
                if (!f.Repaired && f.ComponentId == componentId)
                {
                    f.Repaired = true;
                    f.ForceActive(false);
                    n++;
                }
            }

            return n;
        }

        /// <summary>Repairs wiring faults on a component pin (or all pins when pin is null).</summary>
        public int RepairWiring(string componentId, string? pin)
        {
            int n = 0;
            foreach (FaultInstance f in _faults)
            {
                bool wiring = f.Effect == EffectKind.WireOpen || f.Effect == EffectKind.WireShortGround || f.Effect == EffectKind.WireShortPower
                    || f.Effect == EffectKind.WireHighResistance || f.Effect == EffectKind.ConnectorCorrosion;
                if (!f.Repaired && wiring && f.ComponentId == componentId && (pin == null || f.Pin == pin))
                {
                    f.Repaired = true;
                    f.ForceActive(false);
                    n++;
                }
            }

            return n;
        }

        /// <summary>Unrepaired faults.</summary>
        public IEnumerable<FaultInstance> Unrepaired()
        {
            foreach (FaultInstance f in _faults)
            {
                if (!f.Repaired)
                {
                    yield return f;
                }
            }
        }

        /// <summary>Re-evaluates all conditions.</summary>
        public void Evaluate(in ConditionContext ctx, DeterministicRandom rng)
        {
            foreach (FaultInstance f in _faults)
            {
                f.Evaluate(ctx, rng);
            }
        }

        /// <summary>First active fault of the given effect on a component (optionally a pin).</summary>
        public FaultInstance? Find(string componentId, EffectKind effect, string? pin = null)
        {
            foreach (FaultInstance f in _faults)
            {
                if (f.Active && f.Effect == effect && f.ComponentId == componentId && (pin == null || f.Pin == pin))
                {
                    return f;
                }
            }

            return null;
        }

        /// <summary>True if an active fault of the effect exists.</summary>
        public bool Has(string componentId, EffectKind effect, string? pin = null) => Find(componentId, effect, pin) != null;

        /// <summary>Sum of magnitudes of active faults with the effect.</summary>
        public double Sum(string componentId, EffectKind effect)
        {
            double s = 0;
            foreach (FaultInstance f in _faults)
            {
                if (f.Active && f.Effect == effect && f.ComponentId == componentId)
                {
                    s += f.Magnitude;
                }
            }

            return s;
        }

        /// <summary>Max magnitude of active faults with the effect (0 if none).</summary>
        public double Max(string componentId, EffectKind effect)
        {
            double m = 0;
            foreach (FaultInstance f in _faults)
            {
                if (f.Active && f.Effect == effect && f.ComponentId == componentId && f.Magnitude > m)
                {
                    m = f.Magnitude;
                }
            }

            return m;
        }

        /// <summary>Active faults of a component.</summary>
        public IEnumerable<FaultInstance> ActiveOn(string componentId)
        {
            foreach (FaultInstance f in _faults)
            {
                if (f.Active && f.ComponentId == componentId)
                {
                    yield return f;
                }
            }
        }
    }
}
