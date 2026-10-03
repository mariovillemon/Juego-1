using System.IO;
using System.Linq;
using Garage.Data;
using Garage.Data.Json;
using Garage.Sim.Faults;
using Xunit;

namespace Garage.Sim.Tests;

public class DataTests
{
    [Fact]
    public void AllBaseContent_ValidatesAgainstSchemas()
    {
        LoadReport r = TestContent.Db.Report;
        Assert.True(r.Ok, string.Join("\n", r.Errors.Select(e => e.ToString())));
        Assert.True(r.FilesRead >= 15);
    }

    [Fact]
    public void Catalog_HasAtLeast150GenericCodes_WithCorrectFormat()
    {
        Assert.True(TestContent.Db.Catalog.Count >= 150);
        foreach (var d in TestContent.Db.Catalog.All)
        {
            Assert.Matches("^[PU][0-3][0-9A-F]{3}$", d.Code);
            Assert.False(string.IsNullOrWhiteSpace(d.DescriptionEs));
        }

        Assert.Equal("System Too Lean (Bank 1)", TestContent.Db.Catalog.Get("P0171").Description);
        Assert.Equal("Coolant Thermostat (Coolant Temperature Below Thermostat Regulating Temperature)", TestContent.Db.Catalog.Get("P0128").Description);
    }

