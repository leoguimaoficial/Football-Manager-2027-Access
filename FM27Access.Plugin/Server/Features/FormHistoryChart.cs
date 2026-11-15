using System.Globalization;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// FM27's full form graph is a streamed list of the same playerform records as
// the FM26 pip reader. Keep its native game order and axis captions in the
// existing semantic string-table renderer, rather than reading only 1..20.
internal static class FormHistoryChart
{
    private const string SeriesName = "StreamedObjectList_Large";
    private const string BarName = "data_display-graphs-elements-bar_value_indicator";

    internal static void Consolidate(List<ScreenWalker.RenderItem> items)
    {
        var seen = new HashSet<IntPtr>();
        for (int i = 0; i < items.Count; i++)
        {
            var source = Source(items[i]);
            if (source == null || items[i] is not ScreenWalker.StopItem stop
                || !int.TryParse(DisplayText.Of(stop.Element), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _)) continue;
            VisualElement series = null;
            for (var p = source; p != null; p = p.parent)
                if (p.name == SeriesName) { series = p; break; }
            if (series == null || !seen.Add(series.Pointer)
                || Find(series, e => e.name == BarName) == null) continue;
            var list = Find(series, e => e.GetIl2CppType().Name == "VirtualisedList")?.TryCast<SI.UI.VirtualisedList>();
            int total = ListWindowSurface.ReadItemCount(list);
            var ratings = Features.FormRatingsReader.ReadOrderedSeries(series);
            // Never present a realised window as the complete history. The
            // inherited list window surface retains its native paging/recovery.
            if (total <= 0 || total > StreamedTableLoader.PageRows || ratings.Count != total)
            { Degrade.Note($"Form history has {ratings.Count} readable points of {total}; native window kept."); continue; }

            for (var root = series.parent; root != null; root = root.parent)
            {
                var indices = new List<int>();
                var axis = new List<ScreenWalker.StopItem>();
                var labels = new List<string>();
                bool safe = true;
                for (int n = 0; n < items.Count; n++)
                {
                    var backing = Source(items[n]);
                    if (backing == null || !Within(backing, root)) continue;
                    indices.Add(n);
                    if (items[n] is not ScreenWalker.StopItem text || ServerText.SafeRole(text.Element) != "text")
                    { safe = false; break; }
                    string value = DisplayText.Of(text.Element);
                    if (Within(backing, series)) labels.Add(value);
                    else axis.Add(text);
                }
                // The smallest native scope owns precisely its two axes and
                // rating indices. A larger card also owns stats/actions: stop
                // there rather than consuming unrelated content or controls.
                if (!safe || axis.Count > 2) break;
                if (axis.Count != 2) continue;
                if (labels.Count != total || indices.Count != total + 2
                    || indices[^1] - indices[0] + 1 != indices.Count
                    || labels.Where((label, n) => label != (n + 1).ToString(CultureInfo.InvariantCulture)).Any()) break;
                string y = DisplayText.Of(axis[0].Element), x = DisplayText.Of(axis[1].Element);
                if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y)) break;
                var rows = new List<List<string>>();
                for (int n = 0; n < total; n++)
                    rows.Add(new() { labels[n], FormRatingsHtml.Format(new int?[] { ratings[n] }) });
                int first = indices[0];
                items.RemoveRange(first, indices.Count);
                items.Insert(first, new ScreenWalker.SyntheticTableItem { Headers = new[] { x, y }, Rows = rows, RowHeader = 0 });
                i = first;
                break;
            }
        }
    }

    private static VisualElement Source(ScreenWalker.RenderItem item)
        => ((item as ScreenWalker.StopItem)?.Element as PluginAdapterBase)?.BackingElement;

    private static bool Within(VisualElement element, VisualElement root)
    {
        for (var p = element; p != null; p = p.parent)
            if (p.Pointer == root.Pointer) return true;
        return false;
    }

    private static VisualElement Find(VisualElement root, Func<VisualElement, bool> match, int depth = 0)
    {
        if (root == null || depth > 22) return null;
        if (match(root)) return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            var found = Find(child, match, depth + 1);
            if (found != null) return found;
        }
        return null;
    }
}
