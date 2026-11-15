using System;
using System.Collections.Generic;
using System.Threading;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Server-side searchable-dropdown interaction: the HTML surface's port of the speech
/// SearchDropdownHandler minus the keyboard layer. One session at a time, sharing the
/// single-session invariant with DropdownSessionManager (its CloseCurrentOnMainThread tears
/// this down too, so any /activate, /nav, /body, or a new dropdown closes an open search popup).
///
/// Like DropdownSessionManager, all game-object access runs on the main thread via
/// MainThreadDispatcher.Invoke; the request thread orchestrates the multi-frame waits (popup
/// visibility, filter re-bind) with sleeps between Invokes, standing in for the speech handler's
/// frame counting. The HTML widget is an ARIA editable combobox: the user types (debounced
/// as-you-type), each filter sets the game's search text and reads the rendered filtered window
/// back, and a pick confirms by the index into that window.
///
/// Endpoint contract with the client: Open returns "" on success (an error message otherwise);
/// Filter returns an &lt;li&gt; listbox fragment or plain status text; Select/Close return status.
/// </summary>
internal static class SearchDropdownSession
{
    private const int FrameMs = 16;
    private const int VisibleTimeoutMs = 1000; // speech handler: 30 frames
    private const int PopupSettleMs = 120;     // popup visible -> search field ready
    private const int FilterSettleMs = 120;    // text set -> binding re-filters -> rows rebind
    private const int FilterRetries = 4;       // an empty read may be "not rendered yet"
    private const int FilterRetryMs = 50;
    private const int SelectionSettleMs = 150; // selection propagates into the bound value
                                               // (~9 frames; a straggler self-corrects within
                                               // one LiveValues SSE tick)

    // Main-thread-owned (written/read only inside Invoke), like DropdownSessionManager._levels;
    // request-thread null checks are serialized by DropdownSessionManager.SessionGate, which
    // every entry point here holds too (the two session types close each other on open, so
    // their orchestrations must not interleave).
    private static SearchableDropdownAdapter _adapter;
    private static List<string> _choices = new();

    // Handle id the live session was opened for. An id-carrying action against a session owned
    // by a different control is refused (acting by index would hit the wrong dropdown); id-less
    // dev-driver calls act on whatever is open.
    private static string _ownerId;

    private static string ValidateOwner(string owner)
    {
        if (string.IsNullOrEmpty(owner)) return null;
        if (_adapter == null) return "No search is open. Refresh the screen.";
        return owner == _ownerId ? null : "Stale search state. Close and reopen the search.";
    }

    public static string Open(string id, string expectedScreen = null)
    {
        lock (DropdownSessionManager.SessionGate)
        try
        {
            string err = MainThreadDispatcher.Invoke<string>(() =>
            {
                var stale = ServerHandles.ValidateScreen(expectedScreen);
                if (stale != null) return stale;
                // Closes any open dropdown AND any prior search session (cross-wired into
                // DropdownSessionManager.CloseCurrentOnMainThread), so the game is never left
                // with two popups.
                DropdownSessionManager.CloseCurrentOnMainThread();
                var adapter = ServerHandles.ResolveSearchDropdownHandle(id, out var e);
                if (adapter == null) return e;
                adapter.ShowPopup();
                _adapter = adapter;
                _choices = new List<string>();
                _ownerId = id;
                return null;
            });
            if (err != null) return err;

            if (!WaitFor(() => _adapter.IsPopupVisible(), VisibleTimeoutMs))
                return FailAndClose("The dropdown did not open.");

            // The session is ready; the client follows immediately with a filter. Empty body =
            // success, so the client never has to parse a success message.
            Thread.Sleep(PopupSettleMs);
            return "";
        }
        catch (Exception ex) { return FailAndClose("Error: " + ex.Message); }
    }

