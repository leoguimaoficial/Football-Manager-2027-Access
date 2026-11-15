using System;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe for the game's date selector widget. The HTML surface renders a date field as an
/// editable calendar only for a <c>BoundedDateSelectorController</c>; screens whose picker uses the
/// discrete-set <c>LimitedDateSelectorController</c> (Arrange Friendly) fall back to read-only text.
/// Deciding whether the editor can drive a discrete-set picker needs the popup's live internals:
/// which months the controller allows, what the month dropdown's index means once those months are
/// filtered, and whether the day grid is a full wall calendar (disabled days present) or only the
/// selectable days.
///
/// GET /dateprobe                 - read the widget and controller without touching the popup
/// GET /dateprobe?open=1          - open the popup (multi-frame; re-run plain to read it)
/// GET /dateprobe?close=1         - cancel the popup
/// GET /dateprobe?year=YYYY       - list the controller's enabled days for that year
///
/// The apply steps DateSelectorSession performs, one per request so frames pass between them —
/// this is how the session's sequence is rehearsed against a picker the HTML surface does not yet
/// render as editable:
/// GET /dateprobe?setyear=N       - drive the year dropdown to index N
/// GET /dateprobe?stepmonth=N     - click the next (N&gt;0) or previous (N&lt;0) month button |N| times
/// GET /dateprobe?pick=d/m/y      - press that date's grid cell
/// GET /dateprobe?confirm=1       - click Confirm
/// </summary>
internal static class DateProbe
{
    internal static string Run(bool open, bool close, int enabledYear, Steps steps)
    {
        var sb = new StringBuilder();
        UnityEngine.UIElements.VisualElement panel = null;
        FM.UI.Widgets.DateSelectorWidget widget = null;
        foreach (var view in InteractivePanels.Current())
        {
            if (view.Panel is not UnityEngine.UIElements.VisualElement root) continue;
            var found = Find(root, 0);
            if (found == null) continue;
            panel = root; widget = found; break;
        }
        if (panel == null) return "no DateSelectorWidget on the interactive panels\n";
        sb.Append("panel=").Append(panel.name).Append('\n');
        if (widget == null) return sb.Append("no DateSelectorWidget on this panel\n").ToString();

        var adapter = new Adapters.DateSelectorAdapter(widget);
        if (open) { adapter.ShowPopup(); sb.Append("ShowPopup() called; re-run /dateprobe to read the popup\n"); }
        if (steps.SetYear >= 0) { adapter.SetYearDropdown(steps.SetYear); sb.Append("SetYearDropdown(").Append(steps.SetYear).Append(")\n"); }
        for (int i = 0; i < Math.Abs(steps.StepMonth); i++)
        {
            if (steps.StepMonth > 0) adapter.ClickNextMonth(); else adapter.ClickPreviousMonth();
            sb.Append(steps.StepMonth > 0 ? "ClickNextMonth()\n" : "ClickPreviousMonth()\n");
        }
        if (steps.Pick != "")
        {
            if (!FM27Access.Navigation.DateFieldText.TryParseDmy(steps.Pick, out int py, out int pm, out int pd))
                sb.Append("pick: could not parse \"").Append(steps.Pick).Append("\" as d/m/y\n");
            else
                sb.Append("SelectDate(").Append(py).Append('-').Append(pm).Append('-').Append(pd)
                  .Append(") -> ").Append(adapter.SelectDate(py, pm, pd)).Append('\n');
        }
        if (steps.Confirm) { adapter.ClickConfirm(); sb.Append("ClickConfirm()\n"); }
        if (close) { adapter.ClickCancel(); sb.Append("ClickCancel() called\n"); }

        DumpWidget(widget, adapter, sb);
        if (enabledYear > 0) DumpEnabled(widget, enabledYear, sb);
        return sb.ToString();
    }

    internal struct Steps
    {
        public int SetYear;    // -1 = skip
        public int StepMonth;  // signed click count, 0 = skip
        public string Pick;    // d/m/y, "" = skip
        public bool Confirm;
    }

