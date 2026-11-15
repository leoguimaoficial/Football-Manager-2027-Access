using System;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Protects an open context menu from the streamed-table loaders. A streamed read drives the
/// table's shared scroll position to realize each row, and scrolling recycles virtualised rows —
/// including the row an open menu is anchored to, which makes the game silently drop the menu's
/// context: no CloseContextMenu, the popup left visible as an empty shell (verified live on the
/// squad First Team table: menu bound on the top row, one forced scroll to the bottom row, context
/// unbound). That was the "menu opens but its options never load" failure: closing a menu triggers
/// a page re-render whose table sweep takes seconds, and the next menu opened during the sweep got
/// its row recycled out from under it.
///
/// While a menu is open — or an open is in flight — the loaders stand down (abort their sweep).
/// The page they were reading sits behind the modal menu anyway, and a fresh render happens when
/// the menu closes.
/// </summary>
internal static class ContextMenuGuard
{
    /// <summary>Set for the duration of ServerHandles.OpenContextMenu (realize + bind + confirm).</summary>
    internal static volatile bool OpenInFlight;

    /// <summary>Main thread only. True when a menu context is bound or an open is in flight.</summary>
    internal static bool MenuActive()
    {
        if (OpenInFlight) return true;
        Adapters.ContextMenuOpener.Popup(out bool bound);
        return bound;
    }

    /// <summary>Thrown by a loader's per-frame op to abandon a sweep that would kill the open menu.</summary>
    internal sealed class AbortException : Exception
    {
        public AbortException() : base("streamed read aborted: context menu active") { }
    }
}
