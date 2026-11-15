using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// IL2CPP wrapper identity is not reference identity: the same native panel can arrive as
/// different wrapper objects in different event callbacks. Observed live on the Player Database
/// interest-filter dialog — its PanelClosed carried a fresh wrapper for the pointer already
/// stored as the active panel, so the close was misfiled as non-active and a detached dialog
/// stayed "active", breaking every context-menu open until the next navigation. PanelTracker
/// must resolve panel sameness through the host's IsSameInstance (native-pointer equality
/// in-game), with reference equality only as the fast path / capability-absent fallback.
/// </summary>
public class PanelInstanceIdentityTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private readonly TestHostSpec _host;
    private FakeElement _first;

    // Two wrappers are the same panel instance when they share a Native token — the shape of
    // Il2CppObjectBase.Pointer equality. Identity is the panel TYPE key (PanelID), shared by
    // wrappers and by distinct instances of the same panel alike.
    private sealed class PanelWrapper
    {
        public readonly string Identity;
        public readonly object Native;
        public PanelWrapper(string identity, object native)
        {
            Identity = identity;
            Native = native;
        }
    }

    public PanelInstanceIdentityTests()
    {
        _host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            FindFirst = _ => _first,
            GetPanelIdentity = p => ((PanelWrapper)p).Identity,
            IsSamePanelInstance = (a, b) => ((PanelWrapper)a).Native == ((PanelWrapper)b).Native,
        };
        _host.Apply();
        _first = new FakeElement("Alpha");
    }

    public void Dispose() => NavigationManager.ResetForReload();

    [Fact]
    public void ActiveClose_ViaDifferentWrapper_RestoresPreviousPanel()
    {
        var native = new object();
        var basePanel = new PanelWrapper("base", new object());
        var dialogLoaded = new PanelWrapper("dialog", native);
        var dialogClosed = new PanelWrapper("dialog", native);
        NavigationManager.OnPanelReady(basePanel, "Base", false);
        NavigationManager.OnPanelReady(dialogLoaded, "Dialog", false);

        var wasActive = NavigationManager.OnPanelClosed(dialogClosed);

        // The live bug: this close fell into the non-active branch, leaving the dead dialog
        // as the active panel and its base panel stranded in history.
        Assert.True(wasActive);
        Assert.Same(basePanel, NavigationManager.GetActivePanel());
    }

    [Fact]
    public void NonActiveClose_ViaDifferentWrapper_RemovesStackEntry()
    {
        var stackedNative = new object();
        var stacked = new PanelWrapper("stacked", stackedNative);
        var top = new PanelWrapper("top", new object());
        NavigationManager.OnPanelReady(stacked, "Stacked", false);
        NavigationManager.OnPanelReady(top, "Top", false);

        var wasActive = NavigationManager.OnPanelClosed(new PanelWrapper("stacked", stackedNative));
        Assert.False(wasActive);

        // The stacked entry must be gone: closing the top panel has nothing left to restore.
        Assert.True(NavigationManager.OnPanelClosed(top));
        Assert.Null(NavigationManager.GetActivePanel());
    }

    [Fact]
    public void DistinctInstances_OfSameDialog_AreNotConflated()
    {
        var basePanel = new PanelWrapper("base", new object());
        var oldDialog = new PanelWrapper("dialog", new object());
        var newDialog = new PanelWrapper("dialog", new object());
        NavigationManager.OnPanelReady(basePanel, "Base", false);
        NavigationManager.OnPanelReady(oldDialog, "Dialog", false);
        // In-place reload: same identity adopts the new instance silently.
        NavigationManager.OnPanelReady(newDialog, "Dialog", false);

        // The doomed old instance's close must not count as the active panel closing.
        Assert.False(NavigationManager.OnPanelClosed(oldDialog));
        Assert.Same(newDialog, NavigationManager.GetActivePanel());
    }

    [Fact]
    public void ComparisonFailure_IsTreatedAsDifferent_NotACrash()
    {
        _host.IsSamePanelInstance = (_, _) => throw new InvalidOperationException("collected");
        _host.Apply();

        var basePanel = new PanelWrapper("base", new object());
        var dialog = new PanelWrapper("dialog", new object());
        NavigationManager.OnPanelReady(basePanel, "Base", false);
        NavigationManager.OnPanelReady(dialog, "Dialog", false);

        // Same reference still matches through the fast path even when the delegate throws.
        Assert.True(NavigationManager.OnPanelClosed(dialog));
        Assert.Same(basePanel, NavigationManager.GetActivePanel());
    }

    [Fact]
    public void WithoutCapability_ReferenceEqualityStillGoverns()
    {
        _host.IsSamePanelInstance = null;
        _host.Apply();

        var native = new object();
        var basePanel = new PanelWrapper("base", new object());
        var dialogLoaded = new PanelWrapper("dialog", native);
        var dialogClosed = new PanelWrapper("dialog", native);
        NavigationManager.OnPanelReady(basePanel, "Base", false);
        NavigationManager.OnPanelReady(dialogLoaded, "Dialog", false);

        Assert.False(NavigationManager.OnPanelClosed(dialogClosed));
        Assert.True(NavigationManager.OnPanelClosed(dialogLoaded));
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
