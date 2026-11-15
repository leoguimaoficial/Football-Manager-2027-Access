using System;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the per-position SUITABILITY behind a selector pitch's slots — the colour the
/// game paints each pitch dot with (green "can play here", amber "can fill in", dark "cannot"), which
/// is the whole content of the Player Comparison card's Overview pitches and is currently absent
/// from the HTML surface entirely.
///
/// The colour itself is painted from a style setter, so it is not readable as text. Each slot's
/// <c>FMTacticsPanel.PositionInfo.Data</c> is a <see cref="SI.Bindable.DynamicReference"/> though,
/// and that is what the paint is driven from — so this dumps every property name and value on every
/// valid slot's Data, to establish whether a suitability value is there to read and what it is
/// called. Same binding-read technique as <see cref="ShotMapValues"/>.
///
/// GET /suitpositions           — every FMTacticsPanel on the active panel, listed
/// GET /suitpositions?pitch=N   — dump slot bindings for the Nth pitch
/// Read-only.
/// </summary>
internal static class PositionSuitabilityProbe
{
    private const System.Reflection.BindingFlags AllFlags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

    private static readonly System.Reflection.PropertyInfo PositionsProp =
        typeof(FM.UI.FMTacticsPanel).GetProperty("m_positions", AllFlags);

    internal static string Run(int pitchIndex)
    {
        var sb = new StringBuilder();
        var pitches = new System.Collections.Generic.List<VisualElement>();
        try
        {
            var root = SI.Bindable.PanelManager.Instance.GetOpenPanelInHighestLayer();
            if (root == null) return "no open panel\n";
            Collect(root, 0, pitches);
        }
        catch (Exception e) { return $"pitch enumeration failed: {e.GetType().Name}: {e.Message}\n"; }

        for (int i = 0; i < pitches.Count; i++)
            sb.Append($"pitch[{i}] name={pitches[i].name} parent={pitches[i].hierarchy.parent?.name}\n");
        if (pitchIndex < 0 || pitchIndex >= pitches.Count) return sb.ToString();

        sb.Append($"\n--- pitch[{pitchIndex}] slot bindings ---\n");
        if (PositionsProp == null) return sb.Append("m_positions property not found\n").ToString();

        try
        {
            var pitch = pitches[pitchIndex].Cast<FM.UI.FMTacticsPanel>();
            if (PositionsProp.GetValue(pitch) is not System.Collections.IEnumerable positions)
                return sb.Append("m_positions not enumerable\n").ToString();

            int idx = 0;
            foreach (var entry in positions)
            {
                DumpSlot(sb, entry, idx++);
                if (idx > 40) { sb.Append("... (cap)\n"); break; }
            }
        }
        catch (Exception e) { sb.Append($"slot walk failed: {e.GetType().Name}: {e.Message}\n"); }
        return sb.ToString();
    }

    private static void DumpSlot(StringBuilder sb, object entry, int index)
    {
        string slot = "?";
        try
        {
            var slotProp = entry?.GetType().GetProperty("PitchIndicatorSlot", AllFlags);
            if (slotProp?.GetValue(entry) is VisualElement ind) slot = ind.name ?? "";
        }
        catch { }

        SI.Bindable.DynamicReference data = null;
        try
        {
            var dataProp = entry?.GetType().GetProperty("Data", AllFlags);
            data = dataProp?.GetValue(entry) as SI.Bindable.DynamicReference;
        }
        catch (Exception e) { sb.Append($"  [{index}] slot={slot} Data read failed: {e.GetType().Name}\n"); return; }

        if (data == null) { sb.Append($"  [{index}] slot={slot} Data=null\n"); return; }

        sb.Append($"  [{index}] slot={slot}\n");
        DumpReference(sb, data, "      ", 2);
    }

    // A reference's properties, descending into reference-valued ones: the slot's own Data carries
    // only ids, so anything about the player at that position lives a level or two below it.
    private static void DumpReference(StringBuilder sb, SI.Bindable.DynamicReference reference, string indent, int depth)
    {
        var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        var keys = reference.Keys.GetEnumerator();
        while (keys.MoveNext())
        {
            var key = keys.Current;
            string name;
            try { name = reg.GetInfo((uint)key).Name; }
            catch { name = "<id " + (uint)key + ">"; }

            SI.Bindable.DynamicReference child = null;
            string value;
            try
            {
                var typed = reference[key];
                child = typed?.Get()?.TryCast<SI.Bindable.DynamicReference>();
                value = typed?.AsString() ?? typed?.Get()?.ToString() ?? "<null>";
            }
            catch (Exception e) { value = "<read failed: " + e.GetType().Name + ">"; }

            sb.Append($"{indent}{name} = {value}\n");
            if (child != null && depth > 0) DumpReference(sb, child, indent + "    ", depth - 1);
        }
    }

    private static void Collect(VisualElement el, int depth, System.Collections.Generic.List<VisualElement> into)
    {
        // The comparison card buries its pitches ~50 levels down; same cap as PositionSelectorProbe.
        if (el == null || depth > 80) return;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return; }
        if (type == "FMTacticsPanel") { into.Add(el); return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) Collect(el.hierarchy[i], depth + 1, into);
    }
}
