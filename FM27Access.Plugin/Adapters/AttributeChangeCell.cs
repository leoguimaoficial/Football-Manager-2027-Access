using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// The "Change this month" cell of one attribute row: a read-only view over the row that reports its
/// progression arrow rather than its name and value. The HTML surface renders the row twice — the
/// stat cell and this one — and the live-value channel needs a separate source per cell, since each
/// carries different text and must patch independently.
///
/// Backed by the row's own element so the value channel can tell a recycled row from an unreadable
/// one and drop it quietly, the same as every other tracked cell on the panel.
/// </summary>
internal sealed class AttributeChangeCell : PluginAdapterBase
{
    private readonly AttributeRowAdapter _row;

    internal AttributeChangeCell(AttributeRowAdapter row) : base(row.BackingElement)
    {
        _row = row;
    }

    public override string Role => "text";
    public override bool CanActivate => false;

    // The cell is plain text inside a table; a right-click menu resolved from the row's ancestry
    // would belong to the row, not to this projection of it.
    public override bool HasContextMenu => false;

    public override string GetLabel() => _row.GetChange();
    public override string GetState() => "";

    // The base composition would turn an unchanged row's empty label into the bare role word
    // ("text"). The cell's visible text IS the change wording, and nothing when there is none.
    public override string GetAnnouncement() => GetLabel();

    public override void Activate()
        => throw new System.NotSupportedException("The attribute change cell is read-only.");
}
