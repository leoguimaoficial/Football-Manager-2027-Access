using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the progress report's attribute rows and the graph they feed. The chart plots
/// one unnamed line per ticked attribute and its series order is NOT the rendered table order, so
/// naming the lines needs a stable per-attribute identity that both sides expose. This dumps each
/// row's bound node — child names, typed values, and any DynamicReference properties — so we can
/// see whether such an identity exists, plus the binding nodes around the chart itself in case the
/// selected set is readable as an ordered collection.
/// GET /attrprobe. Read-only.
/// </summary>
internal static class AttributeRowProbe
{
    internal static string Run()
    {
        var panels = InteractivePanels.Current();
        var sb = new StringBuilder();
        foreach (var pv in panels)
        {
            if (pv.Panel is not VisualElement root) continue;
            int budget = 0;
            Walk(root, sb, ref budget, 0);
        }
        return sb.Length > 0 ? sb.ToString() : "no attribute tables or charts on the rendered panels\n";
    }

    private static void Walk(VisualElement el, StringBuilder sb, ref int budget, int depth)
    {
        if (el == null || budget++ > 30000) return;

        string type = null;
        try { type = el.GetIl2CppType().Name; } catch { return; }

        if (type == "TableRowNavigatable")
        {
            sb.Append("---------- ROW ----------\n");
            DumpRow(el, sb);
            return;
        }

        if (type == "FMLineChart" || type == "SILineChart")
        {
            sb.Append("========== CHART (parent=").Append(el.hierarchy.parent?.name ?? "").Append(") ==========\n");
            DumpAncestorBindings(el, sb);
            return;
        }

        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count; i++)
            Walk(el.hierarchy[i], sb, ref budget, depth + 1);
    }

    // The row's own text (so the dump is readable) plus every BindingRoot node inside it.
    private static void DumpRow(VisualElement row, StringBuilder sb)
    {
        var texts = new StringBuilder();
        CollectText(row, texts, 0);
        sb.Append("  text: ").Append(texts).Append('\n');

        // The row's BindingRoot is its PARENT (BindingRoot > TableRowNavigatable > Cell...), so the
        // row's own data is only reachable by walking up, not down.
        var el = row;
        for (int up = 0; up < 4 && el != null; up++)
        {
            SI.Bindable.BindingRoot r = null;
            try { r = el.TryCast<SI.Bindable.BindingRoot>(); } catch { }
            if (r != null)
            {
                sb.Append("  rowRoot at +").Append(up).Append('\n');
                DumpNode(r, sb, "    ");
                break;
            }
            try { el = el.hierarchy.parent; } catch { break; }
        }

        int budget = 0;
        DumpRoots(row, sb, ref budget, 0);
    }

    private static void CollectText(VisualElement el, StringBuilder sb, int depth)
    {
        if (el == null || depth > 14) return;
        // Hidden subtrees carry tooltip and template text that is not what the row shows.
        try { if (!el.visible || el.resolvedStyle.display == DisplayStyle.None) return; } catch { }
        var t = el.TryCast<SI.Bindable.SIText>();
        if (t != null)
        {
            string s = null;
            try { s = t.text; } catch { }
            if (!string.IsNullOrWhiteSpace(s)) sb.Append('[').Append(s).Append(']');
        }
        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count; i++) CollectText(el.hierarchy[i], sb, depth + 1);
    }

    private static void DumpRoots(VisualElement el, StringBuilder sb, ref int budget, int depth)
    {
        if (el == null || depth > 16 || budget++ > 3000) return;
        SI.Bindable.BindingRoot root = null;
        try { root = el.TryCast<SI.Bindable.BindingRoot>(); } catch { }
        if (root != null) DumpNode(root, sb, "  ");

        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count; i++) DumpRoots(el.hierarchy[i], sb, ref budget, depth + 1);
    }

    // Every BindingRoot from the chart up to the panel: if the selected attributes exist anywhere as
    // an ordered bound collection, it is on one of these.
    private static void DumpAncestorBindings(VisualElement chart, StringBuilder sb)
    {
        var el = chart;
        for (int up = 0; up < 12 && el != null; up++)
        {
            SI.Bindable.BindingRoot root = null;
            try { root = el.TryCast<SI.Bindable.BindingRoot>(); } catch { }
            if (root != null)
            {
                sb.Append("  ancestor+").Append(up).Append(" name=\"").Append(el.name ?? "").Append("\"\n");
                DumpNode(root, sb, "    ");
            }
            try { el = el.hierarchy.parent; } catch { break; }
        }
    }

    private static void DumpNode(SI.Bindable.BindingRoot root, StringBuilder sb, string indent)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) { sb.Append(indent).Append("no BindingSubsystem\n"); return; }

        var rootKey = root.Key;
        if (!bindings.TryGetNode(ref rootKey, out var node)) return;

        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            SI.Core.TypedValue value = null;
            try { value = bindings.Get(ref childKey); }
            catch (System.Exception e) { sb.Append(indent).Append("get failed: ").Append(e.GetType().Name).Append('\n'); }

            sb.Append(indent).Append("child \"").Append(child.Name).Append("\" = ")
              .Append(value == null ? "null" : value.ToString()).Append('\n');
            DumpReference(value, sb, indent + "  ");
        }
    }

    private static void DumpReference(SI.Core.TypedValue typedValue, StringBuilder sb, string indent, int depth = 0)
    {
        try
        {
            if (typedValue == null || depth > 3) return;
            var reference = typedValue.Get();
            var dynRef = reference?.TryCast<SI.Bindable.DynamicReference>();
            if (dynRef == null) return;

            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var keys = dynRef.Keys.GetEnumerator();
            while (keys.MoveNext())
            {
                var key = keys.Current;
                string name;
                try { name = reg.GetInfo((uint)key).Name; }
                catch { name = "key:" + key; }
                SI.Core.TypedValue nested = null;
                string val;
                try { nested = dynRef[key]; val = nested?.ToString() ?? "null"; }
                catch (System.Exception e) { val = "err:" + e.GetType().Name; }
                sb.Append(indent).Append("ref ").Append(name).Append(" = ").Append(val).Append('\n');
                // Nested references are where the attribute's identity and history live.
                DumpReference(nested, sb, indent + "  ", depth + 1);
            }
        }
        catch (System.Exception e)
        {
            sb.Append(indent).Append("ref dump failed: ").Append(e.GetType().Name).Append('\n');
        }
    }
}
