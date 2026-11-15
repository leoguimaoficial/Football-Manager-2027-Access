using System.Reflection;
using HarmonyLib;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Shared SIDropdown reflection cache used by both DropdownAdapter and
/// SearchableDropdownAdapter. Resolves once on first access; crashes on
/// any resolution failure since dropdown state is speech-critical.
/// </summary>
internal static class SIDropdownReflection
{
    private static bool _initialized;

    // SIDropdown core
    internal static PropertyInfo SelectedIndex;
    internal static PropertyInfo ChoicesCount;
    internal static PropertyInfo Clickable;
    internal static PropertyInfo FloatingElement;
    internal static PropertyInfo StreamedDropdownList;
    internal static MethodInfo ClickableInvoke;
    internal static MethodInfo HideDropdown;
    internal static MethodInfo CloseSelfAndAllChildren;
    internal static MethodInfo TriggerSelectionEvent;

    // VirtualisedList access (for scroll-on-demand in large dropdowns)
    internal static PropertyInfo ListElement; // StreamedListView.ListElement
    internal static MethodInfo VirtualisedListScrollTo; // VirtualisedList.ScrollTo(int)
    internal static MethodInfo VirtualisedListGetVisibleCount; // VirtualisedList.GetVisibleCount()
    internal static MethodInfo VirtualisedListGetVisibleItem; // VirtualisedList.GetVisibleItem(int)
    internal static MethodInfo VirtualisedListGetActiveCount; // VirtualisedList.GetActiveCount()
    internal static MethodInfo VirtualisedListGetActiveItem; // VirtualisedList.GetActiveItem(int)
    internal static PropertyInfo VirtualisedElementContent; // VirtualisedElement.Content
    internal static PropertyInfo VirtualisedElementIndex; // VirtualisedElement.Index

    // FloatingElement members (resolved from FloatingElement's type)
    internal static PropertyInfo FloatingElementIsVisible;
    internal static MethodInfo FloatingElementHide;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        var dropdownType = typeof(SI.Bindable.SIDropdown);

        SelectedIndex = RequireProperty(AccessTools.Property(dropdownType, "SelectedIndex"),
            "SIDropdown", "SelectedIndex");

        ChoicesCount = RequireProperty(AccessTools.Property(dropdownType, "ChoicesCount"),
            "SIDropdown", "ChoicesCount");

        Clickable = RequireProperty(dropdownType.GetProperty("m_clickable"),
            "SIDropdown", "m_clickable");

        FloatingElement = RequireProperty(dropdownType.GetProperty("m_floatingElement"),
            "SIDropdown", "m_floatingElement");

        StreamedDropdownList = RequireProperty(dropdownType.GetProperty("m_streamedDropdownList"),
            "SIDropdown", "m_streamedDropdownList");

        HideDropdown = RequireMethod(AccessTools.Method(dropdownType, "HideDropdown"),
            "SIDropdown", "HideDropdown");

        // The game's recursive close (closes self + every descendant and unbinds their child roots).
        // HideDropdown alone does not cascade, so children we open by reflection linger visible.
        CloseSelfAndAllChildren = RequireMethod(AccessTools.Method(dropdownType, "CloseSelfAndAllChildren"),
            "SIDropdown", "CloseSelfAndAllChildren");

        TriggerSelectionEvent = RequireMethod(
            dropdownType.GetMethod("TriggerSelectionEvent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new System.Type[] { typeof(int) }, null),
            "SIDropdown", "TriggerSelectionEvent");

        // Resolve Clickable.Invoke(EventBase)
        var clickableType = Clickable.PropertyType;
        ClickableInvoke = RequireMethod(
            clickableType.GetMethod("Invoke",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new System.Type[] { typeof(EventBase) }, null),
            "Clickable", "Invoke");

        // Resolve VirtualisedList access chain
        var streamedListViewType = StreamedDropdownList.PropertyType.BaseType.BaseType; // StreamedDropdownList -> StreamedObjectList -> StreamedListView
        ListElement = RequireProperty(
            streamedListViewType.GetProperty("ListElement",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            "StreamedListView", "ListElement");

        var virtualisedListType = ListElement.PropertyType;
        VirtualisedListScrollTo = RequireMethod(
            virtualisedListType.GetMethod("ScrollTo",
                BindingFlags.Public | BindingFlags.Instance,
                null, new System.Type[] { typeof(int) }, null),
            "VirtualisedList", "ScrollTo");

        VirtualisedListGetVisibleCount = RequireMethod(
            virtualisedListType.GetMethod("GetVisibleCount",
                BindingFlags.Public | BindingFlags.Instance,
                null, System.Type.EmptyTypes, null),
            "VirtualisedList", "GetVisibleCount");

        VirtualisedListGetVisibleItem = RequireMethod(
            virtualisedListType.GetMethod("GetVisibleItem",
                BindingFlags.Public | BindingFlags.Instance,
                null, new System.Type[] { typeof(int) }, null),
            "VirtualisedList", "GetVisibleItem");

        VirtualisedListGetActiveCount = RequireMethod(
            virtualisedListType.GetMethod("GetActiveCount",
                BindingFlags.Public | BindingFlags.Instance,
                null, System.Type.EmptyTypes, null),
            "VirtualisedList", "GetActiveCount");

        VirtualisedListGetActiveItem = RequireMethod(
            virtualisedListType.GetMethod("GetActiveItem",
                BindingFlags.Public | BindingFlags.Instance,
                null, new System.Type[] { typeof(int) }, null),
            "VirtualisedList", "GetActiveItem");

        var virtualisedElementType = VirtualisedListGetVisibleItem.ReturnType;
        VirtualisedElementContent = RequireProperty(
            virtualisedElementType.GetProperty("Content",
                BindingFlags.Public | BindingFlags.Instance),
            "VirtualisedElement", "Content");

        VirtualisedElementIndex = RequireProperty(
            virtualisedElementType.GetProperty("Index",
                BindingFlags.Public | BindingFlags.Instance),
            "VirtualisedElement", "Index");

        // Resolve FloatingElement.IsVisible and Hide()
        var feType = FloatingElement.PropertyType;
        FloatingElementIsVisible = RequireProperty(feType.GetProperty("IsVisible"),
            "FloatingElement", "IsVisible");

        FloatingElementHide = RequireMethod(
            feType.GetMethod("Hide",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, System.Type.EmptyTypes, null),
            "FloatingElement", "Hide");
    }

    private static PropertyInfo RequireProperty(PropertyInfo prop, string typeName, string propName)
    {
        if (prop == null)
        {
            Speech.Log.Error($"{typeName}.{propName} property not found.");
            throw new MissingMemberException(typeName, propName);
        }
        return prop;
    }

    private static MethodInfo RequireMethod(MethodInfo method, string typeName, string methodName)
    {
        if (method == null)
        {
            Speech.Log.Error($"{typeName}.{methodName}() method not found.");
            throw new MissingMethodException(typeName, methodName);
        }
        return method;
    }
}
