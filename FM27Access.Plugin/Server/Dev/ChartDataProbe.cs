using System;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only dump of the data every SIChart on the rendered panels is bound to: the chart type, its
/// ChartDataBinding path, and the bound value (a record list is listed record by record with every
/// property, via FormChartProbe.DescribeValue). The chart draws from exactly this value, so it is the
/// first place to look for figures a chart only draws. <c>?max=N</c> caps the records listed (20).
/// GET /chartdata. Read-only.
/// </summary>
internal static class ChartDataProbe
{
    internal static string Run(int max, string panel = null)
    {
        if (max <= 0) max = 20;
        var sb = new StringBuilder();
        int found = 0;
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            // ?panel=Name: dump that panel's own root binding instead of hunting charts.
            if (!string.IsNullOrEmpty(panel))
            {
                if (root.name != panel) continue;
                var roots = new System.Collections.Generic.List<VisualElement>();
                CollectRoots(root, roots, 0);
                var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
                foreach (var r in roots) DumpRoot(r, "panel", bindings, sb);
                return sb.Length > 0 ? sb.ToString() : "no BindingRoot near the top of " + panel + "\n";
            }
            int budget = 0;
            Walk(root, sb, ref found, ref budget, max);
            sb.Append("(walked ").Append(root.name).Append(": ").Append(budget).Append(" elements)\n");
        }
        return (found > 0 ? "" : "no SIChart on the rendered panels\n") + sb;
    }

    private static void Walk(VisualElement el, StringBuilder sb, ref int found, ref int budget, int max)
    {
        if (budget++ > 40000) return;
        var chart = el.TryCast<SI.Bindable.Charts.SIChart>();
        if (chart == null && (el.GetIl2CppType().Name.Contains("Chart") || el.GetIl2CppType().Name.Contains("PassMap") || IsFirstZone(el)))
        {
            sb.Append("(").Append(el.GetIl2CppType().FullName).Append(" does not cast to SIChart; its BindingRoot children:)\n");
            DumpChildRoots(el, sb);
        }
        if (chart != null)
        {
            found++;
            sb.Append("========== CHART ").Append(found).Append(' ').Append(el.GetIl2CppType().Name)
              .Append(" name=\"").Append(el.name).Append("\" ==========\n");
            try { Dump(chart, sb, max); }
            catch (Exception e) { sb.Append("dump threw ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n'); }
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            Walk(el.hierarchy[i], sb, ref found, ref budget, max);
    }

    // A chart whose interop wrapper is broken still binds its data the ordinary way: dump the binding
    // node of every BindingRoot directly under it (the FMPolarChart pattern).
    // A zone map has no chart element; its first "zone frame" stands in for it.
    private static bool IsFirstZone(VisualElement el)
        => el.name == "zone frame" && el.hierarchy.parent.hierarchy.IndexOf(el) == 0;

    private static void DumpChildRoots(VisualElement el, StringBuilder sb)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        var roots = new System.Collections.Generic.List<VisualElement>();
        CollectRoots(el, roots, 0);
        foreach (var r in roots)
            DumpRoot(r, "below", bindings, sb);

        // The chart's data binding resolves against its context: the nearest BindingRoot above it.
        var up = el.hierarchy.parent;
        while (up != null && up.TryCast<SI.Bindable.BindingRoot>() == null) up = up.hierarchy.parent;
        if (up != null) DumpRoot(up, "above", bindings, sb);
    }

    private static void CollectRoots(VisualElement el, System.Collections.Generic.List<VisualElement> into, int depth)
    {
        if (depth > 3) return;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var c = el.hierarchy[i];
            if (c.TryCast<SI.Bindable.BindingRoot>() != null) { into.Add(c); continue; }
            CollectRoots(c, into, depth + 1);
        }
    }

    private static void DumpRoot(VisualElement el, string where, SI.Bindable.BindingSubsystem bindings, StringBuilder sb)
    {
        var key = el.Cast<SI.Bindable.BindingRoot>().Key;
        if (!bindings.TryGetNode(ref key, out var node)) { sb.Append("  ").Append(where).Append(": no node\n"); return; }
        sb.Append("  ").Append(where).Append(" \"").Append(el.name).Append("\" node \"").Append(node.Name)
          .Append("\" children=").Append(node.ChildCount).Append(":\n");
        DumpNode(bindings, node, sb, 2, 0);
    }

    // Every child with its value; record lists are expanded record by record.
    private static void DumpNode(SI.Bindable.BindingSubsystem bindings, SI.Bindable.IReadOnlyNode node, StringBuilder sb, int indent, int depth)
    {
        if (depth > 4) return;
        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            SI.Core.TypedValue tv = null;
            try { tv = bindings.Get(ref childKey); } catch { }
            sb.Append(' ', indent).Append(child.Name).Append(" = ");
            var list = tv?.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
            if (list != null)
            {
                sb.Append("list of ").Append(list.Count).Append('\n');
                for (int i = 0; i < list.Count && i < 60; i++)
                    sb.Append(' ', indent + 4).Append('[').Append(i).Append("] ").Append(FormChartProbe.DescribeValue(list[i])).Append('\n');
            }
            else if (tv?.Get()?.TryCast<SI.Core.Record>() is SI.Core.Record rec)
            {
                sb.Append("record\n");
                DumpRecord(rec, sb, indent + 4, 0);
            }
            else
            {
                string v = tv == null ? "(null)" : FormChartProbe.DescribeValue(tv);
                sb.Append(v.Length > 300 ? v.Substring(0, 300) + "..." : v).Append('\n');
            }
            DumpNode(bindings, child, sb, indent + 2, depth + 1);
        }
    }

    // A record field by field, expanding record lists (the data a chart plots usually sits in one).
    private static void DumpRecord(SI.Core.Record rec, StringBuilder sb, int indent, int depth)
    {
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var en = rec.GetEnumerator();
        while (en.MoveNext())
        {
            var kv = en.Current;
            string name;
            try { name = reg.GetInfo(kv.Key).Name; } catch { name = "key" + kv.Key; }
            sb.Append(' ', indent).Append(name).Append(" = ");
            var list = kv.Value?.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();
            if (list != null && depth < 3)
            {
                sb.Append("list of ").Append(list.Count).Append('\n');
                for (int i = 0; i < list.Count && i < 80; i++)
                    sb.Append(' ', indent + 4).Append('[').Append(i).Append("] ").Append(FormChartProbe.DescribeValue(list[i])).Append('\n');
                continue;
            }
            string v = kv.Value == null ? "(null)" : FormChartProbe.DescribeValue(kv.Value);
            sb.Append(v.Length > 300 ? v.Substring(0, 300) + "..." : v).Append('\n');
        }
    }

    private static void Dump(SI.Bindable.Charts.SIChart chart, StringBuilder sb, int max)
    {
        // FM27 binds chart data per concrete chart instead of SIChart.ChartDataBinding.
        // Report the actual binding roots, as for the other native chart implementations.
        sb.Append("bound=").Append(chart.IsBound).Append('\n');
        DumpChildRoots(chart,sb);
    }
}
