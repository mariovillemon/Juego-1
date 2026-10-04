using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Garage.Game;
using Garage.Sim.Game;
using Xunit;

namespace Garage.Sim.Tests;

/// <summary>Application layer: the full loop the CLI and Unity share.</summary>
public class GameSessionTests
{
    private static GameSession NewSession(bool training = true) => new(TestContent.Db, 77, training);

    [Fact]
    public void FullLoop_Tutorial_Diagnose_Buy_Repair_Deliver_Save_Load_Continue()
    {
        GameSession s = NewSession();
        var events = new List<GameEvent>();
        s.Events.Raised += events.Add;

        // Tutorial puts its scripted job on the board.
        Assert.True(s.StartTutorial("tut_first_car").Ok);
        s.Notify(GameEventKind.PlayerMoved);
        Job job = s.Offers().First(j => j.Definition.Id == "tut_first_car_job");
        Assert.Contains("tirones", job.Definition.Complaint);

        // Quote from the work sheet.
        QuoteDraft q = s.NewQuote(job);
        q.AddLabour("Sustituir bobina", 0.5);
        Assert.Equal(QuoteAnswer.Accepted, s.SendQuote(q).Answer);
        Assert.Same(job, s.ActiveJob);
        CarWork w = s.Work!;

        // Scanner: needs to be plugged and the ignition on.
        Assert.Equal(CommandError.InvalidState, w.ReadCodes().Error);
        s.Notify(GameEventKind.ToolPickedUp, CarWork.Scanner);
        s.Notify(GameEventKind.ScannerPlugged);
        Assert.False(w.ScanConnect().Ok);
        w.IgnitionOn();
        Assert.True(w.ScanConnect().Ok);
        Assert.True(w.ReadCodes().Ok);
        GameEvent dtc = events.Last(e => e.Kind == GameEventKind.DtcRead);
        Assert.Contains("P0302", dtc.Subject);
        w.Start();
        Assert.True(w.LiveData().Ok);
        Assert.True(w.Inspect("coil2").Ok);

        // Shop: aftermarket coil is immediate.
        PartDefinition coil = s.PartsFor(job.Car, "coil2").First(p => p.Quality == PartQuality.Aftermarket);
        double before = s.Money;
        s.AddToCart(coil.Id);
        Assert.True(s.Checkout().Ok);
        Assert.Equal(before - coil.Price, s.Money, 2);
        Assert.Equal(1, s.Inventory.Count(coil.Id));

        Assert.True(s.InstallPart("coil2", coil.Id).Ok);
        Assert.Equal(0, s.Inventory.Count(coil.Id));
        Assert.Single(s.Inventory.OldParts);
        Assert.True(s.Inventory.OldParts[0].WasFaulty);
        Assert.Contains("Defecto", s.InspectOldPart(0).Message);

        Assert.True(w.ClearCodes().Ok);
        Assert.True(w.RoadTest().Ok);

        JobOutcome? o = s.Deliver(job, out CommandResult delivered);
        Assert.True(delivered.Ok);
        Assert.NotNull(o);
        Assert.True(o!.Success, string.Join(" | ", o.Notes) + " :: " + string.Join(",", job.Car.Faults.Unrepaired().Select(f => f.Mode.Id + "@" + f.ComponentId + "/" + f.Origin)));
        Assert.Null(s.ActiveJob);
        Assert.True(s.Tutorial!.Finished);
        Assert.Contains(events, e => e.Kind == GameEventKind.TutorialCompleted);
        Assert.Contains(events, e => e.Kind == GameEventKind.MoneyChanged);
        Assert.Contains(events, e => e.Kind == GameEventKind.TimeChanged);

        // Next day, save, "close", load and continue.
        int day = s.Day;
        s.EndDay();
        Assert.Equal(day + 1, s.Day);
        Assert.Contains(events, e => e.Kind == GameEventKind.DayStarted);

        string dir = Path.Combine(Path.GetTempPath(), "garage_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var slots = new SaveSlots(dir);
            slots.Save(s, 2);
            SaveSlotInfo info = slots.List().Single(i => i.Slot == 2);
            Assert.True(info.Used);
            Assert.Contains($"Día {s.Day}", info.Summary);

            GameSession loaded = NewSession(false);
            Assert.True(slots.Load(loaded, 2).Ok);
            Assert.Equal(s.Day, loaded.Day);
            Assert.Equal(s.Money, loaded.Money, 2);
            Assert.Equal(s.Reputation, loaded.Reputation, 3);
            Assert.True(loaded.Training);
            Assert.Single(loaded.Inventory.OldParts);

            Job next = loaded.Offers().First();
            Assert.Equal(QuoteAnswer.Accepted, loaded.SendQuote(next, 1).Answer);
            Assert.NotNull(loaded.Work);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MinimalLoop_AsInUnityEditModeTest()
    {
        var s = new GameSession(TestContent.Db, 5, true);
        Assert.True(s.StartTutorial("tut_first_car").Ok);
        Job job = s.Offers().First(j => j.Definition.Id == "tut_first_car_job");
        Assert.Equal(QuoteAnswer.Accepted, s.SendQuote(job, s.SuggestQuote(job)).Answer);
        s.Notify(GameEventKind.ScannerPlugged);
        s.Work!.IgnitionOn();
        Assert.True(s.Work.ReadCodes().Ok);
        PartDefinition coil = s.PartsFor(job.Car, "coil2").First(p => p.Quality == PartQuality.Aftermarket);
        Assert.True(s.InstallPart("coil2", coil.Id, buyIfMissing: true).Ok);
        Assert.True(s.Work.ClearCodes().Ok);
        JobOutcome? o = s.Deliver(job, out CommandResult r);
        Assert.True(r.Ok);
        Assert.True(o!.Success, string.Join(" | ", o.Notes) + " :: " + string.Join(",", job.Car.Faults.Unrepaired().Select(f => f.Mode.Id + "@" + f.ComponentId + "/" + f.Origin)));
    }

    [Fact]
    public void Commands_FailWithReadableReasons()
    {
        GameSession s = NewSession(false);
        Assert.Equal(CommandError.NoActiveJob, s.InstallPart("coil2", "part_ignitioncoil_oem").Error);

        Job job = s.Offers().First();
        s.SendQuote(job, 1);
        CarWork w = s.Work!;
        Assert.Equal(CommandError.ToolLocked, w.RunDyno().Error);
        Assert.Equal(CommandError.ToolLocked, w.CompressionTest(false).Error);
        Assert.Equal(CommandError.ToolLocked, w.Ecu.SetCell(w.Ecu.TableIds[0], 0, 0, 1).Error);

        string anyComponent = job.Car.Parts.All.First(c => s.PartsFor(job.Car, c.Id).Count > 0).Id;
        PartDefinition part = s.PartsFor(job.Car, anyComponent).First();
        Assert.Equal(CommandError.NotInStock, s.InstallPart(anyComponent, part.Id).Error);

        PartDefinition wrong = TestContent.Db.Parts.All.First(p => p.Kind != part.Kind);
        Assert.Equal(CommandError.IncompatiblePart, s.InstallPart(anyComponent, wrong.Id).Error);

        s.Workshop.Money = 1;
        s.AddToCart(part.Id);
        Assert.Equal(CommandError.NotEnoughMoney, s.Checkout().Error);
        CommandError buy = s.BuyUpgrade("tool_dyno").Error;
        Assert.True(buy == CommandError.NotEnoughMoney || buy == CommandError.ReputationTooLow, buy.ToString());
    }

    [Fact]
    public void OemParts_ArriveNextDay()
    {
        GameSession s = NewSession(false);
        PartDefinition oem = TestContent.Db.Parts.All.First(p => p.Quality == PartQuality.Oem);
        s.AddToCart(oem.Id, 2);
        Assert.True(s.Checkout().Ok);
        Assert.Equal(0, s.Inventory.Count(oem.Id));
        Assert.Single(s.Inventory.Orders);
        var delivered = new List<GameEvent>();
        s.Events.Raised += e => { if (e.Kind == GameEventKind.PartsDelivered) { delivered.Add(e); } };
        s.EndDay();
        Assert.Equal(2, s.Inventory.Count(oem.Id));
        Assert.Single(delivered);
    }

    [Fact]
    public void Haggler_CountersAndJobStaysOffered()
    {
        GameSession s = NewSession(false);
        Job job = s.Offers(8).FirstOrDefault(j => j.Customer.Personality == "haggler") ?? throw new InvalidOperationException("no haggler in content");
        double limit = s.Workshop.QuoteLimit(job);
        QuoteDecision d = s.SendQuote(job, limit * 1.15);
        Assert.Equal(QuoteAnswer.Counter, d.Answer);
        Assert.Equal(JobStatus.Offered, job.Status);
        Assert.Equal(QuoteAnswer.Accepted, s.SendQuote(job, d.Amount).Answer);
    }

    [Fact]
    public void EcuEditor_WritesNeedInterface_UndoAndInterpolate()
    {
        GameSession s = NewSession(false);
        Job job = s.Offers().First();
        s.SendQuote(job, 1);
        s.Workshop.Grant(CarWork.EcuFlash);
        CarWork w = s.Work!;
        string table = w.Ecu.TableIds.First(t => w.Ecu.Table(t)!.X.Count >= 3 && w.Ecu.Table(t)!.Y.Count >= 3);
        Assert.Equal(CommandError.InvalidState, w.Ecu.SetCell(table, 0, 0, 5).Error);
        s.Notify(GameEventKind.ScannerPlugged);

        double original = w.Ecu.Table(table)![1, 1];
        Assert.True(w.Ecu.SetCell(table, 1, 1, original + 3).Ok);
        Assert.Equal(original + 3, w.Ecu.Table(table)![1, 1], 6);
        Assert.Contains(w.Ecu.CompareWithStock(), d => d.Table == table && d.ChangedCells == 1);
        Assert.True(w.Ecu.Undo().Ok);
        Assert.Equal(original, w.Ecu.Table(table)![1, 1], 6);

        Assert.True(w.Ecu.SetCell(table, 0, 0, 0).Ok);
        Assert.True(w.Ecu.SetCell(table, 0, 2, 2).Ok);
        Assert.True(w.Ecu.SetCell(table, 2, 0, 2).Ok);
        Assert.True(w.Ecu.SetCell(table, 2, 2, 4).Ok);
        Assert.True(w.Ecu.Interpolate(table, 0, 2, 0, 2).Ok);
        Assert.Equal(2, w.Ecu.Table(table)![1, 1], 6);
        Assert.True(w.Ecu.RestoreStock().Ok);
        Assert.All(w.Ecu.CompareWithStock(), d => Assert.Equal(0, d.ChangedCells));
    }

    [Fact]
    public void Tutorial_IsDataDriven_AndCanBeSkipped()
    {
        GameSession s = NewSession();
        TutorialDefinition t = s.Tutorials().Single(x => x.Id == "tut_first_car");
        Assert.True(t.Steps.Count >= 10);
        s.StartTutorial(t.Id);
        Assert.Equal("move", s.Tutorial!.Current!.Id);
        s.Notify(GameEventKind.ToolPickedUp, CarWork.Scanner); // wrong step: ignored
        Assert.Equal("move", s.Tutorial.Current!.Id);
        s.Notify(GameEventKind.PlayerMoved);
        Assert.Equal("accept", s.Tutorial.Current!.Id);
        s.SkipTutorial();
        Assert.True(s.Tutorial.Finished);
    }
}
