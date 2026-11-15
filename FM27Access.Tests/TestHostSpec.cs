using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// A mutable stand-in for the wiring CoreEntry does in the game: set the delegates a
/// test needs, call <see cref="Apply"/> to snapshot them into a <see cref="NavigationHost"/>
/// and hand it to <see cref="NavigationManager.Initialize"/>. Re-set and re-Apply mid-test
/// to swap a capability (navigation state survives; only <c>ResetForReload</c> clears it).
/// A field left (or set back to) null becomes an absent capability, exactly as an unwired
/// delegate slot — the seam's null semantics are part of what the tests exercise.
/// Dispose fixtures with <see cref="NavigationManager.ResetForReload"/>.
/// </summary>
internal sealed class TestHostSpec
{
    public Action<string> Speak { get; set; } = _ => { };
    public Func<object, IAccessibleElement> FindFirst { get; set; } = _ => null;
    public Func<IAccessibleElement, NavigationDirection, IAccessibleElement> FindNext { get; set; } = (_, _) => null;
    public Func<object, IAccessibleElement> FindLast { get; set; }
    public Func<object, NavigationDirection, IAccessibleElement> FindNextAfterContainer { get; set; }
    public Func<object, string, int, IAccessibleElement> FindByAnnouncement { get; set; }
    public Func<object, IAccessibleElement, string, int> AnnouncementOccurrenceIndex { get; set; }
    public Action<IAccessibleElement> EnsureContext { get; set; }
    public Func<IAccessibleElement, bool> IsDetached { get; set; }
    public Func<IAccessibleElement, bool> IsInActivePanel { get; set; }

    public Func<IAccessibleElement, IContainerContext> FindContext { get; set; }
    public Func<IContainerContext, IContainerContext> FindParentContext { get; set; }
    public Func<IAccessibleElement, (int DataIndex, int ColIndex)?> GetTablePosition { get; set; }

    /// <summary>Registered in list order — order is behavior (transition composition, entry sequence).</summary>
    public List<ContainerKind> ContainerKinds { get; set; } = new();

    public Func<object, string> GetPanelIdentity { get; set; }
    public Func<object, object, bool> IsSamePanelInstance { get; set; }
    public Func<object, string> GetPanelName { get; set; }
    public Action<string> OnActivePanelChanged { get; set; }
    public Func<List<OpenPanel>> GetAllOpenPanels { get; set; }
    public HashSet<int> NavigableLayers { get; set; }
    public Func<ContinueButtonHit?> FindContinueButton { get; set; }
    public Func<string> NavigateBack { get; set; }
    public Func<string> NavigateForward { get; set; }

    public Func<IAccessibleElement, bool> IsHeading { get; set; }
    public Func<IAccessibleElement, int> HeadingLevel { get; set; }
    public Func<IAccessibleElement, string> RenameLabel { get; set; }
    public Func<IAccessibleElement, bool> IsHidden { get; set; }

    public HashSet<string> CursorRestoreExcludedPanels { get; set; } = new();

    // The four canonical kinds exactly as CoreEntry registers them, parameterized on the
    // context lookup so a fixture supplies its fake containers.
    public static ContainerKind TableKind(Func<IAccessibleElement, IContainerContext> findContext) => new()
    {
        Name = ContainerKind.Table,
        EnterAnnouncement = "table",
        ExitAnnouncement = "out of table",
        FindContext = findContext,
        EntryBehavior = ContainerEntry.None,
    };

    public static ContainerKind TabGroupKind(Func<IAccessibleElement, IContainerContext> findContext) => new()
    {
        Name = ContainerKind.TabGroup,
        EnterAnnouncement = "tab selection",
        ExitAnnouncement = "out of tab selection",
        FindContext = findContext,
        EntryBehavior = ContainerEntry.WrapOrEntry,
    };

    public static ContainerKind ButtonBarKind(Func<IAccessibleElement, IContainerContext> findContext) => new()
    {
        Name = ContainerKind.ButtonBar,
        EnterAnnouncement = "button bar",
        ExitAnnouncement = "out of button bar",
        FindContext = findContext,
        EntryBehavior = ContainerEntry.EntryUnlessInside,
    };

    public static ContainerKind CalendarKind(Func<IAccessibleElement, IContainerContext> findContext) => new()
    {
        Name = ContainerKind.Calendar,
        EnterAnnouncement = "calendar",
        ExitAnnouncement = "out of calendar",
        FindContext = findContext,
        EntryBehavior = ContainerEntry.FirstChildOnDownUnlessInside,
    };

    public void Apply() => NavigationManager.Initialize(new NavigationHost
    {
        Speak = Speak,
        Stops = new NavigationHost.StopFinding
        {
            FindFirst = FindFirst,
            FindNext = FindNext,
            FindLast = FindLast,
            FindNextAfterContainer = FindNextAfterContainer,
            FindByAnnouncement = FindByAnnouncement,
            AnnouncementOccurrenceIndex = AnnouncementOccurrenceIndex,
            EnsureContext = EnsureContext,
            IsDetached = IsDetached,
            IsInActivePanel = IsInActivePanel,
        },
        Contexts = new NavigationHost.ContextLookup
        {
            FindContext = FindContext,
            FindParentContext = FindParentContext,
            GetTablePosition = GetTablePosition,
        },
        ContainerKinds = ContainerKinds.ToArray(),
        Panels = new NavigationHost.PanelAccess
        {
            GetIdentity = GetPanelIdentity,
            IsSameInstance = IsSamePanelInstance,
            GetName = GetPanelName,
            OnActivePanelChanged = OnActivePanelChanged,
            GetAllOpen = GetAllOpenPanels,
            NavigableLayers = NavigableLayers,
            FindContinueButton = FindContinueButton,
            NavigateBack = NavigateBack,
            NavigateForward = NavigateForward,
        },
        Profile = new NavigationHost.ProfilePolicy
        {
            IsHeading = IsHeading,
            HeadingLevel = HeadingLevel,
            RenameLabel = RenameLabel,
            IsHidden = IsHidden,
        },
        CursorRestoreExcludedPanels = CursorRestoreExcludedPanels,
    });
}
