using FM27Access.Navigation;

namespace FM27Access.Tests;

public class LoadingScreenLifecycleTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private FakeElement _first;

    public LoadingScreenLifecycleTests()
    {
        var host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            FindFirst = _ => _first,
        };
        host.Apply();
    }

    public void Dispose() => NavigationManager.ResetForReload();

    [Fact]
    public void LoadingScreen_AnnouncesLoading_AndClearsCursor()
    {
        _first = new FakeElement("Alpha");
        NavigationManager.OnPanelReady("main", "Main", false);

        NavigationManager.OnPanelReady("loading", "Processing", true);

        Assert.Equal("Loading", _spoken[^1]);
        Assert.Null(NavigationManager.GetCurrentElement());
        Assert.True(NavigationManager.NeedsRescan);
    }

    [Fact]
    public void PanelReadyDuringLoadingScreen_IsDeferredSilently()
    {
        _first = new FakeElement("Alpha");
        NavigationManager.OnPanelReady("main", "Main", false);
        NavigationManager.OnPanelReady("loading", "Processing", true);
        _spoken.Clear();

        NavigationManager.OnPanelReady("squad", "Squad", false);

        Assert.Empty(_spoken);
        Assert.Equal("loading", NavigationManager.GetActivePanel());
    }

    [Fact]
    public void ClosingLoadingScreen_RestoresDeferredPanel()
    {
        _first = new FakeElement("Alpha");
        NavigationManager.OnPanelReady("main", "Main", false);
        NavigationManager.OnPanelReady("loading", "Processing", true);
        NavigationManager.OnPanelReady("squad", "Squad", false);
        _spoken.Clear();

        var wasActive = NavigationManager.OnPanelClosed("loading");

        Assert.True(wasActive);
        Assert.Equal("squad", NavigationManager.GetActivePanel());
        Assert.Contains(_spoken, s => s == "Squad. Alpha, button");
    }

    [Fact]
    public void DismissLoadingScreen_RestoresStackedPanel()
    {
        _first = new FakeElement("Alpha");
        NavigationManager.OnPanelReady("main", "Main", false);
        NavigationManager.OnPanelReady("loading", "Processing", true);
        _spoken.Clear();

        NavigationManager.DismissLoadingScreen();

        Assert.Equal("main", NavigationManager.GetActivePanel());
        Assert.Contains(_spoken, s => s == "Main. Alpha, button");
    }

    [Fact]
    public void DismissLoadingScreen_PrefersDeferredPanelOverPrevious()
    {
        _first = new FakeElement("Alpha");
        NavigationManager.OnPanelReady("main", "Main", false);
        NavigationManager.OnPanelReady("loading", "Processing", true);
        NavigationManager.OnPanelReady("squad", "Squad", false);
        _spoken.Clear();

        NavigationManager.DismissLoadingScreen();

        Assert.Equal("squad", NavigationManager.GetActivePanel());
        Assert.Contains(_spoken, s => s == "Squad. Alpha, button");
    }

    [Fact]
    public void DismissLoadingScreen_IsNoOpWithoutActiveLoadingScreen()
    {
        _first = new FakeElement("Alpha");
        NavigationManager.OnPanelReady("main", "Main", false);
        _spoken.Clear();

        NavigationManager.DismissLoadingScreen();

        Assert.Empty(_spoken);
        Assert.Equal("main", NavigationManager.GetActivePanel());
    }

    [Fact]
    public void DismissLoadingScreen_IsNoOpWhenCalledTwice()
    {
        _first = new FakeElement("Alpha");
        NavigationManager.OnPanelReady("main", "Main", false);
        NavigationManager.OnPanelReady("loading", "Processing", true);
        NavigationManager.DismissLoadingScreen();
        _spoken.Clear();

        NavigationManager.DismissLoadingScreen();

        Assert.Empty(_spoken);
        Assert.Equal("main", NavigationManager.GetActivePanel());
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
}
