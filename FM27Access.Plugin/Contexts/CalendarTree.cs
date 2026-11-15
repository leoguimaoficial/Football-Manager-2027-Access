using System.Collections.Generic;
using System.Reflection;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

/// <summary>
/// Structural reader for the game's schedule calendar (StreamedObjectList "Weeks"). Returns the month
/// as week rows, each a Monday..Sunday array of the live "Day" cell VisualElements (a null slot is the
/// calendar padding where a week has no day in that weekday column) plus the week's training focus card
/// where the tab draws one. The companion's ScreenWalker builds its week-by-weekday table from this,
/// using the focus card as each row's header. The speech-surface CalendarContext walks the same tree
/// independently (it flattens to a day list); this is the second consumer, so the walk is duplicated
/// rather than unified — extract a single owner if a third lands. Read live, never cached.
/// </summary>
internal static class CalendarTree
{
    internal sealed class Week
    {
        // Monday..Sunday; a null slot is calendar padding.
        public readonly VisualElement[] Days = new VisualElement[7];

        // The week's training focus card (its schedule), or null on the tabs that draw no such card.
        public VisualElement FocusCard;
        public VisualElement FocusContainer;
        public List<VisualElement> SharedContent;
    }

    private static PropertyInfo _indexProperty;
    private static PropertyInfo _contentProperty;
    private static bool _reflectionInitialized;

    private static void EnsureReflection()
    {
        if (_reflectionInitialized) return;
        _reflectionInitialized = true;

        var virtElemType = typeof(SI.UI.VirtualisedList.VirtualisedElement);
        _indexProperty = virtElemType.GetProperty("Index", BindingFlags.Public | BindingFlags.Instance);
        _contentProperty = virtElemType.GetProperty("Content", BindingFlags.Public | BindingFlags.Instance);
        if (_indexProperty == null || _contentProperty == null)
            throw new System.MissingMemberException("VirtualisedElement", "Index/Content");
    }

    // The month's week rows, each mapping its visible day cells into Monday..Sunday columns. A day cell
    // with no date text is calendar padding and left as a null slot; a week with no populated day is
    // dropped. Mirrors CalendarContext's day-list filtering (visible + has date text).
    internal static List<Week> ReadWeeks(VisualElement weeksContainer)
    {
        if (weeksContainer.GetIl2CppType().Name == "DateGrid") return ReadDateGrid(weeksContainer);
        if (weeksContainer.GetIl2CppType().Name == "Calendar") return ReadCompactCalendar(weeksContainer);
        EnsureReflection();
        var weeks = new List<Week>();

        foreach (var weekRow in FindWeekRows(weeksContainer))
        {
            var weekDays = FindDescendantByName(weekRow, "WeekDays")
                ?? FindDescendantByName(weekRow, "Days");
            if (weekDays == null) continue;

            var remappers = new List<VisualElement>();
            CollectDayRemappers(weekDays, remappers);

            var week = new Week();
            bool any = false;
            foreach (var remapper in remappers)
            {
                int col = Navigation.CalendarScheduleHtml.WeekdayIndex(remapper.name);
                if (col < 0) continue;

                var dayCell = FindDescendantByName(remapper, "Day");
                if (dayCell == null || !ElementBridge.IsVisible(dayCell)) continue;
                if (string.IsNullOrEmpty(ReadDayDate(dayCell))) continue;

                week.Days[col] = dayCell;
                any = true;
            }

            // Read from the week row, not from weekDays: the focus card is the Days container's
            // sibling. Only weeks that have a day at all get one, so this follows the same gate.
            if (any)
            {
                week.FocusCard = Adapters.CalendarWeekFocusAdapter.Find(weekRow);
                weeks.Add(week);
            }
        }

        return weeks;
    }

