using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// Pins each <see cref="ContainerEntry"/> strategy the cursor applies when a move lands
/// on a container element: tab groups wrap or redirect to their entry element, button
/// bars redirect only when entered from outside, calendars redirect to their first child
/// only when entered downward.
/// </summary>
public class ContainerEntryBehaviorTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private IAccessibleElement _nextStopResult;
    private IAccessibleElement _firstStopResult;
    private readonly TestHostSpec _host;

    public ContainerEntryBehaviorTests()
    {
        _host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            FindFirst = _ => _firstStopResult,
            FindNext = (_, _) => _nextStopResult,
        };
        _host.Apply();
    }

    public void Dispose() => NavigationManager.ResetForReload();

    private void SetCursor(IAccessibleElement element)
    {
        _firstStopResult = element;
        NavigationManager.OnPanelReady(new object(), "Test Panel", false);
        _spoken.Clear();
    }

    [Fact]
    public void TabGroup_WrapForContext_ReplacesLandedElement()
    {
        var landed = new FakeElement("General");
        var wrapped = new FakeElement("General wrapped");
        var context = new FakeEntryContext { WrapResult = wrapped };
        SetCursor(new FakeElement("before"));
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.TabGroupKind(el => el == landed ? context : null),
        };
        _host.Apply();
        _nextStopResult = landed;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Equal(wrapped, NavigationManager.GetCurrentElement());
    }

    [Fact]
    public void TabGroup_EntryElement_UsedWhenWrapReturnsNull()
    {
        var landed = new FakeElement("General");
        var entry = new FakeElement("first tab");
        var context = new FakeEntryContext { EntryResult = entry };
        SetCursor(new FakeElement("before"));
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.TabGroupKind(el => el == landed ? context : null),
        };
        _host.Apply();
        _nextStopResult = landed;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Equal(entry, NavigationManager.GetCurrentElement());
    }

    [Fact]
    public void ButtonBar_EntryElement_UsedWhenEnteringFromOutside()
    {
        var landed = new FakeElement("second button");
        var entry = new FakeElement("first button");
        var context = new FakeEntryContext { EntryResult = entry };
        SetCursor(new FakeElement("before"));
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.ButtonBarKind(el => el == landed || el == entry ? context : null),
        };
        _host.Apply();
        _nextStopResult = landed;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Equal(entry, NavigationManager.GetCurrentElement());
    }

    [Fact]
    public void ButtonBar_NoRedirect_WhenAlreadyInside()
    {
        var first = new FakeElement("first button");
        var second = new FakeElement("second button");
        var context = new FakeEntryContext { EntryResult = first };
        SetCursor(new FakeElement("before"));
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.ButtonBarKind(el => el == first || el == second ? context : null),
        };
        _host.Apply();
        _nextStopResult = first;
        NavigationManager.MoveCursor(NavigationDirection.Down); // enter the bar

        _nextStopResult = second;
        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Equal(second, NavigationManager.GetCurrentElement());
    }

    [Fact]
    public void Calendar_FirstChild_UsedWhenEnteringDownward()
    {
        var landed = new FakeElement("day 15");
        var firstChild = new FakeElement("day 1");
        var context = new FakeEntryContext { FirstChildResult = firstChild };
        SetCursor(new FakeElement("before"));
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.CalendarKind(el => el == landed ? context : null),
        };
        _host.Apply();
        _nextStopResult = landed;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Equal(firstChild, NavigationManager.GetCurrentElement());
    }

    [Fact]
    public void Calendar_NoRedirect_WhenEnteringUpward()
    {
        var landed = new FakeElement("day 15");
        var firstChild = new FakeElement("day 1");
        var context = new FakeEntryContext { FirstChildResult = firstChild };
        SetCursor(new FakeElement("after"));
        _host.ContainerKinds = new List<ContainerKind>
        {
            TestHostSpec.CalendarKind(el => el == landed ? context : null),
        };
        _host.Apply();
        _nextStopResult = landed;

        NavigationManager.MoveCursor(NavigationDirection.Up);

        Assert.Equal(landed, NavigationManager.GetCurrentElement());
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

    private class FakeEntryContext : IContainerContext
    {
        public IAccessibleElement WrapResult { get; init; }
        public IAccessibleElement EntryResult { get; init; }
        public IAccessibleElement FirstChildResult { get; init; }

        public object Scope { get; } = new();
        public IAccessibleElement GetFirstChild() => FirstChildResult;
        public bool HandlesDirection(NavigationDirection direction) => false;
        public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction) => null;
        public IAccessibleElement WrapForContext(IAccessibleElement element) => WrapResult;
        public IAccessibleElement GetEntryElement(NavigationDirection direction) => EntryResult;
    }
}
