using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the Position suitability visualiser's data model
/// (FMTacticsWithAnalysis.m_analysisCells / m_analysisData). The visualiser paints its
/// position boxes and player chips rather than building elements, so this reads the widget's
/// own cell structs by reflection to establish whether an accessible rendering can be fed
/// from them. GET /suitabilityprobe. Read-only.
/// </summary>
internal static class SuitabilityProbe
{
    internal static string Run()
    {
        var pitch = FindPitch();
        if (pitch == null) return "no FMTacticsWithAnalysis on the active panel\n";

        var sb = new StringBuilder();
        var typed = pitch.Cast<FM.UI.FMTacticsWithAnalysis>();
        var t = typeof(FM.UI.FMTacticsWithAnalysis);
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                  | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

        var cellsProp = t.GetProperty("m_analysisCells", flags);
        var dataProp = t.GetProperty("m_analysisData", flags);
        var showProp = t.GetProperty("m_showAnalysis", flags);
        sb.Append($"m_analysisCells prop: {(cellsProp != null ? cellsProp.PropertyType.ToString() : "NOT FOUND")}\n");
        sb.Append($"m_analysisData prop: {(dataProp != null ? dataProp.PropertyType.ToString() : "NOT FOUND")}\n");
        sb.Append($"m_showAnalysis: {(showProp != null ? showProp.GetValue(typed)?.ToString() : "NOT FOUND")}\n");

        if (cellsProp != null)
        {
            var cells = cellsProp.GetValue(typed)
                as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<FM.UI.FMTacticsWithAnalysis.AnalysisCell>;
            if (cells == null) sb.Append("cells: null or unexpected array type\n");
            else
            {
                sb.Append($"cells: {cells.Length}\n");
                for (int i = 0; i < cells.Length && i < 30; i++)
                {
                    var cell = cells[i];
                    if (cell == null) { sb.Append($"  [{i}] null\n"); continue; }
                    var ct = cell.GetType();
                    string row = Read(ct, cell, "Row"), col = Read(ct, cell, "Column"),
                           score = Read(ct, cell, "Score"), invalid = Read(ct, cell, "InvalidStyle"),
                           key = Read(ct, cell, "Key");
                    sb.Append($"  [{i}] row={row} col={col} score={score} invalid={invalid} key={key}\n");
                }
            }
        }

        if (dataProp != null)
        {
            var data = dataProp.GetValue(typed);
            sb.Append($"analysisData: {(data == null ? "null" : data.GetType().ToString())}\n");
            if (data is Il2CppSystem.Object il2)
                sb.Append($"  il2cpp type: {il2.GetIl2CppType().FullName}\n");
        }
        return sb.ToString();
    }

    private static string Read(System.Type t, object obj, string member)
    {
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                  | System.Reflection.BindingFlags.Instance;
        var p = t.GetProperty(member, flags);
        if (p != null) { try { return p.GetValue(obj)?.ToString() ?? "null"; } catch (System.Exception e) { return "err:" + e.GetType().Name; } }
        var f = t.GetField(member, flags);
        if (f != null) { try { return f.GetValue(obj)?.ToString() ?? "null"; } catch (System.Exception e) { return "err:" + e.GetType().Name; } }
        return "?";
    }

    private static VisualElement FindPitch()
    {
        try
        {
            var panel = SI.Bindable.PanelManager.Instance.GetOpenPanelInHighestLayer();
            if (panel == null) return null;
            return Find(panel, 0);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[SuitabilityProbe] pitch lookup failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static VisualElement Find(VisualElement el, int depth)
    {
        if (el == null || depth > 80) return null;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return null; }
        if (type == "FMTacticsWithAnalysis") return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = Find(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}
