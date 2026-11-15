using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Navigator (docs/navigator-plan.md): a modal search page over every navigation destination
/// the game currently offers — categories, subsections, dropdown children — plus nothing else, so
/// F1, a few letters and Enter replace the walk to the bars. HTML-only, like the sound cues page
/// (the speech cursor never sees it), and modal the same way: while open, ComposeBody renders only
/// this section. Open state is mod-owned; a hot reload resets it closed, which is safe (stateless).
///
/// The destination list is built fresh by <see cref="NavigatorIndex"/> on every open (never
/// cached), and filtering is entirely client-side (FM.navigator in app.js) over the rendered
/// listbox — the full option set is honest at render time, and activation re-resolves live in
/// NavigatorIndex.Go, so a stale option can only ever produce an honest "not found" reply.
/// Entries the game itself would not show (condition off / disabled) are omitted — the Navigator
/// mirrors the real bars, never a wider invented menu. Unreadable entries render as unmistakable
/// marker options that refuse to navigate.
/// </summary>
internal static class NavigatorSurface
{
    private const string PageTitle = "Navigator";

    /// <summary>The browse tree's accessible name, identical on the Navigator modal and the title
    /// bar's in-page copy so the player learns one name for one widget. No "tree"/"view" word in
    /// it — the screen reader appends the role itself ("Menu browser, tree view"), and no heading
    /// accompanies the widget: the label + role announcement is the whole introduction.</summary>
    internal const string TreeHeading = "Menu browser";

    /// <summary>The combined widget's search box label, shared by both homes.</summary>
    internal const string SearchLabel = "Search all screens";

    private static bool _open;

    internal static bool IsOpen() => _open;

    /// <summary>Folded into the screen token so the SSE poll re-renders on open and close.</summary>
    internal static string Signature() => _open ? "navigator:open" : "";

    /// <summary>Close if open; a no-op otherwise. Navigation hygiene, like the other surfaces:
    /// any navigation away (ribbon, history, bookmark, or the Navigator's own Go) closes it.</summary>
    internal static void CloseIfOpen() => _open = false;

    public static void RegisterRoutes(Dictionary<string, Route> routes)
    {
        // Open/close marshal to the main thread themselves (the ServerHandles.Nav pattern).
        // The reply is feedback only on failure: on success the companion refreshes and focus
        // lands in the search box, which announces itself.
        routes["/navigator/open"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (Open(), (string)null),
        };
        routes["/navigator/close"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.\n",
            Handler = (q, b) => (Close(), (string)null),
        };
    }

    private static string Open()
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                // Only offered where the navigation model exists: on the main menu (no career
                // loaded) there is nothing to browse, so refuse honestly instead of opening an
                // empty page.
                List<NavigatorIndex.Entry> entries = null;
                try { entries = NavigatorIndex.Build(); }
                catch (Exception e)
                {
                    Degrade.Note($"[NavigatorSurface] open probe failed: {e.GetType().Name}: {e.Message}");
                }
                if (entries == null) return "Menu browser unavailable.";

