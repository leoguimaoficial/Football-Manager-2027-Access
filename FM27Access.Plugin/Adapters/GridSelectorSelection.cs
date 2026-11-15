#nullable enable annotations
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Selecting a GridSelector card (the team/nation pickers — an SIButton with an "active-toggle"
/// child inside a VirtualisedList) goes through the list's selection model, not a button click:
/// SimulateButtonClick leaves the list's default item selected. Shared by TeamSelectionHandler
/// (in-game Enter) and ButtonAdapter.Activate (companion /activate) so both select identically.
/// </summary>
internal static class GridSelectorSelection
{
    internal static bool IsNativeListEntrySelected(VisualElement element)
        => TryNativeListEntry(element, out var list, out int index)
            && list.Selection?.Contains(index) == true;

    internal static bool? ReadSelected(VisualElement element)
    {
        var toggle = FindChildByName(element, "active-toggle");
        if (toggle == null) return null;
        var ancestor = element.hierarchy.parent;
        while (ancestor != null)
        {
            if (ancestor.GetIl2CppType().Name == "GridSelector") return ElementBridge.IsVisible(toggle);
            ancestor = ancestor.hierarchy.parent;
        }
        return null;
    }
    /// <summary>
    /// If <paramref name="element"/> is a GridSelector card, moves the owning VirtualisedList's
    /// selection to it and returns true. <paramref name="wasSelected"/> reports the prior state
    /// so callers can announce select vs deselect. Returns false (and leaves state untouched) for
    /// any element that is not such a card.
    /// </summary>
    internal static bool TrySelect(VisualElement element, out bool wasSelected)
    {
        wasSelected = false;

        if (TryNativeListEntry(element, out var nativeList, out int nativeIndex))
        {
            wasSelected = nativeList.Selection?.Contains(nativeIndex) == true;
            nativeList.SetSelection(nativeIndex);
            return true;
        }

        if (element.GetIl2CppType().Name != "SIButton")
            return false;

        var activeToggle = FindChildByName(element, "active-toggle");
        if (activeToggle == null)
            return false;

        wasSelected = ElementBridge.IsVisible(activeToggle);

        VisualElement? virtListElement = null;
        var walk = element.hierarchy.parent;
        while (walk != null)
        {
            if (walk.GetIl2CppType().Name == "VirtualisedList")
            {
                virtListElement = walk;
                break;
            }
            walk = walk.hierarchy.parent;
        }

        if (virtListElement == null)
        {
            Speech.Log.Warn("[GridSelectorSelection] active-toggle present but no VirtualisedList ancestor");
            return false;
        }

        var list = new SI.UI.VirtualisedList(virtListElement.Pointer);
        int activeCount = list.GetActiveCount();
        for (int i = 0; i < activeCount; i++)
        {
            var item = list.GetActiveItem(i);
            if (item == null || item.Content == null) continue;
            if (IsAncestorOf(item.Content, element))
            {
                Speech.Log.Info($"[GridSelectorSelection] SetSelection({item.Index})");
                list.SetSelection(item.Index);
                return true;
            }
        }

        Speech.Log.Warn("[GridSelectorSelection] button not found in VirtualisedList active items");
        return false;
    }

    // FM27's cascading popup categories are native list entries, not SIButtons:
    // the NavigatableVisualElement has no submit callback, while its owning list
    // declares ExactlyOne selection. Reuse the same index-addressed operation as
    // GridSelector rather than sending a click to its inert internal Clickable.
    internal static bool TryNativeListEntry(VisualElement element, out SI.UI.VirtualisedList list, out int index)
    {
        list = null;
        index = -1;
        if (element.GetIl2CppType().Name != "NavigatableVisualElement") return false;
        for (var parent = element.hierarchy.parent; parent != null; parent = parent.hierarchy.parent)
        {
            string type = parent.GetIl2CppType().Name;
            if (type is "SIButton" or "SIRadioButton" or "Panel") return false;
            if (type != "VirtualisedList") continue;
            var candidate = parent.TryCast<SI.UI.VirtualisedList>();
            if (candidate == null || candidate.SelectionMode != SI.UI.VirtualisedList.SelectionModes.ExactlyOne) return false;
            for (int i = 0; i < candidate.GetActiveCount(); i++)
            {
                var item = candidate.GetActiveItem(i);
                if (item?.Content == null || !IsAncestorOf(item.Content, element)) continue;
                list = candidate;
                index = item.Index;
                return true;
            }
            return false;
        }
        return false;
    }

    private static VisualElement? FindChildByName(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.name == name)
                return child;
            var deeper = FindChildByName(child, name);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    private static bool IsAncestorOf(VisualElement ancestor, VisualElement descendant)
    {
        var current = descendant.hierarchy.parent;
        while (current != null)
        {
            if (current.Pointer == ancestor.Pointer)
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }
}

