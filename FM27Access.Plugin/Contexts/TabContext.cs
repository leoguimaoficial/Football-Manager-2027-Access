using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

internal sealed class TabContext : IContainerContext
{
    private readonly VisualElement _buttonGroup;
    private readonly ClassificationCache _cache;

    public object Scope => _buttonGroup;

    internal TabContext(VisualElement buttonGroup, ClassificationCache cache)
    {
        _buttonGroup = buttonGroup;
        _cache = cache;
    }

    public bool HandlesDirection(NavigationDirection direction) => true;

    public bool ExitsOnBoundary(NavigationDirection direction) =>
        direction == NavigationDirection.Up || direction == NavigationDirection.Down;

    public IAccessibleElement GetFirstChild()
    {
        var tabStops = GetTabStops();
        int selected = GetSelectedTabIndex(tabStops);
        return tabStops.Count > 0 ? Wrap(tabStops[selected], selected + 1, tabStops.Count, selected) : null;
    }

    public IAccessibleElement GetEntryElement(NavigationDirection direction)
    {
        return GetFirstChild();
    }

    // The full tab set with selection resolved, for the HTML surface to render as a tablist.
    // Reuses the same stop discovery and selection logic the speech surface navigates with,
    // so both surfaces agree on which tab is selected.
    internal List<TabItemAdapter> GetAllTabs()
    {
        var tabStops = GetTabStops();
        int selected = GetSelectedTabIndex(tabStops);
        var result = new List<TabItemAdapter>(tabStops.Count);
        for (int i = 0; i < tabStops.Count; i++)
            result.Add(Wrap(tabStops[i], i + 1, tabStops.Count, selected));
        return result;
    }

    // Backing elements of every tab stop, so the HTML
    // walker can tell tab subtrees apart from non-tab content the game parks inside the same
    // ButtonGroup (the staff responsibilities card keeps its under-control/delegated counts and
    // an Advice and Reports button there).
    internal List<VisualElement> GetTabBackingElements()
    {
        var stops = GetTabStops();
        var result = new List<VisualElement>(stops.Count);
        foreach (var stop in stops)
            result.Add(((PluginAdapterBase)stop).BackingElement);
        return result;
    }

    public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction)
    {
        if (direction == NavigationDirection.Up || direction == NavigationDirection.Down)
            return null;

        var backing = ((PluginAdapterBase)current).BackingElement;
        var tabStops = GetTabStops();

        int currentIndex = FindStopIndex(tabStops, backing);
        if (currentIndex < 0)
            return null;

        int step = direction == NavigationDirection.Right ? 1 : -1;
        int nextIndex = currentIndex + step;

        if (nextIndex < 0 || nextIndex >= tabStops.Count)
            return null;

        int selected = GetSelectedTabIndex(tabStops);
        return Wrap(tabStops[nextIndex], nextIndex + 1, tabStops.Count, selected);
    }

    public IAccessibleElement WrapForContext(IAccessibleElement element)
    {
        var backing = ((PluginAdapterBase)element).BackingElement;
        var tabStops = GetTabStops();

        int index = FindStopIndex(tabStops, backing);
        if (index < 0)
            return null;

        int selected = GetSelectedTabIndex(tabStops);
        return Wrap(tabStops[index], index + 1, tabStops.Count, selected);
    }

    private List<IAccessibleElement> GetTabStops()
    {
        var allStops = TreeWalker.FindAllStopsInContainer(_buttonGroup, _cache);
        var tabStops = new List<IAccessibleElement>();

        foreach (var stop in allStops)
        {
            if (stop.Role != "button")
                break;
            if (InExtraTabSlot(stop)) continue;
            tabStops.Add(stop);
        }

        return tabStops;
    }

    // The tab bar template reserves an "ExtraTabTemplate" slot for non-tab content the game
    // parks in the same ButtonGroup: the opposition instructions card's Clear Instructions
    // action, the staff responsibilities card's Advice and Reports link. Presenting an action
    // as a tab misleads dangerously — "switching" to Clear Instructions wipes the player's
    // instructions — so slot content stays an ordinary button stop on both surfaces.
    private static bool InExtraTabSlot(IAccessibleElement stop)
    {
        var el = ((PluginAdapterBase)stop).BackingElement;
        for (var p = el?.hierarchy.parent; p != null; p = p.hierarchy.parent)
            if (p.name == "ExtraTabTemplate") return true;
        return false;
    }

    private int GetSelectedTabIndex(List<IAccessibleElement> tabStops)
    {
        try
        {
            var bg = _buttonGroup.Cast<ButtonGroup>();
            var selectedStyle = bg.SelectedStyle;

            if (!string.IsNullOrEmpty(selectedStyle))
            {
                for (int i = 0; i < tabStops.Count; i++)
                {
                    if (((PluginAdapterBase)tabStops[i]).BackingElement.ClassListContains(selectedStyle))
                        return i;
                }
                return 0;
            }

            // Fallback: index-based matching (unreliable when hidden slots exist)
            int slotIndex = bg.GetSelectedIndex();
            var allButtons = new List<VisualElement>();
            CollectButtons(_buttonGroup, allButtons);

            if (slotIndex < 0 || slotIndex >= allButtons.Count)
                return 0;

            var selectedButton = allButtons[slotIndex];
            for (int i = 0; i < tabStops.Count; i++)
            {
                if (((PluginAdapterBase)tabStops[i]).BackingElement.Pointer == selectedButton.Pointer)
                    return i;
            }
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"[TabContext] Failed to get selected tab index: {e.GetType().Name}: {e.Message}");
        }
        return 0;
    }

    private static void CollectButtons(VisualElement root, List<VisualElement> buttons)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIButton")
                buttons.Add(child);
            else
                CollectButtons(child, buttons);
        }
    }

    private static TabItemAdapter Wrap(IAccessibleElement element, int position, int total, int selectedIndex) =>
        new(element, position, total, isSelected: position - 1 == selectedIndex);

    private static int FindStopIndex(List<IAccessibleElement> stops, VisualElement backing)
    {
        for (int i = 0; i < stops.Count; i++)
        {
            if (((PluginAdapterBase)stops[i]).BackingElement.Pointer == backing.Pointer)
                return i;
        }
        return -1;
    }
}
