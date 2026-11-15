using System;
using System.Reflection;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Alt+4 match-conditions readouts: a single tap speaks the crowd size and the weather, a double
/// tap both crowds' moods. Everything a sighted player absorbs from the stadium around them without
/// being told.
///
/// Engine sources, read live at the moment of the press:
///   - <c>MatchPlaybackController.Setup.Stadium.stadiumSetupInfo</c> for attendance and capacity. This
///     is the sibling field of the <c>pitchSetupInfo</c> that <see cref="MatchBallLocation"/> already
///     reads for pitch dimensions, so the struct path is proven rather than assumed.
///   - <c>MatchPlaybackController.Setup.MatchLightingAndWeatherSetup</c> for FM27's temperature and
///     precipitation. The old graphics singleton can hold an unused default instead.
///   - <c>Match.m_matchFrame</c> for FM27's playback crowd moods. MatchState no longer carries
///     these fields. Before the first playback frame its neutral values are uninitialized.
///
/// Crowd mood changes continuously. Attendance and weather are near-static, but NOT provably fixed:
/// attendance was observed moving (3,189 to 3,194) within one match, so treating them as constants and
/// caching them would eventually speak a stale number. Nothing is cached — every press re-reads.
///
/// Wind and pitch condition are deliberately absent. Both are readable (<c>m_windStrength</c> 0.75 and
/// <c>pitchConditionRating</c> 170 were captured live) but neither has an enum, a threshold table, or
/// any consumer in the game's managed code, so there is nothing to anchor a spoken band against. A
/// scale invented here would sound authoritative and could be wrong, so they wait for calibration.
///
/// Main thread only — both entry points marshal via <see cref="MainThreadDispatcher"/>.
/// </summary>
internal static class MatchConditionsReadout
{
    private const BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static string _lastDegradeReason;

    // A broken engine path would otherwise log on every keypress for the whole match; log a given
    // reason once until it changes. The spoken marker is still emitted every call.
    private static void NoteDegradeOnce(string reason)
    {
        if (reason == _lastDegradeReason) return;
        _lastDegradeReason = reason;
        Speech.Degrade.Note(reason);
    }

    /// <summary>Alt+4: attendance, share of capacity, away following, temperature and sky.</summary>
    internal static string Describe()
    {
        try
        {
            return MainThreadDispatcher.Invoke(ComposeConditions);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchConditionsReadout] conditions read failed: " + ex.Message);
            return "match conditions " + Speech.Degrade.Unavailable;
        }
    }

    /// <summary>Alt+4 double tap: both crowds' moods, home first, in the engine's own vocabulary.</summary>
    internal static string DescribeCrowdMood()
    {
        try
        {
            return MainThreadDispatcher.Invoke(ComposeCrowdMood);
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchConditionsReadout] crowd mood read failed: " + ex.Message);
            return "crowd mood " + Speech.Degrade.Unavailable;
        }
    }

    private static string ComposeConditions()
    {
        if (MatchBallLocation.CurrentMatch() == null) return "Not in a match.";

        ReadStadium(out int? attendance, out int? capacity, out int? awayFans);
        ReadWeather(out float? temperature, out string precipitation);

        var line = MatchConditions.Compose(attendance, capacity, awayFans, temperature, precipitation);
        if (line == null)
        {
            NoteDegradeOnce("[MatchConditionsReadout] no conditions clause was readable");
            return "match conditions " + Speech.Degrade.Unavailable;
        }
        return line;
    }

    private static string ComposeCrowdMood()
    {
        var match = MatchBallLocation.CurrentMatch();
        if (match == null) return "Not in a match.";

        string home = null, away = null;
        try
        {
            var frame = HarmonyLib.AccessTools.Property(typeof(FM.Match.Match), "m_matchFrame")?.GetValue(match)
                as FM.Match.MatchFrame;
            if (frame == null) NoteDegradeOnce("[MatchConditionsReadout] m_matchFrame was not readable");
            else if (!match.HasStarted || frame.CurrentTimeSlice <= 0)
                NoteDegradeOnce("[MatchConditionsReadout] no played crowd-mood frame yet");
            else
            {
                home = frame.HomeCrowdMood.ToString();
                away = frame.AwayCrowdMood.ToString();
            }
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchConditionsReadout] crowd mood frame read failed: " + ex.Message);
        }

        var line = MatchConditions.ComposeCrowdMood(home, away);
        if (line == null)
        {
            NoteDegradeOnce("[MatchConditionsReadout] neither crowd mood was readable");
            return "crowd mood " + Speech.Degrade.Unavailable;
        }
        return line;
    }

    // ctrl.Setup.Stadium.stadiumSetupInfo. Each value is independently null on failure so one broken
    // field costs its own clause, not the whole readout.
    private static void ReadStadium(out int? attendance, out int? capacity, out int? awayFans)
    {
        attendance = null;
        capacity = null;
        awayFans = null;
        try
        {
            var ctrl = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
            if (ctrl == null)
            {
                NoteDegradeOnce("[MatchConditionsReadout] no MatchPlaybackController for the stadium read");
                return;
            }
            var setup = ctrl.GetType().GetProperty("Setup", AllInstance)?.GetValue(ctrl);
            var stadium = setup?.GetType().GetProperty("Stadium", AllInstance)?.GetValue(setup);
            var info = stadium?.GetType().GetField("stadiumSetupInfo", AllInstance)?.GetValue(stadium);
            if (info == null)
            {
                NoteDegradeOnce("[MatchConditionsReadout] stadiumSetupInfo was not readable");
                return;
            }
            attendance = Int(info, "attendance");
            capacity = Int(info, "stadiumCapacity");
            awayFans = Int(info, "AwayFansCount");
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchConditionsReadout] stadium read failed: " + ex.Message);
        }
    }

    // FM27 exposes the fixture's actual weather on MatchSetup. The legacy
    // graphics-scene lighting singleton can be initialized with a default setup.
    private static void ReadWeather(out float? temperature, out string precipitation)
    {
        temperature = null;
        precipitation = null;
        try
        {
            var controller = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
            if (controller?.Setup == null)
            {
                NoteDegradeOnce("[MatchConditionsReadout] no current MatchSetup for the weather read");
                return;
            }
            var setup = controller.Setup.MatchLightingAndWeatherSetup;
            temperature = setup.temperature;
            precipitation = setup.precipitationType.ToString();
        }
        catch (Exception ex)
        {
            NoteDegradeOnce("[MatchConditionsReadout] weather read failed: " + ex.Message);
        }
    }

    private static int? Int(object target, string field) =>
        target.GetType().GetField(field, AllInstance)?.GetValue(target) as int?;

}
