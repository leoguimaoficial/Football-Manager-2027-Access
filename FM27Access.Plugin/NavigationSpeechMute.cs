namespace FM27Access.Plugin;

/// <summary>
/// A scoped mute on the navigation speech seam (the NavigationHost.Speak delegate wired in
/// CoreEntry). While a Run scope is open, navigation announcements are dropped — each one
/// logged, never silent — while all the bookkeeping inside the wrapped call (cursor placement,
/// panel stack, classification) runs untouched. Used for the arrival quiet window: the panel
/// churn between a sim ending and the arrival report speaking is chatter the report itself
/// supersedes. Scopes are depth-counted and try/finally-closed, so the mute can never stick
/// open past the call it wraps. Main thread only.
/// </summary>
internal static class NavigationSpeechMute
{
    private static int _depth;
    private static Func<string> _panelTitle;

    internal static bool Active => _depth > 0;

    // Panel arrivals keep their actual title, without automatically reading the
    // cursor's first control. Explicit cursor reads use the ordinary seam.
    internal static string Resolve(string announcement)
        => Active ? null : _panelTitle != null ? _panelTitle() : announcement;

    internal static void PanelEntry(Func<string> title, Action body)
    {
        var previous = _panelTitle;
        _panelTitle = title;
        try { body(); }
        finally { _panelTitle = previous; }
    }

    internal static T PanelEntry<T>(Func<string> title, Func<T> body)
    {
        var previous = _panelTitle;
        _panelTitle = title;
        try { return body(); }
        finally { _panelTitle = previous; }
    }

    internal static void Run(Action body)
    {
        _depth++;
        try { body(); }
        finally { _depth--; }
    }

    internal static T Run<T>(Func<T> body)
    {
        _depth++;
        try { return body(); }
        finally { _depth--; }
    }

    // Cross-frame scope for a server-driven UI session (the Team Instructions pair editor drives
    // a game card open/click/close across several frames): Push inside the session's first
    // main-thread Invoke, Pop inside its last, with the pop guaranteed by the request thread's
    // try/finally. The panel churn the session causes is machinery, not navigation the player
    // performed, so its announcements are chatter. Pop is floor-guarded so an unbalanced call
    // can never mute a scope it does not own.
    internal static void Push() => _depth++;

    internal static void Pop()
    {
        if (_depth > 0) _depth--;
    }
}
