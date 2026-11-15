using System;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-driver probe for the game's bookmark-mapping popup (the SIFloatingPopupWidget
/// "BookmarksPopupWidget" in the title bar's Bookmarks strip). The popup is the game's own UI
/// for choosing which bookmarks occupy the ribbon slots. The companion now exposes it through
/// FilterPopupSurface/NativeBookmarkSurface, separately from its Ctrl-shortcut editor.
/// This diagnostic inspects the native widget and its module; current contracts are recorded
/// in docs/fm27-migration.md, rather than the older upstream bookmark-mapping plan.
///
///   GET /bookmarkpopup?op=open           show the floating part
///   GET /bookmarkpopup?op=state          widget + floating element state (visible? attached where?)
///   GET /bookmarkpopup?op=dump           raw tree of the floating root
///   GET /bookmarkpopup?op=rows           clickable candidates inside the popup, indexed
///   GET /bookmarkpopup?op=click&index=N  activate candidate N (the game's own toggle flow)
///   GET /bookmarkpopup?op=close          hide the floating part
/// </summary>
internal static class BookmarkPopupProbe
{
    internal static string Drive(string op, string indexStr)
    {
        try
        {
            switch (op)
            {
                case "open": return Open();
                case "init": return Init();
                case "close": return Close();
                case "module": return Module();
                case "state": return State();
                case "dump": return Dump();
                case "rows": return Rows();
                case "click": return Click(indexStr);
                default: return "ops: open | state | dump | rows | click&index=N | close";
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

    // Self-contained find: the editor surface no longer touches the game popup (the mod's
    // bookmark editor is mod-backed), so this probe keeps its own lookup for spelunking.
    private static SI.Bindable.SIFloatingPopupWidget FindWidget()
    {
        if (FM27Access.Navigation.NavigationManager.GetAllOpenPanels() is not { } openPanels) return null;
        foreach (var entry in openPanels)
        {
            if (entry.Panel is not VisualElement panel) continue;
            var el = FindDescendantNamed(panel, "BookmarksPopupWidget");
            var widget = el?.TryCast<SI.Bindable.SIFloatingPopupWidget>();
            if (widget != null) return widget;
        }
        return null;
    }

    private static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindDescendantNamed(root.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }

    // The bookmark module's own view of the two lists — what the editor filters rows against.
    private static string Module()
    {
        var sb = new StringBuilder();
        var selected = Adapters.BookmarkBar.SelectedNames();
        var unselected = Adapters.BookmarkBar.UnselectedNames();
        sb.AppendLine($"selected ({Adapters.BookmarkBar.SelectedCount()}): "
            + (selected == null ? "<null>" : string.Join(" | ", selected)));
        sb.AppendLine($"unselected ({Adapters.BookmarkBar.UnselectedCount()}): "
            + (unselected == null ? "<null>" : string.Join(" | ", unselected)));
        return sb.ToString();
    }

    // HandleFloatingPart is the widget's own click entry (initialises the floating part on first
    // use, then toggles visibility); calling ShowFloatingPart before init NREs inside the game.
    private static string Open()
    {
        var widget = FindWidget();
        if (widget == null) return "BookmarksPopupWidget not found in any open panel.";
        widget.HandleFloatingPart();
        return "HandleFloatingPart called. Poll op=state, then op=dump.";
    }

    private static string Init()
    {
        var widget = FindWidget();
        if (widget == null) return "BookmarksPopupWidget not found in any open panel.";
        widget.InitialiseFloatingPart();
        return "InitialiseFloatingPart called.";
    }

    private static string Close()
    {
        var widget = FindWidget();
        if (widget == null) return "BookmarksPopupWidget not found in any open panel.";
        widget.HideFloatingPart();
        return "HideFloatingPart called.";
    }

    private static string State()
    {
        var widget = FindWidget();
        if (widget == null) return "BookmarksPopupWidget not found in any open panel.";

        var sb = new StringBuilder();
        var fe = widget.m_floatingElement;
        sb.AppendLine($"widget: name={widget.name} visible={ElementBridge.IsVisible(widget)}");
        sb.AppendLine($"floatingElement: {(fe == null ? "null" : $"IsVisible={fe.IsVisible}")}");
        var content = widget.m_floatingPartContent;
        sb.AppendLine($"floatingPartContent: {(content == null ? "null" : $"name={content.name} children={content.hierarchy.childCount} inPanel={content.panel != null}")}");

        // Where the floating root actually attached: the ancestry of the floating element's root
        // up to the panel root, so we learn which layer/panel hosts popup content.
        var root = fe?.Root;
        if (root == null) { sb.AppendLine("floating root: null"); return sb.ToString(); }
        sb.AppendLine($"floating root: name={root.name} type={root.GetIl2CppType().Name} inPanel={root.panel != null}");
        var cur = root.hierarchy.parent;
        int depth = 0;
        while (cur != null && depth < 40)
        {
            sb.AppendLine($"  ancestor[{depth}]: type={cur.GetIl2CppType().Name} name=\"{cur.name}\"");
            cur = cur.hierarchy.parent;
            depth++;
        }
        return sb.ToString();
    }

    private static string Dump()
    {
        var widget = FindWidget();
        if (widget == null) return "BookmarksPopupWidget not found in any open panel.";
        var root = widget.m_floatingElement?.Root;
        if (root == null) return "Floating root is null (popup never opened this session?).";

        var sb = new StringBuilder();
        Diagnostics.TreeDump.Write(root, sb, new Diagnostics.TreeDump.Options());
        return sb.ToString();
    }

    private static string Rows()
    {
        var candidates = CollectCandidates();
        if (candidates == null) return "Floating root is null (open the popup first).";
        var sb = new StringBuilder();
        for (int i = 0; i < candidates.Count; i++)
        {
            var el = candidates[i];
            string label;
            try
            {
                var adapter = ElementBridge.CreateAdapter(el, FM27Access.Navigation.ElementClassification.Button);
                label = adapter.GetLabel();
            }
            catch (Exception e) { label = $"<label failed: {e.GetType().Name}>"; }
            sb.AppendLine($"[{i}] type={el.GetIl2CppType().Name} name=\"{el.name}\" label=\"{label}\" visible={ElementBridge.IsVisible(el)}");
        }
        return sb.Length == 0 ? "No clickable candidates found." : sb.ToString();
    }

    private static string Click(string indexStr)
    {
        if (!int.TryParse(indexStr, out int index)) return "click needs &index=N";
        var candidates = CollectCandidates();
        if (candidates == null) return "Floating root is null (open the popup first).";
        if (index < 0 || index >= candidates.Count) return $"index out of range, {candidates.Count} candidates.";
        var el = candidates[index];
        var adapter = ElementBridge.CreateAdapter(el, FM27Access.Navigation.ElementClassification.Button);
        var label = adapter.GetLabel();
        adapter.Activate();
        return $"Clicked [{index}] \"{label}\".";
    }

    private static List<VisualElement> CollectCandidates()
    {
        var widget = FindWidget();
        var root = widget?.m_floatingElement?.Root;
        if (root == null) return null;
        var list = new List<VisualElement>();
        Collect(root, list);
        return list;
    }

    private static readonly string[] ClickableTypes = { "SIButton", "SICheckBox", "SIRadioButton" };

    private static void Collect(VisualElement el, List<VisualElement> list)
    {
        var typeName = el.GetIl2CppType().Name;
        foreach (var t in ClickableTypes)
        {
            if (typeName == t) { list.Add(el); break; }
        }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            Collect(el.hierarchy[i], list);
    }
}
