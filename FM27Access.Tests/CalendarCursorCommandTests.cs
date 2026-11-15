using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// Pins the calendar cursor commands (MoveCalendarDay, MoveCalendarInnerStop,
/// IsCurrentInCalendar). The in-game baseline walk cannot cover these: the live
/// calendar grid rebuilds continuously and never yields a byte-stable transcript,
/// so the command behavior is pinned here instead.
/// </summary>
public class CalendarCursorCommandTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private FakeCalendarContext _calendarContext;
    private readonly TestHostSpec _host;

    public CalendarCursorCommandTests()
    {
        _host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            ContainerKinds = { TestHostSpec.CalendarKind(_ => _calendarContext) },
        };
        _host.Apply();
    }

    public void Dispose() => NavigationManager.ResetForReload();

    private void SetCursor(IAccessibleElement element)
    {
        _host.FindFirst = _ => element;
        _host.Apply();
        NavigationManager.OnPanelReady(new object(), "Test Panel", false);
        _spoken.Clear();
    }

    [Fact]
    public void MoveCalendarDay_NavigatesViaCalendarContext()
    {
        var start = new FakeElement("day 11");
        var target = new FakeElement("day 12");
        SetCursor(start);
        _calendarContext = new FakeCalendarContext { NavigateResult = target };

        NavigationManager.MoveCalendarDay(NavigationDirection.Right);

        Assert.Equal(target, NavigationManager.GetCurrentElement());
        Assert.Single(_spoken);
        Assert.Contains("day 12", _spoken[0]);
    }

    [Fact]
    public void MoveCalendarDay_AtBoundaryWithExit_LeavesCalendar()
    {
        var start = new FakeElement("day 30");
        var after = new FakeElement("after calendar");
        var scope = new object();
        SetCursor(start);
        _calendarContext = new FakeCalendarContext
        {
            NavigateResult = null,
            ExitsOnBoundaryResult = true,
            ScopeObject = scope,
        };
        _host.FindNextAfterContainer = (s, _) => s == scope ? after : null;
        _host.Apply();

        NavigationManager.MoveCalendarDay(NavigationDirection.Down);

        Assert.Equal(after, NavigationManager.GetCurrentElement());
        Assert.Single(_spoken);
        Assert.Contains("after calendar", _spoken[0]);
    }

    [Fact]
    public void MoveCalendarDay_AtBoundaryWithoutExit_StaysPut()
    {
        var start = new FakeElement("day 1");
        SetCursor(start);
        _calendarContext = new FakeCalendarContext { NavigateResult = null };

        NavigationManager.MoveCalendarDay(NavigationDirection.Up);

        Assert.Equal(start, NavigationManager.GetCurrentElement());
        Assert.Empty(_spoken);
    }

    [Fact]
    public void MoveCalendarDay_OutsideCalendar_DoesNothing()
    {
        var start = new FakeElement("plain button");
        SetCursor(start);
        _calendarContext = null;

        NavigationManager.MoveCalendarDay(NavigationDirection.Down);

        Assert.Equal(start, NavigationManager.GetCurrentElement());
        Assert.Empty(_spoken);
    }

    [Fact]
    public void MoveCalendarInnerStop_MovesBetweenStopsWithinDay()
    {
        var start = new FakeElement("day 11");
        var inner = new FakeElement("Match");
        SetCursor(start);
        _calendarContext = new FakeCalendarContext { NavigateInnerResult = inner };

        NavigationManager.MoveCalendarInnerStop(NavigationDirection.Right);

        Assert.Equal(inner, NavigationManager.GetCurrentElement());
        Assert.Single(_spoken);
        Assert.Contains("Match", _spoken[0]);
    }

    [Fact]
    public void MoveCalendarInnerStop_AtEdge_SpeaksBoundaryAnnouncement()
    {
        var start = new FakeElement("day 11");
        SetCursor(start);
        _calendarContext = new FakeCalendarContext
        {
            NavigateInnerResult = null,
            BoundaryAnnouncement = "edge of day",
        };

        NavigationManager.MoveCalendarInnerStop(NavigationDirection.Right);

        Assert.Equal(start, NavigationManager.GetCurrentElement());
        Assert.Single(_spoken);
        Assert.Equal("edge of day", _spoken[0]);
    }

    [Fact]
    public void MoveCalendarInnerStop_AtEdgeWithoutAnnouncement_IsSilent()
    {
        var start = new FakeElement("day 11");
        SetCursor(start);
        _calendarContext = new FakeCalendarContext { NavigateInnerResult = null };

        NavigationManager.MoveCalendarInnerStop(NavigationDirection.Left);

        Assert.Empty(_spoken);
    }

    [Fact]
    public void IsCurrentInCalendar_ReflectsContext()
    {
        var start = new FakeElement("day 11");
        SetCursor(start);

        _calendarContext = new FakeCalendarContext();
        Assert.True(NavigationManager.IsCurrentInCalendar());

        _calendarContext = null;
        Assert.False(NavigationManager.IsCurrentInCalendar());
    }

    [Fact]
    public void IsCurrentInCalendar_FalseWithoutCursor()
    {
        Assert.False(NavigationManager.IsCurrentInCalendar());
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

    private class FakeCalendarContext : IContainerContext
    {
        public IAccessibleElement NavigateResult { get; init; }
        public IAccessibleElement NavigateInnerResult { get; init; }
        public bool ExitsOnBoundaryResult { get; init; }
        public string BoundaryAnnouncement { get; init; }
        public object ScopeObject { get; init; } = new();

        public object Scope => ScopeObject;
        public IAccessibleElement GetFirstChild() => null;
        public bool HandlesDirection(NavigationDirection direction) => true;
        public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
            => NavigateResult;
        public bool ExitsOnBoundary(NavigationDirection direction) => ExitsOnBoundaryResult;
        public IAccessibleElement NavigateInnerCell(IAccessibleElement current, NavigationDirection direction)
            => NavigateInnerResult;
        public string GetBoundaryAnnouncement(NavigationDirection direction) => BoundaryAnnouncement;
    }
}
