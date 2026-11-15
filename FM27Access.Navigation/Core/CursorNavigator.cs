using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// Owns the cursor itself: the current element, arrow-key movement through contexts and
/// the flat tree walk, container entry wrapping, the table/calendar key commands, jumps,
/// and panel rescans.
/// </summary>
internal sealed class CursorNavigator
{
    private readonly NavigationSession _session;

    internal IAccessibleElement Current { get; set; }

    /// <summary>
    /// Set when a panel loads but no navigable elements are found.
    /// The plugin layer polls this and schedules a delayed rescan.
    /// </summary>
    internal bool NeedsRescan { get; set; }

    internal CursorNavigator(NavigationSession session) => _session = session;

    internal void MoveCursor(NavigationDirection direction)
    {
        if (Current == null) return;

        try
        {
            MoveCursorCore(direction);
            return;
        }
        catch (InvalidOperationException)
        {
            Log.Warn($"[NavigationManager] Cursor element was detached from tree, rescanning panel.");
            if (!RescanPanel()) return; // cursor reset to the first stop, announced by the rescan
        }

        // The cursor was restored in place (the game rebuilt the element under it), so the
        // player's keypress hasn't landed yet — retry the move once from the fresh element.
        // A second detach mid-retry rescans again and announces wherever the cursor ended,
        // so the keypress is never silent.
        try
        {
            MoveCursorCore(direction);
        }
        catch (InvalidOperationException)
        {
            Log.Warn($"[NavigationManager] Cursor element detached again on retry, rescanning panel.");
            if (RescanPanel() && Current != null)
                NavigationManager.Host.Speak(_session.Announcer.GetDisplayAnnouncement(Current));
        }
    }

    private void MoveCursorCore(NavigationDirection direction)
    {
        var host = NavigationManager.Host;
        var context = host.Contexts.FindContext?.Invoke(Current);
        var firstContext = context;

        while (context != null && context.HandlesDirection(direction))
        {
            var contextNext = context.Navigate(Current, direction);
            if (contextNext != null)
            {
                host.Stops.EnsureContext?.Invoke(contextNext);
                contextNext = ApplyContextWrapping(contextNext, direction);
                Current = contextNext;
                _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(contextNext));
                return;
            }

            if (context.ExitsOnBoundary(direction))
                break;

            context = host.Contexts.FindParentContext?.Invoke(context);
        }

