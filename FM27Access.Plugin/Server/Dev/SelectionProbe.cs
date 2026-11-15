using System;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for table row multi-selection — the feasibility test for a mod-authored
/// "Select all" button. Lists every StreamedTable in the open panels with its selection state
/// (mode, limit, row count, current selection), and can drive the game's own range-selection
/// entry (VirtualisationController.UpdateSelection — the shift-click path) across the whole list.
///
///   GET /selprobe                     list tables, indexed
///   GET /selprobe?op=all&amp;index=N     anchor row 0 then range-select to the last row
///   GET /selprobe?op=clear&amp;index=N   ClearSelection()
/// </summary>
internal static class SelectionProbe
{
    internal static string Run(string op, string indexStr)
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                var tables = FindTables();
                if (string.IsNullOrEmpty(op) || op == "list") return List(tables);
                if (!int.TryParse(indexStr, out int index) || index < 0 || index >= tables.Count)
                    return $"ops need &index=N in 0..{tables.Count - 1} (from op=list)\n";
                var vl = GetList(tables[index].Table);
                if (vl == null) return "ListElement unreadable on that table\n";
                switch (op)
                {
                    case "all":
                    {
                        int rows = RowCount(vl);
                        if (rows <= 0) return $"row count {rows}; nothing to select\n";
                        var controller = vl.Controller;
                        if (controller == null) return "no Controller\n";
                        controller.UpdateSelection(false, false, 0);
                        controller.UpdateSelection(false, true, rows - 1);
                        return $"anchored 0, ranged to {rows - 1} (verify via /body checkbox state)\n";
                    }
                    case "clear":
                        vl.ClearSelection();
                        return "cleared (verify via /body checkbox state)\n";
                    default:
                        return "ops: list | all&index=N | clear&index=N\n";
                }
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n";
        }
    }

    private static string List(System.Collections.Generic.List<(string Panel, VisualElement Table)> tables)
    {
        if (tables.Count == 0) return "no StreamedTable in any open panel\n";
        var sb = new StringBuilder();
        for (int i = 0; i < tables.Count; i++)
        {
            sb.Append('[').Append(i).Append("] panel=").Append(tables[i].Panel)
              .Append(" name=\"").Append(tables[i].Table.name).Append('"');
            var vl = GetList(tables[i].Table);
            if (vl == null) { sb.Append(" <ListElement unreadable>\n"); continue; }
            try { sb.Append(" mode=").Append(vl.SelectionMode); } catch (Exception e) { sb.Append(" mode=<").Append(e.GetType().Name).Append('>'); }
            sb.Append(" rows=").Append(RowCount(vl));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static SI.UI.VirtualisedList GetList(VisualElement table)
    {
        try
        {
            var streamed = new SI.Bindable.StreamedTable(table.Pointer);
            var obj = typeof(SI.Bindable.StreamedListView)
                .GetProperty("ListElement", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(streamed);
            if (obj == null) return null;
            return new SI.UI.VirtualisedList(((Il2CppSystem.Object)obj).Pointer);
        }
        catch { return null; }
    }

    private static int RowCount(SI.UI.VirtualisedList vl)
    {
        try
        {
            var items = vl.Items;
            if (items == null) return -1;
            return items.Cast<Il2CppSystem.Collections.ICollection>().Count;
        }
        catch { return -1; }
    }

    private static System.Collections.Generic.List<(string Panel, VisualElement Table)> FindTables()
    {
        var found = new System.Collections.Generic.List<(string Panel, VisualElement Table)>();
        if (!SI.Bindable.PanelManager.Exists()) return found;
        var layers = FM27PanelCatalog.GetLayers(SI.Bindable.PanelManager.Instance);
        for (int l = 0; l < layers.Count; l++)
        {
            var open = layers[l].Open;
            if (open == null) continue;
            for (int p = 0; p < open.Count; p++)
            {
                if (open[p] is not VisualElement root) continue;
                Collect(root, open[p].name, found, 0);
            }
        }
        return found;
    }

    private static void Collect(VisualElement el, string panel, System.Collections.Generic.List<(string Panel, VisualElement Table)> into, int depth)
    {
        if (depth > 90) return;
        if (el.GetIl2CppType().Name == "StreamedTable") { into.Add((panel, el)); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) Collect(el.hierarchy[i], panel, into, depth + 1);
    }
}
