using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public class NavigatorEntryPolicyTests
{
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "", true)]
    [InlineData(false, "   ", true)]
    [InlineData(false, "Sub-23", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "Sub-23", false)]
    public void IsInactivePlaceholder_OnlySkipsBlankConditionOffRows(
        bool conditionMet, string? label, bool expected)
    {
        Assert.Equal(expected, NavigatorEntryPolicy.IsInactivePlaceholder(conditionMet, label!));
    }
}
