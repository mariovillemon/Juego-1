using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Garage.Data.Json;
using Garage.Sim.Components;
using Garage.Sim.Ecu;
using Garage.Sim.Engine;
using Garage.Sim.Faults;
using Garage.Sim.Game;
using Garage.Sim.Vehicle;

namespace Garage.Data
{
    /// <summary>
    /// Save/load of a whole game to versioned JSON. Cars are rebuilt from their definition and seed and then the
    /// saved state (faults incl. repaired ones, part health, damage, calibration, trims, DTC memory) is re-applied.
    /// </summary>
    public static class SaveGame
    {
        /// <summary>Current format version.</summary>
        public const int FormatVersion = 1;

        /// <summary>Serialises the workshop.</summary>
        public static JsonValue ToJson(Workshop w)
        {
            JsonValue root = JsonValue.NewObject();
            root.Set("formatVersion", FormatVersion);
            root.Set("savedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            root.Set("seed", w.Seed.ToString(CultureInfo.InvariantCulture));
            ulong rngSeed = w.Seed ^ (ulong)(w.Day * 7919 + w.Jobs.Count * 104729 + w.GeneratedCount);
            w.ReseedRandom(rngSeed);
            root.Set("rngSeed", rngSeed.ToString(CultureInfo.InvariantCulture));
            JsonValue ws = JsonValue.NewObject()
                .Set("money", w.Money).Set("reputation", w.Reputation).Set("day", w.Day).Set("minute", w.Minute)
                .Set("labourRate", w.LabourRate).Set("partsMarkup", w.PartsMarkup).Set("generatedCount", w.GeneratedCount)
                .Set("owned", JsonValue.From(w.Owned)).Set("usedJobs", JsonValue.From(w.UsedJobs));
            JsonValue cb = JsonValue.NewArray();
            foreach (Comeback c in w.Comebacks)
            {
                cb.Add(JsonValue.NewObject().Set("jobId", c.JobId).Set("day", c.Day).Set("reason", c.Reason).Set("refund", c.Refund));
            }

            ws.Set("comebacks", cb);
            root.Set("workshop", ws);
            JsonValue jobs = JsonValue.NewArray();
            foreach (Job j in w.Jobs)
            {
                if (j.Status == JobStatus.Cancelled)
                {
                    continue;
                }

                jobs.Add(JobToJson(j));
            }

            root.Set("jobs", jobs);
            return root;
        }

        private static JsonValue FaultToJson(FaultInstance f)
        {
            JsonValue o = JsonValue.NewObject().Set("mode", f.Mode.Id).Set("component", f.ComponentId).Set("severity", f.Severity).Set("pin", f.Pin)
                .Set("repaired", f.Repaired).Set("origin", f.Origin);
            o.Set("condition", JsonValue.NewObject().Set("kind", f.Condition.Kind.ToString()).Set("threshold", f.Condition.Threshold).Set("threshold2", f.Condition.Threshold2));
            return o;
        }

        private static JsonValue JobToJson(Job j)
        {
            Car car = j.Car;
            JsonValue o = JsonValue.NewObject();
            o.Set("definition", ContentDatabase.JobToJson(j.Definition));
            o.Set("seed", j.Seed.ToString(CultureInfo.InvariantCulture));
            o.Set("status", j.Status.ToString()).Set("acceptedDay", j.AcceptedDay).Set("quoted", j.QuotedAmount)
                .Set("labourMinutes", j.LabourMinutes).Set("baselinePs", j.BaselinePowerPs);
            JsonValue lines = JsonValue.NewArray();
            foreach (InvoiceLine l in j.Lines)
            {
                lines.Add(JsonValue.NewObject().Set("description", l.Description).Set("amount", l.Amount).Set("component", l.ComponentId).Set("part", l.PartId));
            }

            o.Set("lines", lines);
            JsonValue original = JsonValue.NewArray();
            foreach (FaultInstance f in j.OriginalFaults)
            {
                original.Add(FaultToJson(f));
            }

            o.Set("originalFaults", original);
            JsonValue faults = JsonValue.NewArray();
            foreach (FaultInstance f in car.Faults.All)
            {
                faults.Add(FaultToJson(f));
            }

            JsonValue c = JsonValue.NewObject();
            c.Set("faults", faults);
            JsonValue comps = JsonValue.NewObject();
            foreach (Component comp in car.Parts.All)
            {
                if (comp.Health < 0.999 || comp.Reliability < 0.999 || comp.PartId.Length > 0)
                {
                    JsonValue cs = JsonValue.NewObject().Set("health", comp.Health).Set("reliability", comp.Reliability).Set("partId", comp.PartId);
                    JsonValue prm = JsonValue.NewObject();
                    foreach (KeyValuePair<string, double> p in comp.Parameters)
                    {
                        prm.Set(p.Key, p.Value);
                    }

                    cs.Set("params", prm);
                    comps.Set(comp.Id, cs);
                }
            }

            c.Set("components", comps);
            EngineDamage d = car.Engine.Damage;
            c.Set("damage", JsonValue.NewObject().Set("piston", JsonValue.From(d.Piston)).Set("exhaustValve", JsonValue.From(d.ExhaustValve))
                .Set("rodBearing", d.RodBearing).Set("headGasket", d.HeadGasket).Set("turbo", d.Turbo).Set("catalyst", d.Catalyst).Set("plugFouling", d.PlugFouling));
            c.Set("calibration", ContentDatabase.CalibrationToJson(car.Ecu.Calibration));
            c.Set("trims", JsonValue.NewObject().Set("stft", car.Ecu.Stft).Set("ltftIdle", car.Ecu.LtftIdle).Set("ltftCruise", car.Ecu.LtftCruise));
            c.Set("dtc", JsonValue.NewObject().Set("confirmed", JsonValue.From(car.Ecu.Dtcs.ConfirmedCodes())).Set("pending", JsonValue.From(car.Ecu.Dtcs.PendingCodes())));
            c.Set("odometerKm", car.OdometerKm).Set("stateOfCharge", car.Electrical.StateOfCharge).Set("coolantLevel", car.Engine.State.CoolantLevel)
                .Set("coolantC", car.Engine.State.CoolantC);
            o.Set("car", c);
            return o;
        }

        /// <summary>Writes the save to a file.</summary>
        public static void Save(Workshop w, string path)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, ToJson(w).ToJson());
        }

