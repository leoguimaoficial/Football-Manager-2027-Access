using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// The pre-career club profile uses a native TeamInfoDetailsTemplate, with
// newline culture lists and separate streamed competition/rival rows. Reuse
// FM26's live paragraph/list rendering without flattening those boundaries.
internal static class ClubProfileGroups
{
    private static List<VisualElement> Children(VisualElement element)
    {
        var result = new List<VisualElement>();
        for (int i = 0; i < element.hierarchy.childCount; i++)
            if (ElementBridge.IsVisible(element.hierarchy[i])) result.Add(element.hierarchy[i]);
        return result;
    }

    private static bool Owns(VisualElement element)
    {
        bool template = false, profile = false;
        for (var parent = element; parent != null; parent = parent.parent)
        {
            template |= parent.name == "TeamInfoDetailsTemplate";
            profile |= parent.name == "ClubProfileContent";
            if (parent.GetIl2CppType().Name == "Panel")
                return template && profile && ElementBridge.IsInPanel(element, "GameCreationLoadingScreen");
        }
        return false;
    }

    private static bool Inline(List<ScreenWalker.RenderItem> row) => row.Count > 0 && row.All(part =>
        part is ScreenWalker.StopItem || part is ScreenWalker.JoinedTextItem { BodySource: null });

    internal static bool TryEmit(VisualElement element, List<ScreenWalker.RenderItem> items,
        Func<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        if (!Owns(element)) return false;
        if (element.name is "BoardCulture" or "SuporterCulture")
            return Culture(element, items, walk);
        if (element.name is "CompetitionsList" or "NotableRivalsList")
            return StreamedList(element, items, walk);
        if (element.name is "Founded" or "Nickname")
        {
            var joined = Fact(element, walk);
            if (joined == null) return false;
            items.Add(joined); return true;
        }
        if (element.name == "GeneralData")
        {
            var groups = Children(element);
            if (groups.Count != 2 || groups[0].name != "FirstRow" || groups[1].name != "SecondRow") return false;
            var cells = groups.SelectMany(Children).ToList();
            if (!cells.Select(e => e.name).SequenceEqual(new[] {
                "Status", "OwnerStatus", "Finances", "Stadium", "Training", "YouthFacilities" })) return false;
            var rows = new List<List<ScreenWalker.RenderItem>>();
            foreach (var cell in cells)
            {
                var joined = Fact(cell, walk);
                if (joined == null) return false;
                rows.Add(new() { joined });
            }
            items.Add(new ScreenWalker.TextListItem { Rows = rows, Source = element }); return true;
        }
        if (element.name == "Kits")
        {
            var rows = Children(element).Select(walk).ToList();
            if (rows.Count == 0 || rows.Any(row => !Inline(row))) return false;
            items.Add(new ScreenWalker.TextListItem { Rows = rows, Source = element }); return true;
        }
        return false;
    }

    private static ScreenWalker.JoinedTextItem Fact(VisualElement element,
        Func<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        var children = Children(element);
        if (children.Count < 2 || children[0].TryCast<SI.Bindable.SIText>() == null) return null;
        var row = children.SelectMany(walk).ToList();
        if (!Inline(row) || row[0] is not ScreenWalker.StopItem label
            || label.Element is not Adapters.PluginAdapterBase adapter
            || adapter.BackingElement.Pointer != children[0].Pointer
            || label.Element.CanActivate) return null;
        var joined = new ScreenWalker.JoinedTextItem { LeadingCaption = true };
        foreach (var part in row)
            if (part is ScreenWalker.StopItem stop) joined.Parts.Add(stop.Element);
            else joined.Parts.AddRange(((ScreenWalker.JoinedTextItem)part).Parts);
        // JoinedText retains embedded SIText commands, but an unrelated native
        // form control must keep its ordinary renderer and activation handle.
        if (joined.Parts.Any(part => ServerText.SafeRole(part) != "text"
            && part is not Adapters.SITextLink.Part)) return null;
        return joined.Parts.Count > 1 ? joined : null;
    }

    private static bool Culture(VisualElement element, List<ScreenWalker.RenderItem> items,
        Func<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        var children = Children(element);
        if (children.Count != 2 || children[0].TryCast<SI.Bindable.SIText>() == null) return false;
        var texts = NativeTreeQuery.All<SI.Bindable.SIText>(children[1])
            .Where(ElementBridge.IsVisibleInHierarchy).ToList();
        if (texts.Count != 1) return false;
        var original = walk(children[1]);
        bool FromSource(IAccessibleElement part) => part is Adapters.PluginAdapterBase adapter
            && adapter.BackingElement.Pointer == texts[0].Pointer;
        if (!Inline(original) || original.Any(item => item is ScreenWalker.StopItem stop
            ? !FromSource(stop.Element) : !((ScreenWalker.JoinedTextItem)item).Parts.All(FromSource))) return false;
        var lines = Adapters.SITextLink.ReadLines(texts[0]);
        if (lines == null) return false;
        var title = walk(children[0]);
        if (title.Count != 1 || title[0] is not ScreenWalker.StopItem heading) return false;
        heading.HeadingLevel = 3;
        items.Add(heading);
        items.Add(new ScreenWalker.TextListItem { Source = texts[0], Rows = lines.Select(parts =>
            new List<ScreenWalker.RenderItem> { new ScreenWalker.JoinedTextItem { Parts = parts } }).ToList() });
        return true;
    }

    private static bool StreamedList(VisualElement element, List<ScreenWalker.RenderItem> items,
        Func<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        if (ListWindowSurface.IsUnderRealised(element)) return false;
        var lists = NativeTreeQuery.All<SI.UI.VirtualisedList>(element)
            .Where(ElementBridge.IsVisibleInHierarchy).ToList();
        if (lists.Count != 1) return false;
        int total = ListWindowSurface.ReadItemCount(lists[0]);
        if (total <= 0 || total > StreamedTableLoader.PageRows) return false;
        var views = NativeTreeQuery.All<VisualElement>(lists[0], "View")
            .Where(e => e.GetIl2CppType().Name == "DropTarget" && ElementBridge.IsVisibleInHierarchy(e)).ToList();
        if (views.Count != 1) return false;
        var roots = Children(views[0]);
        if (roots.Count != total) return false;
        var rows = roots.Select(walk).ToList();
        if (rows.Any(row => !Inline(row))) return false;
        items.Add(new ScreenWalker.TextListItem { Rows = rows, Source = element }); return true;
    }
}
