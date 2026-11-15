using System.Collections.Generic;
using System.Text;
using FM27Access.Plugin.Adapters;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The companion-surface bridge for the game's cascading context menus (the squad "Actions" menu and
/// its kin). The menu opens as a floating element — <c>SISafeAreaElement "floating-element-root"</c> →
/// <c>CascadingMenu</c> — parented to the runtime panel root as a sibling of the PanelManager panels,
/// so it never appears in the interactive panel set the page normally renders.
///
/// The menu is navigated through its DATA MODEL, not the live VisualElements: the CascadingMenu's
/// <c>MenuDescription</c> holds the entire entry tree (every folder and item, with each leaf's action
/// callback) the moment the menu opens, even for submenus the game has not visually expanded. Reading
/// that tree lets the companion browse the whole menu and invoke an action directly — the game only
/// opens a submenu on hover, and synthesising hover corrupts UI Toolkit pointer state and crashes, so
/// the visual menu is never driven. <see cref="_currentPath"/> tracks which folder the player has
/// drilled into (child indices from the root); folder entries push, the synthetic Back pops, leaf
/// items invoke their callback.
///
/// Main thread only (reads live VisualElements and IL2CPP objects), like all panel-tree access.
/// </summary>
internal static class ContextMenuSurface
{
    private static readonly Il2CppSystem.Reflection.BindingFlags Flags =
        Il2CppSystem.Reflection.BindingFlags.Public
        | Il2CppSystem.Reflection.BindingFlags.NonPublic
        | Il2CppSystem.Reflection.BindingFlags.Instance;

    // Child indices from the menu root to the folder the player is viewing. Empty = root. Reset when
    // the menu closes, so a reopened menu always starts at its root.
    private static List<int> _currentPath = new();

    /// <summary>
    /// The name of the entity whose menu is currently open — the label the menu view is titled by
    /// ("Actions for Mohamed Wright"), so the player hears whose menu opened rather than a bare
    /// "Actions". Set (already cleaned) when an open is dispatched in <see cref="ServerHandles"/>,
    /// cleared on <see cref="Close"/>. Null for a textless-holder menu (a nation flag) and for an
    /// in-flight/never-set state, both of which fall back to plain "Actions".
    /// </summary>
    internal static string OpenedOwnerLabel;

    /// <summary>
    /// Hides an open menu from the companion surface: while set, <see cref="TryGetMenu"/> answers
    /// null, so the screen token does not move and a body compose renders the normal page instead
    /// of the menu modal. Set only around a programmatic menu interaction the player never asked
    /// to see (TeamInstructions' commit drives a menu open-invoke-close in under a second); the
    /// setter is serialized under DropdownSessionManager.SessionGate, so it cannot hide a menu the
    /// player is actively browsing.
    /// </summary>
    internal static volatile bool SuppressCompanionView;

    /// <summary>
    /// The open menu's <c>CascadingMenu</c> element, or null when no menu is open. Resolved from
    /// the subsystem's own popup (the single lifecycle owner every cascading menu binds to), NOT
    /// by searching a panel tree: the old walk started from <c>NavigationManager.GetActivePanel()</c>,
    /// and a stale active panel (a modal dialog whose close event arrived as a different IL2CPP
    /// wrapper) made it miss a menu that was open and healthy on screen — the confirm then reported
    /// "did not open" and force-closed the player's real menu. "Open" means the bound menu has
    /// SURFACED: attached to a runtime panel and visible up its floating-root chain; a bound but
    /// not-yet-shown menu is <see cref="Adapters.ContextMenuOpener.PendingMenu"/>'s business.
    /// </summary>
    public static VisualElement TryGetMenu()
    {
        // Suppressed menus return null WITHOUT ClosedNull: the drill path belongs to a player
        // menu, and a programmatic commit window must not reset it as a side effect.
        if (SuppressCompanionView) return null;
        var menu = Adapters.ContextMenuOpener.PendingMenu();
        if (menu == null) return ClosedNull();
        if (menu.panel == null) return ClosedNull();
        // The chain above the menu is short (CascadingMenu → wrapper → floating-element-root →
        // PanelManager-container → root), so walking it is cheap. Any hidden ancestor means the
        // popup has not actually surfaced to the player.
        for (var cur = menu; cur != null; cur = cur.hierarchy.parent)
            if (!ElementBridge.IsVisible(cur)) return ClosedNull();
        return menu;
    }