    private static void DumpWidget(FM.UI.Widgets.DateSelectorWidget w, Adapters.DateSelectorAdapter adapter, StringBuilder sb)
    {
        var controller = w.m_controller;
        sb.Append("controller=")
          .Append(controller == null ? "null" : ((Il2CppSystem.Object)controller).GetIl2CppType().Name).Append('\n');
        sb.Append("floatingWidget=").Append(w.IsFloatingWidget)
          .Append("  popupVisible=").Append(adapter.IsPopupVisible()).Append('\n');

        var dt = w.m_selectedDateTime;
        sb.Append("selected=").Append(dt.Year).Append('-').Append(dt.Month).Append('-').Append(dt.Day)
          .Append("  label=\"").Append(adapter.GetLabel()).Append("\"\n");

        if (controller != null)
        {
            sb.Append("Years=").Append(Ints(controller.Years)).Append('\n');
            sb.Append("Months=").Append(Ints(controller.Months)).Append('\n');
            sb.Append("MonthsCollection=").Append(controller.MonthsCollection == null ? "null" : controller.MonthsCollection.Count.ToString())
              .Append("  AllMonthsCollection=").Append(controller.AllMonthsCollection == null ? "null" : controller.AllMonthsCollection.Count.ToString())
              .Append("  DaysCollection=").Append(controller.DaysCollection == null ? "null" : controller.DaysCollection.Count.ToString()).Append('\n');
        }

        sb.Append("yearDropdown.SelectedIndex=").Append(w.m_yearDropdown == null ? "null" : w.m_yearDropdown.SelectedIndex.ToString())
          .Append("  monthDropdown.SelectedIndex=").Append(w.m_monthDropdown == null ? "null" : w.m_monthDropdown.SelectedIndex.ToString()).Append('\n');
        sb.Append("prevMonthBtn.enabled=").Append(w.m_previousMonthButton == null ? "null" : w.m_previousMonthButton.enabledSelf.ToString())
          .Append("  nextMonthBtn.enabled=").Append(w.m_nextMonthButton == null ? "null" : w.m_nextMonthButton.enabledSelf.ToString()).Append('\n');

        var grid = w.m_dateGrid?.m_cells;
        if (grid == null) { sb.Append("grid=null (popup never opened)\n"); return; }
        var dates = controller?.DateTimeCollection;
        if (dates == null) { sb.Append("grid has no initialized date collection\n"); return; }
        sb.Append("grid=").Append(grid.Count).Append(" cells (index: date  enabled  text)\n");
        for (int i = 0; i < grid.Count; i++)
        {
            var cell = grid[i];
            if (cell == null || cell.Index < 0 || cell.Index >= dates.Count) continue;
            var cd = dates[cell.Index];
            sb.Append("  [").Append(i).Append("] ").Append(cd.Year).Append('-').Append(cd.Month).Append('-').Append(cd.Day)
              .Append("  enabled=").Append(cell.enabledSelf)
              .Append("  text=\"").Append(FirstText(cell.Cast<VisualElement>(), 0)).Append("\"\n");
        }
    }

    private static void DumpEnabled(FM.UI.Widgets.DateSelectorWidget w, int year, StringBuilder sb)
    {
        var controller = w.m_controller;
        if (controller == null) { sb.Append("enabled: no controller (everything permitted)\n"); return; }
        sb.Append("enabled ").Append(year).Append(':');
        int count = 0;
        for (int m = 1; m <= 12; m++)
        {
            int days = DateTime.DaysInMonth(year, m);
            for (int d = 1; d <= days; d++)
                if (controller.IsDateEnabled(new Il2CppSystem.DateTime(year, m, d))) { sb.Append(' ').Append(d).Append('/').Append(m); count++; }
        }
        sb.Append("\n  total=").Append(count).Append('\n');
    }

    private static string Ints(Il2CppSystem.Collections.Generic.List<int> list)
    {
        if (list == null) return "null";
        var sb = new StringBuilder("[");
        for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(' '); sb.Append(list[i]); }
        return sb.Append(']').ToString();
    }

    private static FM.UI.Widgets.DateSelectorWidget Find(VisualElement el, int depth)
    {
        if (depth > 90 || !ElementBridge.IsVisible(el)) return null;
        var hit = el.TryCast<FM.UI.Widgets.DateSelectorWidget>();
        if (hit != null) return hit;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = Find(el.hierarchy[i], depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static string FirstText(VisualElement root, int depth)
    {
        if (depth > 12) return "";
        var text = root.TryCast<TextElement>();
        if (text != null && !string.IsNullOrEmpty(text.text)) return text.text;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            string found = FirstText(root.hierarchy[i], depth + 1);
            if (found.Length > 0) return found;
        }
        return "";
    }
}
