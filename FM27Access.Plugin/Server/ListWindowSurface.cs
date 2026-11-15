using System;
using System.Collections.Generic;
using System.Reflection;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Honest windowing for virtualised lists (<c>SI.UI.VirtualisedList</c> over an
/// <c>SIScrollView</c>, hosted by a <c>StreamedObjectList</c> or a derived list like the
/// Messages inbox's <c>FM.UI.MessagesStreamedObjectList</c>): only the items around the current
/// scroll position exist in the element tree, and their data streams from the game core on
/// realisation — items outside the window have no client-side data at all. Without this chrome
/// the companion silently renders that window as if it were the whole list (verified live
/// 2026-07-05 on the competition Injury Table card: 18 club sections in the list's Items, 3
/// realised and rendered — "the league has 3 clubs with injuries" lie; earlier on Messages:
/// 17 realised of 93 with the newest two weeks absent).
///
/// The fix renders what is knowable and pages the rest through the game's own realisation: a
/// window marker ("Showing items X to Y of Z") plus paging stops whose activation moves the
/// list's real scroll offset one viewport. The game realises the new window, the tree churn
/// re-renders the page, and the marker moves. No harvesting, no caching — every render reads
/// the live window, and items are only ever shown with live data behind them. A fully realised
/// list emits nothing (no gap to mark).
///
/// Main thread only (reads live VisualElements), like all panel-tree access.
/// </summary>
internal static class ListWindowSurface
{
    private static readonly PropertyInfo FocusItemCount = typeof(SI.UI.VirtualisedList).GetProperty(
        "SI.UI.Focus.IFocusListSource.ItemCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static int ReadItemCount(SI.UI.VirtualisedList list)
    {
        if (list == null) return -1;
        var collection = list.Items?.TryCast<Il2CppSystem.Collections.ICollection>();
        if (collection != null) return collection.Count;
        // FM27's IList proxy does not always expose its inherited ICollection.
        // The list's own focus-source getter reads the same native item count.
        return FocusItemCount?.GetValue(list) is int count ? count : -1;
    }
    /// <summary>
    /// Lists this size and under are expanded to full realisation automatically (one
    /// <c>SetView(0, total)</c> through the game's own controller — the same call paging makes,
    /// asking for everything instead of the next window). The walk that noticed the gap still
    /// renders the honest chrome; the game realises the rest over the next frames and the churn
    /// re-render shows the whole list with the chrome self-retired. Sized so the annoying cases
    /// (strengths/weaknesses observations, notable players, small stat lists) expand while the
    /// big lists that virtualisation exists for (a 90-message inbox, database screens) keep
    /// their paging — realising hundreds of rows keeps their widgets and bindings alive and
    /// laid out every frame for as long as the screen is open.
    /// </summary>
    private const int AutoExpandMaxItems = 30;

    // A list that refuses to stay expanded (a controller that recomputes its view from the
    // viewport and snaps back) must not be re-expanded on every render — that is an endless
    // churn loop. Two attempts per list instance, then it keeps its paging chrome.
    private const int MaxExpandAttempts = 2;
    private static readonly Dictionary<IntPtr, int> _expandAttempts = new();

    /// <summary>
    /// The wording for one list family. The Messages inbox reads top-down in time
    /// ("newer/older"); everything else gets the neutral list wording. Button labels double as
    /// the paging stops' stable handle keys (role + label), so changing one changes the
    /// element's DOM id.
    /// </summary>
    internal sealed class Labels
    {
        internal string Noun;            // window marker: "Showing {Noun} X to Y of Z"
        internal string UpLabel;         // paging stop toward the start of the list
        internal string DownLabel;       // paging stop toward the end of the list
        internal string UpLoadingCue;
        internal string DownLoadingCue;
        internal string AtStartCue;      // spoken when paging up at the list's true start
        internal string AtEndCue;
        internal string IncompleteNote;  // marker when the window itself is unreadable; Degrade.Unavailable is appended
        internal string DetachedCue;     // spoken when paging a destroyed list; Degrade.Unavailable is appended
        // Skip the "Showing {Noun} X to Y of Z" marker line. The paging stops still render, and
        // their presence is what keeps the windowed list honest (a "Show older messages" button
        // states there is more than what is shown); the marker adds only the exact range, which
        // the inbox's player found noise on every visit. Degrade notes (an unreadable window)
        // are never skipped.
        internal bool OmitWindowMarker;

        internal static readonly Labels Messages = new()
        {
            OmitWindowMarker = true,
            Noun = "messages",
            UpLabel = "Show newer messages",
            DownLabel = "Show older messages",
            UpLoadingCue = "newer messages loading",
            DownLoadingCue = "older messages loading",
            AtStartCue = "at newest messages",
            AtEndCue = "at oldest messages",
            IncompleteNote = "Message list may be incomplete, count ",
            DetachedCue = "message list ",
        };

        internal static readonly Labels Generic = new()
        {
            Noun = "items",
            UpLabel = "Show earlier items",
            DownLabel = "Show more items",
            UpLoadingCue = "earlier items loading",
            DownLoadingCue = "more items loading",
            AtStartCue = "at start of list",
            AtEndCue = "at end of list",
            IncompleteNote = "List may be incomplete, count ",
            DetachedCue = "list ",
        };
    }

    /// <summary>
    /// The window marker and paging stops for one virtualised list, appended before its realised
    /// items so the player hears where the window sits before the content. Emits nothing when
    /// the whole list is realised (no gap to mark). An unreadable window degrades to an honest
    /// marker: the items still render, but the player is told the list may be incomplete.
    /// </summary>
    internal static void AppendChrome(List<ScreenWalker.RenderItem> items, VisualElement listHost, Labels labels)
    {
        SI.UI.VirtualisedList list;
        ScrollView scroll;
        try
        {
            scroll = FindScrollView(listHost);
            list = scroll?.hierarchy.parent?.TryCast<SI.UI.VirtualisedList>();
        }
        catch (Exception e)
        {
            Degrade.Note($"list window: list lookup failed: {e.GetType().Name}");
            items.Add(new ScreenWalker.NoteItem { Text = labels.IncompleteNote + Degrade.Unavailable });
            return;
        }
        if (list == null) return; // No virtualised list under the host (empty-list switch state): nothing to mark.

        int total, start, end;
        try
        {
            total = ReadItemCount(list);
            var view = list.ActiveView;
            start = view.Start;
            end = view.End;
        }
        catch (Exception e)
        {
            Degrade.Note($"list window: view read failed: {e.GetType().Name}");
            items.Add(new ScreenWalker.NoteItem { Text = labels.IncompleteNote + Degrade.Unavailable });
            return;
        }
        if (total < 0)
        {
            Degrade.Note("list window: Items list unreadable");
            items.Add(new ScreenWalker.NoteItem { Text = labels.IncompleteNote + Degrade.Unavailable });
            return;
        }

        // Whole list realised: the page already shows everything, so say nothing.
        if (start <= 0 && end >= total) return;

        // Small list: ask the game to realise all of it now. This render still shows the honest
        // chrome below (the new items do not exist yet); the realisation churn re-renders the
        // page moments later with the full list and no chrome.
        if (total <= AutoExpandMaxItems)
            TryAutoExpand(list, total);

        // ActiveView is the realised range the walk below will render. 1-based for the player.
        if (!labels.OmitWindowMarker)
            items.Add(new ScreenWalker.NoteItem { Text = $"Showing {labels.Noun} {start + 1} to {Math.Min(end, total)} of {total}" });
        // A native overscan window can already include item0 while its viewport
        // is still scrolled. Later row-height binding then evicts that first
        // message again. Keep the original paging control until the viewport
        // actually reaches its start, instead of declaring newest too early.
        if (start > 0 || scroll.scrollOffset.y >= 1f)
            items.Add(new ScreenWalker.StopItem { Element = new PageAdapter(scroll, up: true, labels) });
        if (end < total)
            items.Add(new ScreenWalker.StopItem { Element = new PageAdapter(scroll, up: false, labels) });
    }

    /// <summary>
    /// The match report tiles' lists (the round's other scores, the live league table, a bench):
    /// their rows are folded into one table, so a partial window would read as the whole list —
    /// verified live 2026-10-05 on a Europa League half-time report, 12 of the round's 18 fixtures
    /// and 15 of the 36-club table. Every list under <paramref name="content"/> is asked to realise
    /// in full (the auto-expand recipe; these lists are a few dozen short rows, well inside the
    /// cost the threshold above guards). Returns null when every row is present, else the window
    /// actually shown ("Showing rows 1 to 15 of 36") for the render the expansion has not reached
    /// yet; the realisation churn re-renders the tile whole.
    /// </summary>
    internal static string ExpandForTile(VisualElement content)
    {
        var lists = new List<SI.UI.VirtualisedList>();
        try { CollectLists(content, lists); }
        catch (Exception e)
        {
            Degrade.Note($"tile list window: list lookup failed: {e.GetType().Name}");
            return Labels.Generic.IncompleteNote + Degrade.Unavailable;
        }

        string note = null;
        foreach (var list in lists)
        {
            int total, start, end;
            try
            {
                total = ReadItemCount(list);
                var view = list.ActiveView;
                start = view.Start;
                end = view.End;
            }
            catch (Exception e)
            {
                Degrade.Note($"tile list window: view read failed: {e.GetType().Name}");
                return Labels.Generic.IncompleteNote + Degrade.Unavailable;
            }
            if (total < 0)
            {
                Degrade.Note("tile list window: Items list unreadable");
                return Labels.Generic.IncompleteNote + Degrade.Unavailable;
            }
            if (start <= 0 && end >= total) continue;

            if (total <= TileExpandMaxItems) TryAutoExpand(list, total);
            note ??= $"Showing rows {start + 1} to {Math.Min(end, total)} of {total}";
        }
        return note;
    }

    // A tile list is never more than a competition's clubs or fixtures; anything larger keeps its
    // window marker rather than realising hundreds of rows.
    private const int TileExpandMaxItems = 60;

    private static void CollectLists(VisualElement el, List<SI.UI.VirtualisedList> into)
    {
        if (el.GetIl2CppType().Name == "VirtualisedList")
        {
            var list = el.TryCast<SI.UI.VirtualisedList>();
            if (list != null) { into.Add(list); return; }
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectLists(el.hierarchy[i], into);
    }

    // One expansion request through the game's own controller, bounded per list instance. The
    // pointer keys the LIVE list object; a destroyed list's entry is dead weight, so the map is
    // simply cleared when it grows past any plausible number of concurrently visible lists.
    private static void TryAutoExpand(SI.UI.VirtualisedList list, int total)
    {
        IntPtr key = list.Pointer;
        _expandAttempts.TryGetValue(key, out int attempts);
        if (attempts >= MaxExpandAttempts) return;
        if (_expandAttempts.Count > 64) _expandAttempts.Clear();
        _expandAttempts[key] = attempts + 1;
        try
        {
            // SetView alone slides the window but the controller clamps its SPAN to what the
            // viewport holds (verified live: an 11-item list asked for 0..10 landed on 5..11).
            // Overprovision is the game's own span extender — how much beyond the viewport to
            // keep realised — so raising it is what lets the full-list view actually stick.
            list.OverprovisionPercent = 100f * total;
            // FM27 can disable overprovision independently of its percentage.
            // Its View.End is exclusive (0..9 has Count 9). Both are required
            // to realise the complete bounded list, including its final item.
            list.OverprovisionEnabled = true;
            list.Controller.SetView(0, total, false);
            list.UpdateView();
        }
        catch (Exception e)
        {
            Degrade.Note($"list window: auto-expand failed: {e.GetType().Name}");
        }
    }

    /// <summary>
    /// True when the virtualised list under <paramref name="listHost"/> has items outside its
    /// realised window — the signal ElementBridge uses to promote the list to a Section so the
    /// speech cursor gets the paging context. False on any read failure (with a note): the list
    /// then walks as an ordinary fully-realised container, and the companion's chrome still
    /// marks the gap.
    /// </summary>
    internal static bool IsUnderRealised(VisualElement listHost)
    {
        try
        {
            var scroll = FindScrollView(listHost);
            var list = scroll?.hierarchy.parent?.TryCast<SI.UI.VirtualisedList>();
            if (list == null) return false;
            int total = ReadItemCount(list);
            if (total < 0) return false;
            var view = list.ActiveView;
            return view.Start > 0 || view.End < total;
        }
        catch (Exception e)
        {
            Degrade.Note($"list window: under-realisation probe failed: {e.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// True when the virtualised list behind this scroll view still has items beyond its
    /// realised window in the given direction — the only case where paging can realise
    /// anything new. Without this gate, a page at the list's end moves the scroll (tall
    /// sections leave slack below the last realised item) and the loading cue promises items
    /// that never come. Callers wrap in try/catch (live-element reads).
    /// </summary>
    internal static bool HasUnrealisedBeyond(ScrollView scroll, bool up)
    {
        var list = scroll.hierarchy.parent?.TryCast<SI.UI.VirtualisedList>();
        if (list == null) return false;
        int total = ReadItemCount(list);
        if (total < 0) return false;
        var view = list.ActiveView;
        return up ? view.Start > 0 : view.End < total;
    }

    internal static ScrollView FindScrollView(VisualElement root)
    {
        if (root.GetIl2CppType().Name == "SIScrollView") return root.TryCast<ScrollView>();
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindScrollView(root.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Move the list's scroll offset one viewport up (toward the list's start) or down, clamped
    /// to the list's ends. True with the spoken cue when the offset moved (the game realises the
    /// new window over the next frames); false when already at that end. Shared by the
    /// companion's paging stops and the speech cursor's list-boundary paging.
    /// </summary>
    internal static bool TryPage(ScrollView scroll, bool up, Labels labels, out string cue)
    {
        float page = scroll.contentViewport.layout.height;
        float max = Math.Max(0f, scroll.contentContainer.layout.height - page);
        float before = scroll.scrollOffset.y;
        float target = Math.Clamp(up ? before - page : before + page, 0f, max);
        scroll.scrollOffset = new UnityEngine.Vector2(scroll.scrollOffset.x, target);

        if (Math.Abs(scroll.scrollOffset.y - before) >= 1f)
        {
            cue = up ? labels.UpLoadingCue : labels.DownLoadingCue;
            return true;
        }

        // Offset paging could not move. That is usually the list's true end — but not always: some
        // virtualised lists never grow their content container past the viewport and change which
        // items exist by rebinding an index range instead of scrolling. Their scroll extent is zero,
        // so this paging has NO effect on them at all, and the player is left on a "Show more items"
        // stop that does nothing while the rest of the list stays unreachable (found live on the
        // training-schedule picker: 17 schedules, 11 realised, the custom ones among the 6 the
        // player could never get to). Fall through to paging the index range itself.
        return TryPageByView(scroll, up, labels, out cue);
    }

    /// <summary>
    /// Page a list that virtualises by index range rather than by scroll offset, by realising the
    /// next/previous window through the game's own <c>SetView</c>. The window keeps its current
    /// length and clamps to the list's ends; a window already at that end returns false, so the
    /// caller still speaks the honest "at end of list".
    /// </summary>
    private static bool TryPageByView(ScrollView scroll, bool up, Labels labels, out string cue)
    {
        cue = null;

        SI.UI.VirtualisedList list;
        int total, start, end;
        try
        {
            list = scroll.hierarchy.parent?.TryCast<SI.UI.VirtualisedList>();
            if (list == null) return false;
            total = ReadItemCount(list);
            var view = list.ActiveView;
            start = view.Start;
            end = view.End;
        }
        catch (Exception e)
        {
            Degrade.Note($"list window: view paging read failed: {e.GetType().Name}");
            return false;
        }

        if (total <= 0 || end <= start || end - start >= total) return false;

        int span = end - start;
        int newStart = up ? Math.Max(0, start - span) : Math.Min(total - span, start + span);
        if (newStart == start) return false;

        try { list.Controller.SetView(newStart, newStart + span, false); }
        catch (Exception e)
        {
            Degrade.Note($"list window: SetView failed: {e.GetType().Name}");
            return false;
        }

        cue = up ? labels.UpLoadingCue : labels.DownLoadingCue;
        return true;
    }

    /// <summary>
    /// A paging stop: one activation moves the list's scroll offset one viewport up (toward the
    /// list's start) or down, clamped to the list's ends. The game realises the items for the
    /// new window and the page re-renders from the churn; the spoken cue tells the player the
    /// press landed (the re-render itself is silent). Synthetic (no game button exists for
    /// this); keyed by role+label so its id is stable across window moves.
    /// </summary>
    internal sealed class PageAdapter : IAccessibleElement
    {
        private readonly ScrollView _scroll;
        private readonly bool _up;
        private readonly Labels _labels;

        internal PageAdapter(ScrollView scroll, bool up, Labels labels)
        {
            _scroll = scroll;
            _up = up;
            _labels = labels;
        }

        private string Label => _up ? _labels.UpLabel : _labels.DownLabel;

        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => Label;
        public string GetPrimaryLabel() => Label;
        public string GetDescription() => "";
        public string GetState() => "";
        public string GetAnnouncement() => Label + ", " + Role;
        public bool IsVisible() => _scroll.panel != null && ElementBridge.IsVisibleInHierarchy(_scroll);

        public void Activate()
        {
            // A destroyed VisualElement (screen left between render and click) has no panel.
            if (!IsVisible())
                throw new InvalidOperationException("That list is no longer on screen. Refresh.");

            if (TryPage(_scroll, _up, _labels, out string cue))
                SpeechPipeline.SpeakInterrupt(cue);
            else
                SpeechPipeline.SpeakInterrupt(_up ? _labels.AtStartCue : _labels.AtEndCue);
        }
    }
}
