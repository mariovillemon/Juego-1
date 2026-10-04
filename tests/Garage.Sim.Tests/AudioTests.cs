using System;
using System.Linq;
using Garage.Game;
using Garage.Game.Audio;
using Xunit;

namespace Garage.Sim.Tests;

/// <summary>Sound bank data and the engine layer mixer.</summary>
public class AudioTests
{
    [Fact]
    public void Bank_LoadsFromData_WithEngineLayersAndEventTriggers()
    {
        SoundBank bank = SoundBank.Load(TestContent.Db);
        Assert.Equal(7, bank.EngineLayers("exterior").Count);
        Assert.Equal(7, bank.EngineLayers("interior").Count);
        Assert.Contains(bank.ForEvent(GameEventKind.ConnectorChanged), s => s.Id == "tool.connector_click");
        // Every trigger names a real event and every simulation cue has a loop.
        foreach (SoundDefinition s in bank.Sounds)
        {
            foreach (string t in s.Triggers)
            {
                Assert.True(Enum.TryParse(t, out GameEventKind _), t);
            }

            Assert.False(string.IsNullOrEmpty(s.Query), s.Id + " has no Freesound query");
        }

        foreach (string cue in new[] { "sound.vacuum_hiss", "sound.rod_knock", "sound.knock", "sound.misfire_exhaust", "sound.fan" })
        {
            Assert.Contains(bank.Sounds, s => s.Id == cue && s.Kind == SoundKind.Loop);
        }
    }

    [Fact]
    public void Mixer_CrossfadesWithEqualPower_AndPitchesByRpm()
    {
        var layers = SoundBank.Load(TestContent.Db).EngineLayers("exterior");
        foreach (double rpm in new[] { 900.0, 1500, 2900, 5000, 7000 })
        {
            foreach (double load in new[] { 0.0, 0.3, 1.0 })
            {
                var mix = EngineSoundMixer.Mix(layers, rpm, load);
                double power = mix.Sum(m => m.Volume * m.Volume);
                Assert.InRange(power, 0.95, 1.05);
                Assert.True(mix.Count(m => m.Volume > 1e-6) <= 4);
            }
        }

        var exact = EngineSoundMixer.Mix(layers, 3800, 1);
        LayerMix mid = exact.Single(m => m.Id == "engine.mid_on_ext");
        Assert.Equal(1, mid.Volume, 6);
        Assert.Equal(1, mid.Pitch, 6);
        LayerMix between = EngineSoundMixer.Mix(layers, 2900, 1).Single(m => m.Id == "engine.low_on_ext");
        Assert.Equal(2900.0 / 2000, between.Pitch, 6);
        Assert.All(EngineSoundMixer.Mix(layers, 0, 0), m => Assert.Equal(0, m.Volume));
    }
}
