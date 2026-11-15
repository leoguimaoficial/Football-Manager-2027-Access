using System;
using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for FM.UI.FMPlayerFormChart — the sparkline inside roster tables' "Form"
/// column. The speech surface reads it via the tooltip dance (FormButtonAdapter); this dumps the
/// chart's own backing collection (m_values: ObservableCollection&lt;DataPoint&gt; of
/// MatchIndex/Rating) to establish (a) that the plotted ratings are directly readable for an HTML
/// cell rendering, and (b) what scalar the game's Form column sort follows — dump on a
/// form-sorted table and compare each row's candidate aggregates against the row order.
///
/// FMPlayerFormChart did NOT survive interop generation (no managed wrapper exists — likely its
/// LiveChartsCore generic base broke the generator), so everything here goes through IL2CPP-side
/// reflection (Il2CppSystem.Reflection), the ContextMenuSurface pattern. DataPoint is a reference
/// type, so no struct-generic hazard.
/// GET /formprobe. Read-only.
/// </summary>
internal static class FormChartProbe
{
    private static readonly Il2CppSystem.Reflection.BindingFlags Flags =
        Il2CppSystem.Reflection.BindingFlags.Public
        | Il2CppSystem.Reflection.BindingFlags.NonPublic
        | Il2CppSystem.Reflection.BindingFlags.Instance;

    internal static string Run()
    {
        var panel = NavigationManager.GetActivePanel();
        if (panel is not VisualElement root) return "no active panel\n";

        var sb = new StringBuilder();
        int found = 0, budget = 0;
        Walk(root, sb, ref found, ref budget);
        return found > 0 ? sb.ToString() : "no FMPlayerFormChart on the active panel\n";
    }

    private static void Walk(VisualElement el, StringBuilder sb, ref int found, ref int budget)
    {
        if (budget++ > 40000) return;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return; }

        if (type == "FMPlayerFormChart")
        {
            found++;
            try { Dump(el, found, sb); }
            catch (Exception e)
            {
                sb.Append('[').Append(found).Append("] dump threw ")
                  .Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
            }
            return;
        }

