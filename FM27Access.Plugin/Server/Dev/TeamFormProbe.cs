using System;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the standings table's team "Form" column: each cell is a VisualElement named
/// <c>form</c> holding a StreamedObjectList of colour-only pips. Dumps, per cell, the list's model
/// (container node children with their values) and each realized pip's binding node plus its drawn
/// background colour, so the colour can be tied to the result it encodes.
/// GET /teamformprobe[?max=4]. Read-only.
/// </summary>
internal static class TeamFormProbe
{
    private static readonly System.Reflection.PropertyInfo ItemsContainerKeyProp =
        typeof(SI.Bindable.StreamedObjectList).GetProperty("m_itemsContainerKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance);

    internal static string Run(int max)
    {
        var sb = new StringBuilder();
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            var forms = new System.Collections.Generic.List<VisualElement>();
            int budget = 0;
            Collect(root, forms, ref budget);
            sb.Append("panel ").Append(pv.Panel.PanelID.name).Append(": ").Append(forms.Count).Append(" form cells\n");
            for (int i = 0; i < forms.Count && i < max; i++)
            {
                sb.Append("=== form ").Append(i).Append('\n');
                try { DumpCell(forms[i], sb); }
                catch (Exception e) { sb.Append("  threw ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n'); }
            }
        }
        return sb.Length > 0 ? sb.ToString() : "no panels\n";
    }

    private static void Collect(VisualElement el, System.Collections.Generic.List<VisualElement> into, ref int budget)
    {
        if (budget++ > 60000) return;
        if (el.name == "form") { into.Add(el); return; }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++) Collect(el.hierarchy[i], into, ref budget);
    }

    private static void DumpCell(VisualElement form, StringBuilder sb)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();

        // Ancestors' binding nodes (the row's), shallow, to see what the cell itself is bound to.
        var up = form.hierarchy.parent;
        for (int d = 0; d < 6 && up != null; d++, up = up.hierarchy.parent)
        {
            var br = up.TryCast<SI.Bindable.BindingRoot>();
            if (br == null) continue;
            var k = br.Key;
            if (!bindings.TryGetNode(ref k, out var n)) continue;
            sb.Append("  ancestor ").Append(d).Append(' ').Append(up.GetIl2CppType().Name).Append(": ");
            FormChartProbe.DescribeChildren(bindings, n, sb, 3);
            sb.Append('\n');
        }

        var listEl = Find(form, "StreamedObjectList");
        if (listEl == null) { sb.Append("  no StreamedObjectList\n"); return; }
        var typed = listEl.Cast<SI.Bindable.StreamedObjectList>();
        if (ItemsContainerKeyProp?.GetValue(typed) is SI.Bindable.Bindings.Key key
            && bindings.TryGetNode(ref key, out var container))
        {
            var ck = container.Key;
            sb.Append("  container self=").Append(FormChartProbe.DescribeValue(bindings.Get(ref ck))).Append('\n');
            foreach (var item in container.Children)
            {
                var ik = item.Key;
                sb.Append("  item name=").Append(item.Name).Append(" self=")
                  .Append(FormChartProbe.DescribeValue(bindings.Get(ref ik))).Append(" | ");
                FormChartProbe.DescribeChildren(bindings, item, sb, 0);
                sb.Append('\n');
            }
        }
        else sb.Append("  model: no container node\n");

        int pip = 0, budget = 0;
        DumpPips(form, bindings, sb, ref pip, ref budget);
    }

    private static void DumpPips(VisualElement el, SI.Bindable.BindingSubsystem bindings, StringBuilder sb, ref int pip, ref int budget)
    {
        if (budget++ > 2000) return;
        var root = el.TryCast<SI.Bindable.BindingRoot>();
        if (root != null && el.GetIl2CppType().Name == "AsyncBindingRoot")
        {
            sb.Append("  pip ").Append(pip++).Append(" colour=").Append(LeafColour(el)).Append(" classes=").Append(AddedClasses(el)).Append(" node: ");
            var k = root.Key;
            if (bindings.TryGetNode(ref k, out var n)) FormChartProbe.DescribeChildren(bindings, n, sb, 0);
            else sb.Append("(none)");
            sb.Append('\n');
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++) DumpPips(el.hierarchy[i], bindings, sb, ref pip, ref budget);
    }

    // The USS class each SIStyleSetter under the pip applied from its bound value.
    private static string AddedClasses(VisualElement el)
    {
        var sb = new StringBuilder();
        while (true)
        {
            var setter = el.TryCast<SI.Bindable.SIStyleSetter>();
            if (setter?.m_addedClasses != null)foreach(var value in setter.m_addedClasses)sb.Append('[').Append(value).Append(']');
            if (el.hierarchy.childCount == 0) break;
            el = el.hierarchy[0];
        }
        return sb.ToString();
    }

    private static string LeafColour(VisualElement el)
    {
        while (el.hierarchy.childCount > 0) el = el.hierarchy[0];
        return el.resolvedStyle.backgroundColor.ToString();
    }

    private static VisualElement Find(VisualElement el, string typeName)
    {
        if (el.GetIl2CppType().Name == typeName) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = Find(el.hierarchy[i], typeName);
            if (found != null) return found;
        }
        return null;
    }
}
