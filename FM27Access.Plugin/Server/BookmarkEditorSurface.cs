using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The mod-owned bookmark editor: while open, ComposeBody renders only this section (modal, the
/// Navigator/Sound-Cues pattern). It edits <see cref="ModBookmarks"/> — the mod's own store,
/// where any destination from the Navigator's menu tree can occupy one of 12 Ctrl-chord slots —
/// separate from the game's bookmark system. The same native widget anchors both this inherited
/// Ctrl editor trigger and the native bar/popup exposed through FilterPopupSurface and
/// NativeBookmarkSurface; editing this store never changes the game's favorite records.
///
/// Layout: the close stop first (context-menu precedent), then "Current bookmarks (N/12)" as
/// one semantic TABLE — columns Key (the Ctrl chord), Action, Move (up/down stops in one cell),
/// Delete — then "Available destinations": the live menu tree as collapsible groups, one
/// disclosure per category and per subsection that has children, with add rows inside. Group
/// expansion is mod-owned per session (a hot reload or editor close keeps it; nothing persists),
/// folded into the token so a toggle re-renders. When all 12 slots are taken the available list
/// says so up front, and every add refuses with the same honest phrase.
///
/// Buttons carry their bookmark's name ("Move up: Messages", "Remove: Messages") — a table cell
/// gives row context to table navigation but not to the Tab order, and a bare "Move up" there
/// would be anonymous. Every edit commits to the store immediately (and persists), so "Save and
/// close" stays factual. Open state is mod-owned (a static bool; a hot reload resets it closed,
/// harmless). Main thread only for the build (NavigatorIndex reads live game objects).
/// </summary>
internal static class BookmarkEditorSurface
{
    private const string Title = "Bookmark editor";
    private const string CloseLabel = "Save and close";
    private const string TriggerLabel = "Edit bookmarks";
    private const string AvailableHeading = "Available destinations";
    private const string FullNote = "Bookmarks full. Remove one to add another.";

    private static bool _open;

    // Expanded available-list groups, keyed "category" or "category/subsection". Session-scoped
    // UI state (which groups the player unfolded), not game state; survives editor close so a
    // reopen shows the same shape.
    private static readonly HashSet<string> Expanded = new();

    internal static bool IsOpen() => _open;

    internal static void Toggle()
    {
        _open = !_open;
        SpeechPipeline.SpeakInterrupt(_open ? "bookmark editor opened" : "bookmark editor closed");
    }

    /// <summary>Close if open; a no-op otherwise. Navigation hygiene (nav/bookmark/history).</summary>
    internal static void CloseIfOpen() => _open = false;

    /// <summary>
    /// Folded into the screen token so the SSE poll re-renders on open, close, and every edit or
    /// group toggle — the store snapshot plus the expansion set IS the editor's rendered truth.
    /// </summary>
    internal static string Signature()
    {
        if (!_open) return "";
        var sb = new StringBuilder("bmedit:");
        foreach (var e in ModBookmarks.Snapshot())
            sb.Append(e.Uid).Append(':').Append(e.Obj).Append('|');
        sb.Append('/');
        foreach (var key in Expanded)
            sb.Append(key).Append('|');
        return sb.ToString();
    }

    /// <summary>The editor section for ComposeBody, or null when closed. Main thread.</summary>
    internal static (string Title, List<ScreenWalker.RenderItem> Items)? TryBuildSection()
    {
        if (!_open) return null;

        var items = new List<ScreenWalker.RenderItem>
        {
            new ScreenWalker.StopItem { Element = new BookmarkEditorCloseAdapter() },
        };

        var current = ModBookmarks.Snapshot();
        items.Add(new ScreenWalker.HeadingItem
        {
            Text = $"Current bookmarks ({current.Count}/{ModBookmarks.MaxSlots})",
            Level = 3,
        });
        if (current.Count == 0)
        {
            items.Add(new ScreenWalker.NoteItem { Text = "No bookmarks set. Add destinations from the list below." });
        }
        else
        {
            var table = new ScreenWalker.BookmarksTableItem();
            for (int i = 0; i < current.Count; i++)
            {
                var entry = current[i];
                table.Rows.Add(new ScreenWalker.BookmarkTableRow
                {
                    Chord = ModBookmarks.ChordName(i + 1),
                    Action = entry.Label,
                    Remove = new RemoveAdapter(entry),
                    MoveUp = i > 0 ? new MoveAdapter(entry, delta: -1) : null,
                    MoveDown = i < current.Count - 1 ? new MoveAdapter(entry, delta: +1) : null,
                });
            }
            items.Add(table);
        }

        items.Add(new ScreenWalker.HeadingItem { Text = AvailableHeading, Level = 3 });
        if (current.Count >= ModBookmarks.MaxSlots)
            items.Add(new ScreenWalker.NoteItem { Text = FullNote });
        AppendAvailable(items, current);

        return (Title, items);
    }

    // The live menu tree as collapsible groups: one disclosure per category, and inside an
    // expanded category one per subsection that has children of its own; add rows sit inside
    // their group. Unavailable/disabled entries and unreadable markers are dropped, and
    // duplicates dedupe by address — one row per destination. A subsection with children is a
    // disclosure only, never a row: its own uid is its Overview child's, offered inside. A
    // missing tree is one honest note, never an empty list that reads as "nothing bookmarkable".
    private static void AppendAvailable(List<ScreenWalker.RenderItem> items, List<ModBookmarks.Entry> current)
    {
        List<NavigatorIndex.Entry> tree;
        try { tree = NavigatorIndex.Build(); }
        catch (Exception e)
        {
            Degrade.Note($"bookmark editor: navigator tree build failed: {e.GetType().Name}: {e.Message}");
            tree = null;
        }
        if (tree == null)
        {
            items.Add(new ScreenWalker.NoteItem { Text = "Destination list " + Degrade.Unavailable });
            return;
        }

        var seen = new HashSet<(uint, int)>();
        foreach (var e in current) seen.Add((e.Uid, e.Obj));

        // The flat DFS list regrouped: per category, its direct entries and its child-bearing
        // subsections (whose deeper entries nest under them).
        int i = 0;
        while (i < tree.Count)
        {
            var cat = tree[i];
            if (cat.Depth != 0 || !Usable(cat)) { i++; continue; }
            string catKey = cat.Label;
            bool catOpen = Expanded.Contains(catKey);
            items.Add(new ScreenWalker.StopItem { Element = new GroupToggleAdapter(cat.Label, catKey, catOpen) });
            i++;
            while (i < tree.Count && tree[i].Depth > 0)
            {
                var entry = tree[i];
                bool hasChildren = i + 1 < tree.Count && tree[i + 1].Depth > entry.Depth;
                if (entry.Depth == 1 && hasChildren)
                {
                    string subKey = catKey + "/" + entry.Label;
                    bool subOpen = Expanded.Contains(subKey);
                    if (catOpen)
                        items.Add(new ScreenWalker.StopItem { Element = new GroupToggleAdapter(entry.Label, subKey, subOpen) });
                    i++;
                    while (i < tree.Count && tree[i].Depth > 1)
                    {
                        var child = tree[i];
                        if (catOpen && subOpen && Usable(child) && seen.Add((child.PanelUid, child.ObjectUniqueId)))
                            items.Add(new ScreenWalker.StopItem { Element = new AddAdapter(child) });
                        i++;
                    }
                    continue;
                }
                if (catOpen && Usable(entry) && seen.Add((entry.PanelUid, entry.ObjectUniqueId)))
                    items.Add(new ScreenWalker.StopItem { Element = new AddAdapter(entry) });
                i++;
            }
        }
    }

    private static bool Usable(NavigatorIndex.Entry entry)
        => entry.PanelUid != 0 && !entry.Disabled && entry.ConditionMet;

    /// <summary>
    /// The "Edit bookmarks" trigger stop the ScreenWalker emits beside the title bar's bookmark
    /// area. Synthetic; announces "expanded" while the editor is open.
    /// </summary>
    internal sealed class BookmarkEditorTriggerAdapter : IAccessibleElement
    {
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => TriggerLabel;
        public string GetPrimaryLabel() => TriggerLabel;
        public string GetDescription() => "Customize the companion's Ctrl shortcuts. These are separate from the game's native bookmark bar and its additional-bookmarks count.";
        public bool IsVisible() => true;
        public string GetState() => IsOpen() ? "expanded" : "";
        public string GetAnnouncement()
        {
            var state = GetState();
            return TriggerLabel + ", " + Role + (state.Length > 0 ? ", " + state : "");
        }
        public void Activate() => Toggle();
    }

    /// <summary>The editor's dismiss stop, first in the section. Every edit already persisted.</summary>
    internal sealed class BookmarkEditorCloseAdapter : IAccessibleElement
    {
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => CloseLabel;
        public string GetPrimaryLabel() => CloseLabel;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => CloseLabel + ", " + Role;
        public void Activate()
        {
            CloseIfOpen();
            SpeechPipeline.SpeakInterrupt("bookmark editor closed");
        }
    }

    /// <summary>A current bookmark's Delete-cell stop: "Remove: Messages".</summary>
    private sealed class RemoveAdapter : IAccessibleElement, IStableKeyElement
    {
        private readonly ModBookmarks.Entry _entry;
        internal RemoveAdapter(ModBookmarks.Entry entry) => _entry = entry;

        public string StableKey => $"modbm:r:{_entry.Uid}:{_entry.Obj}";
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => "Remove: " + _entry.Label;
        public string GetPrimaryLabel() => GetLabel();
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => GetLabel() + ", " + Role;
        public void Activate()
        {
            bool removed = ModBookmarks.Remove(_entry.Uid, _entry.Obj);
            SpeechPipeline.SpeakInterrupt(removed
                ? _entry.Label + " removed"
                : _entry.Label + " " + Degrade.Unavailable);
            if (!removed) Degrade.Note($"bookmark editor: remove missed {_entry.Uid}:{_entry.Obj}");
        }
    }

    /// <summary>A current bookmark's Move-cell stop: "Move up: Messages" / "Move down: Messages".</summary>
    private sealed class MoveAdapter : IAccessibleElement, IStableKeyElement
    {
        private readonly ModBookmarks.Entry _entry;
        private readonly int _delta;
        internal MoveAdapter(ModBookmarks.Entry entry, int delta) { _entry = entry; _delta = delta; }

        private string Verb => _delta < 0 ? "Move up" : "Move down";
        public string StableKey => $"modbm:{(_delta < 0 ? "u" : "d")}:{_entry.Uid}:{_entry.Obj}";
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => Verb + ": " + _entry.Label;
        public string GetPrimaryLabel() => GetLabel();
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => GetLabel() + ", " + Role;
        public void Activate()
        {
            int slot = ModBookmarks.Move(_entry.Uid, _entry.Obj, _delta);
            SpeechPipeline.SpeakInterrupt(slot > 0
                ? $"{_entry.Label} moved, {ModBookmarks.ChordName(slot)}"
                : _entry.Label + " not moved");
        }
    }

    /// <summary>
    /// A collapsible group's disclosure stop (a category, or a subsection with children).
    /// Rendered by <see cref="BookmarkGroupRenderer"/> with aria-expanded; toggling re-renders
    /// through the token and speaks the new state, since a morphed attribute alone is silent
    /// under the screen reader.
    /// </summary>
    internal sealed class GroupToggleAdapter : IAccessibleElement, IStableKeyElement
    {
        private readonly string _label;
        private readonly string _key;
        internal readonly bool IsExpanded;

        internal GroupToggleAdapter(string label, string key, bool isExpanded)
        {
            _label = label;
            _key = key;
            IsExpanded = isExpanded;
        }

        public string StableKey => "modbmg:" + _key;
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => _label;
        public string GetPrimaryLabel() => _label;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => IsExpanded ? "expanded" : "collapsed";
        public string GetAnnouncement() => _label + ", " + Role + ", " + GetState();
        public void Activate()
        {
            bool nowOpen;
            if (Expanded.Remove(_key)) nowOpen = false;
            else { Expanded.Add(_key); nowOpen = true; }
            SpeechPipeline.SpeakInterrupt(_label + (nowOpen ? " expanded" : " collapsed"));
        }
    }

    /// <summary>An available destination's add row: "Fixture Schedule, add".</summary>
    private sealed class AddAdapter : IAccessibleElement, IStableKeyElement
    {
        private readonly NavigatorIndex.Entry _entry;
        internal AddAdapter(NavigatorIndex.Entry entry) => _entry = entry;

        public string StableKey => $"modbm:a:{_entry.PanelUid}:{_entry.ObjectUniqueId}";
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => _entry.Label + ", add";
        public string GetPrimaryLabel() => GetLabel();
        public string GetDescription() => "";  // the group disclosures give the trail
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => GetLabel() + ", " + Role;
        public void Activate()
        {
            var err = ModBookmarks.Add(_entry.Label, _entry.Path, _entry.PanelUid, _entry.ObjectUniqueId);
            SpeechPipeline.SpeakInterrupt(err ?? $"{_entry.Label} added, {ModBookmarks.ChordName(ModBookmarks.Count)}");
        }
    }
}

/// <summary>
/// Renders a bookmark-editor group disclosure as a native-feeling collapsible: a button with
/// aria-expanded, so the screen reader announces "collapsed"/"expanded" with the label. The
/// rows inside exist in the DOM only while expanded (server-rendered), so collapsed content
/// is genuinely absent, not merely hidden.
/// </summary>
internal sealed class BookmarkGroupRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el) => el is BookmarkEditorSurface.GroupToggleAdapter;

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var toggle = (BookmarkEditorSurface.GroupToggleAdapter)el;
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        sb.Append("<p><button type=\"button\" id=\"").Append(id)
          .Append("\" aria-expanded=\"").Append(toggle.IsExpanded ? "true" : "false")
          .Append("\" onclick=\"act('").Append(id).Append("')\">")
          .Append(ServerText.Escape(el.GetLabel())).Append("</button></p>");
    }
}

/// <summary>
/// A synthetic adapter whose DOM id should follow a stable identity of its own rather than the
/// role+label fallback — so a label that carries mutable state (a slot number, a verb) keeps its
/// node across renders and focus survives the edit that changed it. Consumed by
/// ServerHandles.KeyFor.
/// </summary>
internal interface IStableKeyElement
{
    string StableKey { get; }
}