                // The same hygiene as ribbon navigation: the Navigator replaces whatever modal
                // interaction was in flight, never stacks on top of it.
                DropdownSessionManager.CloseCurrentOnMainThread();
                BookmarkEditorSurface.CloseIfOpen();
                SoundCuesSurface.CloseIfOpen();
                FilterPopupSurface.CloseIfOpen();
                MessageViewSurface.CloseIfOpen();
                _open = true;
                return "";
            });
        }
        catch (Exception ex) { return ServerText.Failure("Navigator failed.", ex); }
    }

    private static string Close()
    {
        try
        {
            return MainThreadDispatcher.Invoke(() =>
            {
                _open = false;
                return "";
            });
        }
        catch (Exception ex) { return ServerText.Failure("Navigator failed.", ex); }
    }

    /// <summary>
    /// The page for ComposeBody, or null when closed. One close stop (context-menu precedent),
    /// then the search box stop, rendered by <see cref="NavigatorBoxRenderer"/> as a combobox over
    /// the full destination listbox. An index failure degrades to an honest note — the page still
    /// renders and still closes, it just says it has nothing rather than showing a wrong list.
    /// </summary>
    internal static (string Title, List<ScreenWalker.RenderItem> Items)? TryBuildSection()
    {
        if (!_open) return null;

        var items = new List<ScreenWalker.RenderItem>
        {
            new ScreenWalker.StopItem { Element = new CloseAdapter() },
        };

        List<NavigatorIndex.Entry> entries = null;
        try { entries = NavigatorIndex.Build(); }
        catch (Exception e)
        {
            Degrade.Note($"[NavigatorSurface] index build failed: {e.GetType().Name}: {e.Message}");
        }
        if (entries == null)
        {
            items.Add(new ScreenWalker.NoteItem { Text = "Destinations " + Degrade.Unavailable + "." });
            return (PageTitle, items);
        }

        items.Add(new ScreenWalker.StopItem { Element = new BoxAdapter(entries) });
        return (PageTitle, items);
    }

    // --- adapters --------------------------------------------------------------------------------

    /// <summary>The page's dismiss stop, first in the section (context-menu precedent).</summary>
    internal sealed class CloseAdapter : IAccessibleElement
    {
        private const string CloseLabel = "Close Navigator";
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => CloseLabel;
        public string GetPrimaryLabel() => CloseLabel;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => CloseLabel + ", " + Role;
        public void Activate() => CloseIfOpen();
    }

    /// <summary>
    /// The title bar's Menu Treeview stop: the same browse tree as the Navigator modal, injected
    /// where the game's secondary nav bar (the "&lt;category&gt; sections" tabs) used to render —
    /// see ScreenWalker.WalkRec. Entries are captured fresh at walk time (main thread); a failed
    /// build renders as an honest gap, never an empty tree that reads as "no destinations".
    /// </summary>
    internal sealed class MenuTreeAdapter : IAccessibleElement
    {
        internal readonly List<NavigatorIndex.Entry> Entries; // null = build failed
        internal MenuTreeAdapter()
        {
            try { Entries = NavigatorIndex.Build(); }
            catch (Exception e)
            {
                Degrade.Note($"[NavigatorSurface] menu tree build failed: {e.GetType().Name}: {e.Message}");
            }
        }

        public string Role => "tree";
        public bool CanActivate => false;
        public bool HasContextMenu => false;
        public string GetLabel() => TreeHeading;
        public string GetPrimaryLabel() => TreeHeading;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => TreeHeading + ", " + Role;
        public void Activate()
            => Degrade.Note("[NavigatorSurface] menu tree activated server-side; it is client-side only.");
    }

    /// <summary>
    /// The search box plus its full destination list. All interaction is client-side
    /// (FM.navigator); a server-side activation should never happen and degrades honestly.
    /// </summary>
    internal sealed class BoxAdapter : IAccessibleElement
    {
        internal readonly List<NavigatorIndex.Entry> Entries;
        internal BoxAdapter(List<NavigatorIndex.Entry> entries) => Entries = entries;

        private const string BoxLabel = "Search all screens";
        public string Role => "combobox";
        public bool CanActivate => false;
        public bool HasContextMenu => false;
        public string GetLabel() => BoxLabel;
        public string GetPrimaryLabel() => BoxLabel;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => BoxLabel + ", " + Role;
        public void Activate()
            => Degrade.Note("[NavigatorSurface] search box activated server-side; it is client-side only.");
    }
}

