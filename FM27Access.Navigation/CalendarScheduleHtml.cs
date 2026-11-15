using System.Collections.Generic;
using System.Text;

namespace FM27Access.Navigation;

/// <summary>
/// Pure HTML builder for the in-game schedule calendar (the month view, distinct from the date-picker
/// popup that <see cref="CalendarGrid"/> serves). The game lays the month out as a streamed list of
/// week rows; left to the generic walk it linearizes into a flat run of day cells and events, so a
/// screen reader loses the week/weekday structure. This assembles a semantic table: weekday column
/// headers, one row per week, one cell per weekday, each populated cell carrying its day-of-month,
/// its state, and the day's events. On the Training tab each row also gets a header cell holding that
/// week's training schedule control. Game-free — the per-day event HTML and the per-week focus HTML are
/// rendered live by the Plugin and passed in as opaque (already-escaped) strings — so the scaffolding
/// and escaping are unit-tested here. Weekday names are the calendar's internal (English) column names,
/// matching CalendarGrid's date-picker headers.
/// </summary>
public static class CalendarScheduleHtml
{
    // The seven weekday columns, Monday-first (the game's calendar week), in the order they render.
    // The index into this array is the column a day occupies; the strings double as the column
    // headers. These are the game's internal element names (BindingRemapper("Monday")...), so they
    // are stable code identifiers, not localized text.
    public static readonly string[] Weekdays =
        { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    // The column index (0..6) for a weekday name, or -1 when the name is not a weekday. The reader
    // uses this to place each day cell under its real weekday rather than in tree order.
    public static int WeekdayIndex(string weekday)
    {
        for (int i = 0; i < Weekdays.Length; i++)
            if (Weekdays[i] == weekday) return i;
        return -1;
    }

    // One day in a week row. DateText is the day-of-month as the game shows it; State is the
    // mod-authored status ("current date" / "already simulated" / ""); EventsHtml is the day's events
    // already rendered to safe HTML by the live renderer (injected verbatim, never re-escaped). A null
    // ScheduleDay slot is calendar padding (no day in that weekday column this week) and renders empty.
    public sealed class ScheduleDay
    {
        public readonly string DateText;
        public readonly string State;
        public readonly string EventsHtml;

        public ScheduleDay(string dateText, string state, string eventsHtml)
        {
            DateText = dateText;
            State = state;
            EventsHtml = eventsHtml;
        }
    }

    // A week as seven weekday slots (Monday..Sunday); a null slot is padding. FocusHtml is the week's
    // training schedule control, already rendered to safe HTML by the live renderer (injected verbatim,
    // never re-escaped) — empty on the calendar tabs that draw no such control.
    public sealed class ScheduleWeek
    {
        public readonly ScheduleDay[] Days = new ScheduleDay[7];
        public string FocusHtml;
    }

    // The row-header column carrying each week's training schedule. Mod-authored chrome, in the same
    // shape as the other pure builders' headers (LeagueHistoryHtml's Season/Division/Position): the mod
    // has no LocString facility, and these strings never come from the game. Named for the picker the
    // control opens, which the game titles "Training Schedule".
    public const string FocusColumnHeader = "Training Schedule";

    public static string Build(IReadOnlyList<ScheduleWeek> weeks) => Build(weeks, null);

    // Native captions are optional; internal weekday names remain stable column
    // keys and the inherited fallback when the game supplies no header text.
    public static string Build(IReadOnlyList<ScheduleWeek> weeks,
        [System.Diagnostics.CodeAnalysis.AllowNull] IReadOnlyList<string> weekdayLabels)
    {
        if (weekdayLabels != null && weekdayLabels.Count != Weekdays.Length)
            throw new System.ArgumentException("Calendar labels must match the seven weekday columns.", nameof(weekdayLabels));
        // The focus column exists only where the game draws focus cards (the Training tab). The General
        // and Fixtures tabs would otherwise gain a column of empty header cells — pure noise on a
        // surface where every cell costs a keystroke to skip.
        bool anyFocus = false;
        foreach (var week in weeks)
            if (!string.IsNullOrEmpty(week.FocusHtml)) { anyFocus = true; break; }

        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>");
        if (anyFocus)
            sb.Append("<th scope=\"col\">").Append(FocusColumnHeader).Append("</th>");
        for (int col = 0; col < Weekdays.Length; col++)
            sb.Append("<th scope=\"col\">").Append(Escape(
                weekdayLabels != null && !string.IsNullOrWhiteSpace(weekdayLabels[col])
                    ? weekdayLabels[col] : Weekdays[col])).Append("</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var week in weeks)
        {
            sb.Append("<tr>");
            // The week's schedule is the row's header, so a screen reader announces which week's
            // training a day belongs to as it moves between rows. Without it these rows have no header
            // of any kind and a day cell reads as a bare number. Kept even when this week has no
            // control (the column is present because another week does), so the columns stay aligned.
            if (anyFocus)
            {
                sb.Append("<th scope=\"row\">");
                if (!string.IsNullOrEmpty(week.FocusHtml)) sb.Append(week.FocusHtml);
                sb.Append("</th>");
            }
            for (int col = 0; col < 7; col++)
            {
                var day = week.Days[col];
                if (day == null) { sb.Append("<td></td>"); continue; }

                sb.Append("<td><div>").Append(Escape(day.DateText));
                if (!string.IsNullOrEmpty(day.State))
                    sb.Append(", ").Append(Escape(day.State));
                sb.Append("</div>");
                // Pre-rendered, already-escaped event markup from the live renderer; inject verbatim.
                if (!string.IsNullOrEmpty(day.EventsHtml))
                    sb.Append(day.EventsHtml);
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
