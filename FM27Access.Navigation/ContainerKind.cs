namespace FM27Access.Navigation;

/// <summary>
/// How the cursor auto-enters a container when a move lands on one of its elements
/// (applied by the cursor's context wrapping, in kind registration order).
/// </summary>
public enum ContainerEntry
{
    /// <summary>Never auto-enter; the cursor reaches the container in tree order (tables).</summary>
    None,

    /// <summary>
    /// Ask the context to wrap the landed element (positional announcements), falling back
    /// to its entry element for the direction (tab groups). Applies whether or not the
    /// cursor was already inside one.
    /// </summary>
    WrapOrEntry,

    /// <summary>
    /// Redirect to the context's entry element for the direction, but only when the cursor
    /// was not already inside this kind (button bars).
    /// </summary>
    EntryUnlessInside,

    /// <summary>
    /// Redirect to the context's first child, but only entering downward and when the
    /// cursor was not already inside this kind (calendars).
    /// </summary>
    FirstChildOnDownUnlessInside,
}

/// <summary>
/// One container kind the cursor understands: how to find its context above an element,
/// what entering and leaving it announces, and how the cursor auto-enters it. The Plugin
/// registers the kinds in <see cref="NavigationHost.ContainerKinds"/>; registration order
/// is behavior — it fixes both the transition-prefix composition when one move crosses
/// several kinds and the order entry behaviors are tried.
/// Adding a container kind means writing its <see cref="IContainerContext"/> and
/// registering one descriptor here; the navigation core is not edited.
/// </summary>
public sealed class ContainerKind
{
    /// <summary>Registered kind names with cross-layer meaning (key commands, jumps).</summary>
    public const string Table = "table";
    public const string TabGroup = "tab group";
    public const string ButtonBar = "button bar";
    public const string Calendar = "calendar";

    public required string Name { get; init; }

    /// <summary>Prefix spoken when the cursor enters this kind (e.g. "table").</summary>
    public required string EnterAnnouncement { get; init; }

    /// <summary>Prefix spoken when the cursor leaves this kind (e.g. "out of table").</summary>
    public required string ExitAnnouncement { get; init; }

    /// <summary>
    /// The nearest enclosing context of this kind above an element, or null when the
    /// element is not inside one. Non-null is also the "is inside" test.
    /// </summary>
    public required Func<IAccessibleElement, IContainerContext> FindContext { get; init; }

    public ContainerEntry EntryBehavior { get; init; } = ContainerEntry.None;
}
