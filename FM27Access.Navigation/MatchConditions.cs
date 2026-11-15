using System;
using System.Globalization;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// Phrasing for the Alt+4 match-conditions readout: the crowd size and weather a sighted player takes
/// from the screen and the stadium around them. Pure composition over values the caller has already
/// read from the engine, so it unit-tests without the game.
///
/// Every clause degrades on its own. A failed read never shortens the line silently — silence is
/// indistinguishable from "nothing to report", so an unreadable clause says so out loud and the
/// clauses that did read are still spoken.
/// </summary>
public static class MatchConditions
{
    /// <summary>
    /// The single-tap line: attendance and its share of capacity, the away following, then the weather.
    /// Null when nothing at all could be read, so the caller can degrade the whole readout rather than
    /// speak a line made entirely of gap markers.
    /// </summary>
    /// <param name="attendance">Heads through the gate, or null if unreadable.</param>
    /// <param name="capacity">Stadium capacity; null or non-positive omits the percentage clause only.</param>
    /// <param name="awayFans">Away following, or null if unreadable. Zero is a real answer, not a gap.</param>
    /// <param name="temperatureCelsius">Air temperature in Celsius, or null if unreadable.</param>
    /// <param name="precipitation">
    /// The engine's PrecipitationType name (e.g. "LightSnow"), or null if unreadable.
    /// </param>
    public static string Compose(
        int? attendance, int? capacity, int? awayFans, float? temperatureCelsius, string precipitation)
    {
        string attendanceClause = ComposeAttendance(attendance, capacity);
        string awayClause = ComposeAwayFans(awayFans);
        string weatherClause = ComposeWeather(temperatureCelsius, precipitation);

        // Everything failed: let the caller emit one honest marker instead of three.
        if (attendance == null && awayFans == null && weatherClause == null) return null;

        var sb = new StringBuilder();
        Append(sb, attendanceClause);
        Append(sb, awayClause);
        Append(sb, weatherClause ?? "weather unavailable");
        return sb.ToString();
    }

    /// <summary>
    /// The double-tap line: both crowds' moods, home first, in the engine's own vocabulary. Null when
    /// neither end could be read.
    /// </summary>
    /// <param name="homeMood">The engine's CrowdMood name (e.g. "IdleHappy"), or null if unreadable.</param>
    /// <param name="awayMood">The engine's CrowdMood name, or null if unreadable.</param>
    public static string ComposeCrowdMood(string homeMood, string awayMood)
    {
        string home = SpellEnum(homeMood);
        string away = SpellEnum(awayMood);
        if (home == null && away == null) return null;
        return "home crowd " + (home ?? "unavailable") + ", away crowd " + (away ?? "unavailable");
    }

    private static string ComposeAttendance(int? attendance, int? capacity)
    {
        if (attendance == null) return "attendance unavailable";
        string heads = Number(attendance.Value);
        // A missing or zero capacity costs the share, not the attendance itself.
        if (capacity == null || capacity.Value <= 0) return "Attendance: " + heads;
        // AwayFromZero, not the default banker's rounding, which would speak 62.5% as "62".
        int percent = (int)Math.Round(attendance.Value * 100.0 / capacity.Value, MidpointRounding.AwayFromZero);
        return "Attendance: " + heads + ", " + percent + "% of capacity";
    }

    private static string ComposeAwayFans(int? awayFans)
    {
        if (awayFans == null) return "away fans unavailable";
        // Said explicitly so a genuine empty away end is distinguishable from a failed read.
        if (awayFans.Value == 0) return "No away fans";
        return Number(awayFans.Value) + " away fans";
    }

    // Both halves are needed for an honest weather clause: a temperature with no condition, or a
    // condition with no temperature, would leave the listener unsure whether the rest was dry or unread.
    private static string ComposeWeather(float? temperatureCelsius, string precipitation)
    {
        string condition = Precipitation(precipitation);
        if (temperatureCelsius == null || condition == null) return null;
        return Degrees(temperatureCelsius.Value) + ", " + condition;
    }

    // "minus" is spelled out because a leading hyphen is read inconsistently by screen readers -
    // NVDA announces it as "dash" in some punctuation modes, which would invert a sub-zero reading.
    private static string Degrees(float celsius)
    {
        int rounded = (int)Math.Round(celsius, MidpointRounding.AwayFromZero);
        string magnitude = Math.Abs(rounded).ToString(CultureInfo.InvariantCulture);
        string sign = rounded < 0 ? "minus " : "";
        return sign + magnitude + (Math.Abs(rounded) == 1 ? " degree" : " degrees");
    }

    private static string Precipitation(string name) => name switch
    {
        "Dry" => "dry",
        "Wet" => "wet",
        "Drizzle" => "drizzle",
        "Shower" => "showers",
        "Rain" => "rain",
        "DownPour" => "heavy rain",
        "Sleet" => "sleet",
        "Hail" => "hail",
        "LightSnow" => "light snow",
        "HeavySnow" => "heavy snow",
        // MaxPrecipitationType is the enum's count sentinel, not a real sky; an unmapped or numeric
        // name means the read produced something we cannot name, which is a gap, not "dry".
        _ => null,
    };

    /// <summary>
    /// An engine enum name as spoken words: "IdleHappy" becomes "idle happy". The engine's own
    /// vocabulary is kept rather than reinterpreted, so a state the mod has never seen still speaks
    /// something truthful. Null for a missing name or one that is not an identifier (a numeric
    /// ToString from an unmapped enum value).
    /// </summary>
    private static string SpellEnum(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!char.IsLetter(name[0])) return null;

        var sb = new StringBuilder(name.Length + 6);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsLetterOrDigit(c)) return null;
            if (i > 0 && char.IsUpper(c)) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static void Append(StringBuilder sb, string clause)
    {
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(clause).Append('.');
    }
}
