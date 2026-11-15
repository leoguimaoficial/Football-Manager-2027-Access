using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// Tracks which panel the cursor lives in: the active panel and its title, the
/// loading-screen deferral, the panel history stack restored on close, browse-mode
/// panel cycling (Ctrl+Left/Right), and the continue-button jump that switches panels.
/// </summary>
internal sealed class PanelTracker
{
    private readonly NavigationSession _session;

    internal object ActivePanel { get; private set; }
    internal string ActivePanelTitle { get; private set; }

    private bool _activeIsLoadingScreen;

    private record struct SavedPanel(object Panel, string Title);
    private readonly Stack<SavedPanel> _panelStack = new();

    /// <summary>
    /// Whether we're in panel-browse mode (cycling via Ctrl+Left/Right rather than
    /// having arrived via the normal OnPanelReady path).
    /// </summary>
    private bool _browsing;

    internal PanelTracker(NavigationSession session) => _session = session;

    internal void OnPanelReady(object panel, string title, bool isLoadingScreen)
    {
        NavigationManager.ValidateInitialized();

        if (isLoadingScreen)
        {
            _session.Memory.CommitCursorPosition();
            if (ActivePanel != null)
                _panelStack.Push(new SavedPanel(ActivePanel, ActivePanelTitle));
            ActivePanel = panel;
            ActivePanelTitle = title;
            _activeIsLoadingScreen = true;
            _session.Cursor.Current = null;
            _session.Cursor.NeedsRescan = true;
            NavigationManager.Host.Speak("Loading");
            return;
        }

        // While a loading screen is displayed, lower-layer panels load behind it.
        // Stack them silently so focus stays on the loading screen.
        if (_activeIsLoadingScreen)
        {
            _panelStack.Push(new SavedPanel(panel, title));
            Log.Info($"[NavigationManager] Deferred panel \"{title}\" — loading screen is active.");
            return;
        }

        // The game replaces some panels in place: a fresh instance of the panel the player is
        // already on loads first, then the old instance closes (the Club Site page cycles this
        // way dozens of times while its streamed content settles). Each cycle is the same
        // surface, not a navigation: adopt the fresh instance silently instead of re-announcing
        // the title and pushing the doomed old instance onto the history stack. The cursor may
        // still hold an element of the old tree; the first move after the swap hits the
        // detached-element rescan, which restores the position in the new tree by announcement.
        // The old instance's PanelClosed then lands in the non-active branch of OnPanelClosed,
        // leaving the stack untouched.
        if (ActivePanel != null && IsSamePanelIdentity(panel, ActivePanel))
        {
            ActivePanel = panel;
            ActivePanelTitle = title;
            Log.Info($"[NavigationManager] Panel \"{title}\" reloaded in place; adopted the new instance silently.");
            return;
        }

        _session.Memory.CommitCursorPosition();

        if (_browsing)
        {
            // Exiting browse mode — the panel we were browsing from isn't our "real" history,
            // so don't push it. Push nothing; the stack already has the correct history.
            _browsing = false;
        }
        else if (ActivePanel != null)
        {
            _panelStack.Push(new SavedPanel(ActivePanel, ActivePanelTitle));
        }

        ActivePanel = panel;
        ActivePanelTitle = title;

        _session.AnnouncePanelEntry(panel, title);

        Log.Info($"[NavigationManager] Panel ready: \"{title}\", cursor={_session.Cursor.Current?.Role ?? "none"}");
    }

    internal bool OnPanelClosed(object panel)
    {
        if (!SameInstance(ActivePanel, panel))
        {
            // A non-active panel closed (e.g. a panel lower in the stack).
            // Remove it from the stack if present.
            var temp = new Stack<SavedPanel>();
            while (_panelStack.Count > 0)
            {
                var entry = _panelStack.Pop();
                if (!SameInstance(entry.Panel, panel))
                    temp.Push(entry);
            }
            while (temp.Count > 0)
                _panelStack.Push(temp.Pop());

            Log.Info("[NavigationManager] Non-active panel closed, removed from stack.");
            return false;
        }

        _session.Memory.CommitCursorPosition();
        _session.Cursor.Current = null;
        ActivePanel = null;
        ActivePanelTitle = null;
        _activeIsLoadingScreen = false;
        _session.Memory.ClearLastAnnouncement();

        if (_panelStack.Count > 0)
        {
            var restored = _panelStack.Pop();
            ActivePanel = restored.Panel;
            ActivePanelTitle = restored.Title;

            NotifyActivePanelChanged(restored.Panel);
            _session.AnnouncePanelEntry(restored.Panel, restored.Title);
            Log.Info($"[NavigationManager] Restored panel: \"{restored.Title}\", cursor={_session.Cursor.Current?.Role ?? "none"}");
        }
        else
        {
            Log.Info("[NavigationManager] Panel closed, no panel to restore.");
        }

        return true;
    }

