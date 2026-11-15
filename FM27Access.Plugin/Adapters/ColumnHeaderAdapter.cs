using System.Reflection;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class ColumnHeaderAdapter : PluginAdapterBase
{
    private static readonly MethodInfo ClickableInvokeMethod;

    static ColumnHeaderAdapter()
    {
        var clickableType = typeof(Clickable);
        ClickableInvokeMethod = clickableType.GetMethod("Invoke",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new System.Type[] { typeof(EventBase) }, null);
        if (ClickableInvokeMethod == null)
        {
            Speech.Log.Error("Clickable.Invoke method not found.");
            throw new System.MissingMethodException("Clickable", "Invoke");
        }
    }

    public ColumnHeaderAdapter(VisualElement element) : base(element) { }

    public override string Role => "column header";
    // A shared native header template can contain a plain Button without any
    // primary operation (the FM27 fixture schedule is one such template).
    // A Clickable instance by itself is not a registered game action.
    internal bool HasPrimaryClick
    {
        get
        {
            var clickable = Element.Cast<Button>().clickable;
            return clickable != null && (clickable.clicked is not null
                || clickable.clickedWithEventInfo is not null);
        }
    }

    public override bool CanActivate => HasPrimaryClick;

    public override string GetLabel()
    {
        return FindBestLabel() ?? Element.name;
    }

    public override string GetState()
    {
        // FM27's persisted native sort can be active with no visible SortIcon,
        // and its arrow sprites do not retain FM26's direction convention.
        // Match the actual descriptor by Pointer, then ask the live controller.
        // Only primitive out values cross this native boundary.
        if (TryNativeSortState(out var nativeState)) return nativeState;
        if (!HasPrimaryClick && !HasContextMenu) return "";
        var sortIcon = FindByName(Element, "SortIcon");
        if (sortIcon == null || !ElementBridge.IsVisible(sortIcon))
            return "";

        // The game shows no sort-direction USS class, rotation, or child; direction lives in the
        // arrow sprite. Verified live against the squad table's Age column: "sort arrow down" is
        // ascending order (low to high), "sort arrow up" is descending.
        var sprite = sortIcon.resolvedStyle.backgroundImage.sprite;
        if (sprite == null) return "";
        var name = sprite != null ? sprite.name : null;
        if (name != null)
        {
            if (name.Contains("down")) return "sorted ascending";
            if (name.Contains("up")) return "sorted descending";
        }
        return "sorted";
    }

    private bool TryNativeSortState(out string state)
    {
        state = null;
        try
        {
            for (var up = Element.hierarchy.parent; up != null; up = up.hierarchy.parent)
            {
                var table = up.TryCast<SI.Bindable.StreamedTable>();
                if (table == null) continue;
                var ids = table.m_columnIDs;
                var headers = table.m_headerLookup;
                if (ids == null || headers == null) return false;
                for (int i = 0; i < ids.Count; i++)
                {
                    if (!headers.TryGetValue(ids[i], out var header)
                        || header?.m_button == null || header.m_button.Pointer != Element.Pointer) continue;
                    state = table.TryFindSortedColumn(header.ColumnID, out _, out bool ascending)
                        ? (ascending ? "sorted ascending" : "sorted descending") : "";
                    return true;
                }
                return false;
            }
        }
        catch (System.Exception ex)
        {
            Speech.Log.Warn($"[ColumnHeader] Native sort state unavailable; retaining icon fallback: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    public override void Activate()
    {
        if (!ClickHeader()) return;
        var label = GetLabel();
        Speech.SpeechPipeline.SpeakInterrupt($"{label}, sorted");
    }

    /// <summary>
    /// Dispatches the header's own click to the game (the sort trigger), with no announcement.
    /// Returns false if the header carries no clickable. The cursor surface follows this with a
    /// spoken confirmation; the HTML surface stays silent and lets the refreshed aria-sort speak.
    /// </summary>
    internal bool ClickHeader()
    {
        if (!HasPrimaryClick) return false;
        var button = Element.Cast<Button>();
        var clickable = button.clickable;
        if (clickable == null)
        {
            Speech.Log.Warn("[ColumnHeader] Button has no clickable.");
            return false;
        }
        var evt = ClickEvent.GetPooled();
        ClickableInvokeMethod.Invoke(clickable, new object[] { evt });
        return true;
    }
}