    // DayNames is the native header the HTML walker folds into this table.
    // Read its localized captions from this calendar's nearest visible header,
    // keeping internal Monday..Sunday identifiers solely as column keys.
    internal static string[] ReadWeekdayLabels(VisualElement calendar)
    {
        // A native one-week editor binds seven weekday references instead of
        // dated records and puts its localized captions in the cells themselves.
        // Reuse those exact headers; internal English column keys are not text.
        var grid = calendar.TryCast<FM.UI.Widgets.DateGrid>();
        if (grid != null && !grid.HasPrefix && grid.m_rows?.Count == 1
            && grid.m_cells?.Count == 7 && grid.m_boundDays?.Count == 7)
        {
            var labels = new string[7];
            var seen = new HashSet<int>();
            bool complete = true;
            for (int i = 0; i < grid.m_cells.Count; i++)
            {
                var cell = grid.m_cells[i];
                int column = cell?.Index ?? -1;
                if (cell?.IsBound != true || column < 0 || column >= 7 || !seen.Add(column)
                    || !ElementBridge.IsVisibleInHierarchy(cell))
                { complete = false; break; }
                labels[column] = ReadUndatedWeekdayCaption(cell, grid.m_boundDays[column]);
                if (string.IsNullOrWhiteSpace(labels[column])) { complete = false; break; }
            }
            if (complete) return labels;
        }
        for (var parent = calendar; parent != null; parent = parent.parent)
        {
            if (parent.GetIl2CppType().Name == "Panel") break;
            var header = ElementBridge.IsVisibleInHierarchy(parent) ? FindVisibleWeekdayHeader(parent) : null;
            if (header != null)
            {
                var labels = new string[7];
                for (int i = 0; i < header.hierarchy.childCount; i++)
                {
                    var child = header.hierarchy[i];
                    int column = Navigation.CalendarScheduleHtml.WeekdayIndex(child.name);
                    if (column < 0 || !ElementBridge.IsVisibleInHierarchy(child)) continue;
                    labels[column] = TextFilters.CleanForDisplay(Server.ScreenWalker.ReadFirstText(child));
                }
                return labels;
            }
        }
        return null;
    }

    // The native weekday-template record explicitly has no Date/day number;
    // its Header is bound to DayOfTheWeek. Do not infer this from translated
    // names or treat an ordinary dated record's loading state as a template.
    internal static string ReadUndatedWeekdayCaption(VisualElement cell, SI.Core.TypedValue value)
    {
        var reference = value?.Get()?.TryCast<SI.Bindable.DynamicReference>();
        if (reference == null) return null;
        var fields = Server.BindingRecords.Fields(reference);
        if (Server.BindingRecords.Field(fields, "Date")?.Get() != null
            || Server.BindingRecords.Field(fields, "DayOfTheMonth")?.Get() != null) return null;
        var caption = TextFilters.CleanForDisplay(Server.BindingRecords.TextOf(
            Server.BindingRecords.Field(fields, "DayOfTheWeek")));
        return !string.IsNullOrWhiteSpace(caption)
            && string.Equals(caption.Trim(), ReadDayDate(cell)?.Trim(), System.StringComparison.Ordinal)
            ? caption : null;
    }

