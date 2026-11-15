using FM27Access.Navigation;

namespace FM27Access.Tests;

public class DateFieldTextTests
{
    [Fact]
    public void ToDmy_PadsDayMonthYear()
    {
        Assert.Equal("01/10/1995", DateFieldText.ToDmy(1995, 10, 1));
        Assert.Equal("09/02/0875", DateFieldText.ToDmy(875, 2, 9));
    }

    [Fact]
    public void ToIso_PadsToFixedWidth()
    {
        Assert.Equal("1995-10-01", DateFieldText.ToIso(1995, 10, 1));
    }

    [Theory]
    [InlineData("01/10/1995")]
    [InlineData("1/10/1995")]    // no leading zeros
    [InlineData("01-10-1995")]   // dash separator
    [InlineData("01.10.1995")]   // dot separator
    [InlineData("1 10 1995")]    // space separator
    public void TryParseDmy_AcceptsDayMonthYearWithAnySeparator(string input)
    {
        Assert.True(DateFieldText.TryParseDmy(input, out var y, out var m, out var d));
        Assert.Equal((1995, 10, 1), (y, m, d));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("01/10")]        // too few parts
    [InlineData("01/10/1995/00")] // too many parts
    [InlineData("aa/10/1995")]   // non-numeric day
    [InlineData("01/bb/1995")]   // non-numeric month
    public void TryParseDmy_RejectsMalformed(string input)
    {
        Assert.False(DateFieldText.TryParseDmy(input, out _, out _, out _));
    }

    [Theory]
    [InlineData(1995, 10, 1)]   // ordinary date
    [InlineData(2000, 2, 29)]   // leap day in a leap year
    [InlineData(1999, 12, 31)]  // 31-day month
    public void IsRealCalendarDate_AcceptsRealDates(int y, int m, int d)
    {
        Assert.True(DateFieldText.IsRealCalendarDate(y, m, d));
    }

    [Theory]
    [InlineData(1995, 9, 31)]   // September has 30 days
    [InlineData(1995, 4, 31)]   // April has 30 days
    [InlineData(1999, 2, 29)]   // not a leap year
    [InlineData(1900, 2, 29)]   // century non-leap year
    [InlineData(1995, 13, 1)]   // month out of range
    [InlineData(1995, 0, 1)]    // month zero
    [InlineData(1995, 1, 0)]    // day zero
    [InlineData(1995, 1, 32)]   // day past any month
    public void IsRealCalendarDate_RejectsImpossibleDates(int y, int m, int d)
    {
        Assert.False(DateFieldText.IsRealCalendarDate(y, m, d));
    }

    // A filtered month dropdown (Arrange Friendly's window covering June to August) is three items
    // long, so the target is the month's position in the set. Stepping to month - 1 would walk off
    // the end of the list.
    [Theory]
    [InlineData(6, 0, 0)]   // June is index 0, already there
    [InlineData(7, 0, 1)]   // July is index 1, not 6
    [InlineData(8, 0, 2)]
    [InlineData(6, 2, -2)]  // stepping backwards from August
    public void TryGetMonthStepDelta_StepsByPositionInAFilteredSet(int targetMonth, int currentIndex, int expected)
    {
        var months = new[] { 6, 7, 8 };
        Assert.True(DateFieldText.TryGetMonthStepDelta(months, currentIndex, targetMonth, out var delta));
        Assert.Equal(expected, delta);
    }

    // The unconstrained case, and the regression guard for the bounded (birth-date) picker whose
    // dropdown is the full year: position and month number coincide, so the delta must match the
    // month - 1 arithmetic this replaced.
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(7, 0, 6)]
    [InlineData(12, 11, 0)]
    [InlineData(1, 11, -11)]
    public void TryGetMonthStepDelta_FullYearMatchesMonthMinusOne(int targetMonth, int currentIndex, int expected)
    {
        var all = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        Assert.True(DateFieldText.TryGetMonthStepDelta(all, currentIndex, targetMonth, out var fromSet));
        Assert.Equal(expected, fromSet);

        // An unreadable month set falls back to the same arithmetic.
        Assert.True(DateFieldText.TryGetMonthStepDelta(new int[0], currentIndex, targetMonth, out var fromEmpty));
        Assert.Equal(expected, fromEmpty);
    }

    [Fact]
    public void TryGetMonthStepDelta_RejectsAMonthOutsideTheSet()
    {
        Assert.False(DateFieldText.TryGetMonthStepDelta(new[] { 6, 7, 8 }, 0, 9, out var delta));
        Assert.Equal(0, delta);
    }

    // A dropdown that has never been populated reports index -1; no number of steps reaches a month
    // from there, so the caller must be told rather than handed an off-by-one delta.
    [Fact]
    public void TryGetMonthStepDelta_RejectsAnUnpopulatedDropdown()
    {
        Assert.False(DateFieldText.TryGetMonthStepDelta(new[] { 6, 7, 8 }, -1, 7, out var delta));
        Assert.Equal(0, delta);
    }
}
