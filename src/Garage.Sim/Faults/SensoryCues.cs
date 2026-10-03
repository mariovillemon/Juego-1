using System;
using System.Collections.Generic;
using Garage.Sim.Components;
using Garage.Sim.Engine;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Faults
{
    /// <summary>Channel of a sensory cue.</summary>
    public enum CueChannel
    {
        /// <summary>Audible.</summary>
        Sound,
        /// <summary>Visible smoke.</summary>
        Smoke,
        /// <summary>Smell.</summary>
        Smell,
        /// <summary>Felt vibration.</summary>
        Vibration,
        /// <summary>Dashboard warning light.</summary>
        Warning,
        /// <summary>Other visible sign (leak, steam).</summary>
        Visual,
    }

    /// <summary>A perceivable symptom derived from the simulation state (never authored per fault).</summary>
    public sealed class SensoryCue
    {
        /// <summary>Creates a cue.</summary>
        public SensoryCue(string id, CueChannel channel, double intensity, string description, string location = "")
        {
            Id = id;
            Channel = channel;
            Intensity = Math.Max(0, Math.Min(1, intensity));
            Description = description;
            Location = location;
        }

        /// <summary>Stable id used by Unity to pick audio/particles (e.g. "sound.vacuum_hiss").</summary>
        public string Id { get; }

        /// <summary>Channel.</summary>
        public CueChannel Channel { get; }

        /// <summary>Intensity 0..1.</summary>
        public double Intensity { get; }

        /// <summary>Spanish description.</summary>
        public string Description { get; }

        /// <summary>Visual slot or area where it comes from.</summary>
        public string Location { get; }

        /// <inheritdoc />
        public override string ToString() => $"[{Channel}] {Description} ({Intensity:P0})";
    }

    /// <summary>Derives sensory cues from the physical state of a car.</summary>
    public static class SensoryCueAnalyzer
    {
        /// <summary>Analyses the current state.</summary>
        public static IReadOnlyList<SensoryCue> Analyze(Car car)
        {
            var cues = new List<SensoryCue>();
            EngineState s = car.Engine.State;
            EngineDamage d = car.Engine.Damage;
            bool running = s.Running && s.Rpm > 300;

            if (car.Ecu.Dtcs.MilOn)
            {
                cues.Add(new SensoryCue("warning.mil", CueChannel.Warning, car.Ecu.Dtcs.MilFlashing ? 1 : 0.6, car.Ecu.Dtcs.MilFlashing ? "Testigo de avería motor parpadeando" : "Testigo de avería motor encendido", "dashboard"));
            }

            if (car.Ecu.LimpMode)
            {
                cues.Add(new SensoryCue("warning.limp", CueChannel.Warning, 0.8, "El motor no pasa de medio gas (modo emergencia)", "dashboard"));
            }

            if (car.KeyOn && car.BatteryVolts < 12.2 && running)
            {
                cues.Add(new SensoryCue("warning.battery", CueChannel.Warning, 0.7, "Testigo de batería encendido; luces débiles", "dashboard"));
            }

            if (car.KeyOn && s.CoolantC > 112)
            {
                cues.Add(new SensoryCue("warning.temperature", CueChannel.Warning, (s.CoolantC - 105) / 20, "Aguja de temperatura en zona roja", "dashboard"));
            }

            if (!running)
            {
                if (car.Cranking && s.Rpm > 50)
                {
                    cues.Add(new SensoryCue("sound.cranking", CueChannel.Sound, s.Rpm / 300, s.Rpm < 160 ? "El motor de arranque gira lento" : "El motor gira pero no arranca", "engine"));
                }

                if (s.Seized)
                {
                    cues.Add(new SensoryCue("sound.seized", CueChannel.Sound, 1, "Golpe metálico y el motor no gira: está gripado", "engine"));
                }

                return cues;
            }

            if (s.UnmeteredAirGps > 0.3)
            {
                double i = Math.Min(1, s.UnmeteredAirGps / 2.0) * Math.Min(1, (s.BaroKpaSafe() - s.ManifoldKpa) / 60);
                Component? hose = FirstFaulted(car, ComponentKind.VacuumHose) ?? FirstFaulted(car, ComponentKind.IntakeGasket);
                cues.Add(new SensoryCue("sound.vacuum_hiss", CueChannel.Sound, i, "Silbido de aire en el colector de admisión", hose?.VisualSlot ?? "intake"));
            }

            if (s.BoostLeakGps > 3)
            {
                cues.Add(new SensoryCue("sound.boost_leak", CueChannel.Sound, Math.Min(1, s.BoostLeakGps / 25), "Soplido fuerte al acelerar (fuga de presión)", "charge_pipe"));
            }

            if (s.TurboSpeed > 0.3)
            {
                cues.Add(new SensoryCue("sound.turbo_spool", CueChannel.Sound, s.TurboSpeed * (1 + d.Turbo), d.Turbo > 0.5 || car.Faults.Max(car.Parts.IdOf(ComponentKind.Turbocharger), EffectKind.Wear) > 0.4 ? "Silbido de turbo tipo sirena (rodamientos)" : "Silbido normal del turbo", "turbo"));
            }

            double misfire = 0;
            for (int i = 0; i < s.CylinderBurn.Length; i++)
            {
                misfire = Math.Max(misfire, 1 - s.CylinderBurn[i]);
            }

            if (misfire > 0.08)
            {
                cues.Add(new SensoryCue("vibration.misfire", CueChannel.Vibration, misfire, "El motor tiembla y \"petardea\" de forma irregular", "engine"));
                cues.Add(new SensoryCue("sound.misfire_exhaust", CueChannel.Sound, misfire, "Escape con explosiones irregulares", "exhaust"));
            }

            double knock = 0;
            foreach (double k in s.CylinderKnock)
            {
                knock = Math.Max(knock, k);
            }

            if (knock > 0.1)
            {
                cues.Add(new SensoryCue("sound.knock", CueChannel.Sound, knock, "Picado metálico (como canicas en una lata) al acelerar", "engine"));
            }

            if (d.RodBearing > 0.3)
            {
                cues.Add(new SensoryCue("sound.rod_knock", CueChannel.Sound, d.RodBearing, "Golpeteo grave rítmico que sube con las rpm (biela)", "engine_bottom"));
            }

            if (s.ExhaustLambda < 0.85 || s.SmokeOpacity > 0.2)
            {
                cues.Add(new SensoryCue("smoke.black", CueChannel.Smoke, Math.Max(s.SmokeOpacity, (0.9 - s.ExhaustLambda) * 3), "Humo negro por el escape", "tailpipe"));
                cues.Add(new SensoryCue("smell.fuel", CueChannel.Smell, (0.95 - s.ExhaustLambda) * 3, "Fuerte olor a gasolina sin quemar", "tailpipe"));
            }

            if (d.HeadGasketBlown || car.Faults.Has(car.Parts.IdOf(ComponentKind.HeadGasket), EffectKind.Leak))
            {
                cues.Add(new SensoryCue("smoke.white", CueChannel.Smoke, 0.8, "Humo blanco denso y olor dulzón (refrigerante quemado)", "tailpipe"));
                cues.Add(new SensoryCue("visual.coolant_bubbles", CueChannel.Visual, 0.7, "Burbujas en el vaso de expansión", "coolant_reservoir"));
            }

            if (d.Turbo > 0.6 || d.AnyPistonBroken)
            {
                cues.Add(new SensoryCue("smoke.blue", CueChannel.Smoke, 0.7, "Humo azulado (quema aceite)", "tailpipe"));
            }

            if (car.Faults.Max(car.Parts.IdOf(ComponentKind.Exhaust), EffectKind.Leak) > 0)
            {
                cues.Add(new SensoryCue("sound.exhaust_tick", CueChannel.Sound, 0.6, "Tic-tic rápido en frío cerca del colector de escape", "exhaust_manifold"));
            }

            if (s.CatalystC > 850)
            {
                cues.Add(new SensoryCue("smell.sulfur", CueChannel.Smell, (s.CatalystC - 800) / 300, "Olor a huevo podrido y calor bajo el coche (catalizador)", "catalyst"));
                cues.Add(new SensoryCue("visual.cat_glow", CueChannel.Visual, (s.CatalystC - 900) / 200, "Catalizador al rojo", "catalyst"));
            }

            if (car.Ecu.Outputs.FanRelay)
            {
                cues.Add(new SensoryCue("sound.fan", CueChannel.Sound, 0.6, "Ventilador del radiador funcionando", "radiator"));
            }

            if (s.CamOffsetDeg > 6)
            {
                cues.Add(new SensoryCue("sound.chain_rattle", CueChannel.Sound, Math.Min(1, s.CamOffsetDeg / 12), "Cascabeleo metálico de cadena al arrancar", "timing_cover"));
            }

            if (s.Roughness > 0.05 && s.Rpm < 1100)
            {
                cues.Add(new SensoryCue("vibration.rough_idle", CueChannel.Vibration, s.Roughness, "Ralentí inestable", "engine"));
            }

            return cues;
        }

        private static double BaroKpaSafe(this EngineState s) => 101.3;

        private static Component? FirstFaulted(Car car, ComponentKind kind)
        {
            foreach (Component c in car.Parts.OfKind(kind))
            {
                foreach (FaultInstance _ in car.Faults.ActiveOn(c.Id))
                {
                    return c;
                }
            }

            return null;
        }
    }
}