    private static VisualElement FindVisibleWeekdayHeader(VisualElement root)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (root.name == "DayNames") return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindVisibleWeekdayHeader(root.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    private static List<Week> ReadCompactCalendar(VisualElement element)
    {
        var calendar = element.TryCast<FM.UI.Widgets.Calendar>();
        var weeks = new List<Week>();
        if (calendar?.m_rows == null) return weeks;
        // FM27's processing calendar owns its realised week rows directly,
        // rather than through the older StreamedObjectList("Weeks"). Each row
        // still binds the same seven named weekday remappers in native order.
        foreach (var row in calendar.m_rows)
        {
            if (!ElementBridge.IsVisible(row)) continue;
            var days = FindDescendantByName(row, "WeekDays");
            if (days == null) continue;
            var remappers = new List<VisualElement>();
            CollectDayRemappers(days, remappers);
            if (remappers.Count != 7)
                throw new System.InvalidOperationException("Calendar week is not seven native weekday remappers.");
            var week = new Week();
            var seen = new HashSet<int>();
            bool any = false;
            foreach (var remapper in remappers)
            {
                int column = Navigation.CalendarScheduleHtml.WeekdayIndex(remapper.name);
                if (column < 0 || !seen.Add(column))
                    throw new System.InvalidOperationException("Calendar weekday mapping is ambiguous.");
                var cell = FindDescendantByName(remapper, "CalendarDayBindingExpect");
                if (cell == null || !ElementBridge.IsVisible(cell) || string.IsNullOrEmpty(ReadDayDate(cell))) continue;
                week.Days[column] = cell;
                any = true;
            }
            if (any) weeks.Add(week);
        }
        return weeks;
    }

    private static List<Week> ReadDateGrid(VisualElement element)
    {
        var grid = element.TryCast<FM.UI.Widgets.DateGrid>();
        var weeks = new List<Week>();
        if (grid?.m_rows == null) return weeks;
        if (grid.HasPrefix && grid.m_dayTracks?.Count != grid.m_rows.Count)
            throw new System.InvalidOperationException("DateGrid row and day-track counts disagree.");
        for (int i = 0; i < grid.m_rows.Count; i++)
        {
            var nativeRow = grid.m_rows[i];
            var week = new Week();
            for (int j = 0; j < nativeRow.hierarchy.childCount; j++)
                if (nativeRow.hierarchy[j].GetIl2CppType().Name == "PrefixCell")
                    week.FocusContainer = nativeRow.hierarchy[j];
            var track = grid.HasPrefix ? grid.m_dayTracks[i] : nativeRow;
            int column = 0;
            bool any = false;
            for (int j = 0; j < track.hierarchy.childCount; j++)
            {
                var cell = track.hierarchy[j];
                var type = cell.GetIl2CppType().Name;
                if (type == "SIText")
                {
                    (week.SharedContent ??= new List<VisualElement>()).Add(cell);
                    continue;
                }
                if (type != "DayCell" || column >= 7)
                    throw new System.InvalidOperationException("DateGrid week is not seven native day cells.");
                var day = cell.TryCast<FM.UI.Widgets.DateGrid.DayCell>();
                if (day?.IsBound == true && ElementBridge.IsVisible(cell) && !string.IsNullOrEmpty(ReadDayDate(cell)))
                { week.Days[column] = cell; any = true; }
                column++;
            }
            if (column != 7) throw new System.InvalidOperationException("DateGrid week has missing day columns.");
            if (any) weeks.Add(week);
        }
        return weeks;
    }

    // The day-of-month text the cell shows (Header > text), or null when the cell carries no date
    // (calendar padding). The reader uses this both to fill the cell and to skip padding.
    internal static string ReadDayDate(VisualElement dayCell)
    {
        var textEl = FindDateTextElement(dayCell);
        if (textEl == null) return null;
        var value = textEl.TryCast<TextElement>()?.text;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    internal static VisualElement FindDateTextElement(VisualElement dayCell)
    {
        var header = FindDescendantByName(dayCell, "Header");
        if (header == null) return null;
        var textEl = FindDescendantByName(header, "text");
        return textEl ?? FirstVisibleDateText(header);
    }

    private static VisualElement FirstVisibleDateText(VisualElement element)
    {
        if (!ElementBridge.IsVisible(element)) return null;
        if (element.GetIl2CppType().Name == "SIText") return element;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var found = FirstVisibleDateText(element.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    // The materialized week rows, in chronological order. The "Weeks" list virtualises, so read the
    // active elements by their VirtualisedList Index (recycled tree order is not reliable). Falls back
    // to the scroll view's content container if the VirtualisedList read fails.
    private static List<VisualElement> FindWeekRows(VisualElement weeksContainer)
    {
        var results = new List<VisualElement>();

        var virtListVE = FindChildByType(weeksContainer, "VirtualisedList");
        if (virtListVE == null)
        {
            Log.Warn("[Calendar] Could not find VirtualisedList for the schedule calendar.");
            return results;
        }

        try
        {
            var vl = new SI.UI.VirtualisedList(((Il2CppSystem.Object)virtListVE).Pointer);
            int activeCount = vl.GetActiveCount();

            var indexed = new List<(int Index, VisualElement Content)>();
            for (int i = 0; i < activeCount; i++)
            {
                var virtElem = vl.GetActiveItem(i);
                if (virtElem == null) continue;
                var content = _contentProperty.GetValue(virtElem);
                if (content == null) continue;
                var contentVE = new VisualElement(((Il2CppSystem.Object)content).Pointer);
                int idx = (int)_indexProperty.GetValue(virtElem);
                if (ElementBridge.IsVisible(contentVE))
                    indexed.Add((idx, contentVE));
            }

            indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach (var item in indexed)
                results.Add(item.Content);
        }
        catch (System.Exception e)
        {
            Log.Warn($"[Calendar] VirtualisedList read failed, walking content container: {e.GetType().Name}: {e.Message}");

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

    private static readonly HashSet<string> WeekdayNames = new(System.StringComparer.Ordinal)
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    };

    private static void CollectDayRemappers(VisualElement root, List<VisualElement> results)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == "BindingRemapper" && WeekdayNames.Contains(child.name))
                results.Add(child);
            else
                CollectDayRemappers(child, results);
        }
    }

    private static VisualElement FindDescendantByName(VisualElement root, string name)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            var deeper = FindDescendantByName(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static VisualElement FindChildByType(VisualElement parent, string typeName)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName) return child;
        }
        return null;
    }
}