    /// <summary>
    /// Force-dismiss the active loading screen when the game doesn't fire PanelClosed.
    /// The ProcessingCard panel is hidden without a close event, leaving the mod stuck
    /// in loading-screen mode. This restores the top stacked panel.
    /// </summary>
    internal void DismissLoadingScreen()
    {
        if (!_activeIsLoadingScreen)
            return;

        Log.Info("[NavigationManager] Force-dismissing loading screen (ProcessingCard workaround).");

        _session.Cursor.Current = null;
        ActivePanel = null;
        ActivePanelTitle = null;
        _activeIsLoadingScreen = false;
        _session.Memory.ClearLastAnnouncement();

        if (_panelStack.Count > 0)
        {
            var restored = _panelStack.Pop();
            ActivePanel = restored.Panel;
            ActivePanelTitle = restored.Title;

            NotifyActivePanelChanged(restored.Panel);
            _session.AnnouncePanelEntry(restored.Panel, restored.Title);
            Log.Info($"[NavigationManager] Restored panel: \"{restored.Title}\", cursor={_session.Cursor.Current?.Role ?? "none"}");
        }
    }

    internal void CyclePanel(int direction)
    {
        var host = NavigationManager.Host;
        if (host?.Panels.GetAllOpen == null || host.Panels.NavigableLayers == null) return;

        var allPanels = host.Panels.GetAllOpen();
        var navigable = new List<OpenPanel>();
        foreach (var entry in allPanels)
        {
            if (host.Panels.NavigableLayers.Contains(entry.LayerIndex))
                navigable.Add(entry);
        }

        if (navigable.Count <= 1) return;

        int currentIndex = -1;
        for (int i = 0; i < navigable.Count; i++)
        {
            if (SameInstance(navigable[i].Panel, ActivePanel))
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
        {
            // Active panel not in navigable list — jump to first
            currentIndex = 0;
        }
        else
        {
            currentIndex = (currentIndex + direction + navigable.Count) % navigable.Count;
        }

        var target = navigable[currentIndex];
        SwitchToPanel(target.Panel, target.Title);
    }

    private void SwitchToPanel(object panel, string title)
    {
        _session.Memory.CommitCursorPosition();
        // Don't push to _panelStack — this is browsing, not a panel lifecycle event
        _browsing = true;
        ActivePanel = panel;
        ActivePanelTitle = title;

        NotifyActivePanelChanged(panel);
        _session.AnnouncePanelEntry(panel, title);

        Log.Info($"[NavigationManager] Cycled to panel: \"{title}\", cursor={_session.Cursor.Current?.Role ?? "none"}");
    }

    internal void JumpToContinueButton()
    {
        var host = NavigationManager.Host;
        if (host?.Panels.FindContinueButton == null) return;

        var result = host.Panels.FindContinueButton();
        if (result == null)
        {
            host.Speak("continue button not visible on this screen");
            return;
        }

        var (element, panel, title) = result.Value;

        if (!SameInstance(panel, ActivePanel))
        {
            _browsing = true;
            ActivePanel = panel;
            ActivePanelTitle = title;
            NotifyActivePanelChanged(panel);
            // Scan the panel so tree is classified, but don't announce panel entry
            host.Stops.FindFirst(panel);
        }

        _session.Cursor.SetCursorAndAnnounce(element);
        Log.Info("[NavigationManager] Jumped to continue button.");
    }

    // Panel objects are IL2CPP wrappers, and wrapper reference identity is NOT guaranteed: the
    // same native panel can arrive as different wrapper objects in different event callbacks
    // (observed live on the interest-filter dialog close, which left a detached panel "active"
    // and broke every context-menu open until the next navigation). Reference equality is only
    // the fast path; the host's native-pointer comparison is the authority. Without the
    // capability (tests), reference equality governs, matching the seam's null semantics.
    internal static bool SameInstance(object a, object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        var isSame = NavigationManager.Host?.Panels.IsSameInstance;
        if (isSame == null) return false;
        try
        {
            return isSame(a, b);
        }
        catch (Exception e)
        {
            Log.Warn($"[NavigationManager] Panel instance comparison failed, treating as different: {e.GetType().Name}");
            return false;
        }
    }

    // Two panel objects are the same game panel when the host resolves them to the same panel
    // identity (PanelID). Reference inequality is expected: an in-place reload hands a fresh
    // instance. An absent capability or a failed read compares as different — the cost is one
    // redundant title announcement, never a missed one.
    private static bool IsSamePanelIdentity(object a, object b)
    {
        var getIdentity = NavigationManager.Host?.Panels.GetIdentity;
        if (getIdentity == null) return false;
        try
        {
            var idA = getIdentity(a);
            return !string.IsNullOrEmpty(idA) && idA == getIdentity(b);
        }
        catch (Exception e)
        {
            Log.Warn($"[NavigationManager] Panel identity comparison failed, treating as a new panel: {e.GetType().Name}");
            return false;
        }
    }

    private static void NotifyActivePanelChanged(object panel)
    {
        var host = NavigationManager.Host;
        if (host?.Panels.OnActivePanelChanged != null && host.Panels.GetName != null)
        {
            var panelName = host.Panels.GetName(panel);
            if (!string.IsNullOrEmpty(panelName))
                host.Panels.OnActivePanelChanged(panelName);
        }
    }
}