    private static VisualElement ClosedNull()
    {
        if (_currentPath.Count > 0) _currentPath = new List<int>();
        return null;
    }

    /// <summary>
    /// Dismisses the open menu through the game's own lifecycle owner,
    /// <c>ContextMenuSubsystem.CloseContextMenu</c>: it hides the popup and unbinds the menu context,
    /// so the floating element leaves the tree and the next open starts clean. Resets the drill path.
    /// Safe to call when no menu is open (the subsystem no-ops), which is why a leaf activation calls
    /// it unconditionally — some actions self-close by changing the panel, others (e.g. Compare With)
    /// do not, and a double close is harmless. Verified live on the squad Actions menu: closes without
    /// crashing, and is a no-op when already closed. Main thread only (touches the live subsystem).
    /// </summary>
    public static void Close()
    {
        _currentPath = new List<int>();
        OpenedOwnerLabel = null;
        var subsystem = SI.Core.GameSubsystems.Get<SI.UI.ContextMenuSubsystem>();
        if (subsystem == null)
        {
            // The menu stays up (Escape in-game still dismisses it); this is an action failure, not
            // wrong data, so log and continue. The subsystem is a core game service, so this is
            // defensive — it should not happen in practice.
            Degrade.Note("context menu close: ContextMenuSubsystem unavailable");
            return;
        }
        subsystem.CloseContextMenu();
    }

    /// <summary>
    /// The current folder's entries as render stops: Back (in a submenu), then a "Close menu" dismiss
    /// stop, then one button per child entry.
    /// </summary>
    public static List<ScreenWalker.RenderItem> WalkEntries(VisualElement menu)
    {
        var items = new List<ScreenWalker.RenderItem>();
        var root = GetRootEntry(menu);
        if (root == null)
        {
            // A menu whose description never filled (a bound context whose entity resolved no
            // actions) is an unreadable modal shell; without a dismiss stop it traps the player.
            // The marker matters as much as the dismiss: an entryless level is otherwise
            // indistinguishable from a folder the game genuinely left empty (see below), and the
            // player must be able to tell "the mod could not read this" from "there is nothing here".
            items.Add(Stop(new MenuDataEntryAdapter("Close menu", true, false, Close) { IsMenuDismiss = true }));
            items.Add(Stop(new MenuDataEntryAdapter(Speech.Degrade.Read("context menu description unreadable"),
                false, false, () => { })));
            return items;
        }

        var path = new List<int>(_currentPath);
        var folder = ResolveFolder(root, path);

        if (path.Count > 0)
        {
            var parent = new List<int>(path);
            parent.RemoveAt(parent.Count - 1);
            items.Add(Stop(new MenuDataEntryAdapter("Back", true, false, () => _currentPath = parent) { IsMenuBack = true }));
        }

        // A dismiss-all stop so a companion-only user can leave the modal menu without performing an
        // action. Placed near the top (after Back) so it stays reachable from a deep or long submenu
        // without arrowing past every entry. Closes through the same lifecycle owner an action uses.
        items.Add(Stop(new MenuDataEntryAdapter("Close menu", true, false, Close) { IsMenuDismiss = true }));

        int index = 0;
        int rendered = 0;
        for (var child = folder.FirstChild; child != null; child = child.Next, index++)
        {
            int flags = (int)child.Flags;
            if ((flags & 1) == 0) continue;                 // not Visible
            bool enabled = (flags & 2) != 0;                // Enabled
            bool selected = (flags & 4) != 0;               // Selected — the game's tick
            bool isFolder = (int)child.Kind == 2;           // EntryKind.Folder
            var entry = child;

            System.Action action;
            if (isFolder)
            {
                var into = new List<int>(path) { index };
                action = () => _currentPath = into;
            }
            else
            {
                action = () =>
                {
                    if (TryGetMenu() == null) return;       // menu gone; nothing to invoke
                    var interaction = entry.HandleInteraction;
                    if (interaction != null) interaction.Invoke();
                    // Dismiss explicitly: a navigating action self-closes the menu (Close is then a
                    // no-op), but a non-navigating one (Compare With) leaves it up, stranding the
                    // companion on the modal. Close resets the path too.
                    Close();
                };
            }
            items.Add(Stop(new MenuDataEntryAdapter(child.Name, enabled, isFolder, action) { IsChecked = selected }));
            rendered++;
        }

        // The game leaves a folder in the menu even when every one of its children has been filtered
        // out — a column category whose columns are ALL already in the table (the Player Database's
        // Attributes > Mental, whose fourteen entries are all on screen). Sighted play shows an empty
        // flyout; browsing it here would otherwise land on a level holding nothing but Back and Close,
        // which reads exactly like the mod failing to read the menu. Say which it is.
        if (rendered == 0)
            items.Add(Stop(new MenuDataEntryAdapter(EmptyFolderLabel, false, false, () => { })));

        return items;
    }

