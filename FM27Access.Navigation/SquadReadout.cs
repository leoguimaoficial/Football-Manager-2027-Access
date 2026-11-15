namespace FM27Access.Navigation;

/// <summary>Which per-player match value a squad readout ranks, deciding its spoken format.</summary>
public enum SquadMetric
{
    /// <summary>Match rating, spoken to one decimal ("7.4").</summary>
    Rating,
    /// <summary>Physical condition, spoken as a whole percentage ("84%").</summary>
    Condition,
}

/// <summary>Which end of the ranking a squad readout reports.</summary>
public enum SquadRank
{
    Best,
    Worst,
}

/// <summary>
/// Composes the on-demand "top/bottom three of your on-pitch players" match readouts (the Alt+1
/// and Alt+2 companion chords: single tap best, double tap worst). Pure and game-independent: the
/// plugin reads each player's name and live value from the in-match squad panel and passes them
/// here, so the ranking and phrasing stay unit-testable.
///
/// The line leads with the ranking word so the player immediately knows which of the four similar
/// readouts answered: "best rated: Hamilton 7.4, Wilson 7.1, Vázquez 6.9". Ties keep the callers'
/// order with no special wording. Fewer than three readable players speaks the ones there are — the
/// caller reports players it could NOT read separately, so a short list here is honest, not a gap.
/// An empty list returns null and the caller degrades with its own marker.
/// </summary>
public static class SquadReadout
{
    /// <summary>One readable player: a display name and the live value to rank by.</summary>
    public readonly struct Entry
    {
        public readonly string Name;
        public readonly double Value;

        public Entry(string name, double value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// The spoken readout line, or null when <paramref name="entries"/> is empty. Ranks all entries,
    /// speaks the top three (ties broken by list order — OrderBy is stable), one "name value" clause
    /// per player. <paramref name="unreadableCount"/> is how many on-pitch players the caller could
    /// NOT read a value for; any non-zero count is spoken as a trailing gap clause, because an
    /// unread player could outrank everyone listed — without the clause the ranking would present
    /// possibly-wrong data as correct.
    /// </summary>
    public static string Compose(IReadOnlyList<Entry> entries, SquadMetric metric, SquadRank rank,
                                 int unreadableCount = 0)
    {
        if (entries == null || entries.Count == 0) return null;

        var ranked = rank == SquadRank.Best
            ? entries.OrderByDescending(e => e.Value)
            : entries.OrderBy(e => e.Value);

        var clauses = ranked.Take(3).Select(e => e.Name + " " + Speak(e.Value, metric));
        var line = Lead(metric, rank) + ": " + string.Join(", ", clauses);
        if (unreadableCount > 0)
            line += ", " + unreadableCount + (unreadableCount == 1 ? " player unreadable" : " players unreadable");
        return line;
    }

    /// <summary>
    /// The body-language overview line: how many on-pitch players sit in each band, most
    /// populous first ("body language: 6 neutral, 2 very positive, 2 negative, 1 positive").
    /// <paramref name="bandCounts"/> arrives in canonical band order (best to worst), which
    /// breaks count ties — OrderByDescending is stable. Zero-count bands are omitted entirely.
    /// Null when no player could be counted (the caller degrades); a non-zero
    /// <paramref name="unreadableCount"/> is spoken as the same trailing gap clause as the
    /// ranking readouts.
    /// </summary>
    public static string ComposeBandCounts(IReadOnlyList<(string Band, int Count)> bandCounts,
                                           int unreadableCount = 0)
    {
        if (bandCounts == null) return null;
        var present = bandCounts.Where(b => b.Count > 0).OrderByDescending(b => b.Count).ToList();
        if (present.Count == 0) return null;

        var line = "body language: " + string.Join(", ", present.Select(b => b.Count + " " + b.Band));
        if (unreadableCount > 0)
            line += ", " + unreadableCount + (unreadableCount == 1 ? " player unreadable" : " players unreadable");
        return line;
    }

    /// <summary>
    /// The body-language detail line (the Alt+3 double tap): every on-pitch player whose icon
    /// band is not neutral, with the game's detailed word — "Vázquez Complacent, Wilson Nervous".
    /// When every counted player is neutral it says so instead, because silence would be
    /// indistinguishable from a failed read. <paramref name="neutralCount"/> is how many players
    /// were counted as neutral (and therefore omitted); null when nothing was counted at all
    /// (the caller degrades). The unreadable gap clause matches the other squad readouts.
    /// </summary>
    public static string ComposeNonNeutral(IReadOnlyList<(string Name, string Word)> nonNeutral,
                                           int neutralCount, int unreadableCount = 0)
    {
        if (nonNeutral == null || (nonNeutral.Count == 0 && neutralCount == 0)) return null;

        var line = nonNeutral.Count == 0
            ? "all players neutral body language"
            : string.Join(", ", nonNeutral.Select(p => p.Name + " " + p.Word));
        if (unreadableCount > 0)
            line += ", " + unreadableCount + (unreadableCount == 1 ? " player unreadable" : " players unreadable");
        return line;
    }

    /// <summary>The distinguishing lead word pair for one of the four readouts, spoken first.</summary>
    private static string Lead(SquadMetric metric, SquadRank rank) => (metric, rank) switch
    {
        (SquadMetric.Rating, SquadRank.Best) => "best rated",
        (SquadMetric.Rating, SquadRank.Worst) => "lowest rated",
        (SquadMetric.Condition, SquadRank.Best) => "best condition",
        _ => "lowest condition",
    };

    private static string Speak(double value, SquadMetric metric) => metric == SquadMetric.Rating
        ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        : System.Math.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
}
