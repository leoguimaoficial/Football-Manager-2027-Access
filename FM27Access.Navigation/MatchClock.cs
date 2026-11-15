using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// The match periods that change how the clock reads. Mirrors the subset of
/// SI.Match.MatchPeriod the clock display cares about; the plugin maps the game enum
/// onto this so the formatter stays free of any game dependency and is unit-testable.
/// </summary>
public enum MatchClockState
{
    Playing,
    HalfTime,
    FullTime,
    Penalties,
}

/// <summary>
/// Formats the live match clock from raw engine values. Pure and game-independent:
/// the plugin reads FM.Match.Match.m_matchState and passes the primitives here.
/// </summary>
public static class MatchClock
{
    // Display baseline of each play period: first half, second half, extra-time halves.
    private static readonly int[] PeriodBases = { 0, 45 * 60, 90 * 60, 105 * 60 };

    /// <summary>
    /// Maps the engine's continuous playback clock to the displayed clock. The engine time
    /// runs continuously across the whole match — first-half stoppage carries into the
    /// second half's raw value — while the game's scorebar restarts each period at its
    /// nominal base (45:00, 90:00, 105:00). Which period the scoreboard frames comes from
    /// the boundary times alone, never the engine's period enum: that enum tracks the sim
    /// frontier, which runs ahead of the playback the player is watching.
    ///
    /// periodStarts / periodEnds are the engine times of the period kick-off and period-end
    /// whistle stops, ascending (ends[i] ends the period starts[i] began). Boundaries the
    /// playback has not reached yet are harmless: engineSeconds selects among them. Between
    /// a period's final whistle and the next kick-off the scoreboard shows the next period's
    /// baseline, frozen (observed live: 45:00 during the half-time break). Returns null
    /// when the boundaries are missing or name more periods than football has; the caller
    /// must degrade rather than show the raw value, which reads minutes ahead of the screen.
    /// </summary>
    public static double? DisplaySeconds(
        double engineSeconds, IReadOnlyList<double> periodStarts, IReadOnlyList<double> periodEnds)
    {
        if (periodStarts == null || periodStarts.Count == 0) return null;

        int period = 0;
        for (int i = 1; i < periodStarts.Count; i++)
            if (engineSeconds >= periodStarts[i]) period = i;
        if (periodEnds != null && period < periodEnds.Count && engineSeconds >= periodEnds[period])
            period++;

        if (period >= PeriodBases.Length) return null;
        double sinceKickoff = period < periodStarts.Count ? engineSeconds - periodStarts[period] : 0;
        if (sinceKickoff < 0) sinceKickoff = 0;
        return PeriodBases[period] + sinceKickoff;
    }
    /// <summary>
    /// Renders the clock as MM:SS (seconds zero-padded) with "+N" appended during
    /// injury time. Non-play periods render their state word instead of a ticking
    /// number, since a running clock would be misleading at a break or full time.
    /// </summary>
    public static string Format(double seconds, int injuryTime, MatchClockState state)
    {
        switch (state)
        {
            case MatchClockState.HalfTime:
                return "Half Time";
            case MatchClockState.FullTime:
                return "Full Time";
            case MatchClockState.Penalties:
                return "Penalties";
        }

        int total = seconds > 0 ? (int)seconds : 0;
        string clock = $"{total / 60}:{total % 60:00}";
        if (injuryTime > 0) clock += $" +{injuryTime}";
        return clock;
    }
}