/// <summary>
/// Renders the Navigator's search stop as an ARIA editable combobox (the FM.search markup shape)
/// plus, below it, the same entries as an ARIA tree — search and browse over one dataset. Options
/// and treeitems carry the (uid, obj) address for /navigator/go; the options also carry a
/// lowercase filter string (label + owning path) for FM.navigator's local word filter. No server
/// handles — the widgets own all interaction client-side. Entries whose label is the
/// unknown-control marker render without an address, which the client refuses to navigate (the
/// gap stays visible, the misfire stays impossible).
/// </summary>
internal sealed class NavigatorBoxRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el) => el is NavigatorSurface.BoxAdapter;

    // The visible set, shared by every view. Marker entries stay visible regardless of flags
    // (their Marker rows carry ConditionMet=false): hiding a gap would make it silent. Everything
    // else mirrors the real bars: entries the game would not show are not destinations.
    internal static List<NavigatorIndex.Entry> VisibleEntries(List<NavigatorIndex.Entry> entries)
    {
        var visible = new List<NavigatorIndex.Entry>();
        foreach (var entry in entries)
        {
            bool marker = entry.Label == Degrade.UnknownControl;
            if (!marker && (entry.Disabled || !entry.ConditionMet)) continue;
            visible.Add(entry);
        }
        return visible;
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var box = (NavigatorSurface.BoxAdapter)el;
        RenderWidget(sb, VisibleEntries(box.Entries), modal: true);
    }

    // The combined widget, shared by both homes: one search box that filters the tree itself
    // (FM.navigator prunes to matching branches, auto-expanding their ancestors; Enter in the
    // box goes to the first match, Down hands focus to it), then the tree. There is no separate
    // results listbox — the pruned tree IS the result view, so a match is always announced in
    // its place in the hierarchy. data-home tells the client which home it is driving: with both
    // homes carrying the box, "the input exists" no longer identifies the open modal (F1's
    // toggle and Alt+M's modal-exclusion check need this).
    internal static void RenderWidget(StringBuilder sb, List<NavigatorIndex.Entry> visible, bool modal)
    {
        sb.Append("<p><input id=\"fmnavq\" type=\"text\" data-nav=\"1\" data-home=\"")
          .Append(modal ? "modal" : "page")
          .Append("\" aria-label=\"").Append(ServerText.Escape(NavigatorSurface.SearchLabel))
          .Append("\" aria-controls=\"fmnav-tree\"")
          .Append(" oninput=\"FM.navigator.filter(this)\" autocomplete=\"off\"></p>");
        RenderTree(sb, visible);
    }

    // The hierarchical browse view: the same entries as an ARIA tree, nested by Depth (the flat
    // list is DFS order, so an entry is only ever one level deeper than the one before it, and a
    // parent is exactly "the next visible entry is deeper"). Parents default collapsed with their
    // children in a hidden group; FM.navigator owns the tree keyboard contract and remembers
    // expansion across refreshes by data-key. The caller emits the heading: the modal and the
    // title bar head it at different levels. Shared ids are safe — the modal replaces the page,
    // so only one tree ever exists at a time, and FM.navigator drives whichever is present.
    internal static void RenderTree(StringBuilder sb, List<NavigatorIndex.Entry> visible)
    {
        sb.Append("<ul role=\"tree\" id=\"fmnav-tree\" aria-label=\"")
          .Append(ServerText.Escape(NavigatorSurface.TreeHeading)).Append("\">");

        int prevDepth = -1;
        for (int i = 0; i < visible.Count; i++)
        {
            var entry = visible[i];
            int d = entry.Depth;
            bool marker = entry.Label == Degrade.UnknownControl;
            bool hasChildren = i + 1 < visible.Count && visible[i + 1].Depth > d;

            if (prevDepth < d)
            {
                if (prevDepth >= 0) sb.Append("<ul role=\"group\" hidden>");
            }
            else
            {
                sb.Append("</li>");
                for (int k = d; k < prevDepth; k++) sb.Append("</ul></li>");
            }

            sb.Append("<li role=\"treeitem\" id=\"fmnav-t").Append(i)
              .Append("\" aria-level=\"").Append(d + 1)
              .Append("\" tabindex=\"").Append(i == 0 ? "0" : "-1").Append('"');
            if (hasChildren)
                sb.Append(" aria-expanded=\"false\" data-key=\"")
                  .Append(entry.PanelUid).Append(':').Append(entry.ObjectUniqueId).Append('"');
            AppendAddress(sb, entry, marker);
            // The search box's word filter matches label + owning path (the " > " separator
            // would only add noise tokens).
            sb.Append(" data-filter=\"")
              .Append(ServerText.Escape((entry.Label + " " + entry.Path.Replace(" > ", " ")).ToLowerInvariant()))
              .Append('"');
            sb.Append(" onclick=\"FM.navigator.treeClick(event,this)\">")
              .Append("<span>").Append(ServerText.Escape(entry.Label)).Append("</span>");
            prevDepth = d;
        }
        if (prevDepth >= 0)
        {
            sb.Append("</li>");
            for (int k = 0; k < prevDepth; k++) sb.Append("</ul></li>");
        }
        sb.Append("</ul>");
    }

    internal static void AppendAddress(StringBuilder sb, NavigatorIndex.Entry entry, bool marker)
    {
        if (marker) return;
        sb.Append(" data-uid=\"").Append(entry.PanelUid).Append('"');
        if (entry.ObjectUniqueId != -1)
            sb.Append(" data-obj=\"").Append(entry.ObjectUniqueId).Append('"');
    }
}

/// <summary>
/// Renders the title bar's Menu browser stop: the same combined search-box-plus-tree widget as
/// the Navigator modal (data-home="page"). A failed entry build renders an honest unavailable
/// line rather than an empty widget.
/// </summary>
internal sealed class MenuTreeRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el) => el is NavigatorSurface.MenuTreeAdapter;

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var tree = (NavigatorSurface.MenuTreeAdapter)el;
        if (tree.Entries == null)
        {
            sb.Append("<p>Destinations ").Append(ServerText.Escape(Degrade.Unavailable)).Append(".</p>");
            return;
        }
        NavigatorBoxRenderer.RenderWidget(sb, NavigatorBoxRenderer.VisibleEntries(tree.Entries), modal: false);
    }
}
