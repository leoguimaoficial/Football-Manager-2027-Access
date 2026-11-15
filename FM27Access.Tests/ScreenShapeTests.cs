using System;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class ScreenShapeTests
{
    private static (string, int)[] Nodes(params (string name, int childCount)[] n) => n;

    [Fact]
    public void Empty_IsStable()
    {
        Assert.Equal(ScreenShape.Compose(Array.Empty<(string, int)>()),
                     ScreenShape.Compose(Array.Empty<(string, int)>()));
        // Empty must be distinguishable from a one-node shape (the count prefix guarantees it).
        Assert.NotEqual(ScreenShape.Compose(Array.Empty<(string, int)>()),
                        ScreenShape.Compose(Nodes(("root", 0))));
    }

    [Fact]
    public void SameShape_ProducesSameSignature()
    {
        var a = ScreenShape.Compose(Nodes(("panel", 2), ("title", 0), ("list", 3)));
        var b = ScreenShape.Compose(Nodes(("panel", 2), ("title", 0), ("list", 3)));
        Assert.Equal(a, b);
    }

    [Fact]
    public void AddedNode_ChangesSignature()
    {
        // The core case: content populating after the panel was "ready".
        var empty = ScreenShape.Compose(Nodes(("panel", 0)));
        var filled = ScreenShape.Compose(Nodes(("panel", 1), ("row", 0)));
        Assert.NotEqual(empty, filled);
    }

    [Fact]
    public void RemovedNode_ChangesSignature()
    {
        var a = ScreenShape.Compose(Nodes(("panel", 2), ("a", 0), ("b", 0)));
        var b = ScreenShape.Compose(Nodes(("panel", 1), ("a", 0)));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ReorderedNodes_ChangeSignature()
    {
        var a = ScreenShape.Compose(Nodes(("a", 0), ("b", 0)));
        var b = ScreenShape.Compose(Nodes(("b", 0), ("a", 0)));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ChangedChildCount_ChangesSignature()
    {
        // A subtree gaining children (content filling in) must register even if names repeat.
        var a = ScreenShape.Compose(Nodes(("list", 0)));
        var b = ScreenShape.Compose(Nodes(("list", 5)));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ChangedName_ChangesSignature()
    {
        var a = ScreenShape.Compose(Nodes(("foo", 1)));
        var b = ScreenShape.Compose(Nodes(("bar", 1)));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EmptyNames_AreHandled()
    {
        // Unnamed wrapper elements (the common case) contribute "" names; the shape still depends
        // on their count and branching, and equal inputs stay equal.
        var a = ScreenShape.Compose(Nodes(("", 2), ("", 0), ("", 0)));
        var b = ScreenShape.Compose(Nodes(("", 2), ("", 0), ("", 0)));
        Assert.Equal(a, b);
        Assert.NotEqual(a, ScreenShape.Compose(Nodes(("", 2), ("", 0))));
    }
}
