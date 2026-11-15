using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for line charts on the active panel. The finance cards (Summary, Income,
/// Expenditure, Projections, the budget cards) plot their history as FMLineChart widgets with no
/// text nodes; this dumps each chart's series and data-point property names/values (resolved via
/// the game's PropertyIdentifierSet, the same read LeagueHistoryChartReader does) to establish
/// whether the plotted data is reachable for a semantic-table rendering.
/// GET /chartprobe. Read-only.
/// </summary>
internal static class ChartProbe
{
    internal static string Run()
    {
        // The panels the page renders, not NavigationManager.GetActivePanel(). Those disagree:
        // a loading-layer ProcessingCard can hold "active panel" while the card the player is
        // actually reading walks out into /body, and the probe then reported no charts on a
        // screen whose chart the page was rendering.
        var panels = InteractivePanels.Current();

        var sb = new StringBuilder();
        int found = 0;
        var walked = new StringBuilder();
        foreach (var pv in panels)
        {
            if (pv.Panel is not VisualElement root) continue;
            int budget = 0;
            Walk(root, sb, ref found, ref budget);
            if (walked.Length > 0) walked.Append(", ");
            walked.Append(pv.Panel.name).Append('(').Append(budget).Append(')');
        }
        return found > 0
            ? sb.ToString()
            : $"no line charts on the rendered panels: {walked}\n";
    }

    private static void Walk(VisualElement el, StringBuilder sb, ref int found, ref int budget)
    {
        if (budget++ > 20000) return;
        string type = null;
        try { type = el.GetIl2CppType().Name; } catch { return; }

        if (type == "FMLineChart" || type == "SILineChart")
        {
            found++;
            sb.Append("========== CHART ").Append(found)
              .Append(" (parent=").Append(el.hierarchy.parent?.name ?? "").Append(") ==========\n");
            DumpChart(el, sb);
            return;
        }

        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            Walk(el.hierarchy[i], sb, ref found, ref budget);
    }

    private static void DumpChart(VisualElement chartEl, StringBuilder sb)
    {
        var chart = chartEl.TryCast<SI.Bindable.Charts.SILineChart>();
        if (chart == null) { sb.Append("  TryCast<SILineChart> failed\n"); return; }

        var series = chart.Series;
        if (series == null) { sb.Append("  Series null\n"); return; }
        sb.Append("  series count=").Append(series.Count).Append('\n');

        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        for (int s = 0; s < series.Count; s++)
        {
            var points = series[s].DataPoints;
            sb.Append("  -- series ").Append(s)
              .Append(" colourIndex=").Append(series[s].StyleColourIndex)
              .Append(" yMax=").Append(series[s].YAxisMaximum)
              .Append(" points=").Append(points == null ? "null" : points.Count.ToString());
            // The last point is the current value: the number the attribute table shows for
            // whichever attribute this series plots. The join between the two is what a
            // multi-column rendering needs.
            if (points != null && points.Count > 0)
                sb.Append(" last=").Append(points[points.Count - 1].Value);
            // The whole plotted sequence: two lines sharing a final value may still be telling
            // different stories, and that is what decides whether they can be told apart.
            if (points != null)
            {
                sb.Append(" seq=");
                for (int i = 0; i < points.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(points[i].Value);
                }
            }
            sb.Append('\n');
            if (points == null) continue;

            int dump = points.Count < 2 ? points.Count : 2;
            for (int i = 0; i < dump; i++)
            {
                var p = points[i];
                sb.Append("     point ").Append(i)
                  .Append(" value=").Append(p.Value).Append(" label=\"").Append(p.Label).Append('"');
                var dr = p.DataRef;
                if (dr == null) { sb.Append(" DataRef=null\n"); continue; }
                sb.Append(" props: ");
                var en = dr.Keys.GetEnumerator();
                while (en.MoveNext())
                {
                    var key = en.Current;
                    string name;
                    try { name = reg.GetInfo((uint)key).Name; }
                    catch { name = "key" + key; }
                    string val;
                    try { val = dr[key]?.ToString() ?? "null"; }
                    catch { val = "(unreadable)"; }
                    sb.Append(name).Append('=').Append(val).Append(" | ");
                }
                sb.Append('\n');
            }
            if (points.Count > dump)
                sb.Append("     ... ").Append(points.Count - dump).Append(" more\n");
        }
    }
}
