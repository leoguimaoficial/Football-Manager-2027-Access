using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The training calendar's per-week focus card — the week's training schedule, drawn beside the week's
/// seven day cells on the Calendar panel's Training tab. It names the schedule ("Set Piece", "Defense")
/// and its category ("Tactical Style"), and activating it opens the game's Training Schedule picker.
///
/// The card is an ordinary <c>SIButton</c> carrying a single <c>OpenPanelEventContext</c> and a bound
/// clickable (verified live against the game's declarative event wiring), so
/// <see cref="ButtonAdapter.Activate"/> already drives it correctly — including the guard against
/// touching the element after the click tears the calendar down, which opening a panel does. Nothing
/// is overridden there. Note the click must come from code, never from the /eval REPL: it opens a
/// panel, and an eval closure still holding elements of the dismantled tree kills the process.
///
/// What this adds is a labelled read. The base collects whatever texts a button happens to contain, in
/// document order; here the two texts carry fixed meanings, so they are read by name instead. This
/// label becomes the row header of a whole week on the HTML surface, so reading the wrong text would
/// mislabel seven days.
///
/// Weeks that have already begun carry the same card and the same event, but the game disables them:
/// their schedule is settled. They are deliberately NOT demoted to text the way inert session slots are
/// (see <see cref="CalendarSlotTextAdapter"/>) — an inert slot has nothing behind it on either surface,
/// whereas a locked week's card is the real control in a locked state, and "disabled" is the true and
/// useful thing to say about it.
/// </summary>
internal sealed class CalendarWeekFocusAdapter : ButtonAdapter
{
    /// <summary>The game's element name for the card. Also the handle CalendarTree looks it up by.</summary>
    internal const string CardName = "TrainingFocusCard";

    private const string TitleName = "Title";

    // The game's own misspelling of "Description". Matched verbatim because it is the element name.
    private const string DescriptionName = "Desctiption";

    public CalendarWeekFocusAdapter(VisualElement card) : base(card) { }

    // The schedule's name ("Set Piece") leads; the category ("Tactical Style") follows as the
    // description, the same split a plain content button uses.
    //
    // Each falls back to the base rather than to a marker of its own. The texts are routinely empty
    // for a frame or two after the tab is opened — the card's bindings have not filled yet — and the
    // base's ladder ends at ButtonAdapter.UnlabeledIconButton, which the server already treats as a
    // provisional compose and re-reads (AccessibilityServer's provisional gate, SseStreamer's marker
    // hold). Degrading to a marker here instead would be honest but permanent: the player would be
    // left on "couldn't read" for a week that reads perfectly a frame later.
    public override string GetPrimaryLabel()
    {
        var title = TextNamed(TitleName);
        return string.IsNullOrEmpty(title) ? base.GetPrimaryLabel() : title;
    }

    public override string GetDescription()
    {
        var description = TextNamed(DescriptionName);
        return string.IsNullOrEmpty(description) ? base.GetDescription() : description;
    }

    public override string GetLabel()
    {
        var title = TextNamed(TitleName);
        if (string.IsNullOrEmpty(title)) return base.GetLabel();

        var description = TextNamed(DescriptionName);
        return string.IsNullOrEmpty(description) ? title : $"{title}, {description}";
    }

    /// <summary>
    /// The focus card of a week row, or null when the row has none (the General and Fixtures tabs
    /// build their week rows without one). Searched from the row down but never into the row's day
    /// cells, so a stray same-named element inside a day could not be mistaken for the week's card.
    /// </summary>
    internal static VisualElement Find(VisualElement weekRow) => FindOutsideDays(weekRow, 0);

    // The containers holding the week's seven day cells. The card is a sibling of these, so the
    // search stops at them rather than descending.
    private static bool IsDaysContainer(VisualElement el) => el.name == "Days" || el.name == "WeekDays";

    private static VisualElement FindOutsideDays(VisualElement root, int depth)
    {
        if (depth > 10) return null;
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == CardName) return child;
            if (IsDaysContainer(child)) continue;
            var found = FindOutsideDays(child, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    // A named text inside the card, or null when it is absent or unreadable. Null and empty are the
    // same answer to the caller: the text this card should have carried is not there.
    private string TextNamed(string name)
    {
        var el = FindNamed(Element, name, 0);
        if (el == null) return null;
        try { return el.TryCast<TextElement>()?.text; }
        catch { return null; }
    }

    private static VisualElement FindNamed(VisualElement root, string name, int depth)
    {
        if (depth > 8) return null;
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            var found = FindNamed(child, name, depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}
