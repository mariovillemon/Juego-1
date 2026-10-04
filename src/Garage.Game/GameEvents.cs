using System;

namespace Garage.Game
{
    /// <summary>Everything the UI, audio and tutorial can react to. Presentation-only kinds are reported by the
    /// front end through <see cref="GameSession.Notify"/>; the rest are raised by the game layer itself.</summary>
    public enum GameEventKind
    {
        /// <summary>News feed line (subject empty, text = message).</summary>
        MessagePosted,

        /// <summary>Money changed (value = new amount).</summary>
        MoneyChanged,

        /// <summary>Reputation changed (value = new reputation).</summary>
        ReputationChanged,

        /// <summary>Clock moved (value = minute of day).</summary>
        TimeChanged,

        /// <summary>A new day started (value = day).</summary>
        DayStarted,

        /// <summary>Job board refreshed.</summary>
        OffersRefreshed,

        /// <summary>A job was accepted (subject = job id).</summary>
        JobAccepted,

        /// <summary>A job was rejected or cancelled (subject = job id).</summary>
        JobRejected,

        /// <summary>The customer counter-offered (subject = job id, value = amount).</summary>
        QuoteCountered,

        /// <summary>Car on the lift changed (subject = job id).</summary>
        ActiveJobChanged,

        /// <summary>Parts bought (subject = part ids, comma separated).</summary>
        PartsOrdered,

        /// <summary>Ordered parts arrived (subject = part ids).</summary>
        PartsDelivered,

        /// <summary>A part was fitted (subject = component id).</summary>
        PartInstalled,

        /// <summary>A part was removed to the old-parts box (subject = component id).</summary>
        PartRemoved,

        /// <summary>Wiring repaired (subject = component id).</summary>
        WiringRepaired,

        /// <summary>Any diagnostic tool action (subject = tool id).</summary>
        ToolUsed,

        /// <summary>A component was tested: inspection, meter or scope on it (subject = component id).</summary>
        ComponentTested,

        /// <summary>Scan tool talked to the car.</summary>
        ScannerConnected,

        /// <summary>Codes read (subject = codes, comma separated).</summary>
        DtcRead,

        /// <summary>Codes cleared (subject = module).</summary>
        DtcCleared,

        /// <summary>Live data read.</summary>
        LiveDataRead,

        /// <summary>Multimeter reading (subject = component id, value = reading).</summary>
        MeterMeasured,

        /// <summary>Connector plugged/unplugged (subject = component id, value 1 = connected).</summary>
        ConnectorChanged,

        /// <summary>Ignition on.</summary>
        IgnitionOn,

        /// <summary>Engine started.</summary>
        EngineStarted,

        /// <summary>Engine stopped / key off.</summary>
        EngineStopped,

        /// <summary>Road test finished.</summary>
        RoadTestDone,

        /// <summary>ECU calibration written (subject = table or scalar id).</summary>
        CalibrationChanged,

        /// <summary>Dyno pull finished (value = peak power PS).</summary>
        DynoRunCompleted,

        /// <summary>Engine failure (subject = what broke).</summary>
        EngineFailure,

        /// <summary>Car delivered (subject = job id, value = payment).</summary>
        JobDelivered,

        /// <summary>Tool or upgrade bought (subject = id).</summary>
        UpgradeBought,

        /// <summary>Game saved (subject = slot).</summary>
        GameSaved,

        /// <summary>Game loaded (subject = slot).</summary>
        GameLoaded,

        /// <summary>Tutorial advanced (subject = step id).</summary>
        TutorialStepChanged,

        /// <summary>Tutorial finished or skipped.</summary>
        TutorialCompleted,

        /// <summary>A command failed (text = readable reason).</summary>
        CommandFailed,

        // ---- presentation events (reported by the front end) ----

        /// <summary>Player walked some distance.</summary>
        PlayerMoved,

        /// <summary>Player picked up a tool (subject = tool id).</summary>
        ToolPickedUp,

        /// <summary>Player put a tool down (subject = tool id).</summary>
        ToolPlaced,

        /// <summary>Scan tool plugged into the OBD port.</summary>
        ScannerPlugged,

        /// <summary>Scan tool unplugged.</summary>
        ScannerUnplugged,

        /// <summary>Player selected a component (subject = component id).</summary>
        PartSelected,

        /// <summary>A UI panel was opened (subject = panel id).</summary>
        UiOpened,
    }

    /// <summary>One game event.</summary>
    public sealed class GameEvent
    {
        /// <summary>Creates an event.</summary>
        public GameEvent(GameEventKind kind, string subject = "", string text = "", double value = double.NaN)
        {
            Kind = kind;
            Subject = subject ?? "";
            Text = text ?? "";
            Value = value;
        }

        /// <summary>Kind.</summary>
        public GameEventKind Kind { get; }

        /// <summary>What it refers to (ids).</summary>
        public string Subject { get; }

        /// <summary>Readable text.</summary>
        public string Text { get; }

        /// <summary>Numeric payload (NaN if none).</summary>
        public double Value { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Kind}({Subject}){(Text.Length > 0 ? ": " + Text : "")}";
    }

    /// <summary>Publish/subscribe hub. The UI subscribes once instead of polling.</summary>
    public sealed class GameEventBus
    {
        /// <summary>Raised for every event.</summary>
        public event Action<GameEvent>? Raised;

        /// <summary>Publishes an event.</summary>
        public void Publish(GameEvent e) => Raised?.Invoke(e);

        /// <summary>Publishes an event.</summary>
        public void Publish(GameEventKind kind, string subject = "", string text = "", double value = double.NaN) => Publish(new GameEvent(kind, subject, text, value));
    }
}
