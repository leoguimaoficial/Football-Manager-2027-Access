using System;
using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Pure date-string helpers for the HTML surface's date field. The field is a plain text box the
/// user types a day/month/year into, so the surface owns every value (nothing is silently
/// discarded by a native control). These helpers parse what was typed and format the current
/// value for display; calendar/availability validity is checked by callers. No game dependency,
/// so this lives in Navigation and is covered by the test project.
/// </summary>
public static class DateFieldText
{
    // Day/month/year for display, zero-padded (e.g. 01/10/1995). The order matches what the
    // field's label tells the user to type.
    public static string ToDmy(int year, int month, int day)
        => $"{day:D2}/{month:D2}/{year:D4}";

    // ISO yyyy-MM-dd, used only for diagnostic log lines (an unambiguous, sortable form).
    public static string ToIso(int year, int month, int day)
        => $"{year:D4}-{month:D2}-{day:D2}";

    // Parses a typed date in day-month-year order. Lenient on the separator (slash, dash, dot, or
    // space) and on leading zeros, so "1/10/1995", "01-10-1995", and "1 10 1995" all parse. Only
    // checks that there are three integer parts; whether they form a real, selectable date is the
    // caller's job (IsRealCalendarDate, then the controller's availability rule).
    public static bool TryParseDmy(string s, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var parts = s.Split(new[] { '/', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out day)) return false;
        if (!int.TryParse(parts[1], out month)) return false;
        if (!int.TryParse(parts[2], out year)) return false;

        return true;
    }

    // The number of next-month (positive) or previous-month (negative) button presses that move a
    // date picker's month dropdown from currentIndex onto targetMonth.
    //
    // The dropdown lists only the months the picker's controller allows for the selected year, so
    // the target is targetMonth's POSITION in that list, NOT targetMonth - 1: a friendly window
    // covering June to August is a three-item dropdown where July sits at index 1, and stepping to
    // index 6 walks off the end. An empty months list means the controller constrains nothing (or
    // its month set could not be read), and there the dropdown is the full January..December list
    // where position and month number do coincide.
    //
    // False means the step is not expressible: the month is not in the allowed set, or the dropdown
    // has never been populated (a negative currentIndex), which no amount of stepping repairs.
    // Callers pass a month already known to be 1..12 (see IsRealCalendarDate).
    public static bool TryGetMonthStepDelta(IReadOnlyList<int> months, int currentIndex, int targetMonth, out int delta)
    {
        delta = 0;
        if (currentIndex < 0) return false;

        int targetIndex = -1;
        if (months.Count == 0)
        {
            targetIndex = targetMonth - 1;
        }
        else
        {
            for (int i = 0; i < months.Count; i++)
                if (months[i] == targetMonth) { targetIndex = i; break; }
        }
        if (targetIndex < 0) return false;

        delta = targetIndex - currentIndex;
        return true;
    }

    // True when (year, month, day) is a real calendar date — month 1..12 and day within that
    // month's length (so Sept 31, Apr 31, Feb 29 in a non-leap year are rejected). It also guards
    // callers that build a DateTime from the parts (the constructor throws on an impossible day).
    public static bool IsRealCalendarDate(int year, int month, int day)
    {
        if (month < 1 || month > 12) return false;
        if (year < 1 || year > 9999) return false;
        if (day < 1) return false;
        return day <= DateTime.DaysInMonth(year, month);
    }
}
