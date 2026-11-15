using System.Text;
using System.Threading;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the condition row's FMCombobox stepper (the table's Value cell), hit when
/// ComboboxAdapter.Increment silently no-ops there. GET /conditionprobe?row=N&amp;op=inc|dec|dump
/// — realizes the row's combobox, reports the adapter's view (value, inc/dec enabled, button
/// presence), optionally clicks, waits two frames, and reports the value again.
/// </summary>
internal static class ConditionComboProbe
{
    internal static string Run(string rowStr, string op)
    {
        int row = int.TryParse(rowStr, out var r) ? r : 0;
        var sb = new StringBuilder();

        var before = MainThreadDispatcher.Invoke(() => Inspect(row, op, sb));
        if (before == null) return sb.ToString();

        Thread.Sleep(120); // let frames run so a clicked step lands

        var after = MainThreadDispatcher.Invoke(() =>
        {
            var el = FindCombobox(row);
            if (el == null) return "(combobox gone)";
            try { return new Adapters.ComboboxAdapter(el).GetCurrentValue() ?? "(null)"; }
            catch (System.Exception e) { return "err:" + e.GetType().Name; }
        });
        sb.Append($"value after wait: {after}\n");
        return sb.ToString();
    }

    private static object Inspect(int row, string op, StringBuilder sb)
    {
        var el = FindCombobox(row);
        if (el == null) { sb.Append($"row {row}: no FMCombobox realized\n"); return null; }

        try
        {
            var adapter = new Adapters.ComboboxAdapter(el);
            sb.Append($"value: {adapter.GetCurrentValue() ?? "(null)"}\n");
            sb.Append($"incEnabled: {adapter.IsIncrementEnabled()} decEnabled: {adapter.IsDecrementEnabled()}\n");
            sb.Append($"hasDropdownChoices: {adapter.HasDropdownChoices()}\n");

            if (op == "inc") { adapter.Increment(); sb.Append("Increment() invoked\n"); }
            else if (op == "dec") { adapter.Decrement(); sb.Append("Decrement() invoked\n"); }
            else if (op == "type")
            {
                // Direct numeric entry through the combobox's hidden LongField; a value write
                // fires the change event the widget commits from.
                var numProp = typeof(FM.UI.Widgets.FMCombobox).GetProperty("m_numericInput",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                if (numProp == null) sb.Append("m_numericInput NOT FOUND\n");
                else
                {
                    var typed = el.Cast<FM.UI.Widgets.FMCombobox>();
                    var lf = numProp.GetValue(typed) as UnityEngine.UIElements.LongField;
                    if (lf == null) sb.Append("m_numericInput null/uncastable\n");
                    else
                    {
                        // The real typing sequence: the widget swaps to its input mode on focus and
                        // commits the field on blur.
                        ((VisualElement)lf).Focus();
                        lf.value = 13;
                        ((VisualElement)lf).Blur();
                        sb.Append("m_numericInput focus + value=13 + blur\n");
                    }
                }
            }
            else if (op == "quickup" || op == "quickdown")
            {
                // The game's own gamepad stick-step path, bypassing the click-immune +/- buttons.
                var quick = typeof(FM.UI.Widgets.FMCombobox).GetMethod("QuickChangeOption",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                if (quick == null) sb.Append("QuickChangeOption NOT FOUND\n");
                else
                {
                    var typed = el.Cast<FM.UI.Widgets.FMCombobox>();
                    var dir = new UnityEngine.Vector2(0f, op == "quickup" ? 1f : -1f);
                    quick.Invoke(typed, new object[] { dir });
                    sb.Append($"QuickChangeOption({dir}) invoked\n");
                }
            }
        }
        catch (System.Exception e)
        {
            sb.Append($"adapter error: {e.GetType().Name}: {e.Message}\n");
            return null;
        }
        return this_marker;
    }

    private static readonly object this_marker = new();

    private static VisualElement FindCombobox(int row)
    {
        var list = FindConditionsList();
        if (list == null) return null;
        return new Contexts.ConditionTableContext(list).RealizeControl(row,
            "attribute-combobox-value-min-visible");
    }

    private static VisualElement FindConditionsList()
    {
        try
        {
            var panel = SI.Bindable.PanelManager.Instance.GetOpenPanelInHighestLayer();
            if (panel == null) return null;
            return FindNamed(panel, "conditions-list", 0);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[ConditionComboProbe] list lookup failed: {e.GetType().Name}");
            return null;
        }
    }

    private static VisualElement FindNamed(VisualElement el, string name, int depth)
    {
        if (el == null || depth > 80) return null;
        if (el.name == name) return el;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindNamed(el.hierarchy[i], name, depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}
