using System.Collections.Generic;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Which page each paged table on the current screen is showing. This is view state, not game
/// state: the rows themselves are still re-read live from the game on every render, so nothing
/// stored here can put stale data in front of the player. The worst a wrong entry can do is name a
/// page that no longer exists, and every read clamps against the live row count before using it.
///
/// Keyed by the table's identity path — the same stable-across-renders key the handle ids use — and
/// dropped whole when the screen changes, so arriving back at a screen starts at page 1 rather than
/// resuming a page the player has no memory of choosing.
///
/// Read on the background request thread (the streamed load) and written on the main thread (the
/// pager handle), so every access takes the lock.
/// </summary>
internal static class TablePages
{
    private static readonly object _lock = new object();
    private static readonly Dictionary<string, int> _pages = new();
    private static string _screen;

    /// <summary>
    /// Drop every stored page when the screen token moves. Called once per compose, before the
    /// tables of that compose are loaded.
    /// </summary>
    internal static void SyncScreen(string token)
    {
        lock (_lock)
        {
            if (token == _screen) return;
            _screen = token;
            _pages.Clear();
        }
    }

    internal static int Get(string path)
    {
        if (path == null) return 0;
        lock (_lock)
            return _pages.TryGetValue(path, out var page) ? page : 0;
    }

    internal static void Set(string path, int page)
    {
        if (path == null) return;
        lock (_lock)
            _pages[path] = page;
    }
}
