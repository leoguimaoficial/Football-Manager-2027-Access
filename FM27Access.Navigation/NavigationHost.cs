namespace FM27Access.Navigation;

/// <summary>One open panel as reported by the Plugin layer, ordered by layer index.</summary>
public readonly record struct OpenPanel(object Panel, int LayerIndex, string Title);

/// <summary>The continue button, the panel containing it, and that panel's title.</summary>
public readonly record struct ContinueButtonHit(IAccessibleElement Element, object Panel, string Title);

/// <summary>
/// Everything the Plugin layer provides to the navigation core, grouped by capability.
/// Built once per Mount in a single object initializer and handed to
/// <see cref="NavigationManager.Initialize"/>; a reload drops the whole host and the
/// next Mount builds a fresh one against the new core assembly, so no per-delegate
/// reset list exists to drift.
///
/// The Navigation assembly cannot reference game types, which is why panels travel as
/// <c>object</c> and every capability is a delegate. <c>required</c> members are the
/// ones the core cannot run without — omitting one is a compile error in the wiring.
/// Optional members follow the original seam's null semantics: an unset capability is
/// skipped silently at its use sites (tests wire only a handful).
/// </summary>
public sealed class NavigationHost
{
    /// <summary>Speaks a line through the speech pipeline, interrupting current speech.</summary>
    public required Action<string> Speak { get; init; }

    public required StopFinding Stops { get; init; }
    public ContextLookup Contexts { get; init; } = new();
    public PanelAccess Panels { get; init; } = new();
    public ProfilePolicy Profile { get; init; } = new();

    /// <summary>
    /// The container kinds the cursor understands, in canonical order (order fixes the
    /// transition-prefix composition and the entry-behavior sequence — see
    /// <see cref="ContainerKind"/>).
    /// </summary>
    public IReadOnlyList<ContainerKind> ContainerKinds { get; init; } = Array.Empty<ContainerKind>();

    /// <summary>The registered kind with this name, or null (kind-specific key commands).</summary>
    public ContainerKind Kind(string name)
    {
        foreach (var kind in ContainerKinds)
            if (kind.Name == name)
                return kind;
        return null;
    }

    /// <summary>
    /// Panel identity keys for which cursor restore is skipped (e.g. player reports
    /// where the same panel type displays different bound data each visit).
    /// </summary>
    public HashSet<string> CursorRestoreExcludedPanels { get; init; } = new();

    /// <summary>Walking the classified element tree for navigation stops.</summary>
    public sealed class StopFinding
    {
        /// <summary>First stop in a panel; also rebuilds the classification cache.</summary>
        public required Func<object, IAccessibleElement> FindFirst { get; init; }

        public required Func<IAccessibleElement, NavigationDirection, IAccessibleElement> FindNext { get; init; }

        public Func<object, IAccessibleElement> FindLast { get; init; }

        /// <summary>The next stop after a container's scope element, in the given direction.</summary>
        public Func<object, NavigationDirection, IAccessibleElement> FindNextAfterContainer { get; init; }

        /// <summary>
        /// The Nth stop in a panel whose announcement matches the given text.
        /// Parameters: (panel, announcement, 1-based occurrence index). Null if absent.
        /// </summary>
        public Func<object, string, int, IAccessibleElement> FindByAnnouncement { get; init; }

        /// <summary>
        /// The 1-based occurrence index of an element among all stops sharing its
        /// announcement, in tree-walk order. Parameters: (panel, element, announcement).
        /// </summary>
        public Func<object, IAccessibleElement, string, int> AnnouncementOccurrenceIndex { get; init; }

        /// <summary>Ensures the container contexts above an element exist in the cache.</summary>
        public Action<IAccessibleElement> EnsureContext { get; init; }

        /// <summary>Whether the element has been detached from the live tree.</summary>
        public Func<IAccessibleElement, bool> IsDetached { get; init; }

        public Func<IAccessibleElement, bool> IsInActivePanel { get; init; }
    }

    /// <summary>Container-context lookups around an element (kind-agnostic; the per-kind
    /// lookups live on the registered <see cref="ContainerKind"/>s).</summary>
    public sealed class ContextLookup
    {
        public Func<IAccessibleElement, IContainerContext> FindContext { get; init; }
        public Func<IContainerContext, IContainerContext> FindParentContext { get; init; }

        /// <summary>If the element is in a table, its (dataIndex, colIndex); otherwise null.</summary>
        public Func<IAccessibleElement, (int DataIndex, int ColIndex)?> GetTablePosition { get; init; }
    }

    /// <summary>Panel identity, enumeration, and cross-panel actions.</summary>
    public sealed class PanelAccess
    {
        /// <summary>A stable panel type identity (e.g. PanelID.ID) from a panel object.</summary>
        public Func<object, string> GetIdentity { get; init; }

        /// <summary>
        /// True when two panel objects are the same live panel INSTANCE. IL2CPP managed wrappers
        /// do not guarantee reference identity: the same native panel can arrive as different
        /// wrapper objects in different event callbacks (observed live — PanelClosed delivered a
        /// fresh wrapper for the very pointer stored as the active panel, so the close was
        /// misfiled as non-active and a detached panel stayed "active"). The Plugin implements
        /// this as native-pointer equality; reference equality is only the callers' fast path.
        /// </summary>
        public Func<object, object, bool> IsSameInstance { get; init; }

        /// <summary>The panel template name (e.g. "PortalScreen") from a panel object.</summary>
        public Func<object, string> GetName { get; init; }

        /// <summary>
        /// Raised when the active panel changes (restore from stack, panel cycling).
        /// The Plugin layer hooks this to reactivate the correct panel profile.
        /// </summary>
        public Action<string> OnActivePanelChanged { get; init; }

        /// <summary>All open panels across all layers, ordered by layer index ascending.</summary>
        public Func<List<OpenPanel>> GetAllOpen { get; init; }

        /// <summary>Layers to include when cycling panels.</summary>
        public HashSet<int> NavigableLayers { get; init; }

        /// <summary>The continue button across all open panels, or null if not found/visible.</summary>
        public Func<ContinueButtonHit?> FindContinueButton { get; init; }

        /// <summary>Back/forward navigation. Null on success, or an error message on failure.</summary>
        public Func<string> NavigateBack { get; init; }
        public Func<string> NavigateForward { get; init; }
    }

    /// <summary>The active panel profile's per-element policy (see the UI modification guide).</summary>
    public sealed class ProfilePolicy
    {
        public Func<IAccessibleElement, bool> IsHeading { get; init; }

        /// <summary>
        /// The profile heading level of a stop (0 = not a heading). Consumed by the HTML
        /// surface to pick the heading tag; the speech H-jump uses only IsHeading.
        /// </summary>
        public Func<IAccessibleElement, int> HeadingLevel { get; init; }

        public Func<IAccessibleElement, string> RenameLabel { get; init; }
        public Func<IAccessibleElement, bool> IsHidden { get; init; }
    }
}
