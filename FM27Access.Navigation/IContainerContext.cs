namespace FM27Access.Navigation;

public interface IContainerContext
{
    object Scope { get; }
    IAccessibleElement GetFirstChild();
    IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction);
    bool HandlesDirection(NavigationDirection direction);

    /// <summary>
    /// When Navigate returns null for this direction, should the cursor exit
    /// the container (true) or stay at the edge (false)?
    /// </summary>
    bool ExitsOnBoundary(NavigationDirection direction) => false;

    /// <summary>
    /// Announcement when navigation hits a boundary and the context does not exit.
    /// Return null for no announcement (the default).
    /// </summary>
    string GetBoundaryAnnouncement(NavigationDirection direction) => null;

    /// <summary>
    /// Wraps an element with context-specific information (e.g. positional announcements).
    /// Returns null if no wrapping is needed. Called when the cursor enters or moves within this context.
    /// </summary>
    IAccessibleElement WrapForContext(IAccessibleElement element) => null;

    /// <summary>
    /// Returns the entry element for this context when entering from a given direction.
    /// For example, entering from below (Up) returns the last element, from above (Down) the first.
    /// Returns null if not applicable (the default).
    /// </summary>
    IAccessibleElement GetEntryElement(NavigationDirection direction) => null;

    /// <summary>
    /// Navigate between cells without exiting the container.
    /// Returns the target element, or null if at boundary.
    /// </summary>
    IAccessibleElement NavigateCell(IAccessibleElement current, NavigationDirection direction) => Navigate(current, direction);

    /// <summary>
    /// Navigate between stops within a single cell (intra-cell).
    /// Returns the next inner stop, or null if at cell edge or single-stop cell.
    /// </summary>
    IAccessibleElement NavigateInnerCell(IAccessibleElement current, NavigationDirection direction) => null;

    /// <summary>
    /// Whether the cursor is at a table boundary where plain arrow should exit.
    /// True when on a header (Up) or last data row (Down).
    /// </summary>
    bool IsAtExitBoundary(IAccessibleElement current, NavigationDirection direction) => false;
}
