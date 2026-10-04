using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Garage.Data.Json;
using Garage.Sim.Components;
using Garage.Sim.Ecu;
using Garage.Sim.Electrical;
using Garage.Sim.Faults;
using Garage.Sim.Maps;
using Garage.Sim.Vehicle;

namespace Garage.Data
{
    /// <summary>Thrown when content cannot be mapped to simulation objects.</summary>
    public sealed class ContentException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public ContentException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Typed access to loaded content; maps JSON entities to simulation definitions.</summary>
    public sealed partial class ContentDatabase
    {
        private readonly ContentLoader _loader;

        private ContentDatabase(ContentLoader loader, LoadReport report)
        {
            _loader = loader;
            Report = report;
            Catalog = new DtcCatalog();
            foreach (JsonValue d in Raw("dtc").Values)
            {
                Catalog.Add(MapDtc(d));
            }

            FailureModes = FailureModeLibrary.Default.Clone();
            foreach (JsonValue m in Raw("failure_modes").Values)
            {
                FailureModes.Register(MapFailureMode(m));
            }
        }

        /// <summary>Load report.</summary>
        public LoadReport Report { get; }

        /// <summary>DTC catalog.</summary>
        public DtcCatalog Catalog { get; }

        /// <summary>Failure modes (built-in + data).</summary>
        public FailureModeLibrary FailureModes { get; }

        /// <summary>Loads content from a data root (containing base/ and schemas/) and optional mods directory.</summary>
        public static ContentDatabase Load(string dataRoot, string? modsDir = null)
        {
            var loader = new ContentLoader(Path.Combine(dataRoot, "schemas"));
            var pre = new LoadReport();
            List<string> mods = modsDir == null ? new List<string>() : ContentLoader.DiscoverMods(modsDir, pre);
            LoadReport report = loader.Load(Path.Combine(dataRoot, "base"), mods);
            report.Errors.InsertRange(0, pre.Errors);
            report.Messages.InsertRange(0, pre.Messages);
            return new ContentDatabase(loader, report);
        }

        /// <summary>Finds the repository data directory walking up from a start directory.</summary>
        public static string? FindDataRoot(string startDir)
        {
            DirectoryInfo? d = new DirectoryInfo(startDir);
            while (d != null)
            {
                string candidate = Path.Combine(d.FullName, "data");
                if (Directory.Exists(Path.Combine(candidate, "base")) && Directory.Exists(Path.Combine(candidate, "schemas")))
                {
                    return candidate;
                }

                d = d.Parent;
            }

            return null;
        }

        /// <summary>Raw entities of a category.</summary>
        public IReadOnlyDictionary<string, JsonValue> Raw(string category) =>
            _loader.Entities.TryGetValue(category, out Dictionary<string, JsonValue> d) ? d : new Dictionary<string, JsonValue>();

