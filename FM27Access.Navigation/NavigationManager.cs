using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// The speech cursor's public entry point. Holds the <see cref="NavigationHost"/> seam
/// the Plugin layer wires at Mount and forwards every operation to the current
/// <see cref="NavigationSession"/>, whose collaborators own the state: the cursor and
/// its movement (CursorNavigator), the active panel and history (PanelTracker), saved
/// positions and deferred restore (CursorMemory), and speech composition (Announcer).
///
/// The query methods bake in the seam's optionality: an unwired capability answers
/// with its neutral value (false, null, 0) exactly as the delegate seam's null checks
/// always have, so callers never touch the host directly.
/// </summary>
public static class NavigationManager
{
    internal static NavigationHost Host { get; private set; }

    internal static NavigationSession Session { get; private set; } = new();

    /// <summary>
    /// Wires the Plugin layer's capabilities. Called once per Mount; navigation state
    /// (cursor, history, saved positions) is untouched, so tests may re-Initialize
    /// mid-scenario to swap a capability without losing the cursor.
    /// </summary>
    public static void Initialize(NavigationHost host) => Host = host;

    public static bool NeedsCursorRestore
    {
        get => Session.Memory.NeedsCursorRestore;
        set => Session.Memory.NeedsCursorRestore = value;
    }

    /// <summary>
    /// Set when a panel loads but no navigable elements are found.
    /// The plugin layer polls this and schedules a delayed rescan.
    /// </summary>
    public static bool NeedsRescan
    {
        get => Session.Cursor.NeedsRescan;
        set => Session.Cursor.NeedsRescan = value;
    }

    // --- Queries over the host's capabilities ---

    /// <summary>Speaks through the pipeline, interrupting current speech. No-op before Initialize.</summary>
    public static void Speak(string text) => Host?.Speak(text);

    /// <summary>Whether the active profile hides this stop from both surfaces.</summary>
    public static bool IsHidden(IAccessibleElement element)
        => Host?.Profile.IsHidden?.Invoke(element) ?? false;

    public static bool IsHeading(IAccessibleElement element)
        => Host?.Profile.IsHeading?.Invoke(element) ?? false;

    /// <summary>The profile heading level of a stop (0 = not a heading).</summary>
    public static int GetHeadingLevel(IAccessibleElement element)
        => Host?.Profile.HeadingLevel?.Invoke(element) ?? 0;

    /// <summary>The active profile's rename for this stop, or null when none applies.</summary>
    public static string GetRenameLabel(IAccessibleElement element)
        => Host?.Profile.RenameLabel?.Invoke(element);

    /// <summary>
    /// The raw heading predicate, or null when no profile policy is wired — for callers
    /// that pass the predicate on (the H-key jump) and treat "unwired" as "no jump".
    /// </summary>
    public static Func<IAccessibleElement, bool> HeadingPredicate
        => Host?.Profile.IsHeading;

    /// <summary>The table-context finder, passed to the T-key container jump.</summary>
    public static Func<IAccessibleElement, IContainerContext> TableContextFinder
        => Host?.Kind(ContainerKind.Table)?.FindContext;

    /// <summary>A stable panel type identity, or null when unavailable.</summary>
    public static string GetPanelIdentity(object panel)
        => Host?.Panels.GetIdentity?.Invoke(panel);

    /// <summary>The panel template name (e.g. "PortalScreen"), or null when unavailable.</summary>
    public static string GetPanelName(object panel)
        => Host?.Panels.GetName?.Invoke(panel);

    /// <summary>
    /// Invokes the active-panel-changed hook directly (profile activation). Unlike the
    /// internal panel-lifecycle notification this passes empty names through, which the
    /// server uses to clear the active profile.
    /// </summary>
    public static void RaiseActivePanelChanged(string panelName)
        => Host?.Panels.OnActivePanelChanged?.Invoke(panelName);