    [Fact]
    public void EveryCodeTheEcuCanSet_IsInTheCatalog()
    {
        string src = File.ReadAllText(Path.Combine(TestContent.DataRoot, "..", "src", "Garage.Sim", "Ecu", "EngineControlUnit.cs"));
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "\"([PU][0-9A-F]{4})\""))
        {
            Assert.True(TestContent.Db.Catalog.Contains(m.Groups[1].Value), m.Groups[1].Value);
        }
    }

    [Fact]
    public void AllCars_BuildAndStart()
    {
        foreach (string id in TestContent.Db.Ids("cars"))
        {
            var car = TestContent.Db.CreateCar(id, 1, warm: true);
            Assert.True(car.Start(), id);
            Assert.True(car.Parts.All.Count > 40, id);
        }
    }

    [Fact]
    public void AtLeast40Scenarios_AllReferToExistingComponentsAndModes()
    {
        var scenarios = TestContent.Db.Raw("scenarios");
        Assert.True(scenarios.Count >= 40);
        foreach (var sc in scenarios.Values)
        {
            var car = TestContent.Db.CreateCar(sc.Str("car"), 1);
            foreach (JsonValue f in sc["faults"].Items)
            {
                FaultInstance fi = TestContent.Db.MapFault(f);
                var comp = car.Parts.Get(fi.ComponentId);
                Assert.True(comp != null, $"{sc.Str("id")}: component {fi.ComponentId}");
                Assert.True(comp!.Accepts(fi.Mode), $"{sc.Str("id")}: {fi.Mode.Id} not applicable to {comp.Kind}");
                if (fi.Mode.Family == "wiring" && car.CircuitOf(comp.Id) != null)
                {
                    Assert.True(car.CircuitOf(comp.Id)!.HasPin(fi.Pin), $"{sc.Str("id")}: pin {fi.Pin}");
                }
            }
        }
    }

    [Fact]
    public void TenJobs_ReferenceValidCustomersCarsAndScenarios()
    {
        var jobs = TestContent.Db.Raw("jobs");
        Assert.True(jobs.Count >= 10);
        foreach (var j in jobs.Values)
        {
            Assert.True(TestContent.Db.Raw("customers").ContainsKey(j.Str("customer")));
            Assert.True(TestContent.Db.Raw("cars").ContainsKey(j.Str("car")));
            if (j.Has("scenario"))
            {
                Assert.True(TestContent.Db.Raw("scenarios").ContainsKey(j.Str("scenario")));
            }
        }
    }

    [Fact]
    public void FailureModesJson_MirrorsBuiltInLibrary()
    {
        var raw = TestContent.Db.Raw("failure_modes");
        foreach (FailureModeDefinition m in FailureModeLibrary.Default.All)
        {
            Assert.True(raw.ContainsKey(m.Id), $"failure_modes.json lacks {m.Id} (run: garage export-modes)");
            Assert.Equal(m.Effect.ToString(), raw[m.Id].Str("effect"));
        }
    }

    [Fact]
    public void Json_ParsesAndRoundTrips()
    {
        JsonValue v = JsonValue.Parse("{\"a\": [1, 2.5, -3e2], \"b\": {\"c\": \"x\\n\\u00e9\"}, \"d\": true, \"e\": null} // comment");
        Assert.Equal(-300, v["a"][2].NumberValue);
        Assert.Equal("x\né", v["b"].Str("c"));
        JsonValue back = JsonValue.Parse(v.ToJson());
        Assert.Equal(v.ToJson(false), back.ToJson(false));
    }

    [Theory]
    [InlineData("{\"a\": }")]
    [InlineData("[1, 2")]
    [InlineData("{\"a\": 1, \"a\": 2}")]
    [InlineData("\"unterminated")]
    public void Json_RejectsMalformed(string text)
    {
        Assert.Throws<JsonParseException>(() => JsonValue.Parse(text));
    }

    [Fact]
    public void SchemaValidator_ReportsClearErrors()
    {
        JsonValue schema = JsonValue.Parse("{\"type\":\"object\",\"required\":[\"id\"],\"additionalProperties\":false,\"properties\":{\"id\":{\"type\":\"string\",\"pattern\":\"^[a-z]+$\"},\"n\":{\"type\":\"number\",\"minimum\":0,\"maximum\":10},\"k\":{\"enum\":[\"a\",\"b\"]}}}");
        var v = new SchemaValidator(_ => null);
        Assert.Empty(v.Validate(JsonValue.Parse("{\"id\":\"ok\",\"n\":3,\"k\":\"a\"}"), schema, "t.json"));
        var errors = v.Validate(JsonValue.Parse("{\"n\":30,\"k\":\"z\",\"extra\":1}"), schema, "t.json");
        Assert.Contains(errors, e => e.Message.Contains("obligatoria 'id'"));
        Assert.Contains(errors, e => e.Path == "/n" && e.Message.Contains("máximo"));
        Assert.Contains(errors, e => e.Path == "/k");
        Assert.Contains(errors, e => e.Message.Contains("desconocida 'extra'"));
    }

    [Fact]
    public void BrokenCar_FailsValidation()
    {
        var loader = new ContentLoader(Path.Combine(TestContent.DataRoot, "schemas"));
        JsonValue schema = loader.Schema("car.schema.json")!;
        var v = new SchemaValidator(loader.Schema);
        var errors = v.Validate(JsonValue.Parse("{\"id\":\"x\",\"brand\":\"B\",\"model\":\"M\",\"year\":1800,\"engine\":\"e\",\"template\":\"t\",\"calibration\":\"c\",\"appearance\":{\"maintenance\":3}}"), schema, "car.json");
        Assert.Contains(errors, e => e.Path == "/year");
        Assert.Contains(errors, e => e.Path == "/appearance/maintenance");
    }

    [Fact]
    public void Mods_CanAddAndOverrideContent()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "garage_mod_test_" + System.Guid.NewGuid().ToString("N"));
        string mod = Path.Combine(tmp, "mymod");
        Directory.CreateDirectory(Path.Combine(mod, "cars"));
        File.WriteAllText(Path.Combine(mod, "mod.json"), "{\"name\":\"Test\",\"priority\":10}");
        File.WriteAllText(Path.Combine(mod, "cars", "patch.json"), "{\"id\":\"velmora_pico\",\"$patch\":true,\"model\":\"Pico Sport\",\"appearance\":{\"paintColor\":\"#00FF00\"}}");
        string clone = File.ReadAllText(Path.Combine(TestContent.DataRoot, "base", "cars", "velmora_pico.json")).Replace("\"velmora_pico\"", "\"velmora_pico_clone\"");
        File.WriteAllText(Path.Combine(mod, "cars", "clone.json"), clone);
        try
        {
            ContentDatabase db = ContentDatabase.Load(TestContent.DataRoot, tmp);
            Assert.True(db.Report.Ok, string.Join("\n", db.Report.Errors));
            Assert.Equal("Pico Sport", db.Car("velmora_pico").Model);
            Assert.Equal("#00FF00", db.Car("velmora_pico").Appearance.PaintColor);
            Assert.Equal(14, db.Car("velmora_pico").Appearance.AgeYears);
            Assert.NotNull(db.Car("velmora_pico_clone"));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}
