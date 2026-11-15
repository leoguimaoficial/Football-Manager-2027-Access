using FM27Access.Speech;

namespace FM27Access.Navigation;

/// <summary>
/// Composes the navigation collaborators and owns the one cross-cutting sequence they
/// share: announcing entry into a panel (classify the tree, try to restore the saved
/// cursor or schedule a deferred restore, then speak). A reload or test reset replaces
/// the whole session, so no collaborator state can leak across resets.
/// </summary>
internal sealed class NavigationSession
{
    internal CursorNavigator Cursor { get; }
    internal PanelTracker Panels { get; }
    internal CursorMemory Memory { get; }
    internal Announcer Announcer { get; }

    internal NavigationSession()
    {
        Cursor = new CursorNavigator(this);
        Panels = new PanelTracker(this);
        Memory = new CursorMemory(this);
        Announcer = new Announcer(this);
    }

    internal void AnnouncePanelEntry(object panel, string title)
    {
        var host = NavigationManager.Host;

        // Always rebuild the classification cache
        var firstStop = host.Stops.FindFirst(panel);
        Cursor.NeedsRescan = firstStop == null;

        // Try to restore cursor from saved positions
        IAccessibleElement restored = null;
        if (host.Stops.FindByAnnouncement != null && host.Panels.GetIdentity != null)
        {
            var key = host.Panels.GetIdentity(panel);
            if (key != null)
            {
                var node = Memory.SavedPositions.First;
                while (node != null)
                {
                    if (node.Value.PanelKey == key)
                    {
                        restored = host.Stops.FindByAnnouncement(panel, node.Value.Announcement, node.Value.AnnouncementIndex);
                        if (restored != null)
                        {
                            Log.Info($"[NavigationManager] Restored cursor to \"{node.Value.Announcement}\" (occurrence {node.Value.AnnouncementIndex})");
                        }
                        else
                        {
                            Log.Info($"[NavigationManager] Could not find saved element \"{node.Value.Announcement}\" (occurrence {node.Value.AnnouncementIndex}), deferring restore");
                            Memory.PendingRestoreAnnouncement = node.Value.Announcement;
                            Memory.PendingRestoreAnnouncementIndex = node.Value.AnnouncementIndex;
                            Memory.PendingRestorePanel = panel;
                            Memory.PendingRestoreRetries = 0;
                            Memory.NeedsCursorRestore = true;
                        }
                        break;
                    }
                    node = node.Next;
                }
            }
        }

        Cursor.Current = restored ?? firstStop;
        Memory.LastAnnouncement = Cursor.Current?.GetAnnouncement();
        Memory.LastAnnouncementIndex = (Cursor.Current != null && host.Stops.AnnouncementOccurrenceIndex != null && Panels.ActivePanel != null)
            ? host.Stops.AnnouncementOccurrenceIndex(Panels.ActivePanel, Cursor.Current, Memory.LastAnnouncement)
            : 1;

        if (Cursor.Current != null)
            host.Speak($"{title}. {Announcer.GetDisplayAnnouncement(Cursor.Current)}");
        else
            host.Speak(title);
    }
}
