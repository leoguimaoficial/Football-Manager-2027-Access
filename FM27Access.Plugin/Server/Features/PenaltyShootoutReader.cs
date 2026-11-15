using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Reads the in-match penalty shootout card. The game draws it as a scoreboard ("US Orléans 0 - 1
/// SC Toulon", split across five separate text leaves and two kit-colour swatches) above a pair of
/// indicator strips — one per team — where each taken penalty is a bare icon and the untaken ones
/// are background numbers 1..5. Walked raw that streams out as "0", "-", "1", then "1 2 3 4 5 1 2 3
/// 4 5" with the actual results, the only thing the player needs, carried entirely by sprites and
/// therefore invisible.
///
/// This reads the scoreboard as one line and the strips as one round-per-row table, taking each
/// result from the icon's resolved sprite name. An icon whose sprite names neither outcome degrades
/// to a marker in that cell rather than guessing at scored/missed.
/// </summary>
internal static class PenaltyShootoutReader
{
    public const string PanelName = "PenaltyShootoutPanel";

    // The two blocks this reader consolidates, by their own element names.
    public const string ScoreboardName = "team-info";
    public const string IndicatorsName = "penalty-indicators";

    // Inside each team's frame there are two lists: the background 1..5 round numbers (chrome, named
    // for what they are) and the results list, which carries one entry per penalty actually taken.
    private const string ResultsListName = "StreamedObjectList";
    private const string ResultsViewName = "View";
    private const string IconName = "Icon";

    // A regulation shootout is five rounds; sudden death extends it a round at a time, so the table
    // is at least this tall and grows to fit whichever side has taken more.
    private const int RegulationRounds = 5;

    // Mod-authored: the game states each outcome only as a sprite, so there is no game string to
    // reuse. One word each, matching how a commentator reads a shootout.
    private const string Scored = "Scored";
    private const string Missed = "Missed";

    /// <summary>The shootout scoreboard, already folded into one line ("US Orléans 0 - 1 SC Toulon").</summary>
    public sealed class Scoreboard
    {
        public string HomeTeam;
        public string AwayTeam;
        public string Line;
    }

    /// <summary>Per-round outcomes for both teams, index 0 = round 1. An entry is null for a round
    /// that team has not reached yet.</summary>
    public sealed class Rounds
    {
        public List<string> Home;
        public List<string> Away;
    }

    public static bool IsScoreboard(VisualElement el)
        => (el.name ?? "") == ScoreboardName && ElementBridge.IsInPanel(el, PanelName);

    public static bool IsIndicators(VisualElement el)
        => (el.name ?? "") == IndicatorsName && ElementBridge.IsInPanel(el, PanelName);

    /// <summary>
    /// Folds the scoreboard's text leaves into one line. Reads them in tree order (home name, home
    /// score, separator, away score, away name), so the line says exactly what the card shows.
    /// Null when the block holds no text at all, so the caller falls back to the generic walk rather
    /// than rendering an empty line.
    /// </summary>
    public static Scoreboard TryReadScoreboard(VisualElement el)
    {
        var texts = new List<string>();
        CollectTexts(el, texts);
        if (texts.Count == 0) return null;

        return new Scoreboard
        {
            HomeTeam = texts[0],
            AwayTeam = texts[texts.Count - 1],
            Line = string.Join(" ", texts),
        };
    }

    /// <summary>
    /// Reads both teams' result strips. Null when neither frame holds a results list, so an
    /// unrecognised layout falls back to the generic walk instead of showing an empty shootout.
    /// </summary>
    public static Rounds TryReadRounds(VisualElement el)
    {
        var home = new List<string>();
        var away = new List<string>();
        bool found = false;

        for (int i = 0; i < el.hierarchy.childCount; i++)
        {
            var frame = el.hierarchy[i];
            var name = frame.name ?? "";
            List<string> target = name == "home-frame" ? home : name == "away-frame" ? away : null;
            if (target == null) continue;

            var list = FindDescendantNamed(frame, ResultsListName);
            if (list == null) continue;
            var view = FindDescendantNamed(list, ResultsViewName);
            if (view == null) continue;

            found = true;
            for (int r = 0; r < view.hierarchy.childCount; r++)
                target.Add(ReadResult(view.hierarchy[r]));
        }

        return found ? new Rounds { Home = home, Away = away } : null;
    }

    /// <summary>
    /// The scoreboard block that sits beside <paramref name="indicators"/>, so the table can take its
    /// column headers from the same card the results came from rather than from walk-order state.
    /// Null when the card has no scoreboard, leaving the columns unheaded rather than mislabelled.
    /// </summary>
    public static Scoreboard TryReadTeams(VisualElement indicators)
    {
        var parent = indicators.hierarchy.parent;
        if (parent == null) return null;
        for (int i = 0; i < parent.hierarchy.childCount; i++)
        {
            var sibling = parent.hierarchy[i];
            if ((sibling.name ?? "") == ScoreboardName) return TryReadScoreboard(sibling);
        }
        return null;
    }

    /// <summary>The table rows: one per round, each team's outcome in its own column. Blank for a
    /// round a team has not taken yet.</summary>
    public static List<MatchStatRow> BuildRows(Rounds rounds)
    {
        int count = RegulationRounds;
        if (rounds.Home.Count > count) count = rounds.Home.Count;
        if (rounds.Away.Count > count) count = rounds.Away.Count;

        var rows = new List<MatchStatRow>(count);
        for (int i = 0; i < count; i++)
        {
            string home = i < rounds.Home.Count ? rounds.Home[i] : "";
            string away = i < rounds.Away.Count ? rounds.Away[i] : "";
            rows.Add(new MatchStatRow((i + 1).ToString(), home, away));
        }
        return rows;
    }

    // One result entry: a switch element whose single visible Icon carries the outcome as its
    // background sprite. Unknown or unreadable sprites degrade rather than defaulting to an outcome —
    // reporting a missed penalty as scored would be a lie the player cannot check.
    private static string ReadResult(VisualElement entry)
    {
        var icon = FindDescendantNamed(entry, IconName);
        var sprite = icon != null ? Adapters.SpriteHelper.ReadSpriteName(icon) : null;
        if (string.IsNullOrEmpty(sprite))
        {
            return Degrade.Read("[PenaltyShootoutReader] penalty result icon has no sprite");
        }

        // The card draws exactly two outcomes: a success tick, and one shared "close" cross for every
        // way a penalty fails (saved, off target, off the post). It does not distinguish them, so
        // neither does this — "Missed" is everything the game itself states.
        if (sprite.Contains("tick") || sprite.Contains("success")) return Scored;
        if (sprite.Contains("close") || sprite.Contains("cross") || sprite.Contains("error") || sprite.Contains("fail")) return Missed;

        return Degrade.Read($"[PenaltyShootoutReader] unrecognised penalty result sprite \"{sprite}\"");
    }

    private static void CollectTexts(VisualElement root, List<string> into)
    {
        var text = root.TryCast<TextElement>();
        if (text != null)
        {
            var s = TextFilters.CleanForDisplay(text.text)?.Trim();
            if (!string.IsNullOrEmpty(s)) into.Add(s);
        }
        for (int i = 0; i < root.hierarchy.childCount; i++)
            CollectTexts(root.hierarchy[i], into);
    }

    private static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        if ((root.name ?? "") == name) return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var hit = FindDescendantNamed(root.hierarchy[i], name);
            if (hit != null) return hit;
        }
        return null;
    }
}
