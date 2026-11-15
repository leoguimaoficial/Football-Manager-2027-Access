using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// Remembers where the cursor was: the live last-announcement bookkeeping, the LRU of
/// saved positions per panel identity, and the deferred-restore retry machine used when
/// a saved element has not populated yet (virtualized tables). Fields the session's
/// panel-entry orchestration reads and writes directly are internal.
/// </summary>
internal sealed class CursorMemory
{
    private readonly NavigationSession _session;

    // Cursor restore: continuously updated with the current element's announcement
    internal string LastAnnouncement;
    internal int LastAnnouncementIndex = 1;

    // Deferred cursor restore: when initial restore fails (e.g. virtualized table not populated yet)
    internal string PendingRestoreAnnouncement;
    internal int PendingRestoreAnnouncementIndex = 1;
    internal object PendingRestorePanel;
    internal int PendingRestoreRetries;
    private const int MaxRestoreRetries = 10;
    internal bool NeedsCursorRestore { get; set; }

    // LRU list of saved cursor positions, keyed by panel identity
    private const int MaxSavedPositions = 5;
    internal (int DataIndex, int ColIndex)? LastTablePosition;
    internal readonly LinkedList<(string PanelKey, string Announcement, int AnnouncementIndex, (int DataIndex, int ColIndex)? TablePosition)> SavedPositions = new();

    internal CursorMemory(NavigationSession session) => _session = session;

    internal void RecordPosition(IAccessibleElement element)
    {
        var host = NavigationManager.Host;
        LastAnnouncement = element.GetAnnouncement();
        LastAnnouncementIndex = host.Stops.AnnouncementOccurrenceIndex != null && _session.Panels.ActivePanel != null
            ? host.Stops.AnnouncementOccurrenceIndex(_session.Panels.ActivePanel, element, LastAnnouncement)
            : 1;
        LastTablePosition = host.Contexts.GetTablePosition?.Invoke(element);
    }

    internal void ClearLastAnnouncement() => LastAnnouncement = null;

    internal void CommitCursorPosition()
    {
        var host = NavigationManager.Host;
        if (host?.Panels.GetIdentity == null || _session.Panels.ActivePanel == null || LastAnnouncement == null) return;

        var key = host.Panels.GetIdentity(_session.Panels.ActivePanel);
        if (key == null || host.CursorRestoreExcludedPanels.Contains(key)) return;

        // Remove existing entry for this panel (dedup)
        var node = SavedPositions.First;
        while (node != null)
        {
            if (node.Value.PanelKey == key)
            {
                SavedPositions.Remove(node);
                break;
            }
            node = node.Next;
        }

        // Add to front (most recent)
        SavedPositions.AddFirst((key, LastAnnouncement, LastAnnouncementIndex, LastTablePosition));

        // Evict oldest if over limit
        while (SavedPositions.Count > MaxSavedPositions)
            SavedPositions.RemoveLast();

        Log.Info($"[NavigationManager] Saved cursor position for panel \"{key}\": \"{LastAnnouncement}\" (occurrence {LastAnnouncementIndex}){(LastTablePosition.HasValue ? $" (table row={LastTablePosition.Value.DataIndex}, col={LastTablePosition.Value.ColIndex})" : "")}");
    }

    internal void TryRestoreCursor()
    {
        var host = NavigationManager.Host;
        if (PendingRestorePanel == null || PendingRestoreAnnouncement == null || host?.Stops.FindByAnnouncement == null)
        {
            NeedsCursorRestore = false;
            return;
        }

        // Metadata callbacks can supply another managed wrapper for the same
        // native panel while a virtualized row is still binding. Reuse the
        // panel tracker's pointer-aware identity; a genuinely replaced native
        // instance must still cancel this deferred restore.
        if (!PanelTracker.SameInstance(PendingRestorePanel, _session.Panels.ActivePanel))
        {
            NeedsCursorRestore = false;
            PendingRestoreAnnouncement = null;
            PendingRestorePanel = null;
            PendingRestoreRetries = 0;
            return;
        }

        PendingRestoreRetries++;
        if (PendingRestoreRetries > MaxRestoreRetries)
        {
            Log.Info($"[NavigationManager] Deferred restore gave up after {MaxRestoreRetries} attempts for \"{PendingRestoreAnnouncement}\"");
            NeedsCursorRestore = false;
            PendingRestoreAnnouncement = null;
            PendingRestorePanel = null;
            PendingRestoreRetries = 0;
            return;
        }

        var restored = host.Stops.FindByAnnouncement(PendingRestorePanel, PendingRestoreAnnouncement, PendingRestoreAnnouncementIndex);
        if (restored != null)
        {
            host.Stops.EnsureContext?.Invoke(restored);
            _session.Cursor.Current = restored;
            LastAnnouncement = restored.GetAnnouncement();
            LastAnnouncementIndex = PendingRestoreAnnouncementIndex;
            LastTablePosition = host.Contexts.GetTablePosition?.Invoke(restored);
            NeedsCursorRestore = false;
            PendingRestoreAnnouncement = null;
            PendingRestoreAnnouncementIndex = 1;
            PendingRestorePanel = null;
            PendingRestoreRetries = 0;
            _session.Announcer.AnnounceWithTransitions(_session.Announcer.GetDisplayAnnouncement(restored));
            Log.Info($"[NavigationManager] Deferred restore succeeded: \"{restored.GetAnnouncement()}\"");
        }
    }
}
