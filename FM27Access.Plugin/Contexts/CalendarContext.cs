using System.Collections.Generic;
using System.Reflection;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

internal sealed class CalendarContext : IContainerContext
{
    private readonly VisualElement _container;
    private readonly ClassificationCache _cache;

    private static PropertyInfo _virtualisedElementIndexProperty;
    private static PropertyInfo _virtualisedElementContentProperty;
    private static bool _reflectionInitialized;

    public object Scope => _container;

    internal CalendarContext(VisualElement container, ClassificationCache cache)
    {
        _container = container;
        _cache = cache;
        EnsureReflection();
    }

    private static void EnsureReflection()
    {
        if (_reflectionInitialized) return;
        _reflectionInitialized = true;

        var virtElemType = typeof(SI.UI.VirtualisedList.VirtualisedElement);

        _virtualisedElementIndexProperty = virtElemType.GetProperty("Index",
            BindingFlags.Public | BindingFlags.Instance);
        if (_virtualisedElementIndexProperty == null)
        {
            Speech.Log.Error("[Calendar] VirtualisedElement.Index property not found.");
            throw new System.MissingMemberException("VirtualisedElement", "Index");
        }

        _virtualisedElementContentProperty = virtElemType.GetProperty("Content",
            BindingFlags.Public | BindingFlags.Instance);
        if (_virtualisedElementContentProperty == null)
        {
            Speech.Log.Error("[Calendar] VirtualisedElement.Content property not found.");
            throw new System.MissingMemberException("VirtualisedElement", "Content");
        }

    }

    public bool HandlesDirection(NavigationDirection direction) => true;

    public bool ExitsOnBoundary(NavigationDirection direction) =>
        direction == NavigationDirection.Up || direction == NavigationDirection.Down;

    public string GetBoundaryAnnouncement(NavigationDirection direction) => null;

    public IAccessibleElement GetFirstChild()
    {
        var days = BuildDayList();
        if (days.Count == 0) return null;

        for (int i = 0; i < days.Count; i++)
        {
            if (days[i].GetState() == "current date")
                return days[i];
        }

        return days[0];
    }

    public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
    {
        if (direction == NavigationDirection.Left || direction == NavigationDirection.Right)
            return NavigateInnerCell(current, direction);

        if (direction != NavigationDirection.Up && direction != NavigationDirection.Down)
            return null;

        var days = BuildDayList();
        if (days.Count == 0)
        {
            Speech.Log.Warn("[Calendar] BuildDayList returned empty.");
            return null;
        }

        int currentIndex = FindCurrentDayIndex(current, days);
        if (currentIndex < 0)
        {
            // Element is inside the calendar container but not in a day cell
            // (e.g. TrainingFocusCard). Navigate to the first or last day.
            Speech.Log.Info("[Calendar] Current element not in any day cell, jumping to edge.");
            return direction == NavigationDirection.Down ? days[0] : days[days.Count - 1];
        }

        int step = direction == NavigationDirection.Down ? 1 : -1;
        int nextIndex = currentIndex + step;

        if (nextIndex < 0 || nextIndex >= days.Count)
            return null;

        return days[nextIndex];
    }

    public IAccessibleElement NavigateInnerCell(IAccessibleElement current, NavigationDirection direction)
    {
        if (direction != NavigationDirection.Left && direction != NavigationDirection.Right)
            return null;

        var dayCellRoot = FindDayCellRoot(current);
        if (dayCellRoot == null)
        {
            Speech.Log.Warn("[Calendar] Could not find day cell root for inner navigation.");
            return null;
        }

        var stops = TreeWalker.FindAllStopsInContainer(dayCellRoot, _cache);
        if (stops.Count <= 1) return null;

        int currentStop = FindStopIndex(current, stops);
        if (currentStop < 0) currentStop = 0;

        int step = direction == NavigationDirection.Right ? 1 : -1;
        int next = currentStop + step;

        if (next < 0 || next >= stops.Count)
            return null;

        return stops[next];
    }