        /// <summary>Ids of a category, sorted.</summary>
        public List<string> Ids(string category)
        {
            var l = new List<string>(Raw(category).Keys);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        private JsonValue Require(string category, string id)
        {
            if (!Raw(category).TryGetValue(id, out JsonValue v))
            {
                throw new ContentException($"No existe '{category}/{id}'.");
            }

            return v;
        }

        // ------------------------------------------------------------------ maps

        /// <summary>Maps an axis object {name, unit, values}.</summary>
        public static Axis MapAxis(JsonValue a) => new Axis(a.Str("name", "x"), a.Str("unit"), a["values"].ToDoubleArray());

        /// <summary>Maps a table.</summary>
        public static Map3D MapTable(JsonValue t) => new Map3D(t.Str("id"), t.Str("unit"), MapAxis(t["x"]), MapAxis(t["y"]), t["values"].ToMatrix());

        /// <summary>Maps a curve.</summary>
        public static Map2D MapCurve(JsonValue c) => new Map2D(c.Str("id"), c.Str("unit"), MapAxis(c["x"]), c["values"].ToDoubleArray());

        /// <summary>Serialises a table back to JSON (for saving tunes).</summary>
        public static JsonValue TableToJson(Map3D m)
        {
            JsonValue o = JsonValue.NewObject().Set("id", m.Id).Set("unit", m.Unit);
            o.Set("x", JsonValue.NewObject().Set("name", m.X.Name).Set("unit", m.X.Unit).Set("values", JsonValue.From(m.X.ToArray())));
            o.Set("y", JsonValue.NewObject().Set("name", m.Y.Name).Set("unit", m.Y.Unit).Set("values", JsonValue.From(m.Y.ToArray())));
            JsonValue rows = JsonValue.NewArray();
            double[,] cells = m.ToArray();
            for (int r = 0; r < m.Y.Count; r++)
            {
                var row = new double[m.X.Count];
                for (int c = 0; c < m.X.Count; c++)
                {
                    row[c] = Math.Round(cells[r, c], 4);
                }

                rows.Add(JsonValue.From(row));
            }

            o.Set("values", rows);
            return o;
        }

        /// <summary>Serialises a curve.</summary>
        public static JsonValue CurveToJson(Map2D m)
        {
            JsonValue o = JsonValue.NewObject().Set("id", m.Id).Set("unit", m.Unit);
            o.Set("x", JsonValue.NewObject().Set("name", m.X.Name).Set("unit", m.X.Unit).Set("values", JsonValue.From(m.X.ToArray())));
            o.Set("values", JsonValue.From(m.ToArray()));
            return o;
        }

        /// <summary>Serialises a full calibration.</summary>
        public static JsonValue CalibrationToJson(EcuCalibration cal)
        {
            JsonValue o = JsonValue.NewObject().Set("id", cal.Id).Set("description", cal.Description);
            JsonValue tables = JsonValue.NewArray();
            foreach (Map3D t in cal.Tables.Values)
            {
                tables.Add(TableToJson(t));
            }

            JsonValue curves = JsonValue.NewArray();
            foreach (Map2D c in cal.Curves.Values)
            {
                curves.Add(CurveToJson(c));
            }

            JsonValue scalars = JsonValue.NewObject();
            foreach (KeyValuePair<string, double> s in cal.Scalars)
            {
                scalars.Set(s.Key, s.Value);
            }

            o.Set("tables", tables).Set("curves", curves).Set("scalars", scalars);
            return o;
        }

        /// <summary>Maps a calibration JSON object.</summary>
        public static EcuCalibration MapCalibration(JsonValue c)
        {
            var cal = new EcuCalibration { Id = c.Str("id"), Description = c.Str("description") };
            foreach (JsonValue t in c["tables"].Items)
            {
                cal.SetTable(MapTable(t));
            }

            foreach (JsonValue t in c["curves"].Items)
            {
                cal.SetCurve(MapCurve(t));
            }

            foreach (KeyValuePair<string, JsonValue> s in c["scalars"].Members)
            {
                cal.SetScalar(s.Key, s.Value.NumberValue);
            }

            return cal;
        }

        /// <summary>Calibration by id.</summary>
        public EcuCalibration Calibration(string id) => MapCalibration(Require("calibrations", id));

        // ------------------------------------------------------------------ engines

        /// <summary>Engine definition by id.</summary>
        public EngineDefinition Engine(string id)
        {
            JsonValue e = Require("engines", id);
            if (!Enum.TryParse(e.Str("kind", "GasolineTurbo"), true, out EngineKind kind))
            {
                throw new ContentException($"Tipo de motor desconocido en {id}");
            }

            var def = new EngineDefinition
            {
                Id = id,
                Name = e.Str("name", id),
                Kind = kind,
                Cylinders = e.Int("cylinders", 4),
                DisplacementL = e.Num("displacementL", 2.0),
                BoreMm = e.Num("boreMm", 82),
                StrokeMm = e.Num("strokeMm", 92),
                CompressionRatio = e.Num("compressionRatio", 9.6),
                RedlineRpm = e.Num("redlineRpm", 6500),
                MechanicalLimitRpm = e.Num("mechanicalLimitRpm", 7000),
                ThrottleDiameterMm = e.Num("throttleDiameterMm", 60),
                InertiaKgM2 = e.Num("inertiaKgM2", 0.16),
                KnockMarginDeg = e.Num("knockMarginDeg", 6),
                FuelRon = e.Num("fuelRon", 95),
                InjectorFlowCcMin = e.Num("injectorFlowCcMin", 330),
                InjectorRefPressureKpa = e.Num("injectorRefPressureKpa", 400),
                RailPressureKpa = e.Num("railPressureKpa", 400),
                PumpMaxPressureKpa = e.Num("pumpMaxPressureKpa", 650),
                ThermalCapacityKjK = e.Num("thermalCapacityKjK", 45),
                ThermostatOpenC = e.Num("thermostatOpenC", 88),
                RadiatorKwK = e.Num("radiatorKwK", 0.55),
                FrictionFactor = e.Num("frictionFactor", 1.0),
                IndicatedEfficiency = e.Num("indicatedEfficiency", 0.385),
                DirectInjection = e.Bool("directInjection"),
            };
            if (e["firingOrder"].IsArray)
            {
                double[] fo = e["firingOrder"].ToDoubleArray();
                def.FiringOrder = Array.ConvertAll(fo, x => (int)x);
            }

            if (e["ve"].IsObject)
            {
                def.VolumetricEfficiency = MapTable(e["ve"]);
            }

            if (e["mbt"].IsObject)
            {
                def.MbtAdvance = MapTable(e["mbt"]);
            }

            JsonValue t = e["turbo"];
            if (t.IsObject)
            {
                def.Turbo = new TurboDefinition
                {
                    MaxBoostKpa = t.Num("maxBoostKpa", 150),
                    FullSpoolRpm = t.Num("fullSpoolRpm", 2200),
                    SpoolStartRpm = t.Num("spoolStartRpm", 1300),
                    WastegateSpringKpa = t.Num("wastegateSpringKpa", 45),
                    MaxSafeBoostKpa = t.Num("maxSafeBoostKpa", 170),
                    CompressorEfficiency = t.Num("compressorEfficiency", 0.72),
                    IntercoolerEffectiveness = t.Num("intercoolerEffectiveness", 0.7),
                    SpoolTimeConstant = t.Num("spoolTimeConstant", 0.45),
                    MaxFlowGps = t.Num("maxFlowGps", 200),
                };
            }

            return def;
        }

        // ------------------------------------------------------------------ components / cars

        private static string Expand(string s, int n) => s.Replace("{n}", n.ToString(CultureInfo.InvariantCulture));

        /// <summary>Maps a component (expanding {n} for per-cylinder entries).</summary>
        public static List<ComponentDefinition> MapComponent(JsonValue c, int cylinders)
        {
            var list = new List<ComponentDefinition>();
            bool per = c.Bool("perCylinder");
            int count = per ? cylinders : 1;
            if (!ComponentKinds.TryParse(c.Str("kind"), out ComponentKind kind))
            {
                throw new ContentException($"Tipo de componente desconocido '{c.Str("kind")}' en '{c.Str("id")}'.");
            }

            for (int i = 0; i < count; i++)
            {
                int n = i + 1;
                var d = new ComponentDefinition
                {
                    Id = per ? Expand(c.Str("id"), n) : c.Str("id"),
                    Kind = kind,
                    Name = per ? Expand(c.Str("name"), n) : c.Str("name"),
                    Cylinder = per ? i : c.Int("cylinder", -1),
                    VisualSlot = per ? Expand(c.Str("visualSlot", c.Str("id")), n) : c.Str("visualSlot", c.Str("id")),
                    Location = c.Str("location"),
                    PartId = c.Str("partId"),
                    Fuse = c.Str("fuse"),
                    ReplaceMinutes = c.Num("replaceMinutes", 30),
                };
                foreach (KeyValuePair<string, JsonValue> p in c["params"].Members)
                {
                    d.Parameters[p.Key] = p.Value.NumberValue;
                }

                JsonValue circ = c["circuit"];
                if (circ.IsObject)
                {
                    if (!Enum.TryParse(circ.Str("topology"), true, out CircuitTopology topo))
                    {
                        throw new ContentException($"Topología desconocida '{circ.Str("topology")}' en '{d.Id}'.");
                    }

                    var cd = new CircuitDefinition { Topology = topo, Fuse = circ.Str("fuse") };
                    int pinNo = 1;
                    foreach (JsonValue p in circ["pins"].Items)
                    {
                        string ecuPin;
                        if (p["ecuPin"].IsArray)
                        {
                            ecuPin = p["ecuPin"].Count > i ? p["ecuPin"][i].StringValue : p["ecuPin"][0].StringValue;
                        }
                        else
                        {
                            ecuPin = Expand(p.Str("ecuPin", "?"), n);
                        }

                        cd.Pins.Add(new PinInfo(p.Str("role"), ecuPin, p.Int("pin", pinNo), p.Str("color", "NE")));
                        pinNo++;
                    }

                    d.Circuit = cd;
                }

                list.Add(d);
            }

            return list;
        }

        /// <summary>Builds a full car definition (engine + template + overrides).</summary>
        public CarDefinition Car(string id)
        {
            JsonValue c = Require("cars", id);
            EngineDefinition engine = Engine(c.Str("engine"));
            if (c.Has("fuelRon"))
            {
                engine.FuelRon = c.Num("fuelRon");
            }

            var def = new CarDefinition
            {
                Id = id,
                Brand = c.Str("brand"),
                Model = c.Str("model"),
                Year = c.Int("year", 2015),
                Segment = c.Str("segment"),
                Engine = engine,
                CalibrationId = c.Str("calibration"),
                MassKg = c.Num("massKg", 1350),
                WheelRadiusM = c.Num("wheelRadiusM", 0.315),
                FinalDrive = c.Num("finalDrive", 3.65),
                DrivetrainEfficiency = c.Num("drivetrainEfficiency", 0.88),
                DragAreaM2 = c.Num("dragAreaM2", 0.68),
                Experimental = c.Bool("experimental"),
                Vin = c.Str("vin", "VF0GARAGE00000001"),
            };
            if (c["gearRatios"].IsArray)
            {
                def.GearRatios = c["gearRatios"].ToDoubleArray();
            }

            if (c["modules"].IsArray)
            {
                def.Modules = c["modules"].ToStringList();
            }

            JsonValue a = c["appearance"];
            if (a.IsObject)
            {
                def.Appearance = new AppearanceDefinition
                {
                    AgeYears = a.Num("ageYears", 5),
                    Kilometers = a.Num("kilometers", 80000),
                    Maintenance = a.Num("maintenance", 0.7),
                    PaintColor = a.Str("paintColor", "#8A8D91"),
                    Humidity = a.Num("humidity", 0.3),
                };
            }

            var components = new List<ComponentDefinition>();
            string template = c.Str("template");
            if (template.Length > 0)
            {
                foreach (JsonValue comp in Require("templates", template)["components"].Items)
                {
                    components.AddRange(MapComponent(comp, engine.Cylinders));
                }
            }

            foreach (JsonValue r in c["removeComponents"].Items)
            {
                components.RemoveAll(x => string.Equals(x.Id, r.StringValue, StringComparison.OrdinalIgnoreCase));
            }

            foreach (JsonValue add in c["addComponents"].Items)
            {
                foreach (ComponentDefinition nc in MapComponent(add, engine.Cylinders))
                {
                    components.RemoveAll(x => string.Equals(x.Id, nc.Id, StringComparison.OrdinalIgnoreCase));
                    components.Add(nc);
                }
            }

            foreach (JsonValue ov in c["componentOverrides"].Items)
            {
                ComponentDefinition? target = components.Find(x => string.Equals(x.Id, ov.Str("id"), StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    throw new ContentException($"Override de componente inexistente '{ov.Str("id")}' en coche '{id}'.");
                }

                foreach (KeyValuePair<string, JsonValue> p in ov["params"].Members)
                {
                    target.Parameters[p.Key] = p.Value.NumberValue;
                }

                if (ov.Has("name"))
                {
                    target.Name = ov.Str("name");
                }
            }

            def.Components = components;
            return def;
        }

        /// <summary>Creates a ready car (definition + stock calibration + faults).</summary>
        public Car CreateCar(string carId, ulong seed, IEnumerable<FaultInstance>? faults = null, bool warm = false, double ambientC = 20)
        {
            CarDefinition def = Car(carId);
            EcuCalibration cal = Calibration(def.CalibrationId);
            return CarFactory.Create(def, cal, Catalog, seed, faults, warm, ambientC);
        }

        // ------------------------------------------------------------------ dtc / modes

        /// <summary>Maps a DTC.</summary>
        public static DtcDefinition MapDtc(JsonValue d) => new DtcDefinition
        {
            Code = d.Str("code"),
            Description = d.Str("description"),
            DescriptionEs = d.Str("descriptionEs"),
            System = d.Str("system"),
            Trips = d.Int("trips", 2),
            Mil = d.Bool("mil", true),
            Causes = d["causes"].ToStringList(),
        };

        /// <summary>Maps a failure mode.</summary>
        public static FailureModeDefinition MapFailureMode(JsonValue m)
        {
            if (!Enum.TryParse(m.Str("effect"), true, out EffectKind effect))
            {
                throw new ContentException($"Efecto desconocido '{m.Str("effect")}' en modo '{m.Str("id")}'.");
            }

            var conds = new List<ConditionKind>();
            foreach (JsonValue c in m["conditions"].Items)
            {
                if (Enum.TryParse(c.StringValue, true, out ConditionKind ck))
                {
                    conds.Add(ck);
                }
            }

            return new FailureModeDefinition
            {
                Id = m.Str("id"),
                Name = m.Str("name"),
                Family = m.Str("family"),
                Effect = effect,
                AppliesTo = m["appliesTo"].ToStringList(),
                Pin = m.Str("pin"),
                MagnitudeMin = m.Num("magnitudeMin"),
                MagnitudeMax = m.Num("magnitudeMax", 1),
                Difficulty = m.Int("difficulty", 1),
                Repair = m.Str("repair", "replace"),
                Description = m.Str("description"),
                Conditions = conds,
            };
        }

        /// <summary>Serialises a failure mode (used to export the built-in library to data).</summary>
        public static JsonValue FailureModeToJson(FailureModeDefinition m)
        {
            JsonValue o = JsonValue.NewObject()
                .Set("id", m.Id).Set("name", m.Name).Set("family", m.Family).Set("effect", m.Effect.ToString())
                .Set("appliesTo", JsonValue.From(m.AppliesTo));
            if (m.Pin.Length > 0)
            {
                o.Set("pin", m.Pin);
            }

            o.Set("magnitudeMin", m.MagnitudeMin).Set("magnitudeMax", m.MagnitudeMax).Set("difficulty", m.Difficulty)
                .Set("repair", m.Repair).Set("description", m.Description);
            var conds = new List<string>();
            foreach (ConditionKind c in m.Conditions)
            {
                conds.Add(c.ToString());
            }

            o.Set("conditions", JsonValue.From(conds));
            return o;
        }

        /// <summary>Maps a fault description {mode, component, severity, pin, condition}.</summary>
        public FaultInstance MapFault(JsonValue f)
        {
            FailureModeDefinition mode = FailureModes.Get(f.Str("mode"));
            FaultCondition cond = FaultCondition.AlwaysOn;
            JsonValue c = f["condition"];
            if (c.IsObject)
            {
                if (!Enum.TryParse(c.Str("kind", "Always"), true, out ConditionKind ck))
                {
                    throw new ContentException($"Condición desconocida '{c.Str("kind")}'.");
                }

                cond = new FaultCondition(ck, c.Num("threshold"), c.Num("threshold2"));
            }

            string? pin = f.Has("pin") ? f.Str("pin") : null;
            return new FaultInstance(mode, f.Str("component"), f.Num("severity", 0.6), cond, pin);
        }
    }
}
