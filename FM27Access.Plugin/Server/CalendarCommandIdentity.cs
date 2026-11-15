using System.Globalization;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// DateGrid recycles its physical day/prefix cells when the displayed month
// changes. Tree paths, cell pointers and schedule captions are not date identity.
// Read the actual native bound record and its immutable full GameDate instead.
// Main thread only. Other DateGrid consumers (for example date-picker indices)
// have no such records and retain their existing command contracts.
internal static class CalendarCommandIdentity
{
    internal const string Pending = "calendar-date:pending";

    internal static string Read(VisualElement element) => ReadWithReference(element, out _);

    internal static string ReadWithReference(VisualElement element, out SI.Core.Record weekdayReference)
    {
        weekdayReference = null;
        VisualElement cell = null;
        bool prefix = false;
        for (var up = element; up != null; up = up.parent)
        {
            string type = up.GetIl2CppType().FullName;
            if (type == "SI.Bindable.Panel") break;
            if (type == "FM.UI.Widgets.DateGrid+PrefixCell") { cell = up; prefix = true; break; }
            if (type == "FM.UI.Widgets.DateGrid+DayCell") { cell = up; break; }
        }
        if (cell == null)
        {
            // Floating content is reparented outside the calendar. Its native
            // trigger still owns the bound week; option commands must retain
            // that same identity rather than borrow the next popup's week.
            var owner = FilterPopupSurface.OwnerOf(element);
            return owner == null ? null : ReadWithReference(owner, out weekdayReference);
        }
        for (var up = cell.parent; up != null; up = up.parent)
        {
            if (up.GetIl2CppType().Name == "Panel") break;
            if (up.GetIl2CppType().FullName != "FM.UI.Widgets.DateGrid") continue;
            var grid = up.TryCast<FM.UI.Widgets.DateGrid>();
            if (grid == null) return Pending;
            SI.Core.TypedValue value = null;
            if (prefix)
            {
                var cells = grid.m_prefixCells;
                for (int i = 0; cells != null && i < cells.Count; i++)
                    if (cells[i]?.Pointer == cell.Pointer)
                    {
                        if (!cells[i].IsBound) return Pending;
                        if (grid.m_boundPrefix != null && i < grid.m_boundPrefix.Count) value = grid.m_boundPrefix[i];
                        break;
                    }
            }
            else
            {
                var cells = grid.m_cells;
                for (int i = 0; cells != null && i < cells.Count; i++)
                    if (cells[i]?.Pointer == cell.Pointer)
                    {
                        if (!cells[i].IsBound) return Pending;
                        int index = cells[i].Index;
                        if (grid.m_boundDays != null && index >= 0 && index < grid.m_boundDays.Count) value = grid.m_boundDays[index];
                        break;
                    }
            }
            if (value == null) return Pending;
            var record = BindingRecords.RecordOf(value);
            if (record == null) return null;
            var fields = BindingRecords.Fields(record);
            var date = BindingRecords.Field(fields, prefix ? "WeekStartDate" : "Date");
            if (date == null) return null;
            var native = date.Get();
            if (native == null)
            {
                // A template miniweek has actual bound weekday references,
                // not pending GameDates. Retain its native reference and bound
                // cell/owner scope; never use the caption or index as identity.
                if (!prefix && !grid.HasPrefix && grid.m_rows?.Count == 1
                    && grid.m_boundDays?.Count == 7
                    && Contexts.CalendarTree.ReadUndatedWeekdayCaption(cell, value) != null)
                {
                    var day = cell.TryCast<FM.UI.Widgets.DateGrid.DayCell>();
                    weekdayReference = record;
                    return "calendar-weekday:" + grid.Pointer.ToInt64().ToString(CultureInfo.InvariantCulture)
                        + ":" + record.Pointer.ToInt64().ToString(CultureInfo.InvariantCulture)
                        + ":" + day.m_key.m_key.ToString(CultureInfo.InvariantCulture);
                }
                return Pending;
            }
            if (native.GetIl2CppType().FullName != "FM.GamePlugin.GameDate")
                throw new System.InvalidOperationException("The native calendar record has an unsupported date type.");
            return (prefix ? "calendar-week:" : "calendar-day:")
                + native.Unbox<FM.GamePlugin.GameDate>().m_packedDate.ToString(CultureInfo.InvariantCulture);
        }
        return Pending;
    }
}
