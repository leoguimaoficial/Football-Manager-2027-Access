using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// The surface's liveness rules. The point of these is that the three ways the main-thread hand-off
/// can stop — the core shutting itself down, the core's tick failing, the game no longer running
/// frames — must never collapse into one indistinguishable answer, because that is what left testers
/// hearing "Main-thread dispatch timed out (is the game updating?)" for every failure.
/// </summary>
public class SurfaceHealthTests
{
    [Fact]
    public void RuntimeAndHostAttributeVersionMatchTheCompiledMod()
    {
        var assembly = typeof(FM27Access.Speech.BuildProfile).Assembly;
        var info = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly);
        Assert.NotNull(info);
        Assert.Equal(info.InformationalVersion.Split('+')[0], FM27Access.Speech.BuildProfile.Version);
    }

    private const long Fresh = 100;                       // drained a moment ago
    private const long Stale = SurfaceHealth.StallMs + 1; // long enough to count as stalled

    [Fact]
    public void DrainingNormally_IsLiveAndSaysNothing()
    {
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, Fresh, SurfaceHealth.Never);

        Assert.Equal(SurfaceHealth.State.Live, state);
        Assert.True(SurfaceHealth.CanServe(state));
        Assert.Equal("", SurfaceHealth.Message(state));
    }

    [Fact]
    public void BeforeTheGameIsInteractive_IsLoading()
    {
        var state = SurfaceHealth.Evaluate(ready: false, stopped: false, SurfaceHealth.Never, SurfaceHealth.Never);

        Assert.Equal(SurfaceHealth.State.Loading, state);
        Assert.False(SurfaceHealth.CanServe(state));
        Assert.Equal("The game is still loading.", SurfaceHealth.Message(state));
    }

    [Fact]
    public void AStoppedCore_SaysSoEvenThoughItLooksIdenticalFromTheOutside()
    {
        // The Kill path pauses the tick, so the drain timestamp goes stale exactly as it would for a
        // hung game. The recorded shutdown is what separates them, so it wins over any timing.
        var state = SurfaceHealth.Evaluate(ready: true, stopped: true, Stale, SurfaceHealth.Never);

        Assert.Equal(SurfaceHealth.State.ModStopped, state);
        Assert.Equal("The accessibility mod has stopped. Restart the game.", SurfaceHealth.Message(state));
    }

    [Fact]
    public void AStoppedCore_IsReportedEvenWhileStillDraining()
    {
        // Pause records the stop before dropping the tick, so a frame may still be in flight.
        var state = SurfaceHealth.Evaluate(ready: true, stopped: true, Fresh, SurfaceHealth.Never);

        Assert.Equal(SurfaceHealth.State.ModStopped, state);
    }

    [Fact]
    public void AStalledDrainWithATickThatIsThrowing_BlamesTheMod()
    {
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, Stale, msSinceTickFailure: Fresh);

        Assert.Equal(SurfaceHealth.State.ModFailing, state);
    }

    [Fact]
    public void AModThatMayStillRecover_IsNotToldToRestart()
    {
        // A tick throwing for a few seconds can come back on its own. Advising a restart costs unsaved
        // progress, so the state is described and nothing is prescribed.
        var message = SurfaceHealth.Message(SurfaceHealth.State.ModFailing, prolonged: false);

        Assert.Equal("The accessibility mod is not responding.", message);
        Assert.DoesNotContain("Restart", message);
    }

    [Fact]
    public void AModStillFailingAfterALongWhile_IsToldToRestart()
    {
        var message = SurfaceHealth.Message(SurfaceHealth.State.ModFailing, prolonged: true);

        Assert.Equal("The accessibility mod is not responding. Restart the game.", message);
    }

    [Fact]
    public void AStalledGame_IsNeverToldToRestartHoweverLongItTakes()
    {
        // The game may simply be busy — a 50s pause building a heavy screen was measured live — and a
        // restart on the mod's advice could throw away the player's unsaved progress.
        foreach (var prolonged in new[] { false, true })
        {
            var message = SurfaceHealth.Message(SurfaceHealth.State.GameNotResponding, prolonged);
            Assert.Equal("The game is not responding.", message);
            Assert.DoesNotContain("Restart", message);
        }
    }

    [Fact]
    public void TheProlongedThresholdIsWellClearOfTheStallThreshold()
    {
        // A stall becomes reportable long before it becomes restart-worthy; collapsing the two would
        // put the advice back on every transient hiccup.
        Assert.True(SurfaceHealth.ProlongedMs > SurfaceHealth.StallMs * 3);
        Assert.False(SurfaceHealth.IsProlonged(SurfaceHealth.ProlongedMs - 1));
        Assert.True(SurfaceHealth.IsProlonged(SurfaceHealth.ProlongedMs));
    }

    [Fact]
    public void AStalledDrainWithNoTickFailures_BlamesTheGame()
    {
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, Stale, SurfaceHealth.Never);

        Assert.Equal(SurfaceHealth.State.GameNotResponding, state);
        Assert.Equal("The game is not responding.", SurfaceHealth.Message(state));
    }

    [Fact]
    public void AnOldTickFailure_DoesNotKeepBlamingTheMod()
    {
        // A tick that threw once long ago says nothing about why the drain stopped now.
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, Stale, msSinceTickFailure: Stale);

        Assert.Equal(SurfaceHealth.State.GameNotResponding, state);
    }

    [Fact]
    public void ALongFrameIsNotAStall()
    {
        // Just under the threshold: a heavy load or a processing step must not be called
        // "not responding", which would be its own lie.
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, SurfaceHealth.StallMs - 1, SurfaceHealth.Never);

        Assert.Equal(SurfaceHealth.State.Live, state);
    }

    [Fact]
    public void ATimeoutOnALiveSurface_ReadsAsBusyNotBroken()
    {
        // One read lost a race with a long frame. Nothing has stopped, so nothing should claim it has.
        Assert.Equal("The game is busy.", SurfaceHealth.TimeoutMessage(SurfaceHealth.State.Live));
    }

    [Fact]
    public void ATimeoutOnAStalledSurface_NamesTheSideThatStopped()
    {
        Assert.Equal("The game is not responding.",
                     SurfaceHealth.TimeoutMessage(SurfaceHealth.State.GameNotResponding));
        Assert.Equal("The accessibility mod is not responding.",
                     SurfaceHealth.TimeoutMessage(SurfaceHealth.State.ModFailing));
        Assert.Equal("The accessibility mod has stopped. Restart the game.",
                     SurfaceHealth.TimeoutMessage(SurfaceHealth.State.ModStopped));
    }

    // The misattribution these pin down was measured live: an in-match substitution left the mod
    // holding the main thread for 10-32 seconds, and every reading the surface had — a stale drain
    // with no tick failure — was the exact signature of a game that had stopped. The mod told the
    // player "The game is not responding" about time it was spending itself.

    [Fact]
    public void ModWorkHoldingTheMainThread_BlamesTheModNotTheGame()
    {
        // Identical readings to AStalledDrainWithNoTickFailures_BlamesTheGame, plus the one fact that
        // separates them: the mod is inside its own main-thread work.
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, Stale, SurfaceHealth.Never,
                                           msHoldingMainThread: Stale);

        Assert.Equal(SurfaceHealth.State.ModBusy, state);
        Assert.False(SurfaceHealth.CanServe(state));
        Assert.Equal("The accessibility mod is busy.", SurfaceHealth.Message(state));
    }

    [Fact]
    public void ModWorkHoldingTheMainThread_OutranksATickFailure()
    {
        // A tick that threw says the mod is broken; work still running says where the thread actually
        // is. The second is the live fact, so it wins — otherwise a stale failure mislabels the stall.
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, Stale, msSinceTickFailure: Fresh,
                                           msHoldingMainThread: Stale);

        Assert.Equal(SurfaceHealth.State.ModBusy, state);
    }

    [Fact]
    public void ModWorkUnderTheStallThreshold_IsStillTheGamesStall()
    {
        // An action that started recently is not what has held the thread for the whole stall window,
        // so it must not absolve the game.
        var state = SurfaceHealth.Evaluate(ready: true, stopped: false, Stale, SurfaceHealth.Never,
                                           msHoldingMainThread: SurfaceHealth.StallMs - 1);

        Assert.Equal(SurfaceHealth.State.GameNotResponding, state);
    }

    [Fact]
    public void ABusyModIsDescribedBeforeItIsPrescribedFor()
    {
        // Same tiering as a failing tick: work in flight may still finish, and a restart costs the
        // player unsaved progress, so the advice waits until the stall has earned it.
        Assert.Equal("The accessibility mod is busy.",
                     SurfaceHealth.Message(SurfaceHealth.State.ModBusy, prolonged: false));
        Assert.Equal("The accessibility mod is busy. Restart the game.",
                     SurfaceHealth.Message(SurfaceHealth.State.ModBusy, prolonged: true));
    }

    [Fact]
    public void ATimeoutWhileTheModHoldsTheThread_DoesNotCallTheGameBusy()
    {
        // A timeout arrives on the caller's budget, well before StallMs, so the surface still reads
        // Live. That window is how "The game is busy." was reported for the mod's own hold.
        Assert.Equal("The accessibility mod is busy.",
                     SurfaceHealth.TimeoutMessage(SurfaceHealth.State.Live, modHoldingMainThread: true));
        Assert.Equal("The game is busy.",
                     SurfaceHealth.TimeoutMessage(SurfaceHealth.State.Live, modHoldingMainThread: false));
    }

    [Fact]
    public void NoStateEverSpeaksTheDispatchMechanism()
    {
        foreach (SurfaceHealth.State state in System.Enum.GetValues(typeof(SurfaceHealth.State)))
        {
            var message = SurfaceHealth.Message(state) + " " + SurfaceHealth.TimeoutMessage(state);
            Assert.DoesNotContain("dispatch", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("thread", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("timed out", message, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