        /// <summary>Loads a save from a file.</summary>
        public static Workshop Load(ContentDatabase db, string path) => FromJson(db, JsonValue.Parse(File.ReadAllText(path)));

        private static FaultInstance FaultFromJson(ContentDatabase db, JsonValue f)
        {
            JsonValue c = f["condition"];
            ConditionKind kind = Enum.TryParse(c.Str("kind", "Always"), out ConditionKind k) ? k : ConditionKind.Always;
            var fi = new FaultInstance(db.FailureModes.Get(f.Str("mode")), f.Str("component"), f.Num("severity"), new FaultCondition(kind, c.Num("threshold"), c.Num("threshold2")), f.Str("pin"))
            {
                Repaired = f.Bool("repaired"),
                Origin = f.Str("origin"),
            };
            if (fi.Repaired)
            {
                fi.ForceActive(false);
            }

            return fi;
        }

        /// <summary>Rebuilds a workshop from JSON.</summary>
        public static Workshop FromJson(ContentDatabase db, JsonValue root)
        {
            int version = root.Int("formatVersion");
            if (version < 1 || version > FormatVersion)
            {
                throw new ContentException($"Versión de partida no soportada: {version} (esta versión lee hasta {FormatVersion}).");
            }

            ulong seed = ulong.Parse(root.Str("seed", "1"), CultureInfo.InvariantCulture);
            var w = new Workshop(db, seed);
            JsonValue ws = root["workshop"];
            w.Money = ws.Num("money");
            w.Reputation = ws.Num("reputation");
            w.Day = ws.Int("day", 1);
            w.Minute = ws.Num("minute", Workshop.OpenMinute);
            w.LabourRate = ws.Num("labourRate", 55);
            w.PartsMarkup = ws.Num("partsMarkup", 0.25);
            w.GeneratedCount = ws.Int("generatedCount");
            foreach (JsonValue o in ws["owned"].Items)
            {
                w.Grant(o.StringValue);
            }

            foreach (JsonValue o in ws["usedJobs"].Items)
            {
                w.MarkJobUsed(o.StringValue);
            }

            foreach (JsonValue c in ws["comebacks"].Items)
            {
                w.AddComeback(new Comeback { JobId = c.Str("jobId"), Day = c.Int("day"), Reason = c.Str("reason"), Refund = c.Num("refund") });
            }

            w.ReseedRandom(ulong.Parse(root.Str("rngSeed", "1"), CultureInfo.InvariantCulture));
            foreach (JsonValue jj in root["jobs"].Items)
            {
                JobDefinition def = ContentDatabase.MapJob(jj["definition"]);
                ulong jobSeed = ulong.Parse(jj.Str("seed"), CultureInfo.InvariantCulture);
                JsonValue c = jj["car"];
                var faults = new List<FaultInstance>();
                foreach (JsonValue f in c["faults"].Items)
                {
                    faults.Add(FaultFromJson(db, f));
                }

                Car car = db.CreateCar(def.CarId, jobSeed, faults);
                CustomerProfile cust = null!;
                foreach (CustomerProfile cp in db.Customers)
                {
                    if (cp.Id == def.CustomerId)
                    {
                        cust = cp;
                    }
                }

                var job = new Job(def, cust ?? db.Customers[0], car, jobSeed)
                {
                    Status = Enum.TryParse(jj.Str("status"), out JobStatus st) ? st : JobStatus.Offered,
                    AcceptedDay = jj.Int("acceptedDay"),
                    QuotedAmount = jj.Num("quoted"),
                    LabourMinutes = jj.Num("labourMinutes"),
                    BaselinePowerPs = jj.Num("baselinePs"),
                };
                foreach (JsonValue l in jj["lines"].Items)
                {
                    job.Lines.Add(new InvoiceLine(l.Str("description"), l.Num("amount"), l.Str("component"), l.Str("part")));
                }

                foreach (JsonValue f in jj["originalFaults"].Items)
                {
                    job.OriginalFaults.Add(FaultFromJson(db, f));
                }

                foreach (KeyValuePair<string, JsonValue> kv in c["components"].Members)
                {
                    Component? comp = car.Parts.Get(kv.Key);
                    if (comp == null)
                    {
                        continue;
                    }

                    comp.Health = kv.Value.Num("health", 1);
                    comp.Reliability = kv.Value.Num("reliability", 1);
                    comp.PartId = kv.Value.Str("partId");
                    foreach (KeyValuePair<string, JsonValue> p in kv.Value["params"].Members)
                    {
                        comp.SetParam(p.Key, p.Value.NumberValue);
                    }
                }

                JsonValue d = c["damage"];
                var dmg = new EngineDamage(car.Definition.Engine.Cylinders)
                {
                    RodBearing = d.Num("rodBearing"),
                    HeadGasket = d.Num("headGasket"),
                    Turbo = d.Num("turbo"),
                    Catalyst = d.Num("catalyst"),
                    PlugFouling = d.Num("plugFouling"),
                };
                double[] piston = d["piston"].ToDoubleArray();
                double[] valve = d["exhaustValve"].ToDoubleArray();
                Array.Copy(piston, dmg.Piston, Math.Min(piston.Length, dmg.Piston.Length));
                Array.Copy(valve, dmg.ExhaustValve, Math.Min(valve.Length, dmg.ExhaustValve.Length));
                car.Engine.SetDamage(dmg);
                if (c["calibration"].IsObject)
                {
                    car.Ecu.Calibration = ContentDatabase.MapCalibration(c["calibration"]);
                }

                JsonValue t = c["trims"];
                car.Ecu.SetTrims(t.Num("stft"), t.Num("ltftIdle"), t.Num("ltftCruise"));
                foreach (JsonValue code in c["dtc"]["pending"].Items)
                {
                    car.Ecu.Dtcs.Fail(code.StringValue, null);
                }

                foreach (JsonValue code in c["dtc"]["confirmed"].Items)
                {
                    car.Ecu.Dtcs.Fail(code.StringValue, null);
                    car.Ecu.Dtcs.ConfirmNow(code.StringValue);
                }

                car.Ecu.Dtcs.EndTrip(false);
                foreach (JsonValue code in c["dtc"]["confirmed"].Items)
                {
                    car.Ecu.Dtcs.ConfirmNow(code.StringValue);
                }

                car.OdometerKm = c.Num("odometerKm", car.OdometerKm);
                car.Electrical.StateOfCharge = c.Num("stateOfCharge", 0.9);
                car.Engine.State.CoolantLevel = c.Num("coolantLevel", 1);
                car.Engine.State.CoolantC = c.Num("coolantC", car.Environment.AmbientC);
                car.InvalidateCircuits();
                car.RefreshCircuits();
                AddJob(w, job);
            }

            return w;
        }

        private static void AddJob(Workshop w, Job job)
        {
            // Workshop keeps its job list private; use reflection-free path via a dedicated API.
            w.RestoreJob(job);
        }
    }
}