        // The roster-table form cell: not a chart but a StreamedObjectList of per-match pips,
        // each an AsyncBindingRoot whose Tooltip carries a BindingRoot. Dump every reachable
        // BindingRoot node in the tile to find where the ratings live.
        string name = null;
        try { name = el.name; } catch { }
        if (name == "tile states-player-form-small")
        {
            found++;
            try { DumpTile(el, found, sb); }
            catch (Exception e)
            {
                sb.Append('[').Append(found).Append("] tile dump threw ")
                  .Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
            }
            return;
        }

        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count; i++)
        {
            VisualElement child;
            try { child = el.hierarchy[i]; } catch { return; }
            Walk(child, sb, ref found, ref budget);
        }
    }

    private static void Dump(VisualElement el, int index, StringBuilder sb)
    {
        sb.Append('[').Append(index).Append("] y=");
        try { sb.Append(el.worldBound.y.ToString("F0")); } catch { sb.Append('?'); }
        sb.Append(" row=\"").Append(RowLabel(el)).Append('"');

        var chartType = el.GetIl2CppType();

        var amountField = chartType.GetField("<AmountToDisplay>k__BackingField", Flags);
        if (amountField != null)
        {
            var amountObj = amountField.GetValue(el);
            sb.Append(" amountToDisplay=").Append(amountObj == null ? "null" : amountObj.Unbox<int>().ToString());
        }

        var valuesField = chartType.GetField("m_values", Flags);
        if (valuesField == null)
        {
            sb.Append(" m_values field NOT FOUND; fields:");
            var fields = chartType.GetFields(Flags);
            for (int i = 0; i < fields.Length; i++)
                sb.Append(' ').Append(fields[i].Name);
            sb.Append('\n');
            return;
        }

        var values = valuesField.GetValue(el);
        if (values == null) { sb.Append(" m_values=null\n"); return; }

        // Collection<T>.items (the backing List<T>), then List<T>._items/_size — indexed through
        // an Il2CppReferenceArray view so no boxed-int reflection Invoke is needed.
        var collType = values.GetIl2CppType();
        var itemsField = collType.GetField("items", Flags);
        var list = itemsField?.GetValue(values);
        if (list == null) { sb.Append(" items list not reachable (").Append(FieldNames(collType)).Append(")\n"); return; }

        var listType = list.GetIl2CppType();
        var arrField = listType.GetField("_items", Flags);
        var sizeField = listType.GetField("_size", Flags);
        var arrObj = arrField?.GetValue(list);
        if (arrObj == null || sizeField == null)
        { sb.Append(" list internals not reachable (").Append(FieldNames(listType)).Append(")\n"); return; }

        int n = sizeField.GetValue(list).Unbox<int>();
        sb.Append(" points=").Append(n);
        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>(arrObj.Pointer);

        double sum = 0;
        int summed = 0;
        for (int i = 0; i < n && i < 20; i++)
        {
            var point = arr[i];
            if (point == null) { sb.Append(" (null)"); continue; }

            var pType = point.GetIl2CppType();
            var miObj = pType.GetField("<MatchIndex>k__BackingField", Flags)?.GetValue(point);
            var rObj = pType.GetField("<Rating>k__BackingField", Flags)?.GetValue(point);
            int mi = miObj == null ? -1 : miObj.Unbox<int>();
            double r = rObj == null ? double.NaN : rObj.Unbox<double>();
            sb.Append(" [").Append(mi).Append(':').Append(r.ToString("F2")).Append(']');
            if (!double.IsNaN(r)) { sum += r; summed++; }
        }
        if (summed > 0)
            sb.Append(" avg=").Append((sum / summed).ToString("F3"));
        sb.Append('\n');
    }

    private static void DumpTile(VisualElement tile, int index, StringBuilder sb)
    {
        sb.Append('[').Append(index).Append("] y=");
        try { sb.Append(tile.worldBound.y.ToString("F0")); } catch { sb.Append('?'); }
        sb.Append(" row=\"").Append(RowLabel(tile)).Append("\"\n");

        int pips = 0, budget = 0;
        DumpBindingRoots(tile, sb, ref pips, ref budget, 0);
        if (pips == 0) sb.Append("    no BindingRoot in tile\n");

        // The production reader, run against the same cell button this tile belongs to.
        VisualElement button = null;
        try { button = tile.hierarchy.parent; } catch { }
        sb.Append("    reader: \"")
          .Append(Features.FormRatingsReader.Read(button) ?? "(not recognized as a form cell)")
          .Append("\"\n");

        // Model route: the pip list's own items-container binding node, independent of whether
        // any pip element has realized/bound yet. This is the candidate read for the cell path,
        // where realization timing made the element route return nothing.
        try { DumpModel(tile, sb); }
        catch (Exception e) { sb.Append("    model: threw ").Append(e.GetType().Name).Append(' ').Append(e.Message).Append('\n'); }
    }

    private static readonly System.Reflection.PropertyInfo ItemsContainerKeyProp =
        typeof(SI.Bindable.StreamedObjectList).GetProperty("m_itemsContainerKey",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance);

    private static void DumpModel(VisualElement tile, StringBuilder sb)
    {
        var listEl = FindTyped(tile, "StreamedObjectList", 0);
        if (listEl == null) { sb.Append("    model: no StreamedObjectList\n"); return; }
        if (ItemsContainerKeyProp == null) { sb.Append("    model: m_itemsContainerKey property not found\n"); return; }

        var typed = listEl.Cast<SI.Bindable.StreamedObjectList>();
        var keyObj = ItemsContainerKeyProp.GetValue(typed);
        if (keyObj is not SI.Bindable.Bindings.Key key) { sb.Append("    model: key not a Bindings.Key (").Append(keyObj?.GetType().FullName ?? "null").Append(")\n"); return; }

        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (!bindings.TryGetNode(ref key, out var container)) { sb.Append("    model: container node not found\n"); return; }

        int n = 0;
        foreach (var item in container.Children)
        {
            sb.Append("    model item ").Append(n++).Append(" name=").Append(item.Name).Append(": ");
            var itemKey = item.Key;
            var itemValue = bindings.Get(ref itemKey);
            sb.Append("self=").Append(DescribeValue(itemValue)).Append(" | ");
            DescribeChildren(bindings, item, sb, 0);
            sb.Append('\n');
        }
        if (n == 0) sb.Append("    model: container has no children\n");
    }

    private static VisualElement FindTyped(VisualElement el, string typeName, int depth)
    {
        if (el == null || depth > 8) return null;
        try
        {
            if (el.GetIl2CppType().Name == typeName) return el;
            int count = el.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var found = FindTyped(el.hierarchy[i], typeName, depth + 1);
                if (found != null) return found;
            }
        }
        catch { return null; }
        return null;
    }

    private static void DumpBindingRoots(VisualElement el, StringBuilder sb, ref int pips, ref int budget, int depth)
    {
        if (el == null || depth > 16 || budget++ > 2000) return;
        SI.Bindable.BindingRoot root = null;
        try { root = el.TryCast<SI.Bindable.BindingRoot>(); } catch { }
        if (root != null)
        {
            pips++;
            sb.Append("    pip ").Append(pips).Append(": ");
            try { DescribeNode(root, sb); }
            catch (Exception e) { sb.Append("err:").Append(e.GetType().Name).Append(' ').Append(e.Message); }
            sb.Append('\n');
        }
        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count; i++)
        {
            VisualElement child;
            try { child = el.hierarchy[i]; } catch { return; }
            DumpBindingRoots(child, sb, ref pips, ref budget, depth + 1);
        }
    }

    private static void DescribeNode(SI.Bindable.BindingRoot root, StringBuilder sb)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) { sb.Append("no BindingSubsystem"); return; }
        var key = root.Key;
        if (!bindings.TryGetNode(ref key, out var node)) { sb.Append("no node"); return; }
        DescribeChildren(bindings, node, sb, 0);
    }

    internal static void DescribeChildren(SI.Bindable.BindingSubsystem bindings, SI.Bindable.IReadOnlyNode node, StringBuilder sb, int depth)
    {
        if (depth > 3) return;
        foreach (var child in node.Children)
        {
            sb.Append(child.Name).Append('=');
            var childKey = child.Key;
            Il2CppSystem.Object value = null;
            try { value = bindings.Get(ref childKey); } catch { }
            sb.Append(DescribeValue(value)).Append(" | ");
            DescribeChildren(bindings, child, sb, depth + 1);
        }
    }

    internal static string DescribeValue(Il2CppSystem.Object value) => DescribeValue(value, 0);

    private static string DescribeValue(Il2CppSystem.Object value, int depth)
    {
        if (value == null) return "(null)";
        var tv = value.TryCast<SI.Core.TypedValue>();
        if (tv == null) return "(not TypedValue: " + value.GetIl2CppType().Name + ")";

        Il2CppSystem.Object inner = null;
        try { inner = tv.Get() as Il2CppSystem.Object; } catch { }
        if (inner == null)
        {
            string s0 = null;
            try { s0 = tv.AsString(); } catch { }
            return string.IsNullOrEmpty(s0) ? "(empty)" : s0;
        }

        var rec = inner.TryCast<SI.Core.Record>();
        if (rec != null)
        {
            if (depth >= 3) return "(record, depth capped)";
            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var rsb = new StringBuilder("{");
            try
            {
                var en = rec.GetEnumerator();
                int guard = 0;
                while (en.MoveNext() && guard++ < 40)
                {
                    var kv = en.Current;
                    string pname;
                    try { pname = reg.GetInfo(kv.Key).Name; } catch { pname = "key" + kv.Key; }
                    rsb.Append(pname).Append(':').Append(DescribeValue(kv.Value, depth + 1)).Append(' ');
                }
            }
            catch (Exception e) { rsb.Append("err:").Append(e.GetType().Name); }
            return rsb.Append('}').ToString();
        }

        string s = null;
        try { s = tv.AsString(); } catch { }
        if (!string.IsNullOrEmpty(s)) return s;
        try { return inner.ToString(); } catch { return "(unprintable " + inner.GetIl2CppType().Name + ")"; }
    }

    private static string FieldNames(Il2CppSystem.Type t)
    {
        var sb = new StringBuilder();
        var fields = t.GetFields(Flags);
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(fields[i].Name);
        }
        return sb.ToString();
    }

    // The nearest row ancestor's first few text leaves, so each chart can be matched to the
    // player row it belongs to in the rendered table.
    private static string RowLabel(VisualElement el)
    {
        var row = el;
        for (int up = 0; up < 15 && row != null; up++)
        {
            string type;
            try { type = row.GetIl2CppType().Name; } catch { return "(type unreadable)"; }
            if (type == "TableRowNavigatable") break;
            try { row = row.hierarchy.parent; } catch { return "(parent unreadable)"; }
        }
        if (row == null) return "(no row ancestor)";

        var texts = new StringBuilder();
        int taken = 0;
        CollectTexts(row, texts, ref taken, 0);
        return texts.ToString();
    }

    private static void CollectTexts(VisualElement el, StringBuilder sb, ref int taken, int depth)
    {
        if (el == null || depth > 14 || taken >= 5) return;
        TextElement te = null;
        try { te = el.TryCast<TextElement>(); } catch { }
        if (te != null)
        {
            string text = null;
            try { text = te.text; } catch { }
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (taken > 0) sb.Append(" | ");
                sb.Append(text.Trim());
                taken++;
            }
            return;
        }
        int count;
        try { count = el.hierarchy.childCount; } catch { return; }
        for (int i = 0; i < count && taken < 5; i++)
        {
            VisualElement child;
            try { child = el.hierarchy[i]; } catch { return; }
            CollectTexts(child, sb, ref taken, depth + 1);
        }
    }
}