    /// <summary>First stop in a panel; also rebuilds the classification cache.</summary>
    public static IAccessibleElement FindFirstStop(object panel)
        => Host?.Stops.FindFirst(panel);

    public static IAccessibleElement FindNextStop(IAccessibleElement element, NavigationDirection direction)
        => Host?.Stops.FindNext(element, direction);

    /// <summary>All open panels across all layers, or null when unwired.</summary>
    public static List<OpenPanel> GetAllOpenPanels()
        => Host?.Panels.GetAllOpen?.Invoke();

    /// <summary>
    /// Raw back/forward navigation for the HTML surface's history handle: null on
    /// success, an error message on failure. The speech-side <see cref="NavigateBack"/>
    /// speaks the error instead of returning it.
    /// </summary>
    public static string RequestNavigateBack() => Host.Panels.NavigateBack();
    public static string RequestNavigateForward() => Host.Panels.NavigateForward();

    // --- Panel lifecycle ---

    public static void OnPanelReady(object panel, string title, bool isLoadingScreen)
        => Session.Panels.OnPanelReady(panel, title, isLoadingScreen);

    public static bool OnPanelClosed(object panel)
        => Session.Panels.OnPanelClosed(panel);

    /// <summary>
    /// Force-dismiss the active loading screen when the game doesn't fire PanelClosed.
    /// The ProcessingCard panel is hidden without a close event, leaving the mod stuck
    /// in loading-screen mode. This restores the top stacked panel.
    /// </summary>
    public static void DismissLoadingScreen()
        => Session.Panels.DismissLoadingScreen();

    // --- Cursor movement ---

    public static void MoveCursor(NavigationDirection direction)
        => Session.Cursor.MoveCursor(direction);

    /// <summary>
    /// Ctrl+Alt+Arrow: move between cells in a table, never exiting.
    /// Announces boundary messages at edges.
    /// </summary>
    public static void MoveTableCell(NavigationDirection direction)
        => Session.Cursor.MoveTableCell(direction);

    /// <summary>
    /// Plain Left/Right in a table: move between stops within a single cell.
    /// Does nothing at cell edges or in single-stop cells.
    /// </summary>
    public static void MoveTableInnerCell(NavigationDirection direction)
        => Session.Cursor.MoveTableInnerCell(direction);

    /// <summary>
    /// Plain Up/Down in a table: exits the table only at boundaries
    /// (Up on header, Down on last row). Does nothing otherwise.
    /// </summary>
    public static void ExitTableIfAtBoundary(NavigationDirection direction)
        => Session.Cursor.ExitTableIfAtBoundary(direction);

    /// <summary>
    /// Returns true if the current element is inside a table context.
    /// </summary>
    public static bool IsCurrentInTable()
        => Session.Cursor.IsCurrentInTable();

    public static bool IsCurrentInCalendar()
        => Session.Cursor.IsCurrentInCalendar();

    public static void MoveCalendarDay(NavigationDirection direction)
        => Session.Cursor.MoveCalendarDay(direction);

    public static void MoveCalendarInnerStop(NavigationDirection direction)
        => Session.Cursor.MoveCalendarInnerStop(direction);

    public static IAccessibleElement GetCurrentElement() => Session.Cursor.Current;
    public static object GetActivePanel() => Session.Panels.ActivePanel;

    /// <summary>
    /// Sets the cursor to the given element and ensures context, without announcing.
    /// Used by CursorJump helpers.
    /// </summary>
    public static void SetCursorSilently(IAccessibleElement element)
        => Session.Cursor.SetCursorSilently(element);

    public static void SetCursorAndAnnounce(IAccessibleElement element)
        => Session.Cursor.SetCursorAndAnnounce(element);

    // --- Panel cycling (Ctrl+Left / Ctrl+Right) ---

    public static void CyclePanelForward()
        => Session.Panels.CyclePanel(+1);

    public static void CyclePanelBackward()
        => Session.Panels.CyclePanel(-1);

    // --- Jumps ---

