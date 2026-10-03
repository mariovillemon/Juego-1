using System;
using System.Collections.Generic;

namespace Garage.Sim.Ecu
{
    /// <summary>Catalog entry for a diagnostic trouble code.</summary>
    public sealed class DtcDefinition
    {
        /// <summary>Code such as "P0171".</summary>
        public string Code { get; set; } = "";

        /// <summary>Standard description (English, SAE J2012 wording).</summary>
        public string Description { get; set; } = "";

        /// <summary>Spanish description.</summary>
        public string DescriptionEs { get; set; } = "";

        /// <summary>System group: fuel_air, ignition, emissions, speed_idle, ecu, transmission, network...</summary>
        public string System { get; set; } = "";

        /// <summary>Drive cycles needed to confirm (1 or 2).</summary>
        public int Trips { get; set; } = 2;

        /// <summary>Whether a confirmed code turns the MIL on.</summary>
        public bool Mil { get; set; } = true;

        /// <summary>Typical causes (Spanish), shown in training mode.</summary>
        public List<string> Causes { get; set; } = new List<string>();
    }

    /// <summary>DTC catalog, filled from data/base/dtc.json.</summary>
    public sealed class DtcCatalog
    {
        private readonly Dictionary<string, DtcDefinition> _codes = new Dictionary<string, DtcDefinition>(StringComparer.OrdinalIgnoreCase);

        /// <summary>All definitions.</summary>
        public IEnumerable<DtcDefinition> All => _codes.Values;

        /// <summary>Number of codes.</summary>
        public int Count => _codes.Count;

        /// <summary>Adds or replaces.</summary>
        public void Add(DtcDefinition d) => _codes[d.Code] = d;

        /// <summary>Gets a definition or a generic placeholder.</summary>
        public DtcDefinition Get(string code)
        {
            if (_codes.TryGetValue(code, out DtcDefinition d))
            {
                return d;
            }

            return new DtcDefinition { Code = code, Description = "Unknown / manufacturer specific", DescriptionEs = "Código no catalogado", Trips = 2 };
        }

        /// <summary>True if present.</summary>
        public bool Contains(string code) => _codes.ContainsKey(code);
    }

    /// <summary>Freeze frame: snapshot of key parameters when a DTC first set.</summary>
    public sealed class FreezeFrame
    {
        /// <summary>Code that triggered the frame.</summary>
        public string Code { get; set; } = "";

        /// <summary>Values keyed by PID name.</summary>
        public Dictionary<string, double> Values { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Simulation time.</summary>
        public double Time { get; set; }
    }

    /// <summary>Status of a stored code.</summary>
    public sealed class DtcRecord
    {
        /// <summary>Code.</summary>
        public string Code { get; set; } = "";

        /// <summary>Failed at least once in current or previous trip and not yet confirmed/cleared.</summary>
        public bool Pending { get; set; }

        /// <summary>Confirmed (stored) code.</summary>
        public bool Confirmed { get; set; }

        /// <summary>MIL commanded on by this code.</summary>
        public bool MilOn { get; set; }

        /// <summary>Permanent code (cannot be erased by a scan tool until the monitor passes).</summary>
        public bool Permanent { get; set; }

        /// <summary>Failed during the current trip.</summary>
        public bool FailedThisTrip { get; set; }

        /// <summary>Passed (test completed without failure) during the current trip.</summary>
        public bool PassedThisTrip { get; set; }

        /// <summary>Consecutive trips with failure.</summary>
        public int ConsecutiveFailTrips { get; set; }

        /// <summary>Good trips since the MIL was commanded.</summary>
        public int GoodTrips { get; set; }

        /// <summary>Warm-up cycles without failure since the MIL went off.</summary>
        public int WarmupsSinceOff { get; set; }

        /// <summary>Times it has failed (occurrence counter).</summary>
        public int Occurrences { get; set; }

        /// <summary>Currently failing (test result of last evaluation).</summary>
        public bool CurrentlyFailing { get; set; }

        /// <summary>Short status string as a scan tool shows.</summary>
        public string StatusText => Confirmed ? (MilOn ? "confirmado, MIL" : "confirmado (histórico)") : (Pending ? "pendiente" : "inactivo");
    }

    /// <summary>
    /// DTC memory implementing OBD-II status logic: pending after one failed trip, confirmed (MIL) after
    /// the configured number of consecutive failed trips, MIL off after 3 good trips, erased after 40 warm-ups.
    /// </summary>
    public sealed class DtcStore
    {
        private readonly Dictionary<string, DtcRecord> _records = new Dictionary<string, DtcRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly DtcCatalog _catalog;

