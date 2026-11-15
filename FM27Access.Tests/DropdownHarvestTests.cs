using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class DropdownHarvestTests
{
    [Fact]
    public void AvailabilityStitchesAbsoluteRecycledWindowsWithTheirLabels()
    {
        var labels = new List<string> { "Zero", "One" };
        var disabled = new List<bool> { true, false };
        var window = new Dictionary<int, string> { [1] = "One", [2] = "Two", [3] = "Three" };
        var states = new Dictionary<int, bool> { [1] = false, [2] = true, [3] = false };
        Assert.Equal(2, DropdownHarvest.AppendContiguous(labels, disabled, window, states));
        Assert.Equal(new[] { "Zero", "One", "Two", "Three" }, labels);
        Assert.Equal(new[] { true, false, true, false }, disabled);
    }

    [Fact]
    public void MissingAvailabilityIsAGapRatherThanImplicitPermission()
    {
        var labels = new List<string>();
        var disabled = new List<bool>();
        var window = new Dictionary<int, string> { [0] = "Zero", [1] = "One", [2] = "Two" };
        var states = new Dictionary<int, bool> { [0] = false, [2] = false };
        Assert.Equal(1, DropdownHarvest.AppendContiguous(labels, disabled, window, states));
        Assert.Equal(new[] { "Zero" }, labels);
        Assert.Equal(new[] { false }, disabled);
    }

    [Fact]
    public void MisalignedHarvestIsRejectedWithoutAddingOptions()
    {
        var labels = new List<string> { "Zero" };
        var disabled = new List<bool>();
        Assert.Throws<System.ArgumentException>(() => DropdownHarvest.AppendContiguous(labels, disabled,
            new Dictionary<int, string> { [1] = "One" }, new Dictionary<int, bool> { [1] = false }));
        Assert.Single(labels);
        Assert.Empty(disabled);
    }

    [Fact]
    public void AppendContiguous_StitchesFromTheHarvestFrontier()
    {
        // Choices already holds 0..1; a window covering 2..4 extends it in order.
        var choices = new List<string> { "Afrikaans", "Albanian" };
        var window = new Dictionary<int, string> { { 2, "Arabic" }, { 3, "Armenian" }, { 4, "Basque" } };

        int added = DropdownHarvest.AppendContiguous(choices, window);

        Assert.Equal(3, added);
        Assert.Equal(new[] { "Afrikaans", "Albanian", "Arabic", "Armenian", "Basque" }, choices);
    }

    [Fact]
    public void AppendContiguous_StopsAtTheFirstGap()
    {
        // The render window can extend past the frontier with rows that aren't yet harvestable
        // contiguously (here index 1 is missing). Those must wait for the next scroll, not be
        // appended out of order.
        var choices = new List<string> { "Afrikaans" };
        var window = new Dictionary<int, string> { { 1, "Albanian" }, { 3, "Armenian" } };

        int added = DropdownHarvest.AppendContiguous(choices, window);

        Assert.Equal(1, added);
        Assert.Equal(new[] { "Afrikaans", "Albanian" }, choices);
    }

    [Fact]
    public void AppendContiguous_AppendsNothingWhenWindowMissesFrontier()
    {
        // A window that doesn't reach the frontier (scroll hasn't landed / row unreadable)
        // makes no progress, which the harvester treats as a hard failure.
        var choices = new List<string> { "Afrikaans" };
        var window = new Dictionary<int, string> { { 5, "Basque" }, { 6, "Bengali" } };

        int added = DropdownHarvest.AppendContiguous(choices, window);

        Assert.Equal(0, added);
        Assert.Single(choices);
    }

    [Fact]
    public void AppendContiguous_IgnoresAlreadyHarvestedRows()
    {
        // Scrolling to the frontier leaves earlier rows in the window too; they must not be
        // re-appended as duplicates.
        var choices = new List<string> { "Afrikaans", "Albanian", "Arabic" };
        var window = new Dictionary<int, string> { { 1, "Albanian" }, { 2, "Arabic" }, { 3, "Armenian" } };

        int added = DropdownHarvest.AppendContiguous(choices, window);

        Assert.Equal(1, added);
        Assert.Equal(new[] { "Afrikaans", "Albanian", "Arabic", "Armenian" }, choices);
    }
}
