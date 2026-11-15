using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Live reads against the game's bookmark system, shared by <see cref="BookmarkButtonAdapter"/>
/// (slot names on both surfaces) and ServerHandles.Bookmark (the companion's Ctrl+N chord).
///
/// The game keeps everything in GameWorldTabsAndBookmarksModule (an IGameModule on the
/// GameModules behaviour): the selected bookmarks live in m_currentSelectedBookmarksList, a
/// BookmarksList whose order IS the title-bar strip order — the strip's StreamedObjectList is
/// bound to that list, and the game's own Shift+N shortcuts index into it the same way
/// (UpdateBookmarksShortcuts). Names come from BookmarkInfo.BookmarkText, the game's own
/// localized string; nothing here is mod-authored text. All reads are live per call — the only
/// held state is the module reference itself (a live Unity object, re-found when lost).
/// </summary>
internal static class BookmarkBar
{
    private static GameWorldTabsAndBookmarksModule _module;

    // Shared with NavigatorIndex, which enumerates the same module's navigation maps.
    internal static GameWorldTabsAndBookmarksModule Module()
    {
        // Unity-overloaded null check: a destroyed module reads as null and is re-found.
        if (_module != null) return _module;

        var gameModules = UnityEngine.Object.FindObjectOfType<GameModules>();
        if (gameModules == null) return null;

        var modules = gameModules.m_modules;
        if (modules == null) return null;
        for (int i = 0; i < modules.Count; i++)
        {
            var typed = modules[i]?.TryCast<GameWorldTabsAndBookmarksModule>();
            if (typed != null)
            {
                _module = typed;
                return typed;
            }
        }
        return null;
    }

