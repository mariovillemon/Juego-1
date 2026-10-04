using System.IO;
using System.Linq;
using Garage.Data.Json;
using Garage.Game.Settings;
using Xunit;

namespace Garage.Sim.Tests;

public class SettingsTests
{
    private static string LocaleDir => Path.Combine(TestContent.DataRoot, "locale");

    [Fact]
    public void Settings_RoundTrip_KeepsEveryValue()
    {
        var s = new GameSettings { Quality = 1, ResolutionWidth = 1920, ResolutionHeight = 1080, Window = WindowMode.Windowed, VSync = false, MouseSensitivity = 1.7, InvertY = true, FieldOfView = 85, Language = "en", InspectBlur = true };
        s.SetVolume("Engine", 0.3);
        s.Bindings.Rebind("use", "<Keyboard>/f");
        GameSettings b = GameSettings.Parse(s.ToJson().ToJson());
        Assert.Equal(s.ToJson().ToJson(), b.ToJson().ToJson());
        Assert.Equal(0.3, b.Gain("Engine"), 6);
        Assert.Equal("<Keyboard>/f", b.Bindings.PathOf("use"));
        Assert.Equal("<Keyboard>/e", b.Bindings.PathOf("lamp"));
    }

    [Fact]
    public void Settings_InvalidValues_AreClampedOrDefaulted()
    {
        GameSettings s = GameSettings.Parse("{\"quality\": 9, \"fov\": 300, \"mouseSensitivity\": -2, \"language\": \"xx\", \"window\": \"Bogus\", \"resolutionWidth\": 20, \"volumes\": {\"Master\": 4}}");
        Assert.Equal(3, s.Quality);
        Assert.Equal(100, s.FieldOfView);
        Assert.Equal(0.1, s.MouseSensitivity);
        Assert.Equal("es", s.Language);
        Assert.Equal(WindowMode.FullscreenWindow, s.Window);
        Assert.Equal(0, s.ResolutionWidth);
        Assert.Equal(1, s.Volume("Master"));
        Assert.Equal(2, GameSettings.Parse("{ not json").Quality);
    }

    [Fact]
    public void Rebind_ToUsedKey_Swaps_AndEscapeIsReserved()
    {
        var k = new KeyBindings();
        Assert.True(k.AllUnique());
        Assert.Equal("lamp", k.Rebind("use", "<Keyboard>/f"));
        Assert.Equal("<Keyboard>/e", k.PathOf("lamp"));
        Assert.True(k.AllUnique());
        Assert.Null(k.Rebind("use", KeyBindings.Reserved));
        Assert.Equal("<Keyboard>/f", k.PathOf("use"));
        k.ResetAll();
        Assert.Equal("{}", k.ToJson().ToJson(false));
        Assert.Equal("E", KeyBindings.Display(k.PathOf("use")));
    }

    [Fact]
    public void Bindings_WithDuplicates_AreDiscarded()
    {
        var k = new KeyBindings();
        k.LoadOverrides(JsonValue.Parse("{\"use\": \"<Keyboard>/f\", \"nope\": \"<Keyboard>/x\"}"));
        Assert.Equal("<Keyboard>/e", k.PathOf("use"));
        k.LoadOverrides(JsonValue.Parse("{\"use\": \"<Keyboard>/x\"}"));
        Assert.Equal("<Keyboard>/x", k.PathOf("use"));
    }

    [Fact]
    public void Locale_TablesHaveTheSameKeys_AndCoverAllBindings()
    {
        var loc = new Localizer();
        loc.LoadFolder(LocaleDir);
        Assert.Contains("es", loc.Loaded);
        Assert.Contains("en", loc.Loaded);
        Assert.Empty(loc.Missing("en"));
        Assert.Empty(loc.Keys("en").Except(loc.Keys("es")));
        foreach (BindableAction a in KeyBindings.Actions)
        {
            Assert.Contains(a.LabelKey, loc.Keys("es"));
        }

        foreach (string q in GameSettings.QualityLevels)
        {
            Assert.Contains("opt.quality." + q, loc.Keys("es"));
        }

        foreach (string b in GameSettings.Buses)
        {
            Assert.Contains("opt.vol." + b, loc.Keys("es"));
        }
    }

    [Fact]
    public void Locale_FallsBackToSpanish_ThenToKey()
    {
        var loc = new Localizer();
        loc.LoadTable("{\"language\": \"es\", \"strings\": {\"a\": \"hola {0}\", \"b\": \"sólo es\"}}");
        loc.LoadTable("{\"language\": \"en\", \"strings\": {\"a\": \"hello {0}\"}}");
        int changed = 0;
        loc.Changed += () => changed++;
        loc.SetLanguage("en");
        Assert.Equal(1, changed);
        Assert.Equal("hello Ana", loc.Format("a", "Ana"));
        Assert.Equal("sólo es", loc.Get("b"));
        Assert.Equal("zzz", loc.Get("zzz"));
        Assert.Equal(new[] { "b" }, loc.Missing("en"));
    }
}
