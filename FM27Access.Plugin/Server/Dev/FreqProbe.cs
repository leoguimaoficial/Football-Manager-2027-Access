using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the Routine Frequency popup (SetPieceRoutineFrequencyPopupCard). The
/// popup's three per-row checkboxes are exclusive in the game model (one RoutineFrequency per
/// routine) but their SICheckBox values go stale after any click — the game only re-syncs them
/// when the popup is rebuilt. This dumps, per row: the routine name texts, each checkbox's
/// current (possibly stale) value, and the row's binding node values — to establish whether the
/// row's TRUE frequency is readable live from its BindingRoot, which would let the companion
/// render the row as an honest radiogroup that can never desync.
///
/// GET /freqprobe. Read-only, main thread, no geometry.
/// </summary>
internal static class FreqProbe
{
    private const string PanelName = "SetPieceRoutineFrequencyPopupCard";
    private const int DepthCap = 60;

    internal static string Run()
    {
        var sb = new StringBuilder();
        VisualElement panel = null;
        foreach (var pv in InteractivePanels.Current())
            if (pv.Panel != null && pv.Panel.name == PanelName) { panel = pv.Panel; break; }
        if (panel == null) return "frequency popup not open\n";

        var rows = new List<VisualElement>();
        CollectRows(panel, rows, 0);
        sb.Append("rows=").Append(rows.Count).Append('\n');

        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return sb.Append("no BindingSubsystem\n").ToString();

        foreach (var row in rows)
        {
            sb.Append("========== row ==========\n");
            sb.Append("  texts:");
            AppendTexts(row, sb, 0);
            sb.Append('\n');

            var checkboxes = new List<VisualElement>();
            CollectCheckboxes(row, checkboxes, 0);
            for (int i = 0; i < checkboxes.Count; i++)
            {
                var cb = checkboxes[i].Cast<SI.Bindable.SICheckBox>();
                sb.Append("  checkbox[").Append(i).Append("] value=").Append(cb.value);
                DumpOwnCellNode(bindings, checkboxes[i], sb);
                sb.Append('\n');
            }

            var root = FindRootAbove(row);
            if (root == null) { sb.Append("  no BindingRoot above row\n"); continue; }
            var rootKey = root.Key;
            if (!bindings.TryGetNode(ref rootKey, out var node)) { sb.Append("  TryGetNode failed\n"); continue; }
            DumpNode(bindings, node, 0, sb, "  ");
        }
        return sb.ToString();
    }

    private static void CollectRows(VisualElement el, List<VisualElement> into, int depth)
    {
        if (depth > DepthCap) return;
        if (el.GetIl2CppType().Name == "TableRowNavigatable") { into.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectRows(el.hierarchy[i], into, depth + 1);
    }

    private static void CollectCheckboxes(VisualElement el, List<VisualElement> into, int depth)
    {
        if (depth > 12) return;
        if (el.GetIl2CppType().Name == "SICheckBox") { into.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) CollectCheckboxes(el.hierarchy[i], into, depth + 1);
    }

    private static void AppendTexts(VisualElement el, StringBuilder sb, int depth)
    {
        if (depth > 10) return;
        var text = el.TryCast<TextElement>();
        if (text != null && !string.IsNullOrEmpty(text.text))
            sb.Append(" \"").Append(ServerText.Clean(text.text)).Append('"');
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) AppendTexts(el.hierarchy[i], sb, depth + 1);
    }

    // The checkbox's OWN cell binding: ascend to the nearest BindingRemapper and print its node's
    // frequency_id and Selected, so each document-order column maps to its binding values directly.
    private static void DumpOwnCellNode(SI.Bindable.BindingSubsystem bindings, VisualElement cb, StringBuilder sb)
    {
        SI.Bindable.BindingRemapper remapper = null;
        for (var p = cb.hierarchy.parent; p != null && remapper == null; p = p.hierarchy.parent)
            remapper = p.TryCast<SI.Bindable.BindingRemapper>();
        if (remapper == null) { sb.Append(" (no remapper)"); return; }
        var key = remapper.Key;
        if (!bindings.TryGetNode(ref key, out var node)) { sb.Append(" (no node)"); return; }
        AppendLeaf(bindings, node, "frequency_id", sb);
        AppendLeaf(bindings, node, "Selected", sb);
    }

    private static void AppendLeaf(SI.Bindable.BindingSubsystem bindings,
        SI.Bindable.IReadOnlyNode node, string name, StringBuilder sb)
    {
        foreach (var child in node.Children)
        {
            if (child.Name == name)
            {
                var key = child.Key;
                SI.Core.TypedValue value;
                try { value = bindings.Get(ref key); }
                catch (Exception e) { sb.Append(' ').Append(name).Append("=err:").Append(e.GetType().Name); return; }
                sb.Append(' ').Append(name).Append('=').Append(value == null ? "null" : value.ToString());
                return;
            }
            AppendLeaf(bindings, child, name, sb);
        }
    }

    private static SI.Bindable.BindingRoot FindRootAbove(VisualElement el)
    {
        for (var p = el.hierarchy.parent; p != null; p = p.hierarchy.parent)
        {
            var root = p.TryCast<SI.Bindable.BindingRoot>();
            if (root != null) return root;
        }
        return null;
    }

    private static void DumpNode(SI.Bindable.BindingSubsystem bindings,
        SI.Bindable.IReadOnlyNode node, int depth, StringBuilder sb, string indent)
    {
        if (depth > 3) return;
        foreach (var child in node.Children)
        {
            var childKey = child.Key;
            SI.Core.TypedValue value;
            try { value = bindings.Get(ref childKey); }
            catch (Exception e)
            {
                sb.Append(indent).Append('"').Append(child.Name).Append("\" = err:").Append(e.GetType().Name).Append('\n');
                continue;
            }
            sb.Append(indent).Append('"').Append(child.Name).Append("\" = ")
              .Append(value == null ? "null" : value.ToString()).Append('\n');
            DumpNode(bindings, child, depth + 1, sb, indent + "  ");
        }
    }
}
