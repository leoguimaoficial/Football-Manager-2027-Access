using System;
using System.Reflection;
using System.Text;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only calibration probe for the Alt+4 "match conditions" readout. Dumps the RAW engine values
/// behind attendance, weather, pitch condition, crowd mood and match importance so the spoken phrasing
/// can be written against observed numbers instead of assumptions. GET /conditionsprobe. Read-only.
///
/// Four things this exists to settle, none of which the decompiled source can answer:
///   - whether the current fixture's MatchSetup weather matches its displayed conditions,
///   - whether temperature agrees with the degree value displayed by the game,
///   - whether attendance / stadiumCapacity / AwayFansCount are genuinely populated or left at zero,
///   - whether m_matchImportanceLevel is populated at all, since its default is 0 = ExtremelyImportant
///     rather than Invalid, so an unread field would report every match as a cup final.
/// pitchConditionRating is dumped for the same reason: it is a short with no enum and no managed
/// consumer in the entire game (only the native pitch shader reads it), so its scale is unknown.
///
/// Each section is independently guarded: one broken path prints its own error and the rest still dump.
/// </summary>
internal static class ConditionsProbe
{
    private const BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal static string Run()
    {
        var sb = new StringBuilder();
        DumpStadium(sb);
        DumpPitch(sb);
        DumpWeather(sb);
        DumpCrowdMood(sb);
        DumpImportance(sb);
        return sb.ToString();
    }

    // ctrl.Setup.Stadium.stadiumSetupInfo — the sibling field of the pitchSetupInfo that
    // MatchBallLocation.ReadPitchDimensions already reads successfully, so this path is proven.
    private static void DumpStadium(StringBuilder sb)
    {
        sb.Append("========== STADIUM (attendance) ==========\n");
        try
        {
            var ssi = StadiumMember("stadiumSetupInfo", sb);
            if (ssi == null) return;
            foreach (var name in new[]
            {
                "attendance", "stadiumCapacity", "HomeFansCount", "AwayFansCount",
                "stadiumAge", "stadiumID", "isEnclosed", "usesSeatsOnly",
            })
                DumpMember(ssi, name, sb);
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    private static void DumpPitch(StringBuilder sb)
    {
        sb.Append("========== PITCH ==========\n");
        try
        {
            var psi = StadiumMember("pitchSetupInfo", sb);
            if (psi == null) return;
            foreach (var name in new[]
            {
                "pitchConditionRating", "wetnessStrength", "pitchType", "pitchPattern",
                "hasUnderSoilHeating", "precipitationType", "pitchLength", "pitchWidth",
            })
                DumpMember(psi, name, sb);
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    // Shared hops: MatchPlaybackController.Setup (class) -> MatchSetup.Stadium (struct, by value)
    // -> the named struct field. Returns null and prints why when any hop is unavailable.
    private static object StadiumMember(string field, StringBuilder sb)
    {
        var ctrl = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
        if (ctrl == null) { sb.Append("  no MatchPlaybackController in the scene (not in a match)\n"); return null; }
        var setup = ctrl.GetType().GetProperty("Setup", AllInstance).GetValue(ctrl);
        if (setup == null) { sb.Append("  Setup was null\n"); return null; }
        var stadium = setup.GetType().GetProperty("Stadium", AllInstance).GetValue(setup);
        if (stadium == null) { sb.Append("  Stadium was null\n"); return null; }
        var member = stadium.GetType().GetField(field, AllInstance)?.GetValue(stadium);
        if (member == null) sb.Append("  ").Append(field).Append(" not found or null\n");
        return member;
    }

    // FM27 moved the fixture weather to MatchSetup; the graphics singleton may
    // remain initialized with an unused default, so it cannot calibrate the readout.
    private static void DumpWeather(StringBuilder sb)
    {
        sb.Append("========== WEATHER (MatchSetup) ==========\n");
        try
        {
            var controller = UnityEngine.Object.FindObjectOfType<FM.Match.MatchPlaybackController>();
            if (controller?.Setup == null) { sb.Append("  no current MatchSetup\n"); return; }
            object setup = controller.Setup.MatchLightingAndWeatherSetup;
            foreach (var name in new[]
            {
                "temperature", "precipitationType", "precipitationChance", "precipitationIntensity",
                "windStrength", "windDirection", "fogDensity", "time", "day",
            })
                DumpMember(setup, name, sb);
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    // FM27 moved both moods from MatchState to the actual playback frame.
    private static void DumpCrowdMood(StringBuilder sb)
    {
        sb.Append("========== CROWD MOOD ==========\n");
        try
        {
            var match = UnityEngine.Object.FindObjectOfType<FM.Match.Match>();
            if (match == null) { sb.Append("  no FM.Match.Match in the scene (not in a match)\n"); return; }
            var frame = HarmonyLib.AccessTools.Property(typeof(FM.Match.Match), "m_matchFrame")?.GetValue(match);
            if (frame == null) { sb.Append("  m_matchFrame not readable\n"); return; }
            DumpMember(match, "HasStarted", sb);
            DumpMember(frame, "CurrentTimeSlice", sb);
            DumpMember(frame, "HomeCrowdMood", sb);
            DumpMember(frame, "AwayCrowdMood", sb);
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    // m_enabled is dumped alongside the level because the level's default (0) reads as
    // ExtremelyImportant. If dynamic highlights are off and the binding never populated, the raw value
    // is indistinguishable from a genuine cup final — the readout must gate on this, not on the level.
    private static void DumpImportance(StringBuilder sb)
    {
        sb.Append("========== MATCH IMPORTANCE (DynamicHighlights) ==========\n");
        try
        {
            var dh = UnityEngine.Object.FindObjectOfType<FM.Match.DynamicHighlights>();
            if (dh == null) { sb.Append("  no DynamicHighlights in the scene\n"); return; }
            DumpMember(dh, "m_matchImportanceLevel", sb);
            DumpMember(dh, "m_enabled", sb);
        }
        catch (Exception e)
        {
            sb.Append("  FAILED: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }

    // Prints "<name> = <value> (<type>)". IL2CPP surfaces private fields as properties on some types
    // and keeps them as fields on others, so try both before reporting the member missing.
    private static void DumpMember(object target, string name, StringBuilder sb)
    {
        sb.Append("  ").Append(name).Append(" = ");
        try
        {
            var type = target.GetType();
            object value;
            var field = type.GetField(name, AllInstance);
            if (field != null) value = field.GetValue(target);
            else
            {
                var prop = type.GetProperty(name, AllInstance);
                if (prop == null) { sb.Append("<no such field or property>\n"); return; }
                value = prop.GetValue(target);
            }
            if (value == null) { sb.Append("null\n"); return; }
            sb.Append(value).Append(" (").Append(value.GetType().Name).Append(")\n");
        }
        catch (Exception e)
        {
            sb.Append("ERR ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }
    }
}
