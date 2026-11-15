using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// A training-calendar session slot that carries no action, read as plain text rather than as a
/// control. The Training tab of the calendar draws every slot as an SIButton, including the ones the
/// game gives nothing to do: an empty slot (its game-supplied name reads "Not Available"), and every
/// slot on a day already simulated. Rendered as buttons those became a wall of "&lt;label&gt;, button,
/// unavailable" — the control semantics were pure noise, since there is no action behind them on
/// either surface.
///
/// Only slots with genuinely nothing behind them get this: no tile menu, no menu-bearing text leaf,
/// no embedded link (see <see cref="HasAction"/>). A slot that can still be edited keeps
/// <see cref="CalendarEventAdapter"/> and stays a button, so no action is ever hidden. The label is
/// still collected live from the tile's own texts, so the slot keeps its stop and reads exactly what
/// the sighted player sees — degrading the role, never the information.
///
/// Applied on both surfaces: <see cref="ElementBridge"/> picks it for the speech cursor's per-slot
/// stops, ScreenWalker for the HTML calendar's day cells.
/// </summary>
internal sealed class CalendarSlotTextAdapter : ButtonAdapter
{
    public CalendarSlotTextAdapter(VisualElement element) : base(element) { }

    public override string Role => "text";
    public override bool CanActivate => false;
    public override bool HasContextMenu => false;

    // The whole label reads as the text; a slot's second text is part of what is on screen, not a
    // description to hold back behind the on-demand read (which a text stop cannot reach anyway).
    protected override bool SplitsLabel => false;

    // "disabled" describes a control, and this is no longer presented as one. The day cell already
    // carries the day's own state ("already simulated"), so nothing true is lost.
    public override string GetState() => "";

    public override string GetAnnouncement() => GetLabel();

    public override void Activate() =>
        throw new System.NotSupportedException("An inert calendar slot cannot be activated.");

    // The calendar's Training tab, whose content grid the game names for the tab it belongs to (the
    // General and Fixtures tabs have their own …GridCompositionSet). Only that tab draws action-less
    // session slots as buttons, so only there are inert slots demoted to text.
    internal const string TrainingGridName = "CalendarToolTrainingGridCompositionSet";

    // A slot tile sits a fixed nine levels under its FMCalendarEvent (SIButton, style setter, MaxRes,
    // BindingExpect, TemplateContainer, DynamicItem, SIEnabled, BindingExpect, BindingRemapper). The
    // bound walk is the cheap gate that keeps every other button on every other screen out of the
    // work below — this runs for each button the classifier meets.
    private const int CalendarEventAncestorDepth = 10;

    /// <summary>
    /// True when this element is a training-calendar slot with nothing behind it, and so should read
    /// as text instead of as a button. False for every other element, and for any slot still carrying
    /// an action.
    /// </summary>
    internal static bool IsInertTrainingSlot(VisualElement element)
    {
        var cur = element.hierarchy.parent;
        for (int i = 0; i < CalendarEventAncestorDepth && cur != null; i++, cur = cur.hierarchy.parent)
            if (cur.GetIl2CppType().Name == "FMCalendarEvent")
                return IsUnderTrainingGrid(cur) && !HasAction(element);
        return false;
    }

    private static bool IsUnderTrainingGrid(VisualElement el)
    {
        for (var cur = el; cur != null; cur = cur.hierarchy.parent)
            if (cur.name == TrainingGridName) return true;
        return false;
    }

    /// <summary>
    /// True when a training slot has something behind it that the player can actually reach.
    ///
    /// Reachability comes first, because a menu alone proves nothing here: FMCalendarEvent is a
    /// self-contained entity holder (<see cref="ContextMenuOpener.IsSelfContainedEntityHolder"/>), so
    /// EVERY slot resolves a context menu through its ancestor — an empty "Not Available" slot as much
    /// as an editable session. What separates them is that the game greys the inert ones out, and a
    /// greyed tile takes neither a click nor a right-click on either surface: the HTML renderer strips
    /// its onclick and aria-haspopup, and the cursor's activation would no-op. That greyed-out state is
    /// exactly what the player hears as "unavailable", and it is the same signal the renderer marks
    /// disabled by (ServerText.IsDisabled), so the two can never disagree about which slots are inert.
    ///
    /// A reachable slot then still has to carry something: its own right-click menu, a menu-bearing
    /// text leaf, or an embedded link. Deliberately conservative — anything actionable stays a button,
    /// so this can never hide an action.
    /// </summary>
    internal static bool HasAction(VisualElement tile)
    {
        if (!tile.enabledInHierarchy) return false;
        return CalendarEventAdapter.HasBoundCommand(tile)
            || new CalendarEventAdapter(tile).HasContextMenu || HasActionableTextLeaf(tile);
    }

    private static bool HasActionableTextLeaf(VisualElement el)
    {
        if (el.GetIl2CppType().Name == "SIText" && ElementBridge.IsVisible(el))
        {
            var text = el.Cast<TextElement>().text;
            if (!string.IsNullOrEmpty(text)
                && (SITextLink.HasLink(text) || ContextMenuOpener.HasMenuManipulator(el)))
                return true;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            if (HasActionableTextLeaf(el.hierarchy[i])) return true;
        return false;
    }
}