        if (firstContext != null && firstContext.HandlesDirection(direction))
        {
            if (firstContext.ExitsOnBoundary(direction))
            {
                Log.Debug($"[NavigationManager] MoveCursor({direction}) — exiting container");
                var afterContainer = host.Stops.FindNextAfterContainer?.Invoke(firstContext.Scope, direction);
                if (afterContainer != null)
                {
                    host.Stops.EnsureContext?.Invoke(afterContainer);
                    afterContainer = ApplyContextWrapping(afterContainer, direction);
                    Current = afterContainer;
                    _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(afterContainer));
                }
                else
                {
                    TryAncestorBoundary(firstContext, direction);
                }
                return;
            }
            else
            {
                var boundaryMsg = firstContext.GetBoundaryAnnouncement(direction);
                if (boundaryMsg != null)
                    host.Speak(boundaryMsg);
                else
                    Log.Debug($"[NavigationManager] MoveCursor({direction}) — at container boundary");
                return;
            }
        }

        if (direction == NavigationDirection.Left || direction == NavigationDirection.Right)
        {
            Log.Debug($"[NavigationManager] MoveCursor({direction}) — no context handles this direction");
            return;
        }

        var next = host.Stops.FindNext(Current, direction);
        if (next == null)
        {
            // Surface the detach to MoveCursor's catch so this path gets the same
            // restore-and-retry treatment as a walk that throws mid-move.
            if (host.Stops.IsDetached != null && host.Stops.IsDetached(Current))
                throw new InvalidOperationException("Cursor element is detached from the tree.");
            Log.Debug($"[NavigationManager] MoveCursor({direction}) — at boundary");
            return;
        }

        host.Stops.EnsureContext?.Invoke(next);
        next = ApplyContextWrapping(next, direction);
        Current = next;
        _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(next));
    }

    /// <summary>
    /// A container exit found nothing after the container in the panel walk. That dead end may
    /// really be the realised edge of an ancestor paging container — a virtualised list whose
    /// realised window ends with this container (the Injury Table card's last realised club
    /// section is a table; Down on its last row used to go silent with 10 clubs unrealised
    /// below). Give ancestor contexts the chance to move the cursor, page their window, or
    /// announce the boundary before the press goes silent. True when an ancestor handled it.
    /// </summary>
    private bool TryAncestorBoundary(IContainerContext from, NavigationDirection direction)
    {
        var host = NavigationManager.Host;
        var context = host.Contexts.FindParentContext?.Invoke(from);
        while (context != null && context.HandlesDirection(direction))
        {
            var next = context.Navigate(Current, direction);
            if (next != null)
            {
                host.Stops.EnsureContext?.Invoke(next);
                next = ApplyContextWrapping(next, direction);
                Current = next;
                _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(next));
                return true;
            }

            if (!context.ExitsOnBoundary(direction))
            {
                // The ancestor keeps the cursor at its edge — announced when it has a message
                // (a paging context's loading cue), silently otherwise, exactly as a direct
                // boundary in MoveCursorCore.
                var msg = context.GetBoundaryAnnouncement(direction);
                if (msg != null)
                    host.Speak(msg);
                else
                    Log.Debug($"[NavigationManager] MoveCursor({direction}) — at ancestor container boundary");
                return true;
            }

            context = host.Contexts.FindParentContext?.Invoke(context);
        }
        return false;
    }

    private IAccessibleElement ApplyContextWrapping(IAccessibleElement element, NavigationDirection direction)
    {
        var host = NavigationManager.Host;

        // Entry behaviors run in kind registration order, against the pre-move "was inside"
        // snapshot (the transitions that update it only fire when the move is announced).
        foreach (var kind in host.ContainerKinds)
        {
            switch (kind.EntryBehavior)
            {
                case ContainerEntry.WrapOrEntry:
                {
                    var context = kind.FindContext?.Invoke(element);
                    if (context == null) break;

                    var wrapped = context.WrapForContext(element);
                    if (wrapped != null)
                        return wrapped;

                    var entry = context.GetEntryElement(direction);
                    if (entry != null)
                        return entry;
                    break;
                }

                case ContainerEntry.EntryUnlessInside:
                {
                    if (_session.Announcer.WasIn(kind.Name)) break;
                    var context = kind.FindContext?.Invoke(element);
                    var entry = context?.GetEntryElement(direction);
                    if (entry != null)
                        return entry;
                    break;
                }

                case ContainerEntry.FirstChildOnDownUnlessInside:
                {
                    if (_session.Announcer.WasIn(kind.Name) || direction != NavigationDirection.Down) break;
                    var context = kind.FindContext?.Invoke(element);
                    var entry = context?.GetFirstChild();
                    if (entry != null)
                    {
                        host.Stops.EnsureContext?.Invoke(entry);
                        return entry;
                    }
                    break;
                }
            }
        }

        return element;
    }

    /// <summary>
    /// Ctrl+Alt+Arrow: move between cells in a table, never exiting.
    /// Announces boundary messages at edges.
    /// </summary>
    internal void MoveTableCell(NavigationDirection direction)
    {
        if (Current == null) return;
        var host = NavigationManager.Host;
        var tableContext = host.Kind(ContainerKind.Table)?.FindContext(Current);
        if (tableContext == null)
        {
            host.Speak("not in a table");
            return;
        }

        var next = tableContext.NavigateCell(Current, direction);
        if (next != null)
        {
            host.Stops.EnsureContext?.Invoke(next);
            Current = next;
            _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(next));
        }
        else
        {
            var msg = tableContext.GetBoundaryAnnouncement(direction);
            if (msg != null)
                host.Speak(msg);
        }
    }

    /// <summary>
    /// Plain Left/Right in a table: move between stops within a single cell.
    /// Does nothing at cell edges or in single-stop cells.
    /// </summary>
    internal void MoveTableInnerCell(NavigationDirection direction)
    {
        if (Current == null) return;
        var host = NavigationManager.Host;
        var tableContext = host.Kind(ContainerKind.Table)?.FindContext(Current);
        if (tableContext == null) return;

        var next = tableContext.NavigateInnerCell(Current, direction);
        if (next != null)
        {
            host.Stops.EnsureContext?.Invoke(next);
            Current = next;
            _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(next));
        }
    }

    /// <summary>
    /// Plain Up/Down in a table: exits the table only at boundaries
    /// (Up on header, Down on last row). Does nothing otherwise.
    /// </summary>
    internal void ExitTableIfAtBoundary(NavigationDirection direction)
    {
        if (Current == null) return;
        var host = NavigationManager.Host;
        var tableContext = host.Kind(ContainerKind.Table)?.FindContext(Current);
        if (tableContext == null) return;

        if (!tableContext.IsAtExitBoundary(Current, direction))
            return;

        var afterContainer = host.Stops.FindNextAfterContainer?.Invoke(tableContext.Scope, direction);
        if (afterContainer != null)
        {
            host.Stops.EnsureContext?.Invoke(afterContainer);
            afterContainer = ApplyContextWrapping(afterContainer, direction);
            Current = afterContainer;
            _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(afterContainer));
            return;
        }

        // Nothing after the table: the boundary may belong to an ancestor paging container
        // (a virtualised list whose realised window ends with this table).
        TryAncestorBoundary(tableContext, direction);
    }

    internal bool IsCurrentInTable()
    {
        if (Current == null) return false;
        return NavigationManager.Host?.Kind(ContainerKind.Table)?.FindContext(Current) != null;
    }

    internal bool IsCurrentInCalendar()
    {
        if (Current == null) return false;
        return NavigationManager.Host?.Kind(ContainerKind.Calendar)?.FindContext(Current) != null;
    }

    internal void MoveCalendarDay(NavigationDirection direction)
    {
        if (Current == null) return;
        var host = NavigationManager.Host;
        var calContext = host.Kind(ContainerKind.Calendar)?.FindContext(Current);
        if (calContext == null) return;

        var next = calContext.Navigate(Current, direction);
        if (next != null)
        {
            host.Stops.EnsureContext?.Invoke(next);
            Current = next;
            _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(next));
        }
        else if (calContext.ExitsOnBoundary(direction))
        {
            var afterContainer = host.Stops.FindNextAfterContainer?.Invoke(calContext.Scope, direction);
            if (afterContainer != null)
            {
                host.Stops.EnsureContext?.Invoke(afterContainer);
                afterContainer = ApplyContextWrapping(afterContainer, direction);
                Current = afterContainer;
                _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(afterContainer));
            }
        }
    }

    internal void MoveCalendarInnerStop(NavigationDirection direction)
    {
        if (Current == null) return;
        var host = NavigationManager.Host;
        var calContext = host.Kind(ContainerKind.Calendar)?.FindContext(Current);
        if (calContext == null) return;

        var next = calContext.NavigateInnerCell(Current, direction);
        if (next != null)
        {
            host.Stops.EnsureContext?.Invoke(next);
            Current = next;
            _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(next));
        }
        else
        {
            var msg = calContext.GetBoundaryAnnouncement(direction);
            if (msg != null)
                host.Speak(msg);
        }
    }

    /// <summary>
    /// Sets the cursor to the given element and ensures context, without announcing.
    /// Used by CursorJump helpers.
    /// </summary>
    internal void SetCursorSilently(IAccessibleElement element)
    {
        NavigationManager.Host?.Stops.EnsureContext?.Invoke(element);
        Current = element;
    }

    internal void SetCursorAndAnnounce(IAccessibleElement element)
    {
        NavigationManager.Host?.Stops.EnsureContext?.Invoke(element);
        Current = element;
        _session.Memory.RecordPosition(element);
        _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(element));
    }

    internal void JumpToFirst()
    {
        if (_session.Panels.ActivePanel == null) return;
        var first = NavigationManager.Host.Stops.FindFirst(_session.Panels.ActivePanel);
        if (first == null) return;
        Current = first;
        _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(first));
    }

    internal void JumpToLast()
    {
        var host = NavigationManager.Host;
        if (_session.Panels.ActivePanel == null || host.Stops.FindLast == null) return;
        var last = host.Stops.FindLast(_session.Panels.ActivePanel);
        if (last == null) return;
        Current = last;
        _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(last));
    }

    /// <summary>
    /// Re-scans the active panel from its root, clearing the classification cache.
    /// Call after in-panel content changes (e.g. wizard step pagination).
    /// Keeps the cursor's position when possible: on binding-heavy panels (e.g. the Tactics
    /// Planner toolbar) the game constantly detaches and rebuilds elements, and the rebuilt
    /// stop still matches the stored announcement — the cursor stays put, silently. Only when
    /// no match exists does the cursor reset to the first stop, announced.
    /// Returns true when the position was restored, false on a reset (or an empty panel).
    /// </summary>
    internal bool RescanPanel()
    {
        if (_session.Panels.ActivePanel == null)
        {
            Log.Warn("[NavigationManager] RescanPanel called with no active panel.");
            return false;
        }

        var host = NavigationManager.Host;
        var memory = _session.Memory;

        // Always walk from the root first: this rebuilds the classification cache.
        var firstStop = host.Stops.FindFirst(_session.Panels.ActivePanel);
        NeedsRescan = firstStop == null;

        if (memory.LastAnnouncement != null && host.Stops.FindByAnnouncement != null)
        {
            var restored = host.Stops.FindByAnnouncement(
                _session.Panels.ActivePanel, memory.LastAnnouncement, memory.LastAnnouncementIndex);
            if (restored != null)
            {
                Current = restored;
                Log.Info($"[NavigationManager] Rescan complete, cursor restored to \"{memory.LastAnnouncement}\" (occurrence {memory.LastAnnouncementIndex}).");
                return true;
            }
        }

        Current = firstStop;
        if (firstStop != null)
            host.Speak(_session.Announcer.GetDisplayAnnouncement(firstStop));

        Log.Info($"[NavigationManager] Rescan complete, cursor={firstStop?.Role ?? "none"}");
        return false;
    }

    /// <summary>
    /// Re-scans the panel but keeps the cursor in place if the current element is still visible.
    /// Use for non-structural activations (checkboxes, dropdowns) where content may change
    /// but the user should stay where they are.
    /// </summary>
    internal void RescanPanelKeepCursor()
    {
        if (_session.Panels.ActivePanel == null) return;

        var host = NavigationManager.Host;
        if (Current != null && Current.IsVisible())
        {
            host.Stops.FindFirst(_session.Panels.ActivePanel);
            host.Stops.EnsureContext?.Invoke(Current);
            Log.Info("[NavigationManager] Rescan (keep cursor) complete, cursor unchanged.");
            return;
        }

        RescanPanel();
    }
}
