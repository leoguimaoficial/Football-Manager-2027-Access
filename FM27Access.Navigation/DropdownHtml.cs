using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FM27Access.Navigation;

/// <summary>
/// Pure HTML fragment builders for the accessibility server's dropdown widget.
/// Game-free so escaping, current-item marking, submenu vs leaf rendering and
/// truncation are unit-testable.
///
/// Fragments are sequences of option elements for a native select: the client
/// replaces a select's options on open and chains a new select per submenu level.
/// Option value is the absolute choice index; submenu options carry data-sub,
/// the capped-harvest continuation option carries data-more. The level path is
/// not embedded — the client derives child paths from the expand call it makes.
/// </summary>
public static class DropdownHtml
{
    // Unity rich-text markup that reaches us inside game strings (player names etc.).
    // Only known tags are stripped; anything else is escaped and shown literally.
    private static readonly Regex RichTextTags = new(
        @"</?(color|style|link|b|i|u|size|sprite|font|noparse)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripRichText(string s)
        => string.IsNullOrEmpty(s) ? s : RichTextTags.Replace(s, "");

    /// <summary>All options for one dropdown level.</summary>
    /// <param name="labels">Harvested labels; labels[i] is choice i.</param>
    /// <param name="totalCount">True choice count; a Load More option renders when labels.Count is short of it.</param>
    /// <param name="isChecked">
    /// Per-choice checkbox state for a multi-select toggle list, or null for an ordinary
    /// select-one dropdown. See <see cref="BuildItems"/> for how it is announced.
    /// </param>
    public static string BuildOptionsFragment(IReadOnlyList<string> labels,
        IReadOnlyList<bool> hasChildren, int selectedIndex, int totalCount,
        IReadOnlyList<bool> isChecked = null)
        => BuildOptionsFragment(labels, hasChildren, selectedIndex, totalCount, isChecked, null);

    public static string BuildOptionsFragment(IReadOnlyList<string> labels,
        IReadOnlyList<bool> hasChildren, int selectedIndex, int totalCount,
        IReadOnlyList<bool> isChecked, IReadOnlyList<bool> isDisabled)
        => BuildItems(labels, 0, hasChildren, selectedIndex, totalCount, isChecked, isDisabled);

    /// <summary>
    /// Continuation options for a capped harvest. <paramref name="labels"/> is the
    /// full harvested list; options render from <paramref name="startIndex"/> on,
    /// so values stay absolute.
    /// </summary>
    public static string BuildMoreItems(IReadOnlyList<string> labels,
        int startIndex, IReadOnlyList<bool> hasChildren, int selectedIndex, int totalCount,
        IReadOnlyList<bool> isChecked = null)
        => BuildMoreItems(labels, startIndex, hasChildren, selectedIndex, totalCount, isChecked, null);

    public static string BuildMoreItems(IReadOnlyList<string> labels,
        int startIndex, IReadOnlyList<bool> hasChildren, int selectedIndex, int totalCount,
        IReadOnlyList<bool> isChecked, IReadOnlyList<bool> isDisabled)
        => BuildItems(labels, startIndex, hasChildren, selectedIndex, totalCount, isChecked, isDisabled);

    /// <summary>
    /// The in-game player card's expanded panel: the root menu laid out inline. A root choice
    /// with a sublist is its own closed select box (Substitute, In/Out-of-possession role, ...)
    /// that lazily opens its submenu on interaction. A leaf (no sublist) is either a read-only
    /// status indicator (body language, condition) rendered as plain text, or an actionable
    /// command (the player's name opening the report, Player Instructions opening its page)
    /// rendered as a button that commits the root-level selection on click. The info-vs-action
    /// split comes from <paramref name="isInfoIndicator"/> (the game reports it structurally;
    /// the row text alone cannot tell them apart). <paramref name="cardId"/> ties each
    /// sub-select back to the owning card for teardown and panel refresh; <c>data-index</c> is
    /// the root choice index the sub-select expands.
    /// </summary>
    public static string BuildPlayerPanel(string cardId, IReadOnlyList<string> labels,
        IReadOnlyList<bool> hasChildren, IReadOnlyList<bool> isInfoIndicator,
        IReadOnlyList<bool> isDisabled = null)
    {
        var sb = new StringBuilder();
        string card = Escape(cardId);
        for (int i = 0; i < labels.Count; i++)
        {
            string label = Escape(Speech.TextFilters.CleanForDisplay(labels[i]));
            bool sub = hasChildren != null && i < hasChildren.Count && hasChildren[i];
            bool info = isInfoIndicator != null && i < isInfoIndicator.Count && isInfoIndicator[i];
            bool disabled = isDisabled != null && i < isDisabled.Count && isDisabled[i];
            if (sub && disabled)
            {
                // A greyed-out submenu (e.g. an individual shout on cooldown) renders as a disabled
                // select: no data-player-sub, so the client never opens it and the game's disabled
                // menu is never shown (which would NRE in BindChildRoot). The screen reader announces
                // it as unavailable.
                sb.Append("<p><select disabled aria-label=\"").Append(label).Append("\"><option selected>")
                  .Append(label).Append("</option></select></p>");
            }
            else if (sub)
            {
                sb.Append("<p><select data-dd=\"1\" data-player-sub=\"1\" data-card=\"")
                  .Append(card).Append("\" data-index=\"").Append(i)
                  .Append("\" aria-label=\"").Append(label).Append("\"><option selected>")
                  .Append(label).Append("</option></select></p>");
            }
            else if (info)
            {
                sb.Append("<p>").Append(label).Append("</p>");
            }
            else if (disabled)
            {
                sb.Append("<p><button type=\"button\" disabled>").Append(label).Append("</button></p>");
            }
            else
            {
                // An actionable leaf commits the root selection (index i, root path), which
                // opens the player report / instructions page and ends the menu session.
                sb.Append("<p><button type=\"button\" data-player-leaf=\"1\" onclick=\"fmpselect(")
                  .Append(i).Append(")\">").Append(label).Append("</button></p>");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Listbox rows for a searchable dropdown's filtered results (the ARIA editable-combobox
    /// pattern). Unlike the regular dropdown there is no submenu, current-item, or load-more
    /// concept: the list is whatever the game rendered for the current filter text, in order.
    /// <c>data-index</c> is the index into that filtered read, which is what
    /// <see cref="FM27Access.Plugin.Adapters.SearchableDropdownAdapter.ConfirmSelection"/> expects
    /// (NOT an absolute choice index) — exactly as the speech SearchDropdownHandler does. No DOM id
    /// is baked in: the client assigns each option's id on injection so aria-activedescendant can
    /// point at it, keeping this builder game- and id-agnostic.
    /// </summary>
    public static string BuildSearchListbox(IReadOnlyList<string> labels)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < labels.Count; i++)
        {
            string label = Escape(Speech.TextFilters.CleanForDisplay(labels[i]));
            sb.Append("<li role=\"option\" data-index=\"").Append(i).Append("\">").Append(label).Append("</li>");
        }
        return sb.ToString();
    }

    // A multi-select toggle list carries its state in each option's TEXT ("checked" / "unchecked"),
    // the same way a submenu row says ", submenu". A native <select> can mark only one option
    // selected, so it cannot express "two of these three are on" — and the alternative,
    // <select multiple>, would tell the screen reader that choosing REPLACES the selection set,
    // when in this game choosing toggles exactly one rule. The spoken state is therefore the honest
    // one: it says what each option currently is, and every option stays individually selectable.
    private static string BuildItems(IReadOnlyList<string> labels, int startIndex,
        IReadOnlyList<bool> hasChildren, int selectedIndex, int totalCount,
        IReadOnlyList<bool> isChecked, IReadOnlyList<bool> isDisabled)
    {
        var sb = new StringBuilder();
        for (int i = startIndex; i < labels.Count; i++)
        {
            string label = Escape(Speech.TextFilters.CleanForDisplay(labels[i]));
            bool sub = hasChildren != null && i < hasChildren.Count && hasChildren[i];
            bool toggle = isChecked != null && i < isChecked.Count;
            sb.Append("<option value=\"").Append(i).Append('"');
            if (isDisabled != null && i < isDisabled.Count && isDisabled[i]) sb.Append(" disabled");
            if (sub) sb.Append(" data-sub=\"1\"");
            // Marks the option as belonging to a toggle list, so /rendered and the dev driver can
            // tell a checkbox row apart from an ordinary choice that merely reads "unchecked".
            if (toggle) sb.Append(" data-toggle=\"1\"");
            // On a toggle list "selected" is meaningless (the game leaves SelectedIndex at -1) and
            // would read as a current value the control does not have.
            if (!toggle && i == selectedIndex) sb.Append(" selected");
            sb.Append('>').Append(label);
            if (sub) sb.Append(", submenu");
            if (toggle) sb.Append(isChecked[i] ? ", checked" : ", unchecked");
            sb.Append("</option>");
        }

        if (labels.Count < totalCount)
        {
            sb.Append("<option data-more=\"1\">Load more, showing ")
              .Append(labels.Count).Append(" of ").Append(totalCount).Append("</option>");
        }
        return sb.ToString();
    }

    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