    /// <summary>
    /// Toggle a bookmark's ribbon selection through the module by IDENTITY: find the BookmarkInfo
    /// by name (in the selected list for a remove, the master possible list for an add), load its
    /// PanelID, and call the module's own <c>UpdateBookmarksSelections</c> — the very method the
    /// game's popup rows drive. Identity-based, so it never depends on the popup's virtualised tree
    /// (whose row order and text desync from the real list, and whose pooled elements recycle onto
    /// other bookmarks) nor on a stale page handle: those are what made removes land on the wrong
    /// bookmark. Returns false (the caller degrades) when the bookmark or its panel id can't be
    /// resolved. Main thread only — loads an addressable and mutates game state.
    /// </summary>
    internal static bool ToggleByName(string name, bool currentlySelected)
    {
        try
        {
            var module = Module();
            if (module == null) return false;

            var list = currentlySelected
                ? module.m_currentSelectedBookmarksList.Value?.Bookmarks
                : module.m_possibleBookmarks?.Bookmarks;
            if (list == null) return false;

            SI.Bindable.BookmarkInfo target = null;
            for (int i = 0; i < list.Count; i++)
            {
                var bi = list[i];
                if (bi == null) continue;
                var text = bi.BookmarkText;
                if (!string.IsNullOrWhiteSpace(text) && Speech.TextFilters.CleanForDisplay(text) == name)
                {
                    target = bi;
                    break;
                }
            }
            if (target == null) return false;

            var pid = LoadPanelId(target);
            if (pid == null) return false;

            module.UpdateBookmarksSelections(pid, !currentlySelected);
            // Match the game's own toggle flow (OnToggleBookmarkSelectionEvent): persist the new
            // selection and rebind the Shift+N shortcuts to the reordered slots. Idempotent, so
            // harmless if UpdateBookmarksSelections already does them internally.
            module.SaveSelectedBookmarks();
            module.UpdateBookmarksShortcuts();
            return true;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark toggle '{name}' via module failed: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    // The bookmark's PanelID, from the already-loaded asset when the game has it, otherwise a
    // synchronous addressable load (a small ScriptableObject). Null when the reference is empty or
    // the load fails; the caller degrades rather than toggling the wrong bookmark.
    private static SI.Bindable.PanelID LoadPanelId(SI.Bindable.BookmarkInfo bi)
    {
        var refT = bi.PanelIDRef;
        if (refT == null) return null;
        var asset = refT.Asset;
        if (asset != null) return asset.TryCast<SI.Bindable.PanelID>();
        return refT.LoadAssetAsync().WaitForCompletion();
    }

    /// <summary>
    /// The display name of the bookmark in slot <paramref name="slotIndex"/> (0-based), read live
    /// from the module's selected list. Null when the module, list, or slot cannot be read — the
    /// caller degrades, never guesses.
    /// </summary>
    internal static string NameForSlot(int slotIndex)
    {
        try
        {
            var bookmarks = SelectedBookmarks();
            if (bookmarks == null || slotIndex >= bookmarks.Count) return null;
            var text = bookmarks[slotIndex]?.BookmarkText;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark slot {slotIndex + 1} name read failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static Il2CppSystem.Collections.Generic.List<SI.Bindable.BookmarkInfo> SelectedBookmarks()
    {
        var module = Module();
        if (module == null) return null;
        var list = module.m_currentSelectedBookmarksList.Value;
        if (list == null) return null;
        return list.Bookmarks;
    }

    // The module's own counts for the two bookmark lists, or -1 when unreadable. The bookmark
    // editor compares its rendered row counts against these so a virtualised list that windows
    // its rows renders an honest "N more not shown" instead of reading as complete.
    internal static int SelectedCount()
    {
        try { return SelectedBookmarks()?.Count ?? -1; }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark selected count read failed: {e.GetType().Name}");
            return -1;
        }
    }

    internal static int UnselectedCount()
    {
        try
        {
            var module = Module();
            if (module == null) return -1;
            var list = module.m_currentUnselectedBookmarksList.Value;
            return list?.Bookmarks?.Count ?? -1;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark unselected count read failed: {e.GetType().Name}");
            return -1;
        }
    }

    // The selected list's names, or null when unreadable. This is the ONE membership truth the
    // bookmark editor trusts: the selected list drives the strip, the game's Shift+N shortcuts
    // and the save. The module's UNSELECTED list goes genuinely out of sync (live-observed:
    // after a few edits it held 28 entries including three currently-selected bookmarks, and
    // the game's own popup displayed the duplicates), so the editor derives "available" as
    // NOT-selected rather than reading that list.
    internal static HashSet<string> SelectedNames()
    {
        try { return NamesOf(SelectedBookmarks()); }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark selected names read failed: {e.GetType().Name}");
            return null;
        }
    }

    // Diagnostic only (the /bookmarkpopup?op=module probe): the module's unselected list as-is,
    // unreliable per the note above.
    internal static HashSet<string> UnselectedNames()
    {
        try
        {
            var module = Module();
            if (module == null) return null;
            return NamesOf(module.m_currentUnselectedBookmarksList.Value?.Bookmarks);
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark unselected names read failed: {e.GetType().Name}");
            return null;
        }
    }

    // The master possible-bookmarks list's size, or -1. With SelectedCount it gives the expected
    // available-row count (possible minus selected) without touching the unreliable unselected list.
    internal static int PossibleCount()
    {
        try
        {
            var module = Module();
            return module?.m_possibleBookmarks?.Bookmarks?.Count ?? -1;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark possible count read failed: {e.GetType().Name}");
            return -1;
        }
    }

    private static HashSet<string> NamesOf(Il2CppSystem.Collections.Generic.List<SI.Bindable.BookmarkInfo> bookmarks)
    {
        if (bookmarks == null) return null;
        var names = new HashSet<string>();
        for (int i = 0; i < bookmarks.Count; i++)
        {
            var text = bookmarks[i]?.BookmarkText;
            if (!string.IsNullOrWhiteSpace(text))
                names.Add(Speech.TextFilters.CleanForDisplay(text));
        }
        return names;
    }

    /// <summary>
    /// This button's 0-based position in the title-bar bookmark strip: the index of its
    /// AsyncBindingRoot ancestor among that root's siblings of the same type (the strip's
    /// DropTarget "View" hosts one AsyncBindingRoot per slot, in list order). -1 when the
    /// button is not inside a recognisable strip.
    /// </summary>
    internal static int SlotIndexOf(VisualElement bookmarkButton)
    {
        try
        {
            var current = bookmarkButton.hierarchy.parent;
            while (current != null)
            {
                var parent = current.hierarchy.parent;
                if (parent != null && parent.name == "View" && current.GetIl2CppType().Name == "AsyncBindingRoot")
                {
                    int index = 0;
                    for (int i = 0; i < parent.hierarchy.childCount; i++)
                    {
                        var sibling = parent.hierarchy[i];
                        if (sibling.Pointer == current.Pointer) return index;
                        if (sibling.GetIl2CppType().Name == "AsyncBindingRoot") index++;
                    }
                    return -1;
                }
                current = parent;
            }
            return -1;
        }
        catch (System.Exception e)
        {
            Speech.Degrade.Note($"bookmark slot walk failed: {e.GetType().Name}: {e.Message}");
            return -1;
        }
    }

    /// <summary>
    /// The visible bookmark buttons of the title-bar strip, in slot order (document order under
    /// the strip's "BookmarksList" StreamedObjectList). Empty when no strip is on screen.
    /// </summary>
    internal static List<VisualElement> FindBookmarkButtons()
    {
        var buttons = new List<VisualElement>();
        if (FM27Access.Navigation.NavigationManager.GetAllOpenPanels() is not { } openPanels)
            return buttons;

        foreach (var entry in openPanels)
        {
            if (entry.Panel is not VisualElement panel) continue;
            var strip = FindDescendantNamed(panel, "BookmarksList");
            if (strip == null) continue;
            CollectButtons(strip, buttons);
            if (buttons.Count > 0) return buttons;
        }
        return buttons;
    }

    private static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            var found = FindDescendantNamed(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectButtons(VisualElement root, List<VisualElement> buttons)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.name == "BookmarkButton" && child.GetIl2CppType().Name == "SIButton")
            {
                buttons.Add(child);
                continue;
            }
            CollectButtons(child, buttons);
        }
    }
}
