using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// A table-row hyperlink read as plain, inert text — role "text", not activatable, no context menu.
/// Used where the game styles a name as a rich-text <c>&lt;link&gt;</c> but wires no action to it:
/// the career-setup View Squad preview (<c>TeamSquadViewCard</c>), whose player-name links no-op on
/// the game's own click path and produce an empty context menu before the career starts (its
/// per-link <c>LinkHasActions</c> flag misreports true, so it cannot be trusted as the gate).
/// Presenting them as activatable buttons with menus would be a lie; this keeps the name readable
/// while telling the truth that it does nothing here. Every started-game squad list keeps the
/// interactive <see cref="TableRowTextAdapter"/>.
/// </summary>
internal sealed class InertLinkTextAdapter : TextAdapter
{
    public InertLinkTextAdapter(VisualElement element) : base(element) { }

    public override bool HasContextMenu => false;
}