    public static void JumpToMatch(Func<IAccessibleElement, bool> match, bool forward, string notFoundMessage)
        => CursorJump.ToMatch(match, forward, notFoundMessage);

    public static void JumpToNextContainer(
        Func<IAccessibleElement, IContainerContext> findContainerContext,
        bool forward,
        string notFoundMessage)
        => CursorJump.ToNextContainer(findContainerContext, forward, notFoundMessage);

    public static void JumpToFirst()
        => Session.Cursor.JumpToFirst();

    public static void JumpToLast()
        => Session.Cursor.JumpToLast();

    // --- Rescans ---

    /// <summary>
    /// Re-scans the active panel from its root, clearing the classification cache.
    /// Call after in-panel content changes (e.g. wizard step pagination).
    /// Keeps the cursor's position when possible: on binding-heavy panels (e.g. the Tactics
    /// Planner toolbar) the game constantly detaches and rebuilds elements, and the rebuilt
    /// stop still matches the stored announcement — the cursor stays put, silently. Only when
    /// no match exists does the cursor reset to the first stop, announced.
    /// Returns true when the position was restored, false on a reset (or an empty panel).
    /// </summary>
    public static bool RescanPanel()
        => Session.Cursor.RescanPanel();

    /// <summary>
    /// Re-scans the panel but keeps the cursor in place if the current element is still visible.
    /// Use for non-structural activations (checkboxes, dropdowns) where content may change
    /// but the user should stay where they are.
    /// </summary>
    public static void RescanPanelKeepCursor()
        => Session.Cursor.RescanPanelKeepCursor();

    // --- Continue button, history, current element ---

    public static void AnnounceContinueButton()
    {
        if (Host?.Panels.FindContinueButton == null) return;

        var result = Host.Panels.FindContinueButton();
        if (result == null)
        {
            Speak("continue button not visible on this screen");
            return;
        }

        Speak(Session.Announcer.GetDisplayAnnouncement(result.Value.Element));
    }

    public static void JumpToContinueButton()
        => Session.Panels.JumpToContinueButton();

    public static void NavigateBack()
    {
        if (Host?.Panels.NavigateBack == null) return;
        var error = Host.Panels.NavigateBack();
        if (error != null)
            Speak(error);
        else
            Log.Info("[NavigationManager] Navigated back.");
    }

    public static void NavigateForward()
    {
        if (Host?.Panels.NavigateForward == null) return;
        var error = Host.Panels.NavigateForward();
        if (error != null)
            Speak(error);
        else
            Log.Info("[NavigationManager] Navigated forward.");
    }

    public static void AnnounceCurrentElement()
    {
        if (Session.Cursor.Current == null)
        {
            Log.Warn("[NavigationManager] AnnounceCurrentElement called with no cursor.");
            return;
        }

        Speak(Session.Announcer.GetDisplayAnnouncement(Session.Cursor.Current));
    }

    public static void TryRestoreCursor()
        => Session.Memory.TryRestoreCursor();

    // --- Initialization and reset ---

    private static bool _validated;

    internal static void ResetValidation()
    {
        _validated = false;
        Session = new NavigationSession();
    }

    /// <summary>
    /// Drops the host the Plugin layer wired plus all navigation state, so nothing dangles
    /// into a core assembly that a reload is about to discard. NavigationManager itself is
    /// permanent (it is not reloaded), so the next Mount wires a fresh host built against
    /// the new assembly.
    /// </summary>
    public static void ResetForReload()
    {
        Host = null;
        ResetValidation();
    }

    internal static void ValidateInitialized()
    {
        if (_validated) return;
        _validated = true;

        if (Host == null)
        {
            // The host's required members make a partially-wired seam unrepresentable, so
            // the only wiring failure left is not calling Initialize at all.
            var msg = "NavigationManager missing required delegates: no NavigationHost wired (Initialize not called)";
            Log.Fatal(msg);
            throw new InvalidOperationException(msg);
        }
    }
}
