using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

public class SoundSettingsTests
{
    [Fact]
    public void WingDrones_AreOffUntilThePlayerAsksForThem()
    {
        // The one default that is not "as authored". A fresh install must not start a continuous bed
        // running under the whole match for a player who has never heard of it, so if this flips to
        // true every new and updated install silently gains the drones.
        Assert.False(new SoundSettings().WingDronesEnabled);
    }

    [Fact]
    public void EveryLevel_DefaultsToAsAuthored()
    {
        // 100 is the position the companion maps to exactly the pre-settings loudness. If any group
        // defaulted lower, installing this feature would quietly turn that group down for everyone.
        var s = new SoundSettings();
        Assert.Equal(SoundSettings.DefaultLevel, s.Master);
        Assert.Equal(SoundSettings.DefaultLevel, s.Possession);
        Assert.Equal(SoundSettings.DefaultLevel, s.Playback);
        Assert.Equal(SoundSettings.DefaultLevel, s.Events);
        Assert.Equal(SoundSettings.DefaultLevel, s.Drones);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-1000, 0)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(55, 55)]
    public void ClampLevel_HoldsTheRange(int input, int expected)
    {
        Assert.Equal(expected, SoundSettings.ClampLevel(input));
    }

    [Fact]
    public void TrySetLevel_ClampsRatherThanRejecting()
    {
        // A hand-edited file or a bad request must not be able to push a gain past unity: above 100
        // the companion's taper would amplify, which can clip the mix rather than merely sound loud.
        var s = new SoundSettings();
        Assert.True(s.TrySetLevel(SoundSettings.EventsKey, 400));
        Assert.Equal(100, s.Events);
        Assert.True(s.TrySetLevel(SoundSettings.EventsKey, -5));
        Assert.Equal(0, s.Events);
    }

    [Fact]
    public void TrySetLevel_RoundTripsEveryGroupIndependently()
    {
        // Each key must reach its own field. A copy-paste slip in the switch would make two sliders
        // drive one group, which on the page looks like a slider that does nothing.
        var s = new SoundSettings();
        s.TrySetLevel(SoundSettings.MasterKey, 10);
        s.TrySetLevel(SoundSettings.PossessionKey, 20);
        s.TrySetLevel(SoundSettings.PlaybackKey, 30);
        s.TrySetLevel(SoundSettings.EventsKey, 40);
        s.TrySetLevel(SoundSettings.DronesKey, 50);

        Assert.Equal(10, s.GetLevel(SoundSettings.MasterKey));
        Assert.Equal(20, s.GetLevel(SoundSettings.PossessionKey));
        Assert.Equal(30, s.GetLevel(SoundSettings.PlaybackKey));
        Assert.Equal(40, s.GetLevel(SoundSettings.EventsKey));
        Assert.Equal(50, s.GetLevel(SoundSettings.DronesKey));
    }

    [Fact]
    public void TrySetLevel_RefusesAnUnknownKey()
    {
        // The endpoint reports a bad key rather than answering "ok" to a write that changed nothing.
        var s = new SoundSettings();
        Assert.False(s.TrySetLevel("speech", 50));
        Assert.False(s.TrySetLevel(SoundSettings.WingDronesKey, 50));   // a toggle, not a level
        Assert.Equal(-1, s.GetLevel("speech"));
    }

    [Fact]
    public void TrySetToggle_OnlyAcceptsTheWingDroneKey()
    {
        var s = new SoundSettings();
        Assert.True(s.TrySetToggle(SoundSettings.WingDronesKey, true));
        Assert.True(s.WingDronesEnabled);
        Assert.True(s.TrySetToggle(SoundSettings.WingDronesKey, false));
        Assert.False(s.WingDronesEnabled);
        Assert.False(s.TrySetToggle(SoundSettings.MasterKey, true));    // a level, not a toggle
    }

    [Fact]
    public void PressIndicator_IsOffUntilThePlayerAsksForIt()
    {
        // Same reasoning as the wing drones: it is a sustained bed, and a bed is the kind of sound a
        // player chooses rather than has chosen for them. A fresh install hums at nobody.
        Assert.False(new SoundSettings().PressureEnabled);
        // The LEVEL still defaults to full, so switching it on plays it as authored rather than silent.
        Assert.Equal(SoundSettings.DefaultLevel, new SoundSettings().Pressure);
    }

    [Fact]
    public void TrySetToggle_AcceptsThePressureKeyIndependently()
    {
        // The two switches must not be the same switch: turning the drones off cannot take the pressure
        // bed with it.
        var s = new SoundSettings();
        Assert.True(s.TrySetToggle(SoundSettings.PressureEnabledKey, false));
        Assert.False(s.PressureEnabled);
        Assert.False(s.WingDronesEnabled);

        Assert.True(s.TrySetToggle(SoundSettings.WingDronesKey, true));
        Assert.False(s.PressureEnabled);
        Assert.True(s.WingDronesEnabled);
    }

    [Fact]
    public void PressureLevel_RoundTripsAndClamps()
    {
        var s = new SoundSettings();
        Assert.True(s.TrySetLevel(SoundSettings.PressureKey, 40));
        Assert.Equal(40, s.GetLevel(SoundSettings.PressureKey));
        Assert.True(s.TrySetLevel(SoundSettings.PressureKey, 900));
        Assert.Equal(SoundSettings.MaxLevel, s.Pressure);
        Assert.False(s.TrySetLevel(SoundSettings.PressureEnabledKey, 50));   // a toggle, not a level
    }

    [Fact]
    public void Normalise_RepairsValuesLoadedFromDisk()
    {
        // Deserialising sets the properties directly, bypassing TrySetLevel's clamp, so a file with a
        // level of 5000 would otherwise reach the audio graph unchecked.
        var s = new SoundSettings { Master = 5000, Possession = -20, Playback = 100, Events = 0, Drones = 101,
                                    Pressure = -1 };
        s.Normalise();
        Assert.Equal(100, s.Master);
        Assert.Equal(0, s.Possession);
        Assert.Equal(100, s.Playback);
        Assert.Equal(0, s.Events);
        Assert.Equal(100, s.Drones);
        Assert.Equal(0, s.Pressure);
    }

    [Fact]
    public void Normalise_LeavesTheWingDroneSwitchAlone()
    {
        // Clamping levels must not disturb the toggle: a player who enabled the drones keeps them
        // through any repair of an out-of-range level elsewhere in the same file.
        var s = new SoundSettings { WingDronesEnabled = true, Master = 900 };
        s.Normalise();
        Assert.True(s.WingDronesEnabled);
    }

    [Fact]
    public void Copy_IsIndependentOfItsSource()
    {
        // The store hands snapshots to callers; a shared reference would let a reader mutate the
        // saved settings without ever going through the endpoint.
        var s = new SoundSettings { Master = 40, WingDronesEnabled = true };
        var copy = s.Copy();
        copy.Master = 90;
        copy.WingDronesEnabled = false;
        Assert.Equal(40, s.Master);
        Assert.True(s.WingDronesEnabled);
    }
}