    public static string Filter(string query, string owner = null)
    {
        lock (DropdownSessionManager.SessionGate)
        try
        {
            string stale = ValidateOwner(owner);
            if (stale != null) return stale;
            if (_adapter == null) return "No search is open. Refresh the screen.";

            MainThreadDispatcher.Invoke<object>(() => { _adapter.SetSearchText(query); return null; });
            Thread.Sleep(FilterSettleMs);

            List<string> choices = new();
            for (int attempt = 0; attempt < FilterRetries; attempt++)
            {
                choices = MainThreadDispatcher.Invoke(() => _adapter.ReadFilteredChoices());
                if (choices.Count > 0) break;
                Thread.Sleep(FilterRetryMs);
            }

            return MainThreadDispatcher.Invoke(() =>
            {
                _choices = choices;
                if (choices.Count == 0)
                    return string.IsNullOrEmpty(query)
                        ? "Type to search."
                        : "No results for " + DropdownHtml.StripRichText(query) + ".";
                return DropdownHtml.BuildSearchListbox(choices);
            });
        }
        catch (Exception ex) { return FailAndClose("Error: " + ex.Message); }
    }

    public static string Select(int index, string owner = null)
    {
        lock (DropdownSessionManager.SessionGate)
        try
        {
            string stale = ValidateOwner(owner);
            if (stale != null) return stale;
            string label = MainThreadDispatcher.Invoke(() =>
            {
                if (_adapter == null) return (string)null;
                if (index < 0 || index >= _choices.Count) return (string)null;
                string l = _choices[index];
                _adapter.ConfirmSelection(index);
                _adapter = null;
                _choices = new List<string>();
                _ownerId = null;
                return l;
            });
            if (label == null) return "That option is no longer available. Refresh the screen.";

            // The client refreshes as soon as this returns, but the game needs frames to
            // propagate the selection into the field's bound value; refreshing earlier re-renders
            // the OLD value (mirrors DropdownSessionManager.Select).
            Thread.Sleep(SelectionSettleMs);
            Log("select: " + label);
            return DropdownHtml.StripRichText(label) + " selected";
        }
        catch (Exception ex) { return FailAndClose("Error: " + ex.Message); }
    }

    public static string Close(string owner = null)
    {
        lock (DropdownSessionManager.SessionGate)
        try
        {
            // A close aimed at a superseded session must not tear down its replacement; the
            // caller's popup already closed when the replacement opened.
            if (!string.IsNullOrEmpty(owner) && _adapter != null && owner != _ownerId)
                return "Search closed.";
            return MainThreadDispatcher.Invoke(() =>
            {
                CloseOnMainThread();
                return "Search closed.";
            });
        }
        catch (Exception ex) { return ServerText.Failure("Closing the list failed.", ex); }
    }

    /// <summary>
    /// Close any active search session. MUST be called on the main thread (inside Invoke).
    /// Called from DropdownSessionManager.CloseCurrentOnMainThread so the two session types
    /// share one teardown chokepoint and the game is never left with an orphaned search popup.
    /// </summary>
    public static void CloseOnMainThread()
    {
        if (_adapter == null) return;
        try { _adapter.HideDropdown(); }
        catch (Exception ex) { Speech.Log.Warn($"[SearchDropdownSession] Hide on close failed: {ex.Message}"); }
        _adapter = null;
        _choices = new List<string>();
        _ownerId = null;
    }

    private static bool WaitFor(Func<bool> conditionOnMainThread, int timeoutMs)
    {
        // Wall-clock, not sleep-count: each Invoke costs a frame on top of the sleep, so charging
        // only the sleep stretched the nominal timeout to ~2x or worse (see DropdownSession.WaitFor).
        long start = Environment.TickCount64;
        while (true)
        {
            if (MainThreadDispatcher.Invoke(conditionOnMainThread)) return true;
            if (Environment.TickCount64 - start >= timeoutMs) return false;
            Thread.Sleep(FrameMs);
        }
    }

    private static string FailAndClose(string message)
    {
        try
        {
            MainThreadDispatcher.Invoke<object>(() => { CloseOnMainThread(); return null; });
        }
        catch (Exception ex) { Log("close after failure also failed: " + ex.Message); }
        Log(message);
        return message;
    }

    // Direct, not marshalled: see the note on DropdownSession.Log. Logging from inside a dispatched
    // action would otherwise block for the whole Invoke timeout and then lose the message.
    private static void Log(string msg) => Speech.Log.Info("[SearchDropdownSession] " + msg);
}
