using System;
using System.Collections.Generic;
using System.Globalization;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Coach Report's "Season Stats" block (shown in the radar's place for a player without league
/// figures), as a Statistic | Value table. The block draws, in order: a headline label and its figure
/// ("Saves", "0"), a ring gauge whose caption reads a percentage then its subject ("0%", "Held"), then
/// label/value pairs ("Saves/90", "0.00"…). Walked raw it reads as a column of bare numbers, the
/// gauge's figure split from what it measures.
///
/// The ring also prints a figure of its own inside it. Its meaning is not established, so the table is
/// built only when that figure is the caption's percentage again (then dropping it loses nothing); any
/// other shape returns null and the block keeps its plain reading. Pure, so the tests compile it in.
/// </summary>
internal static class CoachSeasonStats
{
    // Mod-authored chrome.
    internal static readonly string[] Headers = { "Statistic", "Value" };

    /// <param name="texts">The block's visible texts in order, without the ring's own figure.</param>
    /// <param name="ringFigure">The figure printed inside the ring.</param>
    internal static List<List<string>> Build(IReadOnlyList<string> texts, string ringFigure, out string reason)
    {
        reason = null;
        if (texts.Count < 4 || texts.Count % 2 != 0) { reason = $"{texts.Count} texts, not a headline, a gauge and pairs"; return null; }
        string headline = texts[0], headlineValue = texts[1], percent = texts[2], caption = texts[3];
        if (!IsFigure(headlineValue) || IsFigure(headline)) { reason = $"headline \"{headline}\" / \"{headlineValue}\" is not a label and a figure"; return null; }
        if (!percent.EndsWith("%", StringComparison.Ordinal) || !IsFigure(percent.TrimEnd('%')) || IsFigure(caption))
        { reason = $"gauge \"{percent}\" / \"{caption}\" is not a percentage and its subject"; return null; }
        if (!SameNumber(ringFigure, percent.TrimEnd('%')))
        { reason = $"ring prints {ringFigure}, caption {percent}: the ring's own figure would be lost"; return null; }

        var rows = new List<List<string>>
        {
            new() { headline, headlineValue },
            new() { caption, percent },
        };
        for (int i = 4; i < texts.Count; i += 2)
        {
            if (IsFigure(texts[i]) || !IsFigure(texts[i + 1])) { reason = $"pair \"{texts[i]}\" / \"{texts[i + 1]}\" is not a label and a figure"; return null; }
            rows.Add(new List<string> { texts[i], texts[i + 1] });
        }
        return rows;
    }

    private static bool IsFigure(string s)
        => !string.IsNullOrWhiteSpace(s) && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool SameNumber(string a, string b)
        => double.TryParse(a?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
           && double.TryParse(b?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
           && x == y;
}
