using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class ValueDiffTests
{
    [Fact]
    public void NoChanges_ReturnsEmpty()
    {
        var prev = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var curr = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        Assert.Empty(ValueDiff.Changed(prev, curr));
    }

    [Fact]
    public void ChangedValue_IsIncluded()
    {
        var prev = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var curr = new Dictionary<string, string> { ["a"] = "1", ["b"] = "9" };
        var diff = ValueDiff.Changed(prev, curr);
        Assert.Equal(new Dictionary<string, string> { ["b"] = "9" }, diff);
    }

    [Fact]
    public void NewId_IsIncluded()
    {
        var prev = new Dictionary<string, string> { ["a"] = "1" };
        var curr = new Dictionary<string, string> { ["a"] = "1", ["c"] = "3" };
        var diff = ValueDiff.Changed(prev, curr);
        Assert.Equal(new Dictionary<string, string> { ["c"] = "3" }, diff);
    }

    [Fact]
    public void RemovedId_IsNotIncluded()
    {
        // A vanished element is a structural change, handled by a refresh elsewhere.
        var prev = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var curr = new Dictionary<string, string> { ["a"] = "1" };
        Assert.Empty(ValueDiff.Changed(prev, curr));
    }

    [Fact]
    public void EmptyPrev_ReturnsAll()
    {
        var prev = new Dictionary<string, string>();
        var curr = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        Assert.Equal(curr, ValueDiff.Changed(prev, curr));
    }
}
