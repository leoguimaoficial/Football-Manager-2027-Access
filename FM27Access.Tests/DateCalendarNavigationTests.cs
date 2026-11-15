using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class DateCalendarNavigationTests
{
    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(3, -1, 1)]
    public void EmptyInterveningMonthDoesNotStrandRealChoices(int month, int direction, int expected)
        => Assert.Equal((2026, expected), DateCalendarNavigation.Find(2026, month, direction,
            new[] { 2026 }, (y, m) => m is 1 or 3));

    [Fact]
    public void SkipsMissingAndEmptyControllerYears()
        => Assert.Equal((2029, 2), DateCalendarNavigation.Find(2026, 12, 1,
            new[] { 2029, 2026, 2028, 2029, 0, 10000 }, (y, m) => y == 2029 && m == 2));

    [Fact]
    public void YearNavigationPrefersSameMonthInNextAvailableYear()
        => Assert.Equal((2028, 9), DateCalendarNavigation.Find(2026, 9, 1,
            new[] { 2026, 2028 }, (y, m) => y == 2028 && (m == 1 || m == 9), byYear: true));

    [Fact]
    public void YearNavigationUsesRealAlternativeMonthWhenSameMonthUnavailable()
        => Assert.Equal((2028, 4), DateCalendarNavigation.Find(2026, 9, 1,
            new[] { 2026, 2027, 2028 }, (y, m) => y == 2028 && m == 4, byYear: true));

    [Theory]
    [InlineData(1, -1)]
    [InlineData(9999, 1)]
    public void UnconstrainedNavigationDoesNotPassCalendarBounds(int year, int direction)
        => Assert.Null(DateCalendarNavigation.Find(year, direction < 0 ? 1 : 12,
            direction, Array.Empty<int>(), (_, _) => true));

    [Fact]
    public void MissingYearCatalogRetainsAdjacentNativeAvailability()
        => Assert.Equal((2027, 1), DateCalendarNavigation.Find(2026, 12, 1,
            Array.Empty<int>(), (y, m) => y == 2027 && m == 1));

    [Fact]
    public void NoAvailableControllerMonthsMeansNoInventedTarget()
        => Assert.Null(DateCalendarNavigation.Find(2026, 9, 1, new[] { 2026, 2027 }, (_, _) => false));
}
