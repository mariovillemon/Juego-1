using System;
using System.Collections.Generic;
using System.Text;
using Garage.Sim.Components;
using Garage.Sim.Faults;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Tools
{
    /// <summary>Fuel pressure test modes.</summary>
    public enum FuelPressureMode
    {
        /// <summary>Key on, engine off (pump prime).</summary>
        KeyOnPrime,
        /// <summary>Engine idling.</summary>
        Idle,
        /// <summary>Snap throttle / load.</summary>
        Load,
        /// <summary>Residual pressure 5 minutes after shutdown.</summary>
        Residual,
    }

    /// <summary>Fuel pressure gauge, compression, cylinder leakage, smoke machine and visual inspection.</summary>
    public sealed class MechanicalTests
    {
        private readonly DiagnosticSession _s;

        internal MechanicalTests(DiagnosticSession s)
        {
            _s = s;
        }

        private Car Car => _s.Car;

        /// <summary>Fuel pressure gauge on the rail test port.</summary>
        public ToolResult FuelPressure(FuelPressureMode mode)
        {
            Car car = Car;
            if (car.Definition.Engine.IsDiesel)
            {
                return _s.Charge(new ToolResult("Rail común diésel: no se mide con manómetro mecánico (>1500 bar). Use el escáner (presión de rail).", 2, false), "Manómetro: no aplicable a diésel");
            }

            double kpa;
            string note;
            switch (mode)
            {
                case FuelPressureMode.KeyOnPrime:
                    car.Key = KeyPosition.Off;
                    car.RunFor(2);
                    car.Key = KeyPosition.On;
                    car.RunFor(2.5);
                    kpa = car.Engine.State.FuelRailKpa;
                    note = "Contacto puesto, cebado de bomba";
                    break;
                case FuelPressureMode.Idle:
                    if (car.Engine.State.Rpm < 400 && !car.Start())
                    {
                        kpa = car.Engine.State.FuelRailKpa;
                        note = "El motor no arranca; lectura durante arranque";
                        break;
                    }

                    car.Pedal = 0;
                    car.RunFor(5);
                    kpa = car.Engine.State.FuelRailKpa;
                    note = "Ralentí";
                    break;
                case FuelPressureMode.Load:
                    if (car.Engine.State.Rpm < 400)
                    {
                        car.Start();
                    }

                    LoadMode prevMode = car.Mode;
                    int prevGear = car.Gear;
                    car.Gear = Math.Min(3, car.Definition.GearRatios.Length);
                    car.Mode = LoadMode.DynoHoldRpm;
                    double ratio = car.Definition.GearRatios[car.Gear - 1] * car.Definition.FinalDrive;
                    car.DynoHoldRpm = car.Definition.Engine.RedlineRpm * 0.75;
                    car.SetVehicleSpeed(car.DynoHoldRpm / 60.0 * 2 * Math.PI / ratio * car.Definition.WheelRadiusM * 3.6);
                    car.Pedal = 1;
                    car.RunFor(4);
                    kpa = car.Engine.State.FuelRailKpa;
                    car.Pedal = 0;
                    car.Mode = prevMode;
                    car.Gear = prevGear;
                    car.SetVehicleSpeed(0);
                    car.RunFor(2);
                    note = "Plena carga en banco (máximo caudal)";
                    break;
                default:
                    car.Key = KeyPosition.Off;
                    car.RunFor(300, 0.1);
                    kpa = car.Engine.State.FuelRailKpa;
                    note = "Presión residual 5 min tras parar";
                    break;
            }

            string s = $"Manómetro de combustible: {kpa / 100:0.00} bar ({kpa * 0.145038:0} psi) — {note}. Especificación: {car.Definition.Engine.RailPressureKpa / 100:0.0} bar regulada.";
            return _s.Charge(new ToolResult(s, TimeCosts.FuelPressure) { Value = kpa }, $"Manómetro combustible ({mode})");
        }

        /// <summary>Compression test of all cylinders (engine off, cranking). Wet test adds oil.</summary>
        public ToolResult Compression(bool wet = false)
        {
            Car car = Car;
            if (car.Engine.State.Rpm > 50)
            {
                car.Key = KeyPosition.Off;
                car.RunFor(1);
            }

            int n = car.Definition.Engine.Cylinders;
            var sb = new StringBuilder($"Compresión {(wet ? "en húmedo" : "en seco")} (arrastre a {Math.Min(280, 120 + car.BatteryVolts * 12):0} rpm):\n");
            double max = 0;
            var values = new double[n];
            for (int i = 0; i < n; i++)
            {
                double bar = car.Engine.CrankingCompressionBar(i, car.Environment.BaroKpa);
                Component? cyl = car.Parts.Find(ComponentKind.Cylinder, i);
                if (wet && cyl != null)
                {
                    // Oil seals worn rings temporarily; it does not help a leaking valve or gasket.
                    double ringWear = car.Faults.Max(cyl.Id, EffectKind.Wear) * 0.5 + (1 - cyl.Health) * 0.12;
                    bar += car.Engine.CrankingCompressionBar(i, car.Environment.BaroKpa) / Math.Max(0.1, car.Engine.State.CylinderCompression[i]) * ringWear * 0.8;
                }

                bar *= 1 + (car.Rng.NextDouble() - 0.5) * 0.03;
                values[i] = bar;
                max = Math.Max(max, bar);
            }

            for (int i = 0; i < n; i++)
            {
                double pct = values[i] / Math.Max(0.1, max) * 100;
                sb.AppendLine($"  Cilindro {i + 1}: {values[i],5:0.0} bar ({values[i] * 14.5038,4:0} psi)  {(pct < 85 ? "<-- más de un 15 % por debajo" : "")}");
            }

            return _s.Charge(new ToolResult(sb.ToString().TrimEnd(), TimeCosts.CompressionPerCylinder * n), wet ? "Compresímetro (húmedo)" : "Compresímetro");
        }

        /// <summary>Cylinder leakage test: percentage and where the air escapes.</summary>
        public ToolResult LeakDown(int cylinder)
        {
            Car car = Car;
            int i = cylinder - 1;
            if (i < 0 || i >= car.Definition.Engine.Cylinders)
            {
                return new ToolResult("Cilindro inexistente.", 0, false);
            }

            double health = car.Engine.State.CylinderCompression[i];
            double leak = Math.Max(0, (1 - health) * 100) + 4 + car.Rng.NextDouble() * 2;
            var where = new List<string>();
            Component? cyl = car.Parts.Find(ComponentKind.Cylinder, i);
            if (cyl != null && car.Faults.Max(cyl.Id, EffectKind.Leak) > 0)
            {
                where.Add("silbido por el escape (válvula de escape)");
            }

            if (cyl != null && (car.Faults.Max(cyl.Id, EffectKind.Wear) > 0.3 || cyl.Health < 0.6) || car.Engine.Damage.Piston[i] >= 1)
            {
                where.Add("aire por el tapón de llenado de aceite (segmentos/pistón)");
            }

            if (car.Engine.Damage.ExhaustValve[i] >= 1)
            {
                where.Add("silbido fuerte por el escape");
            }

            bool hg = car.Engine.Damage.HeadGasketBlown || car.Faults.Has(car.Parts.IdOf(ComponentKind.HeadGasket), EffectKind.Leak);
            if (hg && (i == 1 || i == 2))
            {
                where.Add("burbujas en el vaso de expansión (junta de culata)");
            }

            if (where.Count == 0)
            {
                where.Add(leak < 10 ? "fuga normal por segmentos" : "fuga difusa");
            }

            string s = $"Fuga del cilindro {cylinder}: {leak:0} %  — {string.Join("; ", where)}. (< 10 % bueno, 10–20 % aceptable, > 30 % malo)";
            return _s.Charge(new ToolResult(s, TimeCosts.LeakDownPerCylinder) { Value = leak }, $"Prueba de fugas cilindro {cylinder}");
        }

        /// <summary>Smoke machine test of the intake (vacuum + boost side) or the exhaust.</summary>
        public ToolResult Smoke(bool exhaust = false)
        {
            Car car = Car;
            var found = new List<string>();
            foreach (Component c in car.Parts.All)
            {
                bool relevant = exhaust ? c.Kind == ComponentKind.Exhaust
                    : c.Kind == ComponentKind.VacuumHose || c.Kind == ComponentKind.IntakeGasket || c.Kind == ComponentKind.BoostHose || c.Kind == ComponentKind.Intercooler;
                if (relevant && car.Faults.Sum(c.Id, EffectKind.Leak) > 0)
                {
                    found.Add($"sale humo en: {c.Name} ({c.Location})");
                }

                if (!exhaust && c.Kind == ComponentKind.PurgeValve && car.Faults.Has(c.Id, EffectKind.StuckOpen))
                {
                    found.Add("sale humo por el respiradero del cánister (válvula de purga abierta)");
                }
            }

            if (!exhaust && car.Engine.State.CamOffsetDeg > 0 && found.Count == 0)
            {
                // nothing: timing is not a leak
            }

            string s = found.Count == 0 ? "La máquina de humo no revela fugas." : string.Join("\n", found);
            return _s.Charge(new ToolResult((exhaust ? "Prueba de humo en escape:\n" : "Prueba de humo en admisión:\n") + s, TimeCosts.SmokeTest) { Value = found.Count }, exhaust ? "Máquina de humo (escape)" : "Máquina de humo (admisión)");
        }

        /// <summary>Visual/hands-on inspection of a component (some faults are visible, many are not).</summary>
        public ToolResult Inspect(string componentId)
        {
            Car car = Car;
            Component? c = car.Parts.Get(componentId);
            if (c == null)
            {
                return new ToolResult("Componente no encontrado.", 0, false);
            }

            var notes = new List<string>();
            foreach (FaultInstance f in car.Faults.Unrepaired())
            {
                if (!string.Equals(f.ComponentId, componentId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool visible = Core.StableHash.Of(f.Mode.Id + componentId) % 3 != 0 || f.Severity > 0.75;
                switch (f.Effect)
                {
                    case EffectKind.Leak when c.Kind == ComponentKind.VacuumHose || c.Kind == ComponentKind.BoostHose:
                        if (visible || f.Severity > 0.5)
                        {
                            notes.Add("el manguito tiene grietas al doblarlo");
                        }

                        break;
                    case EffectKind.ConnectorCorrosion:
                        notes.Add("óxido verde en los pines del conector");
                        break;
                    case EffectKind.WireOpen:
                    case EffectKind.WireShortGround:
                    case EffectKind.WireShortPower:
                        if (visible)
                        {
                            notes.Add("el mazo está rozado contra el soporte del motor");
                        }

                        break;
                    case EffectKind.Wear when c.Kind == ComponentKind.SparkPlug:
                        notes.Add("electrodo desgastado y distancia excesiva");
                        break;
                    case EffectKind.Weak when c.Kind == ComponentKind.SparkPlug:
                        notes.Add("aislador con depósitos negros (carbonilla)");
                        break;
                    case EffectKind.Clog when c.Kind == ComponentKind.AirFilter || c.Kind == ComponentKind.FuelFilter:
                        notes.Add("filtro muy sucio");
                        break;
                    case EffectKind.Restriction when c.Kind == ComponentKind.ElectronicThrottle:
                        notes.Add("mariposa con carbonilla en el borde");
                        break;
                    case EffectKind.Restriction when c.Kind == ComponentKind.AirFilter:
                        notes.Add("filtro de aire obstruido");
                        break;
                    case EffectKind.Leak when c.Kind == ComponentKind.HeadGasket:
                        notes.Add("nivel de refrigerante bajo y restos de aceite en el vaso");
                        break;
                    case EffectKind.Wear when c.Kind == ComponentKind.Turbocharger:
                        notes.Add("holgura radial en el eje del turbo y aceite en el compresor");
                        break;
                    case EffectKind.Leak when c.Kind == ComponentKind.Exhaust:
                        notes.Add("marcas de hollín junto a la junta del colector");
                        break;
                    case EffectKind.Dead when c.Kind == ComponentKind.Fuse:
                        notes.Add("el filamento del fusible está fundido");
                        break;
                    case EffectKind.Dribble:
                        notes.Add("restos de combustible en el asiento del inyector");
                        break;
                }
            }

            if (c.Kind == ComponentKind.SparkPlug && car.Engine.Damage.PlugFouling > 0.4)
            {
                notes.Add("depósitos de hollín (funcionamiento rico)");
            }

            if (notes.Count == 0)
            {
                notes.Add(c.Health < 0.6 ? "aspecto envejecido, sin daño evidente" : "aspecto normal");
            }

            return _s.Charge(new ToolResult($"{c.Name}: {string.Join("; ", notes)}.", TimeCosts.Inspection), $"Inspección visual {componentId}");
        }
    }
}
