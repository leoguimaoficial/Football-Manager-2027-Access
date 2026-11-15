using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// The page controls rendered above and below a paged table: Previous / Next either side of the
/// current position. Pure string building so the markup — and above all the "only when there is
/// more than one page" rule — is unit-tested without the game.
///
/// No landmark wrapper: a screen can carry several tables, and a nav landmark per pager would
/// bury the page's real landmarks. The edge buttons render disabled rather than disappearing, so
/// the control row keeps its shape and the reader announces the edge instead of the player
/// discovering it by a button that silently went missing; the client moves focus off a button
/// that just became disabled (see FM.tablepager in app.js).
/// </summary>
public static class TablePagerHtml
{
    public const string PreviousLabel = "Previous page";
    public const string NextLabel = "Next page";

    /// <summary>
    /// The pager for <paramref name="page"/> (0-based) of <paramref name="pageCount"/>, or an
    /// empty string when the table fits on one page and the controls would be noise.
    /// </summary>
    public static string Build(string prevId, string nextId, int page, int pageCount)
    {
        if (pageCount <= 1) return "";
        var sb = new StringBuilder();
        // Three separate blocks, not one line: sharing a line, the position ran on from the button
        // before it ("Previous page, Page 3 of 9") and read as part of that button's label — as if
        // the button led to that page rather than the player already being on it. On its own block
        // it is announced as its own item, between the two buttons that move off it.
        sb.Append("<p>");
        Button(sb, prevId, nextId, PreviousLabel, page > 0);
        sb.Append("</p>");
        sb.Append("<p>").Append(TablePaging.Label(page, pageCount)).Append("</p>");
        sb.Append("<p>");
        Button(sb, nextId, prevId, NextLabel, page < pageCount - 1);
        sb.Append("</p>");
        return sb.ToString();
    }

    // otherId is where focus goes when this button is the one that just became disabled (pressing
    // Next onto the last page): without it the browser drops focus to the body and the player
    // loses their place in the table they were paging through.
    private static void Button(StringBuilder sb, string id, string otherId, string label, bool enabled)
    {
        sb.Append("<button type=\"button\" id=\"").Append(DropdownHtml.Escape(id)).Append('"');
        if (enabled)
            sb.Append(" onclick=\"FM.tablepager.go('").Append(DropdownHtml.Escape(id))
              .Append("','").Append(DropdownHtml.Escape(otherId)).Append("')\"");
        else
            sb.Append(" disabled");
        sb.Append('>').Append(label).Append("</button>");
    }
}
