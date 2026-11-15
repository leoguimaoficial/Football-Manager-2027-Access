using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>One comparison row of the match-stats card: the stat name and each team's value.
/// Home/Away are the already-folded display values (see <see cref="MatchStatsHtml.FoldCell"/>); a
/// simple stat is a lone number ("27"), a ratio stat folds its tally and percentage into one string
/// ("21 / 43 (48%)").</summary>
public readonly struct MatchStatRow
{
    public readonly string Label;
    public readonly string Home;
    public readonly string Away;

    public MatchStatRow(string label, string home, string away)
    {
        Label = label;
        Home = home;
        Away = away;
    }
}

/// <summary>
/// Pure HTML builder for the match-stats comparison card. The game lays each stat out as a row of a
/// home value, the stat name, an away value, and a graphical comparison bar; flattened in tree order
/// the values stream out asymmetrically (the home value rides inside the bar, the two ratio archetypes
/// differ), which a screen reader cannot associate to a team. This renders the rows as one semantic
/// table — stat name as the row header, one column per team — so each number is tied to its team.
/// Game-free so the folding and escaping are unit-testable; the live read is FM27Access.Plugin's
/// MatchStatsTableReader.
/// </summary>
public static class MatchStatsHtml
{
    // Fold one comparison cell's raw text leaves into a single display value. A simple stat carries a
    // lone leaf ("27"). A ratio stat carries a tally ("21 / 43") and a percentage ("48%") in either
    // order (the home and away sides mirror each other), folded to "21 / 43 (48%)". Anything else is
    // joined in source order rather than guessed at, so an unexpected shape degrades to visible text.
    public static string FoldCell(IReadOnlyList<string> texts)
    {
        if (texts == null || texts.Count == 0) return "";

        var trimmed = new List<string>(texts.Count);
        foreach (var t in texts)
        {
            var s = t?.Trim();
            if (!string.IsNullOrEmpty(s)) trimmed.Add(s);
        }

        if (trimmed.Count == 1) return trimmed[0];

        string ratio = null, percent = null;
        foreach (var s in trimmed)
        {
            if (percent == null && s.Contains("%")) percent = s;
            else if (ratio == null && s.Contains("/")) ratio = s;
        }

        if (ratio != null && percent != null) return $"{ratio} ({percent})";
        return string.Join(" ", trimmed);
    }

    // homeTeam/awayTeam are the live, cleaned team names (the column headers); rows carry game stat
    // names and folded values. All are escaped at render. The stat-name column has no header text (the
    // card already carries a "Match Stats" heading), so the corner cell is left empty.
    public static string Build(string homeTeam, string awayTeam, IReadOnlyList<MatchStatRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>")
          .Append("<th></th>")
          .Append("<th scope=\"col\">").Append(Escape(homeTeam)).Append("</th>")
          .Append("<th scope=\"col\">").Append(Escape(awayTeam)).Append("</th>")
          .Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>")
              .Append("<th scope=\"row\">").Append(Escape(row.Label)).Append("</th>")
              .Append("<td>").Append(Escape(row.Home)).Append("</td>")
              .Append("<td>").Append(Escape(row.Away)).Append("</td>")
              .Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
