using System;
using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class LiveSnapshotTests
{
    // Minimal accessible element whose read either returns a value or throws, so a snapshot
    // read failure can be simulated per element without the game.
    private sealed class FakeElement : IAccessibleElement
    {
        private readonly string _value;
        private readonly bool _throws;
        public FakeElement(string value, bool throws = false) { _value = value; _throws = throws; }

        public string GetLabel() => _throws ? throw new InvalidOperationException("element destroyed") : _value;

        public string Role => "text";
        public bool CanActivate => false;
        public bool HasContextMenu => false;
        public string GetState() => "";
        public string GetAnnouncement() => _value;
        public string GetPrimaryLabel() => _value;
        public string GetDescription() => "";
        public void Activate() => throw new NotSupportedException();
        public bool IsVisible() => true;
    }

    private static readonly Func<IAccessibleElement, string> Read = el => el.GetLabel();

    [Fact]
    public void FailingElement_IsIsolated_OthersStillRead()
    {
        var sources = new Dictionary<string, IAccessibleElement>
        {
            ["a"] = new FakeElement("alpha"),
            ["b"] = new FakeElement("beta", throws: true),
            ["c"] = new FakeElement("gamma"),
        };

        var snap = LiveSnapshot.Build(sources, Read, out var failures);

        Assert.Equal("alpha", snap["a"]);
        Assert.Equal("gamma", snap["c"]);
        Assert.False(snap.ContainsKey("b"));

        Assert.NotNull(failures);
        var failure = Assert.Single(failures);
        Assert.Equal("b", failure.Key);
        Assert.IsType<InvalidOperationException>(failure.Error);
    }

    [Fact]
    public void AllReadable_NoFailures()
    {
        var sources = new Dictionary<string, IAccessibleElement>
        {
            ["a"] = new FakeElement("alpha"),
            ["b"] = new FakeElement("beta"),
        };

        var snap = LiveSnapshot.Build(sources, Read, out var failures);

        Assert.Null(failures);
        Assert.Equal(new Dictionary<string, string> { ["a"] = "alpha", ["b"] = "beta" }, snap);
    }

    [Fact]
    public void EveryElementFails_AllRecorded_NoEarlyBail()
    {
        var sources = new Dictionary<string, IAccessibleElement>
        {
            ["a"] = new FakeElement("alpha", throws: true),
            ["b"] = new FakeElement("beta", throws: true),
        };

        var snap = LiveSnapshot.Build(sources, Read, out var failures);

        Assert.Empty(snap);
        Assert.NotNull(failures);
        Assert.Equal(2, failures.Count);
    }
}
