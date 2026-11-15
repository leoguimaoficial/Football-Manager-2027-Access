using System.Linq;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class CalendarGridTests
{
    [Fact]
    public void BuildWeeks_PlacesEveryDayExactlyOnce()
    {
        var weeks = CalendarGrid.BuildWeeks(2000, 2); // leap February, 29 days
        var days = weeks.SelectMany(w => w).Where(d => d != 0).ToList();
        Assert.Equal(29, days.Count);
        Assert.Equal(Enumerable.Range(1, 29), days);
    }

    [Fact]
    public void BuildWeeks_AlignsFirstDayToItsWeekday()
    {
        // 1 Feb 2000 was a Tuesday; Monday-first, so one lead blank then the 1st in the Tue column.
        var weeks = CalendarGrid.BuildWeeks(2000, 2);
        Assert.Equal(0, weeks[0][0]); // Monday cell blank
        Assert.Equal(1, weeks[0][1]); // Tuesday cell holds the 1st
    }

    [Fact]
    public void BuildWeeks_RejectsImpossibleLengthsViaCalendar()
    {
        Assert.Equal(28, CalendarGrid.BuildWeeks(1999, 2).SelectMany(w => w).Count(d => d != 0)); // non-leap Feb
        Assert.Equal(30, CalendarGrid.BuildWeeks(1995, 9).SelectMany(w => w).Count(d => d != 0)); // September
        Assert.Equal(31, CalendarGrid.BuildWeeks(1995, 12).SelectMany(w => w).Count(d => d != 0)); // December
    }

    [Fact]
    public void BuildWeeks_EveryRowHasSevenCells()
    {
        var weeks = CalendarGrid.BuildWeeks(1995, 10);
        Assert.All(weeks, w => Assert.Equal(7, w.Length));
    }

    [Fact]
    public void BuildPanel_RendersGridLabelAndSelectedDay()
    {
        var enabled = AllEnabled(1995, 10);
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, selectedDay: 12,
            prevYear: true, nextYear: true, prevMonth: true, nextMonth: true);

        Assert.Contains("October 1995", html);
        Assert.Contains("role=\"grid\"", html);
        Assert.Contains("role=\"gridcell\"", html);
        Assert.Contains("data-days=\"31\"", html);
        Assert.Contains("aria-current=\"date\"", html);
        Assert.Contains("fmcalpick('X','12/10/1995')", html);
        Assert.Contains("data-cy=\"1995\"", html);
        Assert.Contains("data-cm=\"10\"", html);
    }

    [Fact]
    public void BuildPanel_HasNoWeekdayHeaderRow()
    {
        var enabled = AllEnabled(1995, 10);
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, 1, true, true, true, true);
        Assert.DoesNotContain("<thead>", html);
    }

    [Fact]
    public void BuildPanel_DayCarriesFullDateWithWeekdayAndYear()
    {
        var enabled = AllEnabled(1995, 10);
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, 12, true, true, true, true);

        string weekday = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat
            .GetDayName(new System.DateTime(1995, 10, 12).DayOfWeek);
        Assert.Contains("aria-label=\"" + weekday + " October 12 1995\"", html);
    }

    [Fact]
    public void BuildPanel_SelectedDayHoldsTheRovingTabindex()
    {
        var enabled = AllEnabled(1995, 10);
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, 12, true, true, true, true);
        Assert.Contains("data-day=\"12\" tabindex=\"0\"", html);
        Assert.Contains("data-day=\"11\" tabindex=\"-1\"", html);
    }

    [Fact]
    public void BuildPanel_DisabledDayIsFocusableButCannotBePicked()
    {
        var enabled = AllEnabled(1995, 10);
        enabled[31] = false; // disable Oct 31
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, 1, true, true, true, true);

        Assert.Contains("data-day=\"31\"", html);          // still a navigable cell
        Assert.Contains("aria-disabled=\"true\" data-dis=\"1\"", html);
        Assert.DoesNotContain("fmcalpick('X','31/10/1995')", html);
    }

    // A screen reader reads a cell it sees as interactive by its accessible name (the full date)
    // but an inert one by its content (the bare day number), so an unavailable day needs a click
    // handler of its own or it announces as "31". It must be the refusal handler, never fmcalpick.
    [Fact]
    public void BuildPanel_DisabledDayCarriesTheRefusalClickHandlerAndItsFullDate()
    {
        var enabled = AllEnabled(1995, 10);
        enabled[31] = false;
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, 1, true, true, true, true);

        string weekday = new System.DateTime(1995, 10, 31).DayOfWeek.ToString();
        Assert.Contains("aria-label=\"" + weekday + " October 31 1995\"", html);
        Assert.Contains("data-dis=\"1\" onclick=\"fmcaldis()\"", html);
    }

    // The reverse: a selectable day is picked by its handler and must not carry the refusal one.
    [Fact]
    public void BuildPanel_EnabledDayCarriesOnlyThePickHandler()
    {
        var enabled = AllEnabled(1995, 10);
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, 1, true, true, true, true);

        Assert.Contains("fmcalpick('X','31/10/1995')", html);
        Assert.DoesNotContain("fmcaldis()", html);
    }

    [Fact]
    public void BuildPanel_DisablesNavButtonsAtBounds()
    {
        var enabled = AllEnabled(1995, 10);
        string html = CalendarGrid.BuildPanel("X", 1995, 10, enabled, 1,
            prevYear: false, nextYear: true, prevMonth: false, nextMonth: true);

        // The previous-year button is present but disabled and carries no click handler.
        Assert.Contains("id=\"X-py\" disabled", html);
        Assert.Contains("id=\"X-pm\" disabled", html);
        Assert.Contains("fmcalnav('X',1,0)", html);  // next year still active
        Assert.Contains("fmcalnav('X',0,1)", html);  // next month still active
    }

    private static bool[] AllEnabled(int year, int month)
    {
        int days = System.DateTime.DaysInMonth(year, month);
        var e = new bool[days + 1];
        for (int d = 1; d <= days; d++) e[d] = true;
        return e;
    }
}
