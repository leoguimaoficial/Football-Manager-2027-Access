using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// The game replaces some panels in place: a fresh instance of the panel the player is already
/// on loads first, then the old instance closes (the Club Site page cycles this way dozens of
/// times while its streamed content settles). PanelTracker must adopt the fresh instance
/// silently — no re-announcement, no history push — while genuinely new panels keep the normal
/// announce-and-push behavior.
/// </summary>
public class PanelReloadInPlaceTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private readonly TestHostSpec _host;
    private FakeElement _first;

    // Distinct instances of the same game panel share an identity, as PanelID.ID does in-game.
    private sealed class GamePanel
    {
        public readonly string Identity;
        public GamePanel(string identity) => Identity = identity;
    }

    public PanelReloadInPlaceTests()
    {
        _host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            FindFirst = _ => _first,
            GetPanelIdentity = p => ((GamePanel)p).Identity,
        };
        _host.Apply();
        _first = new FakeElement("Alpha");
    }

    public void Dispose() => NavigationManager.ResetForReload();

    [Fact]
    public void SameIdentityReload_IsSilent_AndAdoptsNewInstance()
    {
        var clubOld = new GamePanel("club");
        var clubNew = new GamePanel("club");
        NavigationManager.OnPanelReady(clubOld, "Club Site", false);
        _spoken.Clear();

        NavigationManager.OnPanelReady(clubNew, "Club Site", false);

        Assert.Empty(_spoken);
        Assert.Same(clubNew, NavigationManager.GetActivePanel());
    }

    [Fact]
    public void SameIdentityReload_DoesNotPushHistory()
    {
        var main = new GamePanel("main");
        var clubOld = new GamePanel("club");
        var clubNew = new GamePanel("club");
        NavigationManager.OnPanelReady(main, "Main", false);
        NavigationManager.OnPanelReady(clubOld, "Club Site", false);
        NavigationManager.OnPanelReady(clubNew, "Club Site", false);

        var wasActive = NavigationManager.OnPanelClosed(clubNew);

        // A pushed duplicate would restore the doomed old club instance here instead.
        Assert.True(wasActive);
        Assert.Same(main, NavigationManager.GetActivePanel());
    }

    [Fact]
    public void OldInstanceClose_AfterReload_IsNonActive_AndKeepsHistory()
    {
        // The observed game order: the new instance loads BEFORE the old one closes.
        var main = new GamePanel("main");
        var clubOld = new GamePanel("club");
        var clubNew = new GamePanel("club");
        NavigationManager.OnPanelReady(main, "Main", false);
        NavigationManager.OnPanelReady(clubOld, "Club Site", false);
        NavigationManager.OnPanelReady(clubNew, "Club Site", false);

        var oldWasActive = NavigationManager.OnPanelClosed(clubOld);

        Assert.False(oldWasActive);
        Assert.Same(clubNew, NavigationManager.GetActivePanel());

        var newWasActive = NavigationManager.OnPanelClosed(clubNew);
        Assert.True(newWasActive);
        Assert.Same(main, NavigationManager.GetActivePanel());
    }

    [Fact]
    public void RepeatedReloads_AnnounceOnlyTheFirstLoad()
    {
        NavigationManager.OnPanelReady(new GamePanel("club"), "Club Site", false);
        int announcedAfterFirst = _spoken.Count;

        for (int i = 0; i < 50; i++)
            NavigationManager.OnPanelReady(new GamePanel("club"), "Club Site", false);

        Assert.Equal(announcedAfterFirst, _spoken.Count);
    }

    [Fact]
    public void DifferentIdentity_StillAnnouncesAndPushes()
    {
        var club = new GamePanel("club");
        var squad = new GamePanel("squad");
        NavigationManager.OnPanelReady(club, "Club Site", false);
        _spoken.Clear();

        NavigationManager.OnPanelReady(squad, "Squad", false);

        Assert.Contains(_spoken, s => s.StartsWith("Squad"));
        Assert.Same(squad, NavigationManager.GetActivePanel());

        NavigationManager.OnPanelClosed(squad);
        Assert.Same(club, NavigationManager.GetActivePanel());
    }

    [Fact]
    public void WithoutIdentityCapability_ReloadDegradesToAnnouncing()
    {
        _host.GetPanelIdentity = null;
        _host.Apply();
        NavigationManager.OnPanelReady(new GamePanel("club"), "Club Site", false);
        _spoken.Clear();

        NavigationManager.OnPanelReady(new GamePanel("club"), "Club Site", false);

        Assert.NotEmpty(_spoken);
    }

    [Fact]
    public void IdentityReadFailure_DegradesToAnnouncing()
    {
        var clubOld = new GamePanel("club");
        var clubNew = new GamePanel("club");
        // The old instance's native side can already be gone when the reload arrives; a
        // throwing identity read must compare as "different panel", never propagate. No first
        // stop, so the pre-existing unguarded GetIdentity call in CommitCursorPosition (bounded
        // by the patch boundary in production) stays out of this test's path.
        _first = null;
        _host.GetPanelIdentity = p => ReferenceEquals(p, clubOld)
            ? throw new InvalidOperationException("destroyed panel")
            : ((GamePanel)p).Identity;
        _host.Apply();
        NavigationManager.OnPanelReady(clubOld, "Club Site", false);
        _spoken.Clear();

        NavigationManager.OnPanelReady(clubNew, "Club Site", false);

        Assert.NotEmpty(_spoken);
        Assert.Same(clubNew, NavigationManager.GetActivePanel());
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
