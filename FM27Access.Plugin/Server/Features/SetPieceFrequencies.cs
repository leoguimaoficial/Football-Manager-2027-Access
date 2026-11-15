using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Contexts;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Routine Frequency popup (SetPieceRoutineFrequencyPopupCard) as an honest exclusive
/// control. The game draws each routine's More Often / Average / Less Often as three checkbox
/// cells, but its model holds ONE frequency per routine — SetPieceRoutineFrequencyEvent carries a
/// single value, and a rebuilt popup always shows exactly one tick per row — and the checkboxes
/// are write-only UI: clicking commits the clicked frequency, but the game never re-syncs the
/// row's other checkboxes until the popup is rebuilt, and un-checking commits nothing at all
/// (all verified live, 2026-09-17; see the /freqprobe dev probe). Read raw, a clicked row
/// accumulates ticks and can show two frequencies, or none — states the model cannot hold.
///
/// So each row's three cells render as one radio group, and the mod's activation keeps the
/// game's checkboxes in sync with the commit it just made: click the chosen checkbox (the game's
/// own commit path), then SetValueWithoutNotify(false) on the stale sibling tick — exactly the
/// state the game itself draws when it rebuilds the popup. A row found with anything other than
/// one tick (something outside this handle desynced it) renders unavailable markers instead of a
/// guess; reopening the popup restores truth.
/// </summary>
internal static class SetPieceFrequencies
{
    internal const string PanelName = "SetPieceRoutineFrequencyPopupCard";
    private const int RowSearchDepth = 60;

