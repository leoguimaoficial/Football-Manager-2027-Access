using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>One season's row in a club's league-history chart: the season, the division it was
/// played in, and the club's finishing position. Position is the raw value as read from the game
/// (almost always an integer like "17"); the formatter renders it as an ordinal when it parses.</summary>
public readonly struct LeagueHistoryRow
{
    public readonly string Season;
    public readonly string Division;
    public readonly string Position;

    public LeagueHistoryRow(string season, string division, string position)
    {
        Season = season;
        Division = division;
        Position = position;
    }
}

/// <summary>
/// Pure HTML builder for the league-history line chart. The game renders the club's season-by-season
/// finishing positions as a graphical line chart with no text nodes, so the chart is invisible to a
/// screen reader. This turns the chart's underlying data points into a semantic table (Season,
/// Division, Position) the player can browse. Game-free so escaping and ordinal formatting are
/// unit-testable; the live data is read by FM27Access.Plugin's LeagueHistoryChartReader.
/// </summary>
public static class LeagueHistoryHtml
{
    public static string Build(IReadOnlyList<LeagueHistoryRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>")
          .Append("<th scope=\"col\">Season</th>")
          .Append("<th scope=\"col\">Division</th>")
          .Append("<th scope=\"col\">Position</th>")
          .Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>")
              .Append("<td>").Append(Escape(row.Season)).Append("</td>")
              .Append("<td>").Append(Escape(Speech.TextFilters.CleanForDisplay(row.Division))).Append("</td>")
              .Append("<td>").Append(Escape(Ordinal(row.Position))).Append("</td>")
              .Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    // "17" -> "17th", "1" -> "1st", "22" -> "22nd". A non-integer position (a gap marker or an
    // unexpected value) is returned unchanged rather than guessed at.
    internal static string Ordinal(string position)
    {
        if (!int.TryParse(position, out int n) || n < 0) return position;
        int mod100 = n % 100;
        string suffix = (mod100 >= 11 && mod100 <= 13) ? "th" : (n % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return n + suffix;
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
