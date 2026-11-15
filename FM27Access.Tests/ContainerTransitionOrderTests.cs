using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// Pins the exact composition of container enter/exit transition prefixes.
/// Each transition prepends to the announcement in application order
/// (table, tab selection, button bar, calendar), so the spoken order is the
/// reverse: calendar first, table last, then the element's own announcement.
/// </summary>
public class ContainerTransitionOrderTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private IAccessibleElement _nextStopResult;
    private readonly TestHostSpec _host;

    public ContainerTransitionOrderTests()
    {
        _host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            FindNext = (_, _) => _nextStopResult,
        };
        _host.Apply();
    }

    public void Dispose() => NavigationManager.ResetForReload();

    private void SetCursor(IAccessibleElement element)
    {
        var wired = _host.FindFirst;
        _host.FindFirst = _ => element;
        _host.Apply();
        NavigationManager.OnPanelReady(new object(), "Test Panel", false);
        _host.FindFirst = wired;
        _host.Apply();
        _spoken.Clear();
    }

    private void MarkInsideAllFourContainers(IAccessibleElement inside)
    {
        // All four canonical kinds report the element inside them. The fake context's wrap
        // and entry lookups return null, so the entry behaviors pass the element through
        // unchanged and only the transition prefixes are exercised.
        var context = new FakeTabContext();
        Func<IAccessibleElement, IContainerContext> finder = el => el == inside ? context : null;
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.TableKind(finder),
            TestHostSpec.TabGroupKind(finder),
            TestHostSpec.ButtonBarKind(finder),
            TestHostSpec.CalendarKind(finder),
        };
        _host.Apply();
    }

    [Fact]
    public void EnteringAllFourContainersAtOnce_SpeaksPrefixesInReverseApplicationOrder()
    {
        var outside = new FakeElement("outside");
        var inside = new FakeElement("inside");
        SetCursor(outside);
        MarkInsideAllFourContainers(inside);
        _nextStopResult = inside;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Equal("calendar. button bar. tab selection. table. inside, button", _spoken[0]);
    }

    [Fact]
    public void ExitingAllFourContainersAtOnce_SpeaksPrefixesInReverseApplicationOrder()
    {
        var inside = new FakeElement("inside");
        var outside = new FakeElement("outside");
        SetCursor(new FakeElement("start"));
        MarkInsideAllFourContainers(inside);
        _nextStopResult = inside;
        NavigationManager.MoveCursor(NavigationDirection.Down); // enter all four
        _spoken.Clear();

        _nextStopResult = outside;
        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Equal("out of calendar. out of button bar. out of tab selection. out of table. outside, button", _spoken[0]);
    }

    [Fact]
    public void StayingInsideContainers_AddsNoPrefixes()
    {
        var inside = new FakeElement("inside");
        var alsoInside = new FakeElement("alsoInside");
        SetCursor(new FakeElement("start"));
        var context = new FakeTabContext();
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.TableKind(el => el == inside || el == alsoInside ? context : null),
        };
        _host.Apply();
        _nextStopResult = inside;
        NavigationManager.MoveCursor(NavigationDirection.Down); // enter table
        _spoken.Clear();

        _nextStopResult = alsoInside;
        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Equal("alsoInside, button", _spoken[0]);
    }

    private class FakeElement : IAccessibleElement
    {
        private readonly string _label;
        public FakeElement(string label) => _label = label;
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => _label;
        public string GetState() => "";
        public string GetAnnouncement() => $"{_label}, button";
        public string GetPrimaryLabel() => _label;
        public string GetDescription() => "";
        public void Activate() { }
        public bool IsVisible() => true;
    }

    private class FakeTabContext : IContainerContext
    {
        public object Scope => new object();
        public IAccessibleElement GetFirstChild() => null;
        public bool HandlesDirection(NavigationDirection direction) => false;
        public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction) => null;
    }
}
