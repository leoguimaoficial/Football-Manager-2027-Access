using FM27Access.Navigation;

namespace FM27Access.Tests;

public class NavigationManagerContextTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private IAccessibleElement _nextStopResult;
    private IContainerContext _contextResult;
    private IAccessibleElement _firstStopResult;
    private Func<IContainerContext, IContainerContext> _parentContext;
    private Func<object, NavigationDirection, IAccessibleElement> _afterContainer;
    private Func<IAccessibleElement, IContainerContext> _tabGroupContext;
    private Func<IAccessibleElement, IContainerContext> _tableKindContext;

    public NavigationManagerContextTests()
    {
        var host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            FindFirst = _ => _firstStopResult,
            FindNext = (_, _) => _nextStopResult,
            FindContext = _ => _contextResult,
            FindParentContext = ctx => _parentContext?.Invoke(ctx),
            FindNextAfterContainer = (s, d) => _afterContainer?.Invoke(s, d),
            ContainerKinds =
            {
                TestHostSpec.TableKind(el => _tableKindContext?.Invoke(el)),
                TestHostSpec.TabGroupKind(el => _tabGroupContext?.Invoke(el)),
            },
        };
        host.Apply();
    }

    public void Dispose() => NavigationManager.ResetForReload();

    private void SetCursor(IAccessibleElement element)
    {
        _firstStopResult = element;
        NavigationManager.OnPanelReady(new object(), "Test Panel", false);
        _spoken.Clear();
    }

    [Fact]
    public void LeftRight_WithContext_NavigatesViaContext()
    {
        var start = new FakeElement("start");
        var target = new FakeElement("target");
        var context = new FakeContext(
            handlesLeft: true, handlesRight: true,
            navigateResult: target);

        SetCursor(start);
        _contextResult = context;

        NavigationManager.MoveCursor(NavigationDirection.Right);

        Assert.Single(_spoken);
        Assert.Contains("target", _spoken[0]);
    }

    [Fact]
    public void LeftRight_WithoutContext_IsNoOp()
    {
        var start = new FakeElement("start");
        SetCursor(start);
        _contextResult = null;

        NavigationManager.MoveCursor(NavigationDirection.Left);

        Assert.Empty(_spoken);
    }

    [Fact]
    public void LeftRight_ContextDoesNotHandleDirection_IsNoOp()
    {
        var start = new FakeElement("start");
        var context = new FakeContext(
            handlesLeft: false, handlesRight: false,
            navigateResult: null);

        SetCursor(start);
        _contextResult = context;

        NavigationManager.MoveCursor(NavigationDirection.Left);

        Assert.Empty(_spoken);
    }

    [Fact]
    public void LeftRight_ContextReturnsNull_AtBoundary_IsNoOp()
    {
        var start = new FakeElement("start");
        var context = new FakeContext(
            handlesLeft: true, handlesRight: true,
            navigateResult: null);

        SetCursor(start);
        _contextResult = context;

        NavigationManager.MoveCursor(NavigationDirection.Right);

        Assert.Empty(_spoken);
    }

    [Fact]
    public void UpDown_WithContextThatHandlesIt_NavigatesViaContext()
    {
        var start = new FakeElement("start");
        var target = new FakeElement("contextTarget");
        var context = new FakeContext(
            handlesUp: true, handlesDown: true,
            navigateResult: target);

        SetCursor(start);
        _contextResult = context;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("contextTarget", _spoken[0]);
    }

    [Fact]
    public void UpDown_WithoutContext_FallsBackToFlatWalk()
    {
        var start = new FakeElement("start");
        var target = new FakeElement("flatTarget");
        SetCursor(start);
        _contextResult = null;
        _nextStopResult = target;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("flatTarget", _spoken[0]);
    }

    [Fact]
    public void UpDown_ContextDoesNotHandle_FallsBackToFlatWalk()
    {
        var start = new FakeElement("start");
        var target = new FakeElement("flatTarget");
        var context = new FakeContext(
            handlesUp: false, handlesDown: false,
            navigateResult: null);

        SetCursor(start);
        _contextResult = context;
        _nextStopResult = target;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("flatTarget", _spoken[0]);
    }

    [Fact]
    public void BubbleUp_ChildReturnsNull_ParentNavigatesSuccessfully()
    {
        var start = new FakeElement("start");
        var target = new FakeElement("parentTarget");
        var childContext = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true);
        var parentContext = new FakeContext(
            navigateResult: target,
            handlesUp: true, handlesDown: true);

        SetCursor(start);
        _contextResult = childContext;
        _parentContext = ctx =>
            ctx == childContext ? parentContext : null;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("parentTarget", _spoken[0]);
    }

    [Fact]
    public void BubbleUp_ParentAlsoReturnsNull_CursorStaysPut()
    {
        var start = new FakeElement("start");
        var childContext = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true);
        var parentContext = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true);

        SetCursor(start);
        _contextResult = childContext;
        _parentContext = ctx =>
            ctx == childContext ? parentContext : null;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Empty(_spoken);
    }

    [Fact]
    public void ExitsOnBoundary_SkipsParentContext_ExitsContainer()
    {
        var start = new FakeElement("start");
        var afterContainer = new FakeElement("afterContainer");
        var parentTarget = new FakeElement("parentTarget");

        var childScope = new object();
        var childContext = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true,
            exitsUp: true, exitsDown: true,
            scope: childScope);
        var parentContext = new FakeContext(
            navigateResult: parentTarget,
            handlesUp: true, handlesDown: true);

        SetCursor(start);
        _contextResult = childContext;
        _parentContext = ctx =>
            ctx == childContext ? parentContext : null;
        _afterContainer = (scope, _) =>
            scope == childScope ? afterContainer : null;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("afterContainer", _spoken[0]);
    }

    [Fact]
    public void NoBubbling_WhenChildContextSucceeds()
    {
        var start = new FakeElement("start");
        var target = new FakeElement("childTarget");
        var childContext = new FakeContext(
            navigateResult: target,
            handlesUp: true, handlesDown: true);

        bool parentQueried = false;
        SetCursor(start);
        _contextResult = childContext;
        _parentContext = _ =>
        {
            parentQueried = true;
            return null;
        };

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("childTarget", _spoken[0]);
        Assert.False(parentQueried);
    }

    [Fact]
    public void TabGroup_Entry_AnnouncesTabSelection()
    {
        var outside = new FakeElement("outside");
        var tabItem = new FakeElement("General");

        SetCursor(outside);
        _nextStopResult = tabItem;
        _tabGroupContext = el =>
            el == tabItem ? new FakeTabContext() : null;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.StartsWith("tab selection. ", _spoken[0]);
        Assert.Contains("General", _spoken[0]);
    }

    [Fact]
    public void TabGroup_Exit_AnnouncesOutOfTabSelection()
    {
        var tabItem = new FakeElement("General");
        var outside = new FakeElement("content");

        // Start inside tab group
        _tabGroupContext = el =>
            el == tabItem ? new FakeTabContext() : null;
        _nextStopResult = tabItem;
        SetCursor(new FakeElement("before"));
        NavigationManager.MoveCursor(NavigationDirection.Down); // enters tab group
        _spoken.Clear();

        // Now exit
        var tabScope = new object();
        var tabContext = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true,
            exitsUp: true, exitsDown: true,
            scope: tabScope);
        _contextResult = tabContext;
        _afterContainer = (scope, _) =>
            scope == tabScope ? outside : null;
        _tabGroupContext = _ => null;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.StartsWith("out of tab selection. ", _spoken[0]);
        Assert.Contains("content", _spoken[0]);
    }

    [Fact]
    public void TabGroup_UpDown_ExitsBoundary()
    {
        var start = new FakeElement("tab1");
        var afterContainer = new FakeElement("content");
        var tabScope = new object();

        var tabContext = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true,
            handlesLeft: true, handlesRight: true,
            exitsUp: true, exitsDown: true,
            scope: tabScope);

        SetCursor(start);
        _contextResult = tabContext;
        _afterContainer = (scope, _) =>
            scope == tabScope ? afterContainer : null;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("content", _spoken[0]);
    }

    // A container exit that finds nothing after the container must consult ancestor contexts
    // (a virtualised list pages its window there) instead of going silent — the Injury Table
    // card's last realised club section is a table, and Down on its last row used to be a
    // silent dead end with most of the list unrealised below.

    [Fact]
    public void TableExit_DeadEnd_AncestorBoundaryAnnounces()
    {
        var start = new FakeElement("last cell");
        var table = new FakeTableContext();
        var pagingList = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true,
            boundaryAnnouncement: "more items loading");

        SetCursor(start);
        _tableKindContext = _ => table;
        _afterContainer = (_, _) => null;
        _parentContext = ctx => ReferenceEquals(ctx, table) ? pagingList : null;

        NavigationManager.ExitTableIfAtBoundary(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Equal("more items loading", _spoken[0]);
    }

    [Fact]
    public void TableExit_DeadEnd_AncestorNavigatesCursor()
    {
        var start = new FakeElement("last cell");
        var target = new FakeElement("next section");
        var table = new FakeTableContext();
        var list = new FakeContext(
            navigateResult: target,
            handlesUp: true, handlesDown: true);

        SetCursor(start);
        _tableKindContext = _ => table;
        _afterContainer = (_, _) => null;
        _parentContext = ctx => ReferenceEquals(ctx, table) ? list : null;

        NavigationManager.ExitTableIfAtBoundary(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Contains("next section", _spoken[0]);
    }

    [Fact]
    public void TableExit_DeadEnd_NoAncestor_StaysSilent()
    {
        var start = new FakeElement("last cell");
        var table = new FakeTableContext();

        SetCursor(start);
        _tableKindContext = _ => table;
        _afterContainer = (_, _) => null;
        _parentContext = _ => null;

        NavigationManager.ExitTableIfAtBoundary(NavigationDirection.Down);

        Assert.Empty(_spoken);
    }

    [Fact]
    public void ContainerExit_DeadEnd_AncestorBoundaryAnnounces()
    {
        var start = new FakeElement("start");
        var inner = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true,
            exitsUp: true, exitsDown: true);
        var pagingList = new FakeContext(
            navigateResult: null,
            handlesUp: true, handlesDown: true,
            boundaryAnnouncement: "more items loading");

        SetCursor(start);
        _contextResult = inner;
        _afterContainer = (_, _) => null;
        _parentContext = ctx => ReferenceEquals(ctx, inner) ? pagingList : null;

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Single(_spoken);
        Assert.Equal("more items loading", _spoken[0]);
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

    private class FakeContext : IContainerContext
    {
        private readonly bool _handlesLeft;
        private readonly bool _handlesRight;
        private readonly bool _handlesUp;
        private readonly bool _handlesDown;
        private readonly bool _exitsUp;
        private readonly bool _exitsDown;
        private readonly IAccessibleElement _navigateResult;
        private readonly string _boundaryAnnouncement;

        public object Scope { get; }

        public FakeContext(
            IAccessibleElement navigateResult,
            bool handlesLeft = false,
            bool handlesRight = false,
            bool handlesUp = false,
            bool handlesDown = false,
            bool exitsUp = false,
            bool exitsDown = false,
            object scope = null,
            string boundaryAnnouncement = null)
        {
            _navigateResult = navigateResult;
            _handlesLeft = handlesLeft;
            _handlesRight = handlesRight;
            _handlesUp = handlesUp;
            _handlesDown = handlesDown;
            _exitsUp = exitsUp;
            _exitsDown = exitsDown;
            Scope = scope ?? new object();
            _boundaryAnnouncement = boundaryAnnouncement;
        }

        public string GetBoundaryAnnouncement(NavigationDirection direction) => _boundaryAnnouncement;

        public IAccessibleElement GetFirstChild() => _navigateResult;

        public bool HandlesDirection(NavigationDirection direction) => direction switch
        {
            NavigationDirection.Left => _handlesLeft,
            NavigationDirection.Right => _handlesRight,
            NavigationDirection.Up => _handlesUp,
            NavigationDirection.Down => _handlesDown,
            _ => false,
        };

        public bool ExitsOnBoundary(NavigationDirection direction) => direction switch
        {
            NavigationDirection.Up => _exitsUp,
            NavigationDirection.Down => _exitsDown,
            _ => false,
        };

        public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
            => _navigateResult;
    }

    private class FakeTabContext : IContainerContext
    {
        public object Scope => new object();
        public IAccessibleElement GetFirstChild() => null;
        public bool HandlesDirection(NavigationDirection direction) => true;
        public bool ExitsOnBoundary(NavigationDirection direction) =>
            direction == NavigationDirection.Up || direction == NavigationDirection.Down;
        public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction) => null;
        public IAccessibleElement WrapForContext(IAccessibleElement element) => null;
    }

    // A table whose cursor sits at the exit boundary: ExitTableIfAtBoundary always exits.
    private class FakeTableContext : IContainerContext
    {
        public object Scope { get; } = new object();
        public IAccessibleElement GetFirstChild() => null;
        public bool HandlesDirection(NavigationDirection direction) => true;
        public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction) => null;
        public bool IsAtExitBoundary(IAccessibleElement current, NavigationDirection direction) => true;
    }
}