        /// <summary>Creates a store.</summary>
        public DtcStore(DtcCatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>Stored freeze frame (one, like most ECUs).</summary>
        public FreezeFrame? FreezeFrame { get; private set; }

        /// <summary>Distance driven with MIL on (km).</summary>
        public double DistanceWithMilKm { get; set; }

        /// <summary>Time since codes cleared (s).</summary>
        public double TimeSinceClearS { get; set; }

        /// <summary>Warm-up cycles since codes cleared.</summary>
        public int WarmupsSinceClear { get; set; }

        /// <summary>MIL flashing (catalyst damaging misfire).</summary>
        public bool MilFlashing { get; set; }

        /// <summary>All records.</summary>
        public IEnumerable<DtcRecord> Records => _records.Values;

        /// <summary>True if the MIL is on.</summary>
        public bool MilOn
        {
            get
            {
                foreach (DtcRecord r in _records.Values)
                {
                    if (r.MilOn)
                    {
                        return true;
                    }
                }

                return MilFlashing;
            }
        }

        /// <summary>Gets a record if present.</summary>
        public DtcRecord? Get(string code) => _records.TryGetValue(code, out DtcRecord r) ? r : null;

        /// <summary>Confirmed codes (mode 03).</summary>
        public List<string> ConfirmedCodes()
        {
            var l = new List<string>();
            foreach (DtcRecord r in _records.Values)
            {
                if (r.Confirmed)
                {
                    l.Add(r.Code);
                }
            }

            l.Sort(StringComparer.Ordinal);
            return l;
        }

        /// <summary>Pending codes (mode 07).</summary>
        public List<string> PendingCodes()
        {
            var l = new List<string>();
            foreach (DtcRecord r in _records.Values)
            {
                if (r.Pending)
                {
                    l.Add(r.Code);
                }
            }

            l.Sort(StringComparer.Ordinal);
            return l;
        }

        /// <summary>Permanent codes (mode 0A).</summary>
        public List<string> PermanentCodes()
        {
            var l = new List<string>();
            foreach (DtcRecord r in _records.Values)
            {
                if (r.Permanent)
                {
                    l.Add(r.Code);
                }
            }

            l.Sort(StringComparer.Ordinal);
            return l;
        }

        /// <summary>True if the code is pending or confirmed.</summary>
        public bool HasCode(string code) => _records.TryGetValue(code, out DtcRecord r) && (r.Pending || r.Confirmed);

        /// <summary>Reports a failed test result.</summary>
        public void Fail(string code, Func<FreezeFrame>? frame)
        {
            if (!_records.TryGetValue(code, out DtcRecord r))
            {
                r = new DtcRecord { Code = code };
                _records[code] = r;
            }

            r.CurrentlyFailing = true;
            if (r.FailedThisTrip)
            {
                return;
            }

            r.FailedThisTrip = true;
            r.Occurrences++;
            r.Pending = true;
            DtcDefinition def = _catalog.Get(code);
            if (def.Trips <= 1 || r.ConsecutiveFailTrips >= def.Trips - 1)
            {
                Confirm(r, def);
            }

            bool priority = code.StartsWith("P030", StringComparison.Ordinal) || code == "P0171" || code == "P0172" || code == "P0174" || code == "P0175";
            if (frame != null && (FreezeFrame == null || (priority && !IsPriority(FreezeFrame.Code))))
            {
                FreezeFrame = frame();
            }
        }

        private static bool IsPriority(string code) => code.StartsWith("P030", StringComparison.Ordinal) || code == "P0171" || code == "P0172";

        private void Confirm(DtcRecord r, DtcDefinition def)
        {
            r.Confirmed = true;
            r.GoodTrips = 0;
            if (def.Mil)
            {
                r.MilOn = true;
                r.Permanent = true;
            }
        }

        /// <summary>Reports a completed test without failure.</summary>
        public void Pass(string code)
        {
            if (_records.TryGetValue(code, out DtcRecord r))
            {
                r.CurrentlyFailing = false;
                r.PassedThisTrip = true;
            }
        }

        /// <summary>Forces immediate confirmation (catalyst damaging misfire).</summary>
        public void ConfirmNow(string code)
        {
            if (_records.TryGetValue(code, out DtcRecord r))
            {
                Confirm(r, _catalog.Get(code));
            }
        }

        /// <summary>Ends a driving cycle (key off). warmup = engine reached &gt;70 °C rising ≥22 °C.</summary>
        public void EndTrip(bool warmup)
        {
            if (warmup)
            {
                WarmupsSinceClear++;
            }

            var remove = new List<string>();
            foreach (DtcRecord r in _records.Values)
            {
                DtcDefinition def = _catalog.Get(r.Code);
                if (r.FailedThisTrip)
                {
                    r.ConsecutiveFailTrips++;
                    r.GoodTrips = 0;
                    r.WarmupsSinceOff = 0;
                }
                else if (r.PassedThisTrip)
                {
                    r.ConsecutiveFailTrips = 0;
                    if (!r.Confirmed)
                    {
                        r.Pending = false;
                    }

                    if (!r.MilOn)
                    {
                        r.Permanent = false;
                    }
                    else
                    {
                        r.GoodTrips++;
                        if (r.GoodTrips >= 3)
                        {
                            r.MilOn = false;
                            r.Permanent = false;
                            r.Pending = false;
                        }
                    }
                }

                if (r.Confirmed && !r.MilOn && warmup && !r.FailedThisTrip)
                {
                    r.WarmupsSinceOff++;
                    if (r.WarmupsSinceOff >= 40)
                    {
                        remove.Add(r.Code);
                    }
                }

                if (!r.Pending && !r.Confirmed && !r.Permanent)
                {
                    remove.Add(r.Code);
                }

                r.FailedThisTrip = false;
                r.PassedThisTrip = false;
                _ = def;
            }

            foreach (string c in remove)
            {
                _records.Remove(c);
            }

            MilFlashing = false;
        }

        /// <summary>Clears codes (mode 04). Permanent codes survive.</summary>
        public void Clear()
        {
            var keep = new Dictionary<string, DtcRecord>();
            foreach (DtcRecord r in _records.Values)
            {
                if (r.Permanent)
                {
                    keep[r.Code] = new DtcRecord { Code = r.Code, Permanent = true };
                }
            }

            _records.Clear();
            foreach (KeyValuePair<string, DtcRecord> k in keep)
            {
                _records[k.Key] = k.Value;
            }

            FreezeFrame = null;
            DistanceWithMilKm = 0;
            TimeSinceClearS = 0;
            WarmupsSinceClear = 0;
            MilFlashing = false;
        }
    }
}
