using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// One session slot in the create-schedule editor's week grid. It looks like a calendar training tile
/// — same <c>FMCalendarEvent</c> template — but it behaves like the opposite of one, so it must not
/// use <see cref="CalendarEventAdapter"/>.
///
/// A tile on the real calendar carries no click command; its interaction is the right-click menu of the
/// entity behind it, which is why CalendarEventAdapter only follows embedded links and otherwise
/// records an honest gap. A tile here is the reverse (verified live via /eval): its
/// <c>RightClickEvents</c> is null and no menu description ever binds, while it DOES carry a bound
/// clickable and one <c>OpenPanelEventContext</c> — left-clicking it opens the session picker. There is
/// no entity behind it at all; the editor is authoring a template, not scheduling a dated session.
///
/// So: activation is the plain button click the base already performs, and the context menu is declared
/// absent. The mark would otherwise be inferred from the <c>FMCalendarEvent</c> ancestor, which
/// <see cref="ContextMenuOpener.IsSelfContainedEntityHolder"/> treats as always menu-bearing — true on
/// the calendar, false here. Advertising a menu that can never open sends the player to a dead gesture
/// and hides the live one.
/// </summary>
internal sealed class ScheduleSessionAdapter : ButtonAdapter
{
    public ScheduleSessionAdapter(VisualElement element) : base(element) { }

    public override bool HasContextMenu => false;
}
