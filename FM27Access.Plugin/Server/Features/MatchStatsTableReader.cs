using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads the match-stats comparison card into a per-team table. The game lays the card out as a
/// "Title" (the two team names) beside a "Content" of stat rows; each row is a "TextBlock" holding a
/// home value, the stat name, and an away value, plus a graphical comparison bar (hidden via the
/// MatchStatsCard panel profile). Flattened in tree order those values stream out without team
/// attribution, so this recognises the block, reads each row's three texts, and folds them into
/// <see cref="MatchStatsTable"/> rows that <see cref="MatchStatsHtml"/> renders as a semantic table.
/// </summary>
internal static class MatchStatsTableReader
{
    // Below this many parseable stat rows the block is not the comparison table (or is a layout we
    // don't recognise), so detection declines and the generic walk renders it instead.
    private const int MinRows = 3;

    // True for the "Frame" that pairs a team-name "Title" with a "Content" of stat rows. Specific
    // enough that no other Frame on the card qualifies: it must own a "Content" child carrying at
    // least MinRows stat rows.
    public static bool IsStatsTableFrame(VisualElement el)
    {
        var content = FindChild(el, "Content");
        if (content == null) return false;
        return CountStatRows(content) >= MinRows;
    }

    // Reads the table out of a Frame accepted by IsStatsTableFrame. Returns null on any anomaly — a
    // missing Content, or a row whose three texts can't be read cleanly — so the caller falls back to
    // the generic flat render rather than silently dropping a stat. Team names are best-effort: a
    // missing name leaves that column header blank (the rows still tie home/away by column).
    public static MatchStatsTable TryRead(VisualElement frameEl)
    {
        var content = FindChild(frameEl, "Content");
        if (content == null) return null;

        var rows = new List<MatchStatRow>();
        for (int i = 0; i < content.hierarchy.childCount; i++)
        {
            var rowEl = content.hierarchy[i];
            if (!IsStatRow(rowEl)) continue;

            if (!TryReadRow(rowEl, out var row))
            {
                Log.Warn($"[MatchStatsTableReader] Unreadable stat row \"{rowEl.name}\"; declining the table.");
                return null;
            }
            rows.Add(row);
        }

        if (rows.Count < MinRows) return null;

        return new MatchStatsTable
        {
            HomeTeam = ReadTeamName(frameEl, "HomeTeam"),
            AwayTeam = ReadTeamName(frameEl, "AwayTeam"),
            Rows = rows,
        };
    }

    // A row's TextBlock holds the stat name as a direct text child and the two team values as the
    // non-text wrappers (a switch around a single number, or around a radial-gauge cell of a tally and
    // a percentage). The label is the lone text child; the wrappers are home (first) and away (last).
    private static bool TryReadRow(VisualElement rowEl, out MatchStatRow row)
    {
        row = default;
        var textBlock = FindChild(rowEl, "TextBlock");
        if (textBlock == null) return false;

        string label = null;
        var cells = new List<VisualElement>(2);
        for (int i = 0; i < textBlock.hierarchy.childCount; i++)
        {
            var child = textBlock.hierarchy[i];
            if (child.TryCast<TextElement>() != null)
            {
                if (label == null) label = ReadText(child);
            }
            else
            {
                cells.Add(child);
            }
        }

        if (string.IsNullOrEmpty(label) || cells.Count < 2) return false;

        var home = MatchStatsHtml.FoldCell(CollectTexts(cells[0]));
        var away = MatchStatsHtml.FoldCell(CollectTexts(cells[cells.Count - 1]));
        row = new MatchStatRow(label, home, away);
        return true;
    }

    // True for a "<Stat>Row" container holding a "TextBlock". The progress-bar block is hidden by the
    // panel profile but still present in the game tree; the TextBlock is the stable marker.
    private static bool IsStatRow(VisualElement el)
    {
        var name = el.name;
        return !string.IsNullOrEmpty(name) && name.EndsWith("Row") && FindChild(el, "TextBlock") != null;
    }

    private static int CountStatRows(VisualElement content)
    {
        int n = 0;
        for (int i = 0; i < content.hierarchy.childCount; i++)
            if (IsStatRow(content.hierarchy[i])) n++;
        return n;
    }

    // The team name lives on the first text leaf under a "HomeTeam"/"AwayTeam" element in the frame's
    // team-header block. The header container is named differently per tab ("Title" on Key Stats,
    // "TitleBlock" on Advanced Stats), so search the frame directly rather than a fixed container name;
    // the header sits before the stat Content, so DFS reaches it first. Clean the rich-text markup the
    // game wraps the name in. Returns "" when absent (a blank column header rather than a wrong one).
    private static string ReadTeamName(VisualElement frame, string teamElementName)
    {
        var team = FindDescendant(frame, teamElementName, 7);
        if (team == null) return "";
        var texts = CollectTexts(team);
        return texts.Count > 0 ? TextFilters.CleanForDisplay(texts[0]) : "";
    }

    // All text leaves under an element, in document order. Bounded depth: the comparison cells and
    // team-name blocks are shallow (a few levels), so a small cap keeps the walk cheap and safe.
    private static List<string> CollectTexts(VisualElement el)
    {
        var texts = new List<string>(2);
        CollectTextsRec(el, texts, 6);
        return texts;
    }

    private static void CollectTextsRec(VisualElement el, List<string> texts, int depth)
    {
        if (depth < 0) return;
        if (el.TryCast<TextElement>() != null)
        {
            var t = ReadText(el);
            if (!string.IsNullOrEmpty(t)) texts.Add(t);
        }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            CollectTextsRec(el.hierarchy[i], texts, depth - 1);
    }

    private static string ReadText(VisualElement el) => el.Cast<TextElement>().text;

    private static VisualElement FindChild(VisualElement parent, string name)
    {
        for (int i = 0; i < parent.hierarchy.childCount; i++)
        {
            var child = parent.hierarchy[i];
            if (child.name == name) return child;
        }
        return null;
    }

    private static VisualElement FindDescendant(VisualElement el, string name, int depth)
    {
        if (depth < 0) return null;
        for (int i = 0; i < el.hierarchy.childCount; i++)
        {
            var child = el.hierarchy[i];
            if (child.name == name) return child;
            var found = FindDescendant(child, name, depth - 1);
            if (found != null) return found;
        }
        return null;
    }
}

internal sealed class MatchStatsTable
{
    public string HomeTeam;
    public string AwayTeam;
    public List<MatchStatRow> Rows;
}
