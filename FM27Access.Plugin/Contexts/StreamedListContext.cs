using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Plugin.Server;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

/// <summary>
/// Speech-cursor parity for a virtualised streamed list (see <see cref="ListWindowSurface"/> for
/// the shared model and the companion half). Only a window of items is realised in the tree, so a
/// plain walk off the last realised item would silently skip everything beyond the window. This
/// context keeps the cursor inside the list at a window edge and pages the game's own scroll one
/// viewport instead: the boundary press speaks the loading cue, the game realises the next window
/// (the realised span overlaps a full page move, so the cursor's item survives), and the next
/// arrow press continues into the newly realised items. At the list's true ends there is nothing
/// more to load, so the cursor exits like any other container.
///
/// Created by ElementBridge for the Messages inbox list (always, as <see cref="MessagesListContext"/>
/// with inbox wording) and for any other streamed list promoted to Section because it is
/// under-realised (with the generic wording).
/// </summary>
internal class StreamedListContext : IContainerContext
{
    private readonly VisualElement _container;
    private readonly ClassificationCache _cache;
    private readonly ListWindowSurface.Labels _labels;

    // The cue for the boundary the cursor just paged at, set by Navigate and consumed by
    // GetBoundaryAnnouncement within the same keypress. Checked (not the live scroll state) by
    // ExitsOnBoundary so a page that lands exactly on the list's end still announces instead of
    // exiting on the same press.
    private string _pagedCue;

    public object Scope => _container;

    internal StreamedListContext(VisualElement container, ClassificationCache cache, ListWindowSurface.Labels labels)
    {
        _container = container;
        _cache = cache;
        _labels = labels;
    }

    public bool HandlesDirection(NavigationDirection direction)
        => direction == NavigationDirection.Up || direction == NavigationDirection.Down;

    public IAccessibleElement GetFirstChild()
        => TreeWalker.FindFirstStopInContainer(_container, _cache);

    public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
    {
        var backing = ((PluginAdapterBase)current).BackingElement;
        var next = direction == NavigationDirection.Down
            ? TreeWalker.FindNextStopBounded(backing, _container, _cache)
            : TreeWalker.FindPreviousStopBounded(backing, _container, _cache);
        if (next != null)
        {
            _pagedCue = null;
            PageAheadIfAtRealisedEdge(next, direction);
            return next;
        }

        // Window edge. Page the realisation window when more items exist that way; the cue is
        // spoken by GetBoundaryAnnouncement. An unreadable scroll view degrades to exit (the walk
        // beyond the list still works) with a note, never a silent stuck cursor.
        _pagedCue = null;
        try
        {
            bool up = direction == NavigationDirection.Up;
            var scroll = ListWindowSurface.FindScrollView(_container);
            if (scroll != null && ListWindowSurface.HasUnrealisedBeyond(scroll, up)
                && ListWindowSurface.TryPage(scroll, up, _labels, out string cue))
                _pagedCue = cue;
        }
        catch (Exception e)
        {
            Degrade.Note($"streamed list cursor: paging failed: {e.GetType().Name}");
        }
        return null;
    }

    /// <summary>
    /// Page the realisation window one viewport when the stop the cursor is moving to is the LAST
    /// realised stop in that direction. Item structure realises synchronously on the scroll move,
    /// but item TEXTS bind a beat later — an item reached mid-bind announces as "unlabeled icon
    /// button" (the streamed-binding settle race). Paging one press early gives the new items that
    /// press-cycle to bind, so the cursor lands on real text. Silent: the player just hears the
    /// item they moved to. A failed lookahead degrades to the boundary-time page in Navigate.
    /// </summary>
    private void PageAheadIfAtRealisedEdge(IAccessibleElement next, NavigationDirection direction)
    {
        try
        {
            var nextBacking = ((PluginAdapterBase)next).BackingElement;
            var beyond = direction == NavigationDirection.Down
                ? TreeWalker.FindNextStopBounded(nextBacking, _container, _cache)
                : TreeWalker.FindPreviousStopBounded(nextBacking, _container, _cache);
            if (beyond != null) return;

            bool up = direction == NavigationDirection.Up;
            var scroll = ListWindowSurface.FindScrollView(_container);
            if (scroll != null && ListWindowSurface.HasUnrealisedBeyond(scroll, up))
                ListWindowSurface.TryPage(scroll, up, _labels, out _);
        }
        catch (Exception e)
        {
            Degrade.Note($"streamed list cursor: page-ahead failed: {e.GetType().Name}");
        }
    }

    public bool ExitsOnBoundary(NavigationDirection direction) => _pagedCue == null;

    public string GetBoundaryAnnouncement(NavigationDirection direction)
    {
        var cue = _pagedCue;
        _pagedCue = null;
        return cue;
    }
}

/// <summary>
/// The Messages inbox list's paging context: <see cref="StreamedListContext"/> with the inbox
/// wording. A distinct type so CoreEntry's "message list" container kind can keep its own
/// enter/exit announcements while every other streamed list announces as a plain "list".
/// </summary>
internal sealed class MessagesListContext : StreamedListContext
{
    internal MessagesListContext(VisualElement container, ClassificationCache cache)
        : base(container, cache, ListWindowSurface.Labels.Messages)
    {
    }
}
