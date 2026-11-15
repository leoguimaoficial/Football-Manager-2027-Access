using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The result banner atop the match reports (full-time, half-time, and a finished match's report
/// opened from the fixture list). It draws its facts as bare texts the sighted player tells apart by
/// position and icon: the competition, each club with its manager beneath, the score, then a strip
/// of date, stadium, a weather glyph with the conditions, and a star with the player of the match
/// and his rating chip. Walked plainly, that strip read "Calm", "Gazmend Canaj", "9.91" with nothing
/// saying the name was the player of the match or the number his rating.
///
/// This names each text by the slot it sits in (all verified live 2026-10-05 on
/// PastMatchTeamReport against the screen): HomeTeam/AwayTeam blocks hold "ClubName" and the
/// manager's unnamed text; "ClubText" is the competition; "IsMatch" the score; the strip's first
/// text the date, "Stadium" the ground, the text after the "WeatherIcon" image the conditions, and
/// the group holding a "ValidRating" chip the player of the match.
///
/// Every visible text is emitted, in drawing order, each live-tracked: a text in a slot this does
/// not recognise (a second leg's aggregate, a nation line) keeps its place as plain text rather
/// than vanishing or borrowing a label. The crest and weather images are not read: the club names
/// and the conditions text say what they show. HTML-only, like the report tiles beside it.
/// Declines (the caller walks the banner plainly) unless both clubs and the score were found.
/// </summary>
internal static class MatchResultHeader
{
    internal const string WrapperName = "MatchResult";

    // Mod-authored chrome: the banner's facts carry no labels of their own.
    private const string CompetitionLabel = "Competition";
    private const string HomeLabel = "Home";
    private const string AwayLabel = "Away";
    private const string ManagerSuffix = " manager";
    private const string ScoreLabel = "Score";
    private const string DateLabel = "Date";
    private const string StadiumLabel = "Stadium";
    private const string WeatherLabel = "Weather";
    private const string PlayerOfTheMatchLabel = "Player of the match";
    private const string RatingLabel = "Rating";

    internal static bool TryEmit(VisualElement wrapper, List<ScreenWalker.RenderItem> items)
    {
        var texts = new List<(VisualElement El, string Label)>();
        Collect(wrapper, null, texts);

        bool home = false, away = false, score = false;
        foreach (var (_, label) in texts)
        {
            if (label == HomeLabel) home = true;
            else if (label == AwayLabel) away = true;
            else if (label == ScoreLabel) score = true;
        }
        if (!home || !away || !score) return false;

        foreach (var (el, label) in texts)
        {
            var value = new TextAdapter(el);
            items.Add(label == null
                ? new ScreenWalker.StopItem { Element = value }
                : new ScreenWalker.LabelValueItem { Label = label, Value = value });
        }
        return true;
    }

    private static void Collect(VisualElement el, string side, List<(VisualElement, string)> into)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.name == "HomeTeam") side = HomeLabel;
        else if (el.name == "AwayTeam") side = AwayLabel;

        if (el.GetIl2CppType().Name == "SIText")
        {
            if (SyntheticTables.CleanOrNull(ScreenWalker.ReadFirstText(el)) != null)
                into.Add((el, LabelFor(el, side)));
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            Collect(el.hierarchy[i], side, into);
    }

    private static string LabelFor(VisualElement text, string side)
    {
        string name = text.name;
        var parent = text.hierarchy.parent;

        if (side != null)
        {
            if (name == "ClubName") return side;
            if (string.IsNullOrEmpty(name) && !HasAncestorNamed(text, "NationVisible", 4)) return side + ManagerSuffix;
            return null;
        }
        if (name == "ClubText") return CompetitionLabel;
        if (name == "IsMatch") return ScoreLabel;
        if (name == "Stadium") return StadiumLabel;
        if (name == "text" && HasAncestorNamed(text, "ValidRating", 3)) return RatingLabel;

        int at = parent.hierarchy.IndexOf(text);
        var before = at > 0 ? parent.hierarchy[at - 1] : null;
        var after = at + 1 < parent.hierarchy.childCount ? parent.hierarchy[at + 1] : null;
        if (before != null && before.name == "WeatherIcon") return WeatherLabel;
        // The star glyph, the name, then the rating chip — all siblings in one group.
        if (after != null && SyntheticTables.FindDescendantNamed(after, "ValidRating", 4) != null) return PlayerOfTheMatchLabel;
        if (string.IsNullOrEmpty(name) && at == 0 && SyntheticTables.FindDescendantNamed(parent, "Stadium", 2) != null)
            return DateLabel;
        return null;
    }

    private static bool HasAncestorNamed(VisualElement el, string name, int levels)
    {
        var up = el.hierarchy.parent;
        for (int i = 0; i < levels && up != null; i++, up = up.hierarchy.parent)
            if (up.name == name) return true;
        return false;
    }
}
