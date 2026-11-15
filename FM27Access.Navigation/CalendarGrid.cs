using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// Pure builder for the HTML-surface calendar widget: the month's week/day layout (real-world
/// accurate via the .NET calendar) and the panel fragment the companion app renders. No game
/// dependency — availability per day and the nav bounds are passed in by the caller, which reads
/// them from the date selector controller — so this lives in Navigation and is unit-tested. Weeks
/// start on Monday (the game's calendar week).
/// </summary>
public static class CalendarGrid
{
    // The weeks of a month as rows of seven cells; 0 is a blank pad cell, otherwise the day number.
    // Monday-first, so the 1st is padded to fall under its real weekday and the grid is calendar
    // accurate (leap years and 28..31-day months come straight from DateTime).
    public static List<int[]> BuildWeeks(int year, int month)
    {
        int days = System.DateTime.DaysInMonth(year, month);
        int firstDow = (int)new System.DateTime(year, month, 1).DayOfWeek; // Sunday = 0 .. Saturday = 6
        int lead = (firstDow + 6) % 7;                                     // Monday = 0 .. Sunday = 6

        var weeks = new List<int[]>();
        var week = new int[7];
        int col = 0;
        while (col < lead) week[col++] = 0;
        for (int d = 1; d <= days; d++)
        {
            week[col++] = d;
            if (col == 7) { weeks.Add(week); week = new int[7]; col = 0; }
        }
        if (col > 0) { while (col < 7) week[col++] = 0; weeks.Add(week); }
        return weeks;
    }

    // The calendar panel fragment: the month/year label, prev/next year and month buttons (disabled
    // at the controller's bounds), and the day grid as an ARIA grid. Each day cell carries its full
    // date as aria-label ("Tuesday May 12 1995") and a data-day for arrow-key navigation; one cell
    // holds the roving tabindex (the selection, else day 1). Enabled days apply on click; blanks are
    // empty cells.
    //
    // A disabled day keeps a click handler of its own, which only announces the refusal. That is not
    // decoration: a screen reader reads a cell it sees as interactive by its accessible NAME (the
    // aria-label, i.e. the full date) but reads an inert one by its CONTENT (the bare day number).
    // Without the handler, every unavailable day in a sparse set announces as "15" — which is
    // invisible on a picker where all days are selectable, and useless on one where most are not.
    //
    // There are no weekday column headers — each cell announces its own weekday. enabledDays is 1-indexed
    // (length day-count + 1); selectedDay is 0 when the selection is in another month. id is a
    // DOM/JS-safe handle id, so no escaping is needed here.
    public static string BuildPanel(string id, int year, int month, bool[] enabledDays, int selectedDay,
        bool prevYear, bool nextYear, bool prevMonth, bool nextMonth)
    {
        string monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
        int entry = selectedDay > 0 ? selectedDay : 1;
        int dayCount = System.DateTime.DaysInMonth(year, month);
        var sb = new StringBuilder();

        sb.Append("<div class=\"fm-cal\" data-cal data-cy=\"").Append(year).Append("\" data-cm=\"").Append(month).Append("\">");
        sb.Append("<p data-cal-label tabindex=\"-1\">").Append(monthName).Append(' ').Append(year).Append("</p>");

        sb.Append("<p>");
        NavButton(sb, id, "-py", "fmcalnav('" + id + "',-1,0)", "Previous year", prevYear);
        NavButton(sb, id, "-ny", "fmcalnav('" + id + "',1,0)", "Next year", nextYear);
        NavButton(sb, id, "-pm", "fmcalnav('" + id + "',0,-1)", "Previous month", prevMonth);
        NavButton(sb, id, "-nm", "fmcalnav('" + id + "',0,1)", "Next month", nextMonth);
        sb.Append("</p>");

        sb.Append("<table role=\"grid\" data-cal-grid data-days=\"").Append(dayCount).Append("\"><tbody>");
        foreach (var week in BuildWeeks(year, month))
        {
            sb.Append("<tr role=\"row\">");
            foreach (var d in week)
            {
                if (d == 0) { sb.Append("<td role=\"gridcell\"></td>"); continue; }

                string weekday = CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(new System.DateTime(year, month, d).DayOfWeek);
                sb.Append("<td role=\"gridcell\" data-day=\"").Append(d)
                  .Append("\" tabindex=\"").Append(d == entry ? "0" : "-1")
                  .Append("\" aria-label=\"").Append(weekday).Append(' ').Append(monthName).Append(' ').Append(d).Append(' ').Append(year).Append('"');
                if (d == selectedDay) sb.Append(" aria-current=\"date\"");
                if (d < enabledDays.Length && enabledDays[d])
                    sb.Append(" onclick=\"fmcalpick('").Append(id).Append("','").Append(DateFieldText.ToDmy(year, month, d)).Append("')\"");
                else
                    sb.Append(" aria-disabled=\"true\" data-dis=\"1\" onclick=\"fmcaldis()\"");
                sb.Append('>').Append(d).Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    private static void NavButton(StringBuilder sb, string id, string suffix, string onclick, string label, bool enabled)
    {
        sb.Append("<button type=\"button\" id=\"").Append(id).Append(suffix).Append("\"");
        if (enabled) sb.Append(" onclick=\"").Append(onclick).Append("\"");
        else sb.Append(" disabled");
        sb.Append('>').Append(label).Append("</button> ");
    }
}
