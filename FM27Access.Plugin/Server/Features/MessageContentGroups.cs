using UnityEngine.UIElements;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Server;

// The native introduction attachment separates purpose, topics and important
// people. A flat walk loses those boundaries and suggests that unrelated topic
// and person lists are aligned. Reuse FM26 headings/lists and ordinary cell
// rendering, without assigning a person to a topic the game never associates.
internal static class MessageContentGroups
{
    private static List<VisualElement> Children(VisualElement element)
    {
        var result = new List<VisualElement>();
        for (int i = 0; i < element.hierarchy.childCount; i++)
            if (ElementBridge.IsVisible(element.hierarchy[i])) result.Add(element.hierarchy[i]);
        return result;
    }

    internal static bool TryEmit(VisualElement element, List<ScreenWalker.RenderItem> items,
        Func<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        var parent = element.parent;
        // The responsibilities variant wraps purpose in one native SIVisible;
        // that conditional wrapper does not change its section ownership.
        if (parent?.TryCast<SI.Bindable.SIVisible>() != null) parent = parent.parent;
        if (parent == null) return false;
        var siblings = Children(parent).SelectMany(child => child.TryCast<SI.Bindable.SIVisible>() != null
            ? Children(child) : new List<VisualElement> { child }).ToList();
        if (siblings.Count(e => e.name == "purpose_block") != 1
            || siblings.Count(e => e.name == "topic_block") != 1) return false;
        var children = Children(element);
        if (element.name == "squad_block") return TryPeople(element, children, items, walk);
        if (children.Count == 0 || children[0].TryCast<SI.Bindable.SIText>() == null) return false;
        var title = walk(children[0]);
        if (title.Count != 1 || title[0] is not ScreenWalker.StopItem heading
            || ServerText.SafeRole(heading.Element) != "text"
            || string.IsNullOrWhiteSpace(DisplayText.Of(heading.Element))) return false;
        heading.HeadingLevel = 3;
        if (element.name == "topic_block")
        {
            if (children.Count != 2 || children[1].name != "frame") return false;
            var rows = Children(children[1]).Select(walk).ToList();
            if (rows.Count == 0 || rows.Any(row => !IsInlineRow(row))) return false;
            items.Add(heading);
            items.Add(new ScreenWalker.TextListItem { Rows = rows, Source = children[1] });
        }
        else
        {
            // Purpose body and any other content retain their original order.
            items.Add(heading);
            foreach (var child in children.Skip(1)) items.AddRange(walk(child));
        }
        return true;
    }

    private static bool TryPeople(VisualElement element, List<VisualElement> children,
        List<ScreenWalker.RenderItem> items, Func<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        if (children.Count != 1 || children[0].GetIl2CppType().Name != "StreamedObjectList") return false;
        var list = children[0];
        if (ListWindowSurface.IsUnderRealised(list)) return false;
        var native = ListWindowSurface.FindScrollView(list)?.parent?.TryCast<SI.UI.VirtualisedList>();
        var total = ListWindowSurface.ReadItemCount(native);
        // Consolidate only a completely realised native list. Unexpected/large
        // shapes retain the original walking, expansion and paging contract.
        if (total <= 0 || total > StreamedTableLoader.PageRows) return false;
        var roots = NativeTreeQuery.All<SI.Bindable.BindingRoot>(list)
            .Where(ElementBridge.IsVisibleInHierarchy).ToList();
        if (roots.Count != total) return false;
        var rows = new List<List<ScreenWalker.RenderItem>>();
        foreach (var root in roots)
        {
            var row = walk(root);
            if (!IsInlineRow(row)) return false;
            rows.Add(row);
        }
        items.Add(new ScreenWalker.TextListItem { Rows = rows, Source = element });
        return true;
    }

    private static bool IsInlineRow(List<ScreenWalker.RenderItem> row)
        => row.Count > 0 && row.All(part => part is ScreenWalker.StopItem
            || part is ScreenWalker.JoinedTextItem { BodySource: null });
}
