using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the position-selector pitch (the Player Search "Conditions" card and any
/// other panel whose FMTacticsPanel runs in selector mode: indicators carry a
/// "positions-selector-root" binding root and no SIButton). Establishes how selection works so
/// the real adapter can be built on verified mechanics:
///   GET /positionprobe            — dump m_positions index map, m_selection state, selection mode
///   GET /positionprobe?click=DR   — invoke the panel's own HandleClick(index) for that slot, re-dump
///   GET /positionprobe?any=1      — probe the first FMTacticsPanel regardless of selector root
///                                   (the Squad Planner strip runs without one)
/// Read-only except for the explicit click op.
/// </summary>
internal static class PositionSelectorProbe
{
    private const System.Reflection.BindingFlags AllFlags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

    internal static string Run(string clickSlot) => Run(clickSlot, false, null, -1);

    internal static string Run(string clickSlot, bool anyPitch, string submitSlot, int pitchIndex)
    {
        // pitchIndex >= 0: enumerate every FMTacticsPanel on the panel and probe the Nth —
        // screens like the Squad Planner hold several (the strip, per-row mini pitches).
        if (pitchIndex >= 0)
        {
            var all = new System.Collections.Generic.List<VisualElement>();
            try
            {
                var panelRoot = SI.Bindable.PanelManager.Instance.GetOpenPanelInHighestLayer();
                if (panelRoot != null) CollectPitches(panelRoot, 0, all);
            }
            catch (System.Exception e) { return $"pitch enumeration failed: {e.GetType().Name}: {e.Message}\n"; }

            var listing = new StringBuilder();
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                string parent = p.hierarchy.parent?.name ?? "";
                listing.Append($"pitch[{i}] name={p.name} parent={parent} visible={ElementBridge.IsVisible(p)}\n");
            }
            if (pitchIndex >= all.Count) return listing.Append($"pitch index {pitchIndex} out of range\n").ToString();
            return listing + ProbePitch(all[pitchIndex], clickSlot, submitSlot);
        }

        var pitch = FindSelectorPitch(anyPitch);
        if (pitch == null) return "no matching FMTacticsPanel on the active panel\n";
        return ProbePitch(pitch, clickSlot, submitSlot);
    }

    private static void CollectPitches(VisualElement el, int depth, System.Collections.Generic.List<VisualElement> found)
    {
        if (el == null || depth > 80) return;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return; }
        if (type == "FMTacticsPanel") { found.Add(el); return; }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectPitches(el.hierarchy[i], depth + 1, found);
    }

    private static string ProbePitch(VisualElement pitch, string clickSlot, string submitSlot)
    {

        var sb = new StringBuilder();
        var typed = pitch.Cast<FM.UI.FMTacticsPanel>();
        var t = typeof(FM.UI.FMTacticsPanel);

        if (!string.IsNullOrEmpty(clickSlot))
        {
            int index = FindSlotIndex(typed, t, clickSlot);
            if (index < 0)
            {
                sb.Append($"click: slot '{clickSlot}' not found in m_positions\n");
            }
            else
            {
                var handleClick = t.GetMethod("HandleClick", AllFlags);
                if (handleClick == null) sb.Append("click: HandleClick method NOT FOUND\n");
                else
                {
                    try
                    {
                        handleClick.Invoke(typed, new object[] { index });
                        sb.Append($"click: HandleClick({index}) invoked for slot {clickSlot}\n");
                        // HandleClick updates m_selection but the panels observing the selection
                        // read it through the m_selectedPositionKey binding; UpdateSelectionKey
                        // is the publish step the real click path runs after it.
                        var updateKey = t.GetMethod("UpdateSelectionKey", AllFlags);
                        if (updateKey == null) sb.Append("click: UpdateSelectionKey NOT FOUND\n");
                        else
                        {
                            updateKey.Invoke(typed, null);
                            sb.Append("click: UpdateSelectionKey invoked\n");
                        }
                    }
                    catch (System.Exception e)
                    {
                        sb.Append($"click: threw {e.GetType().Name}: {e.Message}\n");
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(submitSlot))
        {
            var indicator = FindSlotIndicator(typed, t, submitSlot);
            if (indicator == null)
            {
                sb.Append($"submit: slot '{submitSlot}' not found in m_positions\n");
            }
            else
            {
                // The slot's NavigatableVisualElement child is what carries the game's click
                // wiring; the bare indicator did nothing when passed to the handler.
                var navChild = FindNavigatable(indicator);
                var target = navChild ?? indicator;
                sb.Append($"submit target: {(navChild != null ? "NavigatableVisualElement child" : "indicator itself")}\n");
                // The full native path: focus the slot through FMNavigationManager (the game's
                // controller-navigation focus), then submit — the same sequence a gamepad press
                // performs. UpdateFocus alone also reveals whether focusing triggers selection.
                try
                {
                    var nm = UnityEngine.Object.FindObjectOfType<FM.UI.FMNavigationManager>();
                    if (nm == null) { sb.Append("submit: no FMNavigationManager\n"); }
                    else
                    {
                        var focus=SI.Core.GameSubsystems.Get<SI.UI.FocusSubsystem>();
                        if(!focus.TryFocusElement(target))return sb.Append("submit: native focus rejected target\n").ToString();
                        sb.Append("submit: native focus accepted; sending NavigationSubmitEvent\n");
                        target.SendEvent(new NavigationSubmitEvent());
                        sb.Append($"submit: NavigationSubmitEvent sent to slot {submitSlot}\n");
                    }
                }
                catch (System.Exception e)
                {
                    sb.Append($"submit: threw {e.GetType().Name}: {e.Message}\n");
                }
            }
        }

        var modeProp = t.GetProperty("m_selectionMode", AllFlags);
        sb.Append($"m_selectionMode: {(modeProp != null ? modeProp.GetValue(typed)?.ToString() : "NOT FOUND")}\n");

        var selProp = t.GetProperty("m_selection", AllFlags);
        var selection = selProp?.GetValue(typed) as SI.UI.Selection<int>;
        sb.Append($"m_selection: {(selProp == null ? "prop NOT FOUND" : selection == null ? "null/uncastable" : $"Count={selection.Count}")}\n");

        var posProp = t.GetProperty("m_positions", AllFlags);
        if (posProp == null)
        {
            sb.Append("m_positions: prop NOT FOUND\n");
            return sb.ToString();
        }
        var positions = posProp.GetValue(typed);
        sb.Append($"m_positions type: {positions?.GetType().ToString() ?? "null"}\n");
        DumpPositions(positions, selection, sb);
        return sb.ToString();
    }

    private static void DumpPositions(object positions, SI.UI.Selection<int> selection, StringBuilder sb)
    {
        if (positions is not System.Collections.IEnumerable seq)
        {
            sb.Append("m_positions: not enumerable\n");
            return;
        }
        int i = 0;
        foreach (var entry in seq)
        {
            if (entry == null) { sb.Append($"  [{i++}] null\n"); continue; }
            var et = entry.GetType();
            string slot = "?", valid = "?";
            var slotProp = et.GetProperty("PitchIndicatorSlot", AllFlags);
            if (slotProp != null)
            {
                try
                {
                    var indicator = slotProp.GetValue(entry) as VisualElement;
                    slot = indicator == null ? "null" : indicator.name;
                }
                catch (System.Exception e) { slot = "err:" + e.GetType().Name; }
            }
            var validProp = et.GetProperty("DataIsValid", AllFlags);
            if (validProp != null)
            {
                try { valid = validProp.GetValue(entry)?.ToString() ?? "null"; }
                catch (System.Exception e) { valid = "err:" + e.GetType().Name; }
            }
            string sel = selection != null ? (selection.Contains(i) ? "SELECTED" : "-") : "?";
            sb.Append($"  [{i}] slot={slot} dataValid={valid} {sel}\n");
            i++;
            if (i > 40) { sb.Append("  ... (cap)\n"); break; }
        }
    }

    private static VisualElement FindNavigatable(VisualElement indicator)
    {
        int n = indicator.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = indicator.hierarchy[i];
            try
            {
                if (child.GetIl2CppType().Name == "NavigatableVisualElement") return child;
            }
            catch { return null; }
        }
        return null;
    }

    private static VisualElement FindSlotIndicator(FM.UI.FMTacticsPanel typed, System.Type t, string slotName)
    {
        var posProp = t.GetProperty("m_positions", AllFlags);
        if (posProp?.GetValue(typed) is not System.Collections.IEnumerable seq) return null;
        foreach (var entry in seq)
        {
            var slotProp = entry?.GetType().GetProperty("PitchIndicatorSlot", AllFlags);
            if (slotProp?.GetValue(entry) is VisualElement indicator && indicator.name == slotName)
                return indicator;
        }
        return null;
    }

    private static int FindSlotIndex(FM.UI.FMTacticsPanel typed, System.Type t, string slotName)
    {
        var posProp = t.GetProperty("m_positions", AllFlags);
        if (posProp?.GetValue(typed) is not System.Collections.IEnumerable seq) return -1;
        int i = 0;
        foreach (var entry in seq)
        {
            var slotProp = entry?.GetType().GetProperty("PitchIndicatorSlot", AllFlags);
            if (slotProp != null)
            {
                try
                {
                    if (slotProp.GetValue(entry) is VisualElement indicator && indicator.name == slotName)
                        return i;
                }
                catch { /* fall through to next entry; the dump op reports per-entry errors */ }
            }
            i++;
        }
        return -1;
    }

    private static VisualElement FindSelectorPitch(bool anyPitch)
    {
        try
        {
            var panel = SI.Bindable.PanelManager.Instance.GetOpenPanelInHighestLayer();
            if (panel == null) return null;
            return Find(panel, 0, anyPitch);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[PositionSelectorProbe] pitch lookup failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static VisualElement Find(VisualElement el, int depth, bool anyPitch)
    {
        if (el == null || depth > 80) return null;
        string type;
        try { type = el.GetIl2CppType().Name; } catch { return null; }
        if (type == "FMTacticsPanel" && (anyPitch || HasSelectorRoot(el, 0))) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = Find(el.hierarchy[i], depth + 1, anyPitch);
            if (found != null) return found;
        }
        return null;
    }

    private static bool HasSelectorRoot(VisualElement el, int depth)
    {
        if (depth > 10) return false;
        if (el.name == "positions-selector-root") return true;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            if (HasSelectorRoot(el.hierarchy[i], depth + 1))
                return true;
        }
        return false;
    }
}
