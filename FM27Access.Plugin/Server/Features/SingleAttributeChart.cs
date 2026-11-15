using System.Collections.Generic;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Features;

/// <summary>
/// Keeps the progress report's attribute graph to exactly one attribute.
///
/// The graph draws one unnamed line per ticked attribute, and nothing on a line says which
/// attribute it is: <c>SILineChart.SeriesData</c> carries only a colour index (which repeats), the
/// points carry no attribute id, and the series order is neither the table's render order nor
/// ascending attribute id — both were tested live against a nineteen-line chart and both failed.
/// Lines can only be matched to rows by the numbers they plot, which leaves attributes on the same
/// rating with differing histories permanently indistinguishable.
///
/// One line has no such problem: it is unambiguously the one attribute that is ticked. So on this
/// panel the mod reduces the selection to a single attribute, and the table above becomes that
/// attribute's history under its own name. The tick boxes below are rendered as radio buttons to
/// match, because that is now what they behave like.
///
/// The game re-seeds the selection to its role key attributes every time the Attributes tab is
/// entered (verified: untick to one, switch tabs and back, seventeen are ticked again), so this
/// runs on every compose that finds more than one ticked rather than once per panel. It runs in
/// the streamed phase, on the request thread between frames — the same place the table loads run —
/// so the first page the player receives is already reduced, with no multi-column flash.
///
/// Panel-scoped by design: this deliberately overwrites a game setting, which is only the right
/// trade where the alternative is a graph the player cannot read at all.
/// </summary>
internal static class SingleAttributeChart
{
    // The only panel this applies to. Matched on the PanelID name, never a title.
    internal const string PanelName = "PlayerReportDevelopmentProgressReportCard";

    // A stuck reduction must not spin the request thread forever; each untick costs ~140ms and a
    // full attribute set is under forty rows.
    private const int MaxUnticks = 60;

    // Shared by every attribute row on the panel so the four tables read as one set of choices.
    private const string RadioGroupName = "fm-progress-attribute";

    // The attribute currently charted. A UI preference, not game state: it is re-checked against
    // the live ticked set on every reduction and ignored the moment it stops being ticked, so it
    // can never put a name on a line the game is not drawing.
    private static string _kept;

    /// <summary>
    /// Which of the ticked attributes survives the reduction.
    ///
    /// Two ticked with one of them the attribute already charted means the player just picked the
    /// other, so the new pick wins — otherwise choosing an attribute would silently undo itself.
    /// More than two is the game re-seeding its role key attributes on entry to the tab; if the
    /// player's attribute is among them it is kept, so a choice survives leaving and returning.
    /// Failing both, the first in render order, which is at least predictable.
    /// </summary>
    private static int ChooseSurvivor(List<TickedCell> ticked)
    {
        if (string.IsNullOrEmpty(_kept)) return 0;

        int keptIndex = -1;
        for (int i = 0; i < ticked.Count; i++)
            if (string.Equals(ticked[i].Name, _kept, System.StringComparison.Ordinal)) { keptIndex = i; break; }
        if (keptIndex < 0) return 0;

        if (ticked.Count == 2) return keptIndex == 0 ? 1 : 0;
        return keptIndex;
    }

    internal static bool AppliesTo(object panel)
        => panel is VisualElement el && el.name == PanelName;

    /// <summary>
    /// Reduces this panel's attribute selection to one. Returns true when it unticked anything, so
    /// the caller knows the loaded rows and the chart it walked are now stale and must be re-read.
    /// Runs on the request thread between frames; never on the main thread.
    /// </summary>
    internal static bool Reduce(List<ScreenWalker.RenderItem> items)
    {
        var ticked = TickedCells(items);
        // Zero is the game between binds, one is the goal; neither needs anything done.
        if (ticked.Count <= 1)
        {
            if (ticked.Count == 1) _kept = ticked[0].Name;
            return false;
        }

        int keep = ChooseSurvivor(ticked);
        _kept = ticked[keep].Name;

        int unticked = 0;
        for (int i = 0; i < ticked.Count && unticked < MaxUnticks; i++)
        {
            if (i == keep) continue;
            var handle = new ServerHandles.CellHandle
            {
                Table = ticked[i].Table,
                Row = ticked[i].DataIndex,
                Col = ticked[i].Column,
                ElementIndex = ticked[i].ElementIndex,
            };
            string result = handle.Activate();
            // A cell that will not untick is worth a line in the log, but the rest of the reduction
            // still runs: one stubborn row leaves two lines, which the caller's matcher handles.
            if (!string.IsNullOrEmpty(result) && result.StartsWith("That "))
                Degrade.Note($"Single-attribute reduction could not untick row {ticked[i].DataIndex}: {result}");
            unticked++;
        }

        if (unticked > 0)
            Log.Debug($"[SingleAttributeChart] Reduced attribute selection by {unticked} to keep one line.");
        return unticked > 0;
    }

