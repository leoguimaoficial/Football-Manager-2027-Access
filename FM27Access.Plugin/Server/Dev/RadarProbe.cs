using System;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for FM.UI.FMPolarChart, the radar on the Data Hub's OnThePitchPolygonGraphCard.
/// Only the first series' figures are text (each axis's Value/Label pair); the comparison series
/// (the league average) is drawn as a polygon alone. This dumps where the chart takes its series
/// from: each DataSeries config's binding path, the binding nodes under the chart's own
/// BindingRoot and its ancestors, and each axis label's bound datapoint, to find a typed read of
/// the comparison figures.
/// GET /radarprobe. Read-only.
/// </summary>
internal static class RadarProbe
{
    internal static string Run()
    {
        var sb = new StringBuilder();
        int found = 0;
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            int budget = 0;
            Walk(root, sb, ref found, ref budget);
        }
        return found > 0 ? sb.ToString() : "no FMPolarChart on the rendered panels\n";
    }

    private static void Walk(VisualElement el, StringBuilder sb, ref int found, ref int budget)
    {
        if (budget++ > 20000) return;
        if (el.GetIl2CppType().Name == "FMSpiderChart")
        {
            found++;
            sb.Append("========== RADAR ").Append(found).Append(" ==========\n");
            try { Dump(el, sb); }
            catch (Exception e) { sb.Append("dump threw ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n'); }
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            Walk(el.hierarchy[i], sb, ref found, ref budget);
    }

    private static void Dump(VisualElement el, StringBuilder sb)
    {
        var chart = el.Cast<FM.UI.FMSpiderChart>();
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();

        // The production reader's result against the figures the chart prints, axis by axis.
        var printed = new System.Collections.Generic.Dictionary<string, string>();
        CollectPrinted(el, printed, 0);
        var axes = RadarSeriesReader.TryRead(el);
        sb.Append("reader: ").Append(axes == null ? "null" : axes.Count + " axes").Append('\n');
        if (axes != null)
            for (int i = 0; i < axes.Count; i++)
            {
                var a = axes[i];
                sb.Append("  axis ").Append(i).Append(" \"").Append(a.Label).Append("\" printed=")
                  .Append(printed.TryGetValue(a.Label, out var p) ? p : "(none)").Append(" series=");
                for (int s = 0; s < a.Series.Length; s++)
                    sb.Append(s > 0 ? ", " : "").Append(a.Series[s]?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "null");
                sb.Append('\n');
            }

        // How each leaf value presents: AsString vs the inner object's type and ToString.
        if (el.hierarchy.childCount > 0)
        {
            var r = el.hierarchy[0].Cast<SI.Bindable.BindingRoot>();
            var rk = r.Key;
            if (bindings.TryGetNode(ref rk, out var n))
                foreach (var axisNode in n.Children)
                {
                    var leaves = new System.Collections.Generic.List<SI.Bindable.IReadOnlyNode>();
                    foreach (var c in axisNode.Children)
                    {
                        leaves.Add(c);
                        foreach (var g in c.Children) leaves.Add(g);
                    }
                    foreach (var c in leaves)
                    {
                        var ck = c.Key;
                        var tv = bindings.Get(ref ck);
                        sb.Append("  leaf ").Append(axisNode.Name).Append('/').Append(c.Name).Append(": ");
                        if (tv == null) { sb.Append("null\n"); continue; }
                        sb.Append("tvType=").Append(tv.GetIl2CppType().Name);
                        string s = null;
                        try { s = tv.AsString(); } catch (Exception e) { s = "threw " + e.GetType().Name; }
                        sb.Append(" AsString=\"").Append(s).Append('"');
                        var inner = tv.Get();
                        sb.Append(" inner=").Append(inner == null ? "null" : inner.GetIl2CppType().Name + " \"" + inner.ToString() + "\"");
                        if (tv.GetIl2CppType().Name == "NumericTypedValue" && inner != null)
                        {
                            try
                            {
                                var dn = inner.Unbox<SI.Core.DynamicNumber>();
                                sb.Append(" kind=").Append(dn.Kind).Append(" double=")
                                  .Append(dn.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                            }
                            catch (Exception e) { sb.Append(" unbox threw ").Append(e.GetType().Name).Append(": ").Append(e.Message); }
                        }
                        sb.Append('\n');
                    }
                    break;
                }
        }

        var configs = chart.m_dataSeriesList;
        sb.Append("series configs: ").Append(configs == null ? "null" : configs.Count.ToString()).Append('\n');
        if (configs != null)
            for (int i = 0; i < configs.Count; i++)
                sb.Append("  series ").Append(i).Append(' ').Append(configs[i].ToString()).Append('\n');

        // The chart's own BindingRoot is its first hierarchy child; the axis labels are bound
        // under it. Then every BindingRoot ancestor, nearest first, for the context the series
        // binding paths resolve against.
        sb.Append("-- chart BindingRoot child:\n");
        if (el.hierarchy.childCount > 0)
            DumpRoot(el.hierarchy[0], bindings, sb);

        sb.Append("-- ancestor BindingRoots (nearest first):\n");
        var up = el.hierarchy.parent;
        int roots = 0;
        while (up != null && roots < 4)
        {
            if (up.TryCast<SI.Bindable.BindingRoot>() != null)
            {
                roots++;
                sb.Append("  [").Append(roots).Append("] ").Append(up.GetIl2CppType().Name)
                  .Append(" name=\"").Append(up.name).Append("\"\n");
                DumpRoot(up, bindings, sb);
            }
            up = up.hierarchy.parent;
        }
    }

    // Each axis template's printed Label -> Value text.
    private static void CollectPrinted(VisualElement el, System.Collections.Generic.Dictionary<string, string> printed, int depth)
    {
        if (depth > 12) return;
        if (el.GetIl2CppType().Name == "TemplateContainer")
        {
            string label = null, value = null;
            FindTexts(el, ref label, ref value, 0);
            if (label != null) printed[label] = value ?? "(no value)";
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectPrinted(el.hierarchy[i], printed, depth + 1);
    }

    private static void FindTexts(VisualElement el, ref string label, ref string value, int depth)
    {
        if (depth > 4) return;
        var te = el.TryCast<TextElement>();
        if (te != null)
        {
            if (el.name == "Label") label = te.text;
            else if (el.name == "Value") value = te.text;
            return;
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            FindTexts(el.hierarchy[i], ref label, ref value, depth + 1);
    }

    private static void DumpRoot(VisualElement el, SI.Bindable.BindingSubsystem bindings, StringBuilder sb)
    {
        var root = el.TryCast<SI.Bindable.BindingRoot>();
        if (root == null) { sb.Append("    (not a BindingRoot: ").Append(el.GetIl2CppType().Name).Append(")\n"); return; }
        var key = root.Key;
        if (!bindings.TryGetNode(ref key, out var node)) { sb.Append("    no node\n"); return; }
        sb.Append("    node \"").Append(node.Name).Append("\" children=").Append(node.ChildCount).Append(": ");
        FormChartProbe.DescribeChildren(bindings, node, sb, 0);
        sb.Append('\n');
    }
}