    private List<CalendarDayAdapter> BuildDayList()
    {
        if (_container.GetIl2CppType().Name is "DateGrid" or "Calendar")
        {
            var nativeDays = new List<CalendarDayAdapter>();
            foreach (var week in CalendarTree.ReadWeeks(_container))
                for (int i = 0; i < week.Days.Length; i++)
                    if (week.Days[i] != null)
                        nativeDays.Add(new CalendarDayAdapter(week.Days[i], CalendarScheduleHtml.Weekdays[i]));
            return nativeDays;
        }
        // The container is StreamedObjectList("Weeks").
        // Inside: VirtualisedList → SIScrollView → VE → VE(viewport) → VE(content-container)
        //   → [DropTarget("View") per week row]
        //     → AsyncBindingRoot → BindingExpect("WeekDays") with 7 BindingRemapper children
        //       → each BindingRemapper (Monday-Sunday) → TemplateContainer("Day") = one day cell
        var weekRows = FindWeekRows();
        var days = new List<CalendarDayAdapter>();

        foreach (var weekRow in weekRows)
        {
            var weekDays = FindDescendantByName(weekRow, "WeekDays")
                ?? FindDescendantByName(weekRow, "Days");
            if (weekDays == null)
                continue;

            var dayColumns = new List<VisualElement>();
            CollectDayRemappers(weekDays, dayColumns);

            foreach (var dayColumn in dayColumns)
            {
                var dayName = dayColumn.name;

                var dayCell = FindDescendantByName(dayColumn, "Day");
                if (dayCell == null) continue;
                if (!ElementBridge.IsVisible(dayCell)) continue;

                var header = FindDescendantByName(dayCell, "Header");
                if (header == null) continue;
                var dateText = FindDescendantByName(header, "text");
                if (dateText == null) continue;

                try
                {
                    var textEl = dateText.TryCast<TextElement>();
                    if (textEl == null || string.IsNullOrEmpty(textEl.text)) continue;
                }
                catch { continue; }

                days.Add(new CalendarDayAdapter(dayCell, dayName));
            }
        }

        return days;
    }

    private List<VisualElement> FindWeekRows()
    {
        var results = new List<VisualElement>();

        // The StreamedObjectList("Weeks") contains a VirtualisedList.
        // Use its active elements to get all materialized week rows.
        var virtListVE = FindChildByType(_container, "VirtualisedList");
        if (virtListVE == null)
        {
            Speech.Log.Warn("[Calendar] Could not find VirtualisedList.");
            return results;
        }

        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtListVE).Pointer);
            int activeCount = vl.GetActiveCount();

            // Collect (index, content) pairs and sort by index for chronological order
            var indexed = new List<(int Index, VisualElement Content)>();
            for (int i = 0; i < activeCount; i++)
            {
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                var content = _virtualisedElementContentProperty.GetValue(virtElem);
                if (content == null) continue;
                var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
                int idx = (int)_virtualisedElementIndexProperty.GetValue(virtElem);
                if (ElementBridge.IsVisible(contentVE))
                    indexed.Add((idx, contentVE));
            }

            indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach (var item in indexed)
                results.Add(item.Content);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[Calendar] Failed to read VirtualisedList active elements: {e.GetType().Name}: {e.Message}");

            // Fallback: walk the content container
            var scrollView = FindChildByType(virtListVE, "SIScrollView");
            if (scrollView == null) return results;
            var contentContainer = FindDescendantByName(scrollView, "unity-content-container");
            if (contentContainer == null) return results;
            int count = contentContainer.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = contentContainer.hierarchy[i];
                if (ElementBridge.IsVisible(child))
                    results.Add(child);
            }
        }

        return results;
    }

    private int FindCurrentDayIndex(IAccessibleElement current, List<CalendarDayAdapter> days)
    {
        if (current is CalendarDayAdapter dayAdapter)
        {
            var cellPtr = dayAdapter.DayCellRoot.Pointer;
            for (int i = 0; i < days.Count; i++)
            {
                if (days[i].DayCellRoot.Pointer == cellPtr)
                    return i;
            }
        }

        // Current is a regular element inside a day cell — find which day contains it
        var backing = ((PluginAdapterBase)current).BackingElement;
        for (int i = 0; i < days.Count; i++)
        {
            if (TreeWalker.IsDescendantOf(backing, days[i].DayCellRoot))
                return i;
        }

        return -1;
    }

    private VisualElement FindDayCellRoot(IAccessibleElement current)
    {
        if (current is CalendarDayAdapter dayAdapter)
            return dayAdapter.DayCellRoot;

        // Walk up from the backing element to find TemplateContainer("Day")
        var backing = ((PluginAdapterBase)current).BackingElement;
        var el = backing;
        while (el != null && el.Pointer != _container.Pointer)
        {
            if (el.GetIl2CppType().Name == "DayCell"
                || el.GetIl2CppType().Name == "TemplateContainer" && el.name == "Day")
                return el;
            el = el.hierarchy.parent;
        }
        return null;
    }

    private static int FindStopIndex(IAccessibleElement current, List<IAccessibleElement> stops)
    {
        if (current is CalendarDayAdapter)
            return 0;

        if (current is PluginAdapterBase adapter)
        {
            var ptr = adapter.BackingElement.Pointer;
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] is PluginAdapterBase s && s.BackingElement.Pointer == ptr)
                    return i;
            }
        }

        return -1;
    }

    private static readonly HashSet<string> DayNameSet = new(System.StringComparer.Ordinal)
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    };

    private static void CollectDayRemappers(VisualElement root, List<VisualElement> results)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == "BindingRemapper" && DayNameSet.Contains(child.name))
            {
                results.Add(child);
            }
            else
            {
                CollectDayRemappers(child, results);
            }
        }
    }

    private static VisualElement FindDescendantByName(VisualElement root, string name)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name)
                return child;
            var deeper = FindDescendantByName(child, name);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    private static VisualElement FindChildByType(VisualElement parent, string typeName)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName)
                return child;
        }
        return null;
    }
}