    /// <summary>
    /// Re-reads each chart's plotted lines after a reduction. The walk read them before the
    /// unticking, so the item still holds every line the graph used to draw. Main thread.
    /// </summary>
    internal static void RereadCharts(List<ScreenWalker.RenderItem> items)
    {
        foreach (var item in items)
        {
            if (item is not ScreenWalker.ValueChartItem chart || chart.Chart == null) continue;

            List<List<FinanceChartReader.Point>> fresh = null;
            try { fresh = FinanceChartReader.TryReadAll(chart.Chart); }
            catch (System.Exception ex)
            {
                Log.Warn($"[SingleAttributeChart] Chart re-read failed: {ex.Message}");
            }
            // A failed re-read leaves the pre-reduction lines in place, and the naming pass then
            // finds more lines than ticked rows and declines. That is the honest outcome: a marker,
            // never a table built from what the graph was showing a moment ago.
            if (fresh != null && fresh.Count > 0) chart.Series = fresh;
        }
    }

    /// <summary>
    /// Renders this panel's selection tick boxes as radio buttons. With the selection reduced to
    /// one they are single-choice in behaviour, and a checkbox would promise the player they can
    /// tick a second. Main thread (called from the post-load pass).
    /// </summary>
    internal static void MarkRadios(List<ScreenWalker.RenderItem> items)
    {
        foreach (var item in items)
        {
            if (item is not ScreenWalker.TableItem t || t.Rows == null) continue;
            foreach (var row in t.Rows)
            {
                if (row.Cells == null) continue;
                foreach (var cell in row.Cells)
                {
                    if (cell.Elements == null) continue;
                    for (int e = 0; e < cell.Elements.Count; e++)
                    {
                        var element = cell.Elements[e];
                        if (!element.IsToggle || element.IsRadio) continue;
                        element.IsRadio = true;
                        // One group across all four attribute tables: the choice is one attribute
                        // from the whole set, not one per group.
                        element.RadioGroup = RadioGroupName;
                        cell.Elements[e] = element;
                    }
                }
            }
        }
    }

    // Every ticked selection cell across the panel's attribute tables, in render order.
    private static List<TickedCell> TickedCells(List<ScreenWalker.RenderItem> items)
    {
        var found = new List<TickedCell>();
        foreach (var item in items)
        {
            if (item is not ScreenWalker.TableItem t || t.Rows == null || t.Table == null) continue;
            foreach (var row in t.Rows)
            {
                if (row.Cells == null) continue;
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    var cell = row.Cells[c];
                    if (cell.Elements == null) continue;
                    for (int e = 0; e < cell.Elements.Count; e++)
                    {
                        if (!cell.Elements[e].IsToggle || !cell.Elements[e].IsChecked) continue;
                        found.Add(new TickedCell(t.Table, row.DataIndex, c, e, RowName(row)));
                    }
                }
            }
        }
        return found;
    }

    // The attribute a row is for: the first non-toggle text in it, which is the name cell.
    private static string RowName(Contexts.TableRow row)
    {
        foreach (var cell in row.Cells)
        {
            if (cell.Elements == null) continue;
            foreach (var e in cell.Elements)
                if (!e.IsToggle && !string.IsNullOrWhiteSpace(e.Text)) return e.Text.Trim();
        }
        return null;
    }

    private readonly struct TickedCell
    {
        public readonly VisualElement Table;
        public readonly int DataIndex;
        public readonly int Column;
        public readonly int ElementIndex;
        public readonly string Name;

        public TickedCell(VisualElement table, int dataIndex, int column, int elementIndex, string name)
        {
            Table = table;
            DataIndex = dataIndex;
            Column = column;
            ElementIndex = elementIndex;
            Name = name;
        }
    }
}