    // Mod-authored: the honest content of a folder the game filled with nothing. Not a degrade —
    // the description read fine and there genuinely is nothing to pick.
    private const string EmptyFolderLabel = "No options";

    /// <summary>
    /// True when the open menu's description holds at least one visible entry — a menu a player can
    /// actually use, as opposed to a bound-but-never-filled shell. The open confirm requires this
    /// before reporting success, because a context can bind (the trigger fired, a contributor
    /// survived) yet fill with nothing when the linked entity's action list comes back empty.
    /// </summary>
    public static bool HasEntries(VisualElement menu)
    {
        var root = GetRootEntry(menu);
        if (root == null) return false;
        for (var child = root.FirstChild; child != null; child = child.Next)
            if (((int)child.Flags & 1) != 0) return true;   // Visible
        return false;
    }

    /// <summary>
    /// Invokes the root-level entry whose name matches <paramref name="name"/> exactly (cleaned of
    /// rich text, like every displayed label). False when no visible, enabled entry matches — the
    /// caller reports the gap; nothing is invoked on a near-miss, because activating the wrong
    /// menu entry would commit a value the player did not choose. Main thread only.
    /// </summary>
    public static bool InvokeEntryByName(VisualElement menu, string name)
    {
        var root = GetRootEntry(menu);
        if (root == null) return false;
        for (var child = root.FirstChild; child != null; child = child.Next)
        {
            int flags = (int)child.Flags;
            if ((flags & 1) == 0 || (flags & 2) == 0) continue;   // Visible + Enabled
            if ((int)child.Kind == 2) continue;                    // never a Folder
            if (ServerText.Clean(child.Name) != name) continue;
            var interaction = child.HandleInteraction;
            if (interaction == null) return false;
            interaction.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Invokes the single root-level entry carrying <paramref name="flagBit"/> in its flags (the
    /// team-instruction quick menu's "Go To ..." navigation entry carries bit 8; the plain option
    /// entries do not). Refuses — false, nothing invoked — unless exactly one visible, enabled
    /// entry matches: the flag is undocumented game state, and invoking a guessed entry could
    /// commit an option. Flag-based so the match survives localisation, unlike the entry's
    /// composed "Go To <name>" label. Main thread only.
    /// </summary>
    public static bool InvokeSingleFlaggedEntry(VisualElement menu, int flagBit)
    {
        var root = GetRootEntry(menu);
        if (root == null) return false;
        SI.UI.MenuDescription.Entry match = null;
        for (var child = root.FirstChild; child != null; child = child.Next)
        {
            int flags = (int)child.Flags;
            if ((flags & 1) == 0 || (flags & 2) == 0) continue;   // Visible + Enabled
            if ((flags & flagBit) == 0) continue;
            if (match != null) return false;                       // ambiguous - refuse
            match = child;
        }
        if (match == null) return false;
        var interaction = match.HandleInteraction;
        if (interaction == null) return false;
        interaction.Invoke();
        return true;
    }

    /// <summary>
    /// True when the visible menu has lost its description entirely — the state a table scroll
    /// leaves behind when it recycles the menu's anchor row: the game drops the context without
    /// closing the popup, so an empty shell stays on screen. A live menu always has a description
    /// root (the popup only shows once entries exist); no root means dead.
    /// </summary>
    public static bool IsDeadShell(VisualElement menu) => GetRootEntry(menu) == null;

    /// <summary>
    /// Diagnostic: the menu's description state as text — root presence, total/visible child counts,
    /// and each child's name with its raw flags. For /ctxstate; never rendered to the player.
    /// </summary>
    public static string DescribeEntries(VisualElement menu)
    {
        var root = GetRootEntry(menu);
        if (root == null) return "root=null";
        var sb = new StringBuilder();
        int total = 0, visible = 0;
        for (var child = root.FirstChild; child != null; child = child.Next)
        {
            total++;
            int flags = (int)child.Flags;
            if ((flags & 1) != 0) visible++;
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(child.Name).Append("(f=").Append(flags).Append(')');
        }
        return $"total={total} visible={visible} [{sb}]";
    }

    /// <summary>
    /// Diagnostic: the menu's description as a nested tree — every entry with its child index, kind,
    /// raw flags and child count, to <paramref name="maxDepth"/> levels. DescribeEntries only reports
    /// root children, which cannot answer the question that matters for a folder that browses empty:
    /// does the GAME have children there, or did the surface fail to reach them. For /ctxstate?tree=N.
    /// </summary>
    public static string DescribeTree(VisualElement menu, int maxDepth)
    {
        var root = GetRootEntry(menu);
        if (root == null) return "root=null";
        var sb = new StringBuilder();
        AppendSubtree(sb, root, 0, maxDepth);
        return sb.ToString();
    }

    private static void AppendSubtree(StringBuilder sb, SI.UI.MenuDescription.Entry folder, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        int index = 0;
        for (var child = folder.FirstChild; child != null; child = child.Next, index++)
        {
            int kids = 0;
            for (var g = child.FirstChild; g != null; g = g.Next) kids++;
            sb.Append(new string(' ', depth * 2))
              .Append('[').Append(index).Append("] ")
              .Append(child.Name)
              .Append(" kind=").Append((int)child.Kind)
              .Append(" f=").Append((int)child.Flags)
              .Append(" kids=").Append(kids)
              .Append('\n');
            AppendSubtree(sb, child, depth + 1, maxDepth);
        }
    }

    /// <summary>
    /// A signature of the open menu — the current folder path plus its child labels — or "" when none
    /// is open. Folded into the screen token so the SSE poll refreshes the page on open, drill-in, or
    /// close.
    /// </summary>
    public static string Signature()
    {
        var menu = TryGetMenu();
        if (menu == null) return "";
        var root = GetRootEntry(menu);
        if (root == null) return "menu:open";

        var path = new List<int>(_currentPath);
        var folder = ResolveFolder(root, path);
        var sb = new StringBuilder("menu:");
        sb.Append(string.Join(".", path)).Append(':');
        // Flags ride along with the label: an entry that gains a tick (or is enabled/disabled) under
        // an unchanged label is a real change the player must be re-read, and a labels-only signature
        // would hold the stale state on screen.
        for (var child = folder.FirstChild; child != null; child = child.Next)
            sb.Append(child.Name).Append('#').Append((int)child.Flags).Append('|');
        return sb.ToString();
    }

    private static ScreenWalker.StopItem Stop(MenuDataEntryAdapter adapter)
        => new() { Element = adapter };

    // The menu's data-model root entry, read from CascadingMenu.Description.m_root. Both are reached by
    // field, not a public getter; the reflected reads are cheap and (verified live) safe.
    private static SI.UI.MenuDescription.Entry GetRootEntry(VisualElement menu)
    {
        var descObj = menu.GetIl2CppType().GetField("<Description>k__BackingField", Flags)?.GetValue(menu);
        if (descObj == null) return null;
        var rootObj = descObj.GetIl2CppType().GetField("m_root", Flags)?.GetValue(descObj);
        if (rootObj == null) return null;
        return rootObj.Cast<SI.UI.MenuDescription.Entry>();
    }

    private static SI.UI.MenuDescription.Entry ResolveFolder(SI.UI.MenuDescription.Entry root, List<int> path)
    {
        var folder = root;
        foreach (var idx in path)
        {
            var child = folder.FirstChild;
            for (int i = 0; i < idx && child != null; i++) child = child.Next;
            if (child == null) { path.Clear(); return root; }   // stale path; fall back to root
            folder = child;
        }
        return folder;
    }

}