    /// <summary>Whole-table override, consulted by HtmlRenderer.RenderTable before the generic
    /// path (the SetPieceTakers precedent). False = shape not recognised; the generic table
    /// stands, so the popup never disappears.</summary>
    internal static bool TryRenderTable(StringBuilder sb, ScreenWalker.TableItem t)
    {
        if (t.Rows == null || t.Headers == null || t.Headers.Count != 4) return false;
        if (!ElementBridge.IsInPanel(t.Table, PanelName)) return false;
        foreach (var row in t.Rows)
        {
            if (row == null || row.Cells.Count != 4 || FirstText(row.Cells[0]) == null) return false;
            for (int c = 1; c <= 3; c++)
                if (row.Cells[c].Elements.Count != 1 || !row.Cells[c].Elements[0].IsToggle)
                    return false;
        }

        string tablePath = ServerHandles.IdentityPath(t.Table);
        uint groupSeed = (uint)tablePath.GetHashCode();
        sb.Append("<table><thead><tr>");
        for (int c = 0; c < 4; c++) HtmlRenderer.RenderHeader(sb, t.Headers[c], tablePath, c);
        sb.Append("</tr></thead><tbody>");
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var row = t.Rows[r];
            string routine = FirstText(row.Cells[0]);
            sb.Append("<tr><th scope=\"row\">");
            AppendCellTexts(sb, row.Cells[0]);
            sb.Append("</th>");

            int ticks = 0;
            for (int c = 1; c <= 3; c++)
                if (row.Cells[c].Elements[0].IsChecked) ticks++;
            if (ticks != 1)
                Degrade.Note($"set-piece frequency: a row shows {ticks} ticks (game visuals desynced); rendered unavailable, reopen the popup");

            for (int c = 1; c <= 3; c++)
            {
                sb.Append("<td>");
                var elem = row.Cells[c].Elements[0];
                string option = t.Headers[c].Text ?? "";
                string label = routine + ", " + option;
                if (ticks != 1)
                {
                    sb.Append(ServerText.Escape(Degrade.Unavailable));
                }
                else if (elem.IsDisabled)
                {
                    sb.Append("<input type=\"radio\" disabled").Append(elem.IsChecked ? " checked" : "")
                      .Append(" aria-label=\"").Append(ServerText.EscapeText(label)).Append("\">");
                }
                else
                {
                    string id = ServerHandles.RegisterKey(
                        new SetPieceFrequencyHandle
                        {
                            Table = t.Table,
                            Row = r,
                            Col = c - 1,
                            Routine = routine,
                            Option = option,
                        },
                        tablePath + "@freq" + r + "," + c);
                    sb.Append("<input type=\"radio\" id=\"").Append(id)
                      .Append("\" name=\"fmspfreq").Append(groupSeed).Append('-').Append(r).Append('"')
                      .Append(elem.IsChecked ? " checked" : "")
                      .Append(" aria-label=\"").Append(ServerText.EscapeText(label))
                      .Append("\" onclick=\"act('").Append(id).Append("')\">");
                }
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return true;
    }

    private static string FirstText(TableCell cell)
    {
        foreach (var e in cell.Elements)
            if (!string.IsNullOrWhiteSpace(e.Text)) return e.Text;
        return null;
    }

    private static void AppendCellTexts(StringBuilder sb, TableCell cell)
    {
        bool first = true;
        foreach (var e in cell.Elements)
        {
            if (string.IsNullOrWhiteSpace(e.Text)) continue;
            if (!first) sb.Append(' ');
            first = false;
            sb.Append(ServerText.EscapeText(e.Text));
        }
    }

    // The popup's row list, small and never virtualised out: document-order TableRowNavigatable.
    internal static List<VisualElement> CollectRows(VisualElement table)
    {
        var rows = new List<VisualElement>();
        CollectByType(table, "TableRowNavigatable", RowSearchDepth, rows);
        return rows;
    }

    internal static List<VisualElement> CollectCheckboxes(VisualElement row)
    {
        var checks = new List<VisualElement>();
        CollectByType(row, "SICheckBox", 12, checks);
        return checks;
    }

    private static void CollectByType(VisualElement el, string typeName, int depth, List<VisualElement> into)
    {
        if (depth < 0 || !ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name == typeName) { into.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectByType(el.hierarchy[i], typeName, depth - 1, into);
    }

    internal static string FirstRowText(VisualElement row)
    {
        var text = row.TryCast<TextElement>();
        if (text != null && !string.IsNullOrWhiteSpace(text.text))
            return ServerText.Clean(text.text);
        int n = row.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FirstRowText(row.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }
}

/// <summary>
/// Selects one frequency for one routine. Runs on the main thread via the standard /activate
/// dispatch. Resolves the row fresh by index and guards it against the routine name the radio
/// was rendered for, so a rebuilt or reordered popup refuses rather than setting the wrong row.
/// A successful reply starts with "Activated" (silent client-side — the radio's own state change
/// is the announcement); anything else is spoken as an error.
/// </summary>
internal sealed class SetPieceFrequencyHandle : ServerHandles.Handle
{
    public VisualElement Table;
    public int Row;
    public int Col;          // 0..2 in document order: the More Often / Average / Less Often cells
    public string Routine;   // the row's name at render time — an identity guard, never a matcher
    public string Option;    // the column header the player chose, for the reply

    public override bool TryValidate(out string error)
    {
        error = null;
        if (Table.panel == null || !ElementBridge.IsVisible(Table))
        {
            error = "That popup is no longer open. Refresh.";
            return false;
        }
        return true;
    }

    public override string Activate()
    {
        var rows = SetPieceFrequencies.CollectRows(Table);
        if (Row < 0 || Row >= rows.Count)
            return "That routine is no longer listed. Refresh.";
        var row = rows[Row];
        if (SetPieceFrequencies.FirstRowText(row) != ServerText.Clean(Routine))
            return "The routines changed. Refresh.";

        var checks = SetPieceFrequencies.CollectCheckboxes(row);
        if (checks.Count != 3)
        {
            Degrade.Note($"set-piece frequency: row holds {checks.Count} checkboxes, expected 3; refusing");
            return "The game did not expose that setting. Refresh.";
        }

        var target = checks[Col].Cast<SI.Bindable.SICheckBox>();
        if (target.value) return "Activated " + Option;   // already the game's selection: no-op

        // The game's own commit path: the checkbox click dispatches the frequency event. Clicking
        // an UNCHECKED box always sets its frequency; only un-checking commits nothing, and this
        // handle never un-checks.
        target.SimulateCheckboxClick();
        if (!target.value)
        {
            Degrade.Note("set-piece frequency: click did not tick the target checkbox");
            return "The game did not apply " + Option + ". Reopen the popup.";
        }

        // The game re-syncs sibling ticks only when the popup is rebuilt; mirror that state now so
        // the next read shows the one frequency the model holds.
        for (int i = 0; i < checks.Count; i++)
        {
            if (i == Col) continue;
            var sibling = checks[i].Cast<SI.Bindable.SICheckBox>();
            if (sibling.value) sibling.SetValueWithoutNotify(false);
        }
        return "Activated " + Option;
    }
}
