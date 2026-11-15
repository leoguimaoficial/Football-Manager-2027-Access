using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// Pure HTML fragment builder for the accessibility server's tab bar. Game-free so the
/// tablist wrapper, per-tab aria-selected and escaping are unit-testable.
///
/// A ButtonGroup that sits inside a TabGroup is the speech surface's tab concept; on the
/// HTML surface it renders as an ARIA tablist of tab buttons. The screen reader reports
/// position ("2 of 4") and selection from the role/aria-selected, so no mod-authored
/// positional text is added. Each tab activates through act(id), the same path as any
/// other interactive stop, which re-renders the body with the new selection.
///
/// The renderer wraps this tablist in a labelled region landmark (HtmlRenderer.RenderTabBar —
/// kept in the reloadable Plugin): a tablist is not a landmark role, so without the wrapper a
/// screen reader's landmark navigation (D / Shift+D) can never jump to the tab row.
/// </summary>
public static class TabBarHtml
{
    /// <summary>
    /// One tab: its server-allocated handle id, display label, selected state, and whether the
    /// game has disabled it. A tab the game refuses (the Transfer Offer panel's Loan Offer tab
    /// when the board blocks loans) does nothing when clicked, so it renders disabled rather
    /// than as a live tab that silently no-ops.
    /// </summary>
    public readonly struct TabEntry
    {
        public readonly string Id;
        public readonly string Label;
        public readonly bool Selected;
        public readonly bool Disabled;

        public TabEntry(string id, string label, bool selected, bool disabled = false)
        {
            Id = id;
            Label = label;
            Selected = selected;
            Disabled = disabled;
        }
    }

    /// <param name="tabs">Tabs in order; labels are already display-cleaned, ids are safe handle ids.</param>
    public static string Build(IReadOnlyList<TabEntry> tabs)
    {
        var sb = new StringBuilder();
        sb.Append("<div role=\"tablist\">");
        foreach (var t in tabs)
        {
            sb.Append("<button type=\"button\" role=\"tab\" id=\"").Append(t.Id)
              .Append("\" aria-selected=\"").Append(t.Selected ? "true" : "false").Append('"');
            // aria-disabled as well as disabled: the native attribute stops the click, and the
            // ARIA state is what a screen reader announces when reading the tab in browse mode.
            if (t.Disabled)
                sb.Append(" disabled aria-disabled=\"true\"");
            else
                sb.Append(" onclick=\"act('").Append(t.Id).Append("')\"");
            sb.Append('>').Append(DropdownHtml.Escape(t.Label)).Append("</button>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }
}
