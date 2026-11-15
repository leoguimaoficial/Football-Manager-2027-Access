using System;
using System.Collections.Generic;
using System.Linq;

namespace FM27Access.Plugin.Server;

// Navigation targets come only from the controller's real years and per-month
// availability. An empty intervening month/year must not strand later choices.
internal static class DateCalendarNavigation
{
    internal static (int Year, int Month)? Find(int year, int month, int direction,
        IEnumerable<int> controllerYears, Func<int, int, bool> available, bool byYear = false)
    {
        if (year < 1 || year > 9999 || month < 1 || month > 12 || direction == 0) return null;
        direction = Math.Sign(direction);
        var years = controllerYears.Where(y => y >= 1 && y <= 9999).Distinct().OrderBy(y => y).ToList();
        if (years.Count == 0)
        {
            // An unconstrained controller has no year list. Probe its adjacent
            // period as before, without inventing a range for an unreadable one.
            int y = year, m = month;
            if (byYear) y += direction;
            else { m += direction; if (m == 0) { m = 12; y--; } else if (m == 13) { m = 1; y++; } }
            return y >= 1 && y <= 9999 && available(y, m) ? (y, m) : null;
        }
        if (direction < 0) years.Reverse();
        foreach (var y in years)
        {
            if (byYear)
            {
                if (direction > 0 ? y <= year : y >= year) continue;
                if (available(y, month)) return (y, month);
            }
            for (int i = 0; i < 12; i++)
            {
                int m = direction > 0 ? i + 1 : 12 - i;
                if (!byYear && (direction > 0 ? y < year || (y == year && m <= month) : y > year || (y == year && m >= month))) continue;
                if (available(y, m)) return (y, m);
            }
        }
        return null;
    }
}
